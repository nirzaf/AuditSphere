using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>The calculation a person performed outside the platform: its method, inputs and date, with the reason for the entry.</summary>
public sealed record EndOfServiceAccrualBasis(string Method, string Inputs, DateOnly CalculationDate, string Reason);

public sealed record RecordEndOfServiceTreatmentRequest(
  string MeasurementTreatment,
  string AccountantName,
  string AccountantCredential,
  Guid ProvisionAccountId,
  Guid ExpenseAccountId,
  long? ExpectedVersion = null);

public sealed record CreateEndOfServiceAccrualRequest(
  Guid PeriodId,
  decimal Amount,
  string Method,
  string Inputs,
  DateOnly CalculationDate,
  string Reason,
  Guid RequestId,
  string? SupportingEvidenceFileName = null,
  string? SupportingEvidenceContentType = null,
  byte[]? SupportingEvidenceContent = null);

/// <summary>
/// End-of-service provision accruals (ADR-0010, STE-NXT-014). A person enters the amount and its basis; the platform
/// computes nothing. Accruals use the ordinary journal maker and checker, and are refused — at draft and again at
/// posting — until the firm's accounting treatment has been recorded by finance and confirmed by a Partner.
/// </summary>
public static partial class LedgerService
{
  private static readonly string[] OwnerRoles = ["Partner"];

