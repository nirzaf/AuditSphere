using System.Text.RegularExpressions;
using System.Text.Json;
using Npgsql;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// WU-004: after applying every migration to an owned PostgreSQL database, each trigger the migrations leave in
/// place exists and is enabled, and no seeded single-column key between firm-scoped tables accepts another firm's row.
/// </summary>
[Trait("Profile", "Database")]
[Trait("CompletionUnit", "WU-004")]
public sealed partial class CompletionSchemaInvariantTests(Xunit.Abstractions.ITestOutputHelper output)
{
  [Fact]
  public async Task MigratedSchema_KeepsEveryMigrationTriggerEnabled()
  {
    var expected = ExpectedTriggers();
    Assert.True(expected.Count > 50, $"Only {expected.Count} triggers parsed from migrations; the probe would be vacuous.");
    await using var pg = await PgTestSchema.CreateAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    var actual = new Dictionary<string, char>(StringComparer.Ordinal);
    await using (var command = new NpgsqlCommand("SELECT tgname, tgenabled FROM pg_trigger WHERE NOT tgisinternal", connection))
    await using (var reader = await command.ExecuteReaderAsync())
      while (await reader.ReadAsync()) actual[reader.GetString(0)] = reader.GetChar(1);

    var missing = expected.Where(name => !actual.ContainsKey(name)).ToList();
    var disabled = expected.Where(name => actual.TryGetValue(name, out var state) && state is not ('O' or 'A')).ToList();
    output.WriteLine($"Triggers expected from migrations: {expected.Count}; present: {expected.Count - missing.Count}; disabled: {disabled.Count}");
    Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    Assert.True(disabled.Count == 0, "Disabled: " + string.Join(", ", disabled));
  }

  [Fact]
  public async Task MigratedSchema_ForeignKeysBetweenFirmScopedTablesCarryTheFirm()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    const string sql = """
      SELECT c.conname, child.relname, parent.relname,
             CASE WHEN cardinality(c.conkey) = 1 THEN (SELECT attname FROM pg_attribute WHERE attrelid = c.conrelid AND attnum = c.conkey[1]) END,
             CASE WHEN cardinality(c.confkey) = 1 THEN (SELECT attname FROM pg_attribute WHERE attrelid = c.confrelid AND attnum = c.confkey[1]) END,
             EXISTS (
               SELECT 1
               FROM unnest(c.conkey, c.confkey) AS key_pair(child_attnum, parent_attnum)
               JOIN pg_attribute child_scope
                 ON child_scope.attrelid = c.conrelid AND child_scope.attnum = key_pair.child_attnum
               JOIN pg_attribute parent_scope
                 ON parent_scope.attrelid = c.confrelid AND parent_scope.attnum = key_pair.parent_attnum
               WHERE child_scope.attname = 'firm_id' AND parent_scope.attname = 'firm_id'
             ) AS carries_firm
      FROM pg_constraint c
      JOIN pg_class child ON child.oid = c.conrelid
      JOIN pg_class parent ON parent.oid = c.confrelid
      JOIN pg_namespace n ON n.oid = child.relnamespace
      WHERE c.contype = 'f' AND n.nspname = current_schema()
        AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.conrelid AND a.attname = 'firm_id' AND NOT a.attisdropped)
        AND EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = c.confrelid AND a.attname = 'firm_id' AND NOT a.attisdropped)
        -- Treat a key as scope-bound only when a second FK preserves every child-to-parent column mapping
        -- and explicitly maps firm_id to the parent firm_id. Merely sharing child columns with some other FK
        -- can otherwise hide an unscoped reference to a different unique key on the same parent table.
        AND NOT EXISTS (
          SELECT 1
          FROM pg_constraint o
          WHERE o.contype = 'f' AND o.oid <> c.oid
            AND o.conrelid = c.conrelid AND o.confrelid = c.confrelid
            AND EXISTS (
              SELECT 1
              FROM unnest(o.conkey, o.confkey) AS scoped(child_attnum, parent_attnum)
              JOIN pg_attribute child_scope
                ON child_scope.attrelid = o.conrelid AND child_scope.attnum = scoped.child_attnum
              JOIN pg_attribute parent_scope
                ON parent_scope.attrelid = o.confrelid AND parent_scope.attnum = scoped.parent_attnum
              WHERE child_scope.attname = 'firm_id' AND parent_scope.attname = 'firm_id'
            )
            AND NOT EXISTS (
              SELECT 1
              FROM unnest(c.conkey, c.confkey) AS direct(child_attnum, parent_attnum)
              WHERE NOT EXISTS (
                SELECT 1
                FROM unnest(o.conkey, o.confkey) AS combined(child_attnum, parent_attnum)
                WHERE combined.child_attnum = direct.child_attnum
                  AND combined.parent_attnum = direct.parent_attnum
              )
            )
        )
      ORDER BY child.relname, c.conname
      """;
    var scoped = 0;
    var unscoped = new List<string>();
    var probes = new List<(string Constraint, string Child, string Column, string Parent, string Reference)>();
    await using (var command = new NpgsqlCommand(sql, connection))
    await using (var reader = await command.ExecuteReaderAsync())
      while (await reader.ReadAsync())
      {
        scoped++;
        if (reader.GetBoolean(5)) continue;
        unscoped.Add($"{reader.GetString(1)} -> {reader.GetString(2)} ({reader.GetString(0)})");
        if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
          probes.Add((reader.GetString(0), reader.GetString(1), reader.GetString(3), reader.GetString(2), reader.GetString(4)));
      }
    output.WriteLine($"Foreign keys between firm-scoped tables: {scoped}; without exact firm_id mapping: {unscoped.Count}");
    Assert.True(scoped > 20, $"Only {scoped} firm-scoped foreign keys found; the probe would be vacuous.");

