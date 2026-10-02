using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ChartPublicationReview(Guid ChartId, string Version, string Digest, int AccountCount, bool CanPublish);
public static class ChartPublicationQuery
{
  public static async Task<CommandResult<ChartPublicationReview>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid chartId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, clientId,
      RequiredRoles: ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ChartPublicationReview>.Fail(ErrorCodes.ScopeDenied, "Publication review unavailable.");
    var chart = await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == chartId, ct);
    if (chart is null) return CommandResult<ChartPublicationReview>.Fail(ErrorCodes.ScopeDenied, "Publication review unavailable.");
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == chartId)
      .OrderBy(x => x.Id).Take(10001).ToListAsync(ct);
    if (accounts.Count > 10000) return CommandResult<ChartPublicationReview>.Fail(ErrorCodes.GateBlocked, "Chart requires bounded publication review.");
    var aliases = await db.SourceAccountAliases.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == chartId)
      .OrderBy(x => x.Id).Take(10001).ToListAsync(ct);
    if (aliases.Count > 10000) return CommandResult<ChartPublicationReview>.Fail(ErrorCodes.GateBlocked, "Chart requires bounded alias review.");
    var canPublish = chart.Status == AccountingWorkflowStates.Draft && chart.CreatedByUserId != actor.UserId && accounts.Count > 0 &&
      (await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId,
        RequiredRoles: ["AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true), ct)).Succeeded;
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ChartPublicationReview>.Fail(ErrorCodes.ScopeDenied, "Publication review unavailable.");
    return CommandResult<ChartPublicationReview>.Ok(new(chartId, chart.Version.ToString(CultureInfo.InvariantCulture), Digest(accounts, aliases), accounts.Count, canPublish));
  }
  internal static string Digest(IEnumerable<ClientAccount> accounts, IEnumerable<SourceAccountAlias> aliases) => Hashing.Sha256Hex(JsonSerializer.Serialize(new
  {
    Accounts = accounts.OrderBy(x => x.Id).Select(x => new { x.Id, x.StableIdentity, x.AccountCode, x.AccountName, x.AccountType,
      x.NormalBalance, x.IsPosting, x.ParentAccountId, x.Status }),
    Aliases = aliases.OrderBy(x => x.Id).Select(x => new { x.Id, x.ClientAccountId, x.SourceSystem, x.AliasCode, x.AliasName })
  }));
}
