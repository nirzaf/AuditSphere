using AuditSphereOps.Application.Security;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Authentication;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record UiRoleSave(RoleAssignmentRequest Request, string ReviewDigest, bool Reviewed);
  public sealed record UiRoleRevocation(Guid GrantId, bool GroupGrant, string Reason, bool Reviewed);
  private static bool ValidRoleRequest(RoleAssignmentRequest? request) => request is not null && request.UserId != Guid.Empty &&
    !string.IsNullOrWhiteSpace(request.Role) && request.Role.Length <= 100 &&
    request.ScopeKind is "FIRM_WIDE" or "CLIENT" or "ENGAGEMENT" or "GROUP" &&
    !string.IsNullOrWhiteSpace(request.Reason) && request.Reason.Length is >= 5 and <= 1000;
  private static void MapAdministrationEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/administration/access", http => ReadAsync(http, (db, actor, ct) => UserAccessWorkspaceQuery.GetAsync(db, actor, DateTimeOffset.UtcNow, ct)));
    group.MapUiGet("/administration/role-catalogue", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var allowed = await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct);
      return allowed ? CommandResult<IReadOnlyList<string>>.Ok(RoleAdministrationService.RoleCodes) :
        CommandResult<IReadOnlyList<string>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }));
    group.MapUiGet("/administration/overview", http => ReadAsync(http, (db, actor, ct) =>
    {
      var settings = http.RequestServices.GetRequiredService<TenantAdministrationSettings>();
      return AdministrationOverviewQuery.GetAsync(db, actor, settings.Options, settings.MailSender, DateTimeOffset.UtcNow, ct);
    }));
    group.MapPost("/administration/access/preview", (RoleAssignmentRequest i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<object>();
        if (!ValidRoleRequest(i)) return CommandResult<object>.Fail("request.invalid", "Select an identity, role and scope, and record a reason.");
        var review = await RoleAssignmentService.PreviewAsync(db, actor, i, DateTimeOffset.UtcNow, ct);
        return review.Succeeded ? CommandResult<object>.Ok(new { review = review.Value, digest = RoleAssignmentReviewDigest.Compute(review.Value!, i) }) :
          CommandResult<object>.Fail(review.ErrorCode!, review.Message!);
      }));
    group.MapPost("/administration/access/assign", (UiRoleSave i, HttpContext http) => CommandAsync(http,
      async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<RoleAssignmentResult>();
        if (!i.Reviewed || !ValidRoleRequest(i.Request) || i.ReviewDigest?.Length != 64 || !i.ReviewDigest.All(Uri.IsHexDigit))
          return CommandResult<RoleAssignmentResult>.Fail("review.required", "Review the proposed access first.");
        return await RoleAssignmentService.AssignAsync(db, actor, i.Request, DateTimeOffset.UtcNow, ct, i.ReviewDigest);
      }));
    group.MapPost("/administration/access/revoke", (UiRoleRevocation i, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => !i.Reviewed || string.IsNullOrWhiteSpace(i.Reason) || i.Reason.Length is < 5 or > 1000 ?
        Task.FromResult(CommandResult.Fail("review.required", "Review the revocation and record a reason.")) : i.GroupGrant ?
          RoleAssignmentService.RevokeGroupGrantAsync(db, actor, i.GrantId, i.Reason, DateTimeOffset.UtcNow, ct) :
          RoleAdministrationService.RevokeRoleGrantAsync(db, actor, new(i.GrantId, Reason: i.Reason), ct)));
  }
}
