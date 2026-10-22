using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Text.Json;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PracticeTimeTests
{
  private sealed record Fixture(
    Guid FirmId,
    Guid ClientId,
    Guid EngagementId,
    Guid ReportingPeriodId,
    AppUser Preparer,
    AppUser Manager,
    AppUser Partner,
    ActorContext RateCardManagerActor,
    ActorContext PreparerActor,
    ActorContext ManagerActor,
    ActorContext PartnerActor);

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var reportingPeriodId = Guid.NewGuid();
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
    var rateCardManager = User(firmId, "FirmRateCardManager");
    db.Users.AddRange(preparer, manager, partner, rateCardManager);
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = reportingPeriodId, FirmId = firmId, ClientId = clientId, PeriodCode = "2026-12",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Basis = "IFRS",
      Currency = "QAR", CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "Staff", clientId, engagementId),
      Grant(firmId, manager, "Manager", clientId, engagementId),
      Grant(firmId, partner, "Partner"),
      Grant(firmId, rateCardManager, "Manager"));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, reportingPeriodId, preparer, manager, partner,
      Actor(rateCardManager, "Manager"), Actor(preparer, "Staff"), Actor(manager, "Manager"), Actor(partner, "Partner"));
  }

  [Fact]
  public async Task EngagementScopedManager_CannotReviseOrApproveFirmRateCards()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var unauthorizedDraft = await PracticeTimeService.ReviseRateCardAsync(db, fixture.ManagerActor,
      new RateCardDraftRequest("Staff", "Scoped", "QAR", 100m));
    Assert.False(unauthorizedDraft.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedDraft.ErrorCode);

    var draft = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
      new RateCardDraftRequest("Staff", "Scoped", "QAR", 100m));
    Assert.True(draft.Succeeded, draft.Message);
    var unauthorizedApproval = await PracticeTimeService.ApproveRateCardAsync(db, fixture.ManagerActor, draft.Value);
    Assert.False(unauthorizedApproval.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, unauthorizedApproval.ErrorCode);
    Assert.Equal(PracticeTimeStates.RateDraft,
      await db.RateCardVersions.Where(x => x.Id == draft.Value).Select(x => x.Status).SingleAsync());
  }

  [Fact]
  public async Task TaskDeadlineAndReportingPeriod_ArePersisted_AndCrossClientLinkIsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    Guid taskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Prepare reporting pack", ClientId: fixture.ClientId,
          EngagementId: fixture.EngagementId, ReportingPeriodId: fixture.ReportingPeriodId,
          DueDate: new DateOnly(2026, 11, 30)));
      Assert.True(created.Succeeded);
      taskId = created.Value;

      var mismatched = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Foreign period", ClientId: fixture.ClientId, ReportingPeriodId: foreign.ReportingPeriodId));
      Assert.False(mismatched.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, mismatched.ErrorCode);

      var missingClient = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Unscoped period", ReportingPeriodId: fixture.ReportingPeriodId));
      Assert.False(missingClient.Succeeded);
      Assert.Equal("time.invalid", missingClient.ErrorCode);

      var unscopedTask = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Unscoped task from engagement grant"));
      Assert.False(unscopedTask.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, unscopedTask.ErrorCode);

      var client = User(fixture.FirmId, "MisclassifiedClient");
      client.UserKind = "Client";
      db.Users.Add(client);
      db.RoleGrants.Add(Grant(fixture.FirmId, client, "Manager", fixture.ClientId, fixture.EngagementId));
      await db.SaveChangesAsync();
      var clientTask = await PracticeTimeService.CreateTaskAsync(db,
        new ActorContext(client.Id, fixture.FirmId, client.SessionEpoch, ["Manager"]),
        new CreateTaskRequest("Client user must not create internal task", fixture.ClientId, fixture.EngagementId));
      Assert.False(clientTask.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, clientTask.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var task = await db.WorkTasks.SingleAsync(x => x.Id == taskId);
      Assert.Equal(fixture.ReportingPeriodId, task.ReportingPeriodId);
      Assert.Equal(new DateOnly(2026, 11, 30), task.DueDate);
    }
  }

  [Trait("CompletionUnit", "WU-016")]
  [Fact]
  public async Task EngagementTaskPinsApprovedFsliMapping_AndTimeCorrectionRetainsItsLineage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid mappingId;
    Guid taskId;
    Guid originalId;
    Guid correctedId;
    Guid originalRateCardVersionId;
    ReviseTimeEntryRequest correctionRequest;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      mappingId = await SeedApprovedTimeMappingAsync(db, fixture);
      var refused = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Unmapped destination", fixture.ClientId, fixture.EngagementId,
          MappingVersionId: mappingId, FsliCode: "NOT_IN_MAPPING"));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);

      var created = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Map cash testing", fixture.ClientId, fixture.EngagementId,
          AssigneeUserId: fixture.Preparer.Id, MappingVersionId: mappingId, FsliCode: " Cash "));
      Assert.True(created.Succeeded, created.Message);
      taskId = created.Value;
      var task = await db.WorkTasks.SingleAsync(x => x.Id == taskId);
      Assert.Equal(mappingId, task.MappingVersionId);
      Assert.Equal("Cash", task.FsliCode);

      originalRateCardVersionId = await CreateApprovedRateAsync(db, fixture, "FSLI journey");
      var draft = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(taskId, new DateOnly(2026, 1, 12), 540, 60,
          "Staff", "FSLI journey", Currency: "QAR"));
      Assert.True(draft.Succeeded, draft.Message);
      originalId = draft.Value;
      var entry = await db.TimeEntries.SingleAsync(x => x.Id == originalId);
      Assert.Equal(mappingId, entry.MappingVersionId);
      Assert.Equal("Cash", entry.FsliCode);
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, fixture.PreparerActor, originalId)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, fixture.PartnerActor, originalId)).Succeeded);

      var revisedRate = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
        new RateCardDraftRequest("Staff", "FSLI journey", "QAR", 120m, ExpectedVersion: 1));
      Assert.True(revisedRate.Succeeded, revisedRate.Message);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor, revisedRate.Value)).Succeeded);

      correctionRequest = new ReviseTimeEntryRequest(originalId, Guid.NewGuid(), 1, "CORRECT_APPROVED",
        new DateOnly(2026, 1, 12), 540, 75, "FSLI journey", "Corrected duration", "Corrected recorded duration");
      var correction = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor, correctionRequest);
      Assert.True(correction.Succeeded, correction.Message);
      correctedId = correction.Value;

      var replay = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor, correctionRequest);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(correctedId, replay.Value);

      var changedIntent = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor,
        correctionRequest with { DurationMinutes = 80 });
      Assert.False(changedIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedIntent.ErrorCode);
      Assert.Equal(2, await db.TimeEntries.CountAsync());
      Assert.Equal(1, await db.Set<TimeEntryChangeReceipt>().CountAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var original = await db.TimeEntries.SingleAsync(x => x.Id == originalId);
      var correction = await db.TimeEntries.SingleAsync(x => x.Id == correctedId);
      Assert.Equal(PracticeTimeStates.TimeSuperseded, original.Status);
      Assert.Equal(1, original.Revision);
      Assert.Equal(mappingId, correction.MappingVersionId);
      Assert.Equal("Cash", correction.FsliCode);
      Assert.Equal(originalId, correction.SupersedesId);
      Assert.Equal(2, correction.Revision);
      Assert.Equal(originalRateCardVersionId, original.RateCardVersionId);
      Assert.Equal(originalRateCardVersionId, correction.RateCardVersionId);
      Assert.Equal(100m, correction.RatePerHour);
      Assert.Equal(2, await db.TimeEntries.CountAsync());
      var receipt = await db.Set<TimeEntryChangeReceipt>().SingleAsync(x => x.RequestId == correctionRequest.RequestId);
      Assert.Equal("CORRECT_APPROVED", receipt.Kind);
      Assert.Equal(originalId, receipt.TimeEntryId);
      Assert.Equal(correctedId, receipt.ResultTimeEntryId);
      Assert.Equal(1, receipt.SourceRevision);
      Assert.Equal(2, receipt.ResultRevision);
      var before = JsonSerializer.Deserialize<TimeEntry>(receipt.BeforeJson)!;
      var sourceAfter = JsonSerializer.Deserialize<TimeEntry>(receipt.SourceAfterJson)!;
      var result = JsonSerializer.Deserialize<TimeEntry>(receipt.ResultJson)!;
      Assert.Equal(PracticeTimeStates.TimeApproved, before.Status);
      Assert.Equal(mappingId, before.MappingVersionId);
      Assert.Equal(originalRateCardVersionId, before.RateCardVersionId);
      Assert.Equal(PracticeTimeStates.TimeSuperseded, sourceAfter.Status);
      Assert.Equal(PracticeTimeStates.TimeDraft, result.Status);
      Assert.Equal(correctedId, result.Id);
      Assert.Equal(mappingId, result.MappingVersionId);
      Assert.Equal(originalRateCardVersionId, result.RateCardVersionId);
      Assert.Equal(100m, result.RatePerHour);
    }
  }

  [Fact]
  public async Task PreparersCanRecordTimeOnlyAgainstAssignedTasksWhileManagersCanRouteUnassignedWork()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid unassignedTaskId;
    Guid assignedTaskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var unassigned = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Unassigned work", fixture.ClientId, fixture.EngagementId));
      Assert.True(unassigned.Succeeded, unassigned.Message);
      unassignedTaskId = unassigned.Value;

      var assigned = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Assigned work", fixture.ClientId, fixture.EngagementId,
          AssigneeUserId: fixture.Preparer.Id));
      Assert.True(assigned.Succeeded, assigned.Message);
      assignedTaskId = assigned.Value;

      var preparerWorkspace = await PracticeTimeWorkspaceQuery.GetAsync(db, fixture.PreparerActor);
      Assert.True(preparerWorkspace.Succeeded, preparerWorkspace.Message);
      Assert.Contains(assignedTaskId, preparerWorkspace.Value!.OpenTasks.Select(x => x.Id));
      Assert.DoesNotContain(unassignedTaskId, preparerWorkspace.Value.OpenTasks.Select(x => x.Id));

      var managerWorkspace = await PracticeTimeWorkspaceQuery.GetAsync(db, fixture.ManagerActor);
      Assert.True(managerWorkspace.Succeeded, managerWorkspace.Message);
      Assert.Contains(unassignedTaskId, managerWorkspace.Value!.OpenTasks.Select(x => x.Id));

      var rate = await CreateApprovedRateAsync(db, fixture, "Assigned task time");
      Assert.NotEqual(Guid.Empty, rate);
      var denied = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(unassignedTaskId, new DateOnly(2026, 1, 12), 540, 60,
          "Staff", "Assigned task time", Currency: "QAR"));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      Assert.False(await db.TimeEntries.AnyAsync(x => x.TaskId == unassignedTaskId));

      var accepted = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(assignedTaskId, new DateOnly(2026, 1, 12), 540, 60,
          "Staff", "Assigned task time", Currency: "QAR"));
      Assert.True(accepted.Succeeded, accepted.Message);
      Assert.True(await db.TimeEntries.AnyAsync(x => x.Id == accepted.Value && x.TaskId == assignedTaskId));
    }
  }

  private static async Task<Guid> SeedApprovedTimeMappingAsync(AuditSphereDbContext db, Fixture fixture)
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.NewGuid();
    var mappingId = Guid.NewGuid();
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      SourceKind = "Raw", Currency = "QAR", Balanced = true, ControlTotal = 0m, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Loading, NormalizedDatasetDigest = Hashing.Sha256Hex(datasetId.ToString()),
      ImportedAt = now, ImportedByUserId = fixture.Preparer.Id
    });
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == datasetId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      DatasetId = datasetId, TaxonomyVersion = "time-test-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = fixture.Preparer.Id,
      ApprovedByUserId = fixture.Manager.Id, ApprovedAt = now, CreatedAt = now
    });
    db.MappingAllocations.Add(new MappingAllocation
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      MappingVersionId = mappingId, SourceAccountCode = "1000", DestinationCode = "Cash",
      StatementSection = "ASSETS", Fraction = 1m, Rationale = "Synthetic approved mapping for time lineage", CreatedAt = now
    });
    await db.SaveChangesAsync();
    return mappingId;
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
  public async Task ReturnedTime_RetainsEachReviewRound_ReusesItsRateSnapshot_AndEntersBillingOnlyOnceAfterApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var finance = User(fixture.FirmId, "FinanceReviewer");
    var financeActor = Actor(finance, "FinanceManager");
    var workDate = new DateOnly(2026, 1, 10);
    Guid taskId;
    Guid originalRateCardVersionId;
    Guid revisedRateCardVersionId;
    Guid timeEntryId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(finance);
      await db.SaveChangesAsync();
      db.RoleGrants.Add(Grant(fixture.FirmId, finance, "FinanceManager"));
      await db.SaveChangesAsync();
      taskId = (await PracticeTimeService.CreateTaskAsync(db, fixture.ManagerActor,
        new CreateTaskRequest("Prepare schedules", fixture.ClientId, fixture.EngagementId))).Value;
      originalRateCardVersionId = await CreateApprovedRateAsync(db, fixture);
      timeEntryId = (await PracticeTimeService.SaveTimeDraftAsync(db, fixture.ManagerActor,
        new SaveTimeDraftRequest(taskId, workDate, 9 * 60, 60, "Staff", "Schedules", Currency: "QAR"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, fixture.ManagerActor, timeEntryId)).Succeeded);

      var selfReturn = await PracticeTimeService.ReturnTimeAsync(db, fixture.ManagerActor, timeEntryId, "Own entry");
      Assert.False(selfReturn.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, selfReturn.ErrorCode);

      var returned = await PracticeTimeService.ReturnTimeAsync(db, fixture.PartnerActor, timeEntryId,
        "Explain the source of the recorded hours.");
      Assert.True(returned.Succeeded, returned.Message);
      var record = await db.TimeEntries.SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(PracticeTimeStates.TimeReturned, record.Status);
      Assert.Equal("Explain the source of the recorded hours.", record.LastReturnReason);
      Assert.Equal(100m, record.RatePerHour);
      Assert.Equal(1, record.SubmissionRound);

      var returnedSnapshot = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      var editRequest = new ReviseTimeEntryRequest(timeEntryId, Guid.NewGuid(), returnedSnapshot.Revision,
        "EDIT_UNAPPROVED", workDate, 9 * 60, 75, "Schedules", "Clarified schedule preparation",
        "Corrected the recorded duration.");
      var edit = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.ManagerActor, editRequest);
      Assert.True(edit.Succeeded, edit.Message);
      Assert.Equal(timeEntryId, edit.Value);
      var edited = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(2, edited.Revision);
      Assert.Equal(PracticeTimeStates.TimeReturned, edited.Status);
      Assert.Equal(1, edited.SubmissionRound);
      Assert.Equal("Explain the source of the recorded hours.", edited.LastReturnReason);
      Assert.Equal(75, edited.DurationMinutes);
      Assert.Equal("Clarified schedule preparation", edited.Narrative);

      var editReceipt = await db.Set<TimeEntryChangeReceipt>().SingleAsync(x => x.RequestId == editRequest.RequestId);
      Assert.Equal("EDIT_UNAPPROVED", editReceipt.Kind);
      Assert.Equal(timeEntryId, editReceipt.TimeEntryId);
      Assert.Equal(timeEntryId, editReceipt.ResultTimeEntryId);
      Assert.Equal(returnedSnapshot.Revision, editReceipt.SourceRevision);
      Assert.Equal(edited.Revision, editReceipt.ResultRevision);
      var before = JsonSerializer.Deserialize<TimeEntry>(editReceipt.BeforeJson)!;
      var sourceAfter = JsonSerializer.Deserialize<TimeEntry>(editReceipt.SourceAfterJson)!;
      var result = JsonSerializer.Deserialize<TimeEntry>(editReceipt.ResultJson)!;
      Assert.Equal(returnedSnapshot.Id, before.Id);
      Assert.Equal(returnedSnapshot.Status, before.Status);
      Assert.Equal(returnedSnapshot.DurationMinutes, before.DurationMinutes);
      Assert.Equal(returnedSnapshot.LastReturnReason, before.LastReturnReason);
      Assert.Equal(edited.Id, sourceAfter.Id);
      Assert.Equal(edited.Revision, sourceAfter.Revision);
      Assert.Equal(edited.LastReturnReason, sourceAfter.LastReturnReason);
      Assert.Equal(edited.Id, result.Id);
      Assert.Equal(edited.DurationMinutes, result.DurationMinutes);
      Assert.Equal(edited.Narrative, result.Narrative);
      Assert.Equal(edited.LastReturnReason, result.LastReturnReason);
      var notYetBillable = await FirmUnbilledTimeQuery.GetAsync(db, financeActor,
        new FirmUnbilledTimeRequest(workDate, workDate));
      Assert.True(notYetBillable.Succeeded, notYetBillable.Message);
      Assert.Empty(notYetBillable.Value!.Rows);

      var revisedRate = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
        new RateCardDraftRequest("Staff", "Schedules", "QAR", 120m, ExpectedVersion: 1));
      Assert.True(revisedRate.Succeeded, revisedRate.Message);
      revisedRateCardVersionId = revisedRate.Value;
      Assert.NotEqual(originalRateCardVersionId, revisedRateCardVersionId);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor,
        revisedRateCardVersionId)).Succeeded);

      var retainedAfterRateRevision = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(originalRateCardVersionId, retainedAfterRateRevision.RateCardVersionId);
      Assert.Equal(100m, retainedAfterRateRevision.RatePerHour);
      Assert.Equal(2, retainedAfterRateRevision.Revision);
      Assert.Equal("Explain the source of the recorded hours.", retainedAfterRateRevision.LastReturnReason);

      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, fixture.ManagerActor, timeEntryId)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, fixture.PartnerActor, timeEntryId)).Succeeded);
      var approved = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(PracticeTimeStates.TimeApproved, approved.Status);
      Assert.Equal(2, approved.SubmissionRound);
      Assert.Equal(originalRateCardVersionId, approved.RateCardVersionId);
      Assert.Equal(100m, approved.RatePerHour);
      Assert.Equal(1, await db.TimeEntries.CountAsync(x => x.Id == timeEntryId));

      var laterEntry = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.ManagerActor,
        new SaveTimeDraftRequest(taskId, workDate.AddDays(1), 9 * 60, 60,
          "Staff", "Schedules", Currency: "QAR"));
      Assert.True(laterEntry.Succeeded, laterEntry.Message);
      var laterRecord = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == laterEntry.Value);
      Assert.Equal(revisedRateCardVersionId, laterRecord.RateCardVersionId);
      Assert.Equal(120m, laterRecord.RatePerHour);

      var history = await db.TimeEntryReviewEvents.AsNoTracking().Where(x => x.TimeEntryId == timeEntryId)
        .OrderBy(x => x.SubmissionRound).ThenBy(x => x.CreatedAt).ToListAsync();
      Assert.Equal(4, history.Count);
      Assert.Collection(history.Where(x => x.SubmissionRound == 1).OrderBy(x => x.CreatedAt),
        submitted => Assert.Equal(PracticeTimeStates.ReviewSubmitted, submitted.Action),
        returnedEvent =>
        {
          Assert.Equal(PracticeTimeStates.ReviewReturned, returnedEvent.Action);
          Assert.Equal("Explain the source of the recorded hours.", returnedEvent.Reason);
          Assert.Equal(fixture.Partner.Id, returnedEvent.ActorUserId);
        });
      Assert.Collection(history.Where(x => x.SubmissionRound == 2).OrderBy(x => x.CreatedAt),
        approvedSubmission => Assert.Equal(PracticeTimeStates.ReviewSubmitted, approvedSubmission.Action),
        approvedEvent =>
        {
          Assert.Equal(PracticeTimeStates.ReviewApproved, approvedEvent.Action);
          Assert.Equal(fixture.Partner.Id, approvedEvent.ActorUserId);
          Assert.Null(approvedEvent.Reason);
        });

      var billable = await FirmUnbilledTimeQuery.GetAsync(db, financeActor,
        new FirmUnbilledTimeRequest(workDate, workDate));
      Assert.True(billable.Succeeded, billable.Message);
      Assert.Single(billable.Value!.Rows);
      Assert.Equal(timeEntryId, billable.Value.Rows[0].TimeEntryId);

      var mutation = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE time_entry_review_events SET reason = 'tampered' WHERE id = {history[1].Id}"));
      Assert.Equal("55000", Assert.IsType<PostgresException>(mutation.GetBaseException()).SqlState);
      var migrations = db.Database.GetMigrations().ToArray();
      var returnWorkflow = Array.FindIndex(migrations,
        x => x.EndsWith("_TimeEntryReturnWorkflow", StringComparison.Ordinal));
      Assert.True(returnWorkflow > 0);
      var downgrade = await Assert.ThrowsAnyAsync<Exception>(() =>
        db.GetService<IMigrator>().MigrateAsync(migrations[returnWorkflow - 1]));
      Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade.GetBaseException()).SqlState);
    }
  }

  [Fact]
  public async Task TimeEntryRevisionRejectsStaleOverlapAndRevokedGrantWithoutWrites()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid timeEntryId;
    Guid overlappingEntryId;
    var workDate = new DateOnly(2026, 2, 10);
    const string returnReason = "Explain the source of the recorded hours.";
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var task = await PracticeTimeService.CreateTaskAsync(db, fixture.PreparerActor,
        new CreateTaskRequest("Revise returned time", fixture.ClientId, fixture.EngagementId,
          AssigneeUserId: fixture.Preparer.Id));
      Assert.True(task.Succeeded, task.Message);
      await CreateApprovedRateAsync(db, fixture);
      var first = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(task.Value, workDate, 9 * 60, 60, "Staff", "Schedules", Currency: "QAR"));
      Assert.True(first.Succeeded, first.Message);
      timeEntryId = first.Value;
      var other = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(task.Value, workDate, 10 * 60, 60, "Staff", "Schedules", Currency: "QAR"));
      Assert.True(other.Succeeded, other.Message);
      overlappingEntryId = other.Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, fixture.PreparerActor, timeEntryId)).Succeeded);
      Assert.True((await PracticeTimeService.ReturnTimeAsync(db, fixture.PartnerActor, timeEntryId, returnReason)).Succeeded);

      var acceptedRequest = new ReviseTimeEntryRequest(timeEntryId, Guid.NewGuid(), 1, "EDIT_UNAPPROVED",
        workDate, 8 * 60, 60, "Schedules", "Revised returned entry", "Corrected the start time.");
      var accepted = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor, acceptedRequest);
      Assert.True(accepted.Succeeded, accepted.Message);
      Assert.Equal(timeEntryId, accepted.Value);

      var stale = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor,
        acceptedRequest with { RequestId = Guid.NewGuid(), ExpectedRevision = 1, StartMinute = 7 * 60 });
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);

      var overlap = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor,
        acceptedRequest with { RequestId = Guid.NewGuid(), ExpectedRevision = 2, StartMinute = 9 * 60 + 30 });
      Assert.False(overlap.Succeeded);
      Assert.Equal("time.overlap", overlap.ErrorCode);
      Assert.Equal(2, await db.TimeEntries.CountAsync());
      Assert.Equal(1, await db.Set<TimeEntryChangeReceipt>().CountAsync());
      var current = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(8 * 60, current.StartMinute);
      Assert.Equal(60, current.DurationMinutes);
      Assert.Equal(2, current.Revision);
      Assert.Equal(PracticeTimeStates.TimeReturned, current.Status);
      Assert.Equal(returnReason, current.LastReturnReason);

      var receiptId = await db.Set<TimeEntryChangeReceipt>().Where(x => x.RequestId == acceptedRequest.RequestId)
        .Select(x => x.Id).SingleAsync();
      var receiptMutation = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE time_entry_change_receipts SET reason = 'tampered' WHERE id = {receiptId}"));
      Assert.Equal("23514", Assert.IsType<PostgresException>(receiptMutation.GetBaseException()).SqlState);
      var receiptDeletion = await Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM time_entry_change_receipts WHERE id = {receiptId}"));
      Assert.Equal("23514", Assert.IsType<PostgresException>(receiptDeletion.GetBaseException()).SqlState);
      Assert.Equal(1, await db.Set<TimeEntryChangeReceipt>().CountAsync());
      var overlappingEntry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == overlappingEntryId);
      Assert.Equal(10 * 60, overlappingEntry.StartMinute);
      Assert.Equal(60, overlappingEntry.DurationMinutes);
      Assert.Equal(PracticeTimeStates.TimeDraft, overlappingEntry.Status);
    }

    await using (var revoke = new AuditSphereDbContext(pg.Options))
    {
      var grants = await revoke.RoleGrants.Where(x => x.FirmId == fixture.FirmId &&
          x.UserId == fixture.Preparer.Id && x.Role == "Staff" && x.ClientId == fixture.ClientId &&
          x.EngagementId == fixture.EngagementId && x.RevokedAt == null)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
      Assert.Equal(1, grants);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await PracticeTimeService.ReviseTimeEntryAsync(db, fixture.PreparerActor,
        new ReviseTimeEntryRequest(timeEntryId, Guid.NewGuid(), 2, "EDIT_UNAPPROVED",
          workDate, 8 * 60, 45, "Schedules", "Revise after grant revocation", "Correction attempt."));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      Assert.Equal(2, await db.TimeEntries.CountAsync());
      Assert.Equal(1, await db.Set<TimeEntryChangeReceipt>().CountAsync());
      var retained = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == timeEntryId);
      Assert.Equal(8 * 60, retained.StartMinute);
      Assert.Equal(60, retained.DurationMinutes);
      Assert.Equal(2, retained.Revision);
      Assert.Equal(PracticeTimeStates.TimeReturned, retained.Status);
      Assert.Equal(returnReason, retained.LastReturnReason);
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
        new CreateTaskRequest("Review controls", fixture.ClientId, fixture.EngagementId,
          AssigneeUserId: fixture.Preparer.Id))).Value;
      await CreateApprovedRateAsync(db, fixture, "Controls");
      originalId = (await PracticeTimeService.SaveTimeDraftAsync(db, fixture.PreparerActor,
        new SaveTimeDraftRequest(taskId, new DateOnly(2026, 1, 11), 10 * 60, 60,
          "Staff", "Controls", Currency: "QAR"))).Value;
      await PracticeTimeService.SubmitTimeAsync(db, fixture.PreparerActor, originalId);
      await PracticeTimeService.ApproveTimeAsync(db, fixture.PartnerActor, originalId);
      var revisedRate = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
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

      var rate = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
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
        new CreateTaskRequest("Budget actual", fixture.ClientId, fixture.EngagementId,
          AssigneeUserId: fixture.Preparer.Id))).Value;
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
    var draft = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
      new RateCardDraftRequest("Staff", activity, "QAR", 100m));
    Assert.True(draft.Succeeded);
    Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor, draft.Value)).Succeeded);
    return draft.Value;
  }

  [Fact]
  public async Task RateCardWorkspace_ShowsTheApprovedRateAndPendingDraft_WithApprovalComputedForTheActor()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var first = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
      new RateCardDraftRequest("Staff", "Workspace", "QAR", 100m));
    Assert.True(first.Succeeded, first.Message);
    Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, fixture.PartnerActor, first.Value)).Succeeded);
    var second = await PracticeTimeService.ReviseRateCardAsync(db, fixture.RateCardManagerActor,
      new RateCardDraftRequest("Staff", "Workspace", "QAR", 110m, ExpectedVersion: 1));
    Assert.True(second.Succeeded, second.Message);

    // A different firm-wide approver sees the pending draft as approvable.
    var asPartner = await RateCardWorkspaceQuery.GetAsync(db, fixture.PartnerActor);
    Assert.True(asPartner.Succeeded, asPartner.Message);
    Assert.True(asPartner.Value!.CanRevise);
    var partnerSlot = Assert.Single(asPartner.Value.Slots, x => x.Activity == "Workspace");
    Assert.Equal(1, partnerSlot.Approved!.Version);
    Assert.Equal(100m, partnerSlot.Approved.RatePerHour);
    Assert.Equal(2, partnerSlot.Draft!.Version);
    Assert.True(partnerSlot.Draft.CanApprove);

    // The preparer of the draft is shown the same draft as not approvable by them.
    var asPreparer = await RateCardWorkspaceQuery.GetAsync(db, fixture.RateCardManagerActor);
    Assert.False(Assert.Single(asPreparer.Value!.Slots, x => x.Activity == "Workspace").Draft!.CanApprove);

    // Staff without firm-wide rate authority are refused for the whole workspace.
    var refused = await RateCardWorkspaceQuery.GetAsync(db, fixture.PreparerActor);
    Assert.False(refused.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, refused.ErrorCode);
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
