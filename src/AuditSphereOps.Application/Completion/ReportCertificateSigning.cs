using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Completion;

/// <summary>
/// Supplies the firm's report-signing certificate (ADR-0016). The host decides where the certificate and its private
/// key live; Application only asks for it at the moment a Partner signs. Nothing here contacts an outside service.
/// </summary>
public interface IReportSigningCredentialSource
{
  /// <summary>When true, a final report or certified statements cannot be issued without a certificate signature.</summary>
  bool Required { get; }
  /// <summary>True when a certificate location is configured. A configured certificate that cannot be used blocks signing.</summary>
  bool Configured { get; }
  /// <summary>Loads the certificate with its private key. The caller disposes it. Throws when it cannot be loaded.</summary>
  X509Certificate2 Load();
}

/// <summary>What the completion screen may show about report signing; never includes key material or file locations.</summary>
public sealed record ReportSigningStatus(string Mode, bool Required, bool Ready, string? Subject, DateTimeOffset? NotAfter, string Message);

public sealed record ReportCertificateEvidence(string Subject, string Issuer, string SerialNumber, string ThumbprintSha256, DateTimeOffset NotAfter);

public sealed record ReportSignatureVerification(string State, string? Subject, string? ThumbprintSha256, string Message);

public static class ReportSignatureStates
{
  public const string NotCertificateSigned = "NOT_CERTIFICATE_SIGNED";
  public const string Valid = "VALID";
  public const string Altered = "ALTERED";
  public const string Unreadable = "UNREADABLE";
}

public static class ReportCertificateSigning
{
  public const int MinimumRsaKeyBits = 2048;

  /// <summary>
  /// Decides how the next signature is applied. No source, or an optional one that is not configured, keeps the visual
  /// signature only. A required-but-missing or configured-but-unusable certificate fails closed: signing never
  /// silently downgrades to an image.
  /// </summary>
  public static CommandResult<X509Certificate2?> Resolve(IReportSigningCredentialSource? source, DateTimeOffset now)
  {
    if (source is null || (!source.Configured && !source.Required)) return CommandResult<X509Certificate2?>.Ok(null);
    if (!source.Configured)
      return CommandResult<X509Certificate2?>.Fail(ErrorCodes.GateBlocked,
        "Certificate signing is required, but no report-signing certificate is configured. An administrator must provide the firm's certificate before the report can be signed.");
    X509Certificate2 certificate;
    try { certificate = source.Load(); }
    catch (Exception ex) when (ex is CryptographicException or IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
    {
      return CommandResult<X509Certificate2?>.Fail(ErrorCodes.GateBlocked,
        "The configured report-signing certificate could not be loaded. An administrator must correct it before the report can be signed.");
    }
    var problem = Validate(certificate, now);
    if (problem is null) return CommandResult<X509Certificate2?>.Ok(certificate);
    certificate.Dispose();
    return CommandResult<X509Certificate2?>.Fail(ErrorCodes.GateBlocked, problem);
  }

  /// <summary>Returns the reason a certificate cannot sign a report now, or null when it can.</summary>
  public static string? Validate(X509Certificate2 certificate, DateTimeOffset now)
  {
    if (!certificate.HasPrivateKey) return "The report-signing certificate has no private key and cannot sign.";
    if (now < certificate.NotBefore.ToUniversalTime() || now > certificate.NotAfter.ToUniversalTime())
      return $"The report-signing certificate is not valid today (valid {certificate.NotBefore.ToUniversalTime():yyyy-MM-dd} to {certificate.NotAfter.ToUniversalTime():yyyy-MM-dd}). Renew it before signing.";
    using var rsa = certificate.GetRSAPrivateKey();
    if (rsa is null || rsa.KeySize < MinimumRsaKeyBits)
      return $"The report-signing certificate must hold an RSA key of at least {MinimumRsaKeyBits} bits.";
    var usage = certificate.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
    if (usage is not null && (usage.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) == 0)
      return "The report-signing certificate is not issued for digital signatures.";
    return null;
  }

  public static ReportCertificateEvidence Evidence(X509Certificate2 certificate) => new(
    certificate.Subject, certificate.Issuer, certificate.SerialNumber, ThumbprintSha256(certificate), certificate.NotAfter.ToUniversalTime());

  public static string ThumbprintSha256(X509Certificate2 certificate) =>
    Convert.ToHexString(certificate.GetCertHash(HashAlgorithmName.SHA256)).ToLowerInvariant();

  public static ReportSigningStatus Status(IReportSigningCredentialSource? source, DateTimeOffset now)
  {
    var required = source?.Required ?? false;
    var resolved = Resolve(source, now);
    if (!resolved.Succeeded)
      return new(ReportSignatureKinds.Certificate, required, false, null, null, resolved.Message!);
    using var certificate = resolved.Value;
    return certificate is null
      ? new(ReportSignatureKinds.Visual, false, true, null, null,
          "No report-signing certificate is configured. The signed report carries the Partner's signature image and firm seal only; it is not tamper-evident.")
      : new(ReportSignatureKinds.Certificate, required, true, certificate.Subject, certificate.NotAfter.ToUniversalTime(),
          "The signed report is certificate-signed: a PDF reader shows any later change to it. Whether readers trust the signer depends on who issued the certificate.");
  }
}

/// <summary>
/// Checks a certificate-signed PDF against its own embedded signature: the signed byte range must cover the whole
/// file and the signature must verify over exactly those bytes. This proves the bytes are unchanged since signing and
/// names the signing certificate. It does not judge whether that certificate is trusted — that is the reader's and
/// the firm's decision.
/// </summary>
public static partial class ReportSignatureVerifier
{
  [GeneratedRegex(@"/ByteRange\s*\[\s*(\d{1,12})\s+(\d{1,12})\s+(\d{1,12})\s+(\d{1,12})\s*\]", RegexOptions.CultureInvariant)]
  private static partial Regex ByteRange();

