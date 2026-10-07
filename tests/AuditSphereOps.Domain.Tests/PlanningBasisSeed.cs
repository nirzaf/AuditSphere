using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Establishes the approved planning basis a substantive execution route requires (STE-REM-04) through
/// the real commands: a sealed, balanced trial balance, an approved FSLI mapping, and a current,
/// independently approved materiality calculation. Fixtures never insert a materiality approval
/// record directly; the approval runs through AuditPlanningService.
/// </summary>
internal static class PlanningBasisSeed
{
  public static async Task<(Guid DatasetId, Guid MappingId, Guid AssessmentId)> EstablishAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    ActorContext preparer, ActorContext reviewer,
    (string Code, string Name, decimal Amount, string Destination, string Section)[]? accounts = null)
  {
    accounts ??=
    [
      ("1000", "Cash", 10_000m, "CASH", "ASSETS"),
      ("3000", "Equity", -5_000m, "EQUITY", "EQUITY"),
      ("4000", "Revenue", -10_000m, "REVENUE", "INCOME"),
      ("5000", "Expense", 5_000m, "EXPENSE", "EXPENSE")
    ];
    var datasetId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading,
      NormalizedDatasetDigest = Hashing.Sha256Hex(datasetId.ToString()), ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.UserId
    });
    db.TrialBalanceRows.AddRange(accounts.Select(x => new TrialBalanceRow
    {
      Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = x.Code, AccountName = x.Name,
      Amount = x.Amount, Currency = "QAR", Entity = "TEST"
    }));
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == datasetId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      DatasetId = datasetId, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = preparer.UserId,
      ApprovedByUserId = reviewer.UserId, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    db.MappingAllocations.AddRange(accounts.Select(x => new MappingAllocation
    {
      Id = Guid.CreateVersion7(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      MappingVersionId = mappingId, SourceAccountCode = x.Code, DestinationCode = x.Destination,
      StatementSection = x.Section, Fraction = 1m, Rationale = "Reviewed source mapping", CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync();
    var materiality = await MaterialityEngineService.CalculateAsync(db, preparer,
      new(engagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Revenue is the stable benchmark."));
    Xunit.Assert.True(materiality.Succeeded, materiality.Message ?? "materiality calculation failed");
    var approved = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, reviewer, materiality.Value!.AssessmentId);
    Xunit.Assert.True(approved.Succeeded, approved.Message ?? "materiality approval failed");
    await db.SaveChangesAsync();
    return (datasetId, mappingId, materiality.Value.AssessmentId);
  }

  /// <summary>Approves current materiality over an already-sealed source through the real calculate/approve commands.</summary>
  public static async Task<Guid> ApproveMaterialityAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId,
    ActorContext preparer, ActorContext reviewer)
  {
    var materiality = await MaterialityEngineService.CalculateAsync(db, preparer,
      new(engagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Revenue is the stable benchmark."));
    Xunit.Assert.True(materiality.Succeeded, materiality.Message ?? "materiality calculation failed");
    var approved = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, reviewer, materiality.Value!.AssessmentId);
    Xunit.Assert.True(approved.Succeeded, approved.Message ?? "materiality approval failed");
    await db.SaveChangesAsync();
    return materiality.Value.AssessmentId;
  }
}
