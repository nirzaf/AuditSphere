using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record IntakeDataset(Guid Id, string PeriodCode, string Currency, DateTimeOffset ImportedAt);
public sealed record TrialBalanceIntakeWorkspace(Guid EngagementId, Guid ClientId, IReadOnlyList<IntakeDataset> Datasets);

/// <summary>Engagement-scoped intake projection (client identity and recent datasets) and the draft-mapping helper.</summary>
public static class TrialBalanceIntakeWorkspaceQuery
{
  private static readonly string[] IntakeRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator", "Senior", "Staff"];

  public static async Task<CommandResult<TrialBalanceIntakeWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().Where(x => x.Id == engagementId && x.FirmId == actor.FirmId)
      .Select(x => new { x.Id, x.PracticeClientId }).SingleOrDefaultAsync(ct);
    if (engagement is null) return CommandResult<TrialBalanceIntakeWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
      IntakeRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<TrialBalanceIntakeWorkspace>.Fail(auth.ErrorCode!, auth.Message!);
    var datasets = await db.TrialBalanceDatasets.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .GroupJoin(db.ClientReportingPeriods.AsNoTracking(), d => d.PeriodId, p => p.Id, (d, p) => new { d, p })
      .SelectMany(x => x.p.DefaultIfEmpty(), (x, p) => new { x.d.Id, x.d.Currency, x.d.ImportedAt, Period = p == null ? "no period" : p.PeriodCode })
      .OrderByDescending(x => x.ImportedAt).Take(50).ToListAsync(ct);
    return CommandResult<TrialBalanceIntakeWorkspace>.Ok(new(engagement.Id, engagement.PracticeClientId,
      datasets.Select(x => new IntakeDataset(x.Id, x.Period, x.Currency, x.ImportedAt)).ToList()));
  }

  /// <summary>Creates the draft mapping for a dataset over its reporting period; approval remains a separate reviewer action.</summary>
  public static async Task<CommandResult<Guid>> CreateDraftAsync(IClientAccountingDbContext db, ActorContext actor, Guid datasetId, string taxonomyVersion,
    IReadOnlyList<MappingAllocationInput> overrides, CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId && x.FirmId == actor.FirmId, ct);
    if (dataset is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var period = dataset.PeriodId is null ? null : await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == dataset.PeriodId, ct);
    return await MappingMemoryService.CreateDraftAsync(db, actor, datasetId, taxonomyVersion, period?.StartDate.ToString("yyyy-MM-dd") ?? "",
      period?.EndDate.ToString("yyyy-MM-dd") ?? "", overrides, ct);
  }
}
