using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private static string SuccessorIntent(ActorContext actor, NativeSuccessorOpeningRequest request) =>
    SuccessorHash(new { actor.FirmId, actor.UserId, actor.SessionEpoch, Request = request with { ReviewBasis = null, Reviewed = false } });

  private static async Task<CommandResult> SuccessorAuthorityAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, bool reviewer, CancellationToken ct, bool write = false)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, RequiredRoles: reviewer ? Reviewers : Preparers,
        InternalOnly: true, RequireProfessionalWork: write), ct);
    if (!auth.Succeeded || !write) return auth;
    if (await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.EngagementId == engagementId && x.State == "FROZEN", ct))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen.");
    return await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)
      ? auth : CommandResult.Fail(ErrorCodes.GateBlocked, "Current accepted BOOKKEEPING authority is required.");
  }

  private static bool ValidSuccessorRequest(NativeSuccessorOpeningRequest r) => r.RequestId != Guid.Empty &&
    r.ClientId != Guid.Empty && r.EngagementId != Guid.Empty && r.PeriodId != Guid.Empty && r.PriorPeriodId != Guid.Empty &&
    r.PeriodId != r.PriorPeriodId && r.NativeTrialBalanceDatasetId != Guid.Empty && r.ExpectedPeriodRevision > 0 &&
    r.ExpectedPriorPeriodRevision > 0 && r.ExpectedSourceIdentityHash is not null && IsSha256(r.ExpectedSourceIdentityHash) &&
    !string.IsNullOrWhiteSpace(r.EvidenceReference) && r.EvidenceReference.Length <= 1000 && r.EvidenceReference == r.EvidenceReference.Trim();

  private static Manifest SuccessorManifest(Guid firmId, Guid clientId, DateOnly date, SuccessorFacts facts,
    string reference, string sourceHash) => new("native-opening-v1", firmId, clientId, facts.TargetPeriodId,
      facts.TargetRevision, facts.ChartId, date, facts.Currency, reference, sourceHash,
      facts.Lines.Select(x => new ManifestLine(x.AccountCode, x.AccountName,
        decimal.Parse(x.Debit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture),
        decimal.Parse(x.Credit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture))).ToArray());
  private static string OpeningHash(Manifest manifest) => SuccessorHash(JsonSerializer.Serialize(manifest, ManifestJsonOptions));

  private static async Task<CommandResult<(SuccessorFacts Facts, Manifest Manifest, NativeSuccessorOpeningPreview Preview)>>
    BuildSuccessorAsync(IClientAccountingDbContext db, ActorContext actor, NativeSuccessorOpeningRequest request, CancellationToken ct)
  {
    var auth = await SuccessorAuthorityAsync(db, actor, request.ClientId, request.EngagementId, false, ct, write: true);
    if (!auth.Succeeded) return SuccessorFail<(SuccessorFacts, Manifest, NativeSuccessorOpeningPreview)>(auth.Message!, auth.ErrorCode);
    var source = await NativeTrialBalanceSourceWorkspace.ReadSuccessorSourceAsync(db, actor.FirmId,
      request.ClientId, request.EngagementId, request.NativeTrialBalanceDatasetId, ct);
    if (!source.Succeeded) return SuccessorFail<(SuccessorFacts, Manifest, NativeSuccessorOpeningPreview)>(source.Message!, source.ErrorCode);
    var target = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.Id == request.PeriodId, ct);
    if (target is null || target.Status is not ("DRAFT" or "ACTIVE") || target.Revision != request.ExpectedPeriodRevision ||
        source.Value!.Period.Id != request.PriorPeriodId || source.Value.Period.Revision != request.ExpectedPriorPeriodRevision ||
        source.Value.Dataset.Sha256Hex != request.ExpectedSourceIdentityHash)
      return SuccessorFail<(SuccessorFacts, Manifest, NativeSuccessorOpeningPreview)>("Refresh the exact open target and closed accepted predecessor source.", ErrorCodes.GenerationStale);
    var facts = await ResolveSuccessorFactsAsync(db, actor.FirmId, request.ClientId, request.PeriodId, source.Value, ct);
    if (!facts.Succeeded) return SuccessorFail<(SuccessorFacts, Manifest, NativeSuccessorOpeningPreview)>(facts.Message!, facts.ErrorCode);
    var f = facts.Value!;
    var manifest = SuccessorManifest(actor.FirmId, request.ClientId, target.StartDate, f, request.EvidenceReference, request.ExpectedSourceIdentityHash);
    var hash = OpeningHash(manifest);
    var basis = SuccessorHash(new { RequestHash = SuccessorIntent(actor, request), Facts = f, OpeningManifestHash = hash });
    var preview = new NativeSuccessorOpeningPreview(basis, request.ExpectedSourceIdentityHash, hash,
      target.Id, target.Revision.ToString(CultureInfo.InvariantCulture), f.PriorPeriodId,
      f.PriorRevision.ToString(CultureInfo.InvariantCulture), target.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      f.Currency, f.Basis, NativeTrialBalanceSourceWorkspace.BookScope, f.Lines, f.Controls, f.PopulationHash, true, []);
    auth = await SuccessorAuthorityAsync(db, actor, request.ClientId, request.EngagementId, false, ct, write: true);
    if (!auth.Succeeded) return SuccessorFail<(SuccessorFacts, Manifest, NativeSuccessorOpeningPreview)>(auth.Message!, auth.ErrorCode);
    return CommandResult<(SuccessorFacts, Manifest, NativeSuccessorOpeningPreview)>.Ok((f, manifest, preview));
  }

  public static async Task<CommandResult<NativeSuccessorOpeningPreview>> PreviewSuccessorAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeSuccessorOpeningRequest request, CancellationToken ct = default)
  {
    if (!ValidSuccessorRequest(request)) return SuccessorFail<NativeSuccessorOpeningPreview>("Provide exact bounded successor intent.");
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
    var result = await BuildSuccessorAsync(db, actor, request, ct);
    if (!result.Succeeded) return SuccessorFail<NativeSuccessorOpeningPreview>(result.Message!, result.ErrorCode);
    await tx.CommitAsync(ct);
    await tx.DisposeAsync();
    var fresh = await BuildSuccessorAsync(db, actor, request, ct);
    if (!fresh.Succeeded) return SuccessorFail<NativeSuccessorOpeningPreview>(fresh.Message!, fresh.ErrorCode);
    if (fresh.Value.Preview.ReviewBasis != result.Value.Preview.ReviewBasis)
      return SuccessorFail<NativeSuccessorOpeningPreview>("Successor source changed during preview.", ErrorCodes.GenerationStale);
    return CommandResult<NativeSuccessorOpeningPreview>.Ok(fresh.Value.Preview);
  }

  public static async Task<CommandResult<NativeSuccessorOpeningView>> CreateSuccessorAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeSuccessorOpeningRequest request, CancellationToken ct = default)
  {
    if (!ValidSuccessorRequest(request)) return SuccessorFail<NativeSuccessorOpeningView>("Provide exact bounded successor intent.");
    var auth = await SuccessorAuthorityAsync(db, actor, request.ClientId, request.EngagementId, false, ct, write: true);
    if (!auth.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(auth.Message!, auth.ErrorCode);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await SuccessorScopeLocksAsync(db, actor, request.ClientId, request.EngagementId, request.RequestId, ct);
    var existing = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.ActorEpoch == actor.SessionEpoch && x.RequestId == request.RequestId, ct);
    if (existing is not null)
    {
      if (existing.RequestHash != SuccessorIntent(actor, request) || !request.Reviewed ||
          existing.ClientId != request.ClientId || existing.EngagementId != request.EngagementId)
        return SuccessorFail<NativeSuccessorOpeningView>("Request already belongs to different successor intent.", ErrorCodes.IdempotencyConflict);
      var retained = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == existing.OpeningId, ct);
      var parsed = ParseSuccessorProof(retained, existing);
      if (!parsed.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(parsed.Message!, parsed.ErrorCode);
      var originalBasis = SuccessorHash(new { RequestHash = existing.RequestHash, Facts = parsed.Value!.Facts,
        OpeningManifestHash = retained.ManifestSha256 });
      if (request.ReviewBasis != originalBasis)
        return SuccessorFail<NativeSuccessorOpeningView>("Exact original preview assent is required for recovery.", ErrorCodes.IdempotencyConflict);
      await tx.CommitAsync(ct); await tx.DisposeAsync();
      return await GetSuccessorAsync(db, actor, request.ClientId, existing.OpeningId, ct);
    }
    var locked = await LockSuccessorPeriodsAsync(db, actor.FirmId, request.ClientId, request.PeriodId, ct,
      requestedPriorPeriodId: request.PriorPeriodId);
    if (!locked.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(locked.Message!, locked.ErrorCode);
    var built = await BuildSuccessorAsync(db, actor, request, ct);
    if (!built.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(built.Message!, built.ErrorCode);
    var b = built.Value;
    if (!request.Reviewed || request.ReviewBasis != b.Preview.ReviewBasis)
      return SuccessorFail<NativeSuccessorOpeningView>("Review and assent to this exact successor preview.", ErrorCodes.GenerationStale);
    if (await db.ClientOperationalOpeningBalances.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == request.PeriodId, ct))
      return SuccessorFail<NativeSuccessorOpeningView>("The target already has an opening.", ErrorCodes.IdempotencyConflict);
    var now = DateTimeOffset.UtcNow;
    var opening = new ClientOperationalOpeningBalance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, PeriodId = request.PeriodId,
      PeriodRevision = request.ExpectedPeriodRevision, AsOfDate = DateOnly.ParseExact(b.Preview.AsOfDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
      Currency = b.Facts.Currency, EvidenceReference = request.EvidenceReference, EvidenceSha256 = request.ExpectedSourceIdentityHash,
      ManifestJson = JsonSerializer.Serialize(b.Manifest), ManifestSha256 = b.Preview.ProspectiveOpeningManifestHash,
      CreatedByUserId = actor.UserId, CreatedAt = now
    };
    var proof = new SuccessorProof("native-opening-successor.v1", opening.Id, opening.ManifestSha256, actor.FirmId,
      request.ClientId, request.EngagementId, request.PriorPeriodId, request.ExpectedPriorPeriodRevision,
      request.NativeTrialBalanceDatasetId, request.ExpectedSourceIdentityHash, b.Facts);
    var json = JsonSerializer.Serialize(proof);
    db.ClientOperationalOpeningBalances.Add(opening);
    db.ClientOperationalOpeningSuccessorProofs.Add(new ClientOperationalOpeningSuccessorProof
    {
      OpeningId = opening.Id, FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PriorPeriodId = request.PriorPeriodId, PriorPeriodRevision = request.ExpectedPriorPeriodRevision,
      SourceDatasetId = request.NativeTrialBalanceDatasetId, SourceIdentityHash = request.ExpectedSourceIdentityHash,
      ProofJson = json, ProofHash = SuccessorHash(json), RequestId = request.RequestId, ActorId = actor.UserId,
      ActorEpoch = actor.SessionEpoch, RequestHash = SuccessorIntent(actor, request), CreatedAt = now
    });
    await db.SaveChangesAsync(ct);
    auth = await SuccessorAuthorityAsync(db, actor, request.ClientId, request.EngagementId, false, ct, write: true);
    if (!auth.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(auth.Message!, auth.ErrorCode);
    await tx.CommitAsync(ct); await tx.DisposeAsync();
    return await GetSuccessorAsync(db, actor, request.ClientId, opening.Id, ct);
  }

  public static async Task<CommandResult> ApproveSuccessorAsync(IClientAccountingDbContext db, ActorContext actor,
    NativeSuccessorOpeningApprovalRequest request, CancellationToken ct = default)
  {
    if (request.RequestId == Guid.Empty || !request.Reviewed) return CommandResult.Fail(ErrorCodes.GateBlocked, "Exact independent proof assent is required.");
    var auth = await SuccessorAuthorityAsync(db, actor, request.ClientId, request.EngagementId, true, ct, write: true);
    if (!auth.Succeeded) return auth;
    var discover = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.OpeningId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (discover is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await SuccessorScopeLocksAsync(db, actor, request.ClientId, request.EngagementId, request.RequestId, ct);
    var locked = await LockSuccessorPeriodsAsync(db, actor.FirmId, request.ClientId, discover.PeriodId, ct);
    if (!locked.Succeeded) return locked;
    var opening = await db.ClientOperationalOpeningBalances.FromSqlInterpolated($"SELECT * FROM client_operational_opening_balances WHERE firm_id={actor.FirmId} AND client_id={request.ClientId} AND id={request.OpeningId} FOR UPDATE").SingleAsync(ct);
    var proof = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x => x.OpeningId == opening.Id, ct);
    if (proof is null || proof.EngagementId != request.EngagementId || opening.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "An independent reviewer of the exact scoped successor is required.");
    if (opening.ManifestSha256 != request.ExpectedOpeningManifestHash || proof.ProofHash != request.ExpectedSuccessorProofHash ||
        opening.PeriodRevision != request.ExpectedPeriodRevision)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Refresh both the opening manifest and retained successor proof.");
    if (opening.ApprovedByUserId is not null)
      return opening.ApprovedByUserId == actor.UserId ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ProtectedState, "Already independently approved.");
    var target = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == opening.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (target.Status is not ("DRAFT" or "ACTIVE") || target.Revision != request.ExpectedPeriodRevision)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Only the exact open target revision can be approved.");
    var current = await CheckSuccessorCurrentAsync(db, opening, ct);
    if (!current.Succeeded) return current;
    auth = await SuccessorAuthorityAsync(db, actor, request.ClientId, request.EngagementId, true, ct, write: true);
    if (!auth.Succeeded) return auth;
    opening.ApprovedByUserId = actor.UserId; opening.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<NativeSuccessorOpeningView>> GetSuccessorAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid openingId, CancellationToken ct = default)
  {
    var proof = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x => x.OpeningId == openingId &&
      x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    if (proof is null) return SuccessorFail<NativeSuccessorOpeningView>("Access denied.", ErrorCodes.ScopeDenied);
    var auth = await SuccessorAuthorityAsync(db, actor, clientId, proof.EngagementId, false, ct);
    if (!auth.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(auth.Message!, auth.ErrorCode);
    var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == openingId && x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    var parsed = ParseSuccessorProof(opening, proof);
    if (!parsed.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(parsed.Message!, parsed.ErrorCode);
    var view = await GetAsync(db, actor, clientId, opening.PeriodId, ct);
    if (!view.Succeeded) return SuccessorFail<NativeSuccessorOpeningView>(view.Message!, view.ErrorCode);
    var current = await CheckSuccessorCurrentAsync(db, opening, ct);
    auth = await SuccessorAuthorityAsync(db, actor, clientId, proof.EngagementId, false, ct);
    return auth.Succeeded ? CommandResult<NativeSuccessorOpeningView>.Ok(new(view.Value!, proof.SourceIdentityHash,
      proof.ProofHash, proof.ProofJson, current.Succeeded, current.Succeeded ? null : current.Message)) :
      SuccessorFail<NativeSuccessorOpeningView>(auth.Message!, auth.ErrorCode);
  }

  internal static async Task<CommandResult> CheckSuccessorConsumptionAsync(IClientAccountingDbContext db,
    ActorContext actor, ClientOperationalOpeningBalance opening, CancellationToken ct)
  {
    var proof = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x => x.OpeningId == opening.Id, ct);
    if (proof is null) return CommandResult.Ok();
    var auth = await SuccessorAuthorityAsync(db, actor, opening.ClientId, proof.EngagementId, false, ct);
    return auth.Succeeded ? await CheckSuccessorCurrentAsync(db, opening, ct) : auth;
  }

  internal static Task<CommandResult> CheckSuccessorCurrentAsync(IClientAccountingDbContext db,
    ClientOperationalOpeningBalance opening, CancellationToken ct = default) =>
    NativeTrialBalanceSourceWorkspace.CheckOpeningSuccessorChainAsync(db, opening, ct);

  private static async Task SuccessorScopeLocksAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, Guid requestId, CancellationToken ct)
  {
    var key = long.Parse(SuccessorHash(new { actor.FirmId, actor.UserId, actor.SessionEpoch, requestId })[..16], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND practice_client_id={clientId} AND id={engagementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
  }

  internal static async Task<CommandResult> LockSuccessorPeriodsAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, Guid targetPeriodId, CancellationToken ct, Guid? requestedPriorPeriodId = null)
  {
    if (db.Database.CurrentTransaction is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "Owned transaction is required for successor publication.");
    if (await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={firmId} AND id={clientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var chain = new List<ClientReportingPeriod>(); var ids = new HashSet<Guid>(); var next = (Guid?)targetPeriodId;
    var proofHashes = new Dictionary<Guid, string?>();
    while (next is { } id)
    {
      if (chain.Count >= 32 || !ids.Add(id)) return CommandResult.Fail(ErrorCodes.GateBlocked, "Cyclic or over-bound period ancestry.");
      var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.Id == id, ct);
      if (period is null || chain.Count > 0 && period.EndDate >= chain[^1].StartDate)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Exact earlier scoped predecessor periods are required.");
      chain.Add(period);
      var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.PeriodId == period.Id, ct);
      var proof = opening is null ? null : await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking()
        .SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.OpeningId == opening.Id, ct);
      proofHashes.Add(period.Id, proof?.ProofHash);
      if (proof is not null)
      {
        var parsed = ParseSuccessorProof(opening!, proof);
        if (!parsed.Succeeded) return CommandResult.Fail(parsed.ErrorCode!, parsed.Message!);
        if (period.PriorPeriodId != proof.PriorPeriodId ||
            chain.Count == 1 && requestedPriorPeriodId.HasValue && proof.PriorPeriodId != requestedPriorPeriodId)
          return CommandResult.Fail(ErrorCodes.GenerationStale, "The consumed successor edge changed.");
        next = proof.PriorPeriodId;
      }
      else if (chain.Count == 1 && requestedPriorPeriodId is { } requested)
      {
        if (period.PriorPeriodId != requested)
          return CommandResult.Fail(ErrorCodes.GenerationStale, "The requested successor predecessor changed.");
        next = requested;
      }
      else
      {
        // A reviewed manual cutover terminates consumed lineage. Ordinary historical
        // PriorPeriodId links are not generated-successor evidence dependencies.
        if (chain.Count > 1 && (opening is null || opening.ApprovedByUserId is null ||
            opening.ApprovedAt is null || !ValidManifest(opening)))
          return CommandResult.Fail(ErrorCodes.GateBlocked, "The predecessor root requires its intact approved opening.");
        next = null;
      }
    }
    chain.Reverse();
    foreach (var expected in chain)
    {
      var current = expected.Id == targetPeriodId
        ? await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={firmId} AND client_id={clientId} AND id={expected.Id} FOR UPDATE").AsNoTracking().SingleAsync(ct)
        : await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={firmId} AND client_id={clientId} AND id={expected.Id} FOR SHARE").AsNoTracking().SingleAsync(ct);
      if (current.Revision != expected.Revision || current.PriorPeriodId != expected.PriorPeriodId || current.StartDate != expected.StartDate || current.EndDate != expected.EndDate)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "Period ancestry changed while fencing publication.");
      var currentProofHash = await (from opening in db.ClientOperationalOpeningBalances.AsNoTracking()
        join proof in db.ClientOperationalOpeningSuccessorProofs.AsNoTracking() on opening.Id equals proof.OpeningId
        where opening.FirmId == firmId && opening.ClientId == clientId && opening.PeriodId == expected.Id &&
          proof.FirmId == firmId && proof.ClientId == clientId
        select proof.ProofHash).SingleOrDefaultAsync(ct);
      if (currentProofHash != proofHashes[expected.Id])
        return CommandResult.Fail(ErrorCodes.GenerationStale, "Consumed successor proof changed while fencing publication.");
    }
    return CommandResult.Ok();
  }
}
