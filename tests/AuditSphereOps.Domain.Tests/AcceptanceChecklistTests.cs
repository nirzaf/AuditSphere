using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Engagement acceptance against the real seeded question banks: the new-client path, the recurring-client delta path,
/// evidence and adverse-answer escalation, and the Partner activation gate.
/// </summary>
[Trait("Profile", "Database")]
public sealed class AcceptanceChecklistTests
{
  private sealed record World(Guid FirmId, Guid ClientId, ActorContext Staff, ActorContext Manager, ActorContext Partner, AppUser PartnerUser);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + "-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-acceptance",
    Email = $"{name}-{Guid.NewGuid():N}@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static async Task<World> SeedAsync(PgTestSchema pg, bool seedBanks = true, Guid? existingFirm = null, Guid? existingClient = null)
  {
    var (firmId, clientId, _) = existingFirm is null ? await pg.SeedScopeAsync() : (existingFirm.Value, existingClient!.Value, Guid.Empty);
    var staff = User(firmId, "staff"); var manager = User(firmId, "manager"); var partner = User(firmId, "partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(staff, manager, partner);
    db.RoleGrants.AddRange(
      new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = staff.Id, Role = "Staff", ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = staff.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = manager.Id, Role = "Manager", ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = manager.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = partner.Id, Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = partner.Id });
    if (seedBanks && !await db.QuestionnaireTemplates.AnyAsync(x => x.Id == QuestionnaireSeed.CeTemplateId))
      await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db);
    await db.SaveChangesAsync();
    return new(firmId, clientId, Actor(staff, "Staff"), Actor(manager, "Manager"), Actor(partner, "Partner"), partner);
  }

  private static string SafeAnswer(ChecklistQuestion q) => q.AdverseAnswer == "NO" ? "Yes" : q.AdverseAnswer == "YES" ? "No" : "Yes";
  private static string? Evidence(ChecklistQuestion q) => q.RequiresEvidence ? $"DOC-{q.Code}" : null;

  private static async Task AnswerAllAsync(PgTestSchema pg, World w, params (string Code, string Answer)[] overrides)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var checklist = (await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!;
    var forced = overrides.ToDictionary(x => x.Code, x => x.Answer, StringComparer.Ordinal);
    foreach (var item in checklist.Items)
    {
      var result = await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, item.Question.Code,
        forced.GetValueOrDefault(item.Question.Code) ?? SafeAnswer(item.Question), Evidence(item.Question));
      Assert.True(result.Succeeded, $"{item.Question.Code}: {result.Message}");
    }
  }

  private static Task<CommandResult<Guid>> Decide(PgTestSchema pg, World w, string decision, out AuditSphereDbContext db, long generation = 1)
  {
    db = new AuditSphereDbContext(pg.Options);
    return AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", decision, "Reviewed against the checklist.", null, generation));
  }

  [Fact]
  public async Task NewClient_MustAnswerEveryOnboardingQuestionWithEvidence_AndAnAdverseAnswerIsNeverAcceptedByFillingFields()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var start = (await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!;
    Assert.Equal((AcceptancePaths.NewClient, 62), (start.Path, start.Items.Count));
    Assert.All(start.Items, x => Assert.Equal("CE", x.Question.Bank));
    Assert.Null(start.PriorDecision);
    Assert.False(start.Ready);

    // Evidence-bearing questions refuse an answer without a supporting reference; garbage answers are refused.
    var noEvidence = await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-030", "Yes", null);
    Assert.False(noEvidence.Succeeded);
    Assert.Contains("evidence", noEvidence.Message);
    Assert.False((await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-030", "Perhaps", "DOC")).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "RV-003", "No", null)).ErrorCode); // a delta question is not on this path

    // Nothing answered: the Partner cannot accept.
    var early = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Reviewed.", null, 1));
    Assert.Equal(ErrorCodes.GateBlocked, early.ErrorCode);
    Assert.Contains("62 outstanding item(s)", early.Message);

    // Every field filled, but a sanctions match (adverse) is reported: still not acceptable.
    await AnswerAllAsync(pg, w, ("CE-032", "Yes"));
    var filled = (await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!;
    Assert.Equal(["adverse-uncleared"], filled.Blockers.Select(x => x.Kind).Distinct());
    Assert.Equal("CE-032", Assert.Single(filled.Blockers).QuestionCode);
    var adverse = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Reviewed.", null, 1));
    Assert.Equal(ErrorCodes.GateBlocked, adverse.ErrorCode);
    Assert.Contains("CE-032", adverse.Message);

    // The escalation needs a specialist of at least Manager level; staff cannot clear their own findings.
    var request = await AcceptanceChecklistService.RequestClearanceAsync(db, w.Staff, w.ClientId, "AML", "AML officer");
    Assert.True(request.Succeeded);
    Assert.Equal(request.Value, (await AcceptanceChecklistService.RequestClearanceAsync(db, w.Staff, w.ClientId, "AML", "AML officer")).Value); // idempotent
    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.RecordClearanceAsync(db, w.Staff, request.Value, "CLEARED", "SCREEN-1", null)).ErrorCode);
    Assert.False((await AcceptanceChecklistService.RecordClearanceAsync(db, w.Manager, request.Value, "CLEARED", null, null)).Succeeded); // evidence required
    Assert.True((await AcceptanceChecklistService.RecordClearanceAsync(db, w.Manager, request.Value, "HOLD", "SCREEN-1", null)).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Reviewed.", null, 1))).ErrorCode);
    Assert.True((await AcceptanceChecklistService.RecordClearanceAsync(db, w.Manager, request.Value, "CLEARED", "SCREEN-1: false positive, list match cleared", null)).Succeeded);
    Assert.True((await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!.Ready);

    // A newer adverse answer after the clearance needs a new review.
    Assert.True((await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-031", "Yes", "PEP-CHECK-9")).Succeeded);
    var again = (await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!;
    Assert.Contains(again.Blockers, x => x.Kind == "adverse-uncleared" && x.QuestionCode == "CE-031");
    Assert.True((await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-031", "No", "PEP-CHECK-10")).Succeeded); // corrected
    Assert.True((await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!.Ready);

    var accepted = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Cleared after AML review.", null, 1));
    Assert.True(accepted.Succeeded, accepted.Message);
    var decision = await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == accepted.Value);
    Assert.Equal((AcceptancePaths.NewClient, null), (decision.Path, decision.PriorDecisionId));
    Assert.EndsWith(AcceptancePaths.NewClient, decision.EvaluationTemplateVersion);
    // A recorded evaluation is immutable.
    Assert.Equal(ErrorCodes.ProtectedState, (await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-001", "No", "X")).ErrorCode);
  }

  [Fact]
  public async Task ChecklistAccess_IsScopedToTheClient()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var other = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient { Id = other, FirmId = w.FirmId, LegalName = "OTHER CLIENT", CreatedAt = DateTimeOffset.UtcNow });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = other, FirmId = w.FirmId });
    await db.SaveChangesAsync();
    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.GetAsync(db, w.Staff, other)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, other, "CE-001", "Yes", "DOC")).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.RequestClearanceAsync(db, w.Staff, other, "AML", "Officer")).ErrorCode);
  }

  [Fact]
  public async Task RecurringClient_AnswersOnlyTheDeltaBank_EscalatesKeyChanges_AndKeepsPriorEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Equal(ErrorCodes.GateBlocked, (await AcceptanceChecklistService.StartContinuanceAsync(db, w.Partner, w.ClientId)).ErrorCode); // nothing accepted yet

    // First-year acceptance through the real new-client path.
    await AnswerAllAsync(pg, w);
    var first = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Year one.", null, 1));
    Assert.True(first.Succeeded, first.Message);

    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.StartContinuanceAsync(db, w.Staff, w.ClientId)).ErrorCode);
    var next = await AcceptanceChecklistService.StartContinuanceAsync(db, w.Partner, w.ClientId);
    Assert.True(next.Succeeded, next.Message);
    Assert.Equal(2L, next.Value);

    var review = (await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!;
    Assert.Equal((AcceptancePaths.Continuance, 30, first.Value), (review.Path, review.Items.Count, review.PriorDecision!.Id));
    Assert.All(review.Items, x => Assert.Equal("RV", x.Question.Bank));
    Assert.Equal(["RV-003", "RV-007", "RV-009", "RV-020"], review.Items.Where(x => x.Question.EscalatesOnChange).Select(x => x.Question.Code));
    Assert.Equal(ErrorCodes.ScopeDenied, (await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-001", "Yes", "DOC")).ErrorCode); // onboarding bank is not asked again
    var oldGeneration = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Stale.", null, 1));
    Assert.Equal(ErrorCodes.StaleRevision, oldGeneration.ErrorCode);

    // Fraud is reported: a completed delta review is still not acceptable until it is escalated and cleared.
    await AnswerAllAsync(pg, w, ("RV-020", "Yes"));
    var filled = (await AcceptanceChecklistService.GetAsync(db, w.Staff, w.ClientId)).Value!;
    var blocker = Assert.Single(filled.Blockers);
    Assert.Equal(("adverse-uncleared", "RV-020"), (blocker.Kind, blocker.QuestionCode));
    Assert.Contains("change that needs a cleared", blocker.Message);
    var blocked = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Year two.", null, 2));
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

    var clearance = (await AcceptanceChecklistService.RequestClearanceAsync(db, w.Staff, w.ClientId, "Continuance", "Risk partner")).Value;
    Assert.True((await AcceptanceChecklistService.RecordClearanceAsync(db, w.Manager, clearance, "CLEARED", "FRAUD-REVIEW-2026-07", null)).Succeeded);
    var second = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(w.ClientId, null, "AccountingOnly", "ACCEPTED", "Year two after fraud review.", null, 2));
    Assert.True(second.Succeeded, second.Message);
    var decision = await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == second.Value);
    Assert.Equal((AcceptancePaths.Continuance, first.Value, 2L), (decision.Path, decision.PriorDecisionId, decision.Generation));
    // The first-year decision and its evidence are preserved untouched.
    Assert.Equal(AcceptancePaths.NewClient, (await db.AcceptanceDecisions.AsNoTracking().SingleAsync(x => x.Id == first.Value)).Path);
    Assert.Equal(62, await db.EvaluationResponses.CountAsync(x => x.Bank == "CE" && x.Generation == 1));
  }

  [Fact]
  public async Task Activation_RequiresACurrentUnconditionalPartnerAcceptanceForTheSameRoute()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg, seedBanks: false);
    await using var db = new AuditSphereDbContext(pg.Options);
    // Each command runs on its own context, as in the application; a shared one would cache the locked client row.
    static async Task<CommandResult<Guid>> ActivateAsync(PgTestSchema schema, ActorContext actor, Guid engagementId)
    {
      await using var fresh = new AuditSphereDbContext(schema.Options);
      return await EngagementLifecycleService.ActivateAsync(fresh, actor, engagementId);
    }

    var created = await EngagementLifecycleService.CreateDraftAsync(db, w.Manager, new(w.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "ACC-2026"));
    Assert.True(created.Succeeded, created.Message);
    Assert.Equal(created.Value, (await EngagementLifecycleService.CreateDraftAsync(db, w.Manager, new(w.ClientId, "AccountingOnly", "2026-01-01", "2026-12-31", "ACC-2026"))).Value);
    Assert.False((await EngagementLifecycleService.CreateDraftAsync(db, w.Staff, new(w.ClientId, "AccountingOnly", "2025-01-01", "2025-12-31", "ACC-2025"))).Succeeded);
    Assert.False((await EngagementLifecycleService.CreateDraftAsync(db, w.Manager, new(w.ClientId, "AccountingOnly", "2026-12-31", "2026-01-01", "ACC-2026"))).Succeeded);
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == created.Value);
    Assert.True(engagement.ProfessionalWorkBlocked);

    // No decision, then a declined / conditional / other-route / stale decision: never activates.
    Assert.Equal(ErrorCodes.GateBlocked, (await ActivateAsync(pg, w.Partner, created.Value)).ErrorCode);
    async Task<Guid> Record(string decision, string route, long generation, string? conditions = null)
    {
      var id = Guid.NewGuid();
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = id, FirmId = w.FirmId, PracticeClientId = w.ClientId, Decision = decision, ServiceRoute = route, Generation = generation,
        Rationale = "Recorded.", Conditions = conditions, EvaluationTemplateVersion = "T", EvaluationSnapshotDigest = new string('a', 64),
        DecidedByUserId = w.PartnerUser.Id, DecidedAt = DateTimeOffset.UtcNow.AddSeconds(generation)
      });
      await db.SaveChangesAsync();
      return id;
    }
    await Record("Declined", "AccountingOnly", 1);
    Assert.Contains("Declined", (await ActivateAsync(pg, w.Partner, created.Value)).Message);
    await Record("Accepted", "FinancialStatementAudit", 1);
    Assert.Equal(ErrorCodes.GateBlocked, (await ActivateAsync(pg, w.Partner, created.Value)).ErrorCode); // route mismatch
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_safety_states SET input_generation = 2 WHERE id = {w.ClientId}");
    await Record("Accepted", "AccountingOnly", 1);
    Assert.Equal(ErrorCodes.GenerationStale, (await ActivateAsync(pg, w.Partner, created.Value)).ErrorCode);
    await Record("AcceptedWithConditions", "AccountingOnly", 2, "Receive signed representation letter");
    Assert.Contains("conditional", (await ActivateAsync(pg, w.Partner, created.Value)).Message);
    var current = await Record("Accepted", "AccountingOnly", 2);
    // The conversion-time portal intent stays closed until the Partner activates.
    var contactId = Guid.NewGuid();
    db.ClientContacts.Add(new ClientContact { Id = contactId, FirmId = w.FirmId, PracticeClientId = w.ClientId, FullName = "Owner", Email = "owner@example.test", Role = "Primary contact", Primary = true });
    db.ClientPortalIntents.Add(new AuditSphereOps.Domain.Documents.ClientPortalIntent { Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = w.ClientId, ClientContactId = contactId, RecipientEmail = "owner@example.test", SourceProposalId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    // A hold blocks; a Manager (not a Partner) is refused; the Partner then activates exactly once.
    db.EngagementHolds.Add(new EngagementHold { Id = Guid.NewGuid(), FirmId = w.FirmId, EngagementId = created.Value, HoldKind = "Independence", Reason = "Pending review", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Assert.Equal(ErrorCodes.GateBlocked, (await ActivateAsync(pg, w.Partner, created.Value)).ErrorCode);
    await db.EngagementHolds.Where(x => x.EngagementId == created.Value).ExecuteUpdateAsync(s => s.SetProperty(x => x.Released, true).SetProperty(x => x.ReleasedAt, DateTimeOffset.UtcNow));
    Assert.Equal(ErrorCodes.ScopeDenied, (await ActivateAsync(pg, w.Manager, created.Value)).ErrorCode);
    var activated = await ActivateAsync(pg, w.Partner, created.Value);
    Assert.True(activated.Succeeded, activated.Message);
    Assert.Equal(activated.Value, (await ActivateAsync(pg, w.Partner, created.Value)).Value);
    var live = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == created.Value);
    Assert.Equal(("Active", false), (live.Status, live.ProfessionalWorkBlocked));
    var record = await db.EngagementActivations.AsNoTracking().SingleAsync();
    Assert.Equal((current, 2L, w.PartnerUser.Id), (record.AcceptanceDecisionId, record.ClientGeneration, record.ActivatedByUserId));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM engagement_activations WHERE id = {record.Id}"));
    var intent = await db.ClientPortalIntents.AsNoTracking().SingleAsync(x => x.PracticeClientId == w.ClientId);
    Assert.Equal((AuditSphereOps.Domain.Documents.ClientPortalIntentStates.AwaitingAcceptance, (Guid?)created.Value), (intent.State, intent.ActivatedEngagementId));
  }
}
