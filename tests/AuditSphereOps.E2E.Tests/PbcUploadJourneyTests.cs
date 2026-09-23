using System.Security.Cryptography;
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

[Trait("Category", "ClientDocuments")]
public sealed class PbcUploadJourneyTests
{
  [Fact]
  [Trait("CaseId", "PROP-E2E-06")]
  public async Task ClientUpload_ReconcilesUncertainProviderResult_AndStaffDownloadsExactBytes()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var clientContext = await browser.NewContextAsync();
    await using var staffContext = await browser.NewContextAsync();
    var clientPage = await clientContext.NewPageAsync();
    var staffPage = await staffContext.NewPageAsync();
    var browserDiagnostics = new List<string>();
    var clientCircuitConnected = WaitForCircuitConnectionAsync(clientPage, browserDiagnostics);
    var fileBytes = "%PDF-1.7 synthetic PBC acceptance fixture"u8.ToArray();
    var digest = Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();

    await clientPage.GotoAsync(SignInUrl(host.ClientUrl,
      $"/portal/requests/{host.RequestId:D}"));
    await clientPage.GetByRole(AriaRole.Heading, new() { Name = "PBC request" }).WaitForAsync();
    await clientCircuitConnected;
    await clientPage.Locator("#content-hash").FillAsync(digest);
    await clientPage.WaitForFunctionAsync(
      "() => document.querySelector('#content-hash-state')?.textContent === 'SHA-256 digest entered.'");
    var clientReconnected = WaitForCircuitConnectionAsync(clientPage, browserDiagnostics);
    await clientPage.ReloadAsync();
    await clientPage.GetByRole(AriaRole.Heading, new() { Name = "PBC request" }).WaitForAsync();
    await clientReconnected;
    await clientPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await clientPage.WaitForFunctionAsync(
      "() => document.querySelector('#content-hash-state')?.textContent === 'SHA-256 digest entered.'");
    var fileInputId = $"pbc-file-{host.RequestId:N}";
    await clientPage.Locator($"#{fileInputId}").SetInputFilesAsync(new FilePayload
    {
      Name = "AuditSphere-E2E-Synthetic-PBC.pdf",
      MimeType = "application/pdf",
      Buffer = fileBytes
    });
    var selectedFile = await clientPage.EvaluateAsync<string?>(
      "inputId => window.auditSpherePbc.readFileMetadata(inputId)?.name ?? null", fileInputId);
    Assert.Equal("AuditSphere-E2E-Synthetic-PBC.pdf", selectedFile);
    await clientPage.GetByRole(AriaRole.Button, new() { Name = "Prepare upload" }).ClickAsync();
    var commandResult = clientPage.Locator(".command-result");
    await commandResult.WaitForAsync();
    var commandMessage = await commandResult.InnerTextAsync();
    Assert.True(commandMessage.StartsWith("Transfer intent created;", StringComparison.Ordinal),
      $"{commandMessage}\n{string.Join("\n", browserDiagnostics)}");
    await clientPage.GetByText($"Staged {fileBytes.Length} bytes in 1 chunk(s); trusted completion is still required.")
      .WaitForAsync(new() { Timeout = 10000 });

