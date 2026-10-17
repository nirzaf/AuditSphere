using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>Exact procedure submit/review preview, receipt and original-outcome recovery.</summary>
public static class AuditProcedureCommandWorkspace
{
  private static readonly string[] ReviewRoles = ["Reviewer", "Senior", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReadRoles = ["Reviewer", "Senior", "Manager", "Partner", "Administrator", "Staff"];
  private const int MaximumWork = 100000;
  private const int MaximumConclusion = 20000;
  private const int MaximumComment = 20000;
  private const int MaximumEvidence = 20000;
  private const int MaximumEvidenceItems = 200;

  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Text(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max && !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));

  private static ProcedureCommandRequest? Canonical(ProcedureCommandRequest? request)
  {
    if (request is null || request.RequestId == Guid.Empty || request.ExpectedDefinitionRevision < 0 ||
        request.ExpectedInputGeneration < 1 || request.ExpectedResultRevision < 0) return null;
    var action = request.Action?.Trim().ToUpperInvariant();
    if (action == ProcedureCommandActions.SubmitResult)
    {
      if (request.ResultId is not null || request.Decision is not null || request.Comment is not null ||
          !Text(request.WorkPerformed, MaximumWork) || !Text(request.StructuredResultJson, ProcedureRiskBandEvaluator.MaximumStructuredResultBytes) ||
          !Text(request.Conclusion, MaximumConclusion) || request.EvidenceReferences is null ||
          request.EvidenceReferences.Count > MaximumEvidenceItems ||
          request.EvidenceReferences.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > MaximumEvidence) ||
          !TryValidateObject(request.StructuredResultJson!))
        return null;
      var selections = CanonicalSelections(request.EvidenceSelections);
      if (selections is null) return null;
      return request with
      {
        Action = action, WorkPerformed = request.WorkPerformed!.Trim(), StructuredResultJson = request.StructuredResultJson!.Trim(),
        EvidenceReferences = request.EvidenceReferences.Select(x => x.Trim()).ToArray(), Conclusion = request.Conclusion!.Trim(),
        EvidenceSelections = selections
      };
    }
    if (action == ProcedureCommandActions.ReviewResult)
    {
      if (request.ResultId is null || request.ResultId == Guid.Empty || request.WorkPerformed is not null ||
          request.StructuredResultJson is not null || request.EvidenceReferences is not null || request.Conclusion is not null ||
          !Text(request.Decision, 30) || request.Comment is { Length: > MaximumComment } ||
          request.Comment?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true)
        return null;
      var decision = request.Decision!.Trim().ToUpperInvariant();
      if (decision is not (AuditProcedureReviewDecisions.Reviewed or AuditProcedureReviewDecisions.ChangesRequired) ||
          decision == AuditProcedureReviewDecisions.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
        return null;
      return request with { Action = action, Decision = decision, Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim() };
    }
    return null;
  }

  private static IReadOnlyList<ProcedureEvidenceSelection>? CanonicalSelections(
    IReadOnlyList<ProcedureEvidenceSelection>? values)
  {
    var list = values ?? [];
    if (list.Count > 50) return null;
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var result = new List<ProcedureEvidenceSelection>();
    foreach (var selection in list)
    {
      if (selection is null) return null;
      var kind = selection.Kind?.Trim().ToUpperInvariant();
      if (kind == ProcedureEvidenceSelectionKinds.StoredLink &&
          selection.LinkId is { } linkId && linkId != Guid.Empty &&
          selection.PhysicalLinkId is null && selection.ManualReference is null && selection.Observation is null &&
          seen.Add("L:" + linkId))
        result.Add(new(ProcedureEvidenceSelectionKinds.StoredLink, LinkId: linkId));
      else if (kind == ProcedureEvidenceSelectionKinds.PhysicalLink &&
               selection.PhysicalLinkId is { } physicalLinkId && physicalLinkId != Guid.Empty &&
               selection.LinkId is null && selection.ManualReference is null && selection.Observation is null &&
               seen.Add("P:" + physicalLinkId))
        result.Add(new(ProcedureEvidenceSelectionKinds.PhysicalLink, PhysicalLinkId: physicalLinkId));
      else if (kind == ProcedureEvidenceSelectionKinds.ManualReference &&
               Text(selection.ManualReference, 500) && Text(selection.Observation, 4000) &&
               selection.LinkId is null && selection.PhysicalLinkId is null &&
               seen.Add("M:" + selection.ManualReference!.Trim() + "|" + selection.Observation!.Trim()))
        result.Add(new(ProcedureEvidenceSelectionKinds.ManualReference,
          ManualReference: selection.ManualReference!.Trim(), Observation: selection.Observation!.Trim()));
      else return null;
    }
    return result;
  }

