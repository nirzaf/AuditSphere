using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  public static async Task<CommandResult<ConsolidationReport>> GetLatestReportAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<ConsolidationReport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId,
      ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<ConsolidationReport>.Fail(auth.ErrorCode!, auth.Message!);

    var empty = Array.Empty<ConsolidationReportLine>();
    if (AdvancedConsolidationMethods.All.Contains(scope.Method))
      return CommandResult<ConsolidationReport>.Ok(new(scope.Id, null, "ADVANCED_PROFILE", null, empty));
    var run = await db.ConsolidationRuns.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (run is null)
      return CommandResult<ConsolidationReport>.Ok(new(scope.Id, null, "NO_RUN", null, empty));
    if (scope.Status != AccountingWorkflowStates.Approved || run.Status != AccountingWorkflowStates.Approved)
      return CommandResult<ConsolidationReport>.Ok(new(scope.Id, run.Id, "REVIEW_REQUIRED", run.ApprovedAt, empty));

    var current = await BuildCalculationAsync(db, actor.FirmId, scope, ct);
    if (!current.Succeeded || current.Value!.Calculation.RunHash != run.RunHash ||
        current.Value.Calculation.InputManifest != run.InputManifest)
      return CommandResult<ConsolidationReport>.Ok(new(scope.Id, run.Id, "STALE", run.ApprovedAt, empty));

    var runLines = await db.ConsolidationRunLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.RunId == run.Id).ToListAsync(ct);
    var componentIds = runLines.Where(x => x.ComponentId.HasValue).Select(x => x.ComponentId!.Value).Distinct().ToArray();
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && componentIds.Contains(x.Id))
      .Select(x => new { x.Id, x.ClientId }).ToListAsync(ct);
    if (components.Count != componentIds.Length)
      return CommandResult<ConsolidationReport>.Ok(new(scope.Id, run.Id, "STALE", run.ApprovedAt, empty));
    var clientIds = components.Select(x => x.ClientId).Distinct().ToArray();
    var clientNames = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    if (clientNames.Count != clientIds.Length)
      return CommandResult<ConsolidationReport>.Ok(new(scope.Id, run.Id, "STALE", run.ApprovedAt, empty));
    var componentNames = components.ToDictionary(x => x.Id, x => clientNames[x.ClientId]);
    var lines = runLines.GroupBy(x => new
      {
        Component = x.ComponentId is { } componentId ? componentNames[componentId] : "Group-only adjustment",
        x.TaxonomyCode, x.Currency
      })
      .OrderBy(x => x.Key.Component, StringComparer.Ordinal).ThenBy(x => x.Key.TaxonomyCode, StringComparer.Ordinal)
      .Select(x => new ConsolidationReportLine(x.Key.Component, x.Key.TaxonomyCode,
        MoneyPolicy.Normalize(x.Sum(y => y.ComponentAmount)), MoneyPolicy.Normalize(x.Sum(y => y.AlignmentAmount)),
        MoneyPolicy.Normalize(x.Sum(y => y.EliminationAmount)), MoneyPolicy.Normalize(x.Sum(y => y.ConsolidatedAmount)),
        x.Key.Currency)).ToArray();
    return CommandResult<ConsolidationReport>.Ok(new(scope.Id, run.Id, "CURRENT_APPROVED", run.ApprovedAt, lines));
  }
}
