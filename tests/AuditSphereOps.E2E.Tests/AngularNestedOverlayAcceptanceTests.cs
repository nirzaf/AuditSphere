using AuditSphereOps.Domain.Tests;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// US-042 nested-overlay depth acceptance: a compact-viewport workspace navigation dialog over a dirty
/// form must stack the unsaved-changes dialog above it with focus trapped in the top layer, Escape
/// closing exactly one layer at a time, focus restoring to the correct layer or trigger, and a completed
/// navigation returning focus to the destination main content. A real screen-reader walk-through remains
/// the separate human acceptance gate.
/// </summary>
public sealed class AngularNestedOverlayAcceptanceTests
{
  private readonly Xunit.Abstractions.ITestOutputHelper output;
  public AngularNestedOverlayAcceptanceTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

  [Fact]
  [Trait("CaseId", "ANGULAR-NESTED-OVERLAY-E2E")]
  public async Task DirtyPageNavigationDialog_StacksGuardDialog_WithPerLayerEscapeAndFocusRestoration()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "ANGULAR-NESTED-OVERLAY-E2E");
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
    // The compact navigation dialog only opens below the 700px breakpoint.
    await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
    {
      ViewportSize = new ViewportSize { Width = 390, Height = 844 },
    });
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);
    page.Dialog += async (_, dialog) => { Assert.Equal("beforeunload", dialog.Type); await dialog.AcceptAsync(); };

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString("/ui/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync();
    await page.GotoAsync(origin + "/ui/app/clients/" + f.ClientId + "/contacts/new");
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add client contact", Exact = true })).ToBeVisibleAsync();

    // A bounded edit makes the form dirty so the route guard has something to protect.
    await page.GetByLabel("Full name", new() { Exact = true }).FillAsync("Nested overlay journey");

    var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Open navigation", Exact = true });
    await toggle.ClickAsync();
    var navDialog = page.Locator(".cdk-overlay-pane").Filter(new() { HasText = "Workspace navigation" });
    await Assertions.Expect(navDialog).ToBeVisibleAsync();

    // Choosing a destination from inside the open dialog raises the guard dialog on top: two stacked
    // dialogs, focus inside the newest layer.
    await navDialog.GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true }).ClickAsync();
    var guardDialog = page.Locator(".cdk-overlay-pane").Filter(new() { HasText = "Unsubmitted edits" });
    await Assertions.Expect(guardDialog).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("[role='dialog']")).ToHaveCountAsync(2);
    // Dialog autoFocus lands a frame after visibility; poll before asserting focus placement.
    var focusInGuard = false;
    for (var attempt = 0; attempt < 25 && !focusInGuard; attempt++)
    {
      focusInGuard = await FocusIsInsideAsync(page, "Unsubmitted edits");
      if (!focusInGuard) await page.WaitForTimeoutAsync(200);
    }
    Assert.True(focusInGuard, "Focus did not move into the stacked guard dialog.");
    for (var tab = 0; tab < 8; tab++)
    {
      await page.Keyboard.PressAsync("Tab");
      Assert.True(await FocusIsInsideAsync(page, "Unsubmitted edits"),
        $"Focus escaped the top dialog after {tab + 1} Tab presses.");
    }

    // Escape closes exactly one layer: the guard first, leaving the navigation dialog trapped below.
    await page.Keyboard.PressAsync("Escape");
    await Assertions.Expect(guardDialog).Not.ToBeVisibleAsync();
    await Assertions.Expect(navDialog).ToBeVisibleAsync();
    Assert.True(await FocusIsInsideAsync(page, "Workspace navigation"),
      "Focus did not return into the navigation dialog after the guard dialog closed.");

    await page.Keyboard.PressAsync("Escape");
    await Assertions.Expect(navDialog).Not.ToBeVisibleAsync();
    await Assertions.Expect(toggle).ToBeFocusedAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Add client contact", Exact = true })).ToBeVisibleAsync();

    // A deliberate discard completes navigation: every overlay closes and focus lands on the destination.
    await toggle.ClickAsync();
    await Assertions.Expect(navDialog).ToBeVisibleAsync();
    await navDialog.GetByRole(AriaRole.Link, new() { Name = "Portfolio", Exact = true }).ClickAsync();
    await Assertions.Expect(guardDialog).ToBeVisibleAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Discard edits and continue", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Portfolio", Exact = true })).ToBeVisibleAsync(new() { Timeout = 15000 });
    await Assertions.Expect(page.Locator("[role='dialog']")).ToHaveCountAsync(0);
    Assert.True(await page.EvaluateAsync<bool>("document.activeElement && document.activeElement.id === 'main'"),
      "Focus did not move to the destination main content after navigation.");

    Assert.Empty(pageErrors);
    output.WriteLine("US-042 nested-overlay depth journey completed with zero page errors.");
  }

  private static Task<bool> FocusIsInsideAsync(IPage page, string markerText) =>
    page.EvaluateAsync<bool>(
      """
      (marker) => {
        const el = document.activeElement && document.activeElement.closest("[role='dialog']");
        return !!el && el.textContent.includes(marker);
      }
      """, markerText);
}
