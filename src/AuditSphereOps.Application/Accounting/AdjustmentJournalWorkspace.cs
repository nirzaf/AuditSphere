using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record JournalEditLine(string AccountCode, string Debit, string Credit);
public sealed record JournalActionRequest(Guid RequestId, string Action, string ReviewBasis,
  string Reason, string EvidenceReference, string ReversalNumber, IReadOnlyList<JournalEditLine> Lines, bool Reviewed);
public sealed record JournalActionReceipt(Guid Id, Guid RequestId, string RequestHash, Guid JournalId,
  Guid ResultJournalId, string Action, long OldRevision, long NewRevision, string OldStatus, string NewStatus,
  Guid ActorId, string Reason, string EvidenceReference, DateTimeOffset CreatedAt);
public sealed record JournalReceiptLookup(bool Found, JournalActionReceipt? Receipt);
public sealed record JournalRevisionSnapshot(Guid JournalId, long Revision, string Status,
  string Reason, string EvidenceReference, IReadOnlyList<JournalLineView> Lines);
public sealed record JournalHistoricalRevision(JournalActionReceipt Receipt, JournalRevisionSnapshot Before, JournalRevisionSnapshot After,
  JournalManagementDecisionView? Management = null);
public sealed record JournalReflectionView(
  string State, string Evidence, Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt, bool IsExactRevision);
public sealed record JournalReviewView(Guid JournalId, Guid ClientId, Guid EngagementId, string JournalNumber,
  string Status, long Revision, string ReviewBasis, Guid DatasetId, long DatasetRevision, string DatasetDigest,
  Guid? PeriodId, string PeriodStart, string PeriodEnd, Guid? BookId, string Currency, string Purpose, string Origin,
  string Reason, string EvidenceReference, string? ReturnReason, Guid PreparerId, Guid? SupersedesId, Guid? ReversalOfId,
  IReadOnlyList<JournalLineView> Lines, decimal TotalDebit, decimal TotalCredit, string? Blocker,
  bool CanEdit, bool CanSubmit, bool CanReturn, bool CanPost, bool CanReverse,
  int HistoryCount, int HistoryPage, IReadOnlyList<JournalActionReceipt> History,
  JournalManagementDecisionView? ManagementDecision = null,
  JournalReflectionView? SourceReflection = null,
  bool CanReconcileReflection = false);

