using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FirmFinanceSnapshot(
  bool CanClosePeriod, List<FirmPeriod> Periods,
  List<FirmAccount> Accounts, List<FirmPosting> RecentPostings);

/// <summary>Firm-ledger projection for a currently authorized finance user.</summary>
public static class FirmFinanceQuery
{
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];

  public static async Task<CommandResult<FirmFinanceSnapshot>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: FinanceRoles,
      InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<FirmFinanceSnapshot>.Fail(auth.ErrorCode!, auth.Message!);

    var canClosePeriod = await db.RoleGrants.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null &&
      x.Role == "FinanceReviewer" && x.ClientId == null && x.EngagementId == null, ct);
    var periods = await db.FirmPeriods.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.PeriodCode)
      .ToListAsync(ct);
    var accounts = await db.FirmAccounts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderBy(x => x.Code)
      .ToListAsync(ct);
    var recentPostings = await db.FirmPostings.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.PostedAt)
      .Take(25)
      .ToListAsync(ct);

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return auth.Succeeded
      ? CommandResult<FirmFinanceSnapshot>.Ok(new FirmFinanceSnapshot(
          canClosePeriod, periods, accounts, recentPostings))
      : CommandResult<FirmFinanceSnapshot>.Fail(auth.ErrorCode!, auth.Message!);
  }
}
