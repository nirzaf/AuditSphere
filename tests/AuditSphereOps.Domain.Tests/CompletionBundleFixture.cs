using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed record CompletionBundleScope(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> U)
{
  public ActorContext A(string name, params string[] roles) => new(U[name].Id, FirmId, U[name].SessionEpoch, roles);
}

// Synthetic fixtures use the actual review, approval, release and billing commands. No live provider acceptance is claimed.
public static class CompletionBundleFixture
{
  public static async Task<(Guid PackageId, Guid ReleaseId)> ReleasedFinancialPackageAsync(
    DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w, string? checkpointRoot = null)
  {
    var now = DateTimeOffset.UtcNow;
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var targetDataset = Guid.NewGuid();
    var cashAmount = 120m;
    var netIncome = 100m;
    var preparer = w.A("associate", "AccountingPreparer");
    var reviewer = w.A("senior", "AccountingReviewer");
    var partner = w.A("partner", "Partner");
    await using (var db = new AuditSphereDbContext(options))
    {
      foreach (var (name, role) in new[]
      {
        ("associate", "AccountingPreparer"), ("senior", "AccountingReviewer"), ("partner", "Partner")
      })
      {
        var userId = w.U[name].Id;
        if (!await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == w.FirmId && x.UserId == userId &&
              x.Role == role && x.ClientId == null && x.EngagementId == null))
          db.RoleGrants.Add(new RoleGrant
          {
            Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = userId, Role = role,
            GrantedAt = now, GrantedByUserId = w.U["partner"].Id
          });
      }
      if (!await db.EngagementStaffAssignments.AsNoTracking().AnyAsync(x => x.FirmId == w.FirmId &&
            x.EngagementId == w.EngagementId && x.UserId == w.U["partner"].Id &&
            x.StaffingLevel == StaffingLevels.EngagementPartner && x.RevokedAt == null))
      {
        var partnerGrant = await db.RoleGrants.SingleOrDefaultAsync(x => x.FirmId == w.FirmId &&
          x.UserId == w.U["partner"].Id && x.Role == "Partner" && x.ClientId == w.ClientId &&
          x.EngagementId == w.EngagementId && x.RevokedAt == null);
        if (partnerGrant is null)
        {
          partnerGrant = new RoleGrant
          {
            Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = w.U["partner"].Id, Role = "Partner",
            ClientId = w.ClientId, EngagementId = w.EngagementId, GrantedAt = now,
            GrantedByUserId = w.U["partner"].Id, Reason = "Engagement staffing: Engagement Partner"
          };
          db.RoleGrants.Add(partnerGrant);
        }
        db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
        {
          Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          UserId = w.U["partner"].Id, StaffingLevel = StaffingLevels.EngagementPartner,
          RoleGrantId = partnerGrant.Id, AssignedByUserId = w.U["partner"].Id, AssignedAt = now
        });
      }
      await CompletionTestFixtures.SeedTaxonomyAsync(db, w.FirmId, w.U["partner"].Id);
      await CompletionTestFixtures.SeedCompleteStatementTaxonomyAsync(db, w.FirmId);
      db.ClientReportingPeriods.Add(new ClientReportingPeriod { Id = periodId, FirmId = w.FirmId, ClientId = w.ClientId, PeriodCode = "BUNDLE-" + periodId.ToString("N")[..8], StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active, CreatedByUserId = w.U["associate"].Id, CreatedAt = now });
      db.ClientReportingBooks.Add(new ClientReportingBook { Id = bookId, FirmId = w.FirmId, ClientId = w.ClientId, PeriodId = periodId, Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES",
        Currency = "QAR", Status = AccountingWorkflowStates.Active, CreatedByUserId = w.U["associate"].Id, CreatedAt = now });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset { Id = targetDataset, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
        SourceKind = "Raw", Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading,
        NormalizedDatasetDigest = Hashing.Sha256Hex(targetDataset.ToString()), ImportedAt = now, ImportedByUserId = w.U["associate"].Id });
      // A complete synthetic package: equity, a revaluation movement in OCI, and operating, investing and financing cash events.
      TrialBalanceRow Row(string account, string name, decimal amount) => new()
      { Id = Guid.NewGuid(), DatasetId = targetDataset, AccountCode = account, AccountName = name, Amount = amount, Currency = "QAR", Entity = "TEST" };
      db.TrialBalanceRows.AddRange(Row("1000", "Cash", cashAmount), Row("1500", "Equipment", 35m), Row("2500", "Borrowings", -40m),
        Row("3000", "Share capital", -5m), Row("4000", "Revenue", -netIncome), Row("4900", "Revaluation surplus", -10m));
      await db.SaveChangesAsync();
      await db.TrialBalanceDatasets.Where(x => x.Id == targetDataset)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    }
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(options, w.FirmId, w.ClientId, w.EngagementId,
      periodId, bookId, w.U["associate"].Id, reviewer, "completion-bundle", netIncome,
      optional: CashFlowSourceSeed.OptionalCashEvents.Equipment | CashFlowSourceSeed.OptionalCashEvents.Borrowing);
    Guid packageId;
    await using (var db = new AuditSphereDbContext(options))
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer, new(targetDataset, "TEST-IFRS", "2026-01-01", "2026-12-31",
        [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"), new("1500", "PPE", "ASSETS", 1m, "Equipment mapping"),
          new("2500", "BORROWINGS", "LIABILITIES", 1m, "Borrowings mapping"), new("3000", "SHARE_CAPITAL", "EQUITY", 1m, "Share capital mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping"), new("4900", "REVALUATION", "OCI", 1m, "Revaluation surplus mapping")]));
      Assert.True(mapping.Succeeded, $"CreateMappingVersion failed: {mapping.ErrorCode} - {mapping.Message}");
      var mappingRow = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.Id == mapping.Value);
      var approved = await FinancialStatementService.ApproveMappingAsync(db, reviewer, mapping.Value, mappingRow.Version);
      Assert.True(approved.Succeeded, $"ApproveMapping failed: {approved.ErrorCode} - {approved.Message}");
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, targetDataset, []);
      Assert.True(plan.Succeeded, plan.Message);
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, plan.Value);
      Assert.True(finalized.Succeeded, finalized.Message);
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, new(plan.Value, mapping.Value, "IFRS", "2026-01-01", "2026-12-31", "bundle-test-v1",
        new FinancialSupplementaryInformation(5m, cashAmount,
          [
            new CashFlowLineInput("OPERATING", "Cash receipts", netIncome,
              MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-bundle"],
              ClassificationRationale: "Synthetic receipt agreed to source.", SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -netIncome)]),
            new CashFlowLineInput("INVESTING", "Purchase of equipment", -25m,
              MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "EQUIPMENT_PURCHASE", SourceReferences: ["bank-ledger-equipment"],
              ClassificationRationale: "Equipment paid from the bank account.", SourceAllocations: [new CashFlowSourceAllocationInput(source.Equipment, 25m)]),
            new CashFlowLineInput("FINANCING", "Proceeds from borrowings", 40m,
              MovementDate: new DateOnly(2026, 6, 30), ActivityCode: "LOAN_PROCEEDS", SourceReferences: ["bank-ledger-loan"],
              ClassificationRationale: "Loan drawdown received in the bank account.", SourceAllocations: [new CashFlowSourceAllocationInput(source.Borrowing, -40m)])
          ],
          [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."), new DisclosureInput("COMMITMENTS", "", true, "No commitments in the synthetic management information.")],
          [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, netIncome, 0m, 0m, 0m, netIncome, "equity-test"),
            new EquityLineInput("REVALUATION_RESERVE", "Revaluation reserve", 0m, 0m, 10m, 0m, 0m, 10m, "valuation-report-test"),
            new EquityLineInput("SHARE_CAPITAL", "Share capital", 5m, 0m, 0m, 0m, 0m, 5m, "share-register-test")],
          NoteLines: [new NoteLineInput("CASH_NOTE", "CASH", cashAmount, "note-test")],
          FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "functional-currency-cash-population",
            "The selected QAR cash population has no foreign-currency cash effect."),
          CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-bundle"], ClosingCashEvidenceReferences: ["closing-cash-bundle"])));
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      packageId = built.Value.PackageId;
      var sourceReview = await CashFlowSourceReviewCall.RecordAsync(
        db, reviewer, packageId, "APPROVED", null);
      Assert.True(sourceReview.Succeeded, sourceReview.Message);
      var layout = await StatementLayoutService.SaveLayoutDraftAsync(db, preparer,
        new StatementLayoutDraftRequest(w.ClientId, "IFRS", "Bundle composition layout",
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
      Assert.True((await StatementLayoutService.ValidateLayoutAsync(db, preparer, layout.Value!.LayoutVersionId)).Value!.IsValid);
      Assert.True((await StatementLayoutService.PublishLayoutAsync(db, reviewer, layout.Value.LayoutVersionId,
        "Published for the synthetic completion bundle composition")).Succeeded);
      var draft = await FinancialStatementService.PinPresentationAsync(db, preparer, packageId, layout.Value.LayoutVersionId);
      Assert.True(draft.Succeeded, draft.Message);
      Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, draft.Value!.RevisionId,
        FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
      var contentReview = await StatementContentReviewCall.RecordAsync(db, reviewer, packageId, draft.Value.RevisionId);
      Assert.True(contentReview.Succeeded, contentReview.Message);
      var candidate = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, packageId, draft.Value.RevisionId);
      Assert.True(candidate.Succeeded, candidate.Message);
      var composition = await FinancialStatementService.SelectCompositionAsync(db, preparer, packageId, candidate.Value!.RevisionId);
      Assert.True(composition.Succeeded, composition.Message);
      foreach (var (actor, stage, mode) in new[] { (preparer, FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewEvidenceModes.Offline),
        (reviewer, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewEvidenceModes.SignedIn), (partner, FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewEvidenceModes.SignedIn) })
      {
        var reviewed = await FinancialPackageReviewService.RecordAsync(db, actor, new(packageId, stage, FinancialPackageReviewDecisions.Approved, mode,
          "assembly-test-" + stage, "Reviewed the exact synthetic statement composition.", candidate.Value.RevisionId, null,
          composition.Value!.Id, composition.Value!.ManifestDigest));
        Assert.True(reviewed.Succeeded, reviewed.Message);
      }
    }
    await using (var db = new AuditSphereDbContext(options))
    {
      var manifest = System.Text.Encoding.UTF8.GetBytes("Synthetic controlled financial-package delivery manifest");
      var digest = Hashing.Sha256Hex(manifest);
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      var policyGeneration = (await db.FirmSafetyStates.AsNoTracking().SingleAsync(x => x.Id == w.FirmId)).PolicyGeneration;
      var approval = await ApprovalService.CreateAsync(db, partner, new("FINANCIAL_PACKAGE", packageId, package.Revision, package.Generation, policyGeneration, digest));
      Assert.True(approval.Succeeded, approval.Message);
      var candidate = await ReleaseService.CreateCandidateAsync(db, partner, new(approval.Value, "FINANCIAL_PACKAGE", packageId, package.Revision, package.Generation, policyGeneration, digest));
      Assert.True(candidate.Succeeded, candidate.Message);
      var root = checkpointRoot ?? Path.Combine(Path.GetTempPath(), "auditsphere-bundle-checkpoint", Guid.NewGuid().ToString("N"));
      try
      {
        var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, new LocalAppendOnlyCheckpointStore(root), partner, new(candidate.Value, 1, "test-bundle-release", digest, manifest));
        Assert.True(checkpoint.Succeeded, checkpoint.Message);
        var release = await ReleaseService.IssueAsync(db, partner, new(candidate.Value, 1, digest, "test-bundle-release"), new ReleaseSafetyOptions());
        Assert.True(release.Succeeded, release.Message);
        return (packageId, release.Value);
      }
      finally { if (checkpointRoot is null && Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
  }

  public static async Task SeedPostedFeeAsync(DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w, bool automateBalance = false)
  {
    var now = DateTimeOffset.UtcNow;
    Guid proposalId;
    await using (var db = new AuditSphereDbContext(options))
    {
      var lead = new Lead { Id = Guid.NewGuid(), FirmId = w.FirmId, Name = "Synthetic bundle client", Source = "Referral", PrimaryContactName = "Synthetic bundle director", PrimaryContactEmail = "director@bundle.example.test", CreatedAt = now };
      var opportunity = new Opportunity { Id = Guid.NewGuid(), FirmId = w.FirmId, LeadId = lead.Id, PracticeClientId = w.ClientId, ServiceRoute = "FinancialStatementAudit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now };
      var quotationId = Guid.NewGuid();
      var proposal = new Proposal { Id = Guid.NewGuid(), FirmId = w.FirmId, OpportunityId = opportunity.Id, PracticeClientId = w.ClientId, Status = CrmStates.ProposalInternalReview,
        Currency = "QAR", Fee = 25000m, ServiceProfileId = "AUDIT-2026", Scope = "Synthetic statutory audit", Deliverables = "Auditor report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        CreatedAt = now };
      proposalId = proposal.Id;
      db.Leads.Add(lead); db.Opportunities.Add(opportunity); db.Proposals.Add(proposal);
      db.QuotationVersions.Add(new QuotationVersion { Id = quotationId, FirmId = w.FirmId, ProposalId = proposal.Id, Currency = "QAR", BaseAmount = 25000m, Fee = 25000m, InputHash = Hashing.Sha256Hex("synthetic price"),
        Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = w.U["manager"].Id, ApprovedAt = now, CreatedAt = now });
      var contactId = Guid.NewGuid();
      db.ClientContacts.Add(new ClientContact { Id = contactId, FirmId = w.FirmId, PracticeClientId = w.ClientId, FullName = "Synthetic bundle director",
        Email = "director@bundle.example.test", Role = "Director", Title = "Finance Director", SignatoryAuthority = "Authorized commercial contact", Primary = true, IsActive = true });
      db.ClientContactRoutings.Add(new ClientContactRouting { Id = Guid.NewGuid(), FirmId = w.FirmId, PracticeClientId = w.ClientId, ClientContactId = contactId,
        Purpose = CorrespondencePurposes.Commercial, IsPrimaryForPurpose = true, CreatedByUserId = w.U["partner"].Id, CreatedAt = now });
      foreach (var (name, role) in new[] { ("manager", "FinanceManager"), ("partner2", "FinanceReviewer") })
        db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = w.U[name].Id, Role = role, GrantedByUserId = w.U["partner"].Id, GrantedAt = now });
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile { Id = Guid.NewGuid(), FirmId = w.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = w.U["partner2"].Id, ApprovedAt = now, CreatedAt = now });
      await db.Engagements.Where(x => x.Id == w.EngagementId).ExecuteUpdateAsync(x => x.SetProperty(e => e.PeriodStart, "2026-01-01"));
      await db.SaveChangesAsync();
      Assert.True((await AuditSphereOps.Application.Practice.CommercialDocumentService.SaveProfileAsync(db, w.A("partner", "Partner"), new("Synthetic audit firm", "Test address", "firm@bundle.example.test", "", "#0F766E", "Reviewed terms"))).Succeeded);
      var partner = w.A("partner", "Partner");
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, partner, proposalId);
      var dispatched = await AuditSphereOps.Application.Practice.PracticeCrmService.SendProposalAsync(db, partner, proposalId);
      Assert.True(dispatched.Succeeded, dispatched.Message);
      var offer = await db.Proposals.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.Id == proposalId);
      var response = await AuditSphereOps.Application.Practice.PracticeCrmService.RecordProposalResponseAsync(db, partner, proposalId,
        new("ACCEPTED", null, offer.SentOfferSha256, "Synthetic bundle director", "director@bundle.example.test",
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(response.Succeeded, response.Message);
    }
    await using (var db = new AuditSphereDbContext(options))
    {
      var partner = w.A("partner", "Partner"); var fm = w.A("manager", "FinanceManager"); var fr = w.A("partner2", "FinanceReviewer");
      var agreement = await AuditSphereOps.Application.Practice.FeeAgreementService.CreateAgreementAsync(db, partner, proposalId);
      Assert.True(agreement.Succeeded, agreement.Message);
      var linked = await AuditSphereOps.Application.Practice.FeeAgreementService.LinkEngagementAsync(db, partner, agreement.Value, w.EngagementId);
      Assert.True(linked.Succeeded, linked.Message);
      var advance = await AuditSphereOps.Application.Practice.FeeAgreementService.IssueAdvanceInvoiceAsync(db, fm, agreement.Value);
      Assert.True(advance.Succeeded, advance.Message);
      Assert.True((await AuditSphereOps.Application.Practice.BillingService.SubmitInvoiceAsync(db, fm, advance.Value)).Succeeded);
      Assert.True((await AuditSphereOps.Application.Practice.BillingService.ApproveInvoiceAsync(db, fr, advance.Value)).Succeeded);
      Assert.True((await AuditSphereOps.Application.Practice.BillingService.PostInvoiceAsync(db, fm, advance.Value)).Succeeded);
      var receipt = await AuditSphereOps.Application.Practice.FeeAgreementService.RecordAdvancePaymentAsync(db, fm, agreement.Value, 12500m, "TEST-TRANSFER-1");
      Assert.True(receipt.Succeeded, receipt.Message);
      if (automateBalance)
      {
        var admin = PbcSeed.User(w.FirmId, "Staff");
        db.Users.Add(admin); db.RoleGrants.Add(PbcSeed.Grant(w.FirmId, admin, "Administrator")); await db.SaveChangesAsync();
        var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(options));
        var store = new PostgresOperationStore(factory);
        var worker = new WorkerOptions(w.FirmId, "Test");
        var policy = new AuditSphereOps.Application.Practice.AutomaticFeeInvoicePolicy(true, fm.UserId, admin.Id);
        var handler = new AuditSphereOps.Application.Practice.AutomaticFeeInvoiceHandler(policy);
        var discovery = new AuditSphereOps.Application.Practice.AutomaticFeeInvoiceDiscovery(factory, store, handler, worker, policy);
        Assert.Equal(1, await discovery.EnqueuePendingAsync(default));
        var dispatcher = new OperationDispatcher(store, new DurableOperationRegistry([handler], worker), worker);
        Assert.True(await dispatcher.ProcessNextAsync());
        Assert.False(await dispatcher.ProcessNextAsync());
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == AuditSphereOps.Application.Practice.AutomaticFeeInvoiceHandler.Kind);
        Assert.Equal(OperationState.COMPLETED, operation.Status);
        Assert.Equal(0, await discovery.EnqueuePendingAsync(default));
        var invoiceId = Guid.Parse(operation.ResultIdentity!);
        Assert.Equal(BillingStates.InvoiceDraft, (await db.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId)).Status);
      }
      var balance = await AuditSphereOps.Application.Practice.FeeAgreementService.IssueBalanceInvoiceAsync(db, fm, agreement.Value);
      Assert.True(balance.Succeeded, balance.Message);
      Assert.True((await AuditSphereOps.Application.Practice.BillingService.SubmitInvoiceAsync(db, fm, balance.Value)).Succeeded);
      Assert.True((await AuditSphereOps.Application.Practice.BillingService.ApproveInvoiceAsync(db, fr, balance.Value)).Succeeded);
      Assert.True((await AuditSphereOps.Application.Practice.BillingService.PostInvoiceAsync(db, fm, balance.Value)).Succeeded);
    }
  }
}
