using System.Text.Json;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record PrepareAuditProgramRequest(string Version, string SourceHash);
public sealed record ReviewAuditProgramPublicationRequest(Guid ProgramVersionId, string ExpectedContentDigest,
  string PublicationReviewReference);

public sealed record AuditProgramResult(
  Guid ProgramVersionId,
  string Version,
  string Status,
  string SourceHash,
  string? ContentDigest,
  int ProcedureCount,
  Guid CreatedByUserId,
  Guid? ApprovedByUserId,
  string? PublicationReviewReference,
  DateTimeOffset CreatedAt,
  DateTimeOffset? ApprovedAt);

public sealed record AdoptAuditProgramRequest(Guid EngagementId, Guid ProgramVersionId,
  string? ExpectedSourceHash = null, string? ExpectedContentDigest = null, Guid? ExpectedAdoptionId = null);

public sealed record EngagementAuditProgramResult(
  Guid EngagementProgramId,
  Guid ProgramVersionId,
  int ProcedureCount,
  string Status,
  string SourceHash,
  string? ContentDigest,
  int BaselineProcedureCount,
  int AdHocProcedureCount);

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

public sealed record TailorProcedureRequest(
  Guid AuditProcedureId,
  string Title,
  string CustomWording,
  string? Rationale,
  int ExpectedDefinitionRevision = 0,
  Guid CommandId = default);

/// <summary>
/// Shared program/adoption/procedure execution commands. Account areas use this path rather than
/// creating separate workflow engines. The source catalog is immutable; engagement rows are copies
/// that retain the exact adopted version and source wording.
/// </summary>
public static partial class AuditProgramService
{
  private static readonly string[] ProgramOwnerRoles = ["Partner", "Administrator"];
  private static readonly string[] PlanningRoles =
    ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Senior", "Manager", "Partner", "Administrator"];
  private const string InvalidCode = ErrorCodes.AuditPlanning.Invalid;

  public static async Task<CommandResult<AuditProgramResult>> PrepareControlledAuditProgramAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    PrepareAuditProgramRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    if (!AuditProgramCatalog.IsComplete)
      return CommandResult<AuditProgramResult>.Fail(InvalidCode, "The controlled audit source catalog is incomplete.");
    if (string.IsNullOrWhiteSpace(request.Version) ||
        !string.Equals(request.SourceHash, AuditProgramCatalog.SourceHash, StringComparison.OrdinalIgnoreCase))
      return CommandResult<AuditProgramResult>.Fail(InvalidCode, "The program version or controlled source hash is invalid.");

