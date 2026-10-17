using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PdfSharp.Pdf.IO;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FinancialStatementTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId, Guid PeriodId, Guid BookId,
    AppUser Preparer, AppUser Reviewer);

  private sealed class TestDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  [Fact]
  public void OfficeArtifacts_AreDeterministicFormulaFreeAndMacroFree()
  {
    const string canonical = "Package: test\nDisclosure: =2+2\nReference: +SUM(A1:A2)\n";
    foreach (var version in new[] { FinancialPackageArtifactVersions.Workbook, FinancialPackageArtifactVersions.Word })
    {
      var first = FinancialPackageOfficeRenderer.Render(version, canonical);
      var second = FinancialPackageOfficeRenderer.Render(version, canonical);
      Assert.Equal(first.ArtifactBytes, second.ArtifactBytes);
      Assert.Equal(first.ArtifactSha256Hex, Hashing.Sha256Hex(first.ArtifactBytes));

      using var stream = new MemoryStream(first.ArtifactBytes);
      using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
      Assert.DoesNotContain(archive.Entries, x =>
        x.FullName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase) ||
        x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
      var xml = archive.Entries.Where(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        .Select(x =>
        {
          using var reader = x.Open();
          return XDocument.Load(reader);
        }).ToList();
      Assert.DoesNotContain(xml.SelectMany(x => x.Descendants()), x => x.Name.LocalName == "f");
      var text = string.Concat(xml.SelectMany(x => x.DescendantNodes().OfType<XText>()).Select(x => x.Value));
      Assert.Contains("=2+2", text, StringComparison.Ordinal);
      Assert.Contains("+SUM(A1:A2)", text, StringComparison.Ordinal);
    }
  }

  [Fact]
  public void ControlledWorkbook_PreservesOnlyItsTrustedFormula()
  {
    const string canonical = "Package: test\nDisclosure: =2+2\nReference: +SUM(A1:A2)\n";
    var artifact = FinancialPackageOfficeRenderer.Render(
      FinancialPackageArtifactVersions.ControlledWorkbook, canonical);

    using var stream = new MemoryStream(artifact.ArtifactBytes);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
    Assert.DoesNotContain(archive.Entries, x =>
      x.FullName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase) ||
      x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
    var xml = archive.Entries.Where(x => x.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
      .Select(x =>
      {
        using var reader = x.Open();
        return XDocument.Load(reader);
      }).ToList();
    var formulas = xml.SelectMany(x => x.Descendants())
      .Where(x => x.Name.LocalName == "f").Select(x => x.Value).ToArray();
    Assert.Equal(["COUNTA('Financial package'!A1:A4)"], formulas);
    var text = string.Concat(xml.SelectMany(x => x.DescendantNodes().OfType<XText>()).Select(x => x.Value));
    Assert.Contains("=2+2", text, StringComparison.Ordinal);
    Assert.Contains("+SUM(A1:A2)", text, StringComparison.Ordinal);
  }

  [Fact]
  public void PdfArtifact_IsDeterministicUnicodeAndInactive()
  {
    const string canonical = "Package: test (QAR) — Crédit\\source\n--- STATEMENT LINES ---\n" +
      "ASSETS | CASH | 1000 | 100.000000 QAR | 1.000000 | residual=0.000000\n";
    var first = FinancialPackageOfficeRenderer.Render(FinancialPackageArtifactVersions.Pdf, canonical);
    var second = FinancialPackageOfficeRenderer.Render(FinancialPackageArtifactVersions.Pdf, canonical);

    Assert.Equal(first.ArtifactBytes, second.ArtifactBytes);
    Assert.Equal(first.ArtifactSha256Hex, Hashing.Sha256Hex(first.ArtifactBytes));
    using var stream = new MemoryStream(first.ArtifactBytes);
    using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
    Assert.Single(document.Pages);
    var pdf = System.Text.Encoding.Latin1.GetString(first.ArtifactBytes);
    Assert.StartsWith("%PDF-", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/JavaScript", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/OpenAction", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/Launch", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/EmbeddedFile", pdf, StringComparison.Ordinal);
  }

  [Fact]
  public async Task MappingPlanAndPackage_AreScopedDeterministicAndReviewGated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId,
      fixture.EngagementId, fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "package-complete");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var incomplete = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping")
        ]));
      Assert.False(incomplete.Succeeded);
      Assert.Equal("mapping.incomplete", incomplete.ErrorCode);

      var sectionMismatch = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "LIABILITIES", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.False(sectionMismatch.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, sectionMismatch.ErrorCode);
      Assert.Contains("statement sections", sectionMismatch.Message!, StringComparison.OrdinalIgnoreCase);
    }

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.True(created.Succeeded);
      mappingId = created.Value;
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Preparer, "AccountingReviewer"));
      await db.SaveChangesAsync();
      var selfApproval = await FinancialStatementService.ApproveMappingAsync(db, preparer, mappingId, 1);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfApproval.ErrorCode);
      await db.Engagements.Where(x => x.Id == fixture.EngagementId).ExecuteUpdateAsync(x => x
        .SetProperty(e => e.PeriodStart, "2026-01-01").SetProperty(e => e.PeriodEnd, "2026-12-31")
        .SetProperty(e => e.ServiceRoute, "FinancialStatementAudit")
        .SetProperty(e => e.ServiceProfileId, "Synthetic annual reference"));
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Reviewer, "Manager")); await db.SaveChangesAsync();
      var draftReference = await AuditSphereOps.Application.Acceptance.AnnualEngagementPreparationWorkspace.StateAsync(db,
        Actor(fixture.Reviewer, "Manager"), fixture.ClientId, fixture.EngagementId);
      Assert.True(draftReference.Succeeded, draftReference.Message);
      Assert.Empty(draftReference.Value!.HistoricalMappings);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var annual = await AuditSphereOps.Application.Acceptance.AnnualEngagementPreparationWorkspace.StateAsync(db,
        Actor(fixture.Reviewer, "Manager"), fixture.ClientId, fixture.EngagementId);
      Assert.True(annual.Succeeded, annual.Message);
      Assert.Equal(mappingId, Assert.Single(annual.Value!.HistoricalMappings).Id);
      Assert.Equal("tax-v1", annual.Value.HistoricalMappings[0].TaxonomyVersion);
      Assert.Single(await db.MappingVersions.ToListAsync());
      var relationshipManager = User(fixture.FirmId); db.Users.Add(relationshipManager);
      db.RoleGrants.Add(Grant(fixture.FirmId, relationshipManager, "RelationshipManager")); await db.SaveChangesAsync();
      var restricted = await AuditSphereOps.Application.Acceptance.AnnualEngagementPreparationWorkspace.StateAsync(db,
        Actor(relationshipManager, "RelationshipManager"), fixture.ClientId, fixture.EngagementId);
      Assert.True(restricted.Succeeded, restricted.Message);
      Assert.Equal("RESTRICTED", restricted.Value!.HistoricalMappingState);
      Assert.Empty(restricted.Value.HistoricalMappings);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1");
    FinancialPackageBuildResult first;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      first = built.Value!;
      Assert.Equal(AccountingPackageStates.PackageReviewRequired, first.Status);
      Assert.Equal(100m, first.StatementTotals["ASSETS"]);
      Assert.Equal(-100m, first.StatementTotals["INCOME"]);
      var package = await db.FinancialPackages.SingleAsync(x => x.Id == first.PackageId);
      Assert.Equal(fixture.PeriodId, package.PeriodId);
      Assert.Equal(fixture.BookId, package.BookId);
      Assert.Equal("STATUTORY", package.Basis);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Reviewer, "Reviewer"));
      var snapshotId = await db.AdjustedTrialBalanceSnapshots.Where(x => x.AdjustmentPlanId == planId).Select(x => x.Id).SingleAsync();
      var journal = await db.AdjustmentJournals.SingleAsync(x => x.JournalNumber == "AJ-001" && x.Revision == 1);
      var reflectionId = await db.JournalSourceReconciliations.Where(x => x.BaseDatasetId == fixture.DatasetId && x.LogicalJournalNumber == "AJ-001")
        .Select(x => x.Id).SingleAsync();
      var differenceId = Guid.NewGuid();
      db.AuditDifferences.Add(new AuditDifference
      {
        Id = differenceId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        AccountArea = "Revenue", DifferenceType = "KNOWN", Description = "Journal impact classification", Amount = -10m,
        Currency = "QAR", CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var linked = await AuditFieldworkService.LinkDifferenceToJournalAsync(db,
        new ActorContext(fixture.Reviewer.Id, fixture.FirmId, fixture.Reviewer.SessionEpoch, ["Reviewer"]),
        new LinkDifferenceToJournalRequest(differenceId, journal.Id, journal.Revision, reflectionId, snapshotId,
          AuditDifferenceCorrectionStates.Agreed));
      Assert.True(linked.Succeeded, linked.Message);
      var saved = await db.AuditDifferences.SingleAsync(x => x.Id == differenceId);
      using var impact = System.Text.Json.JsonDocument.Parse(saved.JournalImpactJson!);
      var classification = impact.RootElement.GetProperty("Classification");
      Assert.Equal("journal-impact.v2", impact.RootElement.GetProperty("Schema").GetString());
      Assert.Equal("MAPPED", classification.GetProperty("Status").GetString());
      Assert.Equal(-10m, classification.GetProperty("ProfitEffect").GetDecimal());
      Assert.Equal(0m, classification.GetProperty("EquityEffect").GetDecimal());
      Assert.Equal("INCOME", classification.GetProperty("StatementEffects")[1].GetProperty("StatementSection").GetString());
      Assert.Equal("UNSPECIFIED", classification.GetProperty("DisclosureEffects")[0].GetProperty("DisclosureArea").GetString());
      Assert.Equal(Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(saved.JournalImpactJson!)), saved.JournalImpactHash);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var periodMismatch = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        request with { TemplateVersion = "template-period-mismatch", PeriodStart = "2026-02-01" });
      Assert.False(periodMismatch.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, periodMismatch.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var repeat = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(repeat.Succeeded);
      Assert.Equal(first.PackageId, repeat.Value!.PackageId);
      Assert.Equal(first.CalculationHash, repeat.Value.CalculationHash);
      Assert.Equal(2, await db.AdjustedTrialBalanceRows.CountAsync());
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == first.PackageId).ToListAsync();
      Assert.Contains(checks, x => x.Code == "SUPPLEMENTARY_INFORMATION" && !x.Passed);
    }

    var durableRequest = request with { TemplateVersion = "template-durable-v1" };
    var operationFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var operationStore = new PostgresOperationStore(operationFactory);
    var operationHandler = new FinancialPackageBuildHandler();
    var workerOptions = new WorkerOptions(fixture.FirmId, "Test");
    Guid operationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(
        db, preparer, durableRequest, operationStore, operationHandler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    var worker = new WorkerHost(
      new OperationDispatcher(operationStore, new DurableOperationRegistry([operationHandler], workerOptions), workerOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    Assert.False(await worker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      var durablePackage = await db.FinancialPackages.SingleAsync(x => x.TemplateVersion == "template-durable-v1");
      Assert.Equal(durablePackage.Id.ToString("D"), operation.ResultIdentity);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == operationId && x.Kind == "financial.package-built.v1"));

      var retry = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(
        db, preparer, durableRequest, operationStore, operationHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(operationId, retry.Value);
    }

    var completeRequest = request with
    {
      TemplateVersion = "template-v2",
      SupplementaryInformation = new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash receipts", 100m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_RECEIPTS", SourceReferences: ["bank-ledger-template-v2"], ClassificationRationale: "Synthetic receipts agreed to source.",
          SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
        [new DisclosureInput("CASH_POLICY", "Cash and cash equivalents are presented at face value."),
         new DisclosureInput("COMMITMENTS", string.Empty, NotApplicable: true, Rationale: "No commitments were identified in the supplied management information.")],
        [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, 100m, 0m, 0m, 0m, 90m, "equity-schedule-1"),
         new EquityLineInput("RESERVE", "General reserve", 0m, 0m, 0m, 0m, 0m, 10m, "reserve-schedule-1")],
        NoteLines: [new NoteLineInput("CASH_NOTE", "CASH", 100m, "note-schedule-1"),
          new NoteLineInput("REVENUE_NOTE", "REVENUE", -100m, "revenue-note-schedule-1",
            [new NoteSourceAllocationInput(source.Receivable, -100m)])],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-template-v2"], ClosingCashEvidenceReferences: ["closing-cash-template-v2"],
        EquityTransfers: [new EquityComponentTransferInput("TRANSFER-001", "RETAINED_EARNINGS", "RESERVE", 10m, "reserve-transfer-approval")])
    };
    var wrongScopeNote = completeRequest with
    {
      SupplementaryInformation = completeRequest.SupplementaryInformation! with
      {
        NoteLines = [new NoteLineInput("REVENUE_NOTE", "REVENUE", -100m, "revenue-note-schedule-1",
          [new NoteSourceAllocationInput(Guid.NewGuid(), -100m)])]
      }
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var refused = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, wrongScopeNote);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
    }
    Guid completePackageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageBuildAsync(db, preparer,
        completeRequest, operationStore, operationHandler);
      Assert.True(queued.Succeeded, queued.Message);
      operationId = queued.Value;
    }
    Assert.True(await worker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == operationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      completePackageId = Guid.Parse(operation.ResultIdentity!);
      var completePackage = await db.FinancialPackages.SingleAsync(x => x.Id == completePackageId);
      Assert.Equal(AccountingPackageStates.PackageValidated, completePackage.Status);
      Assert.NotEqual(first.CalculationHash, completePackage.CalculationHash);
      Assert.Equal(1, await db.FinancialPackageCashFlowLines.CountAsync(x => x.FinancialPackageId == completePackageId));
      Assert.Equal(2, await db.FinancialPackageDisclosures.CountAsync(x => x.FinancialPackageId == completePackageId));
      Assert.Equal(2, await db.FinancialPackageEquityLines.CountAsync(x => x.FinancialPackageId == completePackageId));
      var transfer = await db.FinancialPackageEquityTransfers.SingleAsync(x => x.FinancialPackageId == completePackageId);
      Assert.Equal("RETAINED_EARNINGS", transfer.SourceLineCode);
      Assert.Equal("RESERVE", transfer.DestinationLineCode);
      Assert.Equal(10m, transfer.Amount);
      Assert.Equal("reserve-transfer-approval", transfer.EvidenceReference);
      var savedNotes = await db.FinancialPackageNoteLines.Where(x => x.FinancialPackageId == completePackageId).ToListAsync();
      Assert.Equal(2, savedNotes.Count);
      var linkedNote = savedNotes.Single(x => x.NoteCode == "REVENUE_NOTE");
      using (var lineage = JsonDocument.Parse(linkedNote.SourceLineageJson))
      {
        var link = Assert.Single(lineage.RootElement.EnumerateArray());
        Assert.Equal(source.Receivable, link.GetProperty("GeneralLedgerLineId").GetGuid());
        Assert.Equal(source.BatchId, link.GetProperty("SourceImportBatchId").GetGuid());
        Assert.Matches("^[a-f0-9]{64}$", link.GetProperty("SourceLineHash").GetString());
      }
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_note_lines SET source_lineage_json = '[]'::jsonb WHERE id = {linkedNote.Id}"));
      var historicalPackage = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == first.PackageId);
      Assert.Equal(request.TemplateVersion, historicalPackage.TemplateVersion);
      Assert.Equal(FinancialStatementCalculator.CalculationEngineVersion, historicalPackage.CalculationEngineVersion);
      Assert.Equal(first.CalculationHash, historicalPackage.CalculationHash);
      var currentPackage = await db.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == completePackageId);
      Assert.Equal(completeRequest.TemplateVersion, currentPackage.TemplateVersion);
      Assert.Equal(FinancialStatementCalculator.CalculationEngineVersion, currentPackage.CalculationEngineVersion);
      Assert.NotEqual(historicalPackage.Id, completePackageId);
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == completePackageId).ToListAsync();
      Assert.All(checks.Where(x => x.Code is "CASH_FLOW_RECONCILED" or "DISCLOSURES_COMPLETE" or "SUPPLEMENTARY_INFORMATION"), x => Assert.True(x.Passed));
      Assert.Contains(checks, x => x.Code == "STATEMENT_CROSS_CAST" && x.Passed);
      Assert.Contains(checks, x => x.Code == "ACCOUNTING_EQUATION" && x.Passed);
      Assert.Contains(checks, x => x.Code == "EQUITY_ROLLFORWARD" && x.Passed);
      Assert.Contains(checks, x => x.Code == "EQUITY_PROFIT" && x.Passed);
      Assert.Contains(checks, x => x.Code == "COMPARATIVE_CONSISTENCY" && x.Passed);
      Assert.Contains(checks, x => x.Code == "NOTE_TO_FACE_TOTALS" && x.Passed);

      var cashFlowLineId = await db.FinancialPackageCashFlowLines.Where(x => x.FinancialPackageId == completePackageId).Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_cash_flow_lines SET amount = amount + 1 WHERE id = {cashFlowLineId}"));
      var equityLineId = await db.FinancialPackageEquityLines.Where(x => x.FinancialPackageId == completePackageId && x.LineCode == "RETAINED_EARNINGS")
        .Select(x => x.Id).SingleAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_equity_lines SET closing_amount = closing_amount + 1 WHERE id = {equityLineId}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_equity_transfers SET amount = amount + 1 WHERE id = {transfer.Id}"));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invalidTransfer = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        completeRequest with { TemplateVersion = "template-v4", SupplementaryInformation = completeRequest.SupplementaryInformation! with
        {
          EquityTransfers = [new EquityComponentTransferInput("TRANSFER-BAD", "UNKNOWN", "RESERVE", 10m, "approval")]
        } });
      Assert.False(invalidTransfer.Succeeded);
      Assert.Equal("package.supplementary.invalid", invalidTransfer.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invalidSupplement = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        completeRequest with { TemplateVersion = "template-v3", SupplementaryInformation = completeRequest.SupplementaryInformation! with { CashEnding = 200m } });
      Assert.False(invalidSupplement.Succeeded);
      Assert.Equal("package.supplementary.invalid", invalidSupplement.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lineId = await db.FinancialPackageLines.Select(x => x.Id).FirstAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_lines SET amount = amount + 1 WHERE id = {lineId}"));
    }
  }

  [Fact]
  public async Task AdjustmentInstructions_RequireEvidenceAndCarryExactSourceLineage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    Guid journalId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == fixture.DatasetId);
      dataset.RawFileSha256Hex = new string('a', 64);
      dataset.NormalizedDatasetDigest = new string('b', 64);
      dataset.ImportProfileVersion = "trial-balance.v1";
      dataset.LegalEntityKey = "TEST-ENTITY";
      await db.SaveChangesAsync();

      var created = await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-EXPORT-001", [("=1000", 10m, 0m), ("4000", 0m, 10m)]);
      Assert.True(created.Succeeded, created.Message);
      journalId = created.Value;

      var blocked = await AdjustmentJournalService.BuildInstructionExportAsync(db, reviewer, journalId);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

      var decision = await AdjustmentJournalService.RecordManagementDecisionAsync(db, reviewer,
        new AdjustmentJournalService.ManagementDecisionRequest(journalId, ManagementDecisionStates.Accepted,
          ManagementDecisionEvidenceModes.Offline, "review-note-1"));
      Assert.True(decision.Succeeded, decision.Message);

      var exported = await AdjustmentJournalService.BuildInstructionExportAsync(db, reviewer, journalId);
      Assert.True(exported.Succeeded, exported.Message);
      Assert.Equal("auditsphere-adjustment-AJ-EXPORT-001-r1.csv", exported.Value!.FileName);
      Assert.Contains("NOT_PROOF_OF_EXTERNAL_POSTING", exported.Value.Csv);
      Assert.Contains("source_raw_sha256", exported.Value.Csv);
      Assert.Contains("FY2026", exported.Value.Csv);
      Assert.Contains("\"'=1000\"", exported.Value.Csv);
      Assert.Contains("review-note-1", exported.Value.Csv);
    }
  }

  [Fact]
  public async Task MappingApplicability_BindsApprovedClientChartVersion()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var allocations = new[]
    {
      new MappingAllocationInput("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
      new MappingAllocationInput("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
    };
    var chartId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientChartVersions.Add(new ClientChartVersion
      {
        Id = chartId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, Version = 1,
        SourceScope = "LEDGER-2026", Status = AccountingWorkflowStates.Approved,
        EffectiveFrom = new DateOnly(2026, 1, 1), EffectiveTo = new DateOnly(2026, 12, 31),
        CreatedByUserId = fixture.Preparer.Id, PublishedByUserId = fixture.Reviewer.Id,
        PublishedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var missingApplicability = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", allocations));
      Assert.False(missingApplicability.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, missingApplicability.ErrorCode);

      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", allocations, chartId));
      Assert.True(created.Succeeded, created.Message);
      Assert.Equal(chartId, await db.MappingVersions.Where(x => x.Id == created.Value).Select(x => x.ClientChartVersionId).SingleAsync());
    }
  }

  [Fact]
  public async Task ZeroAdjustmentPlan_ProducesSourceEquivalentPackage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      mappingId = (await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]))).Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId, [])).Value;
    FinalizedPlan finalizedPlan;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var finalized = await AdjustmentPlanService.FinalizeAsync(db, preparer, planId);
      Assert.True(finalized.Succeeded);
      finalizedPlan = finalized.Value!;
      Assert.Equal(0, finalizedPlan.AppliedJournalCount);
      Assert.Equal(100m, finalizedPlan.Balances["1000"]);
      Assert.Equal(-100m, finalizedPlan.Balances["4000"]);
    }

    var legacyPackageId = Guid.CreateVersion7();
    var legacyPackageHash = Hashing.Sha256Hex("legacy-package-v0");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var snapshotId = Guid.CreateVersion7();
      db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
      {
        Id = snapshotId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        BaseDatasetId = fixture.DatasetId, AdjustmentPlanId = planId, Currency = "QAR",
        ResultHash = finalizedPlan.ResultHash, CreatedByUserId = fixture.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AdjustedTrialBalanceRows.AddRange(finalizedPlan.Balances.Select(x => new AdjustedTrialBalanceRow
      {
        Id = Guid.CreateVersion7(), SnapshotId = snapshotId, AccountCode = x.Key, Amount = x.Value, Currency = "QAR"
      }));
      db.FinancialPackages.Add(new FinancialPackage
      {
        Id = legacyPackageId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        AdjustedDatasetId = snapshotId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
        PeriodId = fixture.PeriodId, BookId = fixture.BookId, Basis = "STATUTORY", Framework = "IFRS",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", TaxonomyVersion = "tax-v1",
        TemplateVersion = "legacy-template-v0", CalculationEngineVersion = "legacy-engine-v0", CalculationHash = legacyPackageHash,
        Currency = "QAR", Revision = 4, Generation = 9, Status = AccountingPackageStates.PackageReviewRequired,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    await using var packageDb = new AuditSphereDbContext(pg.Options);
    var package = await FinancialStatementService.BuildFinancialPackageAsync(packageDb, preparer,
      new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1"));
    Assert.True(package.Succeeded);
    Assert.Equal(100m, package.Value!.StatementTotals["ASSETS"]);
    Assert.Equal(-100m, package.Value.StatementTotals["INCOME"]);
    Assert.Equal(2, await packageDb.AdjustedTrialBalanceRows.CountAsync());
    var legacy = await packageDb.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == legacyPackageId);
    Assert.Equal("legacy-template-v0", legacy.TemplateVersion);
    Assert.Equal("legacy-engine-v0", legacy.CalculationEngineVersion);
    Assert.Equal(legacyPackageHash, legacy.CalculationHash);
    var canonical = await packageDb.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == package.Value.PackageId);
    Assert.Equal("template-v1", canonical.TemplateVersion);
    Assert.Equal(FinancialStatementCalculator.CalculationEngineVersion, canonical.CalculationEngineVersion);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void PackageRounding_ConservesEachSourceBalanceAndRecordsResidual()
  {
    var lines = FinancialStatementCalculator.BuildPackageLines(
      new Dictionary<string, decimal> { ["1000"] = 0.01m },
      [new("1000", "A", "ASSETS", 0.333333m, "split"),
       new("1000", "B", "ASSETS", 0.333333m, "split"),
       new("1000", "C", "ASSETS", 0.333334m, "split")], "QAR");

    Assert.Equal(0.01m, lines.Sum(x => x.Amount));
    Assert.Equal(0.003334m, lines.Single(x => x.DestinationCode == "C").Amount);
    Assert.Equal(0.000001m, lines.Sum(x => x.RoundingResidual));
  }

  [Fact]
  public async Task FinancialPackage_RendersDeterministicArtifact_WithVerifiableSha256()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(
          fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
           new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]));
      mappingId = created.Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(
      planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1",
      new FinancialSupplementaryInformation(
        0m, 100m,
        [new CashFlowLineInput("OPERATING", "Cash collections", 100m, MovementDate: new DateOnly(2026, 6, 30),
          ActivityCode: "CASH_COLLECTIONS", SourceReferences: ["bank-ledger-template-v1"], ClassificationRationale: "Synthetic collections agreed to source.")],
        [new DisclosureInput("NOTE_1", "Summary of significant accounting policies."),
         new DisclosureInput("NOTE_2", string.Empty, NotApplicable: true, Rationale: "No discontinued operations.")],
        CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-template-v1"], ClosingCashEvidenceReferences: ["closing-cash-template-v1"]));

    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      packageId = built.Value!.PackageId;
    }

    var expectedArtifactSha = string.Empty;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var artifact1 = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(artifact1.Succeeded);
      Assert.NotNull(artifact1.Value);
      expectedArtifactSha = artifact1.Value.ArtifactSha256Hex;
      Assert.NotEmpty(artifact1.Value.ArtifactBytes);
      var storedArtifact = await db.FinancialPackageArtifacts.SingleAsync(x => x.FinancialPackageId == packageId);
      Assert.Equal(storedArtifact.Id, artifact1.Value.ArtifactId);
      Assert.Equal(expectedArtifactSha, storedArtifact.ArtifactSha256Hex);
      Assert.Equal(artifact1.Value.ArtifactBytes, storedArtifact.ArtifactBytes);
      Assert.Equal(artifact1.Value.ArtifactSha256Hex, Hashing.Sha256Hex(artifact1.Value.ArtifactBytes));
      Assert.Contains("=== AUDITSPHEREOPS FINANCIAL STATEMENT PACKAGE ===", artifact1.Value.RenderedText);
      Assert.Contains("Mapping Version ID:", artifact1.Value.RenderedText);
      Assert.Contains("NOTE_1: Summary of significant accounting policies.", artifact1.Value.RenderedText);
      Assert.Contains("[NOT APPLICABLE: No discontinued operations.]", artifact1.Value.RenderedText);

      // Repeat render produces byte-for-byte identical output and digest
      var artifact2 = await FinancialStatementService.RenderPackageArtifactAsync(db, reviewer, packageId);
      Assert.True(artifact2.Succeeded);
      Assert.Equal(artifact1.Value.ArtifactSha256Hex, artifact2.Value!.ArtifactSha256Hex);
      Assert.Equal(artifact1.Value.ArtifactBytes, artifact2.Value.ArtifactBytes);

      foreach (var version in new[]
      {
        FinancialPackageArtifactVersions.Workbook,
        FinancialPackageArtifactVersions.ControlledWorkbook,
        FinancialPackageArtifactVersions.Word,
        FinancialPackageArtifactVersions.Pdf
      })
      {
        var office1 = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, preparer, packageId, version);
        var office2 = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, reviewer, packageId, version);
        Assert.True(office1.Succeeded, office1.Message);
        Assert.True(office2.Succeeded, office2.Message);
        Assert.Equal(office1.Value!.ArtifactId, office2.Value!.ArtifactId);
        Assert.Equal(office1.Value.ArtifactBytes, office2.Value.ArtifactBytes);
        Assert.Equal(office1.Value.ArtifactSha256Hex, Hashing.Sha256Hex(office1.Value.ArtifactBytes));
      }
      Assert.Equal(5, await db.FinancialPackageArtifacts.CountAsync(x => x.FinancialPackageId == packageId));

      // Denied to client without accounting roles
      var clientUser = User(fixture.FirmId);
      db.Users.Add(clientUser);
      db.RoleGrants.Add(Grant(fixture.FirmId, clientUser, "ClientUser"));
      await db.SaveChangesAsync();
      var clientActor = Actor(clientUser, "ClientUser");

      var denied = await FinancialStatementService.RenderPackageArtifactAsync(db, clientActor, packageId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      var deniedOffice = await FinancialStatementService.RenderPackageOfficeArtifactAsync(
        db, clientActor, packageId, FinancialPackageArtifactVersions.Workbook);
      Assert.False(deniedOffice.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, deniedOffice.ErrorCode);
    }

    var renderFactory = new OperationContextFactory(new TestDbContextFactory(pg.Options));
    var renderStore = new PostgresOperationStore(renderFactory);
    var renderHandler = new FinancialPackageRenderHandler();
    var renderOptions = new WorkerOptions(fixture.FirmId, "Test");
    Guid renderOperationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var queued = await FinancialStatementService.EnqueueFinancialPackageRenderAsync(
        db, preparer, packageId, renderStore, renderHandler);
      Assert.True(queued.Succeeded, queued.Message);
      renderOperationId = queued.Value;
    }
    var renderWorker = new WorkerHost(
      new OperationDispatcher(renderStore, new DurableOperationRegistry([renderHandler], renderOptions), renderOptions),
      Array.Empty<IPendingOperationDiscovery>(), NullLogger<WorkerHost>.Instance);
    Assert.True(await renderWorker.ProcessNextAsync());
    Assert.False(await renderWorker.ProcessNextAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.SingleAsync(x => x.Id == renderOperationId);
      Assert.Equal(OperationState.COMPLETED, operation.Status);
      Assert.Equal(packageId.ToString("D"), operation.ResultIdentity);
      Assert.Equal(expectedArtifactSha, operation.ResultDigest);
      Assert.True(await db.OperationEvents.AnyAsync(x => x.OperationId == renderOperationId && x.Kind == "financial.package-rendered.v1"));

      var retry = await FinancialStatementService.EnqueueFinancialPackageRenderAsync(
        db, preparer, packageId, renderStore, renderHandler);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(renderOperationId, retry.Value);
    }
  }

  [Fact]
  public async Task StatementPresentation_RequiresAPublishedLayout_AndIsScopedAndRepeatable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    // The cash receipt is linked to an accepted ledger line, so the package validates and the presentation can be checked.
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId, fixture.EngagementId,
      fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "layout");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(
          fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
           new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]));
      mappingId = created.Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, new BuildFinancialPackageRequest(
        planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1",
        new FinancialSupplementaryInformation(
          0m, 100m,
          [new CashFlowLineInput("OPERATING", "Cash collections", 100m, MovementDate: new DateOnly(2026, 6, 30),
            ActivityCode: "CASH_COLLECTIONS", SourceReferences: ["bank-ledger-layout-test"], ClassificationRationale: "Synthetic collections agreed to source.",
            SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -100m)])],
          [new DisclosureInput("NOTE_1", "Summary of significant accounting policies.")],
          CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-layout-test"], ClosingCashEvidenceReferences: ["closing-cash-layout-test"])));
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
    }

    // Create a second package with source-derived supplementary values so the seal assertion exercises
    // the permitted path through the package's real cash and equity validation gates.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var sourceLines = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync();
      var signedProfitOrLoss = sourceLines.Where(x => x.StatementSection is "INCOME" or "REVENUE" or "EXPENSE" or "EXPENSES" or "P&L" or "P_AND_L" or "PROFIT_LOSS")
        .Sum(x => x.Amount);
      var closingCash = sourceLines.Where(x => x.DestinationCode == "CASH").Sum(x => x.Amount);
      var completed = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "presentation-seal-v2",
          new FinancialSupplementaryInformation(0m, closingCash,
            [new CashFlowLineInput("OPERATING", "Cash movement", closingCash, MovementDate: new DateOnly(2026, 6, 30),
              ActivityCode: "CASH_MOVEMENT", SourceReferences: ["synthetic-cash-movement"], ClassificationRationale: "Synthetic test input derived from the package cash balance.",
              SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -closingCash)])],
            [new DisclosureInput("NOTE_1", "Synthetic policy disclosure for the test.")],
            [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, -signedProfitOrLoss, 0m, 0m, 0m,
              -signedProfitOrLoss, "synthetic-equity-movement")],
            CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["synthetic-opening-cash"],
            ClosingCashEvidenceReferences: ["synthetic-closing-cash"],
            NoteLines: [new NoteLineInput("NOTE_1", "CASH", closingCash, "synthetic-note-source")],
            FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "synthetic-functional-currency-cash",
              "The synthetic selected cash population has no foreign-currency effect."))));
      Assert.True(completed.Succeeded, completed.Message);
      packageId = completed.Value!.PackageId;
      var sourceReview = await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null);
      Assert.True(sourceReview.Succeeded, sourceReview.Message);
    }

    static StatementLineDefinition Heading(string code, string label, string section, int order) =>
      new(code, label, StatementLayoutLineKinds.Heading, section, order);
    static StatementLineDefinition Mapped(string code, string label, string section, int order, string node, string sign) =>
      new(code, label, StatementLayoutLineKinds.MappedTaxonomyBalance, section, order, sign, TaxonomyNodeCode: node);
    static StatementLineDefinition Sum(string code, string label, string section, int order, string sign, params string[] children) =>
      new(code, label, StatementLayoutLineKinds.SumChildLines, section, order, sign, ChildLineCodes: children, IsSubtotal: true);
    IReadOnlyList<StatementLineDefinition> Lines() =>
    [
      Heading("H_FP", "Assets", StatementLayoutSections.FinancialPosition, 1),
      Mapped("CASH", "Cash", StatementLayoutSections.FinancialPosition, 2, "CASH", StatementLayoutDisplaySigns.Signed),
      Sum("TA", "Total assets", StatementLayoutSections.FinancialPosition, 3, StatementLayoutDisplaySigns.Signed, "CASH"),
      Heading("H_PL", "Revenue", StatementLayoutSections.ProfitOrLoss, 1),
      Mapped("REV", "Revenue", StatementLayoutSections.ProfitOrLoss, 2, "REVENUE", StatementLayoutDisplaySigns.Inverted),
    ];

    Guid layoutId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Without a published layout the presentation is refused rather than guessed from unpublished work.
      var before = await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId);
      Assert.Equal(ErrorCodes.Accounting.MappingIncomplete, before.ErrorCode);

      var draft = await StatementLayoutService.SaveLayoutDraftAsync(db, preparer,
        new StatementLayoutDraftRequest(fixture.ClientId, "IFRS", "Presentation test layout", Lines()));
      Assert.True(draft.Succeeded, draft.Message);
      layoutId = draft.Value!.LayoutVersionId;
      Assert.True((await StatementLayoutService.ValidateLayoutAsync(db, preparer, layoutId)).Value!.IsValid);

      // An explicitly named draft is never presented, and a preparer without publishing authority cannot publish it.
      Assert.Equal(ErrorCodes.Accounting.MappingIncomplete,
        (await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId, layoutId)).ErrorCode);
      Assert.False((await StatementLayoutService.PublishLayoutAsync(db, preparer, layoutId, "Self publish")).Succeeded);
      Assert.True((await StatementLayoutService.PublishLayoutAsync(db, reviewer, layoutId, "Reviewed for the presentation test")).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var presented = await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId);
      Assert.True(presented.Succeeded, presented.Message);
      var p = presented.Value!;
      Assert.Equal(FinancialStatementPresentationPolicy.DraftStatus, p.Status);
      Assert.True(p.CanPin);
      Assert.Equal(layoutId, p.Manifest.LayoutVersionId);
      Assert.Equal(packageId, p.Manifest.PackageId);
      Assert.Equal(new[] { "FINANCIAL_POSITION", "PROFIT_OR_LOSS", "CHANGES_IN_EQUITY", "CASH_FLOWS" },
        p.Statements.Select(statement => statement.Code).ToArray());
      var knownUnapprovedGaps = new[]
      {
        "NOTE_CONTENT", "EQUITY_PROFIT_CROSS_CAST", "EQUITY_OCI_CROSS_CAST", "EQUITY_SFP_CROSS_CAST",
        "EQUITY_SOURCE_LINEAGE", "CASH_FLOW_REQUIRED_SECTIONS", "CASH_FLOW_BRIDGE"
      };
      Assert.Equal(knownUnapprovedGaps.OrderBy(x => x, StringComparer.Ordinal),
        p.Checks.Where(check => !check.Passed).Select(check => check.Code).OrderBy(x => x, StringComparer.Ordinal));
      Assert.All(p.Checks.Where(check => check.Passed), check => Assert.True(check.Passed, $"{check.Code}: {check.Detail}"));
      // The presentation is a read of the persisted package: each figure equals the stored line, shown with its sign.
      var persistedCash = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId && x.DestinationCode == "CASH").SumAsync(x => x.Amount);
      var persistedRevenue = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId && x.DestinationCode == "REVENUE").SumAsync(x => x.Amount);
      var cash = p.Statements.SelectMany(x => x.Rows).Single(x => x.Code == "CASH");
      var revenue = p.Statements.SelectMany(x => x.Rows).Single(x => x.Code == "REV");
      Assert.Equal((persistedCash, -persistedRevenue), (cash.Current, revenue.Current));
      Assert.Null(cash.Prior);
      var note = Assert.Single(p.Notes!, x => x.Code == "NOTE_1");
      Assert.Equal(PresentationDisclosureStates.PreparerSupplied, note.Current.State);
      Assert.Equal("Synthetic policy disclosure for the test.", note.Current.Text);
      Assert.Equal(PresentationDisclosureStates.NotSupplied, note.Comparative.State);
      var noteAmount = Assert.Single(note.Amounts);
      Assert.Equal(persistedCash, noteAmount.Current);
      Assert.Equal("synthetic-note-source", noteAmount.CurrentEvidenceReference);

      // Pin the exact package, note sources and layout version. Repeating the same selection is idempotent.
      var pinnedResult = await FinancialStatementService.PinPresentationAsync(db, preparer, packageId, layoutId);
      Assert.True(pinnedResult.Succeeded, pinnedResult.Message);
      var pin = pinnedResult.Value!;
      Assert.Equal(FinancialStatementPresentationRevisionStatuses.UnapprovedDraft, pin.Status);
      Assert.Equal(layoutId, pin.LayoutVersionId);
      Assert.Equal(p.Manifest.PeriodId, pin.PeriodId);
      Assert.Equal(p.Manifest.TaxonomyVersion, pin.TaxonomyVersion);
      Assert.Equal(p.Manifest.Currency, pin.Currency);
      Assert.Equal(p.Manifest.NoteSourceManifestHash, pin.NoteSourceManifestHash);
      Assert.Equal(64, pin.NoteSourceManifestHash.Length);
      var repeatedPin = await FinancialStatementService.PinPresentationAsync(db, preparer, packageId, layoutId);
      Assert.True(repeatedPin.Succeeded, repeatedPin.Message);
      Assert.Equal(pin.RevisionId, repeatedPin.Value!.RevisionId);

      // Publish a successor after preview. An export must retain the exact layout version the user reviewed.
      var successorLines = Lines().ToList();
      successorLines[1] = successorLines[1] with { Label = "Cash and cash equivalents · successor layout" };
      var successorDraft = await StatementLayoutService.SaveLayoutDraftAsync(db, preparer,
        new StatementLayoutDraftRequest(fixture.ClientId, "IFRS", "Presentation test layout", successorLines, layoutId));
      Assert.True(successorDraft.Succeeded, successorDraft.Message);
      Assert.True((await StatementLayoutService.ValidateLayoutAsync(db, preparer, successorDraft.Value!.LayoutVersionId)).Value!.IsValid);
      var successor = await StatementLayoutService.PublishLayoutAsync(db, reviewer, successorDraft.Value.LayoutVersionId,
        "Verify an already-viewed presentation remains pinned to its original layout version");
      Assert.True(successor.Succeeded, successor.Message);
      Assert.NotEqual(layoutId, successor.Value!.LayoutVersionId);
      var latest = await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId);
      Assert.True(latest.Succeeded, latest.Message);
      Assert.Equal(successor.Value.LayoutVersionId, latest.Value!.Manifest.LayoutVersionId);

      var pinnedRead = await FinancialStatementService.GetPinnedPresentationAsync(db, reviewer, packageId, pin.RevisionId);
      Assert.True(pinnedRead.Succeeded, pinnedRead.Message);
      Assert.Equal(layoutId, pinnedRead.Value!.Manifest.LayoutVersionId);
      Assert.NotEqual(successor.Value.LayoutVersionId, pinnedRead.Value.Manifest.LayoutVersionId);
      Assert.Equal(pin.NoteSourceManifestHash, pinnedRead.Value.Manifest.NoteSourceManifestHash);

      var canonicalArtifact = await FinancialStatementService.RenderPackageArtifactAsync(db, preparer, packageId);
      Assert.True(canonicalArtifact.Succeeded, canonicalArtifact.Message);
      foreach (var format in new[]
      {
        FinancialPackageArtifactVersions.Workbook,
        FinancialPackageArtifactVersions.Word,
        FinancialPackageArtifactVersions.Pdf
      })
      {
        var rendered = await FinancialStatementService.RenderPackageOfficeArtifactAsync(db, preparer, packageId, format);
        Assert.True(rendered.Succeeded, rendered.Message);
      }
      var packageArtifactCount = await db.FinancialPackageArtifacts.CountAsync(x => x.FinancialPackageId == packageId);
      Assert.Equal(4, packageArtifactCount);
      var firstPinnedDocx = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.RevisionId, "docx");
      var repeatedPinnedDocx = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.RevisionId, "docx");
      Assert.True(firstPinnedDocx.Succeeded, firstPinnedDocx.Message);
      Assert.True(repeatedPinnedDocx.Succeeded, repeatedPinnedDocx.Message);
      Assert.Equal(firstPinnedDocx.Value!.ArtifactId, repeatedPinnedDocx.Value!.ArtifactId);
      Assert.Equal(firstPinnedDocx.Value.ArtifactSha256Hex, Hashing.Sha256Hex(firstPinnedDocx.Value.Bytes));
      Assert.Equal(firstPinnedDocx.Value.Bytes, repeatedPinnedDocx.Value.Bytes);
      Assert.Equal(packageArtifactCount + 1, await db.FinancialPackageArtifacts.CountAsync(x => x.FinancialPackageId == packageId));
      var persistedPinnedArtifact = await db.FinancialPackageArtifacts.SingleAsync(x => x.Id == firstPinnedDocx.Value.ArtifactId);
      Assert.Equal(FinancialPackageArtifactVersions.StatementPresentation(pin.RevisionId, "docx"), persistedPinnedArtifact.ArtifactVersion);
      Assert.Equal(firstPinnedDocx.Value.ArtifactSha256Hex, persistedPinnedArtifact.ArtifactSha256Hex);
      Assert.Equal(firstPinnedDocx.Value.Bytes, persistedPinnedArtifact.ArtifactBytes);
      var seal = await PackageSealService.SealPackageAsync(db, preparer, packageId);
      Assert.True(seal.Succeeded, seal.Message);
      Assert.Equal(5, seal.Value!.ArtifactCount); // four canonical artifacts plus the exact pinned statement DOCX
      var sealRecord = await db.FinancialPackageSeals.SingleAsync(x => x.Id == seal.Value.SealId);
      Assert.Equal("package-seal.v2", sealRecord.SealVersion);
      Assert.True((await PackageSealService.GetSealStatusAsync(db, reviewer, packageId)).Value!.IsSealed);
      using (var sealedManifest = JsonDocument.Parse(sealRecord.ArtifactManifestJson))
      {
        var entry = sealedManifest.RootElement.EnumerateArray().Single(x =>
            x.GetProperty(nameof(PackageArtifactManifestEntry.PresentationRevisionId)) is { ValueKind: JsonValueKind.String } revisionId &&
            revisionId.GetGuid() == pin.RevisionId);
        Assert.Equal(pin.RevisionHash, entry.GetProperty(nameof(PackageArtifactManifestEntry.PresentationRevisionHash)).GetString());
        Assert.Equal(layoutId, entry.GetProperty(nameof(PackageArtifactManifestEntry.LayoutVersionId)).GetGuid());
        Assert.Equal(pin.LayoutHash, entry.GetProperty(nameof(PackageArtifactManifestEntry.LayoutHash)).GetString());
        Assert.Equal(pin.NoteSourceManifestHash, entry.GetProperty(nameof(PackageArtifactManifestEntry.NoteSourceManifestHash)).GetString());
        Assert.Equal(firstPinnedDocx.Value.ArtifactSha256Hex, entry.GetProperty(nameof(PackageArtifactManifestEntry.Sha256Hex)).GetString());
      }
      Assert.Equal("docx", (await FinancialStatementService.ListPresentationPinsAsync(db, reviewer, packageId)).Value!.Single().ArtifactFormats.Single());

      using (var archive = new ZipArchive(new MemoryStream(firstPinnedDocx.Value.Bytes), ZipArchiveMode.Read))
      using (var documentXml = archive.GetEntry("word/document.xml")!.Open())
      {
        var renderedText = XDocument.Load(documentXml).Root!.Value;
        Assert.Contains($"Statement layout: {layoutId:D}", renderedText, StringComparison.Ordinal);
        Assert.Contains($"Note source manifest: {pin.NoteSourceManifestHash}", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain($"Statement layout: {successor.Value.LayoutVersionId:D}", renderedText, StringComparison.Ordinal);
      }

      // Both the immutable input snapshot and the exact rendered bytes are protected by database triggers.
      var updatePin = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_statement_presentation_revisions SET display_policy = {"tampered"} WHERE id = {pin.RevisionId}"));
      Assert.Equal("P0001", updatePin.SqlState);
      var updateBytes = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_artifacts SET artifact_sha256_hex = {new string('0', 64)} WHERE id = {firstPinnedDocx.Value.ArtifactId}"));
      Assert.Equal("P0001", updateBytes.SqlState);
      var deleteArtifact = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM financial_statement_presentation_artifacts WHERE id = {firstPinnedDocx.Value.ArtifactId}"));
      Assert.Equal("P0001", deleteArtifact.SqlState);

      // Explicit exports preserve the reviewed version and remain deterministic; unknown formats are refused.
      var first = await FinancialStatementService.RenderPresentationAsync(db, reviewer, packageId, "docx", layoutId);
      var second = await FinancialStatementService.RenderPresentationAsync(db, reviewer, packageId, "docx", layoutId);
      Assert.True(first.Succeeded && second.Succeeded);
      Assert.Equal(first.Value!.ArtifactSha256Hex, second.Value!.ArtifactSha256Hex);
      Assert.Equal(first.Value.Bytes, second.Value.Bytes);
      Assert.Equal("auditsphere-statement-presentation-" + packageId.ToString("D") + ".docx", first.Value.FileName);
      using (var archive = new ZipArchive(new MemoryStream(first.Value.Bytes), ZipArchiveMode.Read))
      using (var documentXml = archive.GetEntry("word/document.xml")!.Open())
      {
        var renderedText = XDocument.Load(documentXml).Root!.Value;
        Assert.Contains($"Statement layout: {layoutId:D}", renderedText, StringComparison.Ordinal);
        Assert.DoesNotContain($"Statement layout: {successor.Value.LayoutVersionId:D}", renderedText, StringComparison.Ordinal);
      }
      Assert.Equal(ErrorCodes.Accounting.PackageInvalid,
        (await FinancialStatementService.RenderPresentationAsync(db, reviewer, packageId, "html")).ErrorCode);

      // A caller with no grant on this package learns nothing about it.
      var stranger = Actor(User(fixture.FirmId), "AccountingPreparer");
      Assert.False((await FinancialStatementService.BuildPresentationAsync(db, stranger, packageId)).Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await FinancialStatementService.GetPinnedPresentationAsync(db, stranger, packageId, pin.RevisionId)).ErrorCode);

      // Replacing the exact accepted GL source reviewed by this package makes the pin stale.
      var replacement = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId,
        fixture.EngagementId, fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "layout-stale-pin");
      Assert.NotEqual(source.BatchId, replacement.BatchId);
      var stalePin = await FinancialStatementService.GetPinnedPresentationAsync(db, reviewer, packageId, pin.RevisionId);
      Assert.False(stalePin.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stalePin.ErrorCode);
      var staleList = await FinancialStatementService.ListPresentationPinsAsync(db, reviewer, packageId);
      Assert.False(staleList.Value!.Single().SourceIsCurrent);
      var historicalDocx = await FinancialStatementService.GetPinnedPresentationArtifactAsync(db, reviewer, pin.RevisionId, "docx");
      Assert.True(historicalDocx.Succeeded, historicalDocx.Message);
      Assert.Equal(firstPinnedDocx.Value.Bytes, historicalDocx.Value!.Bytes);
      var unrenderedStalePdf = await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.RevisionId, "pdf");
      Assert.False(unrenderedStalePdf.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, unrenderedStalePdf.ErrorCode);
    }
  }

  [Fact]
  public async Task StatementPresentation_CashFlowSourceLineageNeedsCurrentIndependentApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    // The accepted ledger is seeded before the mapping and plan, because a later source change moves the input generation.
    var source = await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId, fixture.EngagementId,
      fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "presentation");

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(
          fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31",
          [new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
           new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")]));
      mappingId = created.Value;
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);
    }

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 100m, 0m), ("4000", 0m, 100m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    Guid packageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, new BuildFinancialPackageRequest(
        planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1",
        new FinancialSupplementaryInformation(
          0m, 100m,
          [new CashFlowLineInput("OPERATING", "Cash collections", 100m, MovementDate: new DateOnly(2026, 6, 30),
            ActivityCode: "CASH_COLLECTIONS", SourceReferences: ["bank-ledger-layout-test"], ClassificationRationale: "Synthetic collections agreed to source.")],
          [new DisclosureInput("NOTE_1", "Summary of significant accounting policies.")],
          CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["opening-cash-layout-test"], ClosingCashEvidenceReferences: ["closing-cash-layout-test"])));
      Assert.True(built.Succeeded, built.Message);
      packageId = built.Value!.PackageId;
    }

    // Create a second package with source-derived supplementary values so the seal assertion exercises
    // the permitted path through the package's real cash and equity validation gates.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var sourceLines = await db.FinancialPackageLines.AsNoTracking()
        .Where(x => x.FinancialPackageId == packageId).ToListAsync();
      var signedProfitOrLoss = sourceLines.Where(x => x.StatementSection is "INCOME" or "REVENUE" or "EXPENSE" or "EXPENSES" or "P&L" or "P_AND_L" or "PROFIT_LOSS")
        .Sum(x => x.Amount);
      var closingCash = sourceLines.Where(x => x.DestinationCode == "CASH").Sum(x => x.Amount);
      var completed = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer,
        new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "presentation-seal-v2",
          new FinancialSupplementaryInformation(0m, closingCash,
            [new CashFlowLineInput("OPERATING", "Cash movement", closingCash, MovementDate: new DateOnly(2026, 6, 30),
              ActivityCode: "CASH_MOVEMENT", SourceReferences: ["synthetic-cash-movement"], ClassificationRationale: "Synthetic test input derived from the package cash balance.", SourceAllocations: [new CashFlowSourceAllocationInput(source.Receivable, -closingCash)])],
            [new DisclosureInput("NOTE_1", "Synthetic policy disclosure for the test.")],
            [new EquityLineInput("RETAINED_EARNINGS", "Retained earnings", 0m, -signedProfitOrLoss, 0m, 0m, 0m,
              -signedProfitOrLoss, "synthetic-equity-movement")],
            CashAccountCodes: ["1000"], OpeningCashEvidenceReferences: ["synthetic-opening-cash"],
            ClosingCashEvidenceReferences: ["synthetic-closing-cash"],
            NoteLines: [new NoteLineInput("NOTE_1", "CASH", closingCash, "synthetic-note-source")],
            FxCashAssessment: new FxCashAssessmentInput(FxCashAssessmentStates.AssessedZero, "synthetic-functional-currency-cash",
              "The synthetic selected cash population has no foreign-currency effect."))));
      Assert.True(completed.Succeeded, completed.Message);
      packageId = completed.Value!.PackageId;
    }

    static StatementLineDefinition Heading(string code, string label, string section, int order) =>
      new(code, label, StatementLayoutLineKinds.Heading, section, order);
    static StatementLineDefinition Mapped(string code, string label, string section, int order, string node, string sign) =>
      new(code, label, StatementLayoutLineKinds.MappedTaxonomyBalance, section, order, sign, TaxonomyNodeCode: node);
    static StatementLineDefinition Sum(string code, string label, string section, int order, string sign, params string[] children) =>
      new(code, label, StatementLayoutLineKinds.SumChildLines, section, order, sign, ChildLineCodes: children, IsSubtotal: true);
    IReadOnlyList<StatementLineDefinition> Lines() =>
    [
      Heading("H_FP", "Assets", StatementLayoutSections.FinancialPosition, 1),
      Mapped("CASH", "Cash", StatementLayoutSections.FinancialPosition, 2, "CASH", StatementLayoutDisplaySigns.Signed),
      Sum("TA", "Total assets", StatementLayoutSections.FinancialPosition, 3, StatementLayoutDisplaySigns.Signed, "CASH"),
      Heading("H_PL", "Revenue", StatementLayoutSections.ProfitOrLoss, 1),
      Mapped("REV", "Revenue", StatementLayoutSections.ProfitOrLoss, 2, "REVENUE", StatementLayoutDisplaySigns.Inverted),
    ];

    Guid layoutId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Without a published layout the presentation is refused rather than guessed from unpublished work.
      var before = await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId);
      Assert.Equal(ErrorCodes.Accounting.MappingIncomplete, before.ErrorCode);

      var draft = await StatementLayoutService.SaveLayoutDraftAsync(db, preparer,
        new StatementLayoutDraftRequest(fixture.ClientId, "IFRS", "Presentation test layout", Lines()));
      Assert.True(draft.Succeeded, draft.Message);
      layoutId = draft.Value!.LayoutVersionId;
      Assert.True((await StatementLayoutService.ValidateLayoutAsync(db, preparer, layoutId)).Value!.IsValid);

      // An explicitly named draft is never presented, and a preparer without publishing authority cannot publish it.
      Assert.Equal(ErrorCodes.Accounting.MappingIncomplete,
        (await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId, layoutId)).ErrorCode);
      Assert.False((await StatementLayoutService.PublishLayoutAsync(db, preparer, layoutId, "Self publish")).Succeeded);
      Assert.True((await StatementLayoutService.PublishLayoutAsync(db, reviewer, layoutId, "Reviewed for the presentation test")).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Resolved links are not enough: the presentation reads them as source-approved only after an independent approval.
      Assert.True((await CashFlowSourceReviewCall.RecordAsync(db, reviewer, packageId, "APPROVED", null)).Succeeded);
      var presented = await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId);
      Assert.True(presented.Succeeded, presented.Message);
      var lineage = presented.Value!.Checks.Single(x => x.Code == "CASH_FLOW_SOURCE_LINEAGE");
      Assert.True(lineage.Passed, lineage.Detail);
    }

    // Replacing the accepted ledger stales that approval, so the presentation stops reading the links as approved.
    await CashFlowSourceSeed.SeedAcceptedGeneralLedgerAsync(pg, fixture.FirmId, fixture.ClientId, fixture.EngagementId,
      fixture.PeriodId, fixture.BookId, fixture.Preparer.Id, reviewer, "presentation-replacement");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var presented = await FinancialStatementService.BuildPresentationAsync(db, reviewer, packageId);
      Assert.True(presented.Succeeded, presented.Message);
      Assert.Contains(presented.Value!.Checks, x => x.Code == "CASH_FLOW_SOURCE_LINEAGE" && !x.Passed);
      Assert.Contains(presented.Value.Warnings, x => x.Code == "CASH_FLOW_LINEAGE_PENDING");
    }
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var preparer = User(firmId);
    var reviewer = User(firmId);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "FINANCIAL STATEMENT TEST",
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"));
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
      CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingBooks.Add(new ClientReportingBook
    {
      Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
      Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
      Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    var taxonomyId = Guid.NewGuid();
    db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = taxonomyId, FirmId = firmId, Code = "tax-v1", Framework = "IFRS", Name = "Test taxonomy",
      Status = AccountingWorkflowStates.Approved, EffectiveFrom = new DateOnly(2026, 1, 1),
      CreatedByUserId = preparer.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
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
      PeriodId = periodId, BookId = bookId, Basis = "STATUTORY",
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, periodId, bookId, preparer, reviewer);
  }

  private static AppUser User(Guid firmId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId,
    Subject = "sub-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = $"{Guid.NewGuid():N}@example.test",
    DisplayName = "Synthetic", CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
