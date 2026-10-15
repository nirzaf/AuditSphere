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
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, componentScope.GroupId, componentScope.GroupRevision, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (component.Status != AccountingWorkflowStates.Submitted)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted component can be approved.");
    if (component.SubmittedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The component preparer cannot approve the same submission.");
    if (component.SourceType == ConsolidationComponentSources.ExternalPack)
    {
      if (component.ExternalComponentPackId is not { } externalPackId)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The external component source is incomplete; resubmit the pack.");
      var externalPack = await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == externalPackId && x.GroupId == component.GroupId && x.ScopeVersionId == component.ScopeVersionId &&
        x.ClientId == component.ClientId && x.Status == ExternalComponentPackStates.Approved &&
        x.ReconciliationStatus == ExternalComponentReconciliationStates.Reconciled, ct);
      var externalLines = externalPack is null ? [] : await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == component.GroupId && x.ScopeVersionId == component.ScopeVersionId && x.ExternalComponentPackId == externalPackId).ToListAsync(ct);
      if (externalPack is null || !ExternalPackLinesMatch(externalPack, externalLines) || externalPack.PackDigest != component.PackageHash ||
          externalPack.ReportingCurrency != component.Currency || externalPack.TaxonomyVersion != component.TaxonomyVersion ||
          externalPack.MappingVersion != component.MappingVersion)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The external component pack changed; resubmit the current approved pack.");
    }
    else
    {
      if (component.PackageId is not { } packageId)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The component package is missing; resubmit the current approved package.");
      var packageHash = await db.FinancialPackages.Where(x => x.FirmId == actor.FirmId && x.Id == packageId)
        .Select(x => new { x.CalculationHash, x.Status, x.Currency, x.TaxonomyVersion, x.MappingVersionId }).SingleOrDefaultAsync(ct);
      if (packageHash is null || packageHash.CalculationHash != component.PackageHash || packageHash.Status != AccountingPackageStates.PackageValidated ||
          packageHash.Currency != component.Currency || packageHash.TaxonomyVersion != component.TaxonomyVersion ||
          !Guid.TryParse(component.MappingVersion, out var componentMappingId) || componentMappingId != packageHash.MappingVersionId)
        return CommandResult.Fail(ErrorCodes.GenerationStale, "The component package changed; resubmit the current approved package.");
      var packageReview = await FinancialPackageReviewService.RequireCurrentAsync(db, actor, packageId, requirePartner: true, ct);
      if (!packageReview.Succeeded)
        return packageReview;
    }
    component.Status = AccountingWorkflowStates.Approved;
    component.ApprovedByUserId = actor.UserId;
    component.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
