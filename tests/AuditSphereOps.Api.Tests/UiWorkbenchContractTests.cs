using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

/// <summary>
/// Contract checks for the Angular workbench endpoints: cookie session required, scope enforced by the Application
/// layer, antiforgery on every unsafe request, decimals serialized as exact strings, and evidence bytes never returned.
/// </summary>
public sealed class UiWorkbenchContractTests
{
  private static StandaloneApiApplicationFactory Factory(string connection, string subject, string tenant) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = connection,
    ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = subject, ["DevelopmentIdentity:TenantId"] = tenant,
    ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
  });

  private static async Task<string> SignInAsync(HttpClient client)
  {
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp")).StatusCode);
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, HttpContent content, string? proof)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    if (proof is not null) request.Headers.Add("X-XSRF-TOKEN", proof);
    return await client.SendAsync(request);
  }

  [Fact]
  public async Task WorkbenchEndpoints_RequireSessionScopeAndAntiforgery_AndKeepDecimalsExact()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-API-02");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid expenseAccount, bankAccount;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"));
      await db.SaveChangesAsync();
      var finance = PbcSeed.Actor(seed.Admin, "FinanceManager");
      bankAccount = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("1000", "Bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      expenseAccount = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("6100", "Office rent", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
    }

    using (var anonymousFactory = Factory(pg.ConnectionString, seed.Admin.Subject, seed.Admin.TenantId))
    using (var anonymous = anonymousFactory.CreateClient(new() { AllowAutoRedirect = false }))
      foreach (var url in new[] { "/api/ui/administration/access", "/api/ui/administration/overview", "/api/ui/administration/role-catalogue", "/api/ui/finance/books", "/api/ui/practice/resources", "/api/ui/library", "/api/ui/practice/time",
        "/api/ui/practice/analytics?from=2026-01-01&to=2026-12-31", $"/api/ui/engagements/{seed.EngagementId}/tb-intake" })
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(url)).StatusCode);

    using (var staffFactory = Factory(pg.ConnectionString, seed.Staff.Subject, seed.Staff.TenantId))
    using (var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      await SignInAsync(staff);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/ui/administration/access")).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/ui/administration/overview")).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/ui/finance/books")).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/ui/practice/resources")).StatusCode);
      Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/ui/engagements/{seed.EngagementId}/tb-intake")).StatusCode);
      Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync($"/api/ui/engagements/{Guid.NewGuid()}/tb-intake")).StatusCode);
      var time = await staff.GetFromJsonAsync<JsonElement>("/api/ui/practice/time");
      Assert.False(time.GetProperty("isApprover").GetBoolean());
    }

    using var adminFactory = Factory(pg.ConnectionString, seed.Admin.Subject, seed.Admin.TenantId);
    using var admin = adminFactory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignInAsync(admin);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/administration/access")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/administration/overview")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/administration/role-catalogue")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(admin, "/api/ui/administration/access/preview", JsonContent.Create(new { userId = seed.Staff.Id, role = "Manager", scopeKind = "CLIENT", clientId = seed.ClientId, reason = "Review assigned team" }), null)).StatusCode);
    using var accessPreview = await PostAsync(admin, "/api/ui/administration/access/preview", JsonContent.Create(new { userId = seed.Staff.Id, role = "Manager", scopeKind = "CLIENT", clientId = seed.ClientId, reason = "Review assigned team" }), proof);
    Assert.Equal(HttpStatusCode.OK, accessPreview.StatusCode);
    var reviewed = await accessPreview.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(64, reviewed.GetProperty("value").GetProperty("digest").GetString()!.Length);

    var expenseBytes = "%PDF receipt"u8.ToArray();
    var expenseRequestId = Guid.CreateVersion7();
    const decimal expenseAmount = 12345678.12m;
    var expenseRequest = new RecordFirmExpenseRequest(new DateOnly(2026, 2, 3), "RENT", "Landlord", "February rent", expenseAmount, "QAR",
      expenseAccount, bankAccount, "receipt.pdf", "application/octet-stream", expenseBytes, expenseRequestId);
    var expenseRequestHash = FirmExpenseService.CreateRequestHash(PbcSeed.Actor(seed.Admin, "FinanceManager"), expenseRequest);

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/ui/practice/analytics?from=bad&to=2026-12-31")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/practice/analytics?from=2026-01-01&to=2026-12-31")).StatusCode);
    var resources = await admin.GetFromJsonAsync<JsonElement>("/api/ui/practice/resources?weeks=2");
    Assert.Equal(2, resources.GetProperty("grid").GetProperty("weeks").GetInt32());
    Assert.All(resources.GetProperty("grid").GetProperty("rows").EnumerateArray(),
      r => Assert.Equal(JsonValueKind.String, r.GetProperty("targetUtilizationPercent").ValueKind));

    var library = JsonContent.Create(new { code = "ISA-230", title = "Audit documentation", category = "ISA", audience = "ALL_STAFF", body = "Body text", sourceReference = "IAASB" });
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(admin, "/api/ui/library", library, null)).StatusCode);
    using var created = await PostAsync(admin, "/api/ui/library", JsonContent.Create(new { code = "ISA-230", title = "Audit documentation", category = "ISA",
      audience = "ALL_STAFF", body = "Body text", sourceReference = "IAASB" }), proof);
    Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    var documentId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    var entry = await admin.GetFromJsonAsync<JsonElement>($"/api/ui/library/{documentId}");
    Assert.Equal("Body text", entry.GetProperty("version").GetProperty("body").GetString());
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/ui/library/search?term=" + new string('a', 101))).StatusCode);

    var books = await admin.GetFromJsonAsync<JsonElement>("/api/ui/finance/books");
    Assert.True(books.GetProperty("canPrepare").GetBoolean());
    MultipartFormDataContent Expense()
    {
      var form = new MultipartFormDataContent
      {
        { new StringContent("2026-02-03"), "expenseDate" }, { new StringContent("RENT"), "category" }, { new StringContent("Landlord"), "payee" },
        { new StringContent("February rent"), "description" }, { new StringContent(expenseAmount.ToString(System.Globalization.CultureInfo.InvariantCulture)), "amount" }, { new StringContent("QAR"), "currency" },
        { new StringContent(expenseAccount.ToString()), "expenseAccountId" }, { new StringContent(bankAccount.ToString()), "paymentAccountId" },
        { new StringContent(expenseRequestId.ToString("D")), "requestId" }, { new StringContent(expenseRequestHash), "requestHash" },
      };
      form.Add(new ByteArrayContent(expenseBytes), "evidence", "receipt.pdf");
      return form;
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(admin, "/api/ui/finance/books/expenses", Expense(), null)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await PostAsync(admin, "/api/ui/finance/books/expenses", Expense(), proof)).StatusCode);
    var listed = (await admin.GetFromJsonAsync<JsonElement>("/api/ui/finance/books")).GetProperty("expenses")[0];
    Assert.Equal("12345678.120000", listed.GetProperty("amount").GetString());
    Assert.False(listed.TryGetProperty("evidenceContent", out _));
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/ui/finance/books/trial-balance?from=2026-13&to=2026-01")).StatusCode);
  }
}
