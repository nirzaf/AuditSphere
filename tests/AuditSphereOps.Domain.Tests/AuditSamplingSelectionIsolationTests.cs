using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
