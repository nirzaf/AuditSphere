using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditPlanningCommandReceiptTests
{
  public static TheoryData<string> Actions => new() { "CALCULATE_MATERIALITY", "ROUND_MATERIALITY", "APPROVE_MATERIALITY", "CREATE_RISK", "ASSESS_RISK", "ASSIGN_RISK_OWNER", "CLEAR_RISK", "LINK_PROCEDURE_RISK" };
  [Theory, MemberData(nameof(Actions)), Trait("Profile", "Database")]
  public async Task AllActionsPublishOnceRecoverOriginalOutcomeAndDoNotAdvanceInputGeneration(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, action); AuditPlanningCommandRequest request;
    await using (var db = new AuditSphereDbContext(pg.Options)) request = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    await using var writer = new AuditSphereDbContext(pg.Options); await using var contender = new AuditSphereDbContext(pg.Options);
    var before = await writer.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync();
    var results = await Task.WhenAll(AuditPlanningCommandWorkspace.ExecuteAsync(writer, a, f.EngagementId, request), AuditPlanningCommandWorkspace.ExecuteAsync(contender, a, f.EngagementId, request));
    Assert.All(results, x => Assert.True(x.Succeeded, x.Message)); Assert.Equal(results[0].Value!.Id, results[1].Value!.Id);
    await using var db2 = new AuditSphereDbContext(pg.Options);
    var saved = results[0].Value!;
    await db2.Engagements.Where(x => x.Id == f.EngagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfessionalWorkBlocked, true));
    var replay = await AuditPlanningCommandWorkspace.ExecuteAsync(db2, a, f.EngagementId, request); Assert.True(replay.Succeeded, replay.Message); Assert.Equal(saved.Id, replay.Value!.Id);
    var lookup = await AuditPlanningCommandWorkspace.LookupAsync(db2, a, f.EngagementId, request.RequestId, request.ExpectedRequestHash); Assert.True(lookup.Succeeded, lookup.Message); Assert.True(lookup.Value!.Found);
    Assert.Equal(saved.Result, lookup.Value.Receipt!.Result);
    Assert.Single(await db2.AuditPlanningCommandReceipts.ToListAsync());
    Assert.Equal(before, await db2.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync());
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditPlanningCommandWorkspace.ExecuteAsync(db2, a, f.EngagementId, request with { ReviewBasis = new('b',64) })).ErrorCode);
    await Assert.ThrowsAsync<PostgresException>(() => db2.Database.ExecuteSqlInterpolatedAsync($"UPDATE audit_planning_command_receipts SET result_json='{{}}' WHERE id={saved.Id}"));
    await Assert.ThrowsAsync<PostgresException>(() => db2.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM audit_planning_command_receipts WHERE id={saved.Id}"));
  }

  [Fact, Trait("Profile", "Database")]
  public async Task SupersededAssessmentAndProcedureLinkCannotOvertakeReviewedBasis()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.ClearRisk);
    await using var db = new AuditSphereDbContext(pg.Options); var r = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    await using (var other = new AuditSphereDbContext(pg.Options)) Assert.True((await RiskBandService.AssessAsync(other, PbcSeed.Actor(f.Staff,"Staff"), new(fields.ClearRisk!.RiskId, 3, 3, true, "SYNTHETIC changed assessment"))).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, r)).ErrorCode);
    Assert.Empty(await db.AuditPlanningCommandReceipts.ToListAsync()); Assert.Empty(await db.RiskPartnerClearances.ToListAsync());
    var (linkActor, linkFields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.LinkProcedureRisk);
    var link = await AuditPlanningReceiptSeed.ReviewAsync(db, linkActor, f.EngagementId, linkFields);
    await using (var other = new AuditSphereDbContext(pg.Options)) Assert.True((await AuditProgramService.LinkRiskAsync(other, linkActor, w.ProcedureId, fields.ClearRisk.RiskId)).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCommandWorkspace.ExecuteAsync(db, linkActor, f.EngagementId, link)).ErrorCode);
  }

  private sealed class PauseReceipt(bool refuse) : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
      if (e.Context!.ChangeTracker.Entries<AuditPlanningCommandReceipt>().Any(x => x.State == EntityState.Added))
      { Reached.TrySetResult(true); await Release.Task.WaitAsync(ct); if (refuse) throw new InvalidOperationException("SYNTHETIC receipt refusal"); }
      return result;
    }
  }
  [Theory, InlineData(false), InlineData(true), Trait("Profile", "Database")]
  public async Task LookupWaitsForPublicationOrRollback(bool refuse)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk); AuditPlanningCommandRequest r;
    await using (var setup = new AuditSphereDbContext(pg.Options)) r = await AuditPlanningReceiptSeed.ReviewAsync(setup, a, f.EngagementId, fields);
    var pause = new PauseReceipt(refuse);
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reader = new AuditSphereDbContext(pg.Options); await using var observer = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      var publish = AuditPlanningCommandWorkspace.ExecuteAsync(writer, a, f.EngagementId, r, timeout.Token); await pause.Reached.Task.WaitAsync(timeout.Token);
      var lookup = AuditPlanningCommandWorkspace.LookupAsync(reader, a, f.EngagementId, r.RequestId, r.ExpectedRequestHash, timeout.Token);
      var deadline = DateTimeOffset.UtcNow.AddSeconds(10); var waiting = false;
      while (DateTimeOffset.UtcNow < deadline)
      {
        var count = await observer.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND (query LIKE '%client_safety_states%' OR query LIKE '%users%')").SingleAsync(timeout.Token);
        if (count > 0) { waiting = true; break; } await Task.Delay(20, timeout.Token);
      }
      Assert.True(waiting); Assert.False(lookup.IsCompleted); pause.Release.TrySetResult(true);
      if (refuse) await Assert.ThrowsAsync<InvalidOperationException>(() => publish); else Assert.True((await publish).Succeeded);
      var recovered = await lookup; Assert.True(recovered.Succeeded, recovered.Message); Assert.Equal(!refuse, recovered.Value!.Found);
      await using var verify = new AuditSphereDbContext(pg.Options);
      Assert.Equal(refuse ? 0 : 1, await verify.AuditRisks.CountAsync()); Assert.Equal(refuse ? 0 : 1, await verify.AuditPlanningCommandReceipts.CountAsync());
    }
    finally { pause.Release.TrySetResult(true); }
  }

  [Fact, Trait("Profile", "Database")]
  public async Task ActorScopeEpochAndExactHashAreEnforcedOnRecovery()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk);
    await using var db = new AuditSphereDbContext(pg.Options); var r = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    Assert.False((await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, r with { ExpectedRequestHash = null })).Succeeded);
    var saved = await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, r); Assert.True(saved.Succeeded, saved.Message);
    var other = await AuditPlanningCommandWorkspace.LookupAsync(db, PbcSeed.Actor(f.Admin,"Partner"), f.EngagementId, r.RequestId, r.ExpectedRequestHash); Assert.True(other.Succeeded); Assert.False(other.Value!.Found);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await AuditPlanningCommandWorkspace.LookupAsync(db, a, f.EngagementId, r.RequestId, new('a',64))).ErrorCode);
    Assert.False((await AuditPlanningCommandWorkspace.LookupAsync(db, a, Guid.NewGuid(), r.RequestId, r.ExpectedRequestHash)).Succeeded);
    await db.Users.Where(x => x.Id == a.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.False((await AuditPlanningCommandWorkspace.LookupAsync(db, a, f.EngagementId, r.RequestId, r.ExpectedRequestHash)).Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, (await AuditPlanningCommandWorkspace.LookupAsync(db, a with { SessionEpoch = a.SessionEpoch + 1 }, f.EngagementId, r.RequestId, r.ExpectedRequestHash)).ErrorCode);
  }
  [Fact, Trait("Profile", "Database")]
  public async Task MaximumArabicMultilineRiskFieldsRoundTripThroughPreviewReceiptAndResult()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var text = "مراجعة\r\n\t" + new string('ع', 3991);
    Assert.Equal(4000, text.Length);
    var fields = new AuditPlanningCommandFields(AuditPlanningActions.CreateRisk, CreateRisk: new("النقد", "الوجود", text, text, "SIGNIFICANT", text, text));
    var a = PbcSeed.Actor(f.Staff, "Staff"); await using var db = new AuditSphereDbContext(pg.Options);
    var r = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    var preview = await AuditPlanningCommandWorkspace.PreviewAsync(db, a, f.EngagementId, r); Assert.True(preview.Succeeded, preview.Message);
    Assert.Equal(text, preview.Value!.CanonicalInput.CreateRisk!.Description);
    var result = await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, r); Assert.True(result.Succeeded, result.Message);
    Assert.Equal(text, result.Value!.Result.Risk!.Description);
    Assert.Equal(text, result.Value.Preview.CanonicalInput.CreateRisk!.ControlsConsidered);
    var lookup = await AuditPlanningCommandWorkspace.LookupAsync(db, a, f.EngagementId, r.RequestId, r.ExpectedRequestHash);
    Assert.Equal(text, lookup.Value!.Receipt!.Result.Risk!.Drivers);
    var stored = await db.AuditPlanningCommandReceipts.SingleAsync(); Assert.InRange(stored.InputJson.Length, 30001, AuditPlanningCommandReceipt.MaximumInputJsonLength);
    var oversized = r with { Fields = fields with { CreateRisk = fields.CreateRisk! with { Description = text + "ع" } } };
    Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, oversized)).ErrorCode);
  }

  [Theory, MemberData(nameof(Actions)), Trait("Profile", "Database")]
  public async Task InsertGuardRejectsMissingPolymorphicResultForEveryAction(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, action);
    await using var db = new AuditSphereDbContext(pg.Options); var request = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    var saved = await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, request); Assert.True(saved.Succeeded, saved.Message);
    var row = await db.AuditPlanningCommandReceipts.AsNoTracking().SingleAsync();
    row.Id = Guid.NewGuid(); row.RequestId = Guid.NewGuid(); row.ResultId = Guid.NewGuid();
    var preview = System.Text.Json.Nodes.JsonNode.Parse(row.PreviewJson)!; preview["RequestId"] = row.RequestId.ToString(); row.PreviewJson = preview.ToJsonString();
    var result = System.Text.Json.Nodes.JsonNode.Parse(row.ResultJson)!; result["ResultId"] = row.ResultId.ToString(); row.ResultJson = result.ToJsonString();
    await using var invalid = new AuditSphereDbContext(pg.Options); invalid.AuditPlanningCommandReceipts.Add(row);
    var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync());
    Assert.Equal("23514", Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
  }

  [Theory, InlineData("snapshot"), InlineData("preview"), InlineData("foreign"), InlineData("wrong-type"), Trait("Profile", "Database")]
  public async Task InsertGuardRejectsSnapshotPreviewAndScopedResultForgery(string kind)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk);
    await using var db = new AuditSphereDbContext(pg.Options); var r = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    Assert.True((await AuditPlanningCommandWorkspace.ExecuteAsync(db, a, f.EngagementId, r)).Succeeded);
    var row = await db.AuditPlanningCommandReceipts.AsNoTracking().SingleAsync(); row.Id = Guid.NewGuid(); row.RequestId = Guid.NewGuid();
    var p = System.Text.Json.Nodes.JsonNode.Parse(row.PreviewJson)!; p["RequestId"] = row.RequestId.ToString();
    var result = System.Text.Json.Nodes.JsonNode.Parse(row.ResultJson)!;
    if (kind == "snapshot") result["Risk"]!["Description"] = "forged";
    if (kind == "preview") p["CanonicalInput"]!["CreateRisk"]!["Description"] = "forged";
    if (kind == "foreign")
    {
      var other = await AuditPlanningReceiptSeed.SeedAsync(pg); var pair = await AuditPlanningReceiptSeed.FieldsAsync(pg, other, AuditPlanningActions.AssessRisk);
      row.ResultId = pair.Fields.AssessRisk!.RiskId; result["ResultId"] = row.ResultId.ToString(); result["Risk"]!["RiskId"] = row.ResultId.ToString();
    }
    if (kind == "wrong-type")
    {
      var pair = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.ApproveMateriality);
      row.ResultId = pair.Fields.Approve!.AssessmentId; result["ResultId"] = row.ResultId.ToString(); result["Risk"]!["RiskId"] = row.ResultId.ToString();
    }
    row.PreviewJson = p.ToJsonString(); row.ResultJson = result.ToJsonString();
    await using var invalid = new AuditSphereDbContext(pg.Options); invalid.AuditPlanningCommandReceipts.Add(row);
    var error = await Assert.ThrowsAsync<DbUpdateException>(() => invalid.SaveChangesAsync()); Assert.Equal("23514", Assert.IsType<PostgresException>(error.GetBaseException()).SqlState);
  }

  private sealed class HoldEngagement(string mode) : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int paused;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains("FROM engagements", StringComparison.Ordinal) && command.CommandText.Contains(mode, StringComparison.Ordinal) && Interlocked.Exchange(ref paused, 1) == 0)
      { Reached.TrySetResult(true); await Release.Task.WaitAsync(ct); }
      return result;
    }
  }
  private sealed class HoldWorkpaper : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result, CancellationToken ct = default)
    {
      if (e.Context!.ChangeTracker.Entries<Workpaper>().Any(x => x.State == EntityState.Added))
      { Reached.TrySetResult(true); await Release.Task.WaitAsync(ct); }
      return result;
    }
  }
  private static async Task WaitForLockAsync(AuditSphereDbContext observer, string table, CancellationToken ct)
  {
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", ct);
      var count = await observer.Database.SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() AND wait_event_type='Lock' AND query LIKE {"%" + table + "%"}").SingleAsync(ct);
      if (count > 0) return; await Task.Delay(20, ct);
    }
    Assert.Fail("The expected database lock waiter was not observed.");
  }

  [Theory, InlineData(false), InlineData(true), Trait("Profile", "Database")]
  public async Task PlanningAndProcedureSubmissionOrReviewAcquireClientBeforeEngagement(bool reviewing)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk);
    AuditPlanningCommandRequest request; Guid resultId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      request = await AuditPlanningReceiptSeed.ReviewAsync(setup, a, f.EngagementId, fields);
      if (reviewing)
      {
        setup.AuditProcedureResults.Add(new() { Id = resultId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          AuditProcedureId = w.ProcedureId, Revision = 1, InputGeneration = 1, WorkPerformed = "SYNTHETIC", Conclusion = "SYNTHETIC",
          StructuredResultJson = "{}", EvidenceReferencesJson = "[]", PreparedByUserId = f.Staff.Id, SubmittedAt = DateTimeOffset.UtcNow });
        await setup.SaveChangesAsync();
      }
    }
    var hold = new HoldEngagement(reviewing ? "FOR UPDATE" : "FOR SHARE");
    await using var procedure = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hold).Options);
    await using var planning = new AuditSphereDbContext(pg.Options); await using var observer = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    async Task ExerciseProcedure()
    {
      if (reviewing)
      {
        var result = await AuditProgramService.ReviewResultAsync(procedure, PbcSeed.Actor(f.Reviewer,"Manager"), new(resultId, "REVIEWED", "SYNTHETIC review"), timeout.Token);
        Assert.NotEqual(ErrorCodes.IdempotencyConflict, result.ErrorCode);
      }
      else
      {
        var result = await AuditProgramService.SubmitResultAsync(procedure, a, new(w.ProcedureId, 1, "SYNTHETIC work", "{}", [], "SYNTHETIC conclusion"), timeout.Token);
        Assert.NotEqual(ErrorCodes.IdempotencyConflict, result.ErrorCode);
      }
    }
    try
    {
      var first = ExerciseProcedure(); await hold.Reached.Task.WaitAsync(timeout.Token);
      var second = AuditPlanningCommandWorkspace.ExecuteAsync(planning, a, f.EngagementId, request, timeout.Token);
      await WaitForLockAsync(observer, "client_safety_states", timeout.Token);
      Assert.False(second.IsCompleted); hold.Release.TrySetResult(true); await first;
      var published = await second; Assert.True(published.Succeeded, published.Message);
    }
    finally { hold.Release.TrySetResult(true); }
  }

  [Fact, Trait("Profile", "Database")]
  public async Task SameActorWorkpaperForeignKeyAndPlanningPublicationDoNotCycle()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk); AuditPlanningCommandRequest request;
    await using (var setup = new AuditSphereDbContext(pg.Options)) request = await AuditPlanningReceiptSeed.ReviewAsync(setup, a, f.EngagementId, fields);
    var hold = new HoldWorkpaper(); await using var workpaper = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hold).Options);
    await using var planning = new AuditSphereDbContext(pg.Options); await using var observer = new AuditSphereDbContext(pg.Options);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
      var first = AuditPlanningService.CreateWorkpaperAsync(workpaper, a, new(f.EngagementId, "SYN-KS", "SYNTHETIC workpaper", "SYNTHETIC objective", "SYNTHETIC-v1", null, "SYNTHETIC procedure"), timeout.Token);
      await hold.Reached.Task.WaitAsync(timeout.Token);
      var second = AuditPlanningCommandWorkspace.ExecuteAsync(planning, a, f.EngagementId, request, timeout.Token);
      await WaitForLockAsync(observer, "engagements", timeout.Token);
      hold.Release.TrySetResult(true); Assert.True((await first).Succeeded);
      var published = await second; Assert.True(published.Succeeded, published.Message);
    }
    finally { hold.Release.TrySetResult(true); }
  }

  [Fact, Trait("Profile", "Database")]
  public async Task RevocationHoldingFirmGuardWinsBeforePublication()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, AuditPlanningActions.CreateRisk); AuditPlanningCommandRequest request;
    await using (var setup = new AuditSphereDbContext(pg.Options)) request = await AuditPlanningReceiptSeed.ReviewAsync(setup, a, f.EngagementId, fields);
    await using var revoke = new AuditSphereDbContext(pg.Options); await using var tx = await revoke.Database.BeginTransactionAsync();
    await revoke.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={f.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();
    await using var planning = new AuditSphereDbContext(pg.Options); using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var pending = AuditPlanningCommandWorkspace.ExecuteAsync(planning, a, f.EngagementId, request, timeout.Token);
    await WaitForLockAsync(revoke, "firm_safety_states", timeout.Token);
    await revoke.RoleGrants.Where(x => x.UserId == a.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    await tx.CommitAsync(); Assert.False((await pending).Succeeded);
    await using var verify = new AuditSphereDbContext(pg.Options); Assert.Empty(await verify.AuditRisks.ToListAsync()); Assert.Empty(await verify.AuditPlanningCommandReceipts.ToListAsync());
  }

  [Theory, InlineData("ROUND_MATERIALITY"), InlineData("APPROVE_MATERIALITY"), Trait("Profile", "Database")]
  public async Task NewEffectiveAssessmentCannotBeOvertakenByOldReviewedMateriality(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg, w, action);
    await using var db = new AuditSphereDbContext(pg.Options); var request = await AuditPlanningReceiptSeed.ReviewAsync(db, a, f.EngagementId, fields);
    await using (var other = new AuditSphereDbContext(pg.Options))
      Assert.True((await MaterialityEngineService.CalculateAsync(other, PbcSeed.Actor(f.Staff,"Staff"), new(f.EngagementId,"REVENUE",null,2m,75m,5m,"SYNTHETIC replacement"))).Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, (await AuditPlanningCommandWorkspace.ExecuteAsync(db,a,f.EngagementId,request)).ErrorCode);
    Assert.Empty(await db.MaterialityApprovals.ToListAsync()); Assert.Empty(await db.MaterialityRoundingDecisions.ToListAsync()); Assert.Empty(await db.AuditPlanningCommandReceipts.ToListAsync());
  }

  [Fact, Trait("Profile", "Database")]
  public async Task MigrationDownPreservesRetainedEvidenceAndEmptyDowngradeKeepsExistingPlanning()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await AuditPlanningReceiptSeed.SeedAsync(pg); var f = w.Fixture;
    await using var db = new AuditSphereDbContext(pg.Options);
    var migrations = db.Database.GetMigrations().ToArray(); var index = Array.IndexOf(migrations,"20261016102652_AuditPlanningReviewedCommandReceipts"); Assert.True(index>0);
    await db.GetService<IMigrator>().MigrateAsync(migrations[index-1]);
    Assert.True(await db.Database.SqlQuery<bool>($"SELECT EXISTS (SELECT 1 FROM audit_procedures WHERE id = {w.ProcedureId}) AS \"Value\"").SingleAsync());
    await db.GetService<IMigrator>().MigrateAsync(migrations[^1]);
    var (a, fields) = await AuditPlanningReceiptSeed.FieldsAsync(pg,w,AuditPlanningActions.CreateRisk);
    var request = await AuditPlanningReceiptSeed.ReviewAsync(db,a,f.EngagementId,fields);
    Assert.True((await AuditPlanningCommandWorkspace.ExecuteAsync(db,a,f.EngagementId,request)).Succeeded);
    var refusal = await Assert.ThrowsAsync<InvalidOperationException>(()=>db.GetService<IMigrator>().MigrateAsync(migrations[index-1]));
    Assert.Equal("55000",Assert.IsType<PostgresException>(refusal.GetBaseException()).SqlState);
    await using var verify = new AuditSphereDbContext(pg.Options); Assert.Single(await verify.AuditPlanningCommandReceipts.ToListAsync());
  }

  [Theory, InlineData("APPROVE_MATERIALITY"), InlineData("CLEAR_RISK"), Trait("Profile", "Database")]
  public async Task CompetingPartnersCannotPublishTwoDecisionsOrRecoverEachOthersIntent(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync(); var w=await AuditPlanningReceiptSeed.SeedAsync(pg); var f=w.Fixture;
    var (a,fields)=await AuditPlanningReceiptSeed.FieldsAsync(pg,w,action); var b=PbcSeed.Actor(w.OtherPartner,"Partner");
    AuditPlanningCommandRequest one,two;
    await using(var setup=new AuditSphereDbContext(pg.Options))
    { one=await AuditPlanningReceiptSeed.ReviewAsync(setup,a,f.EngagementId,fields); two=await AuditPlanningReceiptSeed.ReviewAsync(setup,b,f.EngagementId,fields); }
    await using var first=new AuditSphereDbContext(pg.Options); await using var second=new AuditSphereDbContext(pg.Options);
    var results=await Task.WhenAll(AuditPlanningCommandWorkspace.ExecuteAsync(first,a,f.EngagementId,one),AuditPlanningCommandWorkspace.ExecuteAsync(second,b,f.EngagementId,two));
    Assert.Single(results,x=>x.Succeeded);
    await using var verify=new AuditSphereDbContext(pg.Options); Assert.Single(await verify.AuditPlanningCommandReceipts.ToListAsync());
    Assert.Equal(action==AuditPlanningActions.ApproveMateriality?1:0,await verify.MaterialityApprovals.CountAsync());
    Assert.Equal(action==AuditPlanningActions.ClearRisk?1:0,await verify.RiskPartnerClearances.CountAsync());
    var winner=results[0].Succeeded?one:two;var loser=results[0].Succeeded?b:a;
    var hidden=await AuditPlanningCommandWorkspace.LookupAsync(verify,loser,f.EngagementId,winner.RequestId,winner.ExpectedRequestHash);
    Assert.True(hidden.Succeeded);Assert.False(hidden.Value!.Found);
  }

  [Theory, MemberData(nameof(Actions)), Trait("Profile", "Database")]
  public async Task LegacyCommandsRetainAmbientTransactionOwnership(string action)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await AuditPlanningReceiptSeed.SeedAsync(pg);var f=w.Fixture;
    var(a,fields)=await AuditPlanningReceiptSeed.FieldsAsync(pg,w,action);
    async Task<int> Count(AuditSphereDbContext d)=>await d.MaterialityAssessments.CountAsync()+await d.MaterialityApprovals.CountAsync()+await d.MaterialityRoundingDecisions.CountAsync()+await d.AuditRisks.CountAsync()+await d.RiskBandAssessments.CountAsync()+await d.RiskOwnerAssignments.CountAsync()+await d.RiskPartnerClearances.CountAsync();
    int before;
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      before=await Count(db);await using var tx=await db.Database.BeginTransactionAsync();
      var succeeded=action switch
      {
        AuditPlanningActions.CalculateMateriality=>(await MaterialityEngineService.CalculateAsync(db,a,new(f.EngagementId,"REVENUE",null,1m,75m,5m,"SYNTHETIC ambient"))).Succeeded,
        AuditPlanningActions.RoundMateriality=>(await MaterialityEngineService.ApplyPracticalRoundingAsync(db,a,new(fields.Round!.AssessmentId,1000m,750m,50m,"SYNTHETIC ambient"))).Succeeded,
        AuditPlanningActions.ApproveMateriality=>(await AuditPlanningService.ApproveMaterialityAssessmentAsync(db,a,fields.Approve!.AssessmentId)).Succeeded,
        AuditPlanningActions.CreateRisk=>(await AuditPlanningService.CreateAuditRiskAsync(db,a,new(f.EngagementId,"CASH","Existence","SYNTHETIC ambient","SYNTHETIC drivers","NORMAL",null,"SYNTHETIC response"))).Succeeded,
        AuditPlanningActions.AssessRisk=>(await RiskBandService.AssessAsync(db,a,new(fields.AssessRisk!.RiskId,3,3,false,"SYNTHETIC ambient"))).Succeeded,
        AuditPlanningActions.AssignRiskOwner=>(await RiskBandService.AssignOwnerAsync(db,a,fields.AssignRiskOwner!.RiskId,fields.AssignRiskOwner.OwnerUserId)).Succeeded,
        AuditPlanningActions.ClearRisk=>(await RiskBandService.PartnerClearAsync(db,a,fields.ClearRisk!.RiskId,"SYNTHETIC ambient")).Succeeded,
        _=>(await AuditProgramService.LinkRiskAsync(db,a,fields.LinkProcedureRisk!.ProcedureId,fields.LinkProcedureRisk.RiskId)).Succeeded
      };
      Assert.True(succeeded);Assert.Same(tx,db.Database.CurrentTransaction);await tx.RollbackAsync();
    }
    await using var verify=new AuditSphereDbContext(pg.Options);Assert.Equal(before,await Count(verify));Assert.Empty(await verify.AuditPlanningCommandReceipts.ToListAsync());
    Assert.Null((await verify.AuditProcedures.SingleAsync(x=>x.Id==w.ProcedureId)).RiskId);
  }

}
