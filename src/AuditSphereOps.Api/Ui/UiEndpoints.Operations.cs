using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Services;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record OperationCancelInput(string Disposition);

  private static void MapOperationsEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/operations", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var mode = await OperationRecoveryService.GetFirmOperatingModeAsync(db, actor, ct);
      if (!mode.Succeeded) return CommandResult<object>.Fail(mode.ErrorCode!, mode.Message!);
      var listed = await OperationRecoveryService.ListAsync(db, actor, ct);
      if (!listed.Succeeded) return CommandResult<object>.Fail(listed.ErrorCode!, listed.Message!);
      return CommandResult<object>.Ok(new
      {
        OperatingMode = mode.Value,
        RetryableStates = OperationRecoveryService.RetryableStates.Select(x => x.ToString()),
        Operations = listed.Value!.Select(o => new { o.Id, o.Kind, o.Status, o.AttemptCount, o.NextAttemptAt, o.ErrorCode, o.CancellationDisposition }),
      });
    }));
    group.MapPost("/operations/{id:guid}/retry", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => OperationRecoveryService.RetryAsync(db, actor, new OperationRecoveryRequest(id), ct)));
    group.MapPost("/operations/{id:guid}/cancel", (Guid id, OperationCancelInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => OperationRecoveryService.CancelAsync(db, actor, new OperationCancellationRequest(id, i.Disposition ?? ""), ct)));
    group.MapUiPost("/operations/lift-quarantine", http => CommandAsync(http, (db, actor, ct) => OperationRecoveryService.LiftQuarantineAsync(db, actor, ct)));

    // The tracker is a published task-card snapshot shipped with the build; it reports card status, never product readiness.
    group.MapUiGet("/administration/project-progress", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var admin = await FirmAdministrationQuery.GetAsync(db, actor, ct);
      if (!admin.Succeeded) return CommandResult<object>.Fail(admin.ErrorCode!, admin.Message!);
      try
      {
        var snapshot = ProjectProgressReader.Read(Path.Combine(AppContext.BaseDirectory, "project-progress"));
        return CommandResult<object>.Ok(new { Snapshot = snapshot, Untracked = ProjectProgressReader.UntrackedAreas });
      }
      catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
      {
        return CommandResult<object>.Fail("progress.unavailable", "The published task-card snapshot could not be read or is invalid.");
      }
    }));
  }
}
