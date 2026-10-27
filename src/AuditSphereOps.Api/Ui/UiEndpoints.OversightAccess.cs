using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  // Server-owned registration, never a query-string/header permission switch.
  private sealed record OversightReadDescriptor(string Operation, string ResourceKind, string? RouteParameter);

  private static RouteHandlerBuilder WithOversightAccessEvidence(this RouteHandlerBuilder route,
    string operation, string kind, string? parameter = null) =>
    // Inherit the UI group's canonical ApiError 503 contract.
    route.WithMetadata(new OversightReadDescriptor(operation, kind, parameter));

  private static IResult OversightEvidenceUnavailable(HttpContext http, string? code = null, string? message = null) =>
    Results.Json(new ApiError(code ?? AdministratorOversightAccessEvidence.Unavailable,
      message ?? "Oversight access evidence could not be retained. Try again later.",
      http.Response.Headers["X-Correlation-Id"].ToString()), statusCode: 503);

  private static async Task<IResult?> RetainOversightAccessAsync(HttpContext http, AuditSphereDbContext queryDb, ActorContext actor)
  {
    if (!queryDb.AdministratorOversightObserved) return null;
    var descriptor = http.GetEndpoint()?.Metadata.GetMetadata<OversightReadDescriptor>();
    if (descriptor is null || !HttpMethods.IsGet(http.Request.Method))
      return OversightEvidenceUnavailable(http, message: "This read is not registered for oversight access evidence.");
    Guid? resourceId = null;
    if (descriptor.RouteParameter is { } parameter)
    {
      if (!Guid.TryParse(http.Request.RouteValues[parameter]?.ToString(), out var id) || id == Guid.Empty)
        return Failure(ErrorCodes.ScopeDenied, "Access denied.", 403);
      resourceId = id;
    }
    try
    {
      // A fresh context prevents this audit save from publishing query-owned tracked business edits.
      await using var auditDb = await http.RequestServices.GetRequiredService<IDbContextFactory<AuditSphereDbContext>>()
        .CreateDbContextAsync(http.RequestAborted);
      var requestId = Guid.CreateVersion7();
      var result = await AdministratorOversightAccessEvidence.RecordAsync(auditDb, actor, requestId,
        new(descriptor.Operation, descriptor.ResourceKind, resourceId), http.RequestAborted);
      if (result.Succeeded) http.Response.Headers["X-AuditSphere-Oversight-Request-Id"] = requestId.ToString("D");
      return result.Succeeded ? null : result.ErrorCode == ErrorCodes.ScopeDenied
        ? Failure(result.ErrorCode, result.Message, 403)
        : OversightEvidenceUnavailable(http, result.ErrorCode, result.Message);
    }
    catch (Exception error) when (error is DbUpdateException or DbException)
    {
      return OversightEvidenceUnavailable(http);
    }
  }
}
