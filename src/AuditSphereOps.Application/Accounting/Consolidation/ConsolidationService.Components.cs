using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

internal sealed record ConsolidationComponentCurrentness(string State, string? BlockerCode, string? EvidenceHash);

internal sealed record ComponentSourceValidation(CommandResult ApprovalReadiness,
  ConsolidationComponentCurrentness Currentness);

internal static class ConsolidationComponentCurrentnessStates
{
  internal const string Current = "CURRENT";
  internal const string Stale = "STALE";
  internal const string Unavailable = "UNAVAILABLE";
}

public static partial class ConsolidationService
{
  public static async Task<CommandResult<Guid>> SubmitComponentAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationComponentRequest request,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Draft)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "The perimeter is no longer collecting components.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    var advancedMethod = AdvancedConsolidationMethods.All.Contains(scope.Method);
    if ((!advancedMethod && request.OwnershipPercent != 100m) || request.OwnershipPercent is < 0m or > 100m ||
        !request.ControlMethod.Equals("CONTROLLED", StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The restricted profile requires a fully-owned controlled component.");
    var membership = await ConsolidationScopeGuards.ApprovedMembershipsOverlappingPeriod(
      db, actor.FirmId, scope.GroupId, scope.PeriodId).AnyAsync(x => x.ClientId == request.ClientId, ct);
    if (!membership)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The component legal entity is not in the approved perimeter.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.PackageId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The component package is outside the requested scope.");
    if (string.IsNullOrWhiteSpace(request.PeriodBasis) || string.IsNullOrWhiteSpace(request.TaxonomyVersion) ||
        string.IsNullOrWhiteSpace(request.MappingVersion))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A component submission must identify its period basis, taxonomy version and mapping version.");
    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only an approved component package can enter consolidation.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && package.Currency != scope.ReportingCurrency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The restricted profile accepts only same-currency component packages.");
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
    {
      var policy = scope.TranslationPolicyVersionId is { } policyId
        ? await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == policyId &&
            x.Status == AccountingWorkflowStates.Approved, ct)
        : null;
      if (policy is null || (package.Currency != policy.FunctionalCurrency && package.Currency != scope.ReportingCurrency))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The component currency is outside the approved translation policy.");
    }
    if (!string.Equals(request.TaxonomyVersion.Trim(), package.TaxonomyVersion, StringComparison.Ordinal) ||
        !Guid.TryParse(request.MappingVersion.Trim(), out var mappingVersionId) || mappingVersionId != package.MappingVersionId)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The component taxonomy and mapping must match the selected financial package.");
    if (await db.ConsolidationComponents.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This legal entity already has a component submission for the perimeter.");
    var component = new ConsolidationComponent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ClientId = request.ClientId, EngagementId = request.EngagementId, PackageId = package.Id,
      PackageHash = package.CalculationHash, PeriodBasis = request.PeriodBasis.Trim(),
      TaxonomyVersion = request.TaxonomyVersion.Trim(), MappingVersion = request.MappingVersion.Trim(),
      Currency = package.Currency, OwnershipPercent = MoneyPolicy.Normalize(request.OwnershipPercent), ControlMethod = request.ControlMethod.Trim().ToUpperInvariant(),
      SubmittedByUserId = actor.UserId, SubmittedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationComponents.Add(component);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(component.Id);
  }

  /// <summary>Legacy compatibility surface; component pins can only be corrected in a new scope version.</summary>
  public static Task<CommandResult<Guid>> ReplaceComponentAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid oldComponentId, ConsolidationComponentRequest request,
    CancellationToken ct = default)
  {
    return Task.FromResult(CommandResult<Guid>.Fail(ErrorCodes.ProtectedState,
      "Component pins are immutable. Create a new scope version and submit a newly reviewed component package."));
  }

  public static async Task<CommandResult> ApproveComponentAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid componentId,
    CancellationToken ct = default)
  {
    var component = await db.ConsolidationComponents.SingleOrDefaultAsync(x => x.Id == componentId && x.FirmId == actor.FirmId, ct);
    if (component is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, component.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var componentScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == component.ScopeVersionId && x.GroupId == component.GroupId, ct);
    if (componentScope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var approvalReadiness = await GetComponentApprovalReadinessAsync(db, actor, componentScope, [component], ct);
    var readiness = approvalReadiness[component.Id];
    if (!readiness.Succeeded)
      return readiness;

    component.Status = AccountingWorkflowStates.Approved;
    component.ApprovedByUserId = actor.UserId;
    component.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  internal static async Task<IReadOnlyDictionary<Guid, CommandResult>> GetComponentApprovalReadinessAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeVersion scope,
    IReadOnlyCollection<ConsolidationComponent> components, CancellationToken ct = default)
  {
    var results = new Dictionary<Guid, CommandResult>();
    var rows = components.DistinctBy(x => x.Id).ToArray();
    if (rows.Length == 0)
      return results;

    var stalePerimeter = CommandResult.Fail(ErrorCodes.GenerationStale,
      "The group perimeter changed; create a new scope version.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
    {
      foreach (var component in rows)
        results[component.Id] = stalePerimeter;
      return results;
    }

    var candidates = new List<ConsolidationComponent>(rows.Length);
    foreach (var component in rows)
    {
      if (component.Status != AccountingWorkflowStates.Submitted)
        results[component.Id] = CommandResult.Fail(ErrorCodes.ProtectedState,
          "Only a submitted component can be approved.");
      else if (component.SubmittedByUserId == actor.UserId)
        results[component.Id] = CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
          "The component preparer cannot approve the same submission.");
      else
        candidates.Add(component);
    }

    var sourceValidation = await ValidateComponentSourcesAsync(db, actor, scope, candidates, ct);
    foreach (var component in candidates)
      results[component.Id] = sourceValidation[component.Id].ApprovalReadiness;

    return results;
  }

  internal static async Task<IReadOnlyDictionary<Guid, ConsolidationComponentCurrentness>> GetComponentCurrentnessAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeVersion scope,
    IReadOnlyCollection<ConsolidationComponent> components, CancellationToken ct = default)
  {
    var results = new Dictionary<Guid, ConsolidationComponentCurrentness>();
    var rows = components.DistinctBy(x => x.Id).ToArray();
    if (rows.Length == 0)
      return results;

    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
    {
      foreach (var component in rows)
        results[component.Id] = new(ConsolidationComponentCurrentnessStates.Stale, "PERIMETER_STALE", null);
      return results;
    }

    var approved = new List<ConsolidationComponent>(rows.Length);
    foreach (var component in rows)
    {
      if (component.Status != AccountingWorkflowStates.Approved)
        results[component.Id] = new(ConsolidationComponentCurrentnessStates.Unavailable, "COMPONENT_APPROVAL_REQUIRED", null);
      else
        approved.Add(component);
    }

    var sourceValidation = await ValidateComponentSourcesAsync(db, actor, scope, approved, ct);
    foreach (var component in approved)
      results[component.Id] = sourceValidation[component.Id].Currentness;

    return results;
  }

  private static async Task<IReadOnlyDictionary<Guid, ComponentSourceValidation>> ValidateComponentSourcesAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeVersion scope,
    IReadOnlyCollection<ConsolidationComponent> components, CancellationToken ct)
  {
    var results = new Dictionary<Guid, ComponentSourceValidation>();
    if (components.Count == 0)
      return results;

    static ComponentSourceValidation Validation(CommandResult readiness, string state, string? blocker = null, string? evidenceHash = null) =>
      new(readiness, new ConsolidationComponentCurrentness(state, blocker, evidenceHash));

    static string EvidenceHash(params string[] values) => Hashing.Sha256Hex(string.Join('\u001f', values));

    var externalComponents = components.Where(x => x.SourceType == ConsolidationComponentSources.ExternalPack).ToArray();
    foreach (var component in externalComponents.Where(x => x.ExternalComponentPackId is null))
      results[component.Id] = Validation(
        CommandResult.Fail(ErrorCodes.GenerationStale, "The external component source is incomplete; resubmit the pack."),
        ConsolidationComponentCurrentnessStates.Unavailable, "SOURCE_UNAVAILABLE");

    var externalPackIds = externalComponents.Where(x => x.ExternalComponentPackId.HasValue)
      .Select(x => x.ExternalComponentPackId!.Value).Distinct().ToArray();
    var externalPacks = externalPackIds.Length == 0
      ? new List<ExternalComponentPack>()
      : await db.ExternalComponentPacks.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && externalPackIds.Contains(x.Id) && x.GroupId == scope.GroupId &&
          x.ScopeVersionId == scope.Id)
        .ToListAsync(ct);
    var packsById = externalPacks.ToDictionary(x => x.Id);
    var approvedPacks = externalPacks.Where(x => x.Status == ExternalComponentPackStates.Approved &&
      x.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled).ToArray();
    var approvedPackIds = approvedPacks.Select(x => x.Id).ToArray();
    var externalLines = approvedPackIds.Length == 0
      ? new List<ExternalComponentPackLine>()
      : await db.ExternalComponentPackLines.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
          approvedPackIds.Contains(x.ExternalComponentPackId))
        .ToListAsync(ct);
    var linesByPackId = externalLines.GroupBy(x => x.ExternalComponentPackId)
      .ToDictionary(x => x.Key, x => (IReadOnlyCollection<ExternalComponentPackLine>)x.ToArray());

    foreach (var component in externalComponents.Where(x => x.ExternalComponentPackId.HasValue))
    {
      var packId = component.ExternalComponentPackId!.Value;
      if (!packsById.TryGetValue(packId, out var externalPack))
      {
        results[component.Id] = Validation(
          CommandResult.Fail(ErrorCodes.GenerationStale, "The external component pack changed; resubmit the current approved pack."),
          ConsolidationComponentCurrentnessStates.Unavailable, "SOURCE_UNAVAILABLE");
        continue;
      }
      if (externalPack.Status != ExternalComponentPackStates.Approved ||
          externalPack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled)
      {
        results[component.Id] = Validation(
          CommandResult.Fail(ErrorCodes.GenerationStale, "The external component pack changed; resubmit the current approved pack."),
          ConsolidationComponentCurrentnessStates.Unavailable, "SOURCE_APPROVAL_REQUIRED");
        continue;
      }
      if (externalPack.ClientId != component.ClientId || externalPack.EngagementId != component.EngagementId ||
          externalPack.PeriodBasis != component.PeriodBasis ||
          !linesByPackId.TryGetValue(externalPack.Id, out var lines) || !ExternalPackLinesMatch(externalPack, lines) ||
          externalPack.PackDigest != component.PackageHash || externalPack.ReportingCurrency != component.Currency ||
          externalPack.TaxonomyVersion != component.TaxonomyVersion || externalPack.MappingVersion != component.MappingVersion)
      {
        results[component.Id] = Validation(
          CommandResult.Fail(ErrorCodes.GenerationStale, "The external component pack changed; resubmit the current approved pack."),
          ConsolidationComponentCurrentnessStates.Stale, "SOURCE_PIN_STALE");
        continue;
      }

      var evidenceHash = EvidenceHash(externalPack.Id.ToString("D"), externalPack.PackDigest,
        externalPack.Status, externalPack.ReconciliationStatus, externalPack.ReconciliationReference,
        externalPack.ReconciledByUserId?.ToString("D") ?? string.Empty,
        externalPack.ReconciledAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        externalPack.ApprovedByUserId?.ToString("D") ?? string.Empty,
        externalPack.ApprovedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
      results[component.Id] = Validation(CommandResult.Ok(), ConsolidationComponentCurrentnessStates.Current,
        evidenceHash: evidenceHash);
    }

    var packageComponents = components.Where(x => x.SourceType != ConsolidationComponentSources.ExternalPack).ToArray();
    foreach (var component in packageComponents.Where(x => x.PackageId is null))
      results[component.Id] = Validation(
        CommandResult.Fail(ErrorCodes.GenerationStale, "The component package is missing; resubmit the current approved package."),
        ConsolidationComponentCurrentnessStates.Unavailable, "SOURCE_UNAVAILABLE");

    var packageIds = packageComponents.Where(x => x.PackageId.HasValue).Select(x => x.PackageId!.Value).Distinct().ToArray();
    var packageFacts = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.Id))
      .Select(x => new
      {
        x.Id, x.ClientId, x.EngagementId, x.CalculationHash, x.Status, x.Currency, x.TaxonomyVersion,
        x.MappingVersionId, x.Revision, x.Generation
      })
      .ToListAsync(ct);
    var packagesById = packageFacts.ToDictionary(x => x.Id);
    var packageIdsWithReviewEvidence = packageIds.Length == 0
      ? Array.Empty<Guid>()
      : await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId))
        .Select(x => x.FinancialPackageId).Distinct().ToArrayAsync(ct);
    var packageReviews = new Dictionary<Guid, CommandResult>();
    foreach (var component in packageComponents.Where(x => x.PackageId.HasValue))
    {
      var packageId = component.PackageId!.Value;
      if (!packagesById.TryGetValue(packageId, out var package))
      {
        results[component.Id] = Validation(
          CommandResult.Fail(ErrorCodes.GenerationStale, "The component package changed; resubmit the current approved package."),
          ConsolidationComponentCurrentnessStates.Unavailable, "SOURCE_UNAVAILABLE");
        continue;
      }
      // Establish source authorization before exposing whether the retained pin still matches.
      // Preserve the established release-readiness result instead of inventing a review-stage message.
      if (!packageReviews.TryGetValue(packageId, out var packageReview))
      {
        packageReview = await FinancialPackageReviewService.RequireCurrentAsync(
          db, actor, packageId, requirePartner: true, ct);
        packageReviews[packageId] = packageReview;
      }
      if (!packageReview.Succeeded && packageReview.ErrorCode is ErrorCodes.ScopeDenied or ErrorCodes.GenerationStale)
      {
        results[component.Id] = Validation(packageReview,
          ConsolidationComponentCurrentnessStates.Unavailable, "SOURCE_UNAVAILABLE");
        continue;
      }
      if (package.ClientId != component.ClientId || package.EngagementId != component.EngagementId ||
          package.CalculationHash != component.PackageHash || package.Status != AccountingPackageStates.PackageValidated ||
          package.Currency != component.Currency || package.TaxonomyVersion != component.TaxonomyVersion ||
          !Guid.TryParse(component.MappingVersion, out var componentMappingId) || componentMappingId != package.MappingVersionId)
      {
        results[component.Id] = Validation(
          CommandResult.Fail(ErrorCodes.GenerationStale, "The component package changed; resubmit the current approved package."),
          ConsolidationComponentCurrentnessStates.Stale, "SOURCE_PIN_STALE");
        continue;
      }

      if (!packageReview.Succeeded)
      {
        var unavailable = !packageIdsWithReviewEvidence.Contains(packageId);
        results[component.Id] = Validation(packageReview,
          unavailable ? ConsolidationComponentCurrentnessStates.Unavailable : ConsolidationComponentCurrentnessStates.Stale,
          unavailable ? "SOURCE_UNAVAILABLE" : "SOURCE_REVIEW_STALE");
        continue;
      }

      var reviews = await db.FinancialPackageReviewDecisions.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == packageId)
        .OrderBy(x => x.Sequence).ThenBy(x => x.DecidedAt).ThenBy(x => x.Id)
        .ToListAsync(ct);
      var reviewEvidence = string.Join('\n', reviews.Select(x => string.Join('\u001f',
        x.Id.ToString("D"), x.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture), x.Stage, x.Decision,
        x.PackageHash, x.PackageRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        x.PackageGeneration.ToString(System.Globalization.CultureInfo.InvariantCulture), x.FinancialPackageArtifactId.ToString("D"),
        x.ArtifactVersion, x.ArtifactSha256Hex, x.PresentationRevisionId?.ToString("D") ?? string.Empty,
        x.PresentationCompositionId?.ToString("D") ?? string.Empty, x.CompositionManifestDigest ?? string.Empty,
        x.DecidedByUserId?.ToString("D") ?? string.Empty,
        x.DecidedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture))));
      var evidenceHashForPackage = EvidenceHash(packageId.ToString("D"), package.CalculationHash, package.Status,
        package.Currency, package.TaxonomyVersion, package.MappingVersionId.ToString("D"),
        package.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        package.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture), reviewEvidence);
      results[component.Id] = Validation(CommandResult.Ok(), ConsolidationComponentCurrentnessStates.Current,
        evidenceHash: evidenceHashForPackage);
    }

    return results;
  }
}
