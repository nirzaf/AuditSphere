using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>
/// Central production browser-security header policy. Script sources stay strict: hashes are
/// computed from the approved Angular build, never broad allowances. <c>style-src</c> requires
/// <c>'unsafe-inline'</c> as a reviewed, documented need: the Angular production build inlines
/// critical CSS and Angular Material injects component styles at runtime, which style hashes
/// cannot cover. <c>frame-ancestors 'none'</c> is expressed through CSP; X-Frame-Options remains
/// for legacy user agents.
/// </summary>
public sealed partial class SecurityHeaderOptions
{
  public const string SectionName = "HttpBoundary:SecurityHeaders";

  public string PermissionsPolicy { get; set; } = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
  public IReadOnlyList<string> InlineScriptHashes { get; set; } = [];
  public bool UpgradeInsecureRequests { get; set; }
  public string? ContentSecurityPolicy { get; set; }

  public string EffectiveContentSecurityPolicy =>
    ContentSecurityPolicy ?? BuildContentSecurityPolicy(InlineScriptHashes, UpgradeInsecureRequests);

  public static string BuildContentSecurityPolicy(IReadOnlyList<string> inlineScriptHashes, bool upgradeInsecureRequests)
  {
    var script = "'self'" + string.Concat(inlineScriptHashes.Select(hash => $" 'sha256-{hash}'"));
    return $"default-src 'self'; script-src {script}; style-src 'self' 'unsafe-inline'; img-src 'self' data:; " +
      "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'" +
      (upgradeInsecureRequests ? "; upgrade-insecure-requests" : string.Empty);
  }

  /// <summary>
  /// SHA-256 hashes of every inline script in the approved Angular shell so the CSP can keep
  /// script-src strict without 'unsafe-inline' or 'unsafe-eval'. External scripts (src=) are
  /// excluded by filtering the opening tag in code: NonBacktracking regexes do not support
  /// negative lookaheads.
  /// </summary>
  public static IReadOnlyList<string> ComputeInlineScriptHashes(string indexHtml)
  {
    var hashes = new List<string>();
    foreach (Match match in InlineScriptPattern().Matches(indexHtml))
    {
      if (match.Groups["open"].Value.Contains("src=", StringComparison.OrdinalIgnoreCase)) continue;
      var digest = SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups["body"].Value));
      hashes.Add(Convert.ToBase64String(digest));
    }
    return hashes;
  }

  [GeneratedRegex(@"<script(?<open>[^>]*)>(?<body>.*?)</script>",
    RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
  private static partial Regex InlineScriptPattern();
}
