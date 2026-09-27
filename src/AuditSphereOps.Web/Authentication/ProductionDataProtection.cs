using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;

namespace AuditSphereOps.Web.Authentication;

internal static class ProductionDataProtection
{
  public static void Configure(WebApplicationBuilder builder, string? tenantId)
  {
    if (!builder.Environment.IsProduction()) return;

    var keyDirectory = builder.Configuration["DataProtection:KeyDirectory"];
    var certificatePath = builder.Configuration["DataProtection:CertificatePath"];
    var certificatePassword = builder.Configuration["DataProtection:CertificatePassword"];
    if (!Guid.TryParse(tenantId, out var tenant) || tenant == Guid.Empty ||
        !Guid.TryParse(builder.Configuration["Application:InstallationId"], out var installation) ||
        installation == Guid.Empty ||
        string.IsNullOrWhiteSpace(keyDirectory) || !Path.IsPathFullyQualified(keyDirectory) ||
        !Directory.Exists(keyDirectory) ||
        string.IsNullOrWhiteSpace(certificatePath) || !Path.IsPathFullyQualified(certificatePath) ||
        !File.Exists(certificatePath) || string.IsNullOrWhiteSpace(certificatePassword))
      throw new InvalidOperationException(
        "Production Data Protection requires installation ID, existing key directory and certificate credential references.");

    var certificate = LoadCertificate(certificatePath, certificatePassword, requireCurrentValidity: true);
    var decryptionCertificates = new List<X509Certificate2> { certificate };
    foreach (var previous in builder.Configuration.GetSection("DataProtection:PreviousCertificates").GetChildren())
    {
      var path = previous["Path"];
      var password = previous["Password"];
      if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) ||
          !File.Exists(path) || string.IsNullOrWhiteSpace(password))
        throw new InvalidOperationException("Production Data Protection previous certificate is unavailable.");
      decryptionCertificates.Add(LoadCertificate(path, password, requireCurrentValidity: false));
    }

    builder.Services.AddDataProtection()
      .SetApplicationName($"AuditSphereOps.Web:Production:{tenant:D}:{installation:D}")
      .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
      .ProtectKeysWithCertificate(certificate)
      .UnprotectKeysWithAnyCertificate(decryptionCertificates.ToArray());
  }

  private static X509Certificate2 LoadCertificate(string path, string password, bool requireCurrentValidity)
  {
    try
    {
      var certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
      if (!certificate.HasPrivateKey ||
          (requireCurrentValidity && (DateTimeOffset.UtcNow < certificate.NotBefore ||
                                      DateTimeOffset.UtcNow >= certificate.NotAfter)))
        throw new CryptographicException("Data Protection certificate is unavailable for current use.");
      return certificate;
    }
    catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
    {
      throw new InvalidOperationException("Production Data Protection certificate is unavailable.");
    }
  }
}
