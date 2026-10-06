using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
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
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "That code already exists; add a new version instead.");
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
    if (!(await AuthorizeAsync(db, actor, StaffRoles, ct)).Succeeded) return [];
    return (await HasAnyRoleAsync(db, actor, SeniorAudience, ct))
      ? [TechnicalLibraryAudiences.AllStaff, TechnicalLibraryAudiences.PartnersAndManagers] : [TechnicalLibraryAudiences.AllStaff];
  }

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

public sealed record EngagementEconomicsRow(Guid EngagementId, string Label, string Currency, int BudgetMinutes, decimal BudgetValue, int ActualMinutes,
  decimal StandardValue, decimal ActualCost, decimal Billed, decimal Collected, decimal? RealizationPercent, decimal? CollectionPercent,
  decimal Profit, decimal? MarginPercent, int BudgetVarianceMinutes, bool CostComplete)
{
  public decimal? ContractedFee { get; init; }
  public string? ContractCurrency { get; init; }
  public decimal? LifetimeStandardValue { get; init; }
  public decimal? ContractedFeeLessStandardValue { get; init; }
}
public sealed record DepartmentUtilizationRow(string Department, int CapacityMinutes, int ChargeableMinutes, decimal? UtilizationPercent, decimal? TargetPercent);
public sealed record MilestonePerformance(int Due, int CompletedOnTime, int CompletedLate, int Overdue, decimal? OnTimePercent);
public sealed record PracticeAnalyticsView(DateOnly From, DateOnly To, IReadOnlyList<EngagementEconomicsRow> Engagements, IReadOnlyList<DepartmentUtilizationRow> Departments,
  MilestonePerformance Milestones, IReadOnlyList<string> Definitions);

/// <summary>
/// Defined, reconcilable practice measures from approved records only. Standard value is approved time at its captured
/// charge-out rate; cost is approved time at the staff member's recorded internal cost rate (never the charge-out
/// rate); billed is posted invoice lines traced to the engagement's time or fee milestones; collected is receipts
/// allocated to those invoices. A measure without its inputs is reported as unavailable rather than zero.
/// </summary>
public static class PracticeAnalyticsQuery
{
  public static readonly string[] Definitions =
  [
    "Contract contribution = agreed contract fee − lifetime approved-time standard value, in the contract currency. This is separate from period billed-minus-actual-cost profit; missing rates or mixed currencies make it unavailable.",
    "Standard value = approved minutes × captured charge-out rate ÷ 60.",
    "Actual cost = approved minutes × staff cost rate effective on the work date ÷ 60 (charge-out rates are not costs).",
    "Billed = posted invoice lines sourced from the engagement's approved time or fee milestones (credit notes deducted).",
    "Collected = receipt allocations to those invoices, pro rata to the engagement's share of each invoice.",
    "Realization = billed ÷ standard value; collection = collected ÷ billed; margin = (billed − actual cost) ÷ billed.",
    "Utilization = approved billable minutes ÷ (weekly capacity × weeks − recorded unavailability); on-time = tasks completed by their due date ÷ tasks due in the period."
  ];

