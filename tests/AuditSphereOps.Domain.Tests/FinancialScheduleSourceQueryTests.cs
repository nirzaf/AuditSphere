using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FinancialScheduleSourceQueryTests
{
  [Fact]
  public async Task AcceptedSourceLines_AreScopedBoundedSignedAndRepeatable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    GeneralLedgerCompletenessSeed.Pair pair;
    await using (var db = new AuditSphereDbContext(pg.Options)) pair = await GeneralLedgerCompletenessSeed.SeedAsync(db, f, accounts: 28);
    var actor = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    await using var read = new AuditSphereDbContext(pg.Options);
    Assert.Equal(ErrorCodes.GateBlocked, (await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, pair.PeriodId)).ErrorCode);
    var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(read, PbcSeed.Actor(f.Reviewer, "AccountingReviewer"),
      new(f.ClientId, f.EngagementId, AccountingSourceKinds.GeneralLedger, null, pair.BatchId, "Synthetic source review"));
    Assert.True(accepted.Succeeded, accepted.Message);
    var first = await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, pair.PeriodId);
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal(accepted.Value, first.Value!.AcceptanceId);
    Assert.Equal(pair.BookId, first.Value.BookId);
    Assert.Equal(25, first.Value.Lines.Count);
    Assert.True(first.Value.HasMore);
    Assert.All(first.Value.Lines, x => { Assert.Matches("^[a-f0-9]{64}$", x.LineHash); Assert.Equal("QAR", x.Currency); });
    var again = await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, pair.PeriodId);
    Assert.Equal(first.Value.Lines, again.Value!.Lines);
    var next = await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, pair.PeriodId, 2);
    Assert.Equal(3, next.Value!.Lines.Count);
    Assert.False(next.Value.HasMore);
    Assert.Empty(first.Value.Lines.Select(x => x.LineId).Intersect(next.Value.Lines.Select(x => x.LineId)));
    Assert.Contains(first.Value.Lines.Concat(next.Value.Lines), x => x.SignedAmount == "-100.123456");
    Assert.Contains(first.Value.Lines.Concat(next.Value.Lines), x => x.SignedAmount == "100.123456");
    Assert.Equal(ErrorCodes.ScopeDenied, (await FinancialScheduleSourceQuery.GetAsync(read, PbcSeed.Actor(f.Client, "ClientUser"), f.EngagementId, pair.PeriodId)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await FinancialScheduleSourceQuery.GetAsync(read, actor, Guid.NewGuid(), pair.PeriodId)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, Guid.NewGuid())).ErrorCode);
    var prior = await read.ClientReportingPeriods.Where(x => x.ClientId == f.ClientId && x.Id != pair.PeriodId).Select(x => x.Id).SingleAsync();
    Assert.Equal(ErrorCodes.GateBlocked, (await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, prior)).ErrorCode);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, (await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, pair.PeriodId, 0)).ErrorCode);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, (await FinancialScheduleSourceQuery.GetAsync(read, actor, f.EngagementId, pair.PeriodId, 1001)).ErrorCode);
  }

  [Fact]
  public async Task AcceptanceWithDifferentSourceDigestCannotExposeLines()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var pair = await GeneralLedgerCompletenessSeed.SeedAsync(db, f);
    db.SourceAcceptanceDecisions.Add(new SourceAcceptanceDecision { Id = Guid.CreateVersion7(), FirmId = f.FirmId,
      ClientId = f.ClientId, EngagementId = f.EngagementId, SourceKind = "GL", ImportBatchId = pair.BatchId,
      SourceIdentityHash = Hashing.Sha256Hex("Deliberately inconsistent synthetic receipt"), EvidenceReference = "Negative fixture",
      AcceptedByUserId = f.Reviewer.Id, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var result = await FinancialScheduleSourceQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "AccountingPreparer"), f.EngagementId, pair.PeriodId);
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Null(result.Value);
  }
}
