using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Xunit.Abstractions;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// Whole-application AS-PAR-002 differential isolation: a user holding only client-A-scoped grants renders every
/// staff list/queue/search/count route, a sibling client B in the same firm then gains marker-named practice, PBC
/// and accounting records, and every route must render byte-identical text. Client B's detail routes and byte
/// endpoint must be indistinguishable from a random, non-existent identifier.
/// </summary>
[Trait("Category", "AuthorizationAndScope")]
public sealed partial class SiblingClientIsolationJourneyTests(ITestOutputHelper output)
{
  private const string Marker = "ZQXSIBLING";

  private static readonly string[] ListRoutes =
  [
    "/app", "/app/practice/leads", "/app/practice/time", "/app/finance", "/app/operations", "/app/administration",
    "/app/accounting", "/app/accounting/evidence", "/app/accounting/mappings", "/app/accounting/journals",
    "/app/accounting/differences", "/app/accounting/reviews", "/app/accounting/rollforward", "/app/accounting/restatements",
    "/app/accounting/remeasurement", "/app/consolidation", "/app/audit/library"
  ];

  public static TheoryData<string> ScopedRoleSets => new() { "CLIENT", "ENGAGEMENT" };

  [Theory]
  [MemberData(nameof(ScopedRoleSets))]
  [Trait("CaseId", "AS-PAR-002-SIBLING-CLIENT-DIFF-01")]
  public async Task SiblingClientDataNeverChangesWhatAClientScopedUserSees(string grantScope)
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-SIBLING-CLIENT-DIFF-01");
    var scoped = PbcSeed.User(host.Fixture.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(scoped);
      foreach (var role in new[] { "Partner", "Manager", "Staff", "Reviewer", "AccountingPreparer", "AccountingReviewer" })
        db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, scoped, role, host.Fixture.ClientId,
          grantScope == "ENGAGEMENT" ? host.Fixture.EngagementId : null));
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(scoped);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app")}");
    await page.Locator("h1").First.WaitForAsync(new() { Timeout = 15000 });

    var before = new Dictionary<string, string>();
    foreach (var route in ListRoutes) before[route] = await SnapshotAsync(page, origin + route);

    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, Marker);
    try
    {
      var leaks = new List<string>();
      foreach (var route in ListRoutes)
      {
        var after = await SnapshotAsync(page, origin + route);
        if (after.Contains(Marker, StringComparison.OrdinalIgnoreCase)) leaks.Add($"{route}: sibling marker rendered");
        else if (after != before[route]) leaks.Add($"{route}: rendered text changed\n  before: {Excerpt(before[route], after)}\n  after:  {Excerpt(after, before[route])}");
      }

      var engagement = sibling.Fixture.EngagementId;
      var detailRoutes = new[]
      {
        $"/app/clients/{sibling.Fixture.ClientId:D}", $"/app/clients/{sibling.Fixture.ClientId:D}/assessment",
        $"/app/engagements/{engagement:D}", $"/app/engagements/{engagement:D}/pbc", $"/app/engagements/{engagement:D}/audit-plan",
        $"/app/engagements/{engagement:D}/audit-fieldwork", $"/app/engagements/{engagement:D}/completion",
        $"/app/accounting/periods/{sibling.PeriodId:D}", $"/app/accounting/packages/{sibling.PackageId:D}",
        $"/app/accounting/mappings/{sibling.MappingId:D}", $"/app/accounting/journals/{sibling.JournalId:D}"
      };
      var siblingIds = new[] { sibling.Fixture.ClientId, engagement, sibling.PeriodId, sibling.PackageId, sibling.MappingId, sibling.JournalId };
      await CompareDetailRoutesAsync(page, origin, detailRoutes, siblingIds, leaks);
      await CompareDownloadAsync(context, origin, sibling.UploadIntentId, leaks);

      foreach (var leak in leaks) output.WriteLine(leak);
      Assert.Empty(leaks);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      output.WriteLine($"{grantScope}-scoped user: {ListRoutes.Length} list routes unchanged, {detailRoutes.Length} sibling detail routes and the byte endpoint indistinguishable from random identifiers");
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-SIBLING-PORTAL-DIFF-01")]
  public async Task SiblingClientDataNeverChangesWhatAnotherClientSeesInThePortal()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-SIBLING-PORTAL-DIFF-01");
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var origin = host.ClientUrl;
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/portal")}");
    await page.Locator("h1").First.WaitForAsync(new() { Timeout = 15000 });
    string[] portalRoutes = ["/portal", $"/portal/requests/{host.RequestId:D}"];
    var before = new Dictionary<string, string>();
    foreach (var route in portalRoutes) before[route] = await SnapshotAsync(page, origin + route);

    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, Marker);
    try
    {
      var leaks = new List<string>();
      foreach (var route in portalRoutes)
      {
        var after = await SnapshotAsync(page, origin + route);
        if (after.Contains(Marker, StringComparison.OrdinalIgnoreCase)) leaks.Add($"{route}: sibling marker rendered");
        else if (after != before[route]) leaks.Add($"{route}: rendered text changed\n  before: {Excerpt(before[route], after)}\n  after:  {Excerpt(after, before[route])}");
      }
      await CompareDetailRoutesAsync(page, origin,
        [$"/portal/requests/{sibling.PbcRequestId:D}", $"/portal/accounting/packages/{sibling.PackageId:D}"],
        [sibling.PbcRequestId, sibling.PackageId], leaks);
      await CompareDownloadAsync(context, origin, sibling.UploadIntentId, leaks);

      foreach (var leak in leaks) output.WriteLine(leak);
      Assert.Empty(leaks);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      output.WriteLine("client portal user: portal routes unchanged; sibling request, package and upload bytes indistinguishable from random identifiers");
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-SIBLING-GROUP-DIFF-01")]
  public async Task SiblingGroupNeverChangesWhatAGroupScopedUserSees()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-SIBLING-GROUP-DIFF-01");
    var groupUser = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var ownGroupId = Guid.NewGuid();
    var ownScopeId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    await using (var db = host.CreateDbContext())
    {
      db.Users.Add(groupUser);
      db.ClientGroups.Add(new ClientGroup { Id = ownGroupId, FirmId = host.Fixture.FirmId, Code = "OWN-GROUP", Name = "Own reporting group",
        CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = now });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion { Id = ownScopeId, FirmId = host.Fixture.FirmId, GroupId = ownGroupId,
        PeriodId = Guid.NewGuid(), Method = AdvancedConsolidationMethods.AcquisitionNci, ReportingCurrency = "QAR",
        OpeningBasis = "OPENING-2026", CreatedByUserId = host.Fixture.Admin.Id });
      foreach (var role in new[] { "AccountingPreparer", "AccountingReviewer" })
        db.GroupAccessGrants.Add(new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, GroupId = ownGroupId,
          UserId = groupUser.Id, Role = role, GrantedAt = now, GrantedByUserId = host.Fixture.Admin.Id });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(groupUser);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/app/consolidation")}");
    await page.Locator("h1").First.WaitForAsync(new() { Timeout = 15000 });
    string[] groupRoutes = ["/app/consolidation", $"/app/consolidation/advanced/{ownScopeId:D}", "/app"];
    var before = new Dictionary<string, string>();
    foreach (var route in groupRoutes) before[route] = await SnapshotAsync(page, origin + route);

    var siblingGroupId = Guid.NewGuid();
    var siblingScopeId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.ClientGroups.Add(new ClientGroup { Id = siblingGroupId, FirmId = host.Fixture.FirmId, Code = $"{Marker}-G", Name = $"{Marker} Group",
        CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = now });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion { Id = siblingScopeId, FirmId = host.Fixture.FirmId, GroupId = siblingGroupId,
        PeriodId = Guid.NewGuid(), Method = AdvancedConsolidationMethods.AcquisitionNci, ReportingCurrency = "QAR",
        OpeningBasis = $"{Marker}-OPENING", CreatedByUserId = host.Fixture.Admin.Id });
      await db.SaveChangesAsync();
    }

    var leaks = new List<string>();
    foreach (var route in groupRoutes)
    {
      var after = await SnapshotAsync(page, origin + route);
      if (after.Contains(Marker, StringComparison.OrdinalIgnoreCase)) leaks.Add($"{route}: sibling marker rendered");
      else if (after != before[route]) leaks.Add($"{route}: rendered text changed\n  before: {Excerpt(before[route], after)}\n  after:  {Excerpt(after, before[route])}");
    }
    await CompareDetailRoutesAsync(page, origin, [$"/app/consolidation/advanced/{siblingScopeId:D}"], [siblingScopeId], leaks);

    foreach (var leak in leaks) output.WriteLine(leak);
    Assert.Empty(leaks);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await page.GotoAsync(origin + "/app/consolidation");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Perimeter", Exact = true }).WaitForAsync();
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      await page.WaitForFunctionAsync(width < 960
        ? "() => (document.querySelector('.audit-main-content') !== null && getComputedStyle(document.querySelector('.audit-main-content')).marginLeft === '0px')"
        : "() => document.querySelector('.audit-sidebar')?.getBoundingClientRect().left >= -1");
      if (Environment.GetEnvironmentVariable("AUDITSPHERE_GROUP_UI_CAPTURE_DIR") is { Length: > 0 } captureDir &&
          width is 320 or 390 or 1440)
      {
        Directory.CreateDirectory(captureDir);
        await page.ScreenshotAsync(new() { Path = Path.Combine(captureDir, $"group-{width}.png"), FullPage = true });
      }
      var overflowPixels = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
      var overflowing = overflowPixels > 1
        ? await page.EvaluateAsync<string[]>("() => [...document.querySelectorAll('body *')].filter(x => x.getBoundingClientRect().right > innerWidth + 1).slice(0, 12).map(x => `${x.tagName.toLowerCase()}.${String(x.className).slice(0, 80)}`)")
        : [];
      Assert.True(overflowPixels <= 1,
        $"Group consolidation overflows by {overflowPixels}px at {width}px: {string.Join(", ", overflowing)}.");
    }
    var advancedLink = page.GetByRole(AriaRole.Link, new() { Name = "Open advanced workflow" });
    await advancedLink.FocusAsync();
    Assert.Equal("solid", await advancedLink.EvaluateAsync<string>("element => getComputedStyle(element).outlineStyle"));
    await advancedLink.ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Advanced consolidation workflow" }).WaitForAsync();
    output.WriteLine("group-scoped user: consolidation routes unchanged; sibling group scope indistinguishable from a random identifier");
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ASSESSMENT-STALE-ROUTE-01")]
  public async Task AssessmentWorkbenchesClearWhenRouteChangesInPlaceToSiblingClient()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-PAR-002-ASSESSMENT-STALE-ROUTE-01");
    const string ownPrivate = "OWN-PRIVATE-REGISTRATION-7731";
    var reviewer = PbcSeed.User(host.Fixture.FirmId, "Staff");
    var sibling = await SiblingClientSeed.SeedAsync(host.Database, host.Fixture.FirmId, Marker);
    var ownDecision = Guid.NewGuid();
    var siblingDecision = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      (await db.PracticeClients.SingleAsync(x => x.Id == host.Fixture.ClientId)).RegistrationNumber = ownPrivate;
      (await db.PracticeClients.SingleAsync(x => x.Id == sibling.Fixture.ClientId)).RegistrationNumber = $"{Marker}-REGISTRATION";
      db.Users.Add(reviewer);
      foreach (var role in new[] { "Partner", "Manager" })
        db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, reviewer, role, host.Fixture.ClientId));
      foreach (var (id, clientId) in new[] { (ownDecision, host.Fixture.ClientId), (siblingDecision, sibling.Fixture.ClientId) })
        db.AcceptanceDecisions.Add(new AuditSphereOps.Domain.Acceptance.AcceptanceDecision
        {
          Id = id, FirmId = host.Fixture.FirmId, PracticeClientId = clientId, Decision = "Declined", ServiceRoute = "SyntheticAudit",
          Generation = 1, Rationale = "Synthetic in-place navigation decision", EvaluationTemplateVersion = "SYNTHETIC-v1",
          EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = host.Fixture.Admin.Id, DecidedAt = DateTimeOffset.UtcNow
        });
      await db.SaveChangesAsync();
    }

    var origin = await host.StartWebForIdentityAsync(reviewer);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var routes = new (string Template, Guid Own, Guid Sibling)[]
    {
      ("/app/assessments/{0}", ownDecision, siblingDecision),
      ("/app/assessments/{0}/decision", host.Fixture.ClientId, sibling.Fixture.ClientId),
      ("/app/clients/{0}/assessment", host.Fixture.ClientId, sibling.Fixture.ClientId)
    };
    var leaks = new List<string>();
    try
    {
      foreach (var (template, own, siblingId) in routes)
      {
        string Path(Guid id) => string.Format(System.Globalization.CultureInfo.InvariantCulture, template, id.ToString("D"));
        await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(Path(own))}");
        var initial = await SettleAsync(page);
        var token = Guid.NewGuid().ToString("N");
        await page.EvaluateAsync("token => window.__testDocumentToken = token", token);

        var toSibling = Normalize(await SnapshotInPlaceAsync(page, Path(siblingId)), siblingId);
        var random = Guid.NewGuid();
        var toRandom = Normalize(await SnapshotInPlaceAsync(page, Path(random)), random);
        var back = await SnapshotInPlaceAsync(page, Path(own));

        if (toSibling.Contains(Marker, StringComparison.OrdinalIgnoreCase)) leaks.Add($"{template}: sibling marker rendered");
        if (toSibling.Contains(ownPrivate, StringComparison.Ordinal)) leaks.Add($"{template}: prior client content survived in-place navigation");
        if (toSibling != toRandom) leaks.Add($"{template}: sibling distinguishable from random\n  sibling: {Excerpt(toSibling, toRandom)}\n  random:  {Excerpt(toRandom, toSibling)}");
        if (back != initial) leaks.Add($"{template}: returning to the authorized record did not restore it\n  initial: {Excerpt(initial, back)}\n  back:    {Excerpt(back, initial)}");
        if (await page.EvaluateAsync<string>("() => window.__testDocumentToken") != token) leaks.Add($"{template}: navigation was not in-circuit");
      }
      foreach (var leak in leaks) output.WriteLine(leak);
      Assert.Empty(leaks);
      Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
      output.WriteLine($"{routes.Length} assessment workbench routes cleared prior content in-circuit and matched a random identifier for the sibling client");
    }
    finally
    {
      PbcSeed.DeleteDirectory(sibling.StagingRoot);
    }
  }

  /// <summary>Each sibling detail route must render exactly what the same route renders for a random identifier.</summary>
  private static async Task CompareDetailRoutesAsync(IPage page, string origin, IReadOnlyList<string> routes, IReadOnlyList<Guid> ids, List<string> leaks)
  {
    foreach (var route in routes)
    {
      var id = ids.Single(x => route.Contains(x.ToString("D"), StringComparison.Ordinal));
      var random = Guid.NewGuid();
      var real = Normalize(await SnapshotAsync(page, origin + route), id);
      var guessed = Normalize(await SnapshotAsync(page, origin + route.Replace(id.ToString("D"), random.ToString("D"), StringComparison.Ordinal)), random);
      if (real.Contains(Marker, StringComparison.OrdinalIgnoreCase)) leaks.Add($"{route}: sibling marker rendered");
      else if (real != guessed) leaks.Add($"{route}: distinguishable from a random identifier\n  sibling: {Excerpt(real, guessed)}\n  random:  {Excerpt(guessed, real)}");
    }
  }

  private static async Task CompareDownloadAsync(IBrowserContext context, string origin, Guid uploadIntentId, List<string> leaks)
  {
    var download = await context.APIRequest.GetAsync($"{origin}/api/pbc/uploads/{uploadIntentId:D}/download", new() { MaxRedirects = 0 });
    var guessed = await context.APIRequest.GetAsync($"{origin}/api/pbc/uploads/{Guid.NewGuid():D}/download", new() { MaxRedirects = 0 });
    var body = await download.TextAsync();
    if (download.Status != guessed.Status || body.Contains(Marker, StringComparison.OrdinalIgnoreCase) || body != await guessed.TextAsync())
      leaks.Add($"download: sibling {download.Status} vs random {guessed.Status}");
  }

  /// <summary>Visible main text once rendering has settled (two equal reads with no loading indicator).</summary>
  private static async Task<string> SnapshotAsync(IPage page, string url)
  {
    await page.GotoAsync(url);
    return await SettleAsync(page);
  }

  /// <summary>Same-document (in-circuit) navigation, then the settled main text.</summary>
  private static async Task<string> SnapshotInPlaceAsync(IPage page, string path)
  {
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);
    await page.WaitForTimeoutAsync(300);
    return await SettleAsync(page);
  }

  private static async Task<string> SettleAsync(IPage page)
  {
    await page.Locator("h1").First.WaitForAsync(new() { Timeout = 15000 });
    var previous = string.Empty;
    for (var attempt = 0; attempt < 40; attempt++)
    {
      await page.WaitForTimeoutAsync(250);
      // The browser-local draft status reflects this viewer's own earlier visits, not server data.
      var text = DraftStatus().Replace(Whitespace().Replace(await page.Locator("main").First.InnerTextAsync(), " "), "{draft-status}").Trim();
      var loading = await page.Locator(".mud-progress-circular, .mud-progress-linear, .mud-skeleton").CountAsync() > 0;
      if (!loading && text == previous && text.Length > 0) return text;
      previous = text;
    }
    return previous;
  }

  private static string Normalize(string text, Guid id) =>
    text.Replace(id.ToString("D"), "{id}", StringComparison.OrdinalIgnoreCase).Replace(id.ToString("N"), "{id}", StringComparison.OrdinalIgnoreCase);

  /// <summary>Short window around the first character where two snapshots diverge.</summary>
  private static string Excerpt(string text, string other)
  {
    var index = 0;
    while (index < text.Length && index < other.Length && text[index] == other[index]) index++;
    var start = Math.Max(0, index - 60);
    return "…" + text[start..Math.Min(text.Length, index + 140)] + "…";
  }

  [GeneratedRegex(@"Draft autosave is ready\.|Unsaved draft restored locally \([^)]*\)\.")]
  private static partial Regex DraftStatus();

  [GeneratedRegex(@"\s+")]
  private static partial Regex Whitespace();
}
