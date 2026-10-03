using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AnalysisAmount(string Key, string Label, string? Value, string Unit);
public sealed record AnalysisText(string Key, string Label, string Value);
public sealed record AnalysisProcedureLink(Guid Id, Guid ResultId, Guid WorkpaperId, string Status, long InputGeneration,
  bool IsCurrentReviewedResult, Guid LinkedByUserId, DateTimeOffset LinkedAt);
public sealed record AnalysisSource(string Kind, Guid? Id, Guid? ReconciliationId, string? RetainedDigest,
  string? CurrentDigest, bool CurrentChecksPass, string Description);
public sealed record AccountingAnalysisReview(Guid Id, string Kind, Guid ClientId, string ClientName, Guid EngagementId,
  string EngagementName, Guid PeriodId, string PeriodCode, string Basis, string Currency, string Area, string Status,
  int? Version, long? InputGeneration, long? CurrentGeneration, bool InputsCurrent, AnalysisSource Source,
  string? AssumptionsDigest, string? ReplayDigest, bool? ReplayMatchesInputs, Guid? ProposedJournalId,
  Guid? CreatedByUserId, DateTimeOffset CreatedAt, Guid? ReviewedByUserId, DateTimeOffset? ReviewedAt,
  IReadOnlyList<AnalysisAmount> Amounts, IReadOnlyList<AnalysisText> Details, IReadOnlyList<string> Blockers,
  IReadOnlyList<AnalysisProcedureLink> Links, int LinkCount, int Page, bool HasMore, bool HasCurrentReviewedProcedure,
  string ReviewBasis);

