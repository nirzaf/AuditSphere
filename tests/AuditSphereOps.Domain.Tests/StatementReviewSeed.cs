using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class StatementReviewSeed
{
  internal sealed record Context(Guid MappingId, Guid DatasetId, Guid CashProcedureId);
  internal static async Task<Context> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f, bool large = false)
  {
    var seed = await MappingApprovalSeed.SeedAsync(db, f, seal: false);
    if (large)
    {
      for (var i = 1; i <= 30; i++)
      {
        var code = (1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
        db.TrialBalanceRows.Add(new() { Id = Guid.NewGuid(), DatasetId = seed.DatasetId, AccountCode = code, AccountName = "Synthetic cash " + code, Amount = 0.000001m, Currency = "QAR", Entity = "SYN-MAPPING" });
        db.MappingAllocations.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, MappingVersionId = seed.MappingId,
          SourceAccountCode = code, DestinationCode = "CASH", StatementSection = "ASSETS", Fraction = 1m, Rationale = "Synthetic paged exact contribution", CreatedAt = DateTimeOffset.UtcNow });
      }
      db.TrialBalanceRows.Add(new() { Id = Guid.NewGuid(), DatasetId = seed.DatasetId, AccountCode = "3100", AccountName = "=SYNTHETIC_FORMULA", Amount = -0.000030m, Currency = "QAR", Entity = "SYN-MAPPING" });
      db.MappingAllocations.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, MappingVersionId = seed.MappingId,
        SourceAccountCode = "3100", DestinationCode = "REVENUE", StatementSection = "INCOME", Fraction = 1m, Rationale = "Synthetic balance offset", CreatedAt = DateTimeOffset.UtcNow });
    }
    var proc = Guid.NewGuid();
    db.AuditProcedures.Add(new AuditProcedure { Id = proc, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      SourceProcedureId = "CSH-01", SourceSectionTitle = "Cash", Title = "Synthetic cash verification", SourceWording = "Inspect exact source contributions", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Planned, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == seed.DatasetId).ExecuteUpdateAsync(x => x.SetProperty(d => d.ImportState, TrialBalanceImportStates.Sealed));
    var actor = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var review = await MappingApprovalWorkspace.GetAsync(db, actor, seed.MappingId);
    if (!review.Succeeded) throw new InvalidOperationException(review.Message);
    var approval = await MappingApprovalWorkspace.ApproveAsync(db, actor, seed.MappingId, review.Value!.Revision, true);
    if (!approval.Succeeded) throw new InvalidOperationException(approval.Message);
    return new(seed.MappingId, seed.DatasetId, proc);
  }
}
