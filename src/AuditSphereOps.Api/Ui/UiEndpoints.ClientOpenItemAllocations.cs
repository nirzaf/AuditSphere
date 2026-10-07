using System.Globalization;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record OpenItemAllocationLineHttpInput(int LineNumber, string TargetKind, Guid TargetOpenItemId, string Amount, Guid? ReversesAllocationLineId);
  public sealed record OpenItemAllocationHttpInput(Guid CommandId, string SourceKind, Guid SourceItemId, string Disposition,
    string Reference, string Reason, IReadOnlyList<OpenItemAllocationLineHttpInput> Lines, string PreviewDigest, bool Reviewed);
  public sealed record OpenItemAllocationReviewHttpInput(Guid CommandId, Guid SubmissionId, string Decision, string Reason,
    string PreviewDigest, bool Reviewed);

  private static void MapClientOpenItemAllocationEndpoints(RouteGroupBuilder group)
  {
    static IResult Failure(string? code) => Results.Json(new { code }, statusCode: code == "scope.denied" ? 403 : 400);
    static bool TryRequest(OpenItemAllocationHttpInput input, out ClientOpenItemAllocationRequest request)
    {
      request = new(input.CommandId, input.SourceKind, input.SourceItemId, input.Disposition, input.Reference, input.Reason, [], input.PreviewDigest);
      if (input.Lines.Count is < 1 or > 100) return false;
      var lines = new List<ClientOpenItemAllocationLineRequest>();
      foreach (var line in input.Lines)
      {
        if (!decimal.TryParse(line.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) || amount <= 0) return false;
        lines.Add(new(line.LineNumber, line.TargetKind, line.TargetOpenItemId, amount, line.ReversesAllocationLineId));
      }
      request = request with { Lines = lines };
      return true;
    }

    group.MapGet("/accounting/clients/{clientId:guid}/open-item-balances", async (Guid clientId, string asOf,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.BalancesAsync(db, actor, clientId, date, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<IReadOnlyList<ClientOpenItemBalance>>();

    group.MapGet("/accounting/clients/{clientId:guid}/open-item-control-reconciliation", async (Guid clientId,
      Guid periodId, string asOf, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      if (!DateOnly.TryParseExact(asOf, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.ReconcileControlAccountsAsync(db, actor, clientId, periodId, date, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientOpenItemControlReconciliationView>();

    group.MapGet("/accounting/clients/{clientId:guid}/open-item-allocation-submissions", async (Guid clientId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.PendingAsync(db, actor, clientId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<IReadOnlyList<ClientOpenItemAllocationPreview>>();

    group.MapPost("/accounting/clients/{clientId:guid}/open-item-allocations/preview", async (Guid clientId, OpenItemAllocationHttpInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!TryRequest(input, out var request)) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.PreviewAsync(db, actor, clientId, request, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientOpenItemAllocationPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/open-item-allocations/submit", async (Guid clientId, OpenItemAllocationHttpInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return Failure("request.invalid");
      if (!TryRequest(input, out var request)) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.SubmitAsync(db, actor, clientId, request, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientOpenItemAllocationReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/open-item-allocation-submissions/{submissionId:guid}/preview", async (Guid clientId, Guid submissionId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.ReviewPreviewAsync(db, actor, clientId, submissionId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientOpenItemAllocationPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/open-item-allocation-reviews", async (Guid clientId, OpenItemAllocationReviewHttpInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOpenItemAllocationWorkflow.ReviewAsync(db, actor, clientId, input.SubmissionId, input.CommandId,
        input.Decision, input.Reason, input.PreviewDigest, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientOpenItemAllocationReceipt>();
  }
}