  public static async Task<CommandResult<PracticeAnalyticsView>> GetAsync(IClientAccountingDbContext db, ActorContext actor, DateOnly from, DateOnly to, CancellationToken ct = default)
  {
    if (to < from || to.DayNumber - from.DayNumber > 800) return CommandResult<PracticeAnalyticsView>.Fail("analytics.invalid", "Choose a period of up to about two years.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Partner", "Administrator", "FinanceManager"],
      InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded) return CommandResult<PracticeAnalyticsView>.Fail(auth.ErrorCode!, auth.Message!);
    var f = actor.FirmId;
    var time = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == f && x.Status == PracticeTimeStates.TimeApproved && x.WorkDate >= from && x.WorkDate <= to).ToListAsync(ct);
    var costRates = await db.StaffCostRates.AsNoTracking().Where(x => x.FirmId == f).ToListAsync(ct);
    decimal? Cost(TimeEntry t)
    {
      var rate = costRates.Where(r => r.UserId == t.UserId && r.EffectiveFrom <= t.WorkDate).OrderByDescending(r => r.EffectiveFrom).FirstOrDefault();
      return rate is null ? null : t.DurationMinutes * rate.HourlyCost / 60m;
    }
    var engagementIds = time.Where(x => x.EngagementId.HasValue).Select(x => x.EngagementId!.Value).Distinct().ToList();
    // Billing traced to engagements through invoice line sources.
    var lines = await db.InvoiceLines.AsNoTracking().Join(db.Invoices.AsNoTracking(), l => l.InvoiceId, i => i.Id, (l, i) => new { l, i })
      .Where(x => x.i.FirmId == f && x.i.Status == BillingStates.InvoicePosted && x.i.PostedAt != null).ToListAsync(ct);
    var timeSources = lines.Where(x => x.l.SourceKind == "TIME" && x.l.SourceId.HasValue).Select(x => x.l.SourceId!.Value).ToArray();
    var timeEngagement = await db.TimeEntries.AsNoTracking().Where(x => timeSources.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.EngagementId, ct);
    var milestoneSources = lines.Where(x => x.l.SourceKind == FeeAgreementService.MilestoneSourceKind && x.l.SourceId.HasValue).Select(x => x.l.SourceId!.Value).ToArray();
    var milestoneEngagement = await db.FeeMilestones.AsNoTracking().Where(x => milestoneSources.Contains(x.Id))
      .Join(db.EngagementFeeAgreements.AsNoTracking(), m => m.AgreementId, a => a.Id, (m, a) => new { m.Id, a.EngagementId }).ToDictionaryAsync(x => x.Id, x => x.EngagementId, ct);
    Guid? LineEngagement(InvoiceLine l) => l.SourceKind == "TIME" && l.SourceId is { } t ? timeEngagement.GetValueOrDefault(t)
      : l.SourceKind == FeeAgreementService.MilestoneSourceKind && l.SourceId is { } m ? milestoneEngagement.GetValueOrDefault(m) : null;
    var billedLines = lines.Select(x => new { x.l, x.i, Engagement = LineEngagement(x.l) }).Where(x => x.Engagement.HasValue).ToList();
    engagementIds = engagementIds.Union(billedLines.Select(x => x.Engagement!.Value)).Distinct().ToList();
    var agreements = await db.EngagementFeeAgreements.AsNoTracking().Where(x => x.FirmId == f && x.EngagementId != null).ToListAsync(ct);
    engagementIds = engagementIds.Union(agreements.Select(x => x.EngagementId!.Value)).ToList();
    var lifetimeTime = await db.TimeEntries.AsNoTracking().Where(x => x.FirmId == f && x.Status == PracticeTimeStates.TimeApproved &&
      x.EngagementId != null && engagementIds.Contains(x.EngagementId.Value)).Select(x => new { x.EngagementId, x.DurationMinutes, x.RatePerHour, x.Currency }).ToListAsync(ct);
    var invoiceIds = billedLines.Select(x => x.i.Id).Distinct().ToArray();
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
    var labels = await db.Engagements.AsNoTracking().Where(x => engagementIds.Contains(x.Id))
      .Join(db.PracticeClients.AsNoTracking(), e => e.PracticeClientId, c => c.Id, (e, c) => new { e.Id, Label = (c.CommercialName ?? c.LegalName) + " · " + e.ServiceRoute + " " + e.PeriodEnd })
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
      var billed = 0m; var collected = 0m;
      foreach (var invoice in billedLines.Where(x => x.Engagement == id).GroupBy(x => x.i.Id))
      {
        var share = invoice.Sum(x => x.l.LineTotal);
        var total = invoice.First().i.Total;
        var ratio = total == 0 ? 0 : share / total;
        billed += share - credits.GetValueOrDefault(invoice.Key) * ratio;
        collected += Math.Max(0m, allocations.GetValueOrDefault(invoice.Key) - reversedAllocations.GetValueOrDefault(invoice.Key)) * ratio;
      }
      var standard = MoneyPolicy.Normalize(mine.Sum(t => t.RatePerHour.HasValue ? t.DurationMinutes * t.RatePerHour.Value / 60m : 0m));
      var cost = MoneyPolicy.Normalize(costs.Sum(c => c ?? 0m));
      billed = MoneyPolicy.Normalize(billed); collected = MoneyPolicy.Normalize(collected);
      var costComplete = costs.All(c => c.HasValue);
      var contracts = agreements.Where(x => x.EngagementId == id).ToArray();
      var contract = contracts.Length == 1 ? contracts[0] : null; // Ambiguous contract revisions are unavailable, never silently summed.
      var contribution = contract == null ? null : ContractContributionCalculator.Compute(contract.AgreedFee, contract.Currency,
        lifetimeTime.Where(x => x.EngagementId == id).Select(x => new ContractTimeValue(x.DurationMinutes, x.RatePerHour, x.Currency)).ToArray());
      return new EngagementEconomicsRow(id, labels.GetValueOrDefault(id, "Engagement"), budget?.Currency ?? mine.FirstOrDefault(t => t.Currency != null)?.Currency ?? "",
        bl.Sum(x => x.ForecastMinutes), MoneyPolicy.Normalize(bl.Sum(x => x.ForecastCost)), mine.Sum(t => t.DurationMinutes), standard, cost, billed, collected,
        standard > 0 ? Math.Round(billed / standard * 100m, 1) : null, billed > 0 ? Math.Round(collected / billed * 100m, 1) : null,
        billed - cost, billed > 0 && costComplete ? Math.Round((billed - cost) / billed * 100m, 1) : null, bl.Sum(x => x.ForecastMinutes) - mine.Sum(t => t.DurationMinutes), costComplete)
      { ContractedFee = contract?.AgreedFee, ContractCurrency = contract?.Currency,
        LifetimeStandardValue = contribution?.LifetimeStandardValue, ContractedFeeLessStandardValue = contribution?.FeeLessStandardValue };
    }).OrderBy(x => x.Label).ToList();

    // Department utilization over the same period.
    var profiles = await db.StaffProfiles.AsNoTracking().Where(x => x.FirmId == f).ToListAsync(ct);
    var availability = await db.StaffAvailabilities.AsNoTracking().Where(x => x.FirmId == f && x.EndDate >= from && x.StartDate <= to).ToListAsync(ct);
    var departments = profiles.GroupBy(p => p.Department).OrderBy(g => g.Key).Select(g =>
    {
      var capacity = 0; var chargeable = 0;
      foreach (var profile in g)
      {
        var weekly = profile.WeeklyCapacityMinutes;
        var perDay = weekly / 5;
        for (var d = from; d <= to; d = d.AddDays(1))
        {
          if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
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
    var milestones = new MilestonePerformance(tasks.Count, onTime, late, overdue, tasks.Count > 0 ? Math.Round(onTime * 100m / tasks.Count, 1) : null);
    return CommandResult<PracticeAnalyticsView>.Ok(new(from, to, rows, departments, milestones, Definitions));
  }

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
  IReadOnlyList<FirmProfitLossActivity> ProfitLossActivity);
public sealed record FirmTrialBalanceCsvExport(string FileName, string Currency, string Csv);

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

  public static async Task<IReadOnlyList<FirmExpense>> ListAsync(IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default) =>
    (await AuthorizeAsync(db, actor, [.. Preparers, .. Reviewers], ct)).Succeeded
      ? await db.FirmExpenses.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.CreatedAt).Take(200)
        .Select(x => new FirmExpense { Id = x.Id, FirmId = x.FirmId, ExpenseDate = x.ExpenseDate, Category = x.Category, Payee = x.Payee, Description = x.Description, Amount = x.Amount,
          Currency = x.Currency, EvidenceFileName = x.EvidenceFileName, EvidenceSha256 = x.EvidenceSha256, Status = x.Status, PreparedByUserId = x.PreparedByUserId, ReviewedByUserId = x.ReviewedByUserId,
          ReviewComment = x.ReviewComment, PostingId = x.PostingId, CreatedAt = x.CreatedAt }).ToListAsync(ct)
      : [];

  /// <summary>
  /// The trial balance shows cumulative opening, movement and closing balances. Revenue, expenses and profit are
  /// posted activity within the selected periods; YEAR_END_CLOSE postings remain in the trial balance but are
  /// excluded from operating activity. Cumulative P&amp;L is retained separately for the closing-position check.
  /// </summary>
  public const int TrialBalanceActivityPageSize = 100;
  public const int MaxTrialBalanceExportActivityRows = 5000;

  public static async Task<CommandResult<FirmTrialBalanceView>> TrialBalanceAsync(IClientAccountingDbContext db, ActorContext actor,
    string fromPeriod, string toPeriod, CancellationToken ct = default, int activityPage = 1, bool includeAllActivities = false)
  {
    if (!IsPeriod(fromPeriod) || !IsPeriod(toPeriod) || string.CompareOrdinal(fromPeriod, toPeriod) > 0 || activityPage is < 1 or > 100_000)
      return CommandResult<FirmTrialBalanceView>.Fail("ledger.invalid", "Choose a valid YYYY-MM period range and activity page.");
    var auth = await AuthorizeAsync(db, actor, [.. Preparers, .. Reviewers, "Partner", "Administrator"], ct);
    if (!auth.Succeeded) return CommandResult<FirmTrialBalanceView>.Fail(auth.ErrorCode!, auth.Message!);
    var periods = await db.FirmPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && string.Compare(x.PeriodCode, toPeriod) <= 0).ToListAsync(ct);
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
        line.FirmAccountId, line.Debit, line.Credit, PostingId = posting.Id,
        posting.ReversalOfPostingId, posting.PeriodId, posting.Currency, journal.PostingPurpose
      }).ToListAsync(ct);
    var accounts = await db.FirmAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderBy(x => x.Code).ToListAsync(ct);
    var currencies = lines.Select(x => x.Currency).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    if (currencies.Length > 1)
      return CommandResult<FirmTrialBalanceView>.Fail("ledger.currency-mixed",
        "This report contains posted amounts in more than one currency. Filter or translate each currency separately before reconciling it.");
    var currency = currencies.SingleOrDefault() ?? await db.FirmFinanceProfiles.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Approved)
      .OrderByDescending(x => x.ApprovedAt).Select(x => x.FunctionalCurrency).FirstOrDefaultAsync(ct);
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
    var operatingLines = lines.Where(x => !openingIds.Contains(x.PeriodId) &&
      !string.Equals(x.PostingPurpose, LedgerStates.YearEndClosingPurpose, StringComparison.OrdinalIgnoreCase) &&
      !string.Equals(x.PostingPurpose, "OPENING_BALANCE", StringComparison.OrdinalIgnoreCase) &&
      (x.ReversalOfPostingId is null || !yearEndClosingPostingIds.Contains(x.ReversalOfPostingId.Value))).ToList();
    var revenueAccountIds = accounts.Where(x => x.AccountType == LedgerStates.AccountRevenue).Select(x => x.Id).ToHashSet();
    var expenseAccountIds = accounts.Where(x => x.AccountType == LedgerStates.AccountExpense).Select(x => x.Id).ToHashSet();
    var revenue = MoneyPolicy.Normalize(operatingLines.Where(x => revenueAccountIds.Contains(x.FirmAccountId)).Sum(x => x.Credit - x.Debit));
    var expenses = MoneyPolicy.Normalize(operatingLines.Where(x => expenseAccountIds.Contains(x.FirmAccountId)).Sum(x => x.Debit - x.Credit));
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
    var assets = Balance(LedgerStates.AccountAsset, true);
    var liabilities = Balance(LedgerStates.AccountLiability, false);
    var equity = Balance(LedgerStates.AccountEquity, false);
    var profit = revenue - expenses;
    var cumulativeProfit = Balance(LedgerStates.AccountRevenue, false) - Balance(LedgerStates.AccountExpense, true);
    var finalAuth = await AuthorizeAsync(db, actor, [.. Preparers, .. Reviewers, "Partner", "Administrator"], ct);
    if (!finalAuth.Succeeded)
      return CommandResult<FirmTrialBalanceView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<FirmTrialBalanceView>.Ok(new(fromPeriod, toPeriod, rows, totalDebit, totalCredit, totalDebit == totalCredit,
      revenue, expenses, profit, cumulativeProfit, assets, liabilities, equity, assets == liabilities + equity + cumulativeProfit,
      currency, activityPage, TrialBalanceActivityPageSize, totalActivityCount, hasMoreActivity, profitLossActivity));
  }

  public static async Task<CommandResult<FirmTrialBalanceCsvExport>> ExportTrialBalanceAsync(IClientAccountingDbContext db,
    ActorContext actor, string fromPeriod, string toPeriod, CancellationToken ct = default)
  {
    var report = await TrialBalanceAsync(db, actor, fromPeriod, toPeriod, ct, activityPage: 1, includeAllActivities: true);
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
      $"firm-trial-balance-{value.FromPeriod}-{value.ToPeriod}.csv", value.Currency ?? string.Empty, csv.ToString()));
  }

  private static bool IsPeriod(string value) => DateOnly.TryParseExact((value ?? "") + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);
}
