using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE 4.1.2 / AS-PAR-009: the firm-approved pricing basis and limits, quotation validity windows, revoked
/// approvals, version-bound respondent authority and durable delivery receipts, exercised against PostgreSQL
/// through the real commands (no direct status edits except backdating the validity window to simulate time).
/// </summary>
[Trait("Profile", "Database")]
public sealed class CommercialPricingPolicyTests
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  private sealed record World(Guid FirmId, ActorContext Prep, ActorContext Partner, ActorContext Manager,
    ActorContext Admin, AppUser AdminUser);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + "-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-commercial-policy",
    Email = $"{name}-{Guid.NewGuid():N}@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var prep = User(firmId, "prep"); var partner = User(firmId, "partner"); var manager = User(firmId, "manager");
    var admin = User(firmId, "admin");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.AddRange(prep, partner, manager, admin);
    foreach (var (user, role) in new[] { (prep, "RelationshipManager"), (partner, "Partner"), (manager, "Manager"), (admin, "Administrator") })
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id });
    foreach (var (role, rate) in new[] { ("Partner", 1000m), ("Manager", 750m) })
      foreach (var currency in new[] { "QAR", "USD" })
        db.RateCardVersions.Add(new RateCardVersion
        {
          Id = Guid.NewGuid(), FirmId = firmId, Version = 1, Role = role, Activity = "Audit", Currency = currency, RatePerHour = rate,
          Status = PracticeTimeStates.RateApproved, CreatedByUserId = partner.Id, ApprovedByUserId = admin.Id,
          ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
        });
    await db.SaveChangesAsync();
    return new(firmId, Actor(prep, "RelationshipManager"), Actor(partner, "Partner"), Actor(manager, "Manager"), Actor(admin, "Administrator"), admin);
  }

  private static readonly QuotationHoursLine[] StandardLines = [new("Partner", "Audit", 10m), new("Manager", "Audit", 20m)];

  private static SaveQuotationRequest Quote(Guid proposalId, decimal discount = 0m, IReadOnlyList<QuotationHoursLine>? lines = null) =>
    new(proposalId, lines ?? StandardLines, 1m, 0m, discount, false, null);

  private static async Task<Guid> DraftProposalAsync(PgTestSchema pg, World w, string clientName = "Gulf Trading LLC", string currency = "QAR")
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest(clientName, "Referral", "A. Owner", "owner@gulf.example.test"));
    Assert.True(lead.Succeeded, lead.Message);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, lead.Value)).Succeeded);
    var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(lead.Value, "FinancialStatementAudit",
      "GULF-TRADING", "2026-01-01", "2026-12-31", 30000m, currency, 60m));
    Assert.True(opportunity.Succeeded, opportunity.Message);
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunity.Value, "AUDIT-2026",
      "Statutory audit of the 2026 financial statements", "Tax advisory", "Independent auditor's report and management letter",
      "Client supplies the trial balance", 1m, currency, "2026-01-01", "2026-12-31"));
    Assert.True(proposal.Succeeded, proposal.Message);
    return proposal.Value;
  }

  private static SaveFirmPricingPolicyRequest Policy(string currency = "QAR", decimal? min = 1000m, decimal? max = 50000m,
    decimal? maxDiscount = 10m, int validityDays = 30) => new(currency, min, max, maxDiscount, validityDays);

  private static async Task<Guid> ApprovePolicyAsync(PgTestSchema pg, World w, SaveFirmPricingPolicyRequest? request = null)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var saved = await FirmPricingPolicyService.SaveAsync(db, w.Partner, request ?? Policy());
    Assert.True(saved.Succeeded, saved.Message);
    Assert.True((await FirmPricingPolicyService.SubmitAsync(db, w.Partner, saved.Value)).Succeeded);
    var approved = await FirmPricingPolicyService.ApproveAsync(db, w.Admin, saved.Value, "Owner-approved pricing basis for the 2026 fee schedule");
    Assert.True(approved.Succeeded, approved.Message);
    return saved.Value;
  }

  [Fact]
  public async Task PricingPolicy_MakerCheckerAndQuotationLimits_FailClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var saved = await FirmPricingPolicyService.SaveAsync(db, w.Partner, Policy());
    Assert.True(saved.Succeeded, saved.Message);
    // A distinct second rule administrator approves; the author cannot approve their own policy.
    Assert.Equal(ErrorCodes.ProtectedState, (await FirmPricingPolicyService.ApproveAsync(db, w.Partner, saved.Value,
      "Author attempting self-approval of the pricing policy")).ErrorCode);
    Assert.True((await FirmPricingPolicyService.SubmitAsync(db, w.Partner, saved.Value)).Succeeded);
    Assert.True((await FirmPricingPolicyService.ApproveAsync(db, w.Admin, saved.Value,
      "Owner-approved pricing basis for the 2026 fee schedule")).Succeeded);
    var active = await FirmPricingPolicyService.ActiveAsync(db, w.Partner, "QAR");
    Assert.True(active.Succeeded, active.Message);
    Assert.Equal(saved.Value, active.Value!.Id);
    Assert.Null((await FirmPricingPolicyService.ActiveAsync(db, w.Partner, "USD")).Value);

    var proposalId = await DraftProposalAsync(pg, w);
    // A discount above the approved cap fails closed; a fee outside the approved band fails closed.
    Assert.Equal("quotation.limit", (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, discount: 12m))).ErrorCode);
    Assert.Equal("quotation.limit", (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, lines: [new("Partner", "Audit", 0.5m)]))).ErrorCode);
    Assert.Equal("quotation.limit", (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, lines: [new("Partner", "Audit", 100m)]))).ErrorCode);
    var within = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, discount: 8m));
    Assert.True(within.Succeeded, within.Message);
    // Without a policy for the currency nothing is bounded: a fresh USD proposal prices freely.
    var usd = await DraftProposalAsync(pg, w, "USD Trading LLC", "USD");
    var usdQuote = await QuotationService.SaveAsync(db, w.Prep, Quote(usd, discount: 90m));
    Assert.True(usdQuote.Succeeded, usdQuote.Message);
  }

  [Fact]
  public async Task ExpiredQuotation_CannotBeDispatchedOrAccepted()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await ApprovePolicyAsync(pg, w);
    var proposalId = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True(quote.Succeeded, quote.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    var version = await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == quote.Value);
    Assert.NotNull(version.ValidUntil); // the approved policy gives the offer an explicit validity window
    Assert.True(version.ValidUntil!.Value > DateTimeOffset.UtcNow.AddDays(29));

    // Backdate the window: dispatch refuses an expired offer.
    await db.QuotationVersions.Where(x => x.Id == quote.Value)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidUntil, DateTimeOffset.UtcNow.AddDays(-1)));
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).ErrorCode);

    // Restore the window: dispatch succeeds and the offer identity binds the exact revision.
    await db.QuotationVersions.Where(x => x.Id == quote.Value)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidUntil, DateTimeOffset.UtcNow.AddDays(30)));
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.NotNull(offerSha);

    // Expire again: acceptance of the dispatched offer is refused while its window has elapsed.
    await db.QuotationVersions.Where(x => x.Id == quote.Value)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidUntil, DateTimeOffset.UtcNow.AddDays(-1)));
    Assert.Equal("crm.expired-offer", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@gulf.example.test"))).ErrorCode);
    await db.QuotationVersions.Where(x => x.Id == quote.Value)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ValidUntil, DateTimeOffset.UtcNow.AddDays(30)));
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@gulf.example.test"))).Succeeded);
  }

  [Fact]
  public async Task RevokedApproval_ReopensQuotationAndInvalidatesSendAndAcceptance()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    // A discount band forces a Manager approval on the quotation; the matrix keys the approval to the rule identity.
    Assert.True((await QuotationService.SaveRuleAsync(db, w.Admin, "DISCOUNT_OVER_PERCENT", 5m, "Manager")).Succeeded);
    var rule = await db.CommercialApprovalRules.AsNoTracking().SingleAsync(x => x.Kind == "DISCOUNT_OVER_PERCENT" && x.Active);
    var ruleKey = rule.Id.ToString("D");
    var proposalId = await DraftProposalAsync(pg, w);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, discount: 10m));
    Assert.True(quote.Succeeded, quote.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    var approved = await QuotationService.ApproveAsync(db, w.Manager, quote.Value, ruleKey,
      "Discount agreed within the reviewed proposal limits");
    Assert.True(approved.Succeeded, approved.Message);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    var approval = await db.QuotationApprovals.AsNoTracking().SingleAsync(x => x.QuotationVersionId == quote.Value);
    // The approver cannot revoke their own approval and a foreign role cannot revoke it either.
    Assert.Equal(ErrorCodes.ProtectedState, (await QuotationService.RevokeApprovalAsync(db, w.Manager, approval.Id,
      "Self-revocation attempt by the original approver")).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await QuotationService.RevokeApprovalAsync(db, w.Partner, approval.Id,
      "Partner attempting to revoke a Manager approval")).ErrorCode);
    Assert.True((await QuotationService.RevokeApprovalAsync(db, w.Admin, approval.Id,
      "Revoked pending ownership re-confirmation of the discount")).Succeeded);
    // The approval row itself is never edited (commercial approval history is append-only): the version stays
    // approved but its required approval no longer stands, which blocks dispatch.
    var reopened = await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == quote.Value);
    Assert.Equal(QuotationStates.Approved, reopened.Status);
    Assert.Equal(ErrorCodes.GateBlocked, (await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).ErrorCode);
    // Re-approval (a fresh decision) restores the offer; the revoked evidence stays on its append-only record.
    Assert.True((await QuotationService.ApproveAsync(db, w.Manager, quote.Value, ruleKey,
      "Re-approved after ownership confirmed the discount")).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    // Revoking again after dispatch invalidates acceptance of the dispatched offer.
    var second = await db.QuotationApprovals.AsNoTracking()
      .Where(x => x.QuotationVersionId == quote.Value &&
        !db.QuotationApprovalRevocations.Any(r => r.FirmId == x.FirmId && r.QuotationApprovalId == x.Id))
      .OrderByDescending(x => x.ApprovedAt).SingleAsync(x => x.Reason.StartsWith("Re-approved"));
    Assert.True((await QuotationService.RevokeApprovalAsync(db, w.Admin, second.Id,
      "Revoked after dispatch pending signatory verification")).Succeeded);
    Assert.Equal("crm.revoked-approval", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@gulf.example.test"))).ErrorCode);
  }

  [Fact]
  public async Task RespondentAuthority_IsBoundToTheDispatchedSignatory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var proposalId = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True(quote.Succeeded, quote.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    // An acceptance from an address that was never dispatched to cannot bind the offer.
    Assert.Equal("crm.unauthorized-respondent", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "stranger@elsewhere.example.test"))).ErrorCode);
    // The addressed signatory (the dispatch recipient) can accept, and the replay is safe.
    var accepted = await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter"));
    Assert.True(accepted.Succeeded, accepted.Message);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@gulf.example.test"))).Succeeded);
  }

  [Fact]
  public async Task DeliveryReceipts_DistinguishRefusalUnknownAndProviderAcceptance()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var proposalId = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True(quote.Succeeded, quote.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var notificationId = (await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.ProposalId == proposalId)).Id;

    // A provider refusal is a durable REJECTED receipt, distinct from an unknown outcome.
    await RunWorkerAsync(pg, w, new RefusingMailSender());
    Assert.Equal(CommercialDeliveryStates.Rejected, (await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == notificationId)).DeliveryState);
    Assert.Null((await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == notificationId)).DeliveredAt);

    // An operator re-arm of the refused send re-enters the same durable path; this time the outcome is unknown.
    await ReArmAsync(pg, w);
    await RunWorkerAsync(pg, w, new FailingMailSender());
    Assert.Equal(CommercialDeliveryStates.Unknown, (await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == notificationId)).DeliveryState);

    // A second re-arm with a working provider ends at a verified receipt: PROVIDER_ACCEPTED, sent exactly once.
    await ReArmAsync(pg, w);
    var sender = new RecordingSender();
    await RunWorkerAsync(pg, w, sender);
    Assert.Equal(CommercialDeliveryStates.ProviderAccepted, (await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == notificationId)).DeliveryState);
    Assert.Single(sender.Plans);
    Assert.NotNull((await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == notificationId)).DeliveredAt);

    // A further operation against the accepted notification is refused: a verified send can never repeat.
    await using var recheck = new AuditSphereDbContext(pg.Options);
    var recheckFactory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var recheckHandler = new CommercialMailDeliveryHandler(recheckFactory, sender);
    await using var recheckTx = await recheck.Database.BeginTransactionAsync();
    var op = await new PostgresOperationStore(recheckFactory)
      .EnqueueAsync(recheck, new OperationRequest(w.FirmId, null, null, CommercialMailDeliveryHandler.Kind, notificationId, 1,
        "commercial-mail-recheck:" + notificationId.ToString("D"), JsonSerializer.Serialize(new { notificationId = notificationId.ToString("D") }, Json)),
        recheckHandler, CancellationToken.None);
    Assert.True(op.Succeeded, op.Message);
    await recheckTx.CommitAsync();
    var options = new WorkerOptions(w.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var recheckWorker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(new PostgresOperationStore(recheckFactory), new DurableOperationRegistry([recheckHandler], options), options),
      [], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    await recheckWorker.ProcessNextAsync();
    Assert.Single(sender.Plans);
    Assert.Equal(OperationState.AUTHORIZATION_BLOCKED,
      (await recheck.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == op.Value)).Status);
  }

  [Fact]
  public async Task ApprovalHistory_IsAppendOnly_AndSupersededRevisionsStayClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.True((await QuotationService.SaveRuleAsync(db, w.Admin, "DISCOUNT_OVER_PERCENT", 5m, "Manager")).Succeeded);
    var ruleKey = (await db.CommercialApprovalRules.AsNoTracking().SingleAsync(x => x.Active)).Id.ToString("D");
    var proposalId = await DraftProposalAsync(pg, w);
    var first = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, discount: 10m));
    Assert.True(first.Succeeded, first.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, first.Value)).Succeeded);
    Assert.True((await QuotationService.ApproveAsync(db, w.Manager, first.Value, ruleKey, "Discount agreed for the first revision")).Succeeded);
    // Replaying a standing approval adds no second decision.
    Assert.True((await QuotationService.ApproveAsync(db, w.Manager, first.Value, ruleKey, "Replay of the standing approval")).Succeeded);
    var approval = await db.QuotationApprovals.AsNoTracking().SingleAsync(x => x.QuotationVersionId == first.Value);

    // The workspace reports the standing approval and offers revocation only to a distinct authorised actor.
    var asAdmin = (await QuotationWorkspaceQuery.GetAsync(db, w.Admin, proposalId)).Value!.Versions.Single();
    Assert.True(asAdmin.ApprovalsStand);
    Assert.Equal((approval.Id, true), (asAdmin.Rules.Single().ApprovalId, asAdmin.Rules.Single().CanRevoke));
    Assert.False((await QuotationWorkspaceQuery.GetAsync(db, w.Manager, proposalId)).Value!.Versions.Single().Rules.Single().CanRevoke);

    Assert.True((await QuotationService.RevokeApprovalAsync(db, w.Admin, approval.Id, "Withdrawn pending discount confirmation")).Succeeded);
    // A replayed revocation is safe and never adds a second withdrawal.
    Assert.True((await QuotationService.RevokeApprovalAsync(db, w.Admin, approval.Id, "Replay of the same withdrawal")).Succeeded);
    var revocation = await db.QuotationApprovalRevocations.AsNoTracking().SingleAsync();
    var withdrawn = (await QuotationWorkspaceQuery.GetAsync(db, w.Manager, proposalId)).Value!.Versions.Single();
    Assert.False(withdrawn.ApprovalsStand);
    Assert.Equal((false, true, "Withdrawn pending discount confirmation"),
      (withdrawn.Rules.Single().Approved, withdrawn.Rules.Single().CanApprove, withdrawn.Rules.Single().RevocationReason));

    // Neither the approval nor its revocation can be edited or removed, even by direct SQL.
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"UPDATE quotation_approvals SET reason = 'edited' WHERE id = {approval.Id}"));
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"DELETE FROM quotation_approval_revocations WHERE id = {revocation.Id}"));
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"UPDATE quotation_approval_revocations SET reason = 'edited again' WHERE id = {revocation.Id}"));

    // Repricing supersedes the first revision; a superseded revision accepts no approval and no revocation.
    var second = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, discount: 8m));
    Assert.True(second.Succeeded, second.Message);
    Assert.Equal(QuotationStates.Superseded, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == first.Value)).Status);
    Assert.Equal(ErrorCodes.ProtectedState, (await QuotationService.ApproveAsync(db, w.Manager, first.Value, ruleKey,
      "Late approval of a superseded revision")).ErrorCode);
    Assert.Single(await db.QuotationApprovals.AsNoTracking().Where(x => x.QuotationVersionId == first.Value).ToListAsync());
  }

  [Fact]
  public async Task PricingPolicy_AllowsAnUncappedDiscount_AndVersionsAreImmutable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    // A policy may bound only the fee band; the discount cap is optional.
    var saved = await FirmPricingPolicyService.SaveAsync(db, w.Partner, Policy(maxDiscount: null));
    Assert.True(saved.Succeeded, saved.Message);
    var draft = (await FirmPricingPolicyService.WorkspaceAsync(db, w.Partner)).Value!.Single();
    Assert.Equal(("1000", "50000", null, "30", true, false, false),
      (draft.MinimumFee, draft.MaximumFee, draft.MaxDiscountPercent, draft.ValidityDays, draft.CanSubmit, draft.CanApprove, draft.Current));
    Assert.True((await FirmPricingPolicyService.SubmitAsync(db, w.Partner, saved.Value)).Succeeded);
    // The author never sees an approve action; a distinct rule administrator does; a non-administrator sees neither.
    Assert.False((await FirmPricingPolicyService.WorkspaceAsync(db, w.Partner)).Value!.Single().CanApprove);
    Assert.True((await FirmPricingPolicyService.WorkspaceAsync(db, w.Admin)).Value!.Single().CanApprove);
    Assert.False((await FirmPricingPolicyService.WorkspaceAsync(db, w.Manager)).Value!.Single().CanApprove);
    Assert.Equal(ErrorCodes.ScopeDenied, (await FirmPricingPolicyService.ApproveAsync(db, w.Manager, saved.Value,
      "Manager attempting to approve the pricing policy")).ErrorCode);
    Assert.True((await FirmPricingPolicyService.ApproveAsync(db, w.Admin, saved.Value, "Approved fee band for the 2026 schedule")).Succeeded);
    Assert.True((await FirmPricingPolicyService.WorkspaceAsync(db, w.Admin)).Value!.Single().Current);
    // An approved version's limits and state cannot be rewritten; a change is a new version.
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"UPDATE firm_pricing_policies SET maximum_fee = 999999 WHERE id = {saved.Value}"));
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"UPDATE firm_pricing_policies SET status = 'DRAFT', approved_by_user_id = NULL, approved_at = NULL WHERE id = {saved.Value}"));
    var next = await FirmPricingPolicyService.SaveAsync(db, w.Admin, Policy(max: 80000m, maxDiscount: 15m) with { ExpectedRevision = 1 });
    Assert.True(next.Succeeded, next.Message);
    Assert.Equal(ErrorCodes.StaleRevision, (await FirmPricingPolicyService.SaveAsync(db, w.Admin,
      Policy(max: 90000m) with { ExpectedRevision = 1 })).ErrorCode);
    // The approved version keeps governing until the new one is approved.
    Assert.Equal(saved.Value, (await FirmPricingPolicyService.ActiveAsync(db, w.Partner, "QAR")).Value!.Id);
  }

  [Fact]
  public async Task Acceptance_RequiresTheSignatoryEmail()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var proposalId = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    // A name alone identifies nobody: acceptance without the signatory's email cannot bypass the authority check.
    Assert.Equal("crm.invalid", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner"))).ErrorCode);
    Assert.Equal(CrmStates.ProposalSent, (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).Status);
  }

  [Fact]
  public async Task Migration_MapsLegacyReceiptStates_AndKeepsVerifiedReceiptsTerminal()
  {
    await using var pg = await PgTestSchema.CreateAsync("20261010131335_PracticeTimeFsliAttribution");
    var firmId = Guid.NewGuid();
    var sent = Guid.NewGuid(); var failed = Guid.NewGuid(); var queued = Guid.NewGuid();
    await using (var before = new AuditSphereDbContext(pg.Options))
    {
      foreach (var (id, state, delivered) in new[]
      {
        (sent, "SENT", (DateTimeOffset?)DateTimeOffset.UtcNow), (failed, "FAILED", null), (queued, "QUEUED", null)
      })
        await before.Database.ExecuteSqlAsync($"""
          INSERT INTO commercial_notifications (id, firm_id, kind, proposal_id, recipient, subject, body, delivery_state, created_at, delivered_at)
          VALUES ({id}, {firmId}, 'PROPOSAL', {Guid.NewGuid()}, 'owner@gulf.example.test', 'Proposal', 'Body', {state}, now(), {delivered})
          """);
      // The upgrade must succeed on a database that already holds a sent notification.
      await before.GetService<IMigrator>().MigrateAsync();
    }
    await using var db = new AuditSphereDbContext(pg.Options);
    var states = await db.CommercialNotifications.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.DeliveryState);
    Assert.Equal(CommercialDeliveryStates.ProviderAccepted, states[sent]);
    Assert.Equal(CommercialDeliveryStates.Unknown, states[failed]);
    Assert.Equal(CommercialDeliveryStates.Queued, states[queued]);
    // A verified receipt never regresses and the queued content never changes; other receipts still advance.
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"UPDATE commercial_notifications SET delivery_state = 'QUEUED', delivered_at = NULL WHERE id = {sent}"));
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"UPDATE commercial_notifications SET recipient = 'other@elsewhere.example.test' WHERE id = {queued}"));
    await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlAsync(
      $"DELETE FROM commercial_notifications WHERE id = {queued}"));
    Assert.Equal(1, await db.Database.ExecuteSqlAsync(
      $"UPDATE commercial_notifications SET delivery_state = 'DISPATCHED' WHERE id = {failed}"));
    Assert.Equal(1, await db.Database.ExecuteSqlAsync(
      $"UPDATE commercial_notifications SET delivery_state = 'DELIVERED' WHERE id = {sent}"));
  }

  private static async Task RunWorkerAsync(PgTestSchema pg, World w, IPbcMailSender sender)
  {
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var handler = new CommercialMailDeliveryHandler(factory, sender);
    var options = new WorkerOptions(w.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new CommercialMailDiscovery(factory, store, handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    // One pass enqueues the queued dispatch and runs the operation to its disposition; a re-armed
    // operation carries a short recovery delay, so a few bounded passes cover the 5-second window.
    for (var attempt = 0; attempt < 6; attempt++)
    {
      if (await worker.ProcessNextAsync()) return;
      await Task.Delay(1500);
    }
    Assert.Fail("The commercial mail operation was never claimed.");
  }

  private static async Task ReArmAsync(PgTestSchema pg, World w)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var admin = Actor(w.AdminUser, "Administrator");
    var op = await db.DurableOperations.AsNoTracking()
      .Where(o => o.FirmId == w.FirmId && o.OperationKind == CommercialMailDeliveryHandler.Kind &&
        (o.Status == OperationState.PROVIDER_BLOCKED || o.Status == OperationState.RESULT_UNCERTAIN || o.Status == OperationState.DEAD_LETTER))
      .OrderByDescending(o => o.CreatedAt).FirstAsync();
    var rearm = await OperationRecoveryService.RetryAsync(db, admin, new OperationRecoveryRequest(op.Id));
    Assert.True(rearm.Succeeded, rearm.Message);
  }

  private sealed class RecordingSender : IPbcMailSender
  {
    public List<PbcMailPlan> Plans { get; } = [];
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct) { Plans.Add(plan); return Task.CompletedTask; }
  }

  private sealed class RefusingMailSender : IPbcMailSender
  {
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct) => throw new OperationBlockedException("provider-refused-recipient");
  }

  private sealed class FailingMailSender : IPbcMailSender
  {
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct) => throw new InvalidOperationException("connection dropped mid-send");
  }
}
