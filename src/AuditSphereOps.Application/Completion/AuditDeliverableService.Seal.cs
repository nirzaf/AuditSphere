using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public static partial class AuditDeliverableService
{
  public static async Task<CommandResult<Guid>> RegisterFirmSealAsync(IAuditSphereDbContext db, ActorContext actor, byte[] png, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, RequiredRoles: ["Partner", "Administrator"], InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (png is null || png.Length is < 1 or > MaxSignatureBytes || AuditDeliverableRenderer.ReadPngSize(png) is not { } size ||
        size.Width is < 50 or > 2000 || size.Height is < 20 or > 1000 || !AuditDeliverableRenderer.IsRenderablePng(png))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Upload the approved firm seal as a usable PNG of at most 512 KB.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var guard = await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (guard is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var sha = Hashing.Sha256Hex(png);
    var prior = await db.FirmSealSpecimens.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (prior?.Sha256 == sha) return CommandResult<Guid>.Ok(prior.Id);
    var seal = new FirmSealSpecimen { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Version = (prior?.Version ?? 0) + 1, PngContent = png.ToArray(), Sha256 = sha,
      RegisteredByUserId = actor.UserId, RegisteredAt = DateTimeOffset.UtcNow };
    db.FirmSealSpecimens.Add(seal);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(seal.Id);
  }
}
