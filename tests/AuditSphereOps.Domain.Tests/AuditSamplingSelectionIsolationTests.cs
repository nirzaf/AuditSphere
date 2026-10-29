using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class AuditSamplingSelectionIsolationTests
{
  private static async Task<CommandResult<SamplingRunView>> RunReviewedSamplingAsync(
    IAuditSphereDbContext db, ActorContext actor, RunSamplingRequest request)
  {
    var preview = await AuditFieldworkService.PreviewSamplingAsync(db, actor, request);
    if (!preview.Succeeded) return CommandResult<SamplingRunView>.Fail(preview.ErrorCode!, preview.Message!);
    return await AuditFieldworkService.RunSamplingAsync(db, actor, request with { ExpectedPreviewDigest = preview.Value!.PreviewDigest });
  }

  [Fact(DisplayName = "Client-scoped fieldwork cannot reach sibling schedules, selections, tests or sampling runs")]
  public async Task SiblingIdentifiers_AreDeniedAcrossSchedulesSelectionsAndSampling()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var clientA = fixture.Primary;
    var clientB = fixture.Other;
    var now = DateTimeOffset.UtcNow;
    var preparerId = Guid.NewGuid();
    var reviewerId = Guid.NewGuid();
    var preparer = new ActorContext(preparerId, clientB.FirmId, 1, ["Partner"]);
    var reviewer = new ActorContext(reviewerId, clientB.FirmId, 1, ["Partner"]);
    var procedureId = Guid.NewGuid();
    var samplingProcedureId = Guid.NewGuid();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var engagement = await db.Engagements.SingleAsync(x => x.Id == clientB.EngagementId);
      engagement.ProfessionalWorkBlocked = false;
      db.Users.AddRange(NewUser(clientB.FirmId, preparerId, "schedule-preparer"), NewUser(clientB.FirmId, reviewerId, "schedule-reviewer"));
      db.RoleGrants.AddRange(Grant(clientB, preparerId), Grant(clientB, reviewerId));
      db.AuditProcedures.AddRange(
        NewProcedure(clientB, procedureId, "BANK-01", "Agree bank statement"),
        NewProcedure(clientB, samplingProcedureId, "BANK-02", "Sample bank transactions"));
      await db.SaveChangesAsync();
      await PlanningBasisSeed.EstablishAsync(db, clientB.FirmId, clientB.ClientId, clientB.EngagementId, preparer, reviewer);
    }

    Guid scheduleId;
    Guid selectionId;
    Guid selectionItemId;
    Guid itemTestId;
    Guid samplingRunId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, preparer, NewSchedule(clientB.EngagementId, "client-b-receipt"));
      Assert.True(schedule.Succeeded, schedule.Message);
      scheduleId = schedule.Value!.ScheduleId;
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(scheduleId, "Source rows and control total reviewed.", true))).Succeeded);

      var selection = await AuditFieldworkService.CreateSelectionAsync(db, preparer, new CreateSelectionRequest(
        clientB.EngagementId, procedureId, scheduleId, null, "ALL", "Select the complete bank row.",
        [new SelectionItemInput("bank-row-1", 100m, "QAR", "Complete population") ]));
      Assert.True(selection.Succeeded, selection.Message);
      selectionId = selection.Value!.SelectionId;
      Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
        new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.Reviewed, null))).Succeeded);
      selectionItemId = await db.AuditSelectionItems.Where(x => x.SelectionId == selectionId).Select(x => x.Id).SingleAsync();

      var itemTest = await AuditFieldworkService.RecordItemTestAsync(db, preparer,
        new RecordItemTestRequest(selectionItemId, "Agreed to bank evidence.", ["receipt:client-b"], AuditItemTestResults.Pass, null, null, null));
      Assert.True(itemTest.Succeeded, itemTest.Message);
      itemTestId = itemTest.Value!.AuditItemTestId;
      Assert.True((await AuditFieldworkService.ReviewItemTestAsync(db, reviewer,
        new ReviewItemTestRequest(itemTestId, AuditItemTestReviewDecisions.Reviewed, "Evidence agrees."))).Succeeded);

      var run = await RunReviewedSamplingAsync(db, preparer,
        new RunSamplingRequest(clientB.EngagementId, samplingProcedureId, scheduleId, "RANDOM", null, null, 1, 73, "Select one row."));
      Assert.True(run.Succeeded, run.Message);
      samplingRunId = run.Value!.Run.Id;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.CreateScheduleAsync(db, clientA.Actor, NewSchedule(clientB.EngagementId, "unauthorized"))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.ReviewScheduleAsync(db, clientA.Actor,
          new ReviewScheduleRequest(scheduleId, "Unauthorized approval.", false))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.CreateSelectionAsync(db, clientA.Actor, new CreateSelectionRequest(
          clientB.EngagementId, procedureId, scheduleId, null, "ALL", "Unauthorized sibling selection.",
          [new SelectionItemInput("bank-row-1", 100m, "QAR", "Unauthorized")]))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.ReviewSelectionAsync(db, clientA.Actor,
          new ReviewSelectionRequest(selectionId, AuditSelectionStatuses.ChangesRequired, "Unauthorized review."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.RecordItemTestAsync(db, clientA.Actor,
          new RecordItemTestRequest(selectionItemId, "Unauthorized work.", ["guessed"], AuditItemTestResults.Exception, 100m, null, null))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.ReviewItemTestAsync(db, clientA.Actor,
          new ReviewItemTestRequest(itemTestId, AuditItemTestReviewDecisions.ChangesRequired, "Unauthorized review."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await AuditFieldworkService.RunSamplingAsync(db, clientA.Actor,
          new RunSamplingRequest(clientB.EngagementId, samplingProcedureId, scheduleId, "RANDOM", null, null, 1, 73, "Unauthorized sample."))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditFieldworkService.GetSamplingRunAsync(db, clientA.Actor, samplingRunId)).ErrorCode);
      Assert.Empty(await AuditFieldworkService.ListSamplingRunsAsync(db, clientA.Actor, clientB.EngagementId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var schedule = await db.AuditSchedules.AsNoTracking().SingleAsync(x => x.Id == scheduleId);
      var selection = await db.AuditSelections.AsNoTracking().SingleAsync(x => x.Id == selectionId);
      var itemTest = await db.AuditItemTests.AsNoTracking().SingleAsync(x => x.Id == itemTestId);
      Assert.Equal(clientB.ClientId, schedule.ClientId);
      Assert.Equal(AuditScheduleStatuses.Approved, schedule.Status);
      Assert.Equal(clientB.ClientId, selection.ClientId);
      Assert.Equal(AuditSelectionStatuses.Reviewed, selection.Status);
      Assert.Equal(AuditItemTestResults.Pass, itemTest.Result);
      Assert.Equal(preparerId, itemTest.TestedByUserId);
      Assert.Equal(1, await db.AuditItemTestReviews.CountAsync(x => x.AuditItemTestId == itemTestId));
      Assert.Equal(1, await db.AuditSamplingRuns.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(2, await db.AuditSelections.CountAsync(x => x.EngagementId == clientB.EngagementId));
      Assert.Equal(1, await db.AuditSchedules.CountAsync(x => x.EngagementId == clientB.EngagementId));
    }
  }

  [Fact]
  public async Task SamplingRuns_RejectForeignSelectionsAndPreserveExactSamplingEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var own = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    var foreign = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    await using var db = new AuditSphereDbContext(pg.Options);
    async Task<(ActorContext Preparer, Guid Procedure, Guid Schedule, SamplingRunView Run)> SeedRunAsync(PlanningScope scope)
    {
      var preparerId = Guid.NewGuid();
      var reviewerId = Guid.NewGuid();
      var preparer = new ActorContext(preparerId, scope.FirmId, 1, ["Partner"]);
      var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Partner"]);
      var procedure = Guid.NewGuid();
      db.Users.AddRange(NewUser(scope.FirmId, preparerId, "sampling-preparer"), NewUser(scope.FirmId, reviewerId, "sampling-reviewer"));
      db.RoleGrants.AddRange(Grant(scope, preparerId), Grant(scope, reviewerId));
      db.AuditProcedures.Add(NewProcedure(scope, procedure, "BANK-02", "Sample one synthetic bank row"));
      await db.SaveChangesAsync();
      await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, preparer, reviewer);
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, preparer,
        NewSchedule(scope.EngagementId, "sampling-scope-" + scope.ClientId.ToString("N")));
      Assert.True(schedule.Succeeded, schedule.Message);
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(schedule.Value!.ScheduleId, "Independent source and control-total review.", true))).Succeeded);
      var run = await RunReviewedSamplingAsync(db, preparer,
        new RunSamplingRequest(scope.EngagementId, procedure, schedule.Value.ScheduleId, "RANDOM", null, null, 1, 73, "One-row scope control."));
      Assert.True(run.Succeeded, run.Message);
      var read = await AuditFieldworkService.GetSamplingRunAsync(db, preparer, run.Value!.Run.Id);
      Assert.True(read.Succeeded, read.Message);
      Assert.True(read.Value!.Reproduces);
      Assert.Equal("RANDOM", read.Value.Run.Method);
      Assert.Equal(73, read.Value.Run.Seed);
      Assert.Equal(1, read.Value.Run.PopulationCount);
      Assert.Equal(1, read.Value.Run.SelectedCount);
      Assert.Equal(100m, read.Value.Run.PopulationAbsoluteTotal);
      Assert.Equal(100m, read.Value.Run.SelectedAbsoluteTotal);
      Assert.Equal(100m, read.Value.Run.CoveragePercent);
      Assert.Equal(100m, Assert.Single(read.Value.Items).SignedAmount);
      Assert.Equal("QAR", Assert.Single(read.Value.Items).Currency);
      Assert.Equal(64, read.Value.Run.SourceDigest.Length);
      Assert.Equal(64, read.Value.Run.SelectionDigest.Length);
      Assert.Equal(64, read.Value.Run.PreviewDigest!.Length);
      return (preparer, procedure, schedule.Value.ScheduleId, read.Value);
    }
    var native = await SeedRunAsync(own);
    var foreignNative = await SeedRunAsync(foreign);
    Assert.NotEqual(own.FirmId, foreign.FirmId);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await AuditFieldworkService.GetSamplingRunAsync(db, native.Preparer, foreignNative.Run.Run.Id)).ErrorCode);
    var foreignRequest = new RunSamplingRequest(foreign.EngagementId, foreignNative.Procedure, foreignNative.Schedule,
      "RANDOM", null, null, 1, 73, "Denied foreign source.", ExpectedPreviewDigest: foreignNative.Run.Run.PreviewDigest);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await AuditFieldworkService.PreviewSamplingAsync(db, native.Preparer, foreignRequest)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await AuditFieldworkService.RunSamplingAsync(db, native.Preparer, foreignRequest)).ErrorCode);

    // A separate native selection avoids the retained run's unique firm/selection pair.
    var controlProcedure = Guid.NewGuid();
    db.AuditProcedures.Add(NewProcedure(own, controlProcedure, "BANK-03", "Separate selection for relational control"));
    await db.SaveChangesAsync();
    var controlSelection = await AuditFieldworkService.CreateSelectionAsync(db, native.Preparer,
      new CreateSelectionRequest(own.EngagementId, controlProcedure, native.Schedule, null, "RANDOM", "One-row relational control.",
        [new SelectionItemInput("bank-row-1", 100m, "QAR", "Complete population")]));
    Assert.True(controlSelection.Succeeded, controlSelection.Message);
    async Task<string> ReadEvidenceAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT jsonb_build_object(
        'schedules', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_schedules x),
        'rows', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_schedule_rows x),
        'sources', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.schedule_id) FROM audit_schedule_source_captures x),
        'selections', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_selections x),
        'items', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_selection_items x),
        'runs', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_sampling_runs x))::text AS "Value"
      """).SingleAsync();
    var before = await ReadEvidenceAsync();
    await using var connection = new NpgsqlConnection(pg.ConnectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    async Task<int> InsertAsync(Guid firmId, Guid selectionId, Guid clientId, Guid engagementId)
    {
      await using var command = new NpgsqlCommand("""
        INSERT INTO audit_sampling_runs
        SELECT (jsonb_populate_record(NULL::audit_sampling_runs, to_jsonb(r) || jsonb_build_object(
          'id', @id, 'firm_id', @firm, 'selection_id', @selection, 'procedure_id', @procedure,
          'client_id', @client, 'engagement_id', @engagement, 'preview_digest', NULL))).*
        FROM audit_sampling_runs r WHERE r.id = @source
        """, connection, transaction);
      command.Parameters.AddWithValue("id", Guid.NewGuid());
      command.Parameters.AddWithValue("firm", firmId);
      command.Parameters.AddWithValue("selection", selectionId);
      command.Parameters.AddWithValue("procedure", controlProcedure);
      command.Parameters.AddWithValue("client", clientId);
      command.Parameters.AddWithValue("engagement", engagementId);
      command.Parameters.AddWithValue("source", native.Run.Run.Id);
      return await command.ExecuteNonQueryAsync();
    }
    await new NpgsqlCommand("SAVEPOINT sampling_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    // NULL preview is the existing legacy-compatible shape, not a fabricated current preview.
    // No constraint, index, amount or retained preview is altered by this rollback-only control.
    Assert.Equal(1, await InsertAsync(own.FirmId, controlSelection.Value!.SelectionId, own.ClientId, own.EngagementId));
    await new NpgsqlCommand("ROLLBACK TO SAVEPOINT sampling_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    var accepted = new List<string>();
    foreach (var (label, firm, selection, client, engagement) in new[]
    {
      ("foreign selection", own.FirmId, foreignNative.Run.Run.SelectionId, own.ClientId, own.EngagementId),
      ("wrong client", own.FirmId, controlSelection.Value.SelectionId, foreign.ClientId, own.EngagementId),
      ("wrong engagement", own.FirmId, controlSelection.Value.SelectionId, own.ClientId, foreign.EngagementId),
      ("wrong firm", foreign.FirmId, controlSelection.Value.SelectionId, own.ClientId, own.EngagementId)
    })
    {
      try
      {
        Assert.Equal(1, await InsertAsync(firm, selection, client, engagement));
        accepted.Add(label);
      }
      catch (PostgresException exception)
      {
        Assert.Equal("23503", exception.SqlState);
        Assert.Equal("fk_audit_sampling_run_selection_scope", exception.ConstraintName);
      }
      await new NpgsqlCommand("ROLLBACK TO SAVEPOINT sampling_scope_probe", connection, transaction).ExecuteNonQueryAsync();
    }
    await transaction.RollbackAsync();
    Assert.Equal(before, await ReadEvidenceAsync());
    Assert.True(accepted.Count == 0, "Cross-scope sampling-run references accepted: " + string.Join(", ", accepted));
  }

  [Fact]
  public async Task SamplingSelectionScopeUpgrade_PreservesRunsAndRefusesInconsistentHistory()
  {
    const string baseline = "20261029072350_AdjustmentSourceDatasetScope";
    await using var pg = await PgTestSchema.CreateAsync(baseline);
    var own = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    var foreign = (await PlanningSeed.CreateAsync(pg, role: "Partner")).Primary;
    await using var db = new AuditSphereDbContext(pg.Options);
    async Task<AuditSamplingRun> SeedRunAsync(PlanningScope scope)
    {
      var reviewerId = Guid.NewGuid();
      var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Partner"]);
      var procedure = Guid.NewGuid();
      db.Users.Add(NewUser(scope.FirmId, reviewerId, "sampling-upgrade-reviewer"));
      db.RoleGrants.Add(Grant(scope, reviewerId));
      db.AuditProcedures.Add(NewProcedure(scope, procedure, "BANK-02", "Retained one-row sampling source"));
      await db.SaveChangesAsync();
      await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor, reviewer);
      var schedule = await AuditFieldworkService.CreateScheduleAsync(db, scope.Actor,
        NewSchedule(scope.EngagementId, "sampling-upgrade-" + scope.ClientId.ToString("N")));
      Assert.True(schedule.Succeeded, schedule.Message);
      Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
        new ReviewScheduleRequest(schedule.Value!.ScheduleId, "Independent retained-source review.", true))).Succeeded);
      var run = await RunReviewedSamplingAsync(db, scope.Actor,
        new RunSamplingRequest(scope.EngagementId, procedure, schedule.Value.ScheduleId, "RANDOM", null, null, 1, 73, "Retain exact source and sample."));
      Assert.True(run.Succeeded, run.Message);
      Assert.Equal(100m, run.Value!.Run.SelectedAbsoluteTotal);
      Assert.Equal(100m, Assert.Single(run.Value.Items).SignedAmount);
      return run.Value.Run;
    }
    var native = await SeedRunAsync(own);
    var foreignNative = await SeedRunAsync(foreign);
    async Task<string> ReadRowsAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT jsonb_build_object(
        'schedules', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_schedules x),
        'rows', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_schedule_rows x),
        'sources', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.schedule_id) FROM audit_schedule_source_captures x),
        'selections', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_selections x),
        'items', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_selection_items x),
        'runs', (SELECT jsonb_agg(to_jsonb(x) ORDER BY x.id) FROM audit_sampling_runs x))::text AS "Value"
      """).SingleAsync();
    async Task<List<string>> ReadIndexesAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT indexname || ':' || indexdef AS "Value" FROM pg_indexes WHERE schemaname = current_schema()
        AND tablename IN ('audit_sampling_runs', 'audit_selections') ORDER BY tablename, indexname
      """).ToListAsync();
    async Task<List<string>> ReadForeignKeysAsync() => await db.Database.SqlQueryRaw<string>("""
      SELECT conname || ':' || pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
        WHERE conrelid = 'audit_sampling_runs'::regclass AND contype = 'f' ORDER BY conname
      """).ToListAsync();
    var rows = await ReadRowsAsync();
    var indexes = await ReadIndexesAsync();
    var foreignKeys = await ReadForeignKeysAsync();
    var scoped = db.Database.GetMigrations().Single(x => x.EndsWith("_SamplingRunSelectionScope", StringComparison.Ordinal));
    var migrator = db.GetService<IMigrator>();
    await migrator.MigrateAsync(scoped);
    Assert.Equal(302, (await db.Database.GetAppliedMigrationsAsync()).Count());
    Assert.Equal(rows, await ReadRowsAsync());
    var upgradedIndexes = await ReadIndexesAsync();
    Assert.All(indexes, index => Assert.Contains(index, upgradedIndexes));
    Assert.Contains(await ReadForeignKeysAsync(), value => value.StartsWith("fk_audit_sampling_run_selection_scope:", StringComparison.Ordinal));
    await migrator.MigrateAsync(baseline);
    Assert.Equal(301, (await db.Database.GetAppliedMigrationsAsync()).Count());
    Assert.Equal(rows, await ReadRowsAsync());
    Assert.Equal(indexes, await ReadIndexesAsync());
    Assert.Equal(foreignKeys, await ReadForeignKeysAsync());
    await db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO audit_sampling_runs
      SELECT (jsonb_populate_record(NULL::audit_sampling_runs, to_jsonb(r) || jsonb_build_object(
        'id', {Guid.NewGuid()}, 'selection_id', {foreignNative.SelectionId}, 'preview_digest', NULL))).*
      FROM audit_sampling_runs r WHERE r.id = {native.Id}
      """);
    var inconsistent = await ReadRowsAsync();
    var exception = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(scoped));
    Assert.Equal("23503", exception.SqlState);
    Assert.Equal("fk_audit_sampling_run_selection_scope", exception.ConstraintName);
    Assert.Equal(inconsistent, await ReadRowsAsync());
    Assert.Equal(indexes, await ReadIndexesAsync());
    Assert.Equal(foreignKeys, await ReadForeignKeysAsync());
    Assert.Equal(baseline, (await db.Database.GetAppliedMigrationsAsync()).Last());
    Assert.DoesNotContain(scoped, await db.Database.GetAppliedMigrationsAsync());
  }

  private static AppUser NewUser(Guid firmId, Guid userId, string subject) => new()
  {
    Id = userId,
    FirmId = firmId,
    Subject = subject + "-" + userId.ToString("N"),
    TenantId = "tenant-planning",
    Email = subject + "@example.test",
    DisplayName = subject,
    UserKind = "Staff",
    SessionEpoch = 1,
    CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(PlanningScope scope, Guid userId) => new()
  {
    Id = Guid.NewGuid(),
    FirmId = scope.FirmId,
    UserId = userId,
    Role = "Partner",
    ClientId = scope.ClientId,
    EngagementId = scope.EngagementId,
    GrantedAt = DateTimeOffset.UtcNow,
    GrantedByUserId = userId
  };

  private static AuditProcedure NewProcedure(PlanningScope scope, Guid id, string sourceId, string title) => new()
  {
    Id = id,
    FirmId = scope.FirmId,
    ClientId = scope.ClientId,
    EngagementId = scope.EngagementId,
    SourceProcedureId = sourceId,
    Title = title,
    ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
    Status = AuditProcedureStatuses.Planned,
    CreatedAt = DateTimeOffset.UtcNow
  };

  private static CreateScheduleRequest NewSchedule(Guid engagementId, string receipt) => new(
    engagementId, "BANK_LISTING", "CLIENT-B-BANK", receipt, new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
    new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "debits positive; credits negative",
    Hashing.Sha256Hex("bank-listing-" + receipt), 100m,
    [new ScheduleRowInput("bank-row-1", 1, "1100", "Client B operating account", 100m, "QAR", null,
      new DateOnly(2026, 12, 31), null, null, "{\"source\":\"client-b\"}")]);
}
