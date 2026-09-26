using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record PracticeLeadListItem(
  Guid Id, string Name, string Source, string? PrimaryContactName,
  string? PrimaryContactEmail, string Status, DateTimeOffset CreatedAt);

/// <summary>Current firm-scoped commercial lead projection for the staff workbench.</summary>
public static class PracticeLeadQuery
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];

  public static async Task<CommandResult<IReadOnlyList<PracticeLeadListItem>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: CommercialRoles,
      InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<PracticeLeadListItem>>.Fail(auth.ErrorCode!, auth.Message!);

    var leads = await db.Leads.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt)
      .Select(x => new PracticeLeadListItem(x.Id, x.Name, x.Source,
        x.PrimaryContactName, x.PrimaryContactEmail, x.Status, x.CreatedAt))
      .ToListAsync(ct);

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return auth.Succeeded
      ? CommandResult<IReadOnlyList<PracticeLeadListItem>>.Ok(leads)
      : CommandResult<IReadOnlyList<PracticeLeadListItem>>.Fail(auth.ErrorCode!, auth.Message!);
  }
}
