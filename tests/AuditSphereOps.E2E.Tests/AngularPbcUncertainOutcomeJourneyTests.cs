using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPbcUncertainOutcomeJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-PBC-UNCERTAIN-RECOVERY")]
  public async Task StaffInbox_ShowsUncertainProviderOutcome_ThenReconcilesExactUpload()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "ANGULAR-PBC-UNCERTAIN-RECOVERY");
    var fixture = host.Fixture;
    var fileBytes = "%PDF-1.7 synthetic provider recovery fixture"u8.ToArray();
    var staged = await PbcSeed.StageUploadAsync(host.Database, fixture,
      PbcSeed.Actor(fixture.Client, "ClientUser"), host.RequestId, fileBytes,
      "uncertain-provider-response.pdf", "application/pdf");

    try
    {
      var origin = await host.StartApiForIdentityAsync(fixture.Staff,
        new Dictionary<string, string>
        {
          ["AngularUi__Enabled"] = "true",
          ["Storage__PbcStagingRoot"] = staged.StagingRoot
        });
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      var errors = new List<string>();
      page.PageError += (_, error) => errors.Add(error);

      var path = $"/ui/app/engagements/{fixture.EngagementId:D}/pbc";
      await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
      await Assertions.Expect(page.GetByRole(AriaRole.Heading,
        new() { Name = "Prepared-by-client requests", Exact = true })).ToBeVisibleAsync();
      var requestCard = page.Locator("section.panel").Filter(new() { HasText = "Bank statements" });
      var uploadRow = requestCard.GetByRole(AriaRole.Row)
        .Filter(new() { HasText = "uncertain-provider-response.pdf" });
      await Assertions.Expect(uploadRow.GetByText("chunking", new() { Exact = true })).ToBeVisibleAsync();
      foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
      {
        await page.SetViewportSizeAsync(width, 900);
        var overflow = await page.EvaluateAsync<int>(
          "() => document.documentElement.scrollWidth - window.innerWidth");
        Assert.True(overflow <= 1, $"PBC inbox overflows by {overflow}px at {width}px.");
        var recipientBounds = await page.Locator("select[name='owner']").BoundingBoxAsync();
        Assert.NotNull(recipientBounds);
        Assert.True(recipientBounds.X + recipientBounds.Width <= width + 1,
          $"PBC recipient selector extends outside the {width}px viewport.");
      }
      await page.SetViewportSizeAsync(1280, 800);
      await uploadRow.GetByRole(AriaRole.Button,
        new() { Name = "Complete staged transfer", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText(
        "Staged bytes verified; a durable provider transfer is queued. The request is not received until that transfer completes.",
        new() { Exact = true })).ToBeVisibleAsync();

      var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(host.DbOptions));
      var store = new PostgresOperationStore(factory);
      var provider = new PbcSeed.ScriptedSink();
      provider.Bind(new SimulationPbcProviderSink(Path.Combine(host.RunRoot, "provider")));
      provider.OnUpload(async plan =>
      {
        await provider.Inner!.UploadAsync(plan, CancellationToken.None);
        throw new IOException("Synthetic lost provider response after remote commit.");
      });
      var handler = new PbcDocumentTransferHandler(factory, provider);
      var options = new WorkerOptions(fixture.FirmId, "Test", AllowSimulationAdapters: true);
      var worker = new AuditSphereOps.Worker.Worker(
        new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
        [new PbcTransferDiscovery(factory, store, handler, options)],
        NullLogger<AuditSphereOps.Worker.Worker>.Instance);

      Assert.True(await worker.ProcessNextAsync());
      await using (var uncertain = host.CreateDbContext())
      {
        var intent = await uncertain.PbcUploadIntents.AsNoTracking()
          .SingleAsync(x => x.Id == staged.UploadIntentId);
        var operation = await uncertain.DurableOperations.AsNoTracking()
          .SingleAsync(x => x.Id == intent.TransferOperationId);
        Assert.Equal(PbcUploadStates.Staged, intent.State);
        Assert.Equal(OperationState.RESULT_UNCERTAIN, operation.Status);
        var request = await uncertain.PbcRequests.AsNoTracking()
          .SingleAsync(x => x.Id == host.RequestId);
        Assert.Equal(PbcStates.PartiallyReceived, request.State);
        await uncertain.Database.ExecuteSqlRawAsync(
          "UPDATE durable_operations SET next_attempt_at = statement_timestamp() WHERE id = {0}", operation.Id);
      }

      await page.ReloadAsync();
      uploadRow = page.Locator("section.panel").Filter(new() { HasText = "Bank statements" })
        .GetByRole(AriaRole.Row).Filter(new() { HasText = "uncertain-provider-response.pdf" });
      await Assertions.Expect(uploadRow.GetByText("staged", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(uploadRow.GetByText("result uncertain",
        new() { Exact = true })).ToBeVisibleAsync();

      Assert.True(await worker.ProcessNextAsync());
      await using (var received = host.CreateDbContext())
      {
        var intent = await received.PbcUploadIntents.AsNoTracking()
          .SingleAsync(x => x.Id == staged.UploadIntentId);
        var operation = await received.DurableOperations.AsNoTracking()
          .SingleAsync(x => x.Id == intent.TransferOperationId);
        Assert.Equal((PbcUploadStates.Received, staged.DeclaredSha256Hex),
          (intent.State, intent.ProviderReceiptDigest));
        Assert.Equal(OperationState.COMPLETED, operation.Status);
        Assert.Single(Directory.EnumerateFiles(Path.Combine(host.RunRoot, "provider")));
      }

      await page.ReloadAsync();
      uploadRow = page.Locator("section.panel").Filter(new() { HasText = "Bank statements" })
        .GetByRole(AriaRole.Row).Filter(new() { HasText = "uncertain-provider-response.pdf" });
      await Assertions.Expect(uploadRow.GetByText("received", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(uploadRow.GetByText("completed",
        new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(uploadRow.GetByRole(AriaRole.Link,
        new() { Name = "Download", Exact = true })).ToBeVisibleAsync();
      Assert.Empty(errors);
    }
    finally
    {
      PbcSeed.DeleteDirectory(staged.StagingRoot);
    }
  }
}
