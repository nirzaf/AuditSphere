namespace AuditSphereOps.Application.Completion;

internal static class ReleaseDeliveryIdentity
{
  private const int MaximumIdempotencyKeyLength = 200;
  private const string LegacyPrefix = "release:";

  public static string ForRelease(Guid releaseId, string authorizedReleaseKey) =>
    authorizedReleaseKey.Length <= MaximumIdempotencyKeyLength - LegacyPrefix.Length
      ? LegacyPrefix + authorizedReleaseKey
      : $"release:{releaseId:D}";

  public static bool Matches(string idempotencyKey, Guid releaseId, string authorizedReleaseKey) =>
    string.Equals(idempotencyKey, ForRelease(releaseId, authorizedReleaseKey), StringComparison.Ordinal) ||
    string.Equals(idempotencyKey, LegacyPrefix + authorizedReleaseKey, StringComparison.Ordinal);
}
