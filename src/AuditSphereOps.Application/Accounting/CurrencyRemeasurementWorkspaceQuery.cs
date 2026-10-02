using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record RemeasurementContext(Guid PeriodId, Guid ClientId, Guid EngagementId, string ClientName, string PeriodCode, DateOnly EndDate, string EngagementLabel);
public sealed record RemeasurementHistoryRow(Guid Id, string ClientName, string PeriodCode, string Status, DateTimeOffset CreatedAt);
public sealed record RemeasurementWorkspace(IReadOnlyList<RemeasurementContext> Contexts, IReadOnlyList<RemeasurementHistoryRow> History);
public sealed record RemeasurementOption(Guid Id, string Label);
public sealed record RemeasurementGlLine(Guid Id, string Currency, decimal OriginalAmount, decimal FunctionalAmount, string Label);
public sealed record RemeasurementChoices(string FunctionalCurrency, IReadOnlyList<RemeasurementOption> RateSets, IReadOnlyList<RemeasurementOption> Policies,
  IReadOnlyList<RemeasurementGlLine> GlLines);

/// <summary>
/// Scoped inputs for the FX remeasurement workbench: periods per permitted engagement, recent schedules, and for one
/// period the approved rate sets and policies effective at period end plus sealed foreign-currency GL lines. Missing
/// approved inputs leave the lists empty; nothing is defaulted.
/// </summary>
public static class CurrencyRemeasurementWorkspaceQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];

  private sealed record Scope(bool FirmWide, Guid[] Clients, Guid[] PermittedEngagements, List<Domain.Engagements.Engagement> Engagements);

  private static async Task<Scope?> ScopeAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct)
  {
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: Roles, InternalOnly: true), ct)).Succeeded) return null;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null && Roles.Contains(x.Role))
      .Select(x => new { x.ClientId, x.EngagementId }).ToListAsync(ct);
    var firmWide = grants.Any(x => x.ClientId is null && x.EngagementId is null);
    var clients = grants.Where(x => x.ClientId.HasValue && x.EngagementId is null).Select(x => x.ClientId!.Value).Distinct().ToArray();
    var engagementIds = grants.Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).Distinct().ToArray();
    var engagements = await db.Engagements.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      (firmWide || clients.Contains(x.PracticeClientId) || engagementIds.Contains(x.Id))).ToListAsync(ct);
    var permitted = engagements.Where(e => firmWide || grants.Any(g => g.ClientId == e.PracticeClientId && g.EngagementId is null) ||
      grants.Any(g => g.ClientId == e.PracticeClientId && g.EngagementId == e.Id)).ToList();
    return new(firmWide, clients, permitted.Select(x => x.Id).ToArray(), permitted);
  }

  public static async Task<CommandResult<RemeasurementWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (await ScopeAsync(db, actor, ct) is not { } s)
      return CommandResult<RemeasurementWorkspace>.Fail(ErrorCodes.ScopeDenied, "An explicitly scoped internal accounting-preparer or reviewer grant is required.");
    var clientIds = s.Engagements.Select(x => x.PracticeClientId).Distinct().ToArray();
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.ClientId)).ToListAsync(ct);
    var names = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    var contexts = periods.SelectMany(p => s.Engagements.Where(e => e.PracticeClientId == p.ClientId).Select(e => new RemeasurementContext(p.Id, p.ClientId, e.Id,
      names.GetValueOrDefault(p.ClientId, "Scoped client"), p.PeriodCode, p.EndDate, e.ServiceRoute))).OrderByDescending(x => x.EndDate).ThenBy(x => x.ClientName).ToList();
    var history = await db.CurrencyRemeasurementSchedules.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        (s.FirmWide || s.Clients.Contains(x.ClientId) || s.PermittedEngagements.Contains(x.EngagementId)))
      .OrderByDescending(x => x.CreatedAt).Take(50)
      .Join(db.ClientReportingPeriods.AsNoTracking(), x => x.PeriodId, p => p.Id, (x, p) => new { x, p })
      .Join(db.PracticeClients.AsNoTracking(), y => y.x.ClientId, c => c.Id, (y, c) => new RemeasurementHistoryRow(y.x.Id, c.LegalName, y.p.PeriodCode, y.x.Status, y.x.CreatedAt))
      .ToListAsync(ct);
    return CommandResult<RemeasurementWorkspace>.Ok(new(contexts, history));
  }

  public static async Task<CommandResult<RemeasurementChoices>> ChoicesAsync(IClientAccountingDbContext db, ActorContext actor, Guid periodId, Guid engagementId, CancellationToken ct = default)
  {
    if (await ScopeAsync(db, actor, ct) is not { } s || s.Engagements.FirstOrDefault(x => x.Id == engagementId) is not { } engagement)
      return CommandResult<RemeasurementChoices>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == periodId && x.ClientId == engagement.PracticeClientId, ct);
    if (period is null) return CommandResult<RemeasurementChoices>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId, ct);
    if (profile is null) return CommandResult<RemeasurementChoices>.Fail(ErrorCodes.GateBlocked, "Configure the client's accounting profile and functional currency first.");
    var end = period.EndDate;
    var rateSets = await db.ExchangeRateSetVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved &&
      (x.EffectiveFrom == null || x.EffectiveFrom <= end) && (x.EffectiveTo == null || x.EffectiveTo >= end)).OrderByDescending(x => x.ApprovedAt)
      .Select(x => new RemeasurementOption(x.Id, x.Code + " v" + x.Version + " · " + x.Source)).ToListAsync(ct);
    var policies = await db.TranslationPolicyVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Status == AccountingWorkflowStates.Approved &&
      x.FunctionalCurrency == profile.FunctionalCurrency).OrderByDescending(x => x.ApprovedAt)
      .Select(x => new RemeasurementOption(x.Id, x.Code + " · " + x.FunctionalCurrency + " · closing " + x.ClosingRateRule + " / historical " + x.HistoricalRateRule)).ToListAsync(ct);
    var lines = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join batch in db.SourceImportBatches.AsNoTracking() on new { line.FirmId, line.ClientId, line.EngagementId, line.ImportBatchId }
        equals new { batch.FirmId, batch.ClientId, batch.EngagementId, ImportBatchId = batch.Id }
      join transaction in db.GeneralLedgerTransactions.AsNoTracking() on new { line.FirmId, line.ClientId, line.EngagementId, line.TransactionId }
        equals new { transaction.FirmId, transaction.ClientId, transaction.EngagementId, TransactionId = transaction.Id }
      where line.FirmId == actor.FirmId && line.ClientId == period.ClientId && line.EngagementId == engagementId && batch.PeriodId == periodId &&
        batch.SourceKind == "GL" && batch.Status == "SEALED" && batch.NormalizedDatasetDigest.Length == 64 && line.OriginalCurrency != profile.FunctionalCurrency
      orderby transaction.PostingDate descending, transaction.StableJournalId, line.StableLineId
      select new { line.Id, line.OriginalCurrency, line.OriginalAmount, line.FunctionalAmount, transaction.PostingDate, transaction.StableJournalId, line.StableLineId, line.AccountCode })
      .Take(500).ToListAsync(ct);
    return CommandResult<RemeasurementChoices>.Ok(new(profile.FunctionalCurrency, rateSets, policies, lines.Select(x => new RemeasurementGlLine(x.Id, x.OriginalCurrency,
      x.OriginalAmount, x.FunctionalAmount, $"{x.PostingDate:yyyy-MM-dd} · {x.StableJournalId} / {x.StableLineId} · {x.AccountCode}")).ToList()));
  }
}
