using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class UiContractTests
{
  [Fact]
  public async Task CookieSession_PortfolioAndRevokedIdentityFailClosed()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-API-01");
    var seed = await PbcSeed.SeedAsync(pg);
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = seed.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = seed.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    });
    var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
      .Get(CookieAuthenticationDefaults.AuthenticationScheme);
    var http = new DefaultHttpContext();
    http.Request.Path = "/api/ui/session";
    var ticket = new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity("fixture")),
      new AuthenticationProperties(), CookieAuthenticationDefaults.AuthenticationScheme);
    var sliding = new CookieSlidingExpirationContext(http,
      new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler)),
      options, ticket, TimeSpan.FromHours(12), TimeSpan.FromHours(1)) { ShouldRenew = true };
    await options.Events.OnCheckSlidingExpiration(sliding);
    Assert.False(sliding.ShouldRenew);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/session")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/portfolio")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/accounting/clients")).StatusCode);
    using var login = await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp");
    Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    Assert.Contains(session.Headers.GetValues("Set-Cookie"), h => h.StartsWith("XSRF-TOKEN="));
    Assert.True(session.Headers.CacheControl!.NoStore);
    var page = await client.GetFromJsonAsync<PortfolioPage>("/api/ui/portfolio");
    Assert.Equal(seed.ClientId, Assert.Single(page!.Items).Id);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/accounting/clients")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/accounting/clients/" + seed.ClientId)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/ui/accounting/clients/" + seed.ClientId + "/profile",
      new { profileId = (Guid?)null, revision = "0", jurisdiction = "QA", currency = "QAR", fiscalMonth = 1,
        fiscalDay = 1, sourceSystem = "TEST", sourceIdentifier = "TEST", reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/search?term=pbc")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/ui/search?term=" + new string('a', 101))).StatusCode);
    var engagement = await client.GetFromJsonAsync<EngagementWorkspace>("/api/ui/engagements/" + seed.EngagementId);
    Assert.Equal(seed.EngagementId, engagement!.Id);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/clients/" + seed.ClientId)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/engagements/" + Guid.NewGuid())).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/ui/portfolio?pageSize=101")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/clients/" + seed.ClientId + "/acceptance")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/leads")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/proposals/" + Guid.NewGuid())).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/leads/" + Guid.NewGuid())).StatusCode);
    using var opportunityWithoutProof = await client.PostAsJsonAsync("/api/ui/leads/" + Guid.NewGuid() + "/opportunities",
      new { requestId = Guid.NewGuid(), serviceRoute = "FinancialStatementAudit", entityScope = "Entity", periodStart = "2026-01-01", periodEnd = "2026-12-31", expectedFee = "123.45", currency = "QAR" });
    Assert.Equal(HttpStatusCode.Forbidden, opportunityWithoutProof.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/proposals/" + Guid.NewGuid() + "/quotation")).StatusCode);
    using var quotationWithoutProof = await client.PostAsJsonAsync("/api/ui/proposals/" + Guid.NewGuid() + "/quotation/preview",
      new { proposalRevision = "1", revision = "0", complexity = "1", risk = "0", discount = "0", nonStandardTerms = false, note = (string?)null,
        lines = new[] { new { role = "Partner", activity = "Audit", hours = "10", rateCardId = Guid.NewGuid() } } });
    Assert.Equal(HttpStatusCode.Forbidden, quotationWithoutProof.StatusCode);
    using var quotationApprovalWithoutProof = await client.PostAsJsonAsync("/api/ui/quotations/" + Guid.NewGuid() + "/approve", new { ruleKey = "DEFAULT:DISCOUNT", reason = "Reviewed discount" });
    Assert.Equal(HttpStatusCode.Forbidden, quotationApprovalWithoutProof.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/proposals/" + Guid.NewGuid() + "/documents")).StatusCode);
    using var documentWithoutProof = await client.PostAsJsonAsync("/api/ui/proposals/" + Guid.NewGuid() + "/documents/letter",
      new { quotationId = Guid.NewGuid(), profileVersion = "1", reviewed = true, teamCvs = (string?)null, timeline = (string?)null });
    Assert.Equal(HttpStatusCode.Forbidden, documentWithoutProof.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/proposals/" + Guid.NewGuid() + "/fee-agreement")).StatusCode);
    using var feeWithoutProof = await client.PostAsJsonAsync("/api/ui/proposals/" + Guid.NewGuid() + "/fee-agreement", new { reviewed = true });
    Assert.Equal(HttpStatusCode.Forbidden, feeWithoutProof.StatusCode);
    using var paymentWithoutProof = await client.PostAsJsonAsync("/api/ui/fee-agreements/" + Guid.NewGuid() + "/advance-payment", new { amount = "12500", reference = "Reviewed-reference", reviewed = true });
    Assert.Equal(HttpStatusCode.Forbidden, paymentWithoutProof.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/commercial-settings")).StatusCode);
    using var settingsWithoutProof = await client.PostAsJsonAsync("/api/ui/commercial-settings/rules",
      new { rulesRevision = new string('a', 64), kind = "DISCOUNT_OVER_PERCENT", threshold = "10", role = "Partner", reviewed = true });
    Assert.Equal(HttpStatusCode.Forbidden, settingsWithoutProof.StatusCode);
    using var proposalWithoutProof = await client.PostAsJsonAsync("/api/ui/proposals/" + Guid.NewGuid() + "/review", new { });
    Assert.Equal(HttpStatusCode.Forbidden, proposalWithoutProof.StatusCode);
    using var revisionWithoutProof = await client.PostAsJsonAsync("/api/ui/opportunities/" + Guid.NewGuid() + "/proposals",
      new { expectedRevision = "1", serviceProfile = "AUDIT", scope = "Scope", exclusions = "", deliverables = "Report", dependencies = "", fee = "123.45", currency = "QAR", periodStart = "2026-01-01", periodEnd = "2026-12-31" });
    Assert.Equal(HttpStatusCode.Forbidden, revisionWithoutProof.StatusCode);
    using var leadWithoutProof = await client.PostAsJsonAsync("/api/ui/leads", new { name = "Lead", source = "Referral", contactName = (string?)null, contactEmail = (string?)null });
    Assert.Equal(HttpStatusCode.Forbidden, leadWithoutProof.StatusCode);
    using var decisionWithoutProof = await client.PostAsJsonAsync("/api/ui/clients/" + seed.ClientId + "/acceptance/decision",
      new { serviceRoute = "Audit", decision = "Accepted", rationale = "Reviewed", conditions = (string?)null, generation = "1" });
    Assert.Equal(HttpStatusCode.Forbidden, decisionWithoutProof.StatusCode);
    using var answerWithoutProof = await client.PostAsJsonAsync("/api/ui/clients/" + seed.ClientId + "/acceptance/answers/CE-001",
      new { answer = "Yes", evidence = "DOC-1", generation = "1", revision = "0" });
    Assert.Equal(HttpStatusCode.Forbidden, answerWithoutProof.StatusCode);
    using var activationWithoutProof = await client.PostAsJsonAsync("/api/ui/engagements/" + seed.EngagementId + "/activate", new { });
    Assert.Equal(HttpStatusCode.Forbidden, activationWithoutProof.StatusCode);
    using var budgetWithoutProof = await client.PostAsJsonAsync("/api/ui/engagements/" + seed.EngagementId + "/budgets",
      new { currency = "QAR", expectedVersion = "0", lines = new[] { new { role = "Senior", activity = "AUDIT", forecastMinutes = 60 } } });
    Assert.Equal(HttpStatusCode.Forbidden, budgetWithoutProof.StatusCode);
    using var staffingWithoutProof = await client.PostAsJsonAsync("/api/ui/engagements/" + seed.EngagementId + "/staffing",
      new { userId = seed.Reviewer.Id, level = "STAFF_ASSOCIATE", reviewedClientSiteAccess = true });
    Assert.Equal(HttpStatusCode.Forbidden, staffingWithoutProof.StatusCode);
    using var contactWithoutProof = await client.PostAsJsonAsync("/api/ui/clients/" + seed.ClientId + "/contacts",
      new { name = "Contact", email = "contact@example.test", role = "CFO", primary = false, safetyGeneration = "1" });
    Assert.Equal(HttpStatusCode.Forbidden, contactWithoutProof.StatusCode);
    using var absentProof = await client.PostAsync("/api/ui/sign-out", null);
    Assert.Equal(HttpStatusCode.Forbidden, absentProof.StatusCode);
    using var forgedProof = new HttpRequestMessage(HttpMethod.Post, "/api/ui/sign-out");
    forgedProof.Headers.Add("X-XSRF-TOKEN", "forged");
    Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forgedProof)).StatusCode);
    var proof = session.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN="))
      .Split(';')[0]["XSRF-TOKEN=".Length..];
    using var validProof = new HttpRequestMessage(HttpMethod.Post, "/api/ui/sign-out");
    validProof.Headers.Add("X-XSRF-TOKEN", Uri.UnescapeDataString(proof));
    Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(validProof)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/session")).StatusCode);
    using var renewed = await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fapp");
    Assert.Equal(HttpStatusCode.Redirect, renewed.StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options);
    var user = await db.Users.SingleAsync(x => x.Id == seed.Staff.Id);
    user.SessionEpoch++;
    await db.SaveChangesAsync();
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/session")).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/portfolio")).StatusCode);
  }
}
