using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuditSphereOps.Domain.Shared;
using Npgsql;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorCarriesOriginalOpeningItemAndSettlesWithoutReposting()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    // Rehearse the actual new migration in an empty isolated database before creating retained evidence.
    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
    {
      var migrations = migrationDb.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_ClientOpeningSuccessorCarry", StringComparison.Ordinal));
      Assert.True(index > 0);
      await migrationDb.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);
      var constraint = await migrationDb.Database.SqlQueryRaw<string>(
        """SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint WHERE conname='ck_native_opening_successor_proof'""").SingleAsync();
      Assert.Contains("native-opening-successor.v1", constraint);
      Assert.DoesNotContain("native-opening-successor.v2", constraint);
      await migrationDb.GetService<IMigrator>().MigrateAsync(migrations[index]);
    }
    var native = await CreateNativeBehaviourFixtureAsync(pg, withOpening: false, successorAccounts: true, cashAccount: true);
    var scope = native.Scope;
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    foreach (var (role, code) in new[] { ("AR", "1100"), ("AP", "2100") })
    {
      var accountId = await db.ClientAccounts.Where(x => x.ChartVersionId == native.ChartId && x.AccountCode == code).Select(x => x.Id).SingleAsync();
      var proposal = await ClientAccountRoleWorkspace.ProposeAsync(db, preparer, scope.ClientA,
        new(native.ChartId, accountId, role, new(2026, 1, 1), null, "Synthetic exact carry control"));
      Assert.True(proposal.Succeeded, proposal.Message);
      var decision = await ClientAccountRoleWorkspace.ReviewAsync(db, reviewer, scope.ClientA, proposal.Value,
        "APPROVE", "Synthetic independent carry control review");
      Assert.True(decision.Succeeded, decision.Message);
    }
    var party = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, preparer, scope.ClientA,
      new("Original carry customer", "Original carry customer", "BOTH", "QA address", "QA", "", "", "", "", "TEST", "CARRY-001"));
    Assert.True(party.Succeeded, party.Message);
    var partyId = party.Value;
    var root = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, preparer,
      new(scope.ClientA, native.PeriodId, 1, new(2026, 1, 1), "QAR", "Synthetic reviewed original invoice schedule",
        new string('c', 64), [new("1000", 100m, 0m), new("1100", 125m, 0m), new("3000", 0m, 225m)],
        [new("AR", partyId, "ORIGINAL-AR-001", new(2026, 2, 10), "1100", 125m)]));
    Assert.True(root.Succeeded, root.Message);
    native = native with { OpeningId = root.Value };
    var rootView = await ClientOperationalOpeningBalanceWorkspace.GetAsync(db, reviewer, scope.ClientA, native.PeriodId);
    Assert.True(rootView.Succeeded, rootView.Message);
    Assert.True((await ClientOperationalOpeningBalanceWorkspace.ApproveAsync(db, reviewer, scope.ClientA,
      root.Value, 1, rootView.Value!.ManifestSha256)).Succeeded);
    var originalItem = Assert.Single(rootView.Value.OpenItems);
    var originalBytes = (await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == root.Value)).ManifestJson;
    var source = await CaptureSuccessorFixtureSourceAsync(pg, native, 2026);
    var closed = await ClientAccountingService.ClosePeriodAsync(db, reviewer, native.PeriodId, "Synthetic independently closed source");
    Assert.True(closed.Succeeded, closed.Message);
    var periodId = await SeedNativeSuccessorActivePeriodAsync(pg, native, native.PeriodId, 2027);
    var request = new NativeSuccessorOpeningRequest(Guid.CreateVersion7(), scope.ClientA, scope.EngagementA,
      periodId, 1, native.PeriodId, 2, source.DatasetId, source.SourceIdentityHash, "Original-item successor source");
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, preparer, request);
    Assert.True(preview.Succeeded, preview.Message);
    var carried = Assert.Single(preview.Value!.CarriedPopulation!.Items);
    Assert.Equal(originalItem.Id, carried.OriginalOpenItemId);
    Assert.Equal(root.Value, carried.OriginalDocumentId);
    Assert.Equal("125.000000", carried.OriginalAmount);
    Assert.Equal("ORIGINAL-AR-001", carried.Reference);
    Assert.Equal(new DateOnly(2026, 2, 10), carried.DueDate);
    Assert.Null(carried.DocumentDate);
    var retainedSource = await NativeTrialBalanceSourceWorkspace.ReadSuccessorSourceAsync(db, scope.FirmId,
      scope.ClientA, scope.EngagementA, source.DatasetId, default);
    Assert.True(retainedSource.Succeeded, retainedSource.Message);
    var genuineFacts = await ClientOperationalOpeningBalanceWorkspace.ResolveSuccessorFactsAsync(db, scope.FirmId,
      scope.ClientA, periodId, retainedSource.Value!, default);
    Assert.True(genuineFacts.Succeeded, genuineFacts.Message);
    var otherParty = await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db, preparer, scope.ClientA,
      new("Different scoped customer", "Different scoped customer", "CUSTOMER", "QA address", "QA", "", "", "", "", "TEST", "CARRY-002"));
    Assert.True(otherParty.Succeeded, otherParty.Message);
    foreach (var corruption in new[] { "AMOUNT", "PARTY", "OMITTED", "VERSION", "CONTROL_WITNESS" })
    {
      await using var forgedDb = new AuditSphereDbContext(pg.Options);
      await using var transaction = await forgedDb.Database.BeginTransactionAsync();
      var originalPopulation = genuineFacts.Value!.CarriedPopulation!;
      var original = originalPopulation.Items[0];
      var sourceFacts = JsonNode.Parse(original.SourceFactsJson)!.AsObject();
      if (corruption == "AMOUNT") sourceFacts["OriginalAmount"] = "126.000000";
      if (corruption == "PARTY") sourceFacts["CounterpartyId"] = otherParty.Value;
      var controlStateJson = originalPopulation.ControlStateJson;
      var controlStateHash = originalPopulation.ControlStateHash;
      if (corruption == "CONTROL_WITNESS")
      {
        var states = JsonNode.Parse(controlStateJson)!.AsArray();
        var state = states.Single(x => x!["Role"]!.GetValue<string>() == "AR")!.AsObject();
        var configuration = JsonNode.Parse(state["ConfigurationJson"]!.GetValue<string>())!.AsObject();
        configuration["Reason"] = "Invented replacement for the independently reviewed reason";
        var configurationJson = configuration.ToJsonString();
        state["ConfigurationJson"] = configurationJson;
        state["ConfigurationHash"] = Hashing.Sha256Hex(configurationJson);
        sourceFacts["ControlConfigurationHash"] = Hashing.Sha256Hex(configurationJson);
        controlStateJson = states.ToJsonString();
        controlStateHash = Hashing.Sha256Hex(controlStateJson);
      }
      var sourceJson = sourceFacts.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
      var forged = original with { OriginalAmount = corruption == "AMOUNT" ? "126.000000" : original.OriginalAmount,
        BaselineOpenAmount = corruption == "AMOUNT" ? "126.000000" : original.BaselineOpenAmount,
        CounterpartyId = corruption == "PARTY" ? otherParty.Value : original.CounterpartyId,
        SourceFactsJson = sourceJson, SourceFactsHash = Hashing.Sha256Hex(sourceJson) };
      ClientOperationalOpeningBalanceWorkspace.SuccessorCarriedItem[] members = corruption == "OMITTED" ? [] : [forged];
      var population = originalPopulation with { Items = members, Count = members.Length,
        ControlStateJson = controlStateJson, ControlStateHash = controlStateHash,
        PopulationHash = ClientOperationalOpeningBalanceWorkspace.SuccessorCarriedPopulationHash(members, controlStateHash) };
      var facts = genuineFacts.Value with { CarriedPopulation = population, PopulationHash = population.PopulationHash };
      var manifestJson = JsonSerializer.Serialize(new { Version = "native-opening-v1", scope.FirmId, ClientId = scope.ClientA,
        PeriodId = periodId, PeriodRevision = 1L, ChartVersionId = native.ChartId, AsOfDate = new DateOnly(2027, 1, 1),
        Currency = "QAR", request.EvidenceReference, EvidenceSha256 = source.SourceIdentityHash,
        Lines = preview.Value.Lines.Select(x => new { x.AccountCode, x.AccountName,
          Debit = decimal.Parse(x.Debit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture),
          Credit = decimal.Parse(x.Credit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture) }).ToArray() });
      var opening = new ClientOperationalOpeningBalance { Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA,
        PeriodId = periodId, PeriodRevision = 1, AsOfDate = new(2027, 1, 1), Currency = "QAR", EvidenceReference = request.EvidenceReference,
        EvidenceSha256 = source.SourceIdentityHash, ManifestJson = manifestJson, ManifestSha256 = Hashing.Sha256Hex(manifestJson),
        CreatedByUserId = preparer.UserId, CreatedAt = DateTimeOffset.UtcNow };
      var proofJson = JsonSerializer.Serialize(new { Version = "native-opening-successor.v2", OpeningId = opening.Id,
        OpeningManifestHash = opening.ManifestSha256, scope.FirmId, ClientId = scope.ClientA, EngagementId = scope.EngagementA,
        PriorPeriodId = native.PeriodId, PriorPeriodRevision = 2L, SourceDatasetId = source.DatasetId,
        SourceIdentityHash = source.SourceIdentityHash, Facts = facts });
      if (corruption == "VERSION")
      {
        var noVersion = JsonNode.Parse(proofJson)!.AsObject(); noVersion.Remove("Version"); proofJson = noVersion.ToJsonString();
      }
      forgedDb.ClientOperationalOpeningBalances.Add(opening);
      forgedDb.ClientOperationalOpeningSuccessorProofs.Add(new() { OpeningId = opening.Id, FirmId = scope.FirmId,
        ClientId = scope.ClientA, EngagementId = scope.EngagementA, PriorPeriodId = native.PeriodId, PriorPeriodRevision = 2,
        SourceDatasetId = source.DatasetId, SourceIdentityHash = source.SourceIdentityHash, ProofJson = proofJson,
        ProofHash = Hashing.Sha256Hex(proofJson), RequestId = Guid.CreateVersion7(), ActorId = preparer.UserId,
        ActorEpoch = preparer.SessionEpoch, RequestHash = new string('a', 64), CreatedAt = DateTimeOffset.UtcNow });
      if (corruption == "VERSION")
      {
        var wrapper = await Assert.ThrowsAsync<DbUpdateException>(() => forgedDb.SaveChangesAsync());
        var versionFailure = Assert.IsType<PostgresException>(wrapper.InnerException);
        Assert.Equal("23514", versionFailure.SqlState);
        Assert.Equal("ck_native_opening_successor_proof", versionFailure.ConstraintName);
        continue;
      }
      await forgedDb.SaveChangesAsync();
      var refusal = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync());
      Assert.Equal("23514", refusal.SqlState);
      Assert.Contains(corruption == "OMITTED" ? "complete original carried population" :
        corruption == "CONTROL_WITNESS" ? "Retained control witness differs" : "Original reviewed opening item",
        refusal.MessageText, StringComparison.OrdinalIgnoreCase);
    }
    var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, preparer,
      request with { ReviewBasis = preview.Value.ReviewBasis, Reviewed = true });
    Assert.True(created.Succeeded, created.Message);
    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
    {
      var migrations = migrationDb.Database.GetMigrations().ToArray();
      var index = Array.FindIndex(migrations, x => x.EndsWith("_ClientOpeningSuccessorCarry", StringComparison.Ordinal));
      var wrapper = await Assert.ThrowsAsync<InvalidOperationException>(() =>
        migrationDb.GetService<IMigrator>().MigrateAsync(migrations[index - 1]));
      var refusal = Assert.IsType<PostgresException>(wrapper.GetBaseException());
      Assert.Equal("55000", refusal.SqlState);
      Assert.Contains("Cannot remove retained original-item successor evidence", refusal.MessageText);
      Assert.True(await migrationDb.ClientOperationalOpeningSuccessorProofs.AnyAsync(x => x.OpeningId == created.Value!.Opening.Id));
    }
    using (var proof = JsonDocument.Parse(created.Value!.ProofJson))
      Assert.Equal("native-opening-successor.v2", proof.RootElement.GetProperty("Version").GetString());
    var approved = await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer,
      new(Guid.CreateVersion7(), scope.ClientA, scope.EngagementA, created.Value.Opening.Id, 1,
        created.Value.Opening.ManifestSha256, created.Value.ProofHash, true));
    Assert.True(approved.Succeeded, approved.Message);
    var balances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2027, 1, 31));
    Assert.True(balances.Succeeded, balances.Message);
    var sameOriginal = Assert.Single(balances.Value!);
    Assert.Equal(originalItem.Id, sameOriginal.OpenItemId);
    Assert.Equal(root.Value, sameOriginal.SourceDocumentId);
    Assert.Equal("125.000000", sameOriginal.OpenAmount);
    var arAccountId = carried.AccountId;
    var openingItemId = originalItem.Id;
      var settlement = new ClientManualSettlementDraftRequest(Guid.CreateVersion7(), scope.ClientA, periodId, partyId,
        "SALES_RECEIPT", "RCT-OPEN-01", "Record completed customer receipt", new(2027, 2, 11), "1010", 20m,
        "BANK-OPEN-01", "client evidence for completed receipt");
      var settlementPreview = await ClientOperationalLedgerWorkspace.PreviewSettlementAsync(db, preparer, settlement);
      Assert.True(settlementPreview.Succeeded, settlementPreview.Message);
      var settlementDraft = await ClientOperationalLedgerWorkspace.CreateSettlementDraftAsync(db, preparer, settlement, settlementPreview.Value!.Digest);
      Assert.True(settlementDraft.Succeeded, settlementDraft.Message);
      var journalPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, preparer, scope.ClientA, settlementDraft.Value!.JournalId);
      Assert.True(journalPreview.Succeeded, journalPreview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.SubmitAsync(db, preparer, scope.ClientA, settlementDraft.Value.JournalId,
        1, previewDigest: journalPreview.Value!.Digest)).Succeeded);
      var reviewerPreview = await ClientOperationalLedgerWorkspace.PreviewAsync(db, reviewer, scope.ClientA, settlementDraft.Value.JournalId);
      Assert.True(reviewerPreview.Succeeded, reviewerPreview.Message);
      Assert.True((await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, reviewer, scope.ClientA, settlementDraft.Value.JournalId,
        new(2, "APPROVE", "Verified the externally completed client receipt", reviewerPreview.Value!.Digest, Guid.CreateVersion7()))).Succeeded);

      var receipt = Assert.Single((await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2027, 2, 11))).Value!,
        x => x.Kind == "SALES_RECEIPT");
      var allocation = new ClientOpenItemAllocationRequest(Guid.CreateVersion7(), "SALES_RECEIPT", receipt.OpenItemId,
        "ALLOCATE", "BANK-OPEN-01-ALLOC", "Apply part of the completed receipt to the reviewed cutover invoice",
        [new(1, "OPENING_AR_INVOICE", openingItemId, 20m)], "");
      var allocationPreview = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, preparer, scope.ClientA, allocation);
      Assert.True(allocationPreview.Succeeded, allocationPreview.Message);
      var allocationSubmission = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, preparer, scope.ClientA,
        allocation with { PreviewDigest = allocationPreview.Value!.Digest });
      Assert.True(allocationSubmission.Succeeded, allocationSubmission.Message);
      var allocationReview = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, reviewer, scope.ClientA, allocationSubmission.Value!.SubmissionId);
      Assert.True(allocationReview.Succeeded, allocationReview.Message);
      Assert.True((await ClientOpenItemAllocationWorkflow.ReviewAsync(db, reviewer, scope.ClientA, allocationSubmission.Value.SubmissionId,
        Guid.CreateVersion7(), "APPROVE", "Independently reviewed opening-item receipt allocation", allocationReview.Value!.Digest)).Succeeded);
      var settledBalances = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, reviewer, scope.ClientA, new(2027, 2, 11));
      Assert.True(settledBalances.Succeeded, settledBalances.Message);
      Assert.Equal("105.000000", settledBalances.Value!.Single(x => x.OpenItemId == openingItemId).OpenAmount);
      Assert.Equal("0.000000", settledBalances.Value!.Single(x => x.OpenItemId == receipt.OpenItemId).OpenAmount);
      var settledReconciliation = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, reviewer, scope.ClientA, periodId, new(2027, 2, 11));
      Assert.True(settledReconciliation.Succeeded, settledReconciliation.Message);
      Assert.True(settledReconciliation.Value!.Reconciled);
      Assert.Equal("105.000000", settledReconciliation.Value.Accounts.Single(x => x.AccountId == arAccountId).LedgerBalance);
      Assert.Equal("105.000000", settledReconciliation.Value.Accounts.Single(x => x.AccountId == arAccountId).OpenItemBalance);
    var stillCurrent = await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, reviewer, scope.ClientA, created.Value.Opening.Id);
    Assert.True(stillCurrent.Succeeded, stillCurrent.Message);
    Assert.True(stillCurrent.Value!.IsCurrent, stillCurrent.Value.CurrentnessBlocker);
    Assert.Equal(created.Value.ProofJson, stillCurrent.Value.ProofJson);
    Assert.Equal(originalBytes, (await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == root.Value)).ManifestJson);
    Assert.Empty(await db.ClientSalesInvoiceOpenItems.ToListAsync());
    Assert.Empty(await db.ClientPurchaseInvoiceOpenItems.ToListAsync());
  }
}
