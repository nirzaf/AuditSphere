using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// PM-07 first slice: the engagement communication timeline shows only what was recorded for that engagement, words each
/// status as the recorded fact, and is closed to staff outside engagement management and to sibling engagements.
/// </summary>
public sealed class EngagementCommunicationTimelineTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task TimelineListsOnlyThisEngagementsRecordedMailAndEnforcesScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var siblingId = Guid.NewGuid();
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    var siblingManager = PbcSeed.User(seed.FirmId, "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(manager, siblingManager);
    db.Engagements.Add(new Engagement
    {
      Id = siblingId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, Status = "Active",
      ProfessionalWorkBlocked = false, CreatedAt = now
    });
    db.RoleGrants.AddRange(
      PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId),
      PbcSeed.Grant(seed.FirmId, siblingManager, "Manager", seed.ClientId, siblingId));
    CorrespondenceDispatchRecord Routed(Guid engagementId, string reference, DateTimeOffset at) => new()
    {
      Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, EngagementId = engagementId,
      Purpose = CorrespondencePurposes.Commercial, RecipientContactId = Guid.NewGuid(), RecipientName = "Finance Director",
      RecipientEmail = "fd@example.test", DocumentType = "ENGAGEMENT_LETTER", DocumentReference = reference, DocumentRevision = 1,
      DocumentSha256 = new string('a', 64), DispatchedByUserId = seed.Admin.Id, DispatchedAt = at
    };
    db.CorrespondenceDispatchRecords.AddRange(
      Routed(seed.EngagementId, "EL-2026-001", now.AddHours(-2)), Routed(seed.EngagementId, "EL-2026-002", now.AddHours(-1)),
      Routed(siblingId, "EL-SIBLING", now));
    // A commercial notification for a proposal this engagement is not bound to must not appear.
    db.CommercialNotifications.Add(new CommercialNotification
    {
      Id = Guid.NewGuid(), FirmId = seed.FirmId, Kind = CommercialNotificationKinds.Proposal, ProposalId = Guid.NewGuid(),
      Recipient = "other@example.test", Subject = "Unrelated proposal", Body = "Body", DeliveryState = CommercialDeliveryStates.Queued,
      CreatedAt = now
    });
    await db.SaveChangesAsync();

    var timeline = await EngagementCommunicationTimelineQuery.GetAsync(db, PbcSeed.Actor(manager, "Manager"), seed.EngagementId);
    Assert.True(timeline.Succeeded, timeline.Message);
    Assert.Equal(2, timeline.Value!.TotalCount);
    // Newest first, and a routing record is worded as recorded because it carries no provider receipt.
    Assert.Equal(["EL-2026-002", "EL-2026-001"], timeline.Value.Entries.Select(x => x.Subject));
    Assert.All(timeline.Value.Entries, x => Assert.Equal(
      ("CORRESPONDENCE", EngagementCommunicationStatuses.Recorded, (DateTimeOffset?)null), (x.Source, x.Status, x.DeliveredAtUtc)));
    Assert.All(timeline.Value.Entries, x => Assert.StartsWith("/app/", x.SourceLink, StringComparison.Ordinal));

    // A manager of the sibling engagement, an engagement staff member and an unknown engagement all learn nothing.
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await EngagementCommunicationTimelineQuery.GetAsync(db, PbcSeed.Actor(siblingManager, "Manager"), seed.EngagementId)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await EngagementCommunicationTimelineQuery.GetAsync(db, PbcSeed.Actor(seed.Staff, "Staff"), seed.EngagementId)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await EngagementCommunicationTimelineQuery.GetAsync(db, PbcSeed.Actor(manager, "Manager"), Guid.NewGuid())).ErrorCode);
    var sibling = await EngagementCommunicationTimelineQuery.GetAsync(db, PbcSeed.Actor(siblingManager, "Manager"), siblingId);
    Assert.Equal("EL-SIBLING", Assert.Single(sibling.Value!.Entries).Subject);
  }
}