  public static ReportSignatureVerification Verify(byte[] pdf)
  {
    if (pdf.Length < 8 || pdf[0] != (byte)'%' || pdf[1] != (byte)'P' || pdf[2] != (byte)'D' || pdf[3] != (byte)'F')
      return new(ReportSignatureStates.Unreadable, null, null, "The file is not a PDF.");
    var matches = ByteRange().Matches(Encoding.Latin1.GetString(pdf));
    if (matches.Count == 0)
      return new(ReportSignatureStates.NotCertificateSigned, null, null, "The PDF carries no certificate signature.");
    if (matches.Count > 1)
      return new(ReportSignatureStates.Altered, null, null, "The PDF carries more than one signature dictionary; it was changed after the report was signed.");
    var m = matches[0];
    long start = long.Parse(m.Groups[1].Value), firstLength = long.Parse(m.Groups[2].Value),
      second = long.Parse(m.Groups[3].Value), secondLength = long.Parse(m.Groups[4].Value);
    if (start != 0 || firstLength <= 0 || second <= firstLength || secondLength <= 0 || second + secondLength != pdf.Length)
      return new(ReportSignatureStates.Altered, null, null,
        "The signature does not cover the whole file: content was added or the signed range was changed after signing.");

    // Between the two signed ranges sits the hex-encoded signature: <3082...00>.
    var gap = pdf.AsSpan((int)firstLength, (int)(second - firstLength));
    if (gap.Length < 4 || gap[0] != (byte)'<' || gap[^1] != (byte)'>')
      return new(ReportSignatureStates.Unreadable, null, null, "The signature container is malformed.");
    byte[] padded;
    try { padded = Convert.FromHexString(Encoding.ASCII.GetString(gap[1..^1])); }
    catch (FormatException) { return new(ReportSignatureStates.Unreadable, null, null, "The signature container is malformed."); }
    var length = DerLength(padded);
    if (length is null) return new(ReportSignatureStates.Unreadable, null, null, "The signature container is malformed.");

    var signed = new byte[firstLength + secondLength];
    pdf.AsSpan(0, (int)firstLength).CopyTo(signed);
    pdf.AsSpan((int)second, (int)secondLength).CopyTo(signed.AsSpan((int)firstLength));
    try
    {
      var cms = new SignedCms(new ContentInfo(signed), detached: true);
      cms.Decode(padded.AsSpan(0, length.Value).ToArray());
      if (cms.SignerInfos.Count != 1 || cms.SignerInfos[0].Certificate is not { } signer)
        return new(ReportSignatureStates.Unreadable, null, null, "The signature does not identify exactly one signing certificate.");
      cms.CheckSignature(verifySignatureOnly: true);
      return new(ReportSignatureStates.Valid, signer.Subject, ReportCertificateSigning.ThumbprintSha256(signer),
        "The document is unchanged since it was certificate-signed.");
    }
    catch (CryptographicException)
    {
      return new(ReportSignatureStates.Altered, null, null, "The signature does not match the document: it was changed after signing.");
    }
  }

  /// <summary>Total length of the outer DER SEQUENCE, so the zero padding after the signature is ignored exactly.</summary>
  private static int? DerLength(byte[] der)
  {
    if (der.Length < 2 || der[0] != 0x30) return null;
    int header, length;
    if (der[1] < 0x80) { header = 2; length = der[1]; }
    else
    {
      var count = der[1] & 0x7F;
      if (count is < 1 or > 3 || der.Length < 2 + count) return null;
      header = 2 + count; length = 0;
      for (var i = 0; i < count; i++) length = (length << 8) | der[2 + i];
    }
    return header + length <= der.Length ? header + length : null;
  }
}
