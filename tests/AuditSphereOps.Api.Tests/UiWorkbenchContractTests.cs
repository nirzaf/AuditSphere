using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

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
  public async Task AdministratorOversight_HttpLedgerReadDoesNotAuthorizeForgedFinanceCommands()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ADMIN-OVERSIGHT-API");
    var seed = await PbcSeed.SeedAsync(pg);
    var invoiceId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.FirmPeriods.Add(new FirmPeriod { Id = Guid.NewGuid(), FirmId = seed.FirmId, PeriodCode = "2026-10" });
      var account = new BillingAccount { Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow };
      setup.BillingAccounts.Add(account);
      setup.Invoices.Add(new Invoice { Id = invoiceId, FirmId = seed.FirmId, BillingAccountId = account.Id,
        InvoiceNumber = "PRIVATESEARCHMARKER", Currency = "QAR", Total = 125, Subtotal = 125, CreatedAt = DateTimeOffset.UtcNow });
      await setup.SaveChangesAsync();
    }
    using var factory = Factory(pg.ConnectionString, seed.Admin.Subject, seed.Admin.TenantId);
    using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignInAsync(admin);
    var catalogue = await admin.GetFromJsonAsync<string[]>("/api/ui/administration/role-catalogue");
    Assert.Contains("AccountingPreparer", catalogue!);
    Assert.Contains("AccountingReviewer", catalogue!);
    var ledger = await admin.GetFromJsonAsync<JsonElement>("/api/ui/finance");
    Assert.Equal("READ_ONLY", ledger.GetProperty("accessMode").GetString());
    Assert.Equal("2026-10", ledger.GetProperty("periods")[0].GetProperty("periodCode").GetString());
    foreach (var flag in new[] { "canCreateSetup", "canClosePeriod", "canReviewJournals", "canPostJournals",
      "canCreateJournals", "canReversePostings", "canReopenPeriod" })
      Assert.False(ledger.GetProperty(flag).GetBoolean(), flag);
    using var attempt = await PostAsync(admin, "/api/ui/finance/periods", JsonContent.Create(new { periodCode = "2026-11" }), proof);
    Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Single(await verify.FirmPeriods.Where(x => x.FirmId == seed.FirmId).ToListAsync());
    var first = Assert.Single(await verify.AdministratorOversightAccessEvents.ToListAsync());
    Assert.Equal(seed.Admin.Id, first.ActorUserId);
    Assert.Equal("firm-finance.read", first.Operation);
    Assert.Equal("DISCLOSURE_AUTHORIZED", first.Outcome);
    var history = await admin.GetFromJsonAsync<JsonElement>("/api/ui/administration/oversight-access");
    Assert.Equal(first.Id, history.GetProperty("items")[0].GetProperty("id").GetGuid());
    Assert.Single(await verify.AdministratorOversightAccessEvents.ToListAsync()); // history is ordinary authorized access
    Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/ui/finance")).StatusCode);
    Assert.Equal(2, await verify.AdministratorOversightAccessEvents.CountAsync());
    using var invoiceResponse = await admin.GetAsync($"/api/ui/finance/invoices/{invoiceId}");
    Assert.Equal(HttpStatusCode.OK, invoiceResponse.StatusCode);
    var invoiceEvent = await verify.AdministratorOversightAccessEvents.SingleAsync(x => x.Operation == "firm-invoice.read");
    Assert.Equal(invoiceId, invoiceEvent.ResourceId);
    Assert.Equal("INVOICE", invoiceEvent.ResourceKind);
    Assert.Equal(invoiceEvent.RequestId.ToString("D"), invoiceResponse.Headers.GetValues("X-AuditSphere-Oversight-Request-Id").Single());
    using var search = await admin.GetAsync("/api/ui/search?term=PRIVATESEARCHMARKER");
    Assert.Equal(HttpStatusCode.OK, search.StatusCode);
    Assert.Contains(invoiceId.ToString("D"), await search.Content.ReadAsStringAsync());
    Assert.Single(await verify.AdministratorOversightAccessEvents.Where(x => x.Operation == "search.read").ToListAsync());
    Assert.DoesNotContain("PRIVATESEARCHMARKER", JsonSerializer.Serialize(await verify.AdministratorOversightAccessEvents.ToListAsync()));
    // Owned test database only: simulate a durable audit-store failure without changing read authorization.
    await verify.Database.ExecuteSqlRawAsync("""
      CREATE FUNCTION reject_test_oversight_insert() RETURNS trigger LANGUAGE plpgsql AS $$
      BEGIN RAISE EXCEPTION 'synthetic audit store unavailable' USING ERRCODE = '42501'; END; $$;
      CREATE TRIGGER reject_test_oversight_insert BEFORE INSERT ON administrator_oversight_access_events
      FOR EACH ROW EXECUTE FUNCTION reject_test_oversight_insert();
      """);
    using var withheld = await admin.GetAsync("/api/ui/finance");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, withheld.StatusCode);
    var withheldText = await withheld.Content.ReadAsStringAsync();
    Assert.Contains("oversight.audit-unavailable", withheldText);
    Assert.DoesNotContain("2026-10", withheldText);
    Assert.Equal(4, await verify.AdministratorOversightAccessEvents.CountAsync());
    verify.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Reviewer, "FinanceManager"));
    await verify.SaveChangesAsync();
    using var normalFactory = Factory(pg.ConnectionString, seed.Reviewer.Subject, seed.Reviewer.TenantId);
    using var normal = normalFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(normal);
    var ordinary = await normal.GetFromJsonAsync<JsonElement>("/api/ui/finance");
    Assert.Equal("OPERATIONAL", ordinary.GetProperty("accessMode").GetString());
    Assert.Equal(4, await verify.AdministratorOversightAccessEvents.CountAsync());
    Assert.Equal(HttpStatusCode.Forbidden, (await normal.GetAsync("/api/ui/administration/oversight-access")).StatusCode);
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
    foreach (var range in new[] { "start=0001-01-01&weeks=1", "start=9999-12-31&weeks=1", "start=2026-10-11&weeks=13" })
      Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/ui/practice/resources?" + range)).StatusCode);
    Assert.Equal(1, books.GetProperty("expensePage").GetInt32());
    Assert.False(books.GetProperty("hasMoreExpenses").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/ui/finance/books?expensePage=0")).StatusCode);
    var emptyExpensePage = await admin.GetFromJsonAsync<JsonElement>("/api/ui/finance/books?expensePage=2");
    Assert.Equal(2, emptyExpensePage.GetProperty("expensePage").GetInt32());
    Assert.Empty(emptyExpensePage.GetProperty("expenses").EnumerateArray());
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

  [Fact]
  public async Task FirmTrialBalanceContinuationAndExportRequireTheFirstPageIdentity()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("FIRM-REPORT-IDENTITY-API");
    var seed = await PbcSeed.SeedAsync(pg);
    var periodId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"));
      db.FirmPeriods.Add(new FirmPeriod { Id = periodId, FirmId = seed.FirmId, PeriodCode = "2099-12" });
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg.ConnectionString, seed.Admin.Subject, seed.Admin.TenantId);
    using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignInAsync(admin);
    const string firstPageUrl = "/api/ui/finance/books/trial-balance?from=2099-12&to=2099-12";
    using var firstResponse = await admin.GetAsync(firstPageUrl);
    Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
    var firstPage = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
    var identity = firstPage.GetProperty("reportIdentity").GetString()!;
    Assert.Matches("^[0-9a-f]{64}$", identity);

    using var continuationResponse = await admin.GetAsync(
      firstPageUrl + "&activityPage=2&reportIdentity=" + Uri.EscapeDataString(identity));
    Assert.Equal(HttpStatusCode.OK, continuationResponse.StatusCode);
    var continuation = await continuationResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(identity, continuation.GetProperty("reportIdentity").GetString());

    using var unboundContinuation = await admin.GetAsync(firstPageUrl + "&activityPage=2");
    Assert.Equal(HttpStatusCode.Conflict, unboundContinuation.StatusCode);
    Assert.Equal("generation.stale", (await unboundContinuation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

    using var export = await PostAsync(admin, "/api/ui/finance/books/trial-balance/export",
      JsonContent.Create(new { fromPeriod = "2099-12", toPeriod = "2099-12", reportIdentity = identity }), proof);
    Assert.Equal(HttpStatusCode.OK, export.StatusCode);
    Assert.Equal(identity, Assert.Single(export.Headers.GetValues("X-Firm-Trial-Balance-Identity")));
    Assert.Equal("text/csv", export.Content.Headers.ContentType!.MediaType);

    using var unboundExport = await PostAsync(admin, "/api/ui/finance/books/trial-balance/export",
      JsonContent.Create(new { fromPeriod = "2099-12", toPeriod = "2099-12" }), proof);
    Assert.Equal(HttpStatusCode.Conflict, unboundExport.StatusCode);
    Assert.Equal("generation.stale", (await unboundExport.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    Assert.Null(unboundExport.Content.Headers.ContentDisposition);

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(1, await db.FirmPeriods.Where(x => x.Id == periodId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.PeriodCode, "2099-11")));
    using var staleExport = await PostAsync(admin, "/api/ui/finance/books/trial-balance/export",
      JsonContent.Create(new { fromPeriod = "2099-12", toPeriod = "2099-12", reportIdentity = identity }), proof);
    Assert.Equal(HttpStatusCode.Conflict, staleExport.StatusCode);
    Assert.Equal("generation.stale", (await staleExport.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    Assert.Null(staleExport.Content.Headers.ContentDisposition);
    Assert.Equal("application/json", staleExport.Content.Headers.ContentType!.MediaType);
  }
}
