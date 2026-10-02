using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record GeneralLedgerSourceSummary(Guid Id, Guid PeriodId, string PeriodCode,
  Guid? BookId, string Entity, string Currency, int RowCount, string SourceHash, DateTimeOffset ImportedAt);
public sealed record GeneralLedgerCatalogue(Guid ClientId, Guid EngagementId,
  IReadOnlyList<GeneralLedgerSourceSummary> Items, int TotalCount, int Page, int PageSize);
public sealed record GeneralLedgerSourceContext(Guid BatchId, Guid ClientId, Guid EngagementId,
  Guid PeriodId, string PeriodCode, Guid? BookId, string Entity, string Currency, string ImportState,
  string RawFileSha256, string SourceHash, string ProfileVersion, string ParserVersion,
  Guid ImportedByUserId, DateTimeOffset ImportedAt, SelectedSourceDto? Selected);
public sealed record GeneralLedgerSourceWorkspaceDto(GeneralLedgerSourceContext Context,
  GeneralLedgerLineFilter Filter, GeneralLedgerLinesPage Rows, int JournalLineLimit);
public sealed record GeneralLedgerJournalWorkspaceDto(GeneralLedgerSourceContext Context,
  GeneralLedgerJournalDetail Journal, int JournalLineLimit);

/// <summary>Scoped read-only GL inspection. Sealing, balance and selection are separate facts;
/// this workspace makes no completeness decision or professional conclusion.</summary>
public static class GeneralLedgerWorkspace
{
  public const int CataloguePageSize = 20;
  public const int LinesPageSize = 100;

