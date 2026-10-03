using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record PlanJournalSelection(Guid JournalId, long Revision);
public sealed record PlanCommandRequest(Guid RequestId, string Action, string ReviewBasis,
  string Reason, string EvidenceReference, IReadOnlyList<PlanJournalSelection> Journals, bool Reviewed);
public sealed record PlanJournalOption(Guid JournalId, string JournalNumber, long Revision,
  string Purpose, string Layer, string ReflectionState, bool CanInclude, string? Blocker);
public sealed record PlanCreationOptions(JournalCreationContext Source, string ReviewBasis,
  IReadOnlyList<PlanJournalOption> Journals, int Page, bool HasMore);
public sealed record PlanCommandPreview(string Action, string ReviewBasis, string RequestHash,
  bool CanProceed, string? Blocker, IReadOnlyList<PlanJournalOption> Journals,
  decimal? Debits, decimal? Credits, int? AppliedCount, string? ResultHash);
public sealed record PlanCommandReceipt(Guid Id, Guid RequestId, string RequestHash, Guid PlanId,
  Guid DatasetId, string Action, Guid ActorId, string Reason, string EvidenceReference,
  string Status, string? ResultHash, decimal? Debits, decimal? Credits, int? AppliedCount,
  DateTimeOffset CreatedAt);
public sealed record PlanReceiptLookup(bool Found, PlanCommandReceipt? Receipt);
public sealed record PlanHistoryPage(IReadOnlyList<PlanCommandReceipt> Items, int Page, bool HasMore);

