namespace AuditSphereOps.Application.Completion;

internal static class ReleaseCheckpointKey
{
  private const int MaximumLength = 200;

  public static bool TryNormalize(string? value, out string normalized)
  {
    normalized = value?.Trim() ?? string.Empty;
    return normalized.Length is > 0 and <= MaximumLength && !normalized.Any(char.IsControl);
  }
}
