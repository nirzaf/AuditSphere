using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Completion;

public sealed record DeliverableView(Guid Id, string Kind, string Title, int Version, bool Signed, bool Current, string ContentSha256, DateTimeOffset CreatedAt);
public sealed record ReportAttempt(Guid? DeliverableId, Guid? HoldingLetterId, string Message);
public sealed record ConfirmationDashboardRow(Guid CaseId, string Type, string Respondent, decimal BookedAmount, string Currency, string Status,
  string Monitoring, int? DaysSinceDispatch, bool Critical, string? CriticalityRationale);

/// <summary>
/// Completion deliverables. Every generated document records a digest of the reviewed facts it was built from; a
/// Summary Review Memorandum is current only while those facts are unchanged, Partner clearance binds to a current
/// SRM, the opinion binds to that clearance and the Independent Auditor's Report binds to the opinion. The final
/// report is refused (and a versioned holding letter produced) while a critical confirmation is outstanding, and it is
/// signed only by the deciding Partner with a registered PNG specimen after management has acknowledged the current
/// representation letter and client comments are resolved. The platform records human decisions; it never forms one.
/// </summary>
public static partial class AuditDeliverableService
{
  private static readonly string[] PreparerRoles = ["Manager", "Partner", "Administrator"];
  private static readonly string[] ReaderRoles = ["Staff", "Senior", "Manager", "Partner", "Administrator", "Reviewer", "Auditor"];
  public const int FollowUpDays = 14;
  public const int AlternativeProcedureDays = 30;
  public const int MaxSignatureBytes = 512 * 1024;

  // ── Facts and currency ────────────────────────────────────────────────────────────────────────────

  private sealed record MaterialitySummary(string? Currency, decimal OverallMateriality, decimal PerformanceMateriality,
    decimal ClearlyTrivialThreshold, string BenchmarkSource);

  /// <summary>Per-currency uncorrected differences with gross and signed-net amounts, plus AJE linkage counts (STE §4.3.3).</summary>
  private sealed record DifferenceCurrencySummary(string Currency, int UncorrectedCount, decimal UncorrectedGross, decimal UncorrectedNet,
    int ProposedAdjustments, int AgreedAdjustments, int RejectedAdjustments, int AppliedAdjustments);

  private sealed record UnresolvedVariance(string AccountArea, string PeriodReference, decimal DifferenceAmount, string Currency, string Conclusion);

  private sealed record Facts(
    string Client, string PeriodEnd, MaterialitySummary? Materiality, IReadOnlyList<object> Risks, IReadOnlyList<string> RedAreas,
    IReadOnlyList<object> Procedures, int OpenReviewNotes, IReadOnlyList<Finding> Findings,
    IReadOnlyList<DifferenceCurrencySummary> UnadjustedDifferences, int ConfirmationsOutstanding, int ConfirmationsCriticalOutstanding,
    IReadOnlyList<object> Confirmations, object? GoingConcern, IReadOnlyList<UnresolvedVariance> UnresolvedVariances,
    IReadOnlyList<string> Blockers);

