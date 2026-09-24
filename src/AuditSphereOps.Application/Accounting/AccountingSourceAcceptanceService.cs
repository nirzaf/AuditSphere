using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// Module 21 source acceptance (T017): sealing proves immutability; an independent
// reviewer's acceptance decision selects the source revision downstream modules consume.
// Decisions are append-only; the latest ACCEPTED decision per context and source kind is
// the selected pointer. Accepting a new revision bumps the client input generation so
// dependent mappings, completeness proofs and packages stale exactly like any other input.
public sealed record AcceptSourceRevisionRequest(
  Guid ClientId, Guid EngagementId, string SourceKind,
  Guid? TrialBalanceDatasetId, Guid? ImportBatchId, string EvidenceReference);

public sealed record SelectedSourceDto(
  Guid DecisionId, string SourceKind, Guid? TrialBalanceDatasetId, Guid? ImportBatchId,
  string SourceIdentityHash, Guid AcceptedByUserId, DateTimeOffset AcceptedAt, long InputGeneration);

public static class AccountingSourceAcceptanceService
{
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> AcceptSourceRevisionAsync(
    IClientAccountingDbContext db, ActorContext actor, AcceptSourceRevisionRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.EvidenceReference.Trim().Length > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected,
        "A source acceptance needs an engagement scope and an evidence reference.");
    var kind = request.SourceKind?.Trim().ToUpperInvariant() ?? string.Empty;
    if (kind is not (AccountingSourceKinds.TrialBalance or AccountingSourceKinds.GeneralLedger))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "Source kind must be TB or GL.");
    if (kind == AccountingSourceKinds.TrialBalance && request.TrialBalanceDatasetId is not null && request.ImportBatchId is not null ||
        request.TrialBalanceDatasetId is null && request.ImportBatchId is null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected,
        "Accept exactly one source: a trial-balance dataset or a sealed GL batch.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, ReviewerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    string sourceHash;
    if (kind == AccountingSourceKinds.TrialBalance)
    {
      if (request.TrialBalanceDatasetId is { } datasetId)
      {
        var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == datasetId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
          x.EngagementId == request.EngagementId && x.SourceKind == "Raw" &&
          x.ImportState == TrialBalanceImportStates.Sealed && x.ValidationStatus == "Accepted", ct);
        if (dataset is null)
          return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
            "Only an accepted, sealed trial-balance revision can be accepted as the reporting source.");
        sourceHash = dataset.NormalizedDatasetDigest.Length == 64 ? dataset.NormalizedDatasetDigest : dataset.Sha256Hex;
        if (await db.SourceAcceptanceDecisions.AsNoTracking().AnyAsync(x =>
              x.FirmId == actor.FirmId && x.TrialBalanceDatasetId == datasetId && x.Decision == "ACCEPTED", ct))
          return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
            "This trial-balance revision was already accepted.");
      }
      else
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "A trial-balance dataset id is required.");
    }
    else
    {
      if (request.ImportBatchId is { } batchId)
      {
        var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == batchId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
          x.EngagementId == request.EngagementId && x.Status == "SEALED", ct);
        if (batch is null)
          return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
            "Only a sealed general-ledger batch can be accepted as the reporting source.");
        sourceHash = batch.NormalizedDatasetDigest;
        if (await db.SourceAcceptanceDecisions.AsNoTracking().AnyAsync(x =>
              x.FirmId == actor.FirmId && x.ImportBatchId == batchId && x.Decision == "ACCEPTED", ct))
          return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
            "This general-ledger batch was already accepted.");
      }
      else
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "A GL import batch id is required.");
    }

    var decision = new SourceAcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      EngagementId = request.EngagementId, SourceKind = kind,
      TrialBalanceDatasetId = request.TrialBalanceDatasetId, ImportBatchId = request.ImportBatchId,
      SourceIdentityHash = sourceHash, Decision = "ACCEPTED",
      EvidenceReference = request.EvidenceReference.Trim(), AcceptedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.SourceAcceptanceDecisions.Add(decision);

    var safety = await db.ClientSafetyStates.SingleOrDefaultAsync(x =>
      x.Id == request.ClientId && x.FirmId == actor.FirmId, ct);
    if (safety is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The client safety state is unavailable.");
    safety.InputGeneration++;
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(decision.Id);
  }

  /// <summary>The currently selected accepted source for one context and source kind:
  /// the latest ACCEPTED decision. Returns null-safe Ok with a null payload when none.</summary>
  public static async Task<CommandResult<SelectedSourceDto?>> GetSelectedSourceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid engagementId, string sourceKind,
    CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<SelectedSourceDto?>.Fail(auth.ErrorCode!, auth.Message!);
    var decision = await db.SourceAcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId &&
        x.SourceKind == sourceKind && x.Decision == "ACCEPTED")
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    if (decision is null)
      return CommandResult<SelectedSourceDto?>.Ok(null);
    var generation = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.Id == clientId && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    return CommandResult<SelectedSourceDto?>.Ok(new SelectedSourceDto(
      decision.Id, decision.SourceKind, decision.TrialBalanceDatasetId, decision.ImportBatchId,
      decision.SourceIdentityHash, decision.AcceptedByUserId, decision.CreatedAt, generation ?? 0));
  }
}
