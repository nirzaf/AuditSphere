using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// US-014 global receipt reconciliation: after a command response is lost, the shell must surface the
/// persisted unknown outcome on every screen of the same tab, deep-link back to the owning workspace,
/// and clear the indicator once the retained receipt is verified and acknowledged. Nothing is retried
/// automatically; verification stays a manual, per-feature action.
/// </summary>
public sealed class AngularPendingOutcomeReconciliationTests
{
  private readonly Xunit.Abstractions.ITestOutputHelper output;
  public AngularPendingOutcomeReconciliationTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

  [Fact]
  [Trait("CaseId", "ANGULAR-PENDING-OUTCOME-E2E")]
  public async Task LostCommandResponse_SurfacesGlobally_DeepLinksAndClearsAfterAcknowledgment()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PENDING-OUTCOME-E2E");
    var f = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      await ClientProfileWorkspaceSeed.PopulateAsync(db, f);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(f.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var errors = new List<string>();
    page.PageError += (_, e) => errors.Add(e);
    page.Dialog += async (_, d) => { Assert.Equal("beforeunload", d.Type); await d.AcceptAsync(); };

    var contact = "/ui/app/clients/" + f.ClientId + "/contacts/new";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(origin + contact);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add client contact", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Region, new() { Name = "Unverified command outcomes", Exact = true })).ToHaveCountAsync(0);

    // Send the reviewed creation but lose the response after the server accepted it.
    await page.GetByLabel("Full name", new() { Exact = true }).FillAsync("Global receipt journey");
    await page.GetByLabel("Email address", new() { Exact = true }).FillAsync("receipt@example.test");
    await page.GetByLabel("Role or title", new() { Exact = true }).FillAsync("Finance");
    await page.GetByRole(AriaRole.Button, new() { Name = "Review contact", Exact = true }).ClickAsync();
    await page.GetByLabel("I reviewed these exact details and primary-contact changes.", new() { Exact = true }).CheckAsync();
    var endpoint = "**/api/ui/clients/" + f.ClientId + "/contact-creation";
    await page.RouteAsync(endpoint, async route =>
    {
      if (route.Request.Method == "POST")
      {
        await using var accepted = await route.FetchAsync();
        Assert.Equal(200, accepted.Status);
        await route.AbortAsync("failed");
      }
      else await route.ContinueAsync();
    });
    await page.GetByRole(AriaRole.Button, new() { Name = "Confirm contact creation", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Contact outcome unconfirmed", Exact = true })).ToBeVisibleAsync();
    await page.UnrouteAsync(endpoint);

    // The fence blocks in-app navigation while unconfirmed, so a full navigation to a different
    // workspace is how a practitioner leaves it behind: the shell must still surface it there.
    await page.GotoAsync(origin + "/ui/app");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    var banner = page.GetByRole(AriaRole.Region, new() { Name = "Unverified command outcomes", Exact = true });
    await Assertions.Expect(banner).ToBeVisibleAsync();
    var outcomeLink = banner.GetByRole(AriaRole.Link, new() { Name = "Client contact creation — open the workspace and verify the retained receipt.", Exact = true });
    await Assertions.Expect(outcomeLink).ToBeVisibleAsync();

    // The deep link returns to the owning workspace with the unknown-outcome fence intact.
    await outcomeLink.ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Contact outcome unconfirmed", Exact = true })).ToBeVisibleAsync();

    // Manual verification reads the retained receipt; acknowledgment clears the global indicator.
    await page.GetByRole(AriaRole.Button, new() { Name = "Verify contact receipt", Exact = true }).ClickAsync();
    var recorded = page.GetByRole(AriaRole.Region, new() { Name = "Contact creation receipt", Exact = true });
    await Assertions.Expect(recorded).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Acknowledge contact result", Exact = true }).ClickAsync();
    await Assertions.Expect(banner).ToHaveCountAsync(0, new() { Timeout = 10000 });

    Assert.Empty(errors);
    output.WriteLine("US-014 global pending-outcome reconciliation journey completed with zero page errors.");
  }
}
