using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record BankReconciliationInspectionRow(Guid Id, Guid? ProcedureId, DateOnly AsOfDate, string Currency,
  string Status, string LedgerBalance, string StatementBalance, string TimingItemTotal, string ProposedCorrectionTotal,
  string Residual, string InputGeneration, string GenerationState, string? Conclusion, Guid CreatedByUserId,
  DateTimeOffset CreatedAt, Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt);
public sealed record BankReconciliationInspectionPage(Guid EngagementId, string? CurrentInputGeneration,
  int Offset, int Limit, int Total, IReadOnlyList<BankReconciliationInspectionRow> Rows);
public sealed record BankReconciliationScheduleInspection(Guid Id, string ScheduleType, string EntityIdentifier,
  string SourceReceiptReference, DateTimeOffset? AsOfDate, DateOnly? PeriodStart, DateOnly? PeriodEnd,
  string Currency, string SignConvention, string SourceHash, Guid? SourceImportBatchId,
  Guid? AcceptedSourceDecisionId, string AcceptedSourceHash, int RowCount, string SignedControlTotal,
  string GlControlTotal, string Residual, string InputGeneration, string GenerationState, string Status);
public sealed record BankReconciliationItemInspection(Guid Id, string StableItemId, string ItemType,
  string SignedAmount, string Currency, string Description, string SourceReference, string EvidenceReference,
  Guid? SourceScheduleId, Guid? ProposedJournalId);
public sealed record BankReconciliationInspectionDetail(Guid EngagementId, string? CurrentInputGeneration,
  BankReconciliationInspectionRow Reconciliation, Guid LedgerScheduleId, Guid StatementScheduleId,
  BankReconciliationScheduleInspection? LedgerSchedule, BankReconciliationScheduleInspection? StatementSchedule,
  int Offset, int Limit, int Total, IReadOnlyList<BankReconciliationItemInspection> Items);

