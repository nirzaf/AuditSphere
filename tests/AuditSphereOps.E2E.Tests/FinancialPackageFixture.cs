using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditSphereOps.E2E.Tests;

internal static class FinancialPackageFixture
{
  internal static async Task<(Guid PackageId, Dictionary<string, (byte[] Bytes, string Sha256)> Expected)> CreatePackageAsync(
    OwnedHost host)
  {
    var firmId = host.Fixture.FirmId;
    var clientId = host.Fixture.ClientId;
    var engagementId = host.Fixture.EngagementId;
    var preparer = PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer");
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var taxonomyId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(firmId, host.Fixture.Staff, "AccountingPreparer", clientId, engagementId),
        PbcSeed.Grant(firmId, host.Fixture.Reviewer, "AccountingReviewer", clientId, engagementId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026-E2E",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
        Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
      {
        Id = taxonomyId, FirmId = firmId, Code = "tax-e2e-v1", Framework = "IFRS", Name = "Synthetic E2E taxonomy",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
        CreatedByUserId = host.Fixture.Staff.Id, ApprovedByUserId = host.Fixture.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
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
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", Revision = 1,
        Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ControlTotal = 0m,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = host.Fixture.Staff.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
          Amount = 180_000m, Currency = "QAR", Entity = "SYNTHETIC"
        },
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000", AccountName = "Revenue",
          Amount = -180_000m, Currency = "QAR", Entity = "SYNTHETIC"
        });
      await db.SaveChangesAsync();
    }

    Guid mappingId;
    await using (var db = host.CreateDbContext())
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(datasetId, "tax-e2e-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Synthetic cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Synthetic revenue mapping")
        ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      mappingId = mapping.Value;
      var approved = await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1);
      Assert.True(approved.Succeeded, approved.Message);
    }

    Guid planId;
    await using (var db = host.CreateDbContext())
    {
      var journal = await AdjustmentJournalService.CreateDraftAsync(db, preparer, datasetId, "AJ-E2E-001",
        [("1000", 0m, 5_000m), ("4000", 5_000m, 0m)]);
      Assert.True(journal.Succeeded, journal.Message);
      var posted = await AdjustmentJournalService.PostAsync(db, reviewer, journal.Value!);
      Assert.True(posted.Succeeded, posted.Message);
      var reconciled = await SourceReconciliationService.ResolveAsync(db, reviewer, datasetId,
        "AJ-E2E-001", 1, ReflectionStates.NotReflected, "Synthetic source-ledger review: adjustment not posted in client books.");
      Assert.True(reconciled.Succeeded, reconciled.Message);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, datasetId,
        [new PlanLineInput("AJ-E2E-001", 1)]);
      Assert.True(plan.Succeeded, plan.Message);
      planId = plan.Value;
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.True(finalized.Succeeded, finalized.Message);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "e2e-template-v1",
      new FinancialSupplementaryInformation(0m, 175_000m,
        [new CashFlowLineInput("OPERATING", "Synthetic cash collections", 175_000m)],
        [new DisclosureInput("E2E_POLICY", "Synthetic test policy note."),
         new DisclosureInput("E2E_OTHER", string.Empty, NotApplicable: true, Rationale: "No other synthetic items.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 175_000m, 0m, 0m, 0m, 175_000m, "e2e-equity")],
        NoteLines: [new NoteLineInput("E2E_NOTE", "CASH", 175_000m, "e2e-note")]));
    Guid packageId;
    var expected = new Dictionary<string, (byte[] Bytes, string Sha256)>(StringComparer.Ordinal);
    await using (var db = host.CreateDbContext())
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      packageId = built.Value.PackageId;
      var canonical = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(canonical.Succeeded, canonical.Message);
      foreach (var version in new[]
      {
        FinancialPackageArtifactVersions.Workbook,
        FinancialPackageArtifactVersions.Word,
        FinancialPackageArtifactVersions.Pdf
      })
      {
        var rendered = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, preparer, packageId, version);
        Assert.True(rendered.Succeeded, rendered.Message);
        expected.Add(version, (rendered.Value!.ArtifactBytes, rendered.Value.ArtifactSha256Hex));
      }
    }
    return (packageId, expected);
  }
}
