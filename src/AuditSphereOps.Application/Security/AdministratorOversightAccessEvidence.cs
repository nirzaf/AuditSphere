using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

/// <summary>Server-selected scope for one registered oversight read. Contains no query text or document content.</summary>
public sealed record AdministratorOversightAccessTarget(string Operation, string ResourceKind, Guid? ResourceId = null);
public sealed record AdministratorOversightAccessRow(Guid Id, Guid ActorUserId, string SessionEpoch, Guid RequestId,
  string Operation, string ResourceKind, Guid? ResourceId, DateTimeOffset AuthorizedAt, string Outcome);
public sealed record AdministratorOversightAccessPage(IReadOnlyList<AdministratorOversightAccessRow> Items,
  bool HasMore, DateTimeOffset? Before, Guid? BeforeId);

/// <summary>Dedicated append-only read evidence. Never save the business query's change tracker.</summary>
public static class AdministratorOversightAccessEvidence
{
  public const string Unavailable = "oversight.audit-unavailable";

  public static async Task<CommandResult<Guid>> RecordAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid requestId, AdministratorOversightAccessTarget target, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || target.Operation.Length is < 1 or > 100 ||
        target.ResourceKind is not ("FIRM" or "INVOICE" or "GROUP" or "CONSOLIDATION_SCOPE" or "EXTERNAL_PACK") ||
        (target.ResourceKind == "FIRM") != (target.ResourceId is null) || target.ResourceId == Guid.Empty ||
        db is not DbContext ef || ef.ChangeTracker.HasChanges() || db.Database.CurrentTransaction is not null)
      return CommandResult<Guid>.Fail(Unavailable, "Oversight access evidence could not be retained.");
    var auth = await AdministratorOversightReadAuthorization.CheckEligibilityAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var prior = await db.AdministratorOversightAccessEvents.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.RequestId == requestId, ct);
    if (prior is not null)
      return prior.ActorUserId == actor.UserId && prior.SessionEpoch == actor.SessionEpoch &&
        prior.Operation == target.Operation && prior.ResourceKind == target.ResourceKind && prior.ResourceId == target.ResourceId
        ? CommandResult<Guid>.Ok(prior.Id)
        : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The access request identity conflicts with retained evidence.");
    var evidence = new AdministratorOversightAccessEvent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ActorUserId = actor.UserId, SessionEpoch = actor.SessionEpoch,
      RequestId = requestId, Operation = target.Operation, ResourceKind = target.ResourceKind,
      ResourceId = target.ResourceId, AuthorizedAt = DateTimeOffset.UtcNow
    };
    db.AdministratorOversightAccessEvents.Add(evidence);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(evidence.Id);
  }

  public static async Task<CommandResult<AdministratorOversightAccessPage>> PageAsync(IAuditSphereDbContext db,
    ActorContext actor, DateTimeOffset? before = null, Guid? beforeId = null, CancellationToken ct = default)
  {
    if ((before is null) != (beforeId is null) || beforeId == Guid.Empty)
      return CommandResult<AdministratorOversightAccessPage>.Fail("request.invalid", "Invalid access-history cursor.");
    // Reuse current firm-wide Staff Administrator history authority; no new reviewer role is invented.
    var auth = await AdministratorOversightReadAuthorization.CheckEligibilityAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<AdministratorOversightAccessPage>.Fail(auth.ErrorCode!, auth.Message!);
    var query = db.AdministratorOversightAccessEvents.AsNoTracking().Where(x => x.FirmId == actor.FirmId);
    if (before is { } at && beforeId is { } id)
      query = query.Where(x => x.AuthorizedAt < at || x.AuthorizedAt == at && x.Id.CompareTo(id) < 0);
    var rows = await query.OrderByDescending(x => x.AuthorizedAt).ThenByDescending(x => x.Id).Take(26).ToListAsync(ct);
    var visible = rows.Take(25).ToArray();
    auth = await AdministratorOversightReadAuthorization.CheckEligibilityAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<AdministratorOversightAccessPage>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<AdministratorOversightAccessPage>.Ok(new(visible.Select(x => new AdministratorOversightAccessRow(
      x.Id, x.ActorUserId, x.SessionEpoch.ToString(CultureInfo.InvariantCulture), x.RequestId, x.Operation,
      x.ResourceKind, x.ResourceId, x.AuthorizedAt, x.Outcome)).ToArray(), rows.Count > 25,
      visible.LastOrDefault()?.AuthorizedAt, visible.LastOrDefault()?.Id));
  }
}
