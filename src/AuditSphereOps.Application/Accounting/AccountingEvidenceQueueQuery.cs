using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AccountingEvidenceRow(string Kind, string Area, string ClientName, string EngagementName, string PeriodCode, string Status,
  long InputGeneration, long CurrentGeneration, string LinkStatus, Guid? WorkpaperId, string Reference, bool IsStale, bool IsTerminal, Guid? EvidenceId = null);

/// <summary>
/// Grant-scoped accounting evidence queue (ECL, inventory, specialist, analytical, journal-risk) with the audit-procedure
/// link state. A free-text reference alone is never approval evidence; only a reviewed procedure result counts.
/// </summary>
public static class AccountingEvidenceQueueQuery
{
  private static readonly string[] QueueRoles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<IReadOnlyList<AccountingEvidenceRow>>> GetAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: QueueRoles, InternalOnly: true), ct);
    if (!authorization.Succeeded) return CommandResult<IReadOnlyList<AccountingEvidenceRow>>.Fail(ErrorCodes.ScopeDenied, "This queue requires an authorized internal accounting role and an explicit client scope.");
    var f = actor.FirmId;
    var observedAt = DateTimeOffset.UtcNow;
    var grants = db.RoleGrants.AsNoTracking().Where(x => x.FirmId == f && x.UserId == actor.UserId && x.RevokedAt == null &&
      (x.ExpiresAt == null || x.ExpiresAt > observedAt) && QueueRoles.Contains(x.Role));
    var unrestricted = await grants.AnyAsync(x => x.ClientId == null && x.EngagementId == null, ct);
    var directClientIds = await grants.Where(x => x.ClientId.HasValue && x.EngagementId == null).Select(x => x.ClientId!.Value).Distinct().ToArrayAsync(ct);
    var engagementGrants = await grants.Where(x => x.EngagementId.HasValue).Select(x => new { x.ClientId, x.EngagementId }).ToListAsync(ct);
    var engagementIds = engagementGrants.Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var engagementClients = engagementIds.Length == 0 ? new Dictionary<Guid, Guid>() : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == f && engagementIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PracticeClientId, ct);
    var grantedEngagementIds = engagementGrants.Where(x => x.ClientId.HasValue && engagementClients.TryGetValue(x.EngagementId!.Value, out var c) && c == x.ClientId.Value)
      .Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var clientIds = directClientIds.Concat(grantedEngagementIds.Select(x => engagementClients[x])).Distinct().ToArray();
    var scoped = unrestricted ? [] : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == f && (directClientIds.Contains(x.PracticeClientId) || grantedEngagementIds.Contains(x.Id))).Select(x => x.Id).ToArrayAsync(ct);
    if (!unrestricted && clientIds.Length == 0)
    {
      var final = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(f, RequiredRoles: QueueRoles, InternalOnly: true), ct);
      return final.Succeeded ? CommandResult<IReadOnlyList<AccountingEvidenceRow>>.Ok([]) :
        CommandResult<IReadOnlyList<AccountingEvidenceRow>>.Fail(final.ErrorCode!, final.Message!);
    }
    var inScope = unrestricted ? null : clientIds;
    var clients = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == f && (inScope == null || inScope.Contains(x.Id))).ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.PracticeClientId) || scoped.Contains(x.Id)))
      .ToDictionaryAsync(x => x.Id, x => x.ServiceRoute, ct);
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == f && (inScope == null || inScope.Contains(x.ClientId))).ToDictionaryAsync(x => x.Id, x => x.PeriodCode, ct);
    var generations = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == f && (inScope == null || inScope.Contains(x.Id))).ToDictionaryAsync(x => x.Id, x => x.InputGeneration, ct);
    var reconciliations = await db.AccountingReconciliations.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId)))
      .ToDictionaryAsync(x => x.Id, ct);
    var batches = await db.SourceImportBatches.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId)))
      .ToDictionaryAsync(x => x.Id, ct);
    var links = await db.AccountingEvidenceAuditLinks.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId))).ToListAsync(ct);
    var results = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId)))
      .Select(x => new { x.Id, x.Status, x.WorkpaperId }).ToDictionaryAsync(x => x.Id, ct);
    var linkStatus = links.GroupBy(x => Key(x.EvidenceKind, x.EvidenceId)).ToDictionary(x => x.Key,
      x => x.Select(link => results.GetValueOrDefault(link.AuditProcedureResultId)).Where(r => r is not null).OrderByDescending(r => r!.Status == "REVIEWED")
        .Select(r => new LinkTarget(r!.Status == "REVIEWED" ? "REVIEWED_RESULT" : "LINKED_PENDING_REVIEW", r.WorkpaperId)).FirstOrDefault() ?? new LinkTarget("LINKED_PENDING_REVIEW", null));
    var rows = new List<AccountingEvidenceRow>();
    var returnedScopes = new HashSet<(Guid ClientId, Guid EngagementId)>();
    AccountingEvidenceRow Row(string kind, string area, Guid evidenceId, Guid clientId, Guid engagementId, string periodCode, string status, long input, long current, string reference)
    {
      returnedScopes.Add((clientId, engagementId));
      var has = linkStatus.TryGetValue(Key(kind, evidenceId), out var link);
      var terminal = kind == "JOURNAL_RISK" ? status is "CLEARED" or "NOT_AN_ISSUE" : status is "APPROVED" or "REJECTED";
      return new(kind, area, clients.GetValueOrDefault(clientId, "—"), engagements.GetValueOrDefault(engagementId, "Scoped engagement"), periodCode, status, input, current,
        has ? link!.Status : "LINK_PENDING", has ? link!.WorkpaperId : null, reference, input != current, terminal, evidenceId);
    }
    foreach (var r in reconciliations.Values)
      rows.Add(Row("RECONCILIATION", r.Area, r.Id, r.ClientId, r.EngagementId, periods.GetValueOrDefault(r.PeriodId, "—"), r.Status,
        r.InputGeneration, generations.GetValueOrDefault(r.ClientId, 0), r.AccountSelection) with { LinkStatus = "SOURCE_REVIEW_REQUIRED" });
    foreach (var x in await db.EclAssessments.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId))).ToListAsync(ct))
    {
      var r = reconciliations[x.ReconciliationId];
      rows.Add(Row("ECL", r.Area, x.Id, x.ClientId, x.EngagementId, periods.GetValueOrDefault(r.PeriodId, "—"), x.Status, x.InputGeneration, generations.GetValueOrDefault(x.ClientId, 0), x.MethodologyVersion));
    }
    foreach (var x in await db.InventoryValuationAssessments.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId))).ToListAsync(ct))
    {
      var r = reconciliations[x.ReconciliationId];
      rows.Add(Row("INVENTORY", r.Area, x.Id, x.ClientId, x.EngagementId, periods.GetValueOrDefault(r.PeriodId, "—"), x.Status, x.InputGeneration, generations.GetValueOrDefault(x.ClientId, 0), x.MethodologyVersion));
    }
    rows.AddRange((await db.SpecialistAccountingSchedules.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId))).ToListAsync(ct))
      .Select(x => Row("SPECIALIST", x.Area, x.Id, x.ClientId, x.EngagementId, periods.GetValueOrDefault(x.PeriodId, "—"), x.Status, x.InputGeneration, generations.GetValueOrDefault(x.ClientId, 0), x.EvidenceReference)));
    rows.AddRange((await db.AnalyticalReviews.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId))).ToListAsync(ct))
      .Select(x => Row("ANALYTICAL", x.Area, x.Id, x.ClientId, x.EngagementId, periods.GetValueOrDefault(x.PeriodId, "—"), x.Status, x.InputGeneration, generations.GetValueOrDefault(x.ClientId, 0), x.Explanation)));
    rows.AddRange((await db.JournalRiskFlags.AsNoTracking().Where(x => x.FirmId == f && (unrestricted || directClientIds.Contains(x.ClientId) || scoped.Contains(x.EngagementId))).ToListAsync(ct))
      .Select(x =>
      {
        var batch = batches.GetValueOrDefault(x.ImportBatchId);
        var generation = generations.GetValueOrDefault(x.ClientId, 0);
        return Row("JOURNAL_RISK", x.RuleCode, x.Id, x.ClientId, x.EngagementId, batch is null ? "—" : periods.GetValueOrDefault(batch.PeriodId, "—"), x.Status, generation, generation, x.EvidenceReference);
      }));
    // Counts and linked evidence must not survive expiration or revocation during the projection.
    // The stored parent check in AuthorizationDecision also rejects malformed client/engagement links.
    foreach (var (clientId, engagementId) in returnedScopes.OrderBy(x => x.ClientId).ThenBy(x => x.EngagementId))
    {
      var final = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(f, clientId, engagementId, QueueRoles, InternalOnly: true), ct);
      if (!final.Succeeded) return CommandResult<IReadOnlyList<AccountingEvidenceRow>>.Fail(final.ErrorCode!, final.Message!);
    }
    var currentAuthority = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(f, RequiredRoles: QueueRoles, InternalOnly: true), ct);
    return currentAuthority.Succeeded ? CommandResult<IReadOnlyList<AccountingEvidenceRow>>.Ok(
      rows.OrderBy(x => x.ClientName).ThenBy(x => x.PeriodCode).ThenBy(x => x.Kind).ToList()) :
      CommandResult<IReadOnlyList<AccountingEvidenceRow>>.Fail(currentAuthority.ErrorCode!, currentAuthority.Message!);
  }

  private sealed record LinkTarget(string Status, Guid? WorkpaperId);
  private static string Key(string kind, Guid id) => $"{kind}:{id:D}";
}