/// <summary>Stored evidence inspection only. Generation equality is not a source acceptance or review decision.</summary>
public static class BankReconciliationInspectionQuery
{
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<BankReconciliationInspectionPage>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, int offset = 0, int limit = 25,
    CancellationToken ct = default)
  {
    if (!ValidPage(offset, limit, 25)) return Invalid<BankReconciliationInspectionPage>();
    var scope = await AuthorizeAsync(db, actor, engagementId, ct);
    if (!scope.Succeeded) return CommandResult<BankReconciliationInspectionPage>.Fail(scope.ErrorCode!, scope.Message!);
    if (db.Database.CurrentTransaction is not null) return SnapshotRequired<BankReconciliationInspectionPage>();
    int total;
    List<AuditBankReconciliation> records;
    long? generation;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var query = db.AuditBankReconciliations.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == scope.Value && x.EngagementId == engagementId);
      total = await query.CountAsync(ct);
      records = await query.OrderByDescending(x => x.AsOfDate).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
      generation = await GenerationAsync(db, actor.FirmId, scope.Value, ct);
      await snapshot.CommitAsync(ct);
    }
    // Reauthorize against fresh committed state, outside the evidence snapshot.
    var final = await AuthorizeAsync(db, actor, engagementId, ct);
    if (!final.Succeeded) return CommandResult<BankReconciliationInspectionPage>.Fail(final.ErrorCode!, final.Message!);
    if (final.Value != scope.Value) return Denied<BankReconciliationInspectionPage>();
    return CommandResult<BankReconciliationInspectionPage>.Ok(new(engagementId, Exact(generation), offset, limit,
      total, records.Select(x => Row(x, generation)).ToArray()));
  }

  public static async Task<CommandResult<BankReconciliationInspectionDetail>> DetailAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, Guid reconciliationId,
    int offset = 0, int limit = 100, CancellationToken ct = default)
  {
    if (!ValidPage(offset, limit, 100)) return Invalid<BankReconciliationInspectionDetail>();
    var scope = await AuthorizeAsync(db, actor, engagementId, ct);
    if (!scope.Succeeded) return CommandResult<BankReconciliationInspectionDetail>.Fail(scope.ErrorCode!, scope.Message!);
    if (db.Database.CurrentTransaction is not null) return SnapshotRequired<BankReconciliationInspectionDetail>();
    AuditBankReconciliation record;
    List<AuditSchedule> schedules;
    List<AuditBankReconciliationItem> items;
    int total;
    long? generation;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var found = await db.AuditBankReconciliations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == scope.Value && x.EngagementId == engagementId && x.Id == reconciliationId, ct);
      if (found is null) return Denied<BankReconciliationInspectionDetail>();
      record = found;
      schedules = await db.AuditSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == scope.Value && x.EngagementId == engagementId &&
        (x.Id == record.LedgerScheduleId || x.Id == record.StatementScheduleId)).ToListAsync(ct);
      var query = db.AuditBankReconciliationItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ClientId == scope.Value && x.EngagementId == engagementId && x.BankReconciliationId == record.Id);
      total = await query.CountAsync(ct);
      items = await query.OrderBy(x => x.StableItemId).ThenBy(x => x.Id).Skip(offset).Take(limit).ToListAsync(ct);
      generation = await GenerationAsync(db, actor.FirmId, scope.Value, ct);
      await snapshot.CommitAsync(ct);
    }
    // Reauthorize against fresh committed state, outside the evidence snapshot.
    var final = await AuthorizeAsync(db, actor, engagementId, ct);
    if (!final.Succeeded) return CommandResult<BankReconciliationInspectionDetail>.Fail(final.ErrorCode!, final.Message!);
    if (final.Value != scope.Value) return Denied<BankReconciliationInspectionDetail>();
    return CommandResult<BankReconciliationInspectionDetail>.Ok(new(engagementId, Exact(generation), Row(record, generation),
      record.LedgerScheduleId, record.StatementScheduleId,
      Schedule(schedules.SingleOrDefault(x => x.Id == record.LedgerScheduleId), generation),
      Schedule(schedules.SingleOrDefault(x => x.Id == record.StatementScheduleId), generation), offset, limit, total,
      items.Select(x => new BankReconciliationItemInspection(x.Id, x.StableItemId, x.ItemType, Exact(x.SignedAmount),
        x.Currency, x.Description, x.SourceReference, x.EvidenceReference, x.SourceScheduleId, x.ProposedJournalId)).ToArray()));
  }

  private static async Task<CommandResult<Guid>> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Denied<Guid>();
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId,
      engagement.PracticeClientId, engagement.Id, ReviewRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    return auth.Succeeded ? CommandResult<Guid>.Ok(engagement.PracticeClientId)
      : CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
  }

  private static Task<long?> GenerationAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == clientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
  private static string State(long stored, long? current) => current is null ? "UNAVAILABLE" : stored == current ? "CURRENT_GENERATION" : "STALE_GENERATION";
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  private static string? Exact(long? value) => value?.ToString(CultureInfo.InvariantCulture);
  private static bool ValidPage(int offset, int limit, int max) => offset >= 0 && offset <= int.MaxValue - max && limit >= 1 && limit <= max;
  private static CommandResult<T> SnapshotRequired<T>() => CommandResult<T>.Fail(ErrorCodes.ProtectedState,
    "Inspection requires an owned read snapshot followed by a fresh authority check.");
  private static CommandResult<T> Invalid<T>() => CommandResult<T>.Fail(ErrorCodes.AuditPlanning.Invalid, "Choose a bounded nonnegative page.");
  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static BankReconciliationInspectionRow Row(AuditBankReconciliation x, long? generation) => new(x.Id, x.ProcedureId,
    x.AsOfDate, x.Currency, x.Status, Exact(x.LedgerBalance), Exact(x.StatementBalance), Exact(x.TimingItemTotal),
    Exact(x.ProposedCorrectionTotal), Exact(x.Residual), x.InputGeneration.ToString(CultureInfo.InvariantCulture), State(x.InputGeneration, generation),
    x.Conclusion, x.CreatedByUserId, x.CreatedAt, x.ReviewedByUserId, x.ReviewedAt);
  private static BankReconciliationScheduleInspection? Schedule(AuditSchedule? x, long? generation) => x is null ? null : new(x.Id,
    x.ScheduleType, x.EntityIdentifier, x.SourceReceiptReference, x.AsOfDate, x.PeriodStart, x.PeriodEnd, x.Currency, x.SignConvention,
    x.SourceHash, x.SourceImportBatchId, x.AcceptedSourceDecisionId, x.AcceptedSourceHash, x.RowCount, Exact(x.SignedControlTotal),
    Exact(x.GlControlTotal), Exact(x.Residual), x.InputGeneration.ToString(CultureInfo.InvariantCulture), State(x.InputGeneration, generation), x.Status);
}
