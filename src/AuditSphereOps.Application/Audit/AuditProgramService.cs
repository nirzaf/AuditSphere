using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record PublishAuditProgramRequest(string Version, string SourceHash);

public sealed record AuditProgramResult(
  Guid ProgramVersionId,
  string Version,
  string Status,
  int ProcedureCount);

public sealed record AdoptAuditProgramRequest(Guid EngagementId, Guid ProgramVersionId);

public sealed record EngagementAuditProgramResult(
  Guid EngagementProgramId,
  Guid ProgramVersionId,
  int ProcedureCount,
  string Status);

public sealed record DecideProcedureApplicabilityRequest(
  Guid AuditProcedureId,
  string Decision,
  string? Rationale);

public sealed record ProcedureApplicabilityResult(
  Guid AuditProcedureId,
  string SourceProcedureId,
  string Status,
  string ProcedureStatus);

public sealed record SubmitProcedureResultRequest(
  Guid AuditProcedureId,
  long ExpectedInputGeneration,
  string WorkPerformed,
  string StructuredResultJson,
  IReadOnlyList<string> EvidenceReferences,
  string Conclusion);

public sealed record ProcedureResultValue(
  Guid AuditProcedureResultId,
  Guid AuditProcedureId,
  Guid WorkpaperId,
  long Revision,
  string Status);

public sealed record ReviewProcedureResultRequest(
  Guid AuditProcedureResultId,
  string Decision,
  string? Comment);

public sealed record ProcedureReviewValue(
  Guid AuditProcedureResultId,
  Guid AuditProcedureId,
  long ResultRevision,
  string Decision);

/// <summary>
/// Shared program/adoption/procedure execution commands. Account areas use this path rather than
/// creating separate workflow engines. The source catalog is immutable; engagement rows are copies
/// that retain the exact adopted version and source wording.
/// </summary>
public static class AuditProgramService
{
  private static readonly string[] ProgramOwnerRoles = ["Partner", "Administrator"];
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private const string InvalidCode = ErrorCodes.AuditPlanning.Invalid;

