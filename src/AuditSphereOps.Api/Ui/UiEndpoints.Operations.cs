using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Api.Services;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record OperationCancelInput(string Disposition);

  private static void MapOperationsEndpoints(RouteGroupBuilder group)
  {
    MapOperationCommandEndpoints(group);
    group.MapUiGet("/operations", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var workspace = await OperationRecoveryService.WorkspaceAsync(db, actor, ct);
      if (!workspace.Succeeded) return CommandResult<object>.Fail(workspace.ErrorCode!, workspace.Message!);
      return CommandResult<object>.Ok(new
      {
        OperatingMode = workspace.Value!.OperatingMode,
        RetryableStates = OperationRecoveryService.RetryableStates.Select(x => x.ToString()),
        Operations = workspace.Value!.Operations.Select(o => new { o.Id, o.Kind, o.Status, o.AttemptCount, o.NextAttemptAt, o.ErrorCode, o.CancellationDisposition }),
      });
    }));
    // Bounded discovery beyond the latest-50 overview; rows are redacted the same way and grant no recovery authority.
    group.MapGet("/operations/discovery", (string? state, Guid? operationId, string? cursor, int? pageSize, HttpContext http) =>
      ReadAsync(http, async (db, actor, ct) =>
      {
        var page = await OperationRecoveryService.DiscoverAsync(db, actor,
          new(state, operationId, cursor, pageSize ?? OperationRecoveryService.DiscoveryDefaultPageSize), ct);
        if (!page.Succeeded) return CommandResult<object>.Fail(page.ErrorCode!, page.Message!);
        return CommandResult<object>.Ok(new
        {
          page.Value!.State,
          page.Value.OperationId,
          Operations = page.Value.Operations.Select(o => new { o.Id, o.Kind, o.Status, o.AttemptCount, o.NextAttemptAt, o.ErrorCode, o.CancellationDisposition, o.CreatedAt }),
          page.Value.NextCursor,
          page.Value.FirstPageCursor,
        });
      }));
    // Keep the legacy URLs as aliases, but never let them bypass the preview, fresh assent and receipt.
    group.MapPost("/operations/{id:guid}/retry", (Guid id, OperationCommandRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ExecuteReviewedAliasAsync(db, actor, input, OperationCommandActions.Retry, id, ct)));
    group.MapPost("/operations/{id:guid}/cancel", (Guid id, OperationCommandRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ExecuteReviewedAliasAsync(db, actor, input, OperationCommandActions.Cancel, id, ct)));
    group.MapPost("/operations/lift-quarantine", (OperationCommandRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ExecuteReviewedAliasAsync(db, actor, input, OperationCommandActions.LiftQuarantine, null, ct)));

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
      catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
      {
        return CommandResult<object>.Fail("progress.unavailable", "The published task-card snapshot could not be read or is invalid.");
      }
    }));
  }

  private static async Task<CommandResult<OperationCommandReceiptView>> ExecuteReviewedAliasAsync(
    AuditSphereDbContext db, ActorContext actor, OperationCommandRequest? request,
    string expectedAction, Guid? pathOperationId, CancellationToken ct)
  {
    var state = await OperationCommandWorkspace.StateAsync(db, actor, pathOperationId, null, ct);
    if (!state.Succeeded) return CommandResult<OperationCommandReceiptView>.Fail(state.ErrorCode!, state.Message!);
    if (request?.Input is null || request.Input.Action != expectedAction || request.Input.OperationId != pathOperationId)
      return CommandResult<OperationCommandReceiptView>.Fail(ErrorCodes.StaleRevision,
        "Recovery actions require a current preview, explicit review assent, and an immutable command receipt.");
    return await OperationCommandWorkspace.ExecuteAsync(db, actor, request, ct);
  }
}
