using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record JournalCreationRequest(Guid RequestId, string ReviewBasis, string JournalNumber,
  string Purpose, string Origin, string Reason, string EvidenceReference, Guid? SupersedesId, long? SupersedesRevision,
  IReadOnlyList<JournalEditLine> Lines, bool Reviewed);
public sealed record JournalCreationContext(Guid DatasetId, Guid ClientId, Guid EngagementId,
  long DatasetRevision, string DatasetDigest, Guid? PeriodId, string PeriodCode, string PeriodStart,
  string PeriodEnd, Guid? BookId, string BookCode, string Currency, string Basis, string Entity,
  string ReviewBasis, bool CanCreate, string? Blocker);

public static partial class AdjustmentJournalWorkspace
{
  private static Task<CommandResult> CreationAuth(IClientAccountingDbContext db, ActorContext actor,
    JournalCreationContext c, bool write, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, c.ClientId, c.EngagementId, write ? PrepareRoles : ReadRoles,
        InternalOnly: true, RequireProfessionalWork: true), ct);
  private static bool ValidCreation(JournalCreationRequest? r) => r is not null && r.RequestId != Guid.Empty &&
    SourceAcceptanceWorkspace.ValidHash(r.ReviewBasis) && r.JournalNumber is { Length: > 0 and <= 32 } && r.JournalNumber.Trim().Length > 0 &&
    r.Purpose is not null && AdjustmentJournalPurposes.All.Contains(r.Purpose) && r.Purpose != AdjustmentJournalPurposes.GroupOnlyElimination &&
    r.Origin is not null && AdjustmentJournalOrigins.All.Contains(r.Origin) &&
    r.Reason is { Length: > 0 and <= 4000 } && r.Reason.Trim().Length > 0 &&
    r.EvidenceReference is { Length: > 0 and <= 2000 } && r.EvidenceReference.Trim().Length > 0 &&
    (r.SupersedesId is null ? r.SupersedesRevision is null : r.SupersedesId != Guid.Empty && r.SupersedesRevision >= 1) &&
    r.Lines is { Count: >= 2 and <= MaximumLines };
  private static string CreationHash(ActorContext a, Guid id, JournalCreationRequest r) => Hashing.Sha256Hex(JsonSerializer.Serialize(new {
    a.FirmId, a.UserId, DatasetId = id, r.RequestId, r.ReviewBasis, r.JournalNumber,
    r.Purpose, r.Origin, r.Reason, r.EvidenceReference, r.SupersedesId, r.SupersedesRevision, r.Lines
  }));

  private static async Task<CommandResult<JournalCreationContext>> ReadCreationAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid id, CancellationToken ct)
  {
    var d = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId &&
      x.SourceKind == "Raw" && x.ImportState == TrialBalanceImportStates.Sealed, ct);
    if (d is null) return Fail<JournalCreationContext>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, d.ClientId, d.EngagementId,
      ReadRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded) return Fail<JournalCreationContext>(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == d.PeriodId && x.FirmId == actor.FirmId && x.ClientId == d.ClientId, ct);
    var book = await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == d.BookId && x.FirmId == actor.FirmId && x.ClientId == d.ClientId && x.PeriodId == d.PeriodId, ct);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == d.ClientId && x.FirmId == actor.FirmId, ct);
    var frozen = await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == d.EngagementId && x.State == "FROZEN", ct);
    var write = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, d.ClientId, d.EngagementId,
      PrepareRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    var blocker = !write.Succeeded ? "Current scoped preparer authority is required." :
      firm is null || safety is null ? "Safety state is unavailable." :
      d.ValidationStatus != "Accepted" || !d.Balanced || d.ControlTotal != 0m || !SourceAcceptanceWorkspace.ValidHash(MappedTrialBalanceSource.Digest(d)) ? "The exact source must be sealed, balanced and validated." :
      period is null || period.Currency != d.Currency || !string.Equals(period.Basis, d.Basis, StringComparison.OrdinalIgnoreCase) ? "Bind the exact source reporting period, basis and currency." :
      period.Status is not ("ACTIVE" or "DRAFT") || d.BookId is not null && (book is null || book.Status is not ("ACTIVE" or "DRAFT") || book.Currency != d.Currency || !string.Equals(book.Basis, d.Basis, StringComparison.OrdinalIgnoreCase)) ? "The reporting period or book is closed or unavailable." :
      frozen ? "The engagement file is frozen. An approved amendment is required." : null;
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, d, period, book, firm, safety, frozen, blocker }));
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, d.ClientId, d.EngagementId,
      ReadRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded) return Fail<JournalCreationContext>(auth.ErrorCode!, auth.Message!);
    return CommandResult<JournalCreationContext>.Ok(new(d.Id, d.ClientId, d.EngagementId, d.Revision, MappedTrialBalanceSource.Digest(d),
      d.PeriodId, period?.PeriodCode ?? "", period?.StartDate.ToString("yyyy-MM-dd") ?? "", period?.EndDate.ToString("yyyy-MM-dd") ?? "",
      d.BookId, book?.Code ?? "", d.Currency, d.Basis ?? "", d.LegalEntityKey, basis, blocker is null, blocker));
  }
  public static async Task<CommandResult<JournalCreationContext>> GetCreationAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var first = await ReadCreationAsync(db, actor, id, ct); if (!first.Succeeded) return first;
    var final = await ReadCreationAsync(db, actor, id, ct); if (!final.Succeeded) return final;
    return first.Value!.ReviewBasis == final.Value!.ReviewBasis ? final :
      Fail<JournalCreationContext>(ErrorCodes.StaleRevision, "The source context changed. Refresh and review again.");
  }
  public static async Task<CommandResult<JournalActionPreview>> PreviewCreationAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid id, JournalCreationRequest? request, CancellationToken ct = default)
  {
    if (!ValidCreation(request)) return Fail<JournalActionPreview>(ErrorCodes.Accounting.JournalRejected, "Provide a bounded journal number, purpose, origin, rationale, evidence and exact lines.");
    var r = request!; var current = await GetCreationAsync(db, actor, id, ct);
    if (!current.Succeeded) return Fail<JournalActionPreview>(current.ErrorCode!, current.Message!);
    var c = current.Value!;
    if (c.ReviewBasis != r.ReviewBasis) return Fail<JournalActionPreview>(ErrorCodes.StaleRevision, "Refresh the exact source before preparing this journal.");
    var checkedLines = await CheckEditLinesAsync(db, id, r.Lines, ct);
    if (!checkedLines.Succeeded) return Fail<JournalActionPreview>(checkedLines.ErrorCode!, checkedLines.Message!);
    var errors = checkedLines.Value!.Errors.ToList();
    if (await db.AdjustmentJournals.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == c.ClientId && x.EngagementId == c.EngagementId &&
      x.BaseDatasetId == id && x.JournalNumber == r.JournalNumber.Trim(), ct)) errors.Add("This journal number already exists for this exact source.");
    if (r.SupersedesId is { } prior && !await db.AdjustmentJournals.AnyAsync(x => x.Id == prior && x.FirmId == actor.FirmId &&
      x.ClientId == c.ClientId && x.EngagementId == c.EngagementId && x.PeriodId == c.PeriodId && x.BookId == c.BookId &&
      x.Currency == c.Currency && x.Basis == c.Basis && x.Revision == r.SupersedesRevision && (x.Status == "Posted" || x.Status == "Returned"), ct))
      return Fail<JournalActionPreview>(ErrorCodes.ScopeDenied, "The superseded journal must be a posted or returned journal in this exact reporting context.");
    var final = await GetCreationAsync(db, actor, id, ct);
    if (!final.Succeeded) return Fail<JournalActionPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != c.ReviewBasis) return Fail<JournalActionPreview>(ErrorCodes.StaleRevision, "The source changed during preview.");
    var lines = checkedLines.Value.Lines;
    return CommandResult<JournalActionPreview>.Ok(new("CREATE", c.ReviewBasis, CreationHash(actor, id, r), c.CanCreate && errors.Count == 0,
      c.Blocker, errors, lines.Sum(x => x.Debit), lines.Sum(x => x.Credit), lines));
  }
  public static async Task<CommandResult<JournalReceiptLookup>> LookupCreationAsync(IClientAccountingDbContext db,
    IAdjustmentJournalDbContext evidenceDb, ActorContext actor, Guid id, Guid requestId, string requestHash, CancellationToken ct = default)
  {
    var current = await GetCreationAsync(db, actor, id, ct);
    if (!current.Succeeded) return Fail<JournalReceiptLookup>(current.ErrorCode!, current.Message!);
    if (requestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(requestHash)) return Fail<JournalReceiptLookup>(ErrorCodes.Accounting.JournalRejected, "Use the exact pending request identity.");
    var e = await evidenceDb.AdjustmentJournalActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (e is not null && (e.Action != "CREATE" || e.RequestHash != requestHash || !await db.AdjustmentJournals.AnyAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == current.Value!.ClientId && x.EngagementId == current.Value.EngagementId && x.Id == e.JournalId && x.BaseDatasetId == id, ct)))
      return Fail<JournalReceiptLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to a different journal intent.");
    var final = await GetCreationAsync(db, actor, id, ct);
    return final.Succeeded ? CommandResult<JournalReceiptLookup>.Ok(new(e is not null, e is null ? null : Receipt(e))) :
      Fail<JournalReceiptLookup>(final.ErrorCode!, final.Message!);
  }
  public static async Task<CommandResult<JournalActionReceipt>> CreateAsync(IClientAccountingDbContext db,
    IAdjustmentJournalDbContext evidenceDb, ActorContext actor, Guid id, JournalCreationRequest? request, CancellationToken ct = default)
  {
    if (!ValidCreation(request) || !request!.Reviewed) return Fail<JournalActionReceipt>(ErrorCodes.Accounting.JournalRejected, "Preview and explicitly review this exact new journal.");
    var r = request!; var current = await GetCreationAsync(db, actor, id, ct);
    if (!current.Succeeded) return Fail<JournalActionReceipt>(current.ErrorCode!, current.Message!);
    var c = current.Value!; var auth = await CreationAuth(db, actor, c, true, ct);
    if (!auth.Succeeded) return Fail<JournalActionReceipt>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, c.ClientId, c.EngagementId, ct);
    if (!locked.Succeeded) return Fail<JournalActionReceipt>(locked.ErrorCode!, locked.Message!);
    await db.TrialBalanceDatasets.FromSqlInterpolated($"SELECT * FROM trial_balance_datasets WHERE firm_id={actor.FirmId} AND id={id} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var prior = await LookupCreationAsync(db, evidenceDb, actor, id, r.RequestId, CreationHash(actor, id, r), ct);
    if (!prior.Succeeded) return Fail<JournalActionReceipt>(prior.ErrorCode!, prior.Message!);
    if (prior.Value!.Found)
    {
      auth = await CreationAuth(db, actor, c, true, ct);
      if (!auth.Succeeded) return Fail<JournalActionReceipt>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct); return CommandResult<JournalActionReceipt>.Ok(prior.Value.Receipt!);
    }
    if (c.PeriodId is not { } periodId) return Fail<JournalActionReceipt>(ErrorCodes.GateBlocked, "Bind the source reporting period before creation.");
    if (r.SupersedesId is { } previousId)
      await db.AdjustmentJournals.FromSqlInterpolated($"SELECT * FROM adjustment_journals WHERE firm_id={actor.FirmId} AND client_id={c.ClientId} AND engagement_id={c.EngagementId} AND id={previousId} FOR SHARE").AsNoTracking().ToListAsync(ct);
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, c.ClientId, c.EngagementId, periodId, c.BookId, ct);
    if (!mutable.Succeeded) return Fail<JournalActionReceipt>(mutable.ErrorCode!, mutable.Message!);
    var preview = await PreviewCreationAsync(db, actor, id, r, ct);
    if (!preview.Succeeded) return Fail<JournalActionReceipt>(preview.ErrorCode!, preview.Message!);
    if (!preview.Value!.CanProceed) return Fail<JournalActionReceipt>(ErrorCodes.GateBlocked, preview.Value.Errors.FirstOrDefault() ?? preview.Value.Blocker!);
    var created = await AdjustmentJournalService.CreateDraftAsync(db, actor, id, r.JournalNumber,
      preview.Value.Lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray(), ct, r.Purpose, c.BookId, r.Origin, r.Reason, r.EvidenceReference, r.SupersedesId);
    if (!created.Succeeded) return Fail<JournalActionReceipt>(created.ErrorCode!, created.Message!);
    var after = await ReadAsync(db, evidenceDb, actor, created.Value, 1, ct);
    if (!after.Succeeded) return Fail<JournalActionReceipt>(after.ErrorCode!, after.Message!);
    var evidence = new AdjustmentJournalAction {
      Id=Guid.CreateVersion7(), FirmId=actor.FirmId, ClientId=c.ClientId, EngagementId=c.EngagementId,
      JournalId=created.Value, ResultJournalId=created.Value, ActorId=actor.UserId, ActorEpoch=actor.SessionEpoch,
      RequestId=r.RequestId, RequestHash=CreationHash(actor,id,r), ReviewBasis=r.ReviewBasis, Action="CREATE",
      OldRevision=0, NewRevision=1, OldStatus="NOT_CREATED", NewStatus="Draft", Reason=r.Reason.Trim(), EvidenceReference=r.EvidenceReference.Trim(),
      BeforeJson=JsonSerializer.Serialize(new JournalRevisionSnapshot(created.Value,0,"NOT_CREATED","","",[])), AfterJson=after.Value!.Json, CreatedAt=DateTimeOffset.UtcNow
    };
    evidenceDb.AdjustmentJournalActions.Add(evidence); await db.SaveChangesAsync(ct);
    auth = await CreationAuth(db, actor, c, true, ct);
    if (!auth.Succeeded) return Fail<JournalActionReceipt>(auth.ErrorCode!, auth.Message!);
    mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, c.ClientId, c.EngagementId, periodId, c.BookId, ct);
    if (!mutable.Succeeded) return Fail<JournalActionReceipt>(mutable.ErrorCode!, mutable.Message!);
    await tx.CommitAsync(ct); return CommandResult<JournalActionReceipt>.Ok(Receipt(evidence));
  }
}
