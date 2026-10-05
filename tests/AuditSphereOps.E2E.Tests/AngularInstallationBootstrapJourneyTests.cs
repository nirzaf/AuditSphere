using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularInstallationBootstrapJourneyTests
{
  [Theory]
  [InlineData("tenant")]
  [InlineData("object")]
  [Trait("CaseId", "ANGULAR-INSTALLATION-BOOTSTRAP-IDENTITY-DENIAL")]
  public async Task UnapprovedDevelopmentIdentity_CannotStartInitialAdministratorSetup(string mismatch)
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-INSTALLATION-BOOTSTRAP-IDENTITY-DENIAL");
    var identity = PbcSeed.User(host.Fixture.FirmId, "Staff");
    identity.TenantId = "11111111-1111-4111-8111-111111111111";
    identity.Subject = "22222222-2222-4222-8222-222222222222";
    var approvedTenantId = mismatch == "tenant" ? "33333333-3333-4333-8333-333333333333" : identity.TenantId;
    var approvedObjectId = mismatch == "object" ? "44444444-4444-4444-8444-444444444444" : identity.Subject;
    var proof = OwnedHost.BootstrapProof;
    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["Setup__InstallationId"] = "angular-installation-bootstrap-denial",
      ["Setup__BootstrapProofHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant(),
      ["Setup__InitialAdministratorTenantId"] = approvedTenantId,
      ["Setup__InitialAdministratorObjectId"] = approvedObjectId
    };
    var origin = await host.StartApiForIdentityAsync(identity, settings);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var response = await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fsetup%2Fmicrosoft365");

    Assert.Equal(503, response?.Status);
    Assert.Equal(401, await page.EvaluateAsync<int>("async () => (await fetch('/api/setup/session')).status"));
    Assert.Equal(401, await page.EvaluateAsync<int>("async () => (await fetch('/api/ui/session')).status"));
  }

  [Fact]
  [Trait("CaseId", "ANGULAR-INSTALLATION-BOOTSTRAP")]
  public async Task ApprovedIdentity_BindsReviewedInitialAdministrator_AndClosesBootstrapAfterFreshSignIn()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-INSTALLATION-BOOTSTRAP");
    await using (var db = host.CreateDbContext())
      await db.RoleGrants.Where(x => x.Role == "Administrator" && x.FirmId == host.Fixture.FirmId).ExecuteDeleteAsync();

    var identity = PbcSeed.User(host.Fixture.FirmId, "Staff");
    identity.TenantId = "11111111-1111-4111-8111-111111111111";
    identity.Subject = "22222222-2222-4222-8222-222222222222";
    var proof = OwnedHost.BootstrapProof;
    var settings = new Dictionary<string, string>
    {
      ["AngularUi__Enabled"] = "true",
      ["Setup__InstallationId"] = "angular-installation-bootstrap",
      ["Setup__BootstrapProofHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant(),
      ["Setup__InitialAdministratorTenantId"] = identity.TenantId,
      ["Setup__InitialAdministratorObjectId"] = identity.Subject
    };
    var origin = await host.StartApiForIdentityAsync(identity, settings);

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, error) => errors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=%2Fsetup%2Fmicrosoft365");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Initial administrator setup", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(401, await page.EvaluateAsync<int>("async () => (await fetch('/api/ui/session')).status"));
    Assert.Equal(401, await page.EvaluateAsync<int>("async () => (await fetch('/api/ui/portfolio')).status"));
    var proofField = page.GetByLabel("Installation proof", new() { Exact = true });
    Assert.Equal("password", await proofField.GetAttributeAsync("type"));
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Bind initial administrator", Exact = true })).ToBeDisabledAsync();
    await proofField.FillAsync(proof);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Bind initial administrator", Exact = true })).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Checkbox, new() { Name = "I reviewed this initial firm-wide AuditSphere administrator assignment.", Exact = true }).CheckAsync();
    var bootstrapRequestTask = page.WaitForRequestAsync(request => request.Url.EndsWith("/api/setup/bootstrap", StringComparison.Ordinal));
    await page.GetByRole(AriaRole.Button, new() { Name = "Bind initial administrator", Exact = true }).ClickAsync();
    var bootstrapRequest = await bootstrapRequestTask;
    Assert.Contains("\"reviewed\":true", bootstrapRequest.PostData ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    await page.WaitForURLAsync("**/app/administration/microsoft365/tenant-connection", new() { Timeout = 15000 });
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Microsoft tenant connection", Exact = true })).ToBeVisibleAsync();
    Assert.Equal(200, await page.EvaluateAsync<int>("async () => (await fetch('/api/ui/session')).status"));

    await page.GotoAsync(origin + "/setup/microsoft365");
    await Assertions.Expect(page.GetByText("Initial setup is already closed.", new() { Exact = false })).ToBeVisibleAsync();
    Assert.Equal(0, await page.GetByLabel("Installation proof", new() { Exact = true }).CountAsync());

    await using (var db = host.CreateDbContext())
    {
      var bound = await db.Users.SingleAsync(x => x.TenantId == identity.TenantId && x.Subject == identity.Subject);
      Assert.Equal(identity.Email, bound.Email);
      Assert.Equal(identity.DisplayName, bound.DisplayName);
      Assert.Single(await db.RoleGrants.Where(x => x.UserId == bound.Id && x.Role == "Administrator" &&
        x.RevokedAt == null && x.ClientId == null && x.EngagementId == null).ToListAsync());
      Assert.Single(await db.RoleGrantChangeEvidences.Where(x => x.TargetUserId == bound.Id &&
        x.Action == "GRANTED" && x.Source == "BOOTSTRAP").ToListAsync());
      var session = await db.Microsoft365SetupSessions.SingleAsync(x => x.InstallationId == "angular-installation-bootstrap");
      Assert.Equal(bound.Id, session.ClaimedByUserId);
      Assert.NotNull(session.ConsumedAt);
      Assert.NotEqual(proof, session.BootstrapProofHash);
      Assert.DoesNotContain(proof, System.Text.Json.JsonSerializer.Serialize(session));
    }
    Assert.Empty(errors);
    foreach (var log in Directory.EnumerateFiles(host.RunRoot, "*.log"))
      Assert.DoesNotContain(proof, await File.ReadAllTextAsync(log));
  }
}
