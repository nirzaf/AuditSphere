using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record NativeSuccessorSourceCandidate(Guid DatasetId, Guid EngagementId, string LegalEntityKey,
  string SourceIdentityHash, string ValidationStatus, bool Eligible, string? Blocker);
public sealed record NativeSuccessorRetainedOpening(Guid OpeningId, Guid EngagementId, Guid RequestId);
public sealed record NativeSuccessorSourceCatalog(Guid ClientId, Guid PeriodId, string PeriodRevision,
  Guid? PriorPeriodId, string? PriorPeriodRevision, string? PriorPeriodCode, string? PriorPeriodStatus,
  string Origin, Guid? ExistingOpeningId, NativeSuccessorRetainedOpening? RetainedSuccessor,
  bool CanPrepare, bool CanApprove, int Page, int PageSize, int Total,
  IReadOnlyList<NativeSuccessorSourceCandidate> Sources, string? Blocker);

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  // This catalogue reuses the exact frozen source/facts resolvers. Eligibility is not preview assent.
  public static async Task<CommandResult<NativeSuccessorSourceCatalog>> SuccessorCatalogAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid periodId,
    int page = 0, int pageSize = 5, CancellationToken ct = default)
  {
    if (page < 0 || page > int.MaxValue / 5 || pageSize is < 1 or > 5)
      return SuccessorFail<NativeSuccessorSourceCatalog>("Choose a bounded candidate page.", "request.invalid");
    var auth = await Authorize(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return SuccessorFail<NativeSuccessorSourceCatalog>(auth.Message!, auth.ErrorCode);
    var target = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == periodId, ct);
    if (target is null) return SuccessorFail<NativeSuccessorSourceCatalog>("Access denied.", ErrorCodes.ScopeDenied);
    var prior = target.PriorPeriodId is { } priorId ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == priorId, ct) : null;
    var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodId == target.Id, ct);
    var proof = opening is null ? null : await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.OpeningId == opening.Id, ct);
    if (proof is not null)
    {
      auth = await SuccessorAuthorityAsync(db, actor, clientId, proof.EngagementId, false, ct);
      if (!auth.Succeeded) return SuccessorFail<NativeSuccessorSourceCatalog>(auth.Message!, auth.ErrorCode);
    }
    var query = from source in db.TrialBalanceDatasets.AsNoTracking()
      join engagement in db.Engagements.AsNoTracking() on source.EngagementId equals engagement.Id
      where source.FirmId == actor.FirmId && source.ClientId == clientId &&
        source.PeriodId == target.PriorPeriodId && target.PriorPeriodId != null &&
        source.SourceKind == NativeTrialBalanceSourceWorkspace.SourceKind &&
        engagement.FirmId == actor.FirmId && engagement.PracticeClientId == clientId
      select source;
    var total = await query.CountAsync(ct);
    var sources = await query.OrderByDescending(x => x.ImportedAt).ThenBy(x => x.Id)
      .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    var candidates = new List<NativeSuccessorSourceCandidate>();
    foreach (var source in sources)
    {
      auth = await SuccessorAuthorityAsync(db, actor, clientId, source.EngagementId, false, ct);
      if (!auth.Succeeded) return SuccessorFail<NativeSuccessorSourceCatalog>(auth.Message!, auth.ErrorCode);
      var retained = await NativeTrialBalanceSourceWorkspace.ReadSuccessorSourceAsync(db, actor.FirmId,
        clientId, source.EngagementId, source.Id, ct);
      string? blocker = retained.Succeeded ? null : retained.Message;
      if (retained.Succeeded)
      {
        var facts = await ResolveSuccessorFactsAsync(db, actor.FirmId, clientId, target.Id, retained.Value!, ct);
        if (!facts.Succeeded) blocker = facts.Message;
      }
      if (target.Status is not ("DRAFT" or "ACTIVE")) blocker ??= "The target must already be an open DRAFT or ACTIVE period.";
      if (opening is not null) blocker ??= "This target already retains an opening; inspect its original evidence.";
      candidates.Add(new(source.Id, source.EngagementId, source.LegalEntityKey, source.Sha256Hex,
        source.ValidationStatus, blocker is null, blocker));
    }
    // Action suitability reads the same gates as commands without acquiring write locks or mutating state.
    for (var i = 0; i < candidates.Count; i++)
    {
      var candidate = candidates[i];
      var readiness = await SuccessorAuthorityAsync(db, actor, clientId, candidate.EngagementId, false, ct, write: true);
      if (!readiness.Succeeded)
        candidates[i] = candidate with { Eligible = false, Blocker = readiness.Message };
    }
    var canPrepare = opening is null && target.Status is ("DRAFT" or "ACTIVE") && candidates.Any(x => x.Eligible);
    var canApprove = proof is not null && opening is { ApprovedByUserId: null } &&
      target.Status is ("DRAFT" or "ACTIVE") && target.Revision == opening.PeriodRevision &&
      opening.CreatedByUserId != actor.UserId &&
      (await SuccessorAuthorityAsync(db, actor, clientId, proof.EngagementId, true, ct, write: true)).Succeeded;
    // Read suitability is rechecked after every asynchronous write-readiness decision.
    // A lost read grant/session denies the response; a work hold only disables write actions.
    auth = await Authorize(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return SuccessorFail<NativeSuccessorSourceCatalog>(auth.Message!, auth.ErrorCode);
    foreach (var engagementId in sources.Select(x => x.EngagementId).Concat(proof is null ? [] : new[] { proof.EngagementId }).Distinct())
    {
      auth = await SuccessorAuthorityAsync(db, actor, clientId, engagementId, false, ct);
      if (!auth.Succeeded) return SuccessorFail<NativeSuccessorSourceCatalog>(auth.Message!, auth.ErrorCode);
    }
    return CommandResult<NativeSuccessorSourceCatalog>.Ok(new(clientId, target.Id,
      target.Revision.ToString(CultureInfo.InvariantCulture), target.PriorPeriodId,
      prior?.Revision.ToString(CultureInfo.InvariantCulture), prior?.PeriodCode, prior?.Status,
      opening is null ? "NONE" : proof is null ? "MANUAL" : "NATIVE_SUCCESSOR", opening?.Id,
      proof is null ? null : new(proof.OpeningId, proof.EngagementId, proof.RequestId),
      canPrepare, canApprove, page, pageSize, total, candidates,
      prior is null ? "This target has no retained predecessor period. No successor lineage is inferred."
        : prior.Status != "CLOSED" ? "The actual predecessor period must be closed with retained close evidence." : null));
  }
}
