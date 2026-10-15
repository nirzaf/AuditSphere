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
      db.RoleGrants.AddRange(
        Grant(firmId, preparer, "AccountingPreparer"),
        Grant(firmId, reviewer, "AccountingReviewer"),
        Grant(firmId, partner, "Partner"));
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
    }

    var preparerActor = Actor(preparer, "AccountingPreparer");
    var reviewerActor = Actor(reviewer, "AccountingReviewer");
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg.Options, firmId, clientId, engagementId,
      periodId, bookId, preparer.Id, reviewerActor, "candidate");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparerActor,
        new CreateMappingVersionRequest(datasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [
            new MappingAllocationInput("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
            new MappingAllocationInput("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
          ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewerActor, mapping.Value, 1)).Succeeded);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparerActor, datasetId, []);
      Assert.True(plan.Succeeded, plan.Message);
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparerActor, plan.Value)).Succeeded);
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparerActor,
        new BuildFinancialPackageRequest(plan.Value, mapping.Value, "IFRS", "2026-01-01", "2026-12-31", "candidate-v1")
        {
          SupplementaryInformation = new FinancialSupplementaryInformation(
            0m, 100m,
            [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
              ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-candidate"],
              ClassificationRationale: "Synthetic receipts agreed to source.",
              SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
            [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."),
              new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "None identified.")],
            [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 100m, "equity-candidate")],
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
            new StatementLineDefinition("TA", "Total assets", StatementLayoutLineKinds.SumChildLines,
              StatementLayoutSections.FinancialPosition, 3, ChildLineCodes: ["CASH"], IsSubtotal: true),
            new StatementLineDefinition("H_PL", "Revenue", StatementLayoutLineKinds.Heading,
              StatementLayoutSections.ProfitOrLoss, 1),
            new StatementLineDefinition("REV", "Revenue", StatementLayoutLineKinds.MappedTaxonomyBalance,
              StatementLayoutSections.ProfitOrLoss, 2, DisplaySign: StatementLayoutDisplaySigns.Inverted,
              TaxonomyNodeCode: "REVENUE")
          ]));
      Assert.True(layout.Succeeded, layout.Message);
      Assert.True((await StatementLayoutService.ValidateLayoutAsync(db, preparerActor, layout.Value!.LayoutVersionId)).Value!.IsValid);
      Assert.True((await StatementLayoutService.PublishLayoutAsync(db, reviewerActor, layout.Value.LayoutVersionId,
        "Published for the candidate promotion test")).Succeeded);
      return new Seeded(firmId, clientId, engagementId, built.Value.PackageId,
        layout.Value.LayoutVersionId, preparer, reviewer, partner);
    }
  }

  private static ActorContext Actor(AppUser user, string role) =>
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

