using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

// ── Technical library (OV-05) ───────────────────────────────────────────────────────────────────────

public sealed record LibraryHit(Guid DocumentId, string Code, string Title, string Category, int Version, DateOnly EffectiveFrom, string Snippet);
public sealed record LibraryEntry(TechnicalLibraryDocument Document, TechnicalLibraryVersion Version, IReadOnlyList<TechnicalLibraryVersion> History);

/// <summary>
/// Permission-aware, versioned technical library (IFRS, ISA and firm guidance). Entries are prepared as drafts and
/// published by a second approver; publishing supersedes the previous version, which stays readable in history.
/// Search covers published versions the actor's audience allows and always names the version it matched.
/// </summary>
public static class TechnicalLibraryService
{
  private static readonly string[] StaffRoles = ["Staff", "Senior", "Manager", "Partner", "Administrator", "Reviewer", "Auditor"];
  private static readonly string[] SeniorAudience = ["Manager", "Partner", "Administrator"];
  private static readonly string[] Curators = ["Manager", "Partner", "Administrator"];
  private static readonly string[] Publishers = ["Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateAsync(IAuditSphereDbContext db, ActorContext actor, string code, string title, string category, string audience,
    string body, string sourceReference, DateOnly effectiveFrom, CancellationToken ct = default)
  {
    category = (category ?? "").Trim().ToUpperInvariant();
    audience = (audience ?? "").Trim().ToUpperInvariant();
    if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 40 || string.IsNullOrWhiteSpace(title) || !TechnicalLibraryCategories.All.Contains(category) ||
        audience is not (TechnicalLibraryAudiences.AllStaff or TechnicalLibraryAudiences.PartnersAndManagers))
      return CommandResult<Guid>.Fail("library.invalid", "A code, title, category (IFRS, ISA or firm guidance) and audience are required.");
    var auth = await AuthorizeAsync(db, actor, Curators, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.TechnicalLibraryDocuments.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == code.Trim().ToUpperInvariant(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.TechnicalLibrary.DuplicateCode, "That library code already exists. Choose another code or open the entry to prepare a new version.");
    var document = new TechnicalLibraryDocument { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = code.Trim().ToUpperInvariant(), Title = title.Trim(),
      Category = category, Audience = audience, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.TechnicalLibraryDocuments.Add(document);
    await db.SaveChangesAsync(ct);
    var draft = await DraftVersionAsync(db, actor, document.Id, body, sourceReference, effectiveFrom, ct);
    return draft.Succeeded ? CommandResult<Guid>.Ok(document.Id) : draft;
  }

  public static async Task<CommandResult<Guid>> DraftVersionAsync(IAuditSphereDbContext db, ActorContext actor, Guid documentId, string body, string sourceReference,
    DateOnly effectiveFrom, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(body) || body.Length > 200_000 || string.IsNullOrWhiteSpace(sourceReference))
      return CommandResult<Guid>.Fail("library.invalid", "The content and its authoritative source reference are required.");
    var auth = await AuthorizeAsync(db, actor, Curators, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.TechnicalLibraryDocuments.AnyAsync(x => x.Id == documentId && x.FirmId == actor.FirmId, ct)) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await db.TechnicalLibraryVersions.AnyAsync(x => x.DocumentId == documentId && x.Status == TechnicalLibraryStates.Draft, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "A draft version is already awaiting publication.");
    var next = (await db.TechnicalLibraryVersions.Where(x => x.DocumentId == documentId).MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
    var version = new TechnicalLibraryVersion { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, DocumentId = documentId, Version = next, Body = body.Trim(),
      SourceReference = sourceReference.Trim(), EffectiveFrom = effectiveFrom, ContentSha256 = Hashing.Sha256Hex(body.Trim()), PreparedByUserId = actor.UserId, PreparedAt = DateTimeOffset.UtcNow };
    db.TechnicalLibraryVersions.Add(version);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(version.Id);
  }

  public static async Task<CommandResult> PublishAsync(IAuditSphereDbContext db, ActorContext actor, Guid versionId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, Publishers, ct);
    if (!auth.Succeeded) return auth;
    var version = await db.TechnicalLibraryVersions.SingleOrDefaultAsync(x => x.Id == versionId && x.FirmId == actor.FirmId, ct);
    if (version is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (version.Status == TechnicalLibraryStates.Published) return CommandResult.Ok();
    if (version.Status != TechnicalLibraryStates.Draft) return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft can be published.");
    if (version.PreparedByUserId == actor.UserId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Another Partner or administrator must publish what you prepared.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    foreach (var old in await db.TechnicalLibraryVersions.Where(x => x.DocumentId == version.DocumentId && x.Status == TechnicalLibraryStates.Published).ToListAsync(ct))
      old.Status = TechnicalLibraryStates.Superseded;
    await db.SaveChangesAsync(ct);
    version.Status = TechnicalLibraryStates.Published;
    version.ApprovedByUserId = actor.UserId;
    version.PublishedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<IReadOnlyList<LibraryHit>> SearchAsync(IAuditSphereDbContext db, ActorContext actor, string term, CancellationToken ct = default)
  {
    term = (term ?? "").Trim();
    if (term.Length < 2 || term.Length > 100) return [];
    var audiences = await AudiencesAsync(db, actor, ct);
    if (audiences.Length == 0) return [];
    var lowered = term.ToLowerInvariant();
    var rows = await db.TechnicalLibraryVersions.AsNoTracking().Where(v => v.FirmId == actor.FirmId && v.Status == TechnicalLibraryStates.Published)
      .Join(db.TechnicalLibraryDocuments.AsNoTracking(), v => v.DocumentId, d => d.Id, (v, d) => new { v, d })
      .Where(x => audiences.Contains(x.d.Audience) && (x.d.Title.ToLower().Contains(lowered) || x.d.Code.ToLower().Contains(lowered) || x.v.Body.ToLower().Contains(lowered)))
      .OrderBy(x => x.d.Code).Take(20).ToListAsync(ct);
    return rows.Select(x => new LibraryHit(x.d.Id, x.d.Code, x.d.Title, x.d.Category, x.v.Version, x.v.EffectiveFrom, Snippet(x.v.Body, term))).ToList();
  }

  public static async Task<CommandResult<LibraryEntry>> OpenAsync(IAuditSphereDbContext db, ActorContext actor, Guid documentId, int? version = null, CancellationToken ct = default)
  {
    var document = await db.TechnicalLibraryDocuments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == documentId && x.FirmId == actor.FirmId, ct);
    if (document is null || !(await AudiencesAsync(db, actor, ct)).Contains(document.Audience)) return CommandResult<LibraryEntry>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var curator = (await AuthorizeAsync(db, actor, Curators, ct)).Succeeded;
    var history = await db.TechnicalLibraryVersions.AsNoTracking().Where(x => x.DocumentId == documentId && (curator || x.Status != TechnicalLibraryStates.Draft))
      .OrderByDescending(x => x.Version).ToListAsync(ct);
    var chosen = version is null ? history.FirstOrDefault(x => x.Status == TechnicalLibraryStates.Published) ?? history.FirstOrDefault() : history.SingleOrDefault(x => x.Version == version);
    return chosen is null ? CommandResult<LibraryEntry>.Fail(ErrorCodes.GateBlocked, "No readable version exists yet.") : CommandResult<LibraryEntry>.Ok(new(document, chosen, history));
  }

  public static async Task<IReadOnlyList<(TechnicalLibraryDocument Document, int? PublishedVersion, bool HasDraft)>> ListAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var audiences = await AudiencesAsync(db, actor, ct);
    var documents = await db.TechnicalLibraryDocuments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && audiences.Contains(x.Audience)).OrderBy(x => x.Category).ThenBy(x => x.Code).ToListAsync(ct);
    var ids = documents.Select(x => x.Id).ToArray();
    var versions = await db.TechnicalLibraryVersions.AsNoTracking().Where(x => ids.Contains(x.DocumentId)).Select(x => new { x.DocumentId, x.Version, x.Status }).ToListAsync(ct);
    return documents.Select(d => (d, versions.Where(v => v.DocumentId == d.Id && v.Status == TechnicalLibraryStates.Published).Select(v => (int?)v.Version).FirstOrDefault(),
      versions.Any(v => v.DocumentId == d.Id && v.Status == TechnicalLibraryStates.Draft))).ToList();
  }

  private static async Task<string[]> AudiencesAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    if (!(await AuthorizeReadAsync(db, actor, ct)).Succeeded) return [];
    return (await HasAnyRoleAsync(db, actor, SeniorAudience, ct))
      ? [TechnicalLibraryAudiences.AllStaff, TechnicalLibraryAudiences.PartnersAndManagers] : [TechnicalLibraryAudiences.AllStaff];
  }

  internal static Task<CommandResult> AuthorizeReadAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizeAsync(db, actor, StaffRoles, ct);

  private static async Task<bool> HasAnyRoleAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    await db.RoleGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null && roles.Contains(x.Role), ct);

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true), ct);

  private static string Snippet(string body, string term)
  {
    var index = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
    if (index < 0) return body.Length <= 160 ? body : body[..157] + "…";
    var start = Math.Max(0, index - 60);
    var length = Math.Min(body.Length - start, 160);
    return (start > 0 ? "…" : "") + body.Substring(start, length) + (start + length < body.Length ? "…" : "");
  }
}

// ── Engagement economics and firm analytics (4.3-01..03) ───────────────────────────────────────────

