using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class FirmReceivablesAgingApiTests
{
  [Fact]
  public async Task TermsRequireIndependentReviewAndReportAndExportUseAuthorizedAsOfState()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("FIRM-RECEIVABLES-AGING-API");
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.Actor(seed.Admin, "FinanceManager");
    var reviewer = PbcSeed.Actor(seed.Reviewer, "FinanceReviewer");
    var now = DateTimeOffset.UtcNow;
    var asOf = DateOnly.FromDateTime(now.UtcDateTime).AddDays(1);
    Guid accountId, invoiceId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(seed.FirmId, seed.Admin, "FinanceManager"),
        PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"),
        PbcSeed.Grant(seed.Reviewer.FirmId, seed.Reviewer, "FinanceReviewer"));
      await db.SaveChangesAsync();

      var lead = new Lead
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic API ageing client",
        Source = "Referral", CreatedAt = now
      };
      var opportunity = new Opportunity
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = lead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "FinancialStatementAudit", EntityScope = "TEST", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now
      };
      var proposal = new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, OpportunityId = opportunity.Id, PracticeClientId = seed.ClientId,
        Status = CrmStates.ProposalAccepted, Currency = "QAR", Fee = 200m,
        ServiceProfileId = "AUDIT-2026", Scope = "Synthetic API audit scope", Deliverables = "Synthetic API report",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now, ResponseAt = now
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ProposalId = proposal.Id, Currency = "QAR",
        BaseAmount = 200m, Fee = 200m, InputHash = Hashing.Sha256Hex("synthetic API ageing quotation"),
        Status = QuotationStates.Approved, CreatedByUserId = seed.Admin.Id, ApprovedAt = now, CreatedAt = now
      });
      await db.SaveChangesAsync();

      var agreement = await FeeAgreementService.CreateAgreementAsync(db, PbcSeed.Actor(seed.Admin, "Partner"), proposal.Id);
      Assert.True(agreement.Succeeded, agreement.Message);
      var advanceInvoice = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, manager, agreement.Value);
      Assert.True(advanceInvoice.Succeeded, advanceInvoice.Message);
      invoiceId = advanceInvoice.Value;
      accountId = await db.BillingAccounts.Where(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId)
        .Select(x => x.Id).SingleAsync();
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, invoiceId)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true, ApprovedByUserId = seed.Reviewer.Id,
        ApprovedAt = now, CreatedAt = now
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, invoiceId)).Succeeded);
      var unrelatedInvoice = (await BillingService.CreateInvoiceDraftAsync(db, manager,
        new CreateInvoiceDraftRequest(accountId, "SYN-AGING-API-UNLINKED", [new InvoiceLineRequest("Unlinked non-fee item", 1m, 500m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, manager, unrelatedInvoice)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, reviewer, unrelatedInvoice)).Succeeded);
      Assert.True((await BillingService.PostInvoiceAsync(db, manager, unrelatedInvoice)).Succeeded);
    }

    using var managerFactory = Factory(pg, seed.Admin.Subject);
    using var managerClient = managerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var proof = await SignInAsync(managerClient);
    var reportPath = $"/api/ui/finance/receivables-aging?asOf={asOf:yyyy-MM-dd}";
    var initial = await managerClient.GetFromJsonAsync<JsonElement>(reportPath);
    var initialRow = Assert.Single(initial.GetProperty("rows").EnumerateArray());
    Assert.Equal(invoiceId, initialRow.GetProperty("invoiceId").GetGuid());
    Assert.DoesNotContain(initial.GetProperty("rows").EnumerateArray(), x =>
      x.GetProperty("invoiceNumber").GetString() == "SYN-AGING-API-UNLINKED");
    Assert.Equal("UNDATED_REVIEW_REQUIRED", initialRow.GetProperty("bucket").GetString());
    Assert.Null(initialRow.GetProperty("dueDate").GetStringOrNull());

    var terms = new
    {
      dueDate = asOf.AddDays(-31).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      basis = InvoicePaymentTermsKinds.ContractualDueDate,
      termsDescription = "NET 30 from invoice date",
      evidenceReference = "SYN-ENGAGEMENT-LETTER-R1",
      expectedRevision = 0,
      reviewed = true
    };
    using var submitted = await PostAsync(managerClient, $"/api/ui/finance/invoices/{invoiceId:D}/payment-terms", JsonContent.Create(terms), proof);
    Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
    var revisionId = (await submitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    var pending = await managerClient.GetFromJsonAsync<JsonElement>(reportPath);
    Assert.Equal("UNDATED_REVIEW_REQUIRED", Assert.Single(pending.GetProperty("rows").EnumerateArray()).GetProperty("bucket").GetString());

    using var reviewerFactory = Factory(pg, seed.Reviewer.Subject);
    using var reviewerClient = reviewerFactory.CreateClient(new() { AllowAutoRedirect = false });
    var reviewerProof = await SignInAsync(reviewerClient);
    using var decision = await PostAsync(reviewerClient, $"/api/ui/finance/invoice-payment-terms/{revisionId:D}/review",
      JsonContent.Create(new { approve = true, reason = "Verified against the synthetic engagement letter.", reviewed = true }), reviewerProof);
    Assert.Equal(HttpStatusCode.OK, decision.StatusCode);

    using var recorded = await PostAsync(managerClient, $"/api/ui/finance/billing-accounts/{accountId:D}/receipts",
      JsonContent.Create(new { amount = "20", reference = "SYN-AGING-RECEIPT-API", reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.OK, recorded.StatusCode);
    var receiptId = (await recorded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    using var allocated = await PostAsync(managerClient, $"/api/ui/finance/receipts/{receiptId:D}/allocations",
      JsonContent.Create(new { invoiceId, amount = "20", reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.OK, allocated.StatusCode);
    var invoiceDetail = await managerClient.GetFromJsonAsync<JsonElement>($"/api/ui/finance/invoices/{invoiceId:D}");
    var allocationId = Assert.Single(invoiceDetail.GetProperty("allocations").EnumerateArray()).GetProperty("id").GetGuid();
    using var reversalRequest = await PostAsync(managerClient, $"/api/ui/finance/allocations/{allocationId:D}/reversals",
      JsonContent.Create(new { amount = "5", reference = "SYN-AGING-BANK-CORRECTION", reason = "Correct the applied amount.", expectedRevision = 0, reviewed = true }), proof);
    Assert.Equal(HttpStatusCode.OK, reversalRequest.StatusCode);
    var reversalId = (await reversalRequest.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("value").GetGuid();
    var beforeReversalApproval = await managerClient.GetFromJsonAsync<JsonElement>(reportPath);
    var pendingReversalRow = Assert.Single(beforeReversalApproval.GetProperty("rows").EnumerateArray());
    Assert.Equal(0m, decimal.Parse(pendingReversalRow.GetProperty("reversedReceipts").GetString()!, CultureInfo.InvariantCulture));
    Assert.Equal(80m, decimal.Parse(pendingReversalRow.GetProperty("outstanding").GetString()!, CultureInfo.InvariantCulture));
    using var reversalDecision = await PostAsync(reviewerClient, $"/api/ui/finance/allocation-reversals/{reversalId:D}/review",
      JsonContent.Create(new { approve = true, reason = "Verified the bank correction and allocation.", reviewed = true }), reviewerProof);
    Assert.Equal(HttpStatusCode.OK, reversalDecision.StatusCode);

    var aged = await managerClient.GetFromJsonAsync<JsonElement>(reportPath);
    var row = Assert.Single(aged.GetProperty("rows").EnumerateArray());
    Assert.Equal("OVERDUE_31_60", row.GetProperty("bucket").GetString());
    Assert.Equal("APPROVED", row.GetProperty("paymentTermsStatus").GetString());
    Assert.Equal(20m, decimal.Parse(row.GetProperty("appliedReceipts").GetString()!, CultureInfo.InvariantCulture));
    Assert.Equal(5m, decimal.Parse(row.GetProperty("reversedReceipts").GetString()!, CultureInfo.InvariantCulture));
    Assert.Equal(85m, decimal.Parse(row.GetProperty("outstanding").GetString()!, CultureInfo.InvariantCulture));
    Assert.Equal("QAR", Assert.Single(aged.GetProperty("currencySubtotals").EnumerateArray()).GetProperty("currency").GetString());

    using var noCsrf = await managerClient.PostAsync("/api/ui/finance/receivables-aging/export",
      JsonContent.Create(new { asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }));
    Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
    using var csv = await PostAsync(managerClient, "/api/ui/finance/receivables-aging/export",
      JsonContent.Create(new { asOfDate = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }), proof);
    Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
    Assert.StartsWith("text/csv", csv.Content.Headers.ContentType?.MediaType);
    Assert.Equal(asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), csv.Headers.GetValues("X-Receivables-As-Of").Single());
    var csvText = await csv.Content.ReadAsStringAsync();
    Assert.Contains("OVERDUE_31_60", csvText);
    Assert.Contains("CURRENCY TOTAL", csvText);

    using var scopedFactory = Factory(pg, seed.Staff.Subject);
    using var scopedClient = scopedFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(scopedClient);
    Assert.Equal(HttpStatusCode.Forbidden, (await scopedClient.GetAsync(reportPath)).StatusCode);
  }

  private static StandaloneApiApplicationFactory Factory(ITestPostgresDatabase pg, string subject) =>
    new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = subject,
      ["DevelopmentIdentity:TenantId"] = "tenant-test",
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });

  private static async Task<string> SignInAsync(HttpClient client)
  {
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp")).StatusCode);
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, HttpContent content, string proof)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
    request.Headers.Add("X-XSRF-TOKEN", proof);
    return await client.SendAsync(request);
  }
}

internal static class ReceivablesJsonTestExtensions
{
  internal static string? GetStringOrNull(this JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetString();
}
