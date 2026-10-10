using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record EndOfServiceAccountOption(Guid Id, string Code, string Name);
public sealed record EndOfServicePeriodOption(Guid Id, string PeriodCode);
public sealed record EndOfServiceTreatmentView(Guid Id, long Version, string Status, string MeasurementTreatment,
  string AccountantName, string AccountantCredential, string ProvisionAccount, string ExpenseAccount,
  string RecordedBy, DateTimeOffset RecordedAt, string? ConfirmedBy, DateTimeOffset? ConfirmedAt, string? ConfirmationNote);
public sealed record EndOfServiceAccrualView(Guid Id, Guid JournalId, string JournalNumber, string PeriodCode, decimal Amount,
  string Currency, string Method, string Inputs, DateOnly CalculationDate, string Reason, string JournalStatus,
  string PreparedBy, long TreatmentVersion, DateTimeOffset CreatedAt);
public sealed record EndOfServiceWorkspace(
  bool CanRecordTreatment, bool CanConfirmTreatment, bool CanPrepareAccrual, string? AccrualBlockedReason,
  EndOfServiceTreatmentView? Treatment, long TreatmentVersion, string? Currency, decimal PostedProvisionBalance,
  IReadOnlyList<EndOfServiceAccountOption> LiabilityAccounts, IReadOnlyList<EndOfServiceAccountOption> ExpenseAccounts,
  IReadOnlyList<EndOfServicePeriodOption> OpenPeriods, IReadOnlyList<EndOfServiceAccrualView> Accruals,
  int MaxEvidenceBytes);

/// <summary>
/// End-of-service provision workbench projection for firm-wide finance users and Partners. Every figure is an entered
/// amount or a sum of posted ledger lines; nothing here estimates the obligation.
/// </summary>
public static class EndOfServiceWorkspaceQuery
{
  private static readonly string[] ReadRoles = ["FinanceManager", "FinanceReviewer", "Partner"];

  public static async Task<CommandResult<EndOfServiceWorkspace>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (!await HoldsAsync(db, actor, ReadRoles, ct))
      return CommandResult<EndOfServiceWorkspace>.Fail(ErrorCodes.ScopeDenied,
        "The end-of-service provision requires a firm-wide finance or Partner assignment.");
    var manager = await HoldsAsync(db, actor, ["FinanceManager"], ct);
    var partner = await HoldsAsync(db, actor, ["Partner"], ct);

    var accounts = await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderBy(x => x.Code).ToListAsync(ct);
    var byId = accounts.ToDictionary(x => x.Id);
    string AccountLabel(Guid id) => byId.TryGetValue(id, out var a) ? $"{a.Code} · {a.Name}" : "Account unavailable";
    var treatment = await LedgerService.LatestEndOfServiceTreatmentAsync(db, actor.FirmId, ct);
    var confirmed = treatment?.Status == EndOfServiceTreatmentStates.Confirmed;
    var profile = await db.FirmFinanceProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Approved, ct);
    var openPeriods = await db.FirmPeriods.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Status == LedgerStates.PeriodOpen).OrderByDescending(x => x.PeriodCode).Take(60)
      .Select(x => new EndOfServicePeriodOption(x.Id, x.PeriodCode)).ToListAsync(ct);

    var rows = await (
      from accrual in db.FirmEndOfServiceAccruals.AsNoTracking()
      join journal in db.FirmJournals.AsNoTracking() on new { accrual.FirmId, accrual.JournalId } equals new { journal.FirmId, JournalId = journal.Id }
      join period in db.FirmPeriods.AsNoTracking() on new { accrual.FirmId, accrual.PeriodId } equals new { period.FirmId, PeriodId = period.Id }
      join version in db.FirmEndOfServiceTreatments.AsNoTracking() on new { accrual.FirmId, accrual.TreatmentId } equals new { version.FirmId, TreatmentId = version.Id }
      where accrual.FirmId == actor.FirmId
      orderby accrual.CreatedAt descending, accrual.Id descending
      select new
      {
        accrual.Id, accrual.JournalId, journal.JournalNumber, period.PeriodCode, accrual.Amount, journal.Currency, accrual.Method,
        accrual.Inputs, accrual.CalculationDate, accrual.Reason, journal.Status, accrual.CreatedByUserId, version.Version, accrual.CreatedAt
      }).Take(100).ToListAsync(ct);

    var userIds = rows.Select(x => x.CreatedByUserId)
      .Concat(treatment is null ? [] : new[] { treatment.RecordedByUserId }.Concat(treatment.ConfirmedByUserId is { } c ? [c] : []))
      .Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && userIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    string Name(Guid id) => names.TryGetValue(id, out var n) ? n : "User unavailable";

    // The provision carried in the ledger: posted credits less posted debits on the confirmed provision account.
    var balance = 0m;
    if (treatment is not null)
    {
      var totals = await db.FirmPostingLines.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.FirmAccountId == treatment.ProvisionAccountId)
        .GroupBy(x => 1).Select(g => new { Debit = g.Sum(x => x.Debit), Credit = g.Sum(x => x.Credit) }).SingleOrDefaultAsync(ct);
      balance = MoneyPolicy.Normalize((totals?.Credit ?? 0m) - (totals?.Debit ?? 0m));
    }

    var blocked = !confirmed ? LedgerService.EndOfServiceUnconfirmed
      : profile is null ? "An approved finance profile is required before an accrual can be prepared."
      : openPeriods.Count == 0 ? "Open a firm period before preparing an accrual."
      : null;
    // Authority is re-read after the data so a grant revoked mid-request cannot return protected content.
    if (!await HoldsAsync(db, actor, ReadRoles, ct))
      return CommandResult<EndOfServiceWorkspace>.Fail(ErrorCodes.ScopeDenied,
        "The end-of-service provision requires a firm-wide finance or Partner assignment.");

    return CommandResult<EndOfServiceWorkspace>.Ok(new(
      manager,
      partner && treatment is { Status: EndOfServiceTreatmentStates.Recorded } && treatment.RecordedByUserId != actor.UserId,
      manager && blocked is null, blocked,
      treatment is null ? null : new(treatment.Id, treatment.Version, treatment.Status, treatment.MeasurementTreatment,
        treatment.AccountantName, treatment.AccountantCredential, AccountLabel(treatment.ProvisionAccountId),
        AccountLabel(treatment.ExpenseAccountId), Name(treatment.RecordedByUserId), treatment.RecordedAt,
        treatment.ConfirmedByUserId is { } confirmer ? Name(confirmer) : null, treatment.ConfirmedAt, treatment.ConfirmationNote),
      treatment?.Version ?? 0, profile?.FunctionalCurrency, balance,
      Options(accounts, LedgerStates.AccountLiability, LedgerStates.Credit), Options(accounts, LedgerStates.AccountExpense, LedgerStates.Debit),
      openPeriods,
      rows.Select(x => new EndOfServiceAccrualView(x.Id, x.JournalId, x.JournalNumber, x.PeriodCode, x.Amount, x.Currency, x.Method,
        x.Inputs, x.CalculationDate, x.Reason, x.Status, Name(x.CreatedByUserId), x.Version, x.CreatedAt)).ToList(),
      LedgerService.MaxJournalEvidenceBytes));
  }

  private static List<EndOfServiceAccountOption> Options(IEnumerable<FirmAccount> accounts, string type, string side) =>
    accounts.Where(x => x.PostingAllowed && x.AccountType == type && x.NormalSide == side)
      .Select(x => new EndOfServiceAccountOption(x.Id, x.Code, x.Name)).ToList();

  private static async Task<bool> HoldsAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct)).Succeeded;
}
