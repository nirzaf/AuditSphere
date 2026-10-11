using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record UiWorkspaceProvision(Guid TargetId, string? ReviewToken, string? Reason, bool Reviewed);

  private static void MapWorkspaceProvisioningEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/administration/microsoft365/workspaces", http => ReadAsync(http, async (db, actor, ct) =>
    {
      if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<object>();
      var page = 0;
      if (http.Request.Query.TryGetValue("page", out var raw) && !int.TryParse(raw, out page))
        return CommandResult<object>.Fail("request.invalid", "Use a bounded workspace page.");
      var result = await WorkspaceProvisioningAdministration.GetAsync(db, actor, page, ct);
      return result.Succeeded ? CommandResult<object>.Ok(new { workspace = result.Value,
        providerConfigured = http.RequestServices.GetRequiredService<ISelectedSiteWorkspaceProvisioner>().IsConfigured }) :
        CommandResult<object>.Fail(result.ErrorCode!, result.Message!);
    }));
    foreach (var engagement in new[] { false, true })
    {
      var kind = engagement ? "engagements" : "clients";
      group.MapUiGet($"/administration/microsoft365/workspaces/{kind}/{{id:guid}}/review", http => ReadAsync(http,
        (db, actor, ct) => WorkspaceProvisioningAdministration.ReviewAsync(db, actor,
          Guid.Parse(http.Request.RouteValues["id"]!.ToString()!), engagement, ct,
          ReadClientSiteRolloutPolicy(http.RequestServices.GetRequiredService<IConfiguration>()))));
      group.MapPost($"/administration/microsoft365/workspaces/{kind}/provision", (UiWorkspaceProvision i, HttpContext http) => CommandAsync(http, async (db, actor, ct) =>
      {
        if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct)) return TenantAdministration.Denied<WorkspaceProvisioningResult>();
        if (!i.Reviewed || i.ReviewToken is null || i.Reason is null)
          return CommandResult<WorkspaceProvisioningResult>.Fail("review.required", "Review the exact target, resource binding and templates and provide a reason before provisioning.");
        return await WorkspaceProvisioningAdministration.ProvisionAsync(db, actor,
          http.RequestServices.GetRequiredService<ISelectedSiteWorkspaceProvisioner>(), i.TargetId, engagement,
          i.ReviewToken, i.Reason, DateTimeOffset.UtcNow, ct,
          ReadClientSiteRolloutPolicy(http.RequestServices.GetRequiredService<IConfiguration>()));
      }));
    }
    group.MapUiGet("/administration/microsoft365/client-sites", http => ReadAsync(http, (db, actor, ct) =>
    {
      var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
      var policy = ReadClientSiteRolloutPolicy(configuration);
      return ClientSharePointSiteQuery.GetAdministrationAsync(db, actor, policy.Enabled,
        policy.ClientsCreatedAfter, policy.BoundaryInvalid, ct);
    }));
  }

  private static ClientSiteRolloutPolicy ReadClientSiteRolloutPolicy(IConfiguration configuration)
  {
    var enabled = configuration.GetValue("ClientSites:Enabled", true);
    var rawBoundary = configuration["ClientSites:ClientsCreatedAfter"];
    var parsed = DateTimeOffset.TryParse(rawBoundary, System.Globalization.CultureInfo.InvariantCulture,
      System.Globalization.DateTimeStyles.RoundtripKind, out var boundary);
    var validUtcBoundary = parsed && boundary.Offset == TimeSpan.Zero;
    var invalid = enabled && !string.IsNullOrWhiteSpace(rawBoundary) && !validUtcBoundary;
    return new(enabled, validUtcBoundary ? boundary.ToUniversalTime() : null, invalid);
  }
}
