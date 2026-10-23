using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ChartAliasesAndDimensionsApiTests
{
  [Trait("CompletionUnit", "WU-015")]
  [Fact]
  public async Task ChartMutationEndpoints_Return403ForSiblingScopeWithoutEffects()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CHART-SCOPE");
    var seed = await PbcSeed.SeedAsync(pg);
    var siblingClientId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(seed.FirmId, seed.Staff, "AccountingPreparer", clientId: seed.ClientId),
        PbcSeed.Grant(seed.FirmId, seed.Reviewer, "AccountingReviewer", clientId: seed.ClientId));
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClientId, FirmId = seed.FirmId, LegalName = "SIBLING TEST CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = seed.FirmId });
      await db.SaveChangesAsync();
    }

    using var preparerFactory = Factory(pg, seed.Staff);
    using var preparer = preparerFactory.CreateClient();
    var preparerCsrf = await SignIn(preparer);
    using var reviewerFactory = Factory(pg, seed.Reviewer);
    using var reviewer = reviewerFactory.CreateClient();
    var reviewerCsrf = await SignIn(reviewer);

    // Establish successful writes through every mapped mutation before exercising sibling scope.
    using var create = await Post(preparer, preparerCsrf, $"/api/ui/accounting/clients/{seed.ClientId}/charts",
      new { latestVersion = "0", sourceScope = "TEST-SCOPE", effectiveFrom = "2026-01-01", reviewed = true });
    Assert.Equal(HttpStatusCode.OK, create.StatusCode);
    using var createBody = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
    var chartId = createBody.RootElement.GetProperty("id").GetGuid();

    using var addAccounts = await Post(preparer, preparerCsrf,
      $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/accounts",
      new { version = "1", accounts = new[] { new { stableIdentity = "cash", accountCode = "1000", accountName = "Cash", accountType = "ASSET", normalBalance = "DEBIT", isPosting = true } }, reviewed = true });
    Assert.Equal(HttpStatusCode.NoContent, addAccounts.StatusCode);
    Guid accountId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      accountId = await db.ClientAccounts.Where(x => x.FirmId == seed.FirmId && x.ClientId == seed.ClientId && x.ChartVersionId == chartId)
        .Select(x => x.Id).SingleAsync();

    using var addAliases = await Post(preparer, preparerCsrf,
      $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases",
      new { version = "1", aliases = new[] { new { clientAccountId = accountId, sourceSystem = "TEST", aliasCode = "CASH-1000", aliasName = "Cash" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.NoContent, addAliases.StatusCode);

    var publication = await Read(reviewer, $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/publication");
    using var publish = await Post(reviewer, reviewerCsrf,
      $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/publish",
      new { version = publication.GetProperty("version").GetString(), digest = publication.GetProperty("digest").GetString(), reviewed = true });
    Assert.Equal(HttpStatusCode.NoContent, publish.StatusCode);

    using var addDimensions = await Post(preparer, preparerCsrf, $"/api/ui/accounting/clients/{seed.ClientId}/dimensions",
      new { dimensions = new[] { new { dimensionType = "BRANCH", code = "BR01", name = "Main Branch" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.NoContent, addDimensions.StatusCode);

    var before = await ReadMutationCounts(pg, seed.FirmId, chartId);
    Assert.Equal((1, 1, 1, 1, AccountingWorkflowStates.Approved), before);

    using var deniedCreate = await Post(preparer, preparerCsrf, $"/api/ui/accounting/clients/{siblingClientId}/charts",
      new { latestVersion = "0", sourceScope = "TEST-SCOPE", effectiveFrom = "2026-01-01", reviewed = true });
    await AssertScopeDenied(deniedCreate);

    using var deniedAccounts = await Post(preparer, preparerCsrf,
      $"/api/ui/accounting/clients/{siblingClientId}/charts/{chartId}/accounts",
      new { version = "1", accounts = new[] { new { stableIdentity = "bank", accountCode = "1010", accountName = "Bank", accountType = "ASSET", normalBalance = "DEBIT", isPosting = true } }, reviewed = true });
    await AssertScopeDenied(deniedAccounts);

    using var deniedAliases = await Post(preparer, preparerCsrf,
      $"/api/ui/accounting/clients/{siblingClientId}/charts/{chartId}/aliases",
      new { version = "1", aliases = new[] { new { clientAccountId = accountId, sourceSystem = "OTHER", aliasCode = "CASH-ALT", aliasName = "Cash alternate" } }, reviewed = true });
    await AssertScopeDenied(deniedAliases);

    using var deniedPublish = await Post(reviewer, reviewerCsrf,
      $"/api/ui/accounting/clients/{siblingClientId}/charts/{chartId}/publish",
      new { version = publication.GetProperty("version").GetString(), digest = publication.GetProperty("digest").GetString(), reviewed = true });
    await AssertScopeDenied(deniedPublish);

    using var deniedDimensions = await Post(preparer, preparerCsrf, $"/api/ui/accounting/clients/{siblingClientId}/dimensions",
      new { dimensions = new[] { new { dimensionType = "BRANCH", code = "BR02", name = "Sibling Branch" } }, reviewed = true });
    await AssertScopeDenied(deniedDimensions);

    Assert.Equal(before, await ReadMutationCounts(pg, seed.FirmId, chartId));
  }

  [Fact]
  public async Task ChartAliases_CanAddAndQuery_AndRejectsUnreviewedOrDuplicates()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CHART-ALIASES");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid chartId, accountId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "AccountingPreparer", clientId: seed.ClientId));
      await db.SaveChangesAsync();

      var preparer = PbcSeed.Actor(seed.Staff, "AccountingPreparer");
      chartId = (await ClientAccountingService.CreateChartVersionAsync(db, preparer, seed.ClientId, "SCOPE", new(2026, 1, 1))).Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db, preparer, chartId,
        [new("1000", "1000", "Cash", "ASSET", "DEBIT", true)], expectedClientId: seed.ClientId, expectedVersion: 1)).Succeeded);
      accountId = await db.ClientAccounts.Where(x => x.ChartVersionId == chartId).Select(x => x.Id).SingleAsync();
    }

    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);

    // Initial aliases empty
    var initial = await Read(client, $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases");
    Assert.Equal(0, initial.GetArrayLength());

    // Unreviewed fails
    var unreviewed = await Post(client, csrf, $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases",
      new { version = "1", aliases = new[] { new { clientAccountId = accountId, sourceSystem = "XERO", aliasCode = "100-BANK", aliasName = "Bank Account" } }, reviewed = false });
    Assert.Equal(HttpStatusCode.BadRequest, unreviewed.StatusCode);

    // Valid add
    var valid = await Post(client, csrf, $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases",
      new { version = "1", aliases = new[] { new { clientAccountId = accountId, sourceSystem = "XERO", aliasCode = "100-BANK", aliasName = "Bank Account" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);

    // Query aliases returns added alias
    var after = await Read(client, $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases");
    Assert.Equal(1, after.GetArrayLength());
    var alias = after[0];
    Assert.Equal(accountId, alias.GetProperty("clientAccountId").GetGuid());
    Assert.Equal("1000", alias.GetProperty("accountCode").GetString());
    Assert.Equal("XERO", alias.GetProperty("sourceSystem").GetString());
    Assert.Equal("100-BANK", alias.GetProperty("aliasCode").GetString());
    Assert.Equal("Bank Account", alias.GetProperty("aliasName").GetString());

    // Duplicate add fails
    var duplicate = await Post(client, csrf, $"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases",
      new { version = "1", aliases = new[] { new { clientAccountId = accountId, sourceSystem = "XERO", aliasCode = "100-BANK", aliasName = "Bank Account" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);

    // CSRF required
    var noCsrf = await client.PostAsJsonAsync($"/api/ui/accounting/clients/{seed.ClientId}/charts/{chartId}/aliases",
      new { version = "1", aliases = new[] { new { clientAccountId = accountId, sourceSystem = "SAP", aliasCode = "SAP-100", aliasName = "SAP Bank" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
  }

  [Fact]
  public async Task ClientDimensions_CanAddAndQuery_AndRejectsInvalidTypesOrDuplicates()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CLIENT-DIMENSIONS");
    var seed = await PbcSeed.SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "AccountingPreparer", clientId: seed.ClientId));
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);

    // Initial dimensions empty
    var initial = await Read(client, $"/api/ui/accounting/clients/{seed.ClientId}/dimensions");
    Assert.Equal(0, initial.GetArrayLength());

    // Unsupported dimension type fails
    var unsupported = await Post(client, csrf, $"/api/ui/accounting/clients/{seed.ClientId}/dimensions",
      new { dimensions = new[] { new { dimensionType = "INVALID_TYPE", code = "BR01", name = "Main Branch" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);

    // Valid add
    var valid = await Post(client, csrf, $"/api/ui/accounting/clients/{seed.ClientId}/dimensions",
      new { dimensions = new[] {
        new { dimensionType = "BRANCH", code = "BR01", name = "Doha Central Branch" },
        new { dimensionType = "COST_CENTRE", code = "CC10", name = "IT & Systems" }
      }, reviewed = true });
    Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);

    // Query dimensions returns both
    var after = await Read(client, $"/api/ui/accounting/clients/{seed.ClientId}/dimensions");
    Assert.Equal(2, after.GetArrayLength());
    Assert.Equal("BRANCH", after[0].GetProperty("dimensionType").GetString());
    Assert.Equal("BR01", after[0].GetProperty("code").GetString());
    Assert.Equal("Doha Central Branch", after[0].GetProperty("name").GetString());
    Assert.Equal("COST_CENTRE", after[1].GetProperty("dimensionType").GetString());
    Assert.Equal("CC10", after[1].GetProperty("code").GetString());

    // Duplicate code within same type fails
    var duplicate = await Post(client, csrf, $"/api/ui/accounting/clients/{seed.ClientId}/dimensions",
      new { dimensions = new[] { new { dimensionType = "BRANCH", code = "BR01", name = "Another Branch" } }, reviewed = true });
    Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:TenantId"] = u.TenantId,
    ["DevelopmentIdentity:Subject"] = u.Subject,
    ["Application:AllowSimulationAdapters"] = "true",
    ["ExternalEffects:Enabled"] = "false"
  });

  private static async Task<string> SignIn(HttpClient c)
  {
    await c.GetAsync("/auth/sign-in");
    using var r = await c.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<JsonElement> Read(HttpClient c, string url)
  {
    using var r = await c.GetAsync(url);
    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
  }

  private static async Task AssertScopeDenied(HttpResponseMessage response)
  {
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(ErrorCodes.ScopeDenied, body.RootElement.GetProperty("code").GetString());
  }

  private static async Task<(int Charts, int Accounts, int Aliases, int Dimensions, string Status)> ReadMutationCounts(
    OwnedPostgresDatabase pg, Guid firmId, Guid chartId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return (
      await db.ClientChartVersions.CountAsync(x => x.FirmId == firmId),
      await db.ClientAccounts.CountAsync(x => x.FirmId == firmId),
      await db.SourceAccountAliases.CountAsync(x => x.FirmId == firmId),
      await db.ClientAccountingDimensionDefinitions.CountAsync(x => x.FirmId == firmId),
      await db.ClientChartVersions.Where(x => x.Id == chartId && x.FirmId == firmId).Select(x => x.Status).SingleAsync());
  }

  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object body)
  {
    var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    r.Headers.Add("X-XSRF-TOKEN", csrf);
    return c.SendAsync(r);
  }
}
