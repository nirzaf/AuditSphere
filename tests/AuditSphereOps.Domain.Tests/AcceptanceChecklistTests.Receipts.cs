using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AcceptanceChecklistTests
{
  private static async Task<AssessmentCommandRequest> ReviewAsync(AuditSphereDbContext db, ActorContext actor, Guid clientId,
    AssessmentCommandFields fields, Guid? requestId = null)
  {
    var request = new AssessmentCommandRequest(requestId ?? Guid.NewGuid(), fields);
    var preview = await AssessmentCommandWorkspace.PreviewAsync(db, actor, clientId, request);
    Assert.True(preview.Succeeded, preview.Message);
    return request with { Fields = preview.Value!.Fields, ReviewBasis = preview.Value.ReviewBasis, RequestHash = preview.Value.RequestHash, Reviewed = true };
  }

  [Fact]
  public async Task ReviewedAnswerHasImmutableReceiptAndActorOwnedRecovery()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await ReviewAsync(db, w.Staff, w.ClientId, new("ANSWER", "1", "0", "CE-001", "Yes", "DOC"));
    Assert.False((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request with { Reviewed = false })).Succeeded);
    var result = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal("ANSWER", result.Value!.Kind);
    var replay = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(result.Value.Id, replay.Value!.Id);
    Assert.Single(await db.EvaluationResponses.ToListAsync());
    Assert.Single(await db.AssessmentCommandReceipts.ToListAsync());
    var lookup = await AssessmentCommandWorkspace.LookupAsync(db, w.Staff, w.ClientId, request.RequestId, request.RequestHash);
    Assert.True(lookup.Succeeded);
    Assert.Equal(result.Value.Id, lookup.Value!.Receipt!.Id);
    var otherActor = await AssessmentCommandWorkspace.LookupAsync(db, w.Manager, w.ClientId, request.RequestId, request.RequestHash);
    Assert.True(otherActor.Succeeded);
    Assert.False(otherActor.Value!.Found);
    var conflict = await AssessmentCommandWorkspace.LookupAsync(db, w.Staff, w.ClientId, request.RequestId, new string('0', 64));
    Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
    await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(async () => {
      var row = await db.AssessmentCommandReceipts.SingleAsync();
      row.ReviewBasis = new string('0', 64);
      await db.SaveChangesAsync();
    });
    await using var delete = new AuditSphereDbContext(pg.Options);
    await Assert.ThrowsAsync<Npgsql.PostgresException>(async () => await delete.AssessmentCommandReceipts.ExecuteDeleteAsync());
  }

  [Fact]
  public async Task ChangedReviewedIntentAndStaleAnswerCannotPublish()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var fields = new AssessmentCommandFields("ANSWER", "1", "0", "CE-001", "Yes", "DOC");
    var request = await ReviewAsync(db, w.Staff, w.ClientId, fields);
    Assert.True((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request)).Succeeded);
    var changed = await ReviewAsync(db, w.Staff, w.ClientId, fields with { Revision = "1", Evidence = "DIFFERENT" }, request.RequestId);
    var conflict = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, changed);
    Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
    Assert.Single(await db.EvaluationResponses.ToListAsync());
    var stale = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request with { RequestId = Guid.NewGuid() });
    Assert.False(stale.Succeeded);
    Assert.Single(await db.AssessmentCommandReceipts.ToListAsync());
    var irrelevant = await AssessmentCommandWorkspace.PreviewAsync(db, w.Staff, w.ClientId,
      new(Guid.NewGuid(), fields with { Rationale = "Not part of an answer" }));
    Assert.Equal("request.invalid", irrelevant.ErrorCode);
  }

  [Fact]
  public async Task BoundedUnicodeAnswersAndBeforeEvidenceFitTheReceiptBoundary()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.QuestionDefinitions.Add(new QuestionDefinition { Id=Guid.NewGuid(), TemplateId=QuestionnaireSeed.CeTemplateId,
      QuestionCode="CE-TEXT", Section="Evidence", PromptText="Reviewed bounded text", Category="KYC", AnswerType="TEXT",
      RequiresEvidence=true, SortOrder=10000 });
    await db.SaveChangesAsync();
    var previous = new string('界', 2000); var evidence = new string('界', 500);
    Assert.True((await AcceptanceChecklistService.RecordAnswerAsync(db, w.Staff, w.ClientId, "CE-TEXT", previous, evidence)).Succeeded);
    var request = await ReviewAsync(db, w.Staff, w.ClientId, new("ANSWER", "1", "1", "CE-TEXT", new string('文', 2000), evidence));
    var executed = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request);
    Assert.True(executed.Succeeded, executed.Message);
    Assert.Equal(previous, executed.Value!.Preview.Before.Answer);
    Assert.Single(await db.AssessmentCommandReceipts.ToListAsync());
  }

  [Fact]
  public async Task SpecialistRequestAndResultReplayAndSameStatusEvidenceChangeIsStale()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await ReviewAsync(db, w.Staff, w.ClientId, new("REQUEST_REVIEW", "1", Area:"AML", Specialist:"AML reviewer"));
    var first = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request);
    Assert.True(first.Succeeded, first.Message);
    Assert.True((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request)).Succeeded);
    Assert.Single(await db.SpecialistClearances.ToListAsync());
    var review = first.Value!.ResourceId;
    var held = await ReviewAsync(db, w.Manager, w.ClientId,
      new("RECORD_REVIEW", "1", Evidence:"SCREEN", ReviewId:review, Status:"HOLD", ExpectedStatus:"PENDING"));
    Assert.True((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, held)).Succeeded);
    Assert.True((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, held)).Succeeded);
    var clear = await ReviewAsync(db, w.Manager, w.ClientId,
      new("RECORD_REVIEW", "1", Evidence:"CLEAR", ReviewId:review, Status:"CLEARED", ExpectedStatus:"HOLD"));
    // An unchanged status is insufficient: the actual evidence changed after the preview.
    await using (var competing = new AuditSphereDbContext(pg.Options))
      Assert.True((await AcceptanceChecklistService.RecordClearanceAsync(competing, w.Manager, review, "HOLD", "CHANGED", null)).Succeeded);
    var stale = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, clear);
    Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    Assert.Equal("HOLD", (await db.SpecialistClearances.AsNoTracking().SingleAsync()).Status);
    Assert.Equal(2, await db.AssessmentCommandReceipts.CountAsync());
  }

  [Fact]
  public async Task DecisionReceiptRecoversAfterImmutableDecisionWithoutRepeatingIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var fields = new AssessmentCommandFields("DECISION", "1", ServiceRoute:"AccountingOnly", Decision:"Accepted", Rationale:"Reviewed");
    var blocked = await AssessmentCommandWorkspace.PreviewAsync(db, w.Partner, w.ClientId, new(Guid.NewGuid(), fields));
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
    var request = await ReviewAsync(db, w.Partner, w.ClientId, fields with { Decision = "Declined" });
    var first = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId, request);
    Assert.True(first.Succeeded, first.Message);
    var replay = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(first.Value!.ResourceId, replay.Value!.ResourceId);
    Assert.Single(await db.AcceptanceDecisions.ToListAsync());
    Assert.Empty(await db.ClientWorkspaces.ToListAsync());
    Assert.Equal("PROSPECT", (await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == w.ClientId)).Status);
    var lookup = await AssessmentCommandWorkspace.LookupAsync(db, w.Partner, w.ClientId, request.RequestId, request.RequestHash);
    Assert.True(lookup.Value!.Found);
  }

  [Fact]
  public async Task ContinuanceReceiptReplaysOriginalGenerationAndLookupRequiresCurrentAuthority()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await AnswerAllAsync(pg, w);
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.True((await AcceptanceDecisionService.RecordAsync(db, w.Partner,
      new(w.ClientId, null, "AccountingOnly", "Accepted", "Reviewed", null, 1))).Succeeded);
    var request = await ReviewAsync(db, w.Manager, w.ClientId, new("CONTINUANCE", "1"));
    var first = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, request);
    Assert.True(first.Succeeded, first.Message);
    Assert.Equal("2", first.Value!.ResultGeneration);
    var replay = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, request);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(first.Value.Id, replay.Value!.Id);
    Assert.Equal(2, await db.AcceptanceDecisions.CountAsync());
    var foreign = await SeedAsync(pg);
    var denied = await AssessmentCommandWorkspace.LookupAsync(db, w.Manager, foreign.ClientId, request.RequestId, request.RequestHash);
    var missing = await AssessmentCommandWorkspace.LookupAsync(db, w.Manager, Guid.NewGuid(), request.RequestId, request.RequestHash);
    Assert.False(denied.Succeeded);
    Assert.Equal(denied.ErrorCode, missing.ErrorCode);
    await using (var revoke = new AuditSphereDbContext(pg.Options))
      await revoke.Users.Where(x => x.Id == w.Manager.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.False((await AssessmentCommandWorkspace.LookupAsync(db, w.Manager, w.ClientId, request.RequestId, request.RequestHash)).Succeeded);
    Assert.False((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Manager, w.ClientId, request)).Succeeded);
  }

  private sealed class RefuseReceipt : SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context!.ChangeTracker.Entries<AssessmentCommandReceipt>().Any(x => x.State == EntityState.Added))
        throw new InvalidOperationException("Synthetic receipt storage refusal");
      return ValueTask.FromResult(result);
    }
  }

  private sealed class PauseReceipt : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context!.ChangeTracker.Entries<AssessmentCommandReceipt>().Any(x => x.State == EntityState.Added)) {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }
  private sealed class ObserveClientLock : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("client_safety_states", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        Reached.TrySetResult(true);
      return ValueTask.FromResult(result);
    }
  }

  [Fact]
  public async Task RecoveryWaitsForDispatchAndTwoConcurrentExecutionsPublishOneMutation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var pause = new PauseReceipt();
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    var request = await ReviewAsync(writer, w.Staff, w.ClientId, new("ANSWER", "1", "0", "CE-001", "true", "DOC"));
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var dispatch = AssessmentCommandWorkspace.ExecuteAsync(writer, w.Staff, w.ClientId, request, deadline.Token);
    await pause.Reached.Task.WaitAsync(deadline.Token);
    var observed = new ObserveClientLock();
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(observed).Options);
    var lookup = AssessmentCommandWorkspace.LookupAsync(reader, w.Staff, w.ClientId, request.RequestId, request.RequestHash, deadline.Token);
    await observed.Reached.Task.WaitAsync(deadline.Token);
    Assert.False(lookup.IsCompleted);
    await using var retry = new AuditSphereDbContext(pg.Options);
    var replay = AssessmentCommandWorkspace.ExecuteAsync(retry, w.Staff, w.ClientId, request, deadline.Token);
    try {
      pause.Release.TrySetResult(true);
      var first = await dispatch;
      var recovered = await lookup;
      var second = await replay;
      Assert.True(first.Succeeded, first.Message);
      Assert.True(recovered.Succeeded, recovered.Message);
      Assert.True(recovered.Value!.Found);
      Assert.True(second.Succeeded, second.Message);
      Assert.Equal(first.Value!.Id, recovered.Value.Receipt!.Id);
      Assert.Equal(first.Value.Id, second.Value!.Id);
    }
    finally { pause.Release.TrySetResult(true); }
    await using var after = new AuditSphereDbContext(pg.Options);
    Assert.Single(await after.EvaluationResponses.ToListAsync());
    Assert.Single(await after.AssessmentCommandReceipts.ToListAsync());
  }

  [Fact]
  public async Task ReviewedCommandRevokedDuringPublicationWaitPublishesNoReceiptOrAnswer()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    AssessmentCommandRequest request;
    await using (var review = new AuditSphereDbContext(pg.Options))
      request = await ReviewAsync(review, w.Staff, w.ClientId, new("ANSWER", "1", "0", "CE-001", "Yes", "DOC"));
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={w.FirmId} FOR UPDATE").SingleAsync();
    var signal = new ObserveFirmLock();
    await using var writer = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(signal).Options);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    var attempt = AssessmentCommandWorkspace.ExecuteAsync(writer, w.Staff, w.ClientId, request, deadline.Token);
    await signal.Reached.Task.WaitAsync(deadline.Token);
    await blocker.RoleGrants.Where(x => x.UserId == w.Staff.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    await blocker.Users.Where(x => x.Id == w.Staff.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    await tx.CommitAsync();
    Assert.False((await attempt).Succeeded);
    await using var after = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await after.EvaluationResponses.ToListAsync());
    Assert.Empty(await after.AssessmentCommandReceipts.ToListAsync());
  }

  [Fact]
  public async Task ReviewedDecisionCommandRefusesAuthorityRevokedAfterPreview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    AssessmentCommandRequest request;
    await using (var review = new AuditSphereDbContext(pg.Options))
      request = await ReviewAsync(review, w.Partner, w.ClientId,
        new("DECISION", "1", ServiceRoute: "AccountingOnly", Decision: "Declined", Rationale: "Revoked reviewed decision"));

    await using (var revoke = new AuditSphereDbContext(pg.Options))
    {
      await revoke.RoleGrants.Where(x => x.UserId == w.Partner.UserId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      await revoke.Users.Where(x => x.Id == w.Partner.UserId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    }

    await using (var execute = new AuditSphereDbContext(pg.Options))
    {
      var result = await AssessmentCommandWorkspace.ExecuteAsync(execute, w.Partner, w.ClientId, request);
      Assert.False(result.Succeeded);
    }

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await proof.AssessmentCommandReceipts.ToListAsync());
    Assert.False(await proof.AcceptanceDecisions.AnyAsync(x => x.Rationale == "Revoked reviewed decision"));
  }

  [Fact]
  public async Task ReceiptStorageFailureRollsBackThePreviouslySavedAssessmentMutation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new RefuseReceipt()).Options)) {
      var request = await ReviewAsync(db, w.Staff, w.ClientId, new("ANSWER", "1", "0", "CE-001", "Yes", "DOC"));
      await Assert.ThrowsAsync<InvalidOperationException>(async () => await AssessmentCommandWorkspace.ExecuteAsync(db, w.Staff, w.ClientId, request));
    }
    await using var observer = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await observer.EvaluationResponses.ToListAsync());
    Assert.Empty(await observer.AssessmentCommandReceipts.ToListAsync());
  }
}
