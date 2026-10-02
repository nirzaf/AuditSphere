using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record CompletenessAccountResidual(string AccountCode, decimal Closing, decimal Movement,
  decimal? Opening, decimal MovementClosingDifference, decimal? OpeningMovementDifference);
public sealed record CompletenessResidualPage(IReadOnlyList<CompletenessAccountResidual> Items, int TotalCount, int Page, int PageSize);
public sealed record CompletenessReview(CompletenessSourceContext Context, GeneralLedgerCompletenessBridge Bridge,
  CompletenessResidualPage Residuals, string Revision, bool CanApprove, bool CanReject, string? ApprovalBlocker, string? ReviewBlocker);

public static partial class GeneralLedgerCompletenessWorkspace
{
  public const int ResidualPageSize = 50;

  internal static IQueryable<string> AccountUniverse(IClientAccountingDbContext db, GeneralLedgerCompletenessBridge bridge) =>
    db.TrialBalanceRows.Where(x => x.DatasetId == bridge.TrialBalanceDatasetId).Select(x => x.AccountCode)
      .Union(db.GeneralLedgerLines.Where(x => x.FirmId == bridge.FirmId && x.ClientId == bridge.ClientId && x.EngagementId == bridge.EngagementId && x.ImportBatchId == bridge.ImportBatchId).Select(x => x.AccountCode))
      .Union(db.TrialBalanceRows.Where(x => x.DatasetId == bridge.OpeningTrialBalanceDatasetId).Select(x => x.AccountCode));

  internal static Task<bool> MissingOpeningCoverageAsync(IClientAccountingDbContext db, GeneralLedgerCompletenessBridge bridge, CancellationToken ct) =>
    bridge.OpeningTrialBalanceDatasetId is null ? Task.FromResult(true) : AccountUniverse(db, bridge)
      .Except(db.TrialBalanceRows.Where(x => x.DatasetId == bridge.OpeningTrialBalanceDatasetId).Select(x => x.AccountCode)).AnyAsync(ct);

