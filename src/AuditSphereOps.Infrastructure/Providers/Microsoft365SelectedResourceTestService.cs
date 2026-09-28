using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Security;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Administrator-initiated, read-only test of the saved draft target.</summary>
public sealed class Microsoft365SelectedResourceTestService(
  AuditSphereOps.Application.Operations.IAuditSphereDbContextFactory factory,
  GraphSelectedResourceProbe probe)
{
  public async Task TestDraftAsync(ActorContext actor, Guid draftId, CancellationToken ct)
  {
    await AuthorizeAsync(actor, ct);
    await probe.ProbeDraftAsync(actor.FirmId, draftId, ct);
    // A revoked grant during the network read must not return a successful result.
    await AuthorizeAsync(actor, ct);
  }

  private async Task AuthorizeAsync(ActorContext actor, CancellationToken ct)
  {
    await using var db = await factory.CreateAsync(ct);
    var decision = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
        InternalOnly: true, RequireFirmWide: true), ct);
    if (!decision.Succeeded)
      throw new UnauthorizedAccessException("Firm-wide administrator access is required.");
  }
}
