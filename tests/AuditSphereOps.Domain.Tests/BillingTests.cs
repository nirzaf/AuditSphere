using System.Data.Common;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

      var creditRequest = new IssueCreditNoteRequest(invoiceId, "CN-100", 20, "Approved service credit", Guid.NewGuid());
      var note = await BillingService.IssueCreditNoteAsync(db, fixture.ManagerActor, creditRequest);
      Assert.True(note.Succeeded);
      var noteReplay = await BillingService.IssueCreditNoteAsync(db, fixture.ManagerActor, creditRequest);
      Assert.Equal(note.Value, noteReplay.Value);
      Assert.Equal(ErrorCodes.IdempotencyConflict,
        (await BillingService.IssueCreditNoteAsync(db, fixture.ManagerActor, creditRequest with { Amount = 21 })).ErrorCode);

      var receiptRequest = new RecordReceiptRequest(accountId, 200, "BANK-100", RequestId: Guid.NewGuid());
      var recorded = await BillingService.RecordReceiptAsync(db, fixture.ManagerActor, receiptRequest);
      receiptId = recorded.Value;
      Assert.Equal(receiptId, (await BillingService.RecordReceiptAsync(db, fixture.ManagerActor, receiptRequest)).Value);
      Assert.Equal(ErrorCodes.IdempotencyConflict,
        (await BillingService.RecordReceiptAsync(db, fixture.ManagerActor, receiptRequest with { Amount = 201 })).ErrorCode);

      var allocationRequest = new AllocateReceiptRequest(receiptId, invoiceId, 200, Guid.NewGuid());
      var allocation = await BillingService.AllocateReceiptCommandAsync(db, fixture.ManagerActor, allocationRequest);
      Assert.True(allocation.Succeeded);
      var allocationReplay = await BillingService.AllocateReceiptCommandAsync(db, fixture.ManagerActor, allocationRequest);
      Assert.Equal(allocation.Value, allocationReplay.Value);
      Assert.Equal(ErrorCodes.IdempotencyConflict,
        (await BillingService.AllocateReceiptCommandAsync(db, fixture.ManagerActor, allocationRequest with { Amount = 199 })).ErrorCode);
      var over = await BillingService.AllocateReceiptAsync(db, fixture.ManagerActor,
        new AllocateReceiptRequest(receiptId, invoiceId, 1, Guid.NewGuid()));
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

      // Invoice discovery: complete per-currency totals use the detail balance formula; draft value is never receivable.
      var firmReviewer = User(fixture.FirmId, "FinanceReviewer");
      db.Users.Add(firmReviewer);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = firmReviewer.Id, Role = "FinanceReviewer",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = firmReviewer.Id
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-101", [new InvoiceLineRequest("Draft services", 1, 50)]))).Succeeded);
      var firmFinance = Actor(firmReviewer, "FinanceReviewer");
      var invoices = (await BillingWorkbenchQuery.PageAsync(db, firmFinance)).Value!;
      Assert.Equal(2, invoices.Total);
      Assert.Equal(new (string, string, int, decimal, decimal?, decimal?, decimal?)[]
        {
          ("QAR", BillingWorkbenchQuery.Posted, 1, 220m, 20m, 200m, 0m), ("QAR", BillingWorkbenchQuery.Unposted, 1, 50m, null, null, null)
        }, invoices.Totals.Select(t => (t.Currency, t.Basis, t.Count, t.Total, t.Credited, t.Allocated, t.Outstanding)));
      var sentRow = invoices.Items.Single(x => x.Id == invoiceId);
      Assert.Equal((BillingStates.InvoiceSent, "OTHER", 20m, 200m, 0m, (DateOnly?)null, false),
        (sentRow.Status, sentRow.Kind, sentRow.Credited, sentRow.Allocated, sentRow.Outstanding, sentRow.ApprovedDueDate, sentRow.PaymentTermsPending));
      Assert.Equal(invoiceId, Assert.Single((await BillingWorkbenchQuery.PageAsync(db, firmFinance, new(Number: "inv-100"))).Value!.Items).Id);
      Assert.Equal(1, (await BillingWorkbenchQuery.PageAsync(db, firmFinance, new(Status: BillingStates.InvoiceDraft))).Value!.Total);
      Assert.Equal(0, (await BillingWorkbenchQuery.PageAsync(db, firmFinance, new(Currency: "USD"))).Value!.Total);
      Assert.Equal(0, (await BillingWorkbenchQuery.PageAsync(db, firmFinance, new(ClientId: otherClientId))).Value!.Total);
      Assert.Equal("request.invalid", (await BillingWorkbenchQuery.PageAsync(db, firmFinance, new(Status: "PAID"))).ErrorCode);
      // A client-scoped finance grant opens that client's invoices but not the firm-wide list or its totals.
      Assert.False((await BillingWorkbenchQuery.PageAsync(db, fixture.ManagerActor)).Succeeded);
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
  public async Task ConcurrentReceiptAllocations_RecheckRevokedAuthorityAndRefuseExcessWithoutClamping()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid invoiceId, receiptId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var account = await BillingService.CreateBillingAccountAsync(setup, fixture.ManagerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"));
      Assert.True(account.Succeeded, account.Message);
      var invoice = await BillingService.CreateInvoiceDraftAsync(setup, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(account.Value, "INV-RECEIPT-RACE",
          [new InvoiceLineRequest("Synthetic reviewed services", 1m, 200m)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      invoiceId = invoice.Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(setup, fixture.ManagerActor, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(setup, fixture.ReviewerActor, invoiceId)).Succeeded);
      setup.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await setup.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(setup, fixture.ManagerActor, invoiceId)).Succeeded);
      var receipt = await BillingService.RecordReceiptAsync(setup, fixture.ManagerActor,
        new RecordReceiptRequest(account.Value, 100m, "BANK-RECEIPT-RACE", RequestId: Guid.NewGuid()));
      Assert.True(receipt.Succeeded, receipt.Message);
      receiptId = receipt.Value;
    }

    // Both permitted actors pass initial authorization before either can publish an allocation.
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var transaction = await blocker.Database.BeginTransactionAsync(deadline.Token);
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {fixture.FirmId} FOR UPDATE")
      .SingleAsync(deadline.Token);
    var firstLock = new ObserveBillingFirmLock();
    var secondLock = new ObserveBillingFirmLock();
    await using var firstWriter = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(firstLock).Options);
    await using var secondWriter = new AuditSphereDbContext(
      new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(secondLock).Options);
    var permittedRequest = new AllocateReceiptRequest(receiptId, invoiceId, 80m, Guid.NewGuid());
    var revokedRequest = permittedRequest with { RequestId = Guid.NewGuid() };
    var permitted = BillingService.AllocateReceiptCommandAsync(
      firstWriter, fixture.ManagerActor, permittedRequest, deadline.Token);
    var revoked = BillingService.AllocateReceiptCommandAsync(
      secondWriter, fixture.ReviewerActor, revokedRequest, deadline.Token);
    await Task.WhenAll(firstLock.Reached.Task, secondLock.Reached.Task).WaitAsync(deadline.Token);
    Assert.False(permitted.IsCompleted);
    Assert.False(revoked.IsCompleted);
    Assert.Equal(1, await blocker.RoleGrants.Where(x => x.FirmId == fixture.FirmId &&
      x.UserId == fixture.Reviewer.Id && x.Role == "FinanceReviewer" && x.RevokedAt == null)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), deadline.Token));
    await transaction.CommitAsync(deadline.Token);

    var outcomes = await Task.WhenAll(permitted, revoked).WaitAsync(deadline.Token);
    Assert.True(outcomes[0].Succeeded, outcomes[0].Message);
    Assert.False(outcomes[1].Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, outcomes[1].ErrorCode);
    Assert.Null(outcomes[1].Value);
    await using var proof = new AuditSphereDbContext(pg.Options);
    var allocation = Assert.Single(await proof.ReceiptAllocations.Where(x => x.ReceiptId == receiptId).ToListAsync());
    Assert.Equal((invoiceId, 80m), (allocation.InvoiceId, allocation.Amount));
    var retained = Assert.Single(await proof.BillingCommandReceipts.Where(x => x.Action == "ALLOCATE_RECEIPT").ToListAsync());
    Assert.Equal((fixture.Manager.Id, permittedRequest.RequestId, allocation.Id),
      (retained.ActorId, retained.RequestId, retained.ResultId));

    // Only 20 remains: a stale request for 30 must fail, never be clamped to that remainder.
    var excess = await BillingService.AllocateReceiptCommandAsync(proof, fixture.ManagerActor,
      permittedRequest with { Amount = 30m, RequestId = Guid.NewGuid() }, deadline.Token);
    Assert.False(excess.Succeeded);
    Assert.Equal("billing.over-allocation", excess.ErrorCode);
    Assert.Single(await proof.ReceiptAllocations.Where(x => x.ReceiptId == receiptId).ToListAsync());
    Assert.Single(await proof.BillingCommandReceipts.Where(x => x.Action == "ALLOCATE_RECEIPT").ToListAsync());
    var posted = await proof.Invoices.AsNoTracking().SingleAsync(x => x.Id == invoiceId);
    Assert.Equal((BillingStates.InvoicePosted, 200m), (posted.Status, posted.Total));
    var balance = await BillingService.GetInvoiceBalanceAsync(proof, fixture.ManagerActor, invoiceId);
    Assert.True(balance.Succeeded, balance.Message);
    Assert.Equal((200m, 80m, 120m), (balance.Value!.Total, balance.Value.Allocated, balance.Value.Outstanding));
  }

  private sealed class ObserveBillingFirmLock : DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Reached.TrySetResult(true);
      return ValueTask.FromResult(result);
    }
  }

  [Fact]
  public async Task AllocateReceipt_RejectsWrongCurrencyAndPreservesLegacyNullCurrencyCompatibility()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid accountId;
    Guid invoiceId;
    Guid wrongCurrencyReceiptId = Guid.CreateVersion7();
    Guid legacyReceiptId = Guid.CreateVersion7();
    long invoiceRevision;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      accountId = (await BillingService.CreateBillingAccountAsync(db, fixture.ManagerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"))).Value;
      invoiceId = (await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "SYN-RECEIPT-CURRENCY-001", [new InvoiceLineRequest("Synthetic fee", 1m, 100m)]))).Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, fixture.ReviewerActor, invoiceId)).Succeeded);
      db.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      Assert.True((await BillingService.PostInvoiceAsync(db, fixture.ManagerActor, invoiceId)).Succeeded);

      var now = DateTimeOffset.UtcNow;
      db.Receipts.AddRange(
        new Receipt
        {
          Id = wrongCurrencyReceiptId, FirmId = fixture.FirmId, BillingAccountId = accountId,
          Amount = 10m, Currency = "USD", Reference = "SYN-WRONG-CURRENCY-RECEIPT",
          Status = BillingStates.ReceiptRecorded, RecordedByUserId = fixture.Manager.Id, ReceivedAt = now
        },
        new Receipt
        {
          Id = legacyReceiptId, FirmId = fixture.FirmId, BillingAccountId = accountId,
          Amount = 10m, Currency = null, Reference = "SYN-LEGACY-NULL-CURRENCY-RECEIPT",
          Status = BillingStates.ReceiptRecorded, RecordedByUserId = fixture.Manager.Id, ReceivedAt = now
        });
      await db.SaveChangesAsync();
      invoiceRevision = await db.Invoices.Where(x => x.FirmId == fixture.FirmId && x.Id == invoiceId)
        .Select(x => x.Revision).SingleAsync();
    }

    var wrongRequestId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var refused = await BillingService.AllocateReceiptCommandAsync(db, fixture.ManagerActor,
        new AllocateReceiptRequest(wrongCurrencyReceiptId, invoiceId, 10m, wrongRequestId));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      Assert.Null(refused.Value);
    }

    await using (var verify = new AuditSphereDbContext(pg.Options))
    {
      Assert.Empty(await verify.ReceiptAllocations.Where(x => x.FirmId == fixture.FirmId && x.ReceiptId == wrongCurrencyReceiptId).ToListAsync());
      Assert.False(await verify.BillingCommandReceipts.AnyAsync(x => x.FirmId == fixture.FirmId && x.RequestId == wrongRequestId));
      Assert.Equal(invoiceRevision, await verify.Invoices.Where(x => x.FirmId == fixture.FirmId && x.Id == invoiceId)
        .Select(x => x.Revision).SingleAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var legacy = await BillingService.AllocateReceiptCommandAsync(db, fixture.ManagerActor,
        new AllocateReceiptRequest(legacyReceiptId, invoiceId, 10m, Guid.CreateVersion7()));
      Assert.True(legacy.Succeeded, legacy.Message);
    }

    await using var final = new AuditSphereDbContext(pg.Options);
    var retained = Assert.Single(await final.ReceiptAllocations.Where(x => x.FirmId == fixture.FirmId).ToListAsync());
    Assert.Equal(legacyReceiptId, retained.ReceiptId);
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
      var workspace = await BillingInvoiceWorkspaceQuery.GetAsync(db, fixture.ManagerActor, invoiceId);
      Assert.True(workspace.Succeeded, workspace.Message);
      var source = Assert.Single(workspace.Value!.LineSources!);
      Assert.Equal("AVAILABLE", source.State);
      Assert.Equal(entryId, source.SourceId);
      Assert.Equal(1L, source.SourceRevision);
      Assert.Equal(90, source.DurationMinutes);
      Assert.Null(source.FeeAgreementId);
      Assert.False((await BillingInvoiceWorkspaceQuery.GetAsync(db, fixture.PartnerActor, invoiceId)).Succeeded);
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

  [Fact]
  public async Task ReservedTimeCannotBeCorrectedOrRebilledThroughAnExistingCorrectionLineage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var sourceId = await SeedTimeAsync(pg, fixture, PracticeTimeStates.TimeApproved);
    var correctionActor = new ActorContext(fixture.Manager.Id, fixture.FirmId, fixture.Manager.SessionEpoch, ["Manager"]);
    Guid accountId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Manager, "Manager", fixture.ClientId));
      await db.SaveChangesAsync();
      accountId = (await BillingService.CreateBillingAccountAsync(db, fixture.ManagerActor,
        new CreateBillingAccountRequest(fixture.ClientId, "QAR"))).Value;
      var first = await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-TIME-RESERVED", [
          new InvoiceLineRequest("Original approved time", 1.5m, 300m, "TIME", sourceId, 1)
        ]));
      Assert.True(first.Succeeded, first.Message);

      var reviewedCorrection = await PracticeTimeService.ReviseTimeEntryAsync(db, correctionActor,
        new ReviseTimeEntryRequest(sourceId, Guid.NewGuid(), 1, "CORRECT_APPROVED",
          new DateOnly(2026, 3, 2), 540, 120, "Billing source", "Attempt to alter invoiced time",
          "Correct after invoice reservation"));
      Assert.False(reviewedCorrection.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, reviewedCorrection.ErrorCode);
      Assert.Equal(0, await db.Set<TimeEntryChangeReceipt>().CountAsync());

      var correction = await PracticeTimeService.CorrectTimeAsync(db, correctionActor,
        new CorrectTimeRequest(sourceId, new DateOnly(2026, 3, 2), 540, 120,
          "Billing source", "Attempt to alter invoiced time", Reason: "Correct after invoice reservation"));
      Assert.Equal(ErrorCodes.ProtectedState, correction.ErrorCode);
      var original = await db.TimeEntries.SingleAsync(x => x.Id == sourceId);
      Assert.Equal(PracticeTimeStates.TimeApproved, original.Status);
      Assert.Equal(0, await db.TimeEntries.CountAsync(x => x.SupersedesId == sourceId));

      // Simulate a retained legacy correction chain created before both reservation guards existed.
      original.Status = PracticeTimeStates.TimeSuperseded;
      var now = DateTimeOffset.UtcNow;
      var legacyCorrectionId = Guid.NewGuid();
      db.TimeEntries.Add(new TimeEntry
      {
        Id = legacyCorrectionId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, TaskId = original.TaskId,
        UserId = original.UserId, WorkDate = original.WorkDate, StartMinute = original.StartMinute,
        DurationMinutes = original.DurationMinutes, Role = original.Role, Activity = original.Activity,
        Phase = original.Phase, RiskArea = original.RiskArea, MappingVersionId = original.MappingVersionId,
        FsliCode = original.FsliCode, BillableClassification = original.BillableClassification,
        Narrative = "Retained legacy correction", NarrativeVisibility = original.NarrativeVisibility,
        Status = PracticeTimeStates.TimeApproved, Currency = original.Currency, Revision = original.Revision + 1,
        SupersedesId = original.Id, RateCardVersionId = original.RateCardVersionId, RatePerHour = original.RatePerHour,
        ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = now, CreatedAt = now
      });
      await db.SaveChangesAsync();

      var duplicate = await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
        new CreateInvoiceDraftRequest(accountId, "INV-TIME-CORRECTION-DUPLICATE", [
          new InvoiceLineRequest("Legacy corrected time", 1.5m, 300m, "TIME", legacyCorrectionId, 2)
        ]));
      Assert.Equal("billing.source-duplicate", duplicate.ErrorCode);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.Invoices.CountAsync());
    Assert.Equal(1, await verify.BillingSourceAllocations.CountAsync());
    Assert.Equal(2, await verify.TimeEntries.CountAsync());
  }

  [Fact]
  public async Task AdvanceInvoiceIdentity_UsesFullAgreementGuidAndRelinksLegacyDraft()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    Guid[] agreementIds =
    [
      Guid.Parse("a17f0c42-0000-7000-8000-000000000001"),
      Guid.Parse("a17f0c42-0000-7000-8000-000000000002"),
      Guid.Parse("a17f0c42-0000-7000-8000-000000000003"),
      Guid.Parse("a17f0c42-0000-7000-8000-000000000004"),
      Guid.Parse("a17f0c42-0000-7000-8000-000000000005")
    ];
    Assert.Equal(5, agreementIds.Distinct().Count());
    Assert.Single(agreementIds.Select(id => id.ToString("N")[..8]).Distinct(StringComparer.OrdinalIgnoreCase));

    const decimal agreedFee = 1200m;
    const decimal advanceAmount = 600m;
    var milestoneIds = new Guid[agreementIds.Length];
    await using var db = new AuditSphereDbContext(pg.Options);
    for (var index = 0; index < agreementIds.Length; index++)
    {
      var leadId = Guid.CreateVersion7();
      var opportunityId = Guid.CreateVersion7();
      var proposalId = Guid.CreateVersion7();
      var quotationId = Guid.CreateVersion7();
      var milestoneId = Guid.CreateVersion7();

      db.Leads.Add(new Lead
      {
        Id = leadId, FirmId = fixture.FirmId, Name = $"Synthetic billing lead {index + 1}",
        Source = "TEST", Status = CrmStates.LeadQualified, CreatedAt = now
      });
      db.Opportunities.Add(new Opportunity
      {
        Id = opportunityId, FirmId = fixture.FirmId, LeadId = leadId, PracticeClientId = fixture.ClientId,
        ServiceRoute = "FinancialStatementAudit", EntityScope = "Synthetic test scope",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ExpectedFee = agreedFee,
        Currency = "QAR", Stage = CrmStates.OpportunityProposal, CreatedAt = now
      });
      db.Proposals.Add(new Proposal
      {
        Id = proposalId, FirmId = fixture.FirmId, OpportunityId = opportunityId, PracticeClientId = fixture.ClientId,
        Status = CrmStates.ProposalDraft, ServiceProfileId = "synthetic-test-profile", Scope = "Synthetic scope",
        Deliverables = "Synthetic deliverable", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Fee = agreedFee, Currency = "QAR", CreatedAt = now
      });
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = fixture.FirmId, ProposalId = proposalId, Currency = "QAR",
        BaseAmount = agreedFee, Fee = agreedFee, InputHash = new string('a', 64),
        CreatedByUserId = fixture.Manager.Id, CreatedAt = now
      });
      db.EngagementFeeAgreements.Add(new EngagementFeeAgreement
      {
        Id = agreementIds[index], FirmId = fixture.FirmId, ProposalId = proposalId, QuotationVersionId = quotationId,
        PracticeClientId = fixture.ClientId, Currency = "QAR", AgreedFee = agreedFee,
        AdvancePercent = 50m, CreatedByUserId = fixture.Manager.Id, CreatedAt = now
      });
      db.FeeMilestones.Add(new FeeMilestone
      {
        Id = milestoneId, FirmId = fixture.FirmId, AgreementId = agreementIds[index],
        Kind = FeeMilestoneKinds.Advance, Amount = advanceAmount, State = FeeMilestoneStates.Planned, CreatedAt = now
      });
      milestoneIds[index] = milestoneId;
    }

    await db.SaveChangesAsync();
    // These rows establish source identity only; no proposal acceptance, commercial dispatch, or approval evidence is asserted.
    var issuedInvoiceIds = new Guid[2];
    for (var index = 0; index < issuedInvoiceIds.Length; index++)
    {
      var issued = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, fixture.ManagerActor, agreementIds[index]);
      Assert.True(issued.Succeeded, issued.Message);
      db.ChangeTracker.Clear();
      var replay = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, fixture.ManagerActor, agreementIds[index]);
      Assert.True(replay.Succeeded, replay.Message);
      Assert.Equal(issued.Value, replay.Value);
      issuedInvoiceIds[index] = issued.Value;
    }
    Assert.NotEqual(issuedInvoiceIds[0], issuedInvoiceIds[1]);

    var accountId = await db.BillingAccounts.AsNoTracking()
      .Where(x => x.FirmId == fixture.FirmId && x.PracticeClientId == fixture.ClientId)
      .Select(x => x.Id).SingleAsync();
    var legacyNumber = $"AGR-{agreementIds[2].ToString("N")[..8].ToUpperInvariant()}-ADV";
    var legacyDraft = await BillingService.CreateInvoiceDraftAsync(db, fixture.ManagerActor,
      new CreateInvoiceDraftRequest(accountId, legacyNumber,
      [new InvoiceLineRequest("Legacy advance fee draft", 1m, advanceAmount,
        FeeAgreementService.MilestoneSourceKind, milestoneIds[2], 1)]));
    Assert.True(legacyDraft.Succeeded, legacyDraft.Message);
    var unlinkedMilestone = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Id == milestoneIds[2]);
    Assert.Null(unlinkedMilestone.InvoiceId);
    Assert.Equal(FeeMilestoneStates.Planned, unlinkedMilestone.State);

    db.ChangeTracker.Clear(); // Model recovery after the draft committed but before the milestone link was saved.
    var relinked = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, fixture.ManagerActor, agreementIds[2]);
    Assert.True(relinked.Succeeded, relinked.Message);
    Assert.Equal(legacyDraft.Value, relinked.Value);
    db.ChangeTracker.Clear();
    var relinkReplay = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, fixture.ManagerActor, agreementIds[2]);
    Assert.True(relinkReplay.Succeeded, relinkReplay.Message);
    Assert.Equal(legacyDraft.Value, relinkReplay.Value);

    await using var blocker = new AuditSphereDbContext(pg.Options);
    await blocker.Database.OpenConnectionAsync();
    await using var gate = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {fixture.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();

    await using var firstCommandDb = new AuditSphereDbContext(pg.Options);
    await using var secondCommandDb = new AuditSphereDbContext(pg.Options);
    using var commandTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await firstCommandDb.Database.OpenConnectionAsync(commandTimeout.Token);
    await secondCommandDb.Database.OpenConnectionAsync(commandTimeout.Token);
    var firstCommandPid = ((NpgsqlConnection)firstCommandDb.Database.GetDbConnection()).ProcessID;
    var secondCommandPid = ((NpgsqlConnection)secondCommandDb.Database.GetDbConnection()).ProcessID;
    await using var observer = new AuditSphereDbContext(pg.Options);
    var firstConcurrentIssue = FeeAgreementService.IssueAdvanceInvoiceAsync(firstCommandDb,
      fixture.ManagerActor, agreementIds[3], commandTimeout.Token);
    var secondConcurrentIssue = FeeAgreementService.IssueAdvanceInvoiceAsync(secondCommandDb,
      fixture.ManagerActor, agreementIds[3], commandTimeout.Token);
    using var observationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var bothCommandsBlocked = false;
    CommandResult<Guid>[]? concurrentResults = null;
    try
    {
      await observer.Database.OpenConnectionAsync(observationTimeout.Token);
      while (!observationTimeout.IsCancellationRequested)
      {
        await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", observationTimeout.Token);
        bothCommandsBlocked = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database()
              AND waiting.pid = {firstCommandPid}
              AND waiting.wait_event_type = 'Lock'
              AND waiting.query ILIKE '%firm_safety_states%'
              AND cardinality(pg_blocking_pids(waiting.pid)) > 0
          ) AND EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database()
              AND waiting.pid = {secondCommandPid}
              AND waiting.wait_event_type = 'Lock'
              AND waiting.query ILIKE '%firm_safety_states%'
              AND cardinality(pg_blocking_pids(waiting.pid)) > 0
          ) AS "Value"
          """).SingleAsync(observationTimeout.Token);
        if (bothCommandsBlocked) break;
        await Task.Delay(TimeSpan.FromMilliseconds(25), observationTimeout.Token);
      }
    }
    catch (OperationCanceledException) when (observationTimeout.IsCancellationRequested)
    {
      // Release the firm fence before asserting that PostgreSQL did not expose both blocked commands.
    }
    finally
    {
      await gate.RollbackAsync(CancellationToken.None);
      try
      {
        concurrentResults = await Task.WhenAll(firstConcurrentIssue, secondConcurrentIssue)
          .WaitAsync(TimeSpan.FromSeconds(20));
      }
      catch (TimeoutException)
      {
        commandTimeout.Cancel();
        await Task.WhenAll(firstConcurrentIssue, secondConcurrentIssue).WaitAsync(TimeSpan.FromSeconds(5));
        throw;
      }
    }

    Assert.NotEqual(firstCommandPid, secondCommandPid);
    Assert.True(bothCommandsBlocked,
      "PostgreSQL did not show both commands waiting on the firm safety fence with active blockers.");
    var concurrentOutcomes = concurrentResults ?? throw new InvalidOperationException(
      "The bounded concurrent issue commands did not produce results.");
    Assert.True(concurrentOutcomes[0].Succeeded, concurrentOutcomes[0].Message);
    Assert.True(concurrentOutcomes[1].Succeeded, concurrentOutcomes[1].Message);
    Assert.Equal(concurrentOutcomes[0].Value, concurrentOutcomes[1].Value);
    var concurrentInvoiceId = concurrentOutcomes[0].Value;

    var invoiceIds = new[] { issuedInvoiceIds[0], issuedInvoiceIds[1], legacyDraft.Value, concurrentInvoiceId };
    var invoices = await db.Invoices.AsNoTracking().Where(x => invoiceIds.Contains(x.Id)).ToListAsync();
    Assert.Equal(4, invoices.Count);
    Assert.Equal($"AGR-{agreementIds[0].ToString("N").ToUpperInvariant()}-ADV",
      invoices.Single(x => x.Id == issuedInvoiceIds[0]).InvoiceNumber);
    Assert.Equal($"AGR-{agreementIds[1].ToString("N").ToUpperInvariant()}-ADV",
      invoices.Single(x => x.Id == issuedInvoiceIds[1]).InvoiceNumber);
    Assert.Equal(legacyNumber, invoices.Single(x => x.Id == legacyDraft.Value).InvoiceNumber);
    Assert.Equal($"AGR-{agreementIds[3].ToString("N").ToUpperInvariant()}-ADV",
      invoices.Single(x => x.Id == concurrentInvoiceId).InvoiceNumber);
    Assert.Equal(4, invoices.Select(x => x.InvoiceNumber).Distinct(StringComparer.Ordinal).Count());

    var invoiceIdsByMilestone = new[]
      { issuedInvoiceIds[0], issuedInvoiceIds[1], legacyDraft.Value, concurrentInvoiceId };
    for (var index = 0; index < invoiceIdsByMilestone.Length; index++)
    {
      var milestoneId = milestoneIds[index];
      var invoiceId = invoiceIdsByMilestone[index];
      Assert.Equal(1, await db.BillingSourceAllocations.CountAsync(x => x.FirmId == fixture.FirmId &&
        x.SourceKind == FeeAgreementService.MilestoneSourceKind && x.SourceId == milestoneId && x.SourceRevision == 1));
      var milestone = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Id == milestoneId);
      Assert.Equal(invoiceId, milestone.InvoiceId);
      Assert.Equal(FeeMilestoneStates.Invoiced, milestone.State);
      var workspace = await BillingInvoiceWorkspaceQuery.GetAsync(db, fixture.ManagerActor, invoiceId);
      Assert.True(workspace.Succeeded, workspace.Message);
      var source = Assert.Single(workspace.Value!.LineSources!);
      Assert.Equal("AVAILABLE", source.State);
      Assert.Equal(milestoneId, source.SourceId);
      Assert.Equal(agreementIds[index], source.FeeAgreementId);
    }

    var failedInvoiceNumber = $"AGR-{agreementIds[4].ToString("N").ToUpperInvariant()}-ADV";
    var syntheticLinkFailure = new InvalidOperationException("Synthetic fee milestone link persistence failure.");
    var failingOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(new RejectFeeMilestoneLink(milestoneIds[4], syntheticLinkFailure)).Options;
    await using (var failingDb = new AuditSphereDbContext(failingOptions))
    {
      var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        await FeeAgreementService.IssueAdvanceInvoiceAsync(failingDb, fixture.ManagerActor, agreementIds[4]));
      Assert.Same(syntheticLinkFailure, thrown);
    }

    await using (var verifyRollback = new AuditSphereDbContext(pg.Options))
    {
      var failedMilestone = await verifyRollback.FeeMilestones.AsNoTracking()
        .SingleAsync(x => x.Id == milestoneIds[4]);
      Assert.Null(failedMilestone.InvoiceId);
      Assert.Equal(FeeMilestoneStates.Planned, failedMilestone.State);
      Assert.Equal(0, await verifyRollback.Invoices.CountAsync(x => x.FirmId == fixture.FirmId &&
        x.InvoiceNumber == failedInvoiceNumber));
      Assert.Equal(0, await verifyRollback.InvoiceLines.CountAsync(x => x.FirmId == fixture.FirmId &&
        x.SourceKind == FeeAgreementService.MilestoneSourceKind && x.SourceId == milestoneIds[4] &&
        x.SourceRevision == 1));
      Assert.Equal(0, await verifyRollback.BillingSourceAllocations.CountAsync(x => x.FirmId == fixture.FirmId &&
        x.SourceKind == FeeAgreementService.MilestoneSourceKind && x.SourceId == milestoneIds[4] &&
        x.SourceRevision == 1));
    }

    await using var retryDb = new AuditSphereDbContext(pg.Options);
    var retried = await FeeAgreementService.IssueAdvanceInvoiceAsync(retryDb, fixture.ManagerActor, agreementIds[4]);
    Assert.True(retried.Succeeded, retried.Message);
    var retriedMilestone = await retryDb.FeeMilestones.AsNoTracking().SingleAsync(x => x.Id == milestoneIds[4]);
    Assert.Equal(retried.Value, retriedMilestone.InvoiceId);
    Assert.Equal(FeeMilestoneStates.Invoiced, retriedMilestone.State);
    Assert.Equal(1, await retryDb.Invoices.CountAsync(x => x.FirmId == fixture.FirmId &&
      x.InvoiceNumber == failedInvoiceNumber));
    Assert.Equal(1, await retryDb.InvoiceLines.CountAsync(x => x.FirmId == fixture.FirmId &&
      x.SourceKind == FeeAgreementService.MilestoneSourceKind && x.SourceId == milestoneIds[4] &&
      x.SourceRevision == 1));
    Assert.Equal(1, await retryDb.BillingSourceAllocations.CountAsync(x => x.FirmId == fixture.FirmId &&
      x.SourceKind == FeeAgreementService.MilestoneSourceKind && x.SourceId == milestoneIds[4] &&
      x.SourceRevision == 1));

    // Payment identity recovery covers the same five source-bound invoices without relying on commercial approval history.
    var paymentInvoiceIds = new[]
      { issuedInvoiceIds[0], issuedInvoiceIds[1], legacyDraft.Value, concurrentInvoiceId, retried.Value };
    await using (var profileDb = new AuditSphereDbContext(pg.Options))
    {
      profileDb.FirmFinanceProfiles.Add(new FirmFinanceProfile
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, FunctionalCurrency = "QAR",
        ProfileKind = BillingStates.TestProfile, Approved = true,
        ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await profileDb.SaveChangesAsync();
    }

    await using var cachedBillingDb = new AuditSphereDbContext(pg.Options);
    var cachedDraft = await cachedBillingDb.Invoices.SingleAsync(x => x.Id == issuedInvoiceIds[0]);
    Assert.Equal(BillingStates.InvoiceDraft, cachedDraft.Status);
    foreach (var paymentInvoiceId in paymentInvoiceIds)
    {
      await using (var submitDb = new AuditSphereDbContext(pg.Options))
      {
        var submitted = await BillingService.SubmitInvoiceAsync(submitDb, fixture.ManagerActor, paymentInvoiceId);
        Assert.True(submitted.Succeeded, submitted.Message);
      }
      await using (var reviewDb = new AuditSphereDbContext(pg.Options))
      {
        var approved = await BillingService.ApproveInvoiceAsync(reviewDb, fixture.ReviewerActor, paymentInvoiceId);
        Assert.True(approved.Succeeded, approved.Message);
      }
      await using (var postDb = new AuditSphereDbContext(pg.Options))
      {
        var posted = await BillingService.PostInvoiceAsync(postDb, fixture.ManagerActor, paymentInvoiceId);
        Assert.True(posted.Succeeded, posted.Message);
      }
    }

    async Task<CommandResult<AdvancePaymentOutcome>> RecordFeePaymentAsync(
      Guid agreementId, ActorContext actor, decimal amount, string externalReference)
    {
      await using var paymentDb = new AuditSphereDbContext(pg.Options);
      return await FeeAgreementService.RecordAdvancePaymentAsync(
        paymentDb, actor, agreementId, amount, externalReference);
    }

    async Task<Guid> RecordReceiptAsync(
      ActorContext actor, decimal amount, string storedReference, Guid requestId, DateTimeOffset? receivedAt = null)
    {
      await using var receiptDb = new AuditSphereDbContext(pg.Options);
      var recorded = await BillingService.RecordReceiptAsync(receiptDb, actor,
        new RecordReceiptRequest(accountId, amount, storedReference, ReceivedAt: receivedAt, RequestId: requestId));
      Assert.True(recorded.Succeeded, recorded.Message);
      return recorded.Value;
    }

    string LegacyPaymentReference(Guid agreementId, string externalReference) =>
      $"ADV-{agreementId.ToString("N")[..8].ToUpperInvariant()}-{externalReference}";

    Guid LegacyReceiptRequestId(Guid agreementId, string storedReference) =>
      BillingCommandWorkspace.DeriveRequestId(
        $"fee-milestone:receipt:{agreementId:N}:{FeeMilestoneKinds.Advance}:{storedReference}");

    // Both same-prefix agreements receive the same 120-character external reference. Assert that both commands
    // succeed and retain distinct receipt identities before checking their stored references. On the old short-GUID
    // implementation, the second command incorrectly reuses the first receipt and fails over-allocation here.
    var longSharedExternalReference = new string('L', 120);
    var firstSharedPayment = await RecordFeePaymentAsync(
      agreementIds[0], fixture.ManagerActor, 10m, longSharedExternalReference);
    Assert.True(firstSharedPayment.Succeeded, firstSharedPayment.Message);
    var secondSharedPayment = await RecordFeePaymentAsync(
      agreementIds[1], fixture.ManagerActor, 10m, longSharedExternalReference);
    Assert.True(secondSharedPayment.Succeeded, secondSharedPayment.Message);
    Assert.NotEqual(firstSharedPayment.Value!.ReceiptId, secondSharedPayment.Value!.ReceiptId);

    await using (var verifySharedPayments = new AuditSphereDbContext(pg.Options))
    {
      var firstReceipt = await verifySharedPayments.Receipts.AsNoTracking()
        .SingleAsync(x => x.FirmId == fixture.FirmId && x.Id == firstSharedPayment.Value.ReceiptId);
      var secondReceipt = await verifySharedPayments.Receipts.AsNoTracking()
        .SingleAsync(x => x.FirmId == fixture.FirmId && x.Id == secondSharedPayment.Value.ReceiptId);
      Assert.Equal($"ADV-{agreementIds[0].ToString("N").ToUpperInvariant()}-{longSharedExternalReference}", firstReceipt.Reference);
      Assert.Equal($"ADV-{agreementIds[1].ToString("N").ToUpperInvariant()}-{longSharedExternalReference}", secondReceipt.Reference);
      Assert.True(firstReceipt.Reference.Length <= 200);
      Assert.True(secondReceipt.Reference.Length <= 200);
      Assert.NotEqual(firstReceipt.Reference, secondReceipt.Reference);
    }

    const string thirdLegacyExternalReference = "LEGACY-PRE-ALLOCATION";
    var thirdLegacyStoredReference = LegacyPaymentReference(agreementIds[2], thirdLegacyExternalReference);
    var thirdLegacyRequestId = LegacyReceiptRequestId(agreementIds[2], thirdLegacyStoredReference);
    var thirdLegacyReceivedAt = new DateTimeOffset(2026, 3, 15, 10, 30, 0, TimeSpan.Zero).AddTicks(1_234_567);
    var receiptsBeforeThirdLegacy = 0;
    await using (var beforeLegacy = new AuditSphereDbContext(pg.Options))
      receiptsBeforeThirdLegacy = await beforeLegacy.Receipts.CountAsync(x => x.FirmId == fixture.FirmId);
    var thirdLegacyReceiptId = await RecordReceiptAsync(
      fixture.ManagerActor, 10m, thirdLegacyStoredReference, thirdLegacyRequestId, thirdLegacyReceivedAt);
    await using (var verifyThirdLegacySource = new AuditSphereDbContext(pg.Options))
    {
      Assert.False(await verifyThirdLegacySource.ReceiptAllocations.AsNoTracking().AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == thirdLegacyReceiptId));
      var commandReceipt = await verifyThirdLegacySource.BillingCommandReceipts.AsNoTracking().SingleAsync(x =>
        x.FirmId == fixture.FirmId && x.ClientId == fixture.ClientId && x.ActorId == fixture.Manager.Id &&
        x.RequestId == thirdLegacyRequestId);
      var outcome = JsonSerializer.Deserialize<BillingCommandOutcome>(commandReceipt.OutcomeJson);
      Assert.Equal("RECORD_RECEIPT", commandReceipt.Action);
      Assert.Equal(thirdLegacyReceiptId, commandReceipt.ResultId);
      Assert.Equal(thirdLegacyReceiptId, outcome!.ResultId);
      Assert.Equal(thirdLegacyReceiptId, outcome.ReceiptId);
      Assert.Equal("10", outcome.Amount);
      var persistedReceivedAt = await verifyThirdLegacySource.Receipts.AsNoTracking()
        .Where(x => x.FirmId == fixture.FirmId && x.Id == thirdLegacyReceiptId)
        .Select(x => x.ReceivedAt).SingleAsync();
      Assert.NotEqual(thirdLegacyReceivedAt, persistedReceivedAt);
      Assert.Equal(0, persistedReceivedAt.UtcDateTime.Ticks % 10);
    }

    // A different currently-authorized finance actor recovers the old source-specific receipt before allocation.
    var thirdLegacyRecovery = await RecordFeePaymentAsync(
      agreementIds[2], fixture.ReviewerActor, 10m, thirdLegacyExternalReference);
    Assert.True(thirdLegacyRecovery.Succeeded, thirdLegacyRecovery.Message);
    Assert.Equal(thirdLegacyReceiptId, thirdLegacyRecovery.Value!.ReceiptId);
    await using (var verifyThirdLegacyRecovery = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(receiptsBeforeThirdLegacy + 1,
        await verifyThirdLegacyRecovery.Receipts.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(1, await verifyThirdLegacyRecovery.ReceiptAllocations.CountAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == thirdLegacyReceiptId && x.InvoiceId == legacyDraft.Value));
      Assert.False(await verifyThirdLegacyRecovery.ReceiptAllocations.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == thirdLegacyReceiptId && x.InvoiceId != legacyDraft.Value));
    }

    // The same legacy alias already has a receipt proven to belong only to agreement 3. Agreement 4 must ignore that
    // proven-other row and record its own full-source payment using the same external reference.
    var provenOtherPayment = await RecordFeePaymentAsync(
      agreementIds[3], fixture.ManagerActor, 10m, thirdLegacyExternalReference);
    Assert.True(provenOtherPayment.Succeeded, provenOtherPayment.Message);
    Assert.NotEqual(thirdLegacyReceiptId, provenOtherPayment.Value!.ReceiptId);
    await using (var verifyProvenOther = new AuditSphereDbContext(pg.Options))
    {
      var receipt = await verifyProvenOther.Receipts.AsNoTracking()
        .SingleAsync(x => x.FirmId == fixture.FirmId && x.Id == provenOtherPayment.Value.ReceiptId);
      Assert.Equal($"ADV-{agreementIds[3].ToString("N").ToUpperInvariant()}-{thirdLegacyExternalReference}", receipt.Reference);
      Assert.Equal(1, await verifyProvenOther.ReceiptAllocations.CountAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == thirdLegacyReceiptId && x.InvoiceId == legacyDraft.Value));
      Assert.False(await verifyProvenOther.ReceiptAllocations.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == thirdLegacyReceiptId && x.InvoiceId == concurrentInvoiceId));
    }

    // A generic legacy receipt with a random request identity can be recovered only through its actual allocation
    // to this invoice, whose immutable source line is already bound to agreement 4's milestone.
    const string fourthLegacyExternalReference = "LEGACY-ALLOCATED";
    var fourthLegacyStoredReference = LegacyPaymentReference(agreementIds[3], fourthLegacyExternalReference);
    var fourthLegacyReceiptId = await RecordReceiptAsync(
      fixture.ManagerActor, 1m, fourthLegacyStoredReference, Guid.CreateVersion7());
    var fourthLegacyAllocationId = Guid.CreateVersion7();
    await using (var allocateFourthLegacy = new AuditSphereDbContext(pg.Options))
    {
      var allocation = await BillingService.AllocateReceiptCommandAsync(allocateFourthLegacy, fixture.ManagerActor,
        new AllocateReceiptRequest(fourthLegacyReceiptId, concurrentInvoiceId, 1m, fourthLegacyAllocationId));
      Assert.True(allocation.Succeeded, allocation.Message);
      Assert.Equal(concurrentInvoiceId, allocation.Value!.InvoiceId);
      Assert.Equal(fourthLegacyReceiptId, allocation.Value.ReceiptId);
    }
    int receiptsBeforeFourthRecovery;
    int allocationsBeforeFourthRecovery;
    await using (var beforeFourthRecovery = new AuditSphereDbContext(pg.Options))
    {
      receiptsBeforeFourthRecovery = await beforeFourthRecovery.Receipts.CountAsync(x => x.FirmId == fixture.FirmId);
      allocationsBeforeFourthRecovery = await beforeFourthRecovery.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId);
    }
    var fourthLegacyRecovery = await RecordFeePaymentAsync(
      agreementIds[3], fixture.ReviewerActor, 1m, fourthLegacyExternalReference);
    Assert.True(fourthLegacyRecovery.Succeeded, fourthLegacyRecovery.Message);
    Assert.Equal(fourthLegacyReceiptId, fourthLegacyRecovery.Value!.ReceiptId);
    await using (var verifyFourthRecovery = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(receiptsBeforeFourthRecovery,
        await verifyFourthRecovery.Receipts.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(allocationsBeforeFourthRecovery,
        await verifyFourthRecovery.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId));
    }

    // An unbound short-reference receipt is not enough to identify agreement 5's source payment.
    const string fifthUnboundExternalReference = "LEGACY-UNBOUND";
    var fifthUnboundStoredReference = LegacyPaymentReference(agreementIds[4], fifthUnboundExternalReference);
    var fifthUnboundReceiptId = await RecordReceiptAsync(
      fixture.ManagerActor, 1m, fifthUnboundStoredReference, Guid.CreateVersion7());
    int receiptsBeforeUnboundAttempt;
    int allocationsBeforeUnboundAttempt;
    await using (var beforeUnboundAttempt = new AuditSphereDbContext(pg.Options))
    {
      receiptsBeforeUnboundAttempt = await beforeUnboundAttempt.Receipts.CountAsync(x => x.FirmId == fixture.FirmId);
      allocationsBeforeUnboundAttempt = await beforeUnboundAttempt.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId);
    }
    var unboundAttempt = await RecordFeePaymentAsync(
      agreementIds[4], fixture.ManagerActor, 1m, fifthUnboundExternalReference);
    Assert.False(unboundAttempt.Succeeded);
    Assert.Equal("fee.reference-conflict", unboundAttempt.ErrorCode);
    await using (var verifyUnboundDenial = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(receiptsBeforeUnboundAttempt,
        await verifyUnboundDenial.Receipts.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(allocationsBeforeUnboundAttempt,
        await verifyUnboundDenial.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.False(await verifyUnboundDenial.ReceiptAllocations.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == fifthUnboundReceiptId));
      var milestone = await verifyUnboundDenial.FeeMilestones.AsNoTracking()
        .SingleAsync(x => x.FirmId == fixture.FirmId && x.Id == milestoneIds[4]);
      Assert.Equal(retried.Value, milestone.InvoiceId);
      Assert.Null(milestone.ReceiptId);
      Assert.Equal(FeeMilestoneStates.Invoiced, milestone.State);
    }

    // A source row with a well-formed but unrelated intent hash passes the database's format guard;
    // recovery must still reject it against the recorded receipt identity and amount.
    var fifthUnboundSourceRequestId = LegacyReceiptRequestId(agreementIds[4], fifthUnboundStoredReference);
    var unrelatedReceivedAt = new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);
    var unrelatedSourceHash = BillingCommandWorkspace.IntentHash(
      "RECORD_RECEIPT", fixture.FirmId, fixture.ClientId, accountId.ToString("D"),
      BillingCommandWorkspace.Decimal(1m), fifthUnboundStoredReference,
      unrelatedReceivedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
    await using (var seedInvalidSource = new AuditSphereDbContext(pg.Options))
    {
      seedInvalidSource.BillingCommandReceipts.Add(new BillingCommandReceipt
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        ActorId = fixture.Manager.Id, ActorEpoch = fixture.ManagerActor.SessionEpoch,
        RequestId = fifthUnboundSourceRequestId, Action = "RECORD_RECEIPT", RequestHash = unrelatedSourceHash,
        ResultId = fifthUnboundReceiptId,
        OutcomeJson = JsonSerializer.Serialize(new BillingCommandOutcome(
          "RECORD_RECEIPT", fifthUnboundReceiptId, fifthUnboundReceiptId, null, "1", unrelatedReceivedAt)),
        CreatedAt = now
      });
      await seedInvalidSource.SaveChangesAsync();
    }
    int receiptsBeforeInvalidHashAttempt;
    int allocationsBeforeInvalidHashAttempt;
    await using (var beforeInvalidHashAttempt = new AuditSphereDbContext(pg.Options))
    {
      receiptsBeforeInvalidHashAttempt = await beforeInvalidHashAttempt.Receipts.CountAsync(x => x.FirmId == fixture.FirmId);
      allocationsBeforeInvalidHashAttempt = await beforeInvalidHashAttempt.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId);
    }
    var invalidHashAttempt = await RecordFeePaymentAsync(
      agreementIds[4], fixture.ReviewerActor, 1m, fifthUnboundExternalReference);
    Assert.False(invalidHashAttempt.Succeeded);
    Assert.Equal("fee.reference-conflict", invalidHashAttempt.ErrorCode);
    await using (var verifyInvalidHashDenial = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(receiptsBeforeInvalidHashAttempt,
        await verifyInvalidHashDenial.Receipts.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(allocationsBeforeInvalidHashAttempt,
        await verifyInvalidHashDenial.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.False(await verifyInvalidHashDenial.ReceiptAllocations.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == fifthUnboundReceiptId));
      Assert.True(await verifyInvalidHashDenial.BillingCommandReceipts.AsNoTracking().AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ActorId == fixture.Manager.Id &&
        x.RequestId == fifthUnboundSourceRequestId && x.RequestHash == unrelatedSourceHash));
    }

    // A second actor can independently create a command receipt with the same source RequestId. Both ResultIds
    // now claim the legacy source; replay must report ambiguity without SingleOrDefault throwing or mutating history.
    var duplicateThirdLegacyReceiptId = await RecordReceiptAsync(
      fixture.ReviewerActor, 10m, thirdLegacyStoredReference, thirdLegacyRequestId);
    Assert.NotEqual(thirdLegacyReceiptId, duplicateThirdLegacyReceiptId);
    int receiptsBeforeDuplicateReplay;
    int allocationsBeforeDuplicateReplay;
    int sourceReceiptsBeforeDuplicateReplay;
    await using (var beforeDuplicateReplay = new AuditSphereDbContext(pg.Options))
    {
      receiptsBeforeDuplicateReplay = await beforeDuplicateReplay.Receipts.CountAsync(x => x.FirmId == fixture.FirmId);
      allocationsBeforeDuplicateReplay = await beforeDuplicateReplay.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId);
      sourceReceiptsBeforeDuplicateReplay = await beforeDuplicateReplay.BillingCommandReceipts.CountAsync(x =>
        x.FirmId == fixture.FirmId && x.ClientId == fixture.ClientId && x.Action == "RECORD_RECEIPT" &&
        x.RequestId == thirdLegacyRequestId);
      Assert.Equal(2, sourceReceiptsBeforeDuplicateReplay);
      Assert.Equal(2, await beforeDuplicateReplay.BillingCommandReceipts.AsNoTracking()
        .Where(x => x.FirmId == fixture.FirmId && x.ClientId == fixture.ClientId &&
          x.Action == "RECORD_RECEIPT" && x.RequestId == thirdLegacyRequestId)
        .Select(x => x.ResultId).Distinct().CountAsync());
    }
    var ambiguousThirdReplay = await RecordFeePaymentAsync(
      agreementIds[2], fixture.ManagerActor, 10m, thirdLegacyExternalReference);
    Assert.False(ambiguousThirdReplay.Succeeded);
    Assert.Equal("fee.reference-conflict", ambiguousThirdReplay.ErrorCode);
    await using (var verifyDuplicateDenial = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(receiptsBeforeDuplicateReplay,
        await verifyDuplicateDenial.Receipts.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(allocationsBeforeDuplicateReplay,
        await verifyDuplicateDenial.ReceiptAllocations.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(sourceReceiptsBeforeDuplicateReplay,
        await verifyDuplicateDenial.BillingCommandReceipts.CountAsync(x =>
          x.FirmId == fixture.FirmId && x.ClientId == fixture.ClientId && x.Action == "RECORD_RECEIPT" &&
          x.RequestId == thirdLegacyRequestId));
    }

    // Both generic billing commands join an ambient owner transaction and leave its commit decision to the caller.
    // Rollback must remove the provisional receipt, allocation and command receipts while retaining the first payment's 10.
    var ambientReceiptRequestId = Guid.CreateVersion7();
    var ambientAllocationRequestId = Guid.CreateVersion7();
    var ambientReference = $"AMBIENT-ROLLBACK-{Guid.CreateVersion7():N}";
    Guid ambientReceiptId;
    {
      // The invoice was posted through another context; allocation must refresh this retained draft under its row lock.
      var ambientDb = cachedBillingDb;
      await using var ownerTransaction = await ambientDb.Database.BeginTransactionAsync();
      var currentOwnerTransaction = ambientDb.Database.CurrentTransaction;
      Assert.NotNull(currentOwnerTransaction);

      var recorded = await BillingService.RecordReceiptAsync(ambientDb, fixture.ManagerActor,
        new RecordReceiptRequest(accountId, 1m, ambientReference, RequestId: ambientReceiptRequestId));
      Assert.True(recorded.Succeeded, recorded.Message);
      ambientReceiptId = recorded.Value;
      Assert.Same(currentOwnerTransaction, ambientDb.Database.CurrentTransaction);

      var allocated = await BillingService.AllocateReceiptCommandAsync(ambientDb, fixture.ManagerActor,
        new AllocateReceiptRequest(ambientReceiptId, issuedInvoiceIds[0], 1m, ambientAllocationRequestId));
      Assert.True(allocated.Succeeded, allocated.Message);
      Assert.Same(currentOwnerTransaction, ambientDb.Database.CurrentTransaction);
      await ownerTransaction.RollbackAsync();
    }

    await using (var verifyAmbientRollback = new AuditSphereDbContext(pg.Options))
    {
      Assert.False(await verifyAmbientRollback.Receipts.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.Id == ambientReceiptId));
      Assert.False(await verifyAmbientRollback.ReceiptAllocations.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ReceiptId == ambientReceiptId));
      Assert.False(await verifyAmbientRollback.BillingCommandReceipts.AnyAsync(x =>
        x.FirmId == fixture.FirmId && x.ActorId == fixture.Manager.Id &&
        (x.RequestId == ambientReceiptRequestId || x.RequestId == ambientAllocationRequestId)));
      Assert.Equal(10m, await BillingService.GetNetAppliedToInvoiceAsync(
        verifyAmbientRollback, fixture.FirmId, issuedInvoiceIds[0]));
    }
  }

  private sealed class RejectFeeMilestoneLink(Guid milestoneId, InvalidOperationException failure) : SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (eventData.Context?.ChangeTracker.Entries<FeeMilestone>().Any(entry =>
        entry.Entity.Id == milestoneId && entry.State == EntityState.Modified &&
        entry.Property(x => x.InvoiceId).IsModified && entry.Property(x => x.InvoiceId).OriginalValue is null &&
        entry.Entity.InvoiceId is not null && entry.Entity.State == FeeMilestoneStates.Invoiced) == true)
        throw failure;

      return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
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
