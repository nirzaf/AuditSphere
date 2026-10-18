using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcReminderTests
{
  private static readonly PbcReminderOptions EnabledOptions = new(
    Enabled: true, DaysBeforeDue: 3, OverdueIntervalDays: 3, TimeZoneId: "UTC",
    QuietHoursStart: "21:00", QuietHoursEnd: "07:00", BatchSize: 50,
    PublicBaseUrl: "https://audit.example.test/");
  private static readonly DateTimeOffset NoonUtc = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public async Task DisabledReminderPolicyLeavesRequestHistoryAndOutboxUnchanged()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var discovery = new PbcReminderDiscovery(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)),
      LiveMailOptions(fixture.FirmId), EnabledOptions with { Enabled = false },
      new FixedTimeProvider(NoonUtc));

    Assert.Equal(0, await discovery.EnqueuePendingAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(PbcStates.Sent,
      (await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId)).State);
    Assert.Empty(await verify.PbcReminderReceipts.AsNoTracking().Where(x => x.PbcRequestId == requestId).ToListAsync());
    Assert.Empty(await verify.PbcCommunications.AsNoTracking().Where(x =>
      x.PbcRequestId == requestId && x.Kind == PbcCommunicationKinds.Email).ToListAsync());
    Assert.Empty(await verify.DurableOperations.AsNoTracking().Where(x =>
      x.TargetId == requestId && x.OperationKind == PbcMailDeliveryHandler.Kind).ToListAsync());
  }

  [Fact]
  public async Task DueReminderIsIdempotentAndDeliveredThroughExistingMailOutbox()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var workerOptions = LiveMailOptions(fixture.FirmId);
    var clock = new FixedTimeProvider(NoonUtc);
    var reminderDiscovery = new PbcReminderDiscovery(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)),
      workerOptions, EnabledOptions, clock);

    var concurrent = await Task.WhenAll(
      reminderDiscovery.EnqueuePendingAsync(), reminderDiscovery.EnqueuePendingAsync());
    Assert.Equal(1, concurrent.Sum());
    Assert.Equal(0, await reminderDiscovery.EnqueuePendingAsync());

    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
      Assert.Equal(PbcStates.Sent, request.State);
      Assert.Equal(1, request.Revision);
      Assert.Single(await verify.PbcReminderReceipts.AsNoTracking().Where(x => x.PbcRequestId == requestId).ToListAsync());
      var reminder = await verify.PbcCommunications.AsNoTracking().SingleAsync(x =>
        x.PbcRequestId == requestId && x.Kind == PbcCommunicationKinds.Email);
      Assert.Equal(PbcDeliveryStates.Queued, reminder.DeliveryState);
      Assert.Equal(fixture.Client.Email, reminder.RecipientEmail);
      Assert.DoesNotContain("risk", reminder.Body, StringComparison.OrdinalIgnoreCase);
      Assert.Contains("portal/requests/", reminder.PortalUrl);
    }

    var sender = new RecordingSender();
    var handler = new PbcMailDeliveryHandler(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)), sender);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], workerOptions), workerOptions),
      [reminderDiscovery, new PbcMailDiscovery(factory, store, handler, workerOptions)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);

    await worker.DrainAsync();
    await worker.DrainAsync();
    Assert.Single(sender.Plans);
    await using var delivered = new AuditSphereDbContext(pg.Options);
    Assert.Equal(PbcDeliveryStates.Sent,
      (await delivered.PbcCommunications.AsNoTracking().SingleAsync(x =>
        x.PbcRequestId == requestId && x.Kind == PbcCommunicationKinds.Email)).DeliveryState);
    Assert.Equal(OperationState.COMPLETED,
      (await delivered.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == PbcMailDeliveryHandler.Kind)).Status);
  }

  [Fact]
  public async Task ReminderIsSuppressedWhenClientGrantWasRevoked()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == fixture.Client.Id && x.Role == "ClientUser");
      grant.RevokedAt = NoonUtc.AddMinutes(-1);
      await db.SaveChangesAsync();
    }

    var discovery = new PbcReminderDiscovery(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)),
      LiveMailOptions(fixture.FirmId), EnabledOptions, new FixedTimeProvider(NoonUtc));
    Assert.Equal(0, await discovery.EnqueuePendingAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.PbcReminderReceipts.AsNoTracking().Where(x => x.PbcRequestId == requestId).ToListAsync());
    Assert.Empty(await verify.PbcCommunications.AsNoTracking().Where(x =>
      x.PbcRequestId == requestId && x.Kind == PbcCommunicationKinds.Email).ToListAsync());
  }

  [Fact]
  public async Task QueuedReminderIsBlockedAndShownIfClientAccessIsRevokedBeforeDispatch()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var workerOptions = LiveMailOptions(fixture.FirmId);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var reminderDiscovery = new PbcReminderDiscovery(factory, workerOptions, EnabledOptions,
      new FixedTimeProvider(NoonUtc));
    Assert.Equal(1, await reminderDiscovery.EnqueuePendingAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == fixture.Client.Id && x.Role == "ClientUser");
      grant.RevokedAt = NoonUtc.AddMinutes(1);
      await db.SaveChangesAsync();
    }

    var sender = new RecordingSender();
    var handler = new PbcMailDeliveryHandler(factory, sender);
    var store = new PostgresOperationStore(factory);
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], workerOptions), workerOptions),
      [reminderDiscovery, new PbcMailDiscovery(factory, store, handler, workerOptions)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    await worker.DrainAsync();

    Assert.Empty(sender.Plans);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == PbcMailDeliveryHandler.Kind);
    Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, operation.Status);
    var inbox = await PbcInboxWorkspaceQuery.GetAsync(verify,
      PbcSeed.Actor(fixture.Staff, "Staff"), fixture.EngagementId);
    Assert.True(inbox.Succeeded, inbox.Message);
    Assert.Contains(Assert.Single(inbox.Value!.Requests, x => x.Id == requestId).Timeline,
      x => x.Label == "Email blocked before delivery to client");
  }

  [Fact]
  public async Task CompletedRequestsAndQuietHoursDoNotProduceReminders()
  {
    var due = new DateOnly(2026, 10, 13);
    Assert.Equal($"PRE_DUE:{due:yyyy-MM-dd}", PbcReminderDiscovery.ActionKey(due, new(2026, 10, 12), 3));
    Assert.Equal("OVERDUE:0", PbcReminderDiscovery.ActionKey(new(2026, 10, 10), new(2026, 10, 12), 3));
    Assert.Equal("OVERDUE:1", PbcReminderDiscovery.ActionKey(new(2026, 10, 8), new(2026, 10, 12), 3));
    Assert.True(PbcReminderDiscovery.IsQuiet(new TimeSpan(22, 0, 0), new(21, 0), new(7, 0)));
    Assert.True(PbcReminderDiscovery.IsQuiet(new TimeSpan(6, 59, 0), new(21, 0), new(7, 0)));
    Assert.False(PbcReminderDiscovery.IsQuiet(new TimeSpan(12, 0, 0), new(21, 0), new(7, 0)));
    Assert.Throws<InvalidOperationException>(() => EnabledOptions.Validate(liveMailWorker: false));

    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.State = PbcStates.Accepted;
      request.AcceptedAt = NoonUtc;
      request.AcceptedByUserId = fixture.Reviewer.Id;
      await db.SaveChangesAsync();
    }
    var discovery = new PbcReminderDiscovery(
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)),
      LiveMailOptions(fixture.FirmId), EnabledOptions, new FixedTimeProvider(NoonUtc));
    Assert.Equal(0, await discovery.EnqueuePendingAsync());
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.PbcReminderReceipts.AsNoTracking().ToListAsync());
  }

  private static async Task<Guid> CreateDueSentRequestAsync(
    ITestPostgresDatabase pg, PbcSeed.Fixture fixture, string dueDate)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var created = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"),
      new(fixture.EngagementId, "Upload the reviewed bank reconciliation.", "TEST ENTITY",
        "2026-01-01", "2026-12-31", "Cash", "PDF", string.Empty, fixture.Client.Id,
        fixture.Staff.Id, fixture.Reviewer.Id, dueDate, "Confidential", "Readable and complete."));
    Assert.True(created.Succeeded, created.ErrorCode);
    var request = await db.PbcRequests.SingleAsync(x => x.Id == created.Value);
    request.State = PbcStates.Sent;
    request.UpdatedAt = NoonUtc.AddHours(-1);
    await db.SaveChangesAsync();
    return request.Id;
  }

  private static WorkerOptions LiveMailOptions(Guid firmId) =>
    new(firmId, "Acceptance", ExternalEffectsEnabled: true, Group: "mail");

  private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
  {
    public override DateTimeOffset GetUtcNow() => now;
  }

  private sealed class RecordingSender : IPbcMailSender
  {
    public List<PbcMailPlan> Plans { get; } = [];
    public Task SendAsync(PbcMailPlan plan, CancellationToken ct)
    {
      Plans.Add(plan);
      return Task.CompletedTask;
    }
  }
}
