using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Data.Common;
using System.Text.Json;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private static async Task<(Guid SelectionId, Guid[] Items)> SamplingAsync(AuditSphereDbContext db, World w, int count = 1)
  {
    var maker = w.A("associate", "Staff");
    var reviewer = w.A("manager", "Manager");
    var schedule = await AuditFieldworkService.CreateScheduleAsync(db, maker, new(
      w.EngagementId, "RECEIVABLES_LEAD", "sampling-ledger", "retained-listing",
      DateTimeOffset.UtcNow, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "Debit positive",
      Hashing.Sha256Hex("sampling-listing"), count * 10m,
      Enumerable.Range(1, count).Select(i => new ScheduleRowInput("sample-" + i, i, "1000", "Sample", 10m,
        "QAR", null, new DateOnly(2026, 12, 20), null, null, "{}")).ToArray()));
    Assert.True(schedule.Succeeded, schedule.Message);
    Assert.True((await AuditFieldworkService.ReviewScheduleAsync(db, reviewer,
      new(schedule.Value!.ScheduleId, "Agreed retained population", true))).Succeeded);
    var selection = await AuditFieldworkService.CreateSelectionAsync(db, maker, new(w.EngagementId, w.ProcedureId,
      schedule.Value.ScheduleId, null, "KEY_ITEM", "Every identified row",
      Enumerable.Range(1, count).Select(i => new SelectionItemInput("sample-" + i, 10m, "QAR", "Selected exact row", null)).ToArray()));
    Assert.True(selection.Succeeded, selection.Message);
    Assert.True((await AuditFieldworkService.ReviewSelectionAsync(db, reviewer,
      new(selection.Value!.SelectionId, AuditSelectionStatuses.Reviewed, null))).Succeeded);
    return (selection.Value.SelectionId, await db.AuditSelectionItems.Where(x => x.SelectionId == selection.Value.SelectionId)
      .OrderBy(x => x.StableRowId).Select(x => x.Id).ToArrayAsync());
  }

  private static async Task<Guid> SamplingTestAsync(AuditSphereDbContext db, World w, Guid item, string result = AuditItemTestResults.Pass)
  {
    var recorded = await AuditFieldworkService.RecordItemTestAsync(db, w.A("associate", "Staff"),
      new(item, "Agreed the sampled row to retained evidence", ["Paper file B-12, page 3"], result,
        result == AuditItemTestResults.Exception ? 1m : null, null,
        result is AuditItemTestResults.Exception or AuditItemTestResults.Limitation ? "Independent follow-up recorded" : null));
    Assert.True(recorded.Succeeded, recorded.Message);
    return recorded.Value!.AuditItemTestId;
  }

  private static async Task SamplingReviewAsync(AuditSphereDbContext db, World w, Guid test, string decision)
  {
    var result = await AuditFieldworkService.ReviewItemTestAsync(db, w.A("manager", "Manager"),
      new(test, decision, decision == AuditItemTestReviewDecisions.ChangesRequired ? "Re-perform the evidence work" : "Evidence independently reviewed"));
    Assert.True(result.Succeeded, result.Message);
  }

  [Theory]
  [InlineData(AuditItemTestResults.Pass)]
  [InlineData(AuditItemTestResults.Exception)]
  [InlineData(AuditItemTestResults.Limitation)]
  [InlineData(AuditItemTestResults.Pending)]
  public async Task SamplingCompletionAndReadback_UseTheEffectiveReviewAndSubstantiveResult(string result)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w);
    var test = await SamplingTestAsync(db, w, sample.Items[0], result);
    var reviewer = w.A("manager", "Manager");
    foreach (var decision in new[] { AuditItemTestReviewDecisions.Reviewed, AuditItemTestReviewDecisions.ChangesRequired, AuditItemTestReviewDecisions.Reviewed })
    {
      await SamplingReviewAsync(db, w, test, decision);
      var expectedComplete = result != AuditItemTestResults.Pending && decision == AuditItemTestReviewDecisions.Reviewed;
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, w.EngagementId);
      Assert.True(completion.Succeeded, completion.Message);
      Assert.Equal(!expectedComplete, completion.Value!.Blockers.Any(x => x.StartsWith("item-tests:", StringComparison.Ordinal)));
      var view = await AuditSamplingService.GetSampleSetAsync(db, reviewer, sample.SelectionId);
      Assert.True(view.Succeeded, view.Message);
      var item = Assert.Single(view.Value!.Items);
      Assert.Equal(expectedComplete, item.TestReviewed);
      Assert.Equal(decision, item.TestReviewDecision);
    }
    Assert.Equal(3, await db.AuditItemTestReviews.CountAsync(x => x.AuditItemTestId == test));
    var revision = await SamplingTestAsync(db, w, sample.Items[0]);
    Assert.NotEqual(test, revision);
    var unreviewed = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, w.EngagementId);
    Assert.Contains("item-tests:1:unreviewed", unreviewed.Value!.Blockers);
    await SamplingReviewAsync(db, w, revision, AuditItemTestReviewDecisions.Reviewed);
    await db.ClientSafetyStates.Where(x => x.Id == w.ClientId && x.FirmId == w.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    var stale = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, w.EngagementId);
    Assert.Contains("item-tests:1:unreviewed", stale.Value!.Blockers);
  }

  [Fact]
  public async Task SamplingEvidenceChange_StalesTheSrmEvenWhenUnresolvedCountsStayEqual()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w, 2);
    var first = await SamplingTestAsync(db, w, sample.Items[0]);
    var second = await SamplingTestAsync(db, w, sample.Items[1]);
    await SamplingReviewAsync(db, w, first, AuditItemTestReviewDecisions.Reviewed);
    await SamplingReviewAsync(db, w, second, AuditItemTestReviewDecisions.ChangesRequired);
    var manager = w.A("manager", "Manager");
    var result = await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, manager, w.EngagementId, "One sampled item remains under rework.");
    Assert.True(result.Succeeded, result.Message);
    var srm = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == result.Value);
    Assert.True(await AuditDeliverableService.IsCurrentAsync(db, manager, srm));
    Assert.True(await AuditDeliverableService.IsCurrentAsync(db, manager, srm));
    // Replace the first test revision, so even the former any-positive predicate
    // observes one unresolved item before and after the evidence changes.
    first = await SamplingTestAsync(db, w, sample.Items[0]);
    await SamplingReviewAsync(db, w, first, AuditItemTestReviewDecisions.ChangesRequired);
    await SamplingReviewAsync(db, w, second, AuditItemTestReviewDecisions.Reviewed);
    var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, manager, w.EngagementId);
    Assert.Contains("item-tests:1:unreviewed", completion.Value!.Blockers);
    Assert.False(await AuditDeliverableService.IsCurrentAsync(db, manager, srm));
    Assert.Equal(srm.InputDigest, await db.AuditDeliverables.Where(x => x.Id == srm.Id).Select(x => x.InputDigest).SingleAsync());
    Assert.Equal(4, await db.AuditItemTestReviews.CountAsync());
  }

  [Fact]
  public async Task SamplingLegacyTimestampTie_IsConservative_AndNewDecisionsAreSerializedAfterIt()
  {
    await using var pg = await PgTestSchema.CreateAsync("20261027232538_WorkflowTaskApplicabilityEvidence");
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w);
    var test = await SamplingTestAsync(db, w, sample.Items[0]);
    var time = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero);
    foreach (var decision in new[] { AuditItemTestReviewDecisions.ChangesRequired, AuditItemTestReviewDecisions.Reviewed })
    {
      var id = Guid.CreateVersion7();
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO audit_item_test_reviews
          (id, firm_id, client_id, engagement_id, selection_item_id, audit_item_test_id, test_revision, decision, reviewer_user_id, created_at)
        VALUES ({id}, {w.FirmId}, {w.ClientId}, {w.EngagementId}, {sample.Items[0]}, {test}, 1, {decision}, {w.U["manager"].Id}, {time})
        """);
    }
    var retained = await db.Database.SqlQueryRaw<string>("""
      SELECT jsonb_agg(to_jsonb(r) ORDER BY id)::text AS "Value" FROM audit_item_test_reviews r
      """).SingleAsync();
    await db.GetService<IMigrator>().MigrateAsync();
    var upgraded = await db.Database.SqlQueryRaw<string>("""
      SELECT jsonb_agg(to_jsonb(r) - 'review_sequence' ORDER BY id)::text AS "Value" FROM audit_item_test_reviews r
      """).SingleAsync();
    Assert.Equal(retained, upgraded);
    var reviewer = w.A("manager", "Manager");
    var tied = await AuditSamplingService.GetSampleSetAsync(db, reviewer, sample.SelectionId);
    Assert.Equal(AuditItemTestReviewDecisions.ChangesRequired, Assert.Single(tied.Value!.Items).TestReviewDecision);
    await SamplingReviewAsync(db, w, test, AuditItemTestReviewDecisions.Reviewed);
    var latest = await db.AuditItemTestReviews.AsNoTracking().Where(x => x.ReviewSequence != null).SingleAsync();
    Assert.Equal(1L, latest.ReviewSequence);
    Assert.True(latest.CreatedAt < time); // No fabricated future audit timestamp.
    Assert.Equal(2, await db.AuditItemTestReviews.CountAsync(x => x.ReviewSequence == null));
    var current = await AuditSamplingService.GetSampleSetAsync(db, reviewer, sample.SelectionId);
    Assert.True(Assert.Single(current.Value!.Items).TestReviewed);
    var refused = Assert.IsAssignableFrom<Exception>(await Record.ExceptionAsync(() => db.GetService<IMigrator>()
      .MigrateAsync("20261027232538_WorkflowTaskApplicabilityEvidence")));
    Assert.Equal("55000", Assert.IsType<PostgresException>(refused.GetBaseException()).SqlState);
    Assert.Equal(3, await db.AuditItemTestReviews.CountAsync());

  }

  [Fact]
  public async Task SamplingAdverseReview_StalesPartnerClearanceAndOpinionWithoutReplacingHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w);
    var test = await SamplingTestAsync(db, w, sample.Items[0]);
    await SamplingReviewAsync(db, w, test, AuditItemTestReviewDecisions.Reviewed);
    Assert.True((await AuditProgramService.ReviewResultAsync(db, w.A("senior", "Senior"), new(w.ResultId, "REVIEWED", null))).Succeeded);
    var manager = w.A("manager", "Manager");
    var partner = w.A("partner", "Partner");
    var srm = await AuditDeliverableService.GenerateSummaryReviewMemorandumAsync(db, manager, w.EngagementId, "Current sampled evidence independently reviewed.");
    Assert.True(srm.Succeeded, srm.Message);
    var cleared = await AuditDeliverableService.PartnerClearAsync(db, partner, srm.Value, "Risk reviewed", "Notes reviewed");
    Assert.True(cleared.Succeeded, cleared.Message);
    var opinion = await AuditDeliverableService.DecideOpinionAsync(db, partner, w.EngagementId, "UNMODIFIED", null, null);
    Assert.True(opinion.Succeeded, opinion.Message);
    Assert.NotNull(await AuditDeliverableService.CurrentClearanceAsync(db, partner, w.EngagementId));
    Assert.NotNull(await AuditDeliverableService.CurrentOpinionAsync(db, partner, w.EngagementId));
    await SamplingReviewAsync(db, w, test, AuditItemTestReviewDecisions.ChangesRequired);
    Assert.Null(await AuditDeliverableService.CurrentClearanceAsync(db, partner, w.EngagementId));
    Assert.Null(await AuditDeliverableService.CurrentOpinionAsync(db, partner, w.EngagementId));
    Assert.Single(await db.PartnerCompletionClearances.ToListAsync());
    Assert.Single(await db.AuditOpinionDecisions.ToListAsync());
  }

  [Fact]
  public async Task SamplingConcurrentReviews_SerializeBeforeDecidingEffectiveEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w);
    var test = await SamplingTestAsync(db, w, sample.Items[0]);
    var firstBarrier = new SamplingReviewBarrier(holdInsert: true);
    var secondBarrier = new SamplingReviewBarrier(holdInsert: false);
    await using var firstDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(firstBarrier).Options);
    await using var secondDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(secondBarrier).Options);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var reviewer = w.A("manager", "Manager");
    var first = AuditFieldworkService.ReviewItemTestAsync(firstDb, reviewer, new(test, "REVIEWED", "Reviewed evidence"), deadline.Token);
    Task<CommandResult>? second = null;
    try
    {
      await firstBarrier.InsertReached.Task.WaitAsync(deadline.Token);
      second = AuditFieldworkService.ReviewItemTestAsync(secondDb, reviewer, new(test, "CHANGES_REQUIRED", "Later independent return"), deadline.Token);
      await secondBarrier.ScopeReached.Task.WaitAsync(deadline.Token);
      Assert.False(second.IsCompleted);
    }
    finally { firstBarrier.Release.TrySetResult(true); }
    Assert.True((await first.WaitAsync(deadline.Token)).Succeeded);
    Assert.True((await second!.WaitAsync(deadline.Token)).Succeeded);
    var reviews = await db.AuditItemTestReviews.AsNoTracking().OrderBy(x => x.ReviewSequence).ToListAsync();
    Assert.Equal(["REVIEWED", "CHANGES_REQUIRED"], reviews.Select(x => x.Decision));
    Assert.Equal([1L, 2L], reviews.Select(x => x.ReviewSequence!.Value));
    var view = await AuditSamplingService.GetSampleSetAsync(db, reviewer, sample.SelectionId);
    Assert.Equal("CHANGES_REQUIRED", Assert.Single(view.Value!.Items).TestReviewDecision);
    var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, reviewer, w.EngagementId);
    Assert.Contains("item-tests:1:unreviewed", completion.Value!.Blockers);
  }

  private sealed class SamplingReviewBarrier(bool holdInsert) : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> ScopeReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> InsertReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("client_safety_states", StringComparison.Ordinal) && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal))
        ScopeReached.TrySetResult(true);
      if (command.CommandText.Contains("INSERT INTO audit_item_test_reviews", StringComparison.Ordinal))
      {
        InsertReached.TrySetResult(true);
        if (holdInsert) await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }

  [Theory]
  [InlineData(AuditItemTestResults.Pass)]
  [InlineData(AuditItemTestResults.Exception)]
  public async Task SamplingRequiredReferences_RejectSemanticEmptinessAndRetainCanonicalPhysicalReferences(string result)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w);
    var maker = w.A("associate", "Staff");
    foreach (var references in new string[]?[] { null, [], [""], [" \t "] })
    {
      var rejected = await AuditFieldworkService.RecordItemTestAsync(db, maker,
        new(sample.Items[0], "Agreed sampled work", references!, result, result == "EXCEPTION" ? 1m : null, null, "Investigate exception"));
      Assert.False(rejected.Succeeded);
      Assert.Empty(await db.AuditItemTests.ToListAsync());
    }
    var accepted = await AuditFieldworkService.RecordItemTestAsync(db, maker,
      new(sample.Items[0], "Agreed sampled work", [" ", " Paper file B-12, page 3 ", ""], result,
        result == "EXCEPTION" ? 1m : null, null, "Investigate exception"));
    Assert.True(accepted.Succeeded, accepted.Message);
    Assert.Equal(["Paper file B-12, page 3"], JsonSerializer.Deserialize<string[]>((await db.AuditItemTests.SingleAsync()).EvidenceReferencesJson) ?? []);
    foreach (var optionalResult in new[] { "PENDING", "LIMITATION" })
      Assert.True((await AuditFieldworkService.RecordItemTestAsync(db, maker,
        new(sample.Items[0], "Record work and unavailable evidence", [], optionalResult, null, null, "Follow up missing evidence"))).Succeeded);
  }

  [Fact]
  public async Task SamplingCutoffReferences_RequireMeaningfulEvidenceBeforeAnyRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sample = await SamplingAsync(db, w);
    var maker = w.A("associate", "Staff");
    foreach (var references in new string[]?[] { null, [], [""], [" \t "] })
    {
      var rejected = await AuditSamplingService.RecordCutOffTestAsync(db, maker,
        new(sample.Items[0], new(2026, 12, 31), new(2026, 12, 20), null, null, "Reviewed cut-off", references!));
      Assert.False(rejected.Succeeded);
      Assert.Empty(await db.AuditCutOffTestRecords.ToListAsync());
    }
    var accepted = await AuditSamplingService.RecordCutOffTestAsync(db, maker,
      new(sample.Items[0], new(2026, 12, 31), new(2026, 12, 20), null, null, "Reviewed cut-off", ["", " Paper file B-12, page 3 "]));
    Assert.True(accepted.Succeeded, accepted.Message);
    Assert.Equal(["Paper file B-12, page 3"], JsonSerializer.Deserialize<string[]>((await db.AuditCutOffTestRecords.SingleAsync()).EvidenceReferencesJson) ?? []);
  }
}
