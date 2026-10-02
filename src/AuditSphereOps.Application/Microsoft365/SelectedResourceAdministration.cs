using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record SelectedResourceDraft(Guid Id, string Revision, string State, string? TenantId,
  string? SiteUrl, string? SiteId, string? DriveId, string? RootFolderId, string AccessProfile,
  Guid? ConnectionRevisionId, string? ConnectionState, string? ConsentState);
public sealed record FolderTemplateSummary(Guid Id, string Purpose, string Version, string ManifestJson,
  string ManifestDigest, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt);
public sealed record SelectedResourceWorkspace(SelectedResourceDraft? Draft,
  IReadOnlyList<FolderTemplateSummary> Templates, string ClientDefaultManifest, string EngagementDefaultManifest,
  string ClientSteManifest, string EngagementSteManifest, SelectedResourceVerification? Verification);
public sealed record SelectedResourceVerification(string State, string DiagnosticCode, DateTimeOffset ObservedAt);
public sealed record SaveSelectedResourceRequest(Guid DraftId, long ExpectedRevision, string SiteUrl,
  string SiteId, string DriveId, string RootFolderId, string AccessProfile);

/// <summary>Bounded administrator projection. Credential references, callbacks and private proof data never leave Application.</summary>
public static class SelectedResourceAdministrationQuery
{
  public static async Task<CommandResult<SelectedResourceWorkspace>> GetAsync(IAuditSphereDbContext db,
    ActorContext actor, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<SelectedResourceWorkspace>();
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.UpdatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var connection = draft?.ConnectionRevisionId is { } id ? await db.Microsoft365ConnectionRevisions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct) : null;
    var templates = new List<FolderTemplateSummary>();
    foreach (var purpose in new[] { FolderTemplatePurposes.ClientWorkspace, FolderTemplatePurposes.EngagementWorkspace })
    {
      var rows = await db.FolderTemplateVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Purpose == purpose)
        .OrderByDescending(x => x.Version).Take(50).ToListAsync(ct);
      templates.AddRange(rows.Select(x => new FolderTemplateSummary(x.Id, x.Purpose, x.Version.ToString(CultureInfo.InvariantCulture),
        x.ManifestJson, x.ManifestDigest, x.CreatedAt, x.ApprovedAt)));
    }
    var latestVerification = draft is null ? null : await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.TenantId == draft.ExpectedTenantId && x.Capability == Microsoft365Capabilities.SelectedSite)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    var verification = latestVerification is null ? null : new SelectedResourceVerification(
      latestVerification.State == CapabilityVerificationStates.Verified && (draft?.ConnectionRevisionId is null || latestVerification.ConnectionRevisionId != draft.ConnectionRevisionId)
        ? CapabilityVerificationStates.BlockedExternal : latestVerification.State,
      latestVerification.State == CapabilityVerificationStates.Verified && (draft?.ConnectionRevisionId is null || latestVerification.ConnectionRevisionId != draft.ConnectionRevisionId)
        ? "selected-resource-connection-mismatch" : latestVerification.DiagnosticCode, latestVerification.ObservedAt);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<SelectedResourceWorkspace>();
    return CommandResult<SelectedResourceWorkspace>.Ok(new(draft is null ? null : new(draft.Id,
      draft.Revision.ToString(CultureInfo.InvariantCulture), draft.State, draft.ExpectedTenantId, draft.SiteUrl,
      draft.SiteId, draft.DriveId, draft.RootFolderId, draft.AccessProfile, draft.ConnectionRevisionId, connection?.State, connection?.ConsentState),
      templates, Microsoft365ConfigurationService.DefaultManifest(FolderTemplatePurposes.ClientWorkspace),
      Microsoft365ConfigurationService.DefaultManifest(FolderTemplatePurposes.EngagementWorkspace),
      Microsoft365ConfigurationService.SteManifest(FolderTemplatePurposes.ClientWorkspace),
      Microsoft365ConfigurationService.SteManifest(FolderTemplatePurposes.EngagementWorkspace), verification));
  }
}

