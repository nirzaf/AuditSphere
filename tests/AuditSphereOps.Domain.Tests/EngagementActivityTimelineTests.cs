using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class EngagementActivityTimelineTests
{
  private const string PrivateTranscript = "private activity transcript 09";
  private const string PrivateCapability = "private activity capability hash 09";
  private const string PrivatePath = "/private/activity/staging/path";
  private const string PrivateReviewText = "private activity review text 09";

  [Fact]
  public async Task SevenSourcesUseStableRankAndIdPagingSnapshotAndReadOnlyProjection()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var pbcHighId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    var pbcLowId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    var uploadId = Guid.NewGuid();
    var stagedPathUploadId = Guid.NewGuid();
    var procedureId = Guid.NewGuid();
    var resultId = Guid.NewGuid();
    var noteId = Guid.NewGuid();
    var noteEventId = Guid.NewGuid();
    var siblingProcedureId = Guid.NewGuid();
    var siblingResultId = Guid.NewGuid();
    var siblingNoteId = Guid.NewGuid();
    var siblingEventId = Guid.NewGuid();
    var correspondenceId = Guid.NewGuid();
    var deliverableId = Guid.NewGuid();
    var notificationId = Guid.NewGuid();
    var requestId = Guid.NewGuid();
    var contactId = Guid.NewGuid();
    Guid workflowActivityId;
    DateTimeOffset occurredAt;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId));
      db.Engagements.Add(new Engagement
      {
        Id = siblingEngagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var task = await EngagementWorkflowTaskService.CreateAsync(db, PbcSeed.Actor(manager, "Manager"),
        seed.EngagementId, new WorkflowTaskCreateRequest("Activity timeline fixture", WorkflowTaskPhases.AuditPlanning,
          1, IdempotencyKey: Guid.NewGuid()));
      Assert.True(task.Succeeded, task.Message);
      var workflowActivity = await db.WorkflowTaskActivities.SingleAsync(x => x.WorkTaskId == task.Value);
      workflowActivityId = workflowActivity.Id;
      occurredAt = workflowActivity.OccurredAt;

      db.PbcRequests.Add(new PbcRequest
      {
        Id = requestId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        Objective = "Activity timeline fixture", EntityScope = "Test entity", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Area = "Cash", RequestedFormat = "PDF", ControlTotals = "12 months",
        ClientOwnerUserId = seed.Client.Id, FirmOwnerUserId = manager.Id, ReviewerUserId = seed.Reviewer.Id,
        DueDate = "2026-12-31", Confidentiality = "Confidential", AcceptanceCriteria = "Readable source evidence.",
        State = PbcStates.Sent, Revision = 1, CreatedAt = occurredAt, CreatedByUserId = manager.Id,
        UpdatedAt = occurredAt
      });
      PbcCommunication Communication(Guid id) => new()
      {
        Id = id, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        PbcRequestId = requestId, AuthorUserId = manager.Id, Kind = PbcCommunicationKinds.Email,
        Body = PrivateTranscript, RecipientEmail = "client@example.test", Subject = "Recorded PBC request",
        PortalUrl = PrivateCapability, DeliveryState = PbcDeliveryStates.Sent, CreatedAt = occurredAt
      };
      db.PbcCommunications.AddRange(Communication(pbcHighId), Communication(pbcLowId));
      db.PbcUploadIntents.Add(new PbcUploadIntent
      {
        Id = uploadId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        PbcRequestId = requestId, UploaderUserId = seed.Client.Id, FileName = "source.pdf",
        ContentType = "application/pdf", DeclaredByteCount = 1, DeclaredSha256Hex = new string('a', 64),
        CapabilityHash = new string('b', 64), State = PbcUploadStates.Started, ExpiresAt = occurredAt.AddDays(1),
        CreatedAt = occurredAt, Revision = 1
      });
      var oldUploadCreatedAt = occurredAt.AddDays(-120);
      db.PbcUploadIntents.Add(new PbcUploadIntent
      {
        Id = stagedPathUploadId, FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = seed.EngagementId, PbcRequestId = requestId, UploaderUserId = seed.Client.Id,
        FileName = "older-source.pdf", ContentType = "application/pdf", DeclaredByteCount = 1,
        DeclaredSha256Hex = new string('a', 64), CapabilityHash = new string('b', 64),
        ReceivedByteCount = 1, State = PbcUploadStates.Chunking, ExpiresAt = oldUploadCreatedAt.AddDays(1),
        CreatedAt = oldUploadCreatedAt, Revision = 1
      });
      db.PbcUploadChunks.Add(new PbcUploadChunk
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = seed.EngagementId, PbcUploadIntentId = stagedPathUploadId, ChunkIndex = 0,
        Offset = 0, ByteCount = 1, Sha256Hex = new string('a', 64), StagedPath = PrivatePath,
        ReceivedAt = occurredAt
      });

      db.AuditProcedures.AddRange(
        new AuditProcedure
        {
          Id = procedureId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
          SourceProcedureId = "ACT-09", Title = "Activity review fixture",
          ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Submitted,
          CurrentResultRevision = 1, CreatedAt = occurredAt
        },
        new AuditProcedure
        {
          Id = siblingProcedureId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = siblingEngagementId,
          SourceProcedureId = "ACT-09-SIBLING", Title = "Sibling activity review fixture",
          ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Submitted,
          CurrentResultRevision = 1, CreatedAt = occurredAt
        });
      db.AuditProcedureResults.AddRange(
        new AuditProcedureResult
        {
          Id = resultId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
          AuditProcedureId = procedureId, Revision = 1, InputGeneration = 1, WorkPerformed = PrivateReviewText,
          StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Private conclusion",
          Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = manager.Id, SubmittedAt = occurredAt
        },
        new AuditProcedureResult
        {
          Id = siblingResultId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = siblingEngagementId,
          AuditProcedureId = siblingProcedureId, Revision = 1, InputGeneration = 1, WorkPerformed = "Sibling work",
          StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Sibling conclusion",
          Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = manager.Id, SubmittedAt = occurredAt
        });
      db.ProcedureReviewNotes.AddRange(
        new ProcedureReviewNote
        {
          Id = noteId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
          ProcedureId = procedureId, ResultId = resultId, ResultRevision = 1, Field = ReviewNoteFields.WorkPerformed,
          Excerpt = "Private excerpt", StartOffset = 0, Body = PrivateReviewText,
          AuthorUserId = manager.Id, CreatedAt = occurredAt
        },
        new ProcedureReviewNote
        {
          Id = siblingNoteId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = siblingEngagementId,
          ProcedureId = siblingProcedureId, ResultId = siblingResultId, ResultRevision = 1,
          Field = ReviewNoteFields.WorkPerformed, Excerpt = "Sibling excerpt", StartOffset = 0,
          Body = "Sibling private note", AuthorUserId = manager.Id, CreatedAt = occurredAt
        });
      db.ProcedureReviewNoteEvents.AddRange(
        new ProcedureReviewNoteEvent
        {
          Id = noteEventId, FirmId = seed.FirmId, NoteId = noteId, Kind = ReviewNoteEventKinds.Response,
          Body = PrivateReviewText, AuthorUserId = manager.Id, CreatedAt = occurredAt
        },
        new ProcedureReviewNoteEvent
        {
          Id = siblingEventId, FirmId = seed.FirmId, NoteId = siblingNoteId, Kind = ReviewNoteEventKinds.Response,
          Body = "Sibling event must stay out of this timeline", AuthorUserId = manager.Id, CreatedAt = occurredAt
        });

      db.ClientContacts.Add(new ClientContact
      {
        Id = contactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        FullName = "Activity Contact", Email = "activity-contact@example.test", Role = "Finance", Primary = true,
        IsActive = true
      });
      db.CorrespondenceDispatchRecords.Add(new CorrespondenceDispatchRecord
      {
        Id = correspondenceId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        EngagementId = seed.EngagementId, Purpose = CorrespondencePurposes.Completion,
        RecipientContactId = contactId, RecipientName = "Activity Contact", RecipientEmail = "activity-contact@example.test",
        DocumentType = "MANAGEMENT_LETTER", DocumentReference = "ML-09", DocumentRevision = 1,
        DocumentSha256 = new string('c', 64), DispatchedByUserId = manager.Id, DispatchedAt = occurredAt
      });
      db.AuditDeliverables.Add(new AuditDeliverable
      {
        Id = deliverableId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        Kind = DeliverableKinds.ManagementLetter, Version = 1, TemplateVersion = "activity-test",
        InputDigest = new string('d', 64), InputSummaryJson = "{}", FileName = "management-letter.pdf",
        ContentType = "application/pdf", Content = [0x25], ContentSha256 = new string('e', 64),
        CreatedByUserId = manager.Id, CreatedAt = occurredAt
      });
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = notificationId, FirmId = seed.FirmId, Kind = CommercialNotificationKinds.HoldingLetter,
        DeliverableId = deliverableId, DispatchKey = new string('f', 64), Recipient = "client@example.test",
        Subject = "Recorded management letter", Body = PrivateTranscript,
        DeliveryState = CommercialDeliveryStates.Queued, CreatedAt = occurredAt
      });
      await db.SaveChangesAsync();
    }

    var from = DateOnly.FromDateTime(occurredAt.UtcDateTime);
    var to = from;
    var expected = new (int Source, Guid Id)[]
    {
      (1, pbcHighId), (1, pbcLowId), (2, uploadId), (3, workflowActivityId), (4, noteId),
      (5, noteEventId), (6, correspondenceId), (7, notificationId)
    };
    var all = new List<EngagementActivityItem>();
    EngagementActivityRequest request = new(from, to, PageSize: 2);
    DateTimeOffset? firstSnapshot = null;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var page = await EngagementCommunicationTimelineQuery.GetActivityAsync(db,
        PbcSeed.Actor(manager, "Manager"), seed.EngagementId, request);
      Assert.True(page.Succeeded, page.Message);
      firstSnapshot = page.Value!.SnapshotAtUtc;
      all.AddRange(page.Value.Items);
      Assert.Equal(2, page.Value.Items.Count);

      var insertedAfterSnapshotId = Guid.NewGuid();
      db.PbcCommunications.Add(new PbcCommunication
      {
        Id = insertedAfterSnapshotId, FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = seed.EngagementId, PbcRequestId = requestId, AuthorUserId = manager.Id,
        Kind = PbcCommunicationKinds.Email, Body = "new event after snapshot", RecipientEmail = "client@example.test",
        Subject = "Post-snapshot event", PortalUrl = PrivateCapability, DeliveryState = PbcDeliveryStates.Sent,
        CreatedAt = firstSnapshot!.Value.AddMilliseconds(10)
      });
      await db.SaveChangesAsync();
      var baseline = await ReadSourceStateAsync(db, seed.EngagementId);

      while (page.Value!.HasMore)
      {
        var cursor = page.Value.NextCursor!;
        request = request with
        {
          SnapshotAtUtc = firstSnapshot,
          AfterAtUtc = cursor.AtUtc,
          AfterSource = cursor.Source,
          AfterId = cursor.Id
        };
        page = await EngagementCommunicationTimelineQuery.GetActivityAsync(db,
          PbcSeed.Actor(manager, "Manager"), seed.EngagementId, request);
        Assert.True(page.Succeeded, page.Message);
        Assert.Equal(firstSnapshot, page.Value!.SnapshotAtUtc);
        all.AddRange(page.Value.Items);
      }

      Assert.Equal(expected, all.Select(x => (x.Source, x.Id)));
      Assert.DoesNotContain(all, x => x.Id == insertedAfterSnapshotId);
      Assert.Equal("PROVIDER_ACCEPTED", all.Single(x => x.Id == pbcHighId).CurrentStatus);
      Assert.Equal("STARTED", all.Single(x => x.Id == uploadId).CurrentStatus);
      Assert.DoesNotContain("accepted", all.Single(x => x.Id == uploadId).Label, StringComparison.OrdinalIgnoreCase);
      Assert.Equal("RECORDED", all.Single(x => x.Id == correspondenceId).CurrentStatus);
      Assert.Equal("Review note response", all.Single(x => x.Id == noteEventId).Label);
      Assert.DoesNotContain(all, x => x.Id == siblingEventId);
      Assert.All(all, x => Assert.StartsWith("/app/", x.SourceHref, StringComparison.Ordinal));
      var serialized = JsonSerializer.Serialize(all);
      Assert.DoesNotContain(PrivateTranscript, serialized, StringComparison.Ordinal);
      Assert.DoesNotContain(PrivateCapability, serialized, StringComparison.Ordinal);
      Assert.DoesNotContain(PrivatePath, serialized, StringComparison.Ordinal);
      Assert.DoesNotContain(PrivateReviewText, serialized, StringComparison.Ordinal);

      // A cursor-free refresh takes a new snapshot and can include an event recorded after the first snapshot.
      await Task.Delay(15);
      var refreshed = await EngagementCommunicationTimelineQuery.GetActivityAsync(db,
        PbcSeed.Actor(manager, "Manager"), seed.EngagementId,
        new EngagementActivityRequest(from, to, PageSize: 50));
      Assert.True(refreshed.Succeeded, refreshed.Message);
      Assert.True(refreshed.Value!.SnapshotAtUtc >= firstSnapshot);
      Assert.Contains(refreshed.Value.Items, x => x.Id == insertedAfterSnapshotId);
      Assert.Equal(baseline, await ReadSourceStateAsync(db, seed.EngagementId));
    }
  }

  [Fact]
  public async Task InvalidRequestsAndNonManagerScopesFailClosedWithStableMessages()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    var senior = PbcSeed.User(seed.FirmId, "Staff");
    var siblingEngagementId = Guid.NewGuid();
    var foreignClientId = Guid.NewGuid();
    var foreignEngagementId = Guid.NewGuid();
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(manager, senior);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId),
      PbcSeed.Grant(seed.FirmId, senior, "Senior", seed.ClientId, seed.EngagementId));
    db.Engagements.Add(new Engagement
    {
      Id = siblingEngagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
    });
    db.PracticeClients.Add(new PracticeClient
    {
      Id = foreignClientId, FirmId = seed.FirmId, LegalName = "Foreign activity client", CreatedAt = now
    });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = foreignClientId, FirmId = seed.FirmId });
    db.Engagements.Add(new Engagement
    {
      Id = foreignEngagementId, FirmId = seed.FirmId, PracticeClientId = foreignClientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
    });
    await db.SaveChangesAsync();

    var actor = PbcSeed.Actor(manager, "Manager");
    var today = DateOnly.FromDateTime(now.UtcDateTime);
    var unknown = await EngagementCommunicationTimelineQuery.GetActivityAsync(db, actor, Guid.NewGuid(), new());
    var sibling = await EngagementCommunicationTimelineQuery.GetActivityAsync(db, actor, siblingEngagementId, new());
    var foreign = await EngagementCommunicationTimelineQuery.GetActivityAsync(db, actor, foreignEngagementId, new());
    Assert.Equal(ErrorCodes.ScopeDenied, unknown.ErrorCode);
    Assert.Equal((unknown.ErrorCode, unknown.Message), (sibling.ErrorCode, sibling.Message));
    Assert.Equal((unknown.ErrorCode, unknown.Message), (foreign.ErrorCode, foreign.Message));
    Assert.Equal("Access denied.", unknown.Message);

    foreach (var deniedActor in new[]
      {
        PbcSeed.Actor(seed.Staff, "Staff"), PbcSeed.Actor(senior, "Senior"), PbcSeed.Actor(seed.Client, "ClientUser")
      })
    {
      var denied = await EngagementCommunicationTimelineQuery.GetActivityAsync(db, deniedActor, seed.EngagementId, new());
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      Assert.Equal("Access denied.", denied.Message);
    }

    async Task AssertInvalid(EngagementActivityRequest invalid, string code, string message)
    {
      var result = await EngagementCommunicationTimelineQuery.GetActivityAsync(db, actor, seed.EngagementId, invalid);
      Assert.False(result.Succeeded);
      Assert.Equal(code, result.ErrorCode);
      Assert.Equal(message, result.Message);
    }

    const string cursorMessage = "Supply a complete activity cursor and a page size from 1 to 100.";
    const string rangeMessage = "Choose a UTC date range no longer than 90 days.";
    await AssertInvalid(new EngagementActivityRequest(AfterAtUtc: now), "activity.cursor-invalid", cursorMessage);
    await AssertInvalid(new EngagementActivityRequest(SnapshotAtUtc: now, AfterAtUtc: now,
      AfterSource: 8, AfterId: Guid.NewGuid()), "activity.cursor-invalid", cursorMessage);
    await AssertInvalid(new EngagementActivityRequest(PageSize: 101), "activity.cursor-invalid", cursorMessage);
    await AssertInvalid(new EngagementActivityRequest(SnapshotAtUtc: now.AddMinutes(1)),
      "activity.cursor-invalid", cursorMessage);
    await AssertInvalid(new EngagementActivityRequest(today.AddDays(-90), today), "activity.range-invalid", rangeMessage);
  }

  private static async Task<string> ReadSourceStateAsync(AuditSphereDbContext db, Guid engagementId)
  {
    var state = new
    {
      Communications = await db.PbcCommunications.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.CreatedAt, x.DeliveryState, x.Body, x.PortalUrl }).ToArrayAsync(),
      Uploads = await db.PbcUploadIntents.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.Revision, x.CapabilityHash, x.DeclaredSha256Hex, x.State }).ToArrayAsync(),
      UploadChunks = await db.PbcUploadChunks.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.Sha256Hex, x.StagedPath }).ToArrayAsync(),
      Workflow = await db.WorkflowTaskActivities.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.OccurredAt, x.Kind }).ToArrayAsync(),
      Tasks = await db.WorkTasks.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CompletedAt }).ToArrayAsync(),
      Notes = await db.ProcedureReviewNotes.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.Body, x.CreatedAt }).ToArrayAsync(),
      NoteEvents = await db.ProcedureReviewNoteEvents.Join(db.ProcedureReviewNotes,
        e => new { e.FirmId, Id = e.NoteId }, n => new { n.FirmId, Id = n.Id },
        (e, n) => new { Event = e, Note = n }).Where(x => x.Note.EngagementId == engagementId)
        .OrderBy(x => x.Event.Id).Select(x => new { x.Event.Id, x.Event.Body, x.Event.CreatedAt }).ToArrayAsync(),
      Correspondence = await db.CorrespondenceDispatchRecords.Where(x => x.EngagementId == engagementId)
        .OrderBy(x => x.Id).Select(x => new { x.Id, x.DispatchedAt, x.DocumentSha256 }).ToArrayAsync(),
      Commercial = await db.CommercialNotifications.Where(x => x.DeliverableId != null)
        .Join(db.AuditDeliverables, n => new { n.FirmId, Id = n.DeliverableId!.Value },
          d => new { d.FirmId, d.Id }, (n, d) => new { Notification = n, Deliverable = d })
        .Where(x => x.Deliverable.EngagementId == engagementId).OrderBy(x => x.Notification.Id)
        .Select(x => new { x.Notification.Id, x.Notification.DeliveryState, x.Notification.Body, x.Deliverable.ContentSha256 }).ToArrayAsync(),
      DurableOperations = await db.DurableOperations.CountAsync(x => x.EngagementId == engagementId),
      ProcedureReviews = await db.AuditProcedureReviews.CountAsync(x => x.EngagementId == engagementId),
      MaterialityApprovals = await db.MaterialityApprovals.CountAsync(x => x.EngagementId == engagementId)
    };
    return JsonSerializer.Serialize(state);
  }
}
