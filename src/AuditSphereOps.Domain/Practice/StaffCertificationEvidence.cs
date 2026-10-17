using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace AuditSphereOps.Domain.Practice;

/// <summary>An exact-source human decision. Application authorization and persistence fences remain separate.</summary>
public sealed record StaffCertificationVerificationEvidence(
  Guid Id, Guid FirmId, Guid CertificationId, Guid SubjectUserId, string SourceDigest,
  string Decision, Guid ReviewedByUserId, DateTimeOffset ReviewedAt, string EvidenceReference, string Reason);

public static class StaffCertificationEvidence
{
  /// <summary>Versioned, culture-independent identity covering every retained certification source field.</summary>
  public static string SourceDigest(StaffCertification source)
  {
    using var bytes = new MemoryStream();
    using (var writer = new Utf8JsonWriter(bytes))
    {
      writer.WriteStartObject();
      writer.WriteNumber("schemaVersion", 1);
      writer.WriteString("id", source.Id);
      writer.WriteString("firmId", source.FirmId);
      writer.WriteString("userId", source.UserId);
      writer.WriteString("name", source.Name);
      writer.WriteString("issuer", source.Issuer);
      writer.WriteString("expiresOn", source.ExpiresOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
      writer.WriteString("recordedAt", source.RecordedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
      writer.WriteString("recordedByUserId", source.RecordedByUserId);
      writer.WriteEndObject();
    }
    return Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
  }

  /// <summary>Requires the effective decision supplied by the caller; never selects a historical head or grants a role.</summary>
  public static bool IsApplicable(StaffCertification source, StaffCertificationVerificationEvidence? effectiveDecision, DateOnly asOfDate)
  {
    if (effectiveDecision is not { Decision: "VERIFIED" } decision || source.Id == Guid.Empty ||
        source.FirmId == Guid.Empty || source.UserId == Guid.Empty || source.RecordedByUserId == Guid.Empty ||
        string.IsNullOrWhiteSpace(source.Name) || decision.Id == Guid.Empty || decision.ReviewedByUserId == Guid.Empty ||
        decision.FirmId != source.FirmId || decision.CertificationId != source.Id || decision.SubjectUserId != source.UserId ||
        decision.ReviewedByUserId == source.RecordedByUserId || decision.ReviewedByUserId == source.UserId ||
        decision.ReviewedAt < source.RecordedAt || DateOnly.FromDateTime(decision.ReviewedAt.UtcDateTime) > asOfDate ||
        string.IsNullOrWhiteSpace(decision.EvidenceReference) || string.IsNullOrWhiteSpace(decision.Reason) ||
        source.ExpiresOn is { } expiry && expiry < asOfDate)
      return false;
    return string.Equals(decision.SourceDigest, SourceDigest(source), StringComparison.Ordinal);
  }
}
