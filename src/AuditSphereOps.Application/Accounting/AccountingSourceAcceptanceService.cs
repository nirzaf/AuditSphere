using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Records;
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
  Guid? TrialBalanceDatasetId, Guid? ImportBatchId, string EvidenceReference,
  string? ReviewRevision = null, bool Reviewed = false);

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
    if (kind == AccountingSourceKinds.TrialBalance && (request.TrialBalanceDatasetId is null || request.ImportBatchId is not null) ||
        kind == AccountingSourceKinds.GeneralLedger && (request.ImportBatchId is null || request.TrialBalanceDatasetId is not null))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected,
        "Accept exactly one source: a trial-balance dataset or a sealed GL batch.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, ReviewerRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // Serialize decisions and generation changes. Locks follow firm -> client -> engagement -> source/period.
    // A failed command rolls back its local publication, including a late authority change.
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var firm = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR SHARE").SingleOrDefaultAsync(ct);
    var safety = await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id = {actor.FirmId} AND id = {request.ClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id = {actor.FirmId} AND id = {request.EngagementId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (firm is null || safety is null || engagement is null || engagement.PracticeClientId != request.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, ReviewerRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId && x.State == FileFreezeStates.Frozen, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen. An approved amendment is required.");

    string sourceHash;
    Guid importer;
    Guid? periodId;
    if (kind == AccountingSourceKinds.TrialBalance)
    {
      if (request.TrialBalanceDatasetId is { } datasetId)
      {
        var dataset = await db.TrialBalanceDatasets.FromSqlInterpolated($"SELECT * FROM trial_balance_datasets WHERE firm_id = {actor.FirmId} AND id = {datasetId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == datasetId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
          x.EngagementId == request.EngagementId && x.SourceKind == "Raw" &&
          x.ImportState == TrialBalanceImportStates.Sealed && x.ValidationStatus == "Accepted" && x.Balanced, ct);
        if (dataset is null)
          return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
            "Only an accepted, sealed trial-balance revision can be accepted as the reporting source.");
        sourceHash = dataset.NormalizedDatasetDigest.Length == 64 ? dataset.NormalizedDatasetDigest : dataset.Sha256Hex;
        importer = dataset.ImportedByUserId; periodId = dataset.PeriodId;
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
        var batch = await db.SourceImportBatches.FromSqlInterpolated($"SELECT * FROM source_import_batches WHERE firm_id = {actor.FirmId} AND id = {batchId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == batchId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
          x.EngagementId == request.EngagementId && x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
        if (batch is null)
          return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
            "Only a sealed general-ledger batch can be accepted as the reporting source.");
        sourceHash = batch.NormalizedDatasetDigest;
        importer = batch.CreatedByUserId; periodId = batch.PeriodId;
        if (await db.SourceAcceptanceDecisions.AsNoTracking().AnyAsync(x =>
              x.FirmId == actor.FirmId && x.ImportBatchId == batchId && x.Decision == "ACCEPTED", ct))
          return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
            "This general-ledger batch was already accepted.");
      }
      else
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "A GL import batch id is required.");
    }

    if (importer == Guid.Empty || importer == actor.UserId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "A reviewer other than the source importer must accept this revision.");
    if (!SourceAcceptanceWorkspace.ValidHash(sourceHash))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The sealed source has no valid immutable identity digest.");
    if (periodId is { } period)
    {
      var currentPeriod = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id = {actor.FirmId} AND id = {period} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
      if (currentPeriod is null || currentPeriod.ClientId != request.ClientId || currentPeriod.Status != AccountingWorkflowStates.Active)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The source reporting period is unavailable or closed.");
    }
    if (request.ReviewRevision is not null)
    {
      if (kind != AccountingSourceKinds.TrialBalance || !request.Reviewed)
        return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Review the current source and selected pointer before accepting it.");
      var review = await SourceAcceptanceWorkspace.GetAsync(db, actor, request.TrialBalanceDatasetId!.Value, ct);
      if (!review.Succeeded) return CommandResult<Guid>.Fail(review.ErrorCode!, review.Message!);
      if (!review.Value!.CanAccept || review.Value.Revision != request.ReviewRevision)
        return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The source, selected pointer or authority changed. Refresh and review again.");
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

    await db.ClientSafetyStates.Where(x => x.Id == request.ClientId && x.FirmId == actor.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1), ct);
    await db.SaveChangesAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, ReviewerRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct);
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
    {
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, clientId, engagementId, InternalOnly: true), ct);
      return auth.Succeeded ? CommandResult<SelectedSourceDto?>.Ok(null) : CommandResult<SelectedSourceDto?>.Fail(auth.ErrorCode!, auth.Message!);
    }
    var generation = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.Id == clientId && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<SelectedSourceDto?>.Fail(auth.ErrorCode!, auth.Message!);
    if (generation is null) return CommandResult<SelectedSourceDto?>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<SelectedSourceDto?>.Ok(new SelectedSourceDto(
      decision.Id, decision.SourceKind, decision.TrialBalanceDatasetId, decision.ImportBatchId,
      decision.SourceIdentityHash, decision.AcceptedByUserId, decision.CreatedAt, generation.Value));
  }
}
