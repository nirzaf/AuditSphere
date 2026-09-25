using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  public static async Task<CommandResult<AreaAssessmentValue>> RecordAreaAssessmentAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordAreaAssessmentRequest request, CancellationToken ct = default)
  {
    var areaCode = request.AreaCode.Trim().ToUpperInvariant();
    if (!AuditAreaCodes.All.Contains(areaCode) || string.IsNullOrWhiteSpace(request.AssessmentKind) ||
        string.IsNullOrWhiteSpace(request.MethodologyReference) || !JsonObject(request.InputSnapshotJson) ||
        request.EvidenceReferences is null || request.EvidenceReferences.Count == 0 || request.EvidenceReferences.Any(string.IsNullOrWhiteSpace) ||
        string.IsNullOrWhiteSpace(request.Conclusion) || request.Currency is not null && !IsCurrency(request.Currency))
      return Invalid<AreaAssessmentValue>("A typed area assessment requires an approved method, object inputs, evidence and a conclusion.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<AreaAssessmentValue>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.ProcedureId is not null && !await IsApplicableProcedureAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, request.ProcedureId.Value, ct))
      return CommandResult<AreaAssessmentValue>.Fail(ErrorCodes.GateBlocked, "The linked procedure is not applicable.");
    if (request.Currency is not null && !IsCurrency(request.Currency))
      return Invalid<AreaAssessmentValue>("Currency must be ISO 4217.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var generation = await CurrentGenerationAsync(db, auth.ClientId, actor.FirmId, ct);
    var inputSnapshot = request.InputSnapshotJson.Trim();
    if (areaCode == AuditAreaCodes.AuditDifferences)
    {
      if (request.AssessmentKind.Trim() != AuditAreaAssessmentKinds.AggregateDifferences)
        return Invalid<AreaAssessmentValue>("Audit-difference assessments must use the aggregate-differences workflow.");
      inputSnapshot = await CurrentDifferenceSnapshotAsync(db, actor.FirmId, auth.ClientId, request.EngagementId, ct)
        ?? string.Empty;
      if (inputSnapshot.Length == 0)
        return CommandResult<AreaAssessmentValue>.Fail(ErrorCodes.GateBlocked,
          "An independently approved materiality assessment is required before aggregate evaluation.");
    }
    var assessment = new AuditAreaAssessment
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId,
      ProcedureId = request.ProcedureId, AreaCode = request.AreaCode.Trim().ToUpperInvariant(), AssessmentKind = request.AssessmentKind.Trim(),
      MethodologyReference = request.MethodologyReference.Trim(), InputSnapshotJson = inputSnapshot,
      BookedAmount = request.BookedAmount, AuditedAmount = request.AuditedAmount, ResidualAmount = request.ResidualAmount,
      VariancePercent = request.VariancePercent, Currency = request.Currency?.ToUpperInvariant(), PeriodStart = request.PeriodStart,
      PeriodEnd = request.PeriodEnd, EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences), InputGeneration = generation,
      Conclusion = request.Conclusion.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditAreaAssessments.Add(assessment);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<AreaAssessmentValue>.Ok(new(assessment.Id, assessment.Status));
  }

  public static async Task<CommandResult> ReviewAreaAssessmentAsync(
    IAuditSphereDbContext db, ActorContext actor, ReviewAreaAssessmentRequest request, CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditAreaAssessmentStatuses.Reviewed or AuditAreaAssessmentStatuses.ChangesRequired) ||
        decision == AuditAreaAssessmentStatuses.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Area assessment review requires a valid decision and comment for changes.");
    var existing = await db.AuditAreaAssessments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.AuditAreaAssessmentId && x.FirmId == actor.FirmId, ct);
    var auth = await AuthorizeEntityAsync(db, actor, existing, ReviewRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (existing!.InputGeneration != await CurrentGenerationAsync(db, existing.ClientId, existing.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The assessment inputs changed; review the new revision.");
    if (existing.AreaCode == AuditAreaCodes.AuditDifferences &&
        (existing.AssessmentKind != AuditAreaAssessmentKinds.AggregateDifferences ||
         existing.InputSnapshotJson != await CurrentDifferenceSnapshotAsync(db, existing.FirmId, existing.ClientId,
           existing.EngagementId, ct)))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The aggregate difference schedule or approved materiality changed; prepare a new assessment.");
    if (existing.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same assessment.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var live = await db.AuditAreaAssessments.SingleAsync(x => x.Id == existing.Id && x.FirmId == actor.FirmId, ct);
    live.Status = decision;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
