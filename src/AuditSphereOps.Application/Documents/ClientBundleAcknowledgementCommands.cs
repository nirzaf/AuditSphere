using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>Records a client user's explicit receipt report for one exact immutable final bundle.</summary>
public static class ClientBundleAcknowledgementCommands
{
  public static async Task<CommandResult<Guid>> AcknowledgeAsync(IAuditSphereDbContext db, ActorContext actor, Guid bundleId,
    string contentSha256, string manifestSha256, bool received, CancellationToken ct = default)
  {
    if (!received)
      return CommandResult<Guid>.Fail("receipt.confirmation-required", "Confirm receipt of the exact final bundle before recording an acknowledgement.");
    if (!actor.Roles.Contains("ClientUser", StringComparer.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Bundle unavailable.");
    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Bundle unavailable.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var bundle = await db.CommercialDeliverableBundles.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == bundleId, ct);
    if (bundle is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Bundle unavailable.");
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, bundle.ClientId, bundle.EngagementId, ["ClientUser"]), ct);
    if (!authorization.Succeeded) return CommandResult<Guid>.Fail(authorization.ErrorCode!, "Bundle unavailable.");

    // Fence role/session changes and concurrent acknowledgements in a stable lock order.
    var firmSafety = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (firmSafety is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable; no receipt was recorded.");
    var clientSafety = await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE id = {bundle.ClientId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (clientSafety is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable; no receipt was recorded.");
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE id = {bundle.EngagementId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (engagement is null || engagement.PracticeClientId != bundle.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Bundle unavailable.");
    var current = await db.CommercialDeliverableBundles.FromSqlInterpolated(
      $"SELECT * FROM commercial_deliverable_bundles WHERE id = {bundleId} AND firm_id = {actor.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (current is null || current.ClientId != bundle.ClientId || current.EngagementId != bundle.EngagementId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Bundle unavailable.");

    authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, current.ClientId, current.EngagementId, ["ClientUser"]), ct);
    if (!authorization.Succeeded) return CommandResult<Guid>.Fail(authorization.ErrorCode!, "Bundle unavailable.");

    var actualContentHash = Hashing.Sha256Hex(current.Content);
    var actualManifestHash = Hashing.Sha256Hex(current.ManifestJson);
    if (!string.Equals(actualContentHash, current.ContentSha256, StringComparison.Ordinal) ||
        !string.Equals(actualManifestHash, current.ManifestSha256, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The stored bundle failed its integrity check. No receipt was recorded.");
    if (!string.Equals(contentSha256, current.ContentSha256, StringComparison.Ordinal) ||
        !string.Equals(manifestSha256, current.ManifestSha256, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The final bundle changed. Refresh and review the current hashes before acknowledging receipt.");

    var existing = await db.ClientBundleAcknowledgements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.BundleId == current.Id && x.AcknowledgedByUserId == actor.UserId, ct);
    if (existing is not null)
    {
      if (existing.ContentSha256 != current.ContentSha256 || existing.ManifestSha256 != current.ManifestSha256)
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "An acknowledgement already exists for a different bundle identity.");
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(existing.Id);
    }

    var receipt = new ClientBundleAcknowledgement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = current.ClientId, EngagementId = current.EngagementId,
      BundleId = current.Id, AcknowledgedByUserId = actor.UserId, ContentSha256 = current.ContentSha256,
      ManifestSha256 = current.ManifestSha256, AcknowledgedAt = DateTimeOffset.UtcNow
    };
    db.ClientBundleAcknowledgements.Add(receipt);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(receipt.Id);
  }
}
