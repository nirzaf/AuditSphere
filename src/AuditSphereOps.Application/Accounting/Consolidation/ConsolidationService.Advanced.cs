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
  public static async Task<CommandResult<Guid>> RunAdvancedProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeVersion scope,
    CancellationToken ct = default)
  {
    if (!AdvancedConsolidationMethods.All.Contains(scope.Method))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "The selected scope is not an advanced consolidation profile.");
    if (!await HasMethodOwnerAcceptanceAsync(db, actor.FirmId, scope.GroupId, scope.Method, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An independently accepted method-owner capability is required before advanced execution.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; rebuild the advanced scope.");

    var schedule = await db.AdvancedConsolidationMethodSchedules.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
      x.GroupRevision == scope.GroupRevision && x.Method == scope.Method &&
      x.Status == AdvancedConsolidationMethodScheduleStates.Approved)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (schedule is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved current advanced method schedule is required before execution.");

    var componentResult = await LoadAdvancedComponentsAsync(db, actor.FirmId, scope, ct);
    if (!componentResult.Succeeded)
      return CommandResult<Guid>.Fail(componentResult.ErrorCode!, componentResult.Message!);
    var components = componentResult.Value!;
    var sourceBinding = ValidateAdvancedSourceManifest(schedule.SourceManifestJson, components);
    if (!sourceBinding.Succeeded)
      return CommandResult<Guid>.Fail(sourceBinding.ErrorCode!, sourceBinding.Message!);
    var scheduleEvidence = await ValidateAdvancedScheduleEvidenceAsync(db, scope, schedule, ct);
    if (!scheduleEvidence.Succeeded)
      return CommandResult<Guid>.Fail(scheduleEvidence.ErrorCode!, scheduleEvidence.Message!);

    if (!AdvancedConsolidationExecutionCalculator.TryCalculate(scope.Method, schedule.InputSnapshotJson,
        out var calculation, out var calculationError))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        $"The advanced current/comparative statement verification failed: {calculationError}");

    var inputManifest = BuildAdvancedInputManifest(scope, schedule, components);
    var inputDigest = Hashing.Sha256Hex(inputManifest);
    var existing = await db.AdvancedConsolidationExecutions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.Method == scope.Method && x.InputManifestDigest == inputDigest, ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);

    var execution = new AdvancedConsolidationExecution
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ScheduleId = schedule.Id, GroupRevision = scope.GroupRevision, Method = scope.Method, Framework = schedule.Framework,
      EngineVersion = AdvancedConsolidationExecutionCalculator.EngineVersion, InputManifestJson = inputManifest,
      InputManifestDigest = inputDigest, ComparativeStatementJson = calculation!.ComparativeStatementJson,
      ComparativeStatementDigest = Hashing.Sha256Hex(calculation.ComparativeStatementJson),
      CurrentStatementJson = calculation.CurrentStatementJson,
      CurrentStatementDigest = Hashing.Sha256Hex(calculation.CurrentStatementJson),
      OutputManifest = calculation.OutputManifest, OutputDigest = calculation.OutputDigest,
      ComparativeSignedTotal = calculation.ComparativeSignedTotal, CurrentSignedTotal = calculation.CurrentSignedTotal,
      Status = AdvancedConsolidationExecutionStates.Verified, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AdvancedConsolidationExecutions.Add(execution);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(execution.Id);
  }

  public static async Task<CommandResult> ApproveAdvancedExecutionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid executionId, CancellationToken ct = default)
  {
    var execution = await db.AdvancedConsolidationExecutions.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == executionId, ct);
    if (execution is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, execution.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (execution.Status != AdvancedConsolidationExecutionStates.Verified || execution.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Only a separate reviewer can approve a verified advanced execution.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == execution.ScopeVersionId && x.GroupId == execution.GroupId, ct);
    var schedule = await db.AdvancedConsolidationMethodSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == execution.ScheduleId && x.GroupId == execution.GroupId &&
      x.ScopeVersionId == execution.ScopeVersionId, ct);
    if (scope is null || schedule is null || scope.Status != AccountingWorkflowStates.Approved ||
        scope.GroupRevision != execution.GroupRevision || schedule.GroupRevision != execution.GroupRevision ||
        schedule.Method != execution.Method || schedule.Framework != execution.Framework ||
        schedule.Status != AdvancedConsolidationMethodScheduleStates.Approved ||
        !AdvancedConsolidationExecutionCalculator.TryCalculate(schedule.Method, schedule.InputSnapshotJson, out var calculation, out _))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The advanced schedule or scope changed; rerun the execution.");
    if (!await HasMethodOwnerAcceptanceAsync(db, actor.FirmId, execution.GroupId, execution.Method, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An independently accepted method-owner capability is required before advanced execution approval.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, execution.GroupId, execution.GroupRevision, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; rerun the advanced execution.");
    var componentResult = await LoadAdvancedComponentsAsync(db, actor.FirmId, scope, ct);
    if (!componentResult.Succeeded)
      return CommandResult.Fail(componentResult.ErrorCode!, componentResult.Message!);
    var sourceBinding = ValidateAdvancedSourceManifest(schedule.SourceManifestJson, componentResult.Value!);
    if (!sourceBinding.Succeeded)
      return sourceBinding;
    var scheduleEvidence = await ValidateAdvancedScheduleEvidenceAsync(db, scope, schedule, ct);
    if (!scheduleEvidence.Succeeded)
      return scheduleEvidence;
    var inputManifest = BuildAdvancedInputManifest(scope, schedule, componentResult.Value!);
    if (Hashing.Sha256Hex(inputManifest) != execution.InputManifestDigest)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The advanced component inputs changed; rerun the execution.");
    if (Hashing.Sha256Hex(calculation!.ComparativeStatementJson) != execution.ComparativeStatementDigest ||
        Hashing.Sha256Hex(calculation.CurrentStatementJson) != execution.CurrentStatementDigest ||
        calculation.OutputDigest != execution.OutputDigest || calculation.OutputManifest != execution.OutputManifest)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The advanced statement evidence changed; rerun the execution.");
    execution.Status = AdvancedConsolidationExecutionStates.Approved;
    execution.ApprovedByUserId = actor.UserId;
    execution.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateAdvancedMethodScheduleAsync(
    IClientAccountingDbContext db, ActorContext actor, AdvancedConsolidationMethodScheduleRequest request,
    CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || !AdvancedConsolidationMethods.All.Contains(request.Method.Trim().ToUpperInvariant()) ||
        !string.Equals(request.Framework.Trim(), "IFRS", StringComparison.OrdinalIgnoreCase) ||
        !TryCanonicalObject(request.SourceManifestJson, out var sourceManifest) ||
        !TryCanonicalObject(request.InputSnapshotJson, out var inputSnapshot))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An advanced method schedule needs an approved IFRS method and JSON source/input snapshots.");
    using var sourceDocument = JsonDocument.Parse(sourceManifest);
    if (!sourceDocument.RootElement.TryGetProperty("sources", out var sources) ||
        sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An advanced method schedule must identify at least one source record.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ScopeVersionId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var method = request.Method.Trim().ToUpperInvariant();
    if (scope.Method != method)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "An advanced method schedule must match the consolidation scope method.");
    var sourceDigest = Hashing.Sha256Hex(sourceManifest);
    var existing = await db.AdvancedConsolidationMethodSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.Method == method &&
      x.SourceManifestDigest == sourceDigest && x.InputSnapshotDigest == Hashing.Sha256Hex(inputSnapshot), ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);
    var schedule = new AdvancedConsolidationMethodSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      GroupRevision = scope.GroupRevision, Method = method, Framework = "IFRS",
      SourceManifestJson = sourceManifest, SourceManifestDigest = sourceDigest,
      InputSnapshotJson = inputSnapshot, InputSnapshotDigest = Hashing.Sha256Hex(inputSnapshot),
      Status = AdvancedConsolidationMethodScheduleStates.Submitted,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AdvancedConsolidationMethodSchedules.Add(schedule);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(schedule.Id);
  }

  public static async Task<CommandResult> ApproveAdvancedMethodScheduleAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scheduleId, CancellationToken ct = default)
  {
    var schedule = await db.AdvancedConsolidationMethodSchedules.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == scheduleId, ct);
    if (schedule is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, schedule.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (schedule.Status != AdvancedConsolidationMethodScheduleStates.Submitted || schedule.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "Only a separate reviewer can approve a submitted advanced method schedule.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == schedule.ScopeVersionId && x.GroupId == schedule.GroupId, ct);
    if (scope is null || scope.GroupRevision != schedule.GroupRevision || scope.Method != schedule.Method ||
        scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult.Fail(ErrorCodes.GenerationStale,
        "The consolidation perimeter changed; rebuild the advanced method schedule.");
    if (!await HasMethodOwnerAcceptanceAsync(db, actor.FirmId, scope.GroupId, schedule.Method, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An independently accepted method-owner capability is required before advanced schedule approval.");
    var componentCount = await db.ConsolidationComponents.CountAsync(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id, ct);
    if (componentCount > 0)
    {
      var componentResult = await LoadAdvancedComponentsAsync(db, actor.FirmId, scope, ct);
      if (!componentResult.Succeeded)
        return CommandResult.Fail(componentResult.ErrorCode!, componentResult.Message!);
      var sourceBinding = ValidateAdvancedSourceManifest(schedule.SourceManifestJson, componentResult.Value!);
      if (!sourceBinding.Succeeded)
        return sourceBinding;
      var scheduleEvidence = await ValidateAdvancedScheduleEvidenceAsync(db, scope, schedule, ct);
      if (!scheduleEvidence.Succeeded)
        return scheduleEvidence;
    }
    if (!AdvancedConsolidationCalculator.TryValidateScheduleInput(schedule.Method, schedule.InputSnapshotJson, out var inputError))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        $"The advanced method schedule input is invalid: {inputError}");
    schedule.Status = AdvancedConsolidationMethodScheduleStates.Approved;
    schedule.ApprovedByUserId = actor.UserId;
    schedule.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult<List<ConsolidationComponent>>> LoadAdvancedComponentsAsync(
    IClientAccountingDbContext db, Guid firmId, ConsolidationScopeVersion scope, CancellationToken ct)
  {
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id).OrderBy(x => x.Id).ToListAsync(ct);
    if (components.Count == 0 || components.Any(x => x.Status != AccountingWorkflowStates.Approved))
      return CommandResult<List<ConsolidationComponent>>.Fail(ErrorCodes.GateBlocked,
        "Every advanced profile needs approved component reporting packs.");
    foreach (var component in components)
    {
      if (component.SourceType == ConsolidationComponentSources.InternalPackage)
      {
        var package = component.PackageId is { } packageId
          ? await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == packageId, ct)
          : null;
        if (package is null || package.Status != AccountingPackageStates.PackageValidated || package.CalculationHash != component.PackageHash)
          return CommandResult<List<ConsolidationComponent>>.Fail(ErrorCodes.GenerationStale,
            "An advanced component package changed; rebuild the advanced profile.");
      }
      else
      {
        var pack = component.ExternalComponentPackId is { } packId
          ? await db.ExternalComponentPacks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == packId, ct)
          : null;
        if (pack is null || pack.Status != ExternalComponentPackStates.Approved ||
            pack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled || pack.PackDigest != component.PackageHash)
          return CommandResult<List<ConsolidationComponent>>.Fail(ErrorCodes.GenerationStale,
            "An advanced external component pack changed; rebuild the advanced profile.");
      }
    }
    return CommandResult<List<ConsolidationComponent>>.Ok(components);
  }

  private static string BuildAdvancedInputManifest(
    ConsolidationScopeVersion scope, AdvancedConsolidationMethodSchedule schedule,
    IReadOnlyCollection<ConsolidationComponent> components) =>
    JsonSerializer.Serialize(new
    {
      scope.Id, scope.GroupId, scope.GroupRevision, scope.Method, scope.ReportingCurrency,
      ScheduleId = schedule.Id, ScheduleDigest = schedule.InputSnapshotDigest,
      SourceManifestDigest = schedule.SourceManifestDigest,
      Components = components.Select(x => new { x.Id, x.ClientId, x.PackageId, x.ExternalComponentPackId, x.PackageHash, x.Currency })
    });

  private static CommandResult ValidateAdvancedSourceManifest(
    string sourceManifestJson, IReadOnlyCollection<ConsolidationComponent> components)
  {
    try
    {
      using var document = JsonDocument.Parse(sourceManifestJson);
      if (!document.RootElement.TryGetProperty("sources", out var sources) ||
          sources.ValueKind != JsonValueKind.Array || sources.GetArrayLength() == 0)
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "The advanced schedule must identify the selected component sources.");

      var expected = components.ToDictionary(x => x.Id);
      var seen = new HashSet<Guid>();
      foreach (var source in sources.EnumerateArray())
      {
        if (!TryManifestGuid(source, "componentId", out var componentId) ||
            !TryManifestText(source, "kind", out var kindText) ||
            !TryManifestGuid(source, "id", out var sourceId) ||
            !TryManifestText(source, "hash", out var hash))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Each advanced source must include componentId, kind, id and hash.");

        if (!expected.TryGetValue(componentId, out var component) || !seen.Add(componentId))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "The advanced source manifest contains a duplicate or out-of-scope component.");
        var kind = kindText.ToUpperInvariant();
        var expectedSourceId = component.SourceType == ConsolidationComponentSources.InternalPackage
          ? component.PackageId
          : component.ExternalComponentPackId;
        if (!string.Equals(kind, component.SourceType, StringComparison.Ordinal) ||
            expectedSourceId != sourceId ||
            !string.Equals(hash, component.PackageHash, StringComparison.OrdinalIgnoreCase))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "The advanced source manifest does not match the current approved component source.");
      }

      return seen.Count == expected.Count
        ? CommandResult.Ok()
        : CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "The advanced source manifest must cover every approved component exactly once.");
    }
    catch (JsonException)
    {
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "The advanced source manifest is not valid JSON.");
    }
  }

  private static async Task<CommandResult> ValidateAdvancedReviewedJournalsAsync(
    IClientAccountingDbContext db, ConsolidationScopeVersion scope,
    AdvancedConsolidationMethodSchedule schedule, CancellationToken ct)
  {
    if (schedule.Method is not (AdvancedConsolidationMethods.AcquisitionNci or
        AdvancedConsolidationMethods.OwnershipChange or AdvancedConsolidationMethods.AssetTransferElimination))
      return CommandResult.Ok();

    try
    {
      using var document = JsonDocument.Parse(schedule.SourceManifestJson);
      if (!document.RootElement.TryGetProperty("reviewedJournals", out var journals) ||
          journals.ValueKind != JsonValueKind.Array || journals.GetArrayLength() == 0)
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "This advanced method needs at least one approved reviewed consolidation journal.");

      var seen = new HashSet<Guid>();
      var expectedJournalType = schedule.Method switch
      {
        AdvancedConsolidationMethods.AcquisitionNci => "ACQUISITION_NCI",
        AdvancedConsolidationMethods.OwnershipChange => "OWNERSHIP_CHANGE",
        AdvancedConsolidationMethods.AssetTransferElimination => "ASSET_TRANSFER_ELIMINATION",
        _ => string.Empty
      };
      var allLines = new List<ConsolidationJournalLine>();
      foreach (var journalReference in journals.EnumerateArray())
      {
        if (!TryManifestGuid(journalReference, "id", out var journalId) || !seen.Add(journalId))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Advanced reviewed-journal references must contain unique journal IDs.");
        var journal = await db.ConsolidationJournals.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == scope.FirmId && x.Id == journalId && x.GroupId == scope.GroupId &&
          x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved, ct);
        if (journal is null || journal.Currency != scope.ReportingCurrency ||
            !string.Equals(journal.JournalType, expectedJournalType, StringComparison.OrdinalIgnoreCase))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "The advanced schedule references an unavailable, out-of-scope or method-mismatched approved journal.");
        var lines = await db.ConsolidationJournalLines.AsNoTracking().Where(x =>
          x.FirmId == scope.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
          x.ConsolidationJournalId == journal.Id).ToListAsync(ct);
        if (lines.Count < 2 || lines.Any(x => x.Currency != scope.ReportingCurrency) ||
            MoneyPolicy.Normalize(lines.Sum(x => x.Debit)) != journal.TotalDebits ||
            MoneyPolicy.Normalize(lines.Sum(x => x.Credit)) != journal.TotalCreditsAbs ||
            journal.TotalDebits != journal.TotalCreditsAbs)
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Every advanced reviewed journal must remain balanced and bound to the scope currency.");
        allLines.AddRange(lines);
      }
      return ValidateAdvancedReviewedJournalAmounts(schedule.Method, schedule.InputSnapshotJson, allLines);
    }
    catch (JsonException)
    {
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "The advanced source manifest is not valid JSON.");
    }
  }

  private static CommandResult ValidateAdvancedReviewedJournalAmounts(
    string method, string inputSnapshotJson, IReadOnlyCollection<ConsolidationJournalLine> lines)
  {
    try
    {
      using var document = JsonDocument.Parse(inputSnapshotJson);
      var root = document.RootElement;
      var expected = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
      switch (method)
      {
        case AdvancedConsolidationMethods.AcquisitionNci:
          if (!TryManifestDate(root, "acquisitionDate", out var acquisitionDate) ||
              !TryManifestDate(root, "controlDate", out var controlDate) ||
              !TryManifestDecimal(root, "consideration", out var consideration) ||
              !TryManifestDecimal(root, "nciAtAcquisition", out var nciAtAcquisition) ||
              !TryManifestDecimal(root, "fairValueNetAssets", out var fairValueNetAssets) ||
              !TryManifestDecimal(root, "openingReserves", out var openingReserves) ||
              !TryManifestDecimal(root, "fairValueAdjustments", out var fairValueAdjustments))
            return CommandResult.Fail(ErrorCodes.ManifestMismatch,
              "Acquisition evidence must bind its approved goodwill or bargain-purchase amount.");
          var acquisition = AdvancedConsolidationCalculator.CalculateAcquisition(new AcquisitionAccountingInput(
            acquisitionDate, controlDate, consideration, nciAtAcquisition, fairValueNetAssets,
            openingReserves, fairValueAdjustments));
          expected[acquisition.Goodwill > 0m ? "GOODWILL" : "BARGAIN_PURCHASE"] =
            MoneyPolicy.Normalize(acquisition.Goodwill > 0m ? acquisition.Goodwill : -acquisition.BargainPurchase);
          break;

        case AdvancedConsolidationMethods.OwnershipChange:
          if (!TryManifestDate(root, "effectiveDate", out var effectiveDate) ||
              !TryManifestDecimal(root, "previousOwnershipPercent", out var previousOwnership) ||
              !TryManifestDecimal(root, "newOwnershipPercent", out var newOwnership) ||
              !TryManifestDecimal(root, "consideration", out var ownershipConsideration) ||
              !TryManifestDecimal(root, "fairValueRetainedInterest", out var retainedInterest) ||
              !TryManifestDecimal(root, "carryingNetAssets", out var carryingNetAssets) ||
              !TryManifestDecimal(root, "carryingNci", out var carryingNci) ||
              !TryManifestBool(root, "controlLost", out var controlLost))
            return CommandResult.Fail(ErrorCodes.ManifestMismatch,
              "Ownership-change evidence must bind its approved movement and gain/loss amounts.");
          var ownership = AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
            effectiveDate, previousOwnership, newOwnership, ownershipConsideration, retainedInterest,
            carryingNetAssets, carryingNci, controlLost));
          expected["NCI_MOVEMENT"] = ownership.NciMovement;
          expected["OWNERSHIP_CHANGE_GAIN_LOSS"] = ownership.DisposalGainOrLoss;
          break;

        case AdvancedConsolidationMethods.AssetTransferElimination:
          if (!TryManifestDecimal(root, "unrealizedProfit", out var unrealizedProfit) ||
              !TryManifestDecimal(root, "postTransferDepreciation", out var postTransferDepreciation) ||
              !TryManifestDecimal(root, "taxRate", out var taxRate))
            return CommandResult.Fail(ErrorCodes.ManifestMismatch,
              "Asset-transfer evidence must bind its approved net-elimination amount.");
          var elimination = AdvancedConsolidationCalculator.CalculateAssetTransferElimination(
            unrealizedProfit, postTransferDepreciation, taxRate);
          expected["ASSET_TRANSFER_ELIMINATION"] = elimination.NetElimination;
          break;
      }

      foreach (var expectedLine in expected)
      {
        var matching = lines.Where(x => string.Equals(x.TaxonomyCode, expectedLine.Key, StringComparison.OrdinalIgnoreCase)).ToList();
        var actual = MoneyPolicy.Normalize(matching.Sum(x => x.Debit - x.Credit));
        if (matching.Count == 0 || actual != expectedLine.Value)
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            $"The reviewed advanced journal does not match the approved {expectedLine.Key} amount.");
      }
      return CommandResult.Ok();
    }
    catch (JsonException)
    {
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "The advanced input snapshot is not valid JSON.");
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, ex.Message);
    }
  }

  private static async Task<CommandResult> ValidateAdvancedForeignCurrencyEvidenceAsync(
    IClientAccountingDbContext db, ConsolidationScopeVersion scope,
    AdvancedConsolidationMethodSchedule schedule, CancellationToken ct)
  {
    if (scope.ExchangeRateSetVersionId is not { } rateSetId || scope.TranslationPolicyVersionId is not { } policyId ||
        scope.TranslationRateDate is not { } rateDate || string.IsNullOrWhiteSpace(scope.TranslationRateType))
      return CommandResult.Fail(ErrorCodes.ManifestMismatch,
        "Foreign-currency advanced evidence must use the scope's pinned rate set and policy.");

    try
    {
      using var input = JsonDocument.Parse(schedule.InputSnapshotJson);
      var root = input.RootElement;
      if (!TryManifestDecimal(root, "openingRate", out var openingRate) ||
          !TryManifestDecimal(root, "closingRate", out var closingRate) ||
          !TryManifestDecimal(root, "averageRate", out var averageRate) ||
          !TryManifestDecimal(root, "openingTranslationReserve", out var openingReserve) ||
          !TryManifestText(root, "functionalCurrency", out var functionalCurrency) ||
          !TryManifestText(root, "presentationCurrency", out var presentationCurrency))
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Foreign-currency advanced input must include currencies, rates and opening reserve.");

      var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == scope.FirmId && x.Id == policyId && x.Status == AccountingWorkflowStates.Approved, ct);
      var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == scope.FirmId && x.Id == rateSetId && x.Status == AccountingWorkflowStates.Approved, ct);
      if (policy is null || rateSet is null)
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Foreign-currency advanced input is outside the approved policy or rate set lineage.");
      if (
          !string.Equals(functionalCurrency, policy.FunctionalCurrency, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(presentationCurrency, policy.PresentationCurrency, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(presentationCurrency, scope.ReportingCurrency, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(scope.TranslationRateType, policy.ClosingRateRule, StringComparison.OrdinalIgnoreCase) ||
          MoneyPolicy.Normalize(openingReserve) != MoneyPolicy.Normalize(scope.OpeningTranslationReserve))
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Foreign-currency advanced input is outside the approved policy, reporting currency or opening reserve lineage.");
      var componentCurrencies = await db.ConsolidationComponents.AsNoTracking()
        .Where(x => x.FirmId == scope.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id)
        .Select(x => x.Currency).Distinct().ToListAsync(ct);
      if (componentCurrencies.Any(x => !string.Equals(x, policy.FunctionalCurrency, StringComparison.OrdinalIgnoreCase) &&
          !string.Equals(x, policy.PresentationCurrency, StringComparison.OrdinalIgnoreCase)))
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Foreign-currency advanced components must use the approved functional or presentation currency.");

      using var sourceManifest = JsonDocument.Parse(schedule.SourceManifestJson);
      if (!sourceManifest.RootElement.TryGetProperty("fxRates", out var fxRates) ||
          fxRates.ValueKind != JsonValueKind.Array || fxRates.GetArrayLength() != 3)
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Foreign-currency advanced evidence must bind opening, closing and average rate observations.");

      var expectedRates = new Dictionary<string, decimal>(StringComparer.Ordinal)
      {
        ["OPENING"] = openingRate,
        ["CLOSING"] = closingRate,
        ["AVERAGE"] = averageRate
      };
      var seenRoles = new HashSet<string>(StringComparer.Ordinal);
      foreach (var reference in fxRates.EnumerateArray())
      {
        if (!TryManifestText(reference, "role", out var role) || !TryManifestGuid(reference, "id", out var rateId) ||
            !TryManifestDate(reference, "date", out var observationDate) ||
            !TryManifestDecimal(reference, "rate", out var observedRate) ||
            !expectedRates.ContainsKey(role) || !seenRoles.Add(role))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Foreign-currency rate evidence must contain unique opening, closing and average observations.");
        var expectedRateType = role switch
        {
          "OPENING" => policy.HistoricalRateRule.Trim().ToUpperInvariant(),
          "AVERAGE" => policy.AverageRateRule.Trim().ToUpperInvariant(),
          _ => policy.ClosingRateRule.Trim().ToUpperInvariant()
        };
        if (role is "CLOSING" or "AVERAGE" && observationDate != rateDate)
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Current closing and average rate evidence must use the pinned scope date.");
        var observation = await db.ExchangeRates.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == scope.FirmId && x.Id == rateId && x.RateSetVersionId == rateSetId &&
          x.FromCurrency == functionalCurrency.ToUpperInvariant() && x.ToCurrency == presentationCurrency.ToUpperInvariant() &&
          x.RateDate == observationDate && x.RateType == expectedRateType && x.Direction == ExchangeRateDirections.Direct, ct);
        if (observation is null || observation.Rate != observedRate || observation.Rate != expectedRates[role])
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Foreign-currency rate evidence does not match the approved rate observation.");
      }

      return seenRoles.Count == expectedRates.Count
        ? CommandResult.Ok()
        : CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Foreign-currency rate evidence must cover all required rate roles.");
    }
    catch (JsonException)
    {
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "Foreign-currency advanced evidence is not valid JSON.");
    }
  }

  private static async Task<CommandResult> ValidateAdvancedScheduleEvidenceAsync(
    IClientAccountingDbContext db, ConsolidationScopeVersion scope,
    AdvancedConsolidationMethodSchedule schedule, CancellationToken ct)
  {
    var journals = await ValidateAdvancedReviewedJournalsAsync(db, scope, schedule, ct);
    if (!journals.Succeeded)
      return journals;
    if (schedule.Method == AdvancedConsolidationMethods.ForeignCurrencyReserve)
      return await ValidateAdvancedForeignCurrencyEvidenceAsync(db, scope, schedule, ct);
    if (schedule.Method != AdvancedConsolidationMethods.NestedGroup)
      return CommandResult.Ok();

    try
    {
      using var input = JsonDocument.Parse(schedule.InputSnapshotJson);
      if (!input.RootElement.TryGetProperty("components", out var inputComponents) ||
          inputComponents.ValueKind != JsonValueKind.Array || inputComponents.GetArrayLength() == 0)
        return CommandResult.Fail(ErrorCodes.ManifestMismatch, "A nested advanced schedule must identify source scopes.");
      var expectedScopes = new HashSet<Guid>();
      foreach (var component in inputComponents.EnumerateArray())
      {
        if (!TryManifestGuid(component, "sourceScopeVersionId", out var sourceScopeId) ||
            sourceScopeId == scope.Id || !expectedScopes.Add(sourceScopeId))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Nested advanced inputs must contain unique source scopes outside the target scope.");
      }

      using var sourceManifest = JsonDocument.Parse(schedule.SourceManifestJson);
      if (!sourceManifest.RootElement.TryGetProperty("nestedScopes", out var nestedScopes) ||
          nestedScopes.ValueKind != JsonValueKind.Array || nestedScopes.GetArrayLength() != expectedScopes.Count)
        return CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Nested advanced evidence must bind every source scope to an approved run.");

      var seenScopes = new HashSet<Guid>();
      foreach (var reference in nestedScopes.EnumerateArray())
      {
        if (!TryManifestGuid(reference, "scopeVersionId", out var sourceScopeId) ||
            !TryManifestGuid(reference, "runId", out var runId) ||
            !TryManifestText(reference, "runHash", out var runHash) ||
            !expectedScopes.Contains(sourceScopeId) || !seenScopes.Add(sourceScopeId))
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Nested advanced evidence contains a duplicate or out-of-scope source run.");
        var sourceScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == scope.FirmId && x.Id == sourceScopeId && x.Status == AccountingWorkflowStates.Approved, ct);
        var sourceRun = sourceScope is null ? null : await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == scope.FirmId && x.Id == runId && x.GroupId == sourceScope.GroupId &&
          x.ScopeVersionId == sourceScopeId && x.Status == AccountingWorkflowStates.Approved &&
          x.RunHash == runHash && x.ReportingCurrency == scope.ReportingCurrency, ct);
        if (sourceRun is null)
          return CommandResult.Fail(ErrorCodes.ManifestMismatch,
            "Nested advanced evidence must reference an approved run for the exact source scope and currency.");
      }

      return seenScopes.SetEquals(expectedScopes)
        ? CommandResult.Ok()
        : CommandResult.Fail(ErrorCodes.ManifestMismatch,
          "Nested advanced evidence must cover every source scope exactly once.");
    }
    catch (JsonException)
    {
      return CommandResult.Fail(ErrorCodes.ManifestMismatch, "Nested advanced evidence is not valid JSON.");
    }
  }

  private static bool TryManifestGuid(JsonElement element, string name, out Guid value)
  {
    value = Guid.Empty;
    return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
      Guid.TryParse(property.GetString(), out value);
  }

  private static bool TryManifestText(JsonElement element, string name, out string value)
  {
    value = string.Empty;
    if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
      return false;
    value = property.GetString()?.Trim() ?? string.Empty;
    return value.Length > 0;
  }

  private static bool TryManifestDecimal(JsonElement element, string name, out decimal value)
  {
    value = 0m;
    return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number &&
      property.TryGetDecimal(out value);
  }

  private static bool TryManifestDate(JsonElement element, string name, out DateOnly value)
  {
    value = default;
    return element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String &&
      DateOnly.TryParse(property.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
  }

  private static bool TryManifestBool(JsonElement element, string name, out bool value)
  {
    value = false;
    if (!element.TryGetProperty(name, out var property) || property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
      return false;
    value = property.GetBoolean();
    return true;
  }

  private static bool IsSha256(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