/// <summary>Native journal commands compose the existing treatment service in one owned,
/// serialized transaction with exact review fencing, final authority and immutable receipts.
/// Posting is technical review, never external client-book posting or package application.</summary>
public static partial class AdjustmentJournalWorkspace
{
  public const int MaximumLines = 500;
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "Staff", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] PrepareRoles = ["Partner", "Manager", "Staff", "AccountingPreparer"];
  private static readonly string[] ReviewRoles = ["Partner", "Manager", "AccountingReviewer"];
  private static Task<CommandResult> Auth(IAuditSphereDbContext db, ActorContext a, AdjustmentJournal j, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, j.ClientId, j.EngagementId, roles, InternalOnly: true, RequireProfessionalWork: true), ct);
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private sealed record Snapshot(JournalReviewView View, AdjustmentJournal Journal, string Json);
  private static JournalActionReceipt Receipt(AdjustmentJournalAction e) => new(e.Id, e.RequestId, e.RequestHash, e.JournalId,
    e.ResultJournalId, e.Action, e.OldRevision, e.NewRevision, e.OldStatus, e.NewStatus, e.ActorId, e.Reason, e.EvidenceReference, e.CreatedAt);
  private static string SnapshotJson(AdjustmentJournal j, IReadOnlyList<JournalLineView> lines) =>
    JsonSerializer.Serialize(new JournalRevisionSnapshot(j.Id, j.Revision, j.Status, j.Reason, j.EvidenceReference, lines));

  private static async Task<CommandResult<Snapshot>> ReadAsync(IClientAccountingDbContext db, IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor, Guid id, int historyPage, CancellationToken ct)
  {
    var j = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (j is null) return Fail<Snapshot>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await Auth(db, actor, j, ReadRoles, ct);
    if (!auth.Succeeded) return Fail<Snapshot>(auth.ErrorCode!, auth.Message!);
    var d = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == j.BaseDatasetId &&
      x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.EngagementId == j.EngagementId, ct);
    if (d is null) return Fail<Snapshot>(ErrorCodes.ScopeDenied, "Access denied.");
    var lines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == id).OrderBy(x => x.AccountCode).ThenBy(x => x.Id)
      .Take(MaximumLines + 1).Select(x => new JournalLineView(x.AccountCode, x.Debit, x.Credit)).ToListAsync(ct);
    if (lines.Count > MaximumLines) return Fail<Snapshot>(ErrorCodes.GateBlocked, "This journal exceeds the interactive line limit.");
    var decisions = await evidenceDb.AdjustmentJournalManagementDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.JournalId == id && x.ClientId == j.ClientId && x.EngagementId == j.EngagementId).OrderBy(x => x.JournalRevision).Take(1001).ToListAsync(ct);
    var reconciliations = await db.JournalSourceReconciliations.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.EngagementId == j.EngagementId &&
      x.BaseDatasetId == j.BaseDatasetId && x.LogicalJournalNumber == j.JournalNumber).ToListAsync(ct);
    var historyQuery = evidenceDb.AdjustmentJournalActions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == j.ClientId &&
      x.EngagementId == j.EngagementId && (x.JournalId == id || x.ResultJournalId == id));
    var history = await historyQuery.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(1001).ToListAsync(ct);
    if (history.Count > 1000 || decisions.Count > 1000) return Fail<Snapshot>(ErrorCodes.GateBlocked, "This history exceeds the interactive review limit.");
    if (historyPage < 1 || historyPage > Math.Max(1, (history.Count + 24) / 25)) return Fail<Snapshot>(ErrorCodes.Accounting.JournalRejected, "Choose a valid history page.");
    var period = j.PeriodId is { } p ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.Id == p, ct) : null;
    var book = j.BookId is { } b ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.PeriodId == j.PeriodId && x.Id == b, ct) : null;
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == j.ClientId, ct);
    var frozen = await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == j.EngagementId && x.State == "FROZEN", ct);
    var hasReversal = await db.AdjustmentJournals.AnyAsync(x => x.FirmId == actor.FirmId && x.ReversalOfJournalId == id && x.Status != "Void", ct);
    var prepared = (await Auth(db, actor, j, PrepareRoles, ct)).Succeeded;
    var reviewed = (await Auth(db, actor, j, ReviewRoles, ct)).Succeeded && j.CreatedByUserId != actor.UserId;
    var blocker = firm is null || safety is null ? "Safety state is unavailable." :
      d.SourceKind != "Raw" || d.ImportState != TrialBalanceImportStates.Sealed || d.ValidationStatus != "Accepted" || !d.Balanced || d.ControlTotal != 0m || !SourceAcceptanceWorkspace.ValidHash(MappedTrialBalanceSource.Digest(d))
        ? "The exact raw source must be sealed, balanced and validated." :
      period is null || j.PeriodId != d.PeriodId || j.Currency != d.Currency || j.Basis != d.Basis ||
        period.Currency != d.Currency || !string.Equals(period.Basis, d.Basis, StringComparison.OrdinalIgnoreCase)
        ? "The exact source period, basis or currency is unbound or inconsistent." :
      period.Status is not ("ACTIVE" or "DRAFT") || j.BookId is not null &&
        (book is null || book.Status is not ("ACTIVE" or "DRAFT") || book.Currency != d.Currency || !string.Equals(book.Basis, d.Basis, StringComparison.OrdinalIgnoreCase))
        ? "The reporting period or book is closed or unavailable." :
      frozen ? "The engagement file is frozen. An approved amendment is required." : null;
    var hasManagement = decisions.Any(x => x.JournalRevision == j.Revision);
    var managementAccepted = decisions.Any(x => x.JournalRevision == j.Revision && x.Decision is "ACCEPTED" or "PARTIAL");
    var currentDecisionRecord = decisions.SingleOrDefault(x => x.JournalRevision == j.Revision);
    var currentDecision = currentDecisionRecord is null ? null : new JournalManagementDecisionView(
      currentDecisionRecord.Id, currentDecisionRecord.JournalRevision, currentDecisionRecord.Decision,
      currentDecisionRecord.EvidenceMode, currentDecisionRecord.EvidenceReference,
      currentDecisionRecord.DecidedByUserId, currentDecisionRecord.DecidedAt);
    var currentReconciliation = reconciliations.FirstOrDefault();
    var currentReflection = currentReconciliation is null ? null : new JournalReflectionView(
      currentReconciliation.State, currentReconciliation.Evidence, currentReconciliation.ReviewedByUserId,
      currentReconciliation.ReviewedAt, currentReconciliation.JournalRevision == j.Revision);
    var editable = blocker is null && prepared && j.Status is "Draft" or "Returned" && !hasManagement;
    var canSubmit = blocker is null && prepared && j.Status is "Draft" or "Returned";
    var canReturn = blocker is null && reviewed && j.Status == "Submitted";
    var canPost = blocker is null && reviewed && j.Status == "Submitted" &&
      j.Purpose != AdjustmentJournalPurposes.GroupOnlyElimination && (j.Purpose != AdjustmentJournalPurposes.ClientBookCorrection || managementAccepted);
    var canReconcileReflection = blocker is null && reviewed && j.Status == "Posted";
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, j, d, lines, decisions, reconciliations, history, period, book, firm, safety, frozen, hasReversal }));
    auth = await Auth(db, actor, j, ReadRoles, ct);
    if (!auth.Succeeded) return Fail<Snapshot>(auth.ErrorCode!, auth.Message!);
    var view = new JournalReviewView(id, j.ClientId, j.EngagementId, j.JournalNumber, j.Status, j.Revision, basis, d.Id, d.Revision,
      MappedTrialBalanceSource.Digest(d), j.PeriodId, period?.StartDate.ToString("yyyy-MM-dd") ?? "", period?.EndDate.ToString("yyyy-MM-dd") ?? "",
      j.BookId, d.Currency, j.Purpose, j.Origin, j.Reason, j.EvidenceReference, j.ReturnReason, j.CreatedByUserId, j.SupersedesJournalId, j.ReversalOfJournalId,
      lines, lines.Sum(x => x.Debit), lines.Sum(x => x.Credit), blocker, editable, canSubmit, canReturn, canPost,
      blocker is null && prepared && j.Status == "Posted" && !hasReversal, history.Count, historyPage,
      history.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip((historyPage - 1) * 25).Take(25).Select(Receipt).ToArray(),
      currentDecision, currentReflection, canReconcileReflection);
    return CommandResult<Snapshot>.Ok(new(view, j, SnapshotJson(j, lines)));
  }

  public static async Task<CommandResult<JournalReviewView>> GetAsync(IClientAccountingDbContext db, IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor, Guid id, int historyPage = 1, CancellationToken ct = default)
  {
    var first = await ReadAsync(db, evidenceDb, actor, id, historyPage, ct);
    if (!first.Succeeded) return Fail<JournalReviewView>(first.ErrorCode!, first.Message!);
    var final = await ReadAsync(db, evidenceDb, actor, id, historyPage, ct);
    if (!final.Succeeded) return Fail<JournalReviewView>(final.ErrorCode!, final.Message!);
    return first.Value!.View.ReviewBasis == final.Value!.View.ReviewBasis ? CommandResult<JournalReviewView>.Ok(final.Value.View) :
      Fail<JournalReviewView>(ErrorCodes.StaleRevision, "The journal review changed. Refresh before continuing.");
  }

  public static async Task<CommandResult<JournalReceiptLookup>> LookupAsync(IClientAccountingDbContext db, IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor, Guid id, Guid requestId, string requestHash, CancellationToken ct = default)
  {
    var view = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    if (!view.Succeeded) return Fail<JournalReceiptLookup>(view.ErrorCode!, view.Message!);
    if (requestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(requestHash)) return Fail<JournalReceiptLookup>(ErrorCodes.Accounting.JournalRejected, "Use the exact pending request identity.");
    var e = await evidenceDb.AdjustmentJournalActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (e is not null && (e.JournalId != id || e.RequestHash != requestHash)) return Fail<JournalReceiptLookup>(ErrorCodes.IdempotencyConflict, "The request belongs to a different intent.");
    var final = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    return final.Succeeded ? CommandResult<JournalReceiptLookup>.Ok(new(e is not null, e is null ? null : Receipt(e))) :
      Fail<JournalReceiptLookup>(final.ErrorCode!, final.Message!);
  }

  public static async Task<CommandResult<JournalHistoricalRevision>> HistoryAsync(IClientAccountingDbContext db, IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor, Guid id, Guid eventId, CancellationToken ct = default)
  {
    var view = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    if (!view.Succeeded) return Fail<JournalHistoricalRevision>(view.ErrorCode!, view.Message!);
    var e = await evidenceDb.AdjustmentJournalActions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == eventId && x.FirmId == actor.FirmId &&
      x.ClientId == view.Value!.ClientId && x.EngagementId == view.Value.EngagementId && (x.JournalId == id || x.ResultJournalId == id), ct);
    if (e is null) return Fail<JournalHistoricalRevision>(ErrorCodes.ScopeDenied, "Access denied.");
    var final = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    using var retained = JsonDocument.Parse(e.AfterJson);
    var after = e.Action == "MANAGEMENT" ? retained.RootElement.GetProperty("Journal") : retained.RootElement;
    var management = e.Action == "MANAGEMENT" ? retained.RootElement.GetProperty("Management").Deserialize<JournalManagementDecisionView>() : null;
    return final.Succeeded ? CommandResult<JournalHistoricalRevision>.Ok(new(Receipt(e),
      JsonSerializer.Deserialize<JournalRevisionSnapshot>(e.BeforeJson)!, after.Deserialize<JournalRevisionSnapshot>()!, management)) :
      Fail<JournalHistoricalRevision>(final.ErrorCode!, final.Message!);
  }

  public static async Task<CommandResult<AdjustmentJournalService.AdjustmentInstructionExport>> ExportAsync(
    IClientAccountingDbContext db, IAdjustmentJournalDbContext evidenceDb, ActorContext actor, Guid id, string reviewBasis, CancellationToken ct = default)
  {
    var first = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    if (!first.Succeeded) return Fail<AdjustmentJournalService.AdjustmentInstructionExport>(first.ErrorCode!, first.Message!);
    if (!SourceAcceptanceWorkspace.ValidHash(reviewBasis) || first.Value!.ReviewBasis != reviewBasis)
      return Fail<AdjustmentJournalService.AdjustmentInstructionExport>(ErrorCodes.StaleRevision, "Refresh the exact journal before exporting.");
    var export = await AdjustmentJournalService.BuildInstructionExportAsync(evidenceDb, actor, id, ct);
    if (!export.Succeeded) return export;
    var final = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    if (!final.Succeeded) return Fail<AdjustmentJournalService.AdjustmentInstructionExport>(final.ErrorCode!, final.Message!);
    return final.Value!.ReviewBasis == reviewBasis && export.Value!.JournalRevision == final.Value.Revision ? export :
      Fail<AdjustmentJournalService.AdjustmentInstructionExport>(ErrorCodes.StaleRevision, "The journal changed while exporting. Refresh before retrying.");
  }
}
