using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    var receiptRequestId = Guid.NewGuid();
    var receiptBody = new { requestId = receiptRequestId, amount = "40.125", reference = "SYN-BANK-001", reviewed = true };
    Assert.Equal(HttpStatusCode.Forbidden,
      (await PostAsync(client, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts", JsonContent.Create(receiptBody), null)).StatusCode);
    using var recorded = await PostAsync(client, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts", JsonContent.Create(receiptBody), proof);
    Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
    var receiptId = (await recorded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    using var receiptReplay = await PostAsync(client, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts", JsonContent.Create(receiptBody), proof);
    Assert.Equal(HttpStatusCode.OK, receiptReplay.StatusCode);
    Assert.Equal(receiptId, (await receiptReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid());
    var retainedReceipt = await client.GetFromJsonAsync<JsonElement>($"/api/ui/finance/commands/receipts/{receiptRequestId:D}");
    Assert.Equal(receiptId, retainedReceipt.GetProperty("outcome").GetProperty("resultId").GetGuid());
    await using (var db = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE billing_command_receipts"));
    using var receiptConflict = await PostAsync(client, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts",
      JsonContent.Create(new { requestId = receiptRequestId, amount = "40.13", reference = "SYN-BANK-001", reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.Conflict, receiptConflict.StatusCode);

    var allocationBody = new { requestId = Guid.NewGuid(), invoiceId, amount = "25.125", reviewed = true };
    Assert.Equal(HttpStatusCode.Forbidden,
      (await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations", JsonContent.Create(allocationBody), null)).StatusCode);
    using var missingRequestId = await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations",
      JsonContent.Create(new { requestId = Guid.Empty, invoiceId, amount = "1", reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.BadRequest, missingRequestId.StatusCode);
    Assert.Equal("billing.command-invalid", (await missingRequestId.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    using var allocated = await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations", JsonContent.Create(allocationBody), proof);
    Assert.Equal(HttpStatusCode.OK, allocated.StatusCode);
    var allocationId = (await allocated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("allocationId").GetGuid();
    using var allocationReplay = await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations", JsonContent.Create(allocationBody), proof);
    Assert.Equal(HttpStatusCode.OK, allocationReplay.StatusCode);
    Assert.Equal(allocationId, (await allocationReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetProperty("allocationId").GetGuid());
    using var allocationConflict = await PostAsync(client, $"/api/ui/finance/receipts/{receiptId:D}/allocations",
      JsonContent.Create(new { requestId = allocationBody.requestId, invoiceId, amount = "24.125", reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.Conflict, allocationConflict.StatusCode);

    var creditBody = new { requestId = Guid.NewGuid(), noteNumber = "SYN-CN-001", amount = "10.5", reason = "Synthetic reviewed fee adjustment", reviewed = true };
    using var credited = await PostAsync(client, $"/api/ui/finance/invoices/{invoiceId:D}/credit-notes", JsonContent.Create(creditBody), proof);
    Assert.Equal(HttpStatusCode.OK, credited.StatusCode);
    var creditId = (await credited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    using var creditReplay = await PostAsync(client, $"/api/ui/finance/invoices/{invoiceId:D}/credit-notes", JsonContent.Create(creditBody), proof);
    Assert.Equal(HttpStatusCode.OK, creditReplay.StatusCode);
    Assert.Equal(creditId, (await creditReplay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid());
    var final = await client.GetFromJsonAsync<JsonElement>(path);
    Assert.Equal(64.375m, Decimal(final.GetProperty("outstanding")));
    Assert.Equal(10.5m, Decimal(final.GetProperty("credited")));
    Assert.Equal(25.125m, Decimal(final.GetProperty("allocated")));
    var receipt = Assert.Single(final.GetProperty("receipts").EnumerateArray());
    Assert.Equal("SYN-BANK-001", receipt.GetProperty("reference").GetString());
    Assert.Equal(25.125m, Decimal(receipt.GetProperty("allocated")));
    Assert.Equal(15m, Decimal(receipt.GetProperty("remaining")));
    Assert.Equal("SYN-CN-001", Assert.Single(final.GetProperty("creditNotes").EnumerateArray()).GetProperty("noteNumber").GetString());
    Assert.Single(final.GetProperty("allocations").EnumerateArray());

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
    Assert.Equal(HttpStatusCode.Forbidden,
      (await staff.GetAsync($"/api/ui/finance/commands/receipts/{receiptRequestId:D}")).StatusCode);
  }

  [Fact]
  public async Task FeeInvoiceScopeDenialsAreForbiddenWithoutEffectsAndOtherOutcomesKeepTheirStatus()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-FEE-INVOICE-SCOPE");
    var seed = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var managerUser = PbcSeed.User(seed.FirmId, "Staff");
    var siblingClientId = Guid.NewGuid();
    Guid ownAgreementId, siblingAgreementId, foreignAgreementId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(managerUser);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, managerUser, "FinanceManager", clientId: seed.ClientId));
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClientId, FirmId = seed.FirmId, LegalName = "SYNTHETIC SIBLING FEE CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = seed.FirmId });
      ownAgreementId = SeedFeeAgreement(db, seed.FirmId, seed.ClientId, seed.Admin.Id);
      siblingAgreementId = SeedFeeAgreement(db, seed.FirmId, siblingClientId, seed.Admin.Id);
      foreignAgreementId = SeedFeeAgreement(db, foreign.FirmId, foreign.ClientId, foreign.Admin.Id);
      await db.SaveChangesAsync();
    }

    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = managerUser.Subject,
      ["DevelopmentIdentity:TenantId"] = managerUser.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignInAsync(client);
    const string body = "{\"reviewed\":true}";

    using (var noCsrf = await PostAsync(client, $"/api/ui/fee-agreements/{ownAgreementId:D}/advance-invoice",
      new StringContent(body, System.Text.Encoding.UTF8, "application/json"), null))
    {
      Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
      Assert.Equal("csrf.invalid", (await noCsrf.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    foreach (var deniedAgreementId in new[] { siblingAgreementId, foreignAgreementId })
    {
      using var denied = await PostAsync(client, $"/api/ui/fee-agreements/{deniedAgreementId:D}/advance-invoice",
        new StringContent(body, System.Text.Encoding.UTF8, "application/json"), csrf);
      Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
      Assert.Equal("scope.denied", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    await using (var proof = new AuditSphereDbContext(pg.Options))
    {
      Assert.Empty(await proof.BillingAccounts.ToListAsync());
      Assert.Empty(await proof.Invoices.ToListAsync());
      Assert.All(await proof.FeeMilestones.AsNoTracking().ToListAsync(), milestone =>
      {
        Assert.Equal(FeeMilestoneStates.Planned, milestone.State);
        Assert.Null(milestone.InvoiceId);
      });
    }

    using var allowed = await PostAsync(client, $"/api/ui/fee-agreements/{ownAgreementId:D}/advance-invoice",
      new StringContent(body, System.Text.Encoding.UTF8, "application/json"), csrf);
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    var issuedInvoiceId = (await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    using var blockedBalance = await PostAsync(client, $"/api/ui/fee-agreements/{ownAgreementId:D}/balance-invoice",
      new StringContent(body, System.Text.Encoding.UTF8, "application/json"), csrf);
    Assert.Equal(HttpStatusCode.BadRequest, blockedBalance.StatusCode);
    Assert.Equal("gate.blocked", (await blockedBalance.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

    await using var finalProof = new AuditSphereDbContext(pg.Options);
    var advance = await finalProof.FeeMilestones.SingleAsync(x => x.AgreementId == ownAgreementId && x.Kind == FeeMilestoneKinds.Advance);
    var balance = await finalProof.FeeMilestones.SingleAsync(x => x.AgreementId == ownAgreementId && x.Kind == FeeMilestoneKinds.Balance);
    Assert.Equal(FeeMilestoneStates.Invoiced, advance.State);
    Assert.Equal(issuedInvoiceId, advance.InvoiceId);
    Assert.Equal(FeeMilestoneStates.Planned, balance.State);
    Assert.Null(balance.InvoiceId);
    Assert.Single(await finalProof.BillingAccounts.Where(x => x.FirmId == seed.FirmId).ToListAsync());
    Assert.Single(await finalProof.Invoices.Where(x => x.FirmId == seed.FirmId).ToListAsync());
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

  private static Guid SeedFeeAgreement(AuditSphereDbContext db, Guid firmId, Guid clientId, Guid createdByUserId)
  {
    var now = DateTimeOffset.UtcNow;
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    var quotationId = Guid.NewGuid();
    var agreementId = Guid.NewGuid();
    db.Leads.Add(new Lead { Id = leadId, FirmId = firmId, Name = "SYNTHETIC FEE PROPOSAL", Source = "TEST", CreatedAt = now });
    db.Opportunities.Add(new Opportunity
    {
      Id = opportunityId, FirmId = firmId, LeadId = leadId, PracticeClientId = clientId,
      ServiceRoute = "FinancialStatementAudit", EntityScope = "Synthetic scope", PeriodStart = "2026-01-01",
      PeriodEnd = "2026-12-31", ExpectedFee = 100m, Currency = "QAR", CreatedAt = now
    });
    db.Proposals.Add(new Proposal
    {
      Id = proposalId, FirmId = firmId, OpportunityId = opportunityId, PracticeClientId = clientId,
      ServiceProfileId = "synthetic", Scope = "Synthetic scope", Deliverables = "Synthetic invoice fixture",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Fee = 100m, Currency = "QAR", CreatedAt = now
    });
    db.QuotationVersions.Add(new QuotationVersion
    {
      Id = quotationId, FirmId = firmId, ProposalId = proposalId, Currency = "QAR", BaseAmount = 100m,
      Fee = 100m, InputHash = new string('a', 64), CreatedByUserId = createdByUserId, CreatedAt = now
    });
    db.EngagementFeeAgreements.Add(new EngagementFeeAgreement
    {
      Id = agreementId, FirmId = firmId, PracticeClientId = clientId, ProposalId = proposalId,
      QuotationVersionId = quotationId, Currency = "QAR", AgreedFee = 100m, AdvancePercent = 50m,
      CreatedByUserId = createdByUserId, CreatedAt = now
    });
    db.FeeMilestones.AddRange(
      new FeeMilestone { Id = Guid.NewGuid(), FirmId = firmId, AgreementId = agreementId, Kind = FeeMilestoneKinds.Advance, Amount = 50m, CreatedAt = now },
      new FeeMilestone { Id = Guid.NewGuid(), FirmId = firmId, AgreementId = agreementId, Kind = FeeMilestoneKinds.Balance, Amount = 50m, CreatedAt = now });
    return agreementId;
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
