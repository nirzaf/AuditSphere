using AuditSphereOps.Application.Security;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  // Self-service only: both routes act on the signed-in user and take no user identifier.
  private static void MapUserProfileEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/me/profile", http =>
      ReadAsync(http, (db, actor, ct) => UserProfileService.GetAsync(db, actor, ct)))
      .WithT046Response<UserProfileView>();

    group.MapPut("/me/profile", (UpdateUserProfileRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => UserProfileService.UpdateAsync(db, actor, input, ct)))
      .WithT046Response<UiValueResponse<UserProfileView>>(includeConflict: true);
  }
}
