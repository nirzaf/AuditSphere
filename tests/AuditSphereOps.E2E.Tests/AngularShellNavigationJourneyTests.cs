using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularShellNavigationJourneyTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("CaseId", "ANGULAR-SHELL-NAVIGATION")]
  public async Task ResponsiveMenu_KeyboardFocus_ContextAndRevocation(bool canonical)
  {
    await using var host = await OwnedHost.StartAsync(startWorker:false, caseId:"ANGULAR-SHELL-NAVIGATION");
    var settings = new Dictionary<string,string>{["AngularUi__Enabled"]="true",["AngularUi__CanonicalRoutes"]=canonical.ToString()};
    var prefix = canonical ? "" : "/ui";
    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    using var playwright = await Playwright.CreateAsync(); await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync(new(){ViewportSize=new(){Width=390,Height=844},ReducedMotion=ReducedMotion.Reduce});
    var page = await context.NewPageAsync(); var errors = new List<string>(); page.PageError += (_,error) => errors.Add(error);
    await page.GotoAsync(origin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/app"));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Portfolio",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("aside:has(> audit-workspace-navigation)")).ToBeHiddenAsync();
    var menu = page.GetByRole(AriaRole.Button,new(){Name="Open navigation",Exact=true});
    await menu.FocusAsync(); await menu.PressAsync("Enter");
    var dialog = page.GetByRole(AriaRole.Dialog,new(){Name="Workspace navigation",Exact=true});
    await Assertions.Expect(dialog).ToBeVisibleAsync();
    // Material correctly hides the background from accessibility while its modal is open.
    await Assertions.Expect(page.Locator("button.navigation-toggle")).ToHaveAttributeAsync("aria-expanded","true");
    var close = dialog.GetByRole(AriaRole.Button,new(){Name="Close navigation",Exact=true});
    await Assertions.Expect(close).ToBeFocusedAsync();
    await close.PressAsync("Shift+Tab");
    await Assertions.Expect(dialog.GetByRole(AriaRole.Link,new(){Name="Technical library",Exact=true})).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Tab"); await Assertions.Expect(close).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Escape"); await Assertions.Expect(dialog).ToHaveCountAsync(0); await Assertions.Expect(menu).ToBeFocusedAsync();
    await menu.PressAsync("Enter"); await dialog.GetByRole(AriaRole.Link,new(){Name="Accounting",Exact=true}).ClickAsync();
    await Assertions.Expect(dialog).ToHaveCountAsync(0);
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Accounting workspace",Exact=true})).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();
    Assert.Equal(origin+prefix+"/app/accounting",page.Url);
    await menu.ClickAsync(); await dialog.GetByRole(AriaRole.Link,new(){Name="Portfolio",Exact=true}).ClickAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Portfolio",Exact=true})).ToBeVisibleAsync();
    await page.GoBackAsync(); await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Accounting workspace",Exact=true})).ToBeVisibleAsync();
    await page.GoForwardAsync(); await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Portfolio",Exact=true})).ToBeVisibleAsync();
    await menu.ClickAsync(); await Assertions.Expect(dialog).ToBeVisibleAsync(); await page.SetViewportSizeAsync(1100,800);
    await Assertions.Expect(dialog).ToHaveCountAsync(0); await Assertions.Expect(page.Locator("aside:has(> audit-workspace-navigation)")).ToBeVisibleAsync();
    await Assertions.Expect(menu).ToBeHiddenAsync(); await Assertions.Expect(page.Locator("main")).ToBeFocusedAsync();
    await page.SetViewportSizeAsync(320,800); await menu.ClickAsync();
    Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
    await Assertions.Expect(close).ToBeInViewportAsync();
    await using(var db=host.CreateDbContext())
    {var user=await db.Users.SingleAsync(x=>x.Id==host.Fixture.Staff.Id);user.SessionEpoch++;await db.SaveChangesAsync();}
    await Assertions.Expect(dialog).ToHaveCountAsync(0,new(){Timeout=15000});
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,new(){Name="Access unavailable",Exact=true})).ToBeVisibleAsync();
    Assert.DoesNotContain("PBC TEST CLIENT",await page.Locator("body").InnerTextAsync());
    await Assertions.Expect(menu).ToHaveCountAsync(0);
    var clientOrigin=await host.StartApiForIdentityAsync(host.Fixture.Client,settings);
    await using var clientContext=await browser.NewContextAsync(new(){ViewportSize=new(){Width=390,Height=844}});
    var clientPage=await clientContext.NewPageAsync();
    await clientPage.GotoAsync(clientOrigin+"/auth/sign-in?returnUrl="+Uri.EscapeDataString(prefix+"/portal"));
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading,new(){Name="Client portal",Exact=true})).ToBeVisibleAsync();
    await clientPage.GetByRole(AriaRole.Button,new(){Name="Open navigation",Exact=true}).ClickAsync();
    var clientDialog=clientPage.GetByRole(AriaRole.Dialog,new(){Name="Workspace navigation",Exact=true});
    await Assertions.Expect(clientDialog.GetByRole(AriaRole.Link)).ToHaveCountAsync(1);
    await Assertions.Expect(clientDialog.GetByRole(AriaRole.Link,new(){Name="Client portal",Exact=true})).ToHaveAttributeAsync("href",prefix+"/portal");
    Assert.DoesNotContain("Administration",await clientDialog.InnerTextAsync());
    await clientPage.Keyboard.PressAsync("Escape");
    await Assertions.Expect(clientPage.GetByRole(AriaRole.Button,new(){Name="Open navigation",Exact=true})).ToBeFocusedAsync();
    Assert.Empty(errors);
  }
}
