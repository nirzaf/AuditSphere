using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal static class ReconciliationReviewSeed
{
  internal sealed record Result(PbcSeed.Fixture Fixture, Guid ReconciliationId, Guid SourceId, Guid SiblingId);
  internal static async Task<Result> SeedAsync(ITestPostgresDatabase pg)
  {
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var journal = await JournalReviewSeed.SeedAsync(db, f);
    var sourceId = (await db.AdjustmentJournals.AsNoTracking().SingleAsync(x => x.Id == journal)).BaseDatasetId;
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == sourceId);
    var preparer = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    var created = await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
      new(f.ClientId, f.EngagementId, source.PeriodId!.Value, source.BookId, "CASH", sourceId, null, ["1000"], new(2026,12,31)));
    if (!created.Succeeded) throw new InvalidOperationException(created.Message);
    var items = Enumerable.Range(1, 27).Select(i => new ReconciliationItemInput($"SYNTHETIC-ITEM-{i:D3}",
      i == 1 ? 10.123456m : i == 2 ? -10.123456m : 0m, "QAR", new(2026,12,20), "Synthetic timing difference",
      $"SYNTHETIC-EVIDENCE-{i:D3}", "UNRESOLVED")).ToArray();
    var added = await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, created.Value, items);
    if (!added.Succeeded) throw new InvalidOperationException(added.Message);
    var proof = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, created.Value);
    if (!proof.Succeeded) throw new InvalidOperationException(proof.Message);
    var approved = await AccountingAnalysisService.ApproveReconciliationAsync(db, PbcSeed.Actor(f.Reviewer,"AccountingReviewer"), created.Value);
    if (!approved.Succeeded) throw new InvalidOperationException(approved.Message);
    var otherClient = Guid.NewGuid(); var otherEngagement = Guid.NewGuid(); var otherPeriod = Guid.NewGuid(); var sibling = Guid.NewGuid();
    db.PracticeClients.Add(new() { Id=otherClient,FirmId=f.FirmId,LegalName="HIDDEN SYNTHETIC CLIENT",CreatedAt=DateTimeOffset.UtcNow });
    db.Engagements.Add(new() { Id=otherEngagement,FirmId=f.FirmId,PracticeClientId=otherClient,Status="Active",CreatedAt=DateTimeOffset.UtcNow });
    db.ClientReportingPeriods.Add(new() { Id=otherPeriod,FirmId=f.FirmId,ClientId=otherClient,PeriodCode="FY26",Basis="IFRS",Currency="QAR",
      StartDate=new(2026,1,1),EndDate=new(2026,12,31),CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow });
    db.AccountingReconciliations.Add(new() { Id=sibling,FirmId=f.FirmId,ClientId=otherClient,EngagementId=otherEngagement,PeriodId=otherPeriod,
      Area="HIDDEN SYNTHETIC AREA",AccountSelection="1000",AsOfDate=new(2026,12,31),CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    return new(f, created.Value, sourceId, sibling);
  }
}
