using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class NativeTrialBalanceSourceWorkspace
{
  internal sealed record SuccessorSource(TrialBalanceDataset Dataset, NativeTrialBalanceSource Evidence,
    ClientReportingPeriod Period, ClientOperationalTrialBalanceView TrialBalance, string MembershipJson,
    Guid AcceptanceId, string AcceptanceHash);

  internal static async Task<CommandResult<SuccessorSource>> ReadSuccessorSourceAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, Guid engagementId, Guid datasetId, CancellationToken ct)
  {
    var result = await ReadSuccessorSourceCoreAsync(db, firmId, clientId, engagementId, datasetId, ct);
    if (!result.Succeeded) return result;
    var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.PeriodId == result.Value!.Period.Id, ct);
    if (opening is null) return Fail<SuccessorSource>(ErrorCodes.GateBlocked, "Complete predecessor opening required.");
    var chain = await CheckOpeningSuccessorChainAsync(db, opening, ct, expectedEngagementId: engagementId);
    return chain.Succeeded ? result : Fail<SuccessorSource>(chain.ErrorCode!, chain.Message!);
  }

  private static async Task<CommandResult<SuccessorSource>> ReadSuccessorSourceCoreAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, Guid engagementId, Guid datasetId, CancellationToken ct)
  {
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId &&
      x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.SourceKind == SourceKind, ct);
    if (source is null) return Fail<SuccessorSource>(ErrorCodes.ScopeDenied, "The exact scoped native predecessor source is required.");
    var current = await CheckCurrentCoreAsync(db, source, ct, true);
    if (!current.Succeeded) return Fail<SuccessorSource>(current.ErrorCode!, current.Message!);
    if (source.ValidationStatus != "Accepted" || !source.Balanced || source.ControlTotal != 0m)
      return Fail<SuccessorSource>(ErrorCodes.GateBlocked, "The predecessor source requires technical validation.");
    var selected = await CheckSelectedCoreAsync(db, source, ct);
    if (!selected.Succeeded) return Fail<SuccessorSource>(selected.ErrorCode!, selected.Message!);
    var retained = await RetainedAsync(db, source, ct);
    if (!retained.Succeeded) return Fail<SuccessorSource>(retained.ErrorCode!, retained.Message!);
    var r = retained.Value!;
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == r.Manifest.PeriodId &&
      x.FirmId == firmId && x.ClientId == clientId, ct);
    if (period.Status != AccountingWorkflowStates.Closed || period.ClosedByUserId is null || period.ClosedAt is null ||
        string.IsNullOrWhiteSpace(period.CloseReason))
      return Fail<SuccessorSource>(ErrorCodes.GateBlocked, "An actual closed predecessor with retained close evidence is required.");
    var acceptance = await db.SourceAcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.EngagementId == engagementId && x.TrialBalanceDatasetId == source.Id &&
      x.Decision == "ACCEPTED").OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstAsync(ct);
    return CommandResult<SuccessorSource>.Ok(new(source, r.Evidence, period, r.Manifest.TrialBalance,
      r.Manifest.Pins.MembershipJson, acceptance.Id, Hash(acceptance)));
  }

  internal static async Task<CommandResult> CheckOpeningSuccessorChainAsync(IClientAccountingDbContext db,
    ClientOperationalOpeningBalance opening, CancellationToken ct, Guid? expectedEngagementId = null)
  {
    var visited = new HashSet<Guid>();
    for (var depth = 0; depth < 32; depth++)
    {
      if (!visited.Add(opening.PeriodId)) return CommandResult.Fail(ErrorCodes.ProtectedState, "Cyclic successor source lineage.");
      var row = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x => x.OpeningId == opening.Id, ct);
      if (row is null) return ValidLegacyOpening(opening);
      expectedEngagementId ??= row.EngagementId;
      if (row.EngagementId != expectedEngagementId)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Cross-engagement successor source ancestry is unsupported.");
      var proof = ClientOperationalOpeningBalanceWorkspace.ParseSuccessorProof(opening, row);
      if (!proof.Succeeded) return CommandResult.Fail(proof.ErrorCode!, proof.Message!);
      var p = proof.Value!;
      var source = await ReadSuccessorSourceCoreAsync(db, opening.FirmId, opening.ClientId, row.EngagementId, row.SourceDatasetId, ct);
      if (!source.Succeeded) return CommandResult.Fail(source.ErrorCode!, source.Message!);
      var facts = await ClientOperationalOpeningBalanceWorkspace.ResolveSuccessorFactsAsync(db, opening.FirmId,
        opening.ClientId, opening.PeriodId, source.Value!, ct, row.CreatedAt);
      if (!facts.Succeeded) return CommandResult.Fail(facts.ErrorCode!, facts.Message!);
      if (source.Value!.Dataset.Sha256Hex != row.SourceIdentityHash ||
          JsonSerializer.Serialize(facts.Value) != JsonSerializer.Serialize(p.Facts))
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The successor opening's exact closed source or control proof changed.");
      var binding = ClientOperationalOpeningBalanceWorkspace.ParseSuccessorProof(opening, row, facts.Value!);
      if (!binding.Succeeded) return CommandResult.Fail(binding.ErrorCode!, binding.Message!);
      var priorOpening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == opening.FirmId && x.ClientId == opening.ClientId && x.PeriodId == row.PriorPeriodId, ct);
      if (priorOpening is null) return CommandResult.Fail(ErrorCodes.ProtectedState, "Missing predecessor opening.");
      opening = priorOpening;
    }
    return CommandResult.Fail(ErrorCodes.GateBlocked, "Successor ancestry exceeds the supported 32-opening bound.");
  }

  private static CommandResult ValidLegacyOpening(ClientOperationalOpeningBalance opening) =>
    opening.ApprovedByUserId is not null && opening.ApprovedAt is not null &&
    ClientOperationalOpeningBalanceWorkspace.ValidManifest(opening)
      ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.GateBlocked, "The original opening requires intact independent approval.");
}
