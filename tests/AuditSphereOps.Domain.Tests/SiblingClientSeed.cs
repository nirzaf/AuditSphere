using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Populates a sibling client in an existing firm with marker-named records across the practice, PBC and
/// client-accounting capabilities. Authorization isolation tests use it to prove that a user scoped to another
/// client observes no change at all when this client gains data, and cannot reach it by identifier.
/// </summary>
internal static class SiblingClientSeed
{
  internal sealed record Sibling(
    PbcSeed.Fixture Fixture, string Marker, Guid PeriodId, Guid PackageId, Guid MappingId, Guid JournalId,
    Guid PbcRequestId, Guid UploadIntentId, string StagingRoot);

  internal static async Task<Sibling> SeedAsync(ITestPostgresDatabase pg, Guid firmId, string marker)
  {
    var now = DateTimeOffset.UtcNow;
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var staff = PbcSeed.User(firmId, "Staff");
    var reviewer = PbcSeed.User(firmId, "Staff");
    var client = PbcSeed.User(firmId, "Client");
    var admin = PbcSeed.User(firmId, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new PracticeClient { Id = clientId, FirmId = firmId, LegalName = $"{marker} Holdings", CreatedAt = now });
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = firmId, PracticeClientId = clientId, Status = "Active", ProfessionalWorkBlocked = false,
        ServiceRoute = "FinancialStatementAudit", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
      db.Users.AddRange(staff, reviewer, client, admin);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(firmId, staff, "Staff", clientId, engagementId),
        PbcSeed.Grant(firmId, staff, "AccountingPreparer", clientId),
        PbcSeed.Grant(firmId, reviewer, "Reviewer", clientId, engagementId),
        PbcSeed.Grant(firmId, reviewer, "AccountingReviewer", clientId),
        PbcSeed.Grant(firmId, client, "ClientUser", clientId, engagementId),
        PbcSeed.Grant(firmId, admin, "Manager", clientId));
      db.ClientPortalFirstSignIns.Add(PbcSeed.FirstSignIn(client));
      await db.SaveChangesAsync();
    }
    var fixture = new PbcSeed.Fixture(firmId, clientId, engagementId, staff, reviewer, client, admin);
    var preparer = PbcSeed.Actor(staff, "AccountingPreparer");

    Guid periodId, chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Require((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(clientId, "QA", "QAR", 1, 1, $"{marker}-LEDGER", $"{marker}-1"))).Succeeded, "profile");
      var period = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(clientId, $"{marker}-2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"));
      Require(period.Succeeded, "period " + period.Message);
      periodId = period.Value;
      Require((await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(clientId, periodId, $"{marker}-BOOK", "STATUTORY", "STATUTORY_ONLY", "QAR"))).Succeeded, "book");
      var chart = await ClientAccountingService.CreateChartVersionAsync(db, preparer, clientId, $"{marker}-LEDGER", new DateOnly(2026, 1, 1));
      Require(chart.Succeeded, "chart " + chart.Message);
      chartId = chart.Value;
      Require((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("cash", "1000", $"{marker} Cash", "ASSET", "DEBIT", true),
        new("revenue", "4000", $"{marker} Revenue", "INCOME", "CREDIT", true)
      ])).Succeeded, "accounts");
    }

    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var revisedPackageId = Guid.CreateVersion7();
    var journalId = Guid.CreateVersion7();
    var taskId = Guid.NewGuid();
    var digest = Hashing.Sha256Hex($"sibling-{clientId:D}-{marker}");
    var revisedDigest = Hashing.Sha256Hex($"sibling-revised-{clientId:D}-{marker}");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw", Revision = 1,
        LegalEntityKey = clientId.ToString("D"), Currency = "QAR", RawFileSha256Hex = digest, NormalizedDatasetDigest = digest,
        Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed,
        ControlTotal = 0m, ImportedAt = now, ImportedByUserId = staff.Id
      });
      db.AdjustmentPlans.Add(new AdjustmentPlan
      {
        Id = planId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
        Status = "Finalized", ResultHash = digest, CreatedByUserId = staff.Id, CreatedAt = now
      });
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId, Version = 7,
        Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = AccountingPackageStates.MappingApproved, CreatedByUserId = staff.Id, ApprovedByUserId = reviewer.Id,
        ApprovedAt = now, CreatedAt = now
      });
      db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
      {
        Id = adjustedId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
        AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest, CreatedByUserId = staff.Id, CreatedAt = now
      });
      db.FinancialPackages.Add(new FinancialPackage
      {
        Id = packageId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
        MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1",
        CalculationEngineVersion = "test-engine", CalculationHash = digest, Currency = "QAR",
        Status = AccountingPackageStates.PackageValidated, CreatedAt = now
      });
      db.FinancialPackages.Add(new FinancialPackage
      {
        Id = revisedPackageId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
        MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", TaxonomyVersion = "tax-v1", TemplateVersion = "template-v2",
        CalculationEngineVersion = "test-engine", CalculationHash = revisedDigest, Currency = "QAR",
        Status = AccountingPackageStates.PackageValidated, CreatedAt = now
      });
      db.AdjustmentJournals.Add(new AdjustmentJournal
      {
        Id = journalId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
        JournalNumber = $"{marker}-AJ-001", PeriodId = periodId, Basis = "STATUTORY", Currency = "QAR", Reason = $"{marker} journal reason",
        EvidenceReference = $"{marker}-EVIDENCE", CreatedByUserId = staff.Id, CreatedAt = now
      });
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = Guid.CreateVersion7(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        AccountArea = $"{marker} Revenue", DifferenceType = "Factual", Description = $"{marker} cut-off difference",
        Amount = 987654.32m, Currency = "QAR", CreatedByUserId = staff.Id, CreatedAt = now
      });
      db.ClientPeriodRestatements.Add(new ClientPeriodRestatement
      {
        Id = Guid.CreateVersion7(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, PeriodId = periodId,
        OriginalPackageId = packageId, RevisedPackageId = revisedPackageId, OriginalPackageHash = digest, RevisedPackageHash = revisedDigest,
        RevisedBasis = "STATUTORY", ChangeType = "RECLASSIFIED", AffectedPeriods = $"{marker}-2026",
        Reason = $"{marker} restatement", EvidenceReference = $"{marker}-RESTATEMENT", CreatedByUserId = reviewer.Id, CreatedAt = now
      });
      db.WorkTasks.Add(new WorkTask
      {
        Id = taskId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, ReportingPeriodId = periodId,
        Title = $"{marker} close task", AssigneeUserId = staff.Id, DueDate = new DateOnly(2027, 1, 15), CreatedAt = now
      });
      db.TimeEntries.Add(new TimeEntry
      {
        Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, TaskId = taskId, UserId = staff.Id,
        WorkDate = DateOnly.FromDateTime(DateTime.UtcNow), StartMinute = 540, DurationMinutes = 95, Role = "Staff",
        Activity = $"{marker} fieldwork", Narrative = $"{marker} narrative",
        BillableClassification = PracticeTimeStates.NonBillable
      });
      await db.SaveChangesAsync();
    }

    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, fixture, PbcSeed.Actor(staff, "Staff"), PbcSeed.Actor(client, "ClientUser"));
    var staged = await PbcSeed.StageUploadAsync(pg, fixture, PbcSeed.Actor(client, "ClientUser"), requestId,
      System.Text.Encoding.UTF8.GetBytes($"%PDF-1.7 {marker} bank statement"), $"{marker}-statement.pdf");
    return new Sibling(fixture, marker, periodId, packageId, mappingId, journalId, requestId, staged.UploadIntentId, staged.StagingRoot);
  }

  private static void Require(bool condition, string step)
  {
    if (!condition) throw new InvalidOperationException("sibling client seed failed: " + step);
  }
}
