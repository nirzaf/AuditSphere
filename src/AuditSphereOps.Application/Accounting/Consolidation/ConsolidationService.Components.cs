using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

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
    var membership = await db.ClientGroupMemberships.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.ClientId == request.ClientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null, ct);
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

  public static async Task<CommandResult<Guid>> ReplaceComponentAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid oldComponentId, ConsolidationComponentRequest request,
    CancellationToken ct = default)
  {
    var oldComponent = await db.ConsolidationComponents.SingleOrDefaultAsync(x => x.Id == oldComponentId && x.FirmId == actor.FirmId, ct);
    if (oldComponent is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    if (oldComponent.ScopeVersionId != scope.Id)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The component belongs to a different consolidation scope.");

    if (oldComponent.ClientId != request.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The replacement component must belong to the same client entity.");

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

    var membership = await db.ClientGroupMemberships.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.ClientId == request.ClientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null, ct);
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

    db.ConsolidationComponents.Remove(oldComponent);

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

    var externalComponents = candidates.Where(x => x.SourceType == ConsolidationComponentSources.ExternalPack).ToArray();
    foreach (var component in externalComponents.Where(x => x.ExternalComponentPackId is null))
      results[component.Id] = CommandResult.Fail(ErrorCodes.GenerationStale,
        "The external component source is incomplete; resubmit the pack.");

    var externalPackIds = externalComponents.Where(x => x.ExternalComponentPackId.HasValue)
      .Select(x => x.ExternalComponentPackId!.Value).Distinct().ToArray();
    var externalPacks = externalPackIds.Length == 0
      ? new List<ExternalComponentPack>()
      : await db.ExternalComponentPacks.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && externalPackIds.Contains(x.Id) && x.GroupId == scope.GroupId &&
          x.ScopeVersionId == scope.Id && x.Status == ExternalComponentPackStates.Approved &&
          x.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled)
        .ToListAsync(ct);
    var packsById = externalPacks.ToDictionary(x => x.Id);
    var approvedPackIds = externalPacks.Select(x => x.Id).ToArray();
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
      if (!packsById.TryGetValue(component.ExternalComponentPackId!.Value, out var externalPack) ||
          externalPack.ClientId != component.ClientId ||
          !linesByPackId.TryGetValue(externalPack.Id, out var lines) || !ExternalPackLinesMatch(externalPack, lines) ||
          externalPack.PackDigest != component.PackageHash || externalPack.ReportingCurrency != component.Currency ||
          externalPack.TaxonomyVersion != component.TaxonomyVersion || externalPack.MappingVersion != component.MappingVersion)
      {
        results[component.Id] = CommandResult.Fail(ErrorCodes.GenerationStale,
          "The external component pack changed; resubmit the current approved pack.");
        continue;
      }
      results[component.Id] = CommandResult.Ok();
    }

    var packageComponents = candidates.Where(x => x.SourceType != ConsolidationComponentSources.ExternalPack).ToArray();
    foreach (var component in packageComponents.Where(x => x.PackageId is null))
      results[component.Id] = CommandResult.Fail(ErrorCodes.GenerationStale,
        "The component package is missing; resubmit the current approved package.");

    var packageIds = packageComponents.Where(x => x.PackageId.HasValue).Select(x => x.PackageId!.Value).Distinct().ToArray();
    var packageFacts = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.Id))
      .Select(x => new { x.Id, x.CalculationHash, x.Status, x.Currency, x.TaxonomyVersion, x.MappingVersionId })
      .ToListAsync(ct);
    var packagesById = packageFacts.ToDictionary(x => x.Id);
    var packageReviews = new Dictionary<Guid, CommandResult>();
    foreach (var component in packageComponents.Where(x => x.PackageId.HasValue))
    {
      var packageId = component.PackageId!.Value;
      if (!packagesById.TryGetValue(packageId, out var package) || package.CalculationHash != component.PackageHash ||
          package.Status != AccountingPackageStates.PackageValidated || package.Currency != component.Currency ||
          package.TaxonomyVersion != component.TaxonomyVersion ||
          !Guid.TryParse(component.MappingVersion, out var componentMappingId) || componentMappingId != package.MappingVersionId)
      {
        results[component.Id] = CommandResult.Fail(ErrorCodes.GenerationStale,
          "The component package changed; resubmit the current approved package.");
        continue;
      }

      if (!packageReviews.TryGetValue(packageId, out var packageReview))
      {
        packageReview = await FinancialPackageReviewService.RequireCurrentAsync(
          db, actor, packageId, requirePartner: true, ct);
        packageReviews[packageId] = packageReview;
      }
      results[component.Id] = packageReview;
    }

    return results;
  }
}
