using System.Data.Common;
using System.Text.Json.Nodes;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditFieldworkWorkflowTests
{
  [Fact]
  public async Task EmptyBookedSourceMigrationCanRollBackAndForward()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var db = new AuditSphereDbContext(pg.Options);
    var migrations = db.Database.GetMigrations().ToArray();
    var proofMigration = migrations.Single(x => x.EndsWith("_ConfirmationBookedSources", StringComparison.Ordinal));
    await db.GetService<IMigrator>().MigrateAsync(migrations.TakeWhile(x => x != proofMigration).Last());
    Assert.DoesNotContain(proofMigration, await db.Database.GetAppliedMigrationsAsync());
    await db.GetService<IMigrator>().MigrateAsync(migrations.Last());
    Assert.Empty(await db.AuditConfirmationBookedSources.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task ScheduleReviewRefusesAuthorityRevokedBeforeItsTransaction()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    var reviewer = PbcSeed.User(scope.FirmId, "Staff");
    var administrator = PbcSeed.User(scope.FirmId, "Staff");
    var reviewGrant = PbcSeed.Grant(scope.FirmId, reviewer, "Reviewer", scope.ClientId, scope.EngagementId);
    Guid deniedScheduleId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.AddRange(reviewer, administrator);
      setup.RoleGrants.AddRange(reviewGrant, PbcSeed.Grant(scope.FirmId, administrator, "Administrator"));
      await setup.SaveChangesAsync();
      async Task<Guid> Schedule(string reference)
      {
        var created = await AuditFieldworkService.CreateScheduleAsync(setup, scope.Actor,
          new(scope.EngagementId, "RECEIVABLES_LEAD", "synthetic-AR", reference,
            new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), new(2026, 1, 1), new(2026, 9, 19),
            "QAR", "debits positive; credits negative", new string('a', 64), 900m,
            [new("invoice-001", 1, "1200", "Synthetic retained receivable", 900m, "QAR", null, new(2026, 9, 19), null, null, "{}") ]));
        Assert.True(created.Succeeded, created.Message);
        return created.Value!.ScheduleId;
      }
      var allowedId = await Schedule("authorized-review");
      var allowed = await AuditFieldworkService.ReviewScheduleAsync(setup, PbcSeed.Actor(reviewer, "Reviewer"),
        new(allowedId, "Synthetic independently reviewed complete schedule", true));
      Assert.True(allowed.Succeeded, allowed.Message);
      deniedScheduleId = await Schedule("revoked-review");
    }
    var interceptor = new RevokeBeforeScheduleReview(async token =>
    {
      await using var revokeDb = new AuditSphereDbContext(pg.Options);
      var result = await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
        PbcSeed.Actor(administrator, "Administrator"), new(reviewGrant.Id, Reason: "Synthetic schedule approval authority boundary"), token);
      Assert.True(result.Succeeded, result.Message);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var db = new AuditSphereDbContext(options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var reviewed = await AuditFieldworkService.ReviewScheduleAsync(db, PbcSeed.Actor(reviewer, "Reviewer"),
      new(deniedScheduleId, "Must not persist after official revocation", true), timeout.Token);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.True(interceptor.Invoked);
    Assert.NotNull((await verify.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == reviewGrant.Id)).RevokedAt);
    var actual = await verify.AuditSchedules.AsNoTracking().SingleAsync(x => x.Id == deniedScheduleId);
    Assert.True(!reviewed.Succeeded && actual.Status == AuditScheduleStatuses.Reconciled && actual.CompletenessDecision == string.Empty,
      $"Official revocation committed before approval transaction continued: success={reviewed.Succeeded}, status={actual.Status}, decision={actual.CompletenessDecision}");
  }

  private sealed class RevokeBeforeScheduleReview(Func<CancellationToken, Task> revoke) : DbTransactionInterceptor
  {
    public bool Invoked { get; private set; }
    public override async ValueTask<DbTransaction> TransactionStartedAsync(DbConnection connection,
      TransactionEndEventData eventData, DbTransaction result, CancellationToken cancellationToken = default)
    {
      if (!Invoked) { Invoked = true; await revoke(cancellationToken); }
      return result;
    }
  }

  [Fact(DisplayName = "Fieldwork retains signed source rows, reviewed selections, confirmation alternatives and area evidence")]
  public async Task FieldworkEvidence_IsScopedAndReviewable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    await using var db = new AuditSphereDbContext(pg.Options);

    var program = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
      new PublishAuditProgramRequest("2026.2", AuditProgramCatalog.SourceHash));
    var adopted = await AuditProgramService.AdoptAsync(db, scope.Actor,
      new AdoptAuditProgramRequest(scope.EngagementId, program.Value!.ProgramVersionId));
    var procedure = await db.AuditProcedures.SingleAsync(x => x.EngagementId == scope.EngagementId && x.SourceProcedureId == "AWP-02-01");
    Assert.True((await AuditProgramTestHelpers.MarkApplicableAsync(db, scope.Actor, procedure.Id, null)).Succeeded);

    var reviewerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = reviewerId, FirmId = scope.FirmId, Subject = "fieldwork-reviewer-" + reviewerId.ToString("N"),
      TenantId = "tenant-planning", Email = "fieldwork-reviewer@example.test", DisplayName = "Fieldwork Reviewer",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Partner",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = scope.Actor.UserId
    });
    await db.SaveChangesAsync();
    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer", "Partner"]);

    var sourceHash = new string('a', 64);
    var schedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_RECONCILIATION", "bank-001", "receipt-bank-001", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", sourceHash, 50m,
      [
        new("row-001", 1, "1100", "Operating account", 75m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"statement\"}"),
        new("row-002", 2, "1100", "Outstanding cheque", -25m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"statement\"}")
      ]));
    Assert.True(schedule.Succeeded);
    Assert.Equal(AuditScheduleStatuses.Reconciled, schedule.Value!.Status);
    Assert.Equal(50m, schedule.Value.SignedControlTotal);
    var scheduleReview = await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(schedule.Value.ScheduleId, "Complete source listing; no unexplained residual.", true));
    Assert.True(scheduleReview.Succeeded, scheduleReview.ErrorCode + ": " + scheduleReview.Message);

    var glBatchId = Guid.NewGuid();
    var glTransactionId = Guid.NewGuid();
    var glHash = new string('b', 64);
    var glPeriodId = Guid.NewGuid();
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = glPeriodId, FirmId = scope.FirmId, ClientId = scope.ClientId, PeriodCode = "BANK-SOURCE-FY26",
      StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Basis = "IFRS", Currency = "QAR",
      CreatedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    db.SourceImportBatches.Add(new SourceImportBatch
    {
      Id = glBatchId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      PeriodId = glPeriodId, SourceKind = "GL", ProfileVersion = "gl-v1", ParserVersion = "parser-v1",
      RawFileSha256Hex = glHash, NormalizedDatasetDigest = glHash, LegalEntityKey = "bank-001", Currency = "QAR",
      RowCount = 1, ExpectedChunkCount = 1, ExpectedTransactionCount = 1, ExpectedLineCount = 1,
      AcceptedChunkCount = 1, AcceptedTransactionCount = 1, AcceptedLineCount = 1, Status = "SEALED",
      ReceiptReference = "receipt-bank-ledger-001", CreatedByUserId = scope.Actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
    {
      Id = glTransactionId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      ImportBatchId = glBatchId, StableJournalId = "GL-001", DocumentNumber = "GL-001",
      PostingDate = new DateOnly(2026, 9, 19), SourceUser = "client", SourceSystem = "LEDGER",
      Currency = "QAR", CreatedAt = DateTimeOffset.UtcNow
    });
    db.GeneralLedgerLines.Add(new GeneralLedgerLine
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      ImportBatchId = glBatchId, TransactionId = glTransactionId, StableLineId = "GL-001-L1", AccountCode = "1100",
      Debit = 100m, Credit = 0m, OriginalCurrency = "QAR", OriginalAmount = 100m, FunctionalAmount = 100m,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var ledgerSchedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LEDGER", "bank-001", "receipt-bank-ledger-001", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", glHash, 100m,
      [new("ledger-001", 1, "1100", "Operating account ledger", 100m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"ledger\"}")],
      glBatchId));
    var statementSchedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_STATEMENT", "bank-001", "receipt-bank-statement-001", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", new string('c', 64), 90m,
      [new("statement-001", 1, "1100", "Operating account statement", 90m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"statement\"}")]));
    Assert.True(ledgerSchedule.Succeeded);
    Assert.Equal(0m, ledgerSchedule.Value!.Residual);
    Assert.Equal(glBatchId, await db.AuditSchedules.Where(x => x.Id == ledgerSchedule.Value.ScheduleId)
      .Select(x => x.SourceImportBatchId).SingleAsync());
    Assert.Equal(100m, await db.AuditSchedules.Where(x => x.Id == ledgerSchedule.Value.ScheduleId)
      .Select(x => x.GlControlTotal).SingleAsync());
    var wrongEntitySource = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LEDGER", "other-bank", "receipt-bank-ledger-wrong-entity", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", glHash, 100m,
      [new("ledger-wrong-entity-001", 1, "1100", "Other entity ledger", 100m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"ledger\"}")],
      glBatchId));
    Assert.False(wrongEntitySource.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, wrongEntitySource.ErrorCode);
    var mismatchedLedgerSource = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor, new CreateScheduleRequest(
      scope.EngagementId, "BANK_LEDGER", "bank-001", "receipt-bank-ledger-mismatch", new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero),
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), "QAR", "debits positive; credits negative", glHash, 99m,
      [new("ledger-mismatch-001", 1, "1100", "Operating account ledger", 99m, "QAR", null, new DateOnly(2026, 9, 19), null, null, "{\"source\":\"ledger\"}")],
      glBatchId));
    Assert.False(mismatchedLedgerSource.Succeeded);
    Assert.Equal(ErrorCodes.ManifestMismatch, mismatchedLedgerSource.ErrorCode);
    Assert.True(statementSchedule.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(ledgerSchedule.Value!.ScheduleId, "Ledger source reviewed.", true))).Succeeded);
    Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new ReviewScheduleRequest(statementSchedule.Value!.ScheduleId, "Statement source reviewed.", true))).Succeeded);
    var bankReconciliation = await AuditFieldworkService.CreateBankReconciliationAsync(db, scope.Actor,
      new CreateBankReconciliationRequest(scope.EngagementId, procedure.Id, ledgerSchedule.Value.ScheduleId,
        statementSchedule.Value.ScheduleId, new DateOnly(2026, 9, 19),
        [
          new("ledger-balance", AuditBankReconciliationItemTypes.Ledger, 100m, "Ledger balance", "receipt-bank-ledger-001", "ledger-source-001", ledgerSchedule.Value.ScheduleId),
          new("statement-balance", AuditBankReconciliationItemTypes.Statement, 90m, "Statement balance", "receipt-bank-statement-001", "statement-source-001", statementSchedule.Value.ScheduleId),
          new("timing-001", AuditBankReconciliationItemTypes.Timing, -10m, "Outstanding cheque", "receipt-bank-statement-001#timing-001", "timing-evidence-001", statementSchedule.Value.ScheduleId)
        ]));
    Assert.True(bankReconciliation.Succeeded, bankReconciliation.ErrorCode + ": " + bankReconciliation.Message);
    Assert.Equal(AuditBankReconciliationStatuses.Reconciled, bankReconciliation.Value!.Status);
    Assert.Equal(0m, bankReconciliation.Value.Residual);
    Assert.Equal(-10m, bankReconciliation.Value.TimingItemTotal);
    Assert.True((await AuditFieldworkService.ReviewBankReconciliationAsync(db, reviewer,
      new ReviewBankReconciliationRequest(bankReconciliation.Value.BankReconciliationId,
        AuditBankReconciliationStatuses.Approved, "Ledger, statement and timing items reconcile at 19 September 2026."))).Succeeded);
    Assert.Equal(AuditBankReconciliationStatuses.Approved,
      await db.AuditBankReconciliations.Where(x => x.Id == bankReconciliation.Value.BankReconciliationId).Select(x => x.Status).SingleAsync());
    // Read-only drill-down keeps exact stored source provenance separate from approval/currentness.
    var bankId = bankReconciliation.Value.BankReconciliationId;
    var bankBefore = await db.AuditBankReconciliations.AsNoTracking().SingleAsync(x => x.Id == bankId);
    var bankList = await BankReconciliationInspectionQuery.ListAsync(db, reviewer, scope.EngagementId, limit: 1);
    Assert.True(bankList.Succeeded, bankList.Message);
    Assert.Equal(bankId, Assert.Single(bankList.Value!.Rows).Id);
    Assert.Equal(1, bankList.Value.Total);
    var inspection = await BankReconciliationInspectionQuery.DetailAsync(db, reviewer, scope.EngagementId, bankId, limit: 1);
    Assert.True(inspection.Succeeded, inspection.Message);
    Assert.Equal(3, inspection.Value!.Total);
    Assert.Single(inspection.Value.Items);
    Assert.Equal(100m, decimal.Parse(inspection.Value.Reconciliation.LedgerBalance, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Equal(glHash, inspection.Value.LedgerSchedule!.SourceHash);
    Assert.Equal(glBatchId, inspection.Value.LedgerSchedule.SourceImportBatchId);
    Assert.Equal(statementSchedule.Value.ScheduleId, inspection.Value.StatementSchedule!.Id);
    Assert.Equal("CURRENT_GENERATION", inspection.Value.Reconciliation.GenerationState);
    Assert.Equal(bankBefore.ReviewedByUserId, inspection.Value.Reconciliation.ReviewedByUserId);
    var nextBankItem = await BankReconciliationInspectionQuery.DetailAsync(db, reviewer, scope.EngagementId, bankId, offset: 1, limit: 1);
    Assert.True(nextBankItem.Succeeded, nextBankItem.Message);
    Assert.NotEqual(inspection.Value.Items[0].Id, Assert.Single(nextBankItem.Value!.Items).Id);
    Assert.False((await BankReconciliationInspectionQuery.DetailAsync(db, reviewer, fixture.Other.EngagementId, bankId)).Succeeded);
    Assert.False((await BankReconciliationInspectionQuery.ListAsync(db, reviewer, fixture.Other.EngagementId)).Succeeded);
    Assert.False((await BankReconciliationInspectionQuery.DetailAsync(db, reviewer, scope.EngagementId, Guid.NewGuid())).Succeeded);
    Assert.False((await BankReconciliationInspectionQuery.ListAsync(db, reviewer, scope.EngagementId, limit: 26)).Succeeded);
    await using (var callerTransaction = await db.Database.BeginTransactionAsync())
    {
      var callerRead = await BankReconciliationInspectionQuery.DetailAsync(db, reviewer, scope.EngagementId, bankId);
      Assert.False(callerRead.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, callerRead.ErrorCode);
      await callerTransaction.RollbackAsync();
    }
    var bankAfter = await db.AuditBankReconciliations.AsNoTracking().SingleAsync(x => x.Id == bankId);
    Assert.Equal(bankBefore.Status, bankAfter.Status);
    Assert.Equal(bankBefore.Conclusion, bankAfter.Conclusion);
    Assert.Equal(bankBefore.ReviewedByUserId, bankAfter.ReviewedByUserId);
    Assert.Equal(bankBefore.ReviewedAt, bankAfter.ReviewedAt);

    // The selected source is the approved ledger 100, not the statement 90 or the
    // reconciliation residual zero. Human respondent/account mapping remains explicit.
    var bookedSource = await ConfirmationWorkspace.BankSourceAsync(db, scope.Actor, scope.EngagementId, bankId);
    Assert.True(bookedSource.Succeeded, bookedSource.Message);
    Assert.True(bookedSource.Value!.Eligible, bookedSource.Value.Blocker);
    Assert.Equal(100m, decimal.Parse(bookedSource.Value.BookedAmount!, System.Globalization.CultureInfo.InvariantCulture));
    var sourcePin = new ConfirmationBookedSourcePin("BANK_RECONCILIATION", bankId, bookedSource.Value.SnapshotSha256!);
    var typedRequest = new CreateConfirmationRequest(scope.EngagementId, procedure.Id, AuditAreaCodes.CashBank,
      "explicit-bank-account-source", 100m, "QAR", new DateOnly(2026, 9, 20),
      "Bank source respondent", "Synthetic reviewed account-to-respondent mapping", sourcePin);
    foreach (var invalid in new[]
    {
      typedRequest with { BookedAmount = 90m },
      typedRequest with { Currency = "USD" },
      typedRequest with { BookedSource = sourcePin with { ExpectedSnapshotSha256 = new string('0', 64) } },
      typedRequest with { EngagementId = fixture.Other.EngagementId }
    })
      Assert.False((await AuditFieldworkService.CreateConfirmationAsync(db, scope.Actor, invalid)).Succeeded);
    Assert.Empty(await db.AuditConfirmationBookedSources.AsNoTracking().ToListAsync());
    var typed = await AuditFieldworkService.CreateConfirmationAsync(db, scope.Actor, typedRequest);
    Assert.True(typed.Succeeded, typed.Message);
    var typedId = typed.Value!.ConfirmationCaseId;
    var retainedSource = await db.AuditConfirmationBookedSources.AsNoTracking().SingleAsync(x => x.ConfirmationCaseId == typedId);
    Assert.Equal(sourcePin.ExpectedSnapshotSha256, retainedSource.SnapshotSha256);
    var typedDetail = await ConfirmationWorkspace.DetailAsync(db, reviewer, scope.EngagementId, typedId);
    Assert.True(typedDetail.Succeeded, typedDetail.Message);
    Assert.Equal("CURRENT", typedDetail.Value!.BookedSource!.Currentness);
    Assert.Equal(100m, decimal.Parse(typedDetail.Value.BookedSource.BookedAmount!, System.Globalization.CultureInfo.InvariantCulture));
    Assert.Equal("2026-09-19", typedDetail.Value.BookedSource.SourceAsOfDate);
    var invalidBatch = await AuditConfirmationBatchService.CreateConfirmationBatchAsync(db, scope.Actor,
      new(scope.EngagementId, procedure.Id, AuditAreaCodes.CashBank, "QAR", new DateOnly(2026, 9, 20),
        [new("typed-batch-valid", 100m, "Bank A", "Explicit account mapping", sourcePin),
         new("typed-batch-invalid", 90m, "Bank B", "Explicit account mapping", sourcePin)]));
    Assert.False(invalidBatch.Succeeded);
    Assert.False(await db.AuditConfirmationCases.AnyAsync(x => x.SourceRecordId == "typed-batch-valid"));
    var validBatch = await AuditConfirmationBatchService.CreateConfirmationBatchAsync(db, scope.Actor,
      new(scope.EngagementId, procedure.Id, AuditAreaCodes.CashBank, "QAR", new DateOnly(2026, 9, 20),
        [new("typed-batch-a", 100m, "Bank A", "Explicit account mapping A", sourcePin),
         new("typed-batch-b", 100m, "Bank B", "Explicit account mapping B", sourcePin)]));
    Assert.True(validBatch.Succeeded, validBatch.Message);
    Assert.Equal(200m, validBatch.Value!.TotalBookedAmount);
    var batchCaseIds = validBatch.Value.Cases.Select(x => x.ConfirmationCaseId).ToArray();
    Assert.Equal(2, await db.AuditConfirmationBookedSources.CountAsync(x => batchCaseIds.Contains(x.ConfirmationCaseId)));
    var changedProof = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE audit_confirmation_booked_sources SET snapshot_json='{{}}' WHERE confirmation_case_id={typedId}"));
    Assert.Equal("55000", changedProof.SqlState);
    var reboundCase = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE audit_confirmation_cases SET booked_amount=90 WHERE id={typedId}"));
    Assert.Equal("55000", reboundCase.SqlState);
    Assert.Equal(retainedSource.SnapshotJson, (await db.AuditConfirmationBookedSources.AsNoTracking()
      .SingleAsync(x => x.ConfirmationCaseId == typedId)).SnapshotJson);
    foreach (var forgeSnapshot in new[] { false, true })
    {
      await using var forgedDb = new AuditSphereDbContext(pg.Options);
      var unbound = await AuditFieldworkService.CreateConfirmationAsync(forgedDb, scope.Actor,
        typedRequest with { SourceRecordId = "sql-boundary-" + forgeSnapshot, BookedSource = null,
          BookedAmount = forgeSnapshot ? 100m : 90m });
      Assert.True(unbound.Succeeded, unbound.Message);
      var unboundCase = await forgedDb.AuditConfirmationCases.AsNoTracking()
        .SingleAsync(x => x.Id == unbound.Value!.ConfirmationCaseId);
      var forgedJson = retainedSource.SnapshotJson;
      if (forgeSnapshot)
      {
        var document = JsonNode.Parse(forgedJson)!;
        document["Bank"]!["LedgerBalance"] = "90";
        forgedJson = document.ToJsonString();
      }
      await using var forgedTx = await forgedDb.Database.BeginTransactionAsync();
      forgedDb.AuditConfirmationBookedSources.Add(new()
      {
        ConfirmationCaseId = unboundCase.Id, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, BankReconciliationId = bankId,
        LedgerScheduleId = retainedSource.LedgerScheduleId, StatementScheduleId = retainedSource.StatementScheduleId,
        SnapshotJson = forgedJson, SnapshotSha256 = Hashing.Sha256Hex(forgedJson),
        CreatedByUserId = unboundCase.CreatedByUserId, CreatedAt = unboundCase.CreatedAt
      });
      await forgedDb.SaveChangesAsync();
      var forgedRefused = await Assert.ThrowsAsync<PostgresException>(() => forgedTx.CommitAsync());
      Assert.Equal("23514", forgedRefused.SqlState);
    }
    var proofDelete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM audit_confirmation_booked_sources WHERE confirmation_case_id={typedId}"));
    Assert.Equal("55000", proofDelete.SqlState);
    var proofTruncate = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(
      "TRUNCATE audit_confirmation_booked_sources"));
    Assert.Equal("55000", proofTruncate.SqlState);
    var migrationIds = db.Database.GetMigrations().ToArray();
    var precedingProofMigration = migrationIds.TakeWhile(x => !x.EndsWith("_ConfirmationBookedSources", StringComparison.Ordinal)).Last();
    var downRefused = await Assert.ThrowsAsync<InvalidOperationException>(() => db.GetService<IMigrator>().MigrateAsync(precedingProofMigration));
    Assert.Equal("55000", Assert.IsType<PostgresException>(downRefused.InnerException).SqlState);

    var missingProposedJournal = await AuditFieldworkService.CreateBankReconciliationAsync(db, scope.Actor,
      new CreateBankReconciliationRequest(scope.EngagementId, procedure.Id, ledgerSchedule.Value.ScheduleId,
        statementSchedule.Value.ScheduleId, new DateOnly(2026, 9, 19),
        [new("proposed-001", AuditBankReconciliationItemTypes.ProposedCorrection, 5m, "Proposed correction", "bank-workpaper-001", "correction-evidence-001")]));
    Assert.False(missingProposedJournal.Succeeded);

    // Aggregate-difference evaluation stays blocked without current approved materiality (it needs the
    // approved assessment to build its source-bound schedule).
    var unapprovedAggregate = await AuditFieldworkService.RecordAreaAssessmentAsync(db, scope.Actor,
      new RecordAreaAssessmentRequest(scope.EngagementId, null, AuditAreaCodes.AuditDifferences,
        AuditAreaAssessmentKinds.AggregateDifferences, "audit-differences-aggregate.v1", "{}", null, null,
        null, null, null, null, null, ["aggregate-difference-schedule"], "Prepared conclusion."));
    Assert.False(unapprovedAggregate.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unapprovedAggregate.ErrorCode);

    // Substantive item testing requires the approved planning basis (STE-REM-04): sealed balanced TB,
    // approved FSLI mapping and current independently approved materiality, established through the
    // real calculate/approve commands before execution.
    await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor, reviewer);

    var selection = await AuditFieldworkService.CreateSelectionAsync(db, scope.Actor, new CreateSelectionRequest(
      scope.EngagementId, procedure.Id, schedule.Value.ScheduleId, null, "100 percent of bank accounts", "Both signed rows selected for reconciliation.",
      [new("row-001", 75m, "QAR", "All rows in the identified bank account", null), new("row-002", -25m, "QAR", "All rows in the identified bank account", null)]));
    Assert.True(selection.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
      new ReviewSelectionRequest(selection.Value!.SelectionId, AuditSelectionStatuses.Reviewed, null))).Succeeded);

    var items = await db.AuditSelectionItems.Where(x => x.SelectionId == selection.Value.SelectionId).ToListAsync();
    foreach (var item in items)
    {
      var test = await AuditFieldworkService.RecordItemTestAsync(db, scope.Actor,
        new RecordItemTestRequest(item.Id, "Agreed the selected row to the bank statement.", ["receipt-bank-001#" + item.StableRowId],
          AuditItemTestResults.Pass, null, null, null));
      Assert.True(test.Succeeded);
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(test.Value!.AuditItemTestId, AuditItemTestReviewDecisions.Reviewed, "Evidence agrees."))).Succeeded);
    }
    Assert.Equal(2, await db.AuditItemTestReviews.CountAsync());

    var confirmation = await AuditFieldworkService.CreateConfirmationAsync(db, scope.Actor, new CreateConfirmationRequest(
      scope.EngagementId, procedure.Id, AuditAreaCodes.CashBank, "bank-001", 50m, "QAR", new DateOnly(2026, 9, 19),
      "Bank relationship manager", "Client master contact list reviewed by partner."));
    Assert.True(confirmation.Succeeded);
    Assert.False(await db.AuditConfirmationBookedSources.AnyAsync(x => x.ConfirmationCaseId == confirmation.Value!.ConfirmationCaseId));
    Assert.True((await AuditFieldworkService.ApproveConfirmationAsync(db, reviewer, confirmation.Value!.ConfirmationCaseId)).Succeeded);
    Assert.True((await AuditFieldworkService.RecordDispatchEvidenceAsync(db, scope.Actor,
      new RecordConfirmationDispatchRequest(confirmation.Value.ConfirmationCaseId, "provider-dispatch-001"))).Succeeded);
    Assert.True((await AuditFieldworkService.RecordConfirmationResponseAsync(db, scope.Actor,
      new RecordConfirmationResponseRequest(confirmation.Value.ConfirmationCaseId, "DIRECT", "PORTAL", "no-response-2026-09-26",
        null, "No response received by the approved follow-up date.", AuditConfirmationDecisions.NoResponse))).Succeeded);
    var responseId = await db.AuditConfirmationResponses.Select(x => x.Id).SingleAsync();
    Assert.True((await AuditFieldworkService.ReviewConfirmationResponseAsync(db, reviewer,
      new ReviewConfirmationResponseRequest(responseId))).Succeeded);
    var alternative = await AuditFieldworkService.RecordAlternativeProcedureAsync(db, scope.Actor,
      new RecordAlternativeProcedureRequest(confirmation.Value.ConfirmationCaseId, "Inspect subsequent cleared transactions.",
        ["bank-statement-subsequent-001"], "Subsequent clearing supports the recorded balance."));
    Assert.True(alternative.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewAlternativeProcedureAsync(db, reviewer,
      new ReviewAlternativeProcedureRequest(await db.AuditAlternativeProcedures.Select(x => x.Id).SingleAsync(), null))).Succeeded);
    Assert.True((await AuditFieldworkService.CloseConfirmationAsync(db, reviewer,
      new CloseConfirmationRequest(confirmation.Value.ConfirmationCaseId, "Alternative work is adequate."))).Succeeded);

    var assessment = await AuditFieldworkService.RecordAreaAssessmentAsync(db, scope.Actor, new RecordAreaAssessmentRequest(
      scope.EngagementId, procedure.Id, AuditAreaCodes.AnalyticalReview, "MONTHLY_TREND", "methodology:analytics-v1",
      "{\"periods\":12,\"thresholdPercent\":10}", 100m, 102m, 2m, 2m, "QAR",
      new DateOnly(2026, 1, 1), new DateOnly(2026, 9, 19), ["analytics-source-001"], "Variance is below the approved investigation threshold."));
    Assert.True(assessment.Succeeded);
    Assert.True((await AuditFieldworkService.ReviewAreaAssessmentAsync(db, reviewer,
      new ReviewAreaAssessmentRequest(assessment.Value!.AuditAreaAssessmentId, AuditAreaAssessmentStatuses.Reviewed, null))).Succeeded);

    var difference = await AuditFieldworkService.RecordDifferenceAsync(db, scope.Actor,
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Cash", "KNOWN", "Unpresented cheque timing difference.", -25m, "QAR",
        "materiality-2026", "No qualitative concern identified."));
    Assert.True(difference.Succeeded);
    var differenceMetadata = await db.AuditDifferences.SingleAsync(x => x.Id == difference.Value!.AuditDifferenceId);
    Assert.Equal("materiality-2026", differenceMetadata.MaterialityReference);
    Assert.Equal("No qualitative concern identified.", differenceMetadata.QualitativeConcerns);
    var unlinkedCorrection = await AuditFieldworkService.EvaluateDifferenceAsync(db, reviewer,
      new EvaluateDifferenceRequest(difference.Value!.AuditDifferenceId, true, "Correction claimed.", null, null));
    Assert.False(unlinkedCorrection.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unlinkedCorrection.ErrorCode);
    Assert.True((await AuditFieldworkService.EvaluateDifferenceAsync(db, reviewer,
      new EvaluateDifferenceRequest(difference.Value!.AuditDifferenceId, false, "Evaluated against the final unadjusted differences schedule.", "Management will not post; assessed in aggregate.", null))).Succeeded);
    var rejected = await AuditFieldworkService.SetDifferenceCorrectionStateAsync(db, reviewer,
      new SetDifferenceCorrectionStateRequest(difference.Value.AuditDifferenceId, AuditDifferenceCorrectionStates.Rejected,
        "Management rejected the proposed correction; the difference remains unadjusted."));
    Assert.True(rejected.Succeeded, rejected.Message);
    Assert.Equal(AuditDifferenceCorrectionStates.Rejected,
      await db.AuditDifferences.Where(x => x.Id == difference.Value.AuditDifferenceId).Select(x => x.CorrectionState).SingleAsync());
    var offsettingDifference = await AuditFieldworkService.RecordDifferenceAsync(db, scope.Actor,
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Cash", "KNOWN", "Offsetting bank timing difference.", 25m, "QAR"));
    Assert.True(offsettingDifference.Succeeded);
    var summaries = await AuditFieldworkService.GetDifferenceSummariesAsync(db, reviewer, scope.EngagementId);
    Assert.True(summaries.Succeeded);
    var qar = Assert.Single(summaries.Value!);
    Assert.Equal("QAR", qar.Currency);
    Assert.Equal(2, qar.DifferenceCount);
    Assert.Equal(50m, qar.GrossAmount);
    Assert.Equal(0m, qar.SignedNetAmount);
    Assert.Equal(50m, qar.UnadjustedGrossAmount);
    Assert.Equal(0m, qar.UnadjustedSignedNetAmount);
    Assert.Equal(0m, qar.CorrectedGrossAmount);

    var aggregate = await AuditFieldworkService.RecordAreaAssessmentAsync(db, scope.Actor,
      new RecordAreaAssessmentRequest(scope.EngagementId, null, AuditAreaCodes.AuditDifferences,
        AuditAreaAssessmentKinds.AggregateDifferences, "audit-differences-aggregate.v1", "{}", null, null, null,
        null, null, null, null, ["aggregate-difference-schedule"], "Gross QAR differences exceed performance materiality; unadjusted amounts and qualitative factors require partner reporting judgment."));
    Assert.True(aggregate.Succeeded, aggregate.Message);
    var aggregateRow = await db.AuditAreaAssessments.SingleAsync(x => x.Id == aggregate.Value!.AuditAreaAssessmentId);
    Assert.Contains("audit-difference-aggregate.v1", aggregateRow.InputSnapshotJson, StringComparison.Ordinal);
    Assert.True((await AuditFieldworkService.ReviewAreaAssessmentAsync(db, reviewer,
      new ReviewAreaAssessmentRequest(aggregateRow.Id, AuditAreaAssessmentStatuses.Reviewed, null))).Succeeded);

    var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, scope.EngagementId);
    Assert.True(completion.Succeeded);
    Assert.False(completion.Value!.Ready);
    Assert.Contains(completion.Value.Blockers, x => x.StartsWith("procedure:", StringComparison.Ordinal));
    Assert.DoesNotContain("difference-aggregate:missing-stale-or-unreviewed", completion.Value.Blockers);
    Assert.True((await AuditFieldworkService.RecordDifferenceAsync(db, scope.Actor,
      new RecordDifferenceRequest(scope.EngagementId, procedure.Id, "Revenue", "KNOWN", "Later identified cut-off difference.", 100m, "QAR"))).Succeeded);
    var changedSchedule = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, scope.EngagementId);
    Assert.Contains("difference-aggregate:missing-stale-or-unreviewed", changedSchedule.Value!.Blockers);
    Assert.Equal(2, await db.AuditScheduleRows.CountAsync(x => x.ScheduleId == schedule.Value.ScheduleId));
    Assert.Equal(3, await db.AuditDifferences.CountAsync());

    // A later client generation makes the still-approved Bank source schedules stale.
    // This is a generation-change fixture, not a runtime interleaving/race simulation.
    var currentBankGeneration = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.Id == scope.ClientId)
      .Select(x => x.InputGeneration).SingleAsync();
    var sourceSchedules = await db.AuditSchedules.AsNoTracking()
      .Where(x => x.Id == ledgerSchedule.Value.ScheduleId || x.Id == statementSchedule.Value.ScheduleId)
      .ToListAsync();
    Assert.Equal(2, sourceSchedules.Count);
    Assert.All(sourceSchedules, x => Assert.Equal(currentBankGeneration, x.InputGeneration));
    var bankCountBeforeStaleSourceChecks = await db.AuditBankReconciliations.CountAsync();
    var staleCurrentGeneration = currentBankGeneration + 1;
    await db.ClientSafetyStates.Where(x => x.FirmId == scope.FirmId && x.Id == scope.ClientId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, staleCurrentGeneration));

    var staleTyped = await AuditFieldworkService.CreateConfirmationAsync(db, scope.Actor,
      typedRequest with { SourceRecordId = "stale-typed-bank-source" });
    Assert.False(staleTyped.Succeeded);
    Assert.False(await db.AuditConfirmationCases.AnyAsync(x => x.SourceRecordId == "stale-typed-bank-source"));
    var retainedStaleSource = await ConfirmationWorkspace.DetailAsync(db, reviewer, scope.EngagementId, typedId);
    Assert.True(retainedStaleSource.Succeeded, retainedStaleSource.Message);
    Assert.Equal("STALE", retainedStaleSource.Value!.BookedSource!.Currentness);
    Assert.Equal(retainedSource.SnapshotSha256, retainedStaleSource.Value.BookedSource.SnapshotSha256);
    Assert.Equal(100m, decimal.Parse(retainedStaleSource.Value.BookedSource.BookedAmount!, System.Globalization.CultureInfo.InvariantCulture));

    var staleScheduleCreate = await AuditFieldworkService.CreateBankReconciliationAsync(db, reviewer,
      new CreateBankReconciliationRequest(scope.EngagementId, procedure.Id, ledgerSchedule.Value.ScheduleId,
        statementSchedule.Value.ScheduleId, new DateOnly(2026, 9, 19),
        [
          new("stale-create-ledger", AuditBankReconciliationItemTypes.Ledger, 100m, "Ledger balance", "receipt-bank-ledger-001", "ledger-source-001", ledgerSchedule.Value.ScheduleId),
          new("stale-create-statement", AuditBankReconciliationItemTypes.Statement, 90m, "Statement balance", "receipt-bank-statement-001", "statement-source-001", statementSchedule.Value.ScheduleId),
          new("stale-create-timing", AuditBankReconciliationItemTypes.Timing, -10m, "Outstanding cheque", "receipt-bank-statement-001#timing-001", "timing-evidence-001", statementSchedule.Value.ScheduleId)
        ]));

    // Synthetic historical-invalid Bank state: the Bank record is stamped with the current
    // generation while its already-approved linked schedules retain the prior generation.
    // This isolates the review guard; it does not claim to prove a runtime race.
    var syntheticStaleBankId = Guid.NewGuid();
    db.AuditBankReconciliations.Add(new AuditBankReconciliation
    {
      Id = syntheticStaleBankId, FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      ProcedureId = procedure.Id, LedgerScheduleId = ledgerSchedule.Value.ScheduleId,
      StatementScheduleId = statementSchedule.Value.ScheduleId, AsOfDate = new DateOnly(2026, 9, 19),
      Currency = "QAR", LedgerBalance = 100m, StatementBalance = 90m, TimingItemTotal = -10m,
      ProposedCorrectionTotal = 0m, Residual = 0m, InputGeneration = staleCurrentGeneration,
      Status = AuditBankReconciliationStatuses.Reconciled, CreatedByUserId = scope.Actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var staleScheduleReview = await AuditFieldworkService.ReviewBankReconciliationAsync(db, reviewer,
      new ReviewBankReconciliationRequest(syntheticStaleBankId, AuditBankReconciliationStatuses.Approved,
        "Synthetic stale linked schedule review must be refused."));

    Assert.False(staleScheduleCreate.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleScheduleCreate.ErrorCode);
    Assert.False(staleScheduleReview.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleScheduleReview.ErrorCode);
    Assert.Equal(bankCountBeforeStaleSourceChecks + 1, await db.AuditBankReconciliations.CountAsync());
    var originalBankAfterStaleChecks = await db.AuditBankReconciliations.AsNoTracking().SingleAsync(x => x.Id == bankId);
    Assert.Equal(bankBefore.Status, originalBankAfterStaleChecks.Status);
    Assert.Equal(bankBefore.ReviewedByUserId, originalBankAfterStaleChecks.ReviewedByUserId);
    Assert.Equal(bankBefore.ReviewedAt, originalBankAfterStaleChecks.ReviewedAt);
    Assert.Equal(100m, originalBankAfterStaleChecks.LedgerBalance);
    Assert.Equal(90m, originalBankAfterStaleChecks.StatementBalance);
    Assert.Equal(-10m, originalBankAfterStaleChecks.TimingItemTotal);
    var syntheticStaleBankAfterReview = await db.AuditBankReconciliations.AsNoTracking()
      .SingleAsync(x => x.Id == syntheticStaleBankId);
    Assert.Equal(AuditBankReconciliationStatuses.Reconciled, syntheticStaleBankAfterReview.Status);
    Assert.Null(syntheticStaleBankAfterReview.ReviewedByUserId);
    Assert.Null(syntheticStaleBankAfterReview.ReviewedAt);

    // Revoke official authority after the evidence snapshot commits. Final authorization must
    // observe new DB state rather than reuse the repeatable-read snapshot.
    var administrator = PbcSeed.User(scope.FirmId, "Staff");
    db.Users.Add(administrator);
    db.RoleGrants.Add(PbcSeed.Grant(scope.FirmId, administrator, "Administrator"));
    await db.SaveChangesAsync();
    var partnerGrant = await db.RoleGrants.Where(x => x.UserId == scope.Actor.UserId && x.RevokedAt == null)
      .Select(x => x.Id).SingleAsync();
    var revokeAfterSnapshot = new RevokeAfterBankSnapshot(async ct =>
    {
      await using var revokeDb = new AuditSphereDbContext(pg.Options);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
        PbcSeed.Actor(administrator, "Administrator"), new(partnerGrant, Reason: "Synthetic bank inspection authority boundary"), ct);
      Assert.True(revoked.Succeeded, revoked.Message);
    });
    var readOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revokeAfterSnapshot).Options;
    await using var readDb = new AuditSphereDbContext(readOptions);
    var revokedRead = await BankReconciliationInspectionQuery.DetailAsync(readDb, scope.Actor, scope.EngagementId, bankId);
    Assert.True(revokeAfterSnapshot.Invoked);
    Assert.False(revokedRead.Succeeded);
    Assert.Null(revokedRead.Value);
    Assert.Equal(bankBefore.ReviewedAt, (await db.AuditBankReconciliations.AsNoTracking().SingleAsync(x => x.Id == bankId)).ReviewedAt);
  }
  private sealed class RevokeAfterBankSnapshot(Func<CancellationToken, Task> revoke) : DbTransactionInterceptor
  {
    private bool invoked;
    public bool Invoked => invoked;
    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
      CancellationToken cancellationToken = default)
    {
      if (invoked) return;
      invoked = true;
      await revoke(cancellationToken);
    }
  }

}
