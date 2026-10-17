using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal sealed record PublishAuditProgramRequest(string Version, string SourceHash);

internal static class AuditProgramTestHelpers
{
  internal static async Task<CommandResult<ProcedureApplicabilityResult>> MarkApplicableAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid procedureId, string? rationale = null,
    CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleAsync(x =>
      x.FirmId == actor.FirmId && x.Id == procedureId, ct);
    return await AuditProgramService.DecideApplicabilityAsync(db, actor,
      new DecideProcedureApplicabilityRequest(procedureId, AuditApplicabilityStatuses.Applicable, rationale,
        procedure.CurrentApplicabilityRevision, procedure.CurrentDefinitionRevision, Guid.NewGuid()), ct);
  }

  internal static async Task<ActorContext> CreateReviewerAsync(IAuditSphereDbContext db, Guid firmId, Guid grantedByUserId)
  {
    var reviewerId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = reviewerId, FirmId = firmId, Subject = "audit-program-reviewer-" + reviewerId.ToString("N"),
      TenantId = "tenant-audit-program-test", Email = reviewerId.ToString("N") + "@example.test",
      DisplayName = "Independent Programme Reviewer", UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    });
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = reviewerId, Role = "Partner",
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = grantedByUserId
    });
    await db.SaveChangesAsync();
    return new ActorContext(reviewerId, firmId, 1, ["Partner"]);
  }

  internal static async Task<CommandResult<AuditProgramResult>> PublishAsync(
    IAuditSphereDbContext db, ActorContext preparer, PublishAuditProgramRequest request,
    CancellationToken ct = default)
  {
    if (!preparer.Roles.Any(r => r is "Partner" or "Administrator"))
      return CommandResult<AuditProgramResult>.Fail(ErrorCodes.ScopeDenied, "The fixture actor is not a programme owner.");

    var firmWidePreparer = await db.RoleGrants.AnyAsync(g => g.FirmId == preparer.FirmId && g.UserId == preparer.UserId &&
        g.ClientId == null && g.EngagementId == null && g.RevokedAt == null &&
        (g.Role == "Partner" || g.Role == "Administrator"), ct);
    if (!firmWidePreparer)
    {
      var preparerId = Guid.NewGuid();
      db.Users.Add(new AppUser
      {
        Id = preparerId, FirmId = preparer.FirmId, Subject = "audit-program-preparer-" + preparerId.ToString("N"),
        TenantId = "tenant-audit-program-test", Email = preparerId.ToString("N") + "@example.test",
        DisplayName = "Firm-wide Programme Preparer", UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = preparer.FirmId, UserId = preparerId,
        Role = preparer.Roles.Contains("Partner", StringComparer.OrdinalIgnoreCase) ? "Partner" : "Administrator",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = preparer.UserId
      });
      await db.SaveChangesAsync(ct);
      preparer = new ActorContext(preparerId, preparer.FirmId, 1,
        [preparer.Roles.Contains("Partner", StringComparer.OrdinalIgnoreCase) ? "Partner" : "Administrator"]);
    }

    var prepared = await AuditProgramService.PrepareControlledAuditProgramAsync(db, preparer,
      new PrepareAuditProgramRequest(request.Version, request.SourceHash), ct);
    if (!prepared.Succeeded || prepared.Value!.Status == "PUBLISHED") return prepared;

    var reviewer = await CreateReviewerAsync(db, preparer.FirmId, preparer.UserId);
    return await AuditProgramService.ReviewControlledAuditProgramPublicationAsync(db,
      reviewer,
      new ReviewAuditProgramPublicationRequest(prepared.Value.ProgramVersionId,
        prepared.Value.ContentDigest!, "test-review-" + request.Version), ct);
  }
}
