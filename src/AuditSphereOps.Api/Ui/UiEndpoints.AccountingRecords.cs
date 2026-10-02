using System.Text;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Api.Authentication;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record TrialBalanceExportInput(long Revision);
  private static void MapAccountingRecordEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/datasets/{id:guid}/source", (Guid id, string? filter, int? page, int? issuePage, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => TrialBalanceSourceWorkspace.GetAsync(db, actor, id, filter, page ?? 1, issuePage ?? 1, ct)));
    group.MapPost("/datasets/{id:guid}/source/export", async (Guid id, TrialBalanceExportInput input, HttpContext http,
      TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      if (input.Revision < 1) return Invalid("Load a current source revision before exporting.");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var export = await TrialBalanceDatasetQuery.ExportTrialBalanceCsvAsync(db, actor, id, http.RequestAborted, input.Revision);
      if (!export.Succeeded || export.Value is null) return Failure(export.ErrorCode, export.Message);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Failure("session.unavailable", "Sign in again.", 401);
      http.Response.Headers["X-Dataset-Id"] = id.ToString("D");
      http.Response.Headers["X-Dataset-Revision"] = export.Value.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture);
      return Results.File(Encoding.UTF8.GetBytes(export.Value.Csv), "text/csv; charset=utf-8", export.Value.FileName);
    });
    group.MapUiGet("/accounting/mappings", http => ReadAsync(http, (db, actor, ct) => AccountingRecordsQuery.MappingsAsync(db, actor, ct)));
    group.MapUiGet("/accounting/journals", http => ReadAsync(http, (db, actor, ct) => AccountingRecordsQuery.JournalsAsync(db, actor, ct)));
    group.MapUiGet("/accounting/differences", http => ReadAsync(http, (db, actor, ct) => AccountingRecordsQuery.DifferencesAsync(db, actor, ct)));
    group.MapGet("/accounting/journals/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AccountingRecordsQuery.JournalAsync(db, actor, id, ct)));
    group.MapPost("/accounting/journals/{id:guid}/post", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AdjustmentJournalService.PostAsync(db, actor, id, ct)));
    // The export rechecks source, scope and management evidence; it is an instruction file only, never proof of posting.
    group.MapPost("/accounting/journals/{id:guid}/instructions", async (Guid id, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Failure("session.unavailable", "Sign in again.", 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Failure("csrf.invalid", "Refresh the page and try again.", 403); }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var export = await AdjustmentJournalService.BuildInstructionExportAsync(db, actor, id, http.RequestAborted);
      if (!export.Succeeded || export.Value is null) return Failure(export.ErrorCode, export.Message);
      http.Response.Headers["X-Journal-Revision"] = export.Value.JournalRevision.ToString(System.Globalization.CultureInfo.InvariantCulture);
      return Results.File(Encoding.UTF8.GetBytes(export.Value.Csv), "text/csv; charset=utf-8", export.Value.FileName);
    });
  }
}
