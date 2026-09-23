using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record CreateFirmAccountRequest(
  string Code,
  string Name,
  string AccountType,
  string NormalSide,
  bool PostingAllowed = true);

public sealed record CreateFirmPeriodRequest(string PeriodCode);

public sealed record FirmJournalLineRequest(
  Guid FirmAccountId,
  string Description,
  decimal Debit,
  decimal Credit);

public sealed record CreateFirmJournalDraftRequest(
  Guid PeriodId,
  string JournalNumber,
  string SourceKind,
  string SourceKey,
  long SourceRevision,
  string PostingPurpose,
  string Currency,
  IReadOnlyList<FirmJournalLineRequest> Lines);

public sealed record ReverseFirmPostingRequest(Guid PostingId, Guid PeriodId, string Reason);

/// <summary>
/// One guarded transaction owns the firm ledger state transition. Posted rows are copied
/// into immutable posting tables; journals and source links remain idempotent.
/// </summary>
public static class LedgerService
{
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];
  private static readonly string[] ManagerRoles = ["FinanceManager"];
  private static readonly string[] ReviewerRoles = ["FinanceReviewer"];
  private static readonly string[] AccountTypes = [
    LedgerStates.AccountAsset, LedgerStates.AccountLiability, LedgerStates.AccountEquity,
    LedgerStates.AccountRevenue, LedgerStates.AccountExpense];

  public static async Task<CommandResult<Guid>> CreateFirmAccountAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateFirmAccountRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateAccount(request);
    if (validation is not null) return CommandResult<Guid>.Fail("ledger.invalid", validation);
    var auth = await AuthorizeFirmAsync(db, actor, ManagerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (await db.FirmAccounts.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail("ledger.duplicate", "Account code is already used.");
    var account = new FirmAccount
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = request.Code.Trim(),
      Name = request.Name.Trim(), AccountType = request.AccountType.Trim().ToUpperInvariant(),
      NormalSide = request.NormalSide.Trim().ToUpperInvariant(), PostingAllowed = request.PostingAllowed
    };
    db.FirmAccounts.Add(account);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(account.Id);
  }

  public static async Task<CommandResult<Guid>> CreateFirmPeriodAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateFirmPeriodRequest request,
    CancellationToken ct = default)
  {
    if (!ValidPeriodCode(request.PeriodCode))
      return CommandResult<Guid>.Fail("ledger.invalid", "Period code must be YYYY-MM.");
    var auth = await AuthorizeFirmAsync(db, actor, ManagerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var periodCode = request.PeriodCode.Trim();
    if (await db.FirmPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.PeriodCode == periodCode, ct))
      return CommandResult<Guid>.Fail("ledger.duplicate", "Period code is already used.");
    var period = new FirmPeriod { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodCode = periodCode };
    db.FirmPeriods.Add(period);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(period.Id);
  }

  public static async Task<CommandResult<Guid>> CreateFirmJournalDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateFirmJournalDraftRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateJournal(request);
    if (validation is not null) return CommandResult<Guid>.Fail("ledger.invalid", validation);
    var period = await db.FirmPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId, ct);
    if (period is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, FinanceRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var profile = await db.FirmFinanceProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Approved, ct);
    if (profile is null || !string.Equals(profile.FunctionalCurrency, request.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved finance profile in the journal currency is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var lockedPeriod = await LoadPeriodForUpdateAsync(db, actor.FirmId, request.PeriodId, ct);
    if (lockedPeriod is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedPeriod.Status != LedgerStates.PeriodOpen)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A closed period cannot receive a journal draft.");
    if (await db.FirmJournals.AnyAsync(x => x.FirmId == actor.FirmId &&
        (x.JournalNumber == request.JournalNumber.Trim() ||
         (x.SourceKind == request.SourceKind.Trim().ToUpperInvariant() && x.SourceKey == request.SourceKey.Trim() &&
          x.SourceRevision == request.SourceRevision && x.PostingPurpose == request.PostingPurpose.Trim())), ct))
      return CommandResult<Guid>.Fail("ledger.duplicate", "Journal or source identity is already used.");
    var accountIds = request.Lines.Select(x => x.FirmAccountId).Distinct().ToArray();
    var accounts = await db.FirmAccounts.Where(x => x.FirmId == actor.FirmId && accountIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, ct);
    if (accounts.Count != accountIds.Length)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "A journal account is outside the firm scope.");
    if (accounts.Values.Any(x => !x.PostingAllowed))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A journal uses an account that is not open for posting.");
    var journal = new FirmJournal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodId = lockedPeriod.Id,
      JournalNumber = request.JournalNumber.Trim(), SourceKind = request.SourceKind.Trim().ToUpperInvariant(),
      SourceKey = request.SourceKey.Trim(), SourceRevision = request.SourceRevision,
      PostingPurpose = request.PostingPurpose.Trim().ToUpperInvariant(), Currency = request.Currency.Trim().ToUpperInvariant(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.FirmJournals.Add(journal);
    foreach (var line in request.Lines)
      db.FirmJournalLines.Add(new FirmJournalLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, JournalId = journal.Id,
        FirmAccountId = line.FirmAccountId, Description = line.Description.Trim(),
        Debit = line.Debit, Credit = line.Credit
      });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(journal.Id);
  }

  public static Task<CommandResult> SubmitFirmJournalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default) =>
    TransitionJournalAsync(db, actor, journalId, LedgerStates.JournalDraft,
      LedgerStates.JournalReviewRequired, FinanceRoles, null, ct);

  public static Task<CommandResult> ApproveFirmJournalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default) =>
    TransitionJournalAsync(db, actor, journalId, LedgerStates.JournalReviewRequired,
      LedgerStates.JournalApproved, ReviewerRoles, actor.UserId, ct);

  public static async Task<CommandResult<Guid>> PostFirmJournalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default)
  {
    var journal = await db.FirmJournals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ManagerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var period = await LoadPeriodForUpdateAsync(db, actor.FirmId, journal.PeriodId, ct);
    var lockedJournal = await LoadJournalForUpdateAsync(db, actor.FirmId, journalId, ct);
    if (period is null || lockedJournal is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedJournal.Status == LedgerStates.JournalPosted)
    {
      var existing = await db.FirmPostings.SingleAsync(x => x.FirmId == actor.FirmId && x.JournalId == journalId, ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
    }
    if (period.Status != LedgerStates.PeriodOpen)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The fiscal period is closed.");
    if (lockedJournal.Status != LedgerStates.JournalApproved)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only an approved journal can be posted.");
    var profile = await db.FirmFinanceProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Approved, ct);
    if (profile is null || !string.Equals(profile.FunctionalCurrency, lockedJournal.Currency, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved finance profile in the journal currency is required.");
    var lines = await db.FirmJournalLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.JournalId == lockedJournal.Id).ToListAsync(ct);
    var balance = ValidatePostingLines(lines);
    if (balance is not null) return CommandResult<Guid>.Fail("ledger.unbalanced", balance);
    if (await db.LedgerSourceLinks.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.SourceKind == lockedJournal.SourceKind && x.SourceKey == lockedJournal.SourceKey &&
        x.SourceRevision == lockedJournal.SourceRevision && x.PostingPurpose == lockedJournal.PostingPurpose, ct))
      return CommandResult<Guid>.Fail("ledger.duplicate", "The source identity already has a posting.");
    var posting = new FirmPosting
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodId = period.Id, JournalId = lockedJournal.Id,
      Currency = lockedJournal.Currency, PostedByUserId = actor.UserId, PostedAt = DateTimeOffset.UtcNow
    };
    db.FirmPostings.Add(posting);
    foreach (var line in lines)
      db.FirmPostingLines.Add(new FirmPostingLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PostingId = posting.Id,
        FirmAccountId = line.FirmAccountId, Debit = line.Debit, Credit = line.Credit
      });
    db.LedgerSourceLinks.Add(new LedgerSourceLink
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PostingId = posting.Id,
      SourceKind = lockedJournal.SourceKind, SourceKey = lockedJournal.SourceKey,
      SourceRevision = lockedJournal.SourceRevision, PostingPurpose = lockedJournal.PostingPurpose,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.LedgerPostingReceipts.Add(new LedgerPostingReceipt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PostingId = posting.Id,
      RequestKey = SourceRequestKey(lockedJournal),
      RequestDigest = Hashing.Sha256Hex(SourceRequestKey(lockedJournal)), CreatedAt = DateTimeOffset.UtcNow
    });
    var journalUpdated = await db.FirmJournals
      .Where(x => x.Id == lockedJournal.Id && x.FirmId == actor.FirmId && x.Status == LedgerStates.JournalApproved)
      .ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.Status, LedgerStates.JournalPosted)
        .SetProperty(x => x.PostedAt, posting.PostedAt), ct);
    if (journalUpdated != 1)
      return CommandResult<Guid>.Fail("ledger.conflict", "The journal state changed; reload the journal.");
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail("ledger.conflict", "The posting identity changed; reload the journal.");
    }
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(posting.Id);
  }

  public static async Task<CommandResult<Guid>> ReverseFirmPostingAsync(
    IAuditSphereDbContext db, ActorContext actor, ReverseFirmPostingRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Reason))
      return CommandResult<Guid>.Fail("ledger.invalid", "A reversal reason is required.");
    var original = await db.FirmPostings.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PostingId && x.FirmId == actor.FirmId, ct);
    var period = await db.FirmPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId, ct);
    if (original is null || period is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var lockedPeriod = await LoadPeriodForUpdateAsync(db, actor.FirmId, period.Id, ct);
    if (lockedPeriod is null || lockedPeriod.Status != LedgerStates.PeriodOpen)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A reversal requires an open period.");
    var sourceKey = original.Id.ToString("D");
    var existing = await db.LedgerSourceLinks.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.SourceKind == "REVERSAL" && x.SourceKey == sourceKey && x.PostingPurpose == "REVERSAL", ct);
    if (existing is not null)
    {
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.PostingId);
    }
    var originalLines = await db.FirmPostingLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.PostingId == original.Id).ToListAsync(ct);
    var journal = new FirmJournal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodId = lockedPeriod.Id,
      JournalNumber = $"REV-{original.Id:N}"[..Math.Min(64, $"REV-{original.Id:N}".Length)],
      SourceKind = "REVERSAL", SourceKey = sourceKey, SourceRevision = 1, PostingPurpose = "REVERSAL",
      Currency = original.Currency, Status = LedgerStates.JournalPosted, CreatedByUserId = actor.UserId,
      ApprovedByUserId = actor.UserId, ApprovedAt = DateTimeOffset.UtcNow,
      PostedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    };
    db.FirmJournals.Add(journal);
    foreach (var line in originalLines)
      db.FirmJournalLines.Add(new FirmJournalLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, JournalId = journal.Id,
        FirmAccountId = line.FirmAccountId, Description = request.Reason.Trim(),
        Debit = line.Credit, Credit = line.Debit
      });
    var posting = new FirmPosting
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodId = lockedPeriod.Id,
      JournalId = journal.Id, Currency = original.Currency, PostedByUserId = actor.UserId,
      ReversalOfPostingId = original.Id, PostedAt = DateTimeOffset.UtcNow
    };
    db.FirmPostings.Add(posting);
    foreach (var line in originalLines)
      db.FirmPostingLines.Add(new FirmPostingLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PostingId = posting.Id,
        FirmAccountId = line.FirmAccountId, Debit = line.Credit, Credit = line.Debit
      });
    db.LedgerSourceLinks.Add(new LedgerSourceLink
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PostingId = posting.Id,
      SourceKind = "REVERSAL", SourceKey = sourceKey, SourceRevision = 1,
      PostingPurpose = "REVERSAL", CreatedAt = DateTimeOffset.UtcNow
    });
    db.LedgerPostingReceipts.Add(new LedgerPostingReceipt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PostingId = posting.Id,
      RequestKey = $"REVERSAL:{sourceKey}", RequestDigest = Hashing.Sha256Hex($"REVERSAL:{sourceKey}"),
      CreatedAt = DateTimeOffset.UtcNow
    });
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail("ledger.conflict", "The reversal identity changed; reload the posting.");
    }
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(posting.Id);
  }

  public static async Task<CommandResult> CloseFiscalPeriodAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid periodId, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason)) return CommandResult.Fail("ledger.invalid", "A close reason is required.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var period = await LoadPeriodForUpdateAsync(db, actor.FirmId, periodId, ct);
    if (period is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (period.Status == LedgerStates.PeriodClosed) return CommandResult.Ok();
    if (period.Status != LedgerStates.PeriodOpen)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an open period can be closed.");
    if (await db.FirmJournals.AnyAsync(x => x.FirmId == actor.FirmId && x.PeriodId == periodId &&
        x.Status != LedgerStates.JournalPosted, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "All journals must be posted before period close.");
    var closedAt = DateTimeOffset.UtcNow;
    var periodUpdated = await db.FirmPeriods
      .Where(x => x.Id == period.Id && x.FirmId == actor.FirmId && x.Status == LedgerStates.PeriodOpen)
      .ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.Status, LedgerStates.PeriodClosed)
        .SetProperty(x => x.Revision, x => x.Revision + 1)
        .SetProperty(x => x.ClosedAt, closedAt), ct);
    if (periodUpdated != 1)
      return CommandResult.Fail("ledger.conflict", "The fiscal period state changed; reload the period.");
    db.PeriodCloseDecisions.Add(new PeriodCloseDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodId = period.Id,
      DecisionKind = "CLOSE", Reason = reason.Trim(), DecidedByUserId = actor.UserId,
      DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> RequestPeriodReopenAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid periodId, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason)) return CommandResult.Fail("ledger.invalid", "A reopen reason is required.");
    var auth = await AuthorizeFirmAsync(db, actor, ManagerRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var period = await LoadPeriodForUpdateAsync(db, actor.FirmId, periodId, ct);
    if (period is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (period.Status == LedgerStates.PeriodOpen) return CommandResult.Ok();
    if (period.Status != LedgerStates.PeriodClosed)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a closed period can be reopened.");
    var periodUpdated = await db.FirmPeriods
      .Where(x => x.Id == period.Id && x.FirmId == actor.FirmId && x.Status == LedgerStates.PeriodClosed)
      .ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.Status, LedgerStates.PeriodOpen)
        .SetProperty(x => x.Revision, x => x.Revision + 1)
        .SetProperty(x => x.ClosedAt, (DateTimeOffset?)null), ct);
    if (periodUpdated != 1)
      return CommandResult.Fail("ledger.conflict", "The fiscal period state changed; reload the period.");
    db.PeriodCloseDecisions.Add(new PeriodCloseDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PeriodId = period.Id,
      DecisionKind = "REOPEN", Reason = reason.Trim(), DecidedByUserId = actor.UserId,
      DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> TransitionJournalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid journalId, string expected, string next,
    string[] roles, Guid? forbidActor, CancellationToken ct)
  {
    var journal = await db.FirmJournals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, roles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var period = await LoadPeriodForUpdateAsync(db, actor.FirmId, journal.PeriodId, ct);
    var locked = await LoadJournalForUpdateAsync(db, actor.FirmId, journalId, ct);
    if (period is null || locked is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (locked.Status == next) return CommandResult.Ok();
    if (period.Status != LedgerStates.PeriodOpen || locked.Status != expected)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The journal or period is not in the required state.");
    if (forbidActor.HasValue && locked.CreatedByUserId == forbidActor.Value)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Journal preparers cannot approve their own journal.");
    var changed = next == LedgerStates.JournalApproved
      ? await db.FirmJournals.Where(x => x.Id == locked.Id && x.FirmId == actor.FirmId && x.Status == expected)
        .ExecuteUpdateAsync(setters => setters
          .SetProperty(x => x.Status, next)
          .SetProperty(x => x.ApprovedByUserId, actor.UserId)
          .SetProperty(x => x.ApprovedAt, DateTimeOffset.UtcNow), ct)
      : await db.FirmJournals.Where(x => x.Id == locked.Id && x.FirmId == actor.FirmId && x.Status == expected)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, next), ct);
    if (changed != 1)
      return CommandResult.Fail("ledger.conflict", "The journal state changed; reload the journal.");
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> AuthorizeFirmAsync(
    IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static string? ValidateAccount(CreateFirmAccountRequest request) =>
    string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 32
      ? "Account code is required and bounded."
      : string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200
        ? "Account name is required and bounded."
      : string.IsNullOrWhiteSpace(request.AccountType) || !AccountTypes.Contains(request.AccountType.Trim().ToUpperInvariant())
          ? "Account type is invalid."
          : string.IsNullOrWhiteSpace(request.NormalSide) || request.NormalSide.Trim().ToUpperInvariant() is not (LedgerStates.Debit or LedgerStates.Credit)
            ? "Normal side is invalid." : null;

  private static string? ValidateJournal(CreateFirmJournalDraftRequest request)
  {
    if (request.Lines is null || request.Lines.Count is < 2 or > 200) return "A journal needs 2 to 200 lines.";
    if (string.IsNullOrWhiteSpace(request.JournalNumber) || request.JournalNumber.Trim().Length > 64 ||
        string.IsNullOrWhiteSpace(request.SourceKind) || request.SourceKind.Trim().Length > 64 ||
        string.IsNullOrWhiteSpace(request.SourceKey) || request.SourceKey.Trim().Length > 200 ||
        string.IsNullOrWhiteSpace(request.PostingPurpose) || request.PostingPurpose.Trim().Length > 64 ||
        request.SourceRevision < 1) return "Journal source identity is invalid.";
    var currencyError = CurrencyError(request.Currency);
    if (currencyError is not null) return currencyError;
    foreach (var line in request.Lines)
    {
      if (line.FirmAccountId == Guid.Empty || string.IsNullOrWhiteSpace(line.Description) ||
          line.Description.Trim().Length > 300 || line.Debit < 0 || line.Credit < 0 ||
          MoneyPolicy.Normalize(line.Debit) != line.Debit || MoneyPolicy.Normalize(line.Credit) != line.Credit ||
          (line.Debit == 0 && line.Credit == 0) || (line.Debit > 0 && line.Credit > 0))
        return "Journal lines must have one exact non-zero debit or credit value.";
    }
    return null;
  }

  private static string? ValidatePostingLines(IReadOnlyList<FirmJournalLine> lines)
  {
    if (lines.Count < 2) return "A posting needs at least two lines.";
    var debit = MoneyPolicy.Normalize(lines.Sum(x => x.Debit));
    var credit = MoneyPolicy.Normalize(lines.Sum(x => x.Credit));
    return debit == credit ? null : "Debit and credit totals must balance exactly.";
  }

  private static string? CurrencyError(string currency) =>
    !string.IsNullOrWhiteSpace(currency) && currency.Trim().Length == 3 && currency.All(char.IsLetter)
      ? null : "Currency must be a three-letter code.";

  private static bool ValidPeriodCode(string value) =>
    !string.IsNullOrWhiteSpace(value) && DateTime.TryParseExact(value.Trim() + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
      DateTimeStyles.None, out _);

  private static string SourceRequestKey(FirmJournal journal) =>
    $"{journal.SourceKind}:{journal.SourceKey}:{journal.SourceRevision}:{journal.PostingPurpose}";

  private static Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").AsTracking().SingleOrDefaultAsync(ct);

  private static async Task<FirmPeriod?> LoadPeriodForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid periodId, CancellationToken ct)
  {
    var locked = await db.FirmPeriods.FromSqlInterpolated(
      $"SELECT * FROM firm_periods WHERE id = {periodId} AND firm_id = {firmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    return locked;
  }

  private static async Task<FirmJournal?> LoadJournalForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid journalId, CancellationToken ct)
  {
    var locked = await db.FirmJournals.FromSqlInterpolated(
      $"SELECT * FROM firm_journals WHERE id = {journalId} AND firm_id = {firmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    return locked;
  }
}
