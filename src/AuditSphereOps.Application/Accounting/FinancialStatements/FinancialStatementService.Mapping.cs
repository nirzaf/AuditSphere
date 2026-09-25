using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class FinancialStatementService
{
  public static async Task<CommandResult<Guid>> CreateMappingVersionAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    CreateMappingVersionRequest request,
    CancellationToken ct = default)
  {
    var invalid = ValidateMappingRequest(request);
    if (invalid is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, invalid);

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.DatasetId, ct);
    if (dataset is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (dataset.ValidationStatus != "Accepted" || !dataset.Balanced || dataset.ControlTotal != 0m)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Mappings require an accepted balanced dataset.");

    var auth = await AuthorizeAsync(db, actor, dataset.FirmId, dataset.ClientId, dataset.EngagementId, PreparerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    Guid? chartVersionId = request.ClientChartVersionId;
    if (db is IClientAccountingDbContext typed)
    {
      var approvedCharts = await typed.ClientChartVersions.AsNoTracking().Where(x =>
        x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
      if (chartVersionId is null && approvedCharts.Count > 0)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
          "An approved client chart version is required for mapping applicability.");
      if (chartVersionId is { } requestedChartId)
      {
        var chart = approvedCharts.SingleOrDefault(x => x.Id == requestedChartId);
        if (chart is null)
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
            "The mapping chart must be an approved version in the same client scope.");
        if (!DateOnly.TryParse(request.PeriodStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodStart) ||
            !DateOnly.TryParse(request.PeriodEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd) ||
            chart.EffectiveFrom > periodStart || chart.EffectiveTo is { } chartEnd && chartEnd < periodEnd)
          return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
            "The approved client chart is not effective for the mapping period.");
      }
    }
    var taxonomyError = await ValidateApprovedTaxonomyAsync(db, dataset.FirmId, request.TaxonomyVersion, request.Allocations, ct);
    if (taxonomyError is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, taxonomyError);

    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == dataset.Id)
      .Select(x => new SourceBalance(x.AccountCode, x.Amount))
      .ToListAsync(ct);
    var mappingError = ValidateAllocations(rows, request.Allocations);
    if (mappingError is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingIncomplete, mappingError);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var client = await LockClientAsync(db, actor.FirmId, dataset.ClientId, ct);
    if (client is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");

    var version = (await db.MappingVersions
      .Where(x => x.FirmId == actor.FirmId && x.EngagementId == dataset.EngagementId)
      .Select(x => (long?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var mapping = new MappingVersion
    {
      Id = Guid.CreateVersion7(), FirmId = dataset.FirmId, ClientId = dataset.ClientId,
      EngagementId = dataset.EngagementId, DatasetId = dataset.Id, ClientChartVersionId = chartVersionId, Version = version,
      Generation = client.InputGeneration, TaxonomyVersion = request.TaxonomyVersion.Trim(),
      PeriodStart = request.PeriodStart.Trim(), PeriodEnd = request.PeriodEnd.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.MappingVersions.Add(mapping);
    foreach (var input in request.Allocations)
      db.MappingAllocations.Add(ToAllocation(mapping, input));

    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(mapping.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "The mapping version identity changed; reload the current dataset.");
    }
  }

  public static async Task<CommandResult> ApproveMappingAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid mappingVersionId,
    long expectedVersion,
    CancellationToken ct = default)
  {
    if (mappingVersionId == Guid.Empty || expectedVersion < 1)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A mapping version and positive expected version are required.");

    var snapshot = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == mappingVersionId && x.FirmId == actor.FirmId, ct);
    if (snapshot is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, snapshot.FirmId, snapshot.ClientId, snapshot.EngagementId, ReviewerRoles.ToArray(), ct);
    if (!auth.Succeeded)
      return auth;
    if (snapshot.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The mapping preparer cannot approve the same mapping version.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var client = await LockClientAsync(db, actor.FirmId, snapshot.ClientId, ct);
    if (client is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var mapping = await db.MappingVersions
      .FromSqlInterpolated($"SELECT * FROM mapping_versions WHERE id = {mappingVersionId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (mapping is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (mapping.Version != expectedVersion)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The mapping version changed; reload it.");
    if (mapping.Status == AccountingPackageStates.MappingApproved)
      return CommandResult.Ok();
    if (mapping.Status != AccountingPackageStates.MappingDraft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft mapping can be approved.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == mapping.DatasetId && x.FirmId == mapping.FirmId, ct);
    if (dataset is null || dataset.ClientId != mapping.ClientId || dataset.EngagementId != mapping.EngagementId ||
        dataset.ValidationStatus != "Accepted" || !dataset.Balanced || dataset.ControlTotal != 0m)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The mapping dataset is no longer eligible.");
    if (mapping.ClientChartVersionId is { } chartVersionId && db is IClientAccountingDbContext typedCharts)
    {
      var chart = await typedCharts.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == mapping.FirmId && x.ClientId == mapping.ClientId && x.Id == chartVersionId &&
        x.Status == AccountingWorkflowStates.Approved, ct);
      if (chart is null || !DateOnly.TryParse(mapping.PeriodStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodStart) ||
          !DateOnly.TryParse(mapping.PeriodEnd, CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd) ||
          chart.EffectiveFrom > periodStart || chart.EffectiveTo is { } chartEnd && chartEnd < periodEnd)
        return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid,
          "The approved client chart applicability changed; create a new mapping version.");
    }
    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == mapping.DatasetId)
      .Select(x => new SourceBalance(x.AccountCode, x.Amount)).ToListAsync(ct);
    var allocations = await db.MappingAllocations.AsNoTracking()
      .Where(x => x.FirmId == mapping.FirmId && x.MappingVersionId == mapping.Id)
      .Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode,
        x.StatementSection, x.Fraction, x.Rationale, x.AuditArea)).ToListAsync(ct);
    var mappingError = ValidateAllocations(rows, allocations);
    if (mappingError is not null)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, mappingError);
    var taxonomyError = await ValidateApprovedTaxonomyAsync(db, mapping.FirmId, mapping.TaxonomyVersion, allocations, ct);
    if (taxonomyError is not null)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, taxonomyError);

    client.InputGeneration++;
    mapping.Generation = client.InputGeneration;
    mapping.Status = AccountingPackageStates.MappingApproved;
    mapping.ApprovedByUserId = actor.UserId;
    mapping.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static MappingAllocation ToAllocation(MappingVersion mapping, MappingAllocationInput input) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = mapping.FirmId, ClientId = mapping.ClientId,
    EngagementId = mapping.EngagementId, MappingVersionId = mapping.Id,
    SourceAccountCode = input.SourceAccountCode.Trim(), DestinationCode = input.DestinationCode.Trim(),
    StatementSection = input.StatementSection.Trim().ToUpperInvariant(),
    AuditArea = string.IsNullOrWhiteSpace(input.AuditArea) ? null : input.AuditArea.Trim(),
    Fraction = MoneyPolicy.Normalize(input.Fraction),
    ResidualPolicy = input.ResidualPolicy.Trim().ToUpperInvariant(),
    Rationale = input.Rationale.Trim(),
    CreatedAt = mapping.CreatedAt
  };

  private static string? ValidateMappingRequest(CreateMappingVersionRequest request)
  {
    if (request.DatasetId == Guid.Empty || string.IsNullOrWhiteSpace(request.TaxonomyVersion) ||
        string.IsNullOrWhiteSpace(request.PeriodStart) || string.IsNullOrWhiteSpace(request.PeriodEnd))
      return "Dataset, taxonomy and reporting period are required.";
    if (!ValidDate(request.PeriodStart) || !ValidDate(request.PeriodEnd) || string.CompareOrdinal(request.PeriodStart, request.PeriodEnd) > 0)
      return "Reporting period must be an ordered ISO date range.";
    return request.Allocations.Count == 0 ? "At least one mapping allocation is required." : null;
  }

  private static string? ValidateAllocations(
    IReadOnlyCollection<SourceBalance> rows,
    IReadOnlyCollection<MappingAllocationInput> allocations)
  {
    var sourceAmounts = rows.Select(row => (Account: row.AccountCode.Trim(), Amount: row.Amount)).ToList();
    var accountSet = sourceAmounts.Select(x => x.Account).ToHashSet(StringComparer.Ordinal);
    var normalized = allocations.Select(x => new MappingAllocationInput(
      x.SourceAccountCode.Trim(), x.DestinationCode.Trim(), x.StatementSection.Trim().ToUpperInvariant(),
      MoneyPolicy.Normalize(x.Fraction), x.Rationale.Trim(), string.IsNullOrWhiteSpace(x.AuditArea) ? null : x.AuditArea.Trim())).ToList();
    if (normalized.Any(x => x.SourceAccountCode.Length == 0 || x.DestinationCode.Length == 0 ||
        x.StatementSection.Length == 0 || x.Rationale.Length == 0 || x.Fraction <= 0m || x.Fraction > 1m))
      return "Each allocation needs a positive fraction, destination, statement section and rationale.";
    if (normalized.Any(x => !string.Equals(x.ResidualPolicy, "LAST_DESTINATION", StringComparison.Ordinal)))
      return "Only the deterministic LAST_DESTINATION residual policy is supported.";
    if (normalized.Any(x => !accountSet.Contains(x.SourceAccountCode)))
      return "A mapping allocation references an account outside the selected dataset.";
    if (normalized.GroupBy(x => (x.SourceAccountCode, x.DestinationCode)).Any(x => x.Count() > 1))
      return "A source account and destination may occur only once in a mapping version.";
    foreach (var source in sourceAmounts.Where(x => x.Amount != 0m).Select(x => x.Account).Distinct(StringComparer.Ordinal))
    {
      var sum = normalized.Where(x => x.SourceAccountCode == source).Sum(x => x.Fraction);
      if (MoneyPolicy.Normalize(sum) != 1m)
        return $"Non-zero account {source} must be allocated exactly 100%.";
    }
    return null;
  }
}
