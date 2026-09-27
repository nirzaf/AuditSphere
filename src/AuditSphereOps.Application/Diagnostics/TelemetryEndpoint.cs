namespace AuditSphereOps.Application.Diagnostics;

/// <summary>Validates an optional exporter destination without logging its configured value.</summary>
public static class TelemetryEndpoint
{
  public static Uri? Parse(string? configured)
  {
    if (configured is null) return null;
    if (!Uri.TryCreate(configured, UriKind.Absolute, out var endpoint) ||
        endpoint.Scheme is not ("http" or "https") ||
        !string.IsNullOrEmpty(endpoint.UserInfo) ||
        !string.IsNullOrEmpty(endpoint.Query) ||
        !string.IsNullOrEmpty(endpoint.Fragment))
      throw new InvalidOperationException(
        "Telemetry:Otlp:Endpoint must be an absolute HTTP(S) URI without embedded credentials or query data.");
    return endpoint;
  }
}
