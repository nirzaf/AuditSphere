using AuditSphereOps.Application.Accounting;
using System.Text;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  private static void MapJournalReviewEndpoints(RouteGroupBuilder group)
  {
    MapJournalManagementEndpoints(group);
    group.MapGet("/datasets/{id:guid}/journal-drafts", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.GetCreationAsync(db, actor, id, ct)));
    group.MapGet("/datasets/{id:guid}/journal-drafts/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.LookupCreationAsync(db, db, actor, id, requestId, requestHash, ct)));
    group.MapPost("/datasets/{id:guid}/journal-drafts/preview", (Guid id, JournalCreationRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.PreviewCreationAsync(db, actor, id, input, ct)));
    group.MapPost("/datasets/{id:guid}/journal-drafts", (Guid id, JournalCreationRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.CreateAsync(db, db, actor, id, input, ct)));
    group.MapPost("/accounting/journals/{id:guid}/workspace/instructions", async (Guid id, JournalExportInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var export = await AdjustmentJournalWorkspace.ExportAsync(db, db, actor, id, input.ReviewBasis, http.RequestAborted);
      if (!export.Succeeded) return Failure(export.ErrorCode, export.Message);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Journal-Revision"] = export.Value!.JournalRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
      http.Response.Headers["X-Journal-Id"] = id.ToString("D");
      http.Response.Headers["X-Journal-Basis"] = input.ReviewBasis;
      return Results.File(Encoding.UTF8.GetBytes(export.Value.Csv), "text/csv; charset=utf-8", export.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
    group.MapGet("/accounting/journals/{id:guid}/workspace", (Guid id, int? historyPage, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.GetAsync(db, db, actor, id, historyPage ?? 1, ct)));
    group.MapGet("/accounting/journals/{id:guid}/receipts/{requestId:guid}", (Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.LookupAsync(db, db, actor, id, requestId, requestHash, ct)));
    group.MapGet("/accounting/journals/{id:guid}/history/{eventId:guid}", (Guid id, Guid eventId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.HistoryAsync(db, db, actor, id, eventId, ct)));
    group.MapPost("/accounting/journals/{id:guid}/preview", (Guid id, JournalActionRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.PreviewAsync(db, db, actor, id, input, ct)));
    group.MapPost("/accounting/journals/{id:guid}/actions", (Guid id, JournalActionRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.ExecuteAsync(db, db, actor, id, input, ct)));
    group.MapPost("/accounting/journals/{id:guid}/source-reflection", (Guid id, AdjustmentJournalWorkspace.JournalSourceReflectionRequest? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdjustmentJournalWorkspace.ReconcileSourceReflectionAsync(db, db, actor, id, input, ct)));
  }
  public sealed record JournalExportInput(string ReviewBasis);
}