public sealed record EngagementEconomicsRow(Guid EngagementId, string Label, string Currency, int BudgetMinutes, decimal? BudgetValue, int ActualMinutes,
  decimal? StandardValue, decimal? ActualCost, decimal? Billed, decimal? Collected, decimal? RealizationPercent, decimal? CollectionPercent,
  decimal? Profit, decimal? MarginPercent, int BudgetVarianceMinutes, bool CostComplete)
{
  public decimal? ContractedFee { get; init; }
  public string? ContractCurrency { get; init; }
  public decimal? LifetimeStandardValue { get; init; }
  public decimal? ContractedFeeLessStandardValue { get; init; }
  /// <summary>Approved non-billable and no-charge minutes in the period: counted in actual hours and cost, valued at zero.</summary>
  public int NonChargeableMinutes { get; init; }
}
public sealed record DepartmentUtilizationRow(string Department, int CapacityMinutes, int ChargeableMinutes, decimal? UtilizationPercent, decimal? TargetPercent);
public sealed record MilestonePerformance(int Due, int Completed, int CompletedOnTime, int CompletedLate, int Overdue,
  decimal? CompletionPercent, decimal? OnTimePercent);
public sealed record TaskWorkloadRow(string AssigneeKey, string AssigneeName, int Due, int Completed, int Open, int Overdue);
public sealed record TaskPhasePerformance(string Phase, string Label, int Due, int Completed, int Open, int Overdue, decimal? CompletionPercent);
public sealed record ReviewTurnaround(int Decisions, int Reviewed, int ChangesRequired, int Measured, int Excluded, decimal? AverageHours);
public sealed record ClientWaitSummary(int Intervals, int Completed, int Open, int Excluded,
  decimal? AverageCompletedHours, decimal? AverageOpenHours, bool Available, string? UnavailableReason);
public sealed record PracticeAnalyticsPageRequest(Guid? AfterEngagementId = null, int PageSize = 50);
public sealed record PracticeAnalyticsView(DateOnly From, DateOnly To, IReadOnlyList<EngagementEconomicsRow> Engagements, IReadOnlyList<DepartmentUtilizationRow> Departments,
  MilestonePerformance Milestones, IReadOnlyList<TaskWorkloadRow> Workload, IReadOnlyList<TaskPhasePerformance> PhaseWorkload,
  ReviewTurnaround ReviewTurnaround, ClientWaitSummary ClientWait, IReadOnlyList<string> Definitions)
{
  public int EngagementPageSize { get; init; } = 50;
  public bool HasMoreEngagements { get; init; }
  public Guid? NextEngagementId { get; init; }
}

/// <summary>
/// Defined practice measures from persisted evidence. Financial totals use approved time and posted billing records;
/// client wait uses provider-accepted PBC sends and recorded client actions. Standard value is approved time at its
/// captured charge-out rate; cost is the staff member's recorded internal cost rate, never the charge-out rate. Missing
/// inputs are reported as unavailable rather than zero.
/// </summary>
public static partial class PracticeAnalyticsQuery
{
  public static readonly string[] Definitions =
  [
    "Contract contribution = agreed contract fee − lifetime approved-time standard value, in the contract currency. This lifetime basis is separate from the period's billed less recorded staff cost and from firm ledger profit; missing rates or mixed currencies make it unavailable.",
    "Standard value = approved billable minutes × captured charge-out rate ÷ 60. Approved non-billable and no-charge time counts in actual hours and actual cost and has no charge-out value; it is shown separately. Billable time without a captured rate still makes the value unavailable.",
    "Actual cost = approved minutes × staff cost rate effective on the work date ÷ 60 (charge-out rates are not costs). Missing or conflicting latest effective rates make recorded staff cost, billed less recorded staff cost and its margin unavailable.",
    "Billed = posted invoice lines sourced from the engagement's approved time or fee milestones (credit notes deducted), engagement to date at the time of the query rather than limited to the selected work dates. It is not recognized revenue.",
    "Collected = receipt allocations to those invoices net of approved reversals, pro rata to the engagement's share of each invoice, engagement to date. It is not cash received within the selected period.",
    "Billing realization = billed ÷ standard value; collection ratio = collected ÷ billed; billed-less-staff-cost margin = (billed − recorded staff cost) ÷ billed. Billed less recorded staff cost excludes overheads and is not firm accounting profit. Ratios are per engagement and are never averaged into a firm ratio.",
    "Budget hours remaining / overrun = current approved budget minutes − approved actual minutes in the selected work dates; a negative value is an overrun, and an engagement without an approved budget has no budget minutes.",
    "Engagement financial figures are available only when approved time, the current approved budget and posted invoices use one known currency; no foreign-exchange conversion is implied.",
    "Recorded staff cost and billed less recorded staff cost are unavailable when an effective staff cost rate is missing or uses a different currency from the approved time entry.",
    "Utilization = approved billable minutes ÷ (weekly capacity × weeks − recorded unavailability); the weekly resource grid uses the same basis. Task completion = due-date tasks completed ÷ non-cancelled tasks due in the selected period; tasks without a due date are not applicable. On-time = tasks completed by their due date ÷ tasks due in the period.",
    "Workload = non-cancelled tasks due in the selected period, grouped by assigned staff member. Open includes overdue tasks; overdue is also shown separately. Unassigned and unavailable staff assignments have separate buckets. This is a task count, not a capacity or effort measure.",
    "Phase task performance = non-cancelled tasks due in the selected period grouped by their current workflow phase binding. Tasks without a workflow binding appear separately as unassigned phase. Completion is task status plus persisted completion timestamp; these operational counts do not establish phase or audit-gate readiness.",
    "Review turnaround = average elapsed hours from submission of the exact procedure-result revision to each append-only review decision created in the selected period. Both REVIEWED and CHANGES_REQUIRED decisions are included; result-revision mismatches and decisions before submission are excluded and counted. This is an operational duration, not a review-quality or audit-clearance measure.",
    "Client wait = intervals beginning with the first provider-accepted PBC email send while no prior client response is outstanding, and ending at the first recorded client message, acknowledgement/resubmission action, or client upload initiation. The selected period filters interval starts; later client actions are observed through query time. Queued, failed, uncertain and reminder emails do not start or reset a wait. Provider acceptance is not proof of inbox delivery. Open intervals are shown only for requests still in a client-waiting state; terminal requests without a recorded response are excluded. Large samples return unavailable and require a narrower period instead of a partial result."
  ];

