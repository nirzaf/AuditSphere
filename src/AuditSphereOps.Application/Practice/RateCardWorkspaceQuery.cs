using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

/// <summary>One charge-out rate version as the rate-card screen shows it. CanApprove is computed for the current actor.</summary>
public sealed record RateCardVersionView(Guid Id, long Version, decimal RatePerHour, string Status, Guid PreparedByUserId,
  Guid? ApprovedByUserId, DateTimeOffset? ApprovedAt, DateTimeOffset CreatedAt, bool CanApprove);

/// <summary>
/// The current state of one role, activity and currency (STE 4.5.1). Approved is the version that quotations and time
/// capture read; Draft is the newest version awaiting a separate approver.
/// </summary>
public sealed record RateCardSlot(string Role, string Activity, string Currency, RateCardVersionView? Approved, RateCardVersionView? Draft);

/// <summary>The firm's current rate slots, whether the actor may record a draft, and the state of the STE QAR baseline (STE-GAP-009).</summary>
public sealed record RateCardWorkspaceView(bool CanRevise, IReadOnlyList<RateCardSlot> Slots, IReadOnlyList<SteBaselineLine> SteBaseline);

/// <summary>Current charge-out rate versions per role, activity and currency. Firm-wide rate governance only.</summary>
public static class RateCardWorkspaceQuery
{
  public static async Task<CommandResult<RateCardWorkspaceView>> GetAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: PracticeTimeService.ApprovalRoles, InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<RateCardWorkspaceView>.Fail(auth.ErrorCode!, auth.Message!);

    // Newest version first within each key, so the first approved and the first draft of a group are the current ones.
    var versions = await db.RateCardVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderBy(x => x.Role).ThenBy(x => x.Activity).ThenBy(x => x.Currency).ThenByDescending(x => x.Version)
      .ToListAsync(ct);
    var slots = versions.GroupBy(x => (x.Role, x.Activity, x.Currency)).Select(group =>
    {
      var approved = group.FirstOrDefault(x => x.Status == PracticeTimeStates.RateApproved);
      var draft = group.FirstOrDefault(x => x.Status == PracticeTimeStates.RateDraft);
      return new RateCardSlot(group.Key.Role, group.Key.Activity, group.Key.Currency,
        approved is null ? null : Version(approved, actor), draft is null ? null : Version(draft, actor));
    }).ToList();

    var baseline = await SteChargeOutRateBaseline.StatusAsync(db, actor.FirmId, ct);
    return CommandResult<RateCardWorkspaceView>.Ok(new(CanRevise: true, slots, baseline));
  }

  private static RateCardVersionView Version(RateCardVersion version, ActorContext actor) =>
    new(version.Id, version.Version, version.RatePerHour, version.Status, version.CreatedByUserId, version.ApprovedByUserId,
      version.ApprovedAt, version.CreatedAt,
      CanApprove: version.Status == PracticeTimeStates.RateDraft && version.CreatedByUserId != actor.UserId);
}
