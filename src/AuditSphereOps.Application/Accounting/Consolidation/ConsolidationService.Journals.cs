using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  public static async Task<CommandResult<Guid>> CreateConsolidationJournalAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationJournalRequest request,
    CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || string.IsNullOrWhiteSpace(request.JournalNumber) ||
        string.IsNullOrWhiteSpace(request.JournalType) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        request.Lines.Count < 2)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A consolidation journal needs a number, purpose, evidence and balanced lines.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved consolidation perimeter is required before a group-only journal.");
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (currency != scope.ReportingCurrency || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.TaxonomyCode) ||
        string.IsNullOrWhiteSpace(x.Description) || x.Debit < 0m || x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m) ||
        x.Debit != MoneyPolicy.Normalize(x.Debit) || x.Credit != MoneyPolicy.Normalize(x.Credit)))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Group-only journal lines must use the approved scope currency and accounting precision.");
    if (MoneyPolicy.Normalize(request.Lines.Sum(x => x.Debit)) != MoneyPolicy.Normalize(request.Lines.Sum(x => x.Credit)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "The consolidation journal must balance.");
    if (await db.ConsolidationJournals.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        x.JournalNumber == request.JournalNumber.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The consolidation journal number already exists in this perimeter.");
    var matchIds = request.Lines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).Distinct().ToArray();
    if (matchIds.Length > 0 && await db.IntercompanyMatches.CountAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        matchIds.Contains(x.Id) && x.Status == AccountingWorkflowStates.Approved && !x.OutsidePerimeterReview, ct) != matchIds.Length)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Every linked intercompany match must be approved in the same perimeter.");
    var journal = new ConsolidationJournal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      JournalNumber = request.JournalNumber.Trim(), JournalType = request.JournalType.Trim().ToUpperInvariant(), Currency = currency,
      TotalDebits = MoneyPolicy.Normalize(request.Lines.Sum(x => x.Debit)), TotalCreditsAbs = MoneyPolicy.Normalize(request.Lines.Sum(x => x.Credit)),
      EvidenceReference = request.EvidenceReference.Trim(), Status = AccountingWorkflowStates.Submitted,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationJournals.Add(journal);
    db.ConsolidationJournalLines.AddRange(request.Lines.Select(x => new ConsolidationJournalLine
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ConsolidationJournalId = journal.Id, IntercompanyMatchId = x.IntercompanyMatchId,
      TaxonomyCode = x.TaxonomyCode.Trim(), Debit = MoneyPolicy.Normalize(x.Debit), Credit = MoneyPolicy.Normalize(x.Credit),
      Currency = currency, Description = x.Description.Trim(), CreatedAt = journal.CreatedAt
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(journal.Id);
  }

  public static async Task<CommandResult> ApproveConsolidationJournalAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default)
  {
    var initial = await db.ConsolidationJournals.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (initial is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, initial.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var initialScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == initial.ScopeVersionId && x.GroupId == initial.GroupId, ct);
    if (initialScope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var savepoint = "consolidation_journal_approval_" + Guid.NewGuid().ToString("N");
    if (tx is null)
      await db.Database.CurrentTransaction!.CreateSavepointAsync(savepoint, ct);

    if (!await LockGroupAuthorityFenceAsync(db, actor.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The firm safety state is unavailable.");
    var group = await db.ClientGroups.FromSqlInterpolated($"""
      SELECT * FROM client_groups WHERE id = {initial.GroupId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    var journalScope = await db.ConsolidationScopeVersions.FromSqlInterpolated($"""
      SELECT * FROM consolidation_scope_versions
      WHERE id = {initial.ScopeVersionId} AND group_id = {initial.GroupId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (group is null || journalScope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await GroupAuthAsync(db, actor, group.Id, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (group.Revision != journalScope.GroupRevision)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (journalScope.Status != AccountingWorkflowStates.Approved)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The consolidation perimeter is no longer approved; create a new scope version.");
    if (journalScope.ApprovedByUserId is null || journalScope.ApprovedAt is null ||
        journalScope.ApprovedByUserId == journalScope.CreatedByUserId)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation perimeter approval evidence is inconsistent.");

    var freshJournal = await db.ConsolidationJournals.FromSqlInterpolated($"""
      SELECT * FROM consolidation_journals
      WHERE id = {journalId} AND firm_id = {actor.FirmId} AND group_id = {group.Id}
        AND scope_version_id = {journalScope.Id} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (freshJournal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var journal = TrackFreshJournal(db, freshJournal);
    if (journal.Status != AccountingWorkflowStates.Submitted || journal.CreatedByUserId == actor.UserId ||
        journal.ApprovedByUserId is not null || journal.ApprovedAt is not null)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Only a separate reviewer can approve a submitted consolidation journal.");
    var lines = await db.ConsolidationJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == group.Id && x.ScopeVersionId == journalScope.Id && x.ConsolidationJournalId == journal.Id).ToListAsync(ct);
    if (lines.Count < 2 || MoneyPolicy.Normalize(lines.Sum(x => x.Debit)) != journal.TotalDebits ||
        MoneyPolicy.Normalize(lines.Sum(x => x.Credit)) != journal.TotalCreditsAbs || journal.TotalDebits != journal.TotalCreditsAbs ||
        lines.Any(x => x.Currency != journalScope.ReportingCurrency || x.Currency != journal.Currency || x.Debit < 0m || x.Credit < 0m ||
          x.Debit > 0m && x.Credit > 0m || x.Debit != MoneyPolicy.Normalize(x.Debit) || x.Credit != MoneyPolicy.Normalize(x.Credit)))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation journal is no longer balanced.");
    var matchIds = lines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).Distinct().ToArray();
    var linkedMatches = matchIds.Length == 0
      ? new List<IntercompanyMatch>()
      : await db.IntercompanyMatches.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.GroupId == group.Id && x.ScopeVersionId == journalScope.Id && matchIds.Contains(x.Id)).ToListAsync(ct);
    if (linkedMatches.Count != matchIds.Length || linkedMatches.Any(x => x.Status != AccountingWorkflowStates.Approved ||
        x.OutsidePerimeterReview || x.Currency != journalScope.ReportingCurrency ||
        x.ReviewedByUserId is null || x.ReviewedAt is null || x.ReviewedByUserId == x.CreatedByUserId))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "A linked intercompany match changed; rebuild the group journal.");

    var existingReservations = matchIds.Length == 0
      ? new List<ConsolidationMatchReservation>()
      : await db.ConsolidationMatchReservations.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.GroupId == group.Id && x.ScopeVersionId == journalScope.Id &&
        matchIds.Contains(x.IntercompanyMatchId)).ToListAsync(ct);
    if (existingReservations.Count > 0)
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict,
        "Another approved consolidation journal already consumes one of the linked intercompany matches.");

    var createdAt = DateTimeOffset.UtcNow;
    var reservations = matchIds.Select(matchId => new ConsolidationMatchReservation
    {
      FirmId = actor.FirmId, GroupId = group.Id, ScopeVersionId = journalScope.Id,
      IntercompanyMatchId = matchId, ConsolidationJournalId = journal.Id
    }).ToArray();
    db.ConsolidationMatchReservations.AddRange(reservations);
    journal.Status = AccountingWorkflowStates.Approved;
    journal.ApprovedByUserId = actor.UserId;
    journal.ApprovedAt = createdAt;
    try
    {
      await db.SaveChangesAsync(ct);
      if (tx is not null)
        await tx.CommitAsync(ct);
    }
    catch (DbUpdateException ex) when (IsMatchReservationUniqueViolation(ex))
    {
      if (tx is not null)
        await tx.RollbackAsync(CancellationToken.None);
      else
        await db.Database.CurrentTransaction!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
      db.ConsolidationMatchReservations.RemoveRange(reservations);
      journal.Status = AccountingWorkflowStates.Submitted;
      journal.ApprovedByUserId = null;
      journal.ApprovedAt = null;
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict,
        "Another approved consolidation journal already consumes one of the linked intercompany matches.");
    }
    catch
    {
      if (tx is not null)
        await tx.RollbackAsync(CancellationToken.None);
      else
        await db.Database.CurrentTransaction!.RollbackToSavepointAsync(savepoint, CancellationToken.None);
      throw;
    }
    return CommandResult.Ok();
  }

  private static bool IsMatchReservationUniqueViolation(DbUpdateException exception)
  {
    var cause = exception.GetBaseException();
    var causeType = cause.GetType();
    return cause is DbException && causeType.FullName == "Npgsql.PostgresException" &&
      causeType.GetProperty("SqlState")?.GetValue(cause) is "23505" &&
      causeType.GetProperty("ConstraintName")?.GetValue(cause) is "pk_consolidation_match_reservations";
  }

  private static ConsolidationJournal TrackFreshJournal(
    IClientAccountingDbContext db, ConsolidationJournal fresh)
  {
    var tracked = db.ConsolidationJournals.Local.SingleOrDefault(x => x.Id == fresh.Id);
    if (tracked is not null)
      db.ConsolidationJournals.Local.Remove(tracked);
    db.ConsolidationJournals.Attach(fresh);
    return fresh;
  }

  /// <summary>Reviewer return: a submitted group journal goes back to the preparer with a
  /// mandatory reason. The journal stays out of any run until it is resubmitted and
  /// independently approved.</summary>
  public static async Task<CommandResult> ReturnConsolidationJournalAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid journalId, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A return requires a reason of at most 2000 characters.");
    var journal = await db.ConsolidationJournals.SingleOrDefaultAsync(x => x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, journal.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (journal.Status != AccountingWorkflowStates.Submitted)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted consolidation journal can be returned.");
    if (journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied,
        "Separation of duties: the preparer cannot review their own group journal.");

    journal.Status = "Returned";
    journal.ReturnReason = reason.Trim();
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Preparer resubmission of a returned group journal; the reason is cleared and
  /// the journal re-enters the independent review queue.</summary>
  public static async Task<CommandResult> ResubmitConsolidationJournalAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid journalId,
    CancellationToken ct = default)
  {
    var journal = await db.ConsolidationJournals.SingleOrDefaultAsync(x => x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, journal.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (journal.Status != "Returned")
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a returned consolidation journal can be resubmitted.");

    journal.Status = AccountingWorkflowStates.Submitted;
    journal.ReturnReason = null;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
