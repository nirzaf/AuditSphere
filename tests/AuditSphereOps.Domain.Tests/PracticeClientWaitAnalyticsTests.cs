using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PracticeClientWaitAnalyticsTests
{
  [Fact]
  public async Task ClientWait_UsesFirstConfirmedSend_AndClosesOnRecordedClientActions()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var otherFirm = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var partner = PbcSeed.Actor(fixture.Admin, "Administrator");
    var now = DateTimeOffset.UtcNow;
    var today = DateOnly.FromDateTime(now.UtcDateTime);
    var completedRequest = await CreateRequestAsync(pg, fixture, staff, "Completed wait");
    var uploadRequest = await CreateRequestAsync(pg, fixture, staff, "Upload wait");
    var openRequest = await CreateRequestAsync(pg, fixture, staff, "Open wait");
    var terminalRequest = await CreateRequestAsync(pg, fixture, staff, "Terminal wait");
    var unconfirmedRequest = await CreateRequestAsync(pg, fixture, staff, "Unconfirmed mail");
    var foreignRequest = await CreateRequestAsync(pg, otherFirm, PbcSeed.Actor(otherFirm.Staff, "Staff"), "Other firm");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var requests = await db.PbcRequests.Where(x => x.FirmId == fixture.FirmId).ToListAsync();
      foreach (var request in requests)
      {
        request.State = request.Id switch
        {
          var id when id == completedRequest => PbcStates.Acknowledged,
          var id when id == uploadRequest => PbcStates.PartiallyReceived,
          var id when id == openRequest => PbcStates.Sent,
          var id when id == terminalRequest => PbcStates.Closed,
          _ => PbcStates.Sent
        };
        request.Revision++;
        request.UpdatedAt = now;
      }

      var completedStart = now.AddHours(-12);
      var completedRetry = now.AddHours(-10);
      var completedResponse = now.AddHours(-8);
      var uploadStart = now.AddHours(-7);
      var uploadResponse = now.AddHours(-6);
      var openStart = now.AddHours(-4);
      var reminderAt = now.AddHours(-2);
      var terminalStart = now.AddHours(-3);
      var reminderRetry = Email(fixture, openRequest, reminderAt, PbcDeliveryStates.Sent);
      var communications = new[]
      {
        Email(fixture, completedRequest, completedStart, PbcDeliveryStates.Sent),
        Email(fixture, completedRequest, completedRetry, PbcDeliveryStates.Sent),
        ClientMessage(fixture, completedRequest, completedResponse),
        Email(fixture, uploadRequest, uploadStart, PbcDeliveryStates.Sent),
        Email(fixture, openRequest, openStart, PbcDeliveryStates.Sent),
        reminderRetry,
        Email(fixture, terminalRequest, terminalStart, PbcDeliveryStates.Sent),
        Email(fixture, unconfirmedRequest, now.AddHours(-1), PbcDeliveryStates.Queued),
        Email(fixture, unconfirmedRequest, now.AddMinutes(-30), PbcDeliveryStates.Failed),
        Email(otherFirm, foreignRequest, now.AddHours(-2), PbcDeliveryStates.Sent)
      };
      db.PbcCommunications.AddRange(communications);
      db.PbcUploadIntents.Add(new PbcUploadIntent
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        PbcRequestId = uploadRequest, UploaderUserId = fixture.Client.Id, FileName = "response.csv", ContentType = "text/csv",
        DeclaredByteCount = 1, DeclaredSha256Hex = new string('a', 64), CapabilityHash = new string('b', 64),
        State = PbcUploadStates.Started, ExpiresAt = uploadResponse.AddDays(1), CreatedAt = uploadResponse
      });
      db.PbcReminderReceipts.Add(new PbcReminderReceipt
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        PbcRequestId = openRequest, RuleRevisionId = Guid.NewGuid(), ActionKey = "OVERDUE:1",
        CommunicationId = reminderRetry.Id, CreatedAt = reminderAt
      });
      await db.SaveChangesAsync();
    }

    await using var read = new AuditSphereDbContext(pg.Options);
    var result = await PracticeAnalyticsQuery.GetAsync(read, partner, today.AddDays(-1), today);
    Assert.True(result.Succeeded, result.Message);
    var wait = result.Value!.ClientWait;
    Assert.True(wait.Available);
    Assert.Equal((4, 2, 1, 1), (wait.Intervals, wait.Completed, wait.Open, wait.Excluded));
    Assert.Equal(2.5m, wait.AverageCompletedHours);
    Assert.Equal(4.0m, wait.AverageOpenHours);
  }

  [Fact]
  public async Task ClientAcknowledgement_IsRecordedAsAnAppendOnlyClientAction()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var client = PbcSeed.Actor(fixture.Client, "ClientUser");
    var partner = PbcSeed.Actor(fixture.Admin, "Administrator");
    var requestId = await CreateRequestAsync(pg, fixture, staff, "Acknowledgement action");
    var sendAt = DateTimeOffset.UtcNow.AddHours(-2);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.State = PbcStates.Sent;
      request.Revision++;
      request.UpdatedAt = DateTimeOffset.UtcNow;
      db.PbcCommunications.Add(Email(fixture, requestId, sendAt, PbcDeliveryStates.Sent));
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var revision = await db.PbcRequests.Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync();
      var changed = await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(requestId, PbcStates.Acknowledged, revision));
      Assert.True(changed.Succeeded, changed.Message);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var action = await verify.PbcCommunications.AsNoTracking().SingleAsync(x =>
      x.PbcRequestId == requestId && x.Kind == PbcCommunicationKinds.ClientAction);
    Assert.Equal(fixture.Client.Id, action.AuthorUserId);
    Assert.Contains("acknowledged", action.Body, StringComparison.OrdinalIgnoreCase);
    var result = await PracticeAnalyticsQuery.GetAsync(verify, partner,
      DateOnly.FromDateTime(sendAt.UtcDateTime), DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal((1, 1, 0), (result.Value!.ClientWait.Intervals, result.Value.ClientWait.Completed, result.Value.ClientWait.Open));
    Assert.NotNull(result.Value.ClientWait.AverageCompletedHours);
  }

  [Fact]
  public async Task ClientWait_OverLimitSampleReturnsUnavailableWithoutPartialCounts()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var administrator = PbcSeed.Actor(fixture.Admin, "Administrator");
    var requestId = await CreateRequestAsync(pg, fixture, staff, "Bounded wait history");
    var now = DateTimeOffset.UtcNow;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PbcCommunications.AddRange(Enumerable.Range(0, 5_001).Select(_ =>
        Email(fixture, requestId, now.AddMinutes(-1), PbcDeliveryStates.Sent)));
      await db.SaveChangesAsync();
    }

    await using var read = new AuditSphereDbContext(pg.Options);
    var today = DateOnly.FromDateTime(now.UtcDateTime);
    var result = await PracticeAnalyticsQuery.GetAsync(read, administrator, today.AddDays(-1), today);
    Assert.True(result.Succeeded, result.Message);
    Assert.False(result.Value!.ClientWait.Available);
    Assert.Equal((0, 0, 0, 0), (result.Value.ClientWait.Intervals, result.Value.ClientWait.Completed,
      result.Value.ClientWait.Open, result.Value.ClientWait.Excluded));
    Assert.Null(result.Value.ClientWait.AverageCompletedHours);
    Assert.NotNull(result.Value.ClientWait.UnavailableReason);
    Assert.Contains("Narrow the reporting period", result.Value.ClientWait.UnavailableReason!, StringComparison.Ordinal);
  }

  private static async Task<Guid> CreateRequestAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, AuditSphereOps.Application.Abstractions.ActorContext staff, string objective)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
      fixture.EngagementId, objective, "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF", "",
      fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id, "2027-01-31", "Confidential", "Readable and complete."));
    Assert.True(created.Succeeded, created.Message);
    return created.Value;
  }

  private static PbcCommunication Email(PbcSeed.Fixture fixture, Guid requestId, DateTimeOffset at, string state) => new()
  {
    Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
    PbcRequestId = requestId, AuthorUserId = fixture.Staff.Id, Kind = PbcCommunicationKinds.Email,
    Body = "A request is available in the portal.", RecipientEmail = fixture.Client.Email, Subject = "PBC request",
    PortalUrl = "https://audit.example.test/portal/", DeliveryState = state,
    DeliveredAt = state == PbcDeliveryStates.Sent ? at : null, CreatedAt = at
  };

  private static PbcCommunication ClientMessage(PbcSeed.Fixture fixture, Guid requestId, DateTimeOffset at) => new()
  {
    Id = Guid.NewGuid(), FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
    PbcRequestId = requestId, AuthorUserId = fixture.Client.Id, Kind = PbcCommunicationKinds.ClientMessage,
    Body = "We are responding to the request.", CreatedAt = at
  };
}
