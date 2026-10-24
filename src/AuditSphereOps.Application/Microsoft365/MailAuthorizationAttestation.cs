using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuditSphereOps.Application.Microsoft365;

/// <summary>The two explicit mail authorization models already represented by the WU-022 plan.</summary>
public enum MailAuthorizationModel
{
  LegacyApplicationAccessPolicy,
  ExchangeRbacResourceScope
}

/// <summary>
/// Normalized source identity for the offline provider-readback run. Paths are fixed by this contract; only their
/// hashes and the repository commit are carried in the signed evidence.
/// </summary>
public sealed record MailAuthorizationSourceIdentity(
  string RepositoryCommit,
  string ConfigureMailAccessSha256,
  string MailAccessModuleSha256,
  string ModulesLockSha256,
  string CommonModuleSha256,
  string EntraModuleSha256,
  string PlanSchemaSha256);

/// <summary>One actual Entra app-role assignment returned for the dedicated mail enterprise service principal.</summary>
public sealed record MailEntraAppRoleAssignment(
  Guid AssignmentObjectId,
  Guid PrincipalObjectId,
  Guid ResourceObjectId,
  Guid AppRoleId);

/// <summary>Legacy Application Access Policy readback and sender/control probes.</summary>
public sealed record MailLegacyAccessProbe(Guid MailboxObjectId, bool Allowed);

public sealed record MailLegacyAuthorizationEvidence(
  Guid ScopeGroupObjectId,
  string ScopeGroupName,
  string PolicyScopeName,
  string AccessRight,
  IReadOnlyList<Guid> ScopeGroupMemberObjectIds,
  IReadOnlyList<Guid> PolicyApplicationClientIds,
  IReadOnlyList<MailLegacyAccessProbe> Probes);

/// <summary>The exact normalized fields returned for the one Exchange Application Mail.Send assignment.</summary>
public sealed record MailExchangeRoleAssignment(
  string Role,
  Guid RoleAssigneeObjectId,
  string RoleAssigneeType,
  string AssignmentMethod);

/// <summary>One Test-ServicePrincipalAuthorization result, including its resource and effective scope decision.</summary>
public sealed record MailExchangeAuthorizationProbe(
  Guid MailboxObjectId,
  string RoleName,
  IReadOnlyList<string> GrantedPermissions,
  string AllowedResourceScope,
  string ScopeType,
  bool InScope);

/// <summary>Exchange pointer, management-scope readback, exact assignment, and sender/control probes.</summary>
public sealed record MailExchangeRbacEvidence(
  Guid ServicePrincipalObjectId,
  Guid ServicePrincipalAppId,
  string ManagementScopeName,
  string ScopeRestrictionType,
  bool Exclusive,
  string RecipientFilter,
  IReadOnlyList<Guid> ScopeMemberObjectIds,
  IReadOnlyList<MailExchangeAuthorizationProbe> Probes);

/// <summary>
/// A normalized, typed projection of the actual Graph and Exchange readback. It deliberately excludes access tokens,
/// credentials, mailbox addresses, message content, arbitrary provider JSON, and provider response headers.
/// </summary>
public sealed record MailAuthorizationProviderSnapshot(
  Guid TenantId,
  MailAuthorizationModel PermissionModel,
  Guid RuntimeClientId,
  Guid EnterpriseServicePrincipalObjectId,
  Guid GraphServicePrincipalObjectId,
  Guid GraphServicePrincipalAppId,
  Guid GraphMailSendAppRoleId,
  string GraphMailSendRoleValue,
  bool GraphMailSendRoleEnabled,
  IReadOnlyList<string> GraphMailSendAllowedMemberTypes,
  IReadOnlyList<Guid> SenderMailboxObjectIds,
  Guid ControlMailboxObjectId,
  IReadOnlyList<MailEntraAppRoleAssignment> EntraGrantSet,
  IReadOnlyList<MailExchangeRoleAssignment> ExchangeRoleAssignmentSet,
  MailLegacyAuthorizationEvidence? LegacyAuthorization,
  MailExchangeRbacEvidence? ExchangeRbacAuthorization);

/// <summary>
/// Detached signed assertion over one normalized provider readback and its local connection/consent/plan/source
/// binding. The operator signing key is not represented by this type and must remain outside API and worker hosts.
/// </summary>
public sealed record MailAuthorizationAttestation(
  int SchemaVersion,
  Guid ConnectionRevisionId,
  Guid ConsentAttemptId,
  string PlanSha256,
  MailAuthorizationSourceIdentity Source,
  DateTimeOffset ObservedAtUtc,
  DateTimeOffset ExpiresAtUtc,
  string ProviderSnapshotSha256,
  MailAuthorizationProviderSnapshot ProviderSnapshot);

