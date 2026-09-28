using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Tests;
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
      foreach (var route in detailRoutes)
      {
        var id = siblingIds.Single(x => route.Contains(x.ToString("D"), StringComparison.Ordinal));
        var random = Guid.NewGuid();
        var real = Normalize(await SnapshotAsync(page, origin + route), id);
        var guessed = Normalize(await SnapshotAsync(page, origin + route.Replace(id.ToString("D"), random.ToString("D"), StringComparison.Ordinal)), random);
        if (real.Contains(Marker, StringComparison.OrdinalIgnoreCase)) leaks.Add($"{route}: sibling marker rendered");
        else if (real != guessed) leaks.Add($"{route}: distinguishable from a random identifier\n  sibling: {Excerpt(real, guessed)}\n  random:  {Excerpt(guessed, real)}");
      }

      var download = await context.APIRequest.GetAsync($"{origin}/api/pbc/uploads/{sibling.UploadIntentId:D}/download", new() { MaxRedirects = 0 });
      var guessedDownload = await context.APIRequest.GetAsync($"{origin}/api/pbc/uploads/{Guid.NewGuid():D}/download", new() { MaxRedirects = 0 });
      var downloadBody = await download.TextAsync();
      if (download.Status != guessedDownload.Status || downloadBody.Contains(Marker, StringComparison.OrdinalIgnoreCase) ||
          downloadBody != await guessedDownload.TextAsync())
        leaks.Add($"download: sibling {download.Status} vs random {guessedDownload.Status}");

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

  /// <summary>Visible main text once rendering has settled (two equal reads with no loading indicator).</summary>
  private static async Task<string> SnapshotAsync(IPage page, string url)
  {
    await page.GotoAsync(url);
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