  public static async Task<CommandResult<CompletenessReview>> ReviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid bridgeId, int page = 1, CancellationToken ct = default)
  {
    var first = await ReadReviewAsync(db, actor, bridgeId, page, ct);
    if (!first.Succeeded) return first;
    var second = await ReadReviewAsync(db, actor, bridgeId, page, ct);
    if (!second.Succeeded) return second;
    if (first.Value!.Revision != second.Value!.Revision)
      return CommandResult<CompletenessReview>.Fail(ErrorCodes.StaleRevision, "Completeness review inputs changed. Refresh and review again.");
    return second;
  }

  private static async Task<CommandResult<CompletenessReview>> ReadReviewAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid bridgeId, int page, CancellationToken ct)
  {
    var bridge = await db.GeneralLedgerCompletenessBridges.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bridgeId && x.FirmId == actor.FirmId, ct);
    if (bridge is null) return CommandResult<CompletenessReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var context = await ContextAsync(db, actor, bridge.ImportBatchId, ct);
    if (!context.Succeeded) return CommandResult<CompletenessReview>.Fail(context.ErrorCode!, context.Message!);
    var c = context.Value!; var s = c.Source;
    if (bridge.ClientId != s.ClientId || bridge.EngagementId != s.EngagementId || bridge.PeriodId != s.PeriodId || bridge.BookId != s.BookId)
      return CommandResult<CompletenessReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (page < 1 || (long)(page - 1) * ResidualPageSize > int.MaxValue)
      return CommandResult<CompletenessReview>.Fail(ErrorCodes.Accounting.ImportRejected, "The residual page is invalid.");
    var closing = await CompatibleSources(db, actor, c, false).SingleOrDefaultAsync(x => x.Id == bridge.TrialBalanceDatasetId, ct);
    var opening = bridge.OpeningTrialBalanceDatasetId is { } openingId ? await CompatibleSources(db, actor, c, true).SingleOrDefaultAsync(x => x.Id == openingId, ct) : null;
    if (closing is null || bridge.OpeningTrialBalanceDatasetId is not null && opening is null)
      return CommandResult<CompletenessReview>.Fail(ErrorCodes.GateBlocked, "The sealed source pair is unavailable or no longer compatible.");
    var closingHash = closing.NormalizedDatasetDigest.Length == 64 ? closing.NormalizedDatasetDigest : closing.Sha256Hex;
    var openingHash = opening is null ? "" : opening.NormalizedDatasetDigest.Length == 64 ? opening.NormalizedDatasetDigest : opening.Sha256Hex;
    if (closingHash != bridge.TrialBalanceHash || s.SourceHash != bridge.GeneralLedgerHash || openingHash != bridge.OpeningTrialBalanceHash)
      return CommandResult<CompletenessReview>.Fail(ErrorCodes.ManifestMismatch, "A source digest no longer matches the retained bridge.");
    if (!SourceAcceptanceWorkspace.ValidHash(bridge.AccountResidualDigest) || !SourceAcceptanceWorkspace.ValidHash(bridge.OpeningMovementResidualDigest))
      return CommandResult<CompletenessReview>.Fail(ErrorCodes.GateBlocked, "The retained completeness proof identity is unavailable.");
    var universe = AccountUniverse(db, bridge);
    var count = await universe.CountAsync(ct);
    var codes = await universe.OrderBy(x => x).Skip((page - 1) * ResidualPageSize).Take(ResidualPageSize).ToListAsync(ct);
    var tb = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == bridge.TrialBalanceDatasetId && codes.Contains(x.AccountCode)).GroupBy(x => x.AccountCode).Select(x => new { Code = x.Key, Amount = x.Sum(r => r.Amount) }).ToDictionaryAsync(x => x.Code, x => MoneyPolicy.Normalize(x.Amount), ct);
    var gl = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == bridge.ClientId && x.EngagementId == bridge.EngagementId && x.ImportBatchId == bridge.ImportBatchId && codes.Contains(x.AccountCode)).GroupBy(x => x.AccountCode).Select(x => new { Code = x.Key, Amount = x.Sum(r => r.FunctionalAmount) }).ToDictionaryAsync(x => x.Code, x => MoneyPolicy.Normalize(x.Amount), ct);
    var ob = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == bridge.OpeningTrialBalanceDatasetId && codes.Contains(x.AccountCode)).GroupBy(x => x.AccountCode).Select(x => new { Code = x.Key, Amount = x.Sum(r => r.Amount) }).ToDictionaryAsync(x => x.Code, x => MoneyPolicy.Normalize(x.Amount), ct);
    var rows = codes.Select(code => new CompletenessAccountResidual(code, tb.GetValueOrDefault(code), gl.GetValueOrDefault(code),
      ob.TryGetValue(code, out var value) ? value : null, MoneyPolicy.Normalize(gl.GetValueOrDefault(code) - tb.GetValueOrDefault(code)),
      ob.TryGetValue(code, out var amount) ? MoneyPolicy.Normalize(tb.GetValueOrDefault(code) - amount - gl.GetValueOrDefault(code)) : null)).ToArray();
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, s.ClientId, s.EngagementId,
      ReviewerRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    var reviewBlocker = c.Blocker ?? (!auth.Succeeded ? "Current independent reviewer authority is required." :
      bridge.CreatedByUserId == actor.UserId ? "The completeness preparer cannot review the same bridge." :
      bridge.Status is not ("RECONCILED" or "UNRECONCILED") ? "This bridge already has a retained review decision." : null);
    var replaced = c.SelectedTrialBalance is { } tbSelected && tbSelected.TrialBalanceDatasetId != bridge.TrialBalanceDatasetId ||
      s.Selected is { } glSelected && glSelected.ImportBatchId != bridge.ImportBatchId;
    var missingOpening = await MissingOpeningCoverageAsync(db, bridge, ct);
    var approvalBlocker = reviewBlocker ?? (replaced ? "The current selected source pair differs from this historical bridge. Prepare the current pair." :
      bridge.Status != "RECONCILED" || bridge.AbsoluteResidual != 0m || bridge.MismatchedAccountCount != 0 ? "Account-exact residuals are not reconciled." :
      bridge.IncompleteExtract || missingOpening ? "Opening evidence or extract completeness is missing. Unknown opening is never zero." :
      bridge.OpeningMovementResidual != 0m || bridge.OpeningMovementMismatchedAccountCount != 0 || bridge.JournalExceptionCount != 0 ? "Opening/movement residuals or malformed journals block approval." : null);
    var final = await ContextAsync(db, actor, bridge.ImportBatchId, ct);
    if (!final.Succeeded) return CommandResult<CompletenessReview>.Fail(final.ErrorCode!, final.Message!);
    if (c.Revision != final.Value!.Revision) return CommandResult<CompletenessReview>.Fail(ErrorCodes.StaleRevision, "The completeness context changed. Refresh.");
    var revision = Digest(new { c.Revision, bridge, closing, opening, replaced, missingOpening, approvalBlocker, reviewBlocker });
    return CommandResult<CompletenessReview>.Ok(new(final.Value, bridge, new(rows, count, page, ResidualPageSize), revision,
      approvalBlocker is null, reviewBlocker is null, approvalBlocker, reviewBlocker));
  }

  public static Task<CommandResult> DecideAsync(IClientAccountingDbContext db, ActorContext actor, Guid bridgeId,
    bool approve, string revision, bool reviewed, CancellationToken ct = default) =>
    !reviewed || !SourceAcceptanceWorkspace.ValidHash(revision) ?
      Task.FromResult(CommandResult.Fail(ErrorCodes.StaleRevision, "Review the current completeness proof before recording a decision.")) :
      AccountingAnalysisService.ReviewGeneralLedgerCompletenessAsync(db, actor, bridgeId, approve, ct, revision, reviewed);
}
