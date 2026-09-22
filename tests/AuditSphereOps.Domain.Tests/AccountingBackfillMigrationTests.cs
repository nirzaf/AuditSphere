using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class AccountingBackfillMigrationTests
{
  private const string Previous = "20260921212202_ResolveScheduleControlSource";

  [Fact]
  [Trait("Profile", "Database")]
  public async Task LegacyContextBackfill_BindsSingleMatchesAndQuarantinesAmbiguity()
  {
    await using var pg = await PgTestSchema.CreateAsync(Previous);
    var firmId = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var exact = SeedScope(firmId, userId, "EXACT", 1);
    var ambiguous = SeedScope(firmId, userId, "AMBIGUOUS", 2);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(new AppUser
      {
        Id = userId, FirmId = firmId, Subject = "backfill-user", TenantId = "test",
        Email = "backfill@example.invalid", DisplayName = "Backfill fixture", CreatedAt = DateTimeOffset.UtcNow
      });
      Add(db, exact);
      Add(db, ambiguous);
      await db.SaveChangesAsync();
      await db.Database.MigrateAsync();
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(exact.Periods[0].Id, await verify.TrialBalanceDatasets
      .Where(x => x.Id == exact.Dataset.Id).Select(x => x.PeriodId).SingleAsync());
    Assert.Equal(exact.Charts[0].Id, await verify.MappingVersions
      .Where(x => x.Id == exact.Mapping.Id).Select(x => x.ClientChartVersionId).SingleAsync());
    Assert.Null(await verify.TrialBalanceDatasets
      .Where(x => x.Id == ambiguous.Dataset.Id).Select(x => x.PeriodId).SingleAsync());
    Assert.Null(await verify.MappingVersions
      .Where(x => x.Id == ambiguous.Mapping.Id).Select(x => x.ClientChartVersionId).SingleAsync());

    var quarantine = await verify.AccountingBackfillQuarantines
      .Where(x => x.ClientId == ambiguous.Client.Id).OrderBy(x => x.ContextKind).ToListAsync();
    Assert.Equal(2, quarantine.Count);
    Assert.All(quarantine, x => Assert.Equal(2, x.CandidateCount));
    Assert.Contains(quarantine, x => x.TargetId == ambiguous.Dataset.Id && x.ContextKind == "REPORTING_PERIOD");
    Assert.Contains(quarantine, x => x.TargetId == ambiguous.Mapping.Id && x.ContextKind == "CLIENT_CHART_VERSION");

    var error = await Assert.ThrowsAsync<PostgresException>(() => verify.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM accounting_backfill_quarantines WHERE id = {quarantine[0].Id}"));
    Assert.Contains("append-only", error.MessageText);
  }

  private static Fixture SeedScope(Guid firmId, Guid userId, string name, int candidateCount)
  {
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var client = new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = name, CreatedAt = DateTimeOffset.UtcNow
    };
    var engagement = new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = DateTimeOffset.UtcNow
    };
    var periods = Enumerable.Range(1, candidateCount).Select(i => new ClientReportingPeriod
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, PeriodCode = $"FY2026-{i}",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "IFRS", Currency = "QAR", CreatedByUserId = userId, CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    var charts = Enumerable.Range(1, candidateCount).Select(i => new ClientChartVersion
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, Version = i,
      SourceScope = $"{name}-{i}", Status = AccountingWorkflowStates.Approved,
      EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31),
      CreatedByUserId = userId, PublishedByUserId = userId,
      PublishedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    var dataset = new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      Basis = "IFRS", SourceKind = "Raw", LegalEntityKey = name, Currency = "QAR",
      RawFileSha256Hex = new string(name == "EXACT" ? 'a' : 'b', 64),
      NormalizedDatasetDigest = new string(name == "EXACT" ? 'c' : 'd', 64),
      Sha256Hex = new string(name == "EXACT" ? 'c' : 'd', 64),
      ImportProfileVersion = "tb-signed-net.v1", SourceLayout = TrialBalanceLayouts.SignedNet,
      Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed,
      ImportedByUserId = userId, ImportedAt = DateTimeOffset.UtcNow
    };
    var mapping = new MappingVersion
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      DatasetId = datasetId, TaxonomyVersion = "tax-v1", PeriodStart = engagement.PeriodStart,
      PeriodEnd = engagement.PeriodEnd, CreatedByUserId = userId, CreatedAt = DateTimeOffset.UtcNow
    };
    return new(client, engagement, periods, charts, dataset, mapping);
  }

  private static void Add(AuditSphereDbContext db, Fixture fixture)
  {
    db.PracticeClients.Add(fixture.Client);
    db.Engagements.Add(fixture.Engagement);
    db.ClientReportingPeriods.AddRange(fixture.Periods);
    db.ClientChartVersions.AddRange(fixture.Charts);
    db.TrialBalanceDatasets.Add(fixture.Dataset);
    db.MappingVersions.Add(fixture.Mapping);
  }

  private sealed record Fixture(
    PracticeClient Client,
    Engagement Engagement,
    ClientReportingPeriod[] Periods,
    ClientChartVersion[] Charts,
    TrialBalanceDataset Dataset,
    MappingVersion Mapping);
}
