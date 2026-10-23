using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;

namespace AuditSphereOps.Infrastructure.Providers;

public sealed class GraphMailAccessTokenValidationException(string code) : Exception(code)
{
  public string Code { get; } = code;
}

/// <summary>
/// Mail-only claim checks for the access token returned directly by the configured Entra token endpoint. This does
/// not alter the exact-role validator used by other Graph capabilities.
/// </summary>
public static class GraphMailAccessTokenValidator
{
  public const string GraphAudience = "00000003-0000-0000-c000-000000000000";
  public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(2);

  public static void Validate(string accessToken, MailAuthorizationModel model, Guid tenantId, Guid clientId,
    DateTimeOffset nowUtc)
  {
    if (string.IsNullOrWhiteSpace(accessToken) || accessToken.Length > 65_536 ||
        tenantId == Guid.Empty || clientId == Guid.Empty || nowUtc.Offset != TimeSpan.Zero ||
        model is not (MailAuthorizationModel.LegacyApplicationAccessPolicy or MailAuthorizationModel.ExchangeRbacResourceScope))
      Invalid();

    try
    {
      var segments = accessToken.Split('.');
      if (segments.Length != 3 || segments.Any(string.IsNullOrEmpty)) Invalid();
      using var header = JsonDocument.Parse(DecodeBase64Url(segments[0]), StrictJsonDocumentOptions);
      using var payload = JsonDocument.Parse(DecodeBase64Url(segments[1]), StrictJsonDocumentOptions);
      _ = DecodeBase64Url(segments[2]);
      if (header.RootElement.ValueKind != JsonValueKind.Object || payload.RootElement.ValueKind != JsonValueKind.Object)
        Invalid();
      RejectDuplicateProperties(header.RootElement);
      RejectDuplicateProperties(payload.RootElement);

      if (!header.RootElement.TryGetProperty("alg", out var algorithm) ||
          algorithm.ValueKind != JsonValueKind.String || algorithm.GetString() != "RS256" ||
          !GuidClaimMatches(payload.RootElement, "tid", tenantId) ||
          !payload.RootElement.TryGetProperty("aud", out var audience) ||
          audience.ValueKind != JsonValueKind.String || audience.GetString() != GraphAudience ||
          !ClientClaimsMatch(payload.RootElement, clientId) ||
          !ExpirationIsCurrentAndBounded(payload.RootElement, nowUtc) ||
          HasDelegatedScope(payload.RootElement) || !RolesMatch(payload.RootElement, model))
        Invalid();
    }
    catch (GraphMailAccessTokenValidationException)
    {
      throw;
    }
    catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or OverflowException)
    {
      Invalid();
    }
  }

  private static readonly JsonDocumentOptions StrictJsonDocumentOptions = new()
  {
    AllowTrailingCommas = false,
    CommentHandling = JsonCommentHandling.Disallow,
    MaxDepth = 16
  };

  private static bool GuidClaimMatches(JsonElement payload, string claim, Guid expected) =>
    payload.TryGetProperty(claim, out var value) && value.ValueKind == JsonValueKind.String &&
    Guid.TryParse(value.GetString(), out var actual) && actual == expected;

  private static bool ClientClaimsMatch(JsonElement payload, Guid expected)
  {
    var found = false;
    foreach (var name in new[] { "appid", "azp" })
    {
      if (!payload.TryGetProperty(name, out var value)) continue;
      found = true;
      if (value.ValueKind != JsonValueKind.String || !Guid.TryParse(value.GetString(), out var actual) || actual != expected)
        return false;
    }
    return found;
  }

  private static bool ExpirationIsCurrentAndBounded(JsonElement payload, DateTimeOffset nowUtc)
  {
    if (!payload.TryGetProperty("exp", out var expiration) || expiration.ValueKind != JsonValueKind.Number ||
        !expiration.TryGetInt64(out var unixSeconds)) return false;
    DateTimeOffset expiresAt;
    try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(unixSeconds); }
    catch (ArgumentOutOfRangeException) { return false; }
    if (expiresAt <= nowUtc || expiresAt > nowUtc + MaximumLifetime) return false;

    if (!payload.TryGetProperty("nbf", out var notBefore)) return true;
    if (notBefore.ValueKind != JsonValueKind.Number || !notBefore.TryGetInt64(out var nbfSeconds)) return false;
    try { return DateTimeOffset.FromUnixTimeSeconds(nbfSeconds) <= nowUtc; }
    catch (ArgumentOutOfRangeException) { return false; }
  }

  private static bool HasDelegatedScope(JsonElement payload) => payload.TryGetProperty("scp", out _);

  private static bool RolesMatch(JsonElement payload, MailAuthorizationModel model)
  {
    if (!payload.TryGetProperty("roles", out var roles))
      return model == MailAuthorizationModel.ExchangeRbacResourceScope;
    if (roles.ValueKind != JsonValueKind.Array) return false;
    var roleValues = new List<string>();
    foreach (var role in roles.EnumerateArray())
    {
      if (role.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(role.GetString())) return false;
      roleValues.Add(role.GetString()!);
    }
    return model switch
    {
      MailAuthorizationModel.LegacyApplicationAccessPolicy => roleValues.Count == 1 && roleValues[0] == "Mail.Send",
      MailAuthorizationModel.ExchangeRbacResourceScope => roleValues.Count == 0,
      _ => false
    };
  }

  private static byte[] DecodeBase64Url(string value)
  {
    if (value.Length == 0 || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) || value.Length % 4 == 1)
      Invalid();
    var base64 = value.Replace('-', '+').Replace('_', '/');
    base64 += new string('=', (4 - base64.Length % 4) % 4);
    return Convert.FromBase64String(base64);
  }

  private static void RejectDuplicateProperties(JsonElement element)
  {
    if (element.ValueKind == JsonValueKind.Object)
    {
      var names = new HashSet<string>(StringComparer.Ordinal);
      foreach (var property in element.EnumerateObject())
      {
        if (!names.Add(property.Name)) Invalid();
        RejectDuplicateProperties(property.Value);
      }
    }
    else if (element.ValueKind == JsonValueKind.Array)
    {
      foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
  }

  private static void Invalid() => throw new GraphMailAccessTokenValidationException("mail-authorization-token-invalid");
}
