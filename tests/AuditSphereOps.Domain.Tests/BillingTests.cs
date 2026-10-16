using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class BillingTests
{
  private sealed record Fixture(
    Guid FirmId,
    Guid ClientId,
    AppUser Manager,
    AppUser Reviewer,
    AppUser Partner,
    ActorContext ManagerActor,
    ActorContext ReviewerActor,
    ActorContext PartnerActor);

  [Fact]
  public async Task BillingWorkflow_SeparatesFinanceRoles_BalancesReceiptAndCredit_AndProtectsPostedHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid accountId, invoiceId, receiptId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      accountId = (await BillingService.CreateBillingAccountAsync(db, fixture.ManagerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"))).Value;
      invoiceId = (await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-100", [
          new InvoiceLineRequest("Approved services", 2, 100)
        ], Tax: 20))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);
      var denied = await BillingService.ApproveInvoiceAsync(db, fixture.PartnerActor, invoiceId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, fixture.ReviewerActor, invoiceId)).Succeeded);
      var noProfile = await BillingService.PostInvoiceAsync(db, fixture.ManagerActor, invoiceId);
      Assert.False(noProfile.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, noProfile.ErrorCode);

      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);
      Assert.True((await BillingService.SendInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);

      var note = await BillingService.IssueCreditNoteAsync(db, fixture.ManagerActor,
        new IssueCreditNoteRequest(invoiceId, "CN-100", 20, "Approved service credit"));
      Assert.True(note.Succeeded);
      receiptId = (await BillingService.RecordReceiptAsync(db, fixture.ManagerActor,
        new RecordReceiptRequest(accountId, 200, "BANK-100"))).Value;
      Assert.True((await BillingService.AllocateReceiptAsync(db, fixture.ManagerActor,
        new AllocateReceiptRequest(receiptId, invoiceId, 200))).Succeeded);
      var over = await BillingService.AllocateReceiptAsync(db, fixture.ManagerActor,
        new AllocateReceiptRequest(receiptId, invoiceId, 1));
      Assert.False(over.Succeeded);
      Assert.Equal("billing.over-allocation", over.ErrorCode);

      var balance = await BillingService.GetInvoiceBalanceAsync(db, fixture.ManagerActor, invoiceId);
      Assert.True(balance.Succeeded);
      Assert.Equal(220m, balance.Value!.Total);
      Assert.Equal(20m, balance.Value.Credited);
      Assert.Equal(200m, balance.Value.Allocated);
      Assert.Equal(0m, balance.Value.Outstanding);

      var detail = await BillingService.GetInvoiceDetailAsync(db, fixture.ManagerActor, invoiceId);
      Assert.True(detail.Succeeded, detail.Message);
      Assert.Single(detail.Value!.Lines);
      Assert.Equal(0m, detail.Value.Balance.Outstanding);

      var otherClientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient
      {
        Id = otherClientId, FirmId = fixture.FirmId, LegalName = "Unrelated billing client", CreatedAt = DateTimeOffset.UtcNow
      });
      var unrelatedManager = User(fixture.FirmId, "FinanceManager");
      db.Users.Add(unrelatedManager);
      db.RoleGrants.Add(Grant(fixture.FirmId, unrelatedManager, "FinanceManager", otherClientId));
      await db.SaveChangesAsync();
      var deniedDetail = await BillingService.GetInvoiceDetailAsync(db, Actor(unrelatedManager, "FinanceManager"), invoiceId);
      Assert.False(deniedDetail.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, deniedDetail.ErrorCode);
      Assert.Null(deniedDetail.Value);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE invoices SET total = 1 WHERE id = {invoiceId} AND firm_id = {fixture.FirmId}"));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"DELETE FROM receipts WHERE id = {receiptId} AND firm_id = {fixture.FirmId}"));
    }
  }

  [Fact]
  public async Task ConcurrentSourceAllocations_AllowOnlyOneInvoice()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid accountId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      accountId = (await BillingService.CreateBillingAccountAsync(db, fixture.ManagerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"))).Value;
    // A supported, validated source: unknown kinds are refused before any reservation exists.
    var sourceId = await SeedTimeAsync(pg, fixture, PracticeTimeStates.TimeApproved);

    async Task<CommandResult<Guid>> CreateAsync(string number)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, number, [
          new InvoiceLineRequest("One approved time entry", 1.5m, 300m, "TIME", sourceId, 1)
        ]));
    }

    var results = await Task.WhenAll(CreateAsync("INV-200"), CreateAsync("INV-201"));
    Assert.Single(results, x => x.Succeeded);
    Assert.Single(results, x => !x.Succeeded);
    Assert.Contains(results, x => x.ErrorCode is "billing.source-duplicate" or "billing.conflict");
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.BillingSourceAllocations.CountAsync());
  }

  [Theory]
  [InlineData("permitted", null)]
  [InlineData("stale-revision", "billing.source-invalid")]
  [InlineData("wrong-status", "billing.source-invalid")]
  [InlineData("unsupported-kind", "billing.source-unsupported")]
  public async Task InvoiceTimeSourceScope_ValidatesTypedSourceBeforeAnyReservation(string scenario, string? expectedError)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid accountId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      accountId = (await BillingService.CreateBillingAccountAsync(db, fixture.ManagerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"))).Value;
    var entryId = await SeedTimeAsync(pg, fixture,
      scenario == "wrong-status" ? PracticeTimeStates.TimeSubmitted : PracticeTimeStates.TimeApproved);
    // The approved snapshot is 90 minutes at 300/hour: quantity 1.5 and amount 450.
    var line = scenario switch
    {
      "stale-revision" => new InvoiceLineRequest("Approved time", 1.5m, 300m, "TIME", entryId, 2),
      "unsupported-kind" => new InvoiceLineRequest("Unvalidated source", 1.5m, 300m, "MILESTONE", entryId, 1),
      _ => new InvoiceLineRequest("Approved time", 1.5m, 300m, "TIME", entryId, 1)
    };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-SOURCE-1", [line]));
      Assert.Equal(expectedError, result.ErrorCode);
      Assert.Equal(expectedError is null, result.Succeeded);
    }

    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(expectedError is null ? 1 : 0, await verify.Invoices.CountAsync());
      Assert.Equal(expectedError is null ? 1 : 0, await verify.BillingSourceAllocations.CountAsync());
    }
    if (expectedError is not null) return;

    // One reservation survives the invoice lifecycle: no later draft can reserve the same source revision.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var invoiceId = await db.Invoices.Select(x => x.Id).SingleAsync();
      Assert.Equal("billing.source-duplicate", (await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-SOURCE-2", [line]))).ErrorCode);
      Assert.True((await BillingService.SubmitInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, fixture.ReviewerActor, invoiceId)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile,
        Approved = true, ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);
      Assert.Equal("billing.source-duplicate", (await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-SOURCE-3", [line]))).ErrorCode);
    }
    await using (var verify = new AuditSphereDbContext(pg.Options))
      Assert.Equal(1, await verify.BillingSourceAllocations.CountAsync());
  }

  private static async Task<Guid> SeedTimeAsync(PgTestSchema pg, Fixture fixture, string status)
  {
    var now = DateTimeOffset.UtcNow;
    var taskId = Guid.NewGuid();
    var rateCardId = Guid.NewGuid();
    var entryId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.WorkTasks.Add(new WorkTask { Id = taskId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, Title = "Billing source task", CreatedAt = now });
    db.RateCardVersions.Add(new RateCardVersion
    {
      Id = rateCardId, FirmId = fixture.FirmId, Role = "Staff", Activity = "Billing source", Currency = "QAR", RatePerHour = 300m,
      Status = PracticeTimeStates.RateApproved, CreatedByUserId = fixture.Manager.Id, ApprovedByUserId = fixture.Reviewer.Id,
      ApprovedAt = now, CreatedAt = now
    });
    db.TimeEntries.Add(new TimeEntry
    {
      Id = entryId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, TaskId = taskId, UserId = fixture.Manager.Id,
      WorkDate = new DateOnly(2026, 3, 2), StartMinute = 540, DurationMinutes = 90, Role = "Staff", Activity = "Billing source",
      Narrative = "Synthetic billable time", BillableClassification = PracticeTimeStates.Billable, Status = status, Currency = "QAR",
      RateCardVersionId = rateCardId, RatePerHour = 300m, SubmittedAt = now,
      ApprovedByUserId = status == PracticeTimeStates.TimeApproved ? fixture.Reviewer.Id : null,
      ApprovedAt = status == PracticeTimeStates.TimeApproved ? now : null, CreatedAt = now
    });
    await db.SaveChangesAsync();
    return entryId;
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "Billing Client", CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    var manager = User(firmId, "FinanceManager");
    var reviewer = User(firmId, "FinanceReviewer");
    var partner = User(firmId, "Partner");
    db.Users.AddRange(manager, reviewer, partner);
    await db.SaveChangesAsync();
    db.RoleGrants.AddRange(
      Grant(firmId, manager, "FinanceManager", clientId),
      Grant(firmId, reviewer, "FinanceReviewer", clientId),
      Grant(firmId, partner, "Partner", clientId));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, manager, reviewer, partner,
      Actor(manager, "FinanceManager"), Actor(reviewer, "FinanceReviewer"), Actor(partner, "Partner"));
  }

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = $"sub-{label}-{Guid.NewGuid():N}",
    TenantId = "tenant-test", Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
    DisplayName = label, UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role, Guid clientId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, ClientId = clientId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);
}
