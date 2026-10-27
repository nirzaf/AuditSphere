using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

internal sealed record OpeningBridgeCurrentness(ClientReportingPeriod? Prior, bool Current, string? Blocker,
  OpeningBalanceBridgeReview? Review = null);

internal static class OpeningBalanceBridgeCurrentness
{
  internal static string EvidenceHash(OpeningBalanceBridge bridge) => Convert.ToHexStringLower(SHA256.HashData(
    JsonSerializer.SerializeToUtf8Bytes(new
    {
      bridge.Id, bridge.FirmId, bridge.ClientId, bridge.CurrentPeriodId, bridge.PriorPeriodId,
      bridge.SourcePackageId, bridge.SourceHash,
      Closing = bridge.PriorClosingAmount.ToString(CultureInfo.InvariantCulture),
      Opening = bridge.CurrentOpeningAmount.ToString(CultureInfo.InvariantCulture),
      Residual = bridge.Residual.ToString(CultureInfo.InvariantCulture), bridge.EvidenceReference
    })));

  internal static async Task<OpeningBridgeCurrentness> GetAsync(IClientAccountingDbContext db,
    OpeningBalanceBridge bridge, CancellationToken ct)
  {
    var approved = bridge.Status == AccountingWorkflowStates.Approved &&
      bridge.ApprovedByUserId.HasValue && bridge.ApprovedAt.HasValue;
    if (bridge.PriorPeriodId is not { } priorId)
      return new(null, approved, approved ? null : "The opening balance bridge needs reviewer approval.");
    var prior = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == bridge.FirmId && x.ClientId == bridge.ClientId && x.Id == priorId, ct);
    if (prior is null || prior.Status != AccountingWorkflowStates.Closed)
      return new(prior, false, "The linked prior period must be closed before opening evidence can be reviewed or used for close.");
    var hash = EvidenceHash(bridge);
    var review = await db.OpeningBalanceBridgeReviews.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == bridge.FirmId && x.ClientId == bridge.ClientId && x.BridgeId == bridge.Id &&
      x.PriorPeriodId == prior.Id && x.PriorPeriodRevision == prior.Revision && x.EvidenceHash == hash, ct);
    // Existing approvals from the first close remain compatible. A reopened period has a later
    // revision and an immutable amendment; never infer that an earlier approval covers that close.
    var unchangedLegacy = prior.Revision == 2 && !await db.OpeningBalanceBridgeReviews.AsNoTracking().AnyAsync(x =>
      x.FirmId == bridge.FirmId && x.ClientId == bridge.ClientId && x.BridgeId == bridge.Id, ct) &&
      !await db.ClientPeriodAmendments.AsNoTracking().AnyAsync(x =>
      x.FirmId == bridge.FirmId && x.ClientId == bridge.ClientId && x.PeriodId == prior.Id, ct);
    var current = approved && (review is not null || unchangedLegacy);
    return new(prior, current, current ? null : approved
      ? "The prior closing revision changed. Review this opening evidence against the corrected close before closing the successor."
      : "The opening balance bridge needs reviewer approval.", review);
  }
}
