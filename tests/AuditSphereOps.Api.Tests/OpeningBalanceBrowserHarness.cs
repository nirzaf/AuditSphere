using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

/// <summary>Explicit opt-in local browser fixture. Not an xUnit test and never discovered by ordinary CI.</summary>
public static class OpeningBalanceBrowserHarness
{
  public static Task RunAsync(string evidenceDirectory, CancellationToken cancellationToken) =>
    CompletionVarianceCommandsApiTests.RunOpeningBalanceBrowserAsync(evidenceDirectory, cancellationToken);
}

public sealed partial class CompletionVarianceCommandsApiTests
{
  internal static async Task RunOpeningBalanceBrowserAsync(string evidenceDirectory, CancellationToken cancellationToken)
  {
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_R06_BROWSER_OPT_IN") != "local-synthetic-only")
      throw new InvalidOperationException("Explicit local synthetic browser opt-in is required.");
    var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AUDITSPHERE_TEST_CONNECTION") ?? "");
    if (connection.Host != "127.0.0.1" || connection.Port != 5433 || connection.Database != "auditsphere_tests")
      throw new InvalidOperationException("Only the isolated loopback5433 auditsphere_tests root is permitted.");
    evidenceDirectory = Path.GetFullPath(evidenceDirectory);
    Directory.CreateDirectory(evidenceDirectory);
    var ready = Path.Combine(evidenceDirectory, "browser-ready.json");
    var prepared = Path.Combine(evidenceDirectory, "browser-prepared.signal");
    var finish = Path.Combine(evidenceDirectory, "browser-finish.signal");
    if (File.Exists(ready) || File.Exists(prepared) || File.Exists(finish)) throw new InvalidOperationException("Use a fresh owned evidence directory.");
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var preparer = world.Primary.Preparer;
    var reviewer = world.Reviewer;
    string[] initialBlockers;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, world.Primary.EngagementId, cancellationToken);
      Assert.True(completion.Succeeded, completion.Message);
      initialBlockers = completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray();
      Assert.Contains("audit-program-not-adopted", initialBlockers);
    }
    StandaloneApiApplicationFactory BrowserFactory(AppUser user)
    {
      var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
      {
        ["contentRoot"] = "/Users/qts/Repos/AuditSphere/src/AuditSphereOps.Api",
        ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
        ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = user.Subject,
        ["DevelopmentIdentity:TenantId"] = user.TenantId,
        ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false",
        ["AngularUi:Enabled"] = "true", ["AngularUi:CanonicalRoutes"] = "true",
        ["AngularUi:BuildPath"] = "/Users/qts/Repos/AuditSphere/src/AuditSphereOps.Ui/dist/auditsphere-ui/browser"
      });
      factory.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
      return factory;
    }
    await using var preparerHost = BrowserFactory(await UserAsync(pg, preparer.UserId));
    await using var reviewerHost = BrowserFactory(await UserAsync(pg, reviewer.UserId));
    using var preparerClient = preparerHost.CreateClient(new() { AllowAutoRedirect = false });
    using var reviewerClient = reviewerHost.CreateClient(new() { AllowAutoRedirect = false });
    static string Origin(StandaloneApiApplicationFactory factory)
    {
      var address = factory.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
      var uri = new Uri(address);
      if (uri.Host != "127.0.0.1" || uri.Port <= 0) throw new InvalidOperationException("Expected a bound IPv4 loopback origin.");
      return uri.GetLeftPart(UriPartial.Authority);
    }
    foreach (var host in new[] { preparerHost, reviewerHost })
    {
      Assert.Equal("Test", host.Services.GetRequiredService<Microsoft.Extensions.Hosting.IHostEnvironment>().EnvironmentName);
      Assert.Equal("false", host.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["ExternalEffects:Enabled"]);
    }
    // This bounded retry proves native review only. Preparation uses the existing supported
    // HTTP owner and its own cookie container, not browser cookies or raw database state.
    preparerClient.BaseAddress = new Uri(Origin(preparerHost));
    reviewerClient.BaseAddress = new Uri(Origin(reviewerHost));
    var preparerCsrf = await SignIn(preparerClient);
    var root = $"/api/ui/engagements/{world.Primary.EngagementId:D}/completion/opening-balances";
    var preparationReceipts = new List<OpeningBalanceCommandReceiptView>();
    foreach (var (date, agreed) in new[] { (new DateOnly(2026, 1, 1), "990.123456"), (new DateOnly(2026, 1, 2), "1000.123456") })
    {
      using var stateResponse = await preparerClient.GetAsync(root + $"/state?asOfDate={date:yyyy-MM-dd}", cancellationToken);
      Assert.Equal(HttpStatusCode.OK, stateResponse.StatusCode);
      var state = (await stateResponse.Content.ReadFromJsonAsync<OpeningBalanceCommandState>(cancellationToken))!;
      Assert.Equal("0", state.ExpectedRevision); Assert.Null(state.Record);
      var fields = new OpeningBalanceFields(null, null, "Synthetic prior signed statements", date, "QAR",
        "1000.123456", agreed, true, "Synthetic comparison of prior evidence to opening figures.", ["synthetic-opening-source"]);
      var request = new OpeningBalanceCommandRequest(Guid.CreateVersion7(), state.ExpectedRevision, state.ReviewBasis, fields);
      using var previewResponse = await Post(preparerClient, preparerCsrf, root + "/prepare/preview", request);
      Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
      using var previewJson = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync(cancellationToken));
      var preview = previewJson.RootElement.GetProperty("value").Deserialize<OpeningBalanceCommandPreview>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
      Assert.True(preview.CanProceed, string.Join(";", preview.Blockers));
      using var prepareResponse = await Post(preparerClient, preparerCsrf, root + "/prepare",
        request with { ExpectedRequestHash = preview.RequestHash, Reviewed = true });
      Assert.Equal(HttpStatusCode.OK, prepareResponse.StatusCode);
      using var receiptJson = JsonDocument.Parse(await prepareResponse.Content.ReadAsStringAsync(cancellationToken));
      preparationReceipts.Add(receiptJson.RootElement.GetProperty("value").Deserialize<OpeningBalanceCommandReceiptView>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
    }
    await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "api-preparation-receipts.json"),
      JsonSerializer.Serialize(preparationReceipts), cancellationToken);
    await File.WriteAllTextAsync(prepared, "Supported HTTP preparation completed; not native preparation proof.", cancellationToken);
    var path = $"/app/engagements/{world.Primary.EngagementId:D}/completion";
    await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new
    {
      preparerOrigin = Origin(preparerHost), reviewerOrigin = new UriBuilder(Origin(reviewerHost)) { Host = "localhost" }.Uri.GetLeftPart(UriPartial.Authority), path,
      proofScope = "Supported HTTP preparation followed by native independent review; separate from earlier native preparation evidence",
      engagementId = world.Primary.EngagementId, preparerId = preparer.UserId, reviewerId = reviewer.UserId,
      unresolvedDate = "2026-01-01", resolvedDate = "2026-01-02", currency = "QAR",
      openingSignedTotal = "1000.123456", unresolvedAgreedSignedTotal = "990.123456", resolvedAgreedSignedTotal = "1000.123456",
      priorReference = "Synthetic prior signed statements", evidenceReference = "synthetic-opening-source",
      rationale = "Synthetic comparison of prior evidence to opening figures.", initialBlockers,
      environment = "Test; supported prerequisites only; external effects off", expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
    }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    Console.WriteLine("R06_BROWSER_READY " + ready);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(30));
    while (!File.Exists(prepared)) await Task.Delay(250, timeout.Token);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rows = await db.OpeningBalanceVerifications.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(2, rows.Count);
      var unresolved = Assert.Single(rows, x => x.AsOfDate == new DateOnly(2026, 1, 1));
      var resolved = Assert.Single(rows, x => x.AsOfDate == new DateOnly(2026, 1, 2));
      foreach (var row in rows)
      {
        Assert.Equal(preparer.UserId, row.RecordedByUserId); Assert.Null(row.ReviewedByUserId); Assert.Null(row.ReviewedAt); Assert.Null(row.ReviewedBasisRevision);
        Assert.Null(row.ProcedureId); Assert.Null(row.PriorPeriodId); Assert.Equal("QAR", row.Currency); Assert.Equal(1, row.Revision);
        Assert.Equal(1000.123456m, row.OpeningSignedTotal); Assert.True(row.AccountingPoliciesConsistent);
        Assert.Equal("Synthetic prior signed statements", row.PriorReference);
        Assert.Equal(new[] { "synthetic-opening-source" }, JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson));
      }
      Assert.Equal(10m, unresolved.DifferenceAmount); Assert.Equal(OpeningBalanceVerificationConclusions.DifferencesUnresolved, unresolved.Conclusion);
      Assert.Equal(0m, resolved.DifferenceAmount); Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, resolved.Conclusion);
      Assert.False(OpeningBalanceReviewCurrentness.IsCurrent(unresolved)); Assert.False(OpeningBalanceReviewCurrentness.IsCurrent(resolved));
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "browser-preparation-verified.json"), JsonSerializer.Serialize(new
      { unresolvedId = unresolved.Id, resolvedId = resolved.Id, unresolved.Revision }), timeout.Token);
    }
    while (!File.Exists(finish)) await Task.Delay(250, timeout.Token);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rows = await db.OpeningBalanceVerifications.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(2, rows.Count);
      var unresolved = Assert.Single(rows, x => x.AsOfDate == new DateOnly(2026, 1, 1));
      Assert.Equal(1, unresolved.Revision); Assert.Null(unresolved.ReviewedByUserId); Assert.False(OpeningBalanceReviewCurrentness.IsCurrent(unresolved));
      var reviewed = Assert.Single(rows, x => x.AsOfDate == new DateOnly(2026, 1, 2));
      Assert.Equal(reviewer.UserId, reviewed.ReviewedByUserId); Assert.Equal(1, reviewed.ReviewedBasisRevision); Assert.Equal(2, reviewed.Revision);
      Assert.True(OpeningBalanceReviewCurrentness.IsCurrent(reviewed)); Assert.NotEqual(reviewed.RecordedByUserId, reviewed.ReviewedByUserId);
      var receipts = await db.OpeningBalanceCommandReceipts.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(3, receipts.Count); Assert.Equal(2, receipts.Count(x => x.Action == "PREPARE")); Assert.Single(receipts, x => x.Action == "REVIEW");
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, world.Primary.EngagementId, timeout.Token);
      Assert.True(completion.Succeeded, completion.Message); Assert.Equal(initialBlockers, completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray());
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "browser-final-verified.json"), JsonSerializer.Serialize(new
      { unresolvedId = unresolved.Id, unresolvedRevision = unresolved.Revision, reviewedId = reviewed.Id, reviewed.Revision,
        reviewed.ReviewedBasisRevision, reviewed.RecordedByUserId, reviewed.ReviewedByUserId,
        receipts = receipts.Select(x => new { x.Id, x.RequestId, x.ActorId, x.Action, x.RequestHash }), completion.Value.Blockers }), timeout.Token);
    }
    Console.WriteLine("R06_BROWSER_VERIFIED");
  }
}
