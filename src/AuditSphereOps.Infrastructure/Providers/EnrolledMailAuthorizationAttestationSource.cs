using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Infrastructure.Providers;

/// <summary>Exact public signed bytes from the deployment's firm/tenant; the runtime gate verifies every read.</summary>
public sealed class EnrolledMailAuthorizationAttestationSource(
  IAuditSphereDbContextFactory factory, MailAuthorizationRuntimeOptions options) : IMailAuthorizationAttestationSource
{
  public async Task<MailAuthorizationAttestationBundle> ReadAsync(CancellationToken ct)
  {
    options.Validate();
    await using var db = await factory.CreateAsync(ct);
    var row = await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == options.FirmId && x.TenantId == options.TenantId.ToString("D") &&
        x.Capability == Microsoft365Capabilities.OutboundMail && x.State == CapabilityVerificationStates.Verified &&
        x.MailAuthorizationModel == MailAuthorizationEvidenceEnrollment.LegacyModel &&
        x.MailCanonicalPayload != null && x.MailDetachedCms != null)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id)
      .Select(x => new { x.State, x.MailAuthorizationModel, x.MailCanonicalPayload, x.MailDetachedCms })
      .FirstOrDefaultAsync(ct);
    if (row is null || row.State != CapabilityVerificationStates.Verified ||
        row.MailAuthorizationModel != MailAuthorizationEvidenceEnrollment.LegacyModel ||
        row.MailCanonicalPayload is not { Length: >= 2 and <= MailAuthorizationAttestationCanonicalizer.MaximumPayloadBytes } ||
        row.MailDetachedCms is not { Length: >= 1 and <= MailAuthorizationAttestationCanonicalizer.MaximumCmsBytes })
      throw new IOException("Current enrolled mail authorization evidence is unavailable.");
    return new(row.MailCanonicalPayload, row.MailDetachedCms);
  }
}