public sealed class MailAuthorizationAttestationException(string code) : Exception(code)
{
  public string Code { get; } = code;
}

/// <summary>
/// Canonical writer and strict structural validator. Array-valued sets are emitted in ordinal canonical order;
/// duplicate set members are rejected before serialization. The provider digest hashes only the provider snapshot,
/// never the plan or the outer attestation metadata.
/// </summary>
public static partial class MailAuthorizationAttestationCanonicalizer
{
  public const int CurrentSchemaVersion = 1;
  public const int MaximumSenders = 1000;
  public const int MaximumPayloadBytes = 1_048_576;
  public const int MaximumCmsBytes = 65_536;
  public const string InvalidCode = "MAIL_ATTESTATION_INVALID";

  private const string GraphApplicationId = "00000003-0000-0000-c000-000000000000";
  private const string MailSend = "Mail.Send";
  private const string ExchangeMailSendRole = "Application Mail.Send";

  private static readonly JsonSerializerOptions StrictJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = false,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    NumberHandling = JsonNumberHandling.Strict,
    MaxDepth = 24,
    Converters = { new MailAuthorizationModelConverter() }
  };

  public static MailAuthorizationAttestation Create(
    Guid connectionRevisionId,
    Guid consentAttemptId,
    string planSha256,
    MailAuthorizationSourceIdentity source,
    DateTimeOffset observedAtUtc,
    DateTimeOffset expiresAtUtc,
    MailAuthorizationProviderSnapshot providerSnapshot)
  {
    var attestation = new MailAuthorizationAttestation(CurrentSchemaVersion, connectionRevisionId, consentAttemptId,
      planSha256, source, observedAtUtc, expiresAtUtc, ComputeProviderSnapshotSha256(providerSnapshot), providerSnapshot);
    ValidateShape(attestation);
    return attestation;
  }

  public static byte[] SerializeCanonical(MailAuthorizationAttestation attestation)
  {
    ValidateShape(attestation);
    using var output = new MemoryStream();
    using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
    {
      writer.WriteStartObject();
      writer.WriteNumber("schemaVersion", attestation.SchemaVersion);
      writer.WriteString("connectionRevisionId", FormatGuid(attestation.ConnectionRevisionId));
      writer.WriteString("consentAttemptId", FormatGuid(attestation.ConsentAttemptId));
      writer.WriteString("planSha256", attestation.PlanSha256);
      WriteSource(writer, attestation.Source);
      writer.WriteString("observedAtUtc", FormatUtc(attestation.ObservedAtUtc));
      writer.WriteString("expiresAtUtc", FormatUtc(attestation.ExpiresAtUtc));
      writer.WriteString("providerSnapshotSha256", attestation.ProviderSnapshotSha256);
      writer.WritePropertyName("providerSnapshot");
      WriteProviderSnapshot(writer, attestation.ProviderSnapshot);
      writer.WriteEndObject();
    }
    var bytes = output.ToArray();
    if (bytes.Length > MaximumPayloadBytes) Invalid();
    return bytes;
  }

  public static string ComputeProviderSnapshotSha256(MailAuthorizationProviderSnapshot snapshot)
  {
    ValidateProviderSnapshot(snapshot);
    using var output = new MemoryStream();
    using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = false }))
      WriteProviderSnapshot(writer, snapshot);
    return Convert.ToHexString(SHA256.HashData(output.ToArray())).ToLowerInvariant();
  }

  internal static MailAuthorizationAttestation ParseCanonical(ReadOnlySpan<byte> payload)
  {
    if (payload.Length is < 2 or > MaximumPayloadBytes) Invalid();
    try
    {
      using var document = JsonDocument.Parse(payload.ToArray(), new JsonDocumentOptions
      {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 24
      });
      RejectDuplicateProperties(document.RootElement);
      if (document.RootElement.ValueKind != JsonValueKind.Object) Invalid();
      var attestation = JsonSerializer.Deserialize<MailAuthorizationAttestation>(payload, StrictJsonOptions);
      if (attestation is null) Invalid();
      ValidateShape(attestation);
      var canonical = SerializeCanonical(attestation);
      if (!payload.SequenceEqual(canonical)) Invalid();
      return attestation;
    }
    catch (MailAuthorizationAttestationException) { throw; }
    catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
    {
      throw new MailAuthorizationAttestationException(InvalidCode);
    }
  }

  internal static void ValidateFreshness(MailAuthorizationAttestation attestation, DateTimeOffset nowUtc,
    TimeSpan maximumEvidenceAge, X509Certificate2 signerCertificate)
  {
    if (nowUtc.Offset != TimeSpan.Zero || maximumEvidenceAge < TimeSpan.FromHours(1) ||
        maximumEvidenceAge > TimeSpan.FromHours(168)) Invalid();
    if (attestation.ObservedAtUtc > nowUtc || attestation.ExpiresAtUtc <= nowUtc ||
        attestation.ExpiresAtUtc <= attestation.ObservedAtUtc ||
        attestation.ExpiresAtUtc - attestation.ObservedAtUtc > maximumEvidenceAge)
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_STALE");

    var notBefore = new DateTimeOffset(signerCertificate.NotBefore.ToUniversalTime());
    var notAfter = new DateTimeOffset(signerCertificate.NotAfter.ToUniversalTime());
    if (notBefore > attestation.ObservedAtUtc || notAfter < nowUtc || notAfter < attestation.ExpiresAtUtc)
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNER_INVALID");

    var usages = signerCertificate.Extensions.OfType<X509KeyUsageExtension>().ToArray();
    if (usages.Length > 1 || (usages.Length == 1 &&
        (usages[0].KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0))
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNER_INVALID");
  }

  private static void ValidateShape(MailAuthorizationAttestation attestation)
  {
    if (attestation is null || attestation.SchemaVersion != CurrentSchemaVersion ||
        attestation.ConnectionRevisionId == Guid.Empty || attestation.ConsentAttemptId == Guid.Empty ||
        attestation.ObservedAtUtc.Offset != TimeSpan.Zero || attestation.ExpiresAtUtc.Offset != TimeSpan.Zero ||
        attestation.ExpiresAtUtc <= attestation.ObservedAtUtc || !IsSha256(attestation.PlanSha256) ||
        !IsSha256(attestation.ProviderSnapshotSha256) || attestation.Source is null || attestation.ProviderSnapshot is null)
      Invalid();
    ValidateSource(attestation.Source);
    ValidateProviderSnapshot(attestation.ProviderSnapshot);
    var actualDigest = Convert.FromHexString(ComputeProviderSnapshotSha256(attestation.ProviderSnapshot));
    var declaredDigest = Convert.FromHexString(attestation.ProviderSnapshotSha256);
    if (!CryptographicOperations.FixedTimeEquals(actualDigest, declaredDigest)) Invalid();
  }

  private static void ValidateSource(MailAuthorizationSourceIdentity source)
  {
    if (!IsValidSourceIdentity(source)) Invalid();
  }

  internal static bool IsValidSourceIdentity(MailAuthorizationSourceIdentity source) =>
    source is not null && source.RepositoryCommit is { Length: 40 or 64 } &&
    source.RepositoryCommit.All(IsLowerHex) && IsSha256(source.ConfigureMailAccessSha256) &&
    IsSha256(source.MailAccessModuleSha256) && IsSha256(source.ModulesLockSha256) &&
    IsSha256(source.CommonModuleSha256) && IsSha256(source.EntraModuleSha256) &&
    IsSha256(source.PlanSchemaSha256);

  internal static bool IsSha256Value(string value) => IsSha256(value);

  private static void ValidateProviderSnapshot(MailAuthorizationProviderSnapshot snapshot)
  {
    if (snapshot is null || snapshot.TenantId == Guid.Empty || snapshot.RuntimeClientId == Guid.Empty ||
        snapshot.EnterpriseServicePrincipalObjectId == Guid.Empty || snapshot.GraphServicePrincipalObjectId == Guid.Empty ||
        snapshot.GraphMailSendAppRoleId == Guid.Empty || snapshot.ControlMailboxObjectId == Guid.Empty ||
        snapshot.GraphServicePrincipalAppId != Guid.Parse(GraphApplicationId) ||
        !string.Equals(snapshot.GraphMailSendRoleValue, MailSend, StringComparison.Ordinal) ||
        !snapshot.GraphMailSendRoleEnabled) Invalid();

    var allowedMemberTypes = UniqueStrings(snapshot.GraphMailSendAllowedMemberTypes, 16);
    if (allowedMemberTypes.Count != 1 || !allowedMemberTypes.Contains("Application", StringComparer.Ordinal)) Invalid();
    var senders = UniqueGuids(snapshot.SenderMailboxObjectIds, 1, MaximumSenders);
    if (senders.Contains(snapshot.ControlMailboxObjectId)) Invalid();

    var grants = snapshot.EntraGrantSet ?? throw new MailAuthorizationAttestationException(InvalidCode);
    if (grants.Count > MaximumSenders || grants.Any(x => x is null || x.AssignmentObjectId == Guid.Empty ||
        x.PrincipalObjectId == Guid.Empty || x.ResourceObjectId == Guid.Empty || x.AppRoleId == Guid.Empty) ||
        grants.Select(x => x.AssignmentObjectId).Distinct().Count() != grants.Count ||
        grants.Any(x => x.PrincipalObjectId != snapshot.EnterpriseServicePrincipalObjectId)) Invalid();
    var exchangeAssignments = snapshot.ExchangeRoleAssignmentSet ??
      throw new MailAuthorizationAttestationException(InvalidCode);
    if (exchangeAssignments.Any(x => x is null || string.IsNullOrWhiteSpace(x.Role) ||
        x.RoleAssigneeObjectId == Guid.Empty || string.IsNullOrWhiteSpace(x.RoleAssigneeType) ||
        string.IsNullOrWhiteSpace(x.AssignmentMethod))) Invalid();

    switch (snapshot.PermissionModel)
    {
      case MailAuthorizationModel.LegacyApplicationAccessPolicy:
        ValidateLegacy(snapshot, senders, grants);
        break;
      case MailAuthorizationModel.ExchangeRbacResourceScope:
        ValidateExchangeRbac(snapshot, senders, grants);
        break;
      default:
        Invalid();
        break;
    }
  }

  private static void ValidateLegacy(MailAuthorizationProviderSnapshot snapshot, HashSet<Guid> senders,
    IReadOnlyList<MailEntraAppRoleAssignment> grants)
  {
    var legacy = snapshot.LegacyAuthorization;
    if (legacy is null || snapshot.ExchangeRbacAuthorization is not null || grants.Count != 1 ||
        snapshot.ExchangeRoleAssignmentSet.Count != 0 ||
        legacy.ScopeGroupObjectId == Guid.Empty || string.IsNullOrWhiteSpace(legacy.ScopeGroupName) ||
        !string.Equals(legacy.PolicyScopeName, legacy.ScopeGroupName, StringComparison.Ordinal) ||
        !string.Equals(legacy.AccessRight, "RestrictAccess", StringComparison.Ordinal)) Invalid();
    var grant = grants[0];
    if (grant.ResourceObjectId != snapshot.GraphServicePrincipalObjectId || grant.AppRoleId != snapshot.GraphMailSendAppRoleId)
      Invalid();
    if (!ExactSet(legacy.ScopeGroupMemberObjectIds, senders) ||
        !ExactSet(legacy.PolicyApplicationClientIds, new HashSet<Guid> { snapshot.RuntimeClientId })) Invalid();

    var probes = legacy.Probes ?? throw new MailAuthorizationAttestationException(InvalidCode);
    var expected = new HashSet<Guid>(senders) { snapshot.ControlMailboxObjectId };
    if (probes.Count != expected.Count || probes.Any(x => x is null || !expected.Contains(x.MailboxObjectId)) ||
        probes.Select(x => x.MailboxObjectId).Distinct().Count() != probes.Count ||
        probes.Any(x => x.Allowed != senders.Contains(x.MailboxObjectId))) Invalid();
  }

  private static void ValidateExchangeRbac(MailAuthorizationProviderSnapshot snapshot, HashSet<Guid> senders,
    IReadOnlyList<MailEntraAppRoleAssignment> grants)
  {
    var rbac = snapshot.ExchangeRbacAuthorization;
    if (rbac is null || snapshot.LegacyAuthorization is not null || grants.Count != 0 ||
        snapshot.ExchangeRoleAssignmentSet.Count != 1 ||
        rbac.ServicePrincipalObjectId != snapshot.EnterpriseServicePrincipalObjectId ||
        rbac.ServicePrincipalAppId != snapshot.RuntimeClientId ||
        string.IsNullOrWhiteSpace(rbac.ManagementScopeName) ||
        !string.Equals(rbac.ScopeRestrictionType, "RecipientScope", StringComparison.Ordinal) || rbac.Exclusive ||
        string.IsNullOrWhiteSpace(rbac.RecipientFilter) || rbac.RecipientFilter.Length > 4096 ||
        rbac.RecipientFilter.Any(char.IsControl) || !ExactSet(rbac.ScopeMemberObjectIds, senders)) Invalid();

    if (snapshot.ExchangeRoleAssignmentSet.Any(x => x is null ||
        !string.Equals(x.Role, ExchangeMailSendRole, StringComparison.Ordinal) ||
        x.RoleAssigneeObjectId != snapshot.EnterpriseServicePrincipalObjectId ||
        !string.Equals(x.RoleAssigneeType, "ServicePrincipal", StringComparison.Ordinal) ||
        !string.Equals(x.AssignmentMethod, "Direct", StringComparison.Ordinal))) Invalid();

    var probes = rbac.Probes ?? throw new MailAuthorizationAttestationException(InvalidCode);
    var expected = new HashSet<Guid>(senders) { snapshot.ControlMailboxObjectId };
    if (probes.Count != expected.Count || probes.Any(x => x is null || !expected.Contains(x.MailboxObjectId)) ||
        probes.Select(x => x.MailboxObjectId).Distinct().Count() != probes.Count) Invalid();
    foreach (var probe in probes)
    {
      var permissions = UniqueStrings(probe.GrantedPermissions, 16);
      if (!string.Equals(probe.RoleName, ExchangeMailSendRole, StringComparison.Ordinal) ||
          permissions.Count != 1 || !permissions.Contains(MailSend, StringComparer.Ordinal) ||
          !string.Equals(probe.AllowedResourceScope, rbac.ManagementScopeName, StringComparison.Ordinal) ||
          !string.Equals(probe.ScopeType, "CustomRecipientScope", StringComparison.Ordinal) ||
          probe.InScope != senders.Contains(probe.MailboxObjectId)) Invalid();
    }
  }

  private static bool ExactSet(IReadOnlyList<Guid> actual, HashSet<Guid> expected)
  {
    if (actual is null || actual.Any(x => x == Guid.Empty) || actual.Count != expected.Count ||
        actual.Distinct().Count() != actual.Count) return false;
    return actual.All(expected.Contains);
  }

  private static HashSet<Guid> UniqueGuids(IReadOnlyList<Guid> values, int minimum, int maximum)
  {
    if (values is null || values.Count < minimum || values.Count > maximum || values.Any(x => x == Guid.Empty) ||
        values.Distinct().Count() != values.Count) Invalid();
    return values.ToHashSet();
  }

  private static HashSet<string> UniqueStrings(IReadOnlyList<string> values, int maximum)
  {
    if (values is null || values.Count > maximum || values.Any(string.IsNullOrWhiteSpace) ||
        values.Distinct(StringComparer.Ordinal).Count() != values.Count) Invalid();
    return values.ToHashSet(StringComparer.Ordinal);
  }

  private static bool IsSha256(string value) => value is { Length: 64 } && value.All(IsLowerHex);
  private static bool IsLowerHex(char value) => value is >= '0' and <= '9' or >= 'a' and <= 'f';
  private static string FormatGuid(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
  private static string FormatUtc(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

  private static void RejectDuplicateProperties(JsonElement element)
  {
    if (element.ValueKind == JsonValueKind.Object)
    {
      var seen = new HashSet<string>(StringComparer.Ordinal);
      foreach (var property in element.EnumerateObject())
      {
        if (!seen.Add(property.Name)) Invalid();
        RejectDuplicateProperties(property.Value);
      }
    }
    else if (element.ValueKind == JsonValueKind.Array)
      foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
  }

  private static void WriteSource(Utf8JsonWriter writer, MailAuthorizationSourceIdentity source)
  {
    writer.WritePropertyName("source");
    writer.WriteStartObject();
    writer.WriteString("repositoryCommit", source.RepositoryCommit);
    writer.WriteString("configureMailAccessSha256", source.ConfigureMailAccessSha256);
    writer.WriteString("mailAccessModuleSha256", source.MailAccessModuleSha256);
    writer.WriteString("modulesLockSha256", source.ModulesLockSha256);
    writer.WriteString("commonModuleSha256", source.CommonModuleSha256);
    writer.WriteString("entraModuleSha256", source.EntraModuleSha256);
    writer.WriteString("planSchemaSha256", source.PlanSchemaSha256);
    writer.WriteEndObject();
  }

  private static void WriteProviderSnapshot(Utf8JsonWriter writer, MailAuthorizationProviderSnapshot snapshot)
  {
    writer.WriteStartObject();
    writer.WriteString("tenantId", FormatGuid(snapshot.TenantId));
    writer.WriteString("permissionModel", ModelName(snapshot.PermissionModel));
    writer.WriteString("runtimeClientId", FormatGuid(snapshot.RuntimeClientId));
    writer.WriteString("enterpriseServicePrincipalObjectId", FormatGuid(snapshot.EnterpriseServicePrincipalObjectId));
    writer.WriteString("graphServicePrincipalObjectId", FormatGuid(snapshot.GraphServicePrincipalObjectId));
    writer.WriteString("graphServicePrincipalAppId", FormatGuid(snapshot.GraphServicePrincipalAppId));
    writer.WriteString("graphMailSendAppRoleId", FormatGuid(snapshot.GraphMailSendAppRoleId));
    writer.WriteString("graphMailSendRoleValue", snapshot.GraphMailSendRoleValue);
    writer.WriteBoolean("graphMailSendRoleEnabled", snapshot.GraphMailSendRoleEnabled);
    WriteStringArray(writer, "graphMailSendAllowedMemberTypes", snapshot.GraphMailSendAllowedMemberTypes);
    WriteGuidArray(writer, "senderMailboxObjectIds", snapshot.SenderMailboxObjectIds);
    writer.WriteString("controlMailboxObjectId", FormatGuid(snapshot.ControlMailboxObjectId));
    writer.WritePropertyName("entraGrantSet");
    writer.WriteStartArray();
    foreach (var grant in snapshot.EntraGrantSet.OrderBy(x => FormatGuid(x.AssignmentObjectId), StringComparer.Ordinal))
    {
      writer.WriteStartObject();
      writer.WriteString("assignmentObjectId", FormatGuid(grant.AssignmentObjectId));
      writer.WriteString("principalObjectId", FormatGuid(grant.PrincipalObjectId));
      writer.WriteString("resourceObjectId", FormatGuid(grant.ResourceObjectId));
      writer.WriteString("appRoleId", FormatGuid(grant.AppRoleId));
      writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WritePropertyName("exchangeRoleAssignmentSet");
    writer.WriteStartArray();
    foreach (var assignment in snapshot.ExchangeRoleAssignmentSet.OrderBy(x => FormatGuid(x.RoleAssigneeObjectId), StringComparer.Ordinal)
        .ThenBy(x => x.Role, StringComparer.Ordinal).ThenBy(x => x.AssignmentMethod, StringComparer.Ordinal))
    {
      writer.WriteStartObject();
      writer.WriteString("role", assignment.Role);
      writer.WriteString("roleAssigneeObjectId", FormatGuid(assignment.RoleAssigneeObjectId));
      writer.WriteString("roleAssigneeType", assignment.RoleAssigneeType);
      writer.WriteString("assignmentMethod", assignment.AssignmentMethod);
      writer.WriteEndObject();
    }
    writer.WriteEndArray();
    if (snapshot.LegacyAuthorization is null) writer.WriteNull("legacyAuthorization");
    else WriteLegacyAuthorization(writer, snapshot.LegacyAuthorization);
    if (snapshot.ExchangeRbacAuthorization is null) writer.WriteNull("exchangeRbacAuthorization");
    else WriteExchangeRbac(writer, snapshot.ExchangeRbacAuthorization);
    writer.WriteEndObject();
  }

  private static void WriteLegacyAuthorization(Utf8JsonWriter writer, MailLegacyAuthorizationEvidence evidence)
  {
    writer.WritePropertyName("legacyAuthorization");
    writer.WriteStartObject();
    writer.WriteString("scopeGroupObjectId", FormatGuid(evidence.ScopeGroupObjectId));
    writer.WriteString("scopeGroupName", evidence.ScopeGroupName);
    writer.WriteString("policyScopeName", evidence.PolicyScopeName);
    writer.WriteString("accessRight", evidence.AccessRight);
    WriteGuidArray(writer, "scopeGroupMemberObjectIds", evidence.ScopeGroupMemberObjectIds);
    WriteGuidArray(writer, "policyApplicationClientIds", evidence.PolicyApplicationClientIds);
    writer.WritePropertyName("probes");
    writer.WriteStartArray();
    foreach (var probe in evidence.Probes.OrderBy(x => FormatGuid(x.MailboxObjectId), StringComparer.Ordinal))
    {
      writer.WriteStartObject();
      writer.WriteString("mailboxObjectId", FormatGuid(probe.MailboxObjectId));
      writer.WriteBoolean("allowed", probe.Allowed);
      writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
  }

  private static void WriteExchangeRbac(Utf8JsonWriter writer, MailExchangeRbacEvidence evidence)
  {
    writer.WritePropertyName("exchangeRbacAuthorization");
    writer.WriteStartObject();
    writer.WriteString("servicePrincipalObjectId", FormatGuid(evidence.ServicePrincipalObjectId));
    writer.WriteString("servicePrincipalAppId", FormatGuid(evidence.ServicePrincipalAppId));
    writer.WriteString("managementScopeName", evidence.ManagementScopeName);
    writer.WriteString("scopeRestrictionType", evidence.ScopeRestrictionType);
    writer.WriteBoolean("exclusive", evidence.Exclusive);
    writer.WriteString("recipientFilter", evidence.RecipientFilter);
    WriteGuidArray(writer, "scopeMemberObjectIds", evidence.ScopeMemberObjectIds);
    writer.WritePropertyName("probes");
    writer.WriteStartArray();
    foreach (var probe in evidence.Probes.OrderBy(x => FormatGuid(x.MailboxObjectId), StringComparer.Ordinal))
    {
      writer.WriteStartObject();
      writer.WriteString("mailboxObjectId", FormatGuid(probe.MailboxObjectId));
      writer.WriteString("roleName", probe.RoleName);
      WriteStringArray(writer, "grantedPermissions", probe.GrantedPermissions);
      writer.WriteString("allowedResourceScope", probe.AllowedResourceScope);
      writer.WriteString("scopeType", probe.ScopeType);
      writer.WriteBoolean("inScope", probe.InScope);
      writer.WriteEndObject();
    }
    writer.WriteEndArray();
    writer.WriteEndObject();
  }

  private static void WriteGuidArray(Utf8JsonWriter writer, string name, IEnumerable<Guid> values)
  {
    writer.WritePropertyName(name);
    writer.WriteStartArray();
    foreach (var value in values.OrderBy(FormatGuid, StringComparer.Ordinal))
      writer.WriteStringValue(FormatGuid(value));
    writer.WriteEndArray();
  }

  private static void WriteStringArray(Utf8JsonWriter writer, string name, IEnumerable<string> values)
  {
    writer.WritePropertyName(name);
    writer.WriteStartArray();
    foreach (var value in values.OrderBy(x => x, StringComparer.Ordinal)) writer.WriteStringValue(value);
    writer.WriteEndArray();
  }

  private static string ModelName(MailAuthorizationModel model) => model switch
  {
    MailAuthorizationModel.LegacyApplicationAccessPolicy => "LEGACY_APPLICATION_ACCESS_POLICY",
    MailAuthorizationModel.ExchangeRbacResourceScope => "EXCHANGE_RBAC_RESOURCE_SCOPE",
    _ => throw new MailAuthorizationAttestationException(InvalidCode)
  };

  [DoesNotReturn]
  private static void Invalid() => throw new MailAuthorizationAttestationException(InvalidCode);

  private sealed class MailAuthorizationModelConverter : JsonConverter<MailAuthorizationModel>
  {
    public override MailAuthorizationModel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
      reader.GetString() switch
      {
        "LEGACY_APPLICATION_ACCESS_POLICY" => MailAuthorizationModel.LegacyApplicationAccessPolicy,
        "EXCHANGE_RBAC_RESOURCE_SCOPE" => MailAuthorizationModel.ExchangeRbacResourceScope,
        _ => throw new JsonException()
      };

    public override void Write(Utf8JsonWriter writer, MailAuthorizationModel value, JsonSerializerOptions options) =>
      writer.WriteStringValue(ModelName(value));
  }
}

public sealed record MailAuthorizationAttestationVerifierOptions(string PinnedSignerCertificateSha256, TimeSpan MaximumEvidenceAge);

/// <summary>
/// Server-owned current binding values. Submitted values must match these expectations after the detached signature
/// verifies; they are never learned from the attestation itself.
/// </summary>
public sealed record MailAuthorizationAttestationExpectedBinding(
  Guid TenantId,
  MailAuthorizationModel PermissionModel,
  Guid RuntimeClientId,
  Guid EnterpriseServicePrincipalObjectId,
  Guid ConnectionRevisionId,
  Guid ConsentAttemptId,
  string PlanSha256,
  MailAuthorizationSourceIdentity Source,
  IReadOnlyList<Guid> SenderMailboxObjectIds,
  Guid ControlMailboxObjectId);

/// <summary>
/// Verifies detached CMS with the existing Pkcs implementation. Trust is an exact configured SHA-256 certificate pin;
/// embedded certificates are never used to build or extend a trust chain. CMS signingTime is intentionally ignored.
/// </summary>
public static class MailAuthorizationAttestationVerifier
{
  private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

  public static MailAuthorizationAttestation Verify(
    ReadOnlySpan<byte> canonicalPayload,
    ReadOnlySpan<byte> detachedCms,
    MailAuthorizationAttestationVerifierOptions options,
    DateTimeOffset nowUtc)
  {
    if (options is null)
      throw new MailAuthorizationAttestationException(MailAuthorizationAttestationCanonicalizer.InvalidCode);
    var pin = options.PinnedSignerCertificateSha256;
    if (pin is null || pin.Length != 64 ||
        pin.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')) ||
        options.MaximumEvidenceAge < TimeSpan.FromHours(1) || options.MaximumEvidenceAge > TimeSpan.FromHours(168) ||
        nowUtc.Offset != TimeSpan.Zero || canonicalPayload.Length is < 2 or > MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes ||
        detachedCms.Length is < 1 or > MailAuthorizationAttestationCanonicalizer.MaximumCmsBytes)
      throw new MailAuthorizationAttestationException(MailAuthorizationAttestationCanonicalizer.InvalidCode);

    var cms = new SignedCms(new ContentInfo(canonicalPayload.ToArray()), detached: true);
    try { cms.Decode(detachedCms.ToArray()); }
    catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidOperationException)
    {
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNATURE_INVALID");
    }

    if (!cms.Detached || cms.SignerInfos.Count != 1 || cms.Certificates.Count != 1 ||
        cms.SignerInfos[0].Certificate is not { } signer ||
        !string.Equals(cms.SignerInfos[0].DigestAlgorithm.Value, Sha256Oid, StringComparison.Ordinal))
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNATURE_INVALID");

    byte[] pinned;
    try { pinned = Convert.FromHexString(options.PinnedSignerCertificateSha256); }
    catch (FormatException) { throw new MailAuthorizationAttestationException(MailAuthorizationAttestationCanonicalizer.InvalidCode); }
    byte[] actualPin;
    try { actualPin = signer.GetCertHash(HashAlgorithmName.SHA256); }
    catch (CryptographicException)
    {
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNER_INVALID");
    }
    if (!CryptographicOperations.FixedTimeEquals(pinned, actualPin))
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNER_UNTRUSTED");

    try { cms.CheckSignature(verifySignatureOnly: true); }
    catch (Exception ex) when (ex is CryptographicException or ArgumentException or InvalidOperationException)
    {
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_SIGNATURE_INVALID");
    }

    var attestation = MailAuthorizationAttestationCanonicalizer.ParseCanonical(canonicalPayload);
    MailAuthorizationAttestationCanonicalizer.ValidateFreshness(attestation, nowUtc, options.MaximumEvidenceAge, signer);
    return attestation;
  }

  public static MailAuthorizationAttestation VerifyBound(
    ReadOnlySpan<byte> canonicalPayload,
    ReadOnlySpan<byte> detachedCms,
    MailAuthorizationAttestationVerifierOptions options,
    MailAuthorizationAttestationExpectedBinding expected,
    DateTimeOffset nowUtc)
  {
    var attestation = Verify(canonicalPayload, detachedCms, options, nowUtc);
    if (!MatchesExpectedBinding(attestation, expected))
      throw new MailAuthorizationAttestationException("MAIL_ATTESTATION_BINDING_MISMATCH");
    return attestation;
  }

  internal static bool MatchesExpectedBinding(MailAuthorizationAttestation attestation,
    MailAuthorizationAttestationExpectedBinding expected)
  {
    if (expected is null || expected.TenantId == Guid.Empty || expected.RuntimeClientId == Guid.Empty ||
        expected.EnterpriseServicePrincipalObjectId == Guid.Empty || expected.ConnectionRevisionId == Guid.Empty ||
        expected.ConsentAttemptId == Guid.Empty || expected.ControlMailboxObjectId == Guid.Empty ||
        !MailAuthorizationAttestationCanonicalizer.IsSha256Value(expected.PlanSha256) ||
        !MailAuthorizationAttestationCanonicalizer.IsValidSourceIdentity(expected.Source) ||
        expected.SenderMailboxObjectIds is null || expected.SenderMailboxObjectIds.Count is < 1 or > MailAuthorizationAttestationCanonicalizer.MaximumSenders ||
        expected.SenderMailboxObjectIds.Any(x => x == Guid.Empty) ||
        expected.SenderMailboxObjectIds.Distinct().Count() != expected.SenderMailboxObjectIds.Count ||
        expected.SenderMailboxObjectIds.Contains(expected.ControlMailboxObjectId)) return false;

    var snapshot = attestation.ProviderSnapshot;
    return snapshot.TenantId == expected.TenantId && snapshot.PermissionModel == expected.PermissionModel &&
      snapshot.RuntimeClientId == expected.RuntimeClientId &&
      snapshot.EnterpriseServicePrincipalObjectId == expected.EnterpriseServicePrincipalObjectId &&
      snapshot.ControlMailboxObjectId == expected.ControlMailboxObjectId &&
      snapshot.SenderMailboxObjectIds.Count == expected.SenderMailboxObjectIds.Count &&
      expected.SenderMailboxObjectIds.ToHashSet().SetEquals(snapshot.SenderMailboxObjectIds) &&
      attestation.ConnectionRevisionId == expected.ConnectionRevisionId &&
      attestation.ConsentAttemptId == expected.ConsentAttemptId &&
      string.Equals(attestation.PlanSha256, expected.PlanSha256, StringComparison.Ordinal) &&
      attestation.Source.Equals(expected.Source);
  }
}
