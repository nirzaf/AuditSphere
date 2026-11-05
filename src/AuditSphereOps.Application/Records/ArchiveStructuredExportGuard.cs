using System.Text.Json;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;

namespace AuditSphereOps.Application.Records;

/// <summary>
/// Gate for structured archive payloads. Only the engagement-scoped v2 envelope may leave the
/// retained store; the legacy v1 envelope did not declare an audience and is withheld for review.
/// </summary>
public static class ArchiveStructuredExportGuard
{
  private static readonly HashSet<string> GroupWideProperties = new(StringComparer.OrdinalIgnoreCase)
  {
    "clientGroups", "groupMemberships", "scopeVersions", "consolidationComponents",
    "externalComponentPacks", "externalComponentPackLines", "ownershipInterests",
    "intercompanyMatches", "consolidationJournals", "consolidationJournalLines",
    "consolidationRuns", "consolidationRunLines", "translationResults",
    "consolidationReport", "consolidationReports"
  };

  public static bool IsEngagementSafe(Archive archive, string? schema, string? relativeName,
    string? metadataJson, string? payloadJson, out string failure)
  {
    failure = string.Empty;
    if (!string.Equals(schema, ArchiveStructuredExportSchemas.EngagementV2, StringComparison.Ordinal) ||
        !string.Equals(relativeName, ArchiveStructuredExportSchemas.EngagementV2RelativeName, StringComparison.Ordinal))
    {
      failure = "Legacy or unrecognised archive payload has no provable engagement-only audience.";
      return false;
    }

    if (string.IsNullOrWhiteSpace(metadataJson) || string.IsNullOrWhiteSpace(payloadJson))
    {
      failure = "The structured archive payload is empty.";
      return false;
    }

    try
    {
      using var metadata = JsonDocument.Parse(metadataJson);
      var meta = metadata.RootElement;
      if (!string.Equals(meta.GetProperty("schema").GetString(), ArchiveStructuredExportSchemas.EngagementV2,
            StringComparison.Ordinal) ||
          !string.Equals(meta.GetProperty("storage").GetString(), "archive_structured_exports",
            StringComparison.Ordinal) ||
          !string.Equals(meta.GetProperty("audience").GetString(), ArchiveStructuredExportSchemas.EngagementOnlyAudience,
            StringComparison.Ordinal))
      {
        failure = "The structured archive metadata does not declare an engagement-only audience.";
        return false;
      }

      using var document = JsonDocument.Parse(payloadJson);
      var root = document.RootElement;
      if (!string.Equals(root.GetProperty("schema").GetString(), ArchiveStructuredExportSchemas.EngagementV2,
            StringComparison.Ordinal))
      {
        failure = "The structured archive payload schema does not declare engagement-only scope.";
        return false;
      }

      var audience = root.GetProperty("audience");
      if (!string.Equals(audience.GetProperty("scope").GetString(), ArchiveStructuredExportSchemas.EngagementOnlyAudience,
            StringComparison.Ordinal) ||
          audience.GetProperty("groupWideDataIncluded").GetBoolean() ||
          audience.GetProperty("firmId").GetGuid() != archive.FirmId ||
          audience.GetProperty("clientId").GetGuid() != archive.ClientId ||
          audience.GetProperty("engagementId").GetGuid() != archive.EngagementId)
      {
        failure = "The structured archive payload audience does not match its engagement.";
        return false;
      }

      var scope = root.GetProperty("scope");
      if (scope.GetProperty("firmId").GetGuid() != archive.FirmId ||
          scope.GetProperty("clientId").GetGuid() != archive.ClientId ||
          scope.GetProperty("engagementId").GetGuid() != archive.EngagementId)
      {
        failure = "The structured archive payload scope does not match its engagement.";
        return false;
      }

      if (ContainsGroupWideProperty(root))
      {
        failure = "The structured archive payload contains group-wide data outside its engagement audience.";
        return false;
      }

      return true;
    }
    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
    {
      failure = "The structured archive payload audience cannot be verified.";
      return false;
    }
  }

  private static bool ContainsGroupWideProperty(JsonElement element)
  {
    switch (element.ValueKind)
    {
      case JsonValueKind.Object:
        foreach (var property in element.EnumerateObject())
        {
          if (GroupWideProperties.Contains(property.Name) || ContainsGroupWideProperty(property.Value))
            return true;
        }
        return false;
      case JsonValueKind.Array:
        return element.EnumerateArray().Any(ContainsGroupWideProperty);
      default:
        return false;
    }
  }
}
