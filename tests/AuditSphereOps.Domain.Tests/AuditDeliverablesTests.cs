using System.IO.Compression;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Records;
using Microsoft.Extensions.Logging.Abstractions;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE package 5 with staffed role identities: anchored review notes and the Senior/Manager review hierarchy, the
/// Summary Review Memorandum and Partner clearance chain with staleness, the four opinions, the report trio, the
/// holding letter for a critical confirmation, the client comment and representation-letter loop, and PNG signing.
/// </summary>
[Trait("Profile", "Database")]
public sealed class AuditDeliverablesTests
{
  private sealed record World(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> U, Guid ProcedureId, Guid ResultId)
  {
    public ActorContext A(string name, params string[] roles) => new(U[name].Id, FirmId, U[name].SessionEpoch, roles);
  }

  private static AppUser NewUser(Guid firmId, string name, string kind = "Staff") => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + Guid.NewGuid().ToString("N"), TenantId = "tenant-del", Email = $"{name}-{Guid.NewGuid():N}@example.test",
    DisplayName = name, UserKind = kind, CreatedAt = DateTimeOffset.UtcNow
  };

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var u = new[] { "partner", "partner2", "manager", "senior", "associate", "associate2" }.ToDictionary(x => x, x => NewUser(firmId, x));
    u["client"] = NewUser(firmId, "client", "Client");
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.Id == engagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Active").SetProperty(x => x.ProfessionalWorkBlocked, false)
      .SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
    db.Users.AddRange(u.Values);
    RoleGrant G(string name, string role, Guid? client = null, Guid? engagement = null) => new()
    { Id = Guid.NewGuid(), FirmId = firmId, UserId = u[name].Id, Role = role, ClientId = client, EngagementId = engagement, GrantedAt = now, GrantedByUserId = u[name].Id };
    db.RoleGrants.AddRange(G("partner", "Partner"), G("partner2", "Partner"), G("client", "ClientUser", clientId, engagementId));
    db.StaffCertifications.AddRange(new[] { "partner", "manager" }.Select(n => new StaffCertification { Id = Guid.NewGuid(), FirmId = firmId, UserId = u[n].Id, Name = "ACCA", RecordedAt = now, RecordedByUserId = u["partner"].Id }));
    var versionId = Guid.NewGuid();
    db.AuditProgramVersions.Add(new AuditProgramVersion { Id = versionId, FirmId = firmId, Version = "2026.1", SourceHash = Hashing.Sha256Hex("p"), Status = AuditProgramStatuses.Published,
      CreatedByUserId = u["partner"].Id, ApprovedByUserId = u["partner"].Id, CreatedAt = now, ApprovedAt = now });
    await db.SaveChangesAsync();
    var programId = Guid.NewGuid();
    db.EngagementAuditPrograms.Add(new EngagementAuditProgram { Id = programId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, ProgramVersionId = versionId, AdoptedByUserId = u["partner"].Id, AdoptedAt = now });
    var procedure = new AuditProcedure { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, EngagementProgramId = programId, SourceProcedureId = "REV-01",
      SourceSectionTitle = "Revenue", Title = "Revenue cut-off", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Submitted, CurrentResultRevision = 1, CreatedAt = now };
    db.AuditProcedures.Add(procedure);
    var result = new AuditProcedureResult { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, AuditProcedureId = procedure.Id, Revision = 1, InputGeneration = 1,
      WorkPerformed = "Selected 25 invoices around year end and agreed them to dispatch notes.", StructuredResultJson = "{}", Conclusion = "Revenue is recorded in the correct period.",
      PreparedByUserId = u["associate"].Id, SubmittedAt = now };
    db.AuditProcedureResults.Add(result);
    db.GoingConcernAssessments.Add(new GoingConcernAssessment { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, AssessmentDate = new DateOnly(2027, 2, 1),
      PeriodCoveredTo = new DateOnly(2028, 3, 31), ForecastReviewOutcome = "Reviewed", DisclosureAdequate = true, Conclusion = GoingConcernConclusions.NoMaterialUncertainty, Rationale = "Forecast reviewed",
      Currency = "QAR", RecordedByUserId = u["senior"].Id, ReviewedByUserId = u["manager"].Id, RecordedAt = now, ReviewedAt = now });
    db.AnalyticalReviewVarianceInvestigations.Add(new AnalyticalReviewVarianceInvestigation { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      AccountArea = "Revenue", PeriodReference = "FY2026", ExpectedAmount = 100m, ActualAmount = 105m, DifferenceAmount = 5m, InvestigationThreshold = 10m, Conclusion = VarianceInvestigationConclusions.Explained,
      Currency = "QAR", RecordedByUserId = u["senior"].Id, ReviewedByUserId = u["manager"].Id, RecordedAt = now, ReviewedAt = now });
    db.Findings.Add(new Finding { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, ActorId = u["senior"].Id, FindingType = "Control deficiency",
      ImpactDescription = "Credit notes approved without review", MonetaryAmount = 12_000m, ManagementResponse = "Approval workflow introduced from March.", CreatedAt = now });
    await db.SaveChangesAsync();
    var w = new World(firmId, clientId, engagementId, u, procedure.Id, result.Id);
    foreach (var (name, level) in new[] { ("partner", StaffingLevels.EngagementPartner), ("manager", StaffingLevels.AuditManager), ("senior", StaffingLevels.SeniorAuditor),
      ("associate", StaffingLevels.StaffAssociate), ("associate2", StaffingLevels.StaffAssociate) })
    {
      await using var staffDb = new AuditSphereDbContext(pg.Options);
      var assigned = await StaffingService.AssignAsync(staffDb, w.A("partner2", "Partner"), new(engagementId, u[name].Id, level));
      Assert.True(assigned.Succeeded, $"{name}: {assigned.Message}");
    }
    return w;
  }

  [Fact]
  public async Task InlineNotesBlockApproval_AndOnlySomeoneStaffedAboveThePreparerReviews()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var senior = w.A("senior", "Senior");
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Equal(ErrorCodes.GateBlocked, (await ReviewNotesService.AddNoteAsync(db, senior, new(w.ResultId, "WORK_PERFORMED", "not in the text", "Why?"))).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ReviewNotesService.AddNoteAsync(db, w.A("associate", "Staff"), new(w.ResultId, "WORK_PERFORMED", "25 invoices", "Self review"))).ErrorCode);
    var note = await ReviewNotesService.AddNoteAsync(db, senior, new(w.ResultId, "WORK_PERFORMED", "25 invoices", "How was the sample size of 25 determined?"));
    Assert.True(note.Succeeded, note.Message);
    var view = (await ReviewNotesService.ListAsync(db, w.A("manager", "Manager"), w.ProcedureId)).Single();
    Assert.Equal((9, 1L, true), (view.StartOffset, view.ResultRevision, view.Open));

    // An open note blocks approval; a peer of the preparer cannot review at all.
    Assert.Equal(ErrorCodes.ScopeDenied, (await AuditProgramService.ReviewResultAsync(db, w.A("associate2", "Staff", "Reviewer"), new(w.ResultId, "REVIEWED", null))).ErrorCode);
    Assert.Equal(ErrorCodes.GateBlocked, (await AuditProgramService.ReviewResultAsync(db, senior, new(w.ResultId, "REVIEWED", null))).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ReviewNotesService.ResolveAsync(db, w.A("associate", "Staff"), note.Value, "Resolved by me")).ErrorCode);
    Assert.True((await ReviewNotesService.RespondAsync(db, w.A("associate", "Staff"), note.Value, "Per the firm sampling table for moderate risk.")).Succeeded);
    Assert.True((await ReviewNotesService.ResolveAsync(db, senior, note.Value, "Agreed.")).Succeeded);
    var reviewed = await AuditProgramService.ReviewResultAsync(db, senior, new(w.ResultId, "REVIEWED", null));
    Assert.True(reviewed.Succeeded, reviewed.Message);
    var history = (await ReviewNotesService.ListAsync(db, senior, w.ProcedureId)).Single();
    Assert.False(history.Open);
    Assert.Equal(2, history.Events.Count);
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM procedure_review_notes WHERE id = {note.Value}"));
  }

  [Fact]
  public async Task SrmClearanceOpinionReportsHoldingLetterClientLoopAndPngSigning()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    var client = w.A("client", "ClientUser");
    Guid confirmationId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await AuditProgramService.ReviewResultAsync(db, w.A("senior", "Senior"), new(w.ResultId, "REVIEWED", null))).Succeeded);
      db.AuditConfirmationCases.Add(new AuditConfirmationCase { Id = confirmationId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, AreaCode = "BANK",
        SourceRecordId = "ACC-1000", BookedAmount = 800_000m, Currency = "QAR", ConfirmationDate = new DateOnly(2026, 12, 31), Respondent = "Qatar National Bank",
        ContactValidationSource = "Bank website", Status = AuditConfirmationStatuses.Dispatched, DispatchReference = "DISP-1", DispatchedAt = DateTimeOffset.UtcNow.AddDays(-20),
        CreatedByUserId = w.U["senior"].Id, CreatedAt = DateTimeOffset.UtcNow.AddDays(-21) });
      await db.SaveChangesAsync();
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.SetConfirmationCriticalityAsync(db, w.A("senior", "Senior"), confirmationId, true, "Cash is material")).ErrorCode);
      Assert.True((await AuditDeliverableService.SetConfirmationCriticalityAsync(db, manager, confirmationId, true, "Cash balance is material to the opinion")).Succeeded);
      var dashboard = (await AuditDeliverableService.ConfirmationDashboardAsync(db, w.A("senior", "Senior"), w.EngagementId)).Value!.Single();
      Assert.Equal(("Bank", "FOLLOW_UP_DUE", true, 20), (dashboard.Type, dashboard.Monitoring, dashboard.Critical, dashboard.DaysSinceDispatch));
    }

    Guid srm;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.DecideOpinionAsync(db, partner, w.EngagementId, "UNMODIFIED", null, null)).ErrorCode);
      srm = (await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, manager, w.EngagementId, "Clear the confirmation before signing.")).Value;
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.PartnerClearAsync(db, w.A("partner2", "Partner"), srm, "Reviewed", "Reviewed")).ErrorCode);
      Assert.True((await AuditDeliverableService.PartnerClearAsync(db, partner, srm, "Revenue and cash reviewed.", "Notes 1–14 reviewed.")).Succeeded);
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await AuditDeliverableService.DecideOpinionAsync(db, partner, w.EngagementId, "QUALIFIED", null, "basis")).ErrorCode);
      Assert.True((await AuditDeliverableService.DecideOpinionAsync(db, partner, w.EngagementId, "QUALIFIED", "Inventory",
        "We were unable to observe the counting of inventory held at 31 December 2026, stated at QAR 2.1 million.")).Succeeded);
      // The critical confirmation holds the report and produces a holding letter.
      var held = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.IndependentAuditorsReport)).Value!;
      Assert.Null(held.DeliverableId);
      Assert.NotNull(held.HoldingLetterId);
      var letter = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == held.HoldingLetterId);
      Assert.Contains("Qatar National Bank", DocumentText(letter.Content));
    }

    // Closing the confirmation changes the reviewed facts: SRM, clearance and opinion go stale and must be redone.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.AuditConfirmationCases.Where(x => x.Id == confirmationId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, AuditConfirmationStatuses.Closed));
      var stale = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == srm);
      Assert.False(await AuditDeliverableService.IsCurrentAsync(db, partner, stale));
      Assert.Null(await AuditDeliverableService.CurrentOpinionAsync(db, partner, w.EngagementId));
      Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.PartnerClearAsync(db, partner, srm, "x", "y")).ErrorCode);
      var srm2 = (await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, manager, w.EngagementId, "Ready for Partner clearance.")).Value;
      Assert.Equal(2, (await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == srm2)).Version);
      Assert.True((await AuditDeliverableService.PartnerClearAsync(db, partner, srm2, "Revenue and cash reviewed.", "Notes 1–14 reviewed.")).Succeeded);
      Assert.True((await AuditDeliverableService.DecideOpinionAsync(db, partner, w.EngagementId, "QUALIFIED", "Inventory",
        "We were unable to observe the counting of inventory held at 31 December 2026, stated at QAR 2.1 million.")).Succeeded);
    }

    Guid iar, repLetter, repReview, iarReview;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      foreach (var kind in new[] { DeliverableKinds.AuditFindingsReport, DeliverableKinds.ManagementLetter })
      {
        var report = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, kind)).Value!;
        var text = DocumentText((await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == report.DeliverableId)).Content);
        Assert.Contains("Credit notes approved without review", text);
        if (kind == DeliverableKinds.ManagementLetter) Assert.Contains("Approval workflow introduced from March.", text);
      }
      iar = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.IndependentAuditorsReport)).Value!.DeliverableId!.Value;
      var iarText = DocumentText((await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == iar)).Content);
      Assert.Contains("Basis for Qualified Opinion", iarText);
      Assert.Contains("Focus area: Inventory.", iarText);
      Assert.Contains("unable to observe the counting of inventory", iarText);
      repLetter = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.RepresentationLetter)).Value!.DeliverableId!.Value;
      repReview = (await AuditDeliverableService.ShareWithClientAsync(db, manager, repLetter)).Value;
      iarReview = (await AuditDeliverableService.ShareWithClientAsync(db, manager, iar)).Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // PNG specimen: a non-PNG is refused; the Partner registers one.
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await AuditDeliverableService.RegisterSignatureAsync(db, partner, Encoding.UTF8.GetBytes("not a png"))).ErrorCode);
      Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, partner, Png(200, 60))).Succeeded);
      Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, w.A("partner2", "Partner"), Png(200, 60))).Succeeded);

      // Client loop: a comment blocks signing until resolved and the representation letter is acknowledged at its exact hash.
      var comment = await AuditDeliverableService.CommentAsync(db, client, iarReview, "Please correct the inventory amount to QAR 2.1 million.");
      Assert.True(comment.Succeeded, comment.Message);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.SignIndependentReportAsync(db, partner, iar)).ErrorCode);
      Assert.True((await AuditDeliverableService.ResolveCommentAsync(db, manager, comment.Value, "Amount confirmed as QAR 2.1 million in the basis paragraph.")).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await AuditDeliverableService.SignIndependentReportAsync(db, partner, iar)).ErrorCode); // letter not acknowledged
      Assert.Equal(ErrorCodes.GenerationStale, (await AuditDeliverableService.AcknowledgeAsync(db, client, repReview, new string('0', 64))).ErrorCode);
      var letterSha = (await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == repLetter)).ContentSha256;
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.AcknowledgeAsync(db, manager, repReview, letterSha)).ErrorCode);
      Assert.True((await AuditDeliverableService.AcknowledgeAsync(db, client, repReview, letterSha)).Succeeded);

      // Only the deciding Engagement Partner signs, once, embedding the PNG into that exact version.
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditDeliverableService.SignIndependentReportAsync(db, w.A("partner2", "Partner"), iar)).ErrorCode);
      var signed = await AuditDeliverableService.SignIndependentReportAsync(db, partner, iar);
      Assert.True(signed.Succeeded, signed.Message);
      Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditDeliverableService.SignIndependentReportAsync(db, partner, iar)).ErrorCode);
      var signedDoc = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == signed.Value);
      Assert.Equal(iar, signedDoc.SignedFromDeliverableId);
      using (var zip = new ZipArchive(new MemoryStream(signedDoc.Content)))
        Assert.Contains(zip.Entries, e => e.FullName.EndsWith(".png", StringComparison.Ordinal));
      Assert.Equal(Hashing.Sha256Hex(signedDoc.Content), signedDoc.ContentSha256);
      Assert.True(await db.SignatureApplications.AnyAsync(x => x.SourceDeliverableId == iar && x.SignedByUserId == w.U["partner"].Id));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit_deliverables SET content_sha256 = {new string('f', 64)} WHERE id = {signed.Value}"));
    }
  }

  private sealed class FixedClock(DateTimeOffset now) : TimeProvider
  {
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
  }

  private static async Task<Guid> SignedReportAsync(PgTestSchema pg, World w)
  {
    var partner = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.True((await AuditProgramService.ReviewResultAsync(db, w.A("senior", "Senior"), new(w.ResultId, "REVIEWED", null))).Succeeded);
    var srm = (await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, manager, w.EngagementId, "Ready.")).Value;
    Assert.True((await AuditDeliverableService.PartnerClearAsync(db, partner, srm, "Risks reviewed.", "Notes reviewed.")).Succeeded);
    Assert.True((await AuditDeliverableService.DecideOpinionAsync(db, partner, w.EngagementId, "UNMODIFIED", null, null)).Succeeded);
    var letter = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.RepresentationLetter)).Value!.DeliverableId!.Value;
    var review = (await AuditDeliverableService.ShareWithClientAsync(db, manager, letter)).Value;
    Assert.True((await AuditDeliverableService.AcknowledgeAsync(db, w.A("client", "ClientUser"), review, (await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == letter)).ContentSha256)).Succeeded);
    var iar = (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.IndependentAuditorsReport)).Value!.DeliverableId!.Value;
    Assert.True((await AuditDeliverableService.RegisterSignatureAsync(db, partner, Png(120, 40))).Succeeded);
    var signed = await AuditDeliverableService.SignIndependentReportAsync(db, partner, iar);
    Assert.True(signed.Succeeded, signed.Message);
    return signed.Value;
  }

  [Fact]
  public async Task FileFreezesSixtyDaysAfterSigning_RefusesWrites_AmendsWithApproval_AndTracesActivity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var signedId = await SignedReportAsync(pg, w);
    var partner = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    DateTimeOffset signedAt, dueAt;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
      signedAt = (await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == signedId)).CreatedAt;
      Assert.Equal((FileFreezeStates.Scheduled, signedId), (freeze.State, freeze.ReportDeliverableId));
      dueAt = freeze.DueAt;
      Assert.True(Math.Abs((dueAt - signedAt.AddDays(60)).TotalMilliseconds) < 1);
    }

    // The worker freezes only once the 60 days have elapsed on its clock.
    var clock = new FixedClock(dueAt.AddDays(-1));
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var handler = new FileFreezeHandler(clock);
    var options = new WorkerOptions(w.FirmId, "Test");
    var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new FileFreezeDiscovery(factory, store, handler, options, clock)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    Assert.False(await worker.ProcessNextAsync());
    clock.Now = dueAt;
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync()); // idempotent: nothing more to do
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var freeze = await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == w.EngagementId);
      Assert.Equal((FileFreezeStates.Frozen, ExternalReadOnlyStates.BlockedExternal), (freeze.State, freeze.ExternalReadOnly));
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == w.EngagementId)).ProfessionalWorkBlocked);
      Assert.Equal(ErrorCodes.ProtectedState, (await AuditDeliverableService.GenerateReportAsync(db, manager, w.EngagementId, DeliverableKinds.ManagementLetter)).ErrorCode);
      Assert.Equal(ErrorCodes.ProtectedState, (await EngagementActivityQuery.LockAsync(db, w.A("senior", "Senior"), w.EngagementId, "WP-A1")).ErrorCode);
      Assert.Equal(2, await db.FrozenAccessAttempts.CountAsync(x => x.EngagementId == w.EngagementId));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE engagement_file_freezes SET state = 'AMENDMENT_OPEN' WHERE id = {freeze.Id}"));
    }

    // Amendment: requested by a Manager, approved by a different Partner, then closed to re-freeze.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await FileFreezeService.RequestAmendmentAsync(db, manager, w.EngagementId, " ")).ErrorCode);
      var amendment = (await FileFreezeService.RequestAmendmentAsync(db, partner, w.EngagementId, "Subsequent-event disclosure must be added to the file.")).Value;
      Assert.Equal(ErrorCodes.ScopeDenied, (await FileFreezeService.ApproveAmendmentAsync(db, partner, amendment)).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FileFreezeService.ApproveAmendmentAsync(db, manager, amendment)).ErrorCode);
      Assert.True((await FileFreezeService.ApproveAmendmentAsync(db, w.A("partner2", "Partner"), amendment)).Succeeded);
      Assert.False((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == w.EngagementId)).ProfessionalWorkBlocked);
      // Document locks while open: exclusive, releasable by a Manager.
      var senior = w.A("senior", "Senior");
      var lockId = (await EngagementActivityQuery.LockAsync(db, senior, w.EngagementId, "WP-A1")).Value;
      Assert.Equal(ErrorCodes.ProtectedState, (await EngagementActivityQuery.LockAsync(db, w.A("associate", "Staff"), w.EngagementId, "WP-A1")).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await EngagementActivityQuery.UnlockAsync(db, w.A("associate", "Staff"), lockId)).ErrorCode);
      Assert.True((await EngagementActivityQuery.UnlockAsync(db, manager, lockId)).Succeeded);
      Assert.True((await FileFreezeService.CloseAmendmentAsync(db, manager, amendment)).Succeeded);
      Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == w.EngagementId)).ProfessionalWorkBlocked);
      var view = (await FileFreezeService.GetAsync(db, manager, w.EngagementId, clock.Now))!;
      Assert.Equal((FileFreezeStates.Frozen, 1), (view.State, view.Amendments.Count));

      // One attributable trail with edits, comments, sign-offs, the freeze and refused writes.
      var trail = (await EngagementActivityQuery.GetAsync(db, manager, w.EngagementId)).Value!;
      foreach (var kind in new[] { ActivityKinds.Edit, ActivityKinds.SignOff, ActivityKinds.Freeze, ActivityKinds.Denied })
        Assert.Contains(trail, x => x.Kind == kind);
      Assert.Contains(trail, x => x.Kind == ActivityKinds.SignOff && x.Description.Contains("signed") && x.Actor == "partner");
      Assert.Contains(trail, x => x.Kind == ActivityKinds.Denied && x.Actor == "manager");
      Assert.Equal(ErrorCodes.ScopeDenied, (await EngagementActivityQuery.GetAsync(db, w.A("associate", "Staff"), w.EngagementId)).ErrorCode);
    }
  }

  private static string DocumentText(byte[] docx)
  {
    using var zip = new ZipArchive(new MemoryStream(docx));
    using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
    return System.Text.RegularExpressions.Regex.Replace(reader.ReadToEnd(), "<[^>]+>", "");
  }

  /// <summary>A small, valid grayscale PNG.</summary>
  public static byte[] Png(int width, int height)
  {
    static byte[] Chunk(string type, byte[] data)
    {
      var typeBytes = Encoding.ASCII.GetBytes(type);
      var crc = Crc32([.. typeBytes, .. data]);
      return [.. BigEndian(data.Length), .. typeBytes, .. data, .. BigEndian((int)crc)];
    }
    static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    static uint Crc32(byte[] bytes)
    {
      var crc = 0xFFFFFFFFu;
      foreach (var b in bytes) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
      return ~crc;
    }
    byte[] header = [.. BigEndian(width), .. BigEndian(height), 8, 0, 0, 0, 0];
    var raw = new MemoryStream();
    for (var y = 0; y < height; y++) { raw.WriteByte(0); for (var x = 0; x < width; x++) raw.WriteByte((byte)(x == y ? 0 : 255)); }
    var compressed = new MemoryStream();
    using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.ToArray());
    return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", header), .. Chunk("IDAT", compressed.ToArray()), .. Chunk("IEND", [])];
  }
}
