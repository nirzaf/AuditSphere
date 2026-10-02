using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MappingRecordRow(Guid Id, string ClientName, string EngagementName, string PeriodStart, string PeriodEnd, long Version, long Generation,
  bool HasChart, string Status);
public sealed record JournalRecordRow(Guid Id, string JournalNumber, string ClientName, string EngagementName, string Purpose, string PeriodCode, string? Currency,
  string Status, long Revision);
public sealed record DifferenceRecordRow(Guid Id, string AccountArea, string DifferenceType, string ClientName, string EngagementName, decimal Amount, string Currency,
  string Status, string? CorrectionState, Guid? ProposedJournalId);
public sealed record JournalLineView(string AccountCode, decimal Debit, decimal Credit);
public sealed record JournalDetailView(Guid Id, string JournalNumber, string Status, long Revision, Guid BaseDatasetId, DateTimeOffset CreatedAt, Guid CreatedByUserId,
  IReadOnlyList<JournalLineView> Lines, decimal TotalDebit, decimal TotalCredit, string? ManagementDecision, string? ManagementEvidenceMode, long? ManagementJournalRevision,
  bool CanPost, bool CanExport);

/// <summary>
/// Grant-scoped accounting record queues (mapping versions, adjustment journals, audit differences) and the journal
/// detail. Firm-wide accounting grants see the firm; client and exact-engagement grants never widen to siblings.
/// </summary>
public static class AccountingRecordsQuery
{
  private static readonly string[] AccountingRoles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] JournalRoles = ["Administrator", "Partner", "Manager", "Staff", "AccountingPreparer", "AccountingReviewer"];
  private sealed record Scope(bool FirmWide, Guid[] Clients, Guid[] Engagements);

  private static async Task<Scope?> ScopeAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: AccountingRoles, InternalOnly: true), ct);
    if (!authorization.Succeeded) return null;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null && AccountingRoles.Contains(x.Role))
      .Select(x => new { x.ClientId, x.EngagementId }).ToListAsync(ct);
    var firmWide = grants.Any(x => x.ClientId is null && x.EngagementId is null);
    var directClients = grants.Where(x => x.ClientId.HasValue && x.EngagementId is null).Select(x => x.ClientId!.Value).Distinct().ToArray();
    var engagementIds = grants.Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var engagementClients = engagementIds.Length == 0 ? new Dictionary<Guid, Guid>() : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && engagementIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PracticeClientId, ct);
    var granted = grants.Where(x => x.ClientId.HasValue && x.EngagementId.HasValue && engagementClients.TryGetValue(x.EngagementId.Value, out var c) && c == x.ClientId.Value)
      .Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var scoped = firmWide ? [] : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && (directClients.Contains(x.PracticeClientId) || granted.Contains(x.Id))).Select(x => x.Id).ToArrayAsync(ct);
    return new(firmWide, directClients, scoped);
  }

  private static async Task<(Dictionary<Guid, string> Clients, Dictionary<Guid, string> Engagements)> NamesAsync(IAuditSphereDbContext db, Guid firmId,
    IEnumerable<Guid> clientIds, IEnumerable<Guid> engagementIds, CancellationToken ct)
  {
    var c = clientIds.Distinct().ToArray(); var e = engagementIds.Distinct().ToArray();
    return (await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == firmId && c.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.LegalName, ct),
      await db.Engagements.AsNoTracking().Where(x => x.FirmId == firmId && e.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.ServiceRoute, ct));
  }

  public static async Task<CommandResult<IReadOnlyList<MappingRecordRow>>> MappingsAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (await ScopeAsync(db, actor, ct) is not { } s) return CommandResult<IReadOnlyList<MappingRecordRow>>.Fail(ErrorCodes.ScopeDenied, "This queue requires an authorized internal accounting role.");
    var rows = await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (s.FirmWide || s.Clients.Contains(x.ClientId) || s.Engagements.Contains(x.EngagementId)))
      .OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
    var (clients, engagements) = await NamesAsync(db, actor.FirmId, rows.Select(x => x.ClientId), rows.Select(x => x.EngagementId), ct);
    return CommandResult<IReadOnlyList<MappingRecordRow>>.Ok(rows.Select(x => new MappingRecordRow(x.Id, clients.GetValueOrDefault(x.ClientId, "Scoped client"),
      engagements.GetValueOrDefault(x.EngagementId, "Scoped engagement"), x.PeriodStart, x.PeriodEnd, x.Version, x.Generation, x.ClientChartVersionId is not null, x.Status)).ToList());
  }

  public static async Task<CommandResult<IReadOnlyList<JournalRecordRow>>> JournalsAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (await ScopeAsync(db, actor, ct) is not { } s) return CommandResult<IReadOnlyList<JournalRecordRow>>.Fail(ErrorCodes.ScopeDenied, "This queue requires an authorized internal accounting role.");
    var rows = await db.AdjustmentJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (s.FirmWide || s.Clients.Contains(x.ClientId) || s.Engagements.Contains(x.EngagementId)))
      .OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
    var (clients, engagements) = await NamesAsync(db, actor.FirmId, rows.Select(x => x.ClientId), rows.Select(x => x.EngagementId), ct);
    var periodIds = rows.Where(x => x.PeriodId.HasValue).Select(x => x.PeriodId!.Value).Distinct().ToArray();
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => periodIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.PeriodCode, ct);
    return CommandResult<IReadOnlyList<JournalRecordRow>>.Ok(rows.Select(x => new JournalRecordRow(x.Id, x.JournalNumber, clients.GetValueOrDefault(x.ClientId, "Scoped client"),
      engagements.GetValueOrDefault(x.EngagementId, "Scoped engagement"), x.Purpose, x.PeriodId is { } p ? periods.GetValueOrDefault(p, "Unknown period") : "No period",
      x.Currency, x.Status, x.Revision)).ToList());
  }

  public static async Task<CommandResult<IReadOnlyList<DifferenceRecordRow>>> DifferencesAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (await ScopeAsync(db, actor, ct) is not { } s) return CommandResult<IReadOnlyList<DifferenceRecordRow>>.Fail(ErrorCodes.ScopeDenied, "This queue requires an authorized internal accounting role.");
    var rows = await db.AuditDifferences.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (s.FirmWide || s.Clients.Contains(x.ClientId) || s.Engagements.Contains(x.EngagementId)))
      .OrderByDescending(x => x.CreatedAt).Take(1000).ToListAsync(ct);
    var (clients, engagements) = await NamesAsync(db, actor.FirmId, rows.Select(x => x.ClientId), rows.Select(x => x.EngagementId), ct);
    return CommandResult<IReadOnlyList<DifferenceRecordRow>>.Ok(rows.Select(x => new DifferenceRecordRow(x.Id, x.AccountArea, x.DifferenceType,
      clients.GetValueOrDefault(x.ClientId, "Scoped client"), engagements.GetValueOrDefault(x.EngagementId, "Scoped engagement"), x.Amount, x.Currency, x.Status,
      x.CorrectionState, x.ProposedJournalId)).ToList());
  }

  public static async Task<CommandResult<JournalDetailView>> JournalAsync(IAdjustmentJournalDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default)
  {
    var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null) return CommandResult<JournalDetailView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(journal.FirmId, journal.ClientId, journal.EngagementId, JournalRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<JournalDetailView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var lines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == journal.Id).OrderBy(x => x.AccountCode)
      .Select(x => new JournalLineView(x.AccountCode, x.Debit, x.Credit)).ToListAsync(ct);
    var decision = await db.AdjustmentJournalManagementDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == journal.FirmId && x.ClientId == journal.ClientId &&
      x.EngagementId == journal.EngagementId && x.JournalId == journal.Id && x.JournalRevision == journal.Revision, ct);
    return CommandResult<JournalDetailView>.Ok(new(journal.Id, journal.JournalNumber, journal.Status, journal.Revision, journal.BaseDatasetId, journal.CreatedAt,
      journal.CreatedByUserId, lines, lines.Sum(x => x.Debit), lines.Sum(x => x.Credit), decision?.Decision, decision?.EvidenceMode, decision?.JournalRevision,
      actor.UserId != journal.CreatedByUserId && actor.Roles.Any(x => x is "AccountingReviewer" or "Partner" or "Manager"),
      decision?.Decision is ManagementDecisionStates.Accepted or ManagementDecisionStates.Partial &&
        actor.Roles.Any(x => x is "Administrator" or "Partner" or "Manager" or "AccountingPreparer" or "AccountingReviewer")));
  }
}
