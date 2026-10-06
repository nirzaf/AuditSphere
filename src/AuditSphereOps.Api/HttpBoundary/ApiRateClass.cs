namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>
/// Cost classes used by the boundary rate limiter. Classes are assigned by explicit endpoint
/// metadata where cost is not derivable from the HTTP method; everything else falls back to
/// read (GET/HEAD) versus command (state-changing) classification.
/// </summary>
public enum ApiRateClass
{
  /// <summary>Ordinary bounded workspace reads served to an interactive SPA.</summary>
  NormalRead,

  /// <summary>State-changing business commands; refused requests are never retried automatically.</summary>
  Command,

  /// <summary>Global search: repeated database queries can be triggered quickly by one user.</summary>
  Search,

  /// <summary>Generated artifacts and exports (CSV, rendered financial packages, deliverable bundles).</summary>
  Export,

  /// <summary>File ingestion endpoints that stream request bodies to bounded staging storage.</summary>
  FileUpload,

  /// <summary>Anonymous browser-facing authentication entry points, partitioned by remote IP.</summary>
  Authentication,

  /// <summary>Administrative Microsoft 365 verification and provider-backed directory operations.</summary>
  M365Administration
}

/// <summary>Explicit per-endpoint rate class. Applied only where the default method-based class would be wrong.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiRateClassAttribute(ApiRateClass rateClass) : Attribute
{
  public ApiRateClass RateClass { get; } = rateClass;
}
