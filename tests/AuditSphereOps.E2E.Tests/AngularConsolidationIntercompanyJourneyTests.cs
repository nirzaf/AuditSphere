using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "Accessibility")]
public sealed class AngularConsolidationIntercompanyJourneyTests
{
  private const string CaseId = "AS-PAR-002-ANG-CONSOL-IC-RETAINED-01";
  private const string TransactionReference = "SYN-IC-RETAINED-REGULAR-01";
  private const string EvidenceReference = "SYN-IC-CONFIRMATION-01";
  private const string DifferenceReason = "Synthetic timing difference retained for outside-perimeter review.";

  private static async Task TabToAsync(IPage page, ILocator target)
  {
    var focusableCount = await page.Locator(
      "a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),summary:not([tabindex='-1']),[tabindex]:not([tabindex='-1'])")
      .CountAsync();
    for (var attempt = 0; attempt <= focusableCount; attempt++)
    {
      if (await target.EvaluateAsync<bool>("element => element === document.activeElement")) return;
      await page.Keyboard.PressAsync("Tab");
    }

    Assert.True(await target.EvaluateAsync<bool>("element => element === document.activeElement"),
      "The retained details summary could not be reached through the page's normal Tab order.");
  }

  [Fact]
  [Trait("CaseId", CaseId)]
  public async Task RegularScopeRetainsExactOutsidePerimeterMatchForKeyboardReview()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: CaseId);
    var f = host.Fixture;
    var groupId = Guid.NewGuid();
    var scopeId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var outsideClientId = Guid.NewGuid();
    const string groupCode = "SYN-IC-REGULAR-01";
    const string groupName = "Synthetic intercompany group";
    const string sellerTaxonomy = "ASSETS.IC_AR";
    const string buyerTaxonomy = "LIABILITIES.IC_AP";
    const string sellerAmount = "1200.123456";
    const string buyerAmount = "-1200.000000";
    const string matchedAmount = "1200.000000";
    var now = DateTimeOffset.UtcNow;

    await using (var db = host.CreateDbContext())
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = outsideClientId, FirmId = f.FirmId, LegalName = "Synthetic outside-perimeter entity", CreatedAt = now
      });
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = f.FirmId, Code = groupCode, Name = groupName, Revision = 1,
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = groupId, UserId = f.Staff.Id,
        Role = "Manager", GrantedAt = now, GrantedByUserId = f.Admin.Id
      });
      db.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, GroupId = groupId, ClientId = f.ClientId,
        EffectiveFrom = new DateOnly(2020, 1, 1), Status = AccountingWorkflowStates.Approved,
        OwnershipPercent = 100m, EconomicInterestPercent = 100m, ControlMethod = "CONTROLLED",
        EvidenceReference = "SYNTHETIC-IC-EFFECTIVE-MEMBERSHIP",
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "ANNUAL", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeId, FirmId = f.FirmId, GroupId = groupId, PeriodId = periodId, Version = 1,
        GroupRevision = 1, ReportingCurrency = "QAR", Method = ConsolidationCalculator.RestrictedMethod,
        OpeningBasis = "INITIAL_CONSOLIDATION", Status = AccountingWorkflowStates.Draft,
        CreatedByUserId = f.Admin.Id, CreatedAt = now
      });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartApiForIdentityAsync(f.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    var path = $"/app/consolidation/scopes/{scopeId:D}";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var pageErrors = new List<string>();
    page.PageError += (_, error) => pageErrors.Add(error);

    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = $"{groupName} ({groupCode}) · v1", Exact = true })).ToBeVisibleAsync();

    var intercompany = page.Locator("#intercompany");
    var form = intercompany.Locator("form[aria-label='Record intercompany match']");
    await Assertions.Expect(form).ToBeVisibleAsync();
    await Assertions.Expect(form.GetByLabel("Currency", new() { Exact = true })).ToHaveValueAsync("QAR");

    var outsideReview = form.GetByLabel(
      "Related party is outside the approved group perimeter (review only; no automatic elimination)",
      new() { Exact = true });
    const string optionsPattern = "**/intercompany-match-options?*";
    var optionsReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseOptions = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    await page.RouteAsync(optionsPattern, async route =>
    {
      var response = await route.FetchAsync();
      Assert.Equal(200, response.Status);
      optionsReached.TrySetResult();
      await releaseOptions.Task.WaitAsync(TimeSpan.FromSeconds(10));
      await route.FulfillAsync(new() { Response = response });
    });
    await TabToAsync(page, outsideReview);
    await page.Keyboard.PressAsync("Space");
    try
    {
      await optionsReached.Task.WaitAsync(TimeSpan.FromSeconds(10));
      await Assertions.Expect(outsideReview).ToBeCheckedAsync();
      await Assertions.Expect(outsideReview).ToBeFocusedAsync();
      await Assertions.Expect(form.GetByLabel(
        "I reviewed both counterparties, exact amounts, taxonomies, evidence and the current group revision.",
        new() { Exact = true })).ToBeDisabledAsync();
      await Assertions.Expect(form.GetByRole(AriaRole.Button,
        new() { Name = "Submit for independent review", Exact = true })).ToBeDisabledAsync();
    }
    finally { releaseOptions.TrySetResult(); }
    await Assertions.Expect(form.GetByLabel(
      "I reviewed both counterparties, exact amounts, taxonomies, evidence and the current group revision.",
      new() { Exact = true })).ToBeEnabledAsync();
    await page.UnrouteAsync(optionsPattern);
    await Assertions.Expect(form.GetByRole(AriaRole.Combobox, new() { Name = "Outside party side", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(outsideReview).ToBeFocusedAsync();
    await form.GetByRole(AriaRole.Combobox, new() { Name = "Outside party side", Exact = true }).SelectOptionAsync("BUYER");
    var outsideClient = form.GetByLabel("Outside client ID", new() { Exact = true });
    await TabToAsync(page, outsideClient);
    await outsideClient.PressSequentiallyAsync(outsideClientId.ToString("D"));
    await Assertions.Expect(outsideClient).ToHaveValueAsync(outsideClientId.ToString("D"));
    await Assertions.Expect(outsideClient).ToBeFocusedAsync();

    var sellerMember = form.GetByRole(AriaRole.Combobox, new() { Name = "Seller group member", Exact = true });
    await Assertions.Expect(sellerMember.Locator("option")).ToHaveCountAsync(2);
    await Assertions.Expect(sellerMember.Locator($"option[value='{f.ClientId:D}']"))
      .ToContainTextAsync("no approved component");
    Assert.Equal(0, await sellerMember.Locator($"option[value='{outsideClientId:D}']").CountAsync());
    await sellerMember.SelectOptionAsync(f.ClientId.ToString("D"));
    var buyerMember = form.GetByRole(AriaRole.Combobox, new() { Name = "Buyer group member", Exact = true });
    await Assertions.Expect(buyerMember).ToBeDisabledAsync();
    await Assertions.Expect(buyerMember.Locator("option")).ToHaveCountAsync(2);

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>(
      "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "Populated intercompany member selectors overflow the document at 390px.");
    Assert.True(await sellerMember.EvaluateAsync<bool>(
      "element => element.getBoundingClientRect().right <= innerWidth + 1"),
      "The selected seller control extends beyond the viewport.");
    await page.SetViewportSizeAsync(1280, 720);

    await form.GetByRole(AriaRole.Combobox, new() { Name = "Account nature", Exact = true }).SelectOptionAsync("RECEIVABLE_PAYABLE");
    await form.GetByLabel("Period code", new() { Exact = true }).FillAsync("FY2026");
    await form.GetByLabel("Transaction reference", new() { Exact = true }).FillAsync(TransactionReference);
    await form.GetByRole(AriaRole.Combobox, new() { Name = "Match mode", Exact = true }).SelectOptionAsync("ONE_TO_ONE");
    await form.GetByLabel("Seller exact amount", new() { Exact = true }).FillAsync(sellerAmount);
    await form.GetByLabel("Buyer exact amount", new() { Exact = true }).FillAsync(buyerAmount);
    await form.GetByLabel("Matched amount", new() { Exact = true }).FillAsync(matchedAmount);
    await form.GetByLabel("Seller taxonomy code", new() { Exact = true }).FillAsync(sellerTaxonomy);
    await form.GetByLabel("Buyer taxonomy code", new() { Exact = true }).FillAsync(buyerTaxonomy);
    await form.GetByLabel("Evidence reference", new() { Exact = true }).FillAsync(EvidenceReference);
    await form.GetByRole(AriaRole.Textbox, new() { Name = "Unmatched balance or signed difference reason" })
      .FillAsync(DifferenceReason);

    await Assertions.Expect(form.GetByLabel("Seller exact amount", new() { Exact = true }))
      .ToHaveValueAsync(sellerAmount);
    await Assertions.Expect(form.GetByLabel("Buyer exact amount", new() { Exact = true }))
      .ToHaveValueAsync(buyerAmount);
    await Assertions.Expect(form.GetByLabel("Matched amount", new() { Exact = true }))
      .ToHaveValueAsync(matchedAmount);
    await form.GetByLabel(
      "I reviewed both counterparties, exact amounts, taxonomies, evidence and the current group revision.",
      new() { Exact = true }).CheckAsync();

    var submit = form.GetByRole(AriaRole.Button,
      new() { Name = "Submit for independent review", Exact = true });
    await Assertions.Expect(submit).ToBeEnabledAsync();
    await submit.ClickAsync();

    var recordRow = intercompany.GetByRole(AriaRole.Row).Filter(new() { HasText = TransactionReference });
    await Assertions.Expect(recordRow).ToBeVisibleAsync();
    await Assertions.Expect(recordRow.Locator("audit-status .status-chip")).ToHaveTextAsync("submitted");
    await Assertions.Expect(recordRow).ToContainTextAsync(
      "Maker/checker: preparers cannot approve their own match.");
    Assert.Equal(0, await recordRow.GetByRole(AriaRole.Button,
      new() { Name = "Approve match", Exact = true }).CountAsync());

    var details = recordRow.Locator("details");
    var summary = details.Locator("summary");
    await Assertions.Expect(summary).ToContainTextAsync(
      $"RECEIVABLE_PAYABLE · FY2026 · QAR · {TransactionReference}");
    Assert.False(await details.EvaluateAsync<bool>("element => element.open"));
    await TabToAsync(page, summary);
    await Assertions.Expect(summary).ToBeFocusedAsync();
    await page.Keyboard.PressAsync("Enter");
    await Assertions.Expect(details.Locator("dl")).ToBeVisibleAsync();

    var facts = details.Locator("dl");
    await Assertions.Expect(facts.Locator("dd").Nth(0)).ToContainTextAsync(f.ClientId.ToString("D"));
    await Assertions.Expect(facts.Locator("dd").Nth(0)).ToContainTextAsync(sellerTaxonomy);
    await Assertions.Expect(facts.Locator("dd").Nth(0)).ToContainTextAsync(sellerAmount);
    await Assertions.Expect(facts.Locator("dd").Nth(1)).ToContainTextAsync(outsideClientId.ToString("D"));
    await Assertions.Expect(facts.Locator("dd").Nth(1)).ToContainTextAsync(buyerTaxonomy);
    await Assertions.Expect(facts.Locator("dd").Nth(1)).ToContainTextAsync(buyerAmount);
    await Assertions.Expect(facts.Locator("dd").Nth(2)).ToContainTextAsync(matchedAmount);
    await Assertions.Expect(facts.Locator("dd").Nth(2)).ToContainTextAsync("0.123456");
    await Assertions.Expect(facts.Locator("dd").Nth(3)).ToContainTextAsync("0.123456");
    await Assertions.Expect(facts.Locator("dd").Nth(4)).ToContainTextAsync(DifferenceReason);
    await Assertions.Expect(facts.Locator("dd").Nth(6)).ToContainTextAsync("Yes · review only");
    await Assertions.Expect(facts.Locator("dd").Nth(7)).ToContainTextAsync(EvidenceReference);
    await Assertions.Expect(facts.Locator("dd").Nth(8)).ToContainTextAsync(f.Staff.Id.ToString("D"));
    await Assertions.Expect(facts.Locator("dd").Nth(9)).ToContainTextAsync("Not reviewed");

    await page.SetViewportSizeAsync(390, 844);
    Assert.True(await page.EvaluateAsync<bool>(
      "() => document.documentElement.scrollWidth <= window.innerWidth + 1"),
      "The consolidation document overflows at 390px.");
    var tableScroll = intercompany.Locator(".table-scroll");
    Assert.True(await tableScroll.EvaluateAsync<bool>("""
      element => {
        const rect = element.getBoundingClientRect();
        return rect.left >= -1 && rect.right <= innerWidth + 1 && getComputedStyle(element).overflowX === 'auto';
      }
      """), "The retained intercompany table is not bounded by its horizontal scroll region at 390px.");
    Assert.Empty(pageErrors);

    await using (var proof = host.CreateDbContext())
    {
      var match = Assert.Single(await proof.IntercompanyMatches.AsNoTracking()
        .Where(x => x.ScopeVersionId == scopeId).ToListAsync());
      Assert.Equal(f.ClientId, match.SellerClientId);
      Assert.Equal(outsideClientId, match.BuyerClientId);
      Assert.Equal(1200.123456m, match.SellerAmount);
      Assert.Equal(-1200.000000m, match.BuyerAmount);
      Assert.Equal(1200.000000m, match.MatchedAmount);
      Assert.Equal(0.123456m, match.Difference);
      Assert.Equal(AccountingWorkflowStates.Submitted, match.Status);
      Assert.True(match.OutsidePerimeterReview);
      Assert.Equal(EvidenceReference, match.EvidenceReference);
      Assert.Equal(DifferenceReason, match.DifferenceReason);
      Assert.Equal(f.Staff.Id, match.CreatedByUserId);
      Assert.Null(match.ReviewedByUserId);
      Assert.Null(match.ReviewedAt);
      Assert.Empty(await proof.ConsolidationComponents.Where(x => x.ScopeVersionId == scopeId).ToListAsync());
      Assert.Empty(await proof.ConsolidationJournals.Where(x => x.ScopeVersionId == scopeId).ToListAsync());
      Assert.Empty(await proof.ConsolidationRuns.Where(x => x.ScopeVersionId == scopeId).ToListAsync());
    }

    await OwnedBrowserFixtureHandoff.WaitAsync(origin, path, "consolidation-intercompany");
  }
}
