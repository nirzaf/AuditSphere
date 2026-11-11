using System.Net;
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
public static class SubsequentEventBrowserHarness
{
  public static Task RunAsync(string evidenceDirectory, CancellationToken cancellationToken) =>
    CompletionVarianceCommandsApiTests.RunSubsequentEventBrowserAsync(evidenceDirectory, cancellationToken);
}

public sealed partial class CompletionVarianceCommandsApiTests
{
  internal static async Task RunSubsequentEventBrowserAsync(string evidenceDirectory, CancellationToken cancellationToken)
  {
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_R05_BROWSER_OPT_IN") != "local-synthetic-only")
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
    var path = $"/app/engagements/{world.Primary.EngagementId:D}/completion";
    await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new
    {
      preparerOrigin = Origin(preparerHost),
      reviewerOrigin = Origin(reviewerHost), path,
      engagementId = world.Primary.EngagementId, preparerId = preparer.UserId, reviewerId = reviewer.UserId,
      periodEndDate = "2026-12-31", eventDate = "2027-01-20",
      pendingDescription = "Synthetic pending subsequent event", eligibleDescription = "Synthetic classified subsequent event",
      currency = "QAR", financialEffect = "250000.123456", disclosureReference = "Note 27",
      rationale = "Synthetic conditions arose after the reporting date.", initialBlockers,
      environment = "Test; synthetic supported prerequisites; external effects off", expiresAt = DateTimeOffset.UtcNow.AddMinutes(30)
    }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    Console.WriteLine("R05_BROWSER_READY " + ready);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(30));
    while (!File.Exists(prepared)) await Task.Delay(250, timeout.Token);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rows = await db.SubsequentEventReviews.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(2, rows.Count);
      var pending = Assert.Single(rows, x => x.Description == "Synthetic pending subsequent event");
      var eligible = Assert.Single(rows, x => x.Description == "Synthetic classified subsequent event");
      Assert.NotEqual(pending.Id, eligible.Id);
      foreach (var row in rows)
      {
        Assert.Equal(preparer.UserId, row.RecordedByUserId); Assert.Null(row.ReviewedByUserId); Assert.Null(row.ReviewedAt); Assert.Null(row.ReviewedRevision);
        Assert.Equal(new DateOnly(2026, 12, 31), row.PeriodEndDate); Assert.Equal(new DateOnly(2027, 1, 20), row.EventDate);
        Assert.Null(row.ProcedureId); Assert.Equal("[]", row.EvidenceReferencesJson); Assert.Equal("QAR", row.Currency); Assert.Equal(1, row.Revision);
      }
      Assert.Equal(SubsequentEventClassifications.PendingAssessment, pending.Classification); Assert.Null(pending.FinancialEffect); Assert.Null(pending.Rationale);
      Assert.False(pending.AdjustmentRequired); Assert.False(pending.DisclosureRequired); Assert.Null(pending.DisclosureReference);
      Assert.Equal(SubsequentEventClassifications.NonAdjusting, eligible.Classification); Assert.Equal(250000.123456m, eligible.FinancialEffect);
      Assert.False(eligible.AdjustmentRequired); Assert.True(eligible.DisclosureRequired); Assert.Equal("Note 27", eligible.DisclosureReference);
      Assert.Equal("Synthetic conditions arose after the reporting date.", eligible.Rationale);
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, world.Primary.EngagementId, timeout.Token);
      Assert.True(completion.Succeeded, completion.Message); Assert.Equal(initialBlockers, completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray());
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "browser-preparation-verified.json"), JsonSerializer.Serialize(new
      { pendingId = pending.Id, eligibleId = eligible.Id, pending.Revision, completion.Value.Blockers }), timeout.Token);
    }
    while (!File.Exists(finish)) await Task.Delay(250, timeout.Token);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rows = await db.SubsequentEventReviews.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(2, rows.Count);
      var pending = Assert.Single(rows, x => x.Description == "Synthetic pending subsequent event");
      Assert.Null(pending.ReviewedByUserId); Assert.Null(pending.ReviewedRevision); Assert.Equal(1, pending.Revision);
      var row = Assert.Single(rows, x => x.Description == "Synthetic classified subsequent event");
      Assert.Equal(reviewer.UserId, row.ReviewedByUserId); Assert.NotNull(row.ReviewedAt); Assert.Equal(1, row.ReviewedRevision); Assert.Equal(2, row.Revision);
      Assert.NotEqual(row.RecordedByUserId, row.ReviewedByUserId);
      var receipts = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(3, receipts.Count); Assert.Equal(2, receipts.Count(x => x.Action == "PREPARE")); Assert.Single(receipts, x => x.Action == "REVIEW");
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, world.Primary.EngagementId, timeout.Token);
      Assert.True(completion.Succeeded, completion.Message); Assert.Equal(initialBlockers, completion.Value!.Blockers.Order(StringComparer.Ordinal).ToArray());
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "browser-final-verified.json"), JsonSerializer.Serialize(new
      { pendingId = pending.Id, pendingRevision = pending.Revision, eventId = row.Id, row.Revision, row.ReviewedRevision, row.RecordedByUserId, row.ReviewedByUserId,
        receipts = receipts.Select(x => new { x.Id, x.RequestId, x.ActorId, x.Action, x.RequestHash }), completion.Value.Blockers }), timeout.Token);
    }
    Console.WriteLine("R05_BROWSER_VERIFIED");
  }
}
