using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Testing;
internal static class CurrencyReviewFixture
{
  internal sealed record Inputs(Guid Dataset, Guid PriorDataset, Guid RateSet, Guid Period);
  internal static async Task<Inputs> SeedAsync(AuditSphereDbContext db, PbcSeed.Fixture f, bool hiddenPrior = false)
  {
    var fx = await RemeasurementFixture.SeedAsync(db, f); var now = DateTimeOffset.UtcNow;
    Guid current = Guid.NewGuid(), prior = Guid.NewGuid(), priorPeriod = Guid.NewGuid(), priorEngagement = hiddenPrior ? Guid.NewGuid() : f.EngagementId;
    if (hiddenPrior) db.Engagements.Add(new() { Id = priorEngagement, FirmId = f.FirmId, PracticeClientId = f.ClientId, Status = "Active", CreatedAt = now });
    db.ClientReportingPeriods.Add(new() { Id = priorPeriod, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY25-SYNTHETIC", StartDate = new(2025, 1, 1), EndDate = new(2025, 12, 31), Basis = "IFRS", Currency = "USD", CreatedByUserId = f.Admin.Id, CreatedAt = now });
    (await db.ClientReportingPeriods.SingleAsync(x => x.Id == fx.Period)).PriorPeriodId = priorPeriod;
    (await db.ExchangeRateSetVersions.SingleAsync(x => x.Id == fx.RateSet)).EffectiveFrom = new(2025, 1, 1);
    db.ExchangeRates.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, RateSetVersionId = fx.RateSet, FromCurrency = "USD", ToCurrency = "QAR", RateDate = new(2025, 12, 31), RateType = "CLOSING", Direction = "DIRECT", Rate = 3.6m, CreatedAt = now });
    foreach (var (id, period, engagement, amount, imported) in new[] { (current, fx.Period, f.EngagementId, 100.123456m, now), (prior, priorPeriod, priorEngagement, 90m, now.AddDays(-1)) })
    {
      db.TrialBalanceDatasets.Add(new() { Id = id, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = engagement, PeriodId = period, Currency = "USD", LegalEntityKey = "SYNTHETIC-ENTITY", NormalizedDatasetDigest = new string('b', 64), Sha256Hex = new string('b', 64), RawFileSha256Hex = new string(id == current ? 'c' : 'd', 64), Balanced = true, ValidationStatus = "Accepted", ImportState = "LOADING", ImportedAt = imported, ImportedByUserId = f.Staff.Id });
      db.TrialBalanceRows.AddRange(new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = id, AccountCode = "1000", AccountName = hiddenPrior && id == prior ? "HIDDEN-PRIOR-MARKER" : "Synthetic cash", Amount = amount }, new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = id, AccountCode = "3000", AccountName = "Synthetic capital", Amount = -amount });
    }
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == current || x.Id == prior).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    return new(current, prior, fx.RateSet, fx.Period);
  }
}
