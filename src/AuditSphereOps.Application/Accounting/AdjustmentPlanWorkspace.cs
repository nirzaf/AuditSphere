using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AdjustmentPlanListItem(Guid Id, Guid DatasetId, string ClientName,
  string EngagementName, string Status, DateTimeOffset CreatedAt);
public sealed record AdjustmentPlanPage(IReadOnlyList<AdjustmentPlanListItem> Items, int Page, bool HasMore);
public sealed record AdjustmentPlanReview(Guid Id, Guid ClientId, Guid EngagementId, Guid DatasetId,
  long SourceRevision, string SourceDigest, Guid? PeriodId, string PeriodCode, string PeriodStart,
  string PeriodEnd, Guid? BookId, string BookCode, string Basis, string Currency, string Status,
  Guid CreatedByUserId, DateTimeOffset CreatedAt, string? ResultHash, decimal RetainedDebits,
  decimal RetainedCredits, int RetainedAppliedCount, string MembershipDigest, string ReviewBasis,
  int EligibleCount, int ExcludedCount, int BlockedCount, IReadOnlyList<string> Blockers,
  IReadOnlyList<AdjustmentEligibilityRow> Journals, int Page, bool HasMore);

/// <summary>Bounded, current-authority review of persisted plan membership. Retained
/// calculation evidence is distinct from current applicability; this query never applies a plan.</summary>
public static partial class AdjustmentPlanWorkspace
{
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static Task<CommandResult> Auth(IClientAccountingDbContext db, ActorContext actor,
    Guid? clientId = null, Guid? engagementId = null, CancellationToken ct = default) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, engagementId,
      AdjustmentEligibilityQuery.ReadRoles, InternalOnly: true, RequireProfessionalWork: engagementId.HasValue), ct);

  private static IQueryable<AdjustmentPlan> ScopedPlans(IClientAccountingDbContext db, ActorContext actor)
  {
    var now = DateTimeOffset.UtcNow;
    return db.AdjustmentPlans.AsNoTracking().Where(p => p.FirmId == actor.FirmId &&
      db.TrialBalanceDatasets.Any(d => d.Id == p.BaseDatasetId && d.FirmId == p.FirmId &&
        d.ClientId == p.ClientId && d.EngagementId == p.EngagementId) &&
      db.Engagements.Any(e => e.FirmId == actor.FirmId && e.Id == p.EngagementId &&
        e.PracticeClientId == p.ClientId && !e.ProfessionalWorkBlocked) &&
      !db.EngagementHolds.Any(h => h.FirmId == p.FirmId && h.EngagementId == p.EngagementId && !h.Released) &&
      db.RoleGrants.Any(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId && g.RevokedAt == null &&
        (g.ExpiresAt == null || g.ExpiresAt > now) && AdjustmentEligibilityQuery.ReadRoles.Contains(g.Role) &&
        ((g.ClientId == null && g.EngagementId == null) ||
         (g.ClientId == p.ClientId && (g.EngagementId == null || g.EngagementId == p.EngagementId)))));
  }

  public static async Task<CommandResult<AdjustmentPlanPage>> ListAsync(IClientAccountingDbContext db,
    ActorContext actor, int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000) return Fail<AdjustmentPlanPage>(ErrorCodes.GateBlocked, "Use a valid bounded page.");
    var auth = await Auth(db, actor, ct: ct); if (!auth.Succeeded) return Fail<AdjustmentPlanPage>(auth.ErrorCode!, auth.Message!);
    var rows = await ScopedPlans(db, actor).OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id)
      .Skip(page * 25).Take(26).ToListAsync(ct);
    var ids = rows.Select(p => p.Id).ToArray();
    var names = await (from p in ScopedPlans(db, actor) where ids.Contains(p.Id)
      join c in db.PracticeClients on p.ClientId equals c.Id
      join e in db.Engagements on p.EngagementId equals e.Id
      where c.FirmId == actor.FirmId && e.FirmId == actor.FirmId && e.PracticeClientId == p.ClientId
      select new AdjustmentPlanListItem(p.Id, p.BaseDatasetId, c.LegalName, e.ServiceRoute, p.Status, p.CreatedAt)).ToListAsync(ct);
    foreach (var row in rows)
      if (!(await Auth(db, actor, row.ClientId, row.EngagementId, ct)).Succeeded)
        return Fail<AdjustmentPlanPage>(ErrorCodes.ScopeDenied, "Refresh the current scoped queue.");
    if (names.Count != rows.Count) return Fail<AdjustmentPlanPage>(ErrorCodes.StaleRevision, "The plan queue changed. Refresh it.");
    auth = await Auth(db, actor, ct: ct);
    if (!auth.Succeeded) return Fail<AdjustmentPlanPage>(auth.ErrorCode!, auth.Message!);
    return CommandResult<AdjustmentPlanPage>.Ok(new(rows.Take(25).Select(p => names.Single(n => n.Id == p.Id)).ToArray(), page, rows.Count > 25));
  }

  private static async Task<CommandResult<AdjustmentPlanReview>> ReadAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid id, int page, CancellationToken ct)
  {
    var plan = await db.AdjustmentPlans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.FirmId == actor.FirmId, ct);
    if (plan is null) return Fail<AdjustmentPlanReview>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await Auth(db, actor, plan.ClientId, plan.EngagementId, ct);
    if (!auth.Succeeded) return Fail<AdjustmentPlanReview>(auth.ErrorCode!, auth.Message!);
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(d => d.Id == plan.BaseDatasetId &&
      d.FirmId == actor.FirmId && d.ClientId == plan.ClientId && d.EngagementId == plan.EngagementId, ct);
    if (source is null) return Fail<AdjustmentPlanReview>(ErrorCodes.ScopeDenied, "Access denied.");
    var report = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, actor, id, ct);
    if (!report.Succeeded) return Fail<AdjustmentPlanReview>(report.ErrorCode!, report.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(p => p.Id == source.PeriodId && p.FirmId == actor.FirmId && p.ClientId == source.ClientId, ct);
    var book = await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(b => b.Id == source.BookId && b.FirmId == actor.FirmId && b.ClientId == source.ClientId && b.PeriodId == source.PeriodId, ct);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(f => f.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == plan.ClientId && c.FirmId == actor.FirmId, ct);
    var frozen = await db.EngagementFileFreezes.AnyAsync(f => f.EngagementId == plan.EngagementId && f.FirmId == actor.FirmId && f.State == "FROZEN", ct);
    var blockers = new List<string>();
    if (source.SourceKind != "Raw" || source.ImportState != TrialBalanceImportStates.Sealed || source.ValidationStatus != "Accepted" || !source.Balanced || source.ControlTotal != 0m || !SourceAcceptanceWorkspace.ValidHash(MappedTrialBalanceSource.Digest(source)))
      blockers.Add("The exact raw source must be sealed, balanced and validated.");
    if (period is null || period.Currency != source.Currency || !string.Equals(period.Basis, source.Basis, StringComparison.OrdinalIgnoreCase))
      blockers.Add("The exact source period, basis and currency are unbound or inconsistent.");
    else if (period.Status is not ("ACTIVE" or "DRAFT")) blockers.Add("The reporting period is closed; retained evidence does not authorize a new application.");
    if (source.BookId is not null && (book is null || book.Currency != source.Currency || !string.Equals(book.Basis, source.Basis, StringComparison.OrdinalIgnoreCase) || book.Status is not ("ACTIVE" or "DRAFT")))
      blockers.Add("The reporting book is closed or inconsistent with the source.");
    if (firm is null || safety is null) blockers.Add("Required safety state is unavailable.");
    if (frozen) blockers.Add("The engagement file is frozen; an approved amendment is required.");
    if (report.Value!.BlockedCount > 0) blockers.Add("Resolve blocked membership and create a replacement plan after a changed reflection decision.");
    if (plan.Status == "Finalized" && !SourceAcceptanceWorkspace.ValidHash(plan.ResultHash)) blockers.Add("The retained finalized result identity is unavailable.");
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, plan, source, report.Value, period, book, firm, safety, frozen, blockers }));
    auth = await Auth(db, actor, plan.ClientId, plan.EngagementId, ct);
    if (!auth.Succeeded) return Fail<AdjustmentPlanReview>(auth.ErrorCode!, auth.Message!);
    var r = report.Value;
    return CommandResult<AdjustmentPlanReview>.Ok(new(id, plan.ClientId, plan.EngagementId, source.Id,
      source.Revision, MappedTrialBalanceSource.Digest(source), source.PeriodId, period?.PeriodCode ?? "",
      period?.StartDate.ToString("yyyy-MM-dd") ?? "", period?.EndDate.ToString("yyyy-MM-dd") ?? "",
      source.BookId, book?.Code ?? "", source.Basis ?? "", source.Currency, plan.Status, plan.CreatedByUserId, plan.CreatedAt,
      plan.ResultHash, plan.AppliedDebits, plan.AppliedCreditsAbs, plan.AppliedJournalCount, r.MembershipDigest, basis,
      r.EligibleCount, r.ExcludedCount, r.BlockedCount, blockers, r.Journals.Skip(page * 25).Take(25).ToArray(), page, r.Journals.Count > (page + 1) * 25));
  }

  public static async Task<CommandResult<AdjustmentPlanReview>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid id, int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or > 40) return Fail<AdjustmentPlanReview>(ErrorCodes.GateBlocked, "Use a valid membership page.");
    var first = await ReadAsync(db, actor, id, page, ct); if (!first.Succeeded) return first;
    var last = await ReadAsync(db, actor, id, page, ct); if (!last.Succeeded) return last;
    return first.Value!.ReviewBasis == last.Value!.ReviewBasis ? last :
      Fail<AdjustmentPlanReview>(ErrorCodes.StaleRevision, "The plan or its supporting context changed. Refresh the current review.");
  }
}
