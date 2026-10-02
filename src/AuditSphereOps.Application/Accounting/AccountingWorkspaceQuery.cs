using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AccountingClientItem(Guid Id, string Name, bool ProfileConfigured);
public sealed record AccountingClientPage(IReadOnlyList<AccountingClientItem> Items, int Total, int Page, int PageSize);
public sealed record AccountingProfileItem(Guid Id, string Revision, string Jurisdiction, string Currency,
  int FiscalMonth, int FiscalDay, string SourceSystem, string SourceIdentifier, string Status);
public sealed record AccountingPeriodItem(Guid Id, string Revision, string Code, string Start, string End,
  string Basis, string Currency, string Status);
public sealed record AccountingBookItem(Guid Id, Guid PeriodId, string Revision, string Code, string Basis,
  string InclusionRule, string Currency, string Status);
public sealed record AccountingAmendmentItem(Guid Id, Guid PeriodId, string PreviousRevision, string Revision,
  string Reason, Guid ActorId, string RecordedAt);
public sealed record AccountingOpeningItem(Guid Id, Guid PeriodId, Guid? PriorPeriodId, Guid? SourcePackageId,
  string SourceHash, string PriorClosing, string CurrentOpening, string Residual, string Status, string Evidence,
  Guid? ApprovedBy, string? ApprovedAt);
public sealed record AccountingClientWorkspace(Guid ClientId, string Name, AccountingProfileItem? Profile,
  IReadOnlyList<AccountingPeriodItem> Periods, bool HasMorePeriods, IReadOnlyList<AccountingBookItem> Books, bool HasMoreBooks,
  IReadOnlyList<AccountingAmendmentItem> Amendments, bool HasMoreAmendments,
  IReadOnlyList<AccountingOpeningItem> OpeningBridges, bool CanReviewOpening);

/// <summary>Client-owned accounting setup; engagement grants do not authorize entire client books.</summary>
public static class AccountingWorkspaceQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<AccountingClientPage>> ListAsync(IClientAccountingDbContext db,
    ActorContext actor, string? search = null, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is < 1 or > 100 || search?.Length > 100)
      return CommandResult<AccountingClientPage>.Fail("request.invalid", "Invalid pagination or search.");
    var grants = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId &&
      g.RevokedAt == null && g.EngagementId == null && Roles.Contains(g.Role))
      .Select(g => g.ClientId).Distinct().ToListAsync(ct);
    var scopes = new List<AuthorizationRequest>();
    foreach (var id in grants)
    {
      var request = new AuthorizationRequest(actor.FirmId, id, RequiredRoles: Roles, InternalOnly: true, RequireFirmWide: id is null);
      if ((await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded) scopes.Add(request);
    }
    if (scopes.Count == 0) return CommandResult<AccountingClientPage>.Fail(ErrorCodes.ScopeDenied, "Accounting unavailable.");
    var firmWide = scopes.Any(s => s.RequireFirmWide);
    var ids = scopes.Where(s => s.ClientId.HasValue).Select(s => s.ClientId!.Value).ToArray();
    var query = db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId && (firmWide || ids.Contains(c.Id)));
    if (!string.IsNullOrWhiteSpace(search))
    {
      var term = search.Trim().ToLowerInvariant();
      query = query.Where(c => c.LegalName.ToLower().Contains(term));
    }
    var total = await query.CountAsync(ct);
    var items = await query.OrderBy(c => c.LegalName).ThenBy(c => c.Id).Skip(page * pageSize).Take(pageSize)
      .Select(c => new AccountingClientItem(c.Id, c.LegalName,
        db.ClientAccountingProfiles.Any(p => p.FirmId == actor.FirmId && p.ClientId == c.Id))).ToListAsync(ct);
    foreach (var scope in scopes)
      if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, scope, ct)).Succeeded)
        return CommandResult<AccountingClientPage>.Fail(ErrorCodes.ScopeDenied, "Accounting unavailable.");
    return CommandResult<AccountingClientPage>.Ok(new(items, total, page, pageSize));
  }

  public static async Task<CommandResult<AccountingClientWorkspace>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: Roles, InternalOnly: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<AccountingClientWorkspace>.Fail(ErrorCodes.ScopeDenied, "Accounting unavailable.");
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(c => c.FirmId == actor.FirmId && c.Id == clientId, ct);
    var p = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .OrderByDescending(x => x.EndDate).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
    var periodIds = periods.Take(100).Select(x => x.Id).ToArray();
    var books = await db.ClientReportingBooks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && periodIds.Contains(x.PeriodId))
      .OrderBy(x => x.PeriodId).ThenBy(x => x.Code).ThenBy(x => x.Id).Take(501).ToListAsync(ct);
    var amendments = await db.ClientPeriodAmendments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && periodIds.Contains(x.PeriodId))
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(101).ToListAsync(ct);
    var openings = await db.OpeningBalanceBridges.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && periodIds.Contains(x.CurrentPeriodId))
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
    if (openings.Count > 100)
      return CommandResult<AccountingClientWorkspace>.Fail(ErrorCodes.GateBlocked, "Opening history requires bounded review.");
    var canReview = (await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, RequiredRoles: ["AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true), ct)).Succeeded;
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<AccountingClientWorkspace>.Fail(ErrorCodes.ScopeDenied, "Accounting unavailable.");
    return CommandResult<AccountingClientWorkspace>.Ok(new(clientId, client.LegalName,
      p is null ? null : new(p.Id, p.Revision.ToString(CultureInfo.InvariantCulture), p.Jurisdiction, p.FunctionalCurrency,
        p.FiscalYearStartMonth, p.FiscalYearStartDay, p.SourceSystem, p.SourceSystemIdentifier, p.Status),
      periods.Take(100).Select(x => new AccountingPeriodItem(x.Id, x.Revision.ToString(CultureInfo.InvariantCulture),
        x.PeriodCode, x.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        x.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.Basis, x.Currency, x.Status)).ToArray(), periods.Count > 100,
      books.Take(500).Select(x => new AccountingBookItem(x.Id, x.PeriodId, x.Revision.ToString(CultureInfo.InvariantCulture),
        x.Code, x.Basis, x.InclusionRule, x.Currency, x.Status)).ToArray(), books.Count > 500,
      amendments.Take(100).Select(x => new AccountingAmendmentItem(x.Id, x.PeriodId,
        x.PreviousRevision.ToString(CultureInfo.InvariantCulture), x.AmendmentRevision.ToString(CultureInfo.InvariantCulture),
        x.Reason, x.CreatedByUserId, x.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))).ToArray(), amendments.Count > 100,
      openings.Select(x => new AccountingOpeningItem(x.Id, x.CurrentPeriodId, x.PriorPeriodId, x.SourcePackageId, x.SourceHash,
        x.PriorClosingAmount.ToString(CultureInfo.InvariantCulture), x.CurrentOpeningAmount.ToString(CultureInfo.InvariantCulture),
        x.Residual.ToString(CultureInfo.InvariantCulture), x.Status, x.EvidenceReference, x.ApprovedByUserId,
        x.ApprovedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))).ToArray(), canReview));
  }
}
