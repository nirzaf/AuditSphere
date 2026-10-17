using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditProgramWorkflowTests
{
  [Fact(DisplayName = "Controlled programme publication requires current firm-wide authority and an independent reviewer")]
  public async Task ControlledPublication_PreparesBindsAndPublishesOnlyAfterIndependentReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var preparer = fixture.Primary.Actor;
    await using var db = new AuditSphereDbContext(pg.Options);
    var firmGrant = new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = preparer.FirmId, UserId = preparer.UserId, Role = "Partner",
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = preparer.UserId
    };
    db.RoleGrants.Add(firmGrant);
    await db.SaveChangesAsync();

    var request = new PrepareAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash);
    var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, preparer, request);
    Assert.True(draft.Succeeded, draft.Message);
    Assert.Equal("DRAFT", draft.Value!.Status);
    Assert.Equal(165, draft.Value.ProcedureCount);
    Assert.Equal(AuditProgramCatalog.ContentDigest, draft.Value.ContentDigest);
    Assert.Null(draft.Value.ApprovedByUserId);
    Assert.Equal(165, await db.AuditProgramProcedures.CountAsync());

    var selfReview = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, preparer,
      new ReviewAuditProgramPublicationRequest(draft.Value.ProgramVersionId, draft.Value.ContentDigest!, "review-001"));
    Assert.False(selfReview.Succeeded);
    Assert.Equal("DRAFT", (await db.AuditProgramVersions.SingleAsync()).Status);

    var repeatedPrepare = await AuditProgramService.PrepareControlledAuditProgramAsync(db, preparer, request);
    Assert.True(repeatedPrepare.Succeeded, repeatedPrepare.Message);
    Assert.Equal(draft.Value.ProgramVersionId, repeatedPrepare.Value!.ProgramVersionId);

    var reviewer = await AuditProgramTestHelpers.CreateReviewerAsync(db, preparer.FirmId, preparer.UserId);
    var publication = new ReviewAuditProgramPublicationRequest(draft.Value.ProgramVersionId,
      draft.Value.ContentDigest!, "methodology-review-record-2026-1");
    var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, reviewer, publication);
    Assert.True(published.Succeeded, published.Message);
    Assert.Equal("PUBLISHED", published.Value!.Status);
    Assert.Equal(reviewer.UserId, published.Value.ApprovedByUserId);
    Assert.NotEqual(published.Value.CreatedByUserId, published.Value.ApprovedByUserId);
    Assert.Equal(publication.PublicationReviewReference, published.Value.PublicationReviewReference);

    var replay = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, reviewer, publication);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(published.Value.ProgramVersionId, replay.Value!.ProgramVersionId);
    Assert.Equal(1, await db.AuditProgramVersions.CountAsync());

    var changedReview = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, reviewer,
      publication with { ExpectedContentDigest = new string('0', 64) });
    Assert.False(changedReview.Succeeded);

    firmGrant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    var revokedOwner = await AuditProgramService.PrepareControlledAuditProgramAsync(db, preparer,
      new PrepareAuditProgramRequest("2026.2", AuditProgramCatalog.SourceHash));
    Assert.False(revokedOwner.Succeeded);
    Assert.Equal(1, await db.AuditProgramVersions.CountAsync());
  }

  [Fact(DisplayName = "Audit program publishes, adopts, executes and independently reviews the controlled catalog")]
  public async Task ControlledCatalog_UsesScopedAppendOnlyWorkflow()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using var db = new AuditSphereDbContext(pg.Options);
    var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));

    Assert.True(published.Succeeded);
    Assert.Equal(165, published.Value!.ProcedureCount);
    Assert.True(AuditProgramCatalog.IsComplete);
    Assert.Equal(165, await db.AuditProgramProcedures.CountAsync());

    var idempotent = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(idempotent.Succeeded);
    Assert.Equal(published.Value.ProgramVersionId, idempotent.Value!.ProgramVersionId);
    Assert.Equal(165, await db.AuditProgramProcedures.CountAsync());

    var options = await EngagementAuditProgramQuery.GetOptionsAsync(db, scope.Actor, scope.EngagementId);
    Assert.True(options.Succeeded, options.Message);
    var option = Assert.Single(options.Value!.Options, x => x.Version == "2026.1" && x.Eligible);
    Assert.Equal(AuditProgramCatalog.ContentDigest, option.ContentDigest);
    var adoptRequest = new AdoptAuditProgramRequest(scope.EngagementId, option.ProgramVersionId,
      option.SourceHash, option.ContentDigest, options.Value.CurrentAdoptionId);
    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor, adoptRequest);
    Assert.True(adopted.Succeeded);
    Assert.Equal(165, adopted.Value!.ProcedureCount);
    Assert.Equal(165, adopted.Value.BaselineProcedureCount);
    Assert.Equal(0, adopted.Value.AdHocProcedureCount);
    var adoptionReplay = await AuditProgramService.AdoptAsync(db, scope.Actor,
      adoptRequest with { ExpectedAdoptionId = adopted.Value.EngagementProgramId });
    Assert.True(adoptionReplay.Succeeded, adoptionReplay.Message);
    Assert.Equal(adopted.Value.EngagementProgramId, adoptionReplay.Value!.EngagementProgramId);
    var staleSource = await AuditProgramService.AdoptAsync(db, scope.Actor,
      adoptRequest with { ExpectedSourceHash = new string('0', 64) });
    Assert.False(staleSource.Succeeded);
    var secondVersion = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.2", AuditProgramCatalog.SourceHash));
    Assert.True(secondVersion.Succeeded, secondVersion.Message);
    var versionConflict = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, secondVersion.Value!.ProgramVersionId,
        secondVersion.Value.SourceHash, secondVersion.Value.ContentDigest, adopted.Value.EngagementProgramId));
    Assert.False(versionConflict.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, versionConflict.ErrorCode);
    Assert.Equal(165, await db.AuditProcedures.CountAsync(x => x.EngagementId == scope.EngagementId));
    Assert.Equal(0, await db.AuditProcedures.CountAsync(x => x.EngagementId == scope.EngagementId && x.RiskId != null));
    Assert.False((await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(fixture.Other.EngagementId, published.Value.ProgramVersionId))).Succeeded);

    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-01");
    var decided = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityStatuses.Applicable, null,
        procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.True(decided.Succeeded);

    // Substantive result submission requires the approved planning basis (STE-REM-04): sealed balanced
    // TB, approved mapping and current independently approved materiality, through the real commands.
    var basisApproverId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = basisApproverId, FirmId = scope.FirmId, Subject = "basis-approver-" + basisApproverId.ToString("N"),
      TenantId = "tenant-planning", Email = "basis-approver@example.test", DisplayName = "Planning Approver",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = basisApproverId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = basisApproverId
    });
    await db.SaveChangesAsync();
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor,
      new ActorContext(basisApproverId, scope.FirmId, 1, ["Partner"]));

    var submitted = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(procedure.Id, 1, "Agreed the bank listing to the ledger.",
        "{\"result\":\"PASS\"}", ["source:bank-list-1"], "No exception noted."));
    Assert.True(submitted.Succeeded);
    Assert.Equal(1, submitted.Value!.Revision);

    var reviewerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = reviewerId,
      FirmId = scope.FirmId,
      Subject = "reviewer-" + reviewerId.ToString("N"),
      TenantId = "tenant-planning",
      Email = "reviewer@example.test",
      DisplayName = "Independent Reviewer",
      UserKind = "Staff",
      SessionEpoch = 1,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();

    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer"]);
    var reviewed = await AuditProgramService.ReviewResultAsync(db, reviewer,
      new ReviewProcedureResultRequest(submitted.Value.AuditProcedureResultId,
        AuditProcedureReviewDecisions.Reviewed, "Evidence and conclusion agree."));
    Assert.True(reviewed.Succeeded);
    Assert.Equal(AuditProcedureStatuses.Reviewed,
      (await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id)).Status);
    Assert.Equal(1, await db.AuditProcedureReviews.CountAsync(x => x.AuditProcedureId == procedure.Id));

    var second = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-02");
    var secondDecision = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(second.Id, AuditApplicabilityStatuses.Applicable, null,
        second.CurrentApplicabilityRevision, second.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.True(secondDecision.Succeeded);
    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_safety_states SET input_generation = 2 WHERE id = {scope.ClientId}");
    var stale = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(second.Id, 1, "Stale input check.", "{\"result\":\"PASS\"}",
        ["source:bank-list-2"], "No exception noted."));
    Assert.False(stale.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
  }

  [Fact(DisplayName = "Library query lists versions, sections and paged procedures with search")]
  public async Task LibraryQuery_BrowsesVersionsSectionsAndProcedures()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    Guid versionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(published.Succeeded, published.Message);
      versionId = published.Value!.ProgramVersionId;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var library = await AuditProgramLibraryQuery.GetLibraryAsync(db, scope.Actor);
      Assert.True(library.Succeeded, library.Message);
      var selected = library.Value!.SelectedVersion;
      Assert.NotNull(selected);
      Assert.Equal("2026.1", selected!.Version);
      Assert.Equal(165, selected.ProcedureCount);
      Assert.Equal(20, library.Value.Sections.Count);
      Assert.All(library.Value.Sections, s => Assert.True(s.ProcedureCount > 0));
      // Sections are ordered and carry the exact source titles.
      Assert.Equal(1, library.Value.Sections[0].SectionNumber);
      Assert.Equal("Planning & Risk Assessment", library.Value.Sections[0].SectionTitle);
      Assert.Equal(20, library.Value.Sections[^1].SectionNumber);

      var section = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 2);
      Assert.True(section.Succeeded, section.Message);
      Assert.Equal("Cash & Bank", section.Value!.SectionTitle);
      Assert.Equal(8, section.Value.TotalCount);
      Assert.Equal("AWP-02-01", section.Value.Items[0].SourceProcedureId);
      Assert.Equal("Obtain bank reconciliation for all bank accounts at year-end.",
        section.Value.Items[0].SourceWording);

      // Paging is honoured.
      var firstPage = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 2, pageSize: 3);
      Assert.Equal(8, firstPage.Value!.TotalCount);
      Assert.Equal(3, firstPage.Value.Items.Count);

      // Search matches the source id and the exact wording.
      var byId = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 2, search: "AWP-02-0");
      Assert.Equal(8, byId.Value!.TotalCount);
      var byWording = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 2, search: "confirmations");
      Assert.Equal(1, byWording.Value!.TotalCount);
      Assert.Equal("AWP-02-04", byWording.Value.Items[0].SourceProcedureId);
      var noMatch = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 2, search: "zzz-not-present");
      Assert.Equal(0, noMatch.Value!.TotalCount);

      // Invalid section numbers and page sizes are rejected before any query.
      var badSection = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 21);
      Assert.False(badSection.Succeeded);
      var badPage = await AuditProgramLibraryQuery.GetSectionAsync(db, scope.Actor, versionId, 2, pageSize: 900);
      Assert.False(badPage.Succeeded);
    }
  }

  [Fact(DisplayName = "Library query denies a foreign firm and returns an empty view without versions")]
  public async Task LibraryQuery_IsFirmScopedAndHandlesNoVersions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var empty = await AuditProgramLibraryQuery.GetLibraryAsync(db, scope.Actor);
      Assert.True(empty.Succeeded, empty.Message);
      Assert.Empty(empty.Value!.Versions);
      Assert.Null(empty.Value.SelectedVersion);

      var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(published.Succeeded, published.Message);

      // A foreign firm's actor is denied outright (fail closed), and cannot read a
      // version id belonging to another firm.
      var foreign = new ActorContext(fixture.Other.Actor.UserId, Guid.NewGuid(),
        fixture.Other.Actor.SessionEpoch, ["Partner"]);
      var foreignLibrary = await AuditProgramLibraryQuery.GetLibraryAsync(db, foreign);
      Assert.False(foreignLibrary.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreignLibrary.ErrorCode);

      // An internal actor without a covering grant for the firm is also denied.
      var ungranted = new ActorContext(scope.Actor.UserId, scope.Actor.FirmId,
        scope.Actor.SessionEpoch, ["ClientUser"]);
      var ungrantedLibrary = await AuditProgramLibraryQuery.GetLibraryAsync(db, ungranted);
      Assert.False(ungrantedLibrary.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, ungrantedLibrary.ErrorCode);
    }
  }

  [Fact(DisplayName = "Engagement program query reports tailoring, status and section coverage")]
  public async Task EngagementProgramQuery_ReportsTailoredCoverage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    Guid programVersionId, procedureId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(published.Succeeded, published.Message);
      programVersionId = published.Value!.ProgramVersionId;
      Assert.True((await AuditProgramService.AdoptAsync(db, scope.Actor,
        new AdoptAuditProgramRequest(scope.EngagementId, programVersionId))).Succeeded);

      // Tailor one procedure out of scope in the cash section.
      var cash = await db.AuditProcedures.SingleAsync(x =>
        x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-02-04");
      procedureId = cash.Id;
      Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
        new DecideProcedureApplicabilityRequest(cash.Id, AuditApplicabilityStatuses.Applicable,
          "Bank confirmations are required for this engagement.", cash.CurrentApplicabilityRevision,
          cash.CurrentDefinitionRevision, Guid.NewGuid()))).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Cross-engagement and foreign-firm access fails closed.
      var denied = await EngagementAuditProgramQuery.GetProceduresAsync(db, scope.Actor, fixture.Other.EngagementId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      var page = await EngagementAuditProgramQuery.GetProceduresAsync(db, scope.Actor, scope.EngagementId);
      Assert.True(page.Succeeded, page.Message);
      Assert.Equal(165, page.Value!.TotalCount);
      var cashProcedure = page.Value.Items.Single(x => x.SourceProcedureId == "AWP-02-04");
      Assert.Equal(2, cashProcedure.SectionNumber);
      Assert.Equal("Cash & Bank", cashProcedure.SectionTitle);
      Assert.Equal(AuditApplicabilityStatuses.Applicable, cashProcedure.ApplicabilityStatus);
      Assert.Equal("Bank confirmations are required for this engagement.", cashProcedure.ApplicabilityRationale);
      Assert.Equal(4, cashProcedure.Ordinal);
      Assert.Equal("Obtain direct bank confirmations and reconcile confirmed balances.", cashProcedure.SourceWording);

      // Section filter narrows the page to the eight cash procedures.
      var cashOnly = await EngagementAuditProgramQuery.GetProceduresAsync(db, scope.Actor, scope.EngagementId, sectionNumber: 2);
      Assert.Equal(8, cashOnly.Value!.TotalCount);
      Assert.All(cashOnly.Value.Items, x => Assert.Equal(2, x.SectionNumber));

      var program = await EngagementAuditProgramQuery.GetProgramAsync(db, scope.Actor, scope.EngagementId);
      Assert.True(program.Succeeded, program.Message);
      Assert.Equal(programVersionId, program.Value!.ProgramVersionId);
      Assert.Equal(AuditProgramCatalog.ProgramCode, program.Value.ProgramCode);
      Assert.Equal("2026.1", program.Value.ProgramVersion);
      Assert.Equal(165, program.Value.TotalProcedures);
      Assert.Equal(20, program.Value.Sections.Count);
      var cashCoverage = program.Value.Sections.Single(x => x.SectionNumber == 2);
      Assert.Equal(8, cashCoverage.Total);
      Assert.Equal(1, cashCoverage.Applicable);
      Assert.Equal(7, cashCoverage.PendingDecision);
    }
  }

  [Fact(DisplayName = "Procedure tailoring and workpaper generation enforce concurrency, lifecycle and invalidation")]
  public async Task ProcedureTailoringAndWorkpaperGeneration_EnforcesLifecycleAndInvalidation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;

    await using var db = new AuditSphereDbContext(pg.Options);
    var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded);

    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, published.Value!.ProgramVersionId));
    Assert.True(adopted.Succeeded);

    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-02-01");
    Assert.Equal(AuditProcedureStatuses.Planned, procedure.Status);

    // Make procedure applicable
    Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityStatuses.Applicable, "Mandatory cash test",
        procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()))).Succeeded);

    // Initial workpaper generation initializes the workpaper and advances procedure to InProgress
    var wpResult = await AuditProgramService.GetOrCreateWorkpaperAsync(db, scope.Actor, procedure.Id);
    Assert.True(wpResult.Succeeded, wpResult.Message);
    var workpaperId = wpResult.Value;

    var procDb = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id);
    Assert.Equal(AuditProcedureStatuses.InProgress, procDb.Status);

    // Substantive submission later requires the approved planning basis (STE-REM-04).
    var basisApproverId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = basisApproverId, FirmId = scope.FirmId, Subject = "basis-approver-" + basisApproverId.ToString("N"),
      TenantId = "tenant-planning", Email = "basis-approver-tailoring@example.test", DisplayName = "Planning Approver",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = basisApproverId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = basisApproverId
    });
    await db.SaveChangesAsync();
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor,
      new ActorContext(basisApproverId, scope.FirmId, 1, ["Partner"]));

    // Idempotent call returns the same workpaper ID
    var wpRepeat = await AuditProgramService.GetOrCreateWorkpaperAsync(db, scope.Actor, procedure.Id);
    Assert.True(wpRepeat.Succeeded);
    Assert.Equal(workpaperId, wpRepeat.Value);

    // Cross-engagement actor cannot generate or access workpaper
    var foreignUser = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, Subject = "foreign-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-planning", Email = "foreign@example.test", DisplayName = "Foreign Senior",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(foreignUser);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = foreignUser.Id, Role = "Senior",
      ClientId = fixture.Other.ClientId, EngagementId = fixture.Other.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = foreignUser.Id
    });
    await db.SaveChangesAsync();
    var foreignActor = new ActorContext(foreignUser.Id, scope.FirmId, 1, ["Senior"]);

    var crossResult = await AuditProgramService.GetOrCreateWorkpaperAsync(db, foreignActor, procedure.Id);
    Assert.False(crossResult.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, crossResult.ErrorCode);

    // Tailoring appends effective instructions and preserves the exact controlled source text.
    var originalWording = procedure.SourceWording;
    var tailorCommand = Guid.NewGuid();
    var tailorReq = new TailorProcedureRequest(procedure.Id, "Custom Tailored Cash Review",
      "Perform detailed cash count and trace to general ledger.", "High-risk client location", 1, tailorCommand);
    var tailorResult = await AuditProgramService.TailorProcedureAsync(db, scope.Actor, tailorReq);
    Assert.True(tailorResult.Succeeded, tailorResult.Message);

    var updatedProc = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id);
    Assert.Equal("Custom Tailored Cash Review", updatedProc.Title);
    Assert.Equal(originalWording, updatedProc.SourceWording);
    Assert.Equal(2, updatedProc.CurrentDefinitionRevision);
    Assert.Equal("Mandatory cash test", updatedProc.ApplicabilityRationale);
    Assert.Equal(0, updatedProc.CurrentResultRevision);
    var source = await db.AuditProgramProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.ProgramProcedureId);
    Assert.Equal(source.SourceWording, originalWording);
    var firstDefinition = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleAsync(x =>
      x.AuditProcedureId == procedure.Id && x.Revision == 1);
    var tailoredDefinition = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleAsync(x =>
      x.AuditProcedureId == procedure.Id && x.Revision == 2);
    Assert.Equal(firstDefinition.Id, tailoredDefinition.PreviousRevisionId);
    Assert.Equal("Perform detailed cash count and trace to general ledger.", tailoredDefinition.EffectiveWording);

    // An exact retry returns the retained command, while changed content under the same identity conflicts.
    Assert.True((await AuditProgramService.TailorProcedureAsync(db, scope.Actor, tailorReq)).Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditProgramService.TailorProcedureAsync(db, scope.Actor,
      tailorReq with { CustomWording = "Changed request with reused id" })).ErrorCode);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditProgramService.TailorProcedureAsync(db, scope.Actor,
      tailorReq with { CommandId = Guid.NewGuid() })).ErrorCode);
    Assert.Equal(2, await db.AuditProcedureDefinitionRevisions.CountAsync(x => x.AuditProcedureId == procedure.Id));

    // Submit procedure result
    Assert.True((await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(procedure.Id, 1, "Completed count successfully.", "{\"count\": 5000}", ["cash-doc-1"], "Audit objective met."))).Succeeded);

    var submittedProc = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id);
    Assert.Equal(AuditProcedureStatuses.Submitted, submittedProc.Status);

    var submitted = await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.AuditProcedureId == procedure.Id && x.Revision == 1);
    Assert.Equal(tailoredDefinition.Id, submitted.DefinitionRevisionId);

    // New wording stales that exact result without fabricating a result revision or changing its evidence.
    var reTailorReq = new TailorProcedureRequest(procedure.Id, "Custom Tailored Cash Review v2",
      "Updated testing procedures after management query.", "Revised scope", 2, Guid.NewGuid());
    Assert.True((await AuditProgramService.TailorProcedureAsync(db, scope.Actor, reTailorReq)).Succeeded);

    var reTailoredProc = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id);
    Assert.Equal(AuditProcedureStatuses.InProgress, reTailoredProc.Status);
    Assert.Equal(1, reTailoredProc.CurrentResultRevision);
    Assert.Equal(3, reTailoredProc.CurrentDefinitionRevision);
    var reviewer = await AuditProgramService.ReviewResultAsync(db, scope.Actor, new(submitted.Id, "REVIEWED", null));
    Assert.Equal(ErrorCodes.GenerationStale, reviewer.ErrorCode);
    var retained = await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == submitted.Id);
    Assert.Null(retained.ReviewedAt);
    var currentEvidence = await AuditFieldworkService.EvaluateProcedureEvidenceForAuthorizedScopeAsync(
      db, scope.FirmId, scope.ClientId, scope.EngagementId, procedure.Id, CancellationToken.None);
    Assert.Equal("STALE_DEFINITION", currentEvidence.State);
    var resubmitted = await AuditProgramService.SubmitResultAsync(db, scope.Actor,
      new SubmitProcedureResultRequest(procedure.Id, 1, "Repeated the tests under the revised instructions.",
        "{\"count\": 5000, \"followUp\": true}", ["cash-doc-2"], "Revised objective met."));
    Assert.True(resubmitted.Succeeded, resubmitted.Message);
    var currentResult = await db.AuditProcedureResults.AsNoTracking().SingleAsync(x => x.Id == resubmitted.Value!.AuditProcedureResultId);
    var latestDefinition = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleAsync(x =>
      x.AuditProcedureId == procedure.Id && x.Revision == 3);
    Assert.Equal(latestDefinition.Id, currentResult.DefinitionRevisionId);
    var currentEvidenceAfterResubmit = await AuditFieldworkService.EvaluateProcedureEvidenceForAuthorizedScopeAsync(
      db, scope.FirmId, scope.ClientId, scope.EngagementId, procedure.Id, CancellationToken.None);
    Assert.Equal("MISSING_REVIEW", currentEvidenceAfterResubmit.State);

    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE audit_procedure_definition_revisions SET rationale = rationale WHERE id = {tailoredDefinition.Id}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM audit_procedure_definition_revisions WHERE id = {tailoredDefinition.Id}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE audit_procedures SET source_wording = 'legacy writer must be rejected' WHERE id = {procedure.Id}"));
  }

  [Fact(DisplayName = "N/A requires a reason and an independent Partner decision tied to current inputs")]
  public async Task ApplicabilityProposal_RequiresIndependentCurrentPartnerAndRetainsHistory()
  {
    var legacyPending = new AuditProcedure
    {
      ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicablePendingReview,
      CurrentApplicabilityRevision = 0
    };
    Assert.Equal(AuditApplicabilityStatuses.NotApplicableUnverified,
      AuditProgramService.EffectiveApplicabilityStatus(legacyPending, null, null, 1));

    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    await using var db = new AuditSphereDbContext(pg.Options);
    var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, published.Value!.ProgramVersionId));
    Assert.True(adopted.Succeeded, adopted.Message);

    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId &&
      x.SourceProcedureId == "AWP-08-01");
    var directApproval = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityStatuses.NotApplicableApproved,
        "unsupported direct approval", 0, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.False(directApproval.Succeeded);
    Assert.Empty(await db.AuditProcedureApplicabilityDecisions.ToListAsync());

    var missingReason = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.ProposeNotApplicable,
        "  ", 0, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.False(missingReason.Succeeded);

    var proposalRequest = new DecideProcedureApplicabilityRequest(procedure.Id,
      AuditApplicabilityActions.ProposeNotApplicable,
      "The client does not hold inventory; the procedure is outside this engagement's agreed scope.",
      procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid());
    var proposal = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor, proposalRequest);
    Assert.True(proposal.Succeeded, proposal.Message);
    Assert.Equal(AuditApplicabilityStatuses.NotApplicablePendingReview, proposal.Value!.Status);

    var partnerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = partnerId, FirmId = scope.FirmId, Subject = "independent-na-" + partnerId.ToString("N"),
      TenantId = "tenant-planning", Email = "independent-na@example.test", DisplayName = "Independent Partner",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = partnerId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();
    var partner = new ActorContext(partnerId, scope.FirmId, 1, ["Partner"]);

    var managerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = managerId, FirmId = scope.FirmId, Subject = "na-manager-" + managerId.ToString("N"),
      TenantId = "tenant-planning", Email = "na-manager@example.test", DisplayName = "Engagement Manager",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = managerId, Role = "Manager",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();
    var manager = new ActorContext(managerId, scope.FirmId, 1, ["Manager"]);
    var wrongRole = await AuditProgramService.DecideApplicabilityAsync(db, manager,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.ApproveNotApplicable,
        null, proposal.Value.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid(),
        proposal.Value.DecisionId));
    Assert.False(wrongRole.Succeeded);

    var workspace = await AuditFieldworkWorkspaceQuery.GetAsync(db, partner, scope.EngagementId);
    Assert.True(workspace.Succeeded, workspace.Message);
    var queued = Assert.Single(workspace.Value!.Procedures, x => x.Id == procedure.Id);
    Assert.True(queued.CanApproveNotApplicable);
    Assert.True(queued.CanReturnNotApplicable);
    Assert.Equal(proposal.Value.DecisionId, queued.PendingApplicabilityProposalId);
    Assert.Contains("does not hold inventory", queued.ApplicabilityRationale);

    var selfApproval = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.ApproveNotApplicable,
        null, proposal.Value.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid(),
        proposal.Value.DecisionId));
    Assert.False(selfApproval.Succeeded);
    var missingProposal = await AuditProgramService.DecideApplicabilityAsync(db, partner,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.ApproveNotApplicable,
        null, proposal.Value.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.False(missingProposal.Succeeded);

    var returned = await AuditProgramService.DecideApplicabilityAsync(db, partner,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.ReturnNotApplicable,
        "The file needs the current inventory schedule before this exclusion can be assessed.",
        proposal.Value.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid(),
        proposal.Value.DecisionId));
    Assert.True(returned.Succeeded, returned.Message);
    Assert.Equal(AuditApplicabilityStatuses.Pending, returned.Value!.Status);
    Assert.Equal(AuditApplicabilityActions.ReturnNotApplicable, returned.Value.Action);
    var returnEvidence = await db.AuditProcedureApplicabilityDecisions.AsNoTracking().SingleAsync(x =>
      x.Id == returned.Value.DecisionId);
    Assert.Equal(partner.UserId, returnEvidence.ActorUserId);
    Assert.Equal(proposal.Value.DecisionId, returnEvidence.ProposalDecisionId);
    Assert.Contains("needs the current inventory schedule", returnEvidence.Rationale);

    var reproposal = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.ProposeNotApplicable,
        "The accepted inventory schedule confirms there is no inventory balance or activity in the period.",
        returned.Value.ApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.True(reproposal.Succeeded, reproposal.Message);

    var approvalRequest = new DecideProcedureApplicabilityRequest(procedure.Id,
      AuditApplicabilityActions.ApproveNotApplicable, null, reproposal.Value!.ApplicabilityRevision,
      procedure.CurrentDefinitionRevision, Guid.NewGuid(), reproposal.Value.DecisionId);
    var approval = await AuditProgramService.DecideApplicabilityAsync(db, partner, approvalRequest);
    Assert.True(approval.Succeeded, approval.Message);
    Assert.Equal(AuditApplicabilityStatuses.NotApplicableApproved, approval.Value!.Status);
    var replay = await AuditProgramService.DecideApplicabilityAsync(db, partner, approvalRequest);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(approval.Value.DecisionId, replay.Value!.DecisionId);
    var changedIntent = await AuditProgramService.DecideApplicabilityAsync(db, partner,
      approvalRequest with { Decision = AuditApplicabilityActions.ReturnNotApplicable,
        Rationale = "Different intent on an already-used command identity." });
    Assert.Equal(ErrorCodes.IdempotencyConflict, changedIntent.ErrorCode);
    Assert.Equal(4, await db.AuditProcedureApplicabilityDecisions.CountAsync(x => x.AuditProcedureId == procedure.Id));

    await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_safety_states SET input_generation = input_generation + 1 WHERE id = {scope.ClientId}");
    var refreshedProcedure = await db.AuditProcedures.AsNoTracking().SingleAsync(x => x.Id == procedure.Id);
    var current = await AuditProgramService.CurrentApplicabilityDecisionsAsync(db, scope.FirmId, scope.ClientId,
      scope.EngagementId, [refreshedProcedure], CancellationToken.None);
    var proposalRows = await AuditProgramService.ProposalDecisionsAsync(db, scope.FirmId, scope.ClientId,
      scope.EngagementId, current.Values.ToArray(), CancellationToken.None);
    Assert.Equal(AuditApplicabilityStatuses.NotApplicableUnverified,
      AuditProgramService.EffectiveApplicabilityStatus(refreshedProcedure, current[procedure.Id],
        proposalRows[reproposal.Value.DecisionId!.Value], 2));
    var completion = await AuditFieldworkService.EvaluateCompletionForAuthorizedScopeAsync(db,
      scope.FirmId, scope.ClientId, scope.EngagementId, CancellationToken.None);
    Assert.Contains(completion.Value!.Blockers, x => x == $"applicability:{procedure.SourceProcedureId}");

    var staleDefinition = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.Applicable,
        "Current accepted inventory evidence establishes applicability.", procedure.CurrentApplicabilityRevision,
        procedure.CurrentDefinitionRevision + 1, Guid.NewGuid()));
    Assert.False(staleDefinition.Succeeded);
    var applicable = await AuditProgramService.DecideApplicabilityAsync(db, scope.Actor,
      new DecideProcedureApplicabilityRequest(procedure.Id, AuditApplicabilityActions.Applicable,
        "Current accepted inventory evidence establishes applicability.", procedure.CurrentApplicabilityRevision,
        procedure.CurrentDefinitionRevision, Guid.NewGuid()));
    Assert.True(applicable.Succeeded, applicable.Message);
    Assert.Equal(AuditApplicabilityStatuses.Applicable, applicable.Value!.Status);
    Assert.Equal(5, await db.AuditProcedureApplicabilityDecisions.CountAsync(x => x.AuditProcedureId == procedure.Id));

    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE audit_procedure_applicability_decisions SET rationale = 'tampered' WHERE id = {proposal.Value.DecisionId}"));
  }

  [Fact(DisplayName = "Procedure command receipts recover one exact submit and review outcome")]
  public async Task ProcedureCommand_ExactPreviewAndReceiptRecoverOriginalOutcome()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    await using var db = new AuditSphereDbContext(pg.Options);
    var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, published.Value!.ProgramVersionId));
    Assert.True(adopted.Succeeded, adopted.Message);
    var procedure = await db.AuditProcedures.SingleAsync(x =>
      x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-08-01");
    Assert.True((await AuditProgramTestHelpers.MarkApplicableAsync(db, scope.Actor, procedure.Id)).Succeeded);

    var basisApproverId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = basisApproverId, FirmId = scope.FirmId, Subject = "command-basis-" + basisApproverId.ToString("N"),
      TenantId = "tenant-planning", Email = "command-basis@example.test", DisplayName = "Command Basis Approver",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = basisApproverId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = basisApproverId
    });
    await db.SaveChangesAsync();
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor,
      new ActorContext(basisApproverId, scope.FirmId, 1, ["Partner"]));

    var state = await AuditProcedureCommandWorkspace.StateAsync(db, scope.Actor, procedure.Id);
    Assert.True(state.Succeeded, state.Message);
    var basis = state.Value!.Basis;
    Assert.True(basis.CanSubmit);

    var request = new ProcedureCommandRequest(Guid.NewGuid(), ProcedureCommandActions.SubmitResult,
      basis.DefinitionRevision, basis.InputGeneration, basis.CurrentResultRevision, null,
      "Agreed the bank listing to the ledger.", "{\"result\":\"PASS\"}", ["source:bank-list-1"],
      "No exception noted.", null, null, false, null);
    var preview = await AuditProcedureCommandWorkspace.PreviewAsync(db, scope.Actor, procedure.Id, request);
    Assert.True(preview.Succeeded, preview.Message);
    var action = request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash };
    var receipt = await AuditProcedureCommandWorkspace.ExecuteAsync(db, scope.Actor, procedure.Id, action);
    Assert.True(receipt.Succeeded, receipt.Message);
    Assert.Equal(ProcedureCommandActions.SubmitResult, receipt.Value!.Action);
    Assert.Equal(1, receipt.Value.ResultRevision);
    Assert.NotNull(receipt.Value.ResultId);

    var replay = await AuditProcedureCommandWorkspace.ExecuteAsync(db, scope.Actor, procedure.Id, action);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(receipt.Value.Id, replay.Value!.Id);
    Assert.Equal(receipt.Value.ResultId, replay.Value.ResultId);
    Assert.Equal(1, await db.AuditProcedureCommandReceipts.CountAsync(x => x.AuditProcedureId == procedure.Id));

    var changed = await AuditProcedureCommandWorkspace.ExecuteAsync(db, scope.Actor, procedure.Id,
      action with { Conclusion = "Different conclusion on the same request identity." });
    Assert.False(changed.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, changed.ErrorCode);

    var lookup = await AuditProcedureCommandWorkspace.LookupAsync(db, scope.Actor, procedure.Id,
      receipt.Value.RequestId, receipt.Value.RequestHash);
    Assert.True(lookup.Succeeded, lookup.Message);
    Assert.True(lookup.Value!.Found);
    Assert.Equal(receipt.Value.Id, lookup.Value.Receipt!.Id);
    var wrongHash = await AuditProcedureCommandWorkspace.LookupAsync(db, scope.Actor, procedure.Id,
      receipt.Value.RequestId, new string('0', 64));
    Assert.False(wrongHash.Succeeded);
    Assert.Equal(ErrorCodes.IdempotencyConflict, wrongHash.ErrorCode);

    var reviewer = await AuditProgramTestHelpers.CreateReviewerAsync(db, scope.FirmId, scope.Actor.UserId);
    var reviewState = await AuditProcedureCommandWorkspace.StateAsync(db, reviewer, procedure.Id);
    Assert.True(reviewState.Succeeded, reviewState.Message);
    Assert.True(reviewState.Value!.Basis.CanReview);
    var reviewRequest = new ProcedureCommandRequest(Guid.NewGuid(), ProcedureCommandActions.ReviewResult,
      reviewState.Value.Basis.DefinitionRevision, reviewState.Value.Basis.InputGeneration,
      reviewState.Value.Basis.LatestResultRevision, reviewState.Value.Basis.LatestResultId,
      null, null, null, null, AuditProcedureReviewDecisions.Reviewed, "Evidence and conclusion agree.", false, null);
    var reviewPreview = await AuditProcedureCommandWorkspace.PreviewAsync(db, reviewer, procedure.Id, reviewRequest);
    Assert.True(reviewPreview.Succeeded, reviewPreview.Message);
    var reviewAction = reviewRequest with { Reviewed = true, ExpectedRequestHash = reviewPreview.Value!.RequestHash };
    var reviewReceipt = await AuditProcedureCommandWorkspace.ExecuteAsync(db, reviewer, procedure.Id, reviewAction);
    Assert.True(reviewReceipt.Succeeded, reviewReceipt.Message);
    Assert.Equal(ProcedureCommandActions.ReviewResult, reviewReceipt.Value!.Action);
    Assert.Equal(AuditProcedureReviewDecisions.Reviewed, reviewReceipt.Value.Decision);
    Assert.NotNull(reviewReceipt.Value.ReviewId);
    var reviewReplay = await AuditProcedureCommandWorkspace.ExecuteAsync(db, reviewer, procedure.Id, reviewAction);
    Assert.True(reviewReplay.Succeeded, reviewReplay.Message);
    Assert.Equal(reviewReceipt.Value.Id, reviewReplay.Value!.Id);
    Assert.Equal(2, await db.AuditProcedureCommandReceipts.CountAsync(x => x.AuditProcedureId == procedure.Id));
    Assert.Equal(1, await db.AuditProcedureReviews.CountAsync(x => x.AuditProcedureId == procedure.Id));

    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE audit_procedure_command_receipts SET result_status = 'TAMPERED' WHERE id = {receipt.Value.Id}"));
  }

}
