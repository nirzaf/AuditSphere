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

/// <summary>
/// ADR-0010 / STE-NXT-014: end-of-service accruals are entered by a person with their basis, use the journal maker
/// and checker, and are refused at draft, at posting and by the database until the accounting treatment has been
/// recorded by finance and confirmed by a different Partner. Exercised against PostgreSQL through the real commands.
/// </summary>
[Trait("Profile", "Database")]
public sealed class EndOfServiceAccrualTests
{
  private sealed record World(Guid FirmId, ActorContext Manager, ActorContext Reviewer, ActorContext Partner,
    ActorContext ManagerPartner, Guid ProvisionId, Guid ExpenseId, Guid SalariesId, Guid CashId, Guid PeriodId);

  private const string Treatment = "Projected obligation measured per the firm's reporting framework, undiscounted, based on final salary and completed service.";

  private static AppUser User(Guid firmId, string label) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = $"sub-{label}-{Guid.NewGuid():N}", TenantId = "tenant-eos",
    Email = $"{label.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test", DisplayName = label, UserKind = "Staff",
    SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ActorContext Actor(AppUser user, params string[] roles) => new(user.Id, user.FirmId, user.SessionEpoch, roles);

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    var manager = User(firmId, "FinanceManager"); var reviewer = User(firmId, "FinanceReviewer");
    var partner = User(firmId, "Partner"); var both = User(firmId, "ManagerPartner");
    db.Users.AddRange(manager, reviewer, partner, both);
    db.FirmFinanceProfiles.Add(new FirmFinanceProfile
    {
      Id = Guid.NewGuid(), FirmId = firmId, FunctionalCurrency = "QAR", ProfileKind = BillingStates.TestProfile, Approved = true,
      ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    foreach (var (user, role) in new[] { (manager, "FinanceManager"), (reviewer, "FinanceReviewer"), (partner, "Partner"),
      (both, "FinanceManager"), (both, "Partner") })
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id });
    await db.SaveChangesAsync();
    var managerActor = Actor(manager, "FinanceManager");
    async Task<Guid> Account(string code, string name, string type, string side)
    {
      var result = await LedgerService.CreateFirmAccountAsync(db, managerActor, new CreateFirmAccountRequest(code, name, type, side));
      Assert.True(result.Succeeded, result.Message);
      return result.Value;
    }
    var provision = await Account("2400", "End-of-service provision", LedgerStates.AccountLiability, LedgerStates.Credit);
    var expense = await Account("6150", "End-of-service benefit expense", LedgerStates.AccountExpense, LedgerStates.Debit);
    var salaries = await Account("6100", "Salaries", LedgerStates.AccountExpense, LedgerStates.Debit);
    var cash = await Account("1000", "Operating bank", LedgerStates.AccountAsset, LedgerStates.Debit);
    var period = await LedgerService.CreateFirmPeriodAsync(db, managerActor, new CreateFirmPeriodRequest("2026-10"));
    Assert.True(period.Succeeded, period.Message);
    return new(firmId, managerActor, Actor(reviewer, "FinanceReviewer"), Actor(partner, "Partner"),
      Actor(both, "FinanceManager", "Partner"), provision, expense, salaries, cash, period.Value);
  }

  private static RecordEndOfServiceTreatmentRequest TreatmentRequest(World w, long? expected = null) =>
    new(Treatment, "N. Accountant", "CPA licence 12345", w.ProvisionId, w.ExpenseId, expected);

  private static CreateEndOfServiceAccrualRequest Accrual(World w, decimal amount = 12500m, Guid? requestId = null) =>
    new(w.PeriodId, amount, "One month of the annual gratuity per employee", "42 staff; payroll schedule October 2026",
      DateOnly.FromDateTime(DateTime.UtcNow), "Monthly accrual for October 2026", requestId ?? Guid.NewGuid());

  private static async Task<Guid> ConfirmedTreatmentAsync(AuditSphereDbContext db, World w)
  {
    var recorded = await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.Manager, TreatmentRequest(w));
    Assert.True(recorded.Succeeded, recorded.Message);
    var confirmed = await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.Partner, recorded.Value, "Confirmed as the firm's treatment");
    Assert.True(confirmed.Succeeded, confirmed.Message);
    return recorded.Value;
  }

  [Fact]
  public async Task Accruals_AreBlocked_UntilTheTreatmentIsRecordedAndConfirmedByADifferentPartner()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    // Nothing recorded: the accrual fails closed and the workspace says why.
    Assert.Equal(ErrorCodes.GateBlocked, (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w))).ErrorCode);
    var empty = (await EndOfServiceWorkspaceQuery.GetAsync(db, w.Manager)).Value!;
    Assert.Equal((true, false, false, 0L), (empty.CanRecordTreatment, empty.CanConfirmTreatment, empty.CanPrepareAccrual, empty.TreatmentVersion));
    Assert.NotNull(empty.AccrualBlockedReason);

    // Only finance records; the accounts must be a liability provision and an expense.
    Assert.Equal(ErrorCodes.ScopeDenied, (await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.Partner, TreatmentRequest(w))).ErrorCode);
    Assert.Equal("ledger.end-of-service-accounts-invalid", (await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.Manager,
      TreatmentRequest(w) with { ProvisionAccountId = w.CashId })).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.Manager,
      TreatmentRequest(w) with { MeasurementTreatment = "too short" })).ErrorCode);
    var recorded = await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.ManagerPartner, TreatmentRequest(w, expected: 0));
    Assert.True(recorded.Succeeded, recorded.Message);
    Assert.Equal(recorded.Value, (await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.ManagerPartner, TreatmentRequest(w))).Value);
    Assert.Equal(ErrorCodes.StaleRevision, (await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.Manager,
      TreatmentRequest(w, expected: 0) with { AccountantName = "Another Accountant" })).ErrorCode);

    // Recorded is not confirmed: still blocked. Finance cannot confirm; nor can the recorder even as a Partner.
    Assert.Equal(ErrorCodes.GateBlocked, (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w))).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.Manager, recorded.Value, "Finance self-confirmation")).ErrorCode);
    Assert.Equal(ErrorCodes.ProtectedState, (await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.ManagerPartner, recorded.Value, "Recorder confirming own record")).ErrorCode);
    Assert.False((await EndOfServiceWorkspaceQuery.GetAsync(db, w.ManagerPartner)).Value!.CanConfirmTreatment);
    Assert.True((await EndOfServiceWorkspaceQuery.GetAsync(db, w.Partner)).Value!.CanConfirmTreatment);
    Assert.Equal("ledger.invalid", (await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.Partner, recorded.Value, "ok")).ErrorCode);
    Assert.True((await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.Partner, recorded.Value, "Confirmed as the firm's treatment")).Succeeded);
    Assert.True((await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.Partner, recorded.Value, "Replay of the confirmation")).Succeeded);

    var ready = (await EndOfServiceWorkspaceQuery.GetAsync(db, w.Manager)).Value!;
    Assert.Equal((true, null, "CONFIRMED", "QAR"), (ready.CanPrepareAccrual, ready.AccrualBlockedReason, ready.Treatment!.Status, ready.Currency));
    Assert.False((await EndOfServiceWorkspaceQuery.GetAsync(db, w.Reviewer)).Value!.CanPrepareAccrual);

    // A confirmed treatment is final and its content can never be rewritten, even by direct SQL.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE firm_end_of_service_treatments SET measurement_treatment = 'A different treatment entirely, rewritten' WHERE id = {recorded.Value}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE firm_end_of_service_treatments SET status = 'RECORDED', confirmed_by_user_id = NULL, confirmed_at = NULL, confirmation_note = NULL WHERE id = {recorded.Value}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM firm_end_of_service_treatments WHERE id = {recorded.Value}"));
  }

  [Fact]
  public async Task Accrual_NeedsItsBasis_UsesMakerChecker_AndPostsToProvisionAndExpense()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ConfirmedTreatmentAsync(db, w);

    // No basis, no reason, a future calculation date, a zero or over-precise amount: all refused.
    Assert.Equal("ledger.invalid", (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w) with { Method = " " })).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w) with { Inputs = "" })).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w) with { Reason = "x" })).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager,
      Accrual(w) with { CalculationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30) })).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w, amount: 0m))).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w, amount: 10.005m))).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Reviewer, Accrual(w))).ErrorCode);

    // The generic manual-journal command cannot create an accrual without a basis, nor on other accounts.
    var lines = new[] { new FirmJournalLineRequest(w.ExpenseId, "Accrual", 100m, 0m), new FirmJournalLineRequest(w.ProvisionId, "Accrual", 0m, 100m) };
    Assert.Equal("ledger.invalid", (await LedgerService.CreateFirmJournalDraftAsync(db, w.Manager, new CreateFirmJournalDraftRequest(
      w.PeriodId, "EOS-RAW-1", "MANUAL", "RAW-1", 1, LedgerStates.EndOfServiceAccrualPurpose, "QAR", lines))).ErrorCode);
    var basis = new EndOfServiceAccrualBasis("Entered method", "Entered inputs", DateOnly.FromDateTime(DateTime.UtcNow), "Entered reason");
    Assert.Equal("ledger.end-of-service-accounts-invalid", (await LedgerService.CreateFirmJournalDraftAsync(db, w.Manager,
      new CreateFirmJournalDraftRequest(w.PeriodId, "EOS-RAW-2", "MANUAL", "RAW-2", 1, LedgerStates.EndOfServiceAccrualPurpose, "QAR",
        [new FirmJournalLineRequest(w.SalariesId, "Accrual", 100m, 0m), new FirmJournalLineRequest(w.ProvisionId, "Accrual", 0m, 100m)],
        EndOfServiceBasis: basis))).ErrorCode);
    Assert.Equal("ledger.invalid", (await LedgerService.CreateFirmJournalDraftAsync(db, w.Manager, new CreateFirmJournalDraftRequest(
      w.PeriodId, "MAN-1", "MANUAL", "MAN-1", 1, "MANUAL", "QAR", lines, EndOfServiceBasis: basis))).ErrorCode);

    // A retry with the same request identity returns the same draft; a changed basis under it conflicts.
    var request = Accrual(w);
    var draft = await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, request);
    Assert.True(draft.Succeeded, draft.Message);
    Assert.Equal(draft.Value, (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, request)).Value);
    Assert.Equal(ErrorCodes.IdempotencyConflict, (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager,
      request with { Inputs = "43 staff; revised payroll schedule" })).ErrorCode);
    var stored = await db.FirmEndOfServiceAccruals.AsNoTracking().SingleAsync();
    Assert.Equal((12500m, request.Method, request.Inputs, request.Reason), (stored.Amount, stored.Method, stored.Inputs, stored.Reason));

    // Maker and checker: the preparer cannot approve; an independent reviewer does; then the accrual posts.
    Assert.True((await LedgerService.SubmitFirmJournalAsync(db, w.Manager, draft.Value)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await LedgerService.ApproveFirmJournalAsync(db, w.Manager, draft.Value)).ErrorCode);
    Assert.True((await LedgerService.ApproveFirmJournalAsync(db, w.Reviewer, draft.Value)).Succeeded);
    var posted = await LedgerService.PostFirmJournalAsync(db, w.Manager, draft.Value);
    Assert.True(posted.Succeeded, posted.Message);
    var postedLines = await db.FirmPostingLines.AsNoTracking().Where(x => x.PostingId == posted.Value).ToListAsync();
    Assert.Equal((12500m, 12500m), (postedLines.Single(x => x.FirmAccountId == w.ExpenseId).Debit, postedLines.Single(x => x.FirmAccountId == w.ProvisionId).Credit));
    Assert.DoesNotContain(postedLines, x => x.FirmAccountId == w.SalariesId);
    var workspace = (await EndOfServiceWorkspaceQuery.GetAsync(db, w.Reviewer)).Value!;
    Assert.Equal(12500m, workspace.PostedProvisionBalance);
    Assert.Equal((LedgerStates.JournalPosted, 12500m), (workspace.Accruals.Single().JournalStatus, workspace.Accruals.Single().Amount));

    // The entered basis is append-only; a correction is a reversing journal, which brings the provision back down.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE firm_end_of_service_accruals SET amount = 1 WHERE id = {stored.Id}"));
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM firm_end_of_service_accruals WHERE id = {stored.Id}"));
    Assert.True((await LedgerService.ReverseFirmPostingAsync(db, w.Reviewer,
      new ReverseFirmPostingRequest(posted.Value, w.PeriodId, "Accrual entered for the wrong headcount"))).Succeeded);
    Assert.Equal(0m, (await EndOfServiceWorkspaceQuery.GetAsync(db, w.Reviewer)).Value!.PostedProvisionBalance);
  }

  [Fact]
  public async Task Posting_IsRefused_WhenTheTreatmentChanges_AndTheDatabaseHoldsTheSameLine()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ConfirmedTreatmentAsync(db, w);
    var draft = await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w));
    Assert.True(draft.Succeeded, draft.Message);
    Assert.True((await LedgerService.SubmitFirmJournalAsync(db, w.Manager, draft.Value)).Succeeded);
    Assert.True((await LedgerService.ApproveFirmJournalAsync(db, w.Reviewer, draft.Value)).Succeeded);

    // Finance records a new treatment version: until a Partner confirms it, nothing can be prepared or posted.
    var next = await LedgerService.RecordEndOfServiceTreatmentAsync(db, w.Manager,
      TreatmentRequest(w, expected: 1) with { AccountantCredential = "CPA licence 67890" });
    Assert.True(next.Succeeded, next.Message);
    Assert.Equal(ErrorCodes.GateBlocked, (await LedgerService.PostFirmJournalAsync(db, w.Manager, draft.Value)).ErrorCode);
    Assert.Equal(ErrorCodes.GateBlocked, (await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w))).ErrorCode);
    // Even with the new version confirmed, an accrual prepared under the replaced treatment does not post.
    Assert.True((await LedgerService.ConfirmEndOfServiceTreatmentAsync(db, w.Partner, next.Value, "Confirmed the revised treatment")).Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, (await LedgerService.PostFirmJournalAsync(db, w.Manager, draft.Value)).ErrorCode);
    Assert.Equal(LedgerStates.JournalApproved, (await db.FirmJournals.AsNoTracking().SingleAsync(x => x.Id == draft.Value)).Status);
    Assert.Empty(await db.FirmPostings.AsNoTracking().Where(x => x.JournalId == draft.Value).ToListAsync());

    // Second lock: a posting row inserted behind the Application gate is rejected by the database itself.
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO firm_postings (id, firm_id, period_id, journal_id, currency, posted_by_user_id, posted_at)
      VALUES ({Guid.NewGuid()}, {w.FirmId}, {w.PeriodId}, {draft.Value}, 'QAR', {w.Manager.UserId}, now())
      """));

    // A fresh accrual under the current confirmed treatment goes through.
    var fresh = await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, w.Manager, Accrual(w, amount: 9000m));
    Assert.True(fresh.Succeeded, fresh.Message);
    Assert.True((await LedgerService.SubmitFirmJournalAsync(db, w.Manager, fresh.Value)).Succeeded);
    Assert.True((await LedgerService.ApproveFirmJournalAsync(db, w.Reviewer, fresh.Value)).Succeeded);
    Assert.True((await LedgerService.PostFirmJournalAsync(db, w.Manager, fresh.Value)).Succeeded);
  }
}
