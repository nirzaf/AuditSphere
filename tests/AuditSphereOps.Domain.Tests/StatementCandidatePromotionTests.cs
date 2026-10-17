using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// CP-18 candidate successor. Promoting an unapproved statement presentation draft creates one immutable CANDIDATE revision that
/// links to the draft and owns its own rendered artifact. The draft is never edited, and repeating the promotion returns the same
/// candidate instead of creating a second one.
/// </summary>
[Trait("Profile", "Database")]
public sealed class StatementCandidatePromotionTests
{
  internal sealed record Seeded(Guid FirmId, Guid ClientId, Guid EngagementId, Guid PackageId, Guid LayoutId,
    AppUser Preparer, AppUser Reviewer, AppUser Partner);

  [Fact]
  public async Task PromotingADraft_CreatesOneImmutableCandidateSuccessor_WithItsOwnArtifact()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await SeedPresentableAsync(pg);
    var preparer = Actor(s.Preparer, "AccountingPreparer");
    var reviewer = Actor(s.Reviewer, "AccountingReviewer");

    Guid draftId;
    string draftSnapshotHash;
    byte[] draftPdf;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
      Assert.True(pin.Succeeded, pin.Message);
      Assert.Equal(FinancialStatementPresentationRevisionStatuses.UnapprovedDraft, pin.Value!.Status);
      draftId = pin.Value.RevisionId;
      draftSnapshotHash = pin.Value.SnapshotSha256Hex;
      var draftRender = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, draftId,
        FinancialStatementPresentationRenderer.PdfFormat);
      Assert.True(draftRender.Succeeded, draftRender.Message);
      draftPdf = draftRender.Value!.Bytes;
    }

    Guid candidateId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId)).Succeeded);
      var promoted = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
      Assert.True(promoted.Succeeded, promoted.Message);
      var candidate = promoted.Value!;
      candidateId = candidate.RevisionId;

      Assert.NotEqual(draftId, candidateId);
      Assert.Equal(FinancialStatementPresentationRevisionStatuses.Candidate, candidate.Status);
      Assert.Equal(draftId, candidate.PredecessorRevisionId);
      Assert.Equal(s.LayoutId, candidate.LayoutVersionId);
      Assert.Contains(FinancialStatementPresentationRenderer.PdfFormat, candidate.ArtifactFormats);

      // The draft is unchanged: same status, same snapshot, and it has no successor link of its own.
      var draft = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleAsync(x => x.Id == draftId);
      Assert.Equal(FinancialStatementPresentationRevisionStatuses.UnapprovedDraft, draft.Status);
      Assert.Equal(draftSnapshotHash, draft.SnapshotSha256Hex);
      Assert.Null(draft.PredecessorRevisionId);

      // The candidate owns its PDF: its bytes differ from the draft's and are retrievable by revision.
      var candidatePdf = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, candidateId,
        FinancialStatementPresentationRenderer.PdfFormat);
      Assert.True(candidatePdf.Succeeded, candidatePdf.Message);
      Assert.NotEqual(draftPdf, candidatePdf.Value!.Bytes);
    }

    // Repeating the promotion of the same draft is idempotent: it returns the existing candidate and creates no second one.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var repeated = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
      Assert.True(repeated.Succeeded, repeated.Message);
      Assert.Equal(candidateId, repeated.Value!.RevisionId);
      Assert.Equal(1, await db.FinancialStatementPresentationRevisions.CountAsync(x =>
        x.FinancialPackageId == s.PackageId && x.PredecessorRevisionId == draftId));
    }
  }

  [Fact]
  public async Task PromotingAnUnrenderedDraft_IsRefused_AndACandidateCannotBePromotedAgain()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await SeedPresentableAsync(pg);
    var preparer = Actor(s.Preparer, "AccountingPreparer");
    var reviewer = Actor(s.Reviewer, "AccountingReviewer");

    Guid draftId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
      Assert.True(pin.Succeeded, pin.Message);
      draftId = pin.Value!.RevisionId;

      // A candidate must carry an exact rendered artifact, so an unrendered draft is refused rather than given a guessed candidate.
      var unrendered = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
      Assert.False(unrendered.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unrendered.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, draftId,
        FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
      Assert.True((await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId)).Succeeded);
      var candidate = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
      Assert.True(candidate.Succeeded, candidate.Message);

      // A candidate is not a draft, so promoting it again is refused and no candidate chain is created.
      var again = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId,
        candidate.Value!.RevisionId);
      Assert.False(again.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, again.ErrorCode);
    }
  }

  internal static async Task<Seeded> SeedPresentableAsync(PgTestSchema pg)
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
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = clientId, FirmId = firmId, LegalName = "CANDIDATE PROMOTION TEST", CreatedAt = DateTimeOffset.UtcNow
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
        Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Candidate taxonomy",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
        CreatedByUserId = preparer.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      // A complete synthetic package: a balancing statement of financial position with equity, a profit, a revaluation movement in
      // OCI, and operating, investing and financing cash events linked to an accepted general ledger. The entity also holds a USD
      // account translated at the unchanged QAR/USD peg, so its foreign-exchange effect on cash is a genuine, evidenced zero.
      ReportingTaxonomyNode Node(string code, string name, string section, string normal) => new()
      {
        Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = code, Name = name, StatementSection = section,
        DisplaySign = "SIGNED", NormalBalance = normal, IsPosting = true, Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
      };
      db.ReportingTaxonomyNodes.AddRange(
        Node("CASH", "Cash", "ASSETS", "DEBIT"), Node("PPE", "Property and equipment", "ASSETS", "DEBIT"),
        Node("BORROWINGS", "Borrowings", "LIABILITIES", "CREDIT"), Node("SHARE_CAPITAL", "Share capital", "EQUITY", "CREDIT"),
        Node("REVENUE", "Revenue", "INCOME", "CREDIT"), Node("REVALUATION", "Revaluation surplus", "OCI", "CREDIT"));
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
        SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
        ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
        ImportedByUserId = preparer.Id
      });
      TrialBalanceRow Row(string account, string name, decimal amount) => new()
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = account, AccountName = name, Amount = amount, Currency = "QAR", Entity = "TEST"
      };
      db.TrialBalanceRows.AddRange(
        Row("1000", "Cash", 120m), Row("1500", "Equipment", 35m), Row("2500", "Borrowings", -40m),
        Row("3000", "Share capital", -5m), Row("4000", "Revenue", -100m), Row("4900", "Revaluation surplus", -10m));
      await db.SaveChangesAsync();
    }

    var preparerActor = Actor(preparer, "AccountingPreparer");
    var reviewerActor = Actor(reviewer, "AccountingReviewer");
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg.Options, firmId, clientId, engagementId,
      periodId, bookId, preparer.Id, reviewerActor, "candidate",
      optional: CashFlowSourceSeed.OptionalCashEvents.Equipment | CashFlowSourceSeed.OptionalCashEvents.Borrowing);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparerActor,
        new CreateMappingVersionRequest(datasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [
            new MappingAllocationInput("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
            new MappingAllocationInput("1500", "PPE", "ASSETS", 1m, "Equipment mapping"),
            new MappingAllocationInput("2500", "BORROWINGS", "LIABILITIES", 1m, "Borrowings mapping"),
            new MappingAllocationInput("3000", "SHARE_CAPITAL", "EQUITY", 1m, "Share capital mapping"),
            new MappingAllocationInput("4000", "REVENUE", "INCOME", 1m, "Revenue mapping"),
            new MappingAllocationInput("4900", "REVALUATION", "OCI", 1m, "Revaluation surplus mapping")
          ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewerActor, mapping.Value, 1)).Succeeded);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparerActor, datasetId, []);
      Assert.True(plan.Succeeded, plan.Message);
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparerActor, plan.Value)).Succeeded);
      // Each cash movement links the noncash counterpart of its cash event with the ledger sign unchanged (F-01).
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparerActor,
        new BuildFinancialPackageRequest(plan.Value, mapping.Value, "IFRS", "2026-01-01", "2026-12-31", "candidate-v1")
        {
          SupplementaryInformation = new FinancialSupplementaryInformation(
            5m, 120m,
            [
              new CashFlowLineInput("OPERATING", "Cash receipts from customers", 100m, MovementDate: new DateOnly(2026, 6, 30),
                ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-candidate"],
                ClassificationRationale: "Synthetic receipts agreed to source.",
                SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)]),
              new CashFlowLineInput("INVESTING", "Purchase of equipment", -25m, MovementDate: new DateOnly(2026, 6, 30),
                ActivityCode: "EQUIPMENT_PURCHASE", SourceReferences: ["bank-ledger-equipment"],
                ClassificationRationale: "Equipment paid from the bank account.",
                SourceAllocations: [new CashFlowSourceAllocationInput(source.Equipment, 25m)]),
              new CashFlowLineInput("FINANCING", "Proceeds from borrowings", 40m, MovementDate: new DateOnly(2026, 6, 30),
                ActivityCode: "LOAN_PROCEEDS", SourceReferences: ["bank-ledger-loan"],
                ClassificationRationale: "Loan drawdown received in the bank account.",
                SourceAllocations: [new CashFlowSourceAllocationInput(source.Borrowing, -40m)])
            ],
            [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
              new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
            [
              new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-candidate"),
              new EquityLineInput("REVALUATION_RESERVE", "Revaluation reserve", 0m, 0m, 10m, 0m, 0m, 10m, "valuation-report-candidate"),
              new EquityLineInput("SHARE_CAPITAL", "Share capital", 5m, 0m, 0m, 0m, 0m, 5m, "share-register-candidate")
            ],
            FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "functional-currency-cash-population",
              "The selected cash population has no foreign-currency cash effect."),
            CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-candidate"],
            ClosingCashEvidenceReferences: ["closing-cash-candidate"])
        });
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewerActor, built.Value.PackageId,
        "APPROVED", null)).Succeeded);
      var layout = await StatementLayoutService.SaveLayoutDraftAsync(db, preparerActor,
        new StatementLayoutDraftRequest(clientId, "IFRS", "Candidate test layout",
          [
            new StatementLineDefinition("H_FP", "Assets", StatementLayoutLineKinds.Heading,
              StatementLayoutSections.FinancialPosition, 1),
            new StatementLineDefinition("CASH", "Cash", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.FinancialPosition, 2, TaxonomyNodeCode: "CASH"),
            new StatementLineDefinition("PPE", "Property and equipment", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.FinancialPosition, 3, TaxonomyNodeCode: "PPE"),
            new StatementLineDefinition("TA", "Total assets", StatementLayoutLineKinds.SumChildLines,
              StatementLayoutSections.FinancialPosition, 4, ChildLineCodes: ["CASH", "PPE"], IsSubtotal: true),
            new StatementLineDefinition("H_LE", "Liabilities and equity", StatementLayoutLineKinds.Heading,
              StatementLayoutSections.FinancialPosition, 5),
            new StatementLineDefinition("LOANS", "Borrowings", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.FinancialPosition, 6, DisplaySign: StatementLayoutDisplaySigns.Inverted, TaxonomyNodeCode: "BORROWINGS"),
            new StatementLineDefinition("SC", "Share capital", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.FinancialPosition, 7, DisplaySign: StatementLayoutDisplaySigns.Inverted, TaxonomyNodeCode: "SHARE_CAPITAL"),
            new StatementLineDefinition("H_PL", "Revenue", StatementLayoutLineKinds.Heading,
              StatementLayoutSections.ProfitOrLoss, 1),
            new StatementLineDefinition("REV", "Revenue", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.ProfitOrLoss, 2, DisplaySign: StatementLayoutDisplaySigns.Inverted,
              TaxonomyNodeCode: "REVENUE"),
            new StatementLineDefinition("H_OCI", "Other comprehensive income", StatementLayoutLineKinds.Heading,
              StatementLayoutSections.OtherComprehensiveIncome, 1),
            new StatementLineDefinition("REVAL", "Revaluation surplus", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.OtherComprehensiveIncome, 2, DisplaySign: StatementLayoutDisplaySigns.Inverted,
              TaxonomyNodeCode: "REVALUATION")
          ]));
      Assert.True(layout.Succeeded, layout.Message);
      var validated = await StatementLayoutService.ValidateLayoutAsync(db, preparerActor, layout.Value!.LayoutVersionId);
      Assert.True(validated.Value!.IsValid, string.Join("; ", validated.Value.Errors.Select(x => x.Code + " " + x.Message)));
      Assert.True((await StatementLayoutService.PublishLayoutAsync(db, reviewerActor, layout.Value.LayoutVersionId,
        "Published for the candidate promotion test")).Succeeded);
      return new Seeded(firmId, clientId, engagementId, built.Value.PackageId,
        layout.Value.LayoutVersionId, preparer, reviewer, partner);
    }
  }

  internal static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  [Fact]
  public async Task FormalReviewDecisions_BindToTheSameSelectedComposition()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await SeedPresentableAsync(pg);
    var preparer = Actor(s.Preparer, "AccountingPreparer");
    var reviewer = Actor(s.Reviewer, "AccountingReviewer");
    var partner = Actor(s.Partner, "Partner");

    Guid draftId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
      Assert.True(pin.Succeeded, pin.Message);
      draftId = pin.Value!.RevisionId;
      Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, draftId,
        FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
    }

    Guid candidateId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await StatementContentReviewCall.RecordAsync(db, reviewer, s.PackageId, draftId)).Succeeded);
      var promoted = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, draftId);
      Assert.True(promoted.Succeeded, promoted.Message);
      candidateId = promoted.Value!.RevisionId;
      var composed = await FinancialStatementService.SelectCompositionAsync(db, preparer, s.PackageId, candidateId);
      Assert.True(composed.Succeeded, composed.Message);
      var selectedComposition = composed.Value!;

      var management = await FinancialPackageReviewService.RecordAsync(db, preparer,
        new FinancialPackageReviewRequest(s.PackageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "candidate-management", "Management approved the signing candidate.", candidateId, null, selectedComposition.Id, selectedComposition.ManifestDigest));
      Assert.True(management.Succeeded, management.Message);

      // Accounting cannot skip ahead to a different candidate revision; it must follow the same selected revision.
      var stray = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(s.PackageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "candidate-stray", "Accounting on a different revision.", Guid.NewGuid()));
      Assert.False(stray.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid, stray.ErrorCode);

      var accounting = await FinancialPackageReviewService.RecordAsync(db, reviewer,
        new FinancialPackageReviewRequest(s.PackageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "candidate-accounting", "Accounting reviewed the same signing candidate.", candidateId, null, selectedComposition.Id, selectedComposition.ManifestDigest));
      Assert.True(accounting.Succeeded, accounting.Message);

      var partnerApproval = await FinancialPackageReviewService.RecordAsync(db, partner,
        new FinancialPackageReviewRequest(s.PackageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "candidate-partner", "Partner approved the same signing candidate.", candidateId, null, selectedComposition.Id, selectedComposition.ManifestDigest));
      Assert.True(partnerApproval.Succeeded, partnerApproval.Message);

      // Currentness demands all three stages approve the same candidate presentation before release.
      var decisions = await FinancialPackageReviewService.GetAsync(db, partner, s.PackageId);
      Assert.True(decisions.Succeeded, decisions.Message);
      Assert.All(decisions.Value!, d => Assert.Equal(candidateId, d.PresentationRevisionId));
      Assert.All(decisions.Value!, d => Assert.Equal(selectedComposition.Id, d.PresentationCompositionId));
      Assert.All(decisions.Value!, d => Assert.Equal(selectedComposition.ManifestDigest, d.CompositionManifestDigest));
      Assert.True((await FinancialPackageReviewService.RequireCurrentAsync(db, partner, s.PackageId,
        requirePartner: true)).Succeeded);
    }
  }

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };
}

