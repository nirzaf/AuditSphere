using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class BudgetApprovalReviewApiTests
{
  [Fact]
  public async Task ExactReviewCsrfCurrentAuthorityReceiptsAndRetentionMigrationAreEnforced()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-BUDGET-APPROVAL");
    var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg); Guid budgetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var migrations = db.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_NativeBudgetApprovalReview", StringComparison.Ordinal));
      Assert.True(index > 0);
      Assert.Contains(migrations[index], await db.Database.GetAppliedMigrationsAsync());
      budgetId = await BudgetApprovalReviewSeed.PopulateAsync(db, f);
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?> {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Admin.Subject, ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
    using var c = factory.CreateClient(new() { AllowAutoRedirect = false });
    var url = "/api/ui/engagements/" + f.EngagementId + "/budget-approval";
    var r = new BudgetApprovalRequest(Guid.NewGuid(), new(budgetId, "1"));
    Assert.Equal(HttpStatusCode.Unauthorized, (await Post(c, "forged", url + "/preview", r)).StatusCode);
    await c.GetAsync("/auth/sign-in"); using var session = await c.GetAsync("/api/ui/session");
    var csrf = Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "forged", url + "/preview", r)).StatusCode);
    using var preview = await Post(c, csrf, url + "/preview", r); Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
    Assert.True(preview.Headers.CacheControl!.NoStore);
    var p = JsonDocument.Parse(await preview.Content.ReadAsStringAsync()).RootElement.GetProperty("value")
      .Deserialize<BudgetApprovalPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, url, r)).StatusCode);
    r = r with { Reviewed = true, ReviewBasis = p.ReviewBasis, RequestHash = p.RequestHash };
    using var created = await Post(c, csrf, url, r); Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    using var replay = await Post(c, csrf, url, r); Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
    Assert.True((await c.GetFromJsonAsync<BudgetApprovalLookup>(url + "/receipts/" + r.RequestId + "?requestHash=" + p.RequestHash))!.Found);
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync("/api/ui/engagements/" + foreign.EngagementId + "/budget-approval/receipts/" + r.RequestId + "?requestHash=" + p.RequestHash)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await PermanentFileFreezeMigrationAssertions.AssertDowngradeBlockedAsync(db, "_NativeBudgetApprovalReview");
      Assert.Single(await db.BudgetApprovals.ToListAsync());
      await db.RoleGrants.Where(x => x.UserId == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await c.GetAsync(url + "/receipts/" + r.RequestId + "?requestHash=" + p.RequestHash)).StatusCode);
  }
  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object input)
  {
    var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(input) };
    r.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(r);
  }
}