  public static Task<CommandResult<PracticeAnalyticsView>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    DateOnly from, DateOnly to, CancellationToken ct = default) =>
    GetAsync(db, actor, from, to, new PracticeAnalyticsPageRequest(), ct);

  public static async Task<CommandResult<PracticeAnalyticsView>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    DateOnly from, DateOnly to, PracticeAnalyticsPageRequest page, CancellationToken ct = default)
  {
    if (page is null || page.PageSize is < 1 or > 100 || page.AfterEngagementId == Guid.Empty)
      return CommandResult<PracticeAnalyticsView>.Fail("analytics.invalid-page", "Choose an engagement page size from 1 to 100 and a valid cursor.");
    if (to < from || to.DayNumber - from.DayNumber > 800) return CommandResult<PracticeAnalyticsView>.Fail("analytics.invalid", "Choose a period of up to about two years.");
    var auth = await AuthorizeReadAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<PracticeAnalyticsView>.Fail(auth.ErrorCode!, auth.Message!);
    var f = actor.FirmId;
    var engagementIds = await SelectEconomicsPageAsync(db, f, from, to, page, ct);
    var hasMore = engagementIds.Count > page.PageSize;
    if (hasMore) engagementIds.RemoveAt(engagementIds.Count - 1);
    var nextEngagementId = hasMore ? engagementIds[^1] : (Guid?)null;
    var selectedInvoiceIds = await SelectEconomicsInvoiceIdsAsync(db, f, engagementIds, ct);
    var time = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == f && x.Status == PracticeTimeStates.TimeApproved && x.WorkDate >= from && x.WorkDate <= to).ToListAsync(ct);
    var costRates = await db.StaffCostRates.AsNoTracking().Where(x => x.FirmId == f).ToListAsync(ct);
    static string? NormalizeCurrency(string? currency) => string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3
      ? null : currency.Trim().ToUpperInvariant();
    decimal? Cost(TimeEntry t)
    {
      var timeCurrency = NormalizeCurrency(t.Currency);
      if (timeCurrency is null) return null;
      var applicableRates = costRates.Where(r => r.UserId == t.UserId && r.EffectiveFrom <= t.WorkDate && NormalizeCurrency(r.Currency) == timeCurrency).ToArray();
      if (applicableRates.Length == 0) return null;
      var latestEffectiveFrom = applicableRates.Max(r => r.EffectiveFrom);
      var latestRates = applicableRates.Where(r => r.EffectiveFrom == latestEffectiveFrom).Select(r => r.HourlyCost).Distinct().Take(2).ToArray();
      return latestRates.Length == 1 ? t.DurationMinutes * latestRates[0] / 60m : null;
    }
    // Billing traced to engagements through invoice line sources.
    var lines = await db.InvoiceLines.AsNoTracking().Join(db.Invoices.AsNoTracking(), l => l.InvoiceId, i => i.Id, (l, i) => new { l, i })
      .Where(x => x.i.FirmId == f && (x.i.Status == BillingStates.InvoicePosted || x.i.Status == BillingStates.InvoiceSent) && x.i.PostedAt != null && selectedInvoiceIds.Contains(x.i.Id)).ToListAsync(ct);
    var timeSources = lines.Where(x => x.l.SourceKind == "TIME" && x.l.SourceId.HasValue).Select(x => x.l.SourceId!.Value).ToArray();
    var timeEngagement = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == f && timeSources.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.EngagementId, ct);
    var milestoneSources = lines.Where(x => x.l.SourceKind == FeeAgreementService.MilestoneSourceKind && x.l.SourceId.HasValue).Select(x => x.l.SourceId!.Value).ToArray();
    var milestoneEngagement = await db.FeeMilestones.AsNoTracking().Where(x => x.FirmId == f && milestoneSources.Contains(x.Id))
      .Join(db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == f), m => m.AgreementId, a => a.Id, (m, a) => new { m.Id, a.EngagementId }).ToDictionaryAsync(x => x.Id, x => x.EngagementId, ct);
    Guid? LineEngagement(InvoiceLine l) => l.SourceKind == "TIME" && l.SourceId is { } t ? timeEngagement.GetValueOrDefault(t)
      : l.SourceKind == FeeAgreementService.MilestoneSourceKind && l.SourceId is { } m ? milestoneEngagement.GetValueOrDefault(m) : null;
    var billedLines = lines.Select(x => new { x.l, x.i, Engagement = LineEngagement(x.l) }).Where(x => x.Engagement.HasValue && engagementIds.Contains(x.Engagement.Value)).ToList();
    var agreements = await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId != null && engagementIds.Contains(x.EngagementId.Value)).ToListAsync(ct);
    var lifetimeTime = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == f && x.Status == PracticeTimeStates.TimeApproved &&
      x.EngagementId != null && engagementIds.Contains(x.EngagementId.Value)).Select(x => new { x.EngagementId, x.DurationMinutes, x.RatePerHour, x.Currency, x.BillableClassification }).ToListAsync(ct);
    var invoiceIds = billedLines.Select(x => x.i.Id).Distinct().ToArray();
    var billingAccountIds = billedLines.Select(x => x.i.BillingAccountId).Distinct().ToArray();
    var billingCurrencies = await db.BillingAccounts.AsNoTracking().Where(x => billingAccountIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Currency, ct);
    var allocations = await db.ReceiptAllocations.AsNoTracking().Where(x => x.FirmId == f && invoiceIds.Contains(x.InvoiceId)).GroupBy(x => x.InvoiceId)
      .Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
    var reversedAllocations = await (from reversal in db.ReceiptAllocationReversals.AsNoTracking()
      join allocation in db.ReceiptAllocations.AsNoTracking()
        on new { reversal.FirmId, reversal.ReceiptAllocationId } equals new { allocation.FirmId, ReceiptAllocationId = allocation.Id }
      where reversal.FirmId == f && invoiceIds.Contains(allocation.InvoiceId) &&
        reversal.Status == ReceiptAllocationReversalStates.Approved
      group reversal by allocation.InvoiceId into reversals
      select new { InvoiceId = reversals.Key, Amount = reversals.Sum(x => x.Amount) })
      .ToDictionaryAsync(x => x.InvoiceId, x => x.Amount, ct);
    var credits = await db.CreditNotes.AsNoTracking().Where(x => x.FirmId == f && invoiceIds.Contains(x.InvoiceId)).GroupBy(x => x.InvoiceId)
      .Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
    var labels = await db.Engagements.AsNoTracking().Where(x => x.FirmId == f && engagementIds.Contains(x.Id))
      .Join(db.PracticeClients.AsNoTracking().Where(x => x.FirmId == f), e => e.PracticeClientId, c => c.Id, (e, c) => new { e.Id, Label = (c.CommercialName ?? c.LegalName) + " · " + e.ServiceRoute + " " + e.PeriodEnd })
      .ToDictionaryAsync(x => x.Id, x => x.Label, ct);
    var budgets = await db.EngagementBudgets.AsNoTracking().Where(x => x.FirmId == f && engagementIds.Contains(x.EngagementId) && x.Status == PracticeTimeStates.BudgetApproved).ToListAsync(ct);
    var budgetIds = budgets.Select(x => x.Id).ToArray();
    var budgetLines = await db.BudgetLines.AsNoTracking().Where(x => budgetIds.Contains(x.EngagementBudgetId)).ToListAsync(ct);

    var rows = engagementIds.Select(id =>
    {
      var mine = time.Where(t => t.EngagementId == id).ToList();
      var costs = mine.Select(Cost).ToList();
      var budget = budgets.Where(b => b.EngagementId == id).OrderByDescending(b => b.Version).FirstOrDefault();
      var bl = budget is null ? [] : budgetLines.Where(l => l.EngagementBudgetId == budget.Id).ToList();
      var currencyInputs = new List<string?>();
      if (budget is not null) currencyInputs.Add(NormalizeCurrency(budget.Currency));
      // ADR-0018: approved non-billable and no-charge time carries hours and cost but no charge-out value, so it neither needs a
      // rate nor decides the currency. Billable time with a missing rate or currency still makes the value unavailable.
      var valued = mine.Where(t => t.BillableClassification == PracticeTimeStates.Billable).ToList();
      currencyInputs.AddRange(valued.Select(t => NormalizeCurrency(t.Currency)));
      var engagementInvoices = billedLines.Where(x => x.Engagement == id).GroupBy(x => x.i.Id).Select(x => x.First().i).ToList();
      currencyInputs.AddRange(engagementInvoices.Select(i => NormalizeCurrency(i.Currency ?? billingCurrencies.GetValueOrDefault(i.BillingAccountId))));
      var knownCurrencies = currencyInputs.Where(x => x is not null).Distinct(StringComparer.Ordinal).ToArray();
      var currency = knownCurrencies.Length > 1 ? "MIXED"
        : currencyInputs.Count == 0 || currencyInputs.Any(x => x is null) ? "UNAVAILABLE" : knownCurrencies[0]!;
      var currencyIsSafe = currency is not ("MIXED" or "UNAVAILABLE");
      var billed = 0m; var collected = 0m;
      foreach (var invoice in billedLines.Where(x => x.Engagement == id).GroupBy(x => x.i.Id))
      {
        var share = invoice.Sum(x => x.l.LineTotal);
        var total = invoice.First().i.Total;
        var ratio = total == 0 ? 0 : share / total;
        billed += share - credits.GetValueOrDefault(invoice.Key) * ratio;
        collected += Math.Max(0m, allocations.GetValueOrDefault(invoice.Key) - reversedAllocations.GetValueOrDefault(invoice.Key)) * ratio;
      }
      decimal? standard = currencyIsSafe && valued.All(t => t.RatePerHour.HasValue)
        ? MoneyPolicy.Normalize(valued.Sum(t => t.RatePerHour.HasValue ? t.DurationMinutes * t.RatePerHour.Value / 60m : 0m)) : null;
      var costComplete = currencyIsSafe && costs.All(c => c.HasValue);
      decimal? cost = costComplete ? MoneyPolicy.Normalize(costs.Sum(c => c!.Value)) : null;
      billed = MoneyPolicy.Normalize(billed); collected = MoneyPolicy.Normalize(collected);
      decimal? safeBilled = currencyIsSafe ? billed : null;
      decimal? safeCollected = currencyIsSafe ? collected : null;
      decimal? budgetValue = budget is not null && currencyIsSafe ? MoneyPolicy.Normalize(bl.Sum(x => x.ForecastCost)) : null;
      decimal? profit = safeBilled is { } billedForProfit && cost is { } costValue ? billedForProfit - costValue : null;
      var contracts = agreements.Where(x => x.EngagementId == id).ToArray();
      var contract = contracts.Length == 1 ? contracts[0] : null; // Ambiguous contract revisions are unavailable, never silently summed.
      var contribution = contract == null ? null : ContractContributionCalculator.Compute(contract.AgreedFee, contract.Currency,
        lifetimeTime.Where(x => x.EngagementId == id && x.BillableClassification == PracticeTimeStates.Billable)
          .Select(x => new ContractTimeValue(x.DurationMinutes, x.RatePerHour, x.Currency)).ToArray());
      return new EngagementEconomicsRow(id, labels.GetValueOrDefault(id, "Engagement"), currency,
        bl.Sum(x => x.ForecastMinutes), budgetValue, mine.Sum(t => t.DurationMinutes), standard, cost, safeBilled, safeCollected,
        standard is { } standardValue && standardValue > 0m && safeBilled is { } billedForRealization
          ? Math.Round(billedForRealization / standardValue * 100m, 1) : null,
        safeBilled is { } billedForCollection && billedForCollection > 0m && safeCollected is { } collectedValue
          ? Math.Round(collectedValue / billedForCollection * 100m, 1) : null,
        profit, safeBilled is { } billedForMargin && billedForMargin > 0m && profit is { } profitValue
          ? Math.Round(profitValue / billedForMargin * 100m, 1) : null,
        bl.Sum(x => x.ForecastMinutes) - mine.Sum(t => t.DurationMinutes), costComplete)
      { ContractedFee = contract?.AgreedFee, ContractCurrency = contract?.Currency,
        LifetimeStandardValue = contribution?.LifetimeStandardValue, ContractedFeeLessStandardValue = contribution?.FeeLessStandardValue,
        NonChargeableMinutes = mine.Sum(t => t.DurationMinutes) - valued.Sum(t => t.DurationMinutes) };
    }).OrderBy(x => x.EngagementId).ToList();

    // Department utilization over the same period.
    var profiles = await db.StaffProfiles.AsNoTracking().Where(x => x.FirmId == f).ToListAsync(ct);
    var availability = await db.StaffAvailabilities.AsNoTracking().Where(x => x.FirmId == f && x.EndDate >= from && x.StartDate <= to).ToListAsync(ct);
    var departments = profiles.GroupBy(p => p.Department).OrderBy(g => g.Key).Select(g =>
    {
      var capacity = 0; var chargeable = 0;
      foreach (var profile in g)
      {
        var weekly = profile.WeeklyCapacityMinutes;
        for (var day = from.DayNumber; day <= to.DayNumber; day++)
        {
          var d = DateOnly.FromDayNumber(day);
          if (!ResourceGridCalculator.IsWorkingDay(d)) continue;
          var perDay = ResourceGridCalculator.DailyCapacityMinutes(weekly, d.DayOfWeek);
          capacity += Math.Max(0, perDay - Math.Min(perDay, availability.Where(a => a.UserId == profile.UserId && a.StartDate <= d && a.EndDate >= d).Sum(a => a.MinutesPerDay)));
        }
        chargeable += time.Where(t => t.UserId == profile.UserId && t.BillableClassification == PracticeTimeStates.Billable).Sum(t => t.DurationMinutes);
      }
      return new DepartmentUtilizationRow(g.Key, capacity, chargeable, capacity > 0 ? Math.Round(chargeable * 100m / capacity, 1) : null,
        g.Any() ? Math.Round(g.Average(p => p.TargetUtilizationPercent), 1) : null);
    }).ToList();

    // Milestone (task due date) performance.
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var tasks = await db.WorkTasks.AsNoTracking().Where(x => x.FirmId == f && x.DueDate != null && x.DueDate >= from && x.DueDate <= to && x.Status != PracticeTimeStates.TaskCancelled).ToListAsync(ct);
    var onTime = tasks.Count(t => t.CompletedAt is { } c && DateOnly.FromDateTime(c.UtcDateTime) <= t.DueDate);
    var late = tasks.Count(t => t.CompletedAt is { } c && DateOnly.FromDateTime(c.UtcDateTime) > t.DueDate);
    var overdue = tasks.Count(t => t.CompletedAt is null && t.DueDate < today);
    var completed = tasks.Count(t => t.Status == PracticeTimeStates.TaskCompleted && t.CompletedAt is not null);
    var milestones = new MilestonePerformance(tasks.Count, completed, onTime, late, overdue,
      tasks.Count > 0 ? Math.Round(completed * 100m / tasks.Count, 1) : null,
      tasks.Count > 0 ? Math.Round(onTime * 100m / tasks.Count, 1) : null);
    var assigneeIds = tasks.Where(t => t.AssigneeUserId.HasValue).Select(t => t.AssigneeUserId!.Value).Distinct().ToArray();
    var staffUsers = await db.Users.AsNoTracking().Where(u => u.FirmId == f && u.UserKind == "Staff" && assigneeIds.Contains(u.Id))
      .Select(u => new { u.Id, u.DisplayName }).ToListAsync(ct);
    var staffNames = staffUsers.ToDictionary(u => u.Id,
      u => string.IsNullOrWhiteSpace(u.DisplayName) ? "Staff member" : u.DisplayName);
    var workload = tasks.GroupBy(t =>
      {
        var validStaffAssignee = t.AssigneeUserId is { } id && staffNames.ContainsKey(id) ? id : (Guid?)null;
        return (UserId: validStaffAssignee, Unavailable: t.AssigneeUserId.HasValue && validStaffAssignee is null);
      })
      .Select(g =>
      {
        var due = g.Count();
        var workloadCompleted = g.Count(t => t.Status == PracticeTimeStates.TaskCompleted && t.CompletedAt is not null);
        var workloadOverdue = g.Count(t => t.CompletedAt is null && t.DueDate < today);
        var key = g.Key.Unavailable ? "unavailable" : g.Key.UserId?.ToString("N") ?? "unassigned";
        var name = g.Key.Unavailable ? "Unavailable staff assignment"
          : g.Key.UserId is { } userId ? staffNames.GetValueOrDefault(userId, "Staff member") : "Unassigned";
        return new TaskWorkloadRow(key, name, due, workloadCompleted, due - workloadCompleted, workloadOverdue);
      })
      .OrderBy(x => x.AssigneeKey is "unassigned" or "unavailable" ? 1 : 0)
      .ThenBy(x => x.AssigneeName, StringComparer.OrdinalIgnoreCase).ToList();

    var taskIds = tasks.Select(t => t.Id).ToArray();
    var phaseByTask = taskIds.Length == 0 ? new Dictionary<Guid, string>() : await db.WorkflowTaskBindings.AsNoTracking()
      .Where(x => x.FirmId == f && taskIds.Contains(x.WorkTaskId))
      .Select(x => new { x.WorkTaskId, x.Phase }).ToDictionaryAsync(x => x.WorkTaskId, x => x.Phase, ct);
    static string PhaseLabel(string phase) => phase switch
    {
      WorkflowTaskPhases.ClientOnboarding => "Client Onboarding",
      WorkflowTaskPhases.AuditPlanning => "Audit Planning",
      WorkflowTaskPhases.AuditFieldwork => "Audit Fieldwork",
      WorkflowTaskPhases.ManagerReview => "Manager Review",
      WorkflowTaskPhases.PartnerSignoff => "Partner Sign-off",
      WorkflowTaskPhases.ReportDelivery => "Report Delivery",
      WorkflowTaskPhases.ArchiveClose => "Archive and Close",
      _ => "Unassigned phase"
    };
    var phaseWorkload = WorkflowTaskPhases.All.Append("UNASSIGNED_PHASE").Select(phase =>
    {
      var phaseTasks = tasks.Where(t => phaseByTask.GetValueOrDefault(t.Id, "UNASSIGNED_PHASE") == phase).ToArray();
      var due = phaseTasks.Length;
      var phaseCompleted = phaseTasks.Count(t => t.Status == PracticeTimeStates.TaskCompleted && t.CompletedAt is not null);
      var phaseOverdue = phaseTasks.Count(t => t.CompletedAt is null && t.DueDate < today);
      return new TaskPhasePerformance(phase, PhaseLabel(phase), due, phaseCompleted, due - phaseCompleted, phaseOverdue,
        due > 0 ? Math.Round(phaseCompleted * 100m / due, 1) : null);
    }).ToList();

    // Bound review decisions by the requested period and exact firm/result scope. The immutable review event
    // carries its own revision, so stale or impossible chronology is visible but cannot create a negative duration.
    var reviewStart = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
    var reviewSet = db.AuditProcedureReviews.AsNoTracking().Where(x => x.FirmId == f && x.CreatedAt >= reviewStart);
    if (to < DateOnly.MaxValue)
    {
      var reviewEndExclusive = new DateTimeOffset(to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
      reviewSet = reviewSet.Where(x => x.CreatedAt < reviewEndExclusive);
    }
    var reviewRows = await (
      from review in reviewSet
      join result in db.AuditProcedureResults.AsNoTracking()
        on new { review.FirmId, review.ClientId, review.EngagementId, ResultId = review.AuditProcedureResultId }
        equals new { result.FirmId, result.ClientId, result.EngagementId, ResultId = result.Id }
      select new { review.Decision, ReviewRevision = review.ResultRevision, ResultRevision = result.Revision, result.SubmittedAt, review.CreatedAt }
    ).ToListAsync(ct);
    var measurableReviews = reviewRows.Where(x => x.ReviewRevision == x.ResultRevision && x.CreatedAt >= x.SubmittedAt).ToArray();
    var averageReviewHours = measurableReviews.Length == 0 ? (decimal?)null : Math.Round(
      measurableReviews.Sum(x => (decimal)(x.CreatedAt - x.SubmittedAt).Ticks / TimeSpan.TicksPerHour) / measurableReviews.Length, 1);
    var reviewTurnaround = new ReviewTurnaround(reviewRows.Count,
      reviewRows.Count(x => x.Decision == AuditProcedureReviewDecisions.Reviewed),
      reviewRows.Count(x => x.Decision == AuditProcedureReviewDecisions.ChangesRequired), measurableReviews.Length,
      reviewRows.Count - measurableReviews.Length, averageReviewHours);
    var clientWait = await PracticeClientWaitQuery.GetAsync(db, f, from, to, ct);
    return CommandResult<PracticeAnalyticsView>.Ok(new(from, to, rows, departments, milestones, workload, phaseWorkload, reviewTurnaround, clientWait, Definitions)
      { EngagementPageSize = page.PageSize, HasMoreEngagements = hasMore, NextEngagementId = nextEngagementId });
  }

  internal static Task<CommandResult> AuthorizeReadAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId,
      RequiredRoles: ["Partner", "Administrator", "FinanceManager"], InternalOnly: true, RequireFirmWide: true), ct);

  public static async Task<CommandResult<Guid>> RecordCostRateAsync(IClientAccountingDbContext db, ActorContext actor, Guid userId, decimal hourlyCost, string currency, DateOnly effectiveFrom, CancellationToken ct = default)
  {
    if (hourlyCost <= 0 || (currency ?? "").Trim().Length != 3) return CommandResult<Guid>.Fail("analytics.invalid", "A positive hourly cost and a currency are required.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Partner", "Administrator", "FinanceManager"], InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.Users.AnyAsync(x => x.Id == userId && x.FirmId == actor.FirmId && x.UserKind == "Staff", ct)) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rate = new StaffCostRate { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = userId, HourlyCost = hourlyCost, Currency = currency!.Trim().ToUpperInvariant(),
      EffectiveFrom = effectiveFrom, RecordedByUserId = actor.UserId, RecordedAt = DateTimeOffset.UtcNow };
    db.StaffCostRates.Add(rate);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(rate.Id);
  }
}