public static partial class Microsoft365ConfigurationService
{
  public static async Task<CommandResult<Guid>> ActivateSelectedResourceAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid draftId, long expectedRevision, Guid templateId, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == draftId, ct);
    if (draft is null) return TenantAdministration.Denied<Guid>();
    if (draft.Revision != expectedRevision) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Reload the exact resource draft before activation.");
    return await ActivateConnectionAsync(db, actor, new(draft.Id, draft.ConnectionRevisionId ?? Guid.Empty, templateId,
      expectedRevision, draft.SiteId ?? "", draft.DriveId ?? "", draft.RootFolderId ?? "", draft.SiteUrl ?? "", draft.AccessProfile), now, ct);
  }

  /// <summary>Local metadata only; edit invalidates the selected-resource pass and keeps historical evidence intact.</summary>
  public static async Task<CommandResult<Guid>> SaveSelectedResourceAsync(IAuditSphereDbContext db, ActorContext actor,
    SaveSelectedResourceRequest request, string configuredTenantId, DateTimeOffset now, CancellationToken ct = default)
  {
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
    if (request.ExpectedRevision < 1 || !Guid.TryParse(configuredTenantId, out _) ||
        !Uri.TryCreate(request.SiteUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
        !uri.Host.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) ||
        !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
        request.SiteUrl.Length > 2000 || new[] { request.SiteId, request.DriveId, request.RootFolderId }.Any(x =>
          string.IsNullOrWhiteSpace(x) || x.Length > 2000 || x.Any(char.IsControl)) ||
        request.AccessProfile is not (Microsoft365AccessProfiles.AppMediated or Microsoft365AccessProfiles.DirectStaffCollaboration))
      return CommandResult<Guid>.Fail("m365.setup.invalid", "Provide the exact HTTPS SharePoint site, library, root and supported access profile.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var draft = await db.Microsoft365SetupDrafts.FromSqlInterpolated($"""
      SELECT * FROM m365_setup_drafts WHERE id = {request.DraftId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (draft is null || !string.Equals(draft.ExpectedTenantId, configuredTenantId, StringComparison.OrdinalIgnoreCase))
      return TenantAdministration.Denied<Guid>();
    if (draft.Revision != request.ExpectedRevision) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Reload and review the current draft revision.");
    if (draft.State is Microsoft365RevisionStates.Active or Microsoft365RevisionStates.Suspended or Microsoft365RevisionStates.Blocked)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "This configuration is protected. Prepare a separately reviewed replacement draft.");
    if (draft.ConnectionRevisionId is { } connectionId && await db.Microsoft365ConnectionRevisions.AsNoTracking()
        .AnyAsync(x => x.FirmId == actor.FirmId && x.Id == connectionId && x.State == Microsoft365RevisionStates.Active, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "An active connection cannot be edited in place.");
    var previous = TenantAdministration.Fingerprint(draft.SiteUrl, draft.SiteId, draft.DriveId, draft.RootFolderId, draft.AccessProfile);
    draft.SiteUrl = request.SiteUrl.Trim(); draft.SiteId = request.SiteId.Trim(); draft.DriveId = request.DriveId.Trim();
    draft.RootFolderId = request.RootFolderId.Trim(); draft.AccessProfile = request.AccessProfile;
    draft.State = draft.ConnectionRevisionId is null ? Microsoft365RevisionStates.Draft : Microsoft365RevisionStates.Validating;
    draft.Revision++; draft.UpdatedAt = now;
    db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification { Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      TenantId = draft.ExpectedTenantId!, ConnectionRevisionId = draft.ConnectionRevisionId, Capability = Microsoft365Capabilities.SelectedSite,
      Permission = "Sites.Selected", State = CapabilityVerificationStates.BlockedExternal, DiagnosticCode = "selected-resource-draft-edited",
      ObservedAt = now, ObservedByUserId = actor.UserId });
    TenantAdministration.AddEvent(db, actor, "SELECTED_RESOURCE_DRAFT_SAVED", now, oldState: previous,
      newState: TenantAdministration.Fingerprint(draft.SiteUrl, draft.SiteId, draft.DriveId, draft.RootFolderId, draft.AccessProfile),
      reason: "Administrator reviewed exact selected site, library, root and access profile", result: draft.Id.ToString(), targetTenantId: draft.ExpectedTenantId);
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(draft.Id);
  }
}
