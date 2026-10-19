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
      Guid.Parse("a17f0c42-0000-7000-8000-000000000003")
    ];
    Assert.Equal(3, agreementIds.Distinct().Count());
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

    var invoiceIds = new[] { issuedInvoiceIds[0], issuedInvoiceIds[1], legacyDraft.Value };
    var invoices = await db.Invoices.AsNoTracking().Where(x => invoiceIds.Contains(x.Id)).ToListAsync();
    Assert.Equal(3, invoices.Count);
    Assert.Equal($"AGR-{agreementIds[0].ToString("N").ToUpperInvariant()}-ADV",
      invoices.Single(x => x.Id == issuedInvoiceIds[0]).InvoiceNumber);
    Assert.Equal($"AGR-{agreementIds[1].ToString("N").ToUpperInvariant()}-ADV",
      invoices.Single(x => x.Id == issuedInvoiceIds[1]).InvoiceNumber);
    Assert.Equal(legacyNumber, invoices.Single(x => x.Id == legacyDraft.Value).InvoiceNumber);
    Assert.Equal(3, invoices.Select(x => x.InvoiceNumber).Distinct(StringComparer.Ordinal).Count());

    var invoiceIdsByMilestone = new[] { issuedInvoiceIds[0], issuedInvoiceIds[1], legacyDraft.Value };
    for (var index = 0; index < milestoneIds.Length; index++)
    {
      var milestoneId = milestoneIds[index];
      var invoiceId = invoiceIdsByMilestone[index];
      Assert.Equal(1, await db.BillingSourceAllocations.CountAsync(x => x.FirmId == fixture.FirmId &&
        x.SourceKind == FeeAgreementService.MilestoneSourceKind && x.SourceId == milestoneId && x.SourceRevision == 1));
      var milestone = await db.FeeMilestones.AsNoTracking().SingleAsync(x => x.Id == milestoneId);
      Assert.Equal(invoiceId, milestone.InvoiceId);
      Assert.Equal(FeeMilestoneStates.Invoiced, milestone.State);
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