public static partial class AdjustmentPlanWorkspace
{
  public const int MaximumCommandMembership = 100;
  private static readonly string[] PrepareRoles = ["AccountingPreparer", "Staff", "Partner", "Manager"];
  private static Task<CommandResult> WriteAuth(IClientAccountingDbContext db, ActorContext actor,
    Guid client, Guid engagement, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, client, engagement, PrepareRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
  private static bool ValidCommand(PlanCommandRequest? r) => r is not null && r.RequestId != Guid.Empty &&
    r.Action is "CREATE" or "FINALIZE" && SourceAcceptanceWorkspace.ValidHash(r.ReviewBasis) &&
    r.Reason is { Length: > 0 and <= 4000 } && r.Reason.Trim().Length > 0 &&
    r.EvidenceReference is { Length: > 0 and <= 2000 } && r.EvidenceReference.Trim().Length > 0 &&
    r.Journals is { Count: <= MaximumCommandMembership } &&
    (r.Action == "CREATE" || r.Journals.Count == 0) &&
    r.Journals.All(j => j is not null && j.JournalId != Guid.Empty && j.Revision >= 1) &&
    r.Journals.Select(j => j.JournalId).Distinct().Count() == r.Journals.Count;
  private static string CommandHash(ActorContext a, Guid target, PlanCommandRequest r) => Hashing.Sha256Hex(JsonSerializer.Serialize(new {
    a.FirmId, a.UserId, Target = target, r.RequestId, r.Action, r.ReviewBasis, r.Reason, r.EvidenceReference,
    Journals = r.Journals.OrderBy(j => j.JournalId).ToArray()
  }));

  private sealed record CreationSnapshot(JournalCreationContext Source, IReadOnlyList<PlanJournalOption> Rows, string Basis);
  private static async Task<CommandResult<CreationSnapshot>> ReadPlanCreationAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid sourceId, CancellationToken ct)
  {
    var source = await AdjustmentJournalWorkspace.GetCreationAsync(db, actor, sourceId, ct);
    if (!source.Succeeded) return Fail<CreationSnapshot>(source.ErrorCode!, source.Message!);
    var s = source.Value!;
    var journals = await db.AdjustmentJournals.AsNoTracking().Where(j => j.FirmId == actor.FirmId &&
      j.ClientId == s.ClientId && j.EngagementId == s.EngagementId && j.PeriodId == s.PeriodId &&
      j.BookId == s.BookId && j.Currency == s.Currency && j.Basis == s.Basis && j.Status == "Posted" &&
      j.Purpose != AdjustmentJournalPurposes.GroupOnlyElimination)
      .OrderBy(j => j.JournalNumber).ThenBy(j => j.Revision).ThenBy(j => j.Id)
      .Take(AdjustmentEligibilityQuery.MaximumMembership + 1).ToListAsync(ct);
    if (journals.Count > AdjustmentEligibilityQuery.MaximumMembership)
      return Fail<CreationSnapshot>(ErrorCodes.GateBlocked, "This source context exceeds the bounded interactive journal catalogue.");
    var numbers = journals.Select(j => j.JournalNumber).Distinct().ToArray();
    var reflections = await db.JournalSourceReconciliations.AsNoTracking().Where(r => r.FirmId == actor.FirmId &&
      r.ClientId == s.ClientId && r.EngagementId == s.EngagementId && r.BaseDatasetId == sourceId && numbers.Contains(r.LogicalJournalNumber))
      .OrderBy(r => r.LogicalJournalNumber).ThenBy(r => r.JournalRevision).ThenBy(r => r.Id).ToListAsync(ct);
    var rows = journals.Select(j => {
      var reflection = reflections.SingleOrDefault(r => r.LogicalJournalNumber == j.JournalNumber && r.JournalRevision == j.Revision)?.State ?? ReflectionStates.Unknown;
      var blocker = journals.Count(x => x.JournalNumber == j.JournalNumber && x.Revision == j.Revision) != 1 ? "The logical journal revision is ambiguous." :
        reflection is not (ReflectionStates.NotReflected or ReflectionStates.Reflected or ReflectionStates.NotApplicable) ? "An authorized reviewer must resolve unknown or partial source reflection before planning." : null;
      return new PlanJournalOption(j.Id, j.JournalNumber, j.Revision, j.Purpose,
        "REPORTING", reflection, blocker is null, blocker);
    }).ToArray();
    var final = await AdjustmentJournalWorkspace.GetCreationAsync(db, actor, sourceId, ct);
    if (!final.Succeeded) return Fail<CreationSnapshot>(final.ErrorCode!, final.Message!);
    if (s.ReviewBasis != final.Value!.ReviewBasis) return Fail<CreationSnapshot>(ErrorCodes.StaleRevision, "The source context changed. Refresh before planning.");
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { s.ReviewBasis, journals, reflections, rows }));
    return CommandResult<CreationSnapshot>.Ok(new(s, rows, basis));
  }
  private static async Task<CommandResult<CreationSnapshot>> PlanCreationAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid sourceId, CancellationToken ct)
  {
    var first = await ReadPlanCreationAsync(db, actor, sourceId, ct); if (!first.Succeeded) return first;
    var final = await ReadPlanCreationAsync(db, actor, sourceId, ct); if (!final.Succeeded) return final;
    return first.Value!.Basis == final.Value!.Basis ? final : Fail<CreationSnapshot>(ErrorCodes.StaleRevision, "Journal membership or reflection changed. Refresh the catalogue.");
  }
  public static async Task<CommandResult<PlanCreationOptions>> GetCreationOptionsAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid sourceId, int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or > 40) return Fail<PlanCreationOptions>(ErrorCodes.GateBlocked, "Use a valid bounded journal page.");
    var r = await PlanCreationAsync(db, actor, sourceId, ct);
    return r.Succeeded ? CommandResult<PlanCreationOptions>.Ok(new(r.Value!.Source, r.Value.Basis,
      r.Value.Rows.Skip(page * 25).Take(25).ToArray(), page, r.Value.Rows.Count > (page + 1) * 25)) :
      Fail<PlanCreationOptions>(r.ErrorCode!, r.Message!);
  }
  private sealed record Calculation(decimal Debits, decimal Credits, int AppliedCount, string Hash);
  private static async Task<CommandResult<Calculation>> CalculatePlanAsync(IClientAccountingDbContext db, ActorContext actor,
    AdjustmentPlanReview view, CancellationToken ct)
  {
    var report = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, actor, view.Id, ct);
    if (!report.Succeeded) return Fail<Calculation>(report.ErrorCode!, report.Message!);
    var membership = report.Value!.Journals;
    if (membership.Count > MaximumCommandMembership) return Fail<Calculation>(ErrorCodes.GateBlocked, "The plan exceeds the interactive calculation limit.");
    if (report.Value.BlockedCount != 0 || membership.Any(r => r.JournalId is null) || membership.GroupBy(r => r.JournalId).Any(g => g.Count() != 1))
      return Fail<Calculation>(ErrorCodes.GateBlocked, "Resolve blocked, ambiguous or repeated journal membership before calculating.");
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(r => r.DatasetId == view.DatasetId)
      .OrderBy(r => r.Id).Take(20_001).ToListAsync(ct);
    if (rows.Count is 0 or > 20_000 || rows.GroupBy(r => r.AccountCode, StringComparer.Ordinal).Any(g => g.Count() != 1))
      return Fail<Calculation>(ErrorCodes.GateBlocked, "The exact source has missing, duplicate or oversized account balances.");
    var eligible = membership.Where(r => r.Classification == AdjustmentEligibilityQuery.Eligible).ToArray();
    var ids = eligible.Select(r => r.JournalId!.Value).ToArray();
    var journals = await db.AdjustmentJournals.AsNoTracking().Where(j => ids.Contains(j.Id) && j.FirmId == actor.FirmId &&
      j.ClientId == view.ClientId && j.EngagementId == view.EngagementId && j.PeriodId == view.PeriodId &&
      j.BookId == view.BookId && j.Currency == view.Currency && j.Basis == view.Basis && j.Status == "Posted" &&
      j.Purpose != AdjustmentJournalPurposes.GroupOnlyElimination).ToListAsync(ct);
    if (journals.Count != eligible.Length) return Fail<Calculation>(ErrorCodes.GateBlocked, "A journal no longer matches the exact posted reporting context.");
    var lines = await db.AdjustmentLines.AsNoTracking().Where(l => ids.Contains(l.JournalId)).OrderBy(l => l.Id).Take(20_001).ToListAsync(ct);
    if (lines.Count > 20_000) return Fail<Calculation>(ErrorCodes.GateBlocked, "Journal lines exceed the interactive calculation limit.");
    IReadOnlyDictionary<string, decimal> balances = rows.ToDictionary(r => r.AccountCode, r => r.Amount, StringComparer.Ordinal);
    foreach (var j in eligible)
    {
      var selected = lines.Where(l => l.JournalId == j.JournalId).ToArray();
      var error = AdjustmentJournalService.CheckLines(selected.Select(l => (l.AccountCode, l.Debit, l.Credit)).ToArray());
      if (error is not null || selected.Any(l => !balances.ContainsKey(l.AccountCode)))
        return Fail<Calculation>(ErrorCodes.GateBlocked, error ?? "A journal account is absent from this exact source.");
      balances = TrialBalanceCalculator.ApplyJournal(balances, selected.Select(l => (l.AccountCode, l.Debit, l.Credit)));
    }
    var debits = MoneyPolicy.Normalize(balances.Values.Where(v => v >= 0).Sum());
    var credits = MoneyPolicy.Normalize(-balances.Values.Where(v => v < 0).Sum());
    if (debits != credits) return Fail<Calculation>(ErrorCodes.GateBlocked, "The calculated adjusted balances are unbalanced.");
    var hash = Hashing.Sha256Hex(string.Join('\n', balances.OrderBy(k => k.Key, StringComparer.Ordinal)
      .Select(k => k.Key + "|" + k.Value.ToString("0.000000", CultureInfo.InvariantCulture))));
    return CommandResult<Calculation>.Ok(new(debits, credits, eligible.Length, hash));
  }
  public static async Task<CommandResult<PlanCommandPreview>> PreviewCommandAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid target, PlanCommandRequest? request, CancellationToken ct = default)
  {
    if (!ValidCommand(request)) return Fail<PlanCommandPreview>(ErrorCodes.Accounting.PlanRejected, "Provide exact reviewed context, bounded journal revisions, rationale and evidence.");
    var r = request!;
    if (r.Action == "CREATE")
    {
      var current = await PlanCreationAsync(db, actor, target, ct); if (!current.Succeeded) return Fail<PlanCommandPreview>(current.ErrorCode!, current.Message!);
      var c = current.Value!;
      if (c.Basis != r.ReviewBasis) return Fail<PlanCommandPreview>(ErrorCodes.StaleRevision, "The source or journal catalogue changed. Refresh and preview again.");
      var chosen = r.Journals.Select(s => c.Rows.SingleOrDefault(j => j.JournalId == s.JournalId && j.Revision == s.Revision)).ToArray();
      if (chosen.Any(j => j is null)) return Fail<PlanCommandPreview>(ErrorCodes.ScopeDenied, "Choose exact authorized posted journal revisions.");
      var blocker = c.Source.Blocker ?? chosen.FirstOrDefault(j => !j!.CanInclude)?.Blocker;
      return CommandResult<PlanCommandPreview>.Ok(new(r.Action, c.Basis, CommandHash(actor, target, r), blocker is null, blocker,
        chosen.Select(j => j!).ToArray(), null, null, null, null));
    }
    var review = await GetAsync(db, actor, target, ct: ct); if (!review.Succeeded) return Fail<PlanCommandPreview>(review.ErrorCode!, review.Message!);
    var v = review.Value!;
    if (v.ReviewBasis != r.ReviewBasis) return Fail<PlanCommandPreview>(ErrorCodes.StaleRevision, "The plan context changed. Refresh and preview again.");
    var authority = await WriteAuth(db, actor, v.ClientId, v.EngagementId, ct);
    var blocked = !authority.Succeeded ? "Current scoped preparer authority is required." :
      v.Status != "Draft" ? "Only a draft plan can be finalized. Retained results require a replacement plan." : v.Blockers.FirstOrDefault();
    if (blocked is not null) return CommandResult<PlanCommandPreview>.Ok(new(r.Action, v.ReviewBasis, CommandHash(actor, target, r), false, blocked, [], null, null, null, null));
    var calculated = await CalculatePlanAsync(db, actor, v, ct); if (!calculated.Succeeded) return Fail<PlanCommandPreview>(calculated.ErrorCode!, calculated.Message!);
    var final = await GetAsync(db, actor, target, ct: ct);
    if (!final.Succeeded) return Fail<PlanCommandPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != v.ReviewBasis) return Fail<PlanCommandPreview>(ErrorCodes.StaleRevision, "The plan changed during calculation preview.");
    var p = calculated.Value!;
    return CommandResult<PlanCommandPreview>.Ok(new(r.Action, v.ReviewBasis, CommandHash(actor, target, r), true, null, [], p.Debits, p.Credits, p.AppliedCount, p.Hash));
  }
  private static PlanCommandReceipt Receipt(AdjustmentPlanAction a) => JsonSerializer.Deserialize<PlanCommandReceipt>(a.AfterJson)
    ?? throw new InvalidOperationException("Retained plan evidence is unavailable.");
  public static async Task<CommandResult<PlanHistoryPage>> HistoryAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid planId, int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000) return Fail<PlanHistoryPage>(ErrorCodes.GateBlocked, "Use a valid history page.");
    var current = await GetAsync(db, actor, planId, ct: ct);
    if (!current.Succeeded) return Fail<PlanHistoryPage>(current.ErrorCode!, current.Message!);
    var v = current.Value!;
    var rows = await db.AdjustmentPlanActions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == v.ClientId && x.EngagementId == v.EngagementId && x.DatasetId == v.DatasetId && x.PlanId == planId)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(page * 25).Take(26).ToListAsync(ct);
    var auth = await Auth(db, actor, v.ClientId, v.EngagementId, ct);
    return auth.Succeeded ? CommandResult<PlanHistoryPage>.Ok(new(rows.Take(25).Select(Receipt).ToArray(), page, rows.Count > 25)) :
      Fail<PlanHistoryPage>(auth.ErrorCode!, auth.Message!);
  }
  public static async Task<CommandResult<PlanReceiptLookup>> LookupCommandAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid target, string action, Guid requestId, string requestHash, CancellationToken ct = default)
  {
    if (action is not ("CREATE" or "FINALIZE") || requestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(requestHash))
      return Fail<PlanReceiptLookup>(ErrorCodes.Accounting.PlanRejected, "Use the exact pending request identity.");
    Guid client, engagement;
    if (action == "CREATE")
    {
      var c = await AdjustmentJournalWorkspace.GetCreationAsync(db, actor, target, ct);
      if (!c.Succeeded) return Fail<PlanReceiptLookup>(c.ErrorCode!, c.Message!); client = c.Value!.ClientId; engagement = c.Value.EngagementId;
    }
    else
    {
      var c = await GetAsync(db, actor, target, ct: ct);
      if (!c.Succeeded) return Fail<PlanReceiptLookup>(c.ErrorCode!, c.Message!); client = c.Value!.ClientId; engagement = c.Value.EngagementId;
    }
    var e = await db.AdjustmentPlanActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (e is not null && (e.Action != action || e.RequestHash != requestHash || e.ClientId != client || e.EngagementId != engagement ||
      (action == "CREATE" ? e.DatasetId != target : e.PlanId != target)))
      return Fail<PlanReceiptLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another reviewed intent.");
    var auth = await Auth(db, actor, client, engagement, ct);
    return auth.Succeeded ? CommandResult<PlanReceiptLookup>.Ok(new(e is not null, e is null ? null : Receipt(e))) :
      Fail<PlanReceiptLookup>(auth.ErrorCode!, auth.Message!);
  }
  public static async Task<CommandResult<PlanCommandReceipt>> ExecuteCommandAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid target, PlanCommandRequest? request, CancellationToken ct = default)
  {
    if (!ValidCommand(request) || !request!.Reviewed) return Fail<PlanCommandReceipt>(ErrorCodes.Accounting.PlanRejected, "Preview and explicitly review this exact plan command.");
    var r = request!; Guid client, engagement, dataset; Guid? period, book;
    if (r.Action == "CREATE")
    {
      var c = await AdjustmentJournalWorkspace.GetCreationAsync(db, actor, target, ct); if (!c.Succeeded) return Fail<PlanCommandReceipt>(c.ErrorCode!, c.Message!);
      client = c.Value!.ClientId; engagement = c.Value.EngagementId; dataset = target; period = c.Value.PeriodId; book = c.Value.BookId;
    }
    else
    {
      var c = await GetAsync(db, actor, target, ct: ct); if (!c.Succeeded) return Fail<PlanCommandReceipt>(c.ErrorCode!, c.Message!);
      client = c.Value!.ClientId; engagement = c.Value.EngagementId; dataset = c.Value.DatasetId; period = c.Value.PeriodId; book = c.Value.BookId;
    }
    var auth = await WriteAuth(db, actor, client, engagement, ct); if (!auth.Succeeded) return Fail<PlanCommandReceipt>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, client, engagement, ct);
    if (!locked.Succeeded) return Fail<PlanCommandReceipt>(locked.ErrorCode!, locked.Message!);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={actor.FirmId} AND id={actor.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    await db.TrialBalanceDatasets.FromSqlInterpolated($"SELECT * FROM trial_balance_datasets WHERE firm_id={actor.FirmId} AND id={dataset} FOR SHARE").AsNoTracking().SingleAsync(ct);
    if (r.Action == "FINALIZE")
    {
      await db.AdjustmentPlans.FromSqlInterpolated($"SELECT * FROM adjustment_plans WHERE firm_id={actor.FirmId} AND id={target} FOR UPDATE").AsNoTracking().SingleAsync(ct);
      await db.AdjustmentPlanLines.FromSqlInterpolated($"SELECT * FROM adjustment_plan_lines WHERE plan_id={target} ORDER BY id LIMIT 101 FOR SHARE").AsNoTracking().ToListAsync(ct);
    }
    var prior = await LookupCommandAsync(db, actor, target, r.Action, r.RequestId, CommandHash(actor, target, r), ct);
    if (!prior.Succeeded) return Fail<PlanCommandReceipt>(prior.ErrorCode!, prior.Message!);
    if (prior.Value!.Found)
    {
      auth = await WriteAuth(db, actor, client, engagement, ct); if (!auth.Succeeded) return Fail<PlanCommandReceipt>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct); return CommandResult<PlanCommandReceipt>.Ok(prior.Value.Receipt!);
    }
    if (period is not { } periodId) return Fail<PlanCommandReceipt>(ErrorCodes.GateBlocked, "Bind the exact reporting period before writing.");
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, client, engagement, periodId, book, ct);
    if (!mutable.Succeeded) return Fail<PlanCommandReceipt>(mutable.ErrorCode!, mutable.Message!);
    Guid[] journalIds;
    if (r.Action == "CREATE") journalIds = r.Journals.Select(j => j.JournalId).ToArray();
    else
    {
      var membership = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, actor, target, ct);
      if (!membership.Succeeded) return Fail<PlanCommandReceipt>(membership.ErrorCode!, membership.Message!);
      if (membership.Value!.Journals.Count > MaximumCommandMembership)
        return Fail<PlanCommandReceipt>(ErrorCodes.GateBlocked, "The plan exceeds the interactive calculation limit.");
      journalIds = membership.Value.Journals.Where(j => j.JournalId.HasValue).Select(j => j.JournalId!.Value).Distinct().ToArray();
    }
    var lockedJournals = await db.AdjustmentJournals.FromSqlInterpolated($"SELECT * FROM adjustment_journals WHERE firm_id={actor.FirmId} AND client_id={client} AND engagement_id={engagement} AND id=ANY({journalIds}) ORDER BY id FOR SHARE").AsNoTracking().ToListAsync(ct);
    var numbers = lockedJournals.Select(j => j.JournalNumber).Distinct().ToArray();
    // Existing selected decisions cannot change while the exact calculation is committed.
    await db.JournalSourceReconciliations.FromSqlInterpolated($"SELECT * FROM journal_source_reconciliations WHERE firm_id={actor.FirmId} AND client_id={client} AND engagement_id={engagement} AND base_dataset_id={dataset} AND logical_journal_number=ANY({numbers}) ORDER BY id FOR SHARE").AsNoTracking().ToListAsync(ct);
    var preview = await PreviewCommandAsync(db, actor, target, r, ct); if (!preview.Succeeded) return Fail<PlanCommandReceipt>(preview.ErrorCode!, preview.Message!);
    var p = preview.Value!; if (!p.CanProceed) return Fail<PlanCommandReceipt>(ErrorCodes.GateBlocked, p.Blocker!);
    var planId = target; FinalizedPlan? calculated = null;
    if (r.Action == "CREATE")
    {
      var created = await AdjustmentPlanService.CreatePlanAsync(db, actor, dataset,
        p.Journals.Select(j => new PlanLineInput(j.JournalNumber, j.Revision, j.Layer)).ToArray(), ct);
      if (!created.Succeeded) return Fail<PlanCommandReceipt>(created.ErrorCode!, created.Message!); planId = created.Value;
    }
    else
    {
      var result = await AdjustmentPlanService.FinalizeReviewedNativeAsync(db, actor, target, ct);
      if (!result.Succeeded) return Fail<PlanCommandReceipt>(result.ErrorCode!, result.Message!);
      calculated = result.Value!;
      if (calculated.ResultHash != p.ResultHash || calculated.TotalDebits != p.Debits || calculated.TotalCreditsAbs != p.Credits || calculated.AppliedJournalCount != p.AppliedCount)
        return Fail<PlanCommandReceipt>(ErrorCodes.StaleRevision, "The calculation no longer matches its reviewed preview.");
    }
    var id = Guid.CreateVersion7(); var at = DateTimeOffset.UtcNow;
    var receipt = new PlanCommandReceipt(id, r.RequestId, p.RequestHash, planId, dataset, r.Action, actor.UserId,
      r.Reason.Trim(), r.EvidenceReference.Trim(), r.Action == "CREATE" ? "Draft" : "Finalized", calculated?.ResultHash,
      calculated?.TotalDebits, calculated?.TotalCreditsAbs, calculated?.AppliedJournalCount, at);
    db.AdjustmentPlanActions.Add(new() { Id = id, FirmId = actor.FirmId, ClientId = client, EngagementId = engagement,
      DatasetId = dataset, PlanId = planId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      RequestId = r.RequestId, RequestHash = p.RequestHash, ReviewBasis = r.ReviewBasis, Action = r.Action,
      Reason = receipt.Reason, EvidenceReference = receipt.EvidenceReference,
      BeforeJson = JsonSerializer.Serialize(new { Target = target, r.Action, r.ReviewBasis, r.Journals, Preview = p }),
      AfterJson = JsonSerializer.Serialize(receipt), CreatedAt = at });
    await db.SaveChangesAsync(ct);
    auth = await WriteAuth(db, actor, client, engagement, ct); if (!auth.Succeeded) return Fail<PlanCommandReceipt>(auth.ErrorCode!, auth.Message!);
    mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, client, engagement, periodId, book, ct);
    if (!mutable.Succeeded) return Fail<PlanCommandReceipt>(mutable.ErrorCode!, mutable.Message!);
    await tx.CommitAsync(ct); return CommandResult<PlanCommandReceipt>.Ok(receipt);
  }
}
