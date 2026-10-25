using System.Text;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularPbcStaffJourneyTests
{
  [Fact]
  [Trait("CaseId", "ANGULAR-PBC-STAFF-INBOX")]
  [Trait("CompletionUnit", "WU-039")]
  public async Task StaffCreatedRequest_CarriesClientUploadThroughTransferAndIndependentReview()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false, caseId: "ANGULAR-PBC-STAFF-INBOX");
    var fixture = host.Fixture;
    await using (var db = host.CreateDbContext())
    {
      var engagement = await db.Engagements.SingleAsync(x => x.Id == fixture.EngagementId);
      engagement.ServiceRoute = "FinancialStatementAudit";
      engagement.PeriodStart = "2026-01-01";
      engagement.PeriodEnd = "2026-12-31";
      await db.SaveChangesAsync();
    }
    var uploadBytes = Encoding.UTF8.GetBytes("Synthetic PBC bytes for the Angular staff completion journey.");
    const string fileName = "staged-bank-statements.txt";
    var uploadSha256 = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(uploadBytes);

    var origin = await host.StartApiForIdentityAsync(fixture.Staff,
      new Dictionary<string, string>
      {
        ["AngularUi__Enabled"] = "true",
        ["Storage__PbcStagingRoot"] = host.StagingRoot
      });
    var clientOrigin = await host.StartApiForIdentityAsync(fixture.Client,
      new Dictionary<string, string>
      {
        ["AngularUi__Enabled"] = "true",
        ["Storage__PbcStagingRoot"] = host.StagingRoot
      });
    try
    {
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
      await Assertions.Expect(page.GetByText("1 requests · 0 upload intents · 0 received uploads", new() { Exact = true }))
        .ToBeVisibleAsync();

      const string description = "Angular staff journey: November bank reconciliation statements";
      await page.GetByLabel("Files required", new() { Exact = true }).FillAsync(description);
      await page.Locator("select[name='owner']").SelectOptionAsync(fixture.Client.Id.ToString());
      await page.Locator("select[name='reviewer']").SelectOptionAsync(fixture.Reviewer.Id.ToString());
      await page.GetByLabel("Due date", new() { Exact = true }).FillAsync("2027-01-31");
      await page.GetByLabel("Preferred format (optional)", new() { Exact = true }).FillAsync("CSV");
      await page.GetByRole(AriaRole.Button, new() { Name = "Send file request", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText("Request opened and client email queued.", new() { Exact = true }))
        .ToBeVisibleAsync();

      var newRequestCard = page.Locator("section.panel").Filter(new() { HasText = description });
      await Assertions.Expect(newRequestCard).ToBeVisibleAsync();
      await Assertions.Expect(newRequestCard).ToContainTextAsync("Email queued to client");
      await Assertions.Expect(newRequestCard).ToContainTextAsync(description);

      Guid createdRequestId;
      await using (var db = host.CreateDbContext())
      {
        var request = await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Objective == description);
        createdRequestId = request.Id;
        Assert.Equal((PbcStates.Sent, 2L, fixture.Client.Id, fixture.Reviewer.Id, "2027-01-31"),
          (request.State, request.Revision, request.ClientOwnerUserId, request.ReviewerUserId, request.DueDate));
        var messages = await db.PbcCommunications.AsNoTracking().Where(x => x.PbcRequestId == request.Id).ToListAsync();
        Assert.Contains(messages, x => x.Kind == PbcCommunicationKinds.Request && x.Body == description);
        Assert.Contains(messages, x => x.Kind == PbcCommunicationKinds.Email &&
          x.DeliveryState == PbcDeliveryStates.Queued && x.RecipientEmail == fixture.Client.Email);
      }

      const string followUp = "Please include the bank reconciliation for November.";
      await newRequestCard.GetByLabel("Clarification or additional file request", new() { Exact = true }).FillAsync(followUp);
      await newRequestCard.GetByRole(AriaRole.Button, new() { Name = "Send request", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText("Additional request recorded and email queued.", new() { Exact = true }))
        .ToBeVisibleAsync();
      await Assertions.Expect(newRequestCard).ToContainTextAsync(followUp);
      await Assertions.Expect(newRequestCard).ToContainTextAsync("Staff — Staff");

      await using (var db = host.CreateDbContext())
      {
        var request = await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == createdRequestId);
        Assert.Equal(PbcStates.Sent, request.State);
        var followUpRows = await db.PbcCommunications.AsNoTracking().Where(x =>
          x.PbcRequestId == createdRequestId).ToListAsync();
        Assert.Contains(followUpRows, x => x.Kind == PbcCommunicationKinds.StaffMessage && x.Body == followUp);
        Assert.Contains(followUpRows, x => x.Kind == PbcCommunicationKinds.Email &&
          x.Body.StartsWith(followUp) && x.DeliveryState == PbcDeliveryStates.Queued &&
          x.RecipientEmail == fixture.Client.Email);
      }

      await using var clientContext = await browser.NewContextAsync();
      var clientPage = await clientContext.NewPageAsync();
      var clientErrors = new List<string>();
      clientPage.PageError += (_, error) => clientErrors.Add(error);
      await clientPage.GotoAsync(clientOrigin + "/auth/sign-in?returnUrl=%2Fui%2Fportal");
      await Assertions.Expect(clientPage.GetByRole(AriaRole.Heading,
        new() { Name = "Client portal", Exact = true })).ToBeVisibleAsync();
      var provideLink = clientPage.GetByRole(AriaRole.Link, new() { Name = description, Exact = false });
      await Assertions.Expect(provideLink).ToBeVisibleAsync();
      await provideLink.ClickAsync();
      var chooser = clientPage.GetByLabel("Choose a file (up to 250 MB)", new() { Exact = true });
      await chooser.SetInputFilesAsync(new FilePayload
      {
        Name = fileName,
        MimeType = "text/plain",
        Buffer = uploadBytes
      });
      await clientPage.GetByRole(AriaRole.Button, new() { Name = "Upload file", Exact = true }).ClickAsync();
      await Assertions.Expect(clientPage.GetByText(
        "File bytes staged. Your audit team must verify trusted completion and suitability before the request is received or accepted.",
        new() { Exact = true })).ToBeVisibleAsync();

      Guid uploadId;
      await using (var db = host.CreateDbContext())
      {
        var upload = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x =>
          x.PbcRequestId == createdRequestId && x.FileName == fileName);
        uploadId = upload.Id;
        Assert.Equal((PbcUploadStates.Chunking, uploadBytes.Length, uploadSha256),
          (upload.State, upload.ReceivedByteCount, upload.DeclaredSha256Hex));
        Assert.Single(await db.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == uploadId).ToListAsync());
      }

      await page.ReloadAsync();
      await Assertions.Expect(newRequestCard).ToContainTextAsync($"Client uploaded {fileName}");
      var stagedRequestCard = newRequestCard;
      await stagedRequestCard.GetByRole(AriaRole.Button,
        new() { Name = "Complete staged transfer", Exact = true }).ClickAsync();
      await Assertions.Expect(page.GetByText(
        "Staged bytes verified; a durable provider transfer is queued. The request is not received until that transfer completes.",
        new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(stagedRequestCard).ToContainTextAsync($"Client uploaded {fileName}");
      await Assertions.Expect(stagedRequestCard.GetByRole(AriaRole.Link, new() { Name = "Download", Exact = true }))
        .ToHaveAttributeAsync("href", $"/api/pbc/uploads/{uploadId:D}/download");

      await using (var db = host.CreateDbContext())
      {
        var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
        Assert.Equal((PbcUploadStates.Staged, uploadBytes.Length, uploadSha256),
          (intent.State, intent.ReceivedByteCount, intent.FinalSha256Hex));
        Assert.NotNull(intent.TransferOperationId);
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == intent.TransferOperationId);
        Assert.Equal((fixture.ClientId, fixture.EngagementId, uploadId),
          (operation.ClientId, operation.EngagementId, operation.TargetId));
        Assert.Equal((PbcDocumentTransferHandler.Kind, OperationState.PENDING),
          (operation.OperationKind, operation.Status));
      }

      await host.StartGeneralWorkerAsync();
      await host.WaitForReceivedAsync(uploadId);
      await page.ReloadAsync();
      var deliveredRequestCard = page.Locator("section.panel").Filter(new() { HasText = description });
      var deliveredFileRow = deliveredRequestCard.GetByRole(AriaRole.Row)
        .Filter(new() { HasText = fileName });
      await Assertions.Expect(deliveredFileRow.GetByText("received", new() { Exact = true })).ToBeVisibleAsync();
      await Assertions.Expect(deliveredFileRow.GetByText("completed", new() { Exact = true })).ToBeVisibleAsync();

      await using (var db = host.CreateDbContext())
      {
        var intent = await db.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
        Assert.Equal((PbcUploadStates.Received, uploadBytes.Length, uploadSha256),
          (intent.State, intent.ReceivedByteCount, intent.ProviderReceiptDigest));
        var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == intent.TransferOperationId);
        Assert.Equal(OperationState.COMPLETED, operation.Status);
        Assert.StartsWith("sim://pbc/", operation.ResultIdentity!);
        Assert.Equal(uploadSha256, operation.ResultDigest);
      }

      var browserDownload = await page.RunAndWaitForDownloadAsync(() =>
        page.GetByRole(AriaRole.Link, new() { Name = "Download", Exact = true }).ClickAsync());
      await using (var stream = await browserDownload.CreateReadStreamAsync())
      using (var browserBytes = new MemoryStream())
      {
        await stream.CopyToAsync(browserBytes);
        Assert.Equal(uploadBytes, browserBytes.ToArray());
      }

      await using var download = await context.APIRequest.GetAsync(origin + $"/api/pbc/uploads/{uploadId:D}/download",
        new() { MaxRedirects = 0 });
      Assert.Equal(200, download.Status);
      Assert.Equal("no-store", download.Headers["cache-control"]);
      Assert.Equal("nosniff", download.Headers["x-content-type-options"]);
      Assert.Equal(uploadBytes, await download.BodyAsync());

      var reviewerOrigin = await host.StartReviewerApiAsync();
      await using var reviewerContext = await browser.NewContextAsync();
      var reviewerPage = await reviewerContext.NewPageAsync();
      var reviewerErrors = new List<string>();
      reviewerPage.PageError += (_, error) => reviewerErrors.Add(error);
      var reviewerPath = $"/ui/app/engagements/{fixture.EngagementId:D}/pbc";
      await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(reviewerPath));
      await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
        new() { Name = "Prepared-by-client requests", Exact = true })).ToBeVisibleAsync();

      var reviewCard = reviewerPage.Locator("section.panel").Filter(new() { HasText = description });
      await Assertions.Expect(reviewCard.GetByText($"Client uploaded {fileName}", new() { Exact = true }))
        .ToBeVisibleAsync();
      await reviewCard.GetByRole(AriaRole.Button,
        new() { Name = "Begin assigned review", Exact = true }).ClickAsync();
      var acceptButton = reviewCard.GetByRole(AriaRole.Button,
        new() { Name = "Accept this exact version", Exact = true });
      await Assertions.Expect(acceptButton).ToBeDisabledAsync();
      await reviewCard.GetByLabel(
        "I reviewed this provider-verified submission and its content SHA-256.", new() { Exact = true }).CheckAsync();
      await Assertions.Expect(acceptButton).ToBeEnabledAsync();
      await acceptButton.ClickAsync();
      await Assertions.Expect(reviewerPage.GetByText(
        "The exact provider-verified submission version was accepted.", new() { Exact = true })).ToBeVisibleAsync();

      var acceptedEvidence = reviewCard.GetByRole(AriaRole.Region,
        new() { Name = "Accepted submission version", Exact = true });
      await Assertions.Expect(acceptedEvidence).ToContainTextAsync(fileName);
      await Assertions.Expect(acceptedEvidence).ToContainTextAsync(uploadSha256);
      await Assertions.Expect(acceptedEvidence).ToContainTextAsync(fixture.Reviewer.Id.ToString());
      await using (var db = host.CreateDbContext())
      {
        var request = await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == createdRequestId);
        var receipt = await db.PbcAcceptanceReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == createdRequestId);
        Assert.Equal(PbcStates.Accepted, request.State);
        Assert.Equal((uploadId, fixture.Reviewer.Id, uploadSha256),
          (receipt.PbcUploadIntentId, receipt.ReviewerUserId, receipt.ContentSha256Hex));
      }

      Assert.Empty(errors);
      Assert.Empty(clientErrors);
      Assert.Empty(reviewerErrors);
    }
    finally
    {
      PbcSeed.DeleteDirectory(host.StagingRoot);
    }
  }
}
