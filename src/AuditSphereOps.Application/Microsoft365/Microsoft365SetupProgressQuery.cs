using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record Microsoft365SetupProgress(
  bool TenantRecorded,
  bool SiteUrlRecorded,
  bool ConnectionPrepared,
  bool ConsentEvidenceRecorded,
  bool SelectedResourcesEvidenceRecorded,
  bool ClientTemplateApproved,
  bool WorkspaceActivated);

/// <summary>Reads local setup evidence only; it never probes Microsoft or asserts live access.</summary>
public static class Microsoft365SetupProgressQuery
{
  public static async Task<CommandResult<Microsoft365SetupProgress>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid draftId, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
        InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Microsoft365SetupProgress>.Fail(auth.ErrorCode!, auth.Message!);

    var draft = await db.Microsoft365SetupDrafts.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == draftId && x.FirmId == actor.FirmId, ct);
    if (draft is null)
      return CommandResult<Microsoft365SetupProgress>.Fail(ErrorCodes.ScopeDenied, "The setup draft is unavailable.");

    var connection = draft.ConnectionRevisionId is { } connectionId
      ? await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == connectionId && x.FirmId == actor.FirmId && x.TenantId == draft.ExpectedTenantId, ct)
      : null;
    var evidence = connection is null ? [] : await db.IntegrationVerificationEvidences.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.SetupDraftId == draft.Id &&
        x.ConnectionRevisionId == connection.Id && x.Result == "PASS")
      .Select(x => new { x.ResourceKind, x.ResourceId, x.Operation })
      .ToListAsync(ct);
    var consent = evidence.Any(x => x.ResourceKind == "TENANT" && x.Operation == "CONSENT" &&
      x.ResourceId == draft.ExpectedTenantId);
    var resources = new[] { ("SITE", draft.SiteId), ("DRIVE", draft.DriveId), ("ROOT", draft.RootFolderId) }
      .All(required => !string.IsNullOrWhiteSpace(required.Item2) &&
        evidence.Any(x => x.ResourceKind == required.Item1 && x.ResourceId == required.Item2));
    var template = await db.FolderTemplateVersions.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.Purpose == FolderTemplatePurposes.ClientWorkspace &&
      x.ApprovedAt != null, ct);
    var active = connection is not null && connection.State == Microsoft365RevisionStates.Active &&
      await db.FirmWorkspaceConfigurations.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ConnectionRevisionId == connection.Id && x.TenantId == connection.TenantId &&
        x.SiteId == draft.SiteId && x.DriveId == draft.DriveId && x.RootFolderId == draft.RootFolderId, ct);

    return CommandResult<Microsoft365SetupProgress>.Ok(new(
      !string.IsNullOrWhiteSpace(draft.ExpectedTenantId),
      !string.IsNullOrWhiteSpace(draft.SiteUrl), connection is not null,
      consent, resources, template, active));
  }
}
