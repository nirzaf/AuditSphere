using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
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
  [Trait("Category", "AccountingAndReporting")]
  [Trait("CaseId", "AS-ACCOUNTING-REMEASUREMENT-UI-01")]
  public async Task CurrencyRemeasurementWorkbenchRestrictsContextAndRestoresBrowserDraft()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-ACCOUNTING-REMEASUREMENT-UI-01");
    var periodId = Guid.NewGuid();
    var glLineId = Guid.NewGuid();
    await using (var db = host.CreateDbContext())
    {
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingPreparer",
        host.Fixture.ClientId, host.Fixture.EngagementId));
      db.ClientAccountingProfiles.Add(new ClientAccountingProfile
      {
        Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        Jurisdiction = "QA", FunctionalCurrency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        PeriodCode = "FY26-SYNTHETIC", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "IFRS", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      var now = DateTimeOffset.UtcNow;
      var importBatchId = Guid.NewGuid();
      var transactionId = Guid.NewGuid();
      db.SourceImportBatches.Add(new SourceImportBatch
      {
        Id = importBatchId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, PeriodId = periodId, SourceKind = "GL",
        ProfileVersion = "synthetic-v1", ParserVersion = "synthetic-v1", RawFileSha256Hex = new string('1', 64),
        NormalizedDatasetDigest = new string('2', 64), LegalEntityKey = "SYNTHETIC-ENTITY", Currency = "QAR",
        RowCount = 2, ExpectedTransactionCount = 1, ExpectedLineCount = 2, AcceptedTransactionCount = 1,
        AcceptedLineCount = 2, Status = "SEALED", ReceiptReference = "synthetic-e2e-gl",
        CreatedByUserId = host.Fixture.Admin.Id, CreatedAt = now
      });
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = transactionId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
        EngagementId = host.Fixture.EngagementId, ImportBatchId = importBatchId,
        StableJournalId = "SYN-REMEASURE-JOURNAL", PostingDate = new DateOnly(2026, 12, 31),
        Currency = "QAR", SourceSystem = "synthetic-test", CreatedAt = now
      });
      db.GeneralLedgerLines.AddRange(
        new GeneralLedgerLine
        {
          Id = glLineId, FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, ImportBatchId = importBatchId, TransactionId = transactionId,
          StableLineId = "SYN-FOREIGN-LINE", AccountCode = "1200", Debit = 370m,
          OriginalCurrency = "USD", OriginalAmount = 100m, FunctionalAmount = 370m, CreatedAt = now
        },
        new GeneralLedgerLine
        {
          Id = Guid.NewGuid(), FirmId = host.Fixture.FirmId, ClientId = host.Fixture.ClientId,
          EngagementId = host.Fixture.EngagementId, ImportBatchId = importBatchId, TransactionId = transactionId,
          StableLineId = "SYN-OFFSET-LINE", AccountCode = "4000", Credit = 370m,
          OriginalCurrency = "QAR", OriginalAmount = -370m, FunctionalAmount = -370m, CreatedAt = now
        });
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/app/accounting/remeasurement"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Currency remeasurement workpapers" }).WaitForAsync();
    Assert.Contains(await page.Locator("select").First.Locator("option").AllTextContentsAsync(),
      x => x.Contains("PBC TEST CLIENT · FY26-SYNTHETIC", StringComparison.Ordinal));
    var sourceSelector = page.GetByLabel("Imported GL source line *");
    Assert.Contains(await sourceSelector.Locator("option").AllTextContentsAsync(),
      x => x.Contains("SYN-REMEASURE-JOURNAL / SYN-FOREIGN-LINE", StringComparison.Ordinal));
    await connected;
    var reference = page.GetByLabel("Stable source reference *");
    await reference.FillAsync("SYNTHETIC-REMEASUREMENT-LINE");
    await sourceSelector.SelectOptionAsync(glLineId.ToString("D"));
    await page.WaitForFunctionAsync($"() => Object.values(localStorage).some(value => value.includes('{glLineId:D}') && value.includes('SYNTHETIC-REMEASUREMENT-LINE'))", null, new() { Timeout = 5000 });
    var draftBeforeReload = await page.EvaluateAsync<string>("() => JSON.stringify(Object.fromEntries(Object.entries(localStorage)))");
    Assert.Contains("SYNTHETIC-REMEASUREMENT-LINE", draftBeforeReload);
    var reconnected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.ReloadAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Currency remeasurement workpapers" }).WaitForAsync();
    await reconnected;
    await page.WaitForFunctionAsync("() => document.querySelector('[data-draft-field=\\\"reference-0\\\"]')?.value === 'SYNTHETIC-REMEASUREMENT-LINE' && document.querySelector('[data-draft-field=\\\"source-line-0\\\"]')?.value", null, new() { Timeout = 5000 });
    var currentDraft = await page.EvaluateAsync<string>("() => JSON.stringify(Object.fromEntries(Object.entries(localStorage)))");
    Assert.Contains("SYNTHETIC-REMEASUREMENT-LINE", currentDraft);
    Assert.Equal(glLineId.ToString("D"), await page.GetByLabel("Imported GL source line *").InputValueAsync());
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

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
  [Trait("CaseId", "AS-PAR-002-JOURNAL-STALE-ROUTE-01")]
  public async Task JournalPageClearsPriorJournalWhenRouteChangesToUnavailableId()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-JOURNAL-STALE-ROUTE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    Guid journalId;
    const string journalNumber = "SYN-PAR-002-STALE-ROUTE-JOURNAL";
    const string privateAccountCode = "SYN-PRIVATE-STALE-ROUTE-LINE";
    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      var plan = await db.AdjustmentPlans.AsNoTracking().SingleAsync(x => x.Id == package.AdjustmentPlanId);
      var draft = await AdjustmentJournalService.CreateDraftAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"), plan.BaseDatasetId, journalNumber,
        [(privateAccountCode, 75m, 0m), ("SYN-PRIVATE-STALE-ROUTE-OFFSET", 0m, 75m)],
        purpose: AdjustmentJournalPurposes.ReportingAdjustment,
        reason: "Synthetic route-change regression", evidenceReference: "synthetic-test-evidence");
      Assert.True(draft.Succeeded, draft.Message);
      journalId = draft.Value;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/journals/{journalId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = journalNumber }).WaitForAsync();
    await page.GetByText(privateAccountCode, new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = await page.EvaluateAsync<string>("""
      () => {
        window.__journalRouteTestToken ??= crypto.randomUUID();
        return window.__journalRouteTestToken;
      }
      """);

    var missingJournalId = Guid.NewGuid();
    await page.EvaluateAsync("""
      (path) => {
      const link = document.createElement('a');
      link.href = path;
      link.textContent = 'Open unavailable journal';
      document.body.append(link);
      link.click();
      }
      """, $"/app/accounting/journals/{missingJournalId:D}");

    await page.GetByRole(AriaRole.Heading, new() { Name = "Journal unavailable" }).WaitForAsync();
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__journalRouteTestToken"));
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(journalNumber, body);
    Assert.DoesNotContain(privateAccountCode, body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FS-STALE-ROUTE-01")]
  public async Task FinancialPackagePageClearsPriorPackageWhenRouteChangesToUnavailableId()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FS-STALE-ROUTE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await page.GetByText("Mapped statement totals", new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = await page.EvaluateAsync<string>("window.__packageRouteTestToken = crypto.randomUUID()");

    var unavailablePackageId = Guid.NewGuid();
    await page.EvaluateAsync("""
      (path) => {
      const link = document.createElement('a');
      link.href = path;
      link.textContent = 'Open unavailable package';
      document.body.append(link);
      link.click();
      }
      """, $"/app/accounting/packages/{unavailablePackageId:D}");

    await page.GetByRole(AriaRole.Heading, new() { Name = "Package unavailable" })
      .WaitForAsync(new() { Timeout = 5000 });
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__packageRouteTestToken"));
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString("D"), body);
    Assert.DoesNotContain("Mapped statement totals", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-MAPPING-STALE-ROUTE-01")]
  public async Task MappingPageClearsPriorEngagementWhenRouteChangesToUnauthorizedMapping()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-MAPPING-STALE-ROUTE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    Guid authorizedMappingId;
    Guid authorizedDatasetId;
    Guid unauthorizedEngagementId;
    Guid unauthorizedMappingId;
    const string privateMappingMarker = "SYN-PAR-002-MAPPING-PRIVATE";
    await using (var db = host.CreateDbContext())
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
      authorizedMappingId = package.MappingVersionId;
      authorizedDatasetId = await db.MappingVersions.AsNoTracking().Where(x => x.Id == authorizedMappingId)
        .Select(x => x.DatasetId).SingleAsync();
      var sourceDataset = await db.TrialBalanceDatasets.AsNoTracking()
        .SingleAsync(x => x.Id == authorizedDatasetId);
      var sourceRows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == authorizedDatasetId).ToListAsync();
      unauthorizedEngagementId = Guid.NewGuid();
      db.Engagements.Add(new Engagement
      {
        Id = unauthorizedEngagementId, FirmId = host.Fixture.FirmId,
        PracticeClientId = host.Fixture.ClientId, ServiceRoute = "SYNTHETIC-SIBLING",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = "Active",
        ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Reviewer, "AccountingPreparer",
        host.Fixture.ClientId, unauthorizedEngagementId));
      var datasetId = Guid.NewGuid();
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = sourceDataset.FirmId, ClientId = sourceDataset.ClientId,
        EngagementId = unauthorizedEngagementId, PeriodId = sourceDataset.PeriodId, BookId = sourceDataset.BookId,
        Basis = sourceDataset.Basis, SourceKind = sourceDataset.SourceKind, Revision = 1,
        LegalEntityKey = sourceDataset.LegalEntityKey, Currency = sourceDataset.Currency,
        RawFileSha256Hex = sourceDataset.RawFileSha256Hex,
        NormalizedDatasetDigest = sourceDataset.NormalizedDatasetDigest,
        Sha256Hex = sourceDataset.Sha256Hex, ImportProfileVersion = sourceDataset.ImportProfileVersion,
        SourceLayout = sourceDataset.SourceLayout, Balanced = sourceDataset.Balanced,
        ValidationStatus = sourceDataset.ValidationStatus, ImportState = sourceDataset.ImportState,
        ControlTotal = sourceDataset.ControlTotal, ImportedAt = DateTimeOffset.UtcNow,
        ImportedByUserId = host.Fixture.Reviewer.Id
      });
      db.TrialBalanceRows.AddRange(sourceRows.Select(x => new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = x.AccountCode,
        AccountName = x.AccountName, Amount = x.Amount, SourceDebit = x.SourceDebit,
        SourceCredit = x.SourceCredit, Currency = x.Currency, Entity = x.Entity, MappingCode = x.MappingCode
      }));
      await db.SaveChangesAsync();
      var mapping = await FinancialStatementService.CreateMappingVersionAsync(db,
        PbcSeed.Actor(host.Fixture.Reviewer, "AccountingPreparer"),
        new CreateMappingVersionRequest(datasetId, "tax-e2e-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, privateMappingMarker),
          new("4000", "REVENUE", "INCOME", 1m, "Synthetic sibling mapping")
        ]));
      Assert.True(mapping.Succeeded, mapping.Message);
      unauthorizedMappingId = mapping.Value;
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/mappings/{authorizedMappingId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = authorizedMappingId.ToString("D") }).WaitForAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Mapping lineage" }).WaitForAsync();
    await connected;
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__testDocumentToken = token", documentToken);
    await page.EvaluateAsync("path => { history.pushState({}, '', path); dispatchEvent(new PopStateEvent('popstate')); }",
      $"/app/accounting/mappings/{unauthorizedMappingId:D}");
    await page.GetByRole(AriaRole.Heading, new() { Name = "Mapping unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(authorizedMappingId.ToString("D"), body);
    Assert.DoesNotContain(unauthorizedMappingId.ToString("D"), body);
    Assert.DoesNotContain(authorizedDatasetId.ToString("D"), body);
    Assert.DoesNotContain(privateMappingMarker, body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__testDocumentToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-FS-STALE-ROUTE-01")]
  public async Task ClientFinancialPackagePageClearsPriorPackageWhenRouteChangesToUnavailableId()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-FS-STALE-ROUTE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    string packageHash;
    await using (var db = host.CreateDbContext())
      packageHash = await db.FinancialPackages.AsNoTracking()
        .Where(x => x.Id == packageId).Select(x => x.CalculationHash).SingleAsync();

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.ClientUrl, $"/portal/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Management review" }).WaitForAsync();
    await page.GetByText(packageHash, new() { Exact = true }).WaitForAsync();
    Assert.Contains("not an audit opinion, assurance conclusion or proof that an external ledger has been posted",
      await page.Locator("body").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
    await connected;
    var documentToken = await page.EvaluateAsync<string>("window.__clientPackageRouteToken = crypto.randomUUID()");

    var unavailablePackageId = Guid.NewGuid();
    await page.EvaluateAsync("""
      (path) => {
        const link = document.createElement('a');
        link.href = path;
        link.textContent = 'Open unavailable client package';
        document.body.append(link);
        link.click();
      }
      """, $"/portal/accounting/packages/{unavailablePackageId:D}");

    await page.GetByRole(AriaRole.Heading, new() { Name = "Package unavailable" })
      .WaitForAsync(new() { Timeout = 5000 });
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientPackageRouteToken"));
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageHash, body);
    Assert.DoesNotContain("Statement totals", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-CLIENT-FS-REVOKE-01")]
  public async Task ClientFinancialPackagePageClearsViewAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-CLIENT-FS-REVOKE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    string packageHash;
    await using (var db = host.CreateDbContext())
      packageHash = await db.FinancialPackages.AsNoTracking()
        .Where(x => x.Id == packageId).Select(x => x.CalculationHash).SingleAsync();

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.ClientUrl, $"/portal/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Management review" }).WaitForAsync();
    await page.GetByText(packageHash, new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = await page.EvaluateAsync<string>("window.__clientPackageRevokeToken = crypto.randomUUID()");
    await using (var db = host.CreateDbContext())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == host.Fixture.Client.Id &&
        x.Role == "ClientUser" && x.RevokedAt == null);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(grant.Id));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    var decisionButton = page.GetByRole(AriaRole.Button, new() { Name = "Record management decision" });
    if (await decisionButton.CountAsync() > 0)
      await decisionButton.ClickAsync(new() { Force = true });
    try
    {
      await page.GetByRole(AriaRole.Heading, new() { Name = "Package unavailable" }).WaitForAsync();
    }
    catch (TimeoutException)
    {
      throw new Xunit.Sdk.XunitException($"The revoked package view did not clear after an action.\n" +
        $"{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}");
    }
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__clientPackageRevokeToken"));
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageHash, body);
    Assert.DoesNotContain("Statement totals", body);
    Assert.DoesNotContain("Record management decision", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
    await using var verify = host.CreateDbContext();
    Assert.Empty(await verify.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FinancialPackageId == packageId).ToListAsync());
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FS-QUEUE-REVOKE-01")]
  public async Task FinancialPackageReviewQueueClearsAfterGrantRevocationAndRefresh()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FS-QUEUE-REVOKE-01");
    var (packageId, _) = await CreatePackageAsync(host);
    Guid reviewGrantId;
    await using (var db = host.CreateDbContext())
    {
      var grant = PbcSeed.Grant(host.Fixture.FirmId, host.Fixture.Staff, "AccountingReviewer",
        host.Fixture.ClientId, host.Fixture.EngagementId);
      reviewGrantId = grant.Id;
      db.RoleGrants.Add(grant);
      await db.SaveChangesAsync();
    }

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    await using var context = await browser.NewContextAsync();
    var page = await context.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/app/accounting/reviews"));
    await page.GetByText(packageId.ToString("D"), new() { Exact = true }).WaitForAsync();
    await connected;
    var documentToken = Guid.NewGuid().ToString("N");
    await page.EvaluateAsync("token => window.__reviewQueueRouteToken = token", documentToken);

    await using (var db = host.CreateDbContext())
    {
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(db,
        PbcSeed.Actor(host.Fixture.Admin, "Administrator"), new RevokeRoleGrantRequest(reviewGrantId));
      Assert.True(revoked.Succeeded, revoked.Message);
    }

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh queue" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Review queue unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString("D"), body);
    Assert.DoesNotContain("Action required", body);
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__reviewQueueRouteToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-RECORD-TABS-01")]
  public async Task AccountingRecordTabsReloadTheirScopedQueueDuringInAppNavigation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ACCT-RECORD-TABS-01");
    await CreatePackageAsync(host);
    await using (var db = host.CreateDbContext())
      Assert.Contains("AJ-E2E-001", await db.AdjustmentJournals.AsNoTracking().Where(x => x.FirmId == host.Fixture.FirmId)
        .Select(x => x.JournalNumber).ToListAsync());

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/app/accounting/mappings"));
    var prerenderedHeading = await page.QuerySelectorAsync("h1");
    await page.GetByRole(AriaRole.Heading, new() { Name = "COA and accounting mappings" }).WaitForAsync();
    var adjustmentTab = page.Locator("nav[aria-label='Accounting record queues'] a[href='/app/accounting/journals']");
    await adjustmentTab.WaitForAsync();
    await connected;
    await WaitForInteractiveRenderAsync(page, prerenderedHeading);
    await page.GetByText("Only records in the authenticated client or exact engagement scope are shown.").WaitForAsync();
    var documentToken = await page.EvaluateAsync<string>("""
      () => {
        window.__accountingQueueRouteTestToken ??= crypto.randomUUID();
        return window.__accountingQueueRouteTestToken;
      }
      """);

    await adjustmentTab.ClickAsync();
    await page.WaitForURLAsync("**/app/accounting/journals");
    await page.Locator("h1").GetByText("Adjustment journals", new() { Exact = true }).WaitForAsync();
    try
    {
      await page.GetByText("AJ-E2E-001", new() { Exact = true }).WaitForAsync(new() { Timeout = 5_000 });
    }
    catch (TimeoutException)
    {
      var failureBody = await page.Locator("body").InnerTextAsync();
      var staffLog = Directory.GetFiles(host.RunRoot, $"web-Staff-{host.Fixture.Staff.Id:N}.log").SingleOrDefault();
      var logText = staffLog is null ? string.Empty : File.ReadAllText(staffLog);
      var failureAt = logText.LastIndexOf("Failed to load accounting records queue", StringComparison.Ordinal);
      var error = failureAt < 0 ? "No component exception was logged."
        : logText[failureAt..Math.Min(logText.Length, failureAt + 8_000)];
      throw new Xunit.Sdk.XunitException($"Journal list missing after tab navigation. URL={page.Url}\n{failureBody}\n{string.Join("\n", diagnostics)}\n{error}");
    }
    var body = await page.Locator("body").InnerTextAsync();
    if (!body.Contains("AJ-E2E-001", StringComparison.Ordinal))
      throw new Xunit.Sdk.XunitException($"Journal list missing after tab navigation. URL={page.Url}\n{body}\n{string.Join("\n", diagnostics)}");
    Assert.Equal(documentToken, await page.EvaluateAsync<string>("window.__accountingQueueRouteTestToken"));
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-ACCT-RECORD-REVOKED-01")]
  public async Task RefreshClearsPreviouslyAuthorizedAccountingQueueAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-ACCT-RECORD-REVOKED-01");
    await CreatePackageAsync(host);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, "/app/accounting/journals"));
    await page.GetByText("AJ-E2E-001", new() { Exact = true }).WaitForAsync();
    await connected;

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

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh queue" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("AJ-E2E-001", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-JOURNAL-REFRESH-REVOKED-01")]
  public async Task RefreshClearsPreviouslyAuthorizedJournalAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-JOURNAL-REFRESH-REVOKED-01");
    await CreatePackageAsync(host);
    Guid journalId;
    await using (var db = host.CreateDbContext())
      journalId = await db.AdjustmentJournals.AsNoTracking().Where(x =>
        x.FirmId == host.Fixture.FirmId && x.JournalNumber == "AJ-E2E-001")
        .Select(x => x.Id).SingleAsync();

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/journals/{journalId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "AJ-E2E-001" }).WaitForAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Journal lines" }).WaitForAsync();
    await connected;

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

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh journal" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain("AJ-E2E-001", body);
    Assert.DoesNotContain("Journal lines", body);
    Assert.DoesNotContain(journalId.ToString("D"), body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
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
        PbcSeed.Actor(host.Fixture.Admin, "Partner"),
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
    await preparerPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await SelectMudOptionAsync(preparerPage, "Stage", FinancialPackageReviewStages.AccountingReview);
    await preparerPage.GetByLabel("Evidence reference").FillAsync("Synthetic preparer must not self-review");
    await preparerPage.GetByRole(AriaRole.Button, new() { Name = "Record decision" }).ClickAsync();
    await Assertions.Expect(preparerPage.Locator(".command-result")).ToContainTextAsync("Blocked:");
    await preparerPage.CloseAsync();

    await using (var db = host.CreateDbContext())
      Assert.Empty(await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync());

    // Stage sequencing: management approval precedes the independent accounting review.
    await using (var db = host.CreateDbContext())
      Assert.True((await FinancialPackageReviewService.RecordAsync(db,
        PbcSeed.Actor(host.Fixture.Staff, "AccountingPreparer"),
        new FinancialPackageReviewRequest(packageId, FinancialPackageReviewStages.ManagementApproval,
          FinancialPackageReviewDecisions.Approved, FinancialPackageReviewEvidenceModes.Offline,
          "synthetic-management-approval", "Management approved the exact package."))).Succeeded);

    var reviewerUrl = await host.StartReviewerWebAsync();
    var reviewerPage = await browser.NewPageAsync();
    var reviewerDiagnostics = new List<string>();
    var reviewerConnected = WaitForCircuitConnectionAsync(reviewerPage, reviewerDiagnostics);
    await reviewerPage.GotoAsync(SignInUrl(reviewerUrl, route));
    await reviewerPage.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await reviewerConnected;
    await reviewerPage.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5000 });
    await SelectMudOptionAsync(reviewerPage, "Stage", FinancialPackageReviewStages.AccountingReview);
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
      .SingleAsync(x => x.FinancialPackageId == packageId &&
        x.Stage == FinancialPackageReviewStages.AccountingReview);
    Assert.Equal(host.Fixture.Reviewer.Id, decision.DecidedByUserId);
    Assert.Equal(FinancialPackageReviewStages.AccountingReview, decision.Stage);
    Assert.Equal(FinancialPackageReviewDecisions.Approved, decision.Decision);
    Assert.Equal(2, await verify.FinancialPackageReviewDecisions.AsNoTracking()
      .CountAsync(x => x.FinancialPackageId == packageId));
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

  [Fact]
  [Trait("CaseId", "AS-PAR-002-FS-REFRESH-REVOKED-01")]
  public async Task RefreshClearsPreviouslyAuthorizedPackageAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-FS-REFRESH-REVOKED-01");
    var (packageId, _) = await CreatePackageAsync(host);
    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/packages/{packageId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Financial statement package" }).WaitForAsync();
    await page.GetByText("Mapped statement totals", new() { Exact = true }).WaitForAsync();
    await connected;

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

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh package" }).ClickAsync();
    await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync();
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(packageId.ToString("D"), body);
    Assert.DoesNotContain("Mapped statement totals", body);
    Assert.DoesNotContain("Calculation hash", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-002-MAPPING-REFRESH-REVOKED-01")]
  public async Task RefreshClearsPreviouslyAuthorizedMappingAfterGrantRevocation()
  {
    await using var host = await OwnedBlazorHost.StartAsync(startWorker: false,
      caseId: "AS-PAR-002-MAPPING-REFRESH-REVOKED-01");
    var (packageId, _) = await CreatePackageAsync(host);
    Guid mappingId;
    await using (var db = host.CreateDbContext())
      mappingId = await db.FinancialPackages.AsNoTracking().Where(x => x.Id == packageId)
        .Select(x => x.MappingVersionId).SingleAsync();

    using var playwright = await Playwright.CreateAsync();
    await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
    var page = await browser.NewPageAsync();
    var diagnostics = new List<string>();
    var connected = WaitForCircuitConnectionAsync(page, diagnostics);
    await page.GotoAsync(SignInUrl(host.StaffUrl, $"/app/accounting/mappings/{mappingId:D}"));
    await page.GetByRole(AriaRole.Heading, new() { Name = "Current and prior allocation comparison" }).WaitForAsync();
    await connected;

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

    await page.GetByRole(AriaRole.Button, new() { Name = "Refresh mapping" }).ClickAsync();
    try
    {
      await page.GetByRole(AriaRole.Heading, new() { Name = "Access unavailable" }).WaitForAsync(new() { Timeout = 5_000 });
    }
    catch (TimeoutException)
    {
      throw new Xunit.Sdk.XunitException($"Refresh did not render the unavailable mapping state.\n{await page.Locator("body").InnerTextAsync()}\n{string.Join("\n", diagnostics)}");
    }
    var body = await page.Locator("body").InnerTextAsync();
    Assert.DoesNotContain(mappingId.ToString("D"), body);
    Assert.DoesNotContain("Current and prior allocation comparison", body);
    Assert.DoesNotContain("Unmapped source accounts", body);
    Assert.DoesNotContain(diagnostics, x => x.StartsWith("page-error:", StringComparison.Ordinal));
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

  private static async Task WaitForInteractiveRenderAsync(IPage page, IElementHandle? prerendered)
  {
    if (prerendered is null)
      throw new InvalidOperationException("The page was served without the expected prerendered content.");
    await page.WaitForFunctionAsync("element => !element.isConnected", prerendered,
      new() { PollingInterval = 50, Timeout = 30_000 });
  }


  /// <summary>
  /// MudBlazor equivalent of SelectOptionAsync at the same strength: opens the
  /// labelled select and clicks the exact option in the open popover, so the same
  /// value is chosen and every downstream assertion is unchanged.
  /// </summary>
  private static async Task SelectMudOptionAsync(IPage page, string labelText, string optionText)
  {
    await page.Locator($"label:has-text('{labelText}') .mud-select").First.ClickAsync();
    await page.Locator(".mud-popover-open").GetByText(optionText, new() { Exact = true }).ClickAsync();
  }

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
