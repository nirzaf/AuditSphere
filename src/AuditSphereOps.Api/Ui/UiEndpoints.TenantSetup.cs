using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Api.Authentication;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapTenantSetupEndpoints(RouteGroupBuilder group)
  {
    const string root = "/administration/microsoft365/setup";
    group.MapPost(root + "/preview", (TenantSetupEditRequest input, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => TenantSetupMetadataWorkspace.PreviewAsync(db, actor, input,
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options.TenantId, ct)));
    group.MapPost(root + "/commands", (TenantSetupEditRequest input, HttpContext http) => CommandAsync(http,
      (db, actor, ct) => TenantSetupMetadataWorkspace.SaveAsync(db, actor, input,
        http.RequestServices.GetRequiredService<TenantAdministrationSettings>().Options.TenantId, DateTimeOffset.UtcNow, ct)));
    group.MapGet(root + "/receipts/{requestId:guid}", (Guid requestId, string requestHash, HttpContext http) => ReadAsync(http,
      (db, actor, ct) => TenantSetupMetadataWorkspace.LookupAsync(db, actor, requestId, requestHash, ct)));
  }
}
