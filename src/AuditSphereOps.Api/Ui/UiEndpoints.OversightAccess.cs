using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
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
    route.WithMetadata(new OversightReadDescriptor(operation, kind, parameter))
      .Produces<UiFailureResponse>(StatusCodes.Status503ServiceUnavailable);

  private static async Task<IResult?> RetainOversightAccessAsync(HttpContext http, AuditSphereDbContext queryDb, ActorContext actor)
  {
    if (!queryDb.AdministratorOversightObserved) return null;
    var descriptor = http.GetEndpoint()?.Metadata.GetMetadata<OversightReadDescriptor>();
    if (descriptor is null || !HttpMethods.IsGet(http.Request.Method))
      return Failure(AdministratorOversightAccessEvidence.Unavailable, "This read is not registered for oversight access evidence.", 503);
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
      return result.Succeeded ? null : Failure(result.ErrorCode, result.Message,
        result.ErrorCode == ErrorCodes.ScopeDenied ? 403 : 503);
    }
    catch (Exception error) when (error is DbUpdateException or DbException)
    {
      return Failure(AdministratorOversightAccessEvidence.Unavailable, "Oversight access evidence could not be retained. Try again later.", 503);
    }
  }
}
