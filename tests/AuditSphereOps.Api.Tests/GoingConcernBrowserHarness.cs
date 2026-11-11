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
public static class GoingConcernBrowserHarness
{
  public static Task RunAsync(string evidenceDirectory, CancellationToken cancellationToken) =>
    CompletionVarianceCommandsApiTests.RunGoingConcernBrowserAsync(evidenceDirectory, cancellationToken);
}

public sealed partial class CompletionVarianceCommandsApiTests
{
  internal static async Task RunGoingConcernBrowserAsync(string evidenceDirectory, CancellationToken cancellationToken)
  {
    if (Environment.GetEnvironmentVariable("AUDITSPHERE_R04_BROWSER_OPT_IN") != "local-synthetic-only")
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
    var programReviewer = await ProvisionFirmPartnerAsync(pg, world.Administrator, "r04-browser-program-reviewer");
    const string evidenceReference = "r04-supported-browser-procedure-evidence";
    Guid procedureId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await AuditProgramService.PrepareControlledAuditProgramAsync(db, world.Administrator,
        new("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(draft.Succeeded, draft.Message);
      var published = await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db, programReviewer,
        new(draft.Value!.ProgramVersionId, draft.Value.ContentDigest!, "Synthetic R04 controlled-program review"));
      Assert.True(published.Succeeded, published.Message);
      var adopted = await AuditProgramService.AdoptAsync(db, preparer,
        new(world.Primary.EngagementId, published.Value!.ProgramVersionId, AuditProgramCatalog.SourceHash, published.Value.ContentDigest));
      Assert.True(adopted.Succeeded, adopted.Message);
      await EstablishVariancePlanningBasisAsync(pg, world, preparer, reviewer, programReviewer);
      var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == world.Primary.EngagementId && x.SourceProcedureId == "AWP-01-01", cancellationToken);
      procedureId = procedure.Id;
      var applicability = await AuditProgramService.DecideApplicabilityAsync(db, preparer,
        new(procedureId, AuditApplicabilityActions.Applicable, "Synthetic current exact-engagement going-concern evidence.",
          procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.CreateVersion7()));
      Assert.True(applicability.Succeeded, applicability.Message);
      var procedureState = await AuditProcedureCommandWorkspace.StateAsync(db, preparer, procedureId);
      Assert.True(procedureState.Succeeded, procedureState.Message);
      Assert.True(procedureState.Value!.Basis.CanSubmit);
      var submitted = await AuditProgramService.SubmitResultAsync(db, preparer,
        new(procedureId, procedureState.Value.Basis.InputGeneration, "Inspected synthetic forecast support.",
          "{\"observation\":\"synthetic support inspected\"}", [evidenceReference], "Selected evidence inspected."));
      Assert.True(submitted.Succeeded, submitted.Message);
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
    var path = $"/app/engagements/{world.Primary.EngagementId:D}/completion";
    await File.WriteAllTextAsync(ready, JsonSerializer.Serialize(new
    {
      preparerOrigin = Origin(preparerHost),
      reviewerOrigin = Origin(reviewerHost), path,
      engagementId = world.Primary.EngagementId, preparerId = preparer.UserId, reviewerId = reviewer.UserId,
      assessmentDate = "2026-12-31", periodCoveredTo = "2027-06-30", procedureId, evidenceReference,
      forecastReviewOutcome = "Synthetic forecast inspected against current evidence.", materialUncertaintyIdentified = false,
      disclosureAdequate = true, currency = "QAR", rationale = "Synthetic browser assessment determination.",
      environment = "Test; synthetic supported prerequisites; external effects off", expiresAt = DateTimeOffset.UtcNow.AddMinutes(20)
    }, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    Console.WriteLine("R04_BROWSER_READY " + ready);
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromMinutes(20));
    while (!File.Exists(prepared)) await Task.Delay(250, timeout.Token);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rows = await db.GoingConcernAssessments.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      var row = Assert.Single(rows); Assert.Equal(preparer.UserId, row.RecordedByUserId);
      Assert.Null(row.ReviewedByUserId); Assert.Null(row.ReviewedAt); Assert.Null(row.ReviewedRevision);
      Assert.Equal(new DateOnly(2026, 12, 31), row.AssessmentDate); Assert.Equal(new DateOnly(2027, 6, 30), row.PeriodCoveredTo);
      Assert.Equal(procedureId, row.ProcedureId);
      Assert.Equal("Synthetic forecast inspected against current evidence.", row.ForecastReviewOutcome);
      Assert.False(row.MaterialUncertaintyIdentified); Assert.True(row.DisclosureAdequate);
      Assert.Equal("QAR", row.Currency); Assert.Equal("Synthetic browser assessment determination.", row.Rationale);
      Assert.Equal(new[] { evidenceReference }, JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson));
      Assert.Equal(GoingConcernConclusions.NoMaterialUncertainty, row.Conclusion);
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, programReviewer, world.Primary.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.Contains("going-concern:unreviewed", completion.Value!.Blockers);
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "browser-preparation-verified.json"), JsonSerializer.Serialize(new
      { assessmentId = row.Id, row.Revision, row.RecordedByUserId, row.ReviewedByUserId, row.PeriodCoveredTo, row.ForecastReviewOutcome, row.MaterialUncertaintyIdentified, row.DisclosureAdequate, row.Currency, row.Rationale, row.EvidenceReferencesJson, completion.Value.Blockers }), timeout.Token);
    }
    while (!File.Exists(finish)) await Task.Delay(250, timeout.Token);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var row = await db.GoingConcernAssessments.AsNoTracking().SingleAsync(x => x.EngagementId == world.Primary.EngagementId, timeout.Token);
      Assert.Equal(reviewer.UserId, row.ReviewedByUserId); Assert.NotNull(row.ReviewedAt);
      Assert.Equal(row.Revision - 1, row.ReviewedRevision); Assert.NotEqual(row.RecordedByUserId, row.ReviewedByUserId);
      var receipts = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().Where(x => x.EngagementId == world.Primary.EngagementId).ToListAsync(timeout.Token);
      Assert.Equal(2, receipts.Count); Assert.Single(receipts, x => x.Action == "PREPARE"); Assert.Single(receipts, x => x.Action == "REVIEW");
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, programReviewer, world.Primary.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.DoesNotContain("going-concern:missing", completion.Value!.Blockers); Assert.DoesNotContain("going-concern:unreviewed", completion.Value.Blockers);
      Assert.Contains("going-concern:horizon-under-12-months", completion.Value.Blockers); Assert.Contains("analytical-review:missing", completion.Value.Blockers);
      await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "browser-final-verified.json"), JsonSerializer.Serialize(new
      { assessmentId = row.Id, row.Revision, row.ReviewedRevision, row.RecordedByUserId, row.ReviewedByUserId, row.PeriodCoveredTo,
        receipts = receipts.Select(x => new { x.Id, x.RequestId, x.ActorId, x.Action, x.RequestHash }), completion.Value.Blockers }), timeout.Token);
    }
    Console.WriteLine("R04_BROWSER_VERIFIED");
  }
}