// ── Firm expenses and trial balance (4.4-01..02) ───────────────────────────────────────────────────

public sealed record RecordFirmExpenseRequest(DateOnly ExpenseDate, string Category, string Payee, string Description, decimal Amount, string Currency,
  Guid ExpenseAccountId, Guid PaymentAccountId, string EvidenceFileName, string EvidenceContentType, byte[] EvidenceContent,
  Guid? RequestId = null, string? RequestHash = null);
public sealed record FirmExpenseCreationReceipt(Guid ExpenseId, Guid ActorId, Guid RequestId, string RequestHash, string Status);
public sealed record FirmExpenseCreationLookup(bool Found, FirmExpenseCreationReceipt? Receipt);
public sealed record FirmTrialBalanceRow(Guid AccountId, string Code, string Name, string AccountType, decimal OpeningDebit, decimal OpeningCredit,
  decimal MovementDebit, decimal MovementCredit, decimal ClosingDebit, decimal ClosingCredit);
public sealed record FirmProfitLossActivity(Guid LineId, Guid PostingId, Guid JournalId, string PeriodCode, DateTimeOffset PostedAt,
  string JournalNumber, string PostingPurpose, Guid AccountId, string AccountCode, string AccountName, string Description,
  decimal RevenueActivity, decimal ExpenseActivity);