/// <summary>Bounded read-only inspection. Retained approval and current input verification are separate facts.</summary>
public static partial class AccountingAnalysisReviewQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] Kinds = ["ECL", "INVENTORY", "SPECIALIST", "ANALYTICAL", "JOURNAL_RISK"];
  private const int MaximumLinks = 500;
  private sealed record Retained(Guid Id, string Kind, Guid ClientId, Guid EngagementId, Guid PeriodId, string Area,
    string Status, int? Version, long? Generation, Guid? ReconciliationId, Guid? BatchId, Guid? TransactionId,
    string? SourceHash, string? AssumptionsHash, string? ReplayHash, bool? ReplayMatches, Guid? JournalId,
    Guid? Creator, DateTimeOffset CreatedAt, Guid? Reviewer, DateTimeOffset? ReviewedAt,
    IReadOnlyList<AnalysisAmount> Amounts, IReadOnlyList<AnalysisText> Details, IReadOnlyList<string> Problems, string Snapshot);

  public static async Task<CommandResult<AccountingAnalysisReview>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    string kind, Guid id, int page = 0, CancellationToken ct = default)
  {
    if (!Kinds.Contains(kind, StringComparer.Ordinal) || id == Guid.Empty || page is < 0 or >= 20)
      return CommandResult<AccountingAnalysisReview>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Select a supported evidence identity and link page.");
    var first = await ReadAsync(db, actor, kind, id, page, ct);
    if (!first.Succeeded) return first;
    var second = await ReadAsync(db, actor, kind, id, page, ct);
    if (!second.Succeeded) return second;
    if (first.Value!.ReviewBasis != second.Value!.ReviewBasis)
      return CommandResult<AccountingAnalysisReview>.Fail(ErrorCodes.GenerationStale, "The evidence or its inputs changed during inspection. Refresh before review.");
    var final = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, second.Value.ClientId, second.Value.EngagementId, Roles, InternalOnly: true), ct);
    return final.Succeeded ? second : Denied();
  }

  private static async Task<CommandResult<AccountingAnalysisReview>> ReadAsync(IClientAccountingDbContext db, ActorContext actor,
    string kind, Guid id, int page, CancellationToken ct)
  {
    var initial = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, RequiredRoles: Roles, InternalOnly: true), ct);
    if (!initial.Succeeded) return Denied();
    var r = await RetainedAsync(db, actor.FirmId, kind, id, ct);
    if (r is null) return Denied();
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, r.ClientId, r.EngagementId, Roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return Denied();
    var client = await db.PracticeClients.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.ClientId && x.FirmId == actor.FirmId, ct);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.EngagementId && x.FirmId == actor.FirmId && x.PracticeClientId == r.ClientId, ct);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.PeriodId && x.FirmId == actor.FirmId && x.ClientId == r.ClientId, ct);
    if (client is null || engagement is null || period is null) return Denied();
    var currentGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == r.ClientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var blockers = r.Problems.ToList();
    object? sourceSnapshot = null;
    if (r.Status == "STALE") blockers.Add("This evidence is explicitly stale. Prepare a new revision before relying on it.");
    if (r.Generation is null or < 1 || currentGeneration is null or < 1 || r.Generation != currentGeneration)
      blockers.Add("The retained input generation is missing or differs from the current client generation. Historical review is not current input proof.");
    var source = new AnalysisSource("GENERATION_BOUND", null, null, null, null, r.Generation is > 0 && r.Generation == currentGeneration,
      "This record retains period and client-generation inputs. It does not retain an exact TB or GL source identity.");
    if (r.ReconciliationId is { } reconciliationId)
    {
      var rec = await ReconciliationWorkspaceQuery.GetAsync(db, actor, reconciliationId, ct: ct);
      if (!rec.Succeeded || rec.Value!.ClientId != r.ClientId || rec.Value.EngagementId != r.EngagementId || rec.Value.PeriodId != r.PeriodId)
        source = new("UNAVAILABLE", null, null, Digest(r.SourceHash), null, false, "The exact source reconciliation is unavailable in this reporting context.");
      else
      {
        var v = rec.Value;
        sourceSnapshot = v.ReviewBasis;
        var current = v.SourceAvailable && !v.IsStale && v.LatestProof is { MatchesCurrentInputs: true, IsReconciled: true } &&
          v.Status is "RECONCILED" or "APPROVED" && Digest(r.SourceHash) is not null && Digest(r.SourceHash) == v.CurrentSourceHash && r.Generation == v.CurrentGeneration;
        source = new(v.SourceKind, v.SourceId, v.Id, Digest(r.SourceHash), v.CurrentSourceHash, current,
          "The retained valuation source is compared with the exact reconciliation, complete item proof and current accepted/sealed source.");
      }
      if (!source.CurrentChecksPass) blockers.Add("The retained reconciliation, complete proof, source digest or generation no longer passes current checks. Prepare new source-bound evidence.");
    }
    if (r.BatchId is { } batchId)
    {
      var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
        x.EngagementId == r.EngagementId && x.Id == batchId && x.PeriodId == r.PeriodId, ct);
      var transaction = await db.GeneralLedgerTransactions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
        x.EngagementId == r.EngagementId && x.Id == r.TransactionId && x.ImportBatchId == batchId, ct);
      sourceSnapshot = new { batch, transaction };
      source = new("GENERAL_LEDGER", batch?.Id, null, null, Digest(batch?.NormalizedDatasetDigest), false,
        "This legacy flag retains a batch and transaction identity, but no original source digest or input generation. Current batch contents cannot prove its original input binding.");
      blockers.Add("The flag has no retained source digest or input generation. Revalidate it before relying on the historical disposition.");
      if (batch is null || batch.SourceKind != "GL" || batch.Status != "SEALED" || batch.Currency != period.Currency || transaction is null || transaction.Currency != period.Currency)
        blockers.Add("The exact sealed GL batch and flagged transaction are unavailable or differ from the reporting currency.");
    }
    AdjustmentJournal? relatedJournal = null;
    if (r.JournalId is { } journalId)
    {
      relatedJournal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == journalId && x.FirmId == actor.FirmId &&
        x.ClientId == r.ClientId && x.EngagementId == r.EngagementId && x.Status != "Void", ct);
      if (relatedJournal is null || source.Kind == "TRIAL_BALANCE" && source.Id.HasValue && relatedJournal.BaseDatasetId != source.Id)
        return Denied(); // Never publish a malformed related identity or another source's proposed adjustment.
    }
    var completeLinks = await db.AccountingEvidenceAuditLinks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
      x.EngagementId == r.EngagementId && x.EvidenceKind == kind && x.EvidenceId == id).OrderBy(x => x.Id).Take(MaximumLinks + 1).ToListAsync(ct);
    if (completeLinks.Count > MaximumLinks)
      return CommandResult<AccountingAnalysisReview>.Fail(ErrorCodes.GateBlocked, "The complete audit-link set exceeds the supported inspection bound. No partial eligibility is shown.");
    var resultIds = completeLinks.Select(x => x.AuditProcedureResultId).ToArray();
    var results = await db.AuditProcedureResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
      x.EngagementId == r.EngagementId && resultIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var workpaperIds = results.Values.Where(x => x.WorkpaperId.HasValue).Select(x => x.WorkpaperId!.Value).ToArray();
    var papers = await db.Workpapers.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
      x.EngagementId == r.EngagementId && workpaperIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var procedureIds = results.Values.Select(x => x.AuditProcedureId).ToArray();
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == r.ClientId &&
      x.EngagementId == r.EngagementId && procedureIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
    var links = new List<AnalysisProcedureLink>();
    foreach (var link in completeLinks)
    {
      if (!results.TryGetValue(link.AuditProcedureResultId, out var result) || result.WorkpaperId is not { } paperId ||
        !papers.TryGetValue(paperId, out var paper) || !procedures.TryGetValue(result.AuditProcedureId, out var procedure) || paper.ProcedureId != procedure.Id)
        return Denied();
      var current = result.Status == "REVIEWED" && currentGeneration is not null && result.InputGeneration == currentGeneration &&
        result.ReviewedByUserId is not null && result.ReviewedByUserId != result.PreparedByUserId && result.ReviewedAt is not null &&
        procedure.Status == "REVIEWED" && procedure.CurrentResultRevision == result.Revision;
      links.Add(new(link.Id, result.Id, paperId, result.Status, result.InputGeneration, current, link.LinkedByUserId, link.CreatedAt));
    }
    var inputsCurrent = blockers.Count == 0 && source.CurrentChecksPass;
    var reviewedLink = links.Any(x => x.IsCurrentReviewedResult);
    if (!reviewedLink) blockers.Add("A same-engagement audit procedure result with an independent current-generation review is not linked.");
    if (r.Status is "APPROVED" or "CLEARED" or "NOT_AN_ISSUE" && (r.Reviewer is null || r.ReviewedAt is null || r.Creator is null || r.Reviewer == r.Creator))
      blockers.Add("An independent retained reviewer decision is unavailable. No current approval is inferred.");
    var view = new AccountingAnalysisReview(r.Id, kind, r.ClientId, client.LegalName, r.EngagementId, engagement.ServiceRoute,
      period.Id, period.PeriodCode, period.Basis, period.Currency, r.Area, r.Status, r.Version, r.Generation, currentGeneration, inputsCurrent,
      source, Digest(r.AssumptionsHash), Digest(r.ReplayHash), r.ReplayMatches, r.JournalId, r.Creator, r.CreatedAt, r.Reviewer, r.ReviewedAt,
      r.Amounts, r.Details, blockers, links.Skip(page * 25).Take(25).ToArray(), links.Count, page, links.Count > (page + 1) * 25, reviewedLink, "");
    var hash = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, view, r.Snapshot,
      sourceSnapshot, relatedJournal, completeLinks, results = results.Values.OrderBy(x => x.Id), papers = papers.Values.OrderBy(x => x.Id),
      procedures = procedures.Values.OrderBy(x => x.Id), period.Revision, period.Status }));
    return CommandResult<AccountingAnalysisReview>.Ok(view with { ReviewBasis = hash });
  }
  private static string Exact(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
  private static string? Digest(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit) ? value.ToLowerInvariant() : null;
  private static CommandResult<AccountingAnalysisReview> Denied() => CommandResult<AccountingAnalysisReview>.Fail(ErrorCodes.ScopeDenied, "This accounting evidence is unavailable in the current scope.");
}
