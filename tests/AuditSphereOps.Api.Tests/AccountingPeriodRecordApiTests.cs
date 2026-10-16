using System.Net;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AccountingPeriodRecordApiTests
{
  [Fact]
  public async Task ExactPeriodRequiresClientAccountingScopeAndClearsAfterRevocation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PERIOD-EXACT-RECORD");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var periodId = Guid.NewGuid();
    var foreignPeriodId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientReportingPeriods.AddRange(
        Period(periodId, f.FirmId, f.ClientId, f.Staff.Id),
        Period(foreignPeriodId, foreign.FirmId, foreign.ClientId, foreign.Staff.Id));
      await db.SaveChangesAsync();
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var path = "/api/ui/accounting/periods/" + periodId;
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);

    // An engagement-only accounting grant cannot widen to client-level reporting periods.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId));
      await db.SaveChangesAsync();
    }
    using var visible = await client.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
    Assert.True(visible.Headers.CacheControl!.NoStore);
    using var body = System.Text.Json.JsonDocument.Parse(await visible.Content.ReadAsStringAsync());
    Assert.Equal(periodId, body.RootElement.GetProperty("id").GetGuid());
    Assert.Equal(f.ClientId, body.RootElement.GetProperty("clientId").GetGuid());
    Assert.Equal(System.Text.Json.JsonValueKind.Null, body.RootElement.GetProperty("pbc").ValueKind);
    Assert.Equal(System.Text.Json.JsonValueKind.Null, body.RootElement.GetProperty("task").ValueKind);

    using var hidden = await client.GetAsync("/api/ui/accounting/periods/" + foreignPeriodId);
    using var guessed = await client.GetAsync("/api/ui/accounting/periods/" + Guid.NewGuid());
    Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
    Assert.Equal(hidden.StatusCode, guessed.StatusCode);
    Assert.Equal(await hidden.Content.ReadAsStringAsync(), await guessed.Content.ReadAsStringAsync());
    Assert.True(hidden.Headers.CacheControl!.NoStore);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "AccountingPreparer")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(2, await db.ClientReportingPeriods.CountAsync());
  }

  private static ClientReportingPeriod Period(Guid id, Guid firm, Guid client, Guid actor) => new()
  {
    Id = id, FirmId = firm, ClientId = client, PeriodCode = "SYNTHETIC-FY2026",
    StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
    Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Draft,
    CreatedByUserId = actor, CreatedAt = DateTimeOffset.UtcNow
  };
}
