using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;

namespace AuditSphereOps.Api.Tests;

public sealed class BillingWorkspaceApiTests
{
  [Fact]
  public async Task InvoiceWorkspaceRecordsAllocatesAndCreditsWithCurrentAuthorizationAndAntiforgery()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-BILLING-WORKSPACE");
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(seed.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(seed.Reviewer, "FinanceReviewer");
    Guid accountId, invoiceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"),
        PbcSeed.Grant(seed.FirmId, seed.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();
      accountId = (await BillingService.CreateBillingAccountAsync(db, manager,
        new CreateBillingAccountRequest(seed.ClientId, "QAR"))).Value;
      invoiceId = (await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-ANG-INV-001", [new InvoiceLineRequest("Synthetic assurance fee", 1m, 100m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, invoiceId)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile,
        Approved = true, ApprovedByUserId = seed.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignInAsync(client);
    var path = $"/api/ui/finance/invoices/{invoiceId:D}";

    var initial = await client.GetFromJsonAsync<JsonElement>(path);
    Assert.Equal(accountId, initial.GetProperty("billingAccountId").GetGuid());
    Assert.True(initial.GetProperty("canIssueCreditNote").GetBoolean());
    Assert.Empty(initial.GetProperty("receipts").EnumerateArray());
    Assert.Empty(initial.GetProperty("creditNotes").EnumerateArray());

    var receiptBody = new { amount = "40.125", reference = "SYN-BANK-001", reviewed = true };
    Assert.Equal(HttpStatusCode.Forbidden,
      (await PostAsync(client, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts", JsonContent.Create(receiptBody), null)).StatusCode);
    using var recorded = await PostAsync(client, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts", JsonContent.Create(receiptBody), proof);
    Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
    var receiptId = (await recorded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();

    var allocationBody = new { invoiceId, amount = "25.125", reviewed = true };
    Assert.Equal(HttpStatusCode.Forbidden,
      (await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations", JsonContent.Create(allocationBody), null)).StatusCode);
    using var allocated = await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations", JsonContent.Create(allocationBody), proof);
    Assert.Equal(HttpStatusCode.OK, allocated.StatusCode);

    var creditBody = new { noteNumber = "SYN-CN-001", amount = "10.5", reason = "Synthetic reviewed fee adjustment", reviewed = true };
    using var credited = await PostAsync(client, $"/api/ui/finance/invoices/{invoiceId:D}/credit-notes", JsonContent.Create(creditBody), proof);
    Assert.Equal(HttpStatusCode.OK, credited.StatusCode);
    var final = await client.GetFromJsonAsync<JsonElement>(path);
    Assert.Equal(64.375m, Decimal(final.GetProperty("outstanding")));
    Assert.Equal(10.5m, Decimal(final.GetProperty("credited")));
    Assert.Equal(25.125m, Decimal(final.GetProperty("allocated")));
    var receipt = Assert.Single(final.GetProperty("receipts").EnumerateArray());
    Assert.Equal("SYN-BANK-001", receipt.GetProperty("reference").GetString());
    Assert.Equal(25.125m, Decimal(receipt.GetProperty("allocated")));
    Assert.Equal(15m, Decimal(receipt.GetProperty("remaining")));
    Assert.Equal("SYN-CN-001", Assert.Single(final.GetProperty("creditNotes").EnumerateArray()).GetProperty("noteNumber").GetString());

    using var staffFactory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(staff);
    Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync(path)).StatusCode);
  }

  [Fact]
  public async Task InvoiceHistoryUsesStableCursorsAcrossEqualTimestamps()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-BILLING-HISTORY-PAGES");
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(seed.Admin, "FinanceManager");
    Guid accountId, invoiceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"));
      await db.SaveChangesAsync();
      accountId = (await BillingService.CreateBillingAccountAsync(db, manager,
        new CreateBillingAccountRequest(seed.ClientId, "QAR"))).Value;
      invoiceId = (await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-ANG-HISTORY-INV-001", [new InvoiceLineRequest("Synthetic fee", 1m, 100m)]))).Value;
      var sameInstant = DateTimeOffset.UtcNow.AddDays(-2);
      db.Receipts.AddRange(Enumerable.Range(1, 101).Select(i => new Receipt
      {
        Id = HistoryId(i), FirmId = seed.FirmId, BillingAccountId = accountId,
        Amount = 1m, Currency = "QAR", Reference = $"SYN-PAGE-R-{i:D3}",
        RecordedByUserId = seed.Admin.Id, ReceivedAt = sameInstant
      }));
      db.CreditNotes.AddRange(Enumerable.Range(1, 101).Select(i => new CreditNote
      {
        Id = HistoryId(i), FirmId = seed.FirmId, BillingAccountId = accountId, InvoiceId = invoiceId,
        NoteNumber = $"SYN-PAGE-C-{i:D3}", Currency = "QAR", Amount = 0.01m,
        Reason = "Synthetic history page", CreatedByUserId = seed.Admin.Id, CreatedAt = sameInstant
      }));
      await db.SaveChangesAsync();
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(client);
    var path = $"/api/ui/finance/invoices/{invoiceId:D}";
    var first = await client.GetFromJsonAsync<JsonElement>(path);
    var receipts = first.GetProperty("receipts").EnumerateArray().ToArray();
    var credits = first.GetProperty("creditNotes").EnumerateArray().ToArray();
    Assert.Equal(100, receipts.Length);
    Assert.Equal(100, credits.Length);
    Assert.True(first.GetProperty("receiptsHaveMore").GetBoolean());
    Assert.True(first.GetProperty("creditNotesHaveMore").GetBoolean());

    var lastReceipt = receipts[^1];
    var lastCredit = credits[^1];
    var receiptAt = Uri.EscapeDataString(lastReceipt.GetProperty("receivedAt").GetString()!);
    var creditAt = Uri.EscapeDataString(lastCredit.GetProperty("createdAt").GetString()!);
    var nextUrl = $"{path}?receiptBefore={receiptAt}&receiptBeforeId={lastReceipt.GetProperty("id").GetGuid():D}" +
      $"&creditBefore={creditAt}&creditBeforeId={lastCredit.GetProperty("id").GetGuid():D}";
    var older = await client.GetFromJsonAsync<JsonElement>(nextUrl);
    var olderReceipt = Assert.Single(older.GetProperty("receipts").EnumerateArray());
    var olderCredit = Assert.Single(older.GetProperty("creditNotes").EnumerateArray());
    Assert.Equal("SYN-PAGE-R-001", olderReceipt.GetProperty("reference").GetString());
    Assert.Equal("SYN-PAGE-C-001", olderCredit.GetProperty("noteNumber").GetString());
    Assert.False(older.GetProperty("receiptsHaveMore").GetBoolean());
    Assert.False(older.GetProperty("creditNotesHaveMore").GetBoolean());
    Assert.DoesNotContain(olderReceipt.GetProperty("id").GetGuid(), receipts.Select(x => x.GetProperty("id").GetGuid()));
  }

  private static Guid HistoryId(int number) => Guid.Parse($"00000000-0000-7000-8000-{number:000000000000}");

  private static decimal Decimal(JsonElement value) => decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture);

  private static async Task<string> SignInAsync(HttpClient client)
  {
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp")).StatusCode);
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, HttpContent content, string? proof)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    if (proof is not null) request.Headers.Add("X-XSRF-TOKEN", proof);
    return await client.SendAsync(request);
  }
}
