using System.Security.Cryptography.X509Certificates;
using AuditSphereOps.Application.Completion;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>
/// Where the firm's report-signing certificate lives (ADR-0016): a PEM certificate and its PEM private key on a
/// role-specific mount, supplied by the operator. Both paths empty means no certificate is configured.
/// </summary>
public sealed record ReportSigningOptions(string CertificatePath, string PrivateKeyPath, bool Required);

/// <summary>Loads the report-signing certificate on demand; the key is read only at the moment a Partner signs.</summary>
public sealed class PemReportSigningCredentialSource(ReportSigningOptions options) : IReportSigningCredentialSource
{
  public bool Required => options.Required;

  public bool Configured => !string.IsNullOrWhiteSpace(options.CertificatePath) || !string.IsNullOrWhiteSpace(options.PrivateKeyPath);

  public X509Certificate2 Load()
  {
    if (!Path.IsPathFullyQualified(options.CertificatePath) || !Path.IsPathFullyQualified(options.PrivateKeyPath) ||
        !File.Exists(options.CertificatePath) || !File.Exists(options.PrivateKeyPath))
      throw new InvalidOperationException("Report signing requires a certificate file and a private key file at absolute paths.");
    return X509Certificate2.CreateFromPemFile(options.CertificatePath, options.PrivateKeyPath);
  }
}
