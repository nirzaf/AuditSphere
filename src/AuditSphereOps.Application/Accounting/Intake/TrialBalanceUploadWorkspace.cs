using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record TrialBalanceUploadPeriod(string PeriodCode, Guid? PeriodId, string? PeriodRevision, int RowCount, string? Currency,
  decimal? NetTotal, bool Balanced, string? Error, Guid? DatasetId, string? ImportState, string? ValidationStatus,
  Guid? OperationId, string? OperationState);
public sealed record TrialBalanceUploadReview(Guid EngagementId, Guid ClientId, string FileSha256, string Revision,
  bool CanImport, IReadOnlyList<TrialBalanceUploadPeriod> Periods);

/// <summary>Reviewed multi-period intake and read-only reconciliation. Each period keeps its own guarded
/// importer transaction and durable validation operation; a browser response never implies batch atomicity.</summary>
public static class TrialBalanceUploadWorkspace
{
  internal static string PeriodRevision(ClientReportingPeriod p) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
    p.Id, p.PeriodCode, p.StartDate, p.EndDate, p.Currency, p.Basis, p.Status
  })));
  public static async Task<CommandResult<TrialBalanceUploadReview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, string name, byte[] bytes, CancellationToken ct = default)
  {
    var workspace = await TrialBalanceIntakeWorkspaceQuery.GetAsync(db, actor, engagementId, ct);
    if (!workspace.Succeeded) return CommandResult<TrialBalanceUploadReview>.Fail(workspace.ErrorCode!, workspace.Message!);
    var clientId = workspace.Value!.ClientId;
    var preview = await MultiPeriodTrialBalanceService.PreviewAsync(db, actor, clientId, name, bytes, ct, engagementId);
    if (!preview.Succeeded) return CommandResult<TrialBalanceUploadReview>.Fail(preview.ErrorCode!, preview.Message!);
    var value = preview.Value!;
    var ids = value.Periods.Where(x => x.PeriodId is not null).Select(x => x.PeriodId!.Value).ToArray();
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.Id))
      .OrderBy(x => x.Id).ToListAsync(ct);
    var (header, rows) = MultiPeriodTrialBalanceService.ReadTable(name, bytes);
    var split = MultiPeriodTrialBalanceService.Split(header, rows);
    var result = new List<TrialBalanceUploadPeriod>();
    foreach (var p in value.Periods)
    {
      var hash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(split[p.PeriodCode]));
      var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.EngagementId == engagementId && x.PeriodId == p.PeriodId && x.SourceKind == "Raw" && x.RawFileSha256Hex == hash, ct);
      var operation = dataset is null ? null : await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.TargetId == dataset.Id && x.ExpectedRevision == dataset.Revision && x.OperationKind == TrialBalanceValidationHandler.Kind)
        .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new { x.Id, x.Status }).FirstOrDefaultAsync(ct);
      result.Add(new(p.PeriodCode, p.PeriodId, periods.FirstOrDefault(x => x.Id == p.PeriodId) is { } period ? PeriodRevision(period) : null,
        p.RowCount, p.Currency, p.NetTotal, p.Balanced, p.Error,
        dataset?.Id, dataset?.ImportState, dataset?.ValidationStatus, operation?.Id, operation?.Status.ToString()));
    }
    // Only upload bytes, current actor and period metadata bind assent. Validation progressing or a successful
    // earlier period does not invalidate the receipt used to recover a partially observed upload.
    var revision = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
      actor.FirmId, actor.UserId, actor.SessionEpoch, engagementId, clientId, value.FileSha256,
      periods = periods.Select(x => new { x.Id, x.PeriodCode, x.StartDate, x.EndDate, x.Currency, x.Basis, x.Status })
    })));
    workspace = await TrialBalanceIntakeWorkspaceQuery.GetAsync(db, actor, engagementId, ct);
    if (!workspace.Succeeded) return CommandResult<TrialBalanceUploadReview>.Fail(workspace.ErrorCode!, workspace.Message!);
    return CommandResult<TrialBalanceUploadReview>.Ok(new(engagementId, clientId, value.FileSha256, revision, value.CanImport, result));
  }

  public static async Task<CommandResult<TrialBalanceUploadReview>> ImportAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, string name, byte[] bytes, string expectedRevision, string expectedFileSha256,
    bool reviewed, CancellationToken ct = default)
  {
    var current = await PreviewAsync(db, actor, engagementId, name, bytes, ct);
    if (!current.Succeeded) return current;
    if (!reviewed || current.Value!.Revision != expectedRevision || current.Value.FileSha256 != expectedFileSha256)
      return CommandResult<TrialBalanceUploadReview>.Fail(ErrorCodes.StaleRevision, "Upload or reporting-period inputs changed. Preview and review the exact current file again.");
    if (!current.Value.CanImport) return CommandResult<TrialBalanceUploadReview>.Fail(ErrorCodes.Accounting.ImportRejected,
      "Every period must pass preview before any new period is imported.");
    var (header, rows) = MultiPeriodTrialBalanceService.ReadTable(name, bytes);
    var split = MultiPeriodTrialBalanceService.Split(header, rows);
    foreach (var p in current.Value.Periods)
    {
      if (p.DatasetId is not null)
      {
        if (p.ImportState != TrialBalanceImportStates.Sealed) return CommandResult<TrialBalanceUploadReview>.Fail(ErrorCodes.GateBlocked, "An existing source is not sealed. Reconcile its import before proceeding.");
        continue; // Exact existing source, never email/name or a normalized fallback.
      }
      var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == p.PeriodId && x.FirmId == actor.FirmId, ct);
      var csv = split[p.PeriodCode];
      var imported = await TrialBalanceImportService.ImportWithProfileAsync(db, actor, current.Value.ClientId, engagementId,
        csv, MultiPeriodTrialBalanceService.ProfileFor(csv), new(period.Id, null, period.Basis, p.PeriodRevision), null, ct);
      if (!imported.Succeeded)
      {
        // A concurrent identical upload may have won this period. Reconcile exact scoped identities;
        // equivalent-but-different bytes remain a conflict and cannot silently become the reviewed source.
        if (imported.ErrorCode == ErrorCodes.Accounting.ImportDuplicate)
        {
          var observed = await PreviewAsync(db, actor, engagementId, name, bytes, ct);
          if (observed.Succeeded && observed.Value!.Periods.Single(x => x.PeriodCode == p.PeriodCode).DatasetId is not null) continue;
        }
        return CommandResult<TrialBalanceUploadReview>.Fail(imported.ErrorCode!,
          $"Period {p.PeriodCode} was not accepted. Refresh the exact file's persisted period receipts before another reviewed attempt; earlier periods may already exist.");
      }
    }
    return await PreviewAsync(db, actor, engagementId, name, bytes, ct);
  }
}
