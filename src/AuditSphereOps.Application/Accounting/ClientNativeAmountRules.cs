namespace AuditSphereOps.Application.Accounting;

/// <summary>Shared numeric(19,6) storage boundary for native client financial amounts; no currency rounding is inferred.</summary>
public static class ClientNativeAmountRules
{
  public const decimal MaximumStoredAmount = 9_999_999_999_999.999999m;
  public static bool ValidStoredAmount(decimal value) => value >= 0m && value <= MaximumStoredAmount && decimal.Round(value, 6) == value;
}
