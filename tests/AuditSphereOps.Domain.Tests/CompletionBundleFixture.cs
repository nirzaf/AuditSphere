using System.IO.Compression;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed record CompletionBundleScope(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> U)
{
  public ActorContext A(string name, params string[] roles) => new(U[name].Id, FirmId, U[name].SessionEpoch, roles);
}

// Synthetic fixtures use the actual review, approval, release and billing commands. No live provider acceptance is claimed.
public static class CompletionBundleFixture
{
  public static async Task<(Guid PackageId, Guid ReleaseId)> ReleasedFinancialPackageAsync(DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w)
  {
    var now = DateTimeOffset.UtcNow;
    Guid targetDataset = Guid.Empty;
    Guid targetMappingId = Guid.Empty;
    decimal cashAmount = 100m;
    decimal netIncome = 100m;
    await using (var db = new AuditSphereDbContext(options))
    {
      foreach (var (name, role) in new[] { ("associate", "AccountingPreparer"), ("senior", "AccountingReviewer") })
        db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = w.U[name].Id, Role = role, GrantedAt = now, GrantedByUserId = w.U["partner"].Id });

      var existingMapping = await db.MappingVersions.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId && x.EngagementId == w.EngagementId && x.Status == AccountingPackageStates.MappingApproved)
        .OrderByDescending(x => x.ApprovedAt).ThenByDescending(x => x.Version)
        .FirstOrDefaultAsync();

      if (existingMapping is not null)
      {
        targetDataset = existingMapping.DatasetId;
        targetMappingId = existingMapping.Id;
        var tbRows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == targetDataset).ToListAsync();
        cashAmount = tbRows.FirstOrDefault(x => x.AccountCode == "1000")?.Amount ?? 100m;
        var rev = tbRows.Where(x => x.AccountCode.StartsWith("4")).Sum(x => -x.Amount);
        var exp = tbRows.Where(x => x.AccountCode.StartsWith("5")).Sum(x => x.Amount);
        netIncome = rev - exp;
        if (netIncome == 0) netIncome = 100m;
      }
      else
      {
        var period = Guid.NewGuid(); var book = Guid.NewGuid(); var dataset = Guid.NewGuid();
        db.ClientReportingPeriods.Add(new ClientReportingPeriod { Id = period, FirmId = w.FirmId, ClientId = w.ClientId, PeriodCode = "FY2026", StartDate = new DateOnly(2026,1,1), EndDate = new DateOnly(2026,12,31),
          Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active, CreatedByUserId = w.U["associate"].Id, CreatedAt = now });
        db.ClientReportingBooks.Add(new ClientReportingBook { Id = book, FirmId = w.FirmId, ClientId = w.ClientId, PeriodId = period, Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES",
          Currency = "QAR", Status = AccountingWorkflowStates.Active, CreatedByUserId = w.U["associate"].Id, CreatedAt = now });
        db.TrialBalanceDatasets.Add(new TrialBalanceDataset { Id = dataset, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, PeriodId = period, BookId = book, Basis = "STATUTORY",
          SourceKind = "Raw", Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ImportedAt = now, ImportedByUserId = w.U["associate"].Id });
        db.TrialBalanceRows.AddRange(new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = dataset, AccountCode = "1000", AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST" },
          new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = dataset, AccountCode = "4000", AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST" });
        targetDataset = dataset;
      }
      await db.SaveChangesAsync();
    }
    var preparer = w.A("associate", "AccountingPreparer"); var reviewer = w.A("senior", "AccountingReviewer"); var partner = w.A("partner", "Partner");
    Guid packageId;
    await using (var db = new AuditSphereDbContext(options))
    {
      if (targetMappingId == Guid.Empty)
      {
        var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer, new(targetDataset, "TEST-IFRS", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"), new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]));
        var mappingRow = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.Id == mapping.Value);
        var approved = await FinancialStatementService.ApproveMappingAsync(db, reviewer, mapping.Value, mappingRow.Version);
        Assert.True(approved.Succeeded, $"ApproveMapping failed: {approved.ErrorCode} - {approved.Message}");
        targetMappingId = mapping.Value;
      }
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, targetDataset, []);
      Assert.True(plan.Succeeded, plan.Message);
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, plan.Value);
      Assert.True(finalized.Succeeded, finalized.Message);
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, new(plan.Value, targetMappingId, "IFRS", "2026-01-01", "2026-12-31", "bundle-test-v1",
        new FinancialSupplementaryInformation(0m, netIncome, [new CashFlowLineInput("OPERATING", "Cash receipts", netIncome)],
          [new DisclosureInput("CASH_POLICY", "Cash is presented at face value."), new DisclosureInput("COMMITMENTS", "", true, "No commitments in the synthetic management information.")],
          [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, netIncome, 0m, 0m, 0m, netIncome, "equity-test")],
          NoteLines: [new NoteLineInput("CASH_NOTE", "CASH", cashAmount, "note-test")])));
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      packageId = built.Value.PackageId;
      Assert.True((await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId)).Succeeded);
      foreach (var (actor, stage, mode) in new[] { (preparer, FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewEvidenceModes.Offline),
        (reviewer, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewEvidenceModes.SignedIn), (partner, FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewEvidenceModes.SignedIn) })
      {
        var reviewed = await FinancialPackageReviewService.RecordAsync(db, actor, new(packageId, stage, FinancialPackageReviewDecisions.Approved, mode, "assembly-test-" + stage, "Reviewed the exact synthetic package."));
        Assert.True(reviewed.Succeeded, reviewed.Message);
      }
      Assert.True((await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, partner, packageId, FinancialPackageArtifactVersions.Pdf)).Succeeded);
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
      var root = Path.Combine(Path.GetTempPath(), "auditsphere-bundle-checkpoint", Guid.NewGuid().ToString("N"));
      try
      {
        var checkpoint = await ReleaseCheckpointService.RecordCheckpointDirectAsync(db, new LocalAppendOnlyCheckpointStore(root), partner, new(candidate.Value, 1, "test-bundle-release", digest, manifest));
        Assert.True(checkpoint.Succeeded, checkpoint.Message);
        var release = await ReleaseService.IssueAsync(db, partner, new(candidate.Value, 1, digest, "test-bundle-release"), new ReleaseSafetyOptions());
        Assert.True(release.Succeeded, release.Message);
        return (packageId, release.Value);
      }
      finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
  }

  public static async Task SeedPostedFeeAsync(DbContextOptions<AuditSphereDbContext> options, CompletionBundleScope w, bool automateBalance = false)
  {
    var now = DateTimeOffset.UtcNow;
    Guid proposalId;
    await using (var db = new AuditSphereDbContext(options))
    {
      var lead = new Lead { Id = Guid.NewGuid(), FirmId = w.FirmId, Name = "Synthetic bundle client", Source = "Referral", PrimaryContactEmail = "director@bundle.example.test", CreatedAt = now };
      var opportunity = new Opportunity { Id = Guid.NewGuid(), FirmId = w.FirmId, LeadId = lead.Id, ServiceRoute = "FinancialStatementAudit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now };
      var proposal = new Proposal { Id = Guid.NewGuid(), FirmId = w.FirmId, OpportunityId = opportunity.Id, PracticeClientId = w.ClientId, Status = CrmStates.ProposalAccepted,
        Currency = "QAR", Fee = 25000m, ServiceProfileId = "AUDIT-2026", Scope = "Synthetic statutory audit", Deliverables = "Auditor report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now, ResponseAt = now };
      proposalId = proposal.Id;
      db.Leads.Add(lead); db.Opportunities.Add(opportunity); db.Proposals.Add(proposal);
      db.QuotationVersions.Add(new QuotationVersion { Id = Guid.NewGuid(), FirmId = w.FirmId, ProposalId = proposal.Id, Currency = "QAR", BaseAmount = 25000m, Fee = 25000m, InputHash = Hashing.Sha256Hex("synthetic price"),
        Status = QuotationStates.Approved, CreatedByUserId = w.U["manager"].Id, ApprovedAt = now, CreatedAt = now });
      foreach (var (name, role) in new[] { ("manager", "FinanceManager"), ("partner2", "FinanceReviewer") })
        db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = w.U[name].Id, Role = role, GrantedByUserId = w.U["partner"].Id, GrantedAt = now });
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile { Id = Guid.NewGuid(), FirmId = w.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = w.U["partner2"].Id, ApprovedAt = now, CreatedAt = now });
      await db.Engagements.Where(x => x.Id == w.EngagementId).ExecuteUpdateAsync(x => x.SetProperty(e => e.PeriodStart, "2026-01-01"));
      await db.SaveChangesAsync();
      Assert.True((await AuditSphereOps.Application.Practice.CommercialDocumentService.SaveProfileAsync(db, w.A("partner", "Partner"), new("Synthetic audit firm", "Test address", "firm@bundle.example.test", "", "#0F766E", "Reviewed terms"))).Succeeded);
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
