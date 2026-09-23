using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using PdfSharp.Pdf.IO;

namespace AuditSphereOps.E2E.Tests;

[Trait("Category", "AccountingAndReporting")]
public sealed class FinancialArtifactJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-JOURNAL-STALE-01")]
  public async Task JournalPageRemovesReviewerActionAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-JOURNAL-STALE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    Guid journalId;
    const string journalNumber = "SYN-PAR-002-REVOKED-JOURNAL";
    const string privateAccountCode = "SYN-PRIVATE-REVOCATION-LINE";
    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      var plan = await db.AdjustmentPlans.AsNoTracking().SingleAsync(x => x.Id == package.AdjustmentPlanId);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingPreparer",
          host.Fixture.ClientId, host.Fixture.EngagementId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingReviewer",
          host.Fixture.ClientId, host.Fixture.EngagementId));
      await db.SaveChangesAsync();
      var draft = await AdjustmentJournalService.CreateDraftAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingPreparer"), plan.BaseDatasetId, journalNumber,
        [(privateAccountCode, 250m, 0m), ("SYN-PRIVATE-OFFSET-LINE", 0m, 250m)],
        purpose: AdjustmentJournalPurposes.ReportingAdjustment,
        reason: "Synthetic access-revocation regression", evidenceReference: "synthetic-test-evidence");
      Assert.True(draft.Succeeded, draft.Message);
      journalId = draft.Value;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/journals/{journalId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = journalNumber }).WaitForAsync();
    await page.GetByText(privateAccountCode, new() { Exact = true }).WaitForAsync();
    await connected;

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == host.Fixture.Staff.Id &&
        x.Role == "AccountingReviewer" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await page.GetByText("You must have the AccountingReviewer, Partner or Manager role", new() { Exact = false }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains(journalNumber, body);
    Assert.Contains(privateAccountCode, body);
    Assert.Contains("SYN-PRIVATE-OFFSET-LINE", body);
    await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Post adjustment journal" })).ToBeHiddenAsync();
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    await using (var db = host.CreateDbContext())
    {
      var stalePost = await AdjustmentJournalService.PostAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingReviewer"), journalId);
      Assert.Equal(ErrorCodes.GenerationStale, stalePost.ErrorCode);
    }
    await using var verify = host.CreateDbContext();
    Assert.Equal("Draft", (await verify.AdjustmentJournals.AsNoTracking().SingleAsync(x => x.Id == journalId)).Status);
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-01")]
  public async Task ReviewedAdjustmentFlowsFromTrialBalanceIntoTheBrowserPackage()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-01");
    var (packageId, _) = await CreatePackageAsync(host);
    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      var plan = await db.AdjustmentPlans.AsNoTracking().SingleAsync(x => x.Id == package.AdjustmentPlanId);
      var sourceRevenue = await db.TrialBalanceRows.AsNoTracking().Where(x =>
        x.DatasetId == plan.BaseDatasetId && x.AccountCode == "4000").Select(x => x.Amount).SingleAsync();
      Assert.Equal(180_000m, -sourceRevenue);
      Assert.Equal(1, plan.AppliedJournalCount);
      Assert.Equal(-175_000m, await db.FinancialPackageLines.AsNoTracking().Where(x =>
        x.FinancialPackageId == packageId && x.SourceAccountCode == "4000").SumAsync(x => x.Amount));
      Assert.Equal(AccountingPackageStates.PackageValidated, package.Status);
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await connected;
    await page.GetByRole(AriaRole.Heading, new() { Name = "Mapped statement totals" }).WaitForAsync();
    await page.GetByRole(AriaRole.Row, new() { Name = "INCOME -175,000.00 QAR" }).WaitForAsync();
    Assert.Contains("Inputs validated", await page.Locator("body").InnerTextAsync());
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-10")]
  public async Task ReopenedPeriodShowsNewRevisionWithoutReplacingTheValidatedPackage()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-10");
    var (packageId, expected) = await CreatePackageAsync(host);
    Guid periodId;
    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      periodId = package.PeriodId ?? throw new Xunit.Sdk.XunitException("The synthetic package lost its reporting period.");
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingReviewer", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "Partner", host.Fixture.ClientId));
      await db.SaveChangesAsync();
    }

    await using (var db = host.CreateDbContext())
    {
      Assert.True((await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "synthetic-management-review", "Synthetic acceptance fixture."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "synthetic-accounting-review", "Synthetic acceptance fixture."))).Succeeded);
      Assert.True((await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "Partner"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "synthetic-partner-review", "Synthetic acceptance fixture."))).Succeeded);
      var close = await ClientAccountingService.ClosePeriodAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer"), periodId, "Synthetic initial close");
      Assert.True(close.Succeeded, close.Message);
      Assert.True((await ClientAccountingService.ReopenPeriodAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "Partner"), periodId, "Synthetic controlled correction")).Succeeded);
    }

    await using (var db = host.CreateDbContext())
    {
      var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
      Assert.Equal(AccountingWorkflowStates.Draft, period.Status);
      Assert.Equal(3, period.Revision);
      Assert.Equal(1, await db.FinancialPackages.CountAsync(x => x.PeriodId == periodId));
      Assert.Equal(3, await db.FinancialPackageReviewDecisions.CountAsync(x => x.FinancialPackageId == packageId));
      var artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.ArtifactVersion == FinancialPackageArtifactVersions.Pdf);
      Assert.Equal(expected[FinancialPackageArtifactVersions.Pdf].Bytes, artifact.ArtifactBytes);
      Assert.Equal(expected[FinancialPackageArtifactVersions.Pdf].Sha256, artifact.ArtifactSha256Hex);
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/periods/{periodId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Accounting period" }).WaitForAsync();
    await connected;
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains("FY2026-E2E", body);
    Assert.Contains("DRAFT", body);
    Assert.Contains("3", body);
    Assert.Equal($"/app/accounting/packages/{packageId:D}",
      await page.GetByRole(AriaRole.Link, new() { Name = "Open package" }).GetAttributeAsync("href"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-03")]
  public async Task PreparerCannotRecordAccountingReviewButAssignedReviewerCan()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-03");
    var (packageId, _) = await CreatePackageAsync(host);
    var route = $"/app/accounting/packages/{packageId:D}";
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);

    var preparerPage = await browser.NewPageAsync();
    var preparerConnected = WaitForCircuitConnectionAsync(preparerPage, []);
    await preparerPage.GotoAsync(SignInUrl(host.StaffUrl, route));
    await preparerPage.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await preparerConnected;
    await preparerPage.GetByLabel("Stage").SelectOptionAsync(FinancialPackageReviewStages.AccountingReview);
    await preparerPage.GetByLabel("Evidence reference").FillAsync("Synthetic preparer must not self-review");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Record decision" }).ClickAsync();
    await Assertions.Expect(preparerPage.Locator(".command-result")).ToContainTextAsync("Blocked:");
    await preparerPage.CloseAsync();

    await using (var db = host.CreateDbContext())
      Assert.Empty(await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync());

    var reviewerUrl = await host.StartReviewerWebAsync();
    var reviewerPage = await browser.NewPageAsync();
    var reviewerDiagnostics = new List<string>();
    var reviewerConnected = WaitForCircuitConnectionAsync(reviewerPage, reviewerDiagnostics);
    await reviewerPage.GotoAsync(SignInUrl(reviewerUrl, route));
    await reviewerPage.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await reviewerConnected;
    await reviewerPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await reviewerPage.GetByLabel("Stage").SelectOptionAsync(FinancialPackageReviewStages.AccountingReview);
    await reviewerPage.GetByLabel("Evidence reference").FillAsync("Synthetic independent reviewer sign-off");
    var recordButton = reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Record decision" });
    Assert.True(await recordButton.IsEnabledAsync());
    await recordButton.ClickAsync();
    try { await reviewerPage.Locator(".command-result").WaitForAsync(new() { Timeout = 10000 }); }
    catch (TimeoutException ex)
    {
      throw new Xunit.Sdk.XunitException($"Reviewer action produced no command status.\n{string.Join("\n", reviewerDiagnostics)}\n{await reviewerPage.Locator("body").InnerTextAsync()}\n{ex.Message}");
    }
    Assert.Contains("Decision recorded", await reviewerPage.Locator(".command-result").InnerTextAsync());

    await using var verify = host.CreateDbContext();
    var decision = await verify.FinancialPackageReviewDecisions.AsNoTracking()
      .SingleAsync(x => x.FinancialPackageId == packageId);
    Assert.Equal(host.Fixture.Reviewer.Id, decision.DecidedByUserId);
    Assert.Equal(FinancialPackageReviewStages.AccountingReview, decision.Stage);
    Assert.Equal(FinancialPackageReviewDecisions.Approved, decision.Decision);
    await reviewerPage.CloseAsync();
  }

  [Fact]
  [Trait("CaseId", "PROP-E2E-08")]
  public async Task StaffDownloadsExactPersistedWorkbookWordAndPdfArtifacts()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false, caseId: "PROP-E2E-08");
    var (packageId, expected) = await CreatePackageAsync(host);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await connected;
    await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    var body = await page.Locator("body").InnerTextAsync();
    Assert.Contains(packageId.ToString("D"), body);
    Assert.Contains("Inputs validated", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    foreach (var (label, version, extension) in new[]
    {
      ("Download workbook", FinancialPackageArtifactVersions.Workbook, ".xlsx"),
      ("Download Word document", FinancialPackageArtifactVersions.Word, ".docx"),
      ("Download PDF", FinancialPackageArtifactVersions.Pdf, ".pdf")
    })
    {
      var download = await page.RunAndWaitForDownloadAsync(() =>
        page.GetByRole(AriaRole.Button, new() { Name = label }).ClickAsync());
      Assert.EndsWith(extension, download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
      await using var stream = await download.CreateReadStreamAsync();
      using var file = new MemoryStream();
      await stream.CopyToAsync(file);
      var bytes = file.ToArray();
      Assert.Equal(expected[version].Bytes, bytes);
      Assert.Equal(expected[version].Sha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
      await using var db = host.CreateDbContext();
      var stored = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.ArtifactVersion == version);
      Assert.Equal(bytes, stored.ArtifactBytes);
      Assert.Equal(expected[version].Sha256, stored.ArtifactSha256Hex);
      Assert.Contains($"{extension[1..].ToUpperInvariant()} download started", await page.Locator("[role=status]").Last.InnerTextAsync());
      AssertInactiveArtifact(version, bytes);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FS-STALE-DOWNLOAD-01")]
  public async Task RevokedPackageGrantCannotDownloadCachedArtifact()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FS-STALE-DOWNLOAD-01");
    var (packageId, _) = await CreatePackageAsync(host);
    byte[] originalBytes;
    string artifactHash;
    await using (var db = host.CreateDbContext())
    {
      var artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.ArtifactVersion == FinancialPackageArtifactVersions.Text);
      originalBytes = artifact.ArtifactBytes;
      artifactHash = artifact.ArtifactSha256Hex;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await connected;
    var initialBody = await page.Locator("body").InnerTextAsync();
    if (!initialBody.Contains("Mapped statement totals", StringComparison.Ordinal) ||
        !initialBody.Contains(artifactHash, StringComparison.Ordinal))
      throw new Xunit.Sdk.XunitException($"The package artifact did not render for the fixture.\n{initialBody}\n{string.Join("\n", diagnostics)}");
    await page.EvaluateAsync("""
      () => {
      window.__syntheticArtifactDownloadCalls = 0;
      window.auditSphereExports.downloadText = () => window.__syntheticArtifactDownloadCalls++;
      }
      """);

    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null &&
        (x.Role == "Staff" || x.Role == "AccountingPreparer")).ToListAsync();
      Assert.Equal(2, grants.Count);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(artifactHash, body);
    Assert.DoesNotContain("Mapped statement totals", body);
    Assert.Equal(0, await page.EvaluateAsync<int>("window.__syntheticArtifactDownloadCalls"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));

    await using (var db = host.CreateDbContext())
    {
      var denied = await FinancialStatementService.GetStoredPackageArtifactAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"), packageId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, denied.ErrorCode);
      Assert.Null(denied.Value);
    }

    await using var verify = host.CreateDbContext();
    var retained = await verify.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
      x.FinancialPackageId == packageId && x.ArtifactVersion == FinancialPackageArtifactVersions.Text);
    Assert.Equal(originalBytes, retained.ArtifactBytes);
    Assert.Equal(artifactHash, retained.ArtifactSha256Hex);
  }

  internal static async Task<(Guid PackageId, Dictionary<string, (byte[] Bytes, string Sha256)> Expected)> CreatePackageAsync(
    OwnedBlazorHost host)
  {
    var firmId = host.Fixture.FirmId;
    var clientId = host.Fixture.ClientId;
    var engagementId = host.Fixture.EngagementId;
    var preparer = PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer");
    var reviewer = PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer");
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var taxonomyId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.AddRange(
        PbcSeed.Grant(firmId, host.Fixture.Staff, "AccountingPreparer", clientId, engagementId),
        PbcSeed.Grant(firmId, host.Fixture.Reviewer, "AccountingReviewer", clientId, engagementId));
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026-E2E",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
        Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
        Status = AccountingWorkflowStates.Active, CreatedByUserId = host.Fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
      {
        Id = taxonomyId, FirmId = firmId, Code = "tax-e2e-v1", Framework = "IFRS", Name = "Synthetic E2E taxonomy",
        Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
        CreatedByUserId = host.Fixture.Staff.Id, ApprovedByUserId = host.Fixture.Reviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ReportingTaxonomyNodes.AddRange(
        new ReportingTaxonomyNode
        {
          Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "CASH", Name = "Cash",
          StatementSection = "ASSETS", DisplaySign = "SIGNED", NormalBalance = "DEBIT", IsPosting = true,
          Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
        },
        new ReportingTaxonomyNode
        {
          Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = taxonomyId, Code = "REVENUE", Name = "Revenue",
          StatementSection = "INCOME", DisplaySign = "SIGNED", NormalBalance = "CREDIT", IsPosting = true,
          Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
        });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", Revision = 1,
        Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ControlTotal = 0m,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = host.Fixture.Staff.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
          Amount = 180_000m, Currency = "QAR", Entity = "SYNTHETIC"
        },
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000", AccountName = "Revenue",
          Amount = -180_000m, Currency = "QAR", Entity = "SYNTHETIC"
        });
      await db.SaveChangesAsync();
    }

    Guid mappingId;
    await using (var db = host.CreateDbContext())
    {
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(datasetId, "tax-e2e-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Synthetic cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Synthetic revenue mapping")
        ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      mappingId = mapping.Value;
      var approved = await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1);
      Assert.True(approved.Succeeded, approved.Message);
    }

    Guid planId;
    await using (var db = host.CreateDbContext())
    {
      var journal = await AdjustmentJournalService.CreateDraftAsync(db, preparer, datasetId, "AJ-E2E-001",
        [("1000", 0m, 5_000m), ("4000", 5_000m, 0m)]);
      Assert.True(journal.Succeeded, journal.Message);
      var posted = await AdjustmentJournalService.PostAsync(db, reviewer, journal.Value!);
      Assert.True(posted.Succeeded, posted.Message);
      var reconciled = await SourceReconciliationService.ResolveAsync(db, reviewer, datasetId,
        "AJ-E2E-001", 1, ReflectionStates.NotReflected, "Synthetic source-ledger review: adjustment not posted in client books.");
      Assert.True(reconciled.Succeeded, reconciled.Message);
      var plan = await AdjustmentPlanService.CreatePlanAsync(db, preparer, datasetId,
        [new PlanLineInput("AJ-E2E-001", 1)]);
      Assert.True(plan.Succeeded, plan.Message);
      planId = plan.Value;
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.True(finalized.Succeeded, finalized.Message);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "e2e-template-v1",
      new FinancialSupplementaryInformation(0m, 175_000m,
        [new CashFlowLineInput("OPERATING", "Synthetic cash collections", 175_000m)],
        [new DisclosureInput("E2E_POLICY", "Synthetic test policy note."),
         new DisclosureInput("E2E_OTHER", string.Empty, NotApplicable: true, Rationale: "No other synthetic items.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 175_000m, 0m, 0m, 0m, 175_000m, "e2e-equity")],
        NoteLines: [new NoteLineInput("E2E_NOTE", "CASH", 175_000m, "e2e-note")]));
    Guid packageId;
    var expected = new Dictionary<string, (byte[] Bytes, string Sha256)>(StringComparer.Ordinal);
    await using (var db = host.CreateDbContext())
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded, built.Message);
      Assert.Equal(AccountingPackageStates.PackageValidated, built.Value!.Status);
      packageId = built.Value.PackageId;
      var canonical = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(canonical.Succeeded, canonical.Message);
      foreach (var version in new[]
      {
        FinancialPackageArtifactVersions.Workbook,
        FinancialPackageArtifactVersions.Word,
        FinancialPackageArtifactVersions.Pdf
      })
      {
        var rendered = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, preparer, packageId, version);
        Assert.True(rendered.Succeeded, rendered.Message);
        expected.Add(version, (rendered.Value!.ArtifactBytes, rendered.Value.ArtifactSha256Hex));
      }
    }
    return (packageId, expected);
  }

  private static void AssertInactiveArtifact(string version, byte[] bytes)
  {
    if (version == FinancialPackageArtifactVersions.Pdf)
    {
      using var stream = new MemoryStream(bytes);
      using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
      Assert.NotEmpty(document.Pages);
      var pdf = Encoding.Latin1.GetString(bytes);
      Assert.DoesNotContain("/JavaScript", pdf, StringComparison.Ordinal);
      Assert.DoesNotContain("/OpenAction", pdf, StringComparison.Ordinal);
      Assert.DoesNotContain("/Launch", pdf, StringComparison.Ordinal);
      Assert.DoesNotContain("/EmbeddedFile", pdf, StringComparison.Ordinal);
      return;
    }

    using var package = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
    Assert.DoesNotContain(package.Entries, x => x.FullName.Contains("vbaProject.bin", StringComparison.OrdinalIgnoreCase));
    Assert.DoesNotContain(package.Entries, x => x.FullName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase));
    foreach (var relationship in package.Entries.Where(x => x.FullName.EndsWith(".rels", StringComparison.Ordinal)))
    {
      using var reader = new StreamReader(relationship.Open());
      Assert.DoesNotContain("TargetMode=\"External\"", reader.ReadToEnd(), StringComparison.OrdinalIgnoreCase);
    }
  }

  private static string SignInUrl(string origin, string returnUrl) =>
    $"{origin}/auth/sign-in?returnUrl={Uri.EscapeDataString(returnUrl)}";

  private static Task WaitForCircuitConnectionAsync(IPage page, List<string> diagnostics)
  {
    var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    page.Console += (_, message) =>
    {
      diagnostics.Add($"console/{message.Type}: {message.Text}");
      if (message.Text.Contains("WebSocket connected to ws://", StringComparison.Ordinal)) connected.TrySetResult();
    };
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    return connected.Task.WaitAsync(TimeSpan.FromSeconds(10));
  }
}
