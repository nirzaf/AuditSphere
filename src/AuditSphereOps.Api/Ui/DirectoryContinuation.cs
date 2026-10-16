using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AuditSphereOps.Api.Ui;

internal sealed record DirectoryContinuationContext(
  Guid FirmId, Guid ActorId, long ActorEpoch, Guid TenantId, bool Browse, string? Prefix, string? Domain);

/// <summary>
/// HTTP-only query binding. Application and legacy provider cursors remain raw and are not globally fenced.
/// Uses the host's existing Data Protection keys; carries no authority or consent grant.
/// </summary>
internal sealed class DirectoryContinuation(IDataProtectionProvider provider)
{
  internal const int MaximumEnvelopeLength = 20000;
  internal const int MaximumProviderTokenLength = 2048;
  internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
  private const string Marker = "dc1.";
  private readonly IDataProtector protector = provider.CreateProtector("AuditSphere.DirectoryContinuation.v1");

  private sealed record Envelope(int Version, DirectoryContinuationContext Context, string ProviderToken,
    DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

  internal static bool TryContext(ActorContext actor, string configuredTenantId, bool browse, string? prefix,
    string? domain, out DirectoryContinuationContext? context)
  {
    context = null;
    if (!Guid.TryParse(configuredTenantId, out var tenant) || tenant == Guid.Empty)
      return false;
    var normalizedDomain = domain?.Trim().TrimStart('@').ToLowerInvariant();
    if (normalizedDomain is { Length: > 0 } name &&
        (name.Length > 253 || !name.Contains('.') || name.StartsWith('.') || name.EndsWith('.') ||
         name.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '.' or '-'))))
      return false;
    if (!browse && (string.IsNullOrWhiteSpace(prefix) || prefix.Trim().Length < 2 ||
        prefix.Length > 80 || prefix.Any(char.IsControl)))
      return false;
    context = new(actor.FirmId, actor.UserId, actor.SessionEpoch, tenant, browse,
      browse ? null : prefix!.Trim(), string.IsNullOrEmpty(normalizedDomain) ? null : normalizedDomain);
    return true;
  }

  internal bool TryRead(string? token, DirectoryContinuationContext expected, DateTimeOffset now,
    out string? providerToken)
  {
    providerToken = null;
    if (token is null) return true;
    if (token.Length > MaximumEnvelopeLength || !token.StartsWith(Marker, StringComparison.Ordinal))
      return false;
    try
    {
      var envelope = JsonSerializer.Deserialize<Envelope>(protector.Unprotect(token[Marker.Length..]));
      if (envelope is null || envelope.Version != 1 || envelope.Context != expected ||
          envelope.IssuedAt > now || envelope.ExpiresAt <= now ||
          envelope.ExpiresAt - envelope.IssuedAt != Lifetime ||
          string.IsNullOrWhiteSpace(envelope.ProviderToken) || envelope.ProviderToken.Length > MaximumProviderTokenLength)
        return false;
      providerToken = envelope.ProviderToken;
      return true;
    }
    catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException or ArgumentException)
    {
      return false;
    }
  }

  internal bool TryWrite(string? providerToken, DirectoryContinuationContext context, DateTimeOffset now,
    out string? token)
  {
    token = null;
    if (providerToken is null) return true;
    if (string.IsNullOrWhiteSpace(providerToken) || providerToken.Length > MaximumProviderTokenLength)
      return false;
    token = Marker + protector.Protect(JsonSerializer.Serialize(new Envelope(1, context, providerToken, now, now + Lifetime)));
    if (token.Length <= MaximumEnvelopeLength) return true;
    token = null;
    return false;
  }
}
