using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 24/25 fail-closed gates: blocking package validations refuse the
/// VALIDATED state, package decisions and release readiness, and review stages run in
/// order with one person per stage per package version.</summary>
public sealed class FinancialPackageGateTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId, Guid PeriodId, Guid BookId,
    AppUser Preparer, AppUser Reviewer, AppUser Partner);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task BlockingValidationFailure_DemotesPackageAndBlocksDecisionsAndReadiness()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId,
      fixture.EngagementId, fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "blocked-equity");
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    // Equity profit/OCI amounts deliberately disagree with the mapped income totals,
    // while the per-line rollforward still reconciles, so only EQUITY_PROFIT blocks.
    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "gate-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-gate"], ClassificationRationale: "Synthetic receipts agreed to source.",
          SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 60m, 0m, 0m, 0m, 60m, "equity-gate-1")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-gate"], ClosingCashEvidenceReferences: ["closing-cash-gate"])
    };
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
      Assert.Equal(AccountingPackageStates.PackageReviewRequired, built.Value.Status);
      var validations = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync();
      Assert.Contains(validations, x => x.Code == "EQUITY_PROFIT" && !x.Passed);
      Assert.DoesNotContain(validations, x => !x.Passed && x.Code != "EQUITY_PROFIT" &&
        FinancialPackageReviewService.BlockingValidationCodes.Contains(x.Code));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "gate-management", "Management approval attempt."));
      Assert.False(management.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, management.ErrorCode);
      Assert.Contains("EQUITY_PROFIT", management.Message, StringComparison.Ordinal);
    }

    // Defense in depth: a pre-gate legacy row already carrying VALIDATED with a
    // failed blocking validation (financial_packages is append-only, so clone it in)
    // is still refused by the decision, readiness and queue paths.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var legacyId = Guid.CreateVersion7();
      var legacyHash = Hashing.Sha256Hex("legacy-gate-package");
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO financial_packages (id, firm_id, client_id, engagement_id, adjusted_dataset_id, mapping_version_id,
          adjustment_plan_id, period_id, book_id, basis, framework, period_start, period_end, taxonomy_version,
          template_version, calculation_engine_version, calculation_hash, currency, revision, generation, status, created_at)
        SELECT {legacyId}, firm_id, client_id, engagement_id, adjusted_dataset_id, mapping_version_id,
          adjustment_plan_id, period_id, book_id, basis, framework, period_start, period_end, taxonomy_version,
          'legacy-gate-template', calculation_engine_version, {legacyHash}, currency, revision, generation,
          'VALIDATED', created_at
        FROM financial_packages WHERE id = {packageId}
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO financial_package_validations (id, firm_id, financial_package_id, code, passed, detail, created_at)
        SELECT {Guid.CreateVersion7()}, firm_id, {legacyId}, 'ACCOUNTING_EQUATION', false,
          'Legacy validation failure.', created_at
        FROM financial_package_validations WHERE financial_package_id = {packageId} LIMIT 1
        """);
      var partner = Actor(fixture.Partner, "Partner");
      var queue = await FinancialPackageReviewService.GetStaffQueueAsync(db, partner);
      Assert.True(queue.Succeeded, queue.Message);
      Assert.DoesNotContain(queue.Value!, x => x.PackageId == legacyId);

      var reviewable = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, legacyId, requirePartner: true);
      Assert.False(reviewable.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, reviewable.ErrorCode);
      Assert.Contains("ACCOUNTING_EQUATION", reviewable.Message!, StringComparison.Ordinal);

      var decision = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(legacyId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "gate-partner", "Partner attempt on a legacy blocked package."));
      Assert.False(decision.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, decision.ErrorCode);
      Assert.Contains("ACCOUNTING_EQUATION", decision.Message!, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PackageDecisions_RequireStageOrderAndDistinctPersons()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var partner = Actor(fixture.Partner, "Partner");
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId,
      fixture.EngagementId, fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "stage-order");
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "stage-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-stage"], ClassificationRationale: "Synthetic receipts agreed to source.",
          SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-stage-1")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-stage"], ClosingCashEvidenceReferences: ["closing-cash-stage"],
        FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "cash-population-stage",
          "The selected synthetic cash population has no foreign-currency effect."))
    };
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value.Status);
      var sourceReview = await CashFlowSourceReviewCall.RecordAsync(
        db, reviewer, packageId, "APPROVED", null);
      Assert.True(sourceReview.Succeeded, sourceReview.Message);
      var rendered = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(rendered.Succeeded, rendered.Message);
    }

    // An administrator with no Partner grant is a different person with different authority: administration is not professional Partner work.
    var administrator = User(fixture.FirmId, "administrator");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(administrator);
      db.RoleGrants.Add(Grant(fixture.FirmId, administrator, "Administrator"));
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var skippedManagement = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-accounting", "Accounting review before management approval."));
      Assert.False(skippedManagement.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, skippedManagement.ErrorCode);
      Assert.Contains("MANAGEMENT_APPROVAL", skippedManagement.Message!, StringComparison.Ordinal);

      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "stage-management", "Management approved the exact package."));
      Assert.True(management.Succeeded, management.Message);

      var accounting = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-accounting", "Accounting reviewed the exact package."));
      Assert.True(accounting.Succeeded, accounting.Message);

      var selfPartner = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-partner", "Same person switching roles to partner."));
      Assert.False(selfPartner.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, selfPartner.ErrorCode);
      Assert.Contains("two different stages", selfPartner.Message!, StringComparison.Ordinal);

      var administratorPartner = await FinancialPackageReviewService.RecordAsync(db, Actor(administrator, "Administrator"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-admin-partner", "Administrator attempting the professional Partner decision."));
      Assert.False(administratorPartner.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, administratorPartner.ErrorCode);

      var partnerApproval = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "stage-partner", "Independent partner approval."));
      Assert.True(partnerApproval.Succeeded, partnerApproval.Message);

      var ready = await FinancialPackageReviewService.RequireCurrentAsync(db, partner, packageId, requirePartner: true);
      Assert.False(ready.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, ready.ErrorCode);
      Assert.Contains("three-format composition", ready.Message!, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReviewQueue_ContinuesPastFilteredCandidatesWithStableScopedPages()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (fixture, packageId, _, _) = await PackageAwaitingPartnerAsync(pg, "paged-review");
    var foreign = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = Actor(fixture.Partner, "Partner");
    var expected = new HashSet<Guid> { packageId };
    var tie = DateTimeOffset.UtcNow.AddDays(1);
    // Structural query population only: these headers are not further generated reports or human approvals.
    // Missing required validations deliberately discard the newest window AFTER the coarse SQL filter.
    for (var i = 0; i < 205; i++)
    {
      var id = Guid.NewGuid();
      var eligible = i >= 101;
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO financial_packages (id, firm_id, client_id, engagement_id, adjusted_dataset_id, mapping_version_id,
          adjustment_plan_id, period_id, book_id, basis, framework, period_start, period_end, taxonomy_version,
          template_version, calculation_engine_version, calculation_hash, currency, revision, generation, status, created_at)
        SELECT {id}, firm_id, client_id, engagement_id, adjusted_dataset_id, mapping_version_id,
          adjustment_plan_id, period_id, book_id, basis, framework, period_start, period_end, taxonomy_version,
          {"page-" + i}, calculation_engine_version, {Hashing.Sha256Hex("page-" + i)}, currency, revision, generation,
          'VALIDATED', {(eligible ? tie : tie.AddDays(1))}
        FROM financial_packages WHERE id = {packageId}
        """);
      if (!eligible) continue;
      expected.Add(id);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO financial_package_validations (id, firm_id, financial_package_id, code, passed, detail, created_at)
        SELECT gen_random_uuid(), firm_id, {id}, code, passed, detail, created_at
        FROM financial_package_validations WHERE financial_package_id = {packageId}
        """);
    }
    var legacy = await FinancialPackageReviewService.GetStaffQueueAsync(db, actor);
    Assert.Empty(legacy.Value!); // The former terminal newest-100 window conceals all eligible work.
    FinancialPackageReviewCursor? cursor = null;
    var found = new List<Guid>();
    var pages = 0;
    do
    {
      var result = await FinancialPackageReviewService.GetStaffQueuePageAsync(db, actor, cursor?.CreatedAt, cursor?.Id);
      Assert.True(result.Succeeded, result.Message);
      var page = result.Value!;
      Assert.InRange(page.Items.Count, 0, 100);
      if (pages++ == 0) { Assert.Empty(page.Items); Assert.NotNull(page.NextCursor); }
      foreach (var item in page.Items)
      {
        Assert.Equal(fixture.ClientId, item.ClientId);
        Assert.Equal(fixture.EngagementId, item.EngagementId);
        Assert.Equal("PACKAGE GATE TEST", item.ClientName);
        Assert.DoesNotContain(item.PackageId, found);
        found.Add(item.PackageId);
      }
      cursor = page.NextCursor;
      Assert.InRange(pages, 1, 3);
    } while (cursor is not null);
    Assert.True(expected.SetEquals(found));
    var foreignActor = Actor(foreign.Partner, "Partner");
    var foreignPage = await FinancialPackageReviewService.GetStaffQueuePageAsync(db, foreignActor, tie.AddDays(3), Guid.NewGuid());
    Assert.True(foreignPage.Succeeded, foreignPage.Message);
    Assert.Empty(foreignPage.Value!.Items);
    Assert.False((await FinancialPackageReviewService.GetStaffQueuePageAsync(db, actor, tie, null)).Succeeded);
    var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == fixture.FirmId && x.UserId == fixture.Partner.Id);
    var otherEngagementId = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = otherEngagementId, FirmId = fixture.FirmId,
      PracticeClientId = fixture.ClientId, ServiceRoute = "AUDIT", ProfessionalWorkBlocked = false, CreatedAt = tie });
    var liveOtherScope = Grant(fixture.FirmId, fixture.Partner, "Partner");
    liveOtherScope.ClientId = fixture.ClientId; liveOtherScope.EngagementId = otherEngagementId;
    db.RoleGrants.Add(liveOtherScope);
    grant.ExpiresAt = grant.GrantedAt.AddMilliseconds(1);
    Assert.True(grant.ExpiresAt < DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();
    var expiredScope = await FinancialPackageReviewService.GetStaffQueuePageAsync(db, actor);
    Assert.True(expiredScope.Succeeded, expiredScope.Message);
    Assert.Empty(expiredScope.Value!.Items);
    Assert.Null(expiredScope.Value.NextCursor); // An expired scope cannot disclose even a continuation identity.
    grant.RevokedAt = DateTimeOffset.UtcNow;
    liveOtherScope.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    var revoked = await FinancialPackageReviewService.GetStaffQueuePageAsync(db, actor, tie.AddDays(3), Guid.NewGuid());
    Assert.False(revoked.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, revoked.ErrorCode);
  }

  /// <summary>A validated package with source review, a rendered artifact, and management and accounting approvals, waiting for the Partner stage.</summary>
  private static async Task<(Fixture Fixture, Guid PackageId, ActorContext Preparer, ActorContext Reviewer)> PackageAwaitingPartnerAsync(
    PgTestSchema pg, string tag)
  {
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId,
      fixture.EngagementId, fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, tag);
    var (mappingId, planId) = await CreateApprovedMappingAndPlanAsync(pg, fixture, preparer, reviewer);
    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", tag + "-v1")
    {
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-" + tag], ClassificationRationale: "Synthetic receipts agreed to source.",
          SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
        [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-" + tag)],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-" + tag], ClosingCashEvidenceReferences: ["closing-cash-" + tag],
        FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "cash-population-" + tag,
          "The selected synthetic cash population has no foreign-currency effect."))
    };
    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null)).Succeeded);
      Assert.True((await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId)).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          tag + "-management", "Management approved the exact package."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          tag + "-accounting", "Accounting reviewed the exact package."))).Succeeded);
    }
    return (fixture, packageId, preparer, reviewer);
  }

  private static async Task<AppUser> AddUserWithGrantAsync(PgTestSchema pg, Guid firmId, string name, RoleGrant grant)
  {
    var user = User(firmId, name);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    grant.UserId = user.Id;
    grant.FirmId = firmId;
    db.RoleGrants.Add(grant);
    await db.SaveChangesAsync();
    return user;
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyPartnerRowRecordedByAdministrator_DoesNotSatisfyReleaseGate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (fixture, packageId, _, reviewer) = await PackageAwaitingPartnerAsync(pg, "legacy-partner");

    // A Partner row written before Administrator left the stage: an administrator recorded it, and no Partner grant backs it.
    var administrator = User(fixture.FirmId, "legacy-administrator");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(administrator);
      db.RoleGrants.Add(Grant(fixture.FirmId, administrator, "Administrator"));
      var accounting = await db.FinancialPackageReviewDecisions.AsNoTracking()
        .SingleAsync(x => x.FinancialPackageId == packageId && x.Stage == FinancialPackageReviewStages.AccountingReview);
      db.FinancialPackageReviewDecisions.Add(new FinancialPackageReviewDecision
      {
        Id = Guid.CreateVersion7(), FirmId = accounting.FirmId, ClientId = accounting.ClientId, EngagementId = accounting.EngagementId,
        FinancialPackageId = accounting.FinancialPackageId, PackageRevision = accounting.PackageRevision,
        PackageGeneration = accounting.PackageGeneration, PackageHash = accounting.PackageHash,
        Stage = FinancialPackageReviewStages.PartnerApproval, Decision = FinancialPackageReviewDecisions.Approved,
        FinancialPackageArtifactId = accounting.FinancialPackageArtifactId, ArtifactVersion = accounting.ArtifactVersion,
        ArtifactSha256Hex = accounting.ArtifactSha256Hex, EvidenceMode = FinancialPackageReviewEvidenceModes.SignedIn,
        EvidenceReference = "legacy-partner", Comment = "Recorded before the stage changed.",
        DecidedByUserId = administrator.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var gate = await FinancialPackageReviewService.RequireCurrentAsync(db, reviewer, packageId, requirePartner: true);
      Assert.Equal(ErrorCodes.GateBlocked, gate.ErrorCode);
      Assert.Contains("current Partner", gate.Message!, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task PartnerStage_RequiresACurrentPartnerGrantOnThisEngagement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (fixture, packageId, _, _) = await PackageAwaitingPartnerAsync(pg, "partner-scope");
    var now = DateTimeOffset.UtcNow;
    RoleGrant PartnerGrant(Guid? engagementId, DateTimeOffset? revokedAt = null) => new()
    {
      Id = Guid.NewGuid(), Role = "Partner", ClientId = fixture.ClientId, EngagementId = engagementId,
      GrantedAt = now, GrantedByUserId = fixture.Partner.Id, RevokedAt = revokedAt,
    };
    // A Partner on another engagement of the same client, and a Partner whose grant was revoked, cannot decide this package.
    var otherEngagementId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new Engagement { Id = otherEngagementId, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
        ProfessionalWorkBlocked = false, CreatedAt = now });
      await db.SaveChangesAsync();
    }
    var elsewhere = await AddUserWithGrantAsync(pg, fixture.FirmId, "partner-elsewhere", PartnerGrant(otherEngagementId));
    var revoked = await AddUserWithGrantAsync(pg, fixture.FirmId, "partner-revoked", PartnerGrant(fixture.EngagementId, now.AddDays(-1)));
    var unassigned = await AddUserWithGrantAsync(pg, fixture.FirmId, "partner-unassigned", PartnerGrant(fixture.EngagementId));
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var unassignedActor = Actor(unassigned, "Partner");
      var queue = await FinancialPackageReviewService.GetStaffQueueAsync(db, unassignedActor);
      Assert.True(queue.Succeeded, queue.Message);
      Assert.Equal("PARTNER_APPROVAL_REQUIRED", Assert.Single(queue.Value!, x => x.PackageId == packageId).NextAction);
      var workspace = await FinancialPackageWorkspaceQuery.GetAsync(db, unassignedActor, packageId);
      Assert.True(workspace.Succeeded, workspace.Message);
      Assert.DoesNotContain(FinancialPackageReviewStages.PartnerApproval, workspace.Value!.ReviewStages);

      var assignedActor = Actor(fixture.Partner, "Partner");
      var assignedQueue = await FinancialPackageReviewService.GetStaffQueueAsync(db, assignedActor);
      Assert.Equal(FinancialPackageReviewStages.PartnerApproval,
        Assert.Single(assignedQueue.Value!, x => x.PackageId == packageId).NextAction);
      var assignedWorkspace = await FinancialPackageWorkspaceQuery.GetAsync(db, assignedActor, packageId);
      Assert.Contains(FinancialPackageReviewStages.PartnerApproval, assignedWorkspace.Value!.ReviewStages);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await FinancialPackageReviewService.RecordAsync(db, Actor(elsewhere, "Partner"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "partner-elsewhere", "Wrong engagement."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FinancialPackageReviewService.RecordAsync(db, Actor(revoked, "Partner"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "partner-revoked", "Revoked grant."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FinancialPackageReviewService.RecordAsync(db, Actor(unassigned, "Partner"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "partner-unassigned", "Not staffed as the Engagement Partner."))).ErrorCode);
    }

    // An administrator who is also the assigned Engagement Partner has the exact staffing grant and may decide as Partner.
    var both = fixture.Partner;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(Grant(fixture.FirmId, both, "Administrator"));
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var decided = await FinancialPackageReviewService.RecordAsync(db, Actor(both, "Partner"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn, "partner-current", "Partner decision."));
      Assert.True(decided.Succeeded, decided.Message);
      var current = await FinancialPackageReviewService.RequireCurrentAsync(db, Actor(both, "Partner"), packageId, requirePartner: true);
      Assert.False(current.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, current.ErrorCode);
      Assert.Contains("three-format composition", current.Message!, StringComparison.Ordinal);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var assignment = await db.EngagementStaffAssignments.SingleAsync(x => x.FirmId == fixture.FirmId &&
        x.EngagementId == fixture.EngagementId && x.UserId == both.Id && x.RevokedAt == null);
      assignment.RevokedAt = DateTimeOffset.UtcNow;
      assignment.RevokedByUserId = fixture.Reviewer.Id;
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var noLongerCurrent = await FinancialPackageReviewService.RequireCurrentAsync(db, Actor(both, "Partner"), packageId, requirePartner: true);
      Assert.False(noLongerCurrent.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, noLongerCurrent.ErrorCode);
      Assert.Contains("current assigned Engagement Partner", noLongerCurrent.Message!, StringComparison.Ordinal);
    }
  }

  private static async Task<(Guid MappingId, Guid PlanId)> CreateApprovedMappingAndPlanAsync(
    PgTestSchema pg, Fixture fixture, ActorContext preparer, ActorContext reviewer)
  {
    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      mappingId = (await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new MappingAllocationInput("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new MappingAllocationInput("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]))).Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }
    return (mappingId, planId);
  }

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var partner = User(firmId, "partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "PACKAGE GATE TEST", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, partner);
    var partnerGrant = new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = partner.Id, Role = "Partner", ClientId = clientId,
      EngagementId = engagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = reviewer.Id,
      Reason = "Engagement staffing: Engagement Partner"
    };
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, reviewer, "Partner"),
      partnerGrant);
    db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, UserId = partner.Id,
      StaffingLevel = StaffingLevels.EngagementPartner, RoleGrantId = partnerGrant.Id,
      AssignedByUserId = reviewer.Id, AssignedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
      CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingBooks.Add(new ClientReportingBook
    {
      Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
      Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
      Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    var taxonomyId = Guid.NewGuid();
    db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Gate taxonomy",
      Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
      CreatedByUserId = preparer.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.ReportingTaxonomyNodes.AddRange(
      new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "CASH", Name = "Cash",
        StatementSection = "ASSETS", DisplaySign = "SIGNED", NormalBalance = "DEBIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      },
      new ReportingTaxonomyNode
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "REVENUE", Name = "Revenue",
        StatementSection = "INCOME", DisplaySign = "SIGNED", NormalBalance = "CREDIT", IsPosting = true,
        Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      });
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, periodId, bookId, preparer, reviewer, partner);
  }
}