public sealed record FirmTrialBalanceView(string FromPeriod, string ToPeriod, IReadOnlyList<FirmTrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit, bool Balanced,
  decimal Revenue, decimal Expenses, decimal Profit, decimal CumulativeProfit, decimal Assets, decimal Liabilities, decimal Equity, bool PositionReconciles,
  string? Currency, int ActivityPage, int ActivityPageSize, int TotalActivityCount, bool HasMoreActivity,
  IReadOnlyList<FirmProfitLossActivity> ProfitLossActivity, IReadOnlyList<FirmProfitLossMonth> MonthlyProfitLoss, string ReportIdentity = "");
/// <summary>
/// One accounting month of the firm's operating profit and loss, from the same posted lines as the range totals, so the months in
/// a range add up to those totals exactly. The comparator is the same month one year earlier and is null when the firm has no
/// accounting period for that month, which is different from a period with no activity.
/// </summary>
public sealed record FirmProfitLossMonth(string PeriodCode, decimal Revenue, decimal Expenses, decimal Profit,
  decimal? PriorYearRevenue, decimal? PriorYearExpenses, decimal? PriorYearProfit);
public sealed record FirmTrialBalanceCsvExport(string FileName, string Currency, string Csv, string ReportIdentity = "");

/// <summary>
/// The firm's own operating expenses (rent, salaries, petty cash…) with source evidence, prepared by finance,
/// approved by a separate finance reviewer and posted through the firm ledger's own journal controls; and the firm
/// trial balance with opening, movement and closing balances and financial summaries calculated from postings.
/// </summary>
public static class FirmExpenseService
{
  private static readonly string[] Preparers = ["FinanceManager"];
  private static readonly string[] Reviewers = ["FinanceReviewer"];
  public const int MaxEvidenceBytes = 5 * 1024 * 1024;
  public const int MaxReviewCommentLength = 1000;

  private static string Part(string value) => $"{System.Text.Encoding.UTF8.GetByteCount(value)}:{value}";
  private static string CreationHash(ActorContext actor, RecordFirmExpenseRequest request, string category,
    string payee, string description, string currency, string fileName, string contentType, string evidenceSha256) =>
    Hashing.Sha256Hex(string.Concat(new[]
    {
      "firm-expense-create-v1", actor.FirmId.ToString("D"), actor.UserId.ToString("D"),
      request.RequestId!.Value.ToString("D"), request.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      category, payee, description, MoneyPolicy.Normalize(request.Amount, 2).ToString("0.00", CultureInfo.InvariantCulture),
      currency, request.ExpenseAccountId.ToString("D"), request.PaymentAccountId.ToString("D"), fileName, contentType, evidenceSha256
    }.Select(Part)));

  public static string CreateRequestHash(ActorContext actor, RecordFirmExpenseRequest request)
  {
    var contentType = string.IsNullOrWhiteSpace(request.EvidenceContentType) ? "application/octet-stream" : request.EvidenceContentType.Trim();
    return CreationHash(actor, request, (request.Category ?? "").Trim().ToUpperInvariant(), request.Payee.Trim(),
      request.Description.Trim(), request.Currency.Trim().ToUpperInvariant(), request.EvidenceFileName.Trim(),
      contentType, Hashing.Sha256Hex(request.EvidenceContent));
  }

