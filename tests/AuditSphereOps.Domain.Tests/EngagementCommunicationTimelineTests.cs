using System.Data.Common;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
    var wrongClientId = Guid.NewGuid();
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    var siblingManager = PbcSeed.User(seed.FirmId, "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(manager, siblingManager);
    db.Engagements.Add(new Engagement
    {
      Id = siblingId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, Status = "Active",
      ProfessionalWorkBlocked = false, CreatedAt = now
    });
    db.PracticeClients.Add(new PracticeClient
    {
      Id = wrongClientId, FirmId = seed.FirmId, LegalName = "WRONG CLIENT", CreatedAt = now
    });
    db.RoleGrants.AddRange(
      PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId),
      PbcSeed.Grant(seed.FirmId, siblingManager, "Manager", seed.ClientId, siblingId));
    var tiedDispatches = Enumerable.Range(1, EngagementCommunicationTimelineQuery.MaxEntries + 1)
      .Select(index => Routed(seed, seed.EngagementId, $"EL-TIE-{index:D3}", now,
        Guid.Parse($"00000000-0000-0000-0000-{index:D12}"))).ToArray();
    var dispatches = new List<CorrespondenceDispatchRecord>
    {
      Routed(seed, seed.EngagementId, "EL-2026-001", now.AddHours(-2)),
      Routed(seed, seed.EngagementId, "EL-2026-002", now.AddHours(-1)),
    };
    dispatches.AddRange(tiedDispatches);
    dispatches.Add(Routed(seed, siblingId, "EL-SIBLING", now));
    dispatches.Add(Routed(seed, seed.EngagementId, "EL-WRONG-CLIENT", now, clientId: wrongClientId));
    db.CorrespondenceDispatchRecords.AddRange(dispatches);
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
    Assert.Equal(EngagementCommunicationTimelineQuery.MaxEntries + 3, timeline.Value!.TotalCount);
    Assert.Equal(EngagementCommunicationTimelineQuery.MaxEntries, timeline.Value.Entries.Count);
    // Equal timestamps use a stable ID tiebreaker; routing records carry no provider receipt.
    Assert.Equal(tiedDispatches.Take(EngagementCommunicationTimelineQuery.MaxEntries).Select(x => x.Id),
      timeline.Value.Entries.Select(x => x.Id));
    Assert.DoesNotContain(timeline.Value.Entries, x =>
      x.Subject is "EL-TIE-201" or "EL-SIBLING" or "EL-WRONG-CLIENT");
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

  [Fact]
  [Trait("Profile", "Database")]
  public async Task RevocationDuringSnapshotReadRefusesTheTimeline()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(manager);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId));
      await db.SaveChangesAsync();
    }
    var interceptor = new AfterSourceCount("pbc_communications", async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      await revoke.RoleGrants.Where(x => x.FirmId == seed.FirmId && x.UserId == manager.Id && x.Role == "Manager" &&
        x.EngagementId == seed.EngagementId && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var reader = new AuditSphereDbContext(options);
    var result = await EngagementCommunicationTimelineQuery.GetAsync(reader, PbcSeed.Actor(manager, "Manager"), seed.EngagementId);
    Assert.Equal(1, interceptor.Calls);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Null(result.Value);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task InsertAfterSourceCountAppearsOnlyInTheNextSnapshot()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var manager = PbcSeed.User(seed.FirmId, "Staff");
    var originalId = Guid.NewGuid();
    var pbcRequestId = Guid.NewGuid();
    var insertedId = Guid.NewGuid();
    var recordedAt = DateTimeOffset.UtcNow;
    await using (var arrange = new AuditSphereDbContext(pg.Options))
    {
      arrange.Users.Add(manager);
      arrange.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, manager, "Manager", seed.ClientId, seed.EngagementId));
      arrange.CorrespondenceDispatchRecords.Add(Routed(seed, seed.EngagementId, "EL-BEFORE-SNAPSHOT", recordedAt, originalId));
      arrange.PbcRequests.Add(new PbcRequest
      {
        Id = pbcRequestId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        Objective = "Provide the trial balance", EntityScope = "Gulf Trading LLC", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Area = "Financial reporting", RequestedFormat = "Excel", ControlTotals = "",
        ClientOwnerUserId = seed.Client.Id, FirmOwnerUserId = manager.Id, ReviewerUserId = seed.Reviewer.Id,
        DueDate = "2026-12-31", Confidentiality = "Client confidential", AcceptanceCriteria = "Complete and balanced",
        State = PbcStates.Sent, CreatedAt = recordedAt, CreatedByUserId = manager.Id, UpdatedAt = recordedAt
      });
      arrange.PbcCommunications.Add(new PbcCommunication
      {
        Id = originalId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        PbcRequestId = pbcRequestId, AuthorUserId = manager.Id, Kind = PbcCommunicationKinds.Email,
        Body = "Trial balance request", RecipientEmail = "client@example.test", Subject = "Trial balance request",
        PortalUrl = "/portal/requests", DeliveryState = PbcDeliveryStates.Sent, DeliveredAt = recordedAt,
        CreatedAt = recordedAt
      });
      await arrange.SaveChangesAsync();
    }

    var interceptor = new AfterSourceCount("correspondence_dispatch_records", async () =>
    {
      await using var writer = new AuditSphereDbContext(pg.Options);
      writer.CorrespondenceDispatchRecords.Add(Routed(seed, seed.EngagementId, "EL-AFTER-COUNT", recordedAt.AddSeconds(1), insertedId));
      await writer.SaveChangesAsync();
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using (var reader = new AuditSphereDbContext(options))
    {
      var snapshot = await EngagementCommunicationTimelineQuery.GetAsync(
        reader, PbcSeed.Actor(manager, "Manager"), seed.EngagementId);
      Assert.True(snapshot.Succeeded, snapshot.Message);
      Assert.Equal(2, snapshot.Value!.TotalCount);
      Assert.Equal(["CORRESPONDENCE", "PBC"], snapshot.Value.Entries.Select(x => x.Source));
      Assert.Equal([originalId, originalId], snapshot.Value.Entries.Select(x => x.Id));
      var pbcEntry = Assert.Single(snapshot.Value.Entries, x => x.Source == "PBC");
      Assert.Equal(EngagementCommunicationStatuses.ProviderAccepted, pbcEntry.Status);
      Assert.Null(pbcEntry.DeliveredAtUtc);
      Assert.DoesNotContain(snapshot.Value.Entries, x => x.Id == insertedId);
      Assert.Equal(1, interceptor.Calls);
    }

    await using var nextRequest = new AuditSphereDbContext(pg.Options);
    var refreshed = await EngagementCommunicationTimelineQuery.GetAsync(
      nextRequest, PbcSeed.Actor(manager, "Manager"), seed.EngagementId);
    Assert.True(refreshed.Succeeded, refreshed.Message);
    Assert.Equal(3, refreshed.Value!.TotalCount);
    Assert.Contains(refreshed.Value.Entries, x => x.Id == insertedId);
  }

  private static CorrespondenceDispatchRecord Routed(
    PbcSeed.Fixture seed, Guid engagementId, string reference, DateTimeOffset at,
    Guid? id = null, Guid? clientId = null) => new()
  {
    Id = id ?? Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = clientId ?? seed.ClientId, EngagementId = engagementId,
    Purpose = CorrespondencePurposes.Commercial, RecipientContactId = Guid.NewGuid(), RecipientName = "Finance Director",
    RecipientEmail = "fd@example.test", DocumentType = "ENGAGEMENT_LETTER", DocumentReference = reference, DocumentRevision = 1,
    DocumentSha256 = new string('a', 64), DispatchedByUserId = seed.Admin.Id, DispatchedAt = at
  };

  private sealed class AfterSourceCount(string table, Func<Task> action) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => calls;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains(table, StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("COUNT(*)", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref calls, 1, 0) == 0)
        await action();
      return result;
    }
  }
}
