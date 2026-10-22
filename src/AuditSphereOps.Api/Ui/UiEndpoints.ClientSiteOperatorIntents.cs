using AuditSphereOps.Application.Documents;
using AuditSphereOps.Api.Contracts;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  /// <summary>Assent to one current server-owned client-site intent; no resource or staff inputs.</summary>
  public sealed record UiClientSiteIntentInput(string ReviewToken, bool Reviewed, string Reason, bool ReconcileOnly);

  private static void MapClientSiteOperatorIntentEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/administration/microsoft365/client-sites/{id:guid}/review", http => ReadAsync(http, (db, actor, ct) =>
      ClientSiteOperatorIntents.ReviewAsync(db, actor, Guid.Parse(http.Request.RouteValues["id"]!.ToString()!),
        ReadClientSiteOperatorPolicy(http.RequestServices.GetRequiredService<IConfiguration>()), false, ct)))
      .WithUiResponse<ClientSiteOperatorReview>();
    group.MapUiGet("/administration/microsoft365/client-sites/{id:guid}/staff-review", http => ReadAsync(http, (db, actor, ct) =>
      ClientSiteOperatorIntents.ReviewAsync(db, actor, Guid.Parse(http.Request.RouteValues["id"]!.ToString()!),
        ReadClientSiteOperatorPolicy(http.RequestServices.GetRequiredService<IConfiguration>()), true, ct)))
      .WithUiResponse<ClientSiteOperatorReview>();
    group.MapPost("/administration/microsoft365/client-sites/{id:guid}/request", (Guid id, UiClientSiteIntentInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ClientSiteOperatorIntents.RequestAsync(db, actor, id,
        ReadClientSiteOperatorPolicy(http.RequestServices.GetRequiredService<IConfiguration>()), input.ReviewToken,
        input.Reviewed, input.Reason, input.ReconcileOnly, ct)))
      .WithUiResponse<UiValueResponse<ClientSiteIntentRequestReceipt>>(includeConflict: true);
  }

  private static ClientSitePolicy ReadClientSiteOperatorPolicy(IConfiguration configuration)
  {
    var rollout = ReadClientSiteRolloutPolicy(configuration);
    return new(rollout.Enabled && !rollout.BoundaryInvalid,
      configuration["ClientSites:TenantId"] ?? configuration["Identity:TenantId"] ?? string.Empty,
      configuration["ClientSites:SiteHost"] ?? string.Empty, Guid.Empty, rollout.ClientsCreatedAfter);
  }
}
