using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditProgramService
{
  internal static async Task<bool> HasCurrentDefinitionHistoryAsync(IAuditSphereDbContext db,
    IReadOnlyCollection<AuditProcedure> procedures, CancellationToken ct)
  {
    if (procedures.Count == 0 || procedures.Any(x => !x.ProgramProcedureId.HasValue || x.CurrentDefinitionRevision < 1)) return false;
    var ids = procedures.Select(x => x.Id).ToArray();
    var firmId = procedures.First().FirmId;
    var definitions = await db.AuditProcedureDefinitionRevisions.AsNoTracking().Where(definition =>
      definition.FirmId == firmId && ids.Contains(definition.AuditProcedureId) && db.AuditProcedures.Any(procedure =>
        procedure.FirmId == firmId && procedure.Id == definition.AuditProcedureId &&
        procedure.CurrentDefinitionRevision == definition.Revision && procedure.ProgramProcedureId == definition.ProgramProcedureId))
      .Select(x => x.AuditProcedureId).ToListAsync(ct);
    return definitions.Count == procedures.Count && definitions.Distinct().Count() == procedures.Count;
  }

  internal static string DefinitionRequestDigest(string title, string wording, string? rationale) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
      title = title.Trim(), wording = wording.Trim(), rationale = rationale?.Trim()
    })))).ToLowerInvariant();

  public static async Task<CommandResult> TailorProcedureAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    TailorProcedureRequest request,
    CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(db);
    ArgumentNullException.ThrowIfNull(request);
    var title = request.Title?.Trim();
    var wording = request.CustomWording?.Trim();
    var rationale = request.Rationale?.Trim();
    if (string.IsNullOrWhiteSpace(title) || title.Length > 500 || string.IsNullOrWhiteSpace(wording) || wording.Length > 2000 ||
        string.IsNullOrWhiteSpace(rationale) || rationale.Length > 1000 || request.ExpectedDefinitionRevision < 1 || request.CommandId == Guid.Empty)
      return CommandResult.Fail(InvalidCode, "Provide a bounded title, effective wording, reason, current definition revision and command identity.");
    var requestDigest = DefinitionRequestDigest(title, wording, rationale);
    var role = Authorization.RequireRole(actor, PlanningRoles);
    if (!role.Succeeded) return CommandResult.Fail(role.ErrorCode!, role.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var snapshot = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.AuditProcedureId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    // Shared safety locks precede the engagement/procedure locks, matching result submission.
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={snapshot.ClientId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    var scope = await ResolveEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
    if (scope.Denied is not null) return CommandResult.Fail(scope.Denied, scope.Message);

    var procedure = await db.AuditProcedures.FromSqlInterpolated($"""
      SELECT * FROM audit_procedures
      WHERE id={request.AuditProcedureId} AND firm_id={actor.FirmId}
      FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (procedure is null || procedure.ClientId != scope.ClientId || procedure.EngagementId != snapshot.EngagementId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (procedure.ProgramProcedureId is not { } sourceId)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Tailoring through the controlled-programme path is unavailable for ad-hoc procedures; use their retained wording-history workflow.");

    var replay = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == scope.FirmId && x.AuditProcedureId == procedure.Id && x.CommandId == request.CommandId, ct);
    if (replay is not null)
      return string.Equals(replay.RequestDigest, requestDigest, StringComparison.Ordinal)
        ? CommandResult.Ok()
        : CommandResult.Fail(ErrorCodes.IdempotencyConflict, "That definition command identity was already used for different wording.");

    if (procedure.CurrentDefinitionRevision != request.ExpectedDefinitionRevision)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The procedure definition changed. Refresh its current source and effective wording before tailoring.");
    var previous = await db.AuditProcedureDefinitionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == scope.FirmId && x.ClientId == scope.ClientId && x.EngagementId == procedure.EngagementId &&
      x.AuditProcedureId == procedure.Id && x.Revision == procedure.CurrentDefinitionRevision, ct);
    var source = await db.AuditProgramProcedures.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == scope.FirmId && x.Id == sourceId, ct);
    if (previous is null || source is null || previous.ProgramProcedureId != sourceId ||
        !string.Equals(procedure.SourceProcedureId, source.SourceProcedureId, StringComparison.Ordinal))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "The original programme source or current definition history is unavailable or inconsistent.");

    var frozen = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(
      db, actor, procedure.EngagementId, "tailor audit procedure definition", ct);
    if (!frozen.Succeeded)
    {
      await tx.CommitAsync(ct);
      return CommandResult.Fail(frozen.ErrorCode!, frozen.Message!);
    }

    var now = DateTimeOffset.UtcNow;
    var revision = new AuditProcedureDefinitionRevision
    {
      Id = Guid.CreateVersion7(),
      FirmId = scope.FirmId,
      ClientId = scope.ClientId,
      EngagementId = procedure.EngagementId,
      AuditProcedureId = procedure.Id,
      Revision = checked(previous.Revision + 1),
      ProgramProcedureId = sourceId,
      EffectiveTitle = title,
      EffectiveWording = wording,
      Rationale = rationale,
      CommandId = request.CommandId,
      RequestDigest = requestDigest,
      PreviousRevisionId = previous.Id,
      ActorUserId = actor.UserId,
      AuthoredAt = now,
      IsLegacySnapshot = false,
      RecordedAt = now
    };
    db.AuditProcedureDefinitionRevisions.Add(revision);
    procedure.CurrentDefinitionRevision = revision.Revision;
    procedure.Title = title;
    if (procedure.CurrentResultRevision > 0)
      procedure.Status = AuditProcedureStatuses.InProgress;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
