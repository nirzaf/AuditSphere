using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal static partial class AuditScheduleIntakeSources
{
  internal static async Task<CommandResult<AuditSchedulePbcOption>> GetPbcOptionAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid receiptId,
    CancellationToken ct)
  {
    var pbc = await LoadPbcAsync(db, firmId, clientId, engagementId, receiptId, ct);
    if (!pbc.Succeeded) return CopyFailure<PbcFacts, AuditSchedulePbcOption>(pbc);
    if (!DateOnly.TryParseExact(pbc.Value!.PeriodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var start) ||
        !DateOnly.TryParseExact(pbc.Value.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture,
          DateTimeStyles.None, out var end) || start > end)
      return Fail<AuditSchedulePbcOption>("The accepted PBC request has no supported ISO reporting period.");
    return CommandResult<AuditSchedulePbcOption>.Ok(new(receiptId, pbc.Value.FileName,
      pbc.Value.ByteCount, pbc.Value.EntityScope, start, end));
  }

  internal static async Task<CommandResult<AuditScheduleTrialBalanceOption>> GetTrialBalanceOptionAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid decisionId,
    CancellationToken ct)
  {
    var decision = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == decisionId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (decision?.TrialBalanceDatasetId is not { } datasetId)
      return Scope<AuditScheduleTrialBalanceOption>();
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId, ct);
    if (dataset?.PeriodId is not { } periodId)
      return Fail<AuditScheduleTrialBalanceOption>("The accepted TB reporting period is unavailable.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId &&
      x.FirmId == firmId && x.ClientId == clientId, ct);
    if (period is null)
      return Fail<AuditScheduleTrialBalanceOption>("The accepted TB reporting period is unavailable.");
    var current = await LoadTbAsync(db, firmId, clientId, engagementId, decisionId, period.EndDate, ct);
    if (!current.Succeeded) return CopyFailure<TbFacts, AuditScheduleTrialBalanceOption>(current);
    var value = current.Value!;
    return CommandResult<AuditScheduleTrialBalanceOption>.Ok(new(decisionId, value.PeriodId, value.BookId,
      value.Basis, value.PeriodStart, value.PeriodEnd, value.LegalEntityKey, value.Currency,
      Exact(value.DatasetRevision), Exact(value.Generation)));
  }

  internal static async Task<CommandResult<AuditScheduleSourceHeaders>> GetHeadersAsync(
    IClientAccountingDbContext db, ActorContext actor, IAuditScheduleSourceContentReader reader,
    Guid clientId, Guid engagementId, Guid receiptId, CancellationToken ct)
  {
    if (reader is null || receiptId == Guid.Empty) return Fail<AuditScheduleSourceHeaders>("Select an exact accepted PBC source.");
    var pbc = await LoadPbcAsync(db, actor.FirmId, clientId, engagementId, receiptId, ct);
    if (!pbc.Succeeded) return CopyFailure<PbcFacts, AuditScheduleSourceHeaders>(pbc);
    var download = await PbcService.PrepareDownloadAsync(db, actor, pbc.Value!.UploadIntentId, ct);
    if (!download.Succeeded) return CopyFailure<PbcDownload, AuditScheduleSourceHeaders>(download);
    if (download.Value is null || download.Value.FileName != pbc.Value.FileName || download.Value.ByteCount != pbc.Value.ByteCount)
      return Stale<AuditScheduleSourceHeaders>();
    var content = await reader.ReadVerifiedAsync(download.Value, pbc.Value.Hash, pbc.Value.ByteCount, ct);
    if (!content.Succeeded) return CopyFailure<byte[], AuditScheduleSourceHeaders>(content);
    IReadOnlyList<string> headers;
    try { headers = AuditScheduleCsvProfile.DiscoverHeaders(pbc.Value.FileName, content.Value!); }
    catch (FormatException)
    {
      return Fail<AuditScheduleSourceHeaders>("The accepted listing does not match the supported CSV header profile.");
    }
    ct.ThrowIfCancellationRequested();
    var current = await LoadPbcAsync(db, actor.FirmId, clientId, engagementId, receiptId, ct);
    if (!current.Succeeded) return CopyFailure<PbcFacts, AuditScheduleSourceHeaders>(current);
    if (current.Value != pbc.Value) return Stale<AuditScheduleSourceHeaders>();
    var authority = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
    if (!authority.Succeeded) return Denied<AuditScheduleSourceHeaders>(authority);
    return CommandResult<AuditScheduleSourceHeaders>.Ok(new(receiptId, AuditScheduleCsvProfile.Version, headers));
  }

  internal static async Task<CommandResult<AuditScheduleControlAccountPage>> GetControlAccountsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid engagementId, Guid decisionId,
    DateOnly sourceDate, int offset, int limit, CancellationToken ct)
  {
    if (decisionId == Guid.Empty || offset is < 0 or > MaximumControlRows || limit is < 1 or > 100)
      return Fail<AuditScheduleControlAccountPage>("Select an exact accepted TB source and a supported account page.");
    var tb = await LoadTbAsync(db, actor.FirmId, clientId, engagementId, decisionId, sourceDate, ct);
    if (!tb.Succeeded) return CopyFailure<TbFacts, AuditScheduleControlAccountPage>(tb);
    var trialBalance = tb.Value!;
    var population = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == trialBalance.DatasetId)
      .OrderBy(x => x.Id).Take(MaximumControlRows + 1).ToListAsync(ct);
    if (population.Count is 0 or > MaximumControlRows)
      return Fail<AuditScheduleControlAccountPage>("The selected TB account population is empty or exceeds its supported window.");
    var codes = population.Select(x => x.AccountCode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    if (codes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 100 || x != x.Trim()) || codes.Length > 20_000)
      return Fail<AuditScheduleControlAccountPage>("The selected TB contains unsupported account-code evidence.");
    var controls = await LoadControlsAsync(db, trialBalance, codes, ct);
    if (!controls.Succeeded) return CopyFailure<ControlFacts, AuditScheduleControlAccountPage>(controls);
    AuditScheduleControlAccountOption[] options;
    try
    {
      options = controls.Value!.Rows.GroupBy(x => x.AccountCode, StringComparer.Ordinal)
        .OrderBy(x => x.Key, StringComparer.Ordinal).Select(group =>
        {
          var balance = group.Sum(x => x.Amount);
          if (!Money(balance)) throw new OverflowException();
          var names = group.Select(x => x.AccountName).Distinct(StringComparer.Ordinal).ToArray();
          return new AuditScheduleControlAccountOption(group.Key,
            names.Length == 1 ? names[0] : "Multiple source names", Exact(balance), trialBalance.Currency);
        }).ToArray();
    }
    catch (OverflowException)
    {
      return Fail<AuditScheduleControlAccountPage>("A selected TB account total exceeds the supported exact-money range.");
    }
    var current = await LoadTbAsync(db, actor.FirmId, clientId, engagementId, decisionId, sourceDate, ct);
    if (!current.Succeeded) return CopyFailure<TbFacts, AuditScheduleControlAccountPage>(current);
    var currentControls = await LoadControlsAsync(db, current.Value!, codes, ct);
    if (!currentControls.Succeeded) return CopyFailure<ControlFacts, AuditScheduleControlAccountPage>(currentControls);
    if (current.Value != tb.Value || currentControls.Value!.Json != controls.Value!.Json)
      return Stale<AuditScheduleControlAccountPage>();
    var authority = await AuthorizeAsync(db, actor, clientId, engagementId, ct);
    if (!authority.Succeeded) return Denied<AuditScheduleControlAccountPage>(authority);
    var items = options.Skip(offset).Take(limit).ToArray();
    return CommandResult<AuditScheduleControlAccountPage>.Ok(new(offset, limit,
      options.Length > offset + limit, items));
  }
}
