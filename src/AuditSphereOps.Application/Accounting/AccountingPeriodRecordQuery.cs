using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record PeriodPbcHandoff(Guid EngagementId, string Owner, string DueDate, string State);
public sealed record PeriodTaskHandoff(string Title, string Owner, string DueDate, string State);
public sealed record AccountingPeriodRecord(Guid Id, Guid ClientId, string ClientName, string Engagement, string PeriodCode, DateOnly StartDate, DateOnly EndDate,
  string Basis, string Book, string Currency, string Status, long Revision, Guid? PriorPeriodId, Guid? PackageId, string? PackageLabel, Guid? MappingId,
  string? MappingLabel, PeriodPbcHandoff? Pbc, PeriodTaskHandoff? Task);

/// <summary>Read-only exact-record view of one reporting period and its persisted handoffs; no owner or due date is invented.</summary>
public static class AccountingPeriodRecordQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<AccountingPeriodRecord>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid periodId, CancellationToken ct = default)
  {
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == periodId, ct);
    if (period is null) return CommandResult<AccountingPeriodRecord>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, ClientId: period.ClientId, RequiredRoles: Roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<AccountingPeriodRecord>.Fail(ErrorCodes.ScopeDenied, "This period is not visible under the current accounting grant.");
    var client = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == period.ClientId).Select(x => x.LegalName).SingleOrDefaultAsync(ct);
    var book = await db.ClientReportingBooks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId && x.PeriodId == period.Id)
      .OrderBy(x => x.Code).Select(x => x.Code).FirstOrDefaultAsync(ct);
    var package = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId && x.PeriodId == period.Id)
      .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.MappingVersionId, x.Status, x.Revision, x.Currency }).FirstOrDefaultAsync(ct);
    var mapping = package?.MappingVersionId is { } mappingId
      ? await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == mappingId).Select(x => new { x.Id, x.Version, x.Status }).SingleOrDefaultAsync(ct) : null;
    var start = period.StartDate.ToString("yyyy-MM-dd"); var end = period.EndDate.ToString("yyyy-MM-dd");
    var pbc = await db.PbcRequests.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId && x.PeriodStart == start && x.PeriodEnd == end &&
      x.State != PbcStates.Closed).OrderBy(x => x.DueDate).FirstOrDefaultAsync(ct);
    var task = await db.WorkTasks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId && x.ReportingPeriodId == period.Id &&
      (x.Status == PracticeTimeStates.TaskOpen || x.Status == PracticeTimeStates.TaskInProgress)).OrderBy(x => x.DueDate).ThenBy(x => x.CreatedAt).FirstOrDefaultAsync(ct);
    async Task<string?> NameAsync(Guid? userId) => userId is null ? null : await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == userId)
      .Select(x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName).SingleOrDefaultAsync(ct);
    var engagement = pbc is null ? null : await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == pbc.EngagementId).Select(x => x.ServiceRoute).SingleOrDefaultAsync(ct);
    return CommandResult<AccountingPeriodRecord>.Ok(new(period.Id, period.ClientId, client ?? "Scoped client", engagement ?? "No linked engagement", period.PeriodCode,
      period.StartDate, period.EndDate, period.Basis, book ?? "No book", period.Currency, period.Status, period.Revision, period.PriorPeriodId,
      package?.Id, package is null ? null : $"{package.Status} · v{package.Revision} · {package.Currency}", mapping?.Id, mapping is null ? null : $"{mapping.Status} · v{mapping.Version}",
      pbc is null ? null : new PeriodPbcHandoff(pbc.EngagementId, await NameAsync(pbc.FirmOwnerUserId) ?? "Assigned firm owner",
        string.IsNullOrWhiteSpace(pbc.DueDate) ? "Not recorded" : pbc.DueDate, pbc.State),
      task is null ? null : new PeriodTaskHandoff(task.Title, await NameAsync(task.AssigneeUserId) ?? "Assigned staff", task.DueDate?.ToString("yyyy-MM-dd") ?? "Not recorded", task.Status)));
  }
}
