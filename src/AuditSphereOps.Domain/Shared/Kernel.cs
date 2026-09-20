// Shared kernel: IDs, money rules, typed errors, immutable references.
// Spec §§27.1, 27.7, 42.1–42.3. No persistence or provider references here.
namespace AuditSphereOps.Domain.Shared;

/// <summary>Typed command outcome; failures carry machine-readable codes, never bare booleans.</summary>
public sealed record CommandResult(bool Succeeded, string? ErrorCode = null, string? Message = null)
{
  public static CommandResult Ok() => new(true);
  public static CommandResult Fail(string errorCode, string message) => new(false, errorCode, message);
}

/// <summary>Typed command outcome with a value payload.</summary>
public sealed record CommandResult<T>(bool Succeeded, T? Value = default, string? ErrorCode = null, string? Message = null)
{
  public static CommandResult<T> Ok(T value) => new(true, value);
  public static CommandResult<T> Fail(string errorCode, string message) => new(false, default, errorCode, message);
}

/// <summary>Well-known error codes shared by API problem envelopes (§28.7).</summary>
public static class ErrorCodes
{
  public const string ScopeDenied = "scope.denied";
  public const string ProtectedState = "protected-state.denied";
  public const string IdempotencyConflict = "idempotency.conflict";
  public const string StaleRevision = "revision.stale";
  public const string GenerationStale = "generation.stale";
  public const string GateBlocked = "gate.blocked";
  public const string ManifestMismatch = "manifest.mismatch";
  public const string ExternalEffectsFenced = "external-effects.fenced";

  // Stable catalogs. Existing wire values remain unchanged; new code should use
  // the owning catalog instead of parsing or inventing message text.
  public static class Accounting
  {
    public const string ImportRejected = "import.rejected";
    public const string ImportDuplicate = "import.duplicate";
    public const string MappingInvalid = "mapping.invalid";
    public const string MappingIncomplete = "mapping.incomplete";
    public const string PackageInvalid = "package.invalid";
    public const string PackageSupplementaryInvalid = "package.supplementary.invalid";
    public const string JournalRejected = "journal.rejected";
    public const string ReconciliationRejected = "reconciliation.rejected";
    public const string PlanRejected = "plan.rejected";
  }

  public static class AuditPlanning
  {
    public const string Invalid = "audit-planning.invalid";
  }

  public static class Drafts
  {
    public const string NotFound = "draft.not-found";
    public const string Conflict = "draft.conflict";
    public const string TargetChanged = "draft.target-changed";
    public const string Consumed = "draft.consumed";
  }

  public static class Operations
  {
    public const string Invalid = "operations.invalid";
    public const string State = "operations.state";
    public const string Lease = "operations.lease";
    public const string Conflict = "operations.conflict";
    public const string RecoveryInvalid = "recovery.invalid";
    public const string Quarantined = "operations.quarantined";
  }
}

/// <summary>
/// Safe presentation text for command failures. Machine-readable codes remain
/// available to logs and API envelopes, while the UI never has to echo an
/// unknown code or server message to an end user.
/// </summary>
public static class ErrorCatalog
{
  public const string UnknownUiMessage = "The operation could not be completed. Reload and try again.";

  public static string ForUi(string? code) => code switch
  {
    ErrorCodes.ScopeDenied => "You do not have access to this item.",
    ErrorCodes.ProtectedState => "This item is no longer editable.",
    ErrorCodes.StaleRevision => "This item changed. Reload the current version and try again.",
    ErrorCodes.GenerationStale => "The underlying inputs changed. Reload the current version and try again.",
    ErrorCodes.GateBlocked => "This operation is currently blocked by a required prerequisite.",
    ErrorCodes.IdempotencyConflict => "This retry conflicts with an earlier request.",
    ErrorCodes.Drafts.NotFound => "No active draft is available.",
    ErrorCodes.Drafts.Conflict => "The draft could not be saved because it changed.",
    ErrorCodes.Drafts.TargetChanged => "The workpaper changed. Reload it before editing.",
    ErrorCodes.Drafts.Consumed => "This draft was already submitted and is no longer editable.",
    _ => UnknownUiMessage
  };
}

/// <summary>Money policy: max 6dp storage (§42.1), single presentation currency per package (§1.5).</summary>
public static class MoneyPolicy
{
  public const int MaxScale = 6;

  public static decimal Normalize(decimal amount, int scale = MaxScale)
  {
    if (scale is < 0 or > MaxScale)
      throw new ArgumentOutOfRangeException(nameof(scale), $"Scale must be 0..{MaxScale}.");
    return decimal.Round(amount, scale, MidpointRounding.ToEven);
  }

  public static void RequireSameCurrency(string expected, string actual, string paramName)
  {
    if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException($"Mixed currencies are never summed ({expected} vs {actual}, param {paramName}).");
  }
}

/// <summary>Canonical SHA-256 helper for manifest/hash chains (§§11.5, 42.2).</summary>
public static class Hashing
{
  public static string Sha256Hex(byte[] bytes)
  {
    var hash = System.Security.Cryptography.SHA256.HashData(bytes);
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  public static string Sha256Hex(string utf8) =>
    Sha256Hex(System.Text.Encoding.UTF8.GetBytes(utf8));
}