    Guid uploadId;
    await using (var db = host.CreateDbContext())
    {
      var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == host.RequestId);
      Assert.Equal(PbcUploadStates.Chunking, intent.State);
      Assert.Equal(fileBytes.Length, intent.ReceivedByteCount);
      uploadId = intent.Id;
    }

    var staffCircuitConnected = WaitForCircuitConnectionAsync(staffPage, browserDiagnostics);
    await staffPage.GotoAsync(SignInUrl(host.StaffUrl,
      $"/app/engagements/{host.Fixture.EngagementId:D}/pbc"));
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Prepared-by-client requests" }).WaitForAsync();
    await staffCircuitConnected;
    await staffPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var completeButton = staffPage.GetByRole(AriaRole.Button, new() { Name = "Complete staged transfer" });
    Assert.True(await completeButton.IsEnabledAsync());
    await completeButton.ClickAsync(new() { Timeout = 5000 });
    var staffCommandResult = staffPage.Locator(".command-result");
    try { await staffCommandResult.WaitForAsync(new() { Timeout = 10000 }); }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Staff completion produced no status. URL: {staffPage.Url}\n" +
        $"Body: {await staffPage.Locator("body").InnerTextAsync()}\n{string.Join("\n", browserDiagnostics)}\n{ex.Message}");
    }
    var staffCommandMessage = await staffCommandResult.InnerTextAsync();
    Assert.StartsWith("Staged bytes verified;", staffCommandMessage);

    var contextFactory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(host.DbOptions));
    var store = new PostgresOperationStore(contextFactory);
    var simulatedProvider = new SimulationPbcProviderSink(Path.Combine(host.RunRoot, "provider"));
    var interruptedProvider = new PbcSeed.ScriptedSink();
    interruptedProvider.Bind(simulatedProvider);
    interruptedProvider.OnUpload(async plan =>
    {
      await interruptedProvider.Inner!.UploadAsync(plan, CancellationToken.None);
      throw new IOException("Synthetic lost provider response after commit.");
    });
    var handler = new PbcDocumentTransferHandler(contextFactory, interruptedProvider);
    var options = new WorkerOptions(host.Fixture.FirmId, "Test", AllowSimulationAdapters: true);
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new PbcTransferDiscovery(contextFactory, store, handler, options)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);

    Assert.True(await worker.ProcessNextAsync());
    await using (var uncertain = host.CreateDbContext())
    {
      var intent = await uncertain.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
      var operation = await uncertain.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == intent.TransferOperationId);
      Assert.Equal(PbcUploadStates.Staged, intent.State);
      Assert.Equal(OperationState.RESULT_UNCERTAIN, operation.Status);
      await uncertain.Database.ExecuteSqlRawAsync(
        "UPDATE durable_operations SET next_attempt_at = statement_timestamp() WHERE id = {0}", operation.Id);
    }

    await staffPage.ReloadAsync();
    await staffPage.GetByText("Intent state").WaitForAsync();
    Assert.Contains(PbcUploadStates.Staged, await staffPage.Locator("body").InnerTextAsync());
    Assert.True(await worker.ProcessNextAsync());
    await host.WaitForReceivedAsync(uploadId);
    await using (var received = host.CreateDbContext())
    {
      var intent = await received.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
      Assert.Equal(PbcUploadStates.Received, intent.State);
      Assert.Equal(digest, intent.ProviderReceiptDigest);
      Assert.Single(Directory.EnumerateFiles(Path.Combine(host.RunRoot, "provider")));
    }

    await staffPage.ReloadAsync();
    await staffPage.GetByRole(AriaRole.Link, new() { Name = "Download" }).WaitForAsync();
    var download = await staffPage.RunAndWaitForDownloadAsync(() =>
      staffPage.GetByRole(AriaRole.Link, new() { Name = "Download" }).ClickAsync());
    await using var stream = await download.CreateReadStreamAsync();
    using var downloaded = new MemoryStream();
    await stream.CopyToAsync(downloaded);
    Assert.Equal(fileBytes, downloaded.ToArray());

    var clientDownloadStatus = await clientPage.EvaluateAsync<int>(
      "async path => (await fetch(path)).status", $"/api/pbc/uploads/{uploadId:D}/download");
    Assert.Equal(403, clientDownloadStatus);
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task WaitForCircuitConnectionAsync(IPage page, List<string> diagnostics)
  {
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      diagnostics.Add($"console/{message.Type}: {message.Text}");
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal))
        connected.TrySetResult();
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    page.RequestFailed += (_, request) => diagnostics.Add($"request-failed: {request.Method} {request.Url} {request.Failure}");
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
  }
}
