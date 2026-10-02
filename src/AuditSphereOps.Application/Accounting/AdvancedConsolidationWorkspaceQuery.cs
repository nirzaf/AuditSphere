using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AdvancedScopeView(Guid Id, string GroupName, int Version, long GroupRevision, string Method, string ReportingCurrency, string Status,
  int ApprovedComponentCount, int ApprovedReviewedJournalCount);
public sealed record AdvancedScheduleView(Guid Id, string Status, Guid CreatedByUserId, Guid? ApprovedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, string InputDigest);
public sealed record AdvancedExecutionView(Guid Id, string Status, Guid CreatedByUserId, Guid? ApprovedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt,
  decimal ComparativeSignedTotal, decimal CurrentSignedTotal, string OutputDigest);
public sealed record AdvancedConsolidationWorkspace(AdvancedScopeView Scope, IReadOnlyList<AdvancedScheduleView> Schedules, IReadOnlyList<AdvancedExecutionView> Executions,
  bool CanPrepare, bool CanReview, string SuggestedSourceManifestJson);

/// <summary>
/// Advanced-method consolidation workflow for one explicitly granted group scope: perimeter facts, submitted/approved
/// schedules and verified executions. The suggested source manifest lists approved components only; the server
/// canonicalizes and validates every submission, and maker/checker approval stays with the existing commands.
/// </summary>
public static class AdvancedConsolidationWorkspaceQuery
{
  private static readonly string[] GroupRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static async Task<ConsolidationScopeVersion?> ScopeAsync(IClientAccountingDbContext db, ActorContext actor, Guid scopeId, CancellationToken ct)
  {
    var methods = AdvancedConsolidationMethods.All.ToArray();
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == scopeId && methods.Contains(x.Method), ct);
    if (scope is null) return null;
    return (await AuthorizationDecision.AuthorizeGroupAsync(db, actor, scope.GroupId, GroupRoles, ct)).Succeeded ? scope : null;
  }

  public static async Task<CommandResult<AdvancedConsolidationWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid scopeId, CancellationToken ct = default)
  {
    if (await ScopeAsync(db, actor, scopeId, ct) is not { } scope)
      return CommandResult<AdvancedConsolidationWorkspace>.Fail(ErrorCodes.ScopeDenied, "The requested consolidation scope is not available in the current firm and group grant.");
    var components = await db.ConsolidationComponents.AsNoTracking().CountAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved, ct);
    var journalType = RequiredJournalType(scope.Method);
    var journals = journalType is null ? 0 : await db.ConsolidationJournals.AsNoTracking().CountAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved && x.JournalType == journalType, ct);
    var groupName = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId).Select(x => x.Name).SingleAsync(ct);
    var schedules = await db.AdvancedConsolidationMethodSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id)
      .OrderByDescending(x => x.CreatedAt).Select(x => new AdvancedScheduleView(x.Id, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt, x.InputSnapshotDigest))
      .ToListAsync(ct);
    var scheduleIds = schedules.Select(x => x.Id).ToArray();
    var executions = await db.AdvancedConsolidationExecutions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && scheduleIds.Contains(x.ScheduleId))
      .OrderByDescending(x => x.CreatedAt).Select(x => new AdvancedExecutionView(x.Id, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt,
        x.ComparativeSignedTotal, x.CurrentSignedTotal, x.OutputDigest)).ToListAsync(ct);
    async Task<bool> HasRoleAsync(params string[] roles) => await db.GroupAccessGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.UserId == actor.UserId && x.RevokedAt == null && roles.Contains(x.Role), ct);
    return CommandResult<AdvancedConsolidationWorkspace>.Ok(new(
      new AdvancedScopeView(scope.Id, groupName, scope.Version, scope.GroupRevision, scope.Method, scope.ReportingCurrency, scope.Status, components, journals),
      schedules, executions, await HasRoleAsync(GroupRoles), await HasRoleAsync("AccountingReviewer", "Manager", "Partner", "Administrator"),
      await BuildSourceManifestAsync(db, scope, ct)));
  }

  public static async Task<CommandResult<Guid>> RunExecutionAsync(IClientAccountingDbContext db, ActorContext actor, Guid scopeId, CancellationToken ct = default) =>
    await ScopeAsync(db, actor, scopeId, ct) is { } scope
      ? await ConsolidationService.RunAdvancedProfileAsync(db, actor, scope, ct)
      : CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

  private static string? RequiredJournalType(string method) => method switch
  {
    AdvancedConsolidationMethods.AcquisitionNci => "ACQUISITION_NCI",
    AdvancedConsolidationMethods.OwnershipChange => "OWNERSHIP_CHANGE",
    AdvancedConsolidationMethods.AssetTransferElimination => "ASSET_TRANSFER_ELIMINATION",
    _ => null
  };

  private static async Task<string> BuildSourceManifestAsync(IClientAccountingDbContext db, ConsolidationScopeVersion scope, CancellationToken ct)
  {
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).OrderBy(x => x.Id).ToListAsync(ct);
    var manifest = new Dictionary<string, object?>
    {
      ["sources"] = components.Select(x => new { componentId = x.Id, kind = x.SourceType, id = x.PackageId ?? x.ExternalComponentPackId, hash = x.PackageHash }).ToArray()
    };
    if (RequiredJournalType(scope.Method) is { } journalType)
      manifest["reviewedJournals"] = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == scope.FirmId && x.GroupId == scope.GroupId &&
        x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved && x.JournalType == journalType).OrderBy(x => x.Id).Select(x => new { id = x.Id }).ToArrayAsync(ct);
    return JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
  }
}