  public static async Task<CommandResult<Guid>> RecordAsync(IClientAccountingDbContext db, ActorContext actor, RecordFirmExpenseRequest request, CancellationToken ct = default)
  {
    var category = (request.Category ?? "").Trim().ToUpperInvariant();
    if (!FirmExpenseCategories.All.Contains(category) || request.Amount <= 0 || MoneyPolicy.Normalize(request.Amount, 2) != request.Amount ||
        string.IsNullOrWhiteSpace(request.Payee) || string.IsNullOrWhiteSpace(request.Description) ||
        (request.Currency ?? "").Trim().Length != 3 || request.EvidenceContent is not { Length: > 0 and <= MaxEvidenceBytes } || string.IsNullOrWhiteSpace(request.EvidenceFileName))
      return CommandResult<Guid>.Fail("expense.invalid", "Category, payee, description, a positive amount, currency and a source document (up to 5 MB) are required.");
    if ((request.RequestId is null) != (request.RequestHash is null) || request.RequestId == Guid.Empty)
      return CommandResult<Guid>.Fail("request.invalid", "An exact expense creation reference is required.");
    var evidenceSha256 = Hashing.Sha256Hex(request.EvidenceContent);
    var normalizedPayee = request.Payee.Trim();
    var normalizedDescription = request.Description.Trim();
    var currency = request.Currency!.Trim().ToUpperInvariant();
    var fileName = request.EvidenceFileName.Trim();
    var contentType = string.IsNullOrWhiteSpace(request.EvidenceContentType) ? "application/octet-stream" : request.EvidenceContentType.Trim();
    var requestHash = request.RequestId is null ? null : CreationHash(actor, request, category, normalizedPayee,
      normalizedDescription, currency, fileName, contentType, evidenceSha256);
    if (request.RequestHash is not null && (request.RequestHash.Length != 64 ||
        request.RequestHash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) || request.RequestHash != requestHash))
      return CommandResult<Guid>.Fail("request.invalid", "The expense creation reference does not match these exact fields and source bytes.");
    var auth = await AuthorizeAsync(db, actor, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var transaction = request.RequestId is null ? null : await db.Database.BeginTransactionAsync(ct);
    if (request.RequestId is { } requestId)
    {
      var lockedActor = await db.Users.FromSqlInterpolated(
        $"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (lockedActor is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      auth = await AuthorizeAsync(db, actor, Preparers, ct);
      if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
      var existing = await db.FirmExpenses.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.PreparedByUserId == actor.UserId && x.CreateRequestId == requestId, ct);
      if (existing is not null)
      {
        if (existing.CreateRequestHash != requestHash)
          return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This expense reference is already bound to different exact source evidence or fields.");
        await transaction!.CommitAsync(ct);
        return CommandResult<Guid>.Ok(existing.Id);
      }
    }

    var accounts = await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (x.Id == request.ExpenseAccountId || x.Id == request.PaymentAccountId)).ToListAsync(ct);
    if (accounts.SingleOrDefault(x => x.Id == request.ExpenseAccountId)?.AccountType != LedgerStates.AccountExpense ||
        accounts.SingleOrDefault(x => x.Id == request.PaymentAccountId) is not { } payment || payment.AccountType is not (LedgerStates.AccountAsset or LedgerStates.AccountLiability))
      return CommandResult<Guid>.Fail("expense.invalid", "Choose an expense account and a cash, bank, petty-cash or payable account.");
    var expense = new FirmExpense
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ExpenseDate = request.ExpenseDate, Category = category, Payee = normalizedPayee, Description = normalizedDescription,
      Amount = MoneyPolicy.Normalize(request.Amount, 2), Currency = currency, ExpenseAccountId = request.ExpenseAccountId, PaymentAccountId = request.PaymentAccountId,
      EvidenceFileName = fileName, EvidenceContentType = contentType, EvidenceContent = request.EvidenceContent, EvidenceSha256 = evidenceSha256,
      CreateRequestId = request.RequestId, CreateRequestHash = requestHash, PreparedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.FirmExpenses.Add(expense);
    await db.SaveChangesAsync(ct);
    if (transaction is not null) await transaction.CommitAsync(ct);
    return CommandResult<Guid>.Ok(expense.Id);
  }

  public static async Task<CommandResult<FirmExpenseCreationLookup>> LookupCreationAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || requestHash is not { Length: 64 } ||
        requestHash.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
      return CommandResult<FirmExpenseCreationLookup>.Fail("request.invalid", "The expense reference is invalid.");
    var auth = await AuthorizeAsync(db, actor, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<FirmExpenseCreationLookup>.Fail(ErrorCodes.ScopeDenied, "The expense creation receipt is unavailable.");
    var existing = await db.FirmExpenses.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.PreparedByUserId == actor.UserId && x.CreateRequestId == requestId, ct);
    if (existing is null)
    {
      auth = await AuthorizeAsync(db, actor, Preparers, ct);
      return auth.Succeeded
        ? CommandResult<FirmExpenseCreationLookup>.Ok(new(false, null))
        : CommandResult<FirmExpenseCreationLookup>.Fail(ErrorCodes.ScopeDenied, "The expense creation receipt is unavailable.");
    }
    if (existing.CreateRequestHash != requestHash)
      return CommandResult<FirmExpenseCreationLookup>.Fail(ErrorCodes.IdempotencyConflict, "This reference is bound to a different expense intent.");
    auth = await AuthorizeAsync(db, actor, Preparers, ct);
    return auth.Succeeded
      ? CommandResult<FirmExpenseCreationLookup>.Ok(new(true, new(existing.Id, actor.UserId, requestId, requestHash, existing.Status)))
      : CommandResult<FirmExpenseCreationLookup>.Fail(ErrorCodes.ScopeDenied, "The expense creation receipt is unavailable.");
  }

  /// <summary>Submits the expense and drafts its balanced journal (Dr expense, Cr payment account) for review.</summary>
  public static async Task<CommandResult> SubmitAsync(IClientAccountingDbContext db, ActorContext actor, Guid expenseId, CancellationToken ct = default)
  {
    var expense = await db.FirmExpenses.SingleOrDefaultAsync(x => x.Id == expenseId && x.FirmId == actor.FirmId, ct);
    if (expense is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, Preparers, ct);
    if (!auth.Succeeded) return auth;
    if (expense.Status != FirmExpenseStates.Draft) return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft expense can be submitted.");
    var periodCode = expense.ExpenseDate.ToString("yyyy-MM", CultureInfo.InvariantCulture);
    var period = await db.FirmPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.PeriodCode == periodCode, ct);
    if (period is null) return CommandResult.Fail(ErrorCodes.GateBlocked, $"Open the firm period {periodCode} first.");
    var journalNumber = $"EXP-{expense.ExpenseDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-{expense.Id.ToString("N", CultureInfo.InvariantCulture).ToUpperInvariant()}";
    var journal = await LedgerService.CreateFirmJournalDraftAsync(db, actor, new CreateFirmJournalDraftRequest(period.Id, journalNumber,
      "EXPENSE", expense.Id.ToString("D"), 1, $"Firm expense: {expense.Category.ToLowerInvariant().Replace('_', ' ')}", expense.Currency,
      [new FirmJournalLineRequest(expense.ExpenseAccountId, $"{expense.Payee}: {expense.Description}", expense.Amount, 0m),
       new FirmJournalLineRequest(expense.PaymentAccountId, $"{expense.Payee}: {expense.Description}", 0m, expense.Amount)]), ct);
    if (!journal.Succeeded) return CommandResult.Fail(journal.ErrorCode!, journal.Message!);
    var submitted = await LedgerService.SubmitFirmJournalAsync(db, actor, journal.Value, ct);
    if (!submitted.Succeeded) return submitted;
    var live = await db.FirmExpenses.SingleAsync(x => x.Id == expenseId, ct);
    live.JournalId = journal.Value;
    live.Status = FirmExpenseStates.Submitted;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ReviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid expenseId, bool approve, string? comment, CancellationToken ct = default)
  {
    var expense = await db.FirmExpenses.SingleOrDefaultAsync(x => x.Id == expenseId && x.FirmId == actor.FirmId, ct);
    if (expense is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, Reviewers, ct);
    if (!auth.Succeeded) return auth;
    if (expense.PreparedByUserId == actor.UserId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "The preparer cannot review their own expense.");
    if (expense.Status != FirmExpenseStates.Submitted) return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted expense can be reviewed.");
    if (!approve && string.IsNullOrWhiteSpace(comment)) return CommandResult.Fail("expense.invalid", "Explain why the expense is rejected.");
    var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
    if (normalizedComment?.Length > MaxReviewCommentLength)
      return CommandResult.Fail("expense.invalid", $"The review comment must be {MaxReviewCommentLength} characters or fewer.");
    if (approve)
    {
      var approved = await LedgerService.ApproveFirmJournalAsync(db, actor, expense.JournalId!.Value, ct);
      if (!approved.Succeeded) return approved;
    }
    var live = await db.FirmExpenses.SingleAsync(x => x.Id == expenseId, ct);
    live.Status = approve ? FirmExpenseStates.Approved : FirmExpenseStates.Rejected;
    live.ReviewedByUserId = actor.UserId;
    live.ReviewedAt = DateTimeOffset.UtcNow;
    live.ReviewComment = normalizedComment;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> PostAsync(IClientAccountingDbContext db, ActorContext actor, Guid expenseId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var expense = await db.FirmExpenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == expenseId && x.FirmId == actor.FirmId, ct);
    if (expense is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (expense.Status == FirmExpenseStates.Posted) return CommandResult<Guid>.Ok(expense.PostingId!.Value);
    if (expense.Status != FirmExpenseStates.Approved) return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only an approved expense can be posted.");
    var posting = await LedgerService.PostFirmJournalAsync(db, actor, expense.JournalId!.Value, ct);
    if (!posting.Succeeded) return posting;
    var live = await db.FirmExpenses.SingleAsync(x => x.Id == expenseId, ct);
    live.Status = FirmExpenseStates.Posted;
    live.PostingId = posting.Value;
    await db.SaveChangesAsync(ct);
    return posting;
  }

  public static async Task<IReadOnlyList<FirmExpense>> ListAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default,
    int page = 1, Guid? expenseId = null, bool includeContinuation = false)
  {
    if (page is < 1 or > 100000 || expenseId == Guid.Empty || !await CanReadExpenseMetadataAsync(db, actor, ct)) return [];
    // Explicit read projection only: never materialize evidence bytes or reuse oversight for an expense command.
    var rows = await db.FirmExpenses.AsNoTracking().Where(x => x.FirmId == actor.FirmId && (!expenseId.HasValue || x.Id == expenseId))
        .OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
        .Skip((page - 1) * 200).Take(includeContinuation ? 201 : 200)
        .Select(x => new FirmExpense { Id = x.Id, FirmId = x.FirmId, ExpenseDate = x.ExpenseDate, Category = x.Category, Payee = x.Payee, Description = x.Description, Amount = x.Amount,
          Currency = x.Currency, EvidenceFileName = x.EvidenceFileName, EvidenceSha256 = x.EvidenceSha256, Status = x.Status, PreparedByUserId = x.PreparedByUserId, ReviewedByUserId = x.ReviewedByUserId,
          ReviewComment = x.ReviewComment, PostingId = x.PostingId, CreatedAt = x.CreatedAt }).ToListAsync(ct);
    return await CanReadExpenseMetadataAsync(db, actor, ct) ? rows : [];
  }

  private static async Task<bool> CanReadExpenseMetadataAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct) =>
    (await AuthorizeAsync(db, actor, [.. Preparers, .. Reviewers], ct)).Succeeded ||
    (await AdministratorOversightReadAuthorization.AuthorizeAsync(db, actor, ct)).Succeeded;

  /// <summary>
  /// The trial balance shows cumulative opening, movement and closing balances. Revenue, expenses and profit are
  /// posted activity within the selected periods; YEAR_END_CLOSE postings remain in the trial balance but are
  /// excluded from operating activity. Cumulative P&amp;L is retained separately for the closing-position check.
  /// </summary>
  public const int TrialBalanceActivityPageSize = 100;
  public const int MaxTrialBalanceExportActivityRows = 5000;
  public const int MaxMonthlyProfitLossMonths = 1200;

  public static async Task<CommandResult<FirmTrialBalanceView>> TrialBalanceAsync(IClientAccountingDbContext db, ActorContext actor,
    string fromPeriod, string toPeriod, CancellationToken ct = default, int activityPage = 1, bool includeAllActivities = false,
    string? expectedReportIdentity = null)
  {
    if (!IsPeriod(fromPeriod) || !IsPeriod(toPeriod) || string.CompareOrdinal(fromPeriod, toPeriod) > 0 || activityPage is < 1 or > 100_000)
      return CommandResult<FirmTrialBalanceView>.Fail("ledger.invalid", "Choose a valid YYYY-MM period range and activity page.");
    var auth = await AuthorizeAsync(db, actor, [.. Preparers, .. Reviewers, "Partner", "Administrator"], ct);
    if (!auth.Succeeded) return CommandResult<FirmTrialBalanceView>.Fail(auth.ErrorCode!, auth.Message!);
    if ((activityPage > 1 || includeAllActivities) && string.IsNullOrWhiteSpace(expectedReportIdentity))
      return CommandResult<FirmTrialBalanceView>.Fail(ErrorCodes.GenerationStale,
        "Reload the first report page before continuing or exporting it.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<FirmTrialBalanceView>.Fail(ErrorCodes.GateBlocked,
        "Open this report in a separate read context so its source snapshot and current access can be verified.");
    // Totals, comparatives and paged activity must describe the same committed posting population.
    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    var periods = await db.FirmPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && string.Compare(x.PeriodCode, toPeriod) <= 0).ToListAsync(ct);
    // Match the existing Angular monthly-series boundary before materializing financial activity.
    // Wide requested ranges are safe when the actual stored reporting span is small.
    var reportingPeriods = periods.Where(p => string.CompareOrdinal(p.PeriodCode, fromPeriod) >= 0)
      .Select(p => p.PeriodCode).OrderBy(code => code, StringComparer.Ordinal).ToArray();
    if (reportingPeriods.Length > 0)
    {
      var firstMonth = DateOnly.ParseExact(reportingPeriods[0] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
      var lastMonth = DateOnly.ParseExact(reportingPeriods[^1] + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
      var monthCount = (lastMonth.Year - firstMonth.Year) * 12 + lastMonth.Month - firstMonth.Month + 1;
      if (monthCount > MaxMonthlyProfitLossMonths)
        return CommandResult<FirmTrialBalanceView>.Fail("finance.report-limit",
          $"The monthly report exceeds {MaxMonthlyProfitLossMonths} months. Choose a narrower period range.");
    }
    var openingIds = periods.Where(p => string.CompareOrdinal(p.PeriodCode, fromPeriod) < 0).Select(p => p.Id).ToHashSet();
    var periodIds = periods.Select(p => p.Id).ToArray();
    var lines = await (from line in db.FirmPostingLines.AsNoTracking()
      join posting in db.FirmPostings.AsNoTracking()
        on new { line.FirmId, PostingId = line.PostingId } equals new { posting.FirmId, PostingId = posting.Id }
      join journal in db.FirmJournals.AsNoTracking()
        on new { posting.FirmId, JournalId = posting.JournalId } equals new { journal.FirmId, JournalId = journal.Id }
      where line.FirmId == actor.FirmId && periodIds.Contains(posting.PeriodId)
      select new
      {
        LineId = line.Id, line.FirmAccountId, line.Debit, line.Credit, PostingId = posting.Id,
        posting.ReversalOfPostingId, posting.PeriodId, posting.Currency, JournalId = journal.Id,
        PostedAt = posting.PostedAt, journal.JournalNumber, journal.PostingPurpose
      }).ToListAsync(ct);
    var accounts = await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId)
      .OrderBy(x => x.Code).ThenBy(x => x.Id).ToListAsync(ct);
    var currencyValues = lines.Select(x => x.Currency).Distinct(StringComparer.Ordinal)
      .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ThenBy(x => x, StringComparer.Ordinal).ToArray();
    var currencies = currencyValues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (currencies.Length > 1)
      return CommandResult<FirmTrialBalanceView>.Fail("ledger.currency-mixed",
        "This report contains posted amounts in more than one currency. Filter or translate each currency separately before reconciling it.");
    var currency = currencies.SingleOrDefault() ?? await db.FirmFinanceProfiles.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Approved)
      .OrderByDescending(x => x.ApprovedAt).Select(x => x.FunctionalCurrency).FirstOrDefaultAsync(ct);
    var postedJournalIds = lines.Select(x => x.JournalId).Distinct().ToArray();
    var journalLines = await db.FirmJournalLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && postedJournalIds.Contains(x.JournalId))
      .OrderBy(x => x.JournalId).ThenBy(x => x.Id)
      .Select(x => new { x.Id, x.JournalId, x.FirmAccountId, x.Debit, x.Credit, x.Description }).ToListAsync(ct);
    static (decimal Debit, decimal Credit) Net(decimal debit, decimal credit) => debit >= credit ? (MoneyPolicy.Normalize(debit - credit), 0m) : (0m, MoneyPolicy.Normalize(credit - debit));
    var rows = accounts.Select(a =>
    {
      var mine = lines.Where(x => x.FirmAccountId == a.Id).ToList();
      var opening = Net(mine.Where(x => openingIds.Contains(x.PeriodId)).Sum(x => x.Debit), mine.Where(x => openingIds.Contains(x.PeriodId)).Sum(x => x.Credit));
      var movementDebit = MoneyPolicy.Normalize(mine.Where(x => !openingIds.Contains(x.PeriodId)).Sum(x => x.Debit));
      var movementCredit = MoneyPolicy.Normalize(mine.Where(x => !openingIds.Contains(x.PeriodId)).Sum(x => x.Credit));
      var closing = Net(opening.Debit + movementDebit, opening.Credit + movementCredit);
      return new FirmTrialBalanceRow(a.Id, a.Code, a.Name, a.AccountType, opening.Debit, opening.Credit, movementDebit, movementCredit, closing.Debit, closing.Credit);
    }).Where(r => r.OpeningDebit + r.OpeningCredit + r.MovementDebit + r.MovementCredit != 0).ToList();
    var totalDebit = rows.Sum(r => r.ClosingDebit);
    var totalCredit = rows.Sum(r => r.ClosingCredit);
    decimal Balance(string type, bool debitNormal) => rows.Where(r => r.AccountType == type).Sum(r => debitNormal ? r.ClosingDebit - r.ClosingCredit : r.ClosingCredit - r.ClosingDebit);
    var yearEndClosingPostingIds = lines.Where(x => string.Equals(x.PostingPurpose,
      LedgerStates.YearEndClosingPurpose, StringComparison.OrdinalIgnoreCase)).Select(x => x.PostingId).ToHashSet();
    // Operating activity in any loaded period. The range totals and the monthly series both read this one set.
    var operatingLinesAllPeriods = lines.Where(x =>
      !string.Equals(x.PostingPurpose, LedgerStates.YearEndClosingPurpose, StringComparison.OrdinalIgnoreCase) &&
      !string.Equals(x.PostingPurpose, "OPENING_BALANCE", StringComparison.OrdinalIgnoreCase) &&
      (x.ReversalOfPostingId is null || !yearEndClosingPostingIds.Contains(x.ReversalOfPostingId.Value))).ToList();
    var operatingLines = operatingLinesAllPeriods.Where(x => !openingIds.Contains(x.PeriodId)).ToList();
    var revenueAccountIds = accounts.Where(x => x.AccountType == LedgerStates.AccountRevenue).Select(x => x.Id).ToHashSet();
    var expenseAccountIds = accounts.Where(x => x.AccountType == LedgerStates.AccountExpense).Select(x => x.Id).ToHashSet();
    var revenue = MoneyPolicy.Normalize(operatingLines.Where(x => revenueAccountIds.Contains(x.FirmAccountId)).Sum(x => x.Credit - x.Debit));
    var expenses = MoneyPolicy.Normalize(operatingLines.Where(x => expenseAccountIds.Contains(x.FirmAccountId)).Sum(x => x.Debit - x.Credit));
    var periodCodes = periods.ToDictionary(p => p.Id, p => p.PeriodCode);
    var activityByMonth = operatingLinesAllPeriods.GroupBy(x => periodCodes[x.PeriodId], StringComparer.Ordinal).ToDictionary(g => g.Key, g => (
      Revenue: MoneyPolicy.Normalize(g.Where(x => revenueAccountIds.Contains(x.FirmAccountId)).Sum(x => x.Credit - x.Debit)),
      Expenses: MoneyPolicy.Normalize(g.Where(x => expenseAccountIds.Contains(x.FirmAccountId)).Sum(x => x.Debit - x.Credit))), StringComparer.Ordinal);
    var knownMonths = periodCodes.Values.ToHashSet(StringComparer.Ordinal);
    // The series runs from the first to the last accounting period the firm holds inside the range, so its length is bounded by
    // stored periods and not by the requested dates. A month between them with no period or no activity is shown as zero.
    var monthsInRange = knownMonths.Where(code => string.CompareOrdinal(code, fromPeriod) >= 0).OrderBy(code => code, StringComparer.Ordinal).ToList();
    var monthlyProfitLoss = new List<FirmProfitLossMonth>();
    if (monthsInRange.Count > 0)
    {
      static DateOnly Month(string code) => DateOnly.ParseExact(code + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
      static string Code(DateOnly month) => month.ToString("yyyy-MM", CultureInfo.InvariantCulture);
      var last = Month(monthsInRange[^1]);
      for (var month = Month(monthsInRange[0]); ; month = month.AddMonths(1))
      {
        var current = activityByMonth.GetValueOrDefault(Code(month));
        var priorCode = month.Year > 1 ? Code(month.AddYears(-1)) : null;
        (decimal Revenue, decimal Expenses)? prior = priorCode is not null && knownMonths.Contains(priorCode) ? activityByMonth.GetValueOrDefault(priorCode) : null;
        monthlyProfitLoss.Add(new(Code(month), current.Revenue, current.Expenses, current.Revenue - current.Expenses,
          prior?.Revenue, prior?.Expenses, prior is null ? null : prior.Value.Revenue - prior.Value.Expenses));
        // Stop before advancing beyond the final representable month.
        if (month == last) break;
      }
    }
    var revenueAndExpenseAccountIds = revenueAccountIds.Concat(expenseAccountIds).ToArray();
    var openingPeriodIds = openingIds.ToArray();
    var closingPostingIds = yearEndClosingPostingIds.ToArray();
    var activityQuery =
      from line in db.FirmPostingLines.AsNoTracking()
      join posting in db.FirmPostings.AsNoTracking()
        on new { line.FirmId, PostingId = line.PostingId } equals new { posting.FirmId, PostingId = posting.Id }
      join journal in db.FirmJournals.AsNoTracking()
        on new { posting.FirmId, JournalId = posting.JournalId } equals new { journal.FirmId, JournalId = journal.Id }
      join period in db.FirmPeriods.AsNoTracking()
        on new { posting.FirmId, PeriodId = posting.PeriodId } equals new { period.FirmId, PeriodId = period.Id }
      join account in db.FirmAccounts.AsNoTracking()
        on new { line.FirmId, AccountId = line.FirmAccountId } equals new { account.FirmId, AccountId = account.Id }
      where line.FirmId == actor.FirmId && periodIds.Contains(posting.PeriodId) &&
        !openingPeriodIds.Contains(posting.PeriodId) &&
        journal.PostingPurpose != LedgerStates.YearEndClosingPurpose &&
        journal.PostingPurpose != "OPENING_BALANCE" &&
        !closingPostingIds.Contains(posting.ReversalOfPostingId ?? Guid.Empty) &&
        revenueAndExpenseAccountIds.Contains(line.FirmAccountId)
      select new
      {
        LineId = line.Id, PostingId = posting.Id, JournalId = journal.Id, PeriodCode = period.PeriodCode,
        posting.PostedAt, journal.JournalNumber, journal.PostingPurpose, AccountId = account.Id,
        AccountCode = account.Code, AccountName = account.Name, account.AccountType,
        Description = db.FirmJournalLines.AsNoTracking()
          .Where(jline => jline.FirmId == line.FirmId && jline.JournalId == journal.Id &&
            jline.FirmAccountId == line.FirmAccountId && jline.Debit == line.Debit && jline.Credit == line.Credit)
          .OrderBy(jline => jline.Id).Select(jline => jline.Description).FirstOrDefault() ?? journal.PostingPurpose,
        line.Debit, line.Credit
      };
    var totalActivityCount = await activityQuery.CountAsync(ct);
    if (includeAllActivities && totalActivityCount > MaxTrialBalanceExportActivityRows)
      return CommandResult<FirmTrialBalanceView>.Fail("ledger.export.limit",
        $"This report has more than {MaxTrialBalanceExportActivityRows} profit-and-loss journal lines. Narrow the period range before exporting.");
    var activityOffset = (activityPage - 1) * TrialBalanceActivityPageSize;
    var activityPageRows = await activityQuery.OrderBy(x => x.PeriodCode).ThenBy(x => x.PostedAt)
      .ThenBy(x => x.JournalNumber).ThenBy(x => x.AccountId).ThenBy(x => x.LineId)
      .Skip(includeAllActivities ? 0 : activityOffset)
      .Take(includeAllActivities ? MaxTrialBalanceExportActivityRows : TrialBalanceActivityPageSize + 1)
      .ToListAsync(ct);
    var hasMoreActivity = !includeAllActivities && totalActivityCount > activityOffset + TrialBalanceActivityPageSize;
    if (hasMoreActivity) activityPageRows.RemoveAt(activityPageRows.Count - 1);
    var profitLossActivity = activityPageRows.Select(x => new FirmProfitLossActivity(
      x.LineId, x.PostingId, x.JournalId, x.PeriodCode, x.PostedAt, x.JournalNumber, x.PostingPurpose,
      x.AccountId, x.AccountCode, x.AccountName, x.Description,
      x.AccountType == LedgerStates.AccountRevenue ? MoneyPolicy.Normalize(x.Credit - x.Debit) : 0m,
      x.AccountType == LedgerStates.AccountExpense ? MoneyPolicy.Normalize(x.Debit - x.Credit) : 0m)).ToList();
    // This canonical envelope is deliberately independent of the requested activity page. All source
    // collections are ordered explicitly before JSON serialization, and the hash is taken inside this
    // repeatable-read transaction so the first page, later pages and export compare the same snapshot.
    var reportIdentity = Hashing.Sha256Hex(System.Text.Json.JsonSerializer.Serialize(new
    {
      FirmId = actor.FirmId,
      IdentityVersion = "firm-trial-balance/v1",
      FromPeriod = fromPeriod,
      ToPeriod = toPeriod,
      Currency = currency,
      Periods = periods.OrderBy(x => x.PeriodCode, StringComparer.Ordinal).ThenBy(x => x.Id)
        .Select(x => new { x.Id, x.PeriodCode }).ToArray(),
      Accounts = accounts.OrderBy(x => x.Code, StringComparer.Ordinal).ThenBy(x => x.Id)
        .Select(x => new { x.Id, x.Code, x.Name, x.AccountType, x.NormalSide, x.PostingAllowed }).ToArray(),
      PostedLines = lines.OrderBy(x => x.PeriodId).ThenBy(x => x.PostingId).ThenBy(x => x.LineId)
        .Select(x => new { x.LineId, x.FirmAccountId, x.Debit, x.Credit, x.PostingId, x.ReversalOfPostingId,
          x.PeriodId, x.Currency, x.JournalId, x.PostedAt, x.JournalNumber, x.PostingPurpose }).ToArray(),
      JournalLines = journalLines.Select(x => new { x.Id, x.JournalId, x.FirmAccountId, x.Debit, x.Credit, x.Description }).ToArray()
    }));
    var assets = Balance(LedgerStates.AccountAsset, true);
    var liabilities = Balance(LedgerStates.AccountLiability, false);
    var equity = Balance(LedgerStates.AccountEquity, false);
    var profit = revenue - expenses;
    var cumulativeProfit = Balance(LedgerStates.AccountRevenue, false) - Balance(LedgerStates.AccountExpense, true);
    // End the snapshot before checking access again, so a concurrent withdrawal is not hidden by repeatable read.
    await snapshot.CommitAsync(ct);
    var finalAuth = await AuthorizeAsync(db, actor, [.. Preparers, .. Reviewers, "Partner", "Administrator"], ct);
    if (!finalAuth.Succeeded)
      return CommandResult<FirmTrialBalanceView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (expectedReportIdentity is not null && !string.Equals(expectedReportIdentity, reportIdentity, StringComparison.Ordinal))
      return CommandResult<FirmTrialBalanceView>.Fail(ErrorCodes.GenerationStale,
        "The firm-books report changed. Reload the first page before continuing or exporting it.");
    return CommandResult<FirmTrialBalanceView>.Ok(new(fromPeriod, toPeriod, rows, totalDebit, totalCredit, totalDebit == totalCredit,
      revenue, expenses, profit, cumulativeProfit, assets, liabilities, equity, assets == liabilities + equity + cumulativeProfit,
      currency, activityPage, TrialBalanceActivityPageSize, totalActivityCount, hasMoreActivity, profitLossActivity, monthlyProfitLoss, reportIdentity));
  }

  public static async Task<CommandResult<FirmTrialBalanceCsvExport>> ExportTrialBalanceAsync(IClientAccountingDbContext db,
    ActorContext actor, string fromPeriod, string toPeriod, CancellationToken ct = default, string? expectedReportIdentity = null)
  {
    var report = await TrialBalanceAsync(db, actor, fromPeriod, toPeriod, ct, activityPage: 1, includeAllActivities: true,
      expectedReportIdentity: expectedReportIdentity);
    if (!report.Succeeded || report.Value is null)
      return CommandResult<FirmTrialBalanceCsvExport>.Fail(report.ErrorCode!, report.Message!);
    var value = report.Value;
    static string SafeText(string? text)
    {
      var value = text ?? string.Empty;
      var firstMeaningful = value.FirstOrDefault(character => !char.IsWhiteSpace(character));
      if (firstMeaningful is '=' or '+' or '-' or '@' or '\t' or '\r' or '\n')
        value = "'" + value;
      return value;
    }
    static string Cell(string? text) => "\"" + (text ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    static string Amount(decimal amount) => amount.ToString(CultureInfo.InvariantCulture);
    var csv = new System.Text.StringBuilder();
    csv.AppendLine("record_type,from_period,to_period,currency,account_code,account_name,opening_debit,opening_credit,movement_debit,movement_credit,closing_debit,closing_credit,period,posted_at,journal_number,posting_purpose,description,revenue_activity,expense_activity,profit");
    csv.AppendLine(string.Join(",", new[] { "PROFIT_LOSS_SUMMARY", value.FromPeriod, value.ToPeriod, SafeText(value.Currency), "", "", "", "", "", "", "", "", "", "", "", "", "", Amount(value.Revenue), Amount(value.Expenses), Amount(value.Profit) }.Select(Cell)));
    foreach (var month in value.MonthlyProfitLoss)
      csv.AppendLine(string.Join(",", new[] { "PROFIT_LOSS_MONTH", value.FromPeriod, value.ToPeriod, SafeText(value.Currency), "", "", "", "", "", "", "", "",
        month.PeriodCode, "", "", "", "", Amount(month.Revenue), Amount(month.Expenses), Amount(month.Profit) }.Select(Cell)));
    foreach (var row in value.Rows)
      csv.AppendLine(string.Join(",", new[] { "TRIAL_BALANCE", value.FromPeriod, value.ToPeriod, SafeText(value.Currency), SafeText(row.Code), SafeText(row.Name),
        Amount(row.OpeningDebit), Amount(row.OpeningCredit), Amount(row.MovementDebit), Amount(row.MovementCredit), Amount(row.ClosingDebit),
        Amount(row.ClosingCredit), "", "", "", "", "", "", "", "" }.Select(Cell)));
    foreach (var activityLine in value.ProfitLossActivity)
      csv.AppendLine(string.Join(",", new[] { "PROFIT_LOSS_ACTIVITY", value.FromPeriod, value.ToPeriod, SafeText(value.Currency), SafeText(activityLine.AccountCode),
        SafeText(activityLine.AccountName), "", "", "", "", "", "", activityLine.PeriodCode, activityLine.PostedAt.ToString("O", CultureInfo.InvariantCulture),
        SafeText(activityLine.JournalNumber), SafeText(activityLine.PostingPurpose), SafeText(activityLine.Description), Amount(activityLine.RevenueActivity),
        Amount(activityLine.ExpenseActivity), "" }.Select(Cell)));
    return CommandResult<FirmTrialBalanceCsvExport>.Ok(new(
      $"firm-trial-balance-{value.FromPeriod}-{value.ToPeriod}.csv", value.Currency ?? string.Empty, csv.ToString(), value.ReportIdentity));
  }

  private static bool IsPeriod(string value) => DateOnly.TryParseExact((value ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);
}
