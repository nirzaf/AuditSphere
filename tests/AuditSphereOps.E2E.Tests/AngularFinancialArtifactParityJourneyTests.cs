using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class AngularFinancialArtifactParityJourneyTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-FINANCIAL-PACKAGE-01")]
  public async Task PackageReviewDownloadsAndRevocationRemainExactAndScoped()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-FINANCIAL-PACKAGE-01");
    var (packageId, expectedOfficeArtifacts) = await FinancialPackageFixture.CreatePackageAsync(host);
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var staffOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, settings);

    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      var plan = await db.AdjustmentPlans.AsNoTracking().SingleAsync(x => x.Id == package.AdjustmentPlanId);
      var sourceRevenue = await db.TrialBalanceRows.AsNoTracking().Where(x =>
        x.DatasetId == plan.BaseDatasetId && x.AccountCode == "4000").Select(x => x.Amount).SingleAsync();
      var packagedRevenue = await db.FinancialPackageLines.AsNoTracking().Where(x =>
        x.FinancialPackageId == packageId && x.SourceAccountCode == "4000").SumAsync(x => x.Amount);
      Assert.Equal(-180_000m, sourceRevenue);
      Assert.Equal(1, plan.AppliedJournalCount);
      Assert.Equal(-175_000m, packagedRevenue);
      Assert.Equal(AccountingPackageStates.PackageValidated, package.Status);
    }

    byte[] expectedTextArtifact;
    string expectedTextHash;
    await using (var db = host.CreateDbContext())
    {
      var artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.ArtifactVersion == FinancialPackageArtifactVersions.Text);
      expectedTextArtifact = artifact.ArtifactBytes;
      expectedTextHash = artifact.ArtifactSha256Hex;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var staffContext = await browser.NewContextAsync(new() { AcceptDownloads = true });
    var staffPage = await staffContext.NewPageAsync();
    var diagnostics = new List<string>();
    staffPage.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var route = $"/ui/app/accounting/packages/{packageId:D}";
    await staffPage.GotoAsync(staffOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading,
      new() { Name = "Financial statement package", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading,
      new() { Name = "Mapped statement totals", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Row,
      new() { Name = "INCOME -175,000.00 QAR", Exact = true })).ToBeVisibleAsync();
    var packageToken = Guid.NewGuid().ToString("N");
    await staffPage.EvaluateAsync("token => window.__packageParityToken = token", packageToken);
    await NavigateSpaAsync(staffPage, $"/ui/app/accounting/packages/{Guid.NewGuid():D}");
    await Assertions.Expect(staffPage.GetByText("The package is not available in the current firm scope.",
      new() { Exact = true })).ToBeVisibleAsync();
    var unavailablePackageBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString("D"), unavailablePackageBody);
    Assert.DoesNotContain(expectedTextHash, unavailablePackageBody);
    Assert.DoesNotContain("Mapped statement totals", unavailablePackageBody);
    Assert.Equal(packageToken, await staffPage.EvaluateAsync<string>("window.__packageParityToken"));
    await NavigateSpaAsync(staffPage, route);
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Heading,
      new() { Name = "Mapped statement totals", Exact = true })).ToBeVisibleAsync();

    // Establish the required predecessor decision first so this exercises the preparer's authorization boundary.
    await using (var db = host.CreateDbContext())
    {
      var management = await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "synthetic-management-approval", "Management approved the exact package."));
      Assert.True(management.Succeeded, management.Message);
      var refused = await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "synthetic-preparer-attempt", "Preparers cannot record the accounting review."));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, refused.ErrorCode);
      Assert.Single(await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync());
    }

    // The Angular surface refuses the same attempted accounting review and exposes only a safe denial.
    await Assertions.Expect(staffPage.GetByRole(AriaRole.Button,
      new() { Name = "Record decision", Exact = true })).ToBeVisibleAsync();
    await staffPage.Locator("select[name='stage']").SelectOptionAsync(FinancialPackageReviewStages.AccountingReview);
    await staffPage.GetByLabel("Evidence reference", new() { Exact = true })
      .FillAsync("Synthetic preparer must not self-review");
    await staffPage.GetByRole(AriaRole.Button, new() { Name = "Record decision", Exact = true }).ClickAsync();
    await Assertions.Expect(staffPage.Locator(".command-result")).ToContainTextAsync("Access denied");
    await using (var db = host.CreateDbContext())
    {
      Assert.Single(await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync());
    }

    await using var reviewerContext = await browser.NewContextAsync();
    var reviewerPage = await reviewerContext.NewPageAsync();
    reviewerPage.PageError += (_, error) => diagnostics.Add($"reviewer-page-error: {error}");
    await reviewerPage.GotoAsync(reviewerOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(reviewerPage.GetByRole(AriaRole.Heading,
      new() { Name = "Financial statement package", Exact = true })).ToBeVisibleAsync();
    await reviewerPage.Locator("select[name='stage']").SelectOptionAsync(FinancialPackageReviewStages.AccountingReview);
    await reviewerPage.GetByLabel("Evidence reference", new() { Exact = true })
      .FillAsync("Synthetic independent reviewer sign-off");
    await reviewerPage.GetByRole(AriaRole.Button, new() { Name = "Record decision", Exact = true }).ClickAsync();
    await Assertions.Expect(reviewerPage.Locator(".command-result"))
      .ToContainTextAsync("Decision recorded against the current package version.");
    await using (var db = host.CreateDbContext())
    {
      var review = await db.FinancialPackageReviewDecisions.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.Stage == FinancialPackageReviewStages.AccountingReview);
      Assert.Equal(host.Fixture.Reviewer.Id, review.DecidedByUserId);
      Assert.Equal(FinancialPackageReviewDecisions.Approved, review.Decision);
      Assert.Equal(FinancialPackageArtifactVersions.Text, review.ArtifactVersion);
      Assert.Equal(2, await db.FinancialPackageReviewDecisions.CountAsync(x => x.FinancialPackageId == packageId));
    }

    var downloads = new[]
    {
      ("Download artifact", FinancialPackageArtifactVersions.Text, ".txt", expectedTextArtifact, expectedTextHash),
      ("Download workbook", FinancialPackageArtifactVersions.Workbook, ".xlsx", expectedOfficeArtifacts[FinancialPackageArtifactVersions.Workbook].Bytes, expectedOfficeArtifacts[FinancialPackageArtifactVersions.Workbook].Sha256),
      ("Download Word document", FinancialPackageArtifactVersions.Word, ".docx", expectedOfficeArtifacts[FinancialPackageArtifactVersions.Word].Bytes, expectedOfficeArtifacts[FinancialPackageArtifactVersions.Word].Sha256),
      ("Download PDF", FinancialPackageArtifactVersions.Pdf, ".pdf", expectedOfficeArtifacts[FinancialPackageArtifactVersions.Pdf].Bytes, expectedOfficeArtifacts[FinancialPackageArtifactVersions.Pdf].Sha256)
    };
    foreach (var (label, version, extension, expectedBytes, expectedHash) in downloads)
    {
      var download = await staffPage.RunAndWaitForDownloadAsync(() =>
        staffPage.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync());
      Assert.EndsWith(extension, download.SuggestedFilename, StringComparison.OrdinalIgnoreCase);
      await using var stream = await download.CreateReadStreamAsync();
      using var content = new MemoryStream();
      await stream.CopyToAsync(content);
      var bytes = content.ToArray();
      Assert.Equal(expectedBytes, bytes);
      Assert.Equal(expectedHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
      Assert.Contains("Artifact download started. Release and delivery remain separate controls.",
        await staffPage.GetByRole(AriaRole.Status).Last.InnerTextAsync());
      await using var db = host.CreateDbContext();
      var stored = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.ArtifactVersion == version);
      Assert.Equal(expectedHash, stored.ArtifactSha256Hex);
      Assert.Equal(expectedBytes, stored.ArtifactBytes);
    }

    await using (var db = host.CreateDbContext())
    {
      var grants = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId &&
        x.UserId == host.Fixture.Staff.Id && x.RevokedAt == null &&
        (x.Role == "Staff" || x.Role == "AccountingPreparer")).ToListAsync();
      Assert.NotEmpty(grants);
      foreach (var grant in grants)
      {
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
          PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
    }

    await staffPage.GetByRole(AriaRole.Button, new() { Name = "Refresh package", Exact = true }).ClickAsync();
    await staffPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    var deniedBody = await staffPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString("D"), deniedBody);
    Assert.DoesNotContain(expectedTextHash, deniedBody);
    Assert.DoesNotContain("Mapped statement totals", deniedBody);
    Assert.Equal(0, await staffPage.GetByRole(AriaRole.Button, new() { Name = "Download artifact", Exact = true }).CountAsync());
    var revokedDownload = await staffPage.EvaluateAsync<string>("""
      async input => {
        const response = await fetch(`/api/ui/accounting/packages/${input.packageId}/artifact`, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ artifactVersion: 'financial-package-text.v1' })
        });
        const body = await response.text();
        return `${response.status}|${response.headers.get('content-disposition') ?? ''}|${body.includes(input.packageHash)}`;
      }
      """, new { packageId = packageId.ToString("D"), packageHash = expectedTextHash });
    Assert.Equal("401||false", revokedDownload);
    await using (var db = host.CreateDbContext())
    {
      var denied = await FinancialStatementService.GetStoredPackageArtifactAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "Staff"), packageId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, denied.ErrorCode);
    }
    Assert.Empty(diagnostics);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-CLIENT-PACKAGE-01")]
  public async Task ClientManagementDecisionIsHashBoundAndRevocationClearsPackage()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-CLIENT-PACKAGE-01");
    var (packageId, _) = await FinancialPackageFixture.CreatePackageAsync(host);
    var client = PbcSeed.User(host.Fixture.FirmId, "Client");
    var refreshClient = PbcSeed.User(host.Fixture.FirmId, "Client");
    await using (var db = host.CreateDbContext())
    {
      db.Users.AddRange(client, refreshClient);
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, client, "ClientUser", host.Fixture.ClientId, host.Fixture.EngagementId),
        PbcSeed.Grant(host.Fixture.FirmId, refreshClient, "ClientUser", host.Fixture.ClientId, host.Fixture.EngagementId));
      await db.SaveChangesAsync();
    }
    var origin = await host.StartApiForIdentityAsync(client,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    var refreshOrigin = await host.StartApiForIdentityAsync(refreshClient,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    await using var refreshContext = await browser.NewContextAsync();
    var refreshPage = await refreshContext.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    refreshPage.PageError += (_, error) => diagnostics.Add($"refresh-page-error: {error}");
    var path = $"/ui/portal/accounting/packages/{packageId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await refreshPage.GotoAsync(refreshOrigin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Financial package", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(refreshPage.GetByRole(AriaRole.Heading,
      new() { Name = "Financial package", Exact = true })).ToBeVisibleAsync();
    var hash = await GetPackageHashAsync(host, packageId);
    await Assertions.Expect(page.GetByText(hash, new() { Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(refreshPage.GetByText(hash, new() { Exact = true })).ToBeVisibleAsync();
    var initialBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("Calculation hash", initialBody);
    Assert.DoesNotContain("Mapping lineage", initialBody);
    Assert.Contains("This is not an audit opinion or proof of ledger posting.",
      initialBody, StringComparison.OrdinalIgnoreCase);
    Assert.True(await page.GetByRole(AriaRole.Button, new() { Name = "Record decision", Exact = true }).IsDisabledAsync());
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await page.SetViewportSizeAsync(width, 900);
      Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Client financial package overflows the {width}px viewport.");
    }
    await page.SetViewportSizeAsync(1280, 900);

    await page.GetByLabel("Evidence reference", new() { Exact = true })
      .FillAsync("Synthetic client management approval evidence");
    await page.GetByLabel("Decision", new() { Exact = true })
      .SelectOptionAsync(FinancialPackageReviewDecisions.ChangesRequired);
    await page.GetByLabel("I reviewed this exact package and its SHA-256 identity.", new() { Exact = true }).CheckAsync();
    await page.GetByRole(AriaRole.Button, new() { Name = "Record decision", Exact = true }).ClickAsync();
    await Assertions.Expect(page.GetByText("Management decision recorded for this exact package.", new() { Exact = true }))
      .ToBeVisibleAsync();
    await using (var db = host.CreateDbContext())
    {
      var decision = await db.FinancialPackageReviewDecisions.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.Stage == FinancialPackageReviewStages.ManagementApproval);
      Assert.Equal(client.Id, decision.DecidedByUserId);
      Assert.Equal(FinancialPackageReviewDecisions.ChangesRequired, decision.Decision);
      Assert.Equal(FinancialPackageReviewEvidenceModes.SignedIn, decision.EvidenceMode);
      Assert.Equal(hash, decision.PackageHash);
    }

    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__clientPackageParityToken = token", documentToken);
    await NavigateSpaAsync(page, $"/ui/portal/accounting/packages/{Guid.NewGuid():D}");
    await Assertions.Expect(page.GetByText("This package is unavailable in your current client scope.", new() { Exact = true }))
      .ToBeVisibleAsync();
    var unavailable = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(hash, unavailable);
    Assert.DoesNotContain("Statement totals", unavailable);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientPackageParityToken"));
    await NavigateSpaAsync(page, path);
    await Assertions.Expect(page.GetByText(hash, new() { Exact = true })).ToBeVisibleAsync();
    await page.GetByLabel("Evidence reference", new() { Exact = true })
      .FillAsync("Synthetic post-revocation decision must be refused");
    await page.GetByLabel("I reviewed this exact package and its SHA-256 identity.", new() { Exact = true }).CheckAsync();

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == client.Id &&
        x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await page.GetByRole(AriaRole.Button, new() { Name = "Record decision", Exact = true }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    var revokedBody = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(hash, revokedBody);
    Assert.DoesNotContain("Statement totals", revokedBody);
    Assert.DoesNotContain("Record management decision", revokedBody);
    await using (var db = host.CreateDbContext())
    {
      var retainedDecision = await db.FinancialPackageReviewDecisions.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.Stage == FinancialPackageReviewStages.ManagementApproval);
      Assert.Equal(FinancialPackageReviewDecisions.ChangesRequired, retainedDecision.Decision);
      Assert.Equal("Synthetic client management approval evidence", retainedDecision.EvidenceReference);
    }

    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == refreshClient.Id &&
        x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    await refreshPage.GetByRole(AriaRole.Button, new() { Name = "Refresh package", Exact = true }).ClickAsync();
    await refreshPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    var refreshedRevokedBody = await refreshPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(hash, refreshedRevokedBody);
    Assert.DoesNotContain("Statement totals", refreshedRevokedBody);
    Assert.DoesNotContain("Record management decision", refreshedRevokedBody);
    Assert.Empty(diagnostics);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-ACCOUNTING-ROUTES-01")]
  public async Task AccountingQueuesAndDetailsClearAfterRouteChangesAndGrantRevocation()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-ACCOUNTING-ROUTES-01");
    var (packageId, _) = await FinancialPackageFixture.CreatePackageAsync(host);
    Guid mappingId;
    Guid siblingMappingId;
    Guid siblingDatasetId;
    Guid journalId;
    await using (var db = host.CreateDbContext())
    {
      mappingId = await db.FinancialPackages.AsNoTracking().Where(x => x.Id == packageId)
        .Select(x => x.MappingVersionId).SingleAsync();
      var admin = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      foreach (var (userId, role) in new[]
      {
        (host.Fixture.Staff.Id, "AccountingPreparer"),
        (host.Fixture.Reviewer.Id, "AccountingReviewer")
      })
      {
        var grant = await db.RoleGrants.SingleAsync(x => x.FirmId == host.Fixture.FirmId &&
          x.UserId == userId && x.Role == role && x.RevokedAt == null);
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, admin,
          new RevokeRoleGrantRequest(grant.Id));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
      host.Fixture.Staff.SessionEpoch = await db.Users.AsNoTracking()
        .Where(x => x.Id == host.Fixture.Staff.Id).Select(x => x.SessionEpoch).SingleAsync();
      host.Fixture.Reviewer.SessionEpoch = await db.Users.AsNoTracking()
        .Where(x => x.Id == host.Fixture.Reviewer.Id).Select(x => x.SessionEpoch).SingleAsync();
      journalId = await JournalReviewSeed.SeedAsync(db, host.Fixture);

      var sourceDatasetId = await db.MappingVersions.AsNoTracking().Where(x => x.Id == mappingId)
        .Select(x => x.DatasetId).SingleAsync();
      var sourceDataset = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(x => x.Id == sourceDatasetId);
      var sourceRows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == sourceDatasetId).ToListAsync();
      var siblingEngagementId = Guid.NewGuid();
      siblingDatasetId = Guid.NewGuid();
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = host.Fixture.FirmId, PracticeClientId = host.Fixture.ClientId,
        ServiceRoute = "SYNTHETIC-SIBLING", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingPreparer",
        host.Fixture.ClientId, siblingEngagementId));
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = siblingDatasetId, FirmId = sourceDataset.FirmId, ClientId = sourceDataset.ClientId,
        EngagementId = siblingEngagementId, PeriodId = sourceDataset.PeriodId, BookId = sourceDataset.BookId,
        Basis = sourceDataset.Basis, SourceKind = sourceDataset.SourceKind, Revision = 1,
        LegalEntityKey = sourceDataset.LegalEntityKey, Currency = sourceDataset.Currency,
        RawFileSha256Hex = sourceDataset.RawFileSha256Hex, NormalizedDatasetDigest = sourceDataset.NormalizedDatasetDigest,
        Sha256Hex = sourceDataset.Sha256Hex, ImportProfileVersion = sourceDataset.ImportProfileVersion,
        SourceLayout = sourceDataset.SourceLayout, Balanced = sourceDataset.Balanced,
        ValidationStatus = sourceDataset.ValidationStatus, ImportState = sourceDataset.ImportState,
        ControlTotal = sourceDataset.ControlTotal, ImportedAt = DateTimeOffset.UtcNow,
        ImportedByUserId = host.Fixture.Reviewer.Id
      });
      db.TrialBalanceRows.AddRange(sourceRows.Select(x => new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = siblingDatasetId, AccountCode = x.AccountCode, AccountName = x.AccountName,
        Amount = x.Amount, SourceDebit = x.SourceDebit, SourceCredit = x.SourceCredit,
        Currency = x.Currency, Entity = x.Entity, MappingCode = x.MappingCode
      }));
      await db.SaveChangesAsync();
      var chartVersionId = await db.ClientChartVersions.AsNoTracking().Where(x =>
        x.FirmId == host.Fixture.FirmId && x.ClientId == host.Fixture.ClientId &&
        x.Status == AccountingWorkflowStates.Approved).OrderByDescending(x => x.Version)
        .Select(x => x.Id).FirstAsync();
      var siblingMapping = await FinancialStatementService.CreateMappingVersionAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingPreparer"),
        new CreateMappingVersionRequest(siblingDatasetId, "tax-e2e-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "SYN-PRIVATE-SIBLING-ENGAGEMENT-MAPPING"),
          new("4000", "REVENUE", "INCOME", 1m, "Synthetic sibling mapping")
        ], chartVersionId));
      Assert.True(siblingMapping.Succeeded, siblingMapping.Message);
      siblingMappingId = siblingMapping.Value;
    }
    var settings = new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" };
    var staffOrigin = await host.StartApiForIdentityAsync(host.Fixture.Staff, settings);
    var reviewerOrigin = await host.StartApiForIdentityAsync(host.Fixture.Reviewer, settings);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var mappingContext = await browser.NewContextAsync();
    await using var journalContext = await browser.NewContextAsync();
    await using var queueContext = await browser.NewContextAsync();
    await using var reviewQueueContext = await browser.NewContextAsync();
    var mappingPage = await LoginAsync(mappingContext, staffOrigin, $"/ui/app/accounting/mappings/{mappingId:D}");
    var journalPage = await LoginAsync(journalContext, staffOrigin, $"/ui/app/accounting/journals/{journalId:D}");
    var queuePage = await LoginAsync(queueContext, staffOrigin, "/ui/app/accounting/mappings");
    var reviewQueuePage = await LoginAsync(reviewQueueContext, reviewerOrigin, "/ui/app/accounting/reviews");
    var diagnostics = new List<string>();
    foreach (var page in new[] { mappingPage, journalPage, queuePage, reviewQueuePage })
      page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");

    await Assertions.Expect(mappingPage.GetByText("Mapping lineage", new() { Exact = true })).ToBeVisibleAsync();
    var mappingToken = Guid.NewGuid().ToString("N");
    await mappingPage.EvaluateAsync("token => window.__mappingParityToken = token", mappingToken);
    await NavigateSpaAsync(mappingPage, $"/ui/app/accounting/mappings/{Guid.NewGuid():D}");
    await Assertions.Expect(mappingPage.GetByText("The mapping is not available in the current firm scope.", new() { Exact = true }))
      .ToBeVisibleAsync();
    Assert.DoesNotContain(mappingId.ToString("D"), await mappingPage.Locator("body").InnerTextAsync());
    Assert.Equal(mappingToken, await mappingPage.EvaluateAsync<string>("window.__mappingParityToken"));
    await NavigateSpaAsync(mappingPage, $"/ui/app/accounting/mappings/{mappingId:D}");
    await Assertions.Expect(mappingPage.GetByText("Mapping lineage", new() { Exact = true })).ToBeVisibleAsync();
    await NavigateSpaAsync(mappingPage, $"/ui/app/accounting/mappings/{siblingMappingId:D}");
    await Assertions.Expect(mappingPage.GetByText("The mapping is not available in the current firm scope.",
      new() { Exact = true })).ToBeVisibleAsync();
    var siblingMappingBody = await mappingPage.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(mappingId.ToString("D"), siblingMappingBody);
    Assert.DoesNotContain(siblingMappingId.ToString("D"), siblingMappingBody);
    Assert.DoesNotContain(siblingDatasetId.ToString("D"), siblingMappingBody);
    Assert.DoesNotContain("SYN-PRIVATE-SIBLING-ENGAGEMENT-MAPPING", siblingMappingBody);
    Assert.Equal(mappingToken, await mappingPage.EvaluateAsync<string>("window.__mappingParityToken"));
    await NavigateSpaAsync(mappingPage, $"/ui/app/accounting/mappings/{mappingId:D}");
    await Assertions.Expect(mappingPage.GetByText("Mapping lineage", new() { Exact = true })).ToBeVisibleAsync();

    await Assertions.Expect(journalPage.GetByRole(AriaRole.Region, new() { Name = "Exact journal context", Exact = true }))
      .ToContainTextAsync("AJ-SYN");
    await Assertions.Expect(journalPage.GetByRole(AriaRole.Cell,
      new() { Name = "100.123456", Exact = true }).First).ToBeVisibleAsync();
    var journalToken = Guid.NewGuid().ToString("N");
    await journalPage.EvaluateAsync("token => window.__journalParityToken = token", journalToken);
    await NavigateSpaAsync(journalPage, $"/ui/app/accounting/journals/{Guid.NewGuid():D}");
    await Assertions.Expect(journalPage.GetByText("This information is unavailable in your current scope.", new() { Exact = true }))
      .ToBeVisibleAsync();
    Assert.DoesNotContain("AJ-SYN", await journalPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain("100.123456", await journalPage.Locator("body").InnerTextAsync());
    Assert.Equal(journalToken, await journalPage.EvaluateAsync<string>("window.__journalParityToken"));
    await NavigateSpaAsync(journalPage, $"/ui/app/accounting/journals/{journalId:D}");
    await Assertions.Expect(journalPage.GetByRole(AriaRole.Region, new() { Name = "Exact journal context", Exact = true }))
      .ToContainTextAsync("AJ-SYN");

    await queuePage.GetByRole(AriaRole.Navigation, new() { Name = "Accounting record queues", Exact = true })
      .GetByRole(AriaRole.Link, new() { Name = "Adjustments", Exact = true }).ClickAsync();
    await Assertions.Expect(queuePage.GetByRole(AriaRole.Heading, new() { Name = "Adjustment journals", Exact = true }))
      .ToBeVisibleAsync();
    await Assertions.Expect(queuePage.GetByText("AJ-SYN", new() { Exact = true })).ToBeVisibleAsync();
    var queueToken = Guid.NewGuid().ToString("N");
    await queuePage.EvaluateAsync("token => window.__accountingQueueParityToken = token", queueToken);
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await queuePage.SetViewportSizeAsync(width, 900);
      Assert.True(await queuePage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Populated accounting record queue overflows the {width}px viewport.");
    }
    Assert.Equal(queueToken, await queuePage.EvaluateAsync<string>("window.__accountingQueueParityToken"));
    await queuePage.SetViewportSizeAsync(1280, 900);
    await Assertions.Expect(reviewQueuePage.GetByRole(AriaRole.Heading,
      new() { Name = "Financial package reviews", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(reviewQueuePage.GetByText(packageId.ToString("D"), new() { Exact = true }))
      .ToBeVisibleAsync();
    foreach (var width in new[] { 320, 390, 760, 1024, 1440, 1920 })
    {
      await reviewQueuePage.SetViewportSizeAsync(width, 900);
      Assert.True(await reviewQueuePage.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"),
        $"Populated package review queue overflows the {width}px viewport.");
    }
    await reviewQueuePage.SetViewportSizeAsync(1280, 900);

    await using (var db = host.CreateDbContext())
    {
      var admin = PbcSeed.Actor(host.Fixture.Admin, "Administrator");
      foreach (var (userId, roles) in new[]
      {
        (host.Fixture.Staff.Id, new[] { "Staff", "AccountingPreparer" }),
        (host.Fixture.Reviewer.Id, new[] { "Reviewer", "AccountingReviewer" })
      })
      {
        var grants = await db.RoleGrants.Where(x => x.FirmId == host.Fixture.FirmId && x.UserId == userId &&
          x.RevokedAt == null && roles.Contains(x.Role)).ToListAsync();
        Assert.NotEmpty(grants);
        foreach (var grant in grants)
        {
          var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db, admin,
            new RevokeRoleGrantRequest(grant.Id));
          Assert.True(revoked.Succeeded, revoked.Message);
        }
      }
    }

    await mappingPage.GetByRole(AriaRole.Button, new() { Name = "Refresh mapping", Exact = true }).ClickAsync();
    await mappingPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain(mappingId.ToString("D"), await mappingPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain("Current and prior allocation comparison", await mappingPage.Locator("body").InnerTextAsync());

    await journalPage.GetByRole(AriaRole.Button, new() { Name = "Refresh journal review", Exact = true }).ClickAsync();
    await journalPage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("AJ-SYN", await journalPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain("100.123456", await journalPage.Locator("body").InnerTextAsync());
    Assert.DoesNotContain("Exact journal context", await journalPage.Locator("body").InnerTextAsync());

    await queuePage.GetByRole(AriaRole.Button, new() { Name = "Refresh queue", Exact = true }).ClickAsync();
    await queuePage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain("AJ-SYN", await queuePage.Locator("body").InnerTextAsync());

    await reviewQueuePage.GetByRole(AriaRole.Button, new() { Name = "Refresh queue", Exact = true }).ClickAsync();
    await reviewQueuePage.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable", Exact = true })
      .WaitForAsync(new() { Timeout = 15000 });
    Assert.DoesNotContain(packageId.ToString("D"), await reviewQueuePage.Locator("body").InnerTextAsync());
    Assert.Empty(diagnostics);
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ANGULAR-PERIOD-REOPEN-01")]
  public async Task ReopenedPeriodShowsNewRevisionWithoutReplacingValidatedPackage()
  {
    await using var host = await OwnedHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ANGULAR-PERIOD-REOPEN-01");
    var (packageId, expectedArtifacts) = await FinancialPackageFixture.CreatePackageAsync(host);
    Guid periodId;
    long packageRevision;
    string packageHash;
    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      periodId = package.PeriodId ?? throw new InvalidOperationException("The synthetic package has no reporting period.");
      packageRevision = package.Revision;
      packageHash = package.CalculationHash;
      db.RoleGrants.AddRange(
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingReviewer", host.Fixture.ClientId),
        PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "Partner", host.Fixture.ClientId));
      await db.SaveChangesAsync();
    }

    await using (var db = host.CreateDbContext())
    {
      var management = await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "synthetic-management-review", "Synthetic acceptance fixture."));
      Assert.True(management.Succeeded, management.Message);
      var accounting = await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.AccountingReview,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "synthetic-accounting-review", "Synthetic acceptance fixture."));
      Assert.True(accounting.Succeeded, accounting.Message);
      var partner = await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Partner"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.SignedIn,
          "synthetic-partner-review", "Synthetic acceptance fixture."));
      Assert.True(partner.Succeeded, partner.Message);
      var close = await ClientAccountingService.ClosePeriodAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingReviewer"), periodId, "Synthetic initial close");
      Assert.True(close.Succeeded, close.Message);
      var reopen = await ClientAccountingService.ReopenPeriodAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "Partner"), periodId, "Synthetic controlled correction");
      Assert.True(reopen.Succeeded, reopen.Message);
    }

    await using (var db = host.CreateDbContext())
    {
      var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
      Assert.Equal(AccountingWorkflowStates.Draft, period.Status);
      Assert.Equal(3, period.Revision);
      Assert.Equal(1, await db.FinancialPackages.CountAsync(x => x.PeriodId == periodId));
      Assert.Equal(3, await db.FinancialPackageReviewDecisions.CountAsync(x => x.FinancialPackageId == packageId));
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      Assert.Equal(packageRevision, package.Revision);
      Assert.Equal(packageHash, package.CalculationHash);
      Assert.Equal(AccountingPackageStates.PackageValidated, package.Status);
      var artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x =>
        x.FinancialPackageId == packageId && x.ArtifactVersion == FinancialPackageArtifactVersions.Pdf);
      Assert.Equal(expectedArtifacts[FinancialPackageArtifactVersions.Pdf].Bytes, artifact.ArtifactBytes);
      Assert.Equal(expectedArtifacts[FinancialPackageArtifactVersions.Pdf].Sha256, artifact.ArtifactSha256Hex);
    }

    var origin = await host.StartApiForIdentityAsync(host.Fixture.Staff,
      new Dictionary<string, string> { ["AngularUi__Enabled"] = "true" });
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    page.PageError += (_, error) => diagnostics.Add($"page-error: {error}");
    var route = $"/ui/app/accounting/periods/{periodId:D}";
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(route));
    await Assertions.Expect(page.GetByRole(AriaRole.Heading,
      new() { Name = "Accounting period", Exact = true })).ToBeVisibleAsync();
    await Assertions.Expect(page.GetByText("FY2026-E2E")).ToBeVisibleAsync();
    var body = await page.Locator("main").InnerTextAsync();
    Assert.Contains("FY2026-E2E", body);
    Assert.Contains("draft", body, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("3", body);
    var packageLink = page.GetByRole(AriaRole.Link, new() { Name = "Open package", Exact = true });
    Assert.EndsWith($"/app/accounting/packages/{packageId:D}",
      await packageLink.GetAttributeAsync("href"));
    Assert.Empty(diagnostics);
  }

  private static async Task<IPage> LoginAsync(IBrowserContext context, string origin, string path)
  {
    var page = await context.NewPageAsync();
    await page.GotoAsync(origin + "/auth/sign-in?returnUrl=" + Uri.EscapeDataString(path));
    return page;
  }

  private static Task NavigateSpaAsync(IPage page, string path) => page.EvaluateAsync(
    "path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }", path);

  private static async Task<string> GetPackageHashAsync(OwnedHost host, Guid packageId)
  {
    await using var db = host.CreateDbContext();
    return await db.FinancialPackages.AsNoTracking().Where(x => x.Id == packageId)
      .Select(x => x.CalculationHash).SingleAsync();
  }
}
