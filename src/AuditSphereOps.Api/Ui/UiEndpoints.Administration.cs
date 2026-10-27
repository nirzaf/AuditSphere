using AuditSphereOps.Application.Security;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Authentication;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record UiCopyableInvitationResponse(Guid Id, string RecipientEmail, string DestinationPath,
    string DeliveryState, string Link);
  /// <summary>Reviewed access preview returned by the access-preview command.</summary>
  public sealed record UiRoleAssignmentPreviewResponse(RoleAssignmentPreview Review, string Digest);
  /// <summary>Local user identity created by a manual roster binding.</summary>
  public sealed record UiManualBindingResponse(Guid UserId);

  public sealed record UiRoleSave(RoleAssignmentRequest Request, string ReviewDigest, bool Reviewed);
  public sealed record UiRoleRevocation(Guid GrantId, bool GroupGrant, string Reason, bool Reviewed);
  public sealed record UiRosterBind(string TenantId, string Subject, string Email, string DisplayName, string UserKind, string Source);
  private static bool ValidRoleRequest(RoleAssignmentRequest? request) => request is not null && request.UserId != Guid.Empty &&
    !string.IsNullOrWhiteSpace(request.Role) && request.Role.Length <= 100 &&
    request.ScopeKind is "FIRM_WIDE" or "CLIENT" or "ENGAGEMENT" or "GROUP" &&
    !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length is >= 5 and <= 1000;
  private static void MapAdministrationEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/administration/oversight-access", (DateTimeOffset? before, Guid? beforeId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdministratorOversightAccessEvidence.PageAsync(db, actor, before, beforeId, ct)))
      .WithUiResponse<AdministratorOversightAccessPage>();
    group.MapUiGet("/administration/runtime", http => ReadAsync(http, (db, actor, ct) =>
    {
      var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
      var environment = http.RequestServices.GetRequiredService<IHostEnvironment>();
      return FirmAdministrationQuery.RuntimeAsync(db, actor, new(environment.EnvironmentName,
        configuration.GetValue<bool>("ExternalEffects:Enabled"), configuration.GetValue<bool>("Application:AllowSimulationAdapters")), ct);
    })).WithUiResponse<AdministrationRuntimeStatus>();
    group.MapUiGet("/administration/access", http => ReadAsync(http, (db, actor, ct) => UserAccessWorkspaceQuery.GetAsync(db, actor, DateTimeOffset.UtcNow, ct)))
      .WithUiResponse<UserAccessWorkspace>();
    group.MapUiGet("/administration/role-catalogue", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var allowed = await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct);
      return allowed ? CommandResult<IReadOnlyList<string>>.Ok(RoleAdministrationService.RoleCodes) :
        CommandResult<IReadOnlyList<string>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    })).WithUiResponse<IReadOnlyList<string>>();
    group.MapUiGet("/administration/overview", http => ReadAsync(http, (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      return AdministrationOverviewQuery.GetAsync(db, actor, settings.Options, settings.MailSender, DateTimeOffset.UtcNow, ct);
    })).WithUiResponse<AdministrationOverview>();
    group.MapPost("/administration/access/preview", (RoleAssignmentRequest i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<object>();
        if (!ValidRoleRequest(i)) return CommandResult<object>.Fail("request.invalid", "Select an identity, role and scope, and record a reason.");
        var review = await RoleAssignmentService.PreviewAsync(db, actor, i, DateTimeOffset.UtcNow, ct);
        return review.Succeeded ? CommandResult<object>.Ok(new { review = review.Value, digest = RoleAssignmentReviewDigest.Compute(review.Value!, i) }) :
          CommandResult<object>.Fail(review.ErrorCode!, review.Message!);
      })).WithUiResponse<UiValueResponse<UiRoleAssignmentPreviewResponse>>();
    group.MapPost("/administration/access/assign", (UiRoleSave i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<RoleAssignmentResult>();
        if (!i.Reviewed || !ValidRoleRequest(i.Request) || i.ReviewDigest?.Length != 64 || !i.ReviewDigest.All(Uri.IsHexDigit))
          return CommandResult<RoleAssignmentResult>.Fail("review.required", "Review the proposed access first.");
        return await RoleAssignmentService.AssignAsync(db, actor, i.Request, DateTimeOffset.UtcNow, ct, i.ReviewDigest);
      })).WithUiResponse<UiValueResponse<RoleAssignmentResult>>(includeConflict: true);
    group.MapPost("/administration/access/revoke", (UiRoleRevocation i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => !i.Reviewed || string.IsNullOrWhiteSpace(i.Reason) || i.Reason.Length is < 5 or > 1000 ?
        Task.FromResult(CommandResult.Fail("review.required", "Review the revocation and record a reason.")) : i.GroupGrant ?
          RoleAssignmentService.RevokeGroupGrantAsync(db, actor, i.GrantId, i.Reason, DateTimeOffset.UtcNow, ct) :
          RoleAdministrationService.RevokeRoleGrantAsync(db, actor, new(i.GrantId, Reason: i.Reason), ct)))
      .WithUiResponse<UiValueResponse<bool>>();
    group.MapPost("/administration/access/assign-invitation", (UiRoleSave i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<object>();
        if (!i.Reviewed || !ValidRoleRequest(i.Request) || i.ReviewDigest?.Length != 64 || !i.ReviewDigest.All(Uri.IsHexDigit))
          return CommandResult<object>.Fail("review.required", "Review the proposed access first.");
        var applied = await RoleAssignmentService.AssignWithInvitationAsync(db, actor, i.Request, DateTimeOffset.UtcNow, i.ReviewDigest, ct);
        return applied.Succeeded
          ? CommandResult<object>.Ok(new { applied.Value!.UserId, applied.Value.RoleGrantId, applied.Value.InvitationId,
              applied.Value.DestinationPath, applied.Value.DeliveryState })
          : CommandResult<object>.Fail(applied.ErrorCode!, applied.Message!);
      })).WithUiResponse<UiValueResponse<UserAccessAssignmentResult>>(includeConflict: true);
    group.MapGet("/administration/access/invitations/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, async (db, actor, ct) =>
    {
      var copyable = await FirmAdministrationQuery.GetCopyableInvitationAsync(db, actor, id, ct);
      if (!copyable.Succeeded) return CommandResult<object>.Fail(copyable.ErrorCode!, copyable.Message!);
      var destination = copyable.Value!.DestinationPath;
      var configured = http.RequestServices.GetRequiredService<IConfiguration>()["Application:PublicBaseUrl"];
      if (Uri.TryCreate(configured, UriKind.Absolute, out var baseUri) && baseUri.Scheme is "http" or "https")
        destination = new Uri(baseUri, copyable.Value.DestinationPath.TrimStart('/')).AbsoluteUri;
      return CommandResult<object>.Ok(new { copyable.Value.Id, copyable.Value.RecipientEmail,
        copyable.Value.DestinationPath, copyable.Value.DeliveryState, Link = destination });
    })).WithUiResponse<UiCopyableInvitationResponse>();
    group.MapPost("/administration/access/invitations/{id:guid}/copied", (Guid id, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => RoleAdministrationService.MarkInvitationCopiedAsync(db, actor, id, ct)))
      .WithUiResponse<UiValueResponse<bool>>();
    group.MapPost("/administration/access/bind-manual", (UiRosterBind i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<object>();
        if (i.Source is not ("APPROVED_ROSTER" or "VERIFIED_SIGN_IN"))
          return CommandResult<object>.Fail("roles.invalid", "Record whether this exact identity came from an approved roster or a verified sign-in.");
        var bound = await RoleAdministrationService.EnsureUserAsync(db, actor,
          new(i.TenantId, i.Subject, i.Email, i.DisplayName, i.UserKind, i.Source), ct);
        return bound.Succeeded ? CommandResult<object>.Ok(new { userId = bound.Value })
          : CommandResult<object>.Fail(bound.ErrorCode!, bound.Message!);
      })).WithUiResponse<UiValueResponse<UiManualBindingResponse>>(includeConflict: true);
  }
}
