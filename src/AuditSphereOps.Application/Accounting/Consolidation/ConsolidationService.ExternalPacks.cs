using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  public static async Task<CommandResult<Guid>> SubmitExternalComponentPackAsync(
    IClientAccountingDbContext db, ActorContext actor, ExternalComponentPackRequest request,
    CancellationToken ct = default)
  {
    var currency = request.ReportingCurrency.Trim().ToUpperInvariant();
    var periodStart = request.PeriodStart.Trim();
    var periodEnd = request.PeriodEnd.Trim();
    var rawHash = request.RawSourceHash.Trim().ToLowerInvariant();
    var normalizedDigest = request.NormalizedSourceDigest.Trim().ToLowerInvariant();
    var bridgeReference = request.CompatibilityBridgeReference.Trim();
    if (request.ScopeVersionId == Guid.Empty || request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty ||
        !DateOnly.TryParseExact(periodStart, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
        !DateOnly.TryParseExact(periodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
        end < start || string.IsNullOrWhiteSpace(request.Framework) || currency.Length != 3 ||
        currency.Any(c => c is < 'A' or > 'Z') || string.IsNullOrWhiteSpace(request.PeriodBasis) ||
        string.IsNullOrWhiteSpace(request.TaxonomyVersion) || string.IsNullOrWhiteSpace(request.MappingVersion) ||
        string.IsNullOrWhiteSpace(request.SourceReference) || !IsSha256(rawHash) || !IsSha256(normalizedDigest) ||
        request.Lines is null || request.Lines.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An external component pack needs a bounded dated source, both source digests and at least one typed line.");
    if (request.Lines.Any(x => string.IsNullOrWhiteSpace(x.TaxonomyCode) || string.IsNullOrWhiteSpace(x.SourceLineReference) ||
        x.Currency.Trim().ToUpperInvariant() != currency || x.Amount != MoneyPolicy.Normalize(x.Amount)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Every external component line needs a normalized amount, source reference and the pack currency.");
    var declaredTotal = MoneyPolicy.Normalize(request.DeclaredSignedTotal);
    var lineTotal = MoneyPolicy.Normalize(request.Lines.Sum(x => x.Amount));
    if (declaredTotal != lineTotal)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "The external component declared total does not reconcile to its imported lines.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Draft)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The perimeter is no longer collecting component packs.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (!await db.ClientGroupMemberships.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
        x.ClientId == request.ClientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The external component entity is not in the approved perimeter.");
    if (!await db.Engagements.AsNoTracking().AnyAsync(x => x.Id == request.EngagementId && x.FirmId == actor.FirmId && x.PracticeClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The external component engagement is outside the requested scope.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && currency != scope.ReportingCurrency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The restricted profile accepts only same-currency external component packs.");
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
    {
      var policy = scope.TranslationPolicyVersionId is { } policyId
        ? await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
            x.Id == policyId && x.Status == AccountingWorkflowStates.Approved, ct)
        : null;
      if (policy is null || (currency != policy.FunctionalCurrency && currency != policy.PresentationCurrency))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The external component currency is outside the approved translation policy.");
    }

    var version = 1;
    ExternalComponentPack? priorPack = null;
    var bridgeRequired = false;
    if (request.PriorPackId is { } priorId)
    {
      priorPack = await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id && x.Id == priorId && x.ClientId == request.ClientId, ct);
      if (priorPack is null || priorPack.Status != ExternalComponentPackStates.Returned)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A resubmission must link to a returned external component pack.");
      if (await db.ExternalComponentPacks.AnyAsync(x => x.FirmId == actor.FirmId && x.PriorPackId == priorPack.Id, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This returned pack already has a resubmission.");
      version = priorPack.Version + 1;
      bridgeRequired = priorPack.PeriodStart != periodStart || priorPack.PeriodEnd != periodEnd ||
        !string.Equals(priorPack.PeriodBasis, request.PeriodBasis.Trim(), StringComparison.Ordinal);
      if (bridgeRequired && string.IsNullOrWhiteSpace(bridgeReference))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "A changed external component date or basis needs a documented compatibility bridge before resubmission.");
    }
    else if (await db.ExternalComponentPacks.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        x.ClientId == request.ClientId && x.Status != ExternalComponentPackStates.Returned, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This legal entity already has an active external component pack for the perimeter.");

    var packDigest = ComputeExternalPackDigest(version, periodStart, periodEnd, request.Framework, currency,
      request.PeriodBasis, request.TaxonomyVersion, request.MappingVersion, request.SourceReference, rawHash,
      normalizedDigest, declaredTotal, bridgeReference, request.Lines);
    var now = DateTimeOffset.UtcNow;
    var pack = new ExternalComponentPack
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ClientId = request.ClientId, EngagementId = request.EngagementId, PriorPackId = request.PriorPackId,
      Version = version, PeriodStart = periodStart, PeriodEnd = periodEnd, Framework = request.Framework.Trim(),
      ReportingCurrency = currency, PeriodBasis = request.PeriodBasis.Trim(), TaxonomyVersion = request.TaxonomyVersion.Trim(),
      MappingVersion = request.MappingVersion.Trim(), SourceReference = request.SourceReference.Trim(), RawSourceHash = rawHash,
      NormalizedSourceDigest = normalizedDigest, PackDigest = packDigest, DeclaredSignedTotal = declaredTotal,
      CompatibilityBridgeStatus = bridgeRequired ? ExternalComponentBridgeStates.Pending : ExternalComponentBridgeStates.None,
      CompatibilityBridgeReference = bridgeReference,
      Status = request.PriorPackId.HasValue ? ExternalComponentPackStates.Resubmitted : ExternalComponentPackStates.Submitted,
      SubmittedByUserId = actor.UserId, SubmittedAt = now
    };
    db.ExternalComponentPacks.Add(pack);
    db.ExternalComponentPackLines.AddRange(request.Lines.Select(line => new ExternalComponentPackLine
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ExternalComponentPackId = pack.Id, TaxonomyCode = line.TaxonomyCode.Trim(), Amount = line.Amount,
      Currency = currency, SourceLineReference = line.SourceLineReference.Trim()
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(pack.Id);
  }

  public static async Task<CommandResult> ReconcileExternalComponentPackAsync(
    IClientAccountingDbContext db, ActorContext actor, ExternalComponentReconciliationRequest request,
    CancellationToken ct = default)
  {
    var pack = await db.ExternalComponentPacks.SingleOrDefaultAsync(x => x.Id == request.PackId && x.FirmId == actor.FirmId, ct);
    if (pack is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, pack.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (pack.Status is not (ExternalComponentPackStates.Submitted or ExternalComponentPackStates.Resubmitted) ||
        pack.SubmittedByUserId == actor.UserId || string.IsNullOrWhiteSpace(request.EvidenceReference))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A separate reviewer must reconcile a submitted external component pack with evidence.");
    var lines = await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == pack.GroupId && x.ScopeVersionId == pack.ScopeVersionId && x.ExternalComponentPackId == pack.Id).ToListAsync(ct);
    if (!ExternalPackLinesMatch(pack, lines))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The external component pack no longer matches its imported line digest or declared total.");
    pack.ReconciledSignedTotal = MoneyPolicy.Normalize(lines.Sum(x => x.Amount));
    pack.ReconciliationStatus = ExternalComponentReconciliationStates.Reconciled;
    pack.ReconciliationReference = request.EvidenceReference.Trim();
    pack.ReconciledByUserId = actor.UserId;
    pack.ReconciledAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ReturnExternalComponentPackAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packId, string reason,
    CancellationToken ct = default)
  {
    var pack = await db.ExternalComponentPacks.SingleOrDefaultAsync(x => x.Id == packId && x.FirmId == actor.FirmId, ct);
    if (pack is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, pack.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (pack.Status is not (ExternalComponentPackStates.Submitted or ExternalComponentPackStates.Resubmitted) ||
        string.IsNullOrWhiteSpace(reason))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an active submitted pack can be returned with a reason.");
    pack.Status = ExternalComponentPackStates.Returned;
    pack.ReturnReason = reason.Trim();
    pack.ReturnedByUserId = actor.UserId;
    pack.ReturnedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveExternalComponentPackAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packId,
    CancellationToken ct = default)
  {
    var pack = await db.ExternalComponentPacks.SingleOrDefaultAsync(x => x.Id == packId && x.FirmId == actor.FirmId, ct);
    if (pack is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, pack.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (pack.Status is not (ExternalComponentPackStates.Submitted or ExternalComponentPackStates.Resubmitted) ||
        pack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled || pack.SubmittedByUserId == actor.UserId ||
        (pack.CompatibilityBridgeStatus == ExternalComponentBridgeStates.Pending && string.IsNullOrWhiteSpace(pack.CompatibilityBridgeReference)))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A separate reviewer can approve only a reconciled external component pack.");
    var lines = await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == pack.GroupId && x.ScopeVersionId == pack.ScopeVersionId && x.ExternalComponentPackId == pack.Id).ToListAsync(ct);
    if (!ExternalPackLinesMatch(pack, lines))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The external component pack changed after reconciliation.");
    pack.Status = ExternalComponentPackStates.Approved;
    if (pack.CompatibilityBridgeStatus == ExternalComponentBridgeStates.Pending)
    {
      pack.CompatibilityBridgeStatus = ExternalComponentBridgeStates.Approved;
      pack.CompatibilityBridgeApprovedByUserId = actor.UserId;
      pack.CompatibilityBridgeApprovedAt = DateTimeOffset.UtcNow;
    }
    pack.ApprovedByUserId = actor.UserId;
    pack.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> SubmitExternalComponentAsync(
    IClientAccountingDbContext db, ActorContext actor, ExternalComponentRequest request,
    CancellationToken ct = default)
  {
    var pack = await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ExternalComponentPackId &&
      x.FirmId == actor.FirmId && x.ScopeVersionId == request.ScopeVersionId, ct);
    if (pack is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId &&
      x.FirmId == actor.FirmId && x.GroupId == pack.GroupId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Draft || pack.Status != ExternalComponentPackStates.Approved ||
        pack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only an approved and reconciled external pack can enter consolidation.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (request.OwnershipPercent != 100m || !request.ControlMethod.Equals("CONTROLLED", StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The restricted profile requires a fully-owned controlled component.");
    if (!await db.ClientGroupMemberships.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
        x.ClientId == pack.ClientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The component legal entity is not in the approved perimeter.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && pack.ReportingCurrency != scope.ReportingCurrency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The restricted profile accepts only same-currency component packs.");
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
    {
      var policy = scope.TranslationPolicyVersionId is { } policyId
        ? await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
            x.Id == policyId && x.Status == AccountingWorkflowStates.Approved, ct)
        : null;
      if (policy is null || (pack.ReportingCurrency != policy.FunctionalCurrency && pack.ReportingCurrency != policy.PresentationCurrency))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The component pack currency is outside the approved translation policy.");
    }
    var lines = await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.GroupId == pack.GroupId && x.ScopeVersionId == pack.ScopeVersionId && x.ExternalComponentPackId == pack.Id).ToListAsync(ct);
    if (!ExternalPackLinesMatch(pack, lines))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The external component pack no longer matches its approved line digest.");
    if (await db.ConsolidationComponents.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.ClientId == pack.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This legal entity already has a component submission for the perimeter.");
    var component = new ConsolidationComponent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ClientId = pack.ClientId, EngagementId = pack.EngagementId, PackageId = null, ExternalComponentPackId = pack.Id,
      SourceType = ConsolidationComponentSources.ExternalPack, PackageHash = pack.PackDigest,
      PeriodBasis = pack.PeriodBasis, TaxonomyVersion = pack.TaxonomyVersion, MappingVersion = pack.MappingVersion,
      Currency = pack.ReportingCurrency, OwnershipPercent = 100m, ControlMethod = "CONTROLLED",
      SubmittedByUserId = actor.UserId, SubmittedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationComponents.Add(component);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(component.Id);
  }

  private static bool TryCanonicalObject(string json, out string canonical)
  {
    canonical = string.Empty;
    try
    {
      using var document = JsonDocument.Parse(json);
      if (document.RootElement.ValueKind != JsonValueKind.Object)
        return false;
      canonical = JsonSerializer.Serialize(document.RootElement);
      return canonical.Length <= 20000;
    }
    catch (JsonException)
    {
      return false;
    }
  }

  private static string ComputeExternalPackDigest(
    int version, string periodStart, string periodEnd, string framework, string currency,
    string periodBasis, string taxonomyVersion, string mappingVersion, string sourceReference,
    string rawSourceHash, string normalizedSourceDigest, decimal declaredSignedTotal, string compatibilityBridgeReference,
    IEnumerable<ExternalComponentPackLineRequest> lines) =>
    Hashing.Sha256Hex(string.Join('\n',
      $"v={version}", $"start={periodStart}", $"end={periodEnd}", $"framework={framework.Trim()}",
      $"currency={currency.Trim().ToUpperInvariant()}", $"basis={periodBasis.Trim()}", $"taxonomy={taxonomyVersion.Trim()}",
      $"mapping={mappingVersion.Trim()}", $"source={sourceReference.Trim()}", $"raw={rawSourceHash.Trim().ToLowerInvariant()}",
      $"normalized={normalizedSourceDigest.Trim().ToLowerInvariant()}", $"total={MoneyPolicy.Normalize(declaredSignedTotal):0.000000}",
      $"bridge={compatibilityBridgeReference.Trim()}",
      lines.OrderBy(x => x.SourceLineReference, StringComparer.Ordinal).ThenBy(x => x.TaxonomyCode, StringComparer.Ordinal)
        .Select(x => string.Join('|', x.TaxonomyCode.Trim(), MoneyPolicy.Normalize(x.Amount).ToString("0.000000", CultureInfo.InvariantCulture),
          x.Currency.Trim().ToUpperInvariant(), x.SourceLineReference.Trim()))));

  internal static bool ExternalPackLinesMatch(ExternalComponentPack pack, IReadOnlyCollection<ExternalComponentPackLine> lines)
  {
    if (lines.Count == 0 || lines.Any(x => x.Currency != pack.ReportingCurrency || x.Amount != MoneyPolicy.Normalize(x.Amount)))
      return false;
    var total = MoneyPolicy.Normalize(lines.Sum(x => x.Amount));
    if (total != pack.DeclaredSignedTotal || pack.ReconciledSignedTotal != 0m && pack.ReconciledSignedTotal != total)
      return false;
    var digest = ComputeExternalPackDigest(pack.Version, pack.PeriodStart, pack.PeriodEnd, pack.Framework,
      pack.ReportingCurrency, pack.PeriodBasis, pack.TaxonomyVersion, pack.MappingVersion, pack.SourceReference,
      pack.RawSourceHash, pack.NormalizedSourceDigest, pack.DeclaredSignedTotal,
      pack.CompatibilityBridgeReference,
      lines.Select(x => new ExternalComponentPackLineRequest(x.TaxonomyCode, x.Amount, x.Currency, x.SourceLineReference)));
    return digest == pack.PackDigest;
  }
}