  /// <summary>Finance records the treatment a qualified accountant named. It governs nothing until a Partner confirms it.</summary>
  public static async Task<CommandResult<Guid>> RecordEndOfServiceTreatmentAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordEndOfServiceTreatmentRequest request, CancellationToken ct = default)
  {
    var text = (request.MeasurementTreatment ?? string.Empty).Trim();
    var name = (request.AccountantName ?? string.Empty).Trim();
    var credential = (request.AccountantCredential ?? string.Empty).Trim();
    if (text.Length is < 20 or > 4000)
      return CommandResult<Guid>.Fail("ledger.invalid", "State the measurement basis and the standard it follows in 20 to 4000 characters.");
    if (name.Length is < 2 or > 200 || credential.Length is < 2 or > 300)
      return CommandResult<Guid>.Fail("ledger.invalid", "Name the qualified accountant and their qualification or membership reference.");
    if (request.ProvisionAccountId == request.ExpenseAccountId)
      return CommandResult<Guid>.Fail("ledger.invalid", "The provision and expense accounts must differ.");
    var auth = await AuthorizeFirmAsync(db, actor, ManagerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var accounts = await db.FirmAccounts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && (x.Id == request.ProvisionAccountId || x.Id == request.ExpenseAccountId))
      .ToDictionaryAsync(x => x.Id, ct);
    if (!accounts.TryGetValue(request.ProvisionAccountId, out var provision) ||
        !accounts.TryGetValue(request.ExpenseAccountId, out var expense))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "An account is outside the firm scope.");
    if (provision.AccountType != LedgerStates.AccountLiability || provision.NormalSide != LedgerStates.Credit || !provision.PostingAllowed)
      return CommandResult<Guid>.Fail("ledger.end-of-service-accounts-invalid",
        "The provision account must be a credit-normal liability account that is open for posting.");
    if (expense.AccountType != LedgerStates.AccountExpense || expense.NormalSide != LedgerStates.Debit || !expense.PostingAllowed)
      return CommandResult<Guid>.Fail("ledger.end-of-service-accounts-invalid",
        "The expense account must be a debit-normal expense account that is open for posting.");

    var latest = await LatestEndOfServiceTreatmentAsync(db, actor.FirmId, ct);
    if (request.ExpectedVersion.HasValue && request.ExpectedVersion != (latest?.Version ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The recorded treatment changed; review the current version.");
    if (latest is not null && latest.MeasurementTreatment == text && latest.AccountantName == name &&
        latest.AccountantCredential == credential && latest.ProvisionAccountId == provision.Id && latest.ExpenseAccountId == expense.Id)
    {
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(latest.Id); // an unchanged treatment is idempotent
    }
    var treatment = new FirmEndOfServiceTreatment
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Version = (latest?.Version ?? 0) + 1,
      MeasurementTreatment = text, AccountantName = name, AccountantCredential = credential,
      ProvisionAccountId = provision.Id, ExpenseAccountId = expense.Id,
      Status = EndOfServiceTreatmentStates.Recorded, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow
    };
    db.FirmEndOfServiceTreatments.Add(treatment);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult<Guid>.Fail("ledger.conflict", "The recorded treatment changed; reload and retry."); }
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(treatment.Id);
  }

  /// <summary>The owner's confirmation: a firm Partner, never the person who recorded the treatment, with a stated note.</summary>
  public static async Task<CommandResult> ConfirmEndOfServiceTreatmentAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid treatmentId, string note, CancellationToken ct = default)
  {
    var trimmed = (note ?? string.Empty).Trim();
    if (trimmed.Length is < 5 or > 1000)
      return CommandResult.Fail("ledger.invalid", "Record a confirmation note of 5 to 1000 characters.");
    var auth = await AuthorizeFirmAsync(db, actor, OwnerRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var treatment = await db.FirmEndOfServiceTreatments.SingleOrDefaultAsync(x => x.Id == treatmentId && x.FirmId == actor.FirmId, ct);
    if (treatment is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (treatment.Status == EndOfServiceTreatmentStates.Confirmed)
      return treatment.ConfirmedByUserId == actor.UserId
        ? CommandResult.Ok()
        : CommandResult.Fail(ErrorCodes.ProtectedState, "This treatment version is already confirmed.");
    var latest = await LatestEndOfServiceTreatmentAsync(db, actor.FirmId, ct);
    if (latest is null || latest.Id != treatment.Id)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "A newer treatment version was recorded; review the current version.");
    if (treatment.RecordedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The person who recorded the treatment cannot confirm it.");
    treatment.Status = EndOfServiceTreatmentStates.Confirmed;
    treatment.ConfirmedByUserId = actor.UserId;
    treatment.ConfirmedAt = DateTimeOffset.UtcNow;
    treatment.ConfirmationNote = trimmed;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>
  /// Prepares the accrual as a two-line draft journal — expense debit, provision credit — for the entered amount.
  /// The request identity makes a retry return the same draft. Submit, review and post use the ordinary journal path.
  /// </summary>
  public static async Task<CommandResult<Guid>> CreateEndOfServiceAccrualDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateEndOfServiceAccrualRequest request, CancellationToken ct = default)
  {
    if (request.RequestId == Guid.Empty)
      return CommandResult<Guid>.Fail("ledger.invalid", "An accrual request identity is required.");
    if (request.Amount <= 0 || MoneyPolicy.Normalize(request.Amount, 2) != request.Amount)
      return CommandResult<Guid>.Fail("ledger.invalid", "Enter the accrual as a positive amount with at most two decimal places.");
    var auth = await AuthorizeFirmAsync(db, actor, ManagerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.FirmPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PeriodId && x.FirmId == actor.FirmId, ct);
    if (period is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var treatment = await LatestEndOfServiceTreatmentAsync(db, actor.FirmId, ct);
    if (treatment is null || treatment.Status != EndOfServiceTreatmentStates.Confirmed)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, EndOfServiceUnconfirmed);
    var profile = await db.FirmFinanceProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Approved, ct);
    if (profile is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved finance profile is required before an accrual can be prepared.");
    var description = $"End-of-service accrual {period.PeriodCode}";
    return await CreateFirmJournalDraftAsync(db, actor, new CreateFirmJournalDraftRequest(
      period.Id, $"EOS-{period.PeriodCode}-{request.RequestId:N}", "MANUAL", $"END-OF-SERVICE:{request.RequestId:D}", 1,
      LedgerStates.EndOfServiceAccrualPurpose, profile.FunctionalCurrency,
      [
        new FirmJournalLineRequest(treatment.ExpenseAccountId, description, request.Amount, 0m),
        new FirmJournalLineRequest(treatment.ProvisionAccountId, description, 0m, request.Amount)
      ],
      request.SupportingEvidenceFileName, request.SupportingEvidenceContentType, request.SupportingEvidenceContent,
      new EndOfServiceAccrualBasis(request.Method, request.Inputs, request.CalculationDate, request.Reason)), ct);
  }

  internal const string EndOfServiceUnconfirmed =
    "End-of-service accruals are blocked: the accounting treatment named by a qualified accountant must be recorded and confirmed by a Partner first.";

  internal static Task<FirmEndOfServiceTreatment?> LatestEndOfServiceTreatmentAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmEndOfServiceTreatments.AsNoTracking().Where(x => x.FirmId == firmId)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);

  /// <summary>An accrual journal always carries its entered basis, and no other journal may.</summary>
  private static string? ValidateEndOfServiceBasis(CreateFirmJournalDraftRequest request)
  {
    var isAccrual = request.PostingPurpose.Trim().ToUpperInvariant() == LedgerStates.EndOfServiceAccrualPurpose;
    if (!isAccrual)
      return request.EndOfServiceBasis is null ? null : "Only an end-of-service accrual carries a calculation basis.";
    if (!request.SourceKind.Trim().Equals("MANUAL", StringComparison.OrdinalIgnoreCase))
      return "An end-of-service accrual is a manual journal.";
    if (request.EndOfServiceBasis is not { } basis)
      return "An end-of-service accrual needs its calculation basis (method, inputs, date) and a reason.";
    if (string.IsNullOrWhiteSpace(basis.Method) || basis.Method.Trim().Length is < 5 or > 2000)
      return "Describe the calculation method in 5 to 2000 characters.";
    if (string.IsNullOrWhiteSpace(basis.Inputs) || basis.Inputs.Trim().Length is < 5 or > 4000)
      return "Record the calculation inputs in 5 to 4000 characters.";
    if (string.IsNullOrWhiteSpace(basis.Reason) || basis.Reason.Trim().Length is < 5 or > 1000)
      return "Record the reason for the accrual in 5 to 1000 characters.";
    if (basis.CalculationDate == default || basis.CalculationDate > DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
      return "Record the date the calculation was performed; it cannot be in the future.";
    return null;
  }

  /// <summary>Fails closed unless the latest treatment version is confirmed and the lines use exactly its two accounts.</summary>
  private static async Task<CommandResult<FirmEndOfServiceTreatment>> RequireEndOfServiceTreatmentAsync(
    IAuditSphereDbContext db, Guid firmId, IReadOnlyList<FirmJournalLineRequest> lines, CancellationToken ct)
  {
    var treatment = await LatestEndOfServiceTreatmentAsync(db, firmId, ct);
    if (treatment is null || treatment.Status != EndOfServiceTreatmentStates.Confirmed)
      return CommandResult<FirmEndOfServiceTreatment>.Fail(ErrorCodes.GateBlocked, EndOfServiceUnconfirmed);
    if (!UsesTreatmentAccounts(treatment, lines.Select(x => (x.FirmAccountId, x.Debit, x.Credit))))
      return CommandResult<FirmEndOfServiceTreatment>.Fail("ledger.end-of-service-accounts-invalid",
        "An end-of-service accrual debits only the confirmed expense account and credits only the confirmed provision account.");
    return CommandResult<FirmEndOfServiceTreatment>.Ok(treatment);
  }

  private static bool UsesTreatmentAccounts(FirmEndOfServiceTreatment treatment, IEnumerable<(Guid AccountId, decimal Debit, decimal Credit)> lines)
  {
    var rows = lines.ToList();
    return rows.Any(x => x.Debit > 0) && rows.Any(x => x.Credit > 0) &&
      rows.Where(x => x.Debit > 0).All(x => x.AccountId == treatment.ExpenseAccountId) &&
      rows.Where(x => x.Credit > 0).All(x => x.AccountId == treatment.ProvisionAccountId);
  }

  private static FirmEndOfServiceAccrual EndOfServiceAccrualFor(
    FirmJournal journal, FirmEndOfServiceTreatment treatment, CreateFirmJournalDraftRequest request)
  {
    var basis = request.EndOfServiceBasis!;
    return new FirmEndOfServiceAccrual
    {
      Id = Guid.CreateVersion7(), FirmId = journal.FirmId, JournalId = journal.Id, TreatmentId = treatment.Id,
      PeriodId = journal.PeriodId, Amount = MoneyPolicy.Normalize(request.Lines.Sum(x => x.Debit)),
      Method = basis.Method.Trim(), Inputs = basis.Inputs.Trim(), CalculationDate = basis.CalculationDate,
      Reason = basis.Reason.Trim(), CreatedByUserId = journal.CreatedByUserId, CreatedAt = journal.CreatedAt
    };
  }

  /// <summary>A replayed draft request matches only when its entered basis is the stored basis.</summary>
  private static async Task<bool> MatchesEndOfServiceBasisAsync(
    IAuditSphereDbContext db, Guid firmId, Guid journalId, EndOfServiceAccrualBasis? basis, CancellationToken ct)
  {
    var stored = await db.FirmEndOfServiceAccruals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.JournalId == journalId, ct);
    if (stored is null || basis is null) return stored is null && basis is null;
    return stored.Method == basis.Method.Trim() && stored.Inputs == basis.Inputs.Trim() &&
      stored.CalculationDate == basis.CalculationDate && stored.Reason == basis.Reason.Trim();
  }

  /// <summary>
  /// Re-checked under the posting lock: the journal's basis exists, the treatment it was prepared under is still the
  /// firm's confirmed treatment, and the lines still agree with the entered amount and the confirmed accounts.
  /// </summary>
  private static async Task<CommandResult> EndOfServicePostingGateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid journalId, IReadOnlyList<FirmJournalLine> lines, CancellationToken ct)
  {
    var accrual = await db.FirmEndOfServiceAccruals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.JournalId == journalId, ct);
    if (accrual is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "This accrual has no recorded calculation basis and cannot be posted.");
    var treatment = await LatestEndOfServiceTreatmentAsync(db, firmId, ct);
    if (treatment is null || treatment.Status != EndOfServiceTreatmentStates.Confirmed)
      return CommandResult.Fail(ErrorCodes.GateBlocked, EndOfServiceUnconfirmed);
    if (treatment.Id != accrual.TreatmentId)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "The accounting treatment changed after this accrual was prepared; prepare a new accrual under the current treatment.");
    if (!UsesTreatmentAccounts(treatment, lines.Select(x => (x.FirmAccountId, x.Debit, x.Credit))) ||
        MoneyPolicy.Normalize(lines.Sum(x => x.Debit)) != accrual.Amount)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The accrual journal no longer agrees with its recorded basis.");
    return CommandResult.Ok();
  }
}