  private static async Task<Facts> FactsAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == engagement.PracticeClientId, ct);
    var materialityAssessment = await db.MaterialityAssessments.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var latestCalculation = await MaterialityEngineService.GetLatestAsync(db, actor.FirmId, engagementId, ct);
    MaterialitySummary? materiality = null;
    if (materialityAssessment is { } approvedMateriality && await MaterialityEngineService.IsPartnerApprovedCurrentAsync(db, approvedMateriality, ct))
    {
      var currency = latestCalculation is { } latest && latest.AssessmentId == approvedMateriality.Id && !string.IsNullOrWhiteSpace(latest.Calculation.Currency)
        ? latest.Calculation.Currency : null;
      materiality = new MaterialitySummary(currency, approvedMateriality.OverallMateriality, approvedMateriality.PerformanceMateriality,
        approvedMateriality.ClearlyTrivialThreshold, approvedMateriality.BenchmarkSource);
    }
    var routing = await RiskBandService.GetRoutingForAuthorizedScopeAsync(db, actor.FirmId, engagementId, ct);
    var redAreas = routing.Succeeded
      ? routing.Value!.Where(r => r.Band == RiskBands.Red).Select(r => r.Area).Distinct().ToList() : [];
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable)
      .OrderBy(x => x.SourceProcedureId).Select(x => new { x.Id, x.SourceProcedureId, x.Title, x.Status, x.CurrentResultRevision }).ToListAsync(ct);
    var openNotes = 0;
    foreach (var p in procedures) openNotes += await ReviewNotesService.OpenCountAsync(db, actor.FirmId, p.Id, ct);
    var findings = await db.Findings.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    var differenceRows = await db.AuditDifferences.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).ToListAsync(ct);
    var unadjustedDifferences = differenceRows
      .GroupBy(x => x.Currency, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key, StringComparer.Ordinal)
      .Select(g =>
      {
        var open = g.Where(x => !x.Corrected).ToList();
        return new DifferenceCurrencySummary(g.Key.ToUpperInvariant(), open.Count,
          MoneyPolicy.Normalize(open.Sum(x => Math.Abs(x.Amount))), MoneyPolicy.Normalize(open.Sum(x => x.Amount)),
          g.Count(x => x.CorrectionState == AuditDifferenceCorrectionStates.Proposed),
          g.Count(x => x.CorrectionState == AuditDifferenceCorrectionStates.Agreed),
          g.Count(x => x.CorrectionState == AuditDifferenceCorrectionStates.Rejected),
          g.Count(x => x.CorrectionState is AuditDifferenceCorrectionStates.AppliedInReporting
            or AuditDifferenceCorrectionStates.ReportedPostedExternally or AuditDifferenceCorrectionStates.VerifiedReflected));
      }).ToList();
    var confirmations = await ConfirmationRowsAsync(db, actor.FirmId, engagementId, ct);
    var outstandingConfirmations = confirmations.Count(c => c.Status != AuditConfirmationStatuses.Closed);
    var criticalConfirmations = confirmations.Count(c => c.Critical && c.Status != AuditConfirmationStatuses.Closed);
    var unresolvedVariances = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
        (x.Conclusion == VarianceInvestigationConclusions.Unexplained || x.ReviewedByUserId == null))
      .OrderBy(x => x.RecordedAt)
      .Select(x => new UnresolvedVariance(x.AccountArea, x.PeriodReference, x.DifferenceAmount, x.Currency, x.Conclusion))
      .ToListAsync(ct);
    var gc = await db.GoingConcernAssessments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).OrderByDescending(x => x.Revision)
      .Select(x => new { x.Id, x.Revision, x.Conclusion, Reviewed = x.ReviewedByUserId != null }).FirstOrDefaultAsync(ct);
    var completion = await AuditFieldworkService.EvaluateCompletionForAuthorizedScopeAsync(db, actor.FirmId, engagement.PracticeClientId, engagementId, ct);
    return new(client.CommercialName ?? client.LegalName, engagement.PeriodEnd, materiality,
      routing.Succeeded ? routing.Value!.Select(r => (object)new { r.RiskId, r.Area, r.Band, r.PartnerCleared }).ToList() : [],
      redAreas,
      procedures.Select(p => (object)p).ToList(), openNotes, findings, unadjustedDifferences,
      outstandingConfirmations, criticalConfirmations,
      confirmations.Select(c => (object)new { c.CaseId, c.Status, c.Critical }).ToList(), gc, unresolvedVariances,
      completion.Succeeded ? completion.Value!.Blockers : ["completion:unavailable"]);
  }

  private static string Digest(object value) => Hashing.Sha256Hex(JsonSerializer.Serialize(value));

  private static string FactsDigest(Facts facts) => Digest(new { facts.Materiality, facts.Risks, facts.RedAreas, facts.Procedures, facts.OpenReviewNotes,
    Findings = facts.Findings.Select(f => new { f.Id, f.FindingType, f.Corrected, f.MonetaryAmount, f.ManagementResponse, f.Status,
      f.LetterDesignatedAt, f.LetterRecommendation }),
    facts.UnadjustedDifferences, facts.UnresolvedVariances, facts.Confirmations, facts.GoingConcern, facts.Blockers });

  /// <summary>True while the facts a deliverable was generated from are unchanged (a signed copy follows its source).</summary>
  public static async Task<bool> IsCurrentAsync(IAuditSphereDbContext db, ActorContext actor, AuditDeliverable deliverable, CancellationToken ct = default)
  {
    var client = actor.Roles.Contains("ClientUser");
    if (deliverable.FirmId != actor.FirmId || !(await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, deliverable.ClientId, deliverable.EngagementId, client ? ["ClientUser"] : ReaderRoles, InternalOnly: !client), ct)).Succeeded) return false;
    if (deliverable.SignedFromDeliverableId is { } source)
      return await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == source, ct) is { } unsigned && await IsCurrentAsync(db, actor, unsigned, ct);
    var latest = await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == deliverable.FirmId && x.EngagementId == deliverable.EngagementId &&
      x.Kind == deliverable.Kind && x.SignedFromDeliverableId == null).MaxAsync(x => (int?)x.Version, ct);
    if (latest != deliverable.Version) return false;
    return deliverable.InputDigest == await ExpectedDigestAsync(db, actor, deliverable.EngagementId, deliverable.Kind, ct);
  }

  private static async Task<string> ExpectedDigestAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string kind, CancellationToken ct)
  {
    var facts = FactsDigest(await FactsAsync(db, actor, engagementId, ct));
    if (kind != DeliverableKinds.IndependentAuditorsReport) return facts;
    var opinion = await CurrentOpinionForAuthorizedScopeAsync(db, actor, engagementId, ct);
    return Digest(new { facts, Opinion = opinion?.Id });
  }

  // ── Summary Review Memorandum (3.3-03) ────────────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> GenerateSummaryReviewMemorandumAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    string recommendations, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(recommendations) || recommendations.Length > 8000)
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Write the reviewer's recommendations for the Partner.");
    var auth = await AuthorizeAsync(db, actor, engagementId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    return await CompileSummaryAsync(db, actor, engagementId, recommendations, ct);
  }

  /// <summary>Called within the authorized final workprogramme review transaction. Compiles facts, never an audit opinion.</summary>
  internal static async Task CompileAfterFinalReviewAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var applicable = db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable);
    if (!await applicable.AnyAsync(ct) || await applicable.AnyAsync(x => x.Status != AuditProcedureStatuses.Reviewed, ct)) return;
    var facts = await FactsAsync(db, actor, engagementId, ct);
    var digest = FactsDigest(facts);
    if (await db.AuditDeliverables.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      x.Kind == DeliverableKinds.SummaryReviewMemorandum && x.InputDigest == digest, ct)) return;
    var comments = await db.AuditProcedureReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      x.Decision == AuditProcedureReviewDecisions.Reviewed && x.Comment != null).OrderBy(x => x.CreatedAt).Select(x => x.Comment!).ToListAsync(ct);
    var recommendation = comments.Count == 0
      ? "Automatically compiled after all applicable workprogrammes were reviewed. No reviewer recommendation was recorded; human Partner clearance is still required."
      : "Recorded reviewer comments:\n" + string.Join("\n", comments);
    await CompileSummaryAsync(db, actor, engagementId, recommendation, ct);
  }

  private static string Amount(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

  /// <summary>Factual gross/net presentation with SAD/PM comparison where an approved basis exists; never a materiality conclusion.</summary>
  private static string DifferenceComparison(DifferenceCurrencySummary difference, MaterialitySummary? materiality)
  {
    if (materiality is null) return "Unavailable: no Partner-approved materiality is current.";
    if (string.IsNullOrEmpty(materiality.Currency) || !string.Equals(materiality.Currency, difference.Currency, StringComparison.OrdinalIgnoreCase))
      return $"Unavailable: no approved translation basis between {difference.Currency} and " +
        (string.IsNullOrEmpty(materiality.Currency) ? "the materiality currency" : $"{materiality.Currency}") + ".";
    var comparison = $"Gross {Amount(difference.UncorrectedGross)} vs SAD {Amount(materiality.ClearlyTrivialThreshold)} and PM {Amount(materiality.OverallMateriality)}; " +
      $"net {Amount(difference.UncorrectedNet)} vs PM {Amount(materiality.OverallMateriality)}.";
    if (difference.UncorrectedGross > materiality.OverallMateriality) return comparison + " Gross exceeds PM.";
    if (difference.UncorrectedGross > materiality.ClearlyTrivialThreshold) return comparison + " Gross exceeds SAD.";
    return comparison;
  }

  private static async Task<CommandResult<Guid>> CompileSummaryAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    string recommendations, CancellationToken ct)
  {
    var facts = await FactsAsync(db, actor, engagementId, ct);
    var differenceRows = facts.UnadjustedDifferences
      .Select(d => (IReadOnlyList<string>)[$"{d.Currency}", d.UncorrectedCount.ToString(CultureInfo.InvariantCulture),
        Amount(d.UncorrectedGross), Amount(d.UncorrectedNet), DifferenceComparison(d, facts.Materiality)]).ToList();
    var adjustmentLines = facts.UnadjustedDifferences
      .Where(d => d.ProposedAdjustments + d.AgreedAdjustments + d.RejectedAdjustments + d.AppliedAdjustments > 0)
      .Select(d => $"{d.Currency}: proposed {d.ProposedAdjustments}, agreed {d.AgreedAdjustments}, rejected {d.RejectedAdjustments}, applied {d.AppliedAdjustments}.")
      .ToList();
    var sections = new List<DocumentSection>
    {
      new("Engagement", [$"Client: {facts.Client}. Period ended {facts.PeriodEnd}."]),
      new("Materiality", facts.Materiality is { } mat
        ? [$"Planning materiality (PM): {Amount(mat.OverallMateriality)}{(mat.Currency is { } c ? $" {c}" : "")}. Tolerable error (TE): {Amount(mat.PerformanceMateriality)}. " +
           $"Clearly trivial threshold (SAD): {Amount(mat.ClearlyTrivialThreshold)}. Benchmark source: {mat.BenchmarkSource}.",
          "PM is planning materiality, TE is tolerable error, and SAD is the clearly trivial threshold used to summarise uncorrected differences."]
        : ["No Partner-approved materiality is current. SAD/PM comparisons below are unavailable; professional conclusions cannot rely on a materiality comparison."]),
      new("Key risk areas", facts.Risks.Count == 0 ? ["No risks recorded."] : [], facts.Risks.Count == 0 ? null
        : new DocumentTable(["Risk", "Band", "Partner review"], facts.Risks.Select(r => { var j = JsonSerializer.SerializeToElement(r);
          return (IReadOnlyList<string>)[j.GetProperty("Area").GetString() ?? "", j.GetProperty("Band").GetString() ?? "not assessed", j.GetProperty("PartnerCleared").GetBoolean() ? "Cleared" : "Open"]; }).ToList())),
      new("Red areas", facts.RedAreas.Count == 0 ? ["No risk area is currently banded Red."]
        : [$"The following risk areas are banded Red and require Audit Manager execution with mandatory Engagement Partner review: {string.Join(", ", facts.RedAreas)}."]),
      new("Work performed", [$"{facts.Procedures.Count} applicable procedures; {facts.Procedures.Count(p => JsonSerializer.SerializeToElement(p).GetProperty("Status").GetString() == AuditProcedureStatuses.Reviewed)} reviewed; {facts.OpenReviewNotes} open review note(s)."]),
      new("Uncorrected differences", facts.UnadjustedDifferences.Count == 0
        ? ["No uncorrected differences are recorded in the audit documentation. This states the documented position; it is not a conclusion that misstatements are absent or immaterial."]
        : [], facts.UnadjustedDifferences.Count == 0 ? null
        : new DocumentTable(["Currency", "Count", "Gross", "Net (signed)", "SAD/PM comparison"], differenceRows)),
      new("Adjusting journal entries", adjustmentLines.Count == 0
        ? ["No recorded difference is linked to an adjusting journal entry. Adjustment decisions remain the responsible team's human judgment."]
        : adjustmentLines),
      new("Significant variances", facts.UnresolvedVariances.Count == 0 ? ["No unresolved analytical-review variance investigations are recorded."] : [],
        facts.UnresolvedVariances.Count == 0 ? null
        : new DocumentTable(["Area", "Period", "Difference", "Currency", "Conclusion"],
          facts.UnresolvedVariances.Select(v => (IReadOnlyList<string>)[v.AccountArea, v.PeriodReference, Amount(v.DifferenceAmount), v.Currency, v.Conclusion]).ToList())),
      new("Confirmations", [$"{facts.ConfirmationsOutstanding} confirmation(s) are outstanding, of which {facts.ConfirmationsCriticalOutstanding} are critical; " +
        $"{facts.Confirmations.Count - facts.ConfirmationsOutstanding} are closed. Outstanding critical confirmations block report release."]),
      new("Unresolved issues", facts.Blockers.Count == 0 ? ["None: every completion gate is satisfied."] : facts.Blockers.ToList()),
      new("Reviewer recommendations", [recommendations.Trim()])
    };
    return await StoreAsync(db, actor, engagementId, DeliverableKinds.SummaryReviewMemorandum, FactsDigest(facts),
      new { facts.Blockers, facts.OpenReviewNotes, Recommendations = recommendations.Trim() }, facts.Client, sections, null, null, ct);
  }

  // ── Partner clearance (3.3-04) ────────────────────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> PartnerClearAsync(IAuditSphereDbContext db, ActorContext actor, Guid srmId, string keyRiskAreasComment,
    string notesComment, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(keyRiskAreasComment) || string.IsNullOrWhiteSpace(notesComment))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Record the Partner's review of key risk areas and of the financial-statement notes.");
    var srm = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == srmId && x.FirmId == actor.FirmId && x.Kind == DeliverableKinds.SummaryReviewMemorandum, ct);
    if (srm is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizePartnerAsync(db, actor, srm.EngagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await IsCurrentAsync(db, actor, srm, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The work changed after this memorandum; generate a current Summary Review Memorandum first.");
    var facts = await FactsAsync(db, actor, srm.EngagementId, ct);
    var blocking = facts.Blockers.Where(b => !b.StartsWith("confirmation:", StringComparison.Ordinal)).ToList();
    if (blocking.Count > 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, $"Prior review stages are incomplete: {string.Join(", ", blocking.Take(8))}.");
    var clearance = new PartnerCompletionClearance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = srm.ClientId, EngagementId = srm.EngagementId, SummaryReviewMemorandumId = srm.Id,
      KeyRiskAreasComment = keyRiskAreasComment.Trim(), FinancialStatementNotesComment = notesComment.Trim(), PartnerUserId = actor.UserId, ClearedAt = DateTimeOffset.UtcNow
    };
    db.PartnerCompletionClearances.Add(clearance);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(clearance.Id);
  }

  // ── Opinion (4.1-04) ──────────────────────────────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> DecideOpinionAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string opinionType,
    string? focusArea, string? basisText, CancellationToken ct = default)
  {
    var type = (opinionType ?? string.Empty).Trim().ToUpperInvariant();
    if (!AuditOpinionTypes.All.Contains(type)) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Choose Clean, Qualified, Adverse or Disclaimer.");
    if (type != AuditOpinionTypes.Unmodified &&
        (string.IsNullOrWhiteSpace(focusArea) || focusArea.Trim().Length > 200 ||
         string.IsNullOrWhiteSpace(basisText) || basisText.Trim().Length > 4000))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid,
        "A modified opinion needs an affected area of up to 200 characters and a basis of up to 4,000 characters.");
    var auth = await AuthorizePartnerAsync(db, actor, engagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var clearance = await CurrentClearanceAsync(db, actor, engagementId, ct);
    if (clearance is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Partner clearance of a current Summary Review Memorandum is required before the opinion.");
    OpinionFsliOption? selectedArea = null;
    if (type != AuditOpinionTypes.Unmodified)
    {
      var choices = await AffectedFinancialStatementAreasAsync(db, actor, engagementId, ct);
      var matching = choices.Where(x => x.Id.ToString("D") == focusArea?.Trim() || x.Name == focusArea?.Trim() || x.Code == focusArea?.Trim()).Take(2).ToList();
      if (matching.Count != 1) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Select one affected FSLI from the approved reporting taxonomy; free-text or ambiguous areas are not accepted.");
      selectedArea = matching[0];
    }
    var decision = new AuditOpinionDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clearance.ClientId, EngagementId = engagementId, PartnerClearanceId = clearance.Id, OpinionType = type,
      FocusArea = selectedArea?.Name, AffectedTaxonomyNodeId = selectedArea?.Id,
      BasisText = type == AuditOpinionTypes.Unmodified ? null : basisText!.Trim(), DecidedByUserId = actor.UserId, DecidedAt = DateTimeOffset.UtcNow
    };
    db.AuditOpinionDecisions.Add(decision);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(decision.Id);
  }

  /// <summary>Opinion paragraphs for each opinion type, with the Partner's basis injected where the type requires it.</summary>
  public static IReadOnlyList<DocumentSection> OpinionSections(AuditOpinionDecision opinion, string client, string periodEnd)
  {
    var statements = $"the financial statements of {client} for the period ended {periodEnd}";
    return opinion.OpinionType switch
    {
      AuditOpinionTypes.Qualified =>
      [
        new("Qualified Opinion", [$"In our opinion, except for the effects of the matter described in the Basis for Qualified Opinion section of our report, {statements} present fairly, in all material respects, the financial position and performance in accordance with the applicable financial reporting framework."]),
        new("Basis for Qualified Opinion", [$"Focus area: {opinion.FocusArea}.", opinion.BasisText!, "We conducted our audit in accordance with International Standards on Auditing. We believe that the audit evidence we have obtained is sufficient and appropriate to provide a basis for our qualified opinion."])
      ],
      AuditOpinionTypes.Adverse =>
      [
        new("Adverse Opinion", [$"In our opinion, because of the significance of the matter described in the Basis for Adverse Opinion section of our report, {statements} do not present fairly the financial position and performance in accordance with the applicable financial reporting framework."]),
        new("Basis for Adverse Opinion", [$"Focus area: {opinion.FocusArea}.", opinion.BasisText!])
      ],
      AuditOpinionTypes.Disclaimer =>
      [
        new("Disclaimer of Opinion", [$"We do not express an opinion on {statements}. Because of the significance of the matter described in the Basis for Disclaimer of Opinion section of our report, we have not been able to obtain sufficient appropriate audit evidence to provide a basis for an audit opinion."]),
        new("Basis for Disclaimer of Opinion", [$"Focus area: {opinion.FocusArea}.", opinion.BasisText!])
      ],
      _ =>
      [
        new("Opinion", [$"In our opinion, {statements} present fairly, in all material respects, the financial position and performance in accordance with the applicable financial reporting framework."]),
        new("Basis for Opinion", ["We conducted our audit in accordance with International Standards on Auditing. We believe that the audit evidence we have obtained is sufficient and appropriate to provide a basis for our opinion."])
      ]
    };
  }

  // ── Report trio and holding letter (4.1-01, 3.4-02) ───────────────────────────────────────────────

  public static async Task<CommandResult<ReportAttempt>> GenerateReportAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string kind, CancellationToken ct = default)
  {
    kind = (kind ?? string.Empty).Trim().ToUpperInvariant();
    if (kind is not (DeliverableKinds.AuditFindingsReport or DeliverableKinds.ManagementLetter or DeliverableKinds.IndependentAuditorsReport or DeliverableKinds.RepresentationLetter))
      return CommandResult<ReportAttempt>.Fail(ErrorCodes.AuditPlanning.Invalid, "Unsupported deliverable.");
    var auth = await AuthorizeAsync(db, actor, engagementId, PreparerRoles, ct);
    if (!auth.Succeeded) return CommandResult<ReportAttempt>.Fail(auth.ErrorCode!, auth.Message!);
    var writable = await AuditSphereOps.Application.Records.FileFreezeService.RequireWritableAsync(db, actor, engagementId, $"generate {DeliverableKinds.Title(kind)}", ct);
    if (!writable.Succeeded) return CommandResult<ReportAttempt>.Fail(writable.ErrorCode!, writable.Message!);
    var facts = await FactsAsync(db, actor, engagementId, ct);
    var digest = FactsDigest(facts);
    List<DocumentSection> sections;
    string? signature = null;
    switch (kind)
    {
      case DeliverableKinds.AuditFindingsReport:
        sections =
        [
          new("Scope", [$"Findings arising from our audit of {facts.Client} for the period ended {facts.PeriodEnd}."]),
          new("Findings", facts.Findings.Count == 0 ? ["No findings were recorded."] : [], facts.Findings.Count == 0 ? null
            : new DocumentTable(["Type", "Impact", "Amount", "Corrected", "Status"], facts.Findings.Select(f => (IReadOnlyList<string>)[f.FindingType, f.ImpactDescription,
              f.MonetaryAmount?.ToString("N2", CultureInfo.InvariantCulture) ?? "", f.Corrected ? "Yes" : "No", f.Status]).ToList(), [2])),
          new("Uncorrected differences", facts.UnadjustedDifferences.Count == 0
            ? ["No uncorrected differences are recorded in the audit documentation."]
            : [], facts.UnadjustedDifferences.Count == 0 ? null
            : new DocumentTable(["Currency", "Count", "Gross", "Net (signed)"], facts.UnadjustedDifferences.Select(d =>
              (IReadOnlyList<string>)[$"{d.Currency}", d.UncorrectedCount.ToString(CultureInfo.InvariantCulture), Amount(d.UncorrectedGross), Amount(d.UncorrectedNet)]).ToList()))
        ];
        break;
      case DeliverableKinds.ManagementLetter:
        var designated = facts.Findings.Where(f => f.LetterDesignatedAt is not null).ToList();
        var incomplete = designated.Where(f => string.IsNullOrWhiteSpace(f.ImpactDescription) || string.IsNullOrWhiteSpace(f.LetterRecommendation)).ToList();
        if (incomplete.Count > 0)
          return CommandResult<ReportAttempt>.Fail(ErrorCodes.GateBlocked,
            $"{incomplete.Count} designated matter(s) lack an impact description or a recommendation. Complete or un-designate them before the management letter can be generated.");
        sections =
        [
          new("Purpose", ["This letter sets out matters that came to our attention during the audit and management's responses. It is not a complete list of all weaknesses that may exist."]),
          new("Matters and management responses", designated.Count == 0 ? ["No matters were designated for the management letter."] : [], designated.Count == 0 ? null
            : new DocumentTable(["Matter", "Impact", "Recommendation", "Management response"], designated.Select(f => (IReadOnlyList<string>)[f.FindingType, f.ImpactDescription,
              f.LetterRecommendation!, f.ManagementResponse ?? "Awaiting response"]).ToList()))
        ];
        break;
      case DeliverableKinds.RepresentationLetter:
        var recordedDifferences = facts.UnadjustedDifferences.Count == 0
          ? "No uncorrected misstatements were recorded in the audit documentation at the date of this letter."
          : "Uncorrected misstatements recorded in the audit documentation: " + string.Join("; ",
              facts.UnadjustedDifferences.Select(d => $"{d.Currency} gross {Amount(d.UncorrectedGross)}, net {Amount(d.UncorrectedNet)}")) + ".";
        sections =
        [
          new("Representations", [$"We confirm, to the best of our knowledge and belief, the following representations made to you in connection with your audit of the financial statements of {facts.Client} for the period ended {facts.PeriodEnd}.",
            "We have fulfilled our responsibilities for the preparation of the financial statements in accordance with the applicable framework.",
            "We have provided you with all relevant information and access, and all transactions have been recorded and reflected in the financial statements.",
            recordedDifferences,
            "In management's opinion, the effects of uncorrected misstatements are immaterial, individually and in aggregate, to the financial statements taken as a whole."])
        ];
        signature = "Signed on behalf of management";
        break;
      default:
        var opinion = await CurrentOpinionAsync(db, actor, engagementId, ct);
        if (opinion is null)
          return CommandResult<ReportAttempt>.Fail(ErrorCodes.GateBlocked, "Record the Partner's opinion on a current clearance before the Independent Auditor's Report.");
        var critical = (await ConfirmationRowsAsync(db, actor.FirmId, engagementId, ct)).Where(c => c.Critical && c.Status != AuditConfirmationStatuses.Closed).ToList();
        if (critical.Count > 0)
        {
          var holding = await StoreAsync(db, actor, engagementId, DeliverableKinds.HoldingLetter, Digest(critical.Select(c => new { c.CaseId, c.Status })), new { Outstanding = critical.Count },
            facts.Client,
            [
              new("Status of the audit", [$"Our audit of {facts.Client} for the period ended {facts.PeriodEnd} cannot be completed and our report cannot be issued until the following critical third-party confirmations are received and evaluated."]),
              new("Outstanding confirmations", [], new DocumentTable(["Type", "Respondent", "Amount", "Status", "Days since dispatch"],
                critical.Select(c => (IReadOnlyList<string>)[c.Type, c.Respondent, c.BookedAmount.ToString("N2", CultureInfo.InvariantCulture), c.Monitoring, c.DaysSinceDispatch?.ToString(CultureInfo.InvariantCulture) ?? "not dispatched"]).ToList(), [2]))
            ], null, null, ct);
          return CommandResult<ReportAttempt>.Ok(new(null, holding.Value,
            $"The Independent Auditor's Report is held: {critical.Count} critical confirmation(s) are outstanding. A Pending Confirmation / Holding Letter was generated."));
        }
        sections = [.. OpinionSections(opinion, facts.Client, facts.PeriodEnd),
          new("Responsibilities", ["Management is responsible for the preparation of the financial statements. Our responsibility is to express an opinion on them based on our audit."])];
        digest = Digest(new { facts = digest, Opinion = opinion.Id });
        signature = "Engagement Partner";
        break;
    }
    var stored = await StoreAsync(db, actor, engagementId, kind, digest, new { facts.Blockers.Count }, facts.Client, sections, signature, null, ct);
    return stored.Succeeded ? CommandResult<ReportAttempt>.Ok(new(stored.Value, null, $"{DeliverableKinds.Title(kind)} generated.")) : CommandResult<ReportAttempt>.Fail(stored.ErrorCode!, stored.Message!);
  }

  // ── PNG signatures (4.1-03) ───────────────────────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> RegisterSignatureAsync(IAuditSphereDbContext db, ActorContext actor, byte[] png, CancellationToken ct = default)
  {
    if (png.Length == 0 || png.Length > MaxSignatureBytes) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "Upload a PNG signature of at most 512 KB.");
    if (AuditDeliverableRenderer.ReadPngSize(png) is not { } size || size.Width is < 50 or > 2000 || size.Height is < 20 or > 1000 || !AuditDeliverableRenderer.IsRenderablePng(png))
      return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "The file is not a PNG image of a usable signature size.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Partner"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var now = DateTimeOffset.UtcNow;
    foreach (var old in await db.SignatureSpecimens.Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null).ToListAsync(ct))
      old.RevokedAt = now;
    var specimen = new SignatureSpecimen
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, UserId = actor.UserId, PngContent = png, Sha256 = Hashing.Sha256Hex(png),
      WidthPixels = size.Width, HeightPixels = size.Height, RegisteredAt = now
    };
    db.SignatureSpecimens.Add(specimen);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(specimen.Id);
  }

  public static async Task<CommandResult<Guid>> SignIndependentReportAsync(IAuditSphereDbContext db, ActorContext actor, Guid reportId, CancellationToken ct = default)
  {
    var report = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reportId && x.FirmId == actor.FirmId &&
      x.Kind == DeliverableKinds.IndependentAuditorsReport && x.SignedFromDeliverableId == null, ct);
    if (report is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizePartnerAsync(db, actor, report.EngagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockDeliverableEngagementAsync(db, actor.FirmId, report.EngagementId, ct);
    auth = await AuthorizePartnerAsync(db, actor, report.EngagementId, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await IsCurrentAsync(db, actor, report, ct)) return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "This report version is no longer current; generate it again.");
    var opinion = await CurrentOpinionAsync(db, actor, report.EngagementId, ct);
    if (opinion is null || opinion.DecidedByUserId != actor.UserId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Only the Partner who decided the opinion signs the report.");
    if (await db.SignatureApplications.AnyAsync(x => x.FirmId == actor.FirmId && x.SourceDeliverableId == report.Id, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This report version is already signed.");
    var gate = await ClientGateAsync(db, actor, report.EngagementId, ct);
    if (gate is not null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, gate);
    var specimen = await db.SignatureSpecimens.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId && x.RevokedAt == null, ct);
    if (specimen is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Register your PNG signature first.");
    var seal = await db.FirmSealSpecimens.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (seal is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Register the approved firm seal before signing the final report.");
    var partner = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId, ct);
    var facts = await FactsAsync(db, actor, report.EngagementId, ct);
    var sections = OpinionSections(opinion, facts.Client, facts.PeriodEnd).Append(new DocumentSection("Responsibilities",
      ["Management is responsible for the preparation of the financial statements. Our responsibility is to express an opinion on them based on our audit."])).ToList();
    var signed = await StoreAsync(db, actor, report.EngagementId, DeliverableKinds.IndependentAuditorsReport, report.InputDigest, new { SignedFrom = report.Id, Specimen = specimen.Sha256, Seal = seal.Id, SealHash = seal.Sha256 },
      facts.Client, sections, "Engagement Partner", new DeliverableSignature(specimen.PngContent, specimen.WidthPixels, specimen.HeightPixels, partner.DisplayName, "Engagement Partner",
        DateOnly.FromDateTime(DateTime.UtcNow)), ct, signedFrom: report, firmSeal: seal.PngContent);
    if (!signed.Succeeded) return signed;
    var signedDoc = await db.AuditDeliverables.SingleAsync(x => x.Id == signed.Value, ct);
    await AuditSphereOps.Application.Records.FileFreezeService.ScheduleAsync(db, signedDoc, signedDoc.CreatedAt, ct);
    db.SignatureApplications.Add(new SignatureApplication
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, EngagementId = report.EngagementId, SpecimenId = specimen.Id, SourceDeliverableId = report.Id,
      SignedDeliverableId = signed.Value, SignedByUserId = actor.UserId, SignedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return signed;
  }

  // ── Client review cycle (4.1-02) ──────────────────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> ShareWithClientAsync(IAuditSphereDbContext db, ActorContext actor, Guid deliverableId, CancellationToken ct = default)
  {
    var deliverable = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deliverableId && x.FirmId == actor.FirmId, ct);
    if (deliverable is null || deliverable.Kind is DeliverableKinds.SummaryReviewMemorandum) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, deliverable.EngagementId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var existing = await db.ClientDeliverableReviews.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.DeliverableId == deliverableId, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var review = new ClientDeliverableReview
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = deliverable.ClientId, EngagementId = deliverable.EngagementId, DeliverableId = deliverableId,
      SharedByUserId = actor.UserId, SharedAt = DateTimeOffset.UtcNow
    };
    db.ClientDeliverableReviews.Add(review);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }

  public static async Task<CommandResult<Guid>> CommentAsync(IAuditSphereDbContext db, ActorContext actor, Guid reviewId, string body, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(body) || body.Length > 4000) return CommandResult<Guid>.Fail(ErrorCodes.AuditPlanning.Invalid, "A comment is required.");
    var review = await db.ClientDeliverableReviews.AsNoTracking().SingleOrDefaultAsync(x => x.Id == reviewId && x.FirmId == actor.FirmId, ct);
    if (review is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var fromClient = actor.Roles.Contains("ClientUser");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, review.ClientId, review.EngagementId,
      fromClient ? ["ClientUser"] : ReaderRoles, InternalOnly: !fromClient), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (review.AcknowledgedAt is not null) return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The document was already acknowledged.");
    var comment = new ClientDeliverableComment { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ReviewId = reviewId, Body = body.Trim(), AuthorUserId = actor.UserId, FromClient = fromClient, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientDeliverableComments.Add(comment);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(comment.Id);
  }

  public static async Task<CommandResult> ResolveCommentAsync(IAuditSphereDbContext db, ActorContext actor, Guid commentId, string resolution, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(resolution)) return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Explain how the comment was resolved.");
    var comment = await db.ClientDeliverableComments.SingleOrDefaultAsync(x => x.Id == commentId && x.FirmId == actor.FirmId, ct);
    if (comment is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var review = await db.ClientDeliverableReviews.AsNoTracking().SingleAsync(x => x.Id == comment.ReviewId, ct);
    var auth = await AuthorizeAsync(db, actor, review.EngagementId, PreparerRoles, ct);
    if (!auth.Succeeded) return auth;
    if (comment.ResolvedAt is not null) return CommandResult.Ok();
    comment.ResolvedAt = DateTimeOffset.UtcNow;
    comment.ResolvedByUserId = actor.UserId;
    comment.Resolution = resolution.Trim();
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Client management acknowledges the exact shared version (its SHA-256 is recorded).</summary>
  public static async Task<CommandResult> AcknowledgeAsync(IAuditSphereDbContext db, ActorContext actor, Guid reviewId, string sha256, CancellationToken ct = default)
  {
    var review = await db.ClientDeliverableReviews.SingleOrDefaultAsync(x => x.Id == reviewId && x.FirmId == actor.FirmId, ct);
    if (review is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, review.ClientId, review.EngagementId, ["ClientUser"]), ct);
    if (!auth.Succeeded) return auth;
    var deliverable = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == review.DeliverableId, ct);
    if (!string.Equals(deliverable.ContentSha256, sha256?.Trim(), StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The acknowledged document does not match the shared version.");
    if (await db.ClientDeliverableComments.AnyAsync(x => x.ReviewId == reviewId && x.ResolvedAt == null, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Open comments must be resolved before acknowledgement.");
    if (review.AcknowledgedAt is not null) return CommandResult.Ok();
    review.AcknowledgedAt = DateTimeOffset.UtcNow;
    review.AcknowledgedByUserId = actor.UserId;
    review.AcknowledgedSha256 = deliverable.ContentSha256;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>Signing gate: the latest representation letter acknowledged by the client, and no open client comments on any shared deliverable.</summary>
  private static async Task<string?> ClientGateAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var firmId = actor.FirmId;
    var letter = await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId && x.Kind == DeliverableKinds.RepresentationLetter)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (letter is null) return "Generate the management representation letter and have client management acknowledge it first.";
    if (!await IsCurrentAsync(db, actor, letter, ct)) return "The representation letter is stale; generate and obtain a signed current version.";
    var ack = await db.ClientDeliverableReviews.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.DeliverableId == letter.Id, ct);
    if (ack?.AcknowledgedSha256 != letter.ContentSha256) return "Client management has not acknowledged the current representation letter.";
    if (!await db.SignedRepresentationLetters.AnyAsync(x => x.FirmId == firmId && x.DeliverableId == letter.Id &&
        x.DeliverableSha256 == letter.ContentSha256 && db.RepresentationLetterVerifications.Any(v => v.FirmId == firmId && v.SignedLetterId == x.Id), ct))
      return "Upload the management-signed PDF of the current representation letter and have the Engagement Partner verify its signature and completeness.";
    var reviewIds = db.ClientDeliverableReviews.Where(x => x.FirmId == firmId && x.EngagementId == engagementId).Select(x => x.Id);
    return await db.ClientDeliverableComments.AnyAsync(x => reviewIds.Contains(x.ReviewId) && x.ResolvedAt == null, ct)
      ? "Resolve the client's open comments on the shared drafts first." : null;
  }

  // ── Confirmations dashboard and criticality (3.4-01) ──────────────────────────────────────────────

  public static async Task<CommandResult> SetConfirmationCriticalityAsync(IAuditSphereDbContext db, ActorContext actor, Guid caseId, bool critical, string rationale, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(rationale)) return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Explain why the confirmation is (or is not) critical to the report.");
    var confirmation = await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId && x.FirmId == actor.FirmId, ct);
    if (confirmation is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, confirmation.EngagementId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded) return auth;
    db.ConfirmationCriticalities.Add(new ConfirmationCriticality
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ConfirmationCaseId = caseId, Critical = critical, Rationale = rationale.Trim(), SetByUserId = actor.UserId, SetAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<IReadOnlyList<ConfirmationDashboardRow>>> ConfirmationDashboardAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, engagementId, ReaderRoles, ct);
    return auth.Succeeded
      ? CommandResult<IReadOnlyList<ConfirmationDashboardRow>>.Ok(await ConfirmationRowsAsync(db, actor.FirmId, engagementId, ct))
      : CommandResult<IReadOnlyList<ConfirmationDashboardRow>>.Fail(auth.ErrorCode!, auth.Message!);
  }

  /// <summary>Monitoring is derived from recorded status and dispatch time; external response capture stays with the approved channel.</summary>
  private static async Task<List<ConfirmationDashboardRow>> ConfirmationRowsAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var cases = await db.AuditConfirmationCases.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).OrderBy(x => x.AreaCode).ThenBy(x => x.Respondent).ToListAsync(ct);
    var ids = cases.Select(x => x.Id).ToArray();
    var criticality = (await db.ConfirmationCriticalities.AsNoTracking().Where(x => ids.Contains(x.ConfirmationCaseId)).ToListAsync(ct))
      .GroupBy(x => x.ConfirmationCaseId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SetAt).First());
    var now = DateTimeOffset.UtcNow;
    return cases.Select(c =>
    {
      int? days = c.DispatchedAt is { } sent ? (int)(now - sent).TotalDays : null;
      var monitoring = c.Status switch
      {
        AuditConfirmationStatuses.Closed => "CLOSED",
        AuditConfirmationStatuses.ResponseReceived => "RESPONSE_RECEIVED",
        AuditConfirmationStatuses.AlternativeRequired or AuditConfirmationStatuses.NoResponse => "ALTERNATIVE_PROCEDURES",
        AuditConfirmationStatuses.Dispatched when days >= AlternativeProcedureDays => "ALTERNATIVE_PROCEDURES_DUE",
        AuditConfirmationStatuses.Dispatched when days >= FollowUpDays => "FOLLOW_UP_DUE",
        AuditConfirmationStatuses.Dispatched => "AWAITING_RESPONSE",
        _ => "NOT_DISPATCHED"
      };
      var c2 = criticality.GetValueOrDefault(c.Id);
      return new ConfirmationDashboardRow(c.Id, TypeLabel(c.AreaCode), c.Respondent, c.BookedAmount, c.Currency, c.Status, monitoring, days, c2?.Critical ?? false, c2?.Rationale);
    }).ToList();
  }

  private static string TypeLabel(string area) => area.ToUpperInvariant() switch
  {
    var a when a.Contains("BANK") || a.Contains("CASH") => "Bank",
    var a when a.Contains("RECEIV") || a.Contains("DEBTOR") || a.Contains("AR") => "Debtor",
    var a when a.Contains("INVENT") || a.Contains("STOCK") => "Inventory",
    var a when a.Contains("PAYAB") || a.Contains("CREDITOR") => "Creditor",
    var a when a.Contains("LEGAL") => "Legal",
    _ => area
  };

  // ── Downloads and client view ─────────────────────────────────────────────────────────────────────

  /// <summary>Staff on the engagement may download any deliverable; a client user only one shared with their client and engagement.</summary>
  public static async Task<CommandResult<AuditDeliverable>> GetForDownloadAsync(IAuditSphereDbContext db, ActorContext actor, Guid deliverableId, CancellationToken ct = default)
  {
    var deliverable = await db.AuditDeliverables.AsNoTracking().SingleOrDefaultAsync(x => x.Id == deliverableId && x.FirmId == actor.FirmId, ct);
    if (deliverable is null) return CommandResult<AuditDeliverable>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (actor.Roles.Contains("ClientUser"))
    {
      var shared = await db.ClientDeliverableReviews.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.DeliverableId == deliverableId, ct);
      var auth = shared ? await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, deliverable.ClientId, deliverable.EngagementId, ["ClientUser"]), ct)
        : CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      return auth.Succeeded ? CommandResult<AuditDeliverable>.Ok(deliverable) : CommandResult<AuditDeliverable>.Fail(auth.ErrorCode!, auth.Message!);
    }
    var staff = await AuthorizeAsync(db, actor, deliverable.EngagementId, ReaderRoles, ct);
    return staff.Succeeded ? CommandResult<AuditDeliverable>.Ok(deliverable) : CommandResult<AuditDeliverable>.Fail(staff.ErrorCode!, staff.Message!);
  }

  public sealed record SharedDeliverableView(Guid ReviewId, Guid DeliverableId, string Kind, string Title, int Version, string ContentSha256, DateTimeOffset SharedAt,
    DateTimeOffset? AcknowledgedAt, IReadOnlyList<ClientDeliverableComment> Comments);

  /// <summary>Deliverables shared with the client for one engagement, for either side of the review loop.</summary>
  public static async Task<IReadOnlyList<SharedDeliverableView>> SharedAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return [];
    var client = actor.Roles.Contains("ClientUser");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
      client ? ["ClientUser"] : ReaderRoles, InternalOnly: !client), ct);
    if (!auth.Succeeded) return [];
    var reviews = await db.ClientDeliverableReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).OrderByDescending(x => x.SharedAt).ToListAsync(ct);
    var ids = reviews.Select(x => x.DeliverableId).ToArray();
    var deliverables = await db.AuditDeliverables.AsNoTracking().Where(x => ids.Contains(x.Id))
      .Select(x => new { x.Id, x.Kind, x.Version, x.ContentSha256 }).ToDictionaryAsync(x => x.Id, ct);
    var reviewIds = reviews.Select(x => x.Id).ToArray();
    var comments = await db.ClientDeliverableComments.AsNoTracking().Where(x => reviewIds.Contains(x.ReviewId)).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    return reviews.Select(r =>
    {
      var d = deliverables[r.DeliverableId];
      return new SharedDeliverableView(r.Id, d.Id, d.Kind, DeliverableKinds.Title(d.Kind), d.Version, d.ContentSha256, r.SharedAt, r.AcknowledgedAt,
        comments.Where(c => c.ReviewId == r.Id).ToList());
    }).ToList();
  }

  // ── Listing and plumbing ──────────────────────────────────────────────────────────────────────────

  public static async Task<IReadOnlyList<DeliverableView>> ListAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (!(await AuthorizeAsync(db, actor, engagementId, ReaderRoles, ct)).Succeeded) return [];
    var rows = await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).OrderBy(x => x.Kind).ThenByDescending(x => x.Version).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
    var result = new List<DeliverableView>();
    foreach (var row in rows)
      result.Add(new(row.Id, row.Kind, DeliverableKinds.Title(row.Kind), row.Version, row.SignedFromDeliverableId is not null,
        row.Kind == DeliverableKinds.HoldingLetter || await IsCurrentAsync(db, actor, row, ct), row.ContentSha256, row.CreatedAt));
    return result;
  }

  public static async Task<PartnerCompletionClearance?> CurrentClearanceAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default) =>
    (await AuthorizeAsync(db, actor, engagementId, ReaderRoles, ct)).Succeeded ? await CurrentClearanceForAuthorizedScopeAsync(db, actor, engagementId, ct) : null;

  public static async Task<AuditOpinionDecision?> CurrentOpinionAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default) =>
    (await AuthorizeAsync(db, actor, engagementId, ReaderRoles, ct)).Succeeded ? await CurrentOpinionForAuthorizedScopeAsync(db, actor, engagementId, ct) : null;

  private static async Task<PartnerCompletionClearance?> CurrentClearanceForAuthorizedScopeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var clearance = await db.PartnerCompletionClearances.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).OrderByDescending(x => x.ClearedAt).FirstOrDefaultAsync(ct);
    if (clearance is null) return null;
    var srm = await db.AuditDeliverables.AsNoTracking().SingleAsync(x => x.Id == clearance.SummaryReviewMemorandumId, ct);
    return await IsCurrentAsync(db, actor, srm, ct) ? clearance : null;
  }

  private static async Task<AuditOpinionDecision?> CurrentOpinionForAuthorizedScopeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var clearance = await CurrentClearanceForAuthorizedScopeAsync(db, actor, engagementId, ct);
    return clearance is null ? null : await db.AuditOpinionDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PartnerClearanceId == clearance.Id)
      .OrderByDescending(x => x.DecidedAt).FirstOrDefaultAsync(ct);
  }

  private static async Task<CommandResult<Guid>> StoreAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string kind, string digest, object summary,
    string client, IReadOnlyList<DocumentSection> sections, string? signatureLabel, DeliverableSignature? signature, CancellationToken ct, AuditDeliverable? signedFrom = null, byte[]? firmSeal = null)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    var firm = await db.FirmCommercialProfiles.AsNoTracking().Where(x => x.FirmId == actor.FirmId).OrderByDescending(x => x.Version).Select(x => x.LegalName).FirstOrDefaultAsync(ct);
    var version = signedFrom?.Version ?? (await db.AuditDeliverables.Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Kind == kind && x.SignedFromDeliverableId == null)
      .MaxAsync(x => (int?)x.Version, ct) ?? 0) + 1;
    var reference = $"{kind[..3]}-{engagement.PeriodEnd}-v{version}{(signedFrom is null ? "" : "-SIGNED")}";
    var model = new AuditDeliverableModel(firm ?? "Audit firm", DeliverableKinds.Title(kind), reference, DateOnly.FromDateTime(DateTime.UtcNow),
      kind == DeliverableKinds.RepresentationLetter ? "The auditors" : $"Those charged with governance, {client}", sections, signatureLabel, signature, firmSeal);
    var finalPdf = signedFrom != null && kind == DeliverableKinds.IndependentAuditorsReport;
    var content = finalPdf ? AuditDeliverableRenderer.RenderPdf(model) : AuditDeliverableRenderer.RenderDocx(model);
    var deliverable = new AuditDeliverable
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagementId, Kind = kind, Version = version,
      TemplateVersion = finalPdf ? "AUDIT-SIGNED-PDF-v1" : AuditDeliverableRenderer.TemplateVersion, InputDigest = digest, InputSummaryJson = JsonSerializer.Serialize(summary),
      FileName = $"{reference}.{(finalPdf ? "pdf" : "docx")}", ContentType = finalPdf ? "application/pdf" : AuditDeliverableRenderer.DocxContentType, Content = content, ContentSha256 = Hashing.Sha256Hex(content),
      SignedFromDeliverableId = signedFrom?.Id, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditDeliverables.Add(deliverable);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(deliverable.Id);
  }

  private static async Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string[] roles, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    return engagement is null
      ? CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.")
      : await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId, roles, InternalOnly: true), ct);
  }

  /// <summary>A Partner on the engagement; when the engagement is staffed, it must be its Engagement Partner.</summary>
  private static async Task<CommandResult> AuthorizePartnerAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct)
  {
    var auth = await AuthorizeAsync(db, actor, engagementId, ["Partner"], ct);
    if (!auth.Succeeded) return auth;
    var partner = await db.EngagementStaffAssignments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      x.RevokedAt == null && x.StaffingLevel == StaffingLevels.EngagementPartner, ct);
    return partner is null || partner.UserId == actor.UserId
      ? CommandResult.Ok()
      : CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the staffed Engagement Partner can clear, decide the opinion and sign.");
  }
}
