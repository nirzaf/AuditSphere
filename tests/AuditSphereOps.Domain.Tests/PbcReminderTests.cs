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
      new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)), sender, EnabledOptions, clock);
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
  public async Task BatchSizeOneEventuallyProcessesSameDateRequestsWithoutStarvation()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var malformedRequestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    await using (var arrange = new AuditSphereDbContext(pg.Options))
    {
      await arrange.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE pbc_requests SET due_date = {"0000-00-00"} WHERE id = {malformedRequestId}
        """);
    }
    var requestIds = new[]
    {
      await CreateDueSentRequestAsync(pg, fixture, "2026-10-13"),
      await CreateDueSentRequestAsync(pg, fixture, "2026-10-13"),
      await CreateDueSentRequestAsync(pg, fixture, "2026-10-13")
    };
    var workerOptions = LiveMailOptions(fixture.FirmId);
    var options = EnabledOptions with { BatchSize = 1 };
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var discovery = new PbcReminderDiscovery(factory, workerOptions, options,
      new FixedTimeProvider(NoonUtc));

    Assert.Equal(0, await discovery.EnqueuePendingAsync());
    Assert.Equal(1, await discovery.EnqueuePendingAsync());
    Assert.Equal(1, await discovery.EnqueuePendingAsync());
    Assert.Equal(1, await discovery.EnqueuePendingAsync());
    Assert.Equal(0, await discovery.EnqueuePendingAsync());
    var concurrentAfterWrap = await Task.WhenAll(
      discovery.EnqueuePendingAsync(), discovery.EnqueuePendingAsync());
    Assert.Equal(0, concurrentAfterWrap.Sum());

    var restarted = new PbcReminderDiscovery(factory, workerOptions, options,
      new FixedTimeProvider(NoonUtc));
    Assert.Equal(0, await restarted.EnqueuePendingAsync());
    Assert.Equal(0, await restarted.EnqueuePendingAsync());
    Assert.Equal(0, await restarted.EnqueuePendingAsync());
    Assert.Equal(0, await restarted.EnqueuePendingAsync());

    await using var verify = new AuditSphereDbContext(pg.Options);
    var receipts = await verify.PbcReminderReceipts.AsNoTracking()
      .Where(x => requestIds.Contains(x.PbcRequestId)).ToListAsync();
    Assert.Equal(3, receipts.Count);
    Assert.Equal(requestIds.OrderBy(x => x), receipts.Select(x => x.PbcRequestId).OrderBy(x => x));
    Assert.All(requestIds, id => Assert.Single(receipts, x => x.PbcRequestId == id));

    var communications = await verify.PbcCommunications.AsNoTracking()
      .Where(x => requestIds.Contains(x.PbcRequestId) && x.Kind == PbcCommunicationKinds.Email)
      .ToListAsync();
    Assert.Equal(3, communications.Count);
    Assert.All(communications, x => Assert.Equal(PbcDeliveryStates.Queued, x.DeliveryState));
    Assert.All(requestIds, id => Assert.Single(communications, x => x.PbcRequestId == id));
    Assert.All(await verify.PbcRequests.AsNoTracking().Where(x => requestIds.Contains(x.Id)).ToListAsync(),
      x => { Assert.Equal(PbcStates.Sent, x.State); Assert.Equal(1, x.Revision); });
    Assert.False(await verify.PbcReminderReceipts.AsNoTracking()
      .AnyAsync(x => x.PbcRequestId == malformedRequestId));
    Assert.False(await verify.PbcCommunications.AsNoTracking()
      .AnyAsync(x => x.PbcRequestId == malformedRequestId && x.Kind == PbcCommunicationKinds.Email));
    var malformedRequest = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == malformedRequestId);
    Assert.Equal("0000-00-00", malformedRequest.DueDate);
    Assert.Equal(PbcStates.Sent, malformedRequest.State);
    Assert.Equal(1, malformedRequest.Revision);
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
    var clock = new FixedTimeProvider(NoonUtc);
    var reminderDiscovery = new PbcReminderDiscovery(factory, workerOptions, EnabledOptions,
      clock);
    Assert.Equal(1, await reminderDiscovery.EnqueuePendingAsync());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == fixture.Client.Id && x.Role == "ClientUser");
      grant.RevokedAt = NoonUtc.AddMinutes(1);
      await db.SaveChangesAsync();
    }

    var sender = new RecordingSender();
    var handler = new PbcMailDeliveryHandler(factory, sender, EnabledOptions, clock);
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

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task QueuedReminderIsBlockedAndShownWhenPolicyIsDisabledAtDispatch(bool explicitlyDisabled)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var workerOptions = LiveMailOptions(fixture.FirmId);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var reminderDiscovery = new PbcReminderDiscovery(factory, workerOptions, EnabledOptions,
      new FixedTimeProvider(NoonUtc));
    Assert.Equal(1, await reminderDiscovery.EnqueuePendingAsync());

    Guid receiptId;
    Guid communicationId;
    string actionKey;
    DateTimeOffset receiptCreatedAt;
    await using (var queued = new AuditSphereDbContext(pg.Options))
    {
      var request = await queued.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
      Assert.Equal(PbcStates.Sent, request.State);
      Assert.Equal(1, request.Revision);
      var receipt = await queued.PbcReminderReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      receiptId = receipt.Id;
      communicationId = receipt.CommunicationId;
      actionKey = receipt.ActionKey;
      receiptCreatedAt = receipt.CreatedAt;
      var communication = await queued.PbcCommunications.AsNoTracking().SingleAsync(x => x.Id == communicationId);
      Assert.Equal(PbcDeliveryStates.Queued, communication.DeliveryState);
      Assert.Null(communication.ProviderCorrelationId);
      Assert.Null(communication.DeliveredAt);
    }

    var sender = new RecordingSender();
    // Keep the two-argument default-disabled baseline while also covering explicit policy disablement.
    var handler = explicitlyDisabled
      ? new PbcMailDeliveryHandler(factory, sender, EnabledOptions with { Enabled = false }, new FixedTimeProvider(NoonUtc))
      : new PbcMailDeliveryHandler(factory, sender);
    var store = new PostgresOperationStore(factory);
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], workerOptions), workerOptions),
      [reminderDiscovery, new PbcMailDiscovery(factory, store, handler, workerOptions)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    await worker.DrainAsync();

    Assert.Empty(sender.Plans);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var operation = await verify.DurableOperations.AsNoTracking().SingleAsync(x =>
      x.OperationKind == PbcMailDeliveryHandler.Kind && x.TargetId == communicationId);
    Assert.Equal(OperationState.AUTHORIZATION_BLOCKED, operation.Status);
    var requestAfter = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
    Assert.Equal(PbcStates.Sent, requestAfter.State);
    Assert.Equal(1, requestAfter.Revision);
    var receiptAfter = await verify.PbcReminderReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
    Assert.Equal(receiptId, receiptAfter.Id);
    Assert.Equal(communicationId, receiptAfter.CommunicationId);
    Assert.Equal(actionKey, receiptAfter.ActionKey);
    Assert.Equal(receiptCreatedAt, receiptAfter.CreatedAt);
    var communicationAfter = await verify.PbcCommunications.AsNoTracking().SingleAsync(x => x.Id == communicationId);
    Assert.Equal(PbcDeliveryStates.Queued, communicationAfter.DeliveryState);
    Assert.Null(communicationAfter.ProviderCorrelationId);
    Assert.Null(communicationAfter.DeliveredAt);
    var inbox = await PbcInboxWorkspaceQuery.GetAsync(verify,
      PbcSeed.Actor(fixture.Staff, "Staff"), fixture.EngagementId);
    Assert.True(inbox.Succeeded, inbox.Message);
    Assert.Contains(Assert.Single(inbox.Value!.Requests, x => x.Id == requestId).Timeline,
      x => x.Label == "Email blocked before delivery to client");
  }

  [Fact]
  public async Task QueuedReminderEnteringQuietHoursSafeRetriesUntilNextOpening()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var workerOptions = LiveMailOptions(fixture.FirmId);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var clock = new MutableTimeProvider(NoonUtc);
    var reminderDiscovery = new PbcReminderDiscovery(factory, workerOptions, EnabledOptions, clock);
    Assert.Equal(1, await reminderDiscovery.EnqueuePendingAsync());

    var sender = new RecordingSender();
    var handler = new PbcMailDeliveryHandler(factory, sender, EnabledOptions, clock);
    var store = new PostgresOperationStore(factory);
    Assert.Equal(1, await new PbcMailDiscovery(factory, store, handler, workerOptions).EnqueuePendingAsync(CancellationToken.None));
    var registry = new DurableOperationRegistry([handler], workerOptions);
    var claimed = await store.ClaimAsync(workerOptions, registry.Definitions,
      "pbc-reminder-quiet-boundary-test", reconciliation: false, ct: CancellationToken.None);
    Assert.NotNull(claimed);
    Assert.True(await store.ValidateAsync(claimed!, workerOptions, handler, CancellationToken.None));

    clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 22, 0, 0, TimeSpan.Zero));
    var retry = await Assert.ThrowsAsync<OperationDeferredException>(
      () => handler.ExecuteEffectAsync(claimed!, CancellationToken.None));
    Assert.Equal(TimeSpan.FromHours(9), retry.RetryAfter);
    Assert.Empty(sender.Plans);

    await using (var waiting = new AuditSphereDbContext(pg.Options))
    {
      var communication = await waiting.PbcCommunications.AsNoTracking().SingleAsync(x =>
        x.PbcRequestId == requestId && x.Kind == PbcCommunicationKinds.Email);
      Assert.Equal(PbcDeliveryStates.Queued, communication.DeliveryState);
      Assert.Null(communication.ProviderCorrelationId);
      Assert.Null(communication.DeliveredAt);
    }

    clock.SetUtcNow(new DateTimeOffset(2026, 10, 13, 7, 0, 0, TimeSpan.Zero));
    await handler.ExecuteEffectAsync(claimed!, CancellationToken.None);
    Assert.Single(sender.Plans);
  }

  [Fact]
  public async Task QuietHoursWorkerDefersTheSameReminderAndSendsOnceWhenTheWindowOpens()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var options = LiveMailOptions(fixture.FirmId) with { MaxAttempts = 1 };
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    Assert.Equal(1, await new PbcReminderDiscovery(factory, options, EnabledOptions,
      new FixedTimeProvider(NoonUtc)).EnqueuePendingAsync());
    var clock = new MutableTimeProvider(NoonUtc.AddHours(10));
    var sender = new RecordingSender();
    var handler = new PbcMailDeliveryHandler(factory, sender, EnabledOptions, clock);
    var store = new PostgresOperationStore(factory);
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new PbcMailDiscovery(factory, store, handler, options)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    await worker.DrainAsync();
    Assert.Empty(sender.Plans);
    Guid operationId, communicationId, receiptId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var operation = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.OperationKind == PbcMailDeliveryHandler.Kind);
      operationId = operation.Id;
      communicationId = operation.TargetId;
      Assert.Equal(OperationState.RETRY_WAIT, operation.Status);
      Assert.Equal(0, operation.AttemptCount);
      Assert.Equal(1, operation.AttemptToken);
      Assert.True(operation.NextAttemptAt > DateTimeOffset.UtcNow.AddHours(8));
      var firstHistory = await db.OperationAttempts.AsNoTracking().Where(x => x.OperationId == operationId).ToListAsync();
      Assert.Single(firstHistory);
      Assert.Equal(operation.AttemptToken, firstHistory[0].Token);
      var receipt = await db.PbcReminderReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      receiptId = receipt.Id;
      Assert.Equal(communicationId, receipt.CommunicationId);
      Assert.Equal(PbcDeliveryStates.Queued,
        (await db.PbcCommunications.AsNoTracking().SingleAsync(x => x.Id == communicationId)).DeliveryState);
      // Only this disposable fixture's scheduler clock is advanced; no intent,
      // receipt, scope, lease or approval is rewritten to make it eligible.
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE durable_operations SET next_attempt_at = statement_timestamp() - interval '1 second'
        WHERE id = {operationId}
        """);
    }
    clock.SetUtcNow(NoonUtc.AddHours(19));
    await worker.DrainAsync();
    await worker.DrainAsync();
    Assert.Single(sender.Plans);
    await using var completed = new AuditSphereDbContext(pg.Options);
    var completedOperation = await completed.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == operationId);
    Assert.Equal(OperationState.COMPLETED, completedOperation.Status);
    Assert.Equal(1, completedOperation.AttemptCount);
    Assert.Equal(2, completedOperation.AttemptToken);
    Assert.Equal(PbcDeliveryStates.Sent,
      (await completed.PbcCommunications.AsNoTracking().SingleAsync(x => x.Id == communicationId)).DeliveryState);
    var completedReceipts = await completed.PbcReminderReceipts.AsNoTracking().Where(x => x.PbcRequestId == requestId).ToListAsync();
    Assert.Equal(receiptId, Assert.Single(completedReceipts).Id);
    Assert.Equal(communicationId, completedReceipts[0].CommunicationId);
    var attemptHistory = await completed.OperationAttempts.AsNoTracking().Where(x => x.OperationId == operationId)
      .OrderBy(x => x.Token).ToListAsync();
    Assert.Equal([1L, 2L], attemptHistory.Select(x => x.Token));
    var request = await completed.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
    Assert.Equal(PbcStates.Sent, request.State);
    Assert.Equal(1, request.Revision);
  }

  [Fact]
  public async Task ReminderAcceptedAtNoonCompletesIfQuietHoursBeginBeforePublish()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateDueSentRequestAsync(pg, fixture, "2026-10-13");
    var options = LiveMailOptions(fixture.FirmId);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    Assert.Equal(1, await new PbcReminderDiscovery(factory, options, EnabledOptions,
      new FixedTimeProvider(NoonUtc)).EnqueuePendingAsync());
    Guid receiptId, communicationId;
    await using (var queued = new AuditSphereDbContext(pg.Options))
    {
      var queuedReceipt = await queued.PbcReminderReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
      receiptId = queuedReceipt.Id;
      communicationId = queuedReceipt.CommunicationId;
    }

    var clock = new MutableTimeProvider(NoonUtc);
    var sender = new RecordingSender(() => clock.SetUtcNow(new DateTimeOffset(2026, 10, 12, 22, 0, 0, TimeSpan.Zero)));
    var handler = new PbcMailDeliveryHandler(factory, sender, EnabledOptions, clock);
    var store = new PostgresOperationStore(factory);
    var worker = new AuditSphereOps.Worker.Worker(
      new OperationDispatcher(store, new DurableOperationRegistry([handler], options), options),
      [new PbcMailDiscovery(factory, store, handler, options)],
      NullLogger<AuditSphereOps.Worker.Worker>.Instance);

    await worker.DrainAsync();
    await worker.DrainAsync();

    Assert.Single(sender.Plans);
    await using var completed = new AuditSphereDbContext(pg.Options);
    var operation = await completed.DurableOperations.AsNoTracking().SingleAsync(x => x.TargetId == communicationId);
    Assert.Equal(OperationState.COMPLETED, operation.Status);
    Assert.Equal(1, operation.AttemptCount);
    Assert.Equal(1, operation.AttemptToken);
    var mail = await completed.PbcCommunications.AsNoTracking().SingleAsync(x => x.Id == communicationId);
    Assert.Equal(PbcDeliveryStates.Sent, mail.DeliveryState);
    Assert.NotNull(mail.ProviderCorrelationId);
    Assert.NotNull(mail.DeliveredAt);
    var receipt = await completed.PbcReminderReceipts.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId);
    Assert.Equal(receiptId, receipt.Id);
    Assert.Equal(communicationId, receipt.CommunicationId);
    var request = await completed.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
    Assert.Equal(PbcStates.Sent, request.State);
    Assert.Equal(1, request.Revision);
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

  private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
  {
    private DateTimeOffset current = now;
    public override DateTimeOffset GetUtcNow() => current;
    public void SetUtcNow(DateTimeOffset value) => current = value;
  }

  private sealed class RecordingSender : IPbcMailSender
  {
    public List<PbcMailPlan> Plans { get; } = [];
    private readonly Action? onSend;

    public RecordingSender(Action? onSend = null) => this.onSend = onSend;

    public Task SendAsync(PbcMailPlan plan, CancellationToken ct)
    {
      Plans.Add(plan);
      onSend?.Invoke();
      return Task.CompletedTask;
    }
  }
}
