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

    using var factory = new ApiWebApplicationFactory(new Dictionary<string, string?>
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
    var receipt = Assert.Single(final.GetProperty("receipts").EnumerateArray());
    Assert.Equal("SYN-BANK-001", receipt.GetProperty("reference").GetString());
    Assert.Equal(25.125m, Decimal(receipt.GetProperty("allocated")));
    Assert.Equal(15m, Decimal(receipt.GetProperty("remaining")));
    Assert.Equal("SYN-CN-001", Assert.Single(final.GetProperty("creditNotes").EnumerateArray()).GetProperty("noteNumber").GetString());

    using var staffFactory = new ApiWebApplicationFactory(new Dictionary<string, string?>
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