  private static bool TryValidateObject(string json)
  {
    if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > ProcedureRiskBandEvaluator.MaximumStructuredResultBytes) return false;
    try
    {
      using var document = JsonDocument.Parse(json);
      return document.RootElement.ValueKind == JsonValueKind.Object && !ProcedureRiskBandEvaluator.HasReservedBasisProperty(json);
    }
    catch (JsonException) { return false; }
  }

  private static string RequestHash(ActorContext actor, Guid procedureId, ProcedureCommandRequest request) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch, ProcedureId = procedureId, request.RequestId, request.Action,
      request.ExpectedDefinitionRevision, request.ExpectedInputGeneration, request.ExpectedResultRevision, request.ResultId,
      request.WorkPerformed, request.StructuredResultJson, request.EvidenceReferences, request.Conclusion, request.Decision, request.Comment,
      request.EvidenceSelections
    }));

  private static async Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, AuditProcedure procedure,
    string action, bool write, CancellationToken ct)
  {
    var roles = action == ProcedureCommandActions.ReviewResult ? ReviewRoles :
      write ? AuditPlanningService.PlanningRoles : ReadRoles;
    return await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, procedure.ClientId, procedure.EngagementId, roles, InternalOnly: true, RequireProfessionalWork: write), ct);
  }

  internal static async Task BarrierAsync(IAuditSphereDbContext db, ActorContext actor, Guid requestId, bool write, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "audit-procedure-command-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (write)
      await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else
      await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  private static async Task<CommandResult<ProcedureCommandBasis>> BasisAsync(IAuditSphereDbContext db, ActorContext actor,
    AuditProcedure procedure, CancellationToken ct)
  {
    var definition = procedure.ProgramProcedureId is null ? null : await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == procedure.FirmId && x.ClientId == procedure.ClientId && x.EngagementId == procedure.EngagementId &&
      x.AuditProcedureId == procedure.Id && x.Revision == procedure.CurrentDefinitionRevision, ct);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == procedure.ClientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 0;
    var latest = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.AuditProcedureId == procedure.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    var review = latest is null ? null : await db.AuditProcedureReviews.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.AuditProcedureResultId == latest.Id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var openNotes = await ReviewNotesService.OpenCountAsync(db, actor.FirmId, procedure.Id, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == procedure.EngagementId && x.State == AuditSphereOps.Domain.Records.FileFreezeStates.Frozen, ct);
    var submitBlockers = new List<string>();
    if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable) submitBlockers.Add("The procedure is not currently applicable.");
    if (procedure.ProgramProcedureId is not null && definition is null) submitBlockers.Add("The controlled procedure has no verified current definition.");
    if (generation < 1) submitBlockers.Add("Current client input generation is unavailable.");
    if (frozen) submitBlockers.Add("The engagement file is frozen.");
    if (latest is { Status: AuditProcedureResultStatuses.Submitted } && latest.InputGeneration == generation &&
        latest.DefinitionRevisionId == definition?.Id) submitBlockers.Add("The current result is already awaiting review.");
    var reviewBlockers = new List<string>();
    if (latest is null) reviewBlockers.Add("No submitted procedure result is available.");
    else
    {
      if (latest.Status != AuditProcedureResultStatuses.Submitted) reviewBlockers.Add("The latest procedure result is not awaiting review.");
      if (latest.PreparedByUserId == actor.UserId) reviewBlockers.Add("The preparer cannot review the same result.");
      if (latest.InputGeneration != generation) reviewBlockers.Add("The latest result is stale against current client inputs.");
      if (procedure.ProgramProcedureId is not null && latest.DefinitionRevisionId != definition?.Id) reviewBlockers.Add("The latest result uses an older procedure definition.");
      if (openNotes > 0) reviewBlockers.Add($"{openNotes} review note(s) remain open.");
    }
    if (frozen) reviewBlockers.Add("The engagement file is frozen.");
    var basis = new ProcedureCommandBasis(procedure.Id, procedure.SourceProcedureId, procedure.Title, procedure.ApplicabilityStatus,
      procedure.Status, procedure.CurrentDefinitionRevision, generation, procedure.CurrentResultRevision,
      latest?.Id, latest?.Revision ?? 0, latest?.Status, review?.Id, review?.Decision,
      submitBlockers.Count == 0, reviewBlockers.Count == 0, submitBlockers.Concat(reviewBlockers).Distinct().ToArray());
    return CommandResult<ProcedureCommandBasis>.Ok(basis);
  }

  public static async Task<CommandResult<ProcedureCommandState>> StateAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId,
    CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == procedureId, ct);
    if (procedure is null) return Fail<ProcedureCommandState>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, procedure, ProcedureCommandActions.SubmitResult, false, ct);
    if (!auth.Succeeded) return Fail<ProcedureCommandState>(auth.ErrorCode!, auth.Message!);
    var basis = await BasisAsync(db, actor, procedure, ct);
    if (!basis.Succeeded) return Fail<ProcedureCommandState>(basis.ErrorCode!, basis.Message!);
    var hash = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch, Basis = basis.Value }));
    return CommandResult<ProcedureCommandState>.Ok(new(basis.Value!, hash));
  }

  public static async Task<CommandResult<ProcedureCommandPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid procedureId, ProcedureCommandRequest? request, CancellationToken ct = default)
  {
    var canonical = Canonical(request);
    if (canonical is null) return Fail<ProcedureCommandPreview>(ErrorCodes.AuditPlanning.Invalid, "Provide one exact bounded procedure action.");
    var state = await StateAsync(db, actor, procedureId, ct);
    if (!state.Succeeded) return Fail<ProcedureCommandPreview>(state.ErrorCode!, state.Message!);
    var basis = state.Value!.Basis;
    var stale = canonical.ExpectedDefinitionRevision != basis.DefinitionRevision ||
      canonical.ExpectedInputGeneration != basis.InputGeneration ||
      (canonical.Action == ProcedureCommandActions.SubmitResult
        ? canonical.ExpectedResultRevision != basis.CurrentResultRevision
        : canonical.ExpectedResultRevision != basis.LatestResultRevision || canonical.ResultId != basis.LatestResultId);
    if (stale) return Fail<ProcedureCommandPreview>(ErrorCodes.GenerationStale, "The procedure basis changed; reload its current state and review again.");
    var proper = canonical.Action == ProcedureCommandActions.SubmitResult ? basis.CanSubmit : basis.CanReview;
    var blockers = proper ? Array.Empty<string>() : basis.Blockers.ToArray();
    return CommandResult<ProcedureCommandPreview>.Ok(new(canonical.RequestId, RequestHash(actor, procedureId, canonical),
      canonical.Action, basis, proper, blockers));
  }

  public static async Task<CommandResult<ProcedureCommandLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid procedureId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash))
      return Fail<ProcedureCommandLookup>(ErrorCodes.AuditPlanning.Invalid, "Use the exact retained request reference.");
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == procedureId, ct);
    if (procedure is null) return Fail<ProcedureCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, procedure, ProcedureCommandActions.SubmitResult, false, ct);
    if (!auth.Succeeded) return Fail<ProcedureCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await BarrierAsync(db, actor, requestId, false, ct);
    var receipt = await db.AuditProcedureCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.RequestId == requestId, ct);
    if (receipt is not null && (receipt.AuditProcedureId != procedureId || receipt.RequestHash != requestHash))
      return Fail<ProcedureCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    if (receipt is not null && receipt.ActorEpoch != actor.SessionEpoch)
      return Fail<ProcedureCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    await tx.CommitAsync(ct);
    return CommandResult<ProcedureCommandLookup>.Ok(new(receipt is not null, receipt is null ? null : View(receipt)));
  }

  public static async Task<CommandResult<ProcedureCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid procedureId, ProcedureCommandRequest? request, bool createWorkflowReworkTask = false, CancellationToken ct = default)
  {
    var canonical = Canonical(request);
    if (canonical is null) return Fail<ProcedureCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "Provide one exact bounded procedure action.");
    if (!request!.Reviewed || !Hash(request.ExpectedRequestHash))
      return Fail<ProcedureCommandReceiptView>(ErrorCodes.AuditPlanning.Invalid, "Review the exact preview and record fresh assent before dispatching.");
    var state = await StateAsync(db, actor, procedureId, ct);
    if (!state.Succeeded) return Fail<ProcedureCommandReceiptView>(state.ErrorCode!, state.Message!);
    var basis = state.Value!.Basis;
    var expected = RequestHash(actor, procedureId, canonical);
    if (request.ExpectedRequestHash != expected)
      return Fail<ProcedureCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The reviewed intent differs from the original preview.");
    // A retained receipt proves the original outcome even when the current basis has since moved.
    var existing = await db.AuditProcedureCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.RequestId == canonical.RequestId, ct);
    if (existing is not null)
    {
      if (existing.ActorEpoch != actor.SessionEpoch)
        return Fail<ProcedureCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
      if (existing.AuditProcedureId != procedureId || existing.Action != canonical.Action || existing.RequestHash != expected)
        return Fail<ProcedureCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      return CommandResult<ProcedureCommandReceiptView>.Ok(View(existing));
    }
    var stale = canonical.Action == ProcedureCommandActions.SubmitResult
      ? canonical.ExpectedDefinitionRevision != basis.DefinitionRevision || canonical.ExpectedInputGeneration != basis.InputGeneration ||
        canonical.ExpectedResultRevision != basis.CurrentResultRevision
      : canonical.ExpectedDefinitionRevision != basis.DefinitionRevision || canonical.ExpectedInputGeneration != basis.InputGeneration ||
        canonical.ExpectedResultRevision != basis.LatestResultRevision || canonical.ResultId != basis.LatestResultId;
    if (stale) return Fail<ProcedureCommandReceiptView>(ErrorCodes.GenerationStale, "The procedure basis changed; reload and review again.");
    var command = new ProcedureCommandContext(canonical.RequestId, canonical.Action, expected, actor.SessionEpoch,
      canonical.ExpectedDefinitionRevision, canonical.ExpectedResultRevision, true);
    CommandResult result;
    if (canonical.Action == ProcedureCommandActions.SubmitResult)
    {
      var submitted = await AuditProgramService.SubmitResultAsync(db, actor,
        new SubmitProcedureResultRequest(procedureId, canonical.ExpectedInputGeneration, canonical.WorkPerformed!,
          canonical.StructuredResultJson!, canonical.EvidenceReferences!, canonical.Conclusion!,
          canonical.EvidenceSelections), ct, command);
      result = submitted.Succeeded ? CommandResult.Ok() : CommandResult.Fail(submitted.ErrorCode!, submitted.Message!);
    }
    else
    {
      var reviewed = await AuditProgramService.ReviewResultAsync(db, actor,
        new ReviewProcedureResultRequest(canonical.ResultId!.Value, canonical.Decision!, canonical.Comment), ct,
        createWorkflowReworkTask, command);
      result = reviewed.Succeeded ? CommandResult.Ok() : CommandResult.Fail(reviewed.ErrorCode!, reviewed.Message!);
    }
    if (!result.Succeeded) return Fail<ProcedureCommandReceiptView>(result.ErrorCode!, result.Message!);
    var receipt = await db.AuditProcedureCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorUserId == actor.UserId && x.RequestId == canonical.RequestId, ct);
    return receipt is null
      ? Fail<ProcedureCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The original outcome receipt could not be recovered.")
      : CommandResult<ProcedureCommandReceiptView>.Ok(View(receipt));
  }

  internal static ProcedureCommandReceiptView View(AuditProcedureCommandReceipt receipt) => new(
    receipt.Id, receipt.AuditProcedureId, receipt.SourceProcedureId, receipt.Action, receipt.ActorUserId, receipt.ActorEpoch,
    receipt.RequestId, receipt.RequestHash, receipt.DefinitionRevision, receipt.InputGeneration, receipt.ExpectedResultRevision,
    receipt.ResultId, receipt.WorkpaperId, receipt.ReviewId, receipt.ResultRevision, receipt.ResultStatus, receipt.Decision,
    receipt.EvidenceDigest, receipt.CommittedAt.ToUniversalTime());
}
