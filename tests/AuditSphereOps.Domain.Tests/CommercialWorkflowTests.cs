using System.Text.Json;
using System.Globalization;
using System.IO.Compression;
using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Commercial calculation, approval matrix, branded documents and the agreed-fee 50/50 cycle against PostgreSQL,
/// using real role identities and the real billing, release and mail-worker paths (no direct status edits).
/// </summary>
[Trait("Profile", "Database")]
public sealed class CommercialWorkflowTests
{
  private sealed record World(Guid FirmId, ActorContext Prep, ActorContext Partner, ActorContext Manager, ActorContext FinanceManager,
    ActorContext FinanceReviewer, AppUser PartnerUser);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + "-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-commercial",
    Email = $"{name}-{Guid.NewGuid():N}@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var prep = User(firmId, "prep"); var partner = User(firmId, "partner"); var manager = User(firmId, "manager");
    var fm = User(firmId, "financemanager"); var fr = User(firmId, "financereviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.AddRange(prep, partner, manager, fm, fr);
    foreach (var (user, role) in new[] { (prep, "RelationshipManager"), (partner, "Partner"), (manager, "Manager"), (fm, "FinanceManager"), (fr, "FinanceReviewer") })
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id });
    foreach (var (role, rate) in new[] { ("Partner", 1000m), ("Manager", 750m) })
      db.RateCardVersions.Add(new RateCardVersion
      {
        Id = Guid.NewGuid(), FirmId = firmId, Version = 1, Role = role, Activity = "Audit", Currency = "QAR", RatePerHour = rate,
        Status = PracticeTimeStates.RateApproved, CreatedByUserId = partner.Id, ApprovedByUserId = manager.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
    await db.SaveChangesAsync();
    return new(firmId, Actor(prep, "RelationshipManager"), Actor(partner, "Partner"), Actor(manager, "Manager"), Actor(fm, "FinanceManager"), Actor(fr, "FinanceReviewer"), partner);
  }

  private static readonly QuotationHoursLine[] StandardLines = [new("Partner", "Audit", 10m), new("Manager", "Audit", 20m)];
  private const string TestLogoPngBase64 = "iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAN0lEQVR4nO3QQREAMAgDQYoYvGGtJstUBZ+Ngcvsqb4vFpebcQcIECBAgAABAgQIECBAgACBLzDWWQJ4Rpw0uQAAAABJRU5ErkJggg==";

  private static SaveQuotationRequest Quote(Guid proposalId, decimal complexity = 1m, decimal risk = 0m, decimal discount = 0m,
    bool nonStandard = false, string? note = null, IReadOnlyList<QuotationHoursLine>? lines = null) =>
    new(proposalId, lines ?? StandardLines, complexity, risk, discount, nonStandard, note);

  private static async Task<(Guid LeadId, Guid OpportunityId, Guid ProposalId)> DraftProposalAsync(PgTestSchema pg, World w, string timeline = "Fieldwork in March 2027; report by 30 April 2027")
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest("Gulf Trading LLC", "Referral", "A. Owner", "owner@gulf.example.test"));
    Assert.True(lead.Succeeded, lead.Message);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, lead.Value)).Succeeded);
    var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(lead.Value, "FinancialStatementAudit",
      "GULF-TRADING", "2026-01-01", "2026-12-31", 30000m, "QAR", 60m));
    Assert.True(opportunity.Succeeded, opportunity.Message);
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunity.Value, "AUDIT-2026",
      "Statutory audit of the 2026 financial statements", "Tax advisory", "Independent auditor's report and management letter",
      "Client supplies the trial balance", 1m, "QAR", "2026-01-01", "2026-12-31",
      Timeline: timeline));
    Assert.True(proposal.Succeeded, proposal.Message);
    return (lead.Value, opportunity.Value, proposal.Value);
  }

  private sealed class PauseFirstProposalRead : DbCommandInterceptor
  {
    private int paused;
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("proposals", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref paused, 1, 0) == 0)
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  [Fact]
  public async Task ProposalWorkspaceKeepsResponseReadInOneSnapshotAndRechecksCurrentAuthority()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var draft = await DraftProposalAsync(pg, w);
    string offerSha;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await PracticeCrmService.ApproveProposalAsync(setup, w.Partner, draft.ProposalId)).Succeeded);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(setup, w.Prep, draft.ProposalId);
      Assert.True((await PracticeCrmService.SendProposalAsync(setup, w.Prep, draft.ProposalId)).Succeeded);
      offerSha = (await setup.Proposals.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Id == draft.ProposalId)).SentOfferSha256!;
      Assert.False(string.IsNullOrWhiteSpace(offerSha));
    }

    var pause = new PauseFirstProposalRead();
    await using var delayed = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var acceptance = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    try
    {
      var pendingRead = CommercialWorkspaceQuery.ProposalAsync(delayed, w.Prep, draft.ProposalId, timeout.Token);
      await pause.Reached.Task.WaitAsync(timeout.Token);

      // Record a real immutable acceptance on an independent context between the proposal and response-record reads.
      var accepted = await PracticeCrmService.RecordProposalResponseAsync(acceptance, w.Prep, draft.ProposalId,
        new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter",
          Channel: ProposalResponseChannels.Email), timeout.Token);
      Assert.True(accepted.Succeeded, accepted.Message);
      var receipt = await acceptance.ProposalResponseRecords.AsNoTracking()
        .SingleAsync(x => x.FirmId == w.FirmId && x.ProposalId == draft.ProposalId, timeout.Token);
      Assert.Equal(offerSha, receipt.OfferSha256);
      var writerResponseAt = await acceptance.Proposals.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId && x.Id == draft.ProposalId)
        .Select(x => x.ResponseAt).SingleAsync(timeout.Token);
      Assert.Equal(receipt.RecordedAt, writerResponseAt);

      pause.Release.TrySetResult(true);
      var oldSnapshot = await pendingRead;
      Assert.True(oldSnapshot.Succeeded, oldSnapshot.Message);
      Assert.Equal(CrmStates.ProposalSent, oldSnapshot.Value!.Status);
      Assert.Equal(offerSha, oldSnapshot.Value.SentOfferSha256);
      Assert.Equal(CrmStates.ProposalSent, Assert.Single(oldSnapshot.Value.Versions).Status);
      Assert.Null(oldSnapshot.Value.ResponseAt);
      Assert.Null(oldSnapshot.Value.ResponseOfferSha256);
      Assert.Null(oldSnapshot.Value.RespondentName);
      Assert.Null(oldSnapshot.Value.RespondentEmail);
      Assert.Null(oldSnapshot.Value.ResponseEvidenceReference);
      Assert.Null(oldSnapshot.Value.ResponseChannel);
      Assert.Null(oldSnapshot.Value.ResponseRecordedByName);

      await using var fresh = new AuditSphereDbContext(pg.Options);
      var current = await CommercialWorkspaceQuery.ProposalAsync(fresh, w.Prep, draft.ProposalId, timeout.Token);
      Assert.True(current.Succeeded, current.Message);
      Assert.Equal(CrmStates.ProposalAccepted, current.Value!.Status);
      Assert.Equal(offerSha, current.Value.ResponseOfferSha256);
      Assert.Equal(receipt.RecordedAt, current.Value.ResponseAt);
      Assert.Equal("A. Owner", current.Value.RespondentName);
      Assert.Equal("owner@gulf.example.test", current.Value.RespondentEmail);
      Assert.Equal("Signed acceptance letter", current.Value.ResponseEvidenceReference);
      Assert.Equal(ProposalResponseChannels.Email, current.Value.ResponseChannel);

      await using var callerTransactionDb = new AuditSphereDbContext(pg.Options);
      await using var callerTransaction = await callerTransactionDb.Database.BeginTransactionAsync(timeout.Token);
      var rejectedTransactionRead = await CommercialWorkspaceQuery.ProposalAsync(
        callerTransactionDb, w.Prep, draft.ProposalId, timeout.Token);
      Assert.False(rejectedTransactionRead.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, rejectedTransactionRead.ErrorCode);
      Assert.NotNull(callerTransactionDb.Database.CurrentTransaction);
      Assert.Equal(1, await callerTransactionDb.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
        .SingleAsync(timeout.Token));
      await callerTransaction.CommitAsync(timeout.Token);
    }
    finally
    {
      pause.Release.TrySetResult(true);
    }

    // A grant revoked after the first row read must be visible to the final authorization check.
    var authorityPause = new PauseFirstProposalRead();
    await using var authorityReader = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(authorityPause).Options);
    await using var grantWriter = new AuditSphereDbContext(pg.Options);
    try
    {
      var pendingRead = CommercialWorkspaceQuery.ProposalAsync(authorityReader, w.Prep, draft.ProposalId, timeout.Token);
      await authorityPause.Reached.Task.WaitAsync(timeout.Token);
      var revoked = await grantWriter.RoleGrants.Where(x => x.FirmId == w.FirmId && x.UserId == w.Prep.UserId &&
          x.Role == "RelationshipManager" && x.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
      Assert.Equal(1, revoked);
      authorityPause.Release.TrySetResult(true);
      var refused = await pendingRead;
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, refused.ErrorCode);
      Assert.Null(refused.Value);
    }
    finally
    {
      authorityPause.Release.TrySetResult(true);
    }
  }

  [Fact]
  public async Task AngularCommercialSettingsRespectEditAuthorityAndReviewedRevisions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var readOnly = await CommercialSettingsQuery.GetAsync(db, w.Prep);
    Assert.True(readOnly.Succeeded, readOnly.Message);
    Assert.False(readOnly.Value!.CanEdit);
    var initial = await CommercialSettingsQuery.GetAsync(db, w.Partner);
    Assert.True(initial.Value!.CanEdit);
    var request = new SaveCommercialProfileRequest("Synthetic Settings Firm", "Address", "", "", "#0F766E", "Closing", ExpectedVersion: 0);
    Assert.False((await CommercialDocumentService.SaveProfileAsync(db, w.Prep, request)).Succeeded);
    var saved = await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request);
    Assert.True(saved.Succeeded, saved.Message);
    Assert.Equal(saved.Value, (await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request)).Value);
    Assert.False((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request with { LegalName = "Stale revised name" })).Succeeded);
    var rule = await QuotationService.SaveRuleAsync(db, w.Partner, "DISCOUNT_OVER_PERCENT", 5m, "Manager",
      expectedRulesRevision: initial.Value.RulesRevision);
    Assert.True(rule.Succeeded, rule.Message);
    var current = await CommercialSettingsQuery.GetAsync(db, w.Partner);
    Assert.NotEqual(initial.Value.RulesRevision, current.Value!.RulesRevision);
    Assert.Equal("1", current.Value.Profile!.Version);
    Assert.Single(current.Value.Rules);
    var stale = await QuotationService.SaveRuleAsync(db, w.Partner, "DISCOUNT_OVER_PERCENT", 10m, "Partner",
      expectedRulesRevision: initial.Value.RulesRevision);
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.False((await QuotationService.DeactivateRuleAsync(db, w.Prep, rule.Value, expectedRulesRevision: current.Value.RulesRevision)).Succeeded);
    Assert.True((await QuotationService.DeactivateRuleAsync(db, w.Partner, rule.Value, expectedRulesRevision: current.Value.RulesRevision)).Succeeded);
    var inactive = await CommercialSettingsQuery.GetAsync(db, w.Partner);
    Assert.Empty(inactive.Value!.Rules);
    Assert.Equal(initial.Value.RulesRevision, inactive.Value.RulesRevision);
    Assert.False((await QuotationService.SaveRuleAsync(db, w.Partner, "NON_STANDARD_TERMS", null, "Partner",
      expectedRulesRevision: current.Value.RulesRevision)).Succeeded);
  }

  [Theory]
  [InlineData("SUBMIT_QUOTATION")]
  [InlineData("SAVE_QUOTATION")]
  [InlineData("SAVE_RULE")]
  [InlineData("DEACTIVATE_RULE")]
  [InlineData("SAVE_RULE_GRANT_ONLY")]
  public async Task QuotationCommands_RecheckAuthorityAfterFirmLockWait(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var submitting = action == "SUBMIT_QUOTATION";
    var writingQuotation = action is "SUBMIT_QUOTATION" or "SAVE_QUOTATION";
    var grantOnly = action == "SAVE_RULE_GRANT_ONLY";
    var commandActor = writingQuotation ? w.Prep : w.Partner;
    var commandRole = writingQuotation ? "RelationshipManager" : "Partner";
    SaveQuotationRequest? quotationRequest = null;
    Guid quotationId = default;
    Guid ruleId = default;

    if (writingQuotation)
    {
      var proposal = await DraftProposalAsync(pg, w);
      quotationRequest = Quote(proposal.ProposalId) with { RequestId = Guid.NewGuid() };
      if (submitting)
      {
        await using var setup = new AuditSphereDbContext(pg.Options);
        var saved = await QuotationService.SaveAsync(setup, w.Prep, quotationRequest);
        Assert.True(saved.Succeeded, saved.Message);
        quotationId = saved.Value;
      }
    }
    else
    {
      await using var setup = new AuditSphereDbContext(pg.Options);
      var saved = await QuotationService.SaveRuleAsync(setup, w.Partner, CommercialRuleKinds.DiscountOver,
        5m, "Manager");
      Assert.True(saved.Succeeded, saved.Message);
      if (action == "DEACTIVATE_RULE") ruleId = saved.Value;
    }

    Guid grantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      grantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == w.FirmId &&
          x.UserId == commandActor.UserId && x.Role == commandRole && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync();
    }

    async Task<string> SnapshotAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var proposals = await db.Proposals.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var quotations = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var rules = await db.CommercialApprovalRules.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var approvals = await db.QuotationApprovals.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var revocations = await db.QuotationApprovalRevocations.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      return JsonSerializer.Serialize(new { proposals, quotations, rules, approvals, revocations });
    }

    var before = await SnapshotAsync();
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {w.FirmId} FOR UPDATE").SingleAsync();

    var lockAttempt = new ObserveFirmLockAttempt();
    var writerOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(lockAttempt).Options;
    await using var writer = new AuditSphereDbContext(writerOptions);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    async Task<(bool Succeeded, string? ErrorCode)> ExecuteWriterAsync()
    {
      if (action == "SUBMIT_QUOTATION")
      {
        var result = await QuotationService.SubmitAsync(writer, commandActor, quotationId, timeout.Token);
        return (result.Succeeded, result.ErrorCode);
      }
      if (action == "SAVE_QUOTATION")
      {
        var result = await QuotationService.SaveAsync(writer, commandActor, quotationRequest!, timeout.Token);
        return (result.Succeeded, result.ErrorCode);
      }
      if (action == "DEACTIVATE_RULE")
      {
        var result = await QuotationService.DeactivateRuleAsync(writer, commandActor, ruleId, timeout.Token);
        return (result.Succeeded, result.ErrorCode);
      }

      var saved = await QuotationService.SaveRuleAsync(writer, commandActor, CommercialRuleKinds.DiscountOver,
        10m, "Manager", timeout.Token);
      return (saved.Succeeded, saved.ErrorCode);
    }

    var attempt = ExecuteWriterAsync();
    await lockAttempt.Reached.Task.WaitAsync(timeout.Token);
    Assert.True(lockAttempt.Fired);
    Assert.Equal(1, await blocker.RoleGrants.Where(x => x.Id == grantId && x.RevokedAt == null)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token));
    if (!grantOnly)
      Assert.Equal(1, await blocker.Users.Where(x => x.Id == commandActor.UserId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token));
    await blockerTransaction.CommitAsync(timeout.Token);

    var outcome = await attempt.WaitAsync(timeout.Token);
    Assert.False(outcome.Succeeded);
    Assert.Equal(grantOnly ? ErrorCodes.ScopeDenied : ErrorCodes.GenerationStale, outcome.ErrorCode);
    Assert.Equal(before, await SnapshotAsync());
  }

  [Theory]
  [InlineData("BRIEF_NEW")]
  [InlineData("BRIEF_REPLAY")]
  [InlineData("TENDER")]
  [InlineData("BRIEF_GRANT_ONLY")]
  public async Task CommercialDocumentGeneration_RechecksAuthorityAfterFirmLockWait(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var grantOnly = action == "BRIEF_GRANT_ONLY";
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    Guid quotationId;

    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var quotation = await QuotationService.SaveAsync(setup, w.Prep, Quote(proposalId));
      Assert.True(quotation.Succeeded, quotation.Message);
      quotationId = quotation.Value;
      var submitted = await QuotationService.SubmitAsync(setup, w.Prep, quotationId);
      Assert.True(submitted.Succeeded, submitted.Message);
      Assert.Equal(QuotationStates.Approved,
        await setup.QuotationVersions.AsNoTracking().Where(x => x.Id == quotationId).Select(x => x.Status).SingleAsync());

      var profile = new SaveCommercialProfileRequest("Lock-wait synthetic firm", "Doha", "", "", "#0F766E", "Closing",
        "Reviewed firm history and registrations", "Reviewed industry credentials", "Reviewed audit methodology");
      var savedProfile = await CommercialDocumentService.SaveProfileAsync(setup, w.Partner, profile);
      Assert.True(savedProfile.Succeeded, savedProfile.Message);

      if (action == "BRIEF_REPLAY")
      {
        var existing = await CommercialDocumentService.GenerateBriefQuotationAsync(setup, w.Prep, proposalId,
          expectedQuotationId: quotationId, expectedProfileVersion: 1);
        Assert.True(existing.Succeeded, existing.Message);
        Assert.NotEmpty(existing.Value!.Bytes);
      }
    }

    Guid grantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      grantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == w.FirmId &&
          x.UserId == w.Prep.UserId && x.Role == "RelationshipManager" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync();
    }

    async Task<string> SnapshotDocumentsAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var documents = await db.CommercialDocuments.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      return JsonSerializer.Serialize(documents);
    }

    var before = await SnapshotDocumentsAsync();
    await using (var verify = new AuditSphereDbContext(pg.Options))
      Assert.Equal(action == "BRIEF_REPLAY" ? 1 : 0,
        await verify.CommercialDocuments.AsNoTracking().CountAsync(x => x.FirmId == w.FirmId));

    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {w.FirmId} FOR UPDATE").SingleAsync();

    var lockAttempt = new ObserveFirmLockAttempt();
    var writerOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(lockAttempt).Options;
    await using var writer = new AuditSphereDbContext(writerOptions);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    async Task<(bool Succeeded, string? ErrorCode, CommercialDocument? Value)> ExecuteWriterAsync()
    {
      if (action == "TENDER")
      {
        var result = await CommercialDocumentService.GenerateTenderAsync(writer, w.Prep, proposalId,
          "Reviewed assigned team CVs", "Reviewed delivery timeline", true, timeout.Token,
          expectedQuotationId: quotationId, expectedProfileVersion: 1);
        return (result.Succeeded, result.ErrorCode, result.Value);
      }

      var brief = await CommercialDocumentService.GenerateBriefQuotationAsync(writer, w.Prep, proposalId,
        timeout.Token, expectedQuotationId: quotationId, expectedProfileVersion: 1);
      return (brief.Succeeded, brief.ErrorCode, brief.Value);
    }

    var attempt = ExecuteWriterAsync();
    await lockAttempt.Reached.Task.WaitAsync(timeout.Token);
    Assert.True(lockAttempt.Fired);
    Assert.Equal(1, await blocker.RoleGrants.Where(x => x.Id == grantId && x.RevokedAt == null)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token));
    if (!grantOnly)
      Assert.Equal(1, await blocker.Users.Where(x => x.Id == w.Prep.UserId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token));
    await blockerTransaction.CommitAsync(timeout.Token);

    var outcome = await attempt.WaitAsync(timeout.Token);
    Assert.False(outcome.Succeeded);
    Assert.Equal(grantOnly ? ErrorCodes.ScopeDenied : ErrorCodes.GenerationStale, outcome.ErrorCode);
    Assert.Null(outcome.Value);
    Assert.Equal(before, await SnapshotDocumentsAsync());
  }

  [Fact]
  public async Task PortalLogoChangesAreAppendOnlyVersionedAndKeepExactBytesAcrossProfileEdits()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var png = Convert.FromBase64String(TestLogoPngBase64);
    var hash = Hashing.Sha256Hex(png);
    var initialRequest = new SaveCommercialProfileRequest("Synthetic logo firm", "Doha", "firm@example.test", "123",
      "#2B6CB0", "Closing", ExpectedVersion: 0);
    var retainedDocumentBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x10, 0x20 };
    var retainedDocument = new CommercialDocument
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Kind = CommercialDocumentKinds.Quotation,
      TemplateVersion = "synthetic-template-v1", ProfileVersion = 1, FileName = "synthetic-quotation.docx",
      ContentType = "application/octet-stream", Bytes = retainedDocumentBytes,
      Sha256Hex = Hashing.Sha256Hex(retainedDocumentBytes), CreatedByUserId = w.Partner.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.CommercialDocuments.Add(retainedDocument);
      await db.SaveChangesAsync();
      var initial = await CommercialDocumentService.SaveProfileAsync(db, w.Partner, initialRequest);
      Assert.True(initial.Succeeded, initial.Message);
      var replacement = await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
        initialRequest with { ExpectedVersion = 1, LogoPngBase64 = TestLogoPngBase64 });
      Assert.True(replacement.Succeeded, replacement.Message);
      var versions = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Version).ToListAsync();
      Assert.Equal(new long[] { 1, 2 }, versions.Select(x => x.Version));
      Assert.Null(versions[0].LogoPngBytes);
      Assert.Null(versions[0].LogoSha256Hex);
      Assert.Equal(png, versions[1].LogoPngBytes);
      Assert.Equal(hash, versions[1].LogoSha256Hex);

      var keepTextEdit = await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
        initialRequest with { LegalName = "Synthetic revised logo firm", ExpectedVersion = 2 });
      Assert.True(keepTextEdit.Succeeded, keepTextEdit.Message);
      var current = await db.FirmCommercialProfiles.AsNoTracking().SingleAsync(x => x.Id == keepTextEdit.Value);
      Assert.Equal(png, current.LogoPngBytes);
      Assert.Equal(hash, current.LogoSha256Hex);

      var removed = await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
        initialRequest with { LegalName = "Synthetic revised logo firm", ExpectedVersion = 3, RemoveLogo = true });
      Assert.True(removed.Succeeded, removed.Message);
      var removedProfile = await db.FirmCommercialProfiles.AsNoTracking().SingleAsync(x => x.Id == removed.Value);
      Assert.Equal(4, removedProfile.Version);
      Assert.Null(removedProfile.LogoPngBytes);
      Assert.Null(removedProfile.LogoSha256Hex);
      Assert.Equal(png, (await db.FirmCommercialProfiles.AsNoTracking().SingleAsync(x => x.Version == 2)).LogoPngBytes);
      var retained = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == retainedDocument.Id);
      Assert.Equal(retainedDocumentBytes, retained.Bytes);
      Assert.Equal(retainedDocument.Sha256Hex, retained.Sha256Hex);

      var retry = await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
        initialRequest with { LegalName = "Synthetic revised logo firm", ExpectedVersion = 3, RemoveLogo = true });
      Assert.Equal(removed.Value, retry.Value);
      Assert.Equal(4, await db.FirmCommercialProfiles.CountAsync(x => x.FirmId == w.FirmId));

      var stale = await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
        initialRequest with { LegalName = "Different stale profile", ExpectedVersion = 2 });
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    }
  }

  [Fact]
  public async Task PortalLogoValidationAndNonFirmWideRolesFailWithoutWritingProfiles()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var png = Convert.FromBase64String(TestLogoPngBase64);
    var widePng = png.ToArray();
    widePng[16] = 0;
    widePng[17] = 0;
    widePng[18] = 0x04;
    widePng[19] = 0x01;
    var baseline = new SaveCommercialProfileRequest("Synthetic logo firm", "Doha", "", "", "#2B6CB0", "", ExpectedVersion: 0);
    await using var db = new AuditSphereDbContext(pg.Options);

    var invalidRequests = new (SaveCommercialProfileRequest Request, string Code, string Message)[]
    {
      (baseline with { LogoPngBase64 = TestLogoPngBase64, RemoveLogo = true }, "commercial.logo-invalid",
        "Choose either a replacement PNG or Remove logo, not both."),
      (baseline with { LogoPngBase64 = "data:image/png;base64," + TestLogoPngBase64 }, "commercial.logo-invalid",
        "Upload a PNG logo of at most 128 KiB."),
      (baseline with { LogoPngBase64 = " " + TestLogoPngBase64 }, "commercial.logo-invalid",
        "Upload a PNG logo of at most 128 KiB."),
      (baseline with { LogoPngBase64 = Convert.ToBase64String(new byte[131073]) }, "commercial.logo-invalid",
        "Upload a PNG logo of at most 128 KiB."),
      (baseline with { LogoPngBase64 = Convert.ToBase64String(widePng) }, "commercial.logo-invalid",
        "The logo must be a readable PNG between 16x16 and 1024x512 pixels."),
      (baseline with { LogoPngBase64 = Convert.ToBase64String([1, 2, 3]) }, "commercial.logo-invalid",
        "The logo must be a readable PNG between 16x16 and 1024x512 pixels."),
      (baseline with { ExpectedVersion = null, LogoPngBase64 = TestLogoPngBase64 }, "commercial.logo-review-required",
        "Reload the firm profile and review its version before changing the logo.")
    };
    foreach (var (request, code, message) in invalidRequests)
    {
      var result = await CommercialDocumentService.SaveProfileAsync(db, w.Partner, request);
      Assert.False(result.Succeeded);
      Assert.Equal(code, result.ErrorCode);
      Assert.Equal(message, result.Message);
    }
    Assert.Empty(await db.FirmCommercialProfiles.Where(x => x.FirmId == w.FirmId).ToListAsync());

    var client = new PracticeClient { Id = Guid.NewGuid(), FirmId = w.FirmId, LegalName = "Synthetic client", CreatedAt = DateTimeOffset.UtcNow };
    var scopedPartner = User(w.FirmId, "scoped-partner");
    var clientUser = User(w.FirmId, "client");
    clientUser.UserKind = "Client";
    db.PracticeClients.Add(client);
    db.Users.AddRange(scopedPartner, clientUser);
    db.RoleGrants.AddRange(
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = scopedPartner.Id, Role = "Partner", ClientId = client.Id, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientUser.Id, Role = "ClientUser", ClientId = client.Id, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId });
    await db.SaveChangesAsync();

    foreach (var actor in new[] { w.Prep, w.Manager, Actor(scopedPartner, "Partner"), Actor(clientUser, "ClientUser") })
    {
      var denied = await CommercialDocumentService.SaveProfileAsync(db, actor,
        baseline with { LogoPngBase64 = TestLogoPngBase64 });
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
    var administrator = User(w.FirmId, "administrator");
    db.Users.Add(administrator);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = administrator.Id, Role = "Administrator",
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
    });
    await db.SaveChangesAsync();
    var adminWrite = await CommercialDocumentService.SaveProfileAsync(db, Actor(administrator, "Administrator"), baseline);
    Assert.True(adminWrite.Succeeded, adminWrite.Message);
    Assert.Single(await db.FirmCommercialProfiles.Where(x => x.FirmId == w.FirmId).ToListAsync());
    Assert.True(png.Length is > 0 and <= 131072);
  }

  [Fact]
  public async Task ConcurrentPortalLogoProfileWritesCannotOverwriteTheWinningRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var initial = new SaveCommercialProfileRequest("Synthetic concurrent firm", "Doha", "", "", "#2B6CB0", "", ExpectedVersion: 0);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, initial)).Succeeded);

    async Task<CommandResult<Guid>> ReviseAsync(string legalName)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
        initial with { LegalName = legalName, ExpectedVersion = 1, LogoPngBase64 = TestLogoPngBase64 });
    }

    var results = await Task.WhenAll(ReviseAsync("Synthetic concurrent firm A"), ReviseAsync("Synthetic concurrent firm B"));
    Assert.Single(results, x => x.Succeeded);
    var refused = Assert.Single(results, x => !x.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, refused.ErrorCode);
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await check.FirmCommercialProfiles.CountAsync(x => x.FirmId == w.FirmId));
    Assert.Equal(2, await check.FirmCommercialProfiles.Where(x => x.FirmId == w.FirmId).MaxAsync(x => x.Version));
  }

  [Fact]
  public async Task AngularFeeWorkspaceIsFirmScopedAndConcurrentEngagementLinksNeverOverwrite()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var blocked = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(blocked.Succeeded, blocked.Message);
    Assert.False(blocked.Value!.CanCreate);
    Assert.NotEmpty(blocked.Value.CreationBlockers);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalId);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    var client = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Fee workspace client"));
    var eligible = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(eligible.Value!.CanCreate);
    Assert.False(eligible.Value.CanFinance);
    var created = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.True(created.Succeeded, created.Message);
    var engagementA = Guid.NewGuid(); var engagementB = Guid.NewGuid();
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "Audit", CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "Accounting", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var ready = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal(2, ready.Value!.Engagements.Count);
    Assert.Equal(2, ready.Value.Milestones.Count);
    Assert.Equal(25000m, decimal.Parse(ready.Value.Fee, CultureInfo.InvariantCulture));
    Assert.Equal(50m, decimal.Parse(ready.Value.AdvancePercent, CultureInfo.InvariantCulture));
    Assert.False(ready.Value.ReleaseRecorded);
    Assert.False((await FeeAgreementWorkspaceQuery.GetAsync(db, foreign.Prep, proposalId)).Succeeded);
    async Task<CommandResult> LinkAsync(Guid engagement)
    {
      await using var scoped = new AuditSphereDbContext(pg.Options);
      return await FeeAgreementService.LinkEngagementAsync(scoped, w.Prep, created.Value, engagement);
    }
    var links = await Task.WhenAll(LinkAsync(engagementA), LinkAsync(engagementB));
    Assert.Single(links, r => r.Succeeded);
    Assert.Single(links, r => !r.Succeeded);
    var winner = links[0].Succeeded ? engagementA : engagementB;
    var loser = links[0].Succeeded ? engagementB : engagementA;
    var linked = await FeeAgreementWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal<Guid?>(winner, linked.Value!.EngagementId);
    Assert.Empty(linked.Value.Engagements);
    await using (var fresh = new AuditSphereDbContext(pg.Options))
      Assert.Equal<Guid?>(winner, await fresh.EngagementFeeAgreements.AsNoTracking()
        .Where(x => x.Id == created.Value).Select(x => x.EngagementId).SingleAsync());

    // This original context still tracks the agreement from CreateAgreementAsync with a null
    // engagement link. The command must reload it under the firm fence before refusing the loser.
    var staleLoser = await FeeAgreementService.LinkEngagementAsync(db, w.Prep, created.Value, loser);
    Assert.False(staleLoser.Succeeded);
    Assert.Equal(ErrorCodes.ProtectedState, staleLoser.ErrorCode);
    var exactWinnerRetry = await FeeAgreementService.LinkEngagementAsync(db, w.Prep, created.Value, winner);
    Assert.True(exactWinnerRetry.Succeeded, exactWinnerRetry.Message);
    await using var verifyWinner = new AuditSphereDbContext(pg.Options);
    Assert.Equal<Guid?>(winner, await verifyWinner.EngagementFeeAgreements.AsNoTracking()
      .Where(x => x.Id == created.Value).Select(x => x.EngagementId).SingleAsync());
  }

  [Fact]
  public async Task AngularCommercialDocumentsShowBlockersAndRetainImmutableIdentities()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var initial = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(initial.Succeeded, initial.Message);
    Assert.NotEmpty(initial.Value!.CommonBlockers);
    Assert.NotEmpty(initial.Value.LetterBlockers);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    var profileRequest = new SaveCommercialProfileRequest("Synthetic Firm", "Address", "", "", "#0F766E", "Closing",
      "Reviewed history", "Reviewed credentials", "Reviewed methodology");
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, profileRequest)).Succeeded);
    var ready = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Empty(ready.Value!.CommonBlockers);
    Assert.Empty(ready.Value.TenderBlockers);
    Assert.NotEmpty(ready.Value.LetterBlockers);
    Assert.False((await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: Guid.NewGuid(), expectedProfileVersion: 1)).Succeeded);
    Assert.Empty(await db.CommercialDocuments.Where(x => x.ProposalId == proposalId).ToListAsync());
    var brief = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.True(brief.Succeeded, brief.Message);
    var replay = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.Equal(brief.Value!.Id, replay.Value!.Id);
    var tender = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Reviewed assigned team", "Reviewed delivery timeline", true,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.True(tender.Succeeded, tender.Message);
    var listed = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal(2, listed.Value!.Documents.Count);
    var artifact = Assert.Single(listed.Value.Documents, d => d.Id == brief.Value.Id);
    Assert.Equal(brief.Value.Sha256Hex, artifact.Sha256);
    Assert.Equal("1", artifact.ProfileVersion);
    Assert.False((await CommercialDocumentWorkspaceQuery.GetAsync(db, foreign.Prep, proposalId)).Succeeded);
    Assert.False((await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, profileRequest with { LegalName = "Synthetic Revised Firm" })).Succeeded);
    var stale = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    var revised = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal("2", revised.Value!.ProfileVersion);
    Assert.All(revised.Value.Documents, d => Assert.Equal("1", d.ProfileVersion));
  }

  [Theory]
  [InlineData("PROFILE")]
  [InlineData("LIST")]
  public async Task CommercialDocumentReads_DenySameEpochGrantRevokedAfterProjection(string readKind)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var setup = new AuditSphereDbContext(pg.Options);
    var quotation = await QuotationService.SaveAsync(setup, w.Prep, Quote(proposalId));
    Assert.True(quotation.Succeeded, quotation.Message);
    Assert.True((await QuotationService.SubmitAsync(setup, w.Prep, quotation.Value)).Succeeded);
    var profile = new SaveCommercialProfileRequest("Read-interleave synthetic firm", "Doha", "", "", "#0F766E", "Closing",
      "Reviewed history", "Reviewed credentials", "Reviewed methodology");
    Assert.True((await CommercialDocumentService.SaveProfileAsync(setup, w.Partner, profile)).Succeeded);
    var brief = await CommercialDocumentService.GenerateBriefQuotationAsync(setup, w.Prep, proposalId,
      expectedQuotationId: quotation.Value, expectedProfileVersion: 1);
    Assert.True(brief.Succeeded, brief.Message);
    Assert.NotEmpty(brief.Value!.Bytes);

    // Establish both protected projections while the exact unscoped grants are current.
    var profileControl = await CommercialDocumentService.GetProfileAsync(setup, w.Partner);
    Assert.True(profileControl.Succeeded, profileControl.Message);
    Assert.NotNull(profileControl.Value);
    var listControl = await CommercialDocumentService.ListAsync(setup, w.Prep, proposalId);
    Assert.True(listControl.Succeeded, listControl.Message);
    Assert.Contains(listControl.Value!, x => x.Id == brief.Value.Id && x.Bytes.Length > 0);

    var actor = readKind == "PROFILE" ? w.Partner : w.Prep;
    var role = readKind == "PROFILE" ? "Partner" : "RelationshipManager";
    var targetTable = readKind == "PROFILE" ? "firm_commercial_profiles" : "commercial_documents";
    var interleaver = new RevokeCommercialGrantAfterRead(pg, w.FirmId, actor.UserId, role, targetTable);
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interleaver).Options;

    await using (var read = new AuditSphereDbContext(options))
    {
      if (readKind == "PROFILE")
      {
        var result = await CommercialDocumentService.GetProfileAsync(read, actor);
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
        Assert.Null(result.Value);
      }
      else
      {
        var result = await CommercialDocumentService.ListAsync(read, actor, proposalId);
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
        Assert.Null(result.Value);
      }
    }

    Assert.Equal(1, interleaver.Calls);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(actor.SessionEpoch,
      await verify.Users.Where(x => x.FirmId == w.FirmId && x.Id == actor.UserId).Select(x => x.SessionEpoch).SingleAsync());
    Assert.Equal(0, await verify.RoleGrants.CountAsync(x => x.FirmId == w.FirmId && x.UserId == actor.UserId &&
      x.Role == role && x.ClientId == null && x.EngagementId == null && x.RevokedAt == null));
  }

  [Fact]
  public async Task AngularQuotationWorkspaceUsesReviewedRatesAndRevisionFences()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    var workspace = await QuotationWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.True(workspace.Value!.Editable);
    Assert.Empty(workspace.Value.Versions);
    var rate = workspace.Value.Rates.Single(r => r.Role == "Partner");
    var request = new SaveQuotationRequest(proposalId, [new("Partner", "Audit", 10m, rate.Id)],
      1m, 0m, 0m, false, null, ExpectedRevision: 0, ExpectedProposalRevision: 1);
    var preview = await QuotationWorkspaceQuery.PreviewAsync(db, w.Prep, request);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(10000m, decimal.Parse(preview.Value!.Fee, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Empty(await db.QuotationVersions.Where(x => x.ProposalId == proposalId).ToListAsync());
    var saved = await QuotationService.SaveAsync(db, w.Prep, request);
    Assert.True(saved.Succeeded, saved.Message);
    Assert.Equal(saved.Value, (await QuotationService.SaveAsync(db, w.Prep, request)).Value);
    Assert.False((await QuotationService.SaveAsync(db, w.Prep, request with { DiscountPercent = 1m })).Succeeded);
    Assert.False((await QuotationWorkspaceQuery.PreviewAsync(db, w.Prep, request)).Succeeded);
    Assert.False((await QuotationWorkspaceQuery.GetAsync(db, foreign.Prep, proposalId)).Succeeded);
    var current = await QuotationWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.Equal("1", Assert.Single(current.Value!.Versions).Revision);
    db.RateCardVersions.Add(new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Version = 2, Role = "Partner", Activity = "Audit", Currency = "QAR",
      RatePerHour = 1100m, Status = PracticeTimeStates.RateApproved, CreatedByUserId = w.Partner.UserId,
      ApprovedByUserId = w.Manager.UserId, CreatedAt = DateTimeOffset.UtcNow, ApprovedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var stale = await QuotationService.SaveAsync(db, w.Prep, request with { ExpectedRevision = 1 });
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.False((await QuotationWorkspaceQuery.PreviewAsync(db, w.Prep, request with { ExpectedRevision = 1 })).Succeeded);
    Assert.Single(await db.QuotationVersions.Where(x => x.ProposalId == proposalId).ToListAsync());
  }

  [Fact]
  public async Task Quotation_IsCalculatedFromApprovedRates_Versioned_AndApprovedByTheConfiguredMatrix()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    // A role without an approved rate card fails closed instead of pricing at zero.
    var noRate = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, lines: [new("Intern", "Audit", 10m)]));
    Assert.False(noRate.Succeeded);
    Assert.Contains("No approved rate", noRate.Message);

    // Rate cards + hours + complexity + risk premium: 25,000 × 1.2 = 30,000; + 10% = 33,000.
    var first = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m));
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal(first.Value, (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m))).Value); // idempotent
    var views = (await QuotationService.ListAsync(db, w.Prep, proposalId)).Value!;
    var v1 = Assert.Single(views);
    Assert.Equal((25000m, 33000m, 1L), (v1.Version.BaseAmount, v1.Version.Fee, v1.Version.Revision));
    Assert.All(v1.Lines, l => Assert.NotEqual(Guid.Empty, l.RateCardVersionId));
    Assert.Equal(33000m, (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).Fee);

    // No discount and standard terms need no approver: submitting approves; a proposal can then enter review.
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, first.Value)).Succeeded);
    Assert.Equal(QuotationStates.Approved, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == first.Value)).Status);

    // A 15% discount exceeds the unconfigured default (10%): a new revision needs a Partner. Prior version is preserved.
    var discounted = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m, discount: 15m));
    Assert.True(discounted.Succeeded);
    Assert.NotEqual(first.Value, discounted.Value);
    views = (await QuotationService.ListAsync(db, w.Prep, proposalId)).Value!;
    Assert.Equal([2L, 1L], views.Select(x => x.Version.Revision));
    Assert.Equal(QuotationStates.Superseded, views[1].Version.Status);
    Assert.Equal(33000m, views[1].Version.Fee); // history intact
    Assert.Equal(28050m, views[0].Version.Fee);
    var approval = Assert.Single(views[0].RequiredApprovals);
    Assert.Equal(("Partner", "DEFAULT:DISCOUNT"), (approval.Role, approval.RuleKey));

    // Proposal review is blocked until that quotation is approved.
    var early = await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, early.ErrorCode);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, discounted.Value)).Succeeded);
    var wrongRole = await QuotationService.ApproveAsync(db, w.Manager, discounted.Value, "DEFAULT:DISCOUNT", "Manager tries to approve");
    Assert.Equal(ErrorCodes.ScopeDenied, wrongRole.ErrorCode);
    var partnerOwn = await QuotationService.SaveAsync(db, w.Partner, Quote(proposalId, complexity: 1.2m, risk: 10m, discount: 20m));
    Assert.True((await QuotationService.SubmitAsync(db, w.Partner, partnerOwn.Value)).Succeeded);
    var self = await QuotationService.ApproveAsync(db, w.Partner, partnerOwn.Value, "DEFAULT:DISCOUNT", "Partner approves own version");
    Assert.Equal(ErrorCodes.ProtectedState, self.ErrorCode); // preparers never approve their own version
    // Back to the 15% version by the preparer, approved by a different Partner identity.
    var second = User(w.FirmId, "partner2");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(second);
      seed.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = second.Id, Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = second.Id });
      await seed.SaveChangesAsync();
    }
    var final = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m, discount: 15m));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, final.Value)).Succeeded);
    Assert.True((await QuotationService.ApproveAsync(db, Actor(second, "Partner"), final.Value, "DEFAULT:DISCOUNT", "Discount agreed for a multi-year relationship")).Succeeded);
    Assert.Equal(QuotationStates.Approved, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == final.Value)).Status);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);

    // Changing the matrix changes who must approve: a Manager band at 5% and a Partner band at 15%.
    Assert.False((await QuotationService.SaveRuleAsync(db, w.Prep, CommercialRuleKinds.DiscountOver, 5m, "Manager")).Succeeded);
    var proposal2 = (await DraftProposalSecondAsync(pg, w)).ProposalId;
    Assert.True((await QuotationService.SaveRuleAsync(db, w.Partner, CommercialRuleKinds.DiscountOver, 5m, "Manager")).Succeeded);
    var partnerBand = await QuotationService.SaveRuleAsync(db, w.Partner, CommercialRuleKinds.DiscountOver, 15m, "Partner");
    Assert.True(partnerBand.Succeeded);
    var banded = await QuotationService.SaveAsync(db, w.Prep, Quote(proposal2, discount: 8m));
    var bandedRequired = Assert.Single((await QuotationService.ListAsync(db, w.Prep, proposal2)).Value!.Single().RequiredApprovals);
    Assert.Equal("Manager", bandedRequired.Role);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, banded.Value)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await QuotationService.ApproveAsync(db, w.Partner, banded.Value, bandedRequired.RuleKey, "Insufficient: matrix names Manager")).ErrorCode);
    Assert.True((await QuotationService.ApproveAsync(db, w.Manager, banded.Value, bandedRequired.RuleKey, "Within the Manager band")).Succeeded);
    // Deactivating the top band and non-standard terms both route through the matrix.
    var terms = await QuotationService.SaveAsync(db, w.Prep, Quote(proposal2, discount: 8m, nonStandard: true, note: "Fees payable in three instalments"));
    Assert.Equal(2, (await QuotationService.ListAsync(db, w.Prep, proposal2)).Value![0].RequiredApprovals.Count);
    Assert.True((await QuotationService.DeactivateRuleAsync(db, w.Partner, partnerBand.Value)).Succeeded);
    Assert.NotEqual(Guid.Empty, terms.Value);

    // Database guards: an approved version's fee and hash cannot be rewritten, and history cannot be deleted.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE quotation_versions SET fee = 1 WHERE id = {final.Value} AND firm_id = {w.FirmId}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM quotation_approvals WHERE firm_id = {w.FirmId}"));
  }

  private static async Task<(Guid LeadId, Guid OpportunityId, Guid ProposalId)> DraftProposalSecondAsync(PgTestSchema pg, World w)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest("Second Client WLL", "Web Form", "B. Owner", "b@second.example.test"));
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, lead.Value)).Succeeded);
    var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(lead.Value, "AccountingOnly", "SECOND", "2026-01-01", "2026-12-31", 9000m, "QAR"));
    var proposal = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunity.Value, "ACC-2026", "Monthly accounting", "", "Management accounts", "", 1m, "QAR", "2026-01-01", "2026-12-31"));
    return (lead.Value, opportunity.Value, proposal.Value);
  }

  private static string DocxText(byte[] bytes)
  {
    using var archive = new ZipArchive(new MemoryStream(bytes));
    using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
    return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "<[^>]+>", " "));
  }

  [Fact]
  public async Task BriefQuotationRequiresTimelineForNewArtifactsButRecoversRetainedLegacyDocument()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w, " ");
    await using var db = new AuditSphereDbContext(pg.Options);
    var quotation = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True(quotation.Succeeded, quotation.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quotation.Value)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new("Synthetic Firm", "Doha", "", "", "#0F766E", "Reviewed terms"))).Succeeded);
    var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId);
    Assert.True(string.IsNullOrWhiteSpace(proposal.Timeline));
    var blocked = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
    Assert.Contains("timeline", blocked.Message, StringComparison.OrdinalIgnoreCase);
    var blockedPair = await CommercialDocumentService.GenerateProposalDocumentsAsync(db, w.Partner, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, blockedPair.ErrorCode);
    Assert.Empty(await db.CommercialDocuments.ToListAsync());
    var workspace = await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId);
    Assert.True(workspace.Succeeded, workspace.Message);
    Assert.Single(workspace.Value!.QuotationBlockers!);
    Assert.Empty(workspace.Value.CommonBlockers);

    // Model an immutable pre-policy artifact rather than editing proposal terms or bypassing the new generator.
    var profile = await db.FirmCommercialProfiles.AsNoTracking().SingleAsync();
    var bytes = CommercialDocumentRenderer.RenderDocx(new(profile, "Quotation", "LEGACY-R1",
      new DateOnly(2026, 10, 1), "Synthetic Client",
      [new DocumentSection("Scope", [proposal.Scope])], false));
    var retained = new CommercialDocument {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = proposalId,
      QuotationVersionId = quotation.Value, Kind = CommercialDocumentKinds.Quotation,
      TemplateVersion = CommercialDocumentRenderer.QuotationTemplate, ProfileVersion = profile.Version,
      FileName = "legacy-quotation.docx", ContentType = CommercialDocumentRenderer.DocxContentType,
      Bytes = bytes, Sha256Hex = Hashing.Sha256Hex(bytes), CreatedByUserId = w.Prep.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.CommercialDocuments.Add(retained); await db.SaveChangesAsync();
    var recovered = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.True(recovered.Succeeded, recovered.Message);
    Assert.Equal(retained.Id, recovered.Value!.Id);
    Assert.Equal(retained.Sha256Hex, recovered.Value.Sha256Hex);
    Assert.Equal(bytes, recovered.Value.Bytes);
    Assert.Single(await db.CommercialDocuments.ToListAsync());
    Assert.Empty((await CommercialDocumentWorkspaceQuery.GetAsync(db, w.Prep, proposalId)).Value!.QuotationBlockers!);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task OneClickDocuments_AreBrandedImmutableAndBoundToTheApprovedQuotation(bool revokeStandingAuthority)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    var privateStaffCostRateId = Guid.CreateVersion7();
    const decimal privateStaffCost = 1234.56m;
    db.StaffCostRates.Add(new StaffCostRate
    {
      Id = privateStaffCostRateId, FirmId = w.FirmId, UserId = w.Partner.UserId,
      HourlyCost = privateStaffCost, Currency = "QAR", EffectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow),
      RecordedByUserId = w.Manager.UserId, RecordedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.2m, risk: 10m));
    var blocked = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode); // not approved yet
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    var noProfile = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GateBlocked, noProfile.ErrorCode);
    Assert.Contains("commercial profile", noProfile.Message);

    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.SaveProfileAsync(db, w.Prep,
      new("Gulf Audit Partners", "Doha", "hello@gulf.example.test", "+974 0000", "#2B6CB0", ""))).ErrorCode);
    Assert.False((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, new("Gulf Audit Partners", "Doha", "", "", "blue", ""))).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new("Gulf Audit Partners", "West Bay, Doha", "hello@gulf.example.test", "+974 0000 0000", "#0F766E", "This letter is subject to our standard terms of business."))).Succeeded);

    var brief = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId);
    Assert.True(brief.Succeeded, brief.Message);
    Assert.Equal(ErrorCodes.GateBlocked, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId)).ErrorCode);
    Assert.Empty(await db.CommercialDocuments.Where(x => x.Kind == CommercialDocumentKinds.EngagementLetter).ToListAsync());
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalId);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    var clientId = (await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Gulf Trading LLC"))).Value;
    Assert.Equal(ErrorCodes.GateBlocked, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId)).ErrorCode);
    await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db);
    await db.SaveChangesAsync();
    var checklist = (await AcceptanceChecklistService.GetAsync(db, w.Manager, clientId)).Value!;
    Assert.NotEmpty(checklist.Items);
    foreach (var item in checklist.Items)
      Assert.True((await AcceptanceChecklistService.RecordAnswerAsync(db, w.Manager, clientId, item.Question.Code,
        item.Question.AdverseAnswer == "YES" ? "No" : "Yes", item.Question.RequiresEvidence ? $"DOC-{item.Question.Code}" : null)).Succeeded);
    var generation = (await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == clientId)).InputGeneration;
    var acceptance = await AcceptanceDecisionService.RecordAsync(db, w.Partner, new(clientId, null, "FinancialStatementAudit", "Accepted", "All risk and independence evidence reviewed.", null, generation));
    Assert.True(acceptance.Succeeded, acceptance.Message);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Prep, proposalId)).ErrorCode);
    Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, w.Partner, CompletionTestFixtures.Png(100, 60))).Succeeded);
    Assert.True((await AuditDeliverableService.RegisterFirmSealAsync(db, w.Partner, CompletionTestFixtures.Png(100, 100))).Succeeded);
    var generated = await CommercialDocumentService.GenerateProposalDocumentsAsync(db, w.Partner, proposalId);
    Assert.True(generated.Succeeded, generated.Message);
    var (q, letter) = (generated.Value!.Quotation, generated.Value.EngagementLetter);
    Assert.Equal((CommercialDocumentKinds.Quotation, CommercialDocumentKinds.EngagementLetter), (q.Kind, letter.Kind));
    // STE 4.1.4: the statutory route selects its own governed template, and the stored identity is the exact template used.
    Assert.Equal((CommercialDocumentRenderer.QuotationTemplate, EngagementLetterTemplates.Statutory), (q.TemplateVersion, letter.TemplateVersion));
    // STE 4.1.5: while automatic drafting is off, the advance invoice is explicitly pending, never presented as prepared.
    var pending = (await FeeAgreementWorkspaceQuery.GetAsync(db, w.Partner, proposalId)).Value!;
    Assert.Equal(AdvanceInvoicePreparationStates.PendingAutomationDisabled, pending.AdvancePreparation.State);
    Assert.Contains("Finance must prepare the draft manually", pending.AdvancePreparation.Message);
    var automated = (await FeeAgreementWorkspaceQuery.GetAsync(db, w.Partner, proposalId, default, automaticDraftingEnabled: true)).Value!;
    Assert.Equal(AdvanceInvoicePreparationStates.AwaitingAutomation, automated.AdvancePreparation.State);
    var engagement = await EngagementLifecycleService.CreateDraftAsync(db, w.Partner,
      new(clientId, "FinancialStatementAudit", "2026-01-01", "2026-12-31", "AUDIT-2026"));
    Assert.True(engagement.Succeeded, engagement.Message);
    var feeAgreementId = await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.ProposalId == proposalId)
      .Select(x => x.Id).SingleAsync();
    Assert.True((await FeeAgreementService.LinkEngagementAsync(db, w.Prep, feeAgreementId, engagement.Value)).Succeeded);
    var readinessDisabled = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagement.Value);
    Assert.True(readinessDisabled.Succeeded, readinessDisabled.Message);
    Assert.Equal(AdvanceInvoicePreparationStates.PendingAutomationDisabled, readinessDisabled.Value!.AdvancePayment.PreparationState);
    var readinessEnabled = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagement.Value,
      automaticDraftingEnabled: true);
    Assert.True(readinessEnabled.Succeeded, readinessEnabled.Message);
    Assert.Equal(AdvanceInvoicePreparationStates.AwaitingAutomation, readinessEnabled.Value!.AdvancePayment.PreparationState);
    Assert.All(new[] { q, letter }, d =>
    {
      Assert.Equal(Hashing.Sha256Hex(d.Bytes), d.Sha256Hex);
      Assert.Equal(1L, d.ProfileVersion);
      Assert.Equal(quote.Value, d.QuotationVersionId);
    });
    var quoteText = DocxText(q.Bytes);
    Assert.Contains("Gulf Audit Partners", quoteText);
    Assert.Contains("West Bay, Doha", quoteText);
    Assert.Contains("33,000.00", quoteText);              // total fee
    Assert.Contains("Complexity factor × 1.2", quoteText);
    Assert.Contains("16,500.00", quoteText);              // 50% advance stated in the payment terms
    Assert.Contains("Gulf Trading LLC (attention A. Owner)", quoteText);
    Assert.Contains("Statutory audit of the 2026 financial statements", quoteText);
    Assert.Contains("Period: 2026-01-01 to 2026-12-31", quoteText);
    Assert.DoesNotContain(privateStaffCost.ToString(CultureInfo.InvariantCulture), quoteText);
    Assert.DoesNotContain(privateStaffCost.ToString("N2", CultureInfo.InvariantCulture), quoteText);
    Assert.DoesNotContain(privateStaffCostRateId.ToString(), quoteText);
    Assert.DoesNotContain(privateStaffCostRateId.ToString("N"), quoteText);
    var letterText = DocxText(letter.Bytes);
    // The offered timeline is rendered into both artifacts, so it is inside the identity the client accepts.
    Assert.Contains("Fieldwork in March 2027; report by 30 April 2027", quoteText);
    Assert.Contains("Fieldwork in March 2027; report by 30 April 2027", letterText);
    Assert.Contains("Acceptance", letterText);
    Assert.Contains("standard terms of business", letterText);
    Assert.DoesNotContain("Acceptance", quoteText);

    // One click again returns the same documents, not new ones.
    var again = await CommercialDocumentService.GenerateProposalDocumentsAsync(db, w.Partner, proposalId);
    Assert.Equal((q.Id, letter.Id), (again.Value!.Quotation.Id, again.Value.EngagementLetter.Id));
    Assert.Equal(2, await db.CommercialDocuments.CountAsync(x => x.ProposalId == proposalId));

    Assert.Equal(acceptance.Value, letter.AcceptanceDecisionId);
    Assert.NotNull(letter.CommercialAcceptedAt);
    Assert.NotNull(letter.SignatureSpecimenId);
    Assert.NotNull(letter.FirmSealSpecimenId);
    // The gated letter creates the 50/50 agreement. Standing authority creates one DRAFT, never approval/posting.
    var admin = User(w.FirmId, "automation-administrator");
    db.Users.Add(admin);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = admin.Id, Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id });
    await db.SaveChangesAsync();
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(w.FirmId, "Test");
    var policy = new AutomaticFeeInvoicePolicy(true, w.FinanceManager.UserId, admin.Id);
    var handler = new AutomaticFeeInvoiceHandler(policy);
    var discovery = new AutomaticFeeInvoiceDiscovery(factory, store, handler, options, policy);
    Assert.Equal(0, await new AutomaticFeeInvoiceDiscovery(factory, store, handler, options, policy with { Enabled = false }).EnqueuePendingAsync(default));
    Assert.Equal(0, await new AutomaticFeeInvoiceDiscovery(factory, store, handler, options, policy with { ApprovingAdministratorId = w.Prep.UserId }).EnqueuePendingAsync(default));
    Assert.Equal(1, await discovery.EnqueuePendingAsync(default));
    Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
    if (revokeStandingAuthority)
      await db.Users.Where(x => x.Id == admin.Id).ExecuteUpdateAsync(x => x.SetProperty(v => v.SessionEpoch, v => v.SessionEpoch + 1));
    var dispatcher = new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options);
    Assert.True(await dispatcher.ProcessNextAsync());
    Assert.False(await dispatcher.ProcessNextAsync());
    var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == AutomaticFeeInvoiceHandler.Kind);
    Assert.Equal(revokeStandingAuthority ? OperationState.AUTHORIZATION_BLOCKED : OperationState.COMPLETED, operation.Status);
    if (revokeStandingAuthority) Assert.Empty(await db.Invoices.AsNoTracking().ToListAsync());
    else
    {
      var invoice = Assert.Single(await db.Invoices.AsNoTracking().ToListAsync());
      Assert.Equal(BillingStates.InvoiceDraft, invoice.Status);
      Assert.Null(invoice.PostedAt);
      Assert.Equal(16500m, invoice.Total);
      var agreement = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.ProposalId == proposalId);
      Assert.Equal(invoice.Id, (await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.FinanceManager, agreement.Id)).Value);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.Prep, agreement.Id)).ErrorCode);
      Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
      Assert.Equal(1, await db.OperationEvents.CountAsync(x => x.OperationId == operation.Id && x.Kind == "fee.invoice-draft.created.v1"));
      var clientUser = User(w.FirmId, "client-portal-owner"); clientUser.UserKind = "Client";
      db.Users.Add(clientUser);
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientUser.Id, Role = "ClientUser", ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = admin.Id });
      await db.SaveChangesAsync();
      var clientActor = Actor(clientUser, "ClientUser");
      Assert.Equal(ErrorCodes.GateBlocked, (await AuthorizationDecision.AuthorizeAsync(db, clientActor, new(w.FirmId, clientId, RequiredRoles: ["ClientUser"]))).ErrorCode);
      // STE 4.1.5 / C-02: a linked agreement whose 50% advance is not yet paid can no longer activate the engagement.
      Assert.Equal(ErrorCodes.GateBlocked, (await EngagementLifecycleService.ActivateAsync(db, w.Partner, engagement.Value)).ErrorCode);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuthorizationDecision.AuthorizeAsync(db, clientActor, new(w.FirmId, clientId, engagement.Value, ["ClientUser"]))).ErrorCode);
      Assert.Empty(await ClientPortalService.AuthorizedPortalGrantIdsAsync(db, clientActor));
      Assert.True((await BillingService.SubmitInvoiceAsync(db, w.FinanceManager, invoice.Id)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, w.FinanceReviewer, invoice.Id)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile { Id = Guid.NewGuid(), FirmId = w.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = w.FinanceReviewer.UserId, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, w.FinanceManager, invoice.Id)).Succeeded);
      var paid = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreement.Id, 16500m, "PORTAL-TEST-ADVANCE");
      Assert.True(paid.Succeeded, paid.Message);
      var paidReadiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagement.Value,
        automaticDraftingEnabled: true);
      Assert.True(paidReadiness.Succeeded, paidReadiness.Message);
      Assert.Equal(OnboardingReadinessStatuses.Paid, paidReadiness.Value!.AdvancePayment.Status);
      Assert.Equal(OnboardingReadinessStatuses.ReadyForReview, paidReadiness.Value.Activation.Status);
      // Readiness is a fact every commercial role sees, but activation is the Partner's action: the Relationship Manager is
      // not offered it, and the Partner who activates on the next line is.
      Assert.False(paidReadiness.Value.CanActivate);
      var partnerReadiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Partner, engagement.Value,
        automaticDraftingEnabled: true);
      Assert.True(partnerReadiness.Succeeded, partnerReadiness.Message);
      Assert.Equal(OnboardingReadinessStatuses.ReadyForReview, partnerReadiness.Value!.Activation.Status);
      Assert.True(partnerReadiness.Value.CanActivate);
      // The recorded advance now satisfies the activation gate.
      Assert.True((await EngagementLifecycleService.ActivateAsync(db, w.Partner, engagement.Value)).Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.Prep, agreement.Id, 16500m, "PORTAL-TEST-ADVANCE")).ErrorCode);
      Assert.Equal(ErrorCodes.GenerationStale, (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager with { SessionEpoch = w.FinanceManager.SessionEpoch + 1 }, agreement.Id, 16500m, "PORTAL-TEST-ADVANCE")).ErrorCode);
      Assert.True((await AuthorizationDecision.AuthorizeAsync(db, clientActor, new(w.FirmId, clientId, engagement.Value, ["ClientUser"]))).Succeeded);
      Assert.Single(await ClientPortalService.AuthorizedPortalGrantIdsAsync(db, clientActor));
      Assert.Equal(ClientPortalIntentStates.ReadyToInvite, (await db.ClientPortalIntents.AsNoTracking().SingleAsync(x => x.PracticeClientId == clientId)).State);

    }
    Assert.Equal(ErrorCodes.ProtectedState, (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId, complexity: 1.3m, risk: 10m))).ErrorCode);
    await db.ClientSafetyStates.Where(x => x.Id == clientId).ExecuteUpdateAsync(x => x.SetProperty(v => v.InputGeneration, v => v.InputGeneration + 1));
    Assert.Equal(ErrorCodes.GenerationStale, (await CommercialDocumentService.GenerateEngagementLetterAsync(db, w.Partner, proposalId)).ErrorCode);
    // The paid case also retains its immutable settlement receipt and the voucher for its single payment.
    Assert.Equal(revokeStandingAuthority ? 2 : 4, (await CommercialDocumentService.ListAsync(db, w.Prep, proposalId)).Value!.Count);

    // Generated documents are append-only, and a stranger from another firm cannot read them.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE commercial_documents SET file_name = 'x.docx' WHERE id = {q.Id} AND firm_id = {w.FirmId}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM commercial_documents WHERE id = {q.Id} AND firm_id = {w.FirmId}"));
    var outsider = User(Guid.NewGuid(), "outsider");
    Assert.False((await CommercialDocumentService.GetAsync(db, Actor(outsider, "Partner"), q.Id)).Succeeded);
    Assert.True((await CommercialDocumentService.GetAsync(db, w.Prep, q.Id)).Succeeded);
  }

  [Fact]
  public async Task ComprehensiveTender_RequiresReviewedChapters_AndRetainsExactlyOneImmutableVersionPerPrice()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.False((await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Actual team CV", "Draft February", true)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, new("Gulf Audit Partners", "Doha", "", "", "#0F766E", "",
      "Firm history and approved CR registration", "Reviewed retail portfolio", "Reviewed ISA methodology"))).Succeeded);
    Assert.False((await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, "Actual team CV", "Draft February", false)).Succeeded);
    const string teamCvs = "Partner A and actual assigned team CVs";
    const string timeline = "Draft February; final March";
    var result = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId, teamCvs, timeline, true,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.True(result.Succeeded, result.Message);
    var document = result.Value!;
    Assert.Equal(CommercialDocumentKinds.ComprehensiveProposal, document.Kind);
    Assert.Equal(Hashing.Sha256Hex(document.Bytes), document.Sha256Hex);
    var originalBytes = document.Bytes.ToArray();
    var originalHash = document.Sha256Hex;
    async Task AssertOriginalTenderUnchangedAsync()
    {
      var retained = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
        x.ProposalId == proposalId && x.QuotationVersionId == quote.Value && x.Kind == CommercialDocumentKinds.ComprehensiveProposal);
      Assert.Equal(document.Id, retained.Id);
      Assert.Equal(1, retained.ProfileVersion);
      Assert.Equal(originalHash, retained.Sha256Hex);
      Assert.Equal(originalBytes, retained.Bytes);
    }
    var text = DocxText(document.Bytes);
    Assert.Contains("1. Firm profile", text); Assert.Contains("2. Assigned Engagement Partner", text);
    Assert.Contains("3. Industry credentials", text); Assert.Contains("4. Audit methodology", text);
    Assert.Contains("5. Fee schedule", text); Assert.Contains("Partner A", text); Assert.Contains("final March", text);

    var trimmedRetry = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId,
      $"  {teamCvs}  ", $"  {timeline}  ", true,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.True(trimmedRetry.Succeeded, trimmedRetry.Message);
    Assert.Equal(document.Id, trimmedRetry.Value!.Id);
    Assert.Equal(originalHash, trimmedRetry.Value.Sha256Hex);
    Assert.Equal(originalBytes, trimmedRetry.Value.Bytes);

    var changedCvs = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId,
      "Partner B and changed assigned team CVs", timeline, true,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.Equal(ErrorCodes.StaleRevision, changedCvs.ErrorCode);
    Assert.Null(changedCvs.Value);
    await AssertOriginalTenderUnchangedAsync();
    var changedTimeline = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId,
      teamCvs, "Changed fieldwork and reporting dates", true,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1);
    Assert.Equal(ErrorCodes.StaleRevision, changedTimeline.ErrorCode);
    Assert.Null(changedTimeline.Value);
    await AssertOriginalTenderUnchangedAsync();

    var revisedProfile = await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new("Gulf Audit Partners Revised", "Doha", "", "", "#0F766E", "",
        "Firm history and approved CR registration revised", "Reviewed retail portfolio revised", "Reviewed ISA methodology revised",
        ExpectedVersion: 1));
    Assert.True(revisedProfile.Succeeded, revisedProfile.Message);
    Assert.Equal(2, await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == w.FirmId)
      .OrderByDescending(x => x.Version).Select(x => x.Version).FirstAsync());
    var changedProfile = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, proposalId,
      teamCvs, timeline, true, expectedQuotationId: quote.Value, expectedProfileVersion: 2);
    Assert.Equal(ErrorCodes.StaleRevision, changedProfile.ErrorCode);
    Assert.Null(changedProfile.Value);
    await AssertOriginalTenderUnchangedAsync();

    var retained = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
      x.ProposalId == proposalId && x.QuotationVersionId == quote.Value && x.Kind == CommercialDocumentKinds.ComprehensiveProposal);
    Assert.Equal(document.Id, retained.Id);
    Assert.Equal(1, retained.ProfileVersion);
    Assert.Equal(originalHash, retained.Sha256Hex);
    Assert.Equal(originalBytes, retained.Bytes);
    Assert.Single(await db.CommercialDocuments.AsNoTracking().Where(x =>
      x.ProposalId == proposalId && x.QuotationVersionId == quote.Value &&
      x.Kind == CommercialDocumentKinds.ComprehensiveProposal).ToListAsync());
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE commercial_documents SET file_name = 'changed' WHERE id = {document.Id}"));

    var (_, _, legacyProposalId) = await DraftProposalSecondAsync(pg, w);
    var legacyQuote = await QuotationService.SaveAsync(db, w.Prep, Quote(legacyProposalId));
    Assert.True(legacyQuote.Succeeded, legacyQuote.Message);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, legacyQuote.Value)).Succeeded);
    var currentProfile = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == w.FirmId)
      .OrderByDescending(x => x.Version).FirstAsync();
    var legacyBytes = CommercialDocumentRenderer.RenderDocx(new(currentProfile, "Comprehensive proposal",
      $"{legacyProposalId:N}-R1", DateOnly.FromDateTime(DateTime.UtcNow), "Second Client WLL",
      [new DocumentSection("Legacy rendered chapter", ["Retained source document without tender input identity."])], false));
    var legacyDocument = new CommercialDocument
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = legacyProposalId,
      QuotationVersionId = legacyQuote.Value, Kind = CommercialDocumentKinds.ComprehensiveProposal,
      TemplateVersion = "COMMERCIAL-TENDER-v1", ProfileVersion = currentProfile.Version,
      FileName = $"ComprehensiveProposal-{legacyProposalId:N}-R1.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = legacyBytes,
      Sha256Hex = Hashing.Sha256Hex(legacyBytes), CreatedByUserId = w.Prep.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.CommercialDocuments.Add(legacyDocument);
    await db.SaveChangesAsync();

    var legacyReuse = await CommercialDocumentService.GenerateTenderAsync(db, w.Prep, legacyProposalId,
      teamCvs, timeline, true, expectedQuotationId: legacyQuote.Value, expectedProfileVersion: currentProfile.Version);
    Assert.Equal(ErrorCodes.StaleRevision, legacyReuse.ErrorCode);
    Assert.Null(legacyReuse.Value);
    var legacyRead = await CommercialDocumentService.GetAsync(db, w.Prep, legacyDocument.Id);
    Assert.True(legacyRead.Succeeded, legacyRead.Message);
    Assert.Equal(legacyBytes, legacyRead.Value!.Bytes);
    Assert.Equal(legacyDocument.Sha256Hex, legacyRead.Value.Sha256Hex);
    Assert.Single(await db.CommercialDocuments.AsNoTracking().Where(x =>
      x.ProposalId == legacyProposalId && x.QuotationVersionId == legacyQuote.Value &&
      x.Kind == CommercialDocumentKinds.ComprehensiveProposal).ToListAsync());
  }

  private sealed class RecordingMailSender : IPbcMailSender
  {
    public List<PbcMailPlan> Plans { get; } = [];
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct) { Plans.Add(plan); return Task.CompletedTask; }
  }

  /// <summary>Creates the pre-snapshot shape written by older payment-notification code.</summary>
  private sealed class LegacyPaymentRecipientSnapshotInterceptor : SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData,
      InterceptionResult<int> result,
      CancellationToken cancellationToken = default)
    {
      if (eventData.Context is { } context)
      {
        foreach (var entry in context.ChangeTracker.Entries<CommercialNotification>()
          .Where(entry => entry.State == EntityState.Added &&
            (entry.Entity.Kind == CommercialNotificationKinds.Receipt ||
             entry.Entity.Kind == CommercialNotificationKinds.PaymentVoucher)))
        {
          entry.Entity.RecipientContactId = null;
          entry.Entity.RecipientRoutingId = null;
          entry.Entity.RecipientAuthorityBasis = null;
        }
      }

      return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
  }

  [Theory]
  [InlineData(false, false)] // A different Finance mailbox blocks new identity snapshots.
  [InlineData(true, false)]  // The same mailbox with a different authority identity still blocks.
  [InlineData(false, true)]  // Legacy all-null snapshots keep same-mailbox compatibility.
  public async Task AgreedFee_AdvanceReceiptEmailAndBalance_FollowTheFiftyFiftyCycleOnceEach(
    bool replacementUsesSameMailbox, bool legacySnapshot)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    var dbOptions = legacySnapshot
      ? new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
        .AddInterceptors(new LegacyPaymentRecipientSnapshotInterceptor()).Options
      : pg.Options;
    await using var db = new AuditSphereDbContext(dbOptions);

    var quote = await QuotationService.SaveAsync(db, w.Prep,
      Quote(proposalId, lines: [new("Partner", "Audit", 8m), new("Manager", "Audit", 16m)])); // QAR 20,000 → 10,000 + 10,000
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).ErrorCode); // not accepted yet
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalId);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var prospectMailBeforeConversion = await db.CommercialNotifications.AsNoTracking()
      .SingleAsync(x => x.Kind == CommercialNotificationKinds.Proposal && x.ProposalId == proposalId);
    Assert.Equal("owner@gulf.example.test", prospectMailBeforeConversion.Recipient);
    Assert.Equal(ProposalRecipientAuthorityBases.ProspectPrimaryContact, prospectMailBeforeConversion.RecipientAuthorityBasis);
    Assert.Null(prospectMailBeforeConversion.RecipientContactId);
    Assert.Null(prospectMailBeforeConversion.RecipientRoutingId);
    var frozenProspectMail = (prospectMailBeforeConversion.Recipient, prospectMailBeforeConversion.RecipientContactId,
      prospectMailBeforeConversion.RecipientRoutingId, prospectMailBeforeConversion.RecipientAuthorityBasis);
    // Deliver while the frozen prospect contact is still authoritative. Conversion changes the
    // recipient boundary; it must not authorize a delayed prospect notification retroactively.
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var sender = new RecordingMailSender();
    var handler = new CommercialMailDeliveryHandler(factory, sender);
    var options = new WorkerOptions(w.FirmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");
    var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new CommercialMailDiscovery(factory, store, handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.Single(sender.Plans);
    Assert.Contains("Proposal revision", sender.Plans[0].Subject);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    var clientId = (await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Gulf Trading LLC"))).Value;
    var prospectMailAfterConversion = await db.CommercialNotifications.AsNoTracking()
      .SingleAsync(x => x.Id == prospectMailBeforeConversion.Id);
    Assert.Equal(frozenProspectMail, (prospectMailAfterConversion.Recipient, prospectMailAfterConversion.RecipientContactId,
      prospectMailAfterConversion.RecipientRoutingId, prospectMailAfterConversion.RecipientAuthorityBasis));
    var primaryContactId = await db.ClientContacts.AsNoTracking()
      .Where(x => x.FirmId == w.FirmId && x.PracticeClientId == clientId && x.Primary)
      .Select(x => x.Id).SingleAsync();

    var agreementId = (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).Value;
    Assert.Equal(agreementId, (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).Value);
    var view = (await FeeAgreementService.GetForProposalAsync(db, w.Prep, proposalId)).Value!;
    Assert.Equal((20000m, 50m), (view.Agreement.AgreedFee, view.Agreement.AdvancePercent));
    Assert.Equal([10000m, 10000m], view.Milestones.Select(x => x.Milestone.Amount));
    Assert.Equal(view.Agreement.AgreedFee, view.Milestones.Sum(x => x.Milestone.Amount));

    // The balance cannot be invoiced before the advance is paid, nor without a delivered final report.
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).ErrorCode);

    // Invoicing needs a finance identity; the invoice then follows the normal review and posting path.
    Assert.False((await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.Prep, agreementId)).Succeeded);
    var advanceInvoice = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.FinanceManager, agreementId);
    Assert.True(advanceInvoice.Succeeded, advanceInvoice.Message);
    Assert.Equal(advanceInvoice.Value, (await FeeAgreementService.IssueAdvanceInvoiceAsync(db, w.FinanceManager, agreementId)).Value);
    Assert.Equal(10000m, (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == advanceInvoice.Value)).Total);

    var clientUser = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Subject = "client-finance-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-commercial", Email = "client.portal@gulf.example.test", DisplayName = "Client Portal User",
      UserKind = "Client", CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(clientUser);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientUser.Id, Role = "ClientUser",
      ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
    });
    await db.SaveChangesAsync();
    var clientActor = new ActorContext(clientUser.Id, w.FirmId, clientUser.SessionEpoch, ["ClientUser"]);
    var draftPortalFinance = await ClientPortalFinanceQuery.GetAsync(db, clientActor);
    Assert.True(draftPortalFinance.Succeeded, draftPortalFinance.Message);
    var draftPortalInvoice = Assert.Single(Assert.Single(draftPortalFinance.Value!.Agreements).Invoices);
    Assert.Equal(BillingStates.InvoiceDraft, draftPortalInvoice.Status);
    Assert.Equal("NOT_POSTED", draftPortalInvoice.CollectionStatus);
    Assert.Equal("0", draftPortalInvoice.OutstandingAmount);
    Assert.Equal("NOT_APPLICABLE", draftPortalInvoice.AgingStatus);
    Assert.Null(draftPortalInvoice.PostedAt);
    Assert.Null(draftPortalInvoice.SentAt);

    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 10000m, "TT-1")).ErrorCode); // draft invoice
    Assert.True((await BillingService.SubmitInvoiceAsync(db, w.FinanceManager, advanceInvoice.Value)).Succeeded);
    Assert.True((await BillingService.ApproveInvoiceAsync(db, w.FinanceReviewer, advanceInvoice.Value)).Succeeded);
    db.FirmFinanceProfiles.Add(new FirmFinanceProfile
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
      ApprovedByUserId = w.FinanceReviewer.UserId, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    Assert.True((await BillingService.PostInvoiceAsync(db, w.FinanceManager, advanceInvoice.Value)).Succeeded);

    // Gate same-source callers before either can observe the payment source. Both payment
    // kinds use the same firm-fence and recovery assertions.
    async Task<(CommandResult<AdvancePaymentOutcome> Manager, CommandResult<AdvancePaymentOutcome> Reviewer)> RunConcurrentPaymentAsync(
      Func<AuditSphereDbContext, ActorContext, decimal, string, CancellationToken, Task<CommandResult<AdvancePaymentOutcome>>> recordPayment,
      decimal amount, string reference)
    {
      var bothPaymentCallersBlocked = false;
      await using (var paymentGate = new AuditSphereDbContext(pg.Options))
      await using (var managerPaymentDb = new AuditSphereDbContext(dbOptions))
      await using (var reviewerPaymentDb = new AuditSphereDbContext(dbOptions))
      await using (var paymentObserver = new AuditSphereDbContext(pg.Options))
      {
        using var paymentTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await paymentGate.Database.OpenConnectionAsync(paymentTimeout.Token);
        await managerPaymentDb.Database.OpenConnectionAsync(paymentTimeout.Token);
        await reviewerPaymentDb.Database.OpenConnectionAsync(paymentTimeout.Token);
        await paymentObserver.Database.OpenConnectionAsync(paymentTimeout.Token);
        var managerPid = ((NpgsqlConnection)managerPaymentDb.Database.GetDbConnection()).ProcessID;
        var reviewerPid = ((NpgsqlConnection)reviewerPaymentDb.Database.GetDbConnection()).ProcessID;
        await using var firmFence = await paymentGate.Database.BeginTransactionAsync(paymentTimeout.Token);
        Task<CommandResult<AdvancePaymentOutcome>>? managerTask = null;
        Task<CommandResult<AdvancePaymentOutcome>>? reviewerTask = null;
        try
        {
          await paymentGate.FirmSafetyStates.FromSqlInterpolated(
            $"SELECT * FROM firm_safety_states WHERE id = {w.FirmId} FOR UPDATE").AsNoTracking().SingleAsync(paymentTimeout.Token);
          managerTask = recordPayment(managerPaymentDb, w.FinanceManager, amount, reference, paymentTimeout.Token);
          reviewerTask = recordPayment(reviewerPaymentDb, w.FinanceReviewer, amount, reference, paymentTimeout.Token);

          var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
          while (DateTimeOffset.UtcNow < deadline)
          {
            await paymentObserver.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", paymentTimeout.Token);
            bothPaymentCallersBlocked = await paymentObserver.Database.SqlQuery<bool>($"""
              SELECT EXISTS (
                SELECT 1 FROM pg_stat_activity AS waiting
                WHERE waiting.datname = current_database()
                  AND waiting.pid = {managerPid}
                  AND waiting.wait_event_type = 'Lock'
                  AND waiting.query ILIKE '%firm_safety_states%'
                  AND cardinality(pg_blocking_pids(waiting.pid)) > 0
              ) AND EXISTS (
                SELECT 1 FROM pg_stat_activity AS waiting
                WHERE waiting.datname = current_database()
                  AND waiting.pid = {reviewerPid}
                  AND waiting.wait_event_type = 'Lock'
                  AND waiting.query ILIKE '%firm_safety_states%'
                  AND cardinality(pg_blocking_pids(waiting.pid)) > 0
              ) AS "Value"
              """).SingleAsync(paymentTimeout.Token);
            if (bothPaymentCallersBlocked) break;
            await Task.Delay(TimeSpan.FromMilliseconds(25), paymentTimeout.Token);
          }
        }
        finally
        {
          try
          {
            if (paymentGate.Database.CurrentTransaction is not null)
              await firmFence.RollbackAsync(CancellationToken.None);
          }
          finally
          {
            var tasks = new Task?[] { managerTask, reviewerTask }.Where(x => x is not null).Cast<Task>().ToArray();
            if (tasks.Length > 0)
            {
              var allCalls = Task.WhenAll(tasks);
              try { await allCalls.WaitAsync(TimeSpan.FromSeconds(10)); }
              catch (TimeoutException)
              {
                paymentTimeout.Cancel();
                try { await allCalls.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch { _ = allCalls.Exception; }
              }
              catch { _ = allCalls.Exception; }
            }
          }
        }

        Assert.True(bothPaymentCallersBlocked,
          "Both payment callers must be observed waiting on the firm lock held by the test gate.");
        return (await managerTask!.WaitAsync(TimeSpan.FromSeconds(5)), await reviewerTask!.WaitAsync(TimeSpan.FromSeconds(5)));
      }
    }

    var (partial, reviewerPartial) = await RunConcurrentPaymentAsync(
      (paymentDb, actor, paymentAmount, reference, ct) => FeeAgreementService.RecordAdvancePaymentAsync(
        paymentDb, actor, agreementId, paymentAmount, reference, ct: ct), 4000m, "TT-1");

    Assert.True(partial.Succeeded, partial.Message);
    Assert.True(reviewerPartial.Succeeded, reviewerPartial.Message);
    Assert.Equal((false, 6000m, null), (partial.Value!.MilestonePaid, partial.Value.Outstanding, partial.Value.ReceiptDocumentId));
    Assert.Equal((false, 6000m, null), (reviewerPartial.Value!.MilestonePaid, reviewerPartial.Value.Outstanding,
      reviewerPartial.Value.ReceiptDocumentId));
    Assert.Equal(partial.Value.ReceiptId, reviewerPartial.Value.ReceiptId);
    Assert.False(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, w.FirmId, agreementId));
    Assert.Equal(1, await db.Receipts.CountAsync(x => x.FirmId == w.FirmId));
    Assert.Equal(1, await db.ReceiptAllocations.CountAsync(x =>
      x.FirmId == w.FirmId && x.InvoiceId == advanceInvoice.Value));
    var partialAllocation = await db.ReceiptAllocations.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.ReceiptId == partial.Value.ReceiptId && x.InvoiceId == advanceInvoice.Value);
    Assert.Equal(4000m, partialAllocation.Amount);
    Assert.Equal(4000m, await BillingService.GetNetAppliedToInvoiceAsync(db, w.FirmId, advanceInvoice.Value));
    var repeated = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 4000m, "TT-1");
    Assert.Equal(partial.Value.ReceiptId, repeated.Value!.ReceiptId);
    Assert.Equal(1, await db.Receipts.CountAsync(x => x.FirmId == w.FirmId));
    Assert.Equal("fee.reference-conflict", (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 3999m, "TT-1")).ErrorCode);
    Assert.Equal(1, await db.Receipts.CountAsync(x => x.FirmId == w.FirmId));
    Assert.Equal("fee.over-payment", (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 7000m, "TT-2")).ErrorCode);
    Assert.Equal(0, await db.CommercialDocuments.CountAsync(x => x.Kind == CommercialDocumentKinds.PaymentReceipt));

    // Full payment: paid once, one official receipt document, one queued email.
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner, new("Gulf Audit Partners", "Doha", "hello@gulf.example.test", "", "#2B6CB0", ""))).Succeeded);
    var paid = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 6000m, "TT-2");
    Assert.True(paid.Succeeded, paid.Message);
    Assert.True(paid.Value!.MilestonePaid && paid.Value.EmailQueued);
    Assert.True(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, w.FirmId, agreementId));
    Assert.NotNull(paid.Value.ReceiptDocumentId);
    var again = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 6000m, "TT-2");
    Assert.Equal(paid.Value.ReceiptDocumentId, again.Value!.ReceiptDocumentId);
    var receiptDoc = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Kind == CommercialDocumentKinds.PaymentReceipt);
    var advanceMilestoneId = view.Milestones.Single(x => x.Milestone.Kind == FeeMilestoneKinds.Advance).Milestone.Id;
    var advanceReference = $"RCT-{advanceMilestoneId:N}".ToUpperInvariant();
    Assert.Equal($"Receipt-{advanceReference}.docx", receiptDoc.FileName);
    var receiptText = DocxText(receiptDoc.Bytes);
    Assert.Contains(advanceReference, receiptText);
    Assert.Contains("10,000.00 QAR", receiptText);
    Assert.Contains("Balance due on delivery of the final report", receiptText);
    var notification = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Kind == CommercialNotificationKinds.Receipt);
    Assert.Equal("owner@gulf.example.test", notification.Recipient);
    Assert.Equal(FeeMilestoneStates.Paid, (await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Kind == FeeMilestoneKinds.Advance)).State);
    // The settlement receipt lists each constituent payment instead of presenting the last one as the whole advance.
    Assert.Contains("TT-1", receiptText);
    Assert.Contains("TT-2", receiptText);
    Assert.Contains("4,000.00 QAR", receiptText);
    Assert.Contains("6,000.00 QAR", receiptText);

    // Each actual payment has its own voucher (F-09). The first payment was recorded before the profile existed; recording the same
    // reference again issues its voucher without a second receipt, allocation or email for the payment.
    Assert.NotNull(paid.Value.VoucherDocumentId);
    Assert.Equal(paid.Value.VoucherDocumentId, again.Value.VoucherDocumentId);
    var recovered = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 4000m, "TT-1");
    Assert.True(recovered.Succeeded, recovered.Message);
    Assert.NotNull(recovered.Value!.VoucherDocumentId);
    Assert.Equal(recovered.Value.VoucherDocumentId,
      (await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 4000m, "TT-1")).Value!.VoucherDocumentId);
    Assert.Equal(2, await db.Receipts.CountAsync(x => x.FirmId == w.FirmId));
    var vouchers = await db.CommercialDocuments.AsNoTracking().Where(x => x.Kind == CommercialDocumentKinds.PaymentVoucher).ToListAsync();
    Assert.Equal(2, vouchers.Count);
    foreach (var voucher in vouchers)
    {
      var reference = $"PV-{voucher.ReceiptId!.Value:N}".ToUpperInvariant();
      Assert.Equal($"Voucher-{reference}.docx", voucher.FileName);
      Assert.Contains(reference, DocxText(voucher.Bytes));
    }
    var voucherReceipts = await db.Receipts.AsNoTracking().Where(x => vouchers.Select(v => v.ReceiptId).Contains(x.Id)).ToListAsync();
    Assert.Equal(10000m, voucherReceipts.Sum(x => x.Amount));
    var firstVoucher = DocxText(vouchers.Single(x => x.ReceiptId == partial.Value.ReceiptId).Bytes);
    var secondVoucher = DocxText(vouchers.Single(x => x.ReceiptId == paid.Value.ReceiptId).Bytes);
    Assert.Contains("4,000.00 QAR", firstVoucher);
    Assert.Contains("TT-1", firstVoucher);
    Assert.DoesNotContain("10,000.00 QAR from", firstVoucher);
    Assert.Contains("6,000.00 QAR", secondVoucher);
    Assert.Contains("TT-2", secondVoucher);
    // The first voucher states the balance as it stood after the first payment, even though it was issued after the second.
    Assert.Contains("Remaining on this invoice after this payment", firstVoucher);
    Assert.Contains("6,000.00 QAR", firstVoucher);
    Assert.Equal(2, await db.CommercialNotifications.CountAsync(x => x.Kind == CommercialNotificationKinds.PaymentVoucher));

    // The advance receipt and two payment vouchers were queued before any Finance route existed.
    // New snapshots retain the exact client-primary authority; the interceptor above models only
    // the older all-null snapshot shape and never touches a persisted notification.
    var advanceMail = await db.CommercialNotifications.AsNoTracking()
      .Where(x => x.PracticeClientId == clientId &&
        (x.Kind == CommercialNotificationKinds.Receipt || x.Kind == CommercialNotificationKinds.PaymentVoucher))
      .OrderBy(x => x.Kind).ThenBy(x => x.CreatedAt).ToListAsync();
    Assert.Equal(3, advanceMail.Count);
    Assert.Single(advanceMail, x => x.Kind == CommercialNotificationKinds.Receipt);
    Assert.Equal(2, advanceMail.Count(x => x.Kind == CommercialNotificationKinds.PaymentVoucher));
    Assert.All(advanceMail, mail => Assert.Equal("owner@gulf.example.test", mail.Recipient));
    foreach (var voucherMail in advanceMail.Where(x => x.Kind == CommercialNotificationKinds.PaymentVoucher))
    {
      Assert.Contains("attached to this email", voucherMail.Body, StringComparison.Ordinal);
      Assert.DoesNotContain("/api/commercial/documents/", voucherMail.Body, StringComparison.Ordinal);
    }
    if (legacySnapshot)
    {
      Assert.All(advanceMail, mail =>
      {
        Assert.Null(mail.RecipientContactId);
        Assert.Null(mail.RecipientRoutingId);
        Assert.Null(mail.RecipientAuthorityBasis);
      });
    }
    else
    {
      Assert.All(advanceMail, mail =>
      {
        Assert.Equal(primaryContactId, mail.RecipientContactId);
        Assert.Null(mail.RecipientRoutingId);
        Assert.Equal(PaymentRecipientAuthorityBases.ClientPrimaryContact, mail.RecipientAuthorityBasis);
      });
    }
    var frozenMail = advanceMail.ToDictionary(x => x.Id,
      x => (x.Recipient, x.DocumentId, x.OfferSha256, x.Subject, x.Body,
        x.RecipientContactId, x.RecipientRoutingId, x.RecipientAuthorityBasis));

    var replacementFinanceContact = new ClientContact
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, PracticeClientId = clientId,
      FullName = "Replacement Finance Contact",
      Email = replacementUsesSameMailbox ? "owner@gulf.example.test" : "replacement-finance@gulf.example.test",
      Role = "Finance", IsActive = true
    };
    Assert.Equal(replacementUsesSameMailbox, string.Equals(
      advanceMail[0].Recipient, replacementFinanceContact.Email, StringComparison.OrdinalIgnoreCase));
    db.ClientContacts.Add(replacementFinanceContact);
    await db.SaveChangesAsync();
    var replacementFinanceRouting = await PracticeCrmService.AssignContactRoutingAsync(db, w.Partner,
      new AssignContactRoutingRequest(clientId, replacementFinanceContact.Id, CorrespondencePurposes.Finance));
    Assert.True(replacementFinanceRouting.Succeeded, replacementFinanceRouting.Message);

    for (var i = 0; i < advanceMail.Count; i++) Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    Assert.Single(sender.Plans);
    Assert.Contains("Proposal revision", sender.Plans[0].Subject);
    var advanceMailIds = advanceMail.Select(x => x.Id).ToArray();
    var blockedNotifications = await db.CommercialNotifications.AsNoTracking()
      .Where(x => advanceMailIds.Contains(x.Id)).ToListAsync();
    Assert.Equal(advanceMail.Count, blockedNotifications.Count);
    Assert.All(blockedNotifications, mail =>
    {
      Assert.Equal(CommercialDeliveryStates.Queued, mail.DeliveryState);
      Assert.Null(mail.DeliveredAt);
      Assert.Equal(frozenMail[mail.Id], (mail.Recipient, mail.DocumentId, mail.OfferSha256, mail.Subject, mail.Body,
        mail.RecipientContactId, mail.RecipientRoutingId, mail.RecipientAuthorityBasis));
    });
    var blockedOperations = await db.DurableOperations.AsNoTracking()
      .Where(x => x.FirmId == w.FirmId && x.OperationKind == CommercialMailDeliveryHandler.Kind && advanceMailIds.Contains(x.TargetId))
      .ToListAsync();
    Assert.Equal(advanceMail.Count, blockedOperations.Count);
    Assert.All(blockedOperations, operation => Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, operation.Status));

    // Revoking the replacement restores the original client-primary authority. Even when the
    // Finance route had the same mailbox, its different contact/routing/basis must not authorize
    // these queued intents. A mismatched notification client is also refused when its primary
    // contact resolves to the frozen address, proving the artifact/agreement scope check.
    var revokeReplacement = await PracticeCrmService.RevokeContactRoutingAsync(db, w.Partner,
      new RevokeContactRoutingRequest(clientId, replacementFinanceRouting.Value, "Restore the existing recipient fallback."));
    Assert.True(revokeReplacement.Succeeded, revokeReplacement.Message);
    var bindingClient = new PracticeClient
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, LegalName = "Notification Binding Control LLC", CreatedAt = DateTimeOffset.UtcNow
    };
    db.PracticeClients.Add(bindingClient);
    db.ClientContacts.Add(new ClientContact
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, PracticeClientId = bindingClient.Id,
      FullName = "Binding Control Contact", Email = frozenMail[advanceMail[0].Id].Recipient,
      Role = "Finance", Primary = true, IsActive = true
    });
    var operationsAdmin = User(w.FirmId, "commercial-mail-operations-admin");
    db.Users.Add(operationsAdmin);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, UserId = operationsAdmin.Id, Role = "Administrator",
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
    });
    await db.SaveChangesAsync();
    var operationsAdminActor = Actor(operationsAdmin, "Administrator");

    async Task ReArmMailOperationAsync(Guid operationId)
    {
      var request = new OperationCommandRequest(Guid.NewGuid(), new(OperationCommandActions.Retry, OperationId: operationId));
      var preview = await OperationCommandWorkspace.PreviewAsync(db, operationsAdminActor, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.True(preview.Value!.CanProceed, string.Join(';', preview.Value.Blockers));
      var reviewed = request with
      {
        Input = preview.Value.CanonicalInput,
        ReviewBasis = preview.Value.ReviewBasis,
        ExpectedRequestHash = preview.Value.RequestHash,
        Reviewed = true
      };
      var rearmed = await OperationCommandWorkspace.ExecuteAsync(db, operationsAdminActor, reviewed);
      Assert.True(rearmed.Succeeded, rearmed.Message);
    }

    async Task WaitForMailOperationsAsync(Guid[] operationIds, OperationState expectedState)
    {
      for (var attempt = 0; attempt < 12; attempt++)
      {
        await using var check = new AuditSphereDbContext(pg.Options);
        var states = await check.DurableOperations.AsNoTracking()
          .Where(x => operationIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Status);
        if (operationIds.All(id => states.GetValueOrDefault(id) == expectedState)) return;
        if (!await worker.ProcessNextAsync()) await Task.Delay(TimeSpan.FromSeconds(1));
      }
      Assert.Fail($"Commercial mail operations did not reach {expectedState}.");
    }

    var receiptMail = advanceMail.Single(x => x.Kind == CommercialNotificationKinds.Receipt);
    var receiptOperationId = blockedOperations.Single(x => x.TargetId == receiptMail.Id).Id;
    var mutableReceiptMail = await db.CommercialNotifications.SingleAsync(x => x.Id == receiptMail.Id);
    mutableReceiptMail.PracticeClientId = bindingClient.Id;
    await db.SaveChangesAsync();
    var scopeProbe = new DurableOperation
    {
      Id = Guid.CreateVersion7(), OperationKind = CommercialMailDeliveryHandler.Kind, FirmId = w.FirmId,
      ClientId = bindingClient.Id, TargetId = receiptMail.Id, ExpectedRevision = 1, ClaimedEpoch = 1,
      CorrelationId = Guid.CreateVersion7(), PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
      {
        notificationId = receiptMail.Id.ToString("D")
      })
    };
    var scopeBlock = await Assert.ThrowsAsync<OperationBlockedException>(
      () => handler.ExecuteEffectAsync(scopeProbe, CancellationToken.None));
    Assert.True(scopeBlock.Authorization);
    var mismatchedReceipt = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == receiptMail.Id);
    Assert.Equal(CommercialDeliveryStates.Queued, mismatchedReceipt.DeliveryState);
    Assert.Equal(bindingClient.Id, mismatchedReceipt.PracticeClientId);
    Assert.Equal(frozenMail[receiptMail.Id],
      (mismatchedReceipt.Recipient, mismatchedReceipt.DocumentId, mismatchedReceipt.OfferSha256, mismatchedReceipt.Subject,
        mismatchedReceipt.Body, mismatchedReceipt.RecipientContactId, mismatchedReceipt.RecipientRoutingId,
        mismatchedReceipt.RecipientAuthorityBasis));
    Assert.Single(sender.Plans);

    mutableReceiptMail.PracticeClientId = clientId;
    await db.SaveChangesAsync();
    foreach (var operation in blockedOperations) await ReArmMailOperationAsync(operation.Id);
    await WaitForMailOperationsAsync(blockedOperations.Select(x => x.Id).ToArray(), OperationState.COMPLETED);

    Assert.Equal(4, sender.Plans.Count);
    Assert.Equal(2, sender.Plans.Count(p => p.Subject.StartsWith("Payment voucher")));
    Assert.Contains(sender.Plans, p => p.Subject.StartsWith("Payment receipt"));
    Assert.Single(sender.Plans, p => p.Subject.Contains("Proposal revision"));
    var deliveredAdvanceMail = await db.CommercialNotifications.AsNoTracking()
      .Where(x => advanceMailIds.Contains(x.Id)).ToListAsync();
    Assert.Equal(advanceMail.Count, deliveredAdvanceMail.Count);
    foreach (var mail in deliveredAdvanceMail)
    {
      Assert.Equal(CommercialDeliveryStates.ProviderAccepted, mail.DeliveryState);
      Assert.Equal(clientId, mail.PracticeClientId);
      Assert.Equal(frozenMail[mail.Id], (mail.Recipient, mail.DocumentId, mail.OfferSha256, mail.Subject, mail.Body,
        mail.RecipientContactId, mail.RecipientRoutingId, mail.RecipientAuthorityBasis));
      var document = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == mail.DocumentId);
      Assert.Equal(mail.OfferSha256, document.Sha256Hex);
      var plan = Assert.Single(sender.Plans, x => x.Subject == mail.Subject);
      Assert.Equal(mail.Recipient, plan.Recipient);
      Assert.Equal(document.FileName, plan.Attachment?.FileName);
      Assert.Equal(document.ContentType, plan.Attachment?.ContentType);
      Assert.Equal(document.Bytes, plan.Attachment?.Bytes);
    }
    Assert.False(await worker.ProcessNextAsync());
    var deliveredReceipt = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Kind == CommercialNotificationKinds.Receipt);
    Assert.Equal(("PROVIDER_ACCEPTED", true), (deliveredReceipt.DeliveryState, deliveredReceipt.DeliveredAt is not null));

    // Balance: needs the linked engagement and an issued release (final report delivered and signed off).
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).ErrorCode);
    var engagementId = Guid.NewGuid();
    var reviewer = User(w.FirmId, "reviewer"); var releasePartner = User(w.FirmId, "releasepartner");
    var workpaperId = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = engagementId, FirmId = w.FirmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.Users.AddRange(reviewer, releasePartner);
    db.RoleGrants.AddRange(
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = reviewer.Id, Role = "Reviewer", ClientId = clientId, EngagementId = engagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = reviewer.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = releasePartner.Id, Role = "Partner", ClientId = clientId, EngagementId = engagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = releasePartner.Id });
    db.Workpapers.Add(new Workpaper { Id = workpaperId, FirmId = w.FirmId, ClientId = clientId, EngagementId = engagementId, ActorId = releasePartner.Id, Index = "R-01",
      Title = "Final report", Objective = "Deliver", TemplateVersion = "RELEASE-2026-v1", Procedure = "Agree", Status = "WORKING", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Assert.True((await FeeAgreementService.LinkEngagementAsync(db, w.Prep, agreementId, engagementId)).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).ErrorCode); // no release yet

    var manifest = System.Text.Encoding.UTF8.GetBytes("final-report-manifest");
    var digest = Hashing.Sha256Hex(manifest);
    var checkpoints = new LocalAppendOnlyCheckpointStore(Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N")));
    var approval = await ApprovalService.CreateAsync(db, Actor(reviewer, "Reviewer"), new CreateApprovalRequest("WORKPAPER", workpaperId, 1, 1, 1, digest));
    var candidate = await ReleaseService.CreateCandidateAsync(db, Actor(releasePartner, "Partner"), new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", workpaperId, 1, 1, 1, digest));
    Assert.True((await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, checkpoints, Actor(releasePartner, "Partner"),
      new RecordReleaseCheckpointRequest(candidate.Value, 1, "release-final", digest, manifest))).Succeeded);
    Assert.True((await ReleaseService.IssueAsync(db, Actor(releasePartner, "Partner"), new IssueReleaseRequest(candidate.Value, 1, digest, "release-final"))).Succeeded);

    var balance = await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId);
    Assert.True(balance.Succeeded, balance.Message);
    Assert.Equal(balance.Value, (await FeeAgreementService.IssueBalanceInvoiceAsync(db, w.FinanceManager, agreementId)).Value); // never twice
    Assert.NotEqual(advanceInvoice.Value, balance.Value);
    Assert.Equal(20000m, await db.Invoices.Where(x => x.FirmId == w.FirmId).SumAsync(x => x.Total)); // advance + balance = agreed fee
    Assert.Equal(2, await db.BillingSourceAllocations.CountAsync(x => x.SourceKind == FeeAgreementService.MilestoneSourceKind));

    // Balance payment workflow (AS-COMP-08)
    // 1. Payment blocked while invoice is Draft
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 10000m, "TT-BAL-1")).ErrorCode);

    // 2. Submit, approve and post the balance invoice
    Assert.True((await BillingService.SubmitInvoiceAsync(db, w.FinanceManager, balance.Value)).Succeeded);
    Assert.True((await BillingService.ApproveInvoiceAsync(db, w.FinanceReviewer, balance.Value)).Succeeded);
    Assert.True((await BillingService.PostInvoiceAsync(db, w.FinanceManager, balance.Value)).Succeeded);

    // 3. Set up Finance contact routing
    var financeContact = new ClientContact
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = clientId,
      FullName = "Finance Department", Email = "finance@gulf.example.test", Role = "Finance", IsActive = true
    };
    var financeRoutingId = Guid.CreateVersion7();
    db.ClientContacts.Add(financeContact);
    db.ClientContactRoutings.Add(new ClientContactRouting
    {
      Id = financeRoutingId, FirmId = w.FirmId, PracticeClientId = clientId, ClientContactId = financeContact.Id,
      Purpose = CorrespondencePurposes.Finance, IsPrimaryForPurpose = true, CreatedByUserId = w.Partner.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    // 4. Partial balance payment
    var balPartial = await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 4000m, "TT-BAL-1");
    Assert.True(balPartial.Succeeded, balPartial.Message);
    Assert.False(balPartial.Value!.MilestonePaid);
    Assert.Equal(6000m, balPartial.Value.Outstanding);

    // 5. Duplicate balance payment with same reference
    var balRepeated = await FeeAgreementService.RecordBalancePaymentAsync(db, w.FinanceManager, agreementId, 4000m, "TT-BAL-1");
    Assert.Equal(balPartial.Value.ReceiptId, balRepeated.Value!.ReceiptId);

    // 6. Complete balance payment. Same-source callers from distinct authorized actors must
    // converge on one receipt, allocation, voucher, and settlement dispatch.
    var balanceMilestoneId = view.Milestones.Single(x => x.Milestone.Kind == FeeMilestoneKinds.Balance).Milestone.Id;
    var (balPaid, reviewerBalPaid) = await RunConcurrentPaymentAsync(
      (paymentDb, actor, paymentAmount, reference, ct) => FeeAgreementService.RecordBalancePaymentAsync(
        paymentDb, actor, agreementId, paymentAmount, reference, ct: ct), 6000m, "TT-BAL-2");
    Assert.True(balPaid.Succeeded, balPaid.Message);
    Assert.True(reviewerBalPaid.Succeeded, reviewerBalPaid.Message);
    Assert.True(balPaid.Value!.MilestonePaid && balPaid.Value.EmailQueued);
    Assert.NotNull(balPaid.Value.ReceiptDocumentId);
    Assert.Equal((true, 10000m, 0m, true, true),
      (balPaid.Value.MilestonePaid, balPaid.Value.Allocated, balPaid.Value.Outstanding,
        balPaid.Value.EmailQueued, balPaid.Value.VoucherEmailQueued));
    Assert.Equal((true, 10000m, 0m, true, true),
      (reviewerBalPaid.Value!.MilestonePaid, reviewerBalPaid.Value.Allocated, reviewerBalPaid.Value.Outstanding,
        reviewerBalPaid.Value.EmailQueued, reviewerBalPaid.Value.VoucherEmailQueued));
    Assert.Equal((balPaid.Value.ReceiptId, balPaid.Value.ReceiptDocumentId, balPaid.Value.VoucherDocumentId),
      (reviewerBalPaid.Value.ReceiptId, reviewerBalPaid.Value.ReceiptDocumentId, reviewerBalPaid.Value.VoucherDocumentId));

    var finalBalanceReference = $"BAL-{agreementId.ToString("N").ToUpperInvariant()}-TT-BAL-2";
    var finalBalanceReceipt = await db.Receipts.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.Reference == finalBalanceReference);
    Assert.Equal(balPaid.Value.ReceiptId, finalBalanceReceipt.Id);
    var finalBalanceAllocation = await db.ReceiptAllocations.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.ReceiptId == finalBalanceReceipt.Id && x.InvoiceId == balance.Value);
    Assert.Equal(6000m, finalBalanceAllocation.Amount);
    Assert.Equal(2, await db.ReceiptAllocations.CountAsync(x => x.FirmId == w.FirmId && x.InvoiceId == balance.Value));
    Assert.Equal(10000m, await BillingService.GetNetAppliedToInvoiceAsync(db, w.FirmId, balance.Value));

    Assert.NotNull(balPaid.Value.VoucherDocumentId);
    var finalBalanceVoucher = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.ReceiptId == finalBalanceReceipt.Id && x.Kind == CommercialDocumentKinds.PaymentVoucher);
    Assert.Equal(balPaid.Value.VoucherDocumentId, finalBalanceVoucher.Id);
    Assert.Equal(balanceMilestoneId, finalBalanceVoucher.FeeMilestoneId);
    var finalBalanceVoucherMail = await db.CommercialNotifications.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.Kind == CommercialNotificationKinds.PaymentVoucher && x.ReceiptId == finalBalanceReceipt.Id);
    Assert.Equal(finalBalanceVoucher.Id, finalBalanceVoucherMail.DocumentId);
    Assert.Equal(CommercialDeliveryStates.Queued, finalBalanceVoucherMail.DeliveryState);

    var finalBalanceSettlementReceipt = await db.CommercialDocuments.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.FeeMilestoneId == balanceMilestoneId && x.Kind == CommercialDocumentKinds.PaymentReceipt);
    Assert.Equal(balPaid.Value.ReceiptDocumentId, finalBalanceSettlementReceipt.Id);
    var finalBalanceSettlementMail = await db.CommercialNotifications.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.Kind == CommercialNotificationKinds.Receipt && x.FeeMilestoneId == balanceMilestoneId);
    Assert.Equal(finalBalanceSettlementReceipt.Id, finalBalanceSettlementMail.DocumentId);
    Assert.Equal(CommercialDeliveryStates.Queued, finalBalanceSettlementMail.DeliveryState);

    // 7. Balance receipt document text: full settlement phrasing
    var balReceiptDoc = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == balPaid.Value.ReceiptDocumentId);
    var balReceiptText = DocxText(balReceiptDoc.Bytes);
    var balanceReference = $"RCT-{balanceMilestoneId:N}".ToUpperInvariant();
    Assert.Equal($"Receipt-{balanceReference}.docx", balReceiptDoc.FileName);
    Assert.Contains(balanceReference, balReceiptText);
    Assert.NotEqual(receiptDoc.FileName, balReceiptDoc.FileName);
    var retainedAdvance = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == receiptDoc.Id);
    Assert.Equal(receiptDoc.FileName, retainedAdvance.FileName);
    Assert.Equal(receiptDoc.Sha256Hex, retainedAdvance.Sha256Hex);
    Assert.Equal(receiptDoc.Bytes, retainedAdvance.Bytes);
    Assert.Contains("Total outstanding balance", balReceiptText);
    Assert.Contains("0.00 QAR", balReceiptText);
    Assert.Contains("paid in full", balReceiptText);

    // 8. Finance notification routed to finance contact
    var balNotification = await db.CommercialNotifications.AsNoTracking()
      .Where(x => x.Kind == CommercialNotificationKinds.Receipt)
      .OrderByDescending(x => x.CreatedAt)
      .FirstAsync();
    Assert.Equal("finance@gulf.example.test", balNotification.Recipient);
    if (legacySnapshot)
    {
      Assert.Null(balNotification.RecipientContactId);
      Assert.Null(balNotification.RecipientRoutingId);
      Assert.Null(balNotification.RecipientAuthorityBasis);
    }
    else
    {
      Assert.Equal(financeContact.Id, balNotification.RecipientContactId);
      Assert.Equal(financeRoutingId, balNotification.RecipientRoutingId);
      Assert.Equal(PaymentRecipientAuthorityBases.FinanceRouting, balNotification.RecipientAuthorityBasis);
    }
    var frozenBalanceMail = (balNotification.Recipient, balNotification.DocumentId, balNotification.OfferSha256,
      balNotification.Subject, balNotification.Body, balNotification.RecipientContactId,
      balNotification.RecipientRoutingId, balNotification.RecipientAuthorityBasis);
    // Two recorded payments produce two vouchers plus the settlement receipt.
    var pendingBalanceMail = await db.CommercialNotifications.AsNoTracking().Where(x => x.FirmId == w.FirmId &&
      x.PracticeClientId == clientId && x.DeliveryState == CommercialDeliveryStates.Queued &&
      (x.Kind == CommercialNotificationKinds.Receipt || x.Kind == CommercialNotificationKinds.PaymentVoucher)).ToListAsync();
    Assert.Equal(3, pendingBalanceMail.Count);
    Assert.All(pendingBalanceMail, mail =>
    {
      Assert.Equal("finance@gulf.example.test", mail.Recipient);
      Assert.Equal(legacySnapshot ? null : (Guid?)financeContact.Id, mail.RecipientContactId);
      Assert.Equal(legacySnapshot ? null : (Guid?)financeRoutingId, mail.RecipientRoutingId);
      Assert.Equal(legacySnapshot ? null : PaymentRecipientAuthorityBases.FinanceRouting, mail.RecipientAuthorityBasis);
    });
    foreach (var _ in pendingBalanceMail) Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    var balanceMailIds = pendingBalanceMail.Select(x => x.Id).ToArray();
    Assert.Equal(3, await db.CommercialNotifications.AsNoTracking().CountAsync(x => balanceMailIds.Contains(x.Id) &&
      x.DeliveryState == CommercialDeliveryStates.ProviderAccepted));
    var deliveredBalanceMail = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == balNotification.Id);
    Assert.Equal(CommercialDeliveryStates.ProviderAccepted, deliveredBalanceMail.DeliveryState);
    Assert.Equal(frozenBalanceMail, (deliveredBalanceMail.Recipient, deliveredBalanceMail.DocumentId,
      deliveredBalanceMail.OfferSha256, deliveredBalanceMail.Subject, deliveredBalanceMail.Body,
      deliveredBalanceMail.RecipientContactId, deliveredBalanceMail.RecipientRoutingId,
      deliveredBalanceMail.RecipientAuthorityBasis));
    var balancePlan = Assert.Single(sender.Plans, plan => plan.Subject == deliveredBalanceMail.Subject);
    Assert.Equal(deliveredBalanceMail.Recipient, balancePlan.Recipient);
    var balanceAttachment = await db.CommercialDocuments.AsNoTracking().SingleAsync(x => x.Id == deliveredBalanceMail.DocumentId);
    Assert.Equal(balanceAttachment.FileName, balancePlan.Attachment?.FileName);
    Assert.Equal(balanceAttachment.ContentType, balancePlan.Attachment?.ContentType);
    Assert.Equal(balanceAttachment.Bytes, balancePlan.Attachment?.Bytes);

    var paymentTimeline = await EngagementCommunicationTimelineQuery.GetAsync(db, w.Partner, engagementId);
    Assert.True(paymentTimeline.Succeeded, paymentTimeline.Message);
    var voucherEntries = paymentTimeline.Value!.Entries.Where(x =>
      x.Source == "COMMERCIAL" && x.Kind == CommercialNotificationKinds.PaymentVoucher).ToList();
    Assert.Equal(4, voucherEntries.Count);
    Assert.All(voucherEntries, entry =>
    {
      Assert.Equal(CommercialDeliveryStates.ProviderAccepted, entry.Status);
      Assert.Null(entry.DeliveredAtUtc);
    });
    var acceptedVoucher = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.Id == finalBalanceVoucherMail.Id);
    Assert.NotNull(acceptedVoucher.DeliveredAt);
    await db.CommercialNotifications.Where(x => x.Id == acceptedVoucher.Id).ExecuteUpdateAsync(update =>
      update.SetProperty(x => x.DeliveryState, CommercialDeliveryStates.Delivered));
    var deliveredTimeline = await EngagementCommunicationTimelineQuery.GetAsync(db, w.Partner, engagementId);
    Assert.True(deliveredTimeline.Succeeded, deliveredTimeline.Message);
    var deliveredVoucher = Assert.Single(deliveredTimeline.Value!.Entries, x => x.Id == acceptedVoucher.Id);
    Assert.Equal(CommercialDeliveryStates.Delivered, deliveredVoucher.Status);
    Assert.Equal(acceptedVoucher.DeliveredAt, deliveredVoucher.DeliveredAtUtc);

    // 9. Client portal user document download
    var docDownload = await CommercialDocumentService.GetAsync(db, clientActor, balReceiptDoc.Id);
    Assert.True(docDownload.Succeeded, docDownload.Message);
    Assert.NotNull(docDownload.Value);
    Assert.Equal(balReceiptDoc.FileName, docDownload.Value.FileName);

    // Client user without grant to this client is forbidden
    var otherClient = new PracticeClient { Id = Guid.CreateVersion7(), FirmId = w.FirmId, LegalName = "Other Client LLC", CreatedAt = DateTimeOffset.UtcNow };
    db.PracticeClients.Add(otherClient);
    var foreignClientUser = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, Subject = "foreign-client-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-commercial", Email = "unauth@example.test", DisplayName = "Unauth",
      UserKind = "Client", CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(foreignClientUser);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = foreignClientUser.Id, Role = "ClientUser",
      ClientId = otherClient.Id, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
    });
    var termsReviewedAt = DateTimeOffset.UtcNow.AddDays(-12);
    var overdueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-10);
    db.InvoicePaymentTermsRevisions.Add(new InvoicePaymentTermsRevision
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, InvoiceId = advanceInvoice.Value, Revision = 1,
      DueDate = overdueDate, Basis = InvoicePaymentTermsKinds.ContractualDueDate,
      TermsDescription = "Synthetic reviewed payment terms.", EvidenceReference = "TEST-TERMS-001",
      Status = InvoicePaymentTermsStates.Approved, SubmittedByUserId = w.FinanceManager.UserId,
      SubmittedAt = termsReviewedAt.AddMinutes(-1), ReviewedByUserId = w.FinanceReviewer.UserId,
      ReviewedAt = termsReviewedAt, ReviewReason = "Verified synthetic engagement terms."
    });
    await db.SaveChangesAsync();
    var foreignActor = new ActorContext(foreignClientUser.Id, w.FirmId, foreignClientUser.SessionEpoch, ["ClientUser"]);
    var foreignDoc = await CommercialDocumentService.GetAsync(db, foreignActor, balReceiptDoc.Id);
    Assert.False(foreignDoc.Succeeded);

    // 10. Client Portal Finance Query
    var portalFinance = await ClientPortalFinanceQuery.GetAsync(db, clientActor);
    Assert.True(portalFinance.Succeeded, portalFinance.Message);
    Assert.NotNull(portalFinance.Value);
    Assert.Single(portalFinance.Value.Agreements);
    var agreementFinance = portalFinance.Value.Agreements[0];
    Assert.Equal(2, agreementFinance.Invoices.Count);
    Assert.Equal(4, agreementFinance.Receipts.Count); // 2 advance receipts + 2 balance receipts
    Assert.Equal("0", agreementFinance.OutstandingBalance);
    var settledAdvanceInvoice = Assert.Single(agreementFinance.Invoices, x => x.InvoiceId == advanceInvoice.Value);
    Assert.Equal("PAID", settledAdvanceInvoice.CollectionStatus);
    Assert.Equal(FirmReceivablesAgingPolicy.Settled, settledAdvanceInvoice.AgingStatus);
    Assert.Equal(overdueDate, settledAdvanceInvoice.DueDate);
    Assert.Equal(InvoicePaymentTermsStates.Approved, settledAdvanceInvoice.PaymentTermsStatus);
    Assert.Equal("NOT_TRACKED", settledAdvanceInvoice.DeliveryStatus);
    var originalPortalReceipt = Assert.Single(agreementFinance.Receipts, x => x.ReceiptId == partial.Value.ReceiptId);
    Assert.Equal(4000m, decimal.Parse(originalPortalReceipt.Amount, CultureInfo.InvariantCulture));
    Assert.Equal(4000m, decimal.Parse(originalPortalReceipt.AppliedAmount, CultureInfo.InvariantCulture));

    // An approved cash unapplication must reopen the current advance gate without
    // rewriting the receipt proving that it was previously settled.
    var advanceMilestone = await db.FeeMilestones.SingleAsync(x =>
      x.FirmId == w.FirmId && x.AgreementId == agreementId && x.Kind == FeeMilestoneKinds.Advance);
    var originalPaidReceiptId = advanceMilestone.ReceiptId;
    var originalPaidAt = advanceMilestone.PaidAt;
    var originalSettlementId = paid.Value.ReceiptDocumentId!.Value;
    var originalSettlementBytes = (await db.CommercialDocuments.AsNoTracking()
      .SingleAsync(x => x.Id == originalSettlementId)).Bytes.ToArray();
    var advanceAllocation = await db.ReceiptAllocations.AsNoTracking()
      .SingleAsync(x => x.FirmId == w.FirmId && x.InvoiceId == advanceInvoice.Value && x.ReceiptId == partial.Value.ReceiptId);
    var rejectedReversal = await ReceiptAllocationReversalService.SubmitAsync(db, w.FinanceManager, advanceAllocation.Id,
      new(100m, "SYNTHETIC-ADVANCE-REJECTED", "Reviewed correction fixture.", 0, true));
    Assert.True(rejectedReversal.Succeeded, rejectedReversal.Message);
    Assert.Equal(FeeMilestoneStates.Paid, advanceMilestone.State);
    Assert.True((await ReceiptAllocationReversalService.ReviewAsync(db, w.FinanceReviewer, rejectedReversal.Value,
      new(false, "The original allocation is valid.", true))).Succeeded);
    Assert.Equal(FeeMilestoneStates.Paid, advanceMilestone.State);
    var rejectedPortalFinance = await ClientPortalFinanceQuery.GetAsync(db, clientActor);
    Assert.True(rejectedPortalFinance.Succeeded, rejectedPortalFinance.Message);
    Assert.Equal("0", Assert.Single(rejectedPortalFinance.Value!.Agreements).OutstandingBalance);
    Assert.Equal(4000m, decimal.Parse(Assert.Single(rejectedPortalFinance.Value.Agreements[0].Receipts,
      x => x.ReceiptId == partial.Value.ReceiptId).AppliedAmount, CultureInfo.InvariantCulture));

    var approvedReversal = await ReceiptAllocationReversalService.SubmitAsync(db, w.FinanceManager, advanceAllocation.Id,
      new(100m, "SYNTHETIC-ADVANCE-UNAPPLY", "Independently confirmed unapplication.", 1, true));
    Assert.True(approvedReversal.Succeeded, approvedReversal.Message);
    var reversalReview = new ReviewReceiptAllocationReversal(true, "Verified the retained correction evidence.", true);
    await using (var blocker = new AuditSphereDbContext(pg.Options))
    await using (var reviewingDb = new AuditSphereDbContext(pg.Options))
    await using (var replayDb = new AuditSphereDbContext(pg.Options))
    await using (var observer = new AuditSphereDbContext(pg.Options))
    {
      await using var fence = await blocker.Database.BeginTransactionAsync();
      await blocker.FirmSafetyStates.FromSqlInterpolated(
        $"SELECT * FROM firm_safety_states WHERE id = {w.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
      async Task WaitForFirmWaiters(int expected)
      {
        while (await observer.Database.SqlQueryRaw<int>(
          "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() " +
          "AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE '%firm_safety_states%'")
          .SingleAsync(timeout.Token) < expected)
          await Task.Delay(25, timeout.Token);
      }
      var reviewTask = ReceiptAllocationReversalService.ReviewAsync(reviewingDb, w.FinanceReviewer,
        approvedReversal.Value, reversalReview, timeout.Token);
      await WaitForFirmWaiters(1);
      // This replay reads its PAID milestone before waiting; reversal must win before
      // it can republish settlement from that stale tracked instance.
      var paymentReplay = FeeAgreementService.RecordAdvancePaymentAsync(replayDb, w.FinanceManager,
        agreementId, 6000m, "TT-2", ct: timeout.Token);
      try
      {
        await WaitForFirmWaiters(2);
        await fence.CommitAsync(timeout.Token);
      }
      finally
      {
        if (blocker.Database.CurrentTransaction is not null) await fence.RollbackAsync();
      }
      var reviewedReversal = await reviewTask;
      Assert.True(reviewedReversal.Succeeded, reviewedReversal.Message);
      var replayedPayment = await paymentReplay;
      Assert.True(replayedPayment.Succeeded, replayedPayment.Message);
      Assert.False(replayedPayment.Value!.MilestonePaid);
      Assert.Equal(100m, replayedPayment.Value.Outstanding);
    }
    await db.Entry(advanceMilestone).ReloadAsync();
    await db.Entry(db.ReceiptAllocationReversals.Local.Single(x => x.Id == approvedReversal.Value)).ReloadAsync();
    Assert.Equal(FeeMilestoneStates.Paid, advanceMilestone.State); // retained historical settlement
    Assert.Equal(originalPaidReceiptId, advanceMilestone.ReceiptId);
    Assert.Equal(originalPaidAt, advanceMilestone.PaidAt);
    Assert.False(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, w.FirmId, agreementId));
    Assert.Equal(9900m, await BillingService.GetNetAppliedToInvoiceAsync(db, w.FirmId, advanceInvoice.Value));
    var reopenedPortalFinance = await ClientPortalFinanceQuery.GetAsync(db, clientActor);
    Assert.True(reopenedPortalFinance.Succeeded, reopenedPortalFinance.Message);
    var reopenedPortalAgreement = Assert.Single(reopenedPortalFinance.Value!.Agreements);
    Assert.Equal(100m, decimal.Parse(reopenedPortalAgreement.OutstandingBalance, CultureInfo.InvariantCulture));
    var reopenedAdvanceInvoice = Assert.Single(reopenedPortalAgreement.Invoices, x => x.InvoiceId == advanceInvoice.Value);
    Assert.Equal("PARTIALLY_PAID", reopenedAdvanceInvoice.CollectionStatus);
    Assert.Equal("9900.000000", reopenedAdvanceInvoice.CashApplied);
    Assert.Equal("100.000000", reopenedAdvanceInvoice.OutstandingAmount);
    Assert.Equal(FirmReceivablesAgingPolicy.Overdue1To30, reopenedAdvanceInvoice.AgingStatus);
    var reversedPortalReceipt = Assert.Single(reopenedPortalAgreement.Receipts, x => x.ReceiptId == partial.Value.ReceiptId);
    Assert.Equal(4000m, decimal.Parse(reversedPortalReceipt.Amount, CultureInfo.InvariantCulture));
    Assert.Equal(3900m, decimal.Parse(reversedPortalReceipt.AppliedAmount, CultureInfo.InvariantCulture));
    var reopenedReadiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Partner, engagementId);
    Assert.True(reopenedReadiness.Succeeded, reopenedReadiness.Message);
    Assert.NotEqual(OnboardingReadinessStatuses.Paid, reopenedReadiness.Value!.AdvancePayment.Status);
    Assert.True((await ReceiptAllocationReversalService.ReviewAsync(db, w.FinanceReviewer, approvedReversal.Value, reversalReview)).Succeeded);
    Assert.Equal(9900m, await BillingService.GetNetAppliedToInvoiceAsync(db, w.FirmId, advanceInvoice.Value));
    Assert.Equal(originalSettlementBytes, (await db.CommercialDocuments.AsNoTracking()
      .SingleAsync(x => x.Id == originalSettlementId)).Bytes);

    var restored = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager, agreementId, 100m, "TT-REPLACEMENT");
    Assert.True(restored.Succeeded, restored.Message);
    Assert.True(restored.Value!.MilestonePaid);
    Assert.Equal(10000m, await BillingService.GetNetAppliedToInvoiceAsync(db, w.FirmId, advanceInvoice.Value));
    Assert.Equal(FeeMilestoneStates.Paid, advanceMilestone.State);
    Assert.True(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, w.FirmId, agreementId));
    Assert.Equal(originalSettlementBytes, (await db.CommercialDocuments.AsNoTracking()
      .SingleAsync(x => x.Id == originalSettlementId)).Bytes);

    // A credit can settle AR, but cannot replace the required cash allocation.
    var cashReversal = await ReceiptAllocationReversalService.SubmitAsync(db, w.FinanceManager, advanceAllocation.Id,
      new(100m, "SYNTHETIC-ADVANCE-CREDIT-CHECK", "Separate AR credit from actual allocated cash.", 2, true));
    Assert.True(cashReversal.Succeeded, cashReversal.Message);
    Assert.True((await ReceiptAllocationReversalService.ReviewAsync(db, w.FinanceReviewer, cashReversal.Value,
      new(true, "Confirmed the second cash correction.", true))).Succeeded);
    var credit = await BillingService.IssueCreditNoteAsync(db, w.FinanceManager,
      new(advanceInvoice.Value, "CN-SYNTHETIC-ADVANCE", 100m, "AR credit is not cash.", Guid.CreateVersion7()));
    Assert.True(credit.Succeeded, credit.Message);
    Assert.Equal(0m, (await BillingService.GetInvoiceBalanceAsync(db, w.FinanceManager, advanceInvoice.Value)).Value!.Outstanding);
    var cashStillMissing = await FeeAgreementService.RecordAdvancePaymentAsync(db, w.FinanceManager,
      agreementId, 100m, "TT-REPLACEMENT");
    Assert.True(cashStillMissing.Succeeded, cashStillMissing.Message);
    Assert.False(cashStillMissing.Value!.MilestonePaid);
    Assert.Equal(9900m, cashStillMissing.Value.Allocated);
    Assert.Equal(100m, cashStillMissing.Value.Outstanding);
    Assert.Equal(FeeMilestoneStates.Paid, advanceMilestone.State);
    Assert.False(await FeeAgreementService.AdvanceCurrentlyPaidAsync(db, w.FirmId, agreementId));
  }

  [Fact]
  public async Task Dispatch_BindsArtifactToCurrentQuotation_AndDownstreamUsesAcceptedTerms()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Quotation A: approved while the proposal is a draft, priced, and its brief artifact generated.
    var quoteA = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId)); // 25,000
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quoteA.Value)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new SaveCommercialProfileRequest("Binding Firm", "Doha", "", "", "#2B6CB0", "Closing"))).Succeeded);
    var artifactA = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quoteA.Value, expectedProfileVersion: 1);
    Assert.True(artifactA.Succeeded, artifactA.Message);

    // A later approved quotation revision B prices the same fee with a different composition while the
    // proposal is still a draft: the reviewed artifact A is now older than the current quotation, so
    // dispatch is refused until the current brief is regenerated (STE 4.1.2 — the accepted terms are
    // the dispatched artifact's, not merely the newest approved pricing revision).
    var quoteB = await QuotationService.SaveAsync(db, w.Prep,
      Quote(proposalId, lines: [new QuotationHoursLine("Partner", "Audit", 17.5m), new QuotationHoursLine("Manager", "Audit", 10m)]));
    Assert.Equal(25000m, (await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == quoteB.Value)).Fee);
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quoteB.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).ErrorCode);
    Assert.Empty(await db.CommercialNotifications.Where(x => x.ProposalId == proposalId).ToListAsync());

    // Regenerating the current brief binds dispatch and acceptance to revision B.
    var artifactB = await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quoteB.Value, expectedProfileVersion: 1);
    Assert.True(artifactB.Succeeded, artifactB.Message);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalId);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var notification = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.ProposalId == proposalId);
    Assert.Equal(quoteB.Value, notification.QuotationVersionId);
    Assert.Equal(artifactB.Value!.Sha256Hex, notification.OfferSha256);
    var acceptedSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.Equal("crm.stale-offer", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, artifactA.Value!.Sha256Hex, "A. Owner", "owner@gulf.example.test",
        "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).ErrorCode);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, acceptedSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);

    // Repricing after the proposal left draft is refused at the source, so an approved revision can
    // never silently diverge from the accepted artifact downstream; material changes require a new
    // proposal revision with fresh dispatch and acceptance.
    Assert.Equal(ErrorCodes.ProtectedState, (await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId))).ErrorCode);

    // A fresh proposal without repricing creates its agreement on exactly the accepted quotation.
    await using (var db2 = new AuditSphereDbContext(pg.Options))
    {
      var lead2 = await PracticeCrmService.CreateLeadAsync(db2, w.Prep,
        new CreateLeadRequest("Binding Client Lead", "Web Form", "B. Owner", "binding-client@example.test"));
      Assert.True(lead2.Succeeded, lead2.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db2, w.Prep, lead2.Value)).Succeeded);
      var opportunity2 = await PracticeCrmService.CreateOpportunityAsync(db2, w.Prep,
        new CreateOpportunityRequest(lead2.Value, "FinancialStatementAudit", "BINDING-CLIENT", "2026-01-01", "2026-12-31", 30000m, "QAR", 60m));
      Assert.True(opportunity2.Succeeded, opportunity2.Message);
      var revised2 = await PracticeCrmService.ReviseProposalAsync(db2, w.Prep,
        new ReviseProposalRequest(opportunity2.Value, "AUDIT-2026", "Statutory audit of the 2026 financial statements",
          "Tax advisory", "Independent auditor's report and management letter", "Client supplies the trial balance",
          25000m, "QAR", "2026-01-01", "2026-12-31",
          Timeline: "Fieldwork in March 2027; report by 30 April 2027"));
      Assert.True(revised2.Succeeded, revised2.Message);
    }
    var proposal2 = (await db.Proposals.AsNoTracking()
      .Where(x => x.FirmId == w.FirmId && x.Id != proposalId).OrderByDescending(x => x.CreatedAt).FirstAsync()).Id;
    var quote2 = await QuotationService.SaveAsync(db, w.Prep, Quote(proposal2));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote2.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposal2)).Succeeded);
    Assert.True((await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposal2,
      expectedQuotationId: quote2.Value, expectedProfileVersion: 1)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposal2);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposal2)).Succeeded);
    var offer2 = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposal2)).SentOfferSha256;
    Assert.Equal("crm.unauthorized-respondent", (await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposal2,
      new("ACCEPTED", null, offer2, "A. Owner", "owner@gulf.example.test",
        "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).ErrorCode);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposal2,
      new("ACCEPTED", null, offer2, "B. Owner", "binding-client@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    var converted2 = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposal2, "Binding Client LLC"));
    Assert.True(converted2.Succeeded, converted2.Message);
    var agreement = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposal2);
    Assert.True(agreement.Succeeded, agreement.Message);
    var stored = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == agreement.Value);
    Assert.Equal(quote2.Value, stored.QuotationVersionId);
  }

  [Fact]
  public async Task FeeAgreement_RequiresAcceptanceCitingDispatchedOffer()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Standard happy path: approved quotation, brief artifact, dispatch, acceptance of the exact offer, client.
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId,
      lines: [new QuotationHoursLine("Partner", "Audit", 17.5m), new QuotationHoursLine("Manager", "Audit", 10m)]));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await CommercialDocumentService.SaveProfileAsync(db, w.Partner,
      new SaveCommercialProfileRequest("Binding Firm", "Doha", "", "", "#2B6CB0", "Closing"))).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    Assert.True((await CommercialDocumentService.GenerateBriefQuotationAsync(db, w.Prep, proposalId,
      expectedQuotationId: quote.Value, expectedProfileVersion: 1)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalId);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId);
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, proposal.SentOfferSha256, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    Assert.True((await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Binding Client LLC"))).Succeeded);

    // A recorded acceptance that does not cite the dispatched offer identity (a stale, replayed, or
    // tampered citation) can never underwrite the fee agreement: the agreement must carry the exact
    // accepted commercial terms the client responded to (STE 4.1.3, STE-REM-02).
    var recordedAcceptance = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).ResponseOfferSha256;
    await db.Proposals.Where(x => x.Id == proposalId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ResponseOfferSha256, new string('f', 64)));
    var refused = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.Equal(ErrorCodes.GenerationStale, refused.ErrorCode);
    Assert.Empty(await db.EngagementFeeAgreements.Where(x => x.ProposalId == proposalId).ToListAsync());

    // Restoring the exact dispatched citation restores the normal reviewed agreement path.
    await db.Proposals.Where(x => x.Id == proposalId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ResponseOfferSha256, recordedAcceptance));
    var agreement = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.True(agreement.Succeeded, agreement.Message);
    var stored = await db.EngagementFeeAgreements.AsNoTracking().SingleAsync(x => x.Id == agreement.Value);
    Assert.Equal(quote.Value, stored.QuotationVersionId);
  }

  [Theory]
  [InlineData("CREATE_NEW")]
  [InlineData("CREATE_REPLAY")]
  [InlineData("LINK_NEW")]
  [InlineData("LINK_REPLAY")]
  [InlineData("LINK_GRANT_ONLY")]
  public async Task FeeAgreementWriters_RecheckAuthorityAfterFirmLockWait(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    var engagementId = Guid.NewGuid();
    Guid agreementId = Guid.Empty;

    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var quotation = await QuotationService.SaveAsync(setup, w.Prep, Quote(proposalId));
      Assert.True(quotation.Succeeded, quotation.Message);
      Assert.True((await QuotationService.SubmitAsync(setup, w.Prep, quotation.Value)).Succeeded);
      Assert.True((await PracticeCrmService.ApproveProposalAsync(setup, w.Partner, proposalId)).Succeeded);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(setup, w.Prep, proposalId);
      Assert.True((await PracticeCrmService.SendProposalAsync(setup, w.Prep, proposalId)).Succeeded);
      var offerSha = (await setup.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(setup, w.Prep, proposalId,
        new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter",
          Channel: ProposalResponseChannels.Email))).Succeeded);
      var client = await PracticeCrmService.ConvertToClientDraftAsync(setup, w.Prep, new(proposalId, "Fee authority client"));
      Assert.True(client.Succeeded, client.Message);
      setup.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = w.FirmId, PracticeClientId = client.Value,
        ServiceRoute = "FinancialStatementAudit", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        CreatedAt = DateTimeOffset.UtcNow
      });
      await setup.SaveChangesAsync();

      if (action != "CREATE_NEW")
      {
        var created = await FeeAgreementService.CreateAgreementAsync(setup, w.Prep, proposalId);
        Assert.True(created.Succeeded, created.Message);
        agreementId = created.Value;
        if (action is "CREATE_REPLAY" or "LINK_REPLAY")
          Assert.True((await FeeAgreementService.LinkEngagementAsync(setup, w.Prep, agreementId, engagementId)).Succeeded);
      }

      Assert.Equal(action == "CREATE_NEW" ? 0 : 1,
        await setup.EngagementFeeAgreements.AsNoTracking().CountAsync(x => x.FirmId == w.FirmId));
      Assert.Equal(action == "CREATE_NEW" ? 0 : 2,
        await setup.FeeMilestones.AsNoTracking().CountAsync(x => x.FirmId == w.FirmId));
    }

    async Task<string> SnapshotAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.LegalName, x.Status, x.BillingAccountId }).ToArrayAsync();
      var proposalLinks = await db.Proposals.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.OpportunityId, x.PracticeClientId, x.Status, x.Revision }).ToArrayAsync();
      var opportunityLinks = await db.Opportunities.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.LeadId, x.PracticeClientId, x.Stage }).ToArrayAsync();
      var agreements = await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).Select(x => new
        {
          x.Id, x.ProposalId, x.QuotationVersionId, x.PracticeClientId, x.EngagementId,
          x.Currency, x.AgreedFee, x.AdvancePercent, x.CreatedByUserId, x.CreatedAt
        }).ToArrayAsync();
      var milestones = await db.FeeMilestones.AsNoTracking().Where(x => x.FirmId == w.FirmId)
        .OrderBy(x => x.Id).Select(x => new
        {
          x.Id, x.AgreementId, x.Kind, x.Amount, x.State, x.InvoiceId, x.ReceiptId, x.PaidAt, x.CreatedAt
        }).ToArrayAsync();
      return JsonSerializer.Serialize(new { clients, proposalLinks, opportunityLinks, agreements, milestones });
    }

    var before = await SnapshotAsync();
    Guid grantId;
    await using (var grantDb = new AuditSphereDbContext(pg.Options))
    {
      grantId = await grantDb.RoleGrants.AsNoTracking().Where(x => x.FirmId == w.FirmId &&
          x.UserId == w.Prep.UserId && x.Role == "RelationshipManager" && x.RevokedAt == null &&
          x.ClientId == null && x.EngagementId == null)
        .Select(x => x.Id).SingleAsync();
    }

    var grantOnly = action == "LINK_GRANT_ONLY";
    var create = action.StartsWith("CREATE_", StringComparison.Ordinal);
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {w.FirmId} FOR UPDATE").SingleAsync();

    var lockAttempt = new ObserveFirmLockAttempt();
    var writerOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(lockAttempt).Options;
    await using var writer = new AuditSphereDbContext(writerOptions);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    async Task<(bool Succeeded, string? ErrorCode, Guid? Value)> ExecuteWriterAsync()
    {
      if (create)
      {
        var result = await FeeAgreementService.CreateAgreementAsync(writer, w.Prep, proposalId, timeout.Token);
        return (result.Succeeded, result.ErrorCode, result.Value);
      }
      var linked = await FeeAgreementService.LinkEngagementAsync(writer, w.Prep, agreementId, engagementId, timeout.Token);
      return (linked.Succeeded, linked.ErrorCode, null);
    }

    var attempt = ExecuteWriterAsync();
    await lockAttempt.Reached.Task.WaitAsync(timeout.Token);
    Assert.True(lockAttempt.Fired);
    Assert.Equal(1, await blocker.RoleGrants.Where(x => x.Id == grantId && x.RevokedAt == null)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token));
    if (!grantOnly)
      Assert.Equal(1, await blocker.Users.Where(x => x.Id == w.Prep.UserId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token));
    await blockerTransaction.CommitAsync(timeout.Token);

    var outcome = await attempt.WaitAsync(timeout.Token);
    Assert.False(outcome.Succeeded);
    Assert.Equal(grantOnly ? ErrorCodes.ScopeDenied : ErrorCodes.GenerationStale, outcome.ErrorCode);
    Assert.True(outcome.Value is null || outcome.Value == Guid.Empty, "A denied create must not disclose an agreement ID.");
    Assert.Equal(before, await SnapshotAsync());
  }

  [Fact]
  public async Task LegacyAcceptedProposalWithoutDispatchCannotConvertOrCreateFeeAgreement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, opportunityId, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);

    // A legacy/corrupt accepted row without its immutable outbox evidence is not treated as a dispatched client offer.
    var offerHash = Hashing.Sha256Hex("synthetic missing-dispatch offer");
    var now = DateTimeOffset.UtcNow;
    await db.Proposals.Where(x => x.Id == proposalId).ExecuteUpdateAsync(s => s
      .SetProperty(x => x.Status, CrmStates.ProposalAccepted)
      .SetProperty(x => x.SentAt, now)
      .SetProperty(x => x.ResponseAt, now)
      .SetProperty(x => x.SentOfferSha256, offerHash)
      .SetProperty(x => x.ResponseOfferSha256, offerHash));
    await db.Opportunities.Where(x => x.Id == opportunityId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Stage, CrmStates.OpportunityWon));
    // The raw updates bypass the change tracker, which still holds the proposal as it was approved. Clear it so the
    // commands below read the stored row rather than the stale tracked copy.
    db.ChangeTracker.Clear();
    var client = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Missing Dispatch Client"));
    Assert.False(client.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, client.ErrorCode);
    Assert.Contains("immutable acceptance record", client.Message, StringComparison.OrdinalIgnoreCase);
    Assert.False(await db.PracticeClients.AnyAsync(x => x.FirmId == w.FirmId && x.LegalName == "Missing Dispatch Client"));
    Assert.Equal(ErrorCodes.GateBlocked, (await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId)).ErrorCode);
    Assert.Empty(await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.ProposalId == proposalId).ToListAsync());
  }

  [Theory]
  [InlineData("legacy-null-account-mismatch")]
  [InlineData("invoice-account")]
  [InlineData("receipt-account")]
  [InlineData("credit-account")]
  [InlineData("missing-invoice")]
  public async Task ClientPortalFinanceRejectsPersistedCurrencyMismatchesAndMissingLinkedInvoices(string scenario)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var rows = await SeedPortalFinanceProjectionAsync(pg, fixture, scenario);
    var expectedError = scenario == "missing-invoice" ? ErrorCodes.ScopeDenied : ErrorCodes.GateBlocked;
    var actor = PbcSeed.Actor(fixture.Client, "ClientUser");

    await using (var mainDb = new AuditSphereDbContext(pg.Options))
    {
      var finance = await ClientPortalFinanceQuery.GetAsync(mainDb, actor);
      Assert.False(finance.Succeeded);
      Assert.Equal(expectedError, finance.ErrorCode);
      Assert.Null(finance.Value);
    }

    await using (var receiptDb = new AuditSphereDbContext(pg.Options))
    {
      var receipts = await ClientPortalFinanceQuery.GetReceiptsAsync(receiptDb, actor, rows.AgreementId);
      Assert.False(receipts.Succeeded);
      Assert.Equal(expectedError, receipts.ErrorCode);
      Assert.Null(receipts.Value);
    }
  }

  [Fact]
  public async Task ClientPortalFinanceUsesAccountCurrencyForLegacyNullRowsAndAcceptsMatchingQarSources()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var rows = await SeedPortalFinanceProjectionAsync(pg, fixture, "legacy-null");
    var actor = PbcSeed.Actor(fixture.Client, "ClientUser");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var finance = await ClientPortalFinanceQuery.GetAsync(db, actor);
      Assert.True(finance.Succeeded, finance.Message);
      var agreement = Assert.Single(finance.Value!.Agreements);
      Assert.Equal("QAR", agreement.Currency);
      Assert.Equal("150.000000", agreement.OutstandingBalance);
      var advance = Assert.Single(agreement.Invoices, x => x.Kind == FeeMilestoneKinds.Advance);
      Assert.Equal("QAR", advance.Currency);
      Assert.Equal("20.000000", advance.CashApplied);
      Assert.Equal("80.000000", advance.OutstandingAmount);
      var balance = Assert.Single(agreement.Invoices, x => x.Kind == FeeMilestoneKinds.Balance);
      Assert.Equal("QAR", balance.Currency);
      Assert.Equal("30.000000", balance.CashApplied);
      Assert.Equal("5.000000", balance.CreditApplied);
      Assert.Equal("65.000000", balance.OutstandingAmount);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var receipts = await ClientPortalFinanceQuery.GetReceiptsAsync(db, actor, rows.AgreementId);
      Assert.True(receipts.Succeeded, receipts.Message);
      Assert.Equal(2, receipts.Value!.Receipts.Count);
      Assert.Equal(rows.ReceiptIds.OrderBy(x => x), receipts.Value.Receipts.Select(x => x.ReceiptId).OrderBy(x => x));
      Assert.All(receipts.Value.Receipts, receipt => Assert.Equal("QAR", receipt.Currency));
    }
  }

  private sealed record PortalFinanceProjectionRows(Guid AgreementId, Guid[] ReceiptIds);

  private static async Task<PortalFinanceProjectionRows> SeedPortalFinanceProjectionAsync(
    PgTestSchema pg, PbcSeed.Fixture fixture, string scenario)
  {
    // Synthetic persisted rows exercise the read projection only; this fixture asserts no commercial approval path.
    var now = DateTimeOffset.UtcNow;
    const string agreementCurrency = "QAR";
    var accountCurrency = scenario == "legacy-null-account-mismatch" ? "USD" : "QAR";
    var agreementId = Guid.CreateVersion7();
    var accountId = Guid.CreateVersion7();
    var leadId = Guid.CreateVersion7();
    var opportunityId = Guid.CreateVersion7();
    var proposalId = Guid.CreateVersion7();
    var invoiceIds = scenario == "legacy-null"
      ? new[] { Guid.CreateVersion7(), Guid.CreateVersion7() }
      : new[] { Guid.CreateVersion7() };
    var invoiceCurrencies = scenario switch
    {
      "invoice-account" => new string?[] { "USD" },
      "legacy-null-account-mismatch" => new string?[] { null },
      "legacy-null" => new string?[] { null, "QAR" },
      _ => new string?[] { "QAR" }
    };
    var receiptIds = new List<Guid>();
    var invoices = new List<Invoice>();
    var milestones = new List<FeeMilestone>();
    var receipts = new List<Receipt>();
    var allocations = new List<ReceiptAllocation>();

    for (var index = 0; index < invoiceIds.Length; index++)
    {
      var invoiceId = invoiceIds[index];
      if (scenario != "missing-invoice")
      {
        invoices.Add(new Invoice
        {
          Id = invoiceId, FirmId = fixture.FirmId, BillingAccountId = accountId,
          InvoiceNumber = $"SYN-PORTAL-CURRENCY-{scenario}-{index}", Currency = invoiceCurrencies[index],
          Subtotal = 100m, Total = 100m, Status = BillingStates.InvoicePosted,
          CreatedByUserId = fixture.Admin.Id, PostedAt = now, CreatedAt = now
        });
      }

      milestones.Add(new FeeMilestone
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, AgreementId = agreementId,
        Kind = index == 0 ? FeeMilestoneKinds.Advance : FeeMilestoneKinds.Balance,
        Amount = 100m, State = FeeMilestoneStates.Invoiced, InvoiceId = invoiceId, CreatedAt = now
      });
    }

    var receiptCurrencies = scenario switch
    {
      "receipt-account" => new string?[] { "USD" },
      "legacy-null-account-mismatch" => new string?[] { null },
      "legacy-null" => new string?[] { null, "QAR" },
      _ => Array.Empty<string?>()
    };
    var receiptAmounts = scenario == "legacy-null" ? new[] { 20m, 30m } : new[] { 20m };
    for (var index = 0; index < receiptCurrencies.Length; index++)
    {
      var receiptId = Guid.CreateVersion7();
      receiptIds.Add(receiptId);
      receipts.Add(new Receipt
      {
        Id = receiptId, FirmId = fixture.FirmId, BillingAccountId = accountId,
        Amount = receiptAmounts[index], Currency = receiptCurrencies[index],
        Reference = $"SYN-PORTAL-CURRENCY-RECEIPT-{scenario}-{index}",
        Status = BillingStates.ReceiptRecorded, RecordedByUserId = fixture.Admin.Id, ReceivedAt = now
      });
      allocations.Add(new ReceiptAllocation
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ReceiptId = receiptId,
        InvoiceId = invoiceIds[index], Amount = receiptAmounts[index], CreatedAt = now
      });
    }

    await using var db = new AuditSphereDbContext(pg.Options);
    db.BillingAccounts.Add(new BillingAccount
    {
      Id = accountId, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
      Currency = accountCurrency, CreatedAt = now
    });
    db.Leads.Add(new Lead
    {
      Id = leadId, FirmId = fixture.FirmId, Name = "Synthetic portal currency lead",
      Source = "TEST", Status = CrmStates.LeadQualified, CreatedAt = now
    });
    db.Opportunities.Add(new Opportunity
    {
      Id = opportunityId, FirmId = fixture.FirmId, LeadId = leadId, PracticeClientId = fixture.ClientId,
      ServiceRoute = "FinancialStatementAudit", EntityScope = "Synthetic test scope",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ExpectedFee = 200m,
      Currency = agreementCurrency, Stage = CrmStates.OpportunityProposal, CreatedAt = now
    });
    db.Proposals.Add(new Proposal
    {
      Id = proposalId, FirmId = fixture.FirmId, OpportunityId = opportunityId, PracticeClientId = fixture.ClientId,
      Status = CrmStates.ProposalDraft, ServiceProfileId = "SYNTHETIC", Scope = "Projection fixture",
      Exclusions = "None", Deliverables = "Projection fixture", Dependencies = "None",
      Fee = 200m, Currency = agreementCurrency, PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      CreatedAt = now
    });
    db.EngagementFeeAgreements.Add(new EngagementFeeAgreement
    {
      Id = agreementId, FirmId = fixture.FirmId, ProposalId = proposalId,
      QuotationVersionId = Guid.CreateVersion7(), PracticeClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, Currency = agreementCurrency, AgreedFee = 200m,
      AdvancePercent = 50m, CreatedByUserId = fixture.Admin.Id, CreatedAt = now
    });
    db.Invoices.AddRange(invoices);
    db.FeeMilestones.AddRange(milestones);
    db.Receipts.AddRange(receipts);
    db.ReceiptAllocations.AddRange(allocations);
    if (scenario is "credit-account" or "legacy-null")
    {
      var creditInvoiceIndex = scenario == "legacy-null" ? 1 : 0;
      db.CreditNotes.Add(new CreditNote
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, BillingAccountId = accountId,
        InvoiceId = invoiceIds[creditInvoiceIndex], NoteNumber = $"SYN-PORTAL-CURRENCY-CREDIT-{scenario}",
        Currency = scenario == "legacy-null" ? "QAR" : "USD",
        Amount = scenario == "legacy-null" ? 5m : 10m,
        Reason = scenario == "legacy-null" ? "Synthetic matching-currency credit" : "Synthetic wrong-currency persisted credit",
        Status = BillingStates.CreditIssued,
        CreatedByUserId = fixture.Admin.Id, CreatedAt = now
      });
    }
    await db.SaveChangesAsync();
    return new(agreementId, receiptIds.ToArray());
  }

  [Fact]
  public async Task PortalFinanceAndReceiptDownloads_RespectExactClientAndEngagementScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Agreement A goes through the real acceptance → conversion → agreement path.
    var (_, _, proposalA) = await DraftProposalAsync(pg, w);
    var quoteA = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalA));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quoteA.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalA)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalA);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalA)).Succeeded);
    var offerA = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalA)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalA,
      new("ACCEPTED", null, offerA, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalA, "Scoped Group Client"));
    Assert.True(converted.Succeeded, converted.Message);
    var clientId = converted.Value;
    var agreementA = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalA);
    Assert.True(agreementA.Succeeded, agreementA.Message);

    // A second distinct lead supplies the proposal identity for the sibling engagement's agreement.
    var leadB = await PracticeCrmService.CreateLeadAsync(db, w.Prep, new CreateLeadRequest("Scoped Sibling Lead", "Referral", "B. Owner", "sibling@gulf.example.test"));
    Assert.True(leadB.Succeeded, leadB.Message);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, leadB.Value)).Succeeded);
    var opportunityB = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep, new CreateOpportunityRequest(leadB.Value, "FinancialStatementAudit",
      "SCOPED-SIBLING", "2026-01-01", "2026-12-31", 30000m, "QAR", 60m));
    Assert.True(opportunityB.Succeeded, opportunityB.Message);
    var proposalB = await PracticeCrmService.ReviseProposalAsync(db, w.Prep, new ReviseProposalRequest(opportunityB.Value, "AUDIT-2026-B",
      "Statutory audit of the sibling engagement", "Tax advisory", "Independent auditor's report and management letter",
      "Client supplies the trial balance", 1m, "QAR", "2026-01-01", "2026-12-31"));
    Assert.True(proposalB.Succeeded, proposalB.Message);
    // This synthetic sibling source is already paired with the existing client below; keep both persisted lineage links consistent.
    var siblingProposal = await db.Proposals.SingleAsync(x => x.FirmId == w.FirmId && x.Id == proposalB.Value);
    siblingProposal.PracticeClientId = clientId;
    await db.SaveChangesAsync();

    var engagementA = Guid.CreateVersion7();
    var engagementB = Guid.CreateVersion7();
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = w.FirmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = w.FirmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    Assert.True((await FeeAgreementService.LinkEngagementAsync(db, w.Prep, agreementA.Value, engagementA)).Succeeded);

    var agreementB = new EngagementFeeAgreement
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = proposalB.Value, QuotationVersionId = Guid.CreateVersion7(),
      PracticeClientId = clientId, EngagementId = engagementB, Currency = "QAR", AgreedFee = 30000m,
      AdvancePercent = 50m, CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
    };
    db.EngagementFeeAgreements.Add(agreementB);
    // Agreement A's advance/balance milestones already exist from the real agreement command.
    var milestoneA = await db.FeeMilestones.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.AgreementId == agreementA.Value && x.Kind == FeeMilestoneKinds.Advance);
    var mismatchMilestone = await db.FeeMilestones.AsNoTracking().SingleAsync(x =>
      x.FirmId == w.FirmId && x.AgreementId == agreementA.Value && x.Kind == FeeMilestoneKinds.Balance);
    var milestoneB = new FeeMilestone { Id = Guid.CreateVersion7(), FirmId = w.FirmId, AgreementId = agreementB.Id, Kind = FeeMilestoneKinds.Advance, Amount = 15000m, CreatedAt = DateTimeOffset.UtcNow };
    var legacyMilestone = new FeeMilestone { Id = Guid.CreateVersion7(), FirmId = w.FirmId, AgreementId = agreementB.Id, Kind = FeeMilestoneKinds.Balance, Amount = 15000m, CreatedAt = DateTimeOffset.UtcNow };
    db.FeeMilestones.AddRange(milestoneB, legacyMilestone);

    CommercialDocument Receipt(FeeMilestone milestone, Guid proposalId, Guid quotationVersionId) => new()
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = proposalId, QuotationVersionId = quotationVersionId,
      FeeMilestoneId = milestone.Id, Kind = CommercialDocumentKinds.PaymentReceipt,
      TemplateVersion = "receipt-v1", ProfileVersion = 1, FileName = $"receipt-{milestone.Id:N}.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = [1, 2, 3], Sha256Hex = new string('a', 64),
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    var receiptA = Receipt(milestoneA, proposalA, quoteA.Value);
    var receiptB = Receipt(milestoneB, proposalB.Value, agreementB.QuotationVersionId);
    var mismatchedReceipt = Receipt(mismatchMilestone, proposalB.Value, agreementB.QuotationVersionId);
    var legacyReceipt = new CommercialDocument
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, FeeMilestoneId = legacyMilestone.Id, Kind = CommercialDocumentKinds.PaymentReceipt,
      TemplateVersion = "receipt-v1", ProfileVersion = 1, FileName = $"legacy-receipt-{legacyMilestone.Id:N}.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = [7, 8, 9], Sha256Hex = new string('c', 64),
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    // An internal commercial artifact (comprehensive proposal) is never a client-downloadable document.
    var internalDoc = new CommercialDocument
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, FeeMilestoneId = milestoneA.Id, Kind = CommercialDocumentKinds.ComprehensiveProposal,
      QuotationVersionId = Guid.CreateVersion7(), TemplateVersion = "tender-v1", ProfileVersion = 1, FileName = "proposal.docx",
      ContentType = CommercialDocumentRenderer.DocxContentType, Bytes = [4, 5, 6], Sha256Hex = new string('b', 64),
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    db.CommercialDocuments.AddRange(receiptA, receiptB, mismatchedReceipt, legacyReceipt, internalDoc);

    var engagementScopedUser = User(w.FirmId, "engagement-scoped-client");
    engagementScopedUser.UserKind = "Client";
    var clientWideUser = User(w.FirmId, "client-wide-client");
    clientWideUser.UserKind = "Client";
    var financeSnapshotUser = User(w.FirmId, "finance-snapshot-client");
    financeSnapshotUser.UserKind = "Client";
    var receiptSnapshotUser = User(w.FirmId, "receipt-snapshot-client");
    receiptSnapshotUser.UserKind = "Client";
    var nullClientUser = User(w.FirmId, "null-client-grant");
    nullClientUser.UserKind = "Client";
    db.Users.AddRange(engagementScopedUser, clientWideUser, financeSnapshotUser, receiptSnapshotUser, nullClientUser);
    db.RoleGrants.AddRange(
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = engagementScopedUser.Id, Role = "ClientUser", ClientId = clientId, EngagementId = engagementA, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = clientWideUser.Id, Role = "ClientUser", ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = financeSnapshotUser.Id, Role = "ClientUser", ClientId = clientId, EngagementId = engagementA, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = receiptSnapshotUser.Id, Role = "ClientUser", ClientId = clientId, EngagementId = engagementA, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id },
      new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = nullClientUser.Id, Role = "ClientUser", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.PartnerUser.Id });
    await db.SaveChangesAsync();

    var engagementScopedActor = Actor(engagementScopedUser, "ClientUser");
    var clientWideActor = Actor(clientWideUser, "ClientUser");
    var nullClientActor = Actor(nullClientUser, "ClientUser");
    var receiptSnapshotActor = Actor(receiptSnapshotUser, "ClientUser");

    // The engagement-scoped grant sees exactly its engagement's agreement — never the sibling.
    var scopedFinance = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor);
    Assert.True(scopedFinance.Succeeded, scopedFinance.Message);
    var scopedAgreement = Assert.Single(scopedFinance.Value!.Agreements);
    Assert.Equal(agreementA.Value, scopedAgreement.AgreementId);
    Assert.Equal(engagementA, scopedAgreement.EngagementId);
    Assert.StartsWith("25000", scopedAgreement.AgreedFee, StringComparison.Ordinal);

    // Its own engagement's official receipt downloads; the sibling engagement's does not.
    var scopedReceipt = await CommercialDocumentService.GetAsync(db, engagementScopedActor, receiptA.Id);
    Assert.True(scopedReceipt.Succeeded, scopedReceipt.Message);
    Assert.NotEmpty(scopedReceipt.Value!.Bytes);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, engagementScopedActor, receiptB.Id)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, engagementScopedActor, legacyReceipt.Id)).ErrorCode);
    // Internal commercial artifacts are never client-downloadable.
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, engagementScopedActor, internalDoc.Id)).ErrorCode);

    // The client-wide grant covers both engagements of its client but still never internal artifacts.
    var wideFinance = await ClientPortalFinanceQuery.GetAsync(db, clientWideActor);
    Assert.True(wideFinance.Succeeded, wideFinance.Message);
    Assert.Equal(2, wideFinance.Value!.Agreements.Count);
    var wideReceipt = await CommercialDocumentService.GetAsync(db, clientWideActor, receiptB.Id);
    Assert.True(wideReceipt.Succeeded, wideReceipt.Message);
    Assert.NotEmpty(wideReceipt.Value!.Bytes);
    var legacyRead = await CommercialDocumentService.GetAsync(db, clientWideActor, legacyReceipt.Id);
    Assert.True(legacyRead.Succeeded, legacyRead.Message);
    Assert.NotEmpty(legacyRead.Value!.Bytes);
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, clientWideActor, internalDoc.Id)).ErrorCode);
    var mismatched = await CommercialDocumentService.GetAsync(db, clientWideActor, mismatchedReceipt.Id);
    Assert.False(mismatched.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, mismatched.ErrorCode);
    Assert.Null(mismatched.Value);

    // Newer sibling agreements must not consume the bounded result window before the exact engagement grant is applied.
    var siblingDecoys = Enumerable.Range(1, 51).Select(index =>
    {
      var proposalId = Guid.CreateVersion7();
      var createdAt = DateTimeOffset.UtcNow.AddMinutes(10).AddSeconds(index);
      return (
        Proposal: new Proposal
        {
          Id = proposalId, FirmId = w.FirmId, OpportunityId = opportunityB.Value, PracticeClientId = clientId,
          Revision = index + 1, Status = CrmStates.ProposalSuperseded, ServiceProfileId = "SYNTHETIC-OVERFLOW",
          Scope = "Synthetic scope", Exclusions = "None for this synthetic overflow fixture.",
          Deliverables = "Synthetic overflow fixture deliverable.", Dependencies = "None for this synthetic overflow fixture.", Fee = 30000m,
          Currency = "QAR", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = createdAt
        },
        Agreement: new EngagementFeeAgreement
        {
          Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = proposalId,
          QuotationVersionId = Guid.CreateVersion7(), PracticeClientId = clientId, EngagementId = engagementB,
          Currency = "QAR", AgreedFee = 30000m, AdvancePercent = 50m,
          CreatedByUserId = w.PartnerUser.Id, CreatedAt = createdAt
        });
    }).ToArray();
    db.Proposals.AddRange(siblingDecoys.Select(x => x.Proposal));
    db.EngagementFeeAgreements.AddRange(siblingDecoys.Select(x => x.Agreement));
    await db.SaveChangesAsync();

    // A second same-firm billing account supplies valid-looking foreign links. Persisted receipt/credit
    // rows may point to the same-firm invoice by FK, but their account must still match that invoice.
    var clientBillingAccountId = Guid.CreateVersion7();
    var ownedClient = await db.PracticeClients.SingleAsync(x => x.FirmId == w.FirmId && x.Id == clientId);
    ownedClient.BillingAccountId = clientBillingAccountId;
    db.BillingAccounts.Add(new BillingAccount
    {
      Id = clientBillingAccountId, FirmId = w.FirmId, PracticeClientId = clientId,
      Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var foreignClient = new PracticeClient
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, LegalName = "Synthetic sibling billing account owner",
      Status = CrmStates.ClientProspect, CreatedAt = DateTimeOffset.UtcNow
    };
    var foreignBillingAccount = new BillingAccount
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, PracticeClientId = foreignClient.Id,
      Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
    };
    db.PracticeClients.Add(foreignClient);
    await db.SaveChangesAsync();
    db.BillingAccounts.Add(foreignBillingAccount);
    await db.SaveChangesAsync();

    var financialFixtureTicks = DateTimeOffset.UtcNow.UtcTicks;
    var financialFixtureAt = new DateTimeOffset(financialFixtureTicks - financialFixtureTicks % 10, TimeSpan.Zero);
    var ownBalanceInvoice = new Invoice
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = clientBillingAccountId,
      InvoiceNumber = $"SYN-PORTAL-OWN-{Guid.NewGuid():N}", Currency = "QAR", Subtotal = 1000m, Total = 1000m,
      Status = BillingStates.InvoicePosted, CreatedByUserId = w.PartnerUser.Id,
      PostedAt = financialFixtureAt.AddDays(-2), CreatedAt = financialFixtureAt.AddDays(-3)
    };
    var foreignInvoice = new Invoice
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = foreignBillingAccount.Id,
      InvoiceNumber = $"SYN-PORTAL-FOREIGN-{Guid.NewGuid():N}", Currency = "QAR", Subtotal = 500m, Total = 500m,
      Status = BillingStates.InvoicePosted, CreatedByUserId = w.PartnerUser.Id,
      PostedAt = financialFixtureAt.AddDays(-2), CreatedAt = financialFixtureAt.AddDays(-3)
    };
    var ownReceipt = new Receipt
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = clientBillingAccountId,
      Amount = 200m, Currency = "QAR", Reference = $"SYN-PORTAL-OWN-{Guid.NewGuid():N}",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = w.PartnerUser.Id, ReceivedAt = financialFixtureAt.AddDays(-1)
    };
    var foreignReceipt = new Receipt
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = foreignBillingAccount.Id,
      Amount = 70m, Currency = "QAR", Reference = $"SYN-PORTAL-FOREIGN-{Guid.NewGuid():N}",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = w.PartnerUser.Id, ReceivedAt = financialFixtureAt.AddDays(-1)
    };
    var ownCredit = new CreditNote
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = clientBillingAccountId,
      InvoiceId = ownBalanceInvoice.Id, NoteNumber = $"SYN-PORTAL-OWN-{Guid.NewGuid():N}", Currency = "QAR",
      Amount = 100m, Reason = "Synthetic same-account credit", Status = BillingStates.CreditIssued,
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = financialFixtureAt
    };
    var foreignCredit = new CreditNote
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = foreignBillingAccount.Id,
      InvoiceId = ownBalanceInvoice.Id, NoteNumber = $"SYN-PORTAL-FOREIGN-{Guid.NewGuid():N}", Currency = "QAR",
      Amount = 40m, Reason = "Synthetic cross-account credit fixture", Status = BillingStates.CreditIssued,
      CreatedByUserId = w.PartnerUser.Id, CreatedAt = financialFixtureAt
    };
    db.Invoices.AddRange(ownBalanceInvoice, foreignInvoice);
    db.Receipts.AddRange(ownReceipt, foreignReceipt);
    db.CreditNotes.AddRange(ownCredit, foreignCredit);
    await db.SaveChangesAsync();
    await db.FeeMilestones.Where(x => x.FirmId == w.FirmId && x.Id == mismatchMilestone.Id)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.InvoiceId, ownBalanceInvoice.Id)
        .SetProperty(x => x.State, FeeMilestoneStates.Invoiced));
    db.ReceiptAllocations.AddRange(
      new ReceiptAllocation
      {
        Id = Guid.CreateVersion7(), FirmId = w.FirmId, ReceiptId = ownReceipt.Id,
        InvoiceId = ownBalanceInvoice.Id, Amount = 150m, CreatedAt = financialFixtureAt
      },
      new ReceiptAllocation
      {
        Id = Guid.CreateVersion7(), FirmId = w.FirmId, ReceiptId = foreignReceipt.Id,
        InvoiceId = ownBalanceInvoice.Id, Amount = 70m, CreatedAt = financialFixtureAt
      });
    await db.SaveChangesAsync();

    // Fifty additional one-unit receipts and the existing 150-unit allocation make a 51-row
    // agreement receipt history. Equal persisted receipt times exercise the full UUID tie-break.
    var additionalReceipts = Enumerable.Range(1, 50).Select(index => new Receipt
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = clientBillingAccountId,
      Amount = 1m, Currency = "QAR", Reference = $"SYN-PORTAL-PAGE-{index:D2}-{Guid.NewGuid():N}",
      Status = BillingStates.ReceiptRecorded, RecordedByUserId = w.PartnerUser.Id, ReceivedAt = ownReceipt.ReceivedAt
    }).ToArray();
    db.Receipts.AddRange(additionalReceipts);
    await db.SaveChangesAsync();
    db.ReceiptAllocations.AddRange(additionalReceipts.Select(receipt => new ReceiptAllocation
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ReceiptId = receipt.Id,
      InvoiceId = ownBalanceInvoice.Id, Amount = 1m, CreatedAt = financialFixtureAt
    }));
    await db.SaveChangesAsync();

    var retainedReceiptBytes = receiptA.Bytes.ToArray();
    var retainedReceiptSha256 = receiptA.Sha256Hex;

    // Equal persisted timestamps below one millisecond force the opaque cursor to continue through
    // the UUID tie-break without rounding or skipping an authorized agreement.
    var agreementACreatedAt = await db.EngagementFeeAgreements.AsNoTracking()
      .Where(x => x.FirmId == w.FirmId && x.Id == agreementA.Value).Select(x => x.CreatedAt).SingleAsync();
    var agreementTicks = agreementACreatedAt.UtcTicks;
    var priorMillisecondTicks = agreementTicks - agreementTicks % TimeSpan.TicksPerMillisecond - TimeSpan.TicksPerMillisecond;
    var tiedCreatedAt = new DateTimeOffset(priorMillisecondTicks + 1_230, TimeSpan.Zero);
    Assert.Equal(0L, tiedCreatedAt.UtcTicks % 10);
    Assert.Equal(1_230L, tiedCreatedAt.UtcTicks % TimeSpan.TicksPerMillisecond);
    var authorizedDecoys = Enumerable.Range(1, 51).Select(index =>
    {
      var proposalId = Guid.CreateVersion7();
      return (
        Proposal: new Proposal
        {
          Id = proposalId, FirmId = w.FirmId, OpportunityId = opportunityB.Value, PracticeClientId = clientId,
          Revision = index + 52, Status = CrmStates.ProposalSuperseded, ServiceProfileId = "SYNTHETIC-AUTHORIZED-PAGE",
          Scope = "Synthetic authorized paging scope", Exclusions = "No exclusions in synthetic paging fixture.",
          Deliverables = "Synthetic authorized paging deliverable.", Dependencies = "No dependencies in synthetic paging fixture.",
          Fee = 30000m, Currency = "QAR", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = tiedCreatedAt
        },
        Agreement: new EngagementFeeAgreement
        {
          Id = Guid.CreateVersion7(), FirmId = w.FirmId, ProposalId = proposalId,
          QuotationVersionId = Guid.CreateVersion7(), PracticeClientId = clientId, EngagementId = engagementA,
          Currency = "QAR", AgreedFee = 30000m, AdvancePercent = 50m,
          CreatedByUserId = w.PartnerUser.Id, CreatedAt = tiedCreatedAt
        });
    }).ToArray();
    db.Proposals.AddRange(authorizedDecoys.Select(x => x.Proposal));
    db.EngagementFeeAgreements.AddRange(authorizedDecoys.Select(x => x.Agreement));
    await db.SaveChangesAsync();

    var sentAt = financialFixtureAt.AddHours(1);
    var snapshotInterleaver = new SetInvoiceSentAtAfterFinanceAgreementRead(pg, w.FirmId, ownBalanceInvoice.Id, sentAt);
    var pageOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(snapshotInterleaver).Options;
    CommandResult<PortalClientFinanceView> firstPage;
    await using (var pageReader = new AuditSphereDbContext(pageOptions))
      firstPage = await ClientPortalFinanceQuery.GetAsync(pageReader, engagementScopedActor, 50, null);
    Assert.True(firstPage.Succeeded, firstPage.Message);
    Assert.Equal(50, firstPage.Value!.PageSize);
    Assert.True(firstPage.Value.HasMore);
    Assert.False(string.IsNullOrWhiteSpace(firstPage.Value.NextCursor));
    Assert.Equal(1, snapshotInterleaver.Calls);
    Assert.Equal(agreementA.Value, firstPage.Value.Agreements[0].AgreementId);
    var agreementAView = Assert.Single(firstPage.Value.Agreements, x => x.AgreementId == agreementA.Value);
    Assert.Equal(50, agreementAView.Receipts.Count);
    Assert.True(agreementAView.HasMoreReceipts);
    Assert.False(string.IsNullOrWhiteSpace(agreementAView.NextReceiptCursor));
    var balanceInvoiceView = Assert.Single(agreementAView.Invoices, x => x.InvoiceId == ownBalanceInvoice.Id);
    Assert.Null(balanceInvoiceView.SentAt); // The invoice read remained in the original repeatable-read snapshot.
    Assert.Equal(200m, decimal.Parse(balanceInvoiceView.CashApplied, CultureInfo.InvariantCulture));
    Assert.Equal(100m, decimal.Parse(balanceInvoiceView.CreditApplied, CultureInfo.InvariantCulture));
    Assert.Equal(700m, decimal.Parse(balanceInvoiceView.OutstandingAmount, CultureInfo.InvariantCulture));
    Assert.DoesNotContain(agreementAView.Receipts, x => x.Reference == foreignReceipt.Reference);
    await using (var verifySnapshot = new AuditSphereDbContext(pg.Options))
      Assert.Equal(sentAt, await verifySnapshot.Invoices.AsNoTracking().Where(x => x.Id == ownBalanceInvoice.Id)
        .Select(x => x.SentAt).SingleAsync());

    var secondPage = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor, 50, firstPage.Value.NextCursor);
    Assert.True(secondPage.Succeeded, secondPage.Message);
    Assert.Equal(50, secondPage.Value!.PageSize);
    Assert.False(secondPage.Value.HasMore);
    Assert.Null(secondPage.Value.NextCursor);
    Assert.Equal(2, secondPage.Value.Agreements.Count);
    Assert.Equal(49, firstPage.Value.Agreements.Count(x => authorizedDecoys.Any(d => d.Agreement.Id == x.AgreementId)));
    Assert.Equal(2, secondPage.Value.Agreements.Count(x => authorizedDecoys.Any(d => d.Agreement.Id == x.AgreementId)));
    var actualPagedIds = firstPage.Value.Agreements.Concat(secondPage.Value.Agreements).Select(x => x.AgreementId).ToArray();
    var expectedPagedIds = new[] { (Id: agreementA.Value, CreatedAt: agreementACreatedAt) }
      .Concat(authorizedDecoys.Select(x => (Id: x.Agreement.Id, CreatedAt: x.Agreement.CreatedAt)))
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Select(x => x.Id).ToArray();
    Assert.Equal(expectedPagedIds, actualPagedIds);
    Assert.Equal(actualPagedIds.Length, actualPagedIds.Distinct().Count());
    Assert.Empty(actualPagedIds.Intersect(siblingDecoys.Select(x => x.Agreement.Id)));

    var receiptFirstPage = await ClientPortalFinanceQuery.GetReceiptsAsync(db, engagementScopedActor, agreementA.Value);
    Assert.True(receiptFirstPage.Succeeded, receiptFirstPage.Message);
    var firstReceiptPageValue = receiptFirstPage.Value!;
    Assert.Equal(agreementA.Value, firstReceiptPageValue.AgreementId);
    Assert.Equal(50, firstReceiptPageValue.PageSize);
    Assert.Equal(50, firstReceiptPageValue.Receipts.Count);
    Assert.True(firstReceiptPageValue.HasMore);
    Assert.False(string.IsNullOrWhiteSpace(firstReceiptPageValue.NextCursor));
    var receiptSecondPage = await ClientPortalFinanceQuery.GetReceiptsAsync(db, engagementScopedActor, agreementA.Value,
      50, firstReceiptPageValue.NextCursor);
    Assert.True(receiptSecondPage.Succeeded, receiptSecondPage.Message);
    var secondReceiptPageValue = receiptSecondPage.Value!;
    Assert.Equal(agreementA.Value, secondReceiptPageValue.AgreementId);
    Assert.Equal(50, secondReceiptPageValue.PageSize);
    Assert.Single(secondReceiptPageValue.Receipts);
    Assert.False(secondReceiptPageValue.HasMore);
    Assert.Null(secondReceiptPageValue.NextCursor);
    var actualReceiptRows = firstReceiptPageValue.Receipts.Concat(secondReceiptPageValue.Receipts)
      .Select(x => (x.ReceiptId, x.Kind, x.ReceivedAt)).ToArray();
    var expectedReceiptRows = new[] { ownReceipt }.Concat(additionalReceipts)
      .Select(x => (ReceiptId: x.Id, Kind: FeeMilestoneKinds.Balance, x.ReceivedAt))
      .OrderByDescending(x => x.ReceivedAt).ThenByDescending(x => x.ReceiptId)
      .ThenBy(x => x.Kind, StringComparer.Ordinal).ToArray();
    Assert.Equal(expectedReceiptRows, actualReceiptRows);
    Assert.Equal(actualReceiptRows.Length, actualReceiptRows.Select(x => x.ReceiptId).Distinct().Count());
    Assert.DoesNotContain(actualReceiptRows, x => x.ReceiptId == foreignReceipt.Id);
    Assert.Equal(expectedReceiptRows.Take(50).Select(x => x.ReceiptId), agreementAView.Receipts.Select(x => x.ReceiptId));
    Assert.Contains(firstReceiptPageValue.Receipts.Concat(secondReceiptPageValue.Receipts), x =>
      x.Reference == ownReceipt.Reference && decimal.Parse(x.AppliedAmount, CultureInfo.InvariantCulture) == 150m);

    var advanceInvoice = new Invoice
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, BillingAccountId = clientBillingAccountId,
      InvoiceNumber = $"SYN-PORTAL-ADVANCE-{Guid.NewGuid():N}", Currency = "QAR", Subtotal = 1000m, Total = 1000m,
      Status = BillingStates.InvoicePosted, CreatedByUserId = w.PartnerUser.Id,
      PostedAt = financialFixtureAt.AddDays(-2), CreatedAt = financialFixtureAt.AddDays(-3)
    };
    db.Invoices.Add(advanceInvoice);
    await db.SaveChangesAsync();
    await db.FeeMilestones.Where(x => x.FirmId == w.FirmId && x.Id == milestoneA.Id)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.InvoiceId, advanceInvoice.Id)
        .SetProperty(x => x.State, FeeMilestoneStates.Invoiced));
    db.ReceiptAllocations.Add(new ReceiptAllocation
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, ReceiptId = ownReceipt.Id,
      InvoiceId = advanceInvoice.Id, Amount = 50m, CreatedAt = financialFixtureAt
    });
    await db.SaveChangesAsync();
    var receiptAcrossMilestonesPage = await ClientPortalFinanceQuery.GetReceiptsAsync(db, engagementScopedActor, agreementA.Value);
    Assert.True(receiptAcrossMilestonesPage.Succeeded, receiptAcrossMilestonesPage.Message);
    var receiptAcrossMilestonesPageValue = receiptAcrossMilestonesPage.Value!;
    Assert.Equal(50, receiptAcrossMilestonesPageValue.Receipts.Count);
    Assert.True(receiptAcrossMilestonesPageValue.HasMore);
    var receiptAcrossMilestonesContinuation = await ClientPortalFinanceQuery.GetReceiptsAsync(db, engagementScopedActor,
      agreementA.Value, 50, receiptAcrossMilestonesPageValue.NextCursor);
    Assert.True(receiptAcrossMilestonesContinuation.Succeeded, receiptAcrossMilestonesContinuation.Message);
    var receiptAcrossMilestonesContinuationValue = receiptAcrossMilestonesContinuation.Value!;
    Assert.Equal(2, receiptAcrossMilestonesContinuationValue.Receipts.Count);
    Assert.False(receiptAcrossMilestonesContinuationValue.HasMore);
    var sameReceiptRows = receiptAcrossMilestonesContinuationValue.Receipts;
    Assert.All(sameReceiptRows, x => Assert.Equal(ownReceipt.Id, x.ReceiptId));
    Assert.Equal(new[] { FeeMilestoneKinds.Advance, FeeMilestoneKinds.Balance }, sameReceiptRows.Select(x => x.Kind));
    Assert.Equal(2, sameReceiptRows.Select(x => (x.Kind, x.ReceiptId)).Distinct().Count());
    Assert.Equal(50m, decimal.Parse(sameReceiptRows.Single(x => x.Kind == FeeMilestoneKinds.Advance).AppliedAmount, CultureInfo.InvariantCulture));
    Assert.Equal(150m, decimal.Parse(sameReceiptRows.Single(x => x.Kind == FeeMilestoneKinds.Balance).AppliedAmount, CultureInfo.InvariantCulture));

    var mismatchedReceiptCursor = await ClientPortalFinanceQuery.GetReceiptsAsync(db, clientWideActor, agreementB.Id,
      50, firstReceiptPageValue.NextCursor);
    Assert.Equal("request.invalid", mismatchedReceiptCursor.ErrorCode);
    Assert.Null(mismatchedReceiptCursor.Value);
    var deniedSiblingReceipts = await ClientPortalFinanceQuery.GetReceiptsAsync(db, engagementScopedActor, agreementB.Id);
    Assert.Equal(ErrorCodes.ScopeDenied, deniedSiblingReceipts.ErrorCode);
    Assert.Null(deniedSiblingReceipts.Value);

    await using (var receiptCallerTransaction = await db.Database.BeginTransactionAsync())
    {
      var refusedInCallerTransaction = await ClientPortalFinanceQuery.GetReceiptsAsync(db, engagementScopedActor, agreementA.Value);
      Assert.Equal(ErrorCodes.GateBlocked, refusedInCallerTransaction.ErrorCode);
      Assert.Null(refusedInCallerTransaction.Value);
      Assert.Same(receiptCallerTransaction, db.Database.CurrentTransaction);
      Assert.Equal(1, await db.RoleGrants.CountAsync(x => x.FirmId == w.FirmId && x.UserId == engagementScopedUser.Id && x.RevokedAt == null));
      await receiptCallerTransaction.CommitAsync();
    }

    var receiptGrant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId &&
      x.UserId == receiptSnapshotUser.Id && x.Role == "ClientUser" && x.ClientId == clientId && x.EngagementId == engagementA);
    var receiptSnapshotPositive = await ClientPortalFinanceQuery.GetReceiptsAsync(db, receiptSnapshotActor, agreementA.Value);
    Assert.True(receiptSnapshotPositive.Succeeded, receiptSnapshotPositive.Message);
    Assert.Equal(50, receiptSnapshotPositive.Value!.Receipts.Count);
    var receiptGrantInterleaver = new RevokeFinanceGrantAfterReceiptPageRead(pg, w.FirmId, receiptGrant.Id);
    var receiptGrantOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(receiptGrantInterleaver).Options;
    CommandResult<PortalClientFinanceReceiptPage> revokedDuringReceiptRead;
    await using (var receiptGrantReader = new AuditSphereDbContext(receiptGrantOptions))
      revokedDuringReceiptRead = await ClientPortalFinanceQuery.GetReceiptsAsync(receiptGrantReader, receiptSnapshotActor, agreementA.Value);
    Assert.Equal(1, receiptGrantInterleaver.Calls);
    Assert.False(revokedDuringReceiptRead.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, revokedDuringReceiptRead.ErrorCode);
    Assert.Null(revokedDuringReceiptRead.Value);
    await using (var verifyReceiptGrantRevocation = new AuditSphereDbContext(pg.Options))
      Assert.NotNull(await verifyReceiptGrantRevocation.RoleGrants.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.Id == receiptGrant.Id)
        .Select(x => x.RevokedAt).SingleAsync());

    await using (var retainedDocRead = new AuditSphereDbContext(pg.Options))
    {
      var retainedDoc = await retainedDocRead.CommercialDocuments.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Id == receiptA.Id);
      Assert.Equal(retainedReceiptBytes, retainedDoc.Bytes);
      Assert.Equal(retainedReceiptSha256, retainedDoc.Sha256Hex);
    }

    // Seed the malformed link separately: an existing milestone's invoice identity is immutable.
    var malformedAgreement = authorizedDecoys.OrderByDescending(x => x.Agreement.Id).First().Agreement;
    db.FeeMilestones.Add(new FeeMilestone
    {
      Id = Guid.CreateVersion7(), FirmId = w.FirmId, AgreementId = malformedAgreement.Id,
      Kind = FeeMilestoneKinds.Advance, Amount = 500m, State = FeeMilestoneStates.Invoiced,
      InvoiceId = foreignInvoice.Id, CreatedAt = financialFixtureAt
    });
    await db.SaveChangesAsync();
    var foreignInvoiceRead = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor);
    Assert.False(foreignInvoiceRead.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, foreignInvoiceRead.ErrorCode);
    Assert.Null(foreignInvoiceRead.Value);

    var financeSnapshotGrant = await db.RoleGrants.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId &&
      x.UserId == financeSnapshotUser.Id && x.Role == "ClientUser" && x.ClientId == clientId && x.EngagementId == engagementA);
    var zeroSize = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor, 0, null);
    Assert.Equal("request.invalid", zeroSize.ErrorCode);
    Assert.Null(zeroSize.Value);
    var oversizedPage = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor, 51, null);
    Assert.Equal("request.invalid", oversizedPage.ErrorCode);
    Assert.Null(oversizedPage.Value);
    var malformedCursor = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor, 50, "not-a-cursor");
    Assert.Equal("request.invalid", malformedCursor.ErrorCode);
    Assert.Null(malformedCursor.Value);
    await using (var callerTransaction = await db.Database.BeginTransactionAsync())
    {
      var refusedInCallerTransaction = await ClientPortalFinanceQuery.GetAsync(db, engagementScopedActor);
      Assert.Equal(ErrorCodes.GateBlocked, refusedInCallerTransaction.ErrorCode);
      Assert.Null(refusedInCallerTransaction.Value);
      Assert.Same(callerTransaction, db.Database.CurrentTransaction);
      Assert.Equal(1, await db.RoleGrants.CountAsync(x => x.FirmId == w.FirmId && x.Id == financeSnapshotGrant.Id));
      await callerTransaction.CommitAsync();
    }

    // A null-client ClientUser grant is not a firm-wide fallback.
    Assert.Equal(ErrorCodes.ScopeDenied, (await CommercialDocumentService.GetAsync(db, nullClientActor, receiptA.Id)).ErrorCode);
    Assert.Empty((await ClientPortalFinanceQuery.GetAsync(db, nullClientActor)).Value!.Agreements);

    // These persisted receipt rows exercise both identity fences against the real dual-linked receipt shape.
    await AssertReceiptDeniedAfterIdentityChangeAsync(engagementScopedUser, engagementScopedActor, receiptA.Id, disableUser: false);
    await AssertReceiptDeniedAfterIdentityChangeAsync(clientWideUser, clientWideActor, receiptB.Id, disableUser: true);

    var grantInterleaver = new RevokeFinanceGrantAfterAgreementRead(pg, w.FirmId, financeSnapshotGrant.Id);
    var grantOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(grantInterleaver).Options;
    CommandResult<PortalClientFinanceView> revokedDuringRead;
    await using (var grantReader = new AuditSphereDbContext(grantOptions))
      revokedDuringRead = await ClientPortalFinanceQuery.GetAsync(grantReader, Actor(financeSnapshotUser, "ClientUser"), 1, null);
    Assert.Equal(1, grantInterleaver.Calls);
    Assert.False(revokedDuringRead.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, revokedDuringRead.ErrorCode);
    Assert.Null(revokedDuringRead.Value);
    await using (var verifyRevocation = new AuditSphereDbContext(pg.Options))
      Assert.NotNull(await verifyRevocation.RoleGrants.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.Id == financeSnapshotGrant.Id)
        .Select(x => x.RevokedAt).SingleAsync());

    async Task AssertReceiptDeniedAfterIdentityChangeAsync(AppUser user, ActorContext actor, Guid receiptId, bool disableUser)
    {
      var positive = await CommercialDocumentService.GetAsync(db, actor, receiptId);
      Assert.True(positive.Succeeded, positive.Message);
      Assert.NotEmpty(positive.Value!.Bytes);

      var interleaver = new ChangeClientIdentityBeforeReceiptGrantCheck(pg, w.FirmId, user.Id, disableUser);
      var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interleaver).Options;
      await using (var read = new AuditSphereDbContext(options))
      {
        var denied = await CommercialDocumentService.GetAsync(read, actor, receiptId);
        Assert.False(denied.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
        Assert.Null(denied.Value);
      }

      Assert.Equal(1, interleaver.Calls);
      await using var verify = new AuditSphereDbContext(pg.Options);
      var current = await verify.Users.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Id == user.Id);
      Assert.Equal(disableUser, current.Disabled);
      Assert.Equal(disableUser ? actor.SessionEpoch : actor.SessionEpoch + 1, current.SessionEpoch);
      Assert.Equal(1, await verify.RoleGrants.CountAsync(x => x.FirmId == w.FirmId && x.UserId == user.Id &&
        x.Role == "ClientUser" && x.ClientId == clientId && x.RevokedAt == null));
    }
  }
  /// <summary>
  /// WF-03: the readiness cards are evaluated independently from real commercial records: the client's exact-offer acceptance,
  /// the Partner's unconditional clearance (current, conditional, stale), the letter and the fee agreement. No single key opens
  /// the letter or activation, and a foreign firm learns nothing.
  /// </summary>
  [Fact]
  public async Task OnboardingReadinessEvaluatesEachKeyIndependentlyFromRealCommercialRecords()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    var quote = await QuotationService.SaveAsync(db, w.Prep, Quote(proposalId));
    Assert.True((await QuotationService.SubmitAsync(db, w.Prep, quote.Value)).Succeeded);
    Assert.True((await PracticeCrmService.ApproveProposalAsync(db, w.Partner, proposalId)).Succeeded);
    await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, w.Prep, proposalId);
    Assert.True((await PracticeCrmService.SendProposalAsync(db, w.Prep, proposalId)).Succeeded);
    var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
    Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, w.Prep, proposalId,
      new("ACCEPTED", null, offerSha, "A. Owner", "owner@gulf.example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
    var client = await PracticeCrmService.ConvertToClientDraftAsync(db, w.Prep, new(proposalId, "Readiness client"));
    Assert.True(client.Succeeded, client.Message);
    var engagementId = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = engagementId, FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "FinancialStatementAudit",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    // A newer draft on another route must not shadow the accepted offer for this engagement.
    var unrelatedLead = await PracticeCrmService.CreateLeadAsync(db, w.Prep,
      new CreateLeadRequest("Readiness accounting prospect", "Referral", "B. Owner", "accounting@example.test"));
    Assert.True(unrelatedLead.Succeeded, unrelatedLead.Message);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, w.Prep, unrelatedLead.Value)).Succeeded);
    var unrelatedOpportunity = await PracticeCrmService.CreateOpportunityAsync(db, w.Prep,
      new CreateOpportunityRequest(unrelatedLead.Value, "Accounting", "READINESS-ACCOUNTING", "2026-01-01", "2026-12-31", 12000m, "QAR", 50m));
    Assert.True(unrelatedOpportunity.Succeeded, unrelatedOpportunity.Message);
    await db.Opportunities.Where(x => x.Id == unrelatedOpportunity.Value)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.PracticeClientId, client.Value));
    var unrelatedProposal = await PracticeCrmService.ReviseProposalAsync(db, w.Prep,
      new ReviseProposalRequest(unrelatedOpportunity.Value, "ACCOUNTING-2026", "Accounting preparation", "Tax", "Statements",
        "Client supplies records", 1m, "QAR", "2026-01-01", "2026-12-31"));
    Assert.True(unrelatedProposal.Succeeded, unrelatedProposal.Message);
    var unlinkedReadiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagementId);
    Assert.True(unlinkedReadiness.Succeeded, unlinkedReadiness.Message);
    Assert.Equal(OnboardingReadinessStatuses.Current, unlinkedReadiness.Value!.ClientApproval.Status);
    Assert.Equal($"/app/practice/proposals/{proposalId}", unlinkedReadiness.Value.ClientApproval.EvidenceHref);

    var created = await FeeAgreementService.CreateAgreementAsync(db, w.Prep, proposalId);
    Assert.True(created.Succeeded, created.Message);
    // Activation reads only the agreement linked to this engagement, so the readiness advance card reads the same link.
    var linked = await FeeAgreementService.LinkEngagementAsync(db, w.Prep, created.Value, engagementId);
    Assert.True(linked.Succeeded, linked.Message);

    // Client accepted the exact offer; no Partner clearance yet. The client card is current, the Partner card is missing, and
    // the advance is not yet invoiced, so activation stays blocked.
    var readiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagementId);
    Assert.True(readiness.Succeeded, readiness.Message);
    Assert.Equal(OnboardingReadinessStatuses.Current, readiness.Value!.ClientApproval.Status);
    Assert.Equal(OnboardingDisplayStatus.Approved, readiness.Value.ClientApproval.DisplayStatus);
    Assert.Equal("A. Owner", readiness.Value.ClientApproval.Owner);
    Assert.StartsWith("/app/practice/proposals/", readiness.Value.ClientApproval.EvidenceHref, StringComparison.Ordinal);
    Assert.Equal(OnboardingReadinessStatuses.Missing, readiness.Value.PartnerRisk.Status);
    Assert.Equal(OnboardingDisplayStatus.Pending, readiness.Value.PartnerRisk.DisplayStatus);
    Assert.Equal($"/app/clients/{client.Value}/assessment", readiness.Value.PartnerRisk.EvidenceHref);
    Assert.Equal(OnboardingReadinessStatuses.NotEligible, readiness.Value.EngagementLetter.Status);
    Assert.Equal(OnboardingReadinessStatuses.NotInvoiced, readiness.Value.AdvancePayment.Status);
    Assert.Equal(OnboardingReadinessStatuses.Blocked, readiness.Value.Activation.Status);
    Assert.Contains(readiness.Value.Blockers, b => b.Code == "PARTNER_ACCEPTANCE_REQUIRED");
    Assert.DoesNotContain(readiness.Value.Blockers, b => b.Code == "CLIENT_ACCEPTANCE_REQUIRED");

    // A conditional Partner decision never clears its key, even when it is the newest decision.
    var partnerId = w.PartnerUser.Id;
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "FinancialStatementAudit",
      Decision = "AcceptedWithConditions", Conditions = "Obtain bank confirmation", DecidedByUserId = partnerId,
      DecidedAt = DateTimeOffset.UtcNow.AddMinutes(-2), Path = "NEW_CLIENT", Generation = 1, Rationale = "Conditional",
      EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('a', 64)
    });
    await db.SaveChangesAsync();
    readiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagementId);
    Assert.Equal(OnboardingReadinessStatuses.Conditional, readiness.Value!.PartnerRisk.Status);
    Assert.Contains(readiness.Value.Blockers, b => b.Code == "PARTNER_ACCEPTANCE_CONDITIONAL");

    // An unconditional clearance for the current generation is current, and it names the deciding Partner.
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "FinancialStatementAudit",
      Decision = "Accepted", DecidedByUserId = partnerId, DecidedAt = DateTimeOffset.UtcNow, Path = "NEW_CLIENT", Generation = 1,
      Rationale = "Unconditional", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('b', 64)
    });
    await db.SaveChangesAsync();
    readiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagementId);
    Assert.Equal(OnboardingReadinessStatuses.Current, readiness.Value!.PartnerRisk.Status);
    Assert.Equal(OnboardingDisplayStatus.Approved, readiness.Value.PartnerRisk.DisplayStatus);
    Assert.Equal(w.PartnerUser.DisplayName, readiness.Value.PartnerRisk.DecidedByName);
    Assert.Equal(OnboardingReadinessStatuses.Current, readiness.Value.ClientApproval.Status);
    // Both keys are current, but the official seal is not registered, so the letter stays not eligible and says why.
    Assert.Equal(OnboardingReadinessStatuses.NotEligible, readiness.Value.EngagementLetter.Status);
    // The letter's prerequisites are checked in the order the protected command refuses them; the profile is present, but the seal is not.
    Assert.Equal(OnboardingReadinessStatuses.NotEligible, readiness.Value.EngagementLetter.Status);
    Assert.Contains("Register the official firm seal", readiness.Value.EngagementLetter.Label);
    // Only a firm-wide Partner with a registered signature may act: the Relationship Manager can see the cards but cannot act.
    Assert.False(readiness.Value.CanGenerateLetter);
    Assert.False(readiness.Value.CanActivate);

    // Client inputs change: the clearance is stale and no longer opens the letter.
    await db.ClientSafetyStates.Where(x => x.Id == client.Value).ExecuteUpdateAsync(x => x.SetProperty(v => v.InputGeneration, v => v.InputGeneration + 1));
    readiness = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, engagementId);
    Assert.Equal(OnboardingReadinessStatuses.Stale, readiness.Value!.PartnerRisk.Status);
    Assert.Equal(OnboardingDisplayStatus.Stale, readiness.Value.PartnerRisk.DisplayStatus);
    Assert.Contains(readiness.Value.Blockers, b => b.Code == "PARTNER_ACCEPTANCE_STALE");

    // A second engagement of the same client on another service route: the letter route and the risk route cannot be matched to
    // it, and its fee agreement is linked to the first engagement, so readiness must not treat that agreement as its own.
    var otherEngagementId = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = otherEngagementId, FirmId = w.FirmId, PracticeClientId = client.Value, ServiceRoute = "Accounting", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var other = await ClientOnboardingReadinessQuery.GetAsync(db, w.Prep, otherEngagementId);
    Assert.True(other.Succeeded, other.Message);
    Assert.Equal(OnboardingReadinessStatuses.NoAgreement, other.Value!.AdvancePayment.Status);
    Assert.Equal(OnboardingReadinessStatuses.NotDispatched, other.Value.ClientApproval.Status);
    Assert.DoesNotContain(other.Value.Blockers, b => b.Code == "ROUTE_MISMATCH");
    Assert.Equal(OnboardingReadinessStatuses.NotEligible, other.Value.EngagementLetter.Status);
    Assert.False(other.Value.CanActivate);

    // Scope: a user from another firm learns nothing about this engagement.
    var denied = await ClientOnboardingReadinessQuery.GetAsync(db, foreign.Prep, engagementId);
    Assert.False(denied.Succeeded);
    Assert.Null(denied.Value);
  }

  private sealed class PauseAfterOnboardingQuotationRead : DbCommandInterceptor
  {
    private int paused;
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("quotation_versions", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref paused, 1, 0) == 0)
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  [Fact]
  public async Task OnboardingReadinessKeepsCommercialCardsInOneSnapshotAndRechecksCurrentAuthority()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var (_, _, proposalId) = await DraftProposalAsync(pg, w);
    var revokerUser = User(w.FirmId, "partner-revoker");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.Add(revokerUser);
      foreach (var role in new[] { "RelationshipManager", "FinanceManager" })
        seed.RoleGrants.Add(new RoleGrant
        {
          Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = w.Partner.UserId, Role = role,
          GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.Partner.UserId
        });
      seed.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = revokerUser.Id, Role = "Partner",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = revokerUser.Id
      });
      await seed.SaveChangesAsync();
    }
    var revoker = Actor(revokerUser, "Partner");

    Guid engagementId;
    Guid clientId;
    Guid quoteId;
    Guid approvalId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var quote = await QuotationService.SaveAsync(setup, w.Prep, Quote(proposalId, discount: 15m));
      Assert.True(quote.Succeeded, quote.Message);
      quoteId = quote.Value;
      Assert.True((await QuotationService.SubmitAsync(setup, w.Prep, quoteId)).Succeeded);
      var quotations = await QuotationService.ListAsync(setup, w.Prep, proposalId);
      Assert.True(quotations.Succeeded, quotations.Message);
      var required = Assert.Single(quotations.Value!.Single().RequiredApprovals);
      Assert.Equal("Partner", required.Role);
      Assert.True((await QuotationService.ApproveAsync(setup, w.Partner, quoteId, required.RuleKey,
        "Approved the exact submitted quotation revision")).Succeeded);
      approvalId = await setup.QuotationApprovals.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId && x.QuotationVersionId == quoteId && x.RuleKey == required.RuleKey)
        .Select(x => x.Id).SingleAsync();

      Assert.True((await PracticeCrmService.ApproveProposalAsync(setup, w.Partner, proposalId)).Succeeded);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(setup, w.Prep, proposalId);
      Assert.True((await PracticeCrmService.SendProposalAsync(setup, w.Prep, proposalId)).Succeeded);
      var offerSha = await setup.Proposals.AsNoTracking().Where(x => x.FirmId == w.FirmId && x.Id == proposalId)
        .Select(x => x.SentOfferSha256).SingleAsync();
      Assert.False(string.IsNullOrWhiteSpace(offerSha));
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(setup, w.Prep, proposalId,
        new(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@gulf.example.test",
          "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
      var client = await PracticeCrmService.ConvertToClientDraftAsync(setup, w.Prep, new(proposalId, "Snapshot readiness client"));
      Assert.True(client.Succeeded, client.Message);
      clientId = client.Value;
      engagementId = Guid.NewGuid();
      setup.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = w.FirmId, PracticeClientId = clientId, ServiceRoute = "FinancialStatementAudit",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = DateTimeOffset.UtcNow
      });
      await setup.SaveChangesAsync();
    }

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    await using (var beforeDb = new AuditSphereDbContext(pg.Options))
    {
      var before = await ClientOnboardingReadinessQuery.GetAsync(beforeDb, w.Prep, engagementId, timeout.Token);
      Assert.True(before.Succeeded, before.Message);
      var beforeReport = before.Value!;
      Assert.Equal(OnboardingReadinessStatuses.Current, beforeReport.ClientApproval.Status);
      Assert.Equal(OnboardingReadinessStatuses.Blocked, beforeReport.Activation.Status);

      var pause = new PauseAfterOnboardingQuotationRead();
      await using var delayed = new AuditSphereDbContext(
        new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
      await using var approvalRevoker = new AuditSphereDbContext(pg.Options);
      try
      {
        var pendingRead = ClientOnboardingReadinessQuery.GetAsync(delayed, w.Prep, engagementId, timeout.Token);
        await pause.Reached.Task.WaitAsync(timeout.Token);

        // The quotation row has been read, but the client-acceptance predicate still has to check its approval revocation.
        Assert.True((await QuotationService.RevokeApprovalAsync(approvalRevoker, revoker, approvalId,
          "Withdrawn pending a fresh commercial review", timeout.Token)).Succeeded);
        var retainedRevocation = await approvalRevoker.QuotationApprovalRevocations.AsNoTracking()
          .SingleAsync(x => x.FirmId == w.FirmId && x.QuotationApprovalId == approvalId, timeout.Token);
        Assert.Equal(revoker.UserId, retainedRevocation.RevokedByUserId);
        Assert.Equal(QuotationStates.Approved, await approvalRevoker.QuotationVersions.AsNoTracking()
          .Where(x => x.FirmId == w.FirmId && x.Id == quoteId).Select(x => x.Status).SingleAsync(timeout.Token));

        pause.Release.TrySetResult(true);
        var interleaved = await pendingRead;
        Assert.True(interleaved.Succeeded, interleaved.Message);
        var interleavedReport = interleaved.Value!;
        Assert.True(interleavedReport.EvaluatedAtUtc <= retainedRevocation.RevokedAt,
          "The readiness evaluation time must belong to the snapshot captured before this revocation.");
        Assert.Equal(beforeReport.ClientApproval, interleavedReport.ClientApproval);
        Assert.Equal(beforeReport.PartnerRisk, interleavedReport.PartnerRisk);
        Assert.Equal(beforeReport.EngagementLetter, interleavedReport.EngagementLetter);
        Assert.Equal(beforeReport.AdvancePayment, interleavedReport.AdvancePayment);
        Assert.Equal(beforeReport.Activation, interleavedReport.Activation);
      }
      finally
      {
        pause.Release.TrySetResult(true);
      }
    }

    await using (var fresh = new AuditSphereDbContext(pg.Options))
    {
      var current = await ClientOnboardingReadinessQuery.GetAsync(fresh, w.Prep, engagementId, timeout.Token);
      Assert.True(current.Succeeded, current.Message);
      var currentReport = current.Value!;
      Assert.Equal(OnboardingReadinessStatuses.Stale, currentReport.ClientApproval.Status);
      Assert.Equal(OnboardingDisplayStatus.Stale, currentReport.ClientApproval.DisplayStatus);
      Assert.Contains(currentReport.Blockers, b => b.Code == "CLIENT_ACCEPTANCE_STALE");
      Assert.Equal(OnboardingReadinessStatuses.Blocked, currentReport.Activation.Status);
    }

    await using (var callerTransactionDb = new AuditSphereDbContext(pg.Options))
    {
      await using var callerTransaction = await callerTransactionDb.Database.BeginTransactionAsync(timeout.Token);
      var refused = await ClientOnboardingReadinessQuery.GetAsync(callerTransactionDb, w.Prep, engagementId, timeout.Token);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      Assert.Null(refused.Value);
      var refusedFee = await FeeAgreementWorkspaceQuery.GetAsync(callerTransactionDb, w.Partner, proposalId, timeout.Token);
      Assert.False(refusedFee.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refusedFee.ErrorCode);
      Assert.Null(refusedFee.Value);
      Assert.NotNull(callerTransactionDb.Database.CurrentTransaction);
      Assert.Equal(1, await callerTransactionDb.Database.SqlQueryRaw<int>("SELECT 1 AS \"Value\"")
        .SingleAsync(timeout.Token));
      await callerTransaction.CommitAsync(timeout.Token);
    }

    await using (var partnerRead = new AuditSphereDbContext(pg.Options))
    {
      var report = await ClientOnboardingReadinessQuery.GetAsync(partnerRead, w.Partner, engagementId, timeout.Token);
      Assert.True(report.Succeeded, report.Message);
      Assert.True(report.Value!.CanActivate);
      Assert.Equal(OnboardingReadinessStatuses.Blocked, report.Value.Activation.Status);
    }

    var partnerActionPause = new PauseAfterOnboardingQuotationRead();
    await using (var partnerReader = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(partnerActionPause).Options))
    {
      await using var partnerGrantWriter = new AuditSphereDbContext(pg.Options);
      try
      {
        var pendingActionRead = ClientOnboardingReadinessQuery.GetAsync(partnerReader, w.Partner, engagementId, timeout.Token);
        await partnerActionPause.Reached.Task.WaitAsync(timeout.Token);
        var revokedPartner = await partnerGrantWriter.RoleGrants.Where(x => x.FirmId == w.FirmId &&
            x.UserId == w.Partner.UserId && x.Role == "Partner" && x.ClientId == null &&
            x.EngagementId == null && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
        Assert.Equal(1, revokedPartner);
        Assert.Equal(1, await partnerGrantWriter.RoleGrants.CountAsync(x => x.FirmId == w.FirmId &&
          x.UserId == w.Partner.UserId && x.Role == "RelationshipManager" && x.RevokedAt == null, timeout.Token));
        partnerActionPause.Release.TrySetResult(true);
        var refusedAction = await pendingActionRead;
        Assert.False(refusedAction.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, refusedAction.ErrorCode);
        Assert.Null(refusedAction.Value);
      }
      finally
      {
        partnerActionPause.Release.TrySetResult(true);
      }
    }

    await using (var feeRead = new AuditSphereDbContext(pg.Options))
    {
      var fee = await FeeAgreementWorkspaceQuery.GetAsync(feeRead, w.Partner, proposalId, timeout.Token);
      Assert.True(fee.Succeeded, fee.Message);
      Assert.True(fee.Value!.CanFinance);
      Assert.Null(fee.Value.AgreementId);
    }

    var financePause = new PauseAfterOnboardingQuotationRead();
    await using (var feeReader = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(financePause).Options))
    {
      await using var financeGrantWriter = new AuditSphereDbContext(pg.Options);
      try
      {
        var pendingFeeRead = FeeAgreementWorkspaceQuery.GetAsync(feeReader, w.Partner, proposalId, timeout.Token);
        await financePause.Reached.Task.WaitAsync(timeout.Token);
        var revokedFinance = await financeGrantWriter.RoleGrants.Where(x => x.FirmId == w.FirmId &&
            x.UserId == w.Partner.UserId && x.Role == "FinanceManager" && x.ClientId == null &&
            x.EngagementId == null && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
        Assert.Equal(1, revokedFinance);
        Assert.Equal(1, await financeGrantWriter.RoleGrants.CountAsync(x => x.FirmId == w.FirmId &&
          x.UserId == w.Partner.UserId && x.Role == "RelationshipManager" && x.RevokedAt == null, timeout.Token));
        financePause.Release.TrySetResult(true);
        var refusedFee = await pendingFeeRead;
        Assert.False(refusedFee.Succeeded);
        Assert.Equal(ErrorCodes.ScopeDenied, refusedFee.ErrorCode);
        Assert.Null(refusedFee.Value);
      }
      finally
      {
        financePause.Release.TrySetResult(true);
      }
    }

    var authorityPause = new PauseAfterOnboardingQuotationRead();
    await using var authorityReader = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(authorityPause).Options);
    await using var grantWriter = new AuditSphereDbContext(pg.Options);
    try
    {
      var pendingRead = ClientOnboardingReadinessQuery.GetAsync(authorityReader, w.Prep, engagementId, timeout.Token);
      await authorityPause.Reached.Task.WaitAsync(timeout.Token);
      var revoked = await grantWriter.RoleGrants.Where(x => x.FirmId == w.FirmId && x.UserId == w.Prep.UserId &&
          x.Role == "RelationshipManager" && x.ClientId == null && x.EngagementId == null && x.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
      Assert.Equal(1, revoked);
      authorityPause.Release.TrySetResult(true);
      var refused = await pendingRead;
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, refused.ErrorCode);
      Assert.Null(refused.Value);
    }
    finally
    {
      authorityPause.Release.TrySetResult(true);
    }
  }

  private sealed class ObserveFirmLockAttempt : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
      {
        Fired = true;
        Reached.TrySetResult();
      }
      return ValueTask.FromResult(result);
    }
  }

  private sealed class RevokeCommercialGrantAfterRead(
    ITestPostgresDatabase pg, Guid firmId, Guid userId, string role, string targetTable) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains(targetTable, StringComparison.OrdinalIgnoreCase) ||
          Interlocked.CompareExchange(ref calls, 1, 0) != 0)
        return result;

      await using var change = new AuditSphereDbContext(pg.Options);
      var updated = await change.RoleGrants.Where(x => x.FirmId == firmId && x.UserId == userId && x.Role == role &&
          x.ClientId == null && x.EngagementId == null && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
      if (updated != 1)
        throw new InvalidOperationException("The commercial read interleaving did not revoke exactly one active firmwide grant.");
      return result;
    }
  }

  private sealed class ChangeClientIdentityBeforeReceiptGrantCheck(
    ITestPostgresDatabase pg, Guid firmId, Guid userId, bool disableUser) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains("SELECT EXISTS", StringComparison.OrdinalIgnoreCase) ||
          !command.CommandText.Contains("role_grants", StringComparison.OrdinalIgnoreCase) ||
          Interlocked.CompareExchange(ref calls, 1, 0) != 0)
        return result;

      await using var change = new AuditSphereDbContext(pg.Options);
      var updated = disableUser
        ? await change.Users.Where(x => x.FirmId == firmId && x.Id == userId)
          .ExecuteUpdateAsync(update => update.SetProperty(x => x.Disabled, true), cancellationToken)
        : await change.Users.Where(x => x.FirmId == firmId && x.Id == userId)
          .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), cancellationToken);
      if (updated != 1)
        throw new InvalidOperationException("The client receipt interleaving did not update exactly one user.");
      return result;
    }
  }

  private sealed class SetInvoiceSentAtAfterFinanceAgreementRead(
    ITestPostgresDatabase pg, Guid firmId, Guid invoiceId, DateTimeOffset sentAt) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains("engagement_fee_agreements", StringComparison.OrdinalIgnoreCase) ||
          Interlocked.CompareExchange(ref calls, 1, 0) != 0)
        return result;

      await using var change = new AuditSphereDbContext(pg.Options);
      var updated = await change.Invoices.Where(x => x.FirmId == firmId && x.Id == invoiceId && x.SentAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.SentAt, sentAt)
          .SetProperty(x => x.Status, BillingStates.InvoiceSent)
          .SetProperty(x => x.Revision, x => x.Revision + 1), cancellationToken);
      if (updated != 1)
        throw new InvalidOperationException("The finance snapshot interleaving did not update the expected invoice.");
      return result;
    }
  }

  private sealed class RevokeFinanceGrantAfterAgreementRead(
    ITestPostgresDatabase pg, Guid firmId, Guid grantId) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains("engagement_fee_agreements", StringComparison.OrdinalIgnoreCase) ||
          Interlocked.CompareExchange(ref calls, 1, 0) != 0)
        return result;

      await using var change = new AuditSphereDbContext(pg.Options);
      var updated = await change.RoleGrants.Where(x => x.FirmId == firmId && x.Id == grantId && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
      if (updated != 1)
        throw new InvalidOperationException("The finance snapshot interleaving did not revoke the expected client grant.");
      return result;
    }
  }

  private sealed class RevokeFinanceGrantAfterReceiptPageRead(
    ITestPostgresDatabase pg, Guid firmId, Guid grantId) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (!command.CommandText.Contains("ClientPortalFinance.ReceiptPage", StringComparison.Ordinal) ||
          Interlocked.CompareExchange(ref calls, 1, 0) != 0)
        return result;

      await using var change = new AuditSphereDbContext(pg.Options);
      var updated = await change.RoleGrants.Where(x => x.FirmId == firmId && x.Id == grantId && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), cancellationToken);
      if (updated != 1)
        throw new InvalidOperationException("The receipt page interleaving did not revoke the expected client grant.");
      return result;
    }
  }
}
