using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

internal static class AuditCompletionProcedureEvidence
{
  internal static async Task<CommandResult<string>> CurrentAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid engagementId, Guid? procedureId, IReadOnlyList<string> evidenceReferences, CancellationToken ct)
  {
    var references = evidenceReferences.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    if (procedureId is { } requestedProcedureId && !await db.AuditProcedures.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == requestedProcedureId, ct))
      return CommandResult<string>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (references.Length == 0)
      return CommandResult<string>.Ok(Hashing.Sha256Hex(JsonSerializer.Serialize(new { ProcedureId = procedureId, References = references })));
    var proceduresQuery = db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.EngagementId == engagementId &&
      (procedureId == null || x.Id == procedureId));
    if (await proceduresQuery.CountAsync(ct) > 1000)
      return CommandResult<string>.Fail(ErrorCodes.GateBlocked, "The exact procedure evidence set exceeds the bounded currentness check.");
    var procedures = await proceduresQuery.ToListAsync(ct);
    var procedureIds = procedures.Select(x => x.Id).ToArray();
    if (procedureId is not null && procedureIds.Length == 0)
      return CommandResult<string>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var applicability = await AuditProgramService.CurrentApplicabilityDecisionsAsync(db, actor.FirmId, clientId,
      engagementId, procedures, ct);
    var results = procedureIds.Length == 0 ? [] : await db.AuditProcedureResults.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && procedureIds.Contains(x.AuditProcedureId))
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.Id).ToListAsync(ct);
    var supported = new HashSet<string>(StringComparer.Ordinal);
    var identities = new List<object>();
    foreach (var group in results.GroupBy(x => x.AuditProcedureId))
    {
      var latest = group.First();
      string[] resultReferences = [];
      try
      {
        resultReferences = JsonSerializer.Deserialize<string[]>(latest.EvidenceReferencesJson) ?? [];
      }
      catch (JsonException)
      {
        continue;
      }
      var matched = resultReferences.Select(x => x?.Trim()).Where(x => x is not null && references.Contains(x, StringComparer.Ordinal))
        .Distinct(StringComparer.Ordinal).Cast<string>().Order(StringComparer.Ordinal).ToArray();
      if (matched.Length == 0) continue;
      var procedure = procedures.Single(x => x.Id == latest.AuditProcedureId);
      var applicabilityDecision = applicability.GetValueOrDefault(procedure.Id);
      if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable ||
          procedure.Status is not (AuditProcedureStatuses.Submitted or AuditProcedureStatuses.Reviewed) ||
          procedure.CurrentApplicabilityRevision < 1 || procedure.CurrentApplicabilityDecisionId is null ||
          applicabilityDecision is null || applicabilityDecision.Id != procedure.CurrentApplicabilityDecisionId ||
          applicabilityDecision.Revision != procedure.CurrentApplicabilityRevision ||
          applicabilityDecision.Action != AuditApplicabilityActions.Applicable ||
          applicabilityDecision.DefinitionRevision != procedure.CurrentDefinitionRevision ||
          applicabilityDecision.InputGeneration != latest.InputGeneration) continue;
      var current = await AuditFieldworkService.EvaluateProcedureEvidenceForAuthorizedScopeAsync(db, actor.FirmId,
        clientId, engagementId, latest.AuditProcedureId, ct);
      var currentAndNotRejected = current.Satisfied || current.State == "MISSING_REVIEW";
      var submittedOrReviewed = latest.Status is AuditProcedureResultStatuses.Submitted or AuditProcedureResultStatuses.Reviewed;
      if (!currentAndNotRejected || !submittedOrReviewed || current.ResultId != latest.Id || current.Revision != latest.Revision ||
          current.InputGeneration != latest.InputGeneration) continue;
      foreach (var reference in matched) supported.Add(reference);
      identities.Add(new { latest.Id, latest.AuditProcedureId, latest.Revision, latest.InputGeneration,
        latest.DefinitionRevisionId, latest.Status, CurrentState = current.State, current.ReviewId,
        current.ReviewerUserId, procedure.ApplicabilityStatus, procedure.CurrentApplicabilityRevision,
        procedure.CurrentApplicabilityDecisionId, ApplicabilityAction = applicabilityDecision.Action,
        ApplicabilityDefinitionRevision = applicabilityDecision.DefinitionRevision,
        ApplicabilityInputGeneration = applicabilityDecision.InputGeneration, References = matched });
    }
    if (references.Any(x => !supported.Contains(x)))
      return CommandResult<string>.Fail(ErrorCodes.ScopeDenied,
        "One or more references are not supported by current, non-rejected procedure evidence in this exact engagement.");
    return CommandResult<string>.Ok(Hashing.Sha256Hex(JsonSerializer.Serialize(identities)));
  }

}
