using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapArchiveCopyEndpoints(RouteGroupBuilder group)
  {
    // These commands retain local intent/evidence only. Provider credentials and effects stay in client-sites.
    group.MapPost("/records/archive-copy-targets", (CreateArchiveCopyTargetRevisionRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ArchiveCopyService.CreateTargetRevisionAsync(db, actor, input, ct)))
      .WithUiResponse<UiValueResponse<ArchiveCopyTargetRevisionView>>(includeConflict: true);
    group.MapPost("/records/archive-copies", (EnqueueArchiveCopyRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ArchiveCopyService.EnqueueAsync(db,
        http.RequestServices.GetRequiredService<IOperationStore>(), actor, input, ct)))
      .WithUiResponse<UiValueResponse<ArchiveCopyQueued>>(includeConflict: true);
    group.MapGet("/records/archive-copies/{operationId:guid}/evidence", async (Guid operationId, HttpContext http,
      TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      db.AdministratorOversightObserved = false;
      var result = await ArchiveCopyService.ReadEvidenceAsync(db, actor, operationId, http.RequestAborted);
      if (!result.Succeeded)
        return Failure(result.ErrorCode, result.Message,
          result.ErrorCode == ErrorCodes.ScopeDenied || result.ErrorCode is null ? 403 :
          result.ErrorCode == ErrorCodes.ProtectedState ? 409 : StatusFor(result.ErrorCode));
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Failure("session.unavailable", "Sign in again.", 401);
      if (await RetainOversightAccessAsync(http, db, actor) is { } auditFailure) return auditFailure;
      return UiResult(result.Value);
    })
      .WithUiResponse<ArchiveCopyEvidenceReadback>(includeConflict: true);
    group.MapGet("/records/archive-copies/{operationId:guid}/verification-plan", (Guid operationId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ArchiveCopyService.GetVerificationPlanAsync(db,
        http.RequestServices.GetRequiredService<IReleaseCheckpointStore>(), actor, operationId, ct)))
      .WithUiResponse<ArchiveCopyVerificationPlan>(includeConflict: true);
  }
}