  public static async Task<CommandResult<GeneralLedgerCatalogue>> CatalogueAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, int page = 1, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<GeneralLedgerCatalogue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, GeneralLedgerQuery.ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<GeneralLedgerCatalogue>.Fail(auth.ErrorCode!, auth.Message!);
    if (page < 1 || (long)(page - 1) * CataloguePageSize > int.MaxValue)
      return CommandResult<GeneralLedgerCatalogue>.Fail(ErrorCodes.Accounting.ImportRejected, "The ledger catalogue page is invalid.");
    var sources = db.SourceImportBatches.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId && x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED")
      .Join(db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId),
        x => x.PeriodId, period => period.Id, (batch, period) => new { batch, period });
    var count = await sources.CountAsync(ct);
    var items = await sources.OrderByDescending(x => x.batch.CreatedAt).ThenBy(x => x.batch.Id)
      .Skip((page - 1) * CataloguePageSize).Take(CataloguePageSize)
      .Select(x => new GeneralLedgerSourceSummary(x.batch.Id, x.batch.PeriodId, x.period.PeriodCode,
        x.batch.BookId, x.batch.LegalEntityKey, x.batch.Currency, x.batch.RowCount,
        x.batch.NormalizedDatasetDigest, x.batch.CreatedAt)).ToListAsync(ct);
    if (items.Any(x => !ValidHash(x.SourceHash)))
      return CommandResult<GeneralLedgerCatalogue>.Fail(ErrorCodes.GateBlocked, "A ledger source identity is unavailable. Resolve it before inspection.");
    if (!await db.Engagements.AsNoTracking().AnyAsync(x => x.Id == engagementId &&
        x.FirmId == actor.FirmId && x.PracticeClientId == engagement.PracticeClientId, ct))
      return CommandResult<GeneralLedgerCatalogue>.Fail(ErrorCodes.StaleRevision, "The engagement changed. Refresh its ledger catalogue.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, GeneralLedgerQuery.ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<GeneralLedgerCatalogue>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<GeneralLedgerCatalogue>.Ok(new(engagement.PracticeClientId, engagementId, items, count, page, CataloguePageSize));
  }

  public static async Task<CommandResult<GeneralLedgerSourceWorkspaceDto>> SourceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid batchId,
    GeneralLedgerLineFilter? filter = null, int page = 1, CancellationToken ct = default)
  {
    var context = await ContextAsync(db, actor, engagementId, batchId, ct);
    if (!context.Succeeded) return CommandResult<GeneralLedgerSourceWorkspaceDto>.Fail(context.ErrorCode!, context.Message!);
    var rows = await GeneralLedgerQuery.GetLinesAsync(db, actor, batchId, filter, page, LinesPageSize, ct);
    if (!rows.Succeeded) return CommandResult<GeneralLedgerSourceWorkspaceDto>.Fail(rows.ErrorCode!, rows.Message!);
    var final = await ContextAsync(db, actor, engagementId, batchId, ct);
    if (!final.Succeeded) return CommandResult<GeneralLedgerSourceWorkspaceDto>.Fail(final.ErrorCode!, final.Message!);
    if (context.Value != final.Value) return CommandResult<GeneralLedgerSourceWorkspaceDto>.Fail(ErrorCodes.StaleRevision, "The ledger context changed. Refresh its source.");
    return CommandResult<GeneralLedgerSourceWorkspaceDto>.Ok(new(context.Value!,
      new(filter?.AccountCodePrefix?.Trim() ?? "", filter?.PostedFrom, filter?.PostedTo,
        filter?.StableJournalId?.Trim() ?? "", filter?.Counterparty?.Trim() ?? ""), rows.Value!, GeneralLedgerQuery.MaxJournalLines));
  }

  public static async Task<CommandResult<GeneralLedgerJournalWorkspaceDto>> JournalAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid batchId,
    string journalId, CancellationToken ct = default)
  {
    var context = await ContextAsync(db, actor, engagementId, batchId, ct);
    if (!context.Succeeded) return CommandResult<GeneralLedgerJournalWorkspaceDto>.Fail(context.ErrorCode!, context.Message!);
    var journal = await GeneralLedgerQuery.GetJournalDrillDownAsync(db, actor, batchId, journalId, ct);
    if (!journal.Succeeded) return CommandResult<GeneralLedgerJournalWorkspaceDto>.Fail(journal.ErrorCode!, journal.Message!);
    var final = await ContextAsync(db, actor, engagementId, batchId, ct);
    if (!final.Succeeded) return CommandResult<GeneralLedgerJournalWorkspaceDto>.Fail(final.ErrorCode!, final.Message!);
    if (context.Value != final.Value) return CommandResult<GeneralLedgerJournalWorkspaceDto>.Fail(ErrorCodes.StaleRevision, "The ledger context changed. Refresh its source.");
    return CommandResult<GeneralLedgerJournalWorkspaceDto>.Ok(new(context.Value!, journal.Value!, GeneralLedgerQuery.MaxJournalLines));
  }

  private static bool ValidHash(string s) => s.Length == 64 && s.All(char.IsAsciiHexDigit);
  internal static async Task<CommandResult<GeneralLedgerSourceContext>> ContextAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid engagementId, Guid batchId, CancellationToken ct)
  {
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId &&
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
    if (batch is null) return CommandResult<GeneralLedgerSourceContext>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, batch.ClientId, engagementId, GeneralLedgerQuery.ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<GeneralLedgerSourceContext>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == batch.PeriodId && x.FirmId == actor.FirmId && x.ClientId == batch.ClientId, ct);
    if (period is null || !ValidHash(batch.RawFileSha256Hex) || !ValidHash(batch.NormalizedDatasetDigest))
      return CommandResult<GeneralLedgerSourceContext>.Fail(ErrorCodes.GateBlocked, "The reporting period or immutable source identity is unavailable.");
    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, actor, batch.ClientId, engagementId, AccountingSourceKinds.GeneralLedger, ct);
    if (!selected.Succeeded) return CommandResult<GeneralLedgerSourceContext>.Fail(selected.ErrorCode!, selected.Message!);
    var final = await GeneralLedgerQuery.CheckCurrentAsync(db, actor, batch, ct);
    if (!final.Succeeded) return CommandResult<GeneralLedgerSourceContext>.Fail(final.ErrorCode!, final.Message!);
    return CommandResult<GeneralLedgerSourceContext>.Ok(new(batch.Id, batch.ClientId, engagementId, batch.PeriodId,
      period.PeriodCode, batch.BookId, batch.LegalEntityKey, batch.Currency, batch.Status,
      batch.RawFileSha256Hex, batch.NormalizedDatasetDigest, batch.ProfileVersion, batch.ParserVersion,
      batch.CreatedByUserId, batch.CreatedAt, selected.Value));
  }
}
