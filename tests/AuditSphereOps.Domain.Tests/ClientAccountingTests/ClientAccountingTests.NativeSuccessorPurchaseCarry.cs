using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Accounting;
using Npgsql;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorCarriesActualAssetPurchaseAndRetainsReturnedSubmissionHistory(bool returnFirst)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var native = await CreateNativeBehaviourFixtureAsync(pg, successorAccounts: true);
    var scope = native.Scope;
    var maker = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    Guid apRoleId = Guid.Empty, apAccountId = Guid.Empty;
    foreach (var (role, code) in new[] { ("AR", "1100"), ("AP", "2100") })
    {
      var accountId = await db.ClientAccounts.Where(x => x.ChartVersionId == native.ChartId && x.AccountCode == code)
        .Select(x => x.Id).SingleAsync();
      var proposed = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, scope.ClientA,
        new(native.ChartId, accountId, role, new(2026, 1, 1), null, "Synthetic purchase carry control proposal"));
      Assert.True(proposed.Succeeded, proposed.Message);
      var reviewed = await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, scope.ClientA, proposed.Value,
        "APPROVE", "Synthetic independent purchase carry control review");
      Assert.True(reviewed.Succeeded, reviewed.Message);
      if (role == "AP") { apRoleId = proposed.Value; apAccountId = accountId; }
    }
    var supplier = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, maker, scope.ClientA,
      new("Synthetic asset supplier", "Synthetic asset supplier", "SUPPLIER", "QA address", "QA",
        "", "", "", "QAR", "TEST", "ASSET-CARRY-SUPPLIER"));
    Assert.True(supplier.Succeeded, supplier.Message);
    const string supplierReference = "SUP-CARRY-ASSET-001";
    const string syntheticReview = "Synthetic independent untaxed ASSET purchase review; no source receipt supplied; not live supplier evidence";
    var purchase = new ClientPurchaseInvoiceDraftRequest(Guid.CreateVersion7(), null, 0, native.PeriodId,
      supplier.Value, "PV-CARRY-ASSET-001", supplierReference, new(2026, 1, 20), new(2026, 1, 20),
      new(2026, 1, 20), new(2026, 1, 20), new(2026, 2, 10), "QAR",
      new("QAR", 2, "AWAY_FROM_ZERO", "REJECT", 0, ""),
      [new("Synthetic equipment purchase", "1000", 1m, 100m, 0m, "NONE", [])],
      100m, 0m, 100m, null, "Synthetic untaxed ASSET purchase fixture with no source receipt");
    var saved = await ClientPurchaseInvoiceWorkflow.SaveDraftAsync(db, maker, scope.ClientA, purchase);
    Assert.True(saved.Succeeded, saved.Message);
    var draft = saved.Value!;
    var invoiceId = draft.InvoiceId;
    var firstDraftId = draft.Id;
    Assert.Null(draft.Snapshot.SourceReceiptId);
    Assert.Null(draft.Snapshot.SourceReceiptHash);
    Assert.Equal("1000", Assert.Single(draft.Snapshot.Lines).AccountCode);

    async Task<ClientPurchaseInvoiceReceipt> SubmitPurchaseAsync(ClientPurchaseInvoiceDraftView currentDraft)
    {
      var request = new ClientPurchaseInvoiceSubmitRequest(Guid.CreateVersion7(), currentDraft.InvoiceId,
        currentDraft.Revision, apRoleId, "");
      var preview = await ClientPurchaseInvoiceWorkflow.PreviewAsync(db, maker, scope.ClientA, request);
      Assert.True(preview.Succeeded, preview.Message);
      Assert.Equal("100.000000", preview.Value!.Gross);
      Assert.Empty(preview.Value.DuplicateWarnings);
      var submitted = await ClientPurchaseInvoiceWorkflow.SubmitAsync(db, maker, scope.ClientA,
        request with { PreviewDigest = preview.Value.Digest });
      Assert.True(submitted.Succeeded, submitted.Message);
      return submitted.Value!;
    }

    var submittedPurchase = await SubmitPurchaseAsync(draft);
    var firstSubmission = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking()
      .SingleAsync(x => x.Id == submittedPurchase.SubmissionId);
    var originalJournalId = firstSubmission.JournalId;
    var firstSubmissionBytes = firstSubmission.ManifestJson;
    if (returnFirst)
    {
      var returnPreview = await ClientPurchaseInvoiceWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, firstSubmission.Id);
      Assert.True(returnPreview.Succeeded, returnPreview.Message);
      var returned = await ClientPurchaseInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), firstSubmission.Id, "RETURN", "Synthetic request to clarify equipment evidence before posting", "",
          returnPreview.Value!.Digest));
      Assert.True(returned.Succeeded, returned.Message);
      Assert.Empty(await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToArrayAsync());
      var revised = await ClientPurchaseInvoiceWorkflow.SaveDraftAsync(db, maker, scope.ClientA,
        purchase with { CommandId = Guid.CreateVersion7(), InvoiceId = invoiceId, ExpectedRevision = 1,
          Lines = [new("Synthetic equipment evidence clarified after independent return", "1000", 1m, 100m, 0m, "NONE", [])] });
      Assert.True(revised.Succeeded, revised.Message);
      draft = revised.Value!;
      Assert.Equal(invoiceId, draft.InvoiceId);
      Assert.Equal("2", draft.Revision);
      var revisedRow = await db.ClientPurchaseInvoiceDrafts.AsNoTracking().SingleAsync(x => x.Id == draft.Id);
      Assert.Equal(firstDraftId, revisedRow.PreviousRevisionId);
      Assert.Equal(1L, revisedRow.PreviousRevision);
      // This must exercise actual RETURN/rework/resubmission on the original journal.
      // A Submit implementation that always inserts a new CLIENT-PURCHASE-{InvoiceId}
      // journal collides with ux_client_operational_journal_number. Do not seed around it.
      submittedPurchase = await SubmitPurchaseAsync(draft);
    }
    var finalSubmission = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking()
      .SingleAsync(x => x.Id == submittedPurchase.SubmissionId);
    Assert.Equal(invoiceId, finalSubmission.InvoiceId);
    Assert.Equal(draft.Id, finalSubmission.DraftId);
    Assert.Equal(originalJournalId, finalSubmission.JournalId);
    var review = await ClientPurchaseInvoiceWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA, finalSubmission.Id);
    Assert.True(review.Succeeded, review.Message);
    Assert.True(review.Value!.CanPost, review.Value.PostingBlock);
    var posted = await ClientPurchaseInvoiceWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
      new(Guid.CreateVersion7(), finalSubmission.Id, "APPROVE", syntheticReview, "", review.Value.Digest));
    Assert.True(posted.Succeeded, posted.Message);
    var originalItem = await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().SingleAsync(x => x.InvoiceId == invoiceId);
    Assert.Equal(finalSubmission.Id, originalItem.SubmissionId);
    Assert.Equal(originalJournalId, originalItem.JournalId);
    Assert.Equal(supplier.Value, originalItem.SupplierId);
    Assert.Equal(100m, originalItem.OriginalAmount);
    Assert.Equal(new DateOnly(2026, 2, 10), originalItem.DueDate);
    Assert.Equal(returnFirst ? 2 : 1, await db.ClientPurchaseInvoiceSubmissions.CountAsync(x => x.InvoiceId == invoiceId));
    Assert.Equal(returnFirst ? 1 : 0, await db.ClientPurchaseInvoiceDecisions.CountAsync(x =>
      x.ClientId == scope.ClientA && x.SubmissionId == firstSubmission.Id && x.Decision == "RETURN"));
    var postedJournal = await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x => x.Id == originalJournalId);
    var postingReceipt = await db.ClientOperationalPostingReceipts.AsNoTracking().SingleAsync(x =>
      x.JournalId == originalJournalId && x.PostedRevision == postedJournal.Revision);
    var postedLines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.JournalId == originalJournalId).ToArrayAsync();
    Assert.Equal(2, postedLines.Length);
    Assert.Equal(100m, Assert.Single(postedLines, x => x.AccountCode == "1000").Debit);
    Assert.Equal(100m, Assert.Single(postedLines, x => x.ClientAccountId == apAccountId).Credit);
    Assert.DoesNotContain(postedLines, x => x.AccountCode is "4000" or "5000");

    var source = await CaptureSuccessorFixtureSourceAsync(pg, native, 2026);
    var nativeEvidence = await db.NativeTrialBalanceSources.AsNoTracking().SingleAsync(x => x.DatasetId == source.DatasetId);
    using (var manifest = JsonDocument.Parse(nativeEvidence.ManifestJson))
    using (var membership = JsonDocument.Parse(manifest.RootElement.GetProperty("Pins").GetProperty("MembershipJson").GetString()!))
      Assert.Equal(returnFirst ? 2 : 1, membership.RootElement.GetProperty("Purchases").GetArrayLength());
    var closed = await ClientAccountingService.ClosePeriodAsync(db, reviewer, native.PeriodId,
      "Synthetic independent close of the accepted untaxed asset-purchase source");
    Assert.True(closed.Succeeded, closed.Message);
    var targetId = await SeedNativeSuccessorActivePeriodAsync(pg, native, native.PeriodId, 2027);
    var successor = new NativeSuccessorOpeningRequest(Guid.CreateVersion7(), scope.ClientA, scope.EngagementA,
      targetId, 1, native.PeriodId, 2, source.DatasetId, source.SourceIdentityHash,
      "Synthetic exact ASSET-purchase original-item successor");
    var successorPreview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, successor);
    Assert.True(successorPreview.Succeeded, successorPreview.Message);
    var previewItem = Assert.Single(successorPreview.Value!.CarriedPopulation!.Items);
    Assert.Equal("PURCHASE_INVOICE", previewItem.OriginalKind);
    Assert.Equal(originalItem.Id, previewItem.OriginalOpenItemId);
    Assert.Equal(invoiceId, previewItem.OriginalDocumentId);
    Assert.Equal("AP", previewItem.Role);
    Assert.Equal(supplier.Value, previewItem.CounterpartyId);
    Assert.Equal("Synthetic asset supplier", previewItem.CounterpartyName);
    Assert.Equal(apAccountId, previewItem.AccountId);
    Assert.Equal("2100", previewItem.AccountCode);
    Assert.Equal("QAR", previewItem.Currency);
    Assert.Equal(supplierReference, previewItem.Reference);
    Assert.Equal("SUPPLIER_INVOICE_REFERENCE", previewItem.ReferenceKind);
    Assert.Equal("100.000000", previewItem.OriginalAmount);
    Assert.Equal("0.000000", previewItem.BaselineAppliedAmount);
    Assert.Equal("100.000000", previewItem.BaselineOpenAmount);
    Assert.Null(previewItem.SourceAsOfDate);
    Assert.Equal(new DateOnly(2026, 1, 20), previewItem.DocumentDate);
    Assert.Equal(new DateOnly(2026, 1, 20), previewItem.AccountingDate);
    Assert.Equal(new DateOnly(2026, 2, 10), previewItem.DueDate);
    if (!returnFirst)
    {
      var actualSource = await NativeTrialBalanceSourceWorkspace.ReadSuccessorSourceAsync(db, scope.FirmId,
        scope.ClientA, scope.EngagementA, source.DatasetId, default);
      Assert.True(actualSource.Succeeded, actualSource.Message);
      var actual = await ClientOperationalOpeningBalanceWorkspace.ResolveSuccessorFactsAsync(db, scope.FirmId,
        scope.ClientA, targetId, actualSource.Value!, default);
      Assert.True(actual.Succeeded, actual.Message);
      await AssertNativeReceiptForgeryRefusedAtCommitAsync(pg, successor, successorPreview.Value,
        scope.FirmId, maker.UserId, maker.SessionEpoch, actual.Value!);
    }
    var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      successor with { Reviewed = true, ReviewBasis = successorPreview.Value.ReviewBasis });
    Assert.True(created.Succeeded, created.Message);
    using (var proof = JsonDocument.Parse(created.Value!.ProofJson))
      Assert.Equal("native-opening-successor.v2", proof.RootElement.GetProperty("Version").GetString());
    var proofRow = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == created.Value.Opening.Id);
    var openingRow = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == created.Value.Opening.Id);
    var parsedProof = ClientOperationalOpeningBalanceWorkspace.ParseSuccessorProof(openingRow, proofRow);
    Assert.True(parsedProof.Succeeded, parsedProof.Message);
    Assert.Equal(source.DatasetId, parsedProof.Value!.SourceDatasetId);
    Assert.Equal(source.SourceIdentityHash, parsedProof.Value.SourceIdentityHash);
    Assert.Equal(successorPreview.Value.CarriedPopulation.PopulationHash, parsedProof.Value.Facts.CarriedPopulation!.PopulationHash);
    var proofItem = Assert.Single(parsedProof.Value.Facts.CarriedPopulation.Items);
    Assert.Equal(originalItem.Id, proofItem.OriginalOpenItemId);
    Assert.Equal(invoiceId, proofItem.OriginalDocumentId);
    Assert.Equal(finalSubmission.Id, proofItem.SubmissionId);
    Assert.Equal(draft.Id, proofItem.DraftId);
    Assert.Equal(originalJournalId, proofItem.JournalId);
    Assert.Null(proofItem.OriginalOpeningId);
    Assert.Equal(Hashing.Sha256Hex(proofItem.SourceFactsJson), proofItem.SourceFactsHash);
    using (var facts = JsonDocument.Parse(proofItem.SourceFactsJson))
    {
      Assert.Equal(postingReceipt.Id, facts.RootElement.GetProperty("PostingReceiptId").GetGuid());
      Assert.Equal(Hashing.Sha256Hex(JsonSerializer.Serialize(postingReceipt)), facts.RootElement.GetProperty("PostingReceiptHash").GetString());
      Assert.Equal(finalSubmission.ManifestHash, facts.RootElement.GetProperty("SubmissionManifestHash").GetString());
      Assert.Equal(draft.SnapshotHash, facts.RootElement.GetProperty("DraftSnapshotHash").GetString());
      Assert.Null(facts.RootElement.GetProperty("SourceReceiptId").GetString());
    }

    // Self-consistent forged receipt facts must still fail against freshly resolved actual source facts.
    // This uses no SQL helper, forged persisted rows or second fixture.
    if (!returnFirst)
    {
      var retainedSource = await NativeTrialBalanceSourceWorkspace.ReadSuccessorSourceAsync(db, scope.FirmId,
        scope.ClientA, scope.EngagementA, source.DatasetId, default);
      Assert.True(retainedSource.Succeeded, retainedSource.Message);
      var actualFacts = await ClientOperationalOpeningBalanceWorkspace.ResolveSuccessorFactsAsync(db, scope.FirmId,
        scope.ClientA, targetId, retainedSource.Value!, default, proofRow.CreatedAt);
      Assert.True(actualFacts.Succeeded, actualFacts.Message);
      var genuine = ClientOperationalOpeningBalanceWorkspace.ParseSuccessorProof(openingRow, proofRow, actualFacts.Value!);
      Assert.True(genuine.Succeeded, genuine.Message);
      var forgedSource = JsonNode.Parse(proofItem.SourceFactsJson)!.AsObject();
      forgedSource["PostingReceiptId"] = Guid.CreateVersion7();
      forgedSource["PostingReceiptHash"] = new string('0', 64);
      var forgedSourceJson = forgedSource.ToJsonString();
      var forgedItem = proofItem with { SourceFactsJson = forgedSourceJson, SourceFactsHash = Hashing.Sha256Hex(forgedSourceJson) };
      var population = parsedProof.Value.Facts.CarriedPopulation!;
      var forgedPopulation = population with { Items = [forgedItem],
        PopulationHash = ClientOperationalOpeningBalanceWorkspace.SuccessorCarriedPopulationHash([forgedItem], population.ControlStateHash) };
      var forgedProof = parsedProof.Value with { Facts = parsedProof.Value.Facts with {
        CarriedPopulation = forgedPopulation, PopulationHash = forgedPopulation.PopulationHash } };
      proofRow.ProofJson = JsonSerializer.Serialize(forgedProof);
      proofRow.ProofHash = Hashing.Sha256Hex(proofRow.ProofJson);
      var refused = ClientOperationalOpeningBalanceWorkspace.ParseSuccessorProof(openingRow, proofRow, actualFacts.Value!);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, refused.ErrorCode);
    }

    var approved = await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer,
      new(Guid.CreateVersion7(), scope.ClientA, scope.EngagementA, created.Value.Opening.Id, 1,
        created.Value.Opening.ManifestSha256, created.Value.ProofHash, true));
    Assert.True(approved.Succeeded, approved.Message);
    var balances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2027, 1, 31));
    Assert.True(balances.Succeeded, balances.Message);
    var carriedBalance = Assert.Single(balances.Value!);
    Assert.Equal("PURCHASE_INVOICE", carriedBalance.Kind);
    Assert.Equal(originalItem.Id, carriedBalance.OpenItemId);
    Assert.Equal(invoiceId, carriedBalance.SourceDocumentId);
    Assert.Equal("100.000000", carriedBalance.OpenAmount);
    Assert.Equal(new DateOnly(2026, 2, 10), carriedBalance.DueDate);
    var reconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer,
      scope.ClientA, targetId, new(2027, 1, 31));
    Assert.True(reconciliation.Succeeded, reconciliation.Message);
    Assert.True(reconciliation.Value!.Reconciled);
    var ap = Assert.Single(reconciliation.Value.Accounts, x => x.Role == "AP");
    Assert.Equal("100.000000", ap.LedgerBalance);
    Assert.Equal("100.000000", ap.OpenItemBalance);
    Assert.Equal("0.000000", ap.Difference);
    var targetLedger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, reviewer, scope.ClientA, targetId);
    Assert.True(targetLedger.Succeeded, targetLedger.Message);
    Assert.Equal(220m, decimal.Parse(Assert.Single(targetLedger.Value!.TrialBalance.Rows, x => x.AccountCode == "1000").OpeningDebit,
      CultureInfo.InvariantCulture));
    Assert.Equal(100m, decimal.Parse(Assert.Single(targetLedger.Value.TrialBalance.Rows, x => x.AccountCode == "2100").OpeningCredit,
      CultureInfo.InvariantCulture));
    Assert.Empty(await db.ClientOperationalJournals.AsNoTracking().Where(x => x.PeriodId == targetId).ToArrayAsync());
    Assert.Single(await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToArrayAsync());
    Assert.Equal(firstSubmissionBytes, (await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().SingleAsync(x => x.Id == firstSubmission.Id)).ManifestJson);
    var current = await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, reviewer, scope.ClientA, created.Value.Opening.Id);
    Assert.True(current.Succeeded, current.Message);
    Assert.True(current.Value!.IsCurrent, current.Value.CurrentnessBlocker);
    Assert.Equal(created.Value.ProofJson, current.Value.ProofJson);
    if (!returnFirst)
    {
      // A real later supplier credit consumes the original AP item without
      // inventing cash or changing the original posted purchase or opening.
      var credit = new ClientPurchaseCreditPreviewRequest(Guid.CreateVersion7(), "SUP-CARRY-CN-001", invoiceId,
        supplier.Value, targetId, apRoleId, new(2027, 1, 15), "Synthetic partial equipment return after successor opening",
        "", null, "Synthetic reviewed supplier credit evidence", [new(1, "1000", 40m)]);
      var creditPreview = await ClientPurchaseCreditNoteWorkflow.PreviewAsync(db, maker, scope.ClientA, credit);
      Assert.True(creditPreview.Succeeded, creditPreview.Message);
      Assert.Equal("40.000000", creditPreview.Value!.TotalCredit);
      var creditSubmission = await ClientPurchaseCreditNoteWorkflow.SubmitAsync(db, maker, scope.ClientA,
        new(Guid.CreateVersion7(), credit, creditPreview.Value.Digest));
      Assert.True(creditSubmission.Succeeded, creditSubmission.Message);
      var creditReview = await ClientPurchaseCreditNoteWorkflow.PreviewReviewAsync(db, reviewer, scope.ClientA,
        creditSubmission.Value!.SubmissionId);
      Assert.True(creditReview.Succeeded, creditReview.Message);
      var postedCredit = await ClientPurchaseCreditNoteWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        new(Guid.CreateVersion7(), creditSubmission.Value.SubmissionId, "APPROVE",
          "Synthetic independent equipment-return credit review", "", creditReview.Value!.Digest));
      Assert.True(postedCredit.Succeeded, postedCredit.Message);
      var sourceCredit = await db.ClientPurchaseCreditNoteOpenItems.AsNoTracking().SingleAsync(x =>
        x.SubmissionId == creditSubmission.Value.SubmissionId);
      var allocation = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "PURCHASE_CREDIT", sourceCredit.Id,
        "ALLOCATE", "SUP-CARRY-CN-001-ALLOC", "Apply actual later credit to original carried payable",
        [new(1, "PURCHASE_INVOICE", originalItem.Id, 40m)], "");
      var allocationPreview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, maker, scope.ClientA, allocation);
      Assert.True(allocationPreview.Succeeded, allocationPreview.Message);
      var allocationSubmission = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, maker, scope.ClientA,
        allocation with { PreviewDigest = allocationPreview.Value!.Digest });
      Assert.True(allocationSubmission.Succeeded, allocationSubmission.Message);
      var allocationReview = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA,
        allocationSubmission.Value!.SubmissionId);
      Assert.True(allocationReview.Succeeded, allocationReview.Message);
      var applied = await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA,
        allocationSubmission.Value.SubmissionId, Guid.CreateVersion7(), "APPROVE",
        "Synthetic independent original payable allocation review", allocationReview.Value!.Digest);
      Assert.True(applied.Succeeded, applied.Message);
      var finalBalances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2027, 1, 31));
      Assert.True(finalBalances.Succeeded, finalBalances.Message);
      var finalItems = finalBalances.Value!;
      Assert.Equal("60.000000", finalItems.Single(x => x.OpenItemId == originalItem.Id).OpenAmount);
      Assert.Equal("0.000000", finalItems.Single(x => x.OpenItemId == sourceCredit.Id).OpenAmount);
      var finalReconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer,
        scope.ClientA, targetId, new(2027, 1, 31));
      Assert.True(finalReconciliation.Succeeded, finalReconciliation.Message);
      Assert.True(finalReconciliation.Value!.Reconciled);
      Assert.Equal("60.000000", Assert.Single(finalReconciliation.Value.Accounts, x => x.Role == "AP").LedgerBalance);
      var retained = await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, reviewer, scope.ClientA, created.Value.Opening.Id);
      Assert.True(retained.Succeeded, retained.Message); Assert.True(retained.Value!.IsCurrent, retained.Value.CurrentnessBlocker);
      Assert.Equal(created.Value.ProofJson, retained.Value.ProofJson);
      Assert.Single(await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.InvoiceId == invoiceId).ToArrayAsync());
    }
  }
  private static async Task AssertNativeReceiptForgeryRefusedAtCommitAsync(PgTestSchema pg,
    NativeSuccessorOpeningRequest request, NativeSuccessorOpeningPreview preview, Guid firmId, Guid actorId,
    long actorEpoch, ClientOperationalOpeningBalanceWorkspace.SuccessorFacts actual)
  {
    var population = actual.CarriedPopulation!;
    var item = Assert.Single(population.Items);
    var source = JsonNode.Parse(item.SourceFactsJson)!.AsObject();
    source["PostingReceiptId"] = Guid.CreateVersion7(); source["PostingReceiptHash"] = new string('0', 64);
    var sourceJson = source.ToJsonString();
    var forged = item with { SourceFactsJson = sourceJson, SourceFactsHash = Hashing.Sha256Hex(sourceJson) };
    var changed = population with { Items = [forged], PopulationHash =
      ClientOperationalOpeningBalanceWorkspace.SuccessorCarriedPopulationHash([forged], population.ControlStateHash) };
    var facts = actual with { CarriedPopulation = changed, PopulationHash = changed.PopulationHash };
    var date = DateOnly.ParseExact(preview.AsOfDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    var manifest = JsonSerializer.Serialize(new { Version = "native-opening-v1", FirmId = firmId, request.ClientId,
      PeriodId = request.PeriodId, PeriodRevision = request.ExpectedPeriodRevision, ChartVersionId = facts.ChartId,
      AsOfDate = date, facts.Currency, request.EvidenceReference, EvidenceSha256 = request.ExpectedSourceIdentityHash,
      Lines = facts.Lines.Select(x => new { x.AccountCode, x.AccountName,
        Debit = decimal.Parse(x.Debit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture),
        Credit = decimal.Parse(x.Credit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture) }).ToArray() });
    await using var db = new AuditSphereDbContext(pg.Options);
    await using var tx = await db.Database.BeginTransactionAsync();
    var opening = new ClientOperationalOpeningBalance { Id = Guid.CreateVersion7(), FirmId = firmId, ClientId = request.ClientId,
      PeriodId = request.PeriodId, PeriodRevision = request.ExpectedPeriodRevision, AsOfDate = date, Currency = facts.Currency,
      EvidenceReference = request.EvidenceReference, EvidenceSha256 = request.ExpectedSourceIdentityHash,
      ManifestJson = manifest, ManifestSha256 = Hashing.Sha256Hex(manifest), CreatedByUserId = actorId, CreatedAt = DateTimeOffset.UtcNow };
    var proof = JsonSerializer.Serialize(new { Version = "native-opening-successor.v2", OpeningId = opening.Id,
      OpeningManifestHash = opening.ManifestSha256, FirmId = firmId, request.ClientId, request.EngagementId,
      request.PriorPeriodId, PriorPeriodRevision = request.ExpectedPriorPeriodRevision,
      SourceDatasetId = request.NativeTrialBalanceDatasetId, SourceIdentityHash = request.ExpectedSourceIdentityHash, Facts = facts });
    db.ClientOperationalOpeningBalances.Add(opening);
    db.ClientOperationalOpeningSuccessorProofs.Add(new() { OpeningId = opening.Id, FirmId = firmId, ClientId = request.ClientId,
      EngagementId = request.EngagementId, PriorPeriodId = request.PriorPeriodId, PriorPeriodRevision = request.ExpectedPriorPeriodRevision,
      SourceDatasetId = request.NativeTrialBalanceDatasetId, SourceIdentityHash = request.ExpectedSourceIdentityHash,
      ProofJson = proof, ProofHash = Hashing.Sha256Hex(proof), RequestId = Guid.CreateVersion7(), ActorId = actorId,
      ActorEpoch = actorEpoch, RequestHash = new string('a', 64), CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var refusal = await Assert.ThrowsAsync<PostgresException>(() => tx.CommitAsync());
    Assert.Equal("23514", refusal.SqlState);
    Assert.Contains("Native carried posting artifacts", refusal.MessageText, StringComparison.Ordinal);
  }

}
