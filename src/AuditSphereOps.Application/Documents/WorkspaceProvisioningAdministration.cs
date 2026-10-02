using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record WorkspaceTargetReview(Guid TargetId, string Kind, string Name, string CurrentState,
  string ReviewToken, bool Eligible, string RequiredAction, string? TenantId, string? SiteId, string? DriveId,
  string? RootFolderId, Guid? ClientTemplateId, Guid? EngagementTemplateId);
public sealed record WorkspaceAdministrationPage(int Page, bool HasMore, IReadOnlyList<ClientWorkspaceRow> Clients);

/// <summary>Existing selected-resource provisioning, reviewed against exact local inputs; no browser tenant/resource authority.</summary>
public static class WorkspaceProvisioningAdministration
{
  public static async Task<CommandResult<WorkspaceAdministrationPage>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    int page, CancellationToken ct = default)
  {
    var listed = await PbcRepositoryProvisioningQuery.GetAsync(db, actor, ct, page, 25);
    if (!listed.Succeeded) return CommandResult<WorkspaceAdministrationPage>.Fail(listed.ErrorCode!, listed.Message!);
    var clients = listed.Value!.Select(x => x with { Engagements = x.Engagements.Take(25).ToArray() }).ToArray();
    var count = await (from workspace in db.ClientWorkspaces.AsNoTracking()
      join client in db.PracticeClients.AsNoTracking() on new { workspace.FirmId, Id = workspace.PracticeClientId } equals new { client.FirmId, client.Id }
      where workspace.FirmId == actor.FirmId && workspace.Purpose == "PRIMARY" select workspace.Id).CountAsync(ct);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceAdministrationPage>();
    return CommandResult<WorkspaceAdministrationPage>.Ok(new(page, count > (page + 1) * 25, clients));
  }

  public static async Task<CommandResult<WorkspaceTargetReview>> ReviewAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid targetId, bool engagement, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceTargetReview>();
    var target = engagement ? await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == targetId, ct) : null;
    if (engagement && target is null) return TenantAdministration.Denied<WorkspaceTargetReview>();
    var clientId = target?.PracticeClientId ?? targetId;
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == clientId, ct);
    var workspace = await db.ClientWorkspaces.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Purpose == "PRIMARY", ct);
    if (client is null || workspace is null) return TenantAdministration.Denied<WorkspaceTargetReview>();
    var accepted = await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x => x.Id == workspace.AcceptanceDecisionId && x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Decision == "Accepted", ct);
    var active = await PbcRepositoryProvisioningService.ActiveSiteAsync(db, actor.FirmId, ct, clientId);
    var template = engagement ? await db.FolderTemplateVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Purpose == FolderTemplatePurposes.EngagementWorkspace && x.ApprovedAt != null)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct) : null;
    var binding = target is null ? null : await db.RepositoryBindings.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == target.Id && x.Classification == "working" && x.CapabilityProfile == "selected-site").FirstOrDefaultAsync(ct);
    var capability = binding is null ? null : await db.IntegrationCapabilities.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.RepositoryBindingId == binding.Id, ct);
    var config = active.Succeeded ? active.Value.Config : null;
    var connection = active.Succeeded ? active.Value.Connection : null;
    var clientTemplate = active.Succeeded ? active.Value.Template : null;
    var eligible = accepted && active.Succeeded && (!engagement || workspace.State == "READY" && workspace.RemoteItemId != null &&
      workspace.ConnectionRevisionId == connection?.Id && workspace.DriveId == config?.DriveId && template is not null &&
      PbcRepositoryProvisioningService.FindNode(template.ManifestJson, PbcRepositoryProvisioningService.PbcNodeKey) is not null);
    var action = !accepted ? "Complete an Accepted client decision before provisioning." : !active.Succeeded ? active.Message! :
      engagement && workspace.State != "READY" ? "Provision the client workspace first." : !eligible ? "Review the active connection and approve an engagement template containing a PBC folder." :
      "Review this exact target and resource binding before provisioning. Folder names are idempotent; a lost response requires persisted-state review.";
    var siblings = target is null ? [] : await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PracticeClientId == clientId && x.Id != targetId).ToListAsync(ct);
    var targetFolderName = target is null ? null : EngagementWorkspaceProvisioningHandler.SteEngagementName(target, siblings);
    var token = TenantAdministration.Fingerprint(actor.FirmId.ToString(), targetId.ToString(), engagement.ToString(), workspace.Id.ToString(),
      workspace.Revision.ToString(CultureInfo.InvariantCulture), workspace.AcceptanceDecisionId.ToString(), accepted.ToString(),
      workspace.RemoteItemId, workspace.State, client.LegalName, client.CommercialName, target?.Generation.ToString(CultureInfo.InvariantCulture),
      target?.PeriodEnd, target?.ServiceRoute, target?.CreatedAt.ToString("O"), targetFolderName, connection?.Id.ToString(), connection?.State, connection?.ConsentState, connection?.RuntimeCredentialReference,
      config?.Id.ToString(), config?.TenantId, config?.SiteId, config?.DriveId, config?.RootFolderId, config?.AccessProfile,
      clientTemplate?.Id.ToString(), clientTemplate?.ManifestDigest, template?.Id.ToString(), template?.ManifestDigest,
      binding?.Id.ToString(), binding?.RootFolderId, binding?.ObservedAccess, capability?.HealthStatus, capability?.TestedAt?.ToString("O"));
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceTargetReview>();
    return CommandResult<WorkspaceTargetReview>.Ok(new(targetId, engagement ? "ENGAGEMENT" : "CLIENT", engagement ?
      string.Join(" · ", new[] { client.CommercialName ?? client.LegalName, target!.ServiceRoute, target.PeriodEnd }) : client.CommercialName ?? client.LegalName,
      engagement ? capability?.HealthStatus ?? "NOT_PROVISIONED" : workspace.State, token, eligible, action,
      config?.TenantId, config?.SiteId, config?.DriveId, config?.RootFolderId, clientTemplate?.Id, template?.Id));
  }

  public static async Task<CommandResult<WorkspaceProvisioningResult>> ProvisionAsync(IAuditSphereDbContext db, ActorContext actor,
    ISelectedSiteWorkspaceProvisioner provider, Guid targetId, bool engagement, string reviewToken, string reason, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
    if (string.IsNullOrWhiteSpace(reason) || reason.Length > 1000 || reviewToken.Length != 64 || !reviewToken.All(Uri.IsHexDigit))
      return CommandResult<WorkspaceProvisioningResult>.Fail("review.required", "Review the exact target and provide a bounded reason.");
    var current = await ReviewAsync(db, actor, targetId, engagement, ct);
    if (!current.Succeeded) return CommandResult<WorkspaceProvisioningResult>.Fail(current.ErrorCode!, current.Message!);
    if (current.Value!.ReviewToken != reviewToken) return CommandResult<WorkspaceProvisioningResult>.Fail(ErrorCodes.StaleRevision, "The workspace, resource binding or templates changed. Refresh and review again.");
    if (!current.Value.Eligible) return CommandResult<WorkspaceProvisioningResult>.Fail(ErrorCodes.GateBlocked, current.Value.RequiredAction);
    return engagement ? await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, actor, provider, targetId, now, ct, reviewToken, reason) :
      await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, actor, provider, targetId, now, ct, reviewToken, reason);
  }
}
