using System.IO.Compression;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

/// <summary>
/// The Engagement Partner generates the Summary Review Memorandum, records clearance, selects a qualified opinion
/// (the focus and basis fields appear only then), generates and shares the reports; client management comments on
/// and acknowledges the representation letter in the portal.
/// </summary>
[Trait("Category", "AuditCompletion")]
public sealed class CompletionDeliverablesJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-STE-COMPLETION-01")]
  public async Task PartnerClearsDecidesAndIssues_ClientReviewsInThePortal()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "AS-STE-COMPLETION-01");
    var f = host.Fixture;
    var partner = PbcSeed.User(f.FirmId, "Staff"); partner.DisplayName = "Pat Partner";
    var manager = PbcSeed.User(f.FirmId, "Staff"); manager.DisplayName = "Mona Manager";
    var now = DateTimeOffset.UtcNow;
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(partner, manager);
      db.RoleGrants.AddRange(PbcSeed.Grant(f.FirmId, partner, "Partner"), PbcSeed.Grant(f.FirmId, manager, "Manager"));
      await db.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
      db.GoingConcernAssessments.Add(new GoingConcernAssessment { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, AssessmentDate = new DateOnly(2027, 2, 1),
        PeriodCoveredTo = new DateOnly(2028, 3, 31), ForecastReviewOutcome = "Reviewed", DisclosureAdequate = true, Conclusion = GoingConcernConclusions.NoMaterialUncertainty, Rationale = "Forecast reviewed",
        Currency = "QAR", RecordedByUserId = manager.Id, ReviewedByUserId = partner.Id, RecordedAt = now, ReviewedAt = now });
      db.AnalyticalReviewVarianceInvestigations.Add(new AnalyticalReviewVarianceInvestigation { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        AccountArea = "Revenue", PeriodReference = "FY2026", ExpectedAmount = 100m, ActualAmount = 101m, DifferenceAmount = 1m, InvestigationThreshold = 10m,
        Conclusion = VarianceInvestigationConclusions.Explained, Currency = "QAR", RecordedByUserId = manager.Id, ReviewedByUserId = partner.Id, RecordedAt = now, ReviewedAt = now });
      var versionId = Guid.NewGuid();
      db.AuditProgramVersions.Add(new AuditProgramVersion { Id = versionId, FirmId = f.FirmId, Version = "2026.1", SourceHash = Hashing.Sha256Hex("p"), Status = AuditProgramStatuses.Published,
        CreatedByUserId = partner.Id, ApprovedByUserId = partner.Id, CreatedAt = now, ApprovedAt = now });
      db.Findings.Add(new Finding { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ActorId = manager.Id, FindingType = "Control deficiency",
        ImpactDescription = "Credit notes approved without review", ManagementResponse = "Workflow introduced.", CreatedAt = now });
      await db.SaveChangesAsync();
      var programId = Guid.NewGuid();
      db.EngagementAuditPrograms.Add(new EngagementAuditProgram { Id = programId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ProgramVersionId = versionId, AdoptedByUserId = partner.Id, AdoptedAt = now });
      var procedure = new AuditProcedure { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, EngagementProgramId = programId, SourceProcedureId = "REV-01",
        SourceSectionTitle = "Revenue", Title = "Revenue cut-off", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Reviewed, CurrentResultRevision = 1, CreatedAt = now };
      var resultRow = new AuditProcedureResult { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, AuditProcedureId = procedure.Id, Revision = 1, InputGeneration = 1,
        WorkPerformed = "Cut-off tested.", StructuredResultJson = "{}", Conclusion = "No exceptions.", Status = AuditProcedureResultStatuses.Reviewed, PreparedByUserId = f.Staff.Id,
        ReviewedByUserId = manager.Id, SubmittedAt = now, ReviewedAt = now };
      db.AuditProcedures.Add(procedure);
      db.AuditProcedureResults.Add(resultRow);
      db.AuditProcedureReviews.Add(new AuditProcedureReview { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, AuditProcedureResultId = resultRow.Id,
        AuditProcedureId = procedure.Id, ResultRevision = 1, Decision = AuditProcedureReviewDecisions.Reviewed, ReviewerUserId = manager.Id, CreatedAt = now });
      db.StaffCertifications.Add(new StaffCertification { Id = Guid.NewGuid(), FirmId = f.FirmId, UserId = partner.Id, Name = "ACCA", RecordedAt = now, RecordedByUserId = partner.Id });
      await db.SaveChangesAsync();
      await CompletionTestFixtures.SeedTaxonomyAsync(db, f.FirmId, partner.Id);
    }
    await using (var db = host.CreateDbContext())
      Assert.True((await StaffingService.AssignAsync(db, new ActorContext(f.Admin.Id, f.FirmId, f.Admin.SessionEpoch, ["Administrator"]),
        new(f.EngagementId, partner.Id, StaffingLevels.EngagementPartner))).Succeeded);

    var origin = await host.StartWebForIdentityAsync(partner);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await (await browser.NewContextAsync()).NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    page.Console += (_, message) => diagnostics.Add($"console-{message.Type}: {message.Text}");
    async Task SettleAsync()
    {
      await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
      await page.WaitForTimeoutAsync(500);
    }
    var result = page.Locator(".command-result").Last;

    await page.GotoAsync($"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString($"/app/engagements/{f.EngagementId:D}/completion")}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Review, opinion and deliverables" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();

    // The opinion needs a current clearance first.
    await page.GetByRole(AriaRole.Button, new() { Name = "Record opinion (Engagement Partner)" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Partner clearance of a current Summary Review Memorandum is required");

    await page.Locator("#srm-recommendations").FillAsync("All procedures reviewed; no unadjusted differences.");
    await page.Locator("#srm-recommendations").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Generate Summary Review Memorandum" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Summary Review Memorandum generated.");
    await Assertions.Expect(page.GetByText("Latest SRM v1 is current", new() { Exact = false })).ToBeVisibleAsync();
    await page.Locator("#clr-risks").FillAsync("Revenue and inventory reviewed.");
    await page.Locator("#clr-notes").FillAsync("Notes 1 to 14 reviewed.");
    await page.Locator("#clr-notes").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Record Partner clearance" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Partner clearance recorded.");

    // Conditional fields: focus and basis appear only for a qualified opinion.
    await Assertions.Expect(page.Locator("#op-focus")).ToHaveCountAsync(0);
    await page.Locator("#op-type").SelectOptionAsync(AuditOpinionTypes.Qualified);
    await page.Locator("#op-focus").SelectOptionAsync(new SelectOptionValue { Label = "INV — Inventory" });
    await page.Locator("#op-basis").FillAsync("We were unable to observe the inventory count at 31 December 2026.");
    await page.Locator("#op-basis").PressAsync("Tab");
    await page.GetByRole(AriaRole.Button, new() { Name = "Record opinion (Engagement Partner)" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Qualified opinion recorded.");
    await Assertions.Expect(page.GetByText("Current opinion: Qualified — Inventory", new() { Exact = false })).ToBeVisibleAsync();

    foreach (var title in new[] { "Independent Auditor's Report", "Management Representation Letter" })
    {
      await page.GetByRole(AriaRole.Button, new() { Name = $"Generate {title}" }).ClickAsync();
      await Assertions.Expect(result).ToContainTextAsync($"{title} generated.");
    }
    var table = page.Locator("[aria-label='Generated deliverables']");
    await Assertions.Expect(table).ToContainTextAsync("Independent Auditor's Report");
    var download = await page.RunAndWaitForDownloadAsync(() => table.GetByRole(AriaRole.Link, new() { Name = "Independent Auditor's Report" }).ClickAsync());
    Assert.EndsWith(".docx", download.SuggestedFilename);
    await page.GetByRole(AriaRole.Button, new() { Name = "Share Management Representation Letter v1 with the client" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Shared with client management.");

    // Client management reviews in the portal.
    var clientOrigin = await host.StartWebForIdentityAsync(f.Client);
    var clientPage = await (await browser.NewContextAsync()).NewPageAsync();
    clientPage.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    await clientPage.GotoAsync($"{clientOrigin}/auth/sign-in?returnUrl={Uri.EscapeDataString("/portal")}");
    await clientPage.GetByRole(AriaRole.Heading, new() { Name = "Documents for your review" }).WaitForAsync(new() { Timeout = 20000 });
    await clientPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 15000 });
    await clientPage.WaitForTimeoutAsync(500);
    var letter = clientPage.Locator("[data-document='Management Representation Letter']");
    await Assertions.Expect(letter).ToBeVisibleAsync();
    await clientPage.GetByRole(AriaRole.Button, new() { Name = "Acknowledge Management Representation Letter" }).ClickAsync();
    await Assertions.Expect(clientPage.GetByText("Acknowledged. The exact version you reviewed is recorded.")).ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var ack = await db.ClientDeliverableReviews.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId);
      var doc = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == ack.DeliverableId);
      Assert.Equal((doc.ContentSha256, f.Client.Id), (ack.AcknowledgedSha256, ack.AcknowledgedByUserId!.Value));
    }

    await clientPage.GetByLabel("Management signatory", new() { Exact = true }).FillAsync("Authorized Executive Management");
    await clientPage.GetByLabel("Management signatory", new() { Exact = true }).PressAsync("Tab");
    await clientPage.GetByLabel("Upload management-signed representation letter").SetInputFilesAsync(new FilePayload { Name = "signed-representation.pdf", MimeType = "application/pdf", Buffer = CompletionTestFixtures.SignedPdf() });
    await Assertions.Expect(clientPage.GetByText("Signed PDF uploaded for this exact letter version", new() { Exact = false })).ToBeVisibleAsync();

    // Back on the Partner's page: register a PNG signature, sign, and the 60-day freeze is scheduled and traced.
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/completion");
    await page.GetByRole(AriaRole.Heading, new() { Name = "File freeze and activity trail" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Management-signed LOR v1", Exact = false })).ToBeVisibleAsync();
    await page.GetByLabel("Signature verification evidence", new() { Exact = true }).FillAsync("Reviewed the management signature, authority and all representation pages.");
    await page.GetByLabel("I reviewed this exact scan and verified management authority, signature and completeness.").CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Verify signed representation (Partner)" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Signed representation verified");
    await page.Locator("#firm-seal-file").SetInputFilesAsync(new FilePayload { Name = "firm-seal.png", MimeType = "image/png", Buffer = Png(100, 100) });
    await Assertions.Expect(result).ToContainTextAsync("Approved firm seal registered");
    await page.Locator("#signature-file").SetInputFilesAsync(new FilePayload { Name = "signature.png", MimeType = "image/png", Buffer = Png(160, 50) });
    await Assertions.Expect(result).ToContainTextAsync("Signature specimen registered.");
    await page.GetByRole(AriaRole.Button, new() { Name = "Sign with registered signature" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Report signed");
    await Assertions.Expect(page.Locator("[aria-label='Generated deliverables']")).ToContainTextAsync("signed");
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/completion");
    await page.GetByRole(AriaRole.Heading, new() { Name = "File freeze and activity trail" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await Assertions.Expect(page.GetByText("day(s) remaining", new() { Exact = false })).ToBeVisibleAsync();
    await Assertions.Expect(page.Locator("[aria-label='Activity trail']")).ToContainTextAsync("Independent Auditor's Report v1 signed");
    await using (var db = host.CreateDbContext())
      Assert.Equal(FileFreezeStates.Scheduled, (await db.EngagementFileFreezes.AsNoTracking().SingleAsync(x => x.EngagementId == f.EngagementId)).State);
    // The bundle control stays blocked until real reviewed release and posted balance evidence exists.
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Assemble five-part bundle (Partner)" })).ToBeDisabledAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Generate Management Letter", Exact = true }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Management Letter generated.");
    var accountingReviewer = PbcSeed.User(f.FirmId, "Staff");
    var financeReviewer = PbcSeed.User(f.FirmId, "Staff");
    await using (var db = host.CreateDbContext())
    { db.Users.AddRange(accountingReviewer, financeReviewer); await db.SaveChangesAsync(); }
    var financialScope = new CompletionBundleScope(f.FirmId, f.ClientId, f.EngagementId, new()
    { ["associate"] = f.Staff, ["senior"] = accountingReviewer, ["partner"] = partner, ["partner2"] = financeReviewer, ["manager"] = manager });
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(host.DbOptions, financialScope);
    await CompletionBundleFixture.SeedPostedFeeAsync(host.DbOptions, financialScope);
    await page.GotoAsync($"{origin}/app/engagements/{f.EngagementId:D}/completion");
    await page.GetByRole(AriaRole.Heading, new() { Name = "File freeze and activity trail" }).WaitForAsync(new() { Timeout = 20000 });
    await SettleAsync();
    await page.GetByLabel("I reviewed the exact released financial statements for certification with this report.").CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Assemble five-part bundle (Partner)" }).ClickAsync();
    await Assertions.Expect(result).ToContainTextAsync("Five-part final bundle assembled");
    var bundleDownload = await page.RunAndWaitForDownloadAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "Download five-part final bundle" }).ClickAsync());
    Assert.EndsWith(".zip", bundleDownload.SuggestedFilename);
    await clientPage.ReloadAsync();
    await clientPage.GetByRole(AriaRole.Link, new() { Name = "Download five-part final bundle" }).WaitForAsync(new() { Timeout = 20000 });
    var clientDownload = await clientPage.RunAndWaitForDownloadAsync(() => clientPage.GetByRole(AriaRole.Link, new() { Name = "Download five-part final bundle" }).ClickAsync());
    Assert.EndsWith(".zip", clientDownload.SuggestedFilename);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal) || x.Contains("unhandled exception", StringComparison.OrdinalIgnoreCase));
  }

  /// <summary>A small, valid grayscale PNG.</summary>
  private static byte[] Png(int width, int height)
  {
    static byte[] Chunk(string type, byte[] data)
    {
      var typeBytes = Encoding.ASCII.GetBytes(type);
      var crc = Crc32([.. typeBytes, .. data]);
      return [.. BigEndian(data.Length), .. typeBytes, .. data, .. BigEndian((int)crc)];
    }
    static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    static uint Crc32(byte[] bytes)
    {
      var crc = 0xFFFFFFFFu;
      foreach (var b in bytes) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
      return ~crc;
    }
    byte[] header = [.. BigEndian(width), .. BigEndian(height), 8, 0, 0, 0, 0];
    var raw = new MemoryStream();
    for (var y = 0; y < height; y++) { raw.WriteByte(0); for (var x = 0; x < width; x++) raw.WriteByte((byte)(x == y ? 0 : 255)); }
    var compressed = new MemoryStream();
    using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.ToArray());
    return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", header), .. Chunk("IDAT", compressed.ToArray()), .. Chunk("IEND", [])];
  }
}
