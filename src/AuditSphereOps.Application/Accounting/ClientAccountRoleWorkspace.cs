using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientAccountRoleRequest(Guid ChartVersionId, Guid AccountId, string Role, DateOnly EffectiveFrom, DateOnly? EffectiveTo, string Reason);
public sealed record ClientAccountRoleView(Guid Id, Guid ChartVersionId, Guid AccountId, string Role, string EffectiveFrom, string? EffectiveTo, string Reason, Guid ProposedByUserId, string? Decision, string? DecisionReason, Guid? ReviewedByUserId);
public sealed record ClientRoleAccountView(Guid Id, Guid ChartVersionId, string AccountCode, string AccountName, string AccountType);
public sealed record ClientAccountRoleList(Guid ClientId, bool BookkeepingActive, bool CanReview, int Page, bool HasMore, bool HasMoreAccounts, IReadOnlyList<ClientAccountRoleView> Configurations, IReadOnlyList<ClientRoleAccountView> Accounts);

public static class ClientAccountRoleWorkspace
{
  private static readonly string[] Preparers = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Roles = ["AR", "AP", "TAX_RECOVERABLE", "TAX_PAYABLE", "REVENUE", "PURCHASE_EXPENSE", "PURCHASE_ASSET", "RETAINED_EARNINGS", "ROUNDING", "FX"];
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid client, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, client, RequiredRoles: roles, InternalOnly: true), ct);
  private static bool Compatible(string role, string accountType) => role switch
  {
    "AR" or "TAX_RECOVERABLE" or "PURCHASE_ASSET" => accountType == "ASSET",
    "AP" or "TAX_PAYABLE" => accountType == "LIABILITY",
    "REVENUE" => accountType == "INCOME", "PURCHASE_EXPENSE" => accountType == "EXPENSE",
    "RETAINED_EARNINGS" => accountType == "EQUITY", "ROUNDING" or "FX" => accountType is "INCOME" or "EXPENSE", _ => false
  };
  public static async Task<CommandResult<Guid>> ProposeAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, ClientAccountRoleRequest request, CancellationToken ct = default)
  {
    var role = (request.Role ?? "").Trim().ToUpperInvariant(); var reason = (request.Reason ?? "").Trim();
    if (!Roles.Contains(role) || reason.Length is 0 or > 2000 || request.EffectiveFrom == default || request.EffectiveTo < request.EffectiveFrom)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an explicit account role, supported interval and reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct) || !await db.ClientAccountingProfiles.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Current accepted native bookkeeping service is required.");
    var account = await db.ClientAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.AccountId && x.ChartVersionId == request.ChartVersionId, ct);
    var chart = await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.ChartVersionId, ct);
    if (account is null || chart is null || chart.Status != AccountingWorkflowStates.Approved || account.Status != AccountingWorkflowStates.Active || !account.IsPosting || !Compatible(role, account.AccountType) || request.EffectiveFrom < chart.EffectiveFrom || (chart.EffectiveTo.HasValue && (request.EffectiveTo == null || request.EffectiveTo > chart.EffectiveTo)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a compatible active posting account and an interval within its approved chart.");
    var row = new ClientAccountRoleConfiguration { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, ChartVersionId = chart.Id, AccountId = account.Id, Role = role,
      EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, Reason = reason, ProposedByUserId = actor.UserId, ProposedAt = DateTimeOffset.UtcNow };
    db.ClientAccountRoleConfigurations.Add(row); await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Authority changed.");
    await tx.CommitAsync(ct); return CommandResult<Guid>.Ok(row.Id);
  }

  public static async Task<CommandResult> ReviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid configurationId, string decision, string reason, CancellationToken ct = default)
  {
    decision = (decision ?? "").Trim().ToUpperInvariant(); reason = (reason ?? "").Trim();
    if (decision is not ("APPROVE" or "REJECT") || reason.Length is 0 or > 2000) return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose approval/rejection with a reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    var row = await db.ClientAccountRoleConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == configurationId, ct);
    if (client is null || row is null || row.ProposedByUserId == actor.UserId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "An independent scoped reviewer is required.");
    var existing = await db.ClientAccountRoleDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ConfigurationId == configurationId, ct);
    if (existing is not null) return existing.ReviewedByUserId == actor.UserId && existing.Decision == decision && existing.Reason == reason ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.IdempotencyConflict, "This proposal already has an immutable decision.");
    if (!await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)) return CommandResult.Fail(ErrorCodes.GateBlocked, "The bookkeeping service is inactive.");
    if (decision == "APPROVE")
    {
      var account = await db.ClientAccounts.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == row.AccountId, ct);
      if (!account.IsPosting || account.Status != AccountingWorkflowStates.Active || !Compatible(row.Role, account.AccountType)) return CommandResult.Fail(ErrorCodes.GateBlocked, "The proposed account is no longer eligible.");
      var approved = from c in db.ClientAccountRoleConfigurations join d in db.ClientAccountRoleDecisions on c.Id equals d.ConfigurationId
        where c.FirmId == actor.FirmId && c.ClientId == clientId && d.FirmId == actor.FirmId && d.ClientId == clientId && d.Decision == "APPROVE" && c.ChartVersionId == row.ChartVersionId select c;
      if (await approved.AnyAsync(x => (x.Role == row.Role || x.AccountId == row.AccountId && (x.Role == "AR" || x.Role == "AP" || row.Role == "AR" || row.Role == "AP")) && (row.EffectiveTo == null || x.EffectiveFrom <= row.EffectiveTo) && (x.EffectiveTo == null || x.EffectiveTo >= row.EffectiveFrom), ct))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "An approved role or incompatible control assignment overlaps this interval.");
      if (row.Role is "AR" or "AP" && await (from l in db.ClientOperationalJournalLines join j in db.ClientOperationalJournals on l.JournalId equals j.Id
        where l.FirmId == actor.FirmId && l.ClientId == clientId && l.ClientAccountId == row.AccountId && j.FirmId == actor.FirmId && j.ClientId == clientId && j.Status == "POSTED" && j.PostingDate >= row.EffectiveFrom && (row.EffectiveTo == null || j.PostingDate <= row.EffectiveTo) select l.Id).AnyAsync(ct))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Control activation cannot reclassify posted generic activity. Use a reviewed subledger cutover.");
    }
    db.ClientAccountRoleDecisions.Add(new() { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, ConfigurationId = row.Id, Decision = decision, Reason = reason, ReviewedByUserId = actor.UserId, ReviewedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Authority changed.");
    await tx.CommitAsync(ct); return CommandResult.Ok();
  }

  public static async Task<CommandResult<ClientAccountRoleList>> ListAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, int page = 0, string accountSearch = "", CancellationToken ct = default)
  {
    accountSearch = (accountSearch ?? "").Trim();
    if (page is < 0 or > 100000 || accountSearch.Length > 100) return CommandResult<ClientAccountRoleList>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose bounded account search and history page.");
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientAccountRoleList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rows = await db.ClientAccountRoleConfigurations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId).OrderByDescending(x => x.ProposedAt).ThenByDescending(x => x.Id).Skip(page * 25).Take(26).ToListAsync(ct);
    var ids = rows.Take(25).Select(x => x.Id).ToArray();
    var decisions = await db.ClientAccountRoleDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && ids.Contains(x.ConfigurationId)).ToDictionaryAsync(x => x.ConfigurationId, ct);
    var accounts = await (from a in db.ClientAccounts.AsNoTracking() join c in db.ClientChartVersions.AsNoTracking() on a.ChartVersionId equals c.Id
      where a.FirmId == actor.FirmId && a.ClientId == clientId && c.FirmId == actor.FirmId && c.ClientId == clientId && c.Status == AccountingWorkflowStates.Approved && a.IsPosting && a.Status == AccountingWorkflowStates.Active
        && (accountSearch == "" || a.AccountCode.Contains(accountSearch) || a.AccountName.Contains(accountSearch))
      orderby a.AccountCode, a.ChartVersionId, a.Id select new ClientRoleAccountView(a.Id,a.ChartVersionId,a.AccountCode,a.AccountName,a.AccountType)).Take(51).ToListAsync(ct);
    var active = await db.ClientAccountingProfiles.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct) && await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    var canReview = (await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded;
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded) return CommandResult<ClientAccountRoleList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<ClientAccountRoleList>.Ok(new(clientId,active,canReview,page,rows.Count>25,accounts.Count>50,rows.Take(25).Select(c => {
      decisions.TryGetValue(c.Id,out var d); return new ClientAccountRoleView(c.Id,c.ChartVersionId,c.AccountId,c.Role,c.EffectiveFrom.ToString("yyyy-MM-dd"),c.EffectiveTo?.ToString("yyyy-MM-dd"),c.Reason,c.ProposedByUserId,d?.Decision,d?.Reason,d?.ReviewedByUserId);
    }).ToArray(),accounts.Take(50).ToArray()));
  }

  public static Task<bool> UsesControlAsync(IClientAccountingDbContext db, Guid firmId, Guid clientId, DateOnly date, Guid[] accountIds, CancellationToken ct) =>
    (from c in db.ClientAccountRoleConfigurations join d in db.ClientAccountRoleDecisions on c.Id equals d.ConfigurationId
     where c.FirmId == firmId && c.ClientId == clientId && d.FirmId == firmId && d.ClientId == clientId && d.Decision == "APPROVE" && (c.Role == "AR" || c.Role == "AP") && accountIds.Contains(c.AccountId) && c.EffectiveFrom <= date && (c.EffectiveTo == null || c.EffectiveTo >= date) select c.Id).AnyAsync(ct);
}