    var version = request.Version.Trim();
    if (version.Length > 100)
      return CommandResult<AuditProgramResult>.Fail(InvalidCode, "The program version is invalid.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockProgramBoundaryAsync(db, actor.FirmId, version, ct);
    await LockActorGrantsAsync(db, actor, ct);
    var authorization = await AuthorizeProgramOwnerAsync(db, actor, ct);
    if (!authorization.Succeeded)
      return CommandResult<AuditProgramResult>.Fail(authorization.ErrorCode!, authorization.Message!);

    var existing = await db.AuditProgramVersions
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.ProgramCode == AuditProgramCatalog.ProgramCode && x.Version == version, ct);
    if (existing is not null)
    {
      var matches = string.Equals(existing.SourceHash, request.SourceHash, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.ContentDigest, AuditProgramCatalog.ContentDigest, StringComparison.OrdinalIgnoreCase) &&
        existing.Status is AuditProgramStatuses.Draft or AuditProgramStatuses.Published;
      if (!matches)
        return CommandResult<AuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
          "That program version already exists with different or legacy-unverified content.");
      var retained = await db.AuditProgramProcedures.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ProgramVersionId == existing.Id)
        .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
      if (!MatchesControlledCatalog(retained))
        return CommandResult<AuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
          "The retained program version differs from the controlled source and requires controlled reconciliation.");
      await tx.CommitAsync(ct);
      return CommandResult<AuditProgramResult>.Ok(ToProgramResult(existing, retained.Count));
    }

    var now = DateTimeOffset.UtcNow;
    var program = new AuditProgramVersion
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ProgramCode = AuditProgramCatalog.ProgramCode,
      Version = version,
      SourceHash = request.SourceHash.ToLowerInvariant(),
      ContentDigest = AuditProgramCatalog.ContentDigest,
      Status = AuditProgramStatuses.Draft,
      CreatedByUserId = actor.UserId,
      CreatedAt = now
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
      CreatedAt = now
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<AuditProgramResult>.Ok(ToProgramResult(program, AuditProgramCatalog.Items.Count));
  }

  public static async Task<CommandResult<AuditProgramResult>> ReviewControlledAuditProgramPublicationAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    ReviewAuditProgramPublicationRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    if (request.ProgramVersionId == Guid.Empty || string.IsNullOrWhiteSpace(request.ExpectedContentDigest) ||
        string.IsNullOrWhiteSpace(request.PublicationReviewReference) || request.PublicationReviewReference.Trim().Length > 200)
      return CommandResult<AuditProgramResult>.Fail(InvalidCode, "The publication review reference or content binding is invalid.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockProgramVersionAsync(db, actor.FirmId, request.ProgramVersionId, ct);
    await LockActorGrantsAsync(db, actor, ct);
    var authorization = await AuthorizeProgramOwnerAsync(db, actor, ct);
    if (!authorization.Succeeded)
      return CommandResult<AuditProgramResult>.Fail(authorization.ErrorCode!, authorization.Message!);

    var program = await db.AuditProgramVersions.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ProgramVersionId && x.ProgramCode == AuditProgramCatalog.ProgramCode, ct);
    if (program is null)
      return CommandResult<AuditProgramResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var expectedDigest = request.ExpectedContentDigest.Trim().ToLowerInvariant();
    var reviewReference = request.PublicationReviewReference.Trim();
    if (!string.Equals(program.ContentDigest, expectedDigest, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(program.SourceHash, AuditProgramCatalog.SourceHash, StringComparison.OrdinalIgnoreCase))
      return CommandResult<AuditProgramResult>.Fail(ErrorCodes.GenerationStale,
        "The prepared source changed; refresh and review the current version before publication.");

    if (program.Status == AuditProgramStatuses.Published)
    {
      if (program.ApprovedByUserId == actor.UserId &&
          string.Equals(program.PublicationReviewReference, reviewReference, StringComparison.Ordinal))
      {
        await tx.CommitAsync(ct);
        return CommandResult<AuditProgramResult>.Ok(ToProgramResult(program,
          await db.AuditProgramProcedures.CountAsync(x => x.FirmId == actor.FirmId && x.ProgramVersionId == program.Id, ct)));
      }
      return CommandResult<AuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
        "That program version has already been published under a different independent review.");
    }
    if (program.Status != AuditProgramStatuses.Draft || program.CreatedByUserId == actor.UserId)
      return CommandResult<AuditProgramResult>.Fail(ErrorCodes.ScopeDenied,
        "Publication requires a different firm-wide authorized reviewer than the preparer.");

    var procedures = await db.AuditProgramProcedures.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ProgramVersionId == program.Id)
      .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
    if (!MatchesControlledCatalog(procedures))
      return CommandResult<AuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
        "The prepared programme does not match the complete controlled source; publication was refused.");

    var now = DateTimeOffset.UtcNow;
    program.Status = AuditProgramStatuses.Published;
    program.ApprovedByUserId = actor.UserId;
    program.ApprovedAt = now;
    program.PublicationReviewReference = reviewReference;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<AuditProgramResult>.Ok(ToProgramResult(program, procedures.Count));
  }

  private static async Task<CommandResult> AuthorizeProgramOwnerAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ProgramOwnerRoles, InternalOnly: true, RequireFirmWide: true), ct);

  private static async Task LockProgramBoundaryAsync(IAuditSphereDbContext db, Guid firmId, string version,
    CancellationToken ct)
  {
    var key = $"audit-program:{firmId:N}:{AuditProgramCatalog.ProgramCode}:{version}";
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);
  }

  private static async Task LockProgramVersionAsync(IAuditSphereDbContext db, Guid firmId, Guid versionId,
    CancellationToken ct)
  {
    await db.AuditProgramVersions.FromSqlInterpolated($"SELECT * FROM audit_program_versions WHERE firm_id={firmId} AND id={versionId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
  }

  private static async Task LockActorGrantsAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    await db.RoleGrants.FromSqlInterpolated($"SELECT * FROM role_grants WHERE firm_id={actor.FirmId} AND user_id={actor.UserId} AND revoked_at IS NULL FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
  }

  internal static bool MatchesControlledCatalog(IReadOnlyList<AuditProgramProcedure> persisted) =>
    persisted.Count == AuditProgramCatalog.Items.Count && persisted.Zip(AuditProgramCatalog.Items).All(pair =>
      pair.First.SourceProcedureId == pair.Second.SourceProcedureId &&
      pair.First.SectionNumber == pair.Second.SectionNumber && pair.First.SectionTitle == pair.Second.SectionTitle &&
      pair.First.Ordinal == pair.Second.Ordinal && pair.First.SourceWording == pair.Second.SourceWording);

  private static AuditProgramResult ToProgramResult(AuditProgramVersion program, int count) => new(
    program.Id, program.Version, program.Status, program.SourceHash, program.ContentDigest, count,
    program.CreatedByUserId, program.ApprovedByUserId, program.PublicationReviewReference, program.CreatedAt, program.ApprovedAt);

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

    if (!string.Equals(program.SourceHash, AuditProgramCatalog.SourceHash, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(program.ContentDigest, AuditProgramCatalog.ContentDigest, StringComparison.OrdinalIgnoreCase) ||
        (request.ExpectedSourceHash is not null && !string.Equals(request.ExpectedSourceHash, program.SourceHash, StringComparison.OrdinalIgnoreCase)) ||
        (request.ExpectedContentDigest is not null && !string.Equals(request.ExpectedContentDigest, program.ContentDigest, StringComparison.OrdinalIgnoreCase)))
      return CommandResult<EngagementAuditProgramResult>.Fail(ErrorCodes.GenerationStale,
        "The selected programme source changed or is not the complete current controlled catalogue. Refresh the engagement options.");

    var active = await db.EngagementAuditPrograms.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
        x.ClientId == scope.ClientId && x.EngagementId == request.EngagementId && x.Status == EngagementAuditProgramStatuses.Adopted)
      .OrderBy(x => x.AdoptedAt).ThenBy(x => x.Id).ToListAsync(ct);
    if (active.Count > 1)
      return CommandResult<EngagementAuditProgramResult>.Fail(ErrorCodes.GateBlocked,
        "This engagement has inconsistent active programme adoptions; controlled reconciliation is required.");

    var existing = await db.EngagementAuditPrograms.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == scope.FirmId && x.ClientId == scope.ClientId &&
        x.EngagementId == request.EngagementId && x.ProgramVersionId == program.Id, ct);
    if (existing is not null)
    {
      if (existing.Status != EngagementAuditProgramStatuses.Adopted ||
          active.Count != 1 || active[0].Id != existing.Id ||
          (request.ExpectedAdoptionId.HasValue && request.ExpectedAdoptionId != existing.Id))
        return CommandResult<EngagementAuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
          "This programme version is historical or differs from the engagement's current adoption.");
      var existingProcedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == scope.FirmId &&
          x.ClientId == scope.ClientId && x.EngagementId == request.EngagementId && x.EngagementProgramId == existing.Id)
        .ToListAsync(ct);
      var existingTemplates = await db.AuditProgramProcedures.AsNoTracking().Where(x =>
          x.FirmId == scope.FirmId && x.ProgramVersionId == program.Id)
        .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
      if (!MatchesControlledCatalog(existingTemplates) || !HasCompleteAdoption(existingProcedures, existingTemplates) ||
          !await HasCurrentDefinitionHistoryAsync(db, existingProcedures.Where(x => x.ProgramProcedureId.HasValue).ToList(), ct))
        return CommandResult<EngagementAuditProgramResult>.Fail(ErrorCodes.GateBlocked,
          "The retained engagement adoption or its current definition history is incomplete or inconsistent; no automatic repair was performed.");
      var baselineCount = existingProcedures.Count(x => x.ProgramProcedureId.HasValue);
      var adHocCount = existingProcedures.Count - baselineCount;
      await tx.CommitAsync(ct);
      return CommandResult<EngagementAuditProgramResult>.Ok(new(existing.Id, existing.ProgramVersionId,
        existingProcedures.Count, existing.Status, program.SourceHash, program.ContentDigest, baselineCount, adHocCount));
    }

    if (active.Count == 1 || request.ExpectedAdoptionId.HasValue)
      return CommandResult<EngagementAuditProgramResult>.Fail(ErrorCodes.IdempotencyConflict,
        "A different programme is already adopted or the expected adoption changed. Existing work was preserved.");

    var templates = await db.AuditProgramProcedures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProgramVersionId == program.Id)
      .OrderBy(x => x.SectionNumber).ThenBy(x => x.Ordinal).ToListAsync(ct);
    if (!MatchesControlledCatalog(templates))
      return CommandResult<EngagementAuditProgramResult>.Fail(InvalidCode,
        "The published programme does not match the complete controlled source catalogue.");

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
    var adoptedAt = DateTimeOffset.UtcNow;
    var procedures = templates.Select(template => new AuditProcedure
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
      CurrentDefinitionRevision = 1,
      Title = template.SourceWording,
      Status = AuditProcedureStatuses.Planned,
      CreatedAt = adoptedAt
    }).ToList();
    db.AuditProcedures.AddRange(procedures);
    db.AuditProcedureDefinitionRevisions.AddRange(procedures.Select(procedure => new AuditProcedureDefinitionRevision
    {
      Id = Guid.CreateVersion7(),
      FirmId = procedure.FirmId,
      ClientId = procedure.ClientId,
      EngagementId = procedure.EngagementId,
      AuditProcedureId = procedure.Id,
      Revision = 1,
      ProgramProcedureId = procedure.ProgramProcedureId,
      EffectiveTitle = procedure.Title,
      EffectiveWording = procedure.SourceWording!,
      CommandId = Guid.CreateVersion7(),
      RequestDigest = DefinitionRequestDigest(procedure.Title, procedure.SourceWording!, null),
      ActorUserId = actor.UserId,
      AuthoredAt = adoptedAt,
      IsLegacySnapshot = false,
      RecordedAt = adoptedAt
    }));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<EngagementAuditProgramResult>.Ok(new(adoption.Id, adoption.ProgramVersionId,
      templates.Count, adoption.Status, program.SourceHash, program.ContentDigest, templates.Count, 0));
  }

  internal static bool HasCompleteAdoption(IReadOnlyList<AuditProcedure> procedures,
    IReadOnlyList<AuditProgramProcedure> templates)
  {
    var byTemplateId = templates.ToDictionary(x => x.Id);
    var baseline = procedures.Where(x => x.ProgramProcedureId.HasValue).ToList();
    if (baseline.Count != AuditProgramCatalog.Items.Count || baseline.Any(x => x.CurrentDefinitionRevision < 1) ||
        baseline.Select(x => x.ProgramProcedureId).Distinct().Count() != AuditProgramCatalog.Items.Count)
      return false;
    foreach (var procedure in baseline)
    {
      if (!byTemplateId.TryGetValue(procedure.ProgramProcedureId!.Value, out var template) ||
          !string.Equals(procedure.SourceProcedureId, template.SourceProcedureId, StringComparison.Ordinal) ||
          procedure.SourceSectionNumber != template.SectionNumber ||
          !string.Equals(procedure.SourceSectionTitle, template.SectionTitle, StringComparison.Ordinal))
        return false;
    }
    return true;
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

  public static async Task<CommandResult> LinkRiskAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid procedureId,
    Guid? riskId,
    CancellationToken ct = default)
  {
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var snapshot = await db.AuditProcedures.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, snapshot.EngagementId, true, ct);
    if (!publication.Succeeded) return CommandResult.Fail(publication.ErrorCode!, publication.Message!);
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
    if (scope.Denied is not null) return CommandResult.Fail(scope.Denied, scope.Message);

    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id = {procedureId} AND firm_id = {actor.FirmId}
      FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    if (riskId is { } rid && !await db.AuditRisks.AnyAsync(x => x.Id == rid && x.FirmId == actor.FirmId && x.EngagementId == procedure.EngagementId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The risk must belong to the same engagement.");

    var tracked = db.AuditProcedures.Local.SingleOrDefault(x => x.Id == procedure.Id);
    if (tracked is not null) procedure = tracked;
    else db.AuditProcedures.Attach(procedure);
    procedure.RiskId = riskId;
    await db.SaveChangesAsync(ct);
    var finalAuthority = await AuditPlanningCommandWorkspace.AuthorizeAsync(db, actor, snapshot.EngagementId, AuditPlanningActions.LinkProcedureRisk, true, ct);
    if (!finalAuthority.Succeeded) return finalAuthority;
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> GetOrCreateWorkpaperAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid procedureId,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);

    var role = Authorization.RequireRole(actor, PlanningRoles);
    if (!role.Succeeded)
      return CommandResult<Guid>.Fail(role.ErrorCode!, role.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var procedure = await db.AuditProcedures.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    if (procedure is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var scope = await ResolveEngagementAsync(db, actor, procedure.EngagementId, ct, procedure.ClientId);
    if (scope.Denied is not null)
      return CommandResult<Guid>.Fail(scope.Denied, scope.Message);

    var existing = await db.Workpapers.AsNoTracking()
      .Where(w => w.FirmId == actor.FirmId && w.EngagementId == procedure.EngagementId && w.ProcedureId == procedureId)
      .OrderByDescending(w => w.CreatedAt)
      .FirstOrDefaultAsync(ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);

    var effectiveTitle = procedure.Title;
    var effectiveWording = procedure.SourceWording ?? procedure.Title;
    if (procedure.ProgramProcedureId is not null)
    {
      var definition = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == procedure.EngagementId &&
        x.AuditProcedureId == procedure.Id && x.Revision == procedure.CurrentDefinitionRevision, ct);
      if (definition is null || definition.ProgramProcedureId != procedure.ProgramProcedureId)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The current controlled procedure definition is unavailable.");
      effectiveTitle = definition.EffectiveTitle;
      effectiveWording = definition.EffectiveWording;
    }

    var workpaper = new Workpaper
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = procedure.EngagementId,
      ProcedureId = procedure.Id,
      ActorId = actor.UserId,
      Index = procedure.SourceProcedureId,
      Title = effectiveTitle,
      Objective = $"Procedure {procedure.SourceProcedureId}: {effectiveTitle}",
      TemplateVersion = procedure.SourceProcedureId,
      Procedure = effectiveWording,
      Revision = 1,
      Status = WorkpaperStatuses.Working,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Workpapers.Add(workpaper);

    if (procedure.Status == AuditProcedureStatuses.Planned && procedure.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable)
    {
      await db.AuditProcedures.Where(p => p.Id == procedureId && p.FirmId == actor.FirmId)
        .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, AuditProcedureStatuses.InProgress), ct);
    }

    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(workpaper.Id);
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
      return CommandResult<ProcedureResultValue>.Fail(InvalidCode,
        $"Structured result must be a JSON object under {ProcedureRiskBandEvaluator.MaximumStructuredResultBytes} bytes and cannot set the reserved risk-basis field.");
    if (request.EvidenceReferences.Any(string.IsNullOrWhiteSpace))
      return CommandResult<ProcedureResultValue>.Fail(InvalidCode, "Evidence references cannot be blank.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.AuditProcedures.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.AuditProcedureId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var initialAuthority = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, snapshot.ClientId, snapshot.EngagementId, PlanningRoles, InternalOnly: true), ct);
    if (!initialAuthority.Succeeded) return CommandResult<ProcedureResultValue>.Fail(initialAuthority.ErrorCode!, initialAuthority.Message!);
    // Match source publication: firm -> client generation -> engagement -> procedure/result.
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var currentGeneration = await AuditPlanningInputGeneration.ReadSharedAsync(db, actor.FirmId, snapshot.ClientId, ct);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId, sharedEngagement: true);
    if (scope.Denied is not null)
      return CommandResult<ProcedureResultValue>.Fail(scope.Denied, scope.Message);

    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id = {request.AuditProcedureId} AND firm_id = {actor.FirmId}
      FOR UPDATE
    """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GateBlocked,
        "Only an explicitly applicable procedure can receive test results.");
    AuditProcedureDefinitionRevision? effectiveDefinition = null;
    if (procedure.ProgramProcedureId is not null)
    {
      effectiveDefinition = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == procedure.EngagementId &&
        x.AuditProcedureId == procedure.Id && x.Revision == procedure.CurrentDefinitionRevision, ct);
      if (effectiveDefinition is null || effectiveDefinition.ProgramProcedureId != procedure.ProgramProcedureId)
        return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GateBlocked,
          "This controlled procedure has no verified current definition. Refresh or reconcile the retained source before submitting work.");
    }
    // Every professional mutation of a frozen audit baseline is refused under the same transaction
    // boundary as the write (STE-REM-09).
    var frozen = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, procedure.EngagementId, "submit procedure result", ct);
    if (!frozen.Succeeded)
    {
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult<ProcedureResultValue>.Fail(frozen.ErrorCode!, frozen.Message!);
    }

    // Share the client-source fence for independent submissions; generation writers still wait.
    // Risk and materiality revisions are bound below
    // as a narrower immutable basis and must not invalidate unrelated accounting mappings.
    if (currentGeneration is null)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GateBlocked,
        "The current client planning revision is unavailable; the procedure result was not submitted.");
    var generation = currentGeneration.Value < 1 ? 1 : currentGeneration.Value;
    if (generation != request.ExpectedInputGeneration)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GenerationStale,
        "The underlying engagement inputs changed.");

    var effectiveRisk = await ProcedureRiskBandEvaluator.EvaluateAsync(
      db, scope.FirmId, procedure.EngagementId, procedure.RiskId, ct);
    if (effectiveRisk.Blocker is not null)
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.GateBlocked, effectiveRisk.Blocker);

    var latest = await db.AuditProcedureResults.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.AuditProcedureId == procedure.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (latest is not null && latest.Status == AuditProcedureResultStatuses.Submitted && latest.InputGeneration == generation &&
        latest.DefinitionRevisionId == effectiveDefinition?.Id &&
        ProcedureRiskBandEvaluator.CapturedBasisMatches(latest.StructuredResultJson, effectiveRisk.Value))
      return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ProtectedState,
        "The current result is awaiting review; create a new revision only after its recorded planning basis changes or it is returned.");

    if (effectiveRisk.Value is { } riskBand)
    {
      var minRank = FsliRiskBandRules.MinimumExecutorRank(riskBand.Band);
      var staffAssignment = await db.EngagementStaffAssignments.AsNoTracking()
        .FirstOrDefaultAsync(x => x.FirmId == scope.FirmId && x.EngagementId == procedure.EngagementId && x.UserId == actor.UserId && x.RevokedAt == null, ct);
      var actorRank = staffAssignment is not null ? StaffingLevels.Rank(staffAssignment.StaffingLevel) :
        (actor.Roles.Contains("Partner") ? 4 : actor.Roles.Contains("Manager") ? 3 : actor.Roles.Contains("Senior") ? 2 : 1);
      if (actorRank < minRank)
      {
        var reqLevel = StaffingLevels.All.First(x => StaffingLevels.Rank(x) == minRank);
        return CommandResult<ProcedureResultValue>.Fail(ErrorCodes.ScopeDenied,
          $"A {riskBand.Band} risk procedure requires execution by {StaffingLevels.Label(reqLevel)} or above. {riskBand.Explanation}");
      }
    }

    var structuredResult = ProcedureRiskBandEvaluator.WithCapturedBasis(request.StructuredResultJson, effectiveRisk.Value);
    if (Encoding.UTF8.GetByteCount(structuredResult) > ProcedureRiskBandEvaluator.MaximumStructuredResultBytes)
      return CommandResult<ProcedureResultValue>.Fail(InvalidCode,
        "Structured result is too large to retain with its required planning basis.");

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
      Procedure = effectiveDefinition?.EffectiveWording ?? procedure.SourceWording ?? procedure.Title,
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
      DefinitionRevisionId = effectiveDefinition?.Id,
      WorkPerformed = request.WorkPerformed.Trim(),
      StructuredResultJson = structuredResult,
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
    var publicationActor=actor.UserId.ToString();var publicationEpoch=actor.SessionEpoch.ToString(System.Globalization.CultureInfo.InvariantCulture);var publicationPaper=workpaper.Id.ToString();
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('audit.workpaper_action','PROCEDURE_SUBMIT',true),set_config('audit.workpaper_actor_id',{publicationActor},true),set_config('audit.workpaper_actor_epoch',{publicationEpoch},true),set_config('audit.workpaper_procedure_paper',{publicationPaper},true)",ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ProcedureResultValue>.Ok(new(result.Id, procedure.Id, workpaper.Id, revision, result.Status));
  }

  public static async Task<CommandResult<ProcedureReviewValue>> ReviewResultAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    ReviewProcedureResultRequest request,
    CancellationToken ct = default,
    bool createWorkflowReworkTask = false)
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
    var snapshot = await db.AuditProcedureResults.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.AuditProcedureResultId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var initialAuthority = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, snapshot.ClientId, snapshot.EngagementId, ReviewRoles, InternalOnly: true), ct);
    if (!initialAuthority.Succeeded) return CommandResult<ProcedureReviewValue>.Fail(initialAuthority.ErrorCode!, initialAuthority.Message!);
    // Match source publication: firm -> client generation -> engagement -> procedure/result.
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var currentGeneration = await AuditPlanningInputGeneration.LockAndReadAsync(db, actor.FirmId, snapshot.ClientId, ct);
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId, ReviewRoles);
    if (scope.Denied is not null)
      return CommandResult<ProcedureReviewValue>.Fail(scope.Denied, scope.Message);
    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id = {snapshot.AuditProcedureId} AND firm_id = {actor.FirmId}
      FOR UPDATE
    """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // Serialize the final review transition after taking the engagement and procedure locks.
    var result = await db.AuditProcedureResults.FromSqlInterpolated($"""
      SELECT * FROM audit_procedure_results
      WHERE id = {request.AuditProcedureResultId} AND firm_id = {actor.FirmId}
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (result is null || result.AuditProcedureId != procedure.Id || result.EngagementId != procedure.EngagementId || result.ClientId != scope.ClientId)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (procedure.ProgramProcedureId is not null)
    {
      var currentDefinition = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == procedure.EngagementId &&
        x.AuditProcedureId == procedure.Id && x.Revision == procedure.CurrentDefinitionRevision, ct);
      if (currentDefinition is null || result.DefinitionRevisionId != currentDefinition.Id)
        return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GenerationStale,
          "The procedure definition changed after this result was prepared; submit the current effective instructions before review.");
    }
    if (result.PreparedByUserId == actor.UserId)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review the same result.");
    var frozen = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, result.EngagementId, "review procedure result", ct);
    if (!frozen.Succeeded)
    {
      await tx.CommitAsync(ct); // persist the recorded refused attempt before refusing
      return CommandResult<ProcedureReviewValue>.Fail(frozen.ErrorCode!, frozen.Message!);
    }
    if (result.Status != AuditProcedureResultStatuses.Submitted)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ProtectedState,
        "Only the current submitted result can receive a review decision.");
    var currentResultId = await db.AuditProcedureResults.AsNoTracking()
      .Where(x => x.FirmId == scope.FirmId && x.AuditProcedureId == procedure.Id)
      .OrderByDescending(x => x.Revision).Select(x => x.Id).FirstOrDefaultAsync(ct);
    if (currentResultId != result.Id)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ProtectedState,
        "Only the latest submitted procedure revision can receive a review decision.");

    if (currentGeneration is null)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GateBlocked,
        "The current client planning revision is unavailable; the result was not reviewed.");
    var generation = currentGeneration.Value < 1 ? 1 : currentGeneration.Value;
    if (result.InputGeneration != generation)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GenerationStale,
        "The underlying engagement inputs changed; the result must be resubmitted.");

    var hierarchy = await ReviewNotesService.RequireReviewerAboveAsync(db, scope.FirmId, result.EngagementId, result.PreparedByUserId, actor.UserId, ct);
    if (!hierarchy.Succeeded)
      return CommandResult<ProcedureReviewValue>.Fail(hierarchy.ErrorCode!, hierarchy.Message!);

    var effectiveRisk = await ProcedureRiskBandEvaluator.EvaluateAsync(
      db, scope.FirmId, procedure.EngagementId, procedure.RiskId, ct);
    if (effectiveRisk.Blocker is not null)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GateBlocked, effectiveRisk.Blocker);
    if (!ProcedureRiskBandEvaluator.CapturedBasisMatches(result.StructuredResultJson, effectiveRisk.Value))
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GenerationStale,
        "The linked risk, materiality calculation, or approved mapping changed; reassess the procedure and submit a current result.");
    if (effectiveRisk.Value is { } riskBand)
    {
      var minReviewerRank = FsliRiskBandRules.MinimumReviewerRank(riskBand.Band);
      var reviewerStaffing = await db.EngagementStaffAssignments.AsNoTracking()
        .FirstOrDefaultAsync(x => x.FirmId == scope.FirmId && x.EngagementId == result.EngagementId && x.UserId == actor.UserId && x.RevokedAt == null, ct);
      var reviewerRank = reviewerStaffing is not null ? StaffingLevels.Rank(reviewerStaffing.StaffingLevel) :
        (actor.Roles.Contains("Partner") ? 4 : actor.Roles.Contains("Manager") ? 3 : actor.Roles.Contains("Senior") ? 2 : 1);
      if (reviewerRank < minReviewerRank)
      {
        var reqLevel = StaffingLevels.All.First(x => StaffingLevels.Rank(x) == minReviewerRank);
        return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.ScopeDenied,
          $"A {riskBand.Band} risk procedure requires review by {StaffingLevels.Label(reqLevel)} or above. {riskBand.Explanation}");
      }
    }

    if (decision == AuditProcedureReviewDecisions.Reviewed && await ReviewNotesService.OpenCountAsync(db, scope.FirmId, procedure.Id, ct) is > 0 and var open)
      return CommandResult<ProcedureReviewValue>.Fail(ErrorCodes.GateBlocked, $"{open} review note(s) are still open; resolve them before approving.");

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
    if (decision == AuditProcedureReviewDecisions.ChangesRequired && createWorkflowReworkTask)
    {
      var rework = await PracticeTimeService.CreateReviewReworkTaskAsync(db, actor, scope.ClientId, result.EngagementId,
        procedure.Id, result.Id, result.Revision, procedure.SourceProcedureId, result.PreparedByUserId, ct);
      if (!rework.Succeeded)
        return CommandResult<ProcedureReviewValue>.Fail(rework.ErrorCode!, rework.Message!);
    }
    if (decision == AuditProcedureReviewDecisions.Reviewed)
      await AuditSphereOps.Application.Completion.AuditDeliverableService.CompileAfterFinalReviewAsync(db, actor, result.EngagementId, ct);
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
    IReadOnlyList<string>? roles = null,
    bool sharedEngagement = false)
  {
    var engagementQuery = sharedEngagement
      ? db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR SHARE")
      : db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR UPDATE
      """);
    var engagement = await engagementQuery.AsNoTracking().SingleOrDefaultAsync(ct);
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
    if (string.IsNullOrWhiteSpace(json) || Encoding.UTF8.GetByteCount(json) > ProcedureRiskBandEvaluator.MaximumStructuredResultBytes)
      return false;
    return !ProcedureRiskBandEvaluator.HasReservedBasisProperty(json);
  }
}
