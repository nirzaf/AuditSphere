using System.Data.Common;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Search;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE package 7 with real role identities: the permission-aware versioned technical library, practice analytics
/// computed from approved records with cost kept apart from charge-out value, the firm's own expense workflow posted
/// through the ledger, and the calculated firm trial balance with reconciled summaries.
/// </summary>
[Trait("Profile", "Database")]
public sealed partial class FirmOperationsTests
{
  [Fact]
  public async Task AdministratorOversight_ExpenseMetadataDoesNotGrantPreparationOrReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = w.FirmId, UserId = w.U["manager"].Id,
      Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = w.U["manager"].Id });
    await db.SaveChangesAsync();
    var finance = w.A("finance", "FinanceManager");
    var admin = w.A("manager", "Administrator");
    var cash = (await LedgerService.CreateFirmAccountAsync(db, finance,
      new("OS-EXP-CASH", "Bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    var rent = (await LedgerService.CreateFirmAccountAsync(db, finance,
      new("OS-EXP-RENT", "Rent", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
    var request = new RecordFirmExpenseRequest(new(2026, 2, 3), FirmExpenseCategories.Rent,
      "Synthetic oversight supplier", "Read metadata only", 125.12m, "QAR", rent, cash,
      "private-receipt.pdf", "application/pdf", "%PDF private evidence"u8.ToArray());
    var expense = await FirmExpenseService.RecordAsync(db, finance, request);
    Assert.True(expense.Succeeded, expense.Message);
    var view = await FirmBooksWorkspaceQuery.GetAsync(db, admin);
    Assert.True(view.Succeeded, view.Message);
    Assert.Equal("READ_ONLY", view.Value!.AccessMode);
    Assert.False(view.Value.CanPrepare || view.Value.CanReview);
    var row = Assert.Single(view.Value.Expenses);
    Assert.Equal((expense.Value, "Synthetic oversight supplier", 125.12m), (row.Id, row.Payee, row.Amount));
    Assert.All(await FirmExpenseService.ListAsync(db, admin), x => Assert.Empty(x.EvidenceContent));
    Assert.Equal(ErrorCodes.ScopeDenied, (await FirmExpenseService.RecordAsync(db, admin, request)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await FirmExpenseService.SubmitAsync(db, admin, expense.Value)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await FirmExpenseService.ReviewAsync(db, admin, expense.Value, true, null)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await FirmBooksWorkspaceQuery.GetAsync(db, w.A("staff", "Staff"))).ErrorCode);
    Assert.True((await FirmBooksWorkspaceQuery.GetAsync(db, finance)).Value!.CanPrepare);
    Assert.Single(await db.FirmExpenses.ToListAsync());
    Assert.Equal(FirmExpenseStates.Draft, (await db.FirmExpenses.SingleAsync()).Status);
  }

  private sealed record World(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> U)
  {
    public ActorContext A(string name, params string[] roles) => new(U[name].Id, FirmId, U[name].SessionEpoch, roles);
  }

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var u = new[] { "partner", "partner2", "manager", "staff", "finance", "reviewer" }.ToDictionary(x => x, x => new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = x + Guid.NewGuid().ToString("N"), TenantId = "tenant-ops", Email = $"{x}-{Guid.NewGuid():N}@example.test",
      DisplayName = x, UserKind = "Staff", CreatedAt = DateTimeOffset.UtcNow
    });
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.Id == engagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Active").SetProperty(x => x.ProfessionalWorkBlocked, false)
      .SetProperty(x => x.ServiceRoute, "AccountingOnly").SetProperty(x => x.PeriodEnd, "2026-12-31"));
    db.Users.AddRange(u.Values);
    RoleGrant G(string name, string role) => new() { Id = Guid.NewGuid(), FirmId = firmId, UserId = u[name].Id, Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = u[name].Id };
    db.RoleGrants.AddRange(G("partner", "Partner"), G("partner2", "Partner"), G("manager", "Manager"), G("staff", "Staff"), G("finance", "FinanceManager"), G("reviewer", "FinanceReviewer"));
    db.FirmFinanceProfiles.Add(new FirmFinanceProfile { Id = Guid.NewGuid(), FirmId = firmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
      ApprovedByUserId = u["reviewer"].Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    return new(firmId, clientId, engagementId, u);
  }

  [Fact]
  public async Task TechnicalLibrary_IsVersioned_PublishedByASecondApprover_AndSearchRespectsAudience()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var manager = w.A("manager", "Manager");
    var staff = w.A("staff", "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);
    var isa = await TechnicalLibraryService.CreateAsync(db, manager, "ISA-570", "Going concern", "ISA", "ALL_STAFF",
      "The auditor evaluates management's assessment of the entity's ability to continue as a going concern for at least twelve months.", "ISA 570 (Revised)", new DateOnly(2016, 12, 15));
    Assert.True(isa.Succeeded, isa.Message);
    var fee = await TechnicalLibraryService.CreateAsync(db, manager, "FIRM-FEES", "Fee dispute escalation", "FIRM_GUIDANCE", "PARTNERS_MANAGERS",
      "Going concern doubts about a client with overdue fees must be escalated to the Partner.", "Firm policy manual §8", new DateOnly(2026, 1, 1));
    var drafts = await db.TechnicalLibraryVersions.AsNoTracking().ToListAsync();
    foreach (var draft in drafts)
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await TechnicalLibraryService.PublishAsync(db, manager, draft.Id)).ErrorCode); // curators cannot publish
      Assert.True((await TechnicalLibraryService.PublishAsync(db, w.A("partner", "Partner"), draft.Id)).Succeeded);
    }

    // Staff see the all-staff entry only; managers see both.
    var staffHits = await TechnicalLibraryService.SearchAsync(db, staff, "going concern");
    Assert.Equal(["ISA-570"], staffHits.Select(x => x.Code));
    Assert.Contains("twelve months", staffHits.Single().Snippet);
    Assert.Equal(2, (await TechnicalLibraryService.SearchAsync(db, manager, "going concern")).Count);
    Assert.Equal(ErrorCodes.ScopeDenied, (await TechnicalLibraryService.OpenAsync(db, staff, fee.Value)).ErrorCode);
    var global = (await GlobalSearchQuery.SearchAsync(db, staff, "ISA-570")).Value!;
    Assert.Contains(global.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Library && x.Href == $"/app/library/{isa.Value:D}");

    // A new version supersedes the old one, which stays readable and immutable.
    var v2 = await TechnicalLibraryService.DraftVersionAsync(db, manager, isa.Value, "Revised: management's assessment covers at least twelve months from the date of approval.", "ISA 570 (Revised 2024)", new DateOnly(2026, 12, 15));
    Assert.True(v2.Succeeded, v2.Message);
    Assert.Equal(ErrorCodes.ScopeDenied, (await TechnicalLibraryService.PublishAsync(db, staff, v2.Value)).ErrorCode);
    Assert.True((await TechnicalLibraryService.PublishAsync(db, w.A("partner2", "Partner"), v2.Value)).Succeeded);
    var entry = (await TechnicalLibraryService.OpenAsync(db, staff, isa.Value)).Value!;
    Assert.Equal((2, 2), (entry.Version.Version, entry.History.Count));
    Assert.Equal(TechnicalLibraryStates.Superseded, entry.History.Single(x => x.Version == 1).Status);
    Assert.Contains("ability to continue as a going concern", (await TechnicalLibraryService.OpenAsync(db, staff, isa.Value, 1)).Value!.Version.Body);
    var v1 = entry.History.Single(x => x.Version == 1);
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE technical_library_versions SET body = 'changed' WHERE id = {v1.Id}"));
  }

  [Fact]
  public async Task Analytics_ReconcileBilledCollectedStandardValueAndCost_FromApprovedRecords()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    var finance = w.A("finance", "FinanceManager");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var periodStart = today.AddDays(-4);
    var workDate = today.AddDays(-1);
    var costRateEffectiveFrom = new DateOnly(2026, 1, 1);
    var capacityDays = Enumerable.Range(0, 5).Select(offset => periodStart.AddDays(offset))
      .Count(ResourceGridCalculator.IsWorkingDay);
    Guid entryId;
    Guid billedInvoiceId;
    Guid completedTaskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var card = (await PracticeTimeService.ReviseRateCardAsync(db, manager, new("Staff", "AUDIT", "QAR", 600m))).Value;
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, card)).Succeeded);
      var budget = (await PracticeTimeService.ReviseBudgetAsync(db, manager, new(w.EngagementId, "QAR", [new("Staff", "AUDIT", 600, BudgetPhases.Fieldwork)]))).Value;
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, partner, budget)).Succeeded);
      var task = (await PracticeTimeService.CreateTaskAsync(db, manager, new("Bank reconciliation", w.ClientId, w.EngagementId, w.U["staff"].Id, DueDate: today))).Value;
      completedTaskId = task;
      entryId = (await PracticeTimeService.SaveTimeDraftAsync(db, w.A("staff", "Staff"), new(task, workDate, 540, 300, "Staff", "AUDIT"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, w.A("staff", "Staff"), entryId)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, manager, entryId)).Succeeded);
      Assert.True((await PracticeTimeService.CompleteTaskAsync(db, manager, task)).Succeeded);
      Assert.True((await PracticeAnalyticsQuery.RecordCostRateAsync(db, partner, w.U["staff"].Id, 200m, "QAR", costRateEffectiveFrom)).Succeeded);
      Assert.True((await ResourcePlanningService.SaveProfileAsync(db, manager, new(w.U["staff"].Id, "Audit", "", 2400, 75m))).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.WorkTasks.AddRange(
        new WorkTask { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          Title = "Open task due in range", Status = PracticeTimeStates.TaskOpen, DueDate = today, CreatedAt = DateTimeOffset.UtcNow },
        new WorkTask { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          Title = "Overdue assigned task", Status = PracticeTimeStates.TaskOpen, AssigneeUserId = w.U["manager"].Id, DueDate = today.AddDays(-1), CreatedAt = DateTimeOffset.UtcNow },
        new WorkTask { Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          Title = "Cancelled task excluded", Status = PracticeTimeStates.TaskCancelled, DueDate = today, CreatedAt = DateTimeOffset.UtcNow });
      var reviewAnchor = DateTimeOffset.UtcNow;
      db.WorkflowTaskBindings.Add(new WorkflowTaskBinding
      {
        Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
        WorkTaskId = completedTaskId, Phase = WorkflowTaskPhases.AuditFieldwork, Ordinal = 1,
        SourceKey = "analytics-phase-1", CreationRequestDigest = Hashing.Sha256Hex("analytics-phase-1"), Generation = 1,
        CreatedAt = reviewAnchor, CreatedByUserId = w.U["manager"].Id
      });
      var reviewSamples = new[]
      {
        (Decision: AuditProcedureReviewDecisions.Reviewed, SubmittedAt: reviewAnchor.AddHours(-10), CreatedAt: reviewAnchor, ResultRevision: 1L),
        (Decision: AuditProcedureReviewDecisions.ChangesRequired, SubmittedAt: reviewAnchor.AddHours(-6), CreatedAt: reviewAnchor.AddHours(-1), ResultRevision: 1L),
        (Decision: AuditProcedureReviewDecisions.Reviewed, SubmittedAt: reviewAnchor.AddHours(2), CreatedAt: reviewAnchor, ResultRevision: 1L),
        (Decision: AuditProcedureReviewDecisions.Reviewed, SubmittedAt: reviewAnchor.AddHours(-2), CreatedAt: reviewAnchor, ResultRevision: 2L),
        (Decision: AuditProcedureReviewDecisions.Reviewed, SubmittedAt: reviewAnchor.AddDays(-7), CreatedAt: reviewAnchor.AddDays(-6), ResultRevision: 1L)
      };
      for (var i = 0; i < reviewSamples.Length; i++)
      {
        var sample = reviewSamples[i];
        var procedureId = Guid.NewGuid();
        var resultId = Guid.NewGuid();
        var procedureStatus = sample.Decision == AuditProcedureReviewDecisions.Reviewed ? AuditProcedureStatuses.Reviewed : AuditProcedureStatuses.ChangesRequired;
        db.AuditProcedures.Add(new AuditProcedure
        {
          Id = procedureId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          SourceProcedureId = $"AN-{i + 1:D2}", Title = $"Analytics review sample {i + 1}", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
          Status = procedureStatus, CurrentResultRevision = 1, CreatedAt = reviewAnchor.AddDays(-10)
        });
        db.AuditProcedureResults.Add(new AuditProcedureResult
        {
          Id = resultId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, AuditProcedureId = procedureId,
          Revision = 1, InputGeneration = 1, WorkPerformed = "Reviewed synthetic analytics fixture.", StructuredResultJson = "{}",
          Conclusion = "Synthetic fixture only.", Status = sample.Decision == AuditProcedureReviewDecisions.Reviewed
            ? AuditProcedureResultStatuses.Reviewed : AuditProcedureResultStatuses.ChangesRequired,
          PreparedByUserId = w.U["staff"].Id, ReviewedByUserId = w.U["manager"].Id, ReviewComment = "Synthetic review fixture.",
          SubmittedAt = sample.SubmittedAt, ReviewedAt = sample.CreatedAt
        });
        db.AuditProcedureReviews.Add(new AuditProcedureReview
        {
          Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
          AuditProcedureResultId = resultId, AuditProcedureId = procedureId, ResultRevision = sample.ResultRevision,
          Decision = sample.Decision, ReviewerUserId = w.U["manager"].Id, CreatedAt = sample.CreatedAt
        });
      }
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var account = (await BillingService.CreateBillingAccountAsync(db, finance, new CreateBillingAccountRequest(w.ClientId, "QAR"))).Value;
      var invoice = await BillingService.CreateInvoiceDraftAsync(db, finance, new CreateInvoiceDraftRequest(account, "INV-T1", [new InvoiceLineRequest("Audit fieldwork", 5m, 600m, "TIME", entryId, 1)]));
      Assert.True(invoice.Succeeded, invoice.Message);
      billedInvoiceId = invoice.Value;
      Assert.True((await BillingService.SubmitInvoiceAsync(db, finance, invoice.Value)).Succeeded);
      Assert.True((await BillingService.ApproveInvoiceAsync(db, w.A("reviewer", "FinanceReviewer"), invoice.Value)).Succeeded);
      var clientBookCountsBeforePost = (
        await db.ClientOperationalJournals.CountAsync(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId),
        await db.GeneralLedgerTransactions.CountAsync(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId));
      Assert.True((await BillingService.PostInvoiceAsync(db, finance, invoice.Value)).Succeeded);
      Assert.Equal(clientBookCountsBeforePost, (
        await db.ClientOperationalJournals.CountAsync(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId),
        await db.GeneralLedgerTransactions.CountAsync(x => x.FirmId == w.FirmId && x.ClientId == w.ClientId)));
      var receipt = (await BillingService.RecordReceiptAsync(db, finance, new RecordReceiptRequest(account, 2400m, "BANK-1", RequestId: Guid.NewGuid()))).Value;
      Assert.True((await BillingService.AllocateReceiptAsync(db, finance, new AllocateReceiptRequest(receipt, invoice.Value, 2400m, Guid.NewGuid()))).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeAnalyticsQuery.GetAsync(db, manager, new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))).ErrorCode);
      var view = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!;
      var row = view.Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal("QAR", row.Currency);
      Assert.Equal(300, row.ActualMinutes);
      Assert.Equal<decimal?>(6000m, row.BudgetValue);
      Assert.Equal<decimal?>(3000m, row.StandardValue);
      Assert.Equal<decimal?>(1000m, row.ActualCost);
      Assert.Equal<decimal?>(3000m, row.Billed);
      Assert.Equal<decimal?>(2400m, row.Collected);
      Assert.Equal((100.0m, 80.0m, 2000m, 66.7m, 300), (row.RealizationPercent!.Value, row.CollectionPercent!.Value, row.Profit!.Value, row.MarginPercent!.Value, row.BudgetVarianceMinutes));
      Assert.True(row.CostComplete);
      // Sending a posted invoice changes delivery status, never its retained financial evidence.
      // A period without approved time also proves the invoice-only economics selection path.
      var invoiceOnlyDate = today.AddDays(1);
      var invoiceOnlyBefore = (await PracticeAnalyticsQuery.GetAsync(db, partner, invoiceOnlyDate, invoiceOnlyDate)).Value!
        .Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.True((await BillingService.SendInvoiceAsync(db, finance, billedInvoiceId)).Succeeded);
      var afterSend = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!
        .Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal(row, afterSend);
      Assert.Equal((3000m, 2400m, 100m, 80m, 2000m, 66.7m),
        (afterSend.Billed!.Value, afterSend.Collected!.Value, afterSend.RealizationPercent!.Value,
          afterSend.CollectionPercent!.Value, afterSend.Profit!.Value, afterSend.MarginPercent!.Value));
      var invoiceOnlyAfter = (await PracticeAnalyticsQuery.GetAsync(db, partner, invoiceOnlyDate, invoiceOnlyDate)).Value!
        .Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal(invoiceOnlyBefore, invoiceOnlyAfter);

      var replacementBudget = await PracticeTimeService.ReviseBudgetAsync(db, manager,
        new ReviseBudgetRequest(w.EngagementId, "QAR",
          [new BudgetLineRequest("Staff", "AUDIT", 300, BudgetPhases.Fieldwork)], ExpectedVersion: 1));
      Assert.True(replacementBudget.Succeeded, replacementBudget.Message);
      var whileReplacementIsDraft = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!
        .Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal<decimal?>(6000m, whileReplacementIsDraft.BudgetValue);
      Assert.Equal(300, whileReplacementIsDraft.BudgetVarianceMinutes);
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, partner, replacementBudget.Value)).Succeeded);
      var afterReplacementApproval = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!
        .Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal<decimal?>(3000m, afterReplacementApproval.BudgetValue);
      Assert.Equal(0, afterReplacementApproval.BudgetVarianceMinutes);

      var audit = view.Departments.Single(x => x.Department == "Audit");
      Assert.Equal((capacityDays * 480, 300, Math.Round(300m * 100m / (capacityDays * 480), 1)),
        (audit.CapacityMinutes, audit.ChargeableMinutes, audit.UtilizationPercent!.Value));
      Assert.Equal((3, 1, 1, 0, 1, 33.3m, 33.3m), (view.Milestones.Due, view.Milestones.Completed, view.Milestones.CompletedOnTime,
        view.Milestones.CompletedLate, view.Milestones.Overdue, view.Milestones.CompletionPercent!.Value, view.Milestones.OnTimePercent!.Value));
      var assigned = view.Workload.Single(x => x.AssigneeKey == w.U["staff"].Id.ToString("N"));
      Assert.Equal((1, 1, 0, 0), (assigned.Due, assigned.Completed, assigned.Open, assigned.Overdue));
      var managerWorkload = view.Workload.Single(x => x.AssigneeKey == w.U["manager"].Id.ToString("N"));
      Assert.Equal((1, 0, 1, 1), (managerWorkload.Due, managerWorkload.Completed, managerWorkload.Open, managerWorkload.Overdue));
      var unassigned = view.Workload.Single(x => x.AssigneeKey == "unassigned");
      Assert.Equal((1, 0, 1, 0), (unassigned.Due, unassigned.Completed, unassigned.Open, unassigned.Overdue));
      Assert.Equal(8, view.PhaseWorkload.Count);
      var fieldwork = view.PhaseWorkload.Single(x => x.Phase == WorkflowTaskPhases.AuditFieldwork);
      Assert.Equal((1, 1, 0, 0, 100.0m), (fieldwork.Due, fieldwork.Completed, fieldwork.Open, fieldwork.Overdue, fieldwork.CompletionPercent!.Value));
      var unassignedPhase = view.PhaseWorkload.Single(x => x.Phase == "UNASSIGNED_PHASE");
      Assert.Equal((2, 0, 2, 1, 0.0m), (unassignedPhase.Due, unassignedPhase.Completed, unassignedPhase.Open, unassignedPhase.Overdue, unassignedPhase.CompletionPercent!.Value));
      Assert.Equal((4, 3, 1, 2, 2, 7.5m), (view.ReviewTurnaround.Decisions, view.ReviewTurnaround.Reviewed,
        view.ReviewTurnaround.ChangesRequired, view.ReviewTurnaround.Measured, view.ReviewTurnaround.Excluded, view.ReviewTurnaround.AverageHours!.Value));
      Assert.Contains(view.Definitions, d => d.Contains("charge-out rates are not costs"));
      // Labels name the existing formulas; billed less staff cost is never presented as firm accounting profit.
      Assert.Contains(view.Definitions, d => d.StartsWith("Billing realization = billed ÷ standard value", StringComparison.Ordinal) &&
        d.Contains("is not firm accounting profit") && d.Contains("never averaged into a firm ratio"));
      Assert.Contains(view.Definitions, d => d.StartsWith("Budget hours remaining / overrun = current approved budget minutes − approved actual minutes", StringComparison.Ordinal));
      Assert.Contains(view.Definitions, d => d.StartsWith("Billed = ", StringComparison.Ordinal) && d.Contains("engagement to date"));
      Assert.Contains(view.Definitions, d => d.Contains("task count, not a capacity or effort measure"));
      Assert.Contains(view.Definitions, d => d.Contains("do not establish phase or audit-gate readiness"));
      Assert.Contains(view.Definitions, d => d.Contains("not a review-quality or audit-clearance measure"));
      // ADR-0018: approved non-billable time adds hours and cost but no charge-out value, so value and realization stay available.
      var internalTask = (await PracticeTimeService.CreateTaskAsync(db, manager, new("Internal file review", w.ClientId, w.EngagementId, w.U["staff"].Id))).Value;
      var internalEntry = await PracticeTimeService.SaveTimeDraftAsync(db, w.A("staff", "Staff"),
        new(internalTask, workDate, 900, 60, "Staff", "AUDIT", PracticeTimeStates.NonBillable, "Internal file review"));
      Assert.True(internalEntry.Succeeded, internalEntry.Message);
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, w.A("staff", "Staff"), internalEntry.Value)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, manager, internalEntry.Value)).Succeeded);
      var withInternal = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!.Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal((360, 60), (withInternal.ActualMinutes, withInternal.NonChargeableMinutes));
      Assert.Equal<decimal?>(3000m, withInternal.StandardValue);
      Assert.Equal<decimal?>(100.0m, withInternal.RealizationPercent);
      var utilizationWithInternal = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!.Departments.Single(x => x.Department == "Audit");
      Assert.Equal(300, utilizationWithInternal.ChargeableMinutes);
      // The weekly grid reads the same basis: all approved time as hours, billable time as utilization.
      var gridCells = (await ResourcePlanningService.GetGridAsync(db, manager, workDate, 1)).Value!.Rows.Single(x => x.UserId == w.U["staff"].Id).Weeks;
      Assert.Equal((360, 300), (gridCells[0].ApprovedActualMinutes, gridCells[0].ApprovedBillableMinutes));

      // Same-value duplicates do not change the amount; conflicting rates at the latest effective date make only cost-derived figures unavailable.
      Assert.True((await PracticeAnalyticsQuery.RecordCostRateAsync(db, partner, w.U["staff"].Id, 200m, "QAR", costRateEffectiveFrom)).Succeeded);
      var withIdenticalRate = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!.Engagements
        .Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal(withInternal, withIdenticalRate);

      Assert.True((await PracticeAnalyticsQuery.RecordCostRateAsync(db, partner, w.U["staff"].Id, 250m, "QAR", costRateEffectiveFrom)).Succeeded);
      var withConflictingRates = (await PracticeAnalyticsQuery.GetAsync(db, partner, periodStart, today)).Value!.Engagements
        .Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal(withInternal with { ActualCost = null, Profit = null, MarginPercent = null, CostComplete = false }, withConflictingRates);
    }
  }

  [Fact]
  public async Task Analytics_MissingCostRateKeepsChargeOutValueAndZeroCapacityDoesNotInventUtilization()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var manager = w.A("manager", "Manager");
    var partner = w.A("partner", "Partner");
    var staff = w.A("staff", "Staff");
    var date = DateOnly.FromDateTime(DateTime.UtcNow);
    await using var db = new AuditSphereDbContext(pg.Options);
    var card = await PracticeTimeService.ReviseRateCardAsync(db, manager, new("Staff", "AUDIT", "QAR", 600m));
    Assert.True(card.Succeeded, card.Message);
    Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, card.Value)).Succeeded);
    var task = await PracticeTimeService.CreateTaskAsync(db, manager,
      new("Synthetic missing-cost-rate work", w.ClientId, w.EngagementId, w.U["staff"].Id));
    Assert.True(task.Succeeded, task.Message);
    var entry = await PracticeTimeService.SaveTimeDraftAsync(db, staff,
      new(task.Value, date, 540, 60, "Staff", "AUDIT", Currency: "QAR"));
    Assert.True(entry.Succeeded, entry.Message);
    Assert.True((await PracticeTimeService.SubmitTimeAsync(db, staff, entry.Value)).Succeeded);
    Assert.True((await PracticeTimeService.ApproveTimeAsync(db, manager, entry.Value)).Succeeded);
    Assert.True((await ResourcePlanningService.SaveProfileAsync(db, manager,
      new(w.U["staff"].Id, "Zero capacity", "", 0, 75m))).Succeeded);
    Assert.False(await db.StaffCostRates.AnyAsync(x => x.UserId == w.U["staff"].Id));
    var result = await PracticeAnalyticsQuery.GetAsync(db, partner, date, date);
    Assert.True(result.Succeeded, result.Message);
    var row = Assert.Single(result.Value!.Engagements);
    Assert.Equal("QAR", row.Currency);
    Assert.Equal(60, row.ActualMinutes);
    Assert.Equal<decimal?>(600m, row.StandardValue);
    Assert.Equal<decimal?>(0m, row.Billed);
    Assert.False(row.CostComplete);
    Assert.Null(row.ActualCost); Assert.Null(row.Profit); Assert.Null(row.MarginPercent);
    var department = Assert.Single(result.Value.Departments);
    Assert.Equal(0, department.CapacityMinutes); Assert.Equal(60, department.ChargeableMinutes);
    Assert.Null(department.UtilizationPercent); Assert.Equal<decimal?>(75m, department.TargetPercent);
    Assert.True((await ResourcePlanningService.SaveProfileAsync(db, manager,
      new(w.U["staff"].Id, "Remainder capacity", "", 2401, 75m))).Succeeded);
    var sunday = new DateOnly(2026, 10, 11);
    var completeWeek = await PracticeAnalyticsQuery.GetAsync(db, partner, sunday, sunday.AddDays(4));
    Assert.True(completeWeek.Succeeded, completeWeek.Message);
    Assert.Equal(2401, Assert.Single(completeWeek.Value!.Departments).CapacityMinutes);
    var grid = await ResourcePlanningService.GetGridAsync(db, manager, sunday, 1);
    Assert.Equal(2401, grid.Value!.Rows.Single(x => x.UserId == w.U["staff"].Id).Weeks[0].CapacityMinutes);
    Assert.True((await ResourcePlanningService.AddAvailabilityAsync(db, manager,
      new(w.U["staff"].Id, sunday, sunday, StaffAvailabilityKinds.Leave, 481))).Succeeded);
    var withLeave = await PracticeAnalyticsQuery.GetAsync(db, partner, sunday, sunday.AddDays(4));
    Assert.Equal(1920, Assert.Single(withLeave.Value!.Departments).CapacityMinutes);
    var leaveGrid = await ResourcePlanningService.GetGridAsync(db, manager, sunday, 1);
    Assert.Equal(1920, leaveGrid.Value!.Rows.Single(x => x.UserId == w.U["staff"].Id).Weeks[0].CapacityMinutes);
    var finalDate = await PracticeAnalyticsQuery.GetAsync(db, partner, DateOnly.MaxValue, DateOnly.MaxValue);
    Assert.True(finalDate.Succeeded, finalDate.Message);

  }

  [Fact]
  public async Task Analytics_MixedCurrenciesAndMismatchedCostRatesDoNotProduceCombinedTotals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var administrator = w.A("partner", "Partner");
    var manager = w.A("manager", "Manager");
    var staff = w.A("staff", "Staff");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var qarCard = (await PracticeTimeService.ReviseRateCardAsync(db, manager, new("Staff", "AUDIT", "QAR", 600m))).Value;
      var usdCard = (await PracticeTimeService.ReviseRateCardAsync(db, manager, new("Staff", "AUDIT", "USD", 600m))).Value;
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, administrator, qarCard)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, administrator, usdCard)).Succeeded);
      var budget = (await PracticeTimeService.ReviseBudgetAsync(db, manager,
        new(w.EngagementId, "QAR", [new("Staff", "AUDIT", 60, BudgetPhases.Fieldwork)]))).Value;
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, administrator, budget)).Succeeded);
      var qarTask = (await PracticeTimeService.CreateTaskAsync(db, manager,
        new("QAR approved work", w.ClientId, w.EngagementId, w.U["staff"].Id))).Value;
      var usdTask = (await PracticeTimeService.CreateTaskAsync(db, manager,
        new("USD approved work", w.ClientId, w.EngagementId, w.U["staff"].Id))).Value;
      var qarEntry = (await PracticeTimeService.SaveTimeDraftAsync(db, staff,
        new(qarTask, today, 540, 60, "Staff", "AUDIT", Currency: "QAR"))).Value;
      var usdEntry = (await PracticeTimeService.SaveTimeDraftAsync(db, staff,
        new(usdTask, today, 600, 60, "Staff", "AUDIT", Currency: "USD"))).Value;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, staff, qarEntry)).Succeeded);
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, staff, usdEntry)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, manager, qarEntry)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, manager, usdEntry)).Succeeded);
      Assert.True((await PracticeAnalyticsQuery.RecordCostRateAsync(db, administrator, w.U["staff"].Id, 200m, "QAR", today.AddDays(-1))).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var view = (await PracticeAnalyticsQuery.GetAsync(db, administrator, today, today)).Value!;
      var row = view.Engagements.Single(x => x.EngagementId == w.EngagementId);
      Assert.Equal("MIXED", row.Currency);
      Assert.Equal(120, row.ActualMinutes);
      Assert.False(row.CostComplete);
      Assert.Null(row.BudgetValue);
      Assert.Null(row.StandardValue);
      Assert.Null(row.ActualCost);
      Assert.Null(row.Billed);
      Assert.Null(row.Collected);
      Assert.Null(row.RealizationPercent);
      Assert.Null(row.CollectionPercent);
      Assert.Null(row.Profit);
      Assert.Null(row.MarginPercent);
    }
  }

  [Fact]
  public async Task FirmExpenses_PostThroughTheLedger_AndTheFirmTrialBalanceReconciles()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var finance = w.A("finance", "FinanceManager");
    var reviewer = w.A("reviewer", "FinanceReviewer");
    Guid cash, capital, rent, salaries, revenueAccount;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      cash = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("1000", "Bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      capital = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("3000", "Partners' capital", LedgerStates.AccountEquity, LedgerStates.Credit))).Value;
      rent = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("6100", "Office rent", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
      salaries = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("6200", "Staff salaries", LedgerStates.AccountExpense, LedgerStates.Debit))).Value;
      revenueAccount = (await LedgerService.CreateFirmAccountAsync(db, finance, new CreateFirmAccountRequest("4000", "Service revenue", LedgerStates.AccountRevenue, LedgerStates.Credit))).Value;
      foreach (var period in new[] { "2026-01", "2026-02" })
        Assert.True((await LedgerService.CreateFirmPeriodAsync(db, finance, new CreateFirmPeriodRequest(period))).Succeeded);
      var january = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.PeriodCode == "2026-01");
      var capitalJournal = (await LedgerService.CreateFirmJournalDraftAsync(db, finance, new CreateFirmJournalDraftRequest(january.Id, "J-CAP", "MANUAL", "CAP-1", 1, "CAPITAL", "QAR",
        [new FirmJournalLineRequest(cash, "Capital introduced", 50_000m, 0m), new FirmJournalLineRequest(capital, "Capital introduced", 0m, 50_000m)]))).Value;
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, finance, capitalJournal)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, capitalJournal)).Succeeded);
      Assert.True((await LedgerService.PostFirmJournalAsync(db, finance, capitalJournal)).Succeeded);
      var revenueJournal = (await LedgerService.CreateFirmJournalDraftAsync(db, finance, new CreateFirmJournalDraftRequest(january.Id, "J-REV", "MANUAL", "REV-1", 1, "MONTHLY_REVENUE", "QAR",
        [new FirmJournalLineRequest(cash, "January client service", 10_000m, 0m), new FirmJournalLineRequest(revenueAccount, "=HYPERLINK(\"https://example.invalid\",\"open\")", 0m, 10_000m)]))).Value;
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, finance, revenueJournal)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, revenueJournal)).Succeeded);
      Assert.True((await LedgerService.PostFirmJournalAsync(db, finance, revenueJournal)).Succeeded);
    }

    async Task<Guid> ExpenseAsync(DateOnly date, string category, Guid account, decimal amount, string payee)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var expense = await FirmExpenseService.RecordAsync(db, finance, new(date, category, payee, $"{category.ToLowerInvariant()} for {date:MMMM}", amount, "QAR", account, cash,
        "receipt.pdf", "application/pdf", Encoding.UTF8.GetBytes($"%PDF receipt {payee} {amount}")));
      Assert.True(expense.Succeeded, expense.Message);
      Assert.True((await FirmExpenseService.SubmitAsync(db, finance, expense.Value)).Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FirmExpenseService.ReviewAsync(db, finance, expense.Value, true, null)).ErrorCode);
      Assert.True((await FirmExpenseService.ReviewAsync(db, reviewer, expense.Value, true, "Agreed to the invoice.")).Succeeded);
      var posted = await FirmExpenseService.PostAsync(db, finance, expense.Value);
      Assert.True(posted.Succeeded, posted.Message);
      return expense.Value;
    }
    await ExpenseAsync(new DateOnly(2026, 1, 31), FirmExpenseCategories.Salaries, salaries, 18_000m, "Payroll");
    await ExpenseAsync(new DateOnly(2026, 2, 1), FirmExpenseCategories.Rent, rent, 6_500m, "West Bay Towers");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var february = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.FirmId == w.FirmId && x.PeriodCode == "2026-02");
      var openingBalance = await LedgerService.CreateFirmJournalDraftAsync(db, finance,
        new CreateFirmJournalDraftRequest(february.Id, "J-OPEN", "MANUAL", "OPEN-2026-02", 1,
          "OPENING_BALANCE", "QAR",
          [new FirmJournalLineRequest(salaries, "Brought-forward adjustment", 50m, 0m),
           new FirmJournalLineRequest(cash, "Brought-forward adjustment", 0m, 50m)],
          "opening-balance.csv", "text/csv", "opening schedule"u8.ToArray()));
      Assert.True(openingBalance.Succeeded, openingBalance.Message);
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, finance, openingBalance.Value)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, openingBalance.Value)).Succeeded);
      Assert.True((await LedgerService.PostFirmJournalAsync(db, finance, openingBalance.Value)).Succeeded);
      var closing = await LedgerService.CreateFirmJournalDraftAsync(db, finance, new CreateFirmJournalDraftRequest(february.Id, "J-CLOSE", "MANUAL", "CLOSE-2026", 1,
        LedgerStates.YearEndClosingPurpose, "QAR",
        [new FirmJournalLineRequest(revenueAccount, "Close annual revenue", 10_000m, 0m),
         new FirmJournalLineRequest(salaries, "Close annual expenses", 0m, 18_000m),
         new FirmJournalLineRequest(rent, "Close annual expenses", 0m, 6_500m),
         new FirmJournalLineRequest(capital, "Transfer annual loss to equity", 14_500m, 0m)]));
      Assert.True(closing.Succeeded, closing.Message);
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, finance, closing.Value)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, closing.Value)).Succeeded);
      var closingPosting = await LedgerService.PostFirmJournalAsync(db, finance, closing.Value);
      Assert.True(closingPosting.Succeeded, closingPosting.Message);
      Assert.True((await LedgerService.ReverseFirmPostingAsync(db, reviewer,
        new ReverseFirmPostingRequest(closingPosting.Value, february.Id, "Reopen the year-end transfer for correction."))).Succeeded);
      var drawing = await LedgerService.CreateFirmJournalDraftAsync(db, finance,
        new CreateFirmJournalDraftRequest(february.Id, "J-DRAW", "MANUAL", "DRAW-2026-02", 1,
          "PARTNER_DRAWING", "QAR",
          [new FirmJournalLineRequest(capital, "Partner distribution", 100m, 0m),
           new FirmJournalLineRequest(cash, "Partner distribution", 0m, 100m)],
          "partner-drawing.pdf", "application/pdf", "%PDF approved partner drawing"u8.ToArray()));
      Assert.True(drawing.Succeeded, drawing.Message);
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, finance, drawing.Value)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, drawing.Value)).Succeeded);
      Assert.True((await LedgerService.PostFirmJournalAsync(db, finance, drawing.Value)).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Validation: an income account cannot be the expense account; a missing source document is refused.
      Assert.Equal("expense.invalid", (await FirmExpenseService.RecordAsync(db, finance, new(new DateOnly(2026, 2, 3), "PETTY_CASH", "Shop", "Tea", 50m, "QAR", capital, cash, "r.pdf", "application/pdf", [1]))).ErrorCode);
      Assert.Equal("expense.invalid", (await FirmExpenseService.RecordAsync(db, finance, new(new DateOnly(2026, 2, 3), "PETTY_CASH", "Shop", "Tea", 50m, "QAR", rent, cash, "r.pdf", "application/pdf", []))).ErrorCode);

      var januaryTb = (await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "2026-01", "2026-01")).Value!;
      Assert.Equal((10_000m, 18_000m, -8_000m, -8_000m),
        (januaryTb.Revenue, januaryTb.Expenses, januaryTb.Profit, januaryTb.CumulativeProfit));
      Assert.Equal("QAR", januaryTb.Currency);
      Assert.Matches("^[0-9a-f]{64}$", januaryTb.ReportIdentity);
      Assert.Equal(2, januaryTb.TotalActivityCount);
      Assert.Contains(januaryTb.ProfitLossActivity, x => x.JournalNumber == "J-REV" && x.RevenueActivity == 10_000m);
      Assert.Contains(januaryTb.ProfitLossActivity, x => x.AccountCode == "6200" && x.ExpenseActivity == 18_000m);
      Assert.True(januaryTb.PositionReconciles);

      var tb = (await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "2026-02", "2026-02")).Value!;
      Assert.Matches("^[0-9a-f]{64}$", tb.ReportIdentity);
      Assert.True(tb.Balanced);
      var bank = tb.Rows.Single(x => x.Code == "1000");
      Assert.Equal((42_000m, 0m, 0m, 6_650m, 35_350m), (bank.OpeningDebit, bank.MovementDebit, bank.OpeningCredit, bank.MovementCredit, bank.ClosingDebit));
      var rentRow = tb.Rows.Single(x => x.Code == "6100");
      Assert.Equal((0m, 13_000m, 6_500m), (rentRow.OpeningDebit, rentRow.MovementDebit, rentRow.ClosingDebit));
      Assert.Equal(18_050m, tb.Rows.Single(x => x.Code == "6200").MovementDebit);
      Assert.Equal((0m, 6_500m, -6_500m, -14_550m), (tb.Revenue, tb.Expenses, tb.Profit, tb.CumulativeProfit));
      Assert.Equal((35_350m, 0m, 49_900m), (tb.Assets, tb.Liabilities, tb.Equity));
      Assert.True(tb.PositionReconciles);
      Assert.Equal("QAR", tb.Currency);
      Assert.Equal(1, tb.TotalActivityCount);
      Assert.Equal("6100", Assert.Single(tb.ProfitLossActivity).AccountCode);
      Assert.Equal(6_500m, tb.ProfitLossActivity[0].ExpenseActivity);
      Assert.DoesNotContain(tb.ProfitLossActivity, x => x.JournalNumber is "J-OPEN" or "J-CLOSE");
      Assert.Equal(tb.TotalDebit, tb.TotalCredit);
      var continuation = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "2026-02", "2026-02",
        activityPage: 2, expectedReportIdentity: tb.ReportIdentity);
      Assert.True(continuation.Succeeded, continuation.Message);
      Assert.Equal(tb.ReportIdentity, continuation.Value!.ReportIdentity);
      Assert.Empty(continuation.Value.ProfitLossActivity);
      var unboundContinuation = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "2026-02", "2026-02", activityPage: 2);
      Assert.Equal(ErrorCodes.GenerationStale, unboundContinuation.ErrorCode);
      Assert.Null(unboundContinuation.Value);
      var unboundExport = await FirmExpenseService.ExportTrialBalanceAsync(db, w.A("partner", "Partner"), "2026-02", "2026-02");
      Assert.Equal(ErrorCodes.GenerationStale, unboundExport.ErrorCode);
      Assert.Null(unboundExport.Value);
      var mismatchedExport = await FirmExpenseService.ExportTrialBalanceAsync(db, w.A("partner", "Partner"), "2026-02", "2026-02",
        expectedReportIdentity: new string('0', 64));
      Assert.Equal(ErrorCodes.GenerationStale, mismatchedExport.ErrorCode);
      Assert.Null(mismatchedExport.Value);
      // Month by month: each month carries only its own operating activity, and the months add up to the range totals.
      var twoMonths = (await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "2026-01", "2026-02")).Value!;
      Assert.Equal([("2026-01", 10_000m, 18_000m, -8_000m), ("2026-02", 0m, 6_500m, -6_500m)],
        twoMonths.MonthlyProfitLoss.Select(x => (x.PeriodCode, x.Revenue, x.Expenses, x.Profit)));
      Assert.Equal((twoMonths.Revenue, twoMonths.Expenses, twoMonths.Profit),
        (twoMonths.MonthlyProfitLoss.Sum(x => x.Revenue), twoMonths.MonthlyProfitLoss.Sum(x => x.Expenses), twoMonths.MonthlyProfitLoss.Sum(x => x.Profit)));
      // No accounting period exists a year earlier, so the comparator is absent and not a zero.
      Assert.All(twoMonths.MonthlyProfitLoss, x => Assert.Null(x.PriorYearProfit));
      Assert.Equal(("2026-02", -6_500m), (Assert.Single(tb.MonthlyProfitLoss).PeriodCode, tb.MonthlyProfitLoss[0].Profit));
      // A range far wider than the stored periods is still bounded by them.
      Assert.Equal(2, (await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "1990-01", "2099-12")).Value!.MonthlyProfitLoss.Count);
      var export = await FirmExpenseService.ExportTrialBalanceAsync(db, w.A("partner", "Partner"), "2026-02", "2026-02",
        expectedReportIdentity: tb.ReportIdentity);
      Assert.True(export.Succeeded, export.Message);
      Assert.Equal(tb.ReportIdentity, export.Value!.ReportIdentity);
      Assert.Contains("PROFIT_LOSS_SUMMARY", export.Value!.Csv);
      Assert.Contains("PROFIT_LOSS_ACTIVITY", export.Value.Csv);
      Assert.Contains("\"PROFIT_LOSS_MONTH\",\"2026-02\",\"2026-02\",\"QAR\"", export.Value.Csv);
      Assert.Contains("6500", export.Value.Csv);
      Assert.DoesNotContain("J-OPEN", export.Value.Csv);
      var januaryExport = await FirmExpenseService.ExportTrialBalanceAsync(db, w.A("partner", "Partner"), "2026-01", "2026-01",
        expectedReportIdentity: januaryTb.ReportIdentity);
      Assert.True(januaryExport.Succeeded, januaryExport.Message);
      Assert.Contains("'=HYPERLINK", januaryExport.Value!.Csv);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FirmExpenseService.TrialBalanceAsync(db, w.A("staff", "Staff"), "2026-02", "2026-02")).ErrorCode);
      // Both accepted DateOnly boundaries must remain reportable without inventing a prior year.
      foreach (var edgePeriod in new[] { "0001-01", "9999-12" })
      {
        db.FirmPeriods.Add(new FirmPeriod { Id = Guid.NewGuid(), FirmId = w.FirmId, PeriodCode = edgePeriod });
        await db.SaveChangesAsync();
        var edgeReport = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), edgePeriod, edgePeriod);
        Assert.True(edgeReport.Succeeded, edgeReport.Message);
        var edgeMonth = Assert.Single(edgeReport.Value!.MonthlyProfitLoss);
        Assert.Equal(edgePeriod, edgeMonth.PeriodCode);
        Assert.Equal(0m, edgeMonth.Profit);
        Assert.Null(edgeMonth.PriorYearProfit);
      }
      var oversizedSeries = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "0001-01", "9999-12");
      Assert.False(oversizedSeries.Succeeded);
      Assert.Equal("finance.report-limit", oversizedSeries.ErrorCode);
      Assert.Contains("narrower period range", oversizedSeries.Message);
      foreach (var periodCode in new[] { "1000-01", "1099-12", "1100-01" })
        db.FirmPeriods.Add(new FirmPeriod { Id = Guid.NewGuid(), FirmId = w.FirmId, PeriodCode = periodCode });
      await db.SaveChangesAsync();
      var exactSeriesLimit = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "1000-01", "1099-12");
      Assert.True(exactSeriesLimit.Succeeded, exactSeriesLimit.Message);
      Assert.Equal(1200, exactSeriesLimit.Value!.MonthlyProfitLoss.Count);
      var oneMonthTooMany = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "1000-01", "1100-01");
      Assert.Equal("finance.report-limit", oneMonthTooMany.ErrorCode);
      var listed = await FirmExpenseService.ListAsync(db, reviewer);
      Assert.All(listed, x => Assert.Equal(FirmExpenseStates.Posted, x.Status));

      var returned = await FirmExpenseService.RecordAsync(db, finance, new(new DateOnly(2026, 2, 3), FirmExpenseCategories.Rent,
        "Synthetic property manager", "February rent", 6_500m, "QAR", rent, cash, "return.pdf", "application/pdf", "%PDF source"u8.ToArray()));
      Assert.True(returned.Succeeded, returned.Message);
      Assert.True((await FirmExpenseService.SubmitAsync(db, finance, returned.Value)).Succeeded);
      Assert.Equal("expense.invalid", (await FirmExpenseService.ReviewAsync(db, reviewer, returned.Value, false, "  ")).ErrorCode);
      Assert.Equal("expense.invalid", (await FirmExpenseService.ReviewAsync(db, reviewer, returned.Value, false,
        new string('r', FirmExpenseService.MaxReviewCommentLength + 1))).ErrorCode);
      var beforeReview = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == returned.Value);
      Assert.Equal(FirmExpenseStates.Submitted, beforeReview.Status);
      Assert.Null(beforeReview.ReviewedByUserId);
      Assert.Null(beforeReview.ReviewComment);
      Assert.True((await FirmExpenseService.ReviewAsync(db, reviewer, returned.Value, false, "  Missing receipt date.  ")).Succeeded);
      var rejected = await db.FirmExpenses.AsNoTracking().SingleAsync(x => x.Id == returned.Value);
      Assert.Equal(FirmExpenseStates.Rejected, rejected.Status);
      Assert.Equal("Missing receipt date.", rejected.ReviewComment);
      var fixtureBytes = "%PDF synthetic paging"u8.ToArray();
      for (var i = 0; i < 201; i++)
        db.FirmExpenses.Add(new FirmExpense { Id = Guid.NewGuid(), FirmId = w.FirmId,
          ExpenseDate = new DateOnly(2027, 1, 1), Category = FirmExpenseCategories.Other,
          Payee = $"Synthetic paging {i}", Description = "Paging fixture", Amount = 1m, Currency = "QAR",
          ExpenseAccountId = rent, PaymentAccountId = cash, PreparedByUserId = finance.UserId,
          EvidenceFileName = "paging.pdf", EvidenceContentType = "application/pdf", EvidenceContent = fixtureBytes,
          EvidenceSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(fixtureBytes)).ToLowerInvariant(),
          CreatedAt = DateTimeOffset.Parse("2027-01-01T00:00:00Z") });
      await db.SaveChangesAsync();
      var firstExpenses = await FirmBooksWorkspaceQuery.GetAsync(db, reviewer);
      var secondExpenses = await FirmBooksWorkspaceQuery.GetAsync(db, reviewer, expensePage: 2);
      Assert.True(firstExpenses.Succeeded, firstExpenses.Message);
      Assert.True(secondExpenses.Succeeded, secondExpenses.Message);
      Assert.Equal(200, firstExpenses.Value!.Expenses.Count);
      Assert.True(firstExpenses.Value.HasMoreExpenses);
      Assert.False(secondExpenses.Value!.HasMoreExpenses);
      Assert.Empty(firstExpenses.Value.Expenses.Select(x => x.Id).Intersect(secondExpenses.Value.Expenses.Select(x => x.Id)));
      Assert.Equal(await db.FirmExpenses.CountAsync(x => x.FirmId == w.FirmId), firstExpenses.Value.Expenses.Count + secondExpenses.Value.Expenses.Count);
      Assert.DoesNotContain(firstExpenses.Value.Expenses, x => x.Id == returned.Value);
      var exactOlder = await FirmBooksWorkspaceQuery.GetAsync(db, reviewer, expenseId: returned.Value);
      Assert.Equal(returned.Value, Assert.Single(exactOlder.Value!.Expenses).Id);
      Assert.Empty((await FirmBooksWorkspaceQuery.GetAsync(db, reviewer, expenseId: Guid.NewGuid())).Value!.Expenses);
      Assert.Equal(ErrorCodes.ScopeDenied, (await FirmBooksWorkspaceQuery.GetAsync(db, w.A("staff", "Staff"), expenseId: returned.Value)).ErrorCode);
      Assert.Equal("finance.page-invalid", (await FirmBooksWorkspaceQuery.GetAsync(db, reviewer, expensePage: 0)).ErrorCode);

      Guid priorJanuaryId;
      await using (var priorPeriods = new AuditSphereDbContext(pg.Options))
      {
        var january = await LedgerService.CreateFirmPeriodAsync(priorPeriods, finance, new CreateFirmPeriodRequest("2025-01"));
        Assert.True(january.Succeeded, january.Message);
        priorJanuaryId = january.Value;
        var emptyFebruary = await LedgerService.CreateFirmPeriodAsync(priorPeriods, finance, new CreateFirmPeriodRequest("2025-02"));
        Assert.True(emptyFebruary.Succeeded, emptyFebruary.Message);
      }
      Guid priorJournalId;
      await using (var priorJournal = new AuditSphereDbContext(pg.Options))
      {
        var draft = await LedgerService.CreateFirmJournalDraftAsync(priorJournal, finance,
          new CreateFirmJournalDraftRequest(priorJanuaryId, "J-PRIOR-2025-01", "MANUAL", "FIN06-PRIOR-2025-01", 1,
            "MONTHLY_REVENUE", "QAR",
            [new FirmJournalLineRequest(cash, "Prior-year service receipts", 80m, 0m),
             new FirmJournalLineRequest(revenueAccount, "Prior-year service revenue", 0m, 80m),
             new FirmJournalLineRequest(rent, "Prior-year rent", 20m, 0m),
             new FirmJournalLineRequest(cash, "Prior-year rent payment", 0m, 20m)]));
        Assert.True(draft.Succeeded, draft.Message);
        priorJournalId = draft.Value;
        Assert.True((await LedgerService.SubmitFirmJournalAsync(priorJournal, finance, priorJournalId)).Succeeded);
        Assert.True((await LedgerService.ApproveFirmJournalAsync(priorJournal, reviewer, priorJournalId)).Succeeded);
      }
      await using (var postPrior = new AuditSphereDbContext(pg.Options))
        Assert.True((await LedgerService.PostFirmJournalAsync(postPrior, finance, priorJournalId)).Succeeded);

      await using (var januaryReportDb = new AuditSphereDbContext(pg.Options))
      {
        var january = await FirmExpenseService.TrialBalanceAsync(januaryReportDb, w.A("partner", "Partner"), "2026-01", "2026-01");
        Assert.True(january.Succeeded, january.Message);
        var view = january.Value!;
        Assert.Equal((10_000m, 18_000m, -8_000m), (view.Revenue, view.Expenses, view.Profit));
        var month = Assert.Single(view.MonthlyProfitLoss);
        Assert.Equal(((decimal?)80m, (decimal?)20m, (decimal?)60m),
          (month.PriorYearRevenue, month.PriorYearExpenses, month.PriorYearProfit));
        Assert.Equal((42_060m, 0m, 50_000m, -7_940m), (view.Assets, view.Liabilities, view.Equity, view.CumulativeProfit));
        Assert.True(view.Balanced);
        Assert.True(view.PositionReconciles);
      }
      await using (var februaryReportDb = new AuditSphereDbContext(pg.Options))
      {
        var february = await FirmExpenseService.TrialBalanceAsync(februaryReportDb, w.A("partner", "Partner"), "2026-02", "2026-02");
        Assert.True(february.Succeeded, february.Message);
        var view = february.Value!;
        Assert.Equal((0m, 6_500m, -6_500m), (view.Revenue, view.Expenses, view.Profit));
        var month = Assert.Single(view.MonthlyProfitLoss);
        Assert.Equal((decimal?)0m, month.PriorYearRevenue);
        Assert.Equal((decimal?)0m, month.PriorYearExpenses);
        Assert.Equal((decimal?)0m, month.PriorYearProfit);
        Assert.True(view.PositionReconciles);
      }
    }
  }

  [Fact]
  public async Task FirmTrialBalance_FailsClosedWhenPostedHistoryMixesCurrencies()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var finance = w.A("finance", "FinanceManager");
    var reviewer = w.A("reviewer", "FinanceReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var cash = (await LedgerService.CreateFirmAccountAsync(db, finance,
      new CreateFirmAccountRequest("1000", "Bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
    var capital = (await LedgerService.CreateFirmAccountAsync(db, finance,
      new CreateFirmAccountRequest("3000", "Partners' capital", LedgerStates.AccountEquity, LedgerStates.Credit))).Value;
    var period = (await LedgerService.CreateFirmPeriodAsync(db, finance,
      new CreateFirmPeriodRequest("2026-03"))).Value;
    foreach (var (currency, number) in new[] { ("QAR", "J-QAR"), ("USD", "J-USD") })
    {
      if (currency == "USD")
      {
        var profile = await db.FirmFinanceProfiles.SingleAsync(x => x.FirmId == w.FirmId && x.Approved);
        profile.FunctionalCurrency = currency;
        await db.SaveChangesAsync();
      }
      var draft = await LedgerService.CreateFirmJournalDraftAsync(db, finance,
        new CreateFirmJournalDraftRequest(period, number, "MANUAL", $"CURRENCY-{currency}", 1, "CAPITAL", currency,
          [new FirmJournalLineRequest(cash, "Opening capital", 100m, 0m),
           new FirmJournalLineRequest(capital, "Opening capital", 0m, 100m)]));
      Assert.True(draft.Succeeded, draft.Message);
      var journal = draft.Value;
      Assert.True((await LedgerService.SubmitFirmJournalAsync(db, finance, journal)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await LedgerService.PostFirmJournalAsync(db, finance, journal)).Succeeded);
    }

    var report = await FirmExpenseService.TrialBalanceAsync(db, w.A("partner", "Partner"), "2026-03", "2026-03");
    Assert.Equal("ledger.currency-mixed", report.ErrorCode);
  }

  [Fact]
  public async Task FirmTrialBalance_UsesOnePostedSnapshotAndRechecksSessionAfterSnapshot()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var finance = w.A("finance", "FinanceManager");
    var reviewer = w.A("reviewer", "FinanceReviewer");
    var reader = w.A("partner", "Partner");
    Guid cash, revenue, periodId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      cash = (await LedgerService.CreateFirmAccountAsync(setup, finance,
        new CreateFirmAccountRequest("1000", "Bank", LedgerStates.AccountAsset, LedgerStates.Debit))).Value;
      revenue = (await LedgerService.CreateFirmAccountAsync(setup, finance,
        new CreateFirmAccountRequest("4000", "Service revenue", LedgerStates.AccountRevenue, LedgerStates.Credit))).Value;
      periodId = (await LedgerService.CreateFirmPeriodAsync(setup, finance,
        new CreateFirmPeriodRequest("2026-03"))).Value;
    }

    async Task<Guid> CreateApprovedRevenueJournalAsync(string journalNumber, string sourceKey, decimal amount)
    {
      await using var journalDb = new AuditSphereDbContext(pg.Options);
      var draft = await LedgerService.CreateFirmJournalDraftAsync(journalDb, finance,
        new CreateFirmJournalDraftRequest(periodId, journalNumber, "MANUAL", sourceKey, 1, "MONTHLY_REVENUE", "QAR",
          [new FirmJournalLineRequest(cash, "Service revenue received", amount, 0m),
           new FirmJournalLineRequest(revenue, "Service revenue", 0m, amount)]));
      Assert.True(draft.Succeeded, draft.Message);
      Assert.True((await LedgerService.SubmitFirmJournalAsync(journalDb, finance, draft.Value)).Succeeded);
      Assert.True((await LedgerService.ApproveFirmJournalAsync(journalDb, reviewer, draft.Value)).Succeeded);
      return draft.Value;
    }

    var firstJournalId = await CreateApprovedRevenueJournalAsync("J-SNAPSHOT-100", "SNAPSHOT-100", 100m);
    await using (var postFirst = new AuditSphereDbContext(pg.Options))
      Assert.True((await LedgerService.PostFirmJournalAsync(postFirst, finance, firstJournalId)).Succeeded);
    var stagedJournalId = await CreateApprovedRevenueJournalAsync("J-SNAPSHOT-50", "SNAPSHOT-50", 50m);

    async Task<CommandResult<FirmTrialBalanceView>> ReadWithInterleavingAsync(Func<CancellationToken, Task> interleave)
    {
      var pause = new PauseAfterFirmPostingLinesReader();
      var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options;
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
      await using var reportDb = new AuditSphereDbContext(options);
      var pending = FirmExpenseService.TrialBalanceAsync(reportDb, reader, "2026-03", "2026-03", timeout.Token);
      try
      {
        await pause.ReaderCreated.Task.WaitAsync(TimeSpan.FromSeconds(20), timeout.Token);
        await interleave(timeout.Token);
      }
      catch
      {
        pause.Release.TrySetResult();
        try { await pending.WaitAsync(TimeSpan.FromSeconds(20)); }
        catch { /* Preserve the original barrier/interleaving failure. */ }
        throw;
      }
      pause.Release.TrySetResult();
      return await pending.WaitAsync(TimeSpan.FromSeconds(20), timeout.Token);
    }

    var snapshot = await ReadWithInterleavingAsync(async ct =>
    {
      await using var postSecond = new AuditSphereDbContext(pg.Options);
      var posted = await LedgerService.PostFirmJournalAsync(postSecond, finance, stagedJournalId, ct);
      Assert.True(posted.Succeeded, posted.Message);
    });
    Assert.True(snapshot.Succeeded, snapshot.Message);
    var firstView = snapshot.Value!;
    Assert.Equal((100m, 0m, 100m), (firstView.Revenue, firstView.Expenses, firstView.Profit));
    Assert.Equal((100m, 100m, 100m, 100m),
      (firstView.TotalDebit, firstView.TotalCredit, firstView.Rows.Single(x => x.Code == "1000").ClosingDebit,
        firstView.Rows.Single(x => x.Code == "4000").ClosingCredit));
    Assert.True(firstView.Balanced);
    Assert.True(firstView.PositionReconciles);
    var firstMonth = Assert.Single(firstView.MonthlyProfitLoss);
    Assert.Equal((100m, 0m, 100m), (firstMonth.Revenue, firstMonth.Expenses, firstMonth.Profit));
    Assert.Equal(1, firstView.TotalActivityCount);
    var firstActivity = Assert.Single(firstView.ProfitLossActivity);
    Assert.Equal(100m, firstActivity.RevenueActivity);

    await using (var freshDb = new AuditSphereDbContext(pg.Options))
    {
      var fresh = await FirmExpenseService.TrialBalanceAsync(freshDb, reader, "2026-03", "2026-03");
      Assert.True(fresh.Succeeded, fresh.Message);
      var freshView = fresh.Value!;
      Assert.Equal((150m, 0m, 150m), (freshView.Revenue, freshView.Expenses, freshView.Profit));
      Assert.Equal((150m, 150m, 150m, 150m),
        (freshView.TotalDebit, freshView.TotalCredit, freshView.Rows.Single(x => x.Code == "1000").ClosingDebit,
          freshView.Rows.Single(x => x.Code == "4000").ClosingCredit));
      Assert.Equal((150m, 0m, 150m),
        (Assert.Single(freshView.MonthlyProfitLoss).Revenue, freshView.MonthlyProfitLoss[0].Expenses,
          freshView.MonthlyProfitLoss[0].Profit));
      Assert.Equal(2, freshView.TotalActivityCount);
      Assert.Equal(150m, freshView.ProfitLossActivity.Sum(x => x.RevenueActivity));
      Assert.NotEqual(firstView.ReportIdentity, freshView.ReportIdentity);
      var staleContinuation = await FirmExpenseService.TrialBalanceAsync(freshDb, reader, "2026-03", "2026-03",
        activityPage: 2, expectedReportIdentity: firstView.ReportIdentity);
      Assert.Equal(ErrorCodes.GenerationStale, staleContinuation.ErrorCode);
      Assert.Null(staleContinuation.Value);
      var boundContinuation = await FirmExpenseService.TrialBalanceAsync(freshDb, reader, "2026-03", "2026-03",
        activityPage: 2, expectedReportIdentity: freshView.ReportIdentity);
      Assert.True(boundContinuation.Succeeded, boundContinuation.Message);
      Assert.Equal(freshView.ReportIdentity, boundContinuation.Value!.ReportIdentity);
    }

    await using (var ambientDb = new AuditSphereDbContext(pg.Options))
    {
      await using var callerTransaction = await ambientDb.Database.BeginTransactionAsync();
      try
      {
        var changed = await ambientDb.Users.Where(x => x.Id == reader.UserId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.DisplayName, "uncommitted report probe"));
        Assert.Equal(1, changed);
        var refused = await FirmExpenseService.TrialBalanceAsync(ambientDb, reader, "2026-03", "2026-03");
        Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
        Assert.Same(callerTransaction, ambientDb.Database.CurrentTransaction);
        await using var observer = new AuditSphereDbContext(pg.Options);
        Assert.Equal(w.U["partner"].DisplayName,
          await observer.Users.Where(x => x.Id == reader.UserId).Select(x => x.DisplayName).SingleAsync());
      }
      finally
      {
        await callerTransaction.RollbackAsync();
      }
    }

    var revoked = await ReadWithInterleavingAsync(async ct =>
    {
      await using var revokeSession = new AuditSphereDbContext(pg.Options);
      var changed = await revokeSession.Users.Where(x => x.Id == reader.UserId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), ct);
      Assert.Equal(1, changed);
    });
    Assert.False(revoked.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, revoked.ErrorCode);
  }

  private sealed class PauseAfterFirmPostingLinesReader : DbCommandInterceptor
  {
    private int paused;
    public TaskCompletionSource ReaderCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_posting_lines", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.Exchange(ref paused, 1) == 0)
      {
        ReaderCreated.TrySetResult();
        await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
      }
      return result;
    }
  }
}
