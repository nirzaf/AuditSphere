using System.Globalization;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record UiResourceDraft(Guid DraftId, string? ExpectedRevision, string? SiteUrl,
    string? SiteId, string? DriveId, string? RootFolderId, string? AccessProfile, bool Reviewed);
  public sealed record UiTemplateSave(string? Purpose, string? ManifestJson, string? ExpectedVersion, bool Reviewed);
  public sealed record UiTemplateApproval(Guid TemplateId, string? ExpectedDigest, bool Reviewed);
  public sealed record UiResourceVerification(Guid DraftId, string? ExpectedRevision, bool Reviewed);
  public sealed record UiResourceActivation(Guid DraftId, string? ExpectedRevision, Guid TemplateId, bool Reviewed);

  private static void MapSelectedResourceEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/administration/microsoft365/resources", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var result = await SelectedResourceAdministrationQuery.GetAsync(db, actor, ct);
      if (!result.Succeeded) return CommandResult<object>.Fail(result.ErrorCode!, result.Message!);
      var config = http.RequestServices.GetRequiredService<IConfiguration>();
      var configured = new[] { "TenantId", "ClientId", "CertificatePath", "PrivateKeyPath", "CredentialReference", "NegativeControlSiteUrl" }
        .All(key => !string.IsNullOrWhiteSpace(config[$"SelectedSite:{key}"]));
      return CommandResult<object>.Ok(new { workspace = result.Value, probeConfigured = configured });
    }));
    group.MapPost("/administration/microsoft365/resources/save", (UiResourceDraft i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      if (!i.Reviewed || !Revision(i.ExpectedRevision, out var revision) || i.SiteUrl is null || i.SiteId is null ||
          i.DriveId is null || i.RootFolderId is null || i.AccessProfile is null)
        return CommandResult<Guid>.Fail("review.required", "Review the exact resource draft and its current revision before saving.");
      return await Microsoft365ConfigurationService.SaveSelectedResourceAsync(db, actor,
        new(i.DraftId, revision, i.SiteUrl, i.SiteId, i.DriveId, i.RootFolderId, i.AccessProfile),
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options.TenantId, DateTimeOffset.UtcNow, ct);
    }));
    group.MapPost("/administration/microsoft365/templates/save", (UiTemplateSave i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<FolderTemplateResult>();
      if (!i.Reviewed || i.Purpose is null || i.ManifestJson is null || !long.TryParse(i.ExpectedVersion, NumberStyles.None,
          CultureInfo.InvariantCulture, out var version) || version < 0)
        return CommandResult<FolderTemplateResult>.Fail("review.required", "Review the bounded folder manifest and current version before saving.");
      return await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor, new(i.Purpose, i.ManifestJson, version), DateTimeOffset.UtcNow, ct);
    }));
    group.MapPost("/administration/microsoft365/templates/approve", (UiTemplateApproval i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied();
      if (!i.Reviewed) return CommandResult.Fail("review.required", "Review the immutable folder template before approval.");
      var ws = await SelectedResourceAdministrationQuery.GetAsync(db, actor, ct);
      if (!ws.Succeeded) return CommandResult.Fail(ws.ErrorCode!, ws.Message!);
      var template = ws.Value!.Templates.SingleOrDefault(t => t.Id == i.TemplateId);
      if (template is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The template is unavailable.");
      if (template.ManifestDigest != i.ExpectedDigest) return CommandResult.Fail(ErrorCodes.StaleRevision, "Review the exact template digest before approval.");
      return await Microsoft365ConfigurationService.ApproveFolderTemplateAsync(db, actor, i.TemplateId, DateTimeOffset.UtcNow, ct);
    }));
    group.MapPost("/administration/microsoft365/resources/verify", (UiResourceVerification i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied();
      if (!i.Reviewed || !Revision(i.ExpectedRevision, out var revision)) return CommandResult.Fail("review.required", "Review the exact selected resource and draft revision before verification.");
      var ws = await SelectedResourceAdministrationQuery.GetAsync(db, actor, ct);
      if (!ws.Succeeded || ws.Value!.Draft?.Id != i.DraftId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The resource draft is unavailable.");
      if (ws.Value.Draft.Revision != i.ExpectedRevision) return CommandResult.Fail(ErrorCodes.StaleRevision, "Reload the exact resource draft before verification.");
      try
      {
        await http.RequestServices.GetRequiredService<SelectedSiteBoundaryVerificationService>().VerifyDraftAsync(actor, i.DraftId, ct, revision);
        return await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct) ? CommandResult.Ok() : TenantAdministration.Denied();
      }
      catch (OperationBlockedException) { return CommandResult.Fail(ErrorCodes.GateBlocked, "BLOCKED_EXTERNAL: verify Microsoft consent, the selected-site grant, exact resource IDs and the approved unrelated-site denial control."); }
      catch (SafeRetryException) { return CommandResult.Fail(ErrorCodes.GateBlocked, "Microsoft temporarily limited the check. Refresh persisted verification status before requesting it again."); }
    }));
    group.MapPost("/administration/microsoft365/resources/activate", (UiResourceActivation i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      if (!i.Reviewed || !Revision(i.ExpectedRevision, out var revision)) return CommandResult<Guid>.Fail("review.required", "Review the verified resource and approved client template before activation.");
      return await Microsoft365ConfigurationService.ActivateSelectedResourceAsync(db, actor, i.DraftId, revision, i.TemplateId, DateTimeOffset.UtcNow, ct);
    }));
  }

  private static bool Revision(string? value, out long revision) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision > 0;
}
