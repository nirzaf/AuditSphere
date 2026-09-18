using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PracticeTimeTests
{
  private sealed record Fixture(
    Guid FirmId,
    Guid ClientId,
    Guid EngagementId,
    AppUser Preparer,
    AppUser Manager,
    AppUser Partner,
    ActorContext PreparerActor,
    ActorContext ManagerActor,
    ActorContext PartnerActor);

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "Time Client", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ServiceRoute = "AccountingOnly", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      ServiceProfileId = "test", CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    var preparer = User(firmId, "Preparer");
    var manager = User(firmId, "Manager");
    var partner = User(firmId, "Partner");
    db.Users.AddRange(preparer, manager, partner);
    await db.SaveChangesAsync();
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "Staff", clientId, engagementId),
      Grant(firmId, manager, "Manager", clientId, engagementId),
      Grant(firmId, partner, "Partner"));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, preparer, manager, partner,
      Actor(preparer, "Staff"), Actor(manager, "Manager"), Actor(partner, "Partner"));
  }

  [Fact]
  public async Task TimeWorkflow_RequiresReview_UsesIntegerMinutes_AndRejectsOverlap()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      taskId = (await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Prepare schedules", fixture.ClientId, fixture.EngagementId))).Value;
      var card = await CreateApprovedRateAsync(db, fixture);
      Assert.NotEqual(Guid.Empty, card);
      var entry = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.ManagerActor,
        new SaveTimeDraftRequest(taskId, new DateOnly(2026, 1, 10), 9 * 60, 60,
          "Staff", "Schedules", Currency: "QAR"));
      Assert.True(entry.Succeeded);
      var overlap = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.ManagerActor,
        new SaveTimeDraftRequest(taskId, new DateOnly(2026, 1, 10), 9 * 60 + 30, 30,
          "Staff", "Schedules", Currency: "QAR"));
      Assert.False(overlap.Succeeded);
      Assert.Equal("time.overlap", overlap.ErrorCode);
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, fixture.ManagerActor, entry.Value)).Succeeded);
      var selfApprove = await PracticeTimeService.ApproveTimeAsync(db, fixture.ManagerActor, entry.Value);
      Assert.False(selfApprove.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, selfApprove.ErrorCode);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, fixture.PartnerActor, entry.Value)).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var entry = await db.TimeEntries.SingleAsync(x => x.TaskId == taskId);
      Assert.Equal(60, entry.DurationMinutes);
      Assert.Equal(PracticeTimeStates.TimeApproved, entry.Status);
      Assert.Equal(100m, entry.RatePerHour);
      Assert.Equal("QAR", entry.Currency);
      Assert.Equal(1, entry.Revision);
    }
  }

  [Fact]
  public async Task Correction_SupersedesApprovedFact_AndRetainsHistoricalRate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid taskId, originalId, correctedId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      taskId = (await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Review controls", fixture.ClientId, fixture.EngagementId))).Value;
      await CreateApprovedRateAsync(db, fixture, "Controls");
      originalId = (await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(taskId, new DateOnly(2026, 1, 11), 10 * 60, 60,
          "Staff", "Controls", Currency: "QAR"))).Value;
      await PracticeTimeService.SubmitTimeAsync(db, fixture.PreparerActor, originalId);
      await PracticeTimeService.ApproveTimeAsync(db, fixture.PartnerActor, originalId);
      var revisedRate = await PracticeTimeService.ReviseRateCardAsync(db, fixture.ManagerActor,
        new RateCardDraftRequest("Staff", "Controls", "QAR", 120m, ExpectedVersion: 1));
      Assert.True(revisedRate.Succeeded);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor, revisedRate.Value)).Succeeded);
      var correction = await PracticeTimeService.CorrectTimeAsync(db, fixture.PreparerActor,
        new CorrectTimeRequest(originalId, new DateOnly(2026, 1, 11), 10 * 60, 90,
          "Controls", "Corrected duration", Reason: "Forgot walkthrough"));
      Assert.True(correction.Succeeded);
      correctedId = correction.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var original = await db.TimeEntries.SingleAsync(x => x.Id == originalId);
      var correction = await db.TimeEntries.SingleAsync(x => x.Id == correctedId);
      Assert.Equal(60, original.DurationMinutes);
      Assert.Equal(PracticeTimeStates.TimeSuperseded, original.Status);
      Assert.Equal(90, correction.DurationMinutes);
      Assert.Equal(2, correction.Revision);
      Assert.Equal(original.RateCardVersionId, correction.RateCardVersionId);
      Assert.Equal(100m, correction.RatePerHour);
      Assert.Equal(originalId, correction.SupersedesId);
      var repeat = await PracticeTimeService.CorrectTimeAsync(db, fixture.PreparerActor,
        new CorrectTimeRequest(originalId, new DateOnly(2026, 1, 11), 10 * 60, 80, "Controls", "Again"));
      Assert.False(repeat.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, repeat.ErrorCode);
    }
  }

  [Fact]
  public async Task BudgetVersions_SnapshotApprovedRates_AndRejectSelfApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid firstBudgetId, secondBudgetId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await CreateApprovedRateAsync(db, fixture);
      var first = await PracticeTimeService.ReviseBudgetAsync(db, fixture.ManagerActor,
        new ReviseBudgetRequest(fixture.EngagementId, "QAR",
          [new BudgetLineRequest("Staff", "Schedules", 120)]));
      Assert.True(first.Succeeded);
      firstBudgetId = first.Value;
      var self = await PracticeTimeService.ApproveBudgetAsync(db, fixture.ManagerActor, firstBudgetId);
      Assert.False(self.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, self.ErrorCode);
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, fixture.PartnerActor, firstBudgetId)).Succeeded);

      var rate = await PracticeTimeService.ReviseRateCardAsync(db, fixture.ManagerActor,
        new RateCardDraftRequest("Staff", "Schedules", "QAR", 120m, ExpectedVersion: 1));
      Assert.True(rate.Succeeded);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor, rate.Value)).Succeeded);
      var second = await PracticeTimeService.ReviseBudgetAsync(db, fixture.ManagerActor,
        new ReviseBudgetRequest(fixture.EngagementId, "QAR",
          [new BudgetLineRequest("Staff", "Schedules", 60)], ExpectedVersion: 1));
      Assert.True(second.Succeeded);
      secondBudgetId = second.Value;
      var lines = await db.BudgetLines.Where(x => x.EngagementBudgetId == secondBudgetId).ToListAsync();
      Assert.Single(lines);
      Assert.Equal(120m, lines[0].RatePerHour);
      Assert.Equal(120m, lines[0].ForecastCost);
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, fixture.PartnerActor, secondBudgetId)).Succeeded);

      var task = (await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Budget actual", fixture.ClientId, fixture.EngagementId))).Value;
      var time = (await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(task, new DateOnly(2026, 1, 15), 8 * 60, 60,
          "Staff", "Schedules", Currency: "QAR"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, fixture.PreparerActor, time)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, fixture.PartnerActor, time)).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(PracticeTimeStates.BudgetSuperseded,
        (await db.EngagementBudgets.SingleAsync(x => x.Id == firstBudgetId)).Status);
      var summary = await PracticeTimeService.GetBudgetActualAsync(db, fixture.PreparerActor, fixture.EngagementId);
      Assert.True(summary.Succeeded);
      Assert.Equal(secondBudgetId, summary.Value!.BudgetId);
      Assert.Equal(60, summary.Value.ForecastMinutes);
      Assert.Equal(120m, summary.Value.ForecastCost);
      Assert.Equal(60, summary.Value.ActualMinutes);
      Assert.Equal(120m, summary.Value.ActualCost);
    }
  }

  [Fact]
  public async Task TaskAssignment_AndCrossFirmScope_AreEnforced()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      taskId = (await PracticeTimeService.CreateTaskAsync(db, fixture.ManagerActor,
        new CreateTaskRequest("Assign evidence", fixture.ClientId, fixture.EngagementId))).Value;
      Assert.True((await PracticeTimeService.AssignTaskAsync(db, fixture.ManagerActor, taskId,
        fixture.Preparer.Id)).Succeeded);
      var alreadyAssigned = await PracticeTimeService.AssignTaskAsync(db, fixture.ManagerActor, taskId,
        fixture.Preparer.Id);
      Assert.False(alreadyAssigned.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, alreadyAssigned.ErrorCode);
      Assert.True((await PracticeTimeService.CompleteTaskAsync(db, fixture.PreparerActor, taskId)).Succeeded);
    }

    var foreign = await SeedAsync(pg);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await PracticeTimeService.SaveTimeDraftAsync(db, foreign.PreparerActor,
        new SaveTimeDraftRequest(taskId, new DateOnly(2026, 1, 12), 8 * 60, 30,
          "Staff", "Evidence", Currency: "QAR"));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  [Fact]
  public async Task DatabaseRejectsInvalidTimeShape()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var task = new WorkTask
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, Title = "Invalid fixture", CreatedAt = DateTimeOffset.UtcNow
    };
    db.WorkTasks.Add(task);
    await db.SaveChangesAsync();
    db.TimeEntries.Add(new TimeEntry
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, TaskId = task.Id, UserId = fixture.Preparer.Id,
      WorkDate = new DateOnly(2026, 1, 12), StartMinute = 1430, DurationMinutes = 30,
      Role = "Staff", Activity = "Invalid", BillableClassification = PracticeTimeStates.NonBillable,
      NarrativeVisibility = PracticeTimeStates.NarrativeInternal, Currency = null,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }

  private static async Task<Guid> CreateApprovedRateAsync(
    AuditSphereDbContext db, Fixture fixture, string activity = "Schedules")
  {
    var draft = await PracticeTimeService.ReviseRateCardAsync(db, fixture.ManagerActor,
      new RateCardDraftRequest("Staff", activity, "QAR", 100m));
    Assert.True(draft.Succeeded);
    Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor, draft.Value)).Succeeded);
    return draft.Value;
  }

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId,
    Subject = $"sub-{label}-{Guid.NewGuid():N}", TenantId = "tenant-test",
    Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
    DisplayName = label, UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role,
    Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = clientId, EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);
}
