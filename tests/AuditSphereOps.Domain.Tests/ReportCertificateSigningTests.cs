using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// ADR-0016: the final report PDF is certificate-signed so that any later change is detectable. These tests use a
/// throwaway self-signed certificate; they prove tamper evidence, not that a reader trusts the signer.
/// </summary>
public sealed class ReportCertificateSigningTests
{
  internal static X509Certificate2 TestCertificate(string subject = "CN=AuditSphere Test Firm, O=Test", int keyBits = 2048,
    DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null, X509KeyUsageFlags? usage = X509KeyUsageFlags.DigitalSignature)
  {
    using var rsa = RSA.Create(keyBits);
    var request = new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    if (usage is { } flags) request.CertificateExtensions.Add(new X509KeyUsageExtension(flags, critical: true));
    return request.CreateSelfSigned(notBefore ?? DateTimeOffset.UtcNow.AddDays(-1), notAfter ?? DateTimeOffset.UtcNow.AddYears(1));
  }

  internal sealed class FixedSource(Func<X509Certificate2>? load, bool required) : IReportSigningCredentialSource
  {
    public bool Required => required;
    public bool Configured => load is not null;
    public X509Certificate2 Load() => load!();
  }

  private static readonly AuditDeliverableModel Model = new("Test Audit Firm", "Independent auditor's report", "IND-2026-12-31-v1-SIGNED",
    new DateOnly(2027, 3, 1), "Those charged with governance, Gulf Trading LLC",
    [new DocumentSection("Opinion", ["In our opinion the financial statements present fairly, in all material respects, the financial position."])],
    "Engagement Partner");

  [Fact]
  public async Task CertificateSignedReport_Verifies_AndAnyLaterChangeIsDetected()
  {
    using var certificate = TestCertificate();
    var signed = await AuditDeliverableRenderer.RenderCertificateSignedPdfAsync(Model, certificate, "Signed by the Engagement Partner");

    var verified = ReportSignatureVerifier.Verify(signed);
    Assert.Equal((ReportSignatureStates.Valid, ReportCertificateSigning.ThumbprintSha256(certificate)), (verified.State, verified.ThumbprintSha256));
    Assert.Contains("AuditSphere Test Firm", verified.Subject);

    // One changed byte in the signed content breaks the signature.
    var altered = (byte[])signed.Clone();
    var index = Array.IndexOf(altered, (byte)'%', 20); // inside the signed range, away from the header
    altered[index + 1] ^= 0x01;
    Assert.Equal(ReportSignatureStates.Altered, ReportSignatureVerifier.Verify(altered).State);

    // Content appended after signing is not covered by the signature and is reported as a change.
    var appended = signed.Concat("\n% appended after signing\n"u8.ToArray()).ToArray();
    Assert.Equal(ReportSignatureStates.Altered, ReportSignatureVerifier.Verify(appended).State);

    // The image-only PDF is reported honestly as not certificate-signed.
    Assert.Equal(ReportSignatureStates.NotCertificateSigned, ReportSignatureVerifier.Verify(AuditDeliverableRenderer.RenderPdf(Model)).State);
    Assert.Equal(ReportSignatureStates.Unreadable, ReportSignatureVerifier.Verify("not a pdf at all"u8.ToArray()).State);
  }

  [Fact]
  public async Task AnExistingPdf_CanBeCertificateSigned_WithoutLosingItsPages()
  {
    using var certificate = TestCertificate();
    var plain = AuditDeliverableRenderer.RenderPdf(Model);
    var signed = await AuditDeliverableRenderer.CertificateSignPdfAsync(plain, certificate, "Certified financial statements", "Test Audit Firm");
    Assert.Equal(ReportSignatureStates.Valid, ReportSignatureVerifier.Verify(signed).State);
    using var before = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(plain), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
    using var after = PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(signed), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
    Assert.Equal(before.PageCount, after.PageCount);
  }

  [Fact]
  public void SigningPolicy_FailsClosed_AndNeverDowngradesSilently()
  {
    var now = DateTimeOffset.UtcNow;
    // No certificate and not required: the visual signature only, stated as such.
    Assert.Null(ReportCertificateSigning.Resolve(null, now).Value);
    Assert.Null(ReportCertificateSigning.Resolve(new FixedSource(null, required: false), now).Value);
    var visual = ReportCertificateSigning.Status(new FixedSource(null, required: false), now);
    Assert.Equal((ReportSignatureKinds.Visual, true), (visual.Mode, visual.Ready));

    // Required but missing: blocked.
    Assert.Equal(ErrorCodes.GateBlocked, ReportCertificateSigning.Resolve(new FixedSource(null, required: true), now).ErrorCode);
    Assert.False(ReportCertificateSigning.Status(new FixedSource(null, required: true), now).Ready);

    // Configured but unusable is blocked even when certificate signing is optional.
    foreach (var broken in new Func<X509Certificate2>[]
    {
      () => TestCertificate(notBefore: now.AddYears(-2), notAfter: now.AddDays(-1)),            // expired
      () => TestCertificate(notBefore: now.AddDays(5), notAfter: now.AddYears(1)),              // not yet valid
      () => TestCertificate(keyBits: 1024),                                                      // weak key
      () => TestCertificate(usage: X509KeyUsageFlags.KeyEncipherment),                           // not for signatures
      () => { using var full = TestCertificate(); return X509CertificateLoader.LoadCertificate(full.Export(X509ContentType.Cert)); }, // no private key
      () => throw new CryptographicException("unreadable key file")
    })
      Assert.Equal(ErrorCodes.GateBlocked, ReportCertificateSigning.Resolve(new FixedSource(broken, required: false), now).ErrorCode);

    using var good = ReportCertificateSigning.Resolve(new FixedSource(() => TestCertificate(), required: true), now).Value;
    Assert.NotNull(good);
    var ready = ReportCertificateSigning.Status(new FixedSource(() => TestCertificate(), required: true), now);
    Assert.Equal((ReportSignatureKinds.Certificate, true, true), (ready.Mode, ready.Required, ready.Ready));
    Assert.Contains("AuditSphere Test Firm", ready.Subject);
  }
}
