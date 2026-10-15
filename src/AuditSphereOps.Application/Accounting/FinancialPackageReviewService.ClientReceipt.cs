using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Client-safe status for one signed-in management decision request.</summary>
public sealed record ClientFinancialPackageDecisionReceipt(
  Guid RequestId, bool Found, Guid? DecisionId, string? Decision, string? PackageHash,
  Guid? PresentationRevisionId, Guid? PresentationCompositionId, string? CompositionManifestDigest, DateTimeOffset? DecidedAt);

public static partial class FinancialPackageReviewService
{
  /// <summary>
  /// Resolves only the authenticated client's own signed-in management decision. A missing receipt means no matching row was visible
  /// at this read; callers may retry only the original request identity and must not infer that another decision is safe.
  /// </summary>
  public static async Task<CommandResult<ClientFinancialPackageDecisionReceipt>> GetClientDecisionReceiptAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId, Guid requestId, CancellationToken ct = default)
  {
    if (financialPackageId == Guid.Empty || requestId == Guid.Empty ||
        !actor.Roles.Contains("ClientUser", StringComparer.Ordinal))
      return CommandResult<ClientFinancialPackageDecisionReceipt>.Fail(ErrorCodes.ScopeDenied, "Package unavailable.");

    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == financialPackageId, ct);
    if (package is null)
      return CommandResult<ClientFinancialPackageDecisionReceipt>.Fail(ErrorCodes.ScopeDenied, "Package unavailable.");

    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == actor.UserId, ct);
    if (user is null || user.Disabled || !string.Equals(user.UserKind, "Client", StringComparison.Ordinal))
      return CommandResult<ClientFinancialPackageDecisionReceipt>.Fail(ErrorCodes.ScopeDenied, "Package unavailable.");

    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(package.FirmId, package.ClientId, package.EngagementId, ["ClientUser"]), ct);
    if (!authorization.Succeeded)
      return CommandResult<ClientFinancialPackageDecisionReceipt>.Fail(authorization.ErrorCode!, "Package unavailable.");

    var decision = await db.FinancialPackageReviewDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
      x.FinancialPackageId == package.Id && x.RequestId == requestId &&
      x.Stage == FinancialPackageReviewStages.ManagementApproval &&
      x.EvidenceMode == FinancialPackageReviewEvidenceModes.SignedIn && x.DecidedByUserId == actor.UserId, ct);

    return CommandResult<ClientFinancialPackageDecisionReceipt>.Ok(new ClientFinancialPackageDecisionReceipt(
      requestId, decision is not null, decision?.Id, decision?.Decision, decision?.PackageHash,
      decision?.PresentationRevisionId, decision?.PresentationCompositionId, decision?.CompositionManifestDigest,
      decision?.DecidedAt));
  }
}
