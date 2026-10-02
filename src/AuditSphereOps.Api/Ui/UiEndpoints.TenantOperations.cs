using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Authentication;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record UiCreateTenantUser(CreateTenantUserRequest? Request, bool Reviewed, bool ReviewedPasswordHandling);
  public sealed record UiInviteGuest(InviteGuestRequest? Request, bool Reviewed);
  public sealed record UiResumeTenantOperation(bool Reviewed);
  public sealed record UiApproveManagedGroup(string GroupObjectId, string Purpose, string Reason, bool Reviewed);
  public sealed record UiGroupMemberPage(string? PageToken);
  public sealed record UiGroupMembershipPreview(Guid UserId);
  public sealed record UiChangeGroupMembership(ChangeGroupMembershipRequest? Request, bool Reviewed);
  public sealed record UiRetireManagedGroup(string Reason, bool Reviewed);

  private static void MapTenantOperationEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/administration/microsoft365/operations/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => DirectoryProvisioningService.ReviewOperationAsync(db, actor, id, ct)));
    group.MapPost("/administration/directory/create", (UiCreateTenantUser input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
      if (!input.Reviewed || !input.ReviewedPasswordHandling || input.Request is null)
        return CommandResult<ExternalOperationResult>.Fail("review.required", "Review the new identity, role, scope and one-time password handling before creation.");
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      var result = await DirectoryProvisioningService.CreateTenantUserAsync(db, actor,
        http.RequestServices.GetRequiredService<IMicrosoftDirectoryUserProvisioner>(), settings.Options, input.Request, DateTimeOffset.UtcNow, ct);
      // A temporary password is an explicitly reviewed one-time response only. Never return it to a stale actor.
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
      http.Response.Headers["Referrer-Policy"] = "no-referrer";
      return result;
    }));
    group.MapPost("/administration/directory/invite", (UiInviteGuest input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
      if (!input.Reviewed || input.Request is null) return CommandResult<ExternalOperationResult>.Fail("review.required", "Review the approved email, tenant, client and scope before inviting.");
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      var result = await DirectoryProvisioningService.InviteGuestAsync(db, actor,
        http.RequestServices.GetRequiredService<IMicrosoftGuestInvitationProvider>(), settings.Options, input.Request, DateTimeOffset.UtcNow, ct);
      return result.Succeeded ? CommandResult<ExternalOperationResult>.Ok(result.Value! with { TemporaryPassword = null }) : result;
    }));
    group.MapPost("/administration/microsoft365/operations/{id:guid}/resume", (Guid id, UiResumeTenantOperation input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<ExternalOperationResult>();
      if (!input.Reviewed) return CommandResult<ExternalOperationResult>.Fail("review.required", "Review the persisted operation before reconciliation or binding.");
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      var result = await DirectoryProvisioningService.ResumeAsync(db, actor, id, new(
        http.RequestServices.GetRequiredService<IMicrosoftDirectoryUserProvisioner>(),
        http.RequestServices.GetRequiredService<IMicrosoftGuestInvitationProvider>(),
        http.RequestServices.GetRequiredService<IMicrosoftGroupMembershipProvider>()), settings.Options, DateTimeOffset.UtcNow, ct);
      return result.Succeeded ? CommandResult<ExternalOperationResult>.Ok(result.Value! with { TemporaryPassword = null }) : result;
    }));
    group.MapPost("/administration/groups/approve", (UiApproveManagedGroup input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<Guid>();
      if (!input.Reviewed) return CommandResult<Guid>.Fail("review.required", "Review the group, purpose and reason before allowlisting.");
      return await ManagedGroupService.ApproveGroupAsync(db, actor, http.RequestServices.GetRequiredService<IMicrosoftGroupMembershipProvider>(),
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options, input.GroupObjectId, input.Purpose, input.Reason, DateTimeOffset.UtcNow, ct);
    }));
    group.MapPost("/administration/groups/{id:guid}/members", (Guid id, UiGroupMemberPage input, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ManagedGroupService.ListMembersAsync(db, actor, http.RequestServices.GetRequiredService<IMicrosoftGroupMembershipProvider>(),
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options, id, input.PageToken, DateTimeOffset.UtcNow, ct)));
    group.MapPost("/administration/groups/{id:guid}/preview", (Guid id, UiGroupMembershipPreview input, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => ManagedGroupService.PreviewMembershipAsync(db, actor, http.RequestServices.GetRequiredService<IMicrosoftGroupMembershipProvider>(),
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options, id, input.UserId, DateTimeOffset.UtcNow, ct)));
    group.MapPost("/administration/groups/change", (UiChangeGroupMembership input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<GroupMembershipChangeResult>();
      if (!input.Reviewed || input.Request?.ExpectedExistingMembership is null)
        return CommandResult<GroupMembershipChangeResult>.Fail("review.required", "Review the exact group, user and current membership before changing it.");
      return await ManagedGroupService.ChangeMembershipAsync(db, actor, http.RequestServices.GetRequiredService<IMicrosoftGroupMembershipProvider>(),
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options, input.Request, DateTimeOffset.UtcNow, ct);
    }));
    group.MapPost("/administration/groups/{id:guid}/retire", (Guid id, UiRetireManagedGroup input, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied();
      if (!input.Reviewed) return CommandResult.Fail("review.required", "Review the retirement reason. Existing Microsoft memberships are not removed by retiring this allowlist entry.");
      return await ManagedGroupService.RetireGroupAsync(db, actor, id, input.Reason, DateTimeOffset.UtcNow, ct);
    }));
  }
}