    // Probe each single-column key: copy a seeded child row, repoint it at another firm's parent and observe
    // whether the database refuses it. Every probe is rolled back; nothing is committed.
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var ownSpareBudgetId = await SeedBudgetChildRowsAsync(pg, own);
    var foreignSpareBudgetId = await SeedBudgetChildRowsAsync(pg, foreign);
    var accepted = new List<string>();
    var notProbed = new List<string>();
    var unexpectedRefusals = new List<string>();
    var refusedByTargetForeignKey = 0;
    await using var transaction = await connection.BeginTransactionAsync();
    await new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE", connection, transaction).ExecuteNonQueryAsync();
    foreach (var (constraint, child, column, parent, reference) in probes)
    {
      await new NpgsqlCommand("SAVEPOINT probe", connection, transaction).ExecuteNonQueryAsync();
      var probe = $"""
        INSERT INTO "{child}"
        SELECT (jsonb_populate_record(NULL::"{child}", to_jsonb(c) || jsonb_build_object(
          '{column}', (SELECT p."{reference}" FROM "{parent}" p WHERE p.firm_id <> c.firm_id LIMIT 1), 'id', gen_random_uuid()))).*
        FROM "{child}" c
        WHERE c.firm_id IS NOT NULL AND EXISTS (SELECT 1 FROM "{parent}" p WHERE p.firm_id <> c.firm_id)
        LIMIT 1
        """;
      string outcome;
      try
      {
        var rows = await new NpgsqlCommand(probe, connection, transaction).ExecuteNonQueryAsync();
        outcome = rows == 0 ? "NO_FIXTURE_ROW" : "ACCEPTED_CROSS_FIRM";
        if (rows == 0) notProbed.Add($"{child}.{column} -> {parent}");
        else accepted.Add($"{child}.{column} -> {parent}");
      }
      catch (PostgresException exception)
      {
        if (exception.SqlState == "23503" && exception.ConstraintName == constraint)
        {
          refusedByTargetForeignKey++;
          outcome = $"REFUSED_BY_TARGET_FK {exception.SqlState} {constraint}";
        }
        else
        {
          unexpectedRefusals.Add($"{child}.{column} -> {parent}: {exception.SqlState} {exception.ConstraintName ?? "(no constraint)"}");
          outcome = $"UNEXPECTED_REFUSAL {exception.SqlState} {exception.ConstraintName ?? "(no constraint)"}";
        }
      }
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT probe", connection, transaction).ExecuteNonQueryAsync();
      output.WriteLine($"PROBE {child}.{column} -> {parent}: {outcome}");
    }
    foreach (var child in new[] { "budget_approvals", "budget_preparations" })
    {
      await AssertBudgetReceiptCrossFirmReferenceRefusedAsync(connection, transaction, child, "budget_id", own,
        foreign, ownSpareBudgetId, foreignSpareBudgetId);
      await AssertBudgetReceiptCrossFirmReferenceRefusedAsync(connection, transaction, child, "engagement_id", own,
        foreign, ownSpareBudgetId, foreignSpareBudgetId);
    }
    await transaction.RollbackAsync();
    output.WriteLine($"Single-column scoped-key probes: discovered {probes.Count}; refused by target FK {refusedByTargetForeignKey}; " +
      $"accepted cross-firm {accepted.Count}; no fixture row {notProbed.Count}; unexpected refusal {unexpectedRefusals.Count}");
    Assert.True(accepted.Count == 0, "Cross-firm references accepted: " + string.Join(", ", accepted));
    Assert.True(unexpectedRefusals.Count == 0, "Probe blocked before the target FK: " + string.Join("; ", unexpectedRefusals));
  }

  [Fact]
  public async Task StaffingChangeReceipts_RejectForeignFirmParentsBeforePublication()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    async Task<(StaffingChange Row, StaffingChangePreview Probe)> AssignAsync(PbcSeed.Fixture fixture)
    {
      var actor = PbcSeed.Actor(fixture.Admin, "Administrator");
      var request = new StaffingChangeRequest(Guid.NewGuid(),
        new("ASSIGN", fixture.Reviewer.Id, StaffingLevels.StaffAssociate));
      var preview = await StaffingChangeWorkspace.PreviewAsync(db, actor, fixture.EngagementId, request);
      Assert.True(preview.Succeeded, preview.Message);
      var probe = await StaffingChangeWorkspace.PreviewAsync(db, actor, fixture.EngagementId,
        request with { RequestId = Guid.NewGuid() });
      Assert.True(probe.Succeeded, probe.Message);
      var published = await StaffingChangeWorkspace.ExecuteAsync(db, actor, fixture.EngagementId,
        request with { Reviewed = true, RequestHash = preview.Value!.RequestHash, ReviewBasis = preview.Value.ReviewBasis });
      Assert.True(published.Succeeded, published.Message);
      return (await db.StaffingChanges.AsNoTracking().SingleAsync(x => x.Id == published.Value!.Id), probe.Value!);
    }

    var original = await AssignAsync(own);
    var other = await AssignAsync(foreign);
    var before = JsonSerializer.Serialize(await db.StaffingChanges.AsNoTracking().OrderBy(x => x.Id).ToListAsync());
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    await new NpgsqlCommand("SET CONSTRAINTS ALL IMMEDIATE", connection, transaction).ExecuteNonQueryAsync();

    async Task<int> InsertAsync(Guid engagementId, Guid assignmentId)
    {
      await using var command = new NpgsqlCommand("""
        INSERT INTO staffing_changes
        SELECT (jsonb_populate_record(NULL::staffing_changes, to_jsonb(c) || jsonb_build_object(
          'id', @id, 'request_id', @request_id, 'request_hash', @request_hash,
          'review_basis', @review_basis, 'engagement_id', @engagement_id,
          'assignment_id', @assignment_id, 'preview_json', @preview_json))).*
        FROM staffing_changes c WHERE c.id = @source_id
        """, connection, transaction);
      command.Parameters.AddWithValue("id", Guid.NewGuid());
      command.Parameters.AddWithValue("request_id", original.Probe.RequestId);
      command.Parameters.AddWithValue("request_hash", original.Probe.RequestHash);
      command.Parameters.AddWithValue("review_basis", original.Probe.ReviewBasis);
      command.Parameters.AddWithValue("engagement_id", engagementId);
      command.Parameters.AddWithValue("assignment_id", assignmentId);
      command.Parameters.AddWithValue("preview_json", JsonSerializer.Serialize(original.Probe with { EngagementId = engagementId }));
      command.Parameters.AddWithValue("source_id", original.Row.Id);
      return await command.ExecuteNonQueryAsync();
    }

    await new NpgsqlCommand("SAVEPOINT staffing_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    Assert.Equal(1, await InsertAsync(own.EngagementId, original.Row.AssignmentId));
    output.WriteLine("SAME_FIRM_CURRENT_ASSIGNMENT: ACCEPTED rows=1");
    await new NpgsqlCommand("ROLLBACK TO SAVEPOINT staffing_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    foreach (var (label, engagementId, assignmentId) in new[]
    {
      ("FOREIGN_ENGAGEMENT", foreign.EngagementId, original.Row.AssignmentId),
      ("FOREIGN_ASSIGNMENT", own.EngagementId, other.Row.AssignmentId)
    })
    {
      var exception = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync(engagementId, assignmentId));
      Assert.Equal("23514", exception.SqlState);
      Assert.Equal("Staffing change requires exact authorized committed assignment", exception.MessageText);
      output.WriteLine($"{label}: REFUSED SqlState={exception.SqlState} publication guard");
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT staffing_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    }
    await transaction.RollbackAsync();
    Assert.Equal(before, JsonSerializer.Serialize(await db.StaffingChanges.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
  }

  private static async Task AssertBudgetReceiptCrossFirmReferenceRefusedAsync(
    NpgsqlConnection connection, NpgsqlTransaction transaction, string child, string column,
    PbcSeed.Fixture own, PbcSeed.Fixture foreign, Guid ownSpareBudgetId, Guid foreignSpareBudgetId)
  {
    var budgetId = column == "budget_id" ? foreignSpareBudgetId : ownSpareBudgetId;
    var engagementId = column == "engagement_id" ? foreign.EngagementId : own.EngagementId;
    var expectedConstraint = $"FK_{child}_{(column == "budget_id" ? "engagement_budgets" : "engagements")}_firm_id_{column}";
    await new NpgsqlCommand("SAVEPOINT budget_receipt_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    var insert = new NpgsqlCommand($"""
      INSERT INTO "{child}"
      SELECT (jsonb_populate_record(NULL::"{child}", to_jsonb(c) || jsonb_build_object(
        'budget_id', @budget_id, 'engagement_id', @engagement_id,
        'request_id', gen_random_uuid(), 'id', gen_random_uuid()))).*
      FROM "{child}" c
      WHERE c.firm_id = @firm_id
      LIMIT 1
      """, connection, transaction);
    insert.Parameters.AddWithValue("budget_id", budgetId);
    insert.Parameters.AddWithValue("engagement_id", engagementId);
    insert.Parameters.AddWithValue("firm_id", own.FirmId);
    PostgresException? observed = null;
    try { await insert.ExecuteNonQueryAsync(); }
    catch (PostgresException exception) { observed = exception; }
    await new NpgsqlCommand("ROLLBACK TO SAVEPOINT budget_receipt_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    await new NpgsqlCommand("RELEASE SAVEPOINT budget_receipt_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    Assert.True(observed is not null, $"{child}.{column} unexpectedly accepted a cross-firm reference.");
    Assert.Equal("23503", observed!.SqlState);
    Assert.Equal(expectedConstraint, observed.ConstraintName);
  }

  private static async Task<Guid> SeedBudgetChildRowsAsync(PgTestSchema pg, PbcSeed.Fixture fixture)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    await BudgetPreparationReviewSeed.PopulateAsync(db, fixture);

    var preparer = PbcSeed.Actor(fixture.Staff, "Manager");
    var preparation = new BudgetPreparationRequest(Guid.NewGuid(), new("QAR", "0",
      [new("Senior", "AUDIT", 480, "PLANNING", "Revenue")]));
    var preparationPreview = await BudgetPreparationWorkspace.PreviewAsync(db, preparer, fixture.EngagementId, preparation);
    Assert.True(preparationPreview.Succeeded, preparationPreview.Message);
    preparation = preparation with
    {
      Fields = preparationPreview.Value!.Fields,
      Reviewed = true,
      ReviewBasis = preparationPreview.Value.ReviewBasis,
      RequestHash = preparationPreview.Value.RequestHash
    };
    var prepared = await BudgetPreparationWorkspace.ExecuteAsync(db, preparer, fixture.EngagementId, preparation);
    Assert.True(prepared.Succeeded, prepared.Message);

    var approver = PbcSeed.Actor(fixture.Admin, "Administrator");
    var approval = new BudgetApprovalRequest(Guid.NewGuid(), new(prepared.Value!.BudgetId, "1"));
    var approvalPreview = await BudgetApprovalWorkspace.PreviewAsync(db, approver, fixture.EngagementId, approval);
    Assert.True(approvalPreview.Succeeded, approvalPreview.Message);
    approval = approval with
    {
      Reviewed = true,
      ReviewBasis = approvalPreview.Value!.ReviewBasis,
      RequestHash = approvalPreview.Value.RequestHash
    };
    var approved = await BudgetApprovalWorkspace.ExecuteAsync(db, approver, fixture.EngagementId, approval);
    Assert.True(approved.Succeeded, approved.Message);

    // A second valid parent keeps the foreign-key probe away from immutable-receipt unique indexes.
    var spare = await PracticeTimeService.ReviseBudgetAsync(db, preparer,
      new(fixture.EngagementId, "QAR", [new("Senior", "AUDIT", 480, "PLANNING", "Revenue")], 1));
    Assert.True(spare.Succeeded, spare.Message);
    return spare.Value;
  }

  [Fact]
  public async Task StaffCostRates_RejectForeignFirmUser()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();

    var sameFirmId = Guid.NewGuid();
    var crossFirmId = Guid.NewGuid();
    string crossFirmOutcome;
    await using (var transaction = await connection.BeginTransactionAsync())
    {
      async Task<int> InsertRateAsync(Guid id, Guid firmId, Guid userId, Guid recordedByUserId)
      {
        await using var command = new NpgsqlCommand("""
          INSERT INTO staff_cost_rates
            (id, firm_id, user_id, hourly_cost, currency, effective_from, recorded_by_user_id, recorded_at)
          VALUES
            (@id, @firm_id, @user_id, @hourly_cost, @currency, @effective_from, @recorded_by_user_id, @recorded_at)
          """, connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("firm_id", firmId);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("hourly_cost", 200m);
        command.Parameters.AddWithValue("currency", "QAR");
        command.Parameters.AddWithValue("effective_from", new DateOnly(2026, 1, 1));
        command.Parameters.AddWithValue("recorded_by_user_id", recordedByUserId);
        command.Parameters.AddWithValue("recorded_at", DateTimeOffset.UtcNow);
        return await command.ExecuteNonQueryAsync();
      }

      var sameFirmRows = await InsertRateAsync(sameFirmId, own.FirmId, own.Staff.Id, own.Admin.Id);
      output.WriteLine($"SAME_FIRM_CONTROL: inserted={sameFirmRows}");
      Assert.Equal(1, sameFirmRows);

      try
      {
        var rows = await InsertRateAsync(crossFirmId, own.FirmId, foreign.Staff.Id, own.Admin.Id);
        crossFirmOutcome = $"ACCEPTED rows={rows}";
      }
      catch (PostgresException exception)
      {
        crossFirmOutcome = $"REFUSED SqlState={exception.SqlState} Constraint={exception.ConstraintName ?? "(none)"}";
      }
      output.WriteLine($"CROSS_FIRM_USER: {crossFirmOutcome}");
      await transaction.RollbackAsync();
    }

    await using var rollbackCheck = new NpgsqlCommand(
      "SELECT count(*) FROM staff_cost_rates WHERE id IN (@same_firm_id, @cross_firm_id)", connection);
    rollbackCheck.Parameters.AddWithValue("same_firm_id", sameFirmId);
    rollbackCheck.Parameters.AddWithValue("cross_firm_id", crossFirmId);
    Assert.Equal(0L, await rollbackCheck.ExecuteScalarAsync());
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_staff_cost_rate_user_scope", crossFirmOutcome);
  }

  [Fact]
  public async Task FirmExpenses_RejectForeignFirmReferences()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var accounts = new[]
    {
      new FirmAccount { Id = Guid.NewGuid(), FirmId = own.FirmId, Code = "6100", Name = "Own expense", AccountType = LedgerStates.AccountExpense, NormalSide = LedgerStates.Debit },
      new FirmAccount { Id = Guid.NewGuid(), FirmId = own.FirmId, Code = "1000", Name = "Own bank", AccountType = LedgerStates.AccountAsset, NormalSide = LedgerStates.Debit },
      new FirmAccount { Id = Guid.NewGuid(), FirmId = foreign.FirmId, Code = "6100", Name = "Foreign expense", AccountType = LedgerStates.AccountExpense, NormalSide = LedgerStates.Debit },
      new FirmAccount { Id = Guid.NewGuid(), FirmId = foreign.FirmId, Code = "1000", Name = "Foreign bank", AccountType = LedgerStates.AccountAsset, NormalSide = LedgerStates.Debit }
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.FirmAccounts.AddRange(accounts);
      foreach (var fixture in new[] { own, foreign })
      {
        db.RoleGrants.AddRange(
          PbcSeed.Grant(fixture.FirmId, fixture.Staff, "FinanceManager"),
          PbcSeed.Grant(fixture.FirmId, fixture.Reviewer, "FinanceReviewer"));
        db.FirmFinanceProfiles.Add(new FirmFinanceProfile
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR",
          ProfileKind = BillingStates.TestProfile, Approved = true,
          ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
          CreatedAt = DateTimeOffset.UtcNow
        });
      }
      await db.SaveChangesAsync();
    }

    async Task<(Guid JournalId, Guid PostingId)> CreatePostedJournalAsync(
      PbcSeed.Fixture fixture, Guid expenseAccountId, Guid bankAccountId, string journalNumber, string sourceKey)
    {
      var finance = PbcSeed.Actor(fixture.Staff, "FinanceManager");
      var reviewer = PbcSeed.Actor(fixture.Reviewer, "FinanceReviewer");
      await using var db = new AuditSphereDbContext(pg.Options);
      var period = await LedgerService.CreateFirmPeriodAsync(db, finance, new CreateFirmPeriodRequest("2026-10"));
      Assert.True(period.Succeeded, period.Message);
      var journal = await LedgerService.CreateFirmJournalDraftAsync(db, finance,
        new CreateFirmJournalDraftRequest(period.Value, journalNumber, "MANUAL", sourceKey, 1, "MANUAL", "QAR",
          [new FirmJournalLineRequest(expenseAccountId, "Synthetic scope debit", 1m, 0m),
           new FirmJournalLineRequest(bankAccountId, "Synthetic scope credit", 0m, 1m)]));
      Assert.True(journal.Succeeded, journal.Message);
      var submitted = await LedgerService.SubmitFirmJournalAsync(db, finance, journal.Value);
      Assert.True(submitted.Succeeded, submitted.Message);
      var approved = await LedgerService.ApproveFirmJournalAsync(db, reviewer, journal.Value);
      Assert.True(approved.Succeeded, approved.Message);
      var posting = await LedgerService.PostFirmJournalAsync(db, finance, journal.Value);
      Assert.True(posting.Succeeded, posting.Message);
      return (journal.Value, posting.Value);
    }

    var ownLedger = await CreatePostedJournalAsync(own, accounts[0].Id, accounts[1].Id, "J-SCOPE", "SCOPE-OWN");
    var foreignLedger = await CreatePostedJournalAsync(foreign, accounts[2].Id, accounts[3].Id, "J-SCOPE", "SCOPE-FOREIGN");

    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var evidence = "synthetic expense receipt"u8.ToArray();
    var evidenceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(evidence)).ToLowerInvariant();
    var expenseIds = new List<Guid>();

    async Task<string> InsertExpenseAsync(
      Guid expenseAccountId, Guid paymentAccountId, Guid preparerId, Guid reviewerId,
      Guid? journalId = null, Guid? postingId = null, string status = "APPROVED")
    {
      var expenseId = Guid.NewGuid();
      expenseIds.Add(expenseId);
      await new NpgsqlCommand("SAVEPOINT expense_probe", connection, transaction).ExecuteNonQueryAsync();
      string outcome;
      try
      {
        await using var command = new NpgsqlCommand("""
          INSERT INTO firm_expenses
            (id, firm_id, expense_date, category, payee, description, amount, currency,
             expense_account_id, payment_account_id, journal_id, posting_id,
             evidence_file_name, evidence_content_type, evidence_content, evidence_sha256,
             status, prepared_by_user_id, reviewed_by_user_id, reviewed_at, created_at)
          VALUES
            (@id, @firm_id, @expense_date, 'OTHER', 'Synthetic payee', 'Scope probe', 1.00, 'QAR',
             @expense_account_id, @payment_account_id, @journal_id, @posting_id,
             'receipt.pdf', 'application/pdf', @evidence_content, @evidence_sha256, @status, @prepared_by_user_id,
             @reviewed_by_user_id, @reviewed_at, @created_at)
          """, connection, transaction);
        command.Parameters.AddWithValue("id", expenseId);
        command.Parameters.AddWithValue("firm_id", own.FirmId);
        command.Parameters.AddWithValue("expense_date", new DateOnly(2026, 10, 23));
        command.Parameters.AddWithValue("expense_account_id", expenseAccountId);
        command.Parameters.AddWithValue("payment_account_id", paymentAccountId);
        command.Parameters.AddWithValue("journal_id", (object?)journalId ?? DBNull.Value);
        command.Parameters.AddWithValue("posting_id", (object?)postingId ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence_content", evidence);
        command.Parameters.AddWithValue("evidence_sha256", evidenceHash);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.AddWithValue("prepared_by_user_id", preparerId);
        command.Parameters.AddWithValue("reviewed_by_user_id", reviewerId);
        command.Parameters.AddWithValue("reviewed_at", DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("created_at", DateTimeOffset.UtcNow);
        var rows = await command.ExecuteNonQueryAsync();
        outcome = $"ACCEPTED rows={rows}";
      }
      catch (PostgresException exception)
      {
        outcome = $"REFUSED SqlState={exception.SqlState} Constraint={exception.ConstraintName ?? "(none)"}";
      }
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT expense_probe", connection, transaction).ExecuteNonQueryAsync();
      await new NpgsqlCommand("RELEASE SAVEPOINT expense_probe", connection, transaction).ExecuteNonQueryAsync();
      return outcome;
    }

    var sameFirmNullableLinksControl = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id);
    var sameFirmJournalControl = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id, ownLedger.JournalId);
    var sameFirmPostingControl = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id,
      ownLedger.JournalId, ownLedger.PostingId, "POSTED");
    var foreignJournal = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id, foreignLedger.JournalId);
    var foreignPosting = await InsertExpenseAsync(
      accounts[0].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id,
      postingId: foreignLedger.PostingId, status: "POSTED");
    var foreignExpenseAccount = await InsertExpenseAsync(accounts[2].Id, accounts[1].Id, own.Admin.Id, own.Staff.Id);
    var foreignPaymentAccount = await InsertExpenseAsync(accounts[0].Id, accounts[3].Id, own.Admin.Id, own.Staff.Id);
    var foreignPreparer = await InsertExpenseAsync(accounts[0].Id, accounts[1].Id, foreign.Admin.Id, own.Staff.Id);
    var foreignReviewer = await InsertExpenseAsync(accounts[0].Id, accounts[1].Id, own.Admin.Id, foreign.Admin.Id);
    output.WriteLine($"SAME_FIRM_NULLABLE_LINKS: {sameFirmNullableLinksControl}");
    output.WriteLine($"SAME_FIRM_JOURNAL: {sameFirmJournalControl}");
    output.WriteLine($"SAME_FIRM_POSTING: {sameFirmPostingControl}");
    output.WriteLine($"FOREIGN_JOURNAL: {foreignJournal}");
    output.WriteLine($"FOREIGN_POSTING: {foreignPosting}");
    output.WriteLine($"FOREIGN_EXPENSE_ACCOUNT: {foreignExpenseAccount}");
    output.WriteLine($"FOREIGN_PAYMENT_ACCOUNT: {foreignPaymentAccount}");
    output.WriteLine($"FOREIGN_PREPARER: {foreignPreparer}");
    output.WriteLine($"FOREIGN_REVIEWER: {foreignReviewer}");
    await transaction.RollbackAsync();

    await using var rollbackCheck = new NpgsqlCommand(
      "SELECT count(*) FROM firm_expenses WHERE id = ANY(@expense_ids)", connection);
    rollbackCheck.Parameters.AddWithValue("expense_ids", expenseIds.ToArray());
    Assert.Equal(0L, await rollbackCheck.ExecuteScalarAsync());
    Assert.Equal("ACCEPTED rows=1", sameFirmNullableLinksControl);
    Assert.Equal("ACCEPTED rows=1", sameFirmJournalControl);
    Assert.Equal("ACCEPTED rows=1", sameFirmPostingControl);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_journal_scope", foreignJournal);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_posting_scope", foreignPosting);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_expense_account_scope", foreignExpenseAccount);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_payment_account_scope", foreignPaymentAccount);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_prepared_by_user_scope", foreignPreparer);
    Assert.Equal("REFUSED SqlState=23503 Constraint=fk_firm_expense_reviewed_by_user_scope", foreignReviewer);
  }

  [Theory]
  [InlineData("ENGAGEMENT")]
  [InlineData("USER")]
  [InlineData("ROLE_GRANT")]
  [InlineData("CLIENT")]
  [InlineData("OTHER_USER_GRANT")]
  public async Task StaffingReferences_RefuseForeignFirmParents(string parent)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var staffGrant = await db.RoleGrants.SingleAsync(x => x.FirmId == own.FirmId &&
      x.UserId == own.Staff.Id && x.Role == "Staff");
    var ownAdminGrant = PbcSeed.Grant(own.FirmId, own.Admin, "Staff", own.ClientId, own.EngagementId);
    var foreignAdminGrant = PbcSeed.Grant(foreign.FirmId, foreign.Admin, "Staff", foreign.ClientId, foreign.EngagementId);
    db.RoleGrants.AddRange(ownAdminGrant, foreignAdminGrant);
    db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId, ClientId = own.ClientId,
      EngagementId = own.EngagementId, UserId = own.Staff.Id, RoleGrantId = staffGrant.Id,
      StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = own.Admin.Id,
      AssignedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(); // All-owned control is valid; candidate uses a different local user.

    var candidate = new EngagementStaffAssignment
    {
      Id = Guid.NewGuid(), FirmId = own.FirmId,
      ClientId = parent == "CLIENT" ? foreign.ClientId : own.ClientId,
      EngagementId = parent == "ENGAGEMENT" ? foreign.EngagementId : own.EngagementId,
      UserId = parent == "USER" ? foreign.Admin.Id : own.Admin.Id,
      RoleGrantId = parent == "ROLE_GRANT" ? foreignAdminGrant.Id :
        parent == "OTHER_USER_GRANT" ? staffGrant.Id : ownAdminGrant.Id,
      StaffingLevel = StaffingLevels.StaffAssociate, AssignedByUserId = own.Admin.Id,
      AssignedAt = DateTimeOffset.UtcNow
    };
    db.EngagementStaffAssignments.Add(candidate);
    var failure = await Record.ExceptionAsync(() => db.SaveChangesAsync());
    if (failure is null) output.WriteLine($"ACCEPTED_FOREIGN_STAFFING_PARENT:{parent}");
    var rejected = Assert.IsType<DbUpdateException>(failure);
    var postgres = Assert.IsType<PostgresException>(rejected.InnerException);
    Assert.Equal("23503", postgres.SqlState);
    var expectedConstraints = parent switch
    {
      "ENGAGEMENT" or "CLIENT" => new[] { "fk_staff_assignment_engagement_scope" },
      "USER" => new[] { "fk_staff_assignment_user_scope", "fk_staff_assignment_grant_scope" },
      _ => new[] { "fk_staff_assignment_grant_scope" }
    };
    Assert.Contains(postgres.ConstraintName, expectedConstraints);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.False(await proof.EngagementStaffAssignments.AnyAsync(x => x.Id == candidate.Id));
    Assert.Equal(1, await proof.EngagementStaffAssignments.CountAsync(x => x.FirmId == own.FirmId));
  }

  [Theory]
  [InlineData("PROFILE", "staff_profiles", "fk_staff_profile_user_scope")]
  [InlineData("CERTIFICATION", "staff_certifications", "fk_staff_certification_user_scope")]
  [InlineData("AVAILABILITY", "staff_availabilities", "fk_staff_availability_user_scope")]
  [InlineData("ALLOCATION_USER", "staff_allocations", "fk_staff_allocation_user_scope")]
  [InlineData("ALLOCATION_ENGAGEMENT", "staff_allocations", "fk_staff_allocation_engagement_scope")]
  [InlineData("ALLOCATION_CLIENT", "staff_allocations", "fk_staff_allocation_engagement_scope")]
  public async Task ResourceRecords_RefuseMismatchedScopedParents(string kind, string table, string constraint)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var sunday = new DateOnly(2026, 10, 25);
    object Row(Guid id, bool invalid)
    {
      var user = invalid && kind is not ("ALLOCATION_ENGAGEMENT" or "ALLOCATION_CLIENT")
        ? foreign.Admin.Id : invalid ? own.Admin.Id : own.Staff.Id;
      return kind switch
      {
        "PROFILE" => new StaffProfile { Id = id, FirmId = own.FirmId, UserId = user,
          Department = "AUDIT", WeeklyCapacityMinutes = 2400, TargetUtilizationPercent = 75,
          UpdatedAt = now, UpdatedByUserId = own.Admin.Id },
        "CERTIFICATION" => new StaffCertification { Id = id, FirmId = own.FirmId, UserId = user,
          Name = "SYNTHETIC CPA", RecordedAt = now, RecordedByUserId = own.Admin.Id },
        "AVAILABILITY" => new StaffAvailability { Id = id, FirmId = own.FirmId, UserId = user,
          StartDate = sunday, EndDate = sunday, MinutesPerDay = 480,
          RecordedAt = now, RecordedByUserId = own.Admin.Id },
        _ => new StaffAllocation { Id = id, FirmId = own.FirmId, UserId = user,
          ClientId = invalid && kind == "ALLOCATION_CLIENT" ? foreign.ClientId : own.ClientId,
          EngagementId = invalid && kind == "ALLOCATION_ENGAGEMENT" ? foreign.EngagementId : own.EngagementId,
          WeekStart = sunday, PlannedMinutes = 480, UpdatedAt = now, UpdatedByUserId = own.Admin.Id }
      };
    }

    var validId = Guid.NewGuid();
    var invalidId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Add(Row(validId, false));
    await db.SaveChangesAsync(); // Distinct valid subject avoids a unique-index false refusal.
    db.Add(Row(invalidId, true));
    var failure = await Record.ExceptionAsync(() => db.SaveChangesAsync());
    if (failure is null) output.WriteLine($"ACCEPTED_FOREIGN_RESOURCE_PARENT:{kind}");
    var rejected = Assert.IsType<DbUpdateException>(failure);
    var postgres = Assert.IsType<PostgresException>(rejected.InnerException);
    Assert.Equal("23503", postgres.SqlState);
    Assert.Equal(constraint, postgres.ConstraintName);

    // Table identifiers are fixed InlineData above, never caller input.
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table} WHERE id = @id", connection);
    count.Parameters.AddWithValue("id", invalidId);
    Assert.Equal(0L, await count.ExecuteScalarAsync());
    count.Parameters["id"].Value = validId;
    Assert.Equal(1L, await count.ExecuteScalarAsync());
  }

  private static HashSet<string> ExpectedTriggers()
  {
    var root = AppContext.BaseDirectory;
    for (var dir = new DirectoryInfo(root); dir is not null; dir = dir.Parent)
      if (File.Exists(Path.Combine(dir.FullName, "AuditSphereOps.slnx"))) { root = dir.FullName; break; }
    var migrations = Directory.GetFiles(Path.Combine(root, "src", "AuditSphereOps.Infrastructure", "Persistence", "Migrations"), "*.cs")
      .Where(file => !file.EndsWith(".Designer.cs") && Regex.IsMatch(Path.GetFileName(file), @"^\d{14}_")).Order(StringComparer.Ordinal);
    var live = new HashSet<string>(StringComparer.Ordinal);
    foreach (var file in migrations)
    {
      // Only the Up direction defines the schema the application runs on.
      var text = File.ReadAllText(file);
      var down = text.IndexOf("void Down(", StringComparison.Ordinal);
      foreach (Match operation in TriggerOperation().Matches(down < 0 ? text : text[..down]))
        if (operation.Groups[1].Value.StartsWith("DROP", StringComparison.OrdinalIgnoreCase)) live.Remove(operation.Groups[2].Value);
        else live.Add(operation.Groups[2].Value);
    }
    return live;
  }

  [GeneratedRegex(@"\b(CREATE\s+(?:OR\s+REPLACE\s+)?(?:CONSTRAINT\s+)?TRIGGER|DROP\s+TRIGGER(?:\s+IF\s+EXISTS)?)\s+""?(\w+)""?", RegexOptions.IgnoreCase)]
  private static partial Regex TriggerOperation();
}
