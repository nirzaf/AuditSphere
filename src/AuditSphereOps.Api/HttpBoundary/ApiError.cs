namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>
/// Stable, bounded error body for boundary-produced refusals (429, 413, 503). Messages never
/// expose SQL, stack traces, provider exception bodies, secrets, internal paths or tenant
/// details beyond the caller's scope; the correlation ID matches X-Correlation-Id telemetry.
/// </summary>
public sealed record ApiError(string Code, string? Message = null, string? CorrelationId = null);
