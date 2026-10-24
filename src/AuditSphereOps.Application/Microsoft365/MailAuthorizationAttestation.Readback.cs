using System.Text.Json;

namespace AuditSphereOps.Application.Microsoft365;

public static partial class MailAuthorizationAttestationCanonicalizer
{
  /// <summary>
  /// Prepares unsigned evidence from the existing ReadOnly envelope. Expected binding and source come from
  /// independently reviewed inputs, never from the observed snapshot. This does not authenticate the operator,
  /// sign evidence, enroll it, or enable mail; the existing pinned-signature verifier remains authoritative.
  /// </summary>
  public static MailAuthorizationAttestation PrepareReadOnlyAttestation(
    ReadOnlySpan<byte> readbackJson, ReadOnlySpan<byte> expectedBindingJson,
    MailAuthorizationSourceIdentity checkoutSource, DateTimeOffset nowUtc, DateTimeOffset expiresAtUtc)
  {
    try
    {
      if (readbackJson.Length is < 2 or > MaximumPayloadBytes ||
          expectedBindingJson.Length is < 2 or > MaximumPayloadBytes || nowUtc.Offset != TimeSpan.Zero)
        Invalid();
      using var bindingDocument = JsonDocument.Parse(expectedBindingJson.ToArray());
      RejectDuplicateProperties(bindingDocument.RootElement);
      var expected = bindingDocument.RootElement.Deserialize<MailAuthorizationAttestationExpectedBinding>(StrictJsonOptions);
      if (expected is null || checkoutSource is null || expected.Source != checkoutSource) Invalid();
      ValidateSource(checkoutSource);

      using var document = JsonDocument.Parse(readbackJson.ToArray(), new JsonDocumentOptions { MaxDepth = 24 });
      var root = document.RootElement;
      RejectDuplicateProperties(root);
      RequireReadbackFields(root, "schemaVersion", "repositoryCommit", "mode", "status", "tenantId", "planSha256",
        "startedAtUtc", "completedAtUtc", "mutationsAttempted", "mutationsConfirmed", "networkCalls", "changes",
        "checks", "blockedPrerequisites", "errorCode", "redactionsApplied");
      // A successful provider observation intentionally remains incomplete until signed runtime evidence is enrolled.
      if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
          root.GetProperty("mode").GetString() != "ReadOnly" ||
          root.GetProperty("status").GetString() != "INCOMPLETE_EVIDENCE" ||
          root.GetProperty("errorCode").GetString() != "mail-legacy-capability-evidence-not-verified" ||
          root.GetProperty("mutationsAttempted").GetInt32() != 0 ||
          root.GetProperty("mutationsConfirmed").GetInt32() != 0 ||
          root.GetProperty("changes").GetArrayLength() != 0 ||
          root.GetProperty("networkCalls").GetInt32() <= 0 ||
          !root.GetProperty("redactionsApplied").GetBoolean() ||
          root.GetProperty("repositoryCommit").GetString() != checkoutSource.RepositoryCommit ||
          root.GetProperty("planSha256").GetString() != expected.PlanSha256 ||
          root.GetProperty("tenantId").GetGuid() != expected.TenantId) Invalid();
      var prerequisites = root.GetProperty("blockedPrerequisites");
      if (prerequisites.GetArrayLength() != 1 || prerequisites[0].GetString() !=
          "verification:legacy-mail-capability-evidence-not-verified") Invalid();

      JsonElement? observation = null;
      var gateCount = 0;
      var planCount = 0;
      if (root.GetProperty("checks").GetArrayLength() != 3) Invalid();
      foreach (var check in root.GetProperty("checks").EnumerateArray())
      {
        var id = check.GetProperty("id").GetString();
        string[] commonFields = ["id", "capability", "resourceId", "expected", "observed", "state",
          "observedAtUtc", "expiresAtUtc", "evidenceReference", "errorCode"];
        RequireReadbackFields(check, id == "mail-current-state-authorization"
          ? [.. commonFields, "source", "providerSnapshot"] : commonFields);
        if (id == "mail-plan-contract") planCount++;
        else if (id is not ("mail-current-state-authorization" or "mail-capability-evidence")) Invalid();
        if (id == "mail-capability-evidence" && check.GetProperty("state").GetString() == "INCOMPLETE_EVIDENCE")
          gateCount++;
        else if (check.GetProperty("state").GetString() != "PASS") Invalid();
        if (!check.TryGetProperty("providerSnapshot", out _)) continue;
        if (observation.HasValue || id != "mail-current-state-authorization" ||
            check.GetProperty("state").GetString() != "PASS" ||
            check.GetProperty("capability").GetString() != "MAIL" ||
            check.GetProperty("resourceId").GetGuid() != expected.RuntimeClientId) Invalid();
        observation = check;
      }
      if (!observation.HasValue || gateCount != 1 || planCount != 1) Invalid();
      var observed = observation.Value;
      var source = observed.GetProperty("source").Deserialize<MailAuthorizationSourceIdentity>(StrictJsonOptions);
      var snapshot = observed.GetProperty("providerSnapshot").Deserialize<MailAuthorizationProviderSnapshot>(StrictJsonOptions);
      if (source != checkoutSource || snapshot is null ||
          snapshot.PermissionModel != MailAuthorizationModel.LegacyApplicationAccessPolicy) Invalid();
      var started = ReadUtcTimestamp(root.GetProperty("startedAtUtc"));
      var completed = ReadUtcTimestamp(root.GetProperty("completedAtUtc"));
      var observedAt = ReadUtcTimestamp(observed.GetProperty("observedAtUtc"));
      if (started.Offset != TimeSpan.Zero || completed.Offset != TimeSpan.Zero || observedAt.Offset != TimeSpan.Zero ||
          started > observedAt || observedAt > completed || completed > nowUtc ||
          nowUtc - started > TimeSpan.FromHours(1) || expiresAtUtc <= nowUtc ||
          expiresAtUtc - started > TimeSpan.FromHours(1)) Invalid();
      var attestation = Create(expected.ConnectionRevisionId, expected.ConsentAttemptId, expected.PlanSha256,
        source, observedAt, expiresAtUtc, snapshot);
      if (!MailAuthorizationAttestationVerifier.MatchesExpectedBinding(attestation, expected)) Invalid();
      return attestation;
    }
    catch (MailAuthorizationAttestationException) { throw; }
    catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or
        InvalidOperationException or OverflowException or KeyNotFoundException)
    {
      throw new MailAuthorizationAttestationException(InvalidCode);
    }
  }

  private static void RequireReadbackFields(JsonElement value, params string[] names)
  {
    if (value.ValueKind != JsonValueKind.Object) Invalid();
    var properties = value.EnumerateObject().ToArray();
    if (properties.Length != names.Length || properties.Any(x => !names.Contains(x.Name, StringComparer.Ordinal))) Invalid();
  }

  private static DateTimeOffset ReadUtcTimestamp(JsonElement value)
  {
    var text = value.GetString();
    if (text is null || !(text.EndsWith('Z') || text.EndsWith("+00:00", StringComparison.Ordinal))) Invalid();
    return value.GetDateTimeOffset();
  }
}