  public static async Task<CommandResult<AuditProgramResult>> PublishAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    PublishAuditProgramRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);

    var role = Authorization.RequireRole(actor, ProgramOwnerRoles);
    if (!role.Succeeded)
      return CommandResult<AuditProgramResult>.Fail(role.ErrorCode!, role.Message!);
    if (!AuditProgramCatalog.IsComplete)
      return CommandResult<AuditProgramResult>.Fail(InvalidCode, "The controlled audit source catalog is incomplete.");
    if (string.IsNullOrWhiteSpace(request.Version) ||
        !string.Equals(request.SourceHash, AuditProgramCatalog.SourceHash, StringComparison.OrdinalIgnoreCase))
      return CommandResult<AuditProgramResult>.Fail(InvalidCode, "The program version or controlled source hash is invalid.");

    var version = request.Version.Trim();
    var existing = await db.AuditProgramVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ProgramCode == AuditProgramCatalog.ProgramCode && x.Version == version, ct);
    if (existing is not null)
    {
      return string.Equals(existing.SourceHash, request.SourceHash, StringComparison.OrdinalIgnoreCase) &&
             existing.Status == AuditProgramStatuses.Published
        ? CommandResult<AuditProgramResult>.Ok(new(existing.Id, existing.Version, existing.Status,
            await db.AuditProgramProcedures.CountAsync(x => x.FirmId == actor.FirmId && x.ProgramVersionId == existing.Id, ct)))
        : CommandResult<AuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
          "That program version already exists with different or unapproved content.");
    }

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var program = new AuditProgramVersion
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ProgramCode = AuditProgramCatalog.ProgramCode,
      Version = version,
      SourceHash = request.SourceHash.ToLowerInvariant(),
      Status = AuditProgramStatuses.Published,
      CreatedByUserId = actor.UserId,
      ApprovedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow,
      ApprovedAt = DateTimeOffset.UtcNow
    };
    db.AuditProgramVersions.Add(program);
    db.AuditProgramProcedures.AddRange(AuditProgramCatalog.Items.Select(item => new AuditProgramProcedure
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ProgramVersionId = program.Id,
      SourceProcedureId = item.SourceProcedureId,
      SectionNumber = item.SectionNumber,
      SectionTitle = item.SectionTitle,
      Ordinal = item.Ordinal,
      SourceWording = item.SourceWording,
      CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<AuditProgramResult>.Ok(new(program.Id, program.Version, program.Status, AuditProgramCatalog.Items.Count));
  }

  public static async Task<CommandResult<EngagementAuditProgramResult>> AdoptAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    AdoptAuditProgramRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);

    var invalid = Authorization.RequireRole(actor, PlanningRoles);
    if (!invalid.Succeeded)
      return CommandResult<EngagementAuditProgramResult>.Fail(invalid.ErrorCode!, invalid.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var scope = await ResolveEngagementAsync(db, actor, request.EngagementId, ct);
    if (scope.Denied is not null)
      return CommandResult<EngagementAuditProgramResult>.Fail(scope.Denied, scope.Message);

    var program = await db.AuditProgramVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.ProgramVersionId && x.FirmId == actor.FirmId &&
        x.Status == AuditProgramStatuses.Published, ct);
    if (program is null)
      return CommandResult<EngagementAuditProgramResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var existing = await db.EngagementAuditPrograms.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientId &&
        x.EngagementId == request.EngagementId && x.ProgramVersionId == program.Id, ct);
    if (existing is not null)
    {
      await tx.CommitAsync(ct);
      return CommandResult<EngagementAuditProgramResult>.Ok(new(existing.Id, existing.ProgramVersionId,
        await db.AuditProcedures.CountAsync(x => x.FirmId == scope.FirmId && x.EngagementId == request.EngagementId &&
          x.EngagementProgramId == existing.Id, ct), existing.Status));
    }

    var templates = await db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProgramVersionId == program.Id)
      .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
    if (templates.Count != AuditProgramCatalog.Items.Count)
      return CommandResult<EngagementAuditProgramResult>.Fail(InvalidCode,
        "The published program does not contain the complete controlled catalog.");

    var adoption = new EngagementAuditProgram
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = request.EngagementId,
      ProgramVersionId = program.Id,
      Status = EngagementAuditProgramStatuses.Adopted,
      AdoptedByUserId = actor.UserId,
      AdoptedAt = DateTimeOffset.UtcNow
    };
    db.EngagementAuditPrograms.Add(adoption);
    db.AuditProcedures.AddRange(templates.Select(template => new AuditProcedure
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = request.EngagementId,
      EngagementProgramId = adoption.Id,
      ProgramProcedureId = template.Id,
      RiskId = null,
      SourceProcedureId = template.SourceProcedureId,
      SourceSectionNumber = template.SectionNumber,
      SourceSectionTitle = template.SectionTitle,
      SourceWording = template.SourceWording,
      ApplicabilityStatus = AuditApplicabilityStatuses.Pending,
      CurrentResultRevision = 0,
      Title = template.SourceWording,
      Status = AuditProcedureStatuses.Planned,
      CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<EngagementAuditProgramResult>.Ok(new(adoption.Id, adoption.ProgramVersionId,
      templates.Count, adoption.Status));
  }

  public static async Task<CommandResult<ProcedureApplicabilityResult>> DecideApplicabilityAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    DecideProcedureApplicabilityRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditApplicabilityStatuses.Applicable or
      AuditApplicabilityStatuses.NotApplicablePendingReview or AuditApplicabilityStatuses.NotApplicableApproved))
      return CommandResult<ProcedureApplicabilityResult>.Fail(InvalidCode, "Unsupported applicability decision.");
    if (decision is AuditApplicabilityStatuses.NotApplicablePendingReview or AuditApplicabilityStatuses.NotApplicableApproved &&
        string.IsNullOrWhiteSpace(request.Rationale))
      return CommandResult<ProcedureApplicabilityResult>.Fail(InvalidCode, "A not-applicable rationale is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.AuditProcedures.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.AuditProcedureId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId,
      decision == AuditApplicabilityStatuses.NotApplicableApproved ? ReviewRoles : PlanningRoles);
    if (scope.Denied is not null)
      return CommandResult<ProcedureApplicabilityResult>.Fail(scope.Denied, scope.Message);

    if (decision == AuditApplicabilityStatuses.NotApplicableApproved &&
        !actor.Roles.Any(x => ReviewRoles.Contains(x, StringComparer.OrdinalIgnoreCase)))
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "A reviewer must approve not-applicable status.");

    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id = {request.AuditProcedureId} AND firm_id = {actor.FirmId}
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult<ProcedureApplicabilityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    procedure.ApplicabilityStatus = decision;
    procedure.ApplicabilityRationale = string.IsNullOrWhiteSpace(request.Rationale) ? null : request.Rationale.Trim();
    procedure.ApplicabilityDecidedByUserId = actor.UserId;
    procedure.ApplicabilityDecidedAt = DateTimeOffset.UtcNow;
    procedure.Status = decision == AuditApplicabilityStatuses.Applicable
      ? AuditProcedureStatuses.InProgress : AuditProcedureStatuses.Planned;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ProcedureApplicabilityResult>.Ok(new(procedure.Id, procedure.SourceProcedureId,
      procedure.ApplicabilityStatus, procedure.Status));
  }

  public static async Task<CommandResult<ProcedureResultValue>> SubmitResultAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    SubmitProcedureResultRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    if (request.ExpectedInputGeneration < 1 || string.IsNullOrWhiteSpace(request.WorkPerformed) ||
        string.IsNullOrWhiteSpace(request.Conclusion) || request.EvidenceReferences is null)
      return CommandResult<ProcedureResultValue>.Fail(InvalidCode, "A generation, work performed, evidence and conclusion are required.");
    if (!TryValidateObject(request.StructuredResultJson))
      return CommandResult<ProcedureResultValue>.Fail(InvalidCode, "Structured result must be a JSON object.");
    if (request.EvidenceReferences.Any(string.IsNullOrWhiteSpace))
      return CommandResult<ProcedureResultValue>.Fail(InvalidCode, "Evidence references cannot be blank.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.AuditProcedures.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.AuditProcedureId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
    if (scope.Denied is not null)
      return CommandResult<ProcedureResultValue>.Fail(scope.Denied, scope.Message);
    if (snapshot.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GateBlocked,
        "Only an explicitly applicable procedure can receive test results.");

    var generation = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.Id == scope.ClientId)
      .Select(x => x.InputGeneration).SingleOrDefaultAsync(ct);
    generation = generation < 1 ? 1 : generation;
    if (generation != request.ExpectedInputGeneration)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GenerationStale,
        "The underlying engagement inputs changed.");

    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id = {request.AuditProcedureId} AND firm_id = {actor.FirmId}
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var latest = await db.AuditProcedureResults.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.AuditProcedureId == procedure.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (latest is not null && latest.Status == AuditProcedureResultStatuses.Submitted)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ProtectedState,
        "The current result is awaiting review; create a new revision only after review.");

    var workpaper = new Workpaper
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = procedure.EngagementId,
      ProcedureId = procedure.Id,
      ActorId = actor.UserId,
      Index = procedure.SourceProcedureId,
      Title = procedure.Title,
      Objective = $"Execute {procedure.SourceProcedureId}",
      TemplateVersion = procedure.SourceProcedureId,
      Procedure = procedure.SourceWording ?? procedure.Title,
      WorkPerformed = request.WorkPerformed.Trim(),
      Conclusion = request.Conclusion.Trim(),
      Revision = 1,
      Status = WorkpaperStatuses.SubmittedSnapshot,
      SubmittedAt = DateTimeOffset.UtcNow,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Workpapers.Add(workpaper);
    var revision = (latest?.Revision ?? 0) + 1;
    var result = new AuditProcedureResult
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = procedure.EngagementId,
      AuditProcedureId = procedure.Id,
      WorkpaperId = workpaper.Id,
      Revision = revision,
      InputGeneration = generation,
      WorkPerformed = request.WorkPerformed.Trim(),
      StructuredResultJson = request.StructuredResultJson.Trim(),
      EvidenceReferencesJson = JsonSerializer.Serialize(request.EvidenceReferences),
      Conclusion = request.Conclusion.Trim(),
      Status = AuditProcedureResultStatuses.Submitted,
      PreparedByUserId = actor.UserId,
      SubmittedAt = DateTimeOffset.UtcNow
    };
    db.AuditProcedureResults.Add(result);
    db.WorkpaperSubmissions.Add(new WorkpaperSubmission
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = procedure.EngagementId,
      WorkpaperId = workpaper.Id,
      ActorId = actor.UserId,
      Revision = workpaper.Revision,
      WorkPerformed = workpaper.WorkPerformed,
      Conclusion = workpaper.Conclusion,
      SubmittedAt = workpaper.SubmittedAt!.Value
    });
    procedure.CurrentResultRevision = revision;
    procedure.Status = AuditProcedureStatuses.Submitted;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ProcedureResultValue>.Ok(new(result.Id, procedure.Id, workpaper.Id, revision, result.Status));
  }

  public static async Task<CommandResult<ProcedureReviewValue>> ReviewResultAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    ReviewProcedureResultRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    var decision = request.Decision.Trim().ToUpperInvariant();
    if (decision is not (AuditProcedureReviewDecisions.Reviewed or AuditProcedureReviewDecisions.ChangesRequired))
      return CommandResult<ProcedureReviewValue>.Fail(InvalidCode, "Unsupported review decision.");
    if (decision == AuditProcedureReviewDecisions.ChangesRequired && string.IsNullOrWhiteSpace(request.Comment))
      return CommandResult<ProcedureReviewValue>.Fail(InvalidCode, "A changes-required review must include a comment.");
    var role = Authorization.RequireRole(actor, ReviewRoles);
    if (!role.Succeeded)
      return CommandResult<ProcedureReviewValue>.Fail(role.ErrorCode!, role.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var result = await db.AuditProcedureResults
      .SingleOrDefaultAsync(x => x.Id == request.AuditProcedureResultId && x.FirmId == actor.FirmId, ct);
    if (result is null)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (result.PreparedByUserId == actor.UserId)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same result.");
    var scope = await ResolveEngagementAsync(db, actor, result.EngagementId, ct, result.ClientId, ReviewRoles);
    if (scope.Denied is not null)
      return CommandResult<ProcedureReviewValue>.Fail(scope.Denied, scope.Message);
    var generation = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.Id == scope.ClientId)
      .Select(x => x.InputGeneration).SingleOrDefaultAsync(ct);
    generation = generation < 1 ? 1 : generation;
    if (result.InputGeneration != generation)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GenerationStale,
        "The underlying engagement inputs changed; the result must be resubmitted.");
    if (result.Status != AuditProcedureResultStatuses.Submitted)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ProtectedState,
        "Only the current submitted result can receive a review decision.");
    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id = {result.AuditProcedureId} AND firm_id = {actor.FirmId}
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    db.AuditProcedureReviews.Add(new AuditProcedureReview
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = result.EngagementId,
      AuditProcedureResultId = result.Id,
      AuditProcedureId = procedure.Id,
      ResultRevision = result.Revision,
      Decision = decision,
      Comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim(),
      ReviewerUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    result.Status = decision == AuditProcedureReviewDecisions.Reviewed
      ? AuditProcedureResultStatuses.Reviewed : AuditProcedureResultStatuses.ChangesRequired;
    result.ReviewedByUserId = actor.UserId;
    result.ReviewComment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
    result.ReviewedAt = DateTimeOffset.UtcNow;
    procedure.Status = decision == AuditProcedureReviewDecisions.Reviewed
      ? AuditProcedureStatuses.Reviewed : AuditProcedureStatuses.ChangesRequired;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ProcedureReviewValue>.Ok(new(result.Id, result.AuditProcedureId, result.Revision, decision));
  }

  private sealed record Scope(Guid FirmId, Guid ClientId, string? Denied, string Message = "Access denied.");

  private static async Task<Scope> ResolveEngagementAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid engagementId,
    CancellationToken ct,
    Guid? expectedClientId = null,
    IReadOnlyList<string>? roles = null)
  {
    var engagement = await db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null || (expectedClientId is not null && engagement.PracticeClientId != expectedClientId))
      return new(Guid.Empty, Guid.Empty, ErrorCodes.ScopeDenied);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
        (roles ?? PlanningRoles).ToArray(), InternalOnly: true, RequireProfessionalWork: true), ct);
    return auth.Succeeded
      ? new(engagement.FirmId, engagement.PracticeClientId, null)
      : new(engagement.FirmId, engagement.PracticeClientId, auth.ErrorCode, auth.Message ?? "Access denied.");
  }

  private static bool TryValidateObject(string json)
  {
    if (string.IsNullOrWhiteSpace(json))
      return false;
    try
    {
      using var document = JsonDocument.Parse(json);
      return document.RootElement.ValueKind == JsonValueKind.Object;
    }
    catch (JsonException)
    {
      return false;
    }
  }
}
