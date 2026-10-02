using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record TrialBalanceSourceWorkspaceDto(
  Guid DatasetId, Guid ClientId, Guid EngagementId, Guid? PeriodId, string? PeriodCode,
  long Revision, string Currency, string Entity, string SourceKind, string ImportState,
  string ValidationStatus, bool Balanced, string RawFileSha256, string NormalizedDigest,
  string AccountCodeFilter, TrialBalanceRowsPage Rows, TrialBalanceIssuesPage Issues, int ExportRowLimit, int ExportByteLimit);

/// <summary>Bounded source inspection, distinct from acceptance, GL completeness and mapping approval.</summary>
public static class TrialBalanceSourceWorkspace
{
  public static async Task<CommandResult<TrialBalanceSourceWorkspaceDto>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid datasetId, string? filter = null,
    int page = 1, int issuePage = 1, CancellationToken ct = default)
  {
    var source = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == datasetId && x.FirmId == actor.FirmId && x.ImportState == TrialBalanceImportStates.Sealed, ct);
    if (source is null) return CommandResult<TrialBalanceSourceWorkspaceDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(source.FirmId, source.ClientId, source.EngagementId, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<TrialBalanceSourceWorkspaceDto>.Fail(auth.ErrorCode!, auth.Message!);
    var periodCode = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.Id == source.PeriodId &&
      x.FirmId == source.FirmId && x.ClientId == source.ClientId).Select(x => x.PeriodCode).SingleOrDefaultAsync(ct);
    var rows = await TrialBalanceDatasetQuery.GetTrialBalanceRowsAsync(db, actor, source.Id, filter, page, 100, ct);
    if (!rows.Succeeded) return CommandResult<TrialBalanceSourceWorkspaceDto>.Fail(rows.ErrorCode!, rows.Message!);
    var issues = await TrialBalanceValidationIssueQuery.GetIssuesAsync(db, actor, source.Id, issuePage, 100, ct);
    if (!issues.Succeeded) return CommandResult<TrialBalanceSourceWorkspaceDto>.Fail(issues.ErrorCode!, issues.Message!);
    var currentPeriodCode = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.Id == source.PeriodId &&
      x.FirmId == source.FirmId && x.ClientId == source.ClientId).Select(x => x.PeriodCode).SingleOrDefaultAsync(ct);
    if (periodCode != currentPeriodCode)
      return CommandResult<TrialBalanceSourceWorkspaceDto>.Fail(ErrorCodes.StaleRevision, "The reporting period changed. Refresh its source.");
    var final = await TrialBalanceDatasetQuery.CheckCurrentAsync(db, actor, source, ct);
    if (!final.Succeeded) return CommandResult<TrialBalanceSourceWorkspaceDto>.Fail(final.ErrorCode!, final.Message!);
    return CommandResult<TrialBalanceSourceWorkspaceDto>.Ok(new(source.Id, source.ClientId, source.EngagementId,
      source.PeriodId, periodCode, source.Revision, source.Currency, source.LegalEntityKey, source.SourceKind,
      source.ImportState, source.ValidationStatus, source.Balanced, source.RawFileSha256Hex,
      source.NormalizedDatasetDigest, filter?.Trim() ?? "", rows.Value!, issues.Value!, TrialBalanceDatasetQuery.MaxExportRows, TrialBalanceDatasetQuery.MaxExportBytes));
  }
}
