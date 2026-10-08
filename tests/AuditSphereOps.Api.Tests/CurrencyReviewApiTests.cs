using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Api.Tests;
public sealed class CurrencyReviewApiTests
{
  private static string Path(Guid id, string currency = "QAR", string percent = "10.123456", string amount = "0") => $"/api/ui/datasets/{id}/currency-review?presentation={currency}&percent={percent}&amount={amount}";
  [Fact]
  public async Task ExactClosingComparison_Provenance_Identity_Thresholds_AndRevocation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-REVIEW"); var f = await PbcSeed.SeedAsync(pg);
    CurrencyReviewFixture.Inputs i; await using (var db = new AuditSphereDbContext(pg.Options)) i = await CurrencyReviewFixture.SeedAsync(db, f);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); await c.GetAsync("/auth/sign-in");
    var v = await Get(c, Path(i.Dataset)); Assert.Equal(f.ClientId, v.GetProperty("clientId").GetGuid()); Assert.Equal(f.EngagementId, v.GetProperty("engagementId").GetGuid()); Assert.Equal(i.Period, v.GetProperty("periodId").GetGuid());
    Assert.Equal("UPLOAD_CLOSING_COMPARISON", v.GetProperty("method").GetString()); Assert.Equal("10.123456", v.GetProperty("thresholdPercent").GetString()); Assert.Equal(i.RateSet, v.GetProperty("currentRate").GetProperty("rateSetId").GetGuid()); Assert.NotEqual(Guid.Empty, v.GetProperty("currentRate").GetProperty("observationId").GetGuid()); Assert.Equal(i.PriorDataset, v.GetProperty("priorDatasetId").GetGuid());
    var cash = v.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("accountCode").GetString() == "1000"); Assert.Equal("100.123456", cash.GetProperty("sourceAmount").GetString()); Assert.Equal(370.46m, decimal.Parse(cash.GetProperty("translatedAmount").GetString()!, System.Globalization.CultureInfo.InvariantCulture)); Assert.Equal("324.00", cash.GetProperty("priorTranslatedAmount").GetString()); Assert.Equal("46.46", cash.GetProperty("variance").GetString()); Assert.True(cash.GetProperty("highlighted").GetBoolean());
    var identity = (await Get(c, Path(i.Dataset, "USD"))).GetProperty("currentRate"); Assert.Equal("IDENTITY", identity.GetProperty("rateType").GetString()); Assert.Equal("1", identity.GetProperty("rate").GetString()); Assert.Equal("Same currency", identity.GetProperty("source").GetString()); Assert.Equal(JsonValueKind.Null, identity.GetProperty("rateSetId").ValueKind); Assert.Equal(JsonValueKind.Null, identity.GetProperty("observationId").ValueKind);
    foreach (var (currency, percent, amount) in new[] { ("EUR", "10", "0"), ("QAR", "1.1234567", "0"), ("QAR", "1e3", "0"), ("QAR", "-1", "0"), ("QAR", "10", "1.00000000000000000000000000001") }) Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Path(i.Dataset, currency, percent, amount))).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) { (await db.Users.SingleAsync(x => x.Id == f.Staff.Id)).SessionEpoch++; await db.SaveChangesAsync(); }
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync(Path(i.Dataset))).StatusCode);
  }
  [Fact]
  public async Task PriorPeriodData_NeverWidensAnEngagementGrant_OrExpiredWiderGrant()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-PRIOR-SCOPE"); var f = await PbcSeed.SeedAsync(pg); var other = await PbcSeed.SeedAsync(pg);
    CurrencyReviewFixture.Inputs i, foreign; await using (var db = new AuditSphereDbContext(pg.Options)) { i = await CurrencyReviewFixture.SeedAsync(db, f, hiddenPrior: true); foreign = await CurrencyReviewFixture.SeedAsync(db, other); var expired = PbcSeed.AdminGrant(f.FirmId, f.Staff); expired.GrantedAt = DateTimeOffset.UtcNow.AddHours(-2); expired.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1); db.RoleGrants.Add(expired); await db.SaveChangesAsync(); }
    await using (var db = new AuditSphereDbContext(pg.Options)) { var r = await TrialBalanceCurrencyReviewQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "Staff"), i.Dataset, "QAR"); Assert.True(r.Succeeded, r.Message); Assert.Null(r.Value!.PriorDatasetId); Assert.All(r.Value.Lines, l => Assert.Null(l.PriorTranslatedAmount)); }
    await using (var db = new AuditSphereDbContext(pg.Options)) Assert.True(await AuditSphereOps.Application.Security.RoleGrantExpiry.RevokeExpiredForUserAsync(db, f.FirmId, f.Staff.Id, DateTimeOffset.UtcNow));
    using var sf = Factory(pg, f.Staff); using var af = Factory(pg, f.Admin); using var staff = sf.CreateClient(new() { AllowAutoRedirect = false }); using var admin = af.CreateClient(new() { AllowAutoRedirect = false }); await staff.GetAsync("/auth/sign-in"); await admin.GetAsync("/auth/sign-in");
    var deniedPrior = await Get(staff, Path(i.Dataset)); Assert.Equal(JsonValueKind.Null, deniedPrior.GetProperty("priorDatasetId").ValueKind); Assert.DoesNotContain("HIDDEN-PRIOR-MARKER", deniedPrior.ToString());
    var grantedPrior = await Get(admin, Path(i.Dataset)); Assert.Equal(i.PriorDataset, grantedPrior.GetProperty("priorDatasetId").GetGuid());
    foreach (var id in new[] { i.PriorDataset, foreign.Dataset, Guid.NewGuid() }) Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(Path(id))).StatusCode);
  }
  [Fact]
  public async Task InvalidLatestDirection_OrEffectiveDate_DoesNotFallback_AndFreshRevisionChanges()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-RATE-BOUNDARY"); var f = await PbcSeed.SeedAsync(pg); CurrencyReviewFixture.Inputs i;
    await using (var db = new AuditSphereDbContext(pg.Options)) i = await CurrencyReviewFixture.SeedAsync(db, f);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(new() { AllowAutoRedirect = false }); await c.GetAsync("/auth/sign-in"); var before = await Get(c, Path(i.Dataset)); Guid set = Guid.NewGuid(), observation = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options)) { var now = DateTimeOffset.UtcNow.AddMinutes(1); db.ExchangeRateSetVersions.Add(new() { Id = set, FirmId = f.FirmId, Code = "SYN-NEWER", Version = 2, Source = "Synthetic newer observation", Status = "APPROVED", CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id, CreatedAt = now, ApprovedAt = now }); db.ExchangeRates.Add(new() { Id = observation, FirmId = f.FirmId, RateSetVersionId = set, FromCurrency = "USD", ToCurrency = "QAR", RateDate = new(2026, 12, 31), RateType = "CLOSING", Rate = 3.9m, Direction = "INVERSE", CreatedAt = now }); await db.SaveChangesAsync(); }
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Path(i.Dataset))).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.ExchangeRates.Where(x => x.Id == observation).ExecuteUpdateAsync(s => s.SetProperty(x => x.Direction, "DIRECT"));
    var after = await Get(c, Path(i.Dataset)); Assert.NotEqual(before.GetProperty("revision").GetString(), after.GetProperty("revision").GetString()); Assert.Equal(set, after.GetProperty("currentRate").GetProperty("rateSetId").GetGuid());
    await using (var db = new AuditSphereDbContext(pg.Options)) await db.ExchangeRateSetVersions.Where(x => x.Id == set).ExecuteUpdateAsync(s => s.SetProperty(x => x.EffectiveFrom, new DateOnly(2027, 1, 1)));
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Path(i.Dataset))).StatusCode);
  }
  [Fact]
  public async Task ChangedPriorPeriod_OrRevokedActor_DuringRead_NeverPublishesTheOldComparison()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CURRENCY-READ-FENCE"); var f = await PbcSeed.SeedAsync(pg); CurrencyReviewFixture.Inputs i;
    await using (var db = new AuditSphereDbContext(pg.Options)) i = await CurrencyReviewFixture.SeedAsync(db, f);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var changed = new BeforePriorBalances(async () => { await using var db = new AuditSphereDbContext(pg.Options); var prior = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == i.PriorDataset); await db.ClientReportingPeriods.Where(x => x.Id == prior.PeriodId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, new DateOnly(2025, 12, 30))); });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(changed).Options)) { var result = await TrialBalanceCurrencyReviewQuery.GetAsync(db, actor, i.Dataset, "QAR"); Assert.False(result.Succeeded); Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.StaleRevision, result.ErrorCode); Assert.Null(result.Value); }
    await using (var db = new AuditSphereDbContext(pg.Options)) { var prior = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == i.PriorDataset); await db.ClientReportingPeriods.Where(x => x.Id == prior.PeriodId).ExecuteUpdateAsync(s => s.SetProperty(x => x.EndDate, new DateOnly(2025, 12, 31))); }
    var revoked = new BeforePriorBalances(async () => { await using var db = new AuditSphereDbContext(pg.Options); await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1)); });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revoked).Options)) { var result = await TrialBalanceCurrencyReviewQuery.GetAsync(db, actor, i.Dataset, "QAR"); Assert.False(result.Succeeded); Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.GenerationStale, result.ErrorCode); Assert.Null(result.Value); }
  }
  private sealed class BeforePriorBalances(Func<Task> change) : DbCommandInterceptor
  {
    private int reads;
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("trial_balance_rows", StringComparison.Ordinal) && ++reads == 2) await change();
      return result;
    }
  }
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AuditSphereOps.Domain.Security.AppUser u) => new(new Dictionary<string, string?> { ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId, ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<JsonElement> Get(HttpClient c, string path) { using var r = await c.GetAsync(path); Assert.Equal(HttpStatusCode.OK, r.StatusCode); using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return j.RootElement.Clone(); }
}
