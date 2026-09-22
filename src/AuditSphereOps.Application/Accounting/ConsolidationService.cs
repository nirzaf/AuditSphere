using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientGroupRequest(string Code, string Name);

public sealed record GroupMembershipRequest(
  Guid GroupId, Guid ClientId, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
  string ControlMethod, decimal OwnershipPercent, decimal EconomicInterestPercent,
  string EvidenceReference);

public sealed record OwnershipInterestRequest(
  Guid ScopeVersionId, Guid ParentClientId, Guid ChildClientId,
  DateOnly EffectiveFrom, DateOnly? EffectiveTo, decimal OwnershipPercent,
  decimal EconomicInterestPercent, string ControlAssessment, string Method,
  string EvidenceReference);

public sealed record ConsolidationScopeRequest(
  Guid GroupId, Guid PeriodId, string ReportingCurrency, string Method, string OpeningBasis,
  Guid? ExchangeRateSetVersionId = null, Guid? TranslationPolicyVersionId = null,
  DateOnly? TranslationRateDate = null, string TranslationRateType = "", Guid? PriorScopeVersionId = null);

public sealed record ConsolidationComponentRequest(
  Guid ScopeVersionId, Guid ClientId, Guid EngagementId, Guid PackageId,
  decimal OwnershipPercent, string ControlMethod, string PeriodBasis,
  string TaxonomyVersion, string MappingVersion);

public sealed record ExternalComponentPackLineRequest(
  string TaxonomyCode, decimal Amount, string Currency, string SourceLineReference);

public sealed record ExternalComponentPackRequest(
  Guid ScopeVersionId, Guid ClientId, Guid EngagementId, string PeriodStart, string PeriodEnd,
  string Framework, string ReportingCurrency, string PeriodBasis, string TaxonomyVersion,
  string MappingVersion, string SourceReference, string RawSourceHash, string NormalizedSourceDigest,
  decimal DeclaredSignedTotal, IReadOnlyList<ExternalComponentPackLineRequest> Lines,
  Guid? PriorPackId = null, string CompatibilityBridgeReference = "");

public sealed record ExternalComponentReconciliationRequest(Guid PackId, string EvidenceReference);

public sealed record ExternalComponentRequest(Guid ScopeVersionId, Guid ExternalComponentPackId,
  decimal OwnershipPercent = 100m, string ControlMethod = "CONTROLLED");

public sealed record IntercompanyMatchRequest(
  Guid ScopeVersionId, Guid SellerClientId, Guid BuyerClientId,
  string AccountNature, string PeriodCode, string Currency, string TransactionReference,
  decimal SellerAmount, decimal BuyerAmount, decimal MatchedAmount, string EvidenceReference,
  string SellerTaxonomyCode = "", string BuyerTaxonomyCode = "", string DifferenceReason = "",
  string MatchMode = IntercompanyMatchModes.OneToOne, string MatchGroupReference = "", bool OutsidePerimeterReview = false);

public sealed record ConsolidationJournalLineInput(
  Guid? IntercompanyMatchId, string TaxonomyCode, decimal Debit, decimal Credit, string Description);

public sealed record ConsolidationJournalRequest(
  Guid ScopeVersionId, string JournalNumber, string JournalType, string Currency,
  string EvidenceReference, IReadOnlyList<ConsolidationJournalLineInput> Lines);

public sealed record AdvancedConsolidationMethodScheduleRequest(
  Guid ScopeVersionId, string Method, string Framework, string SourceManifestJson, string InputSnapshotJson);

public static class ConsolidationEliminationKinds
{
  public const string ReceivablePayable = "RECEIVABLE_PAYABLE";
  public const string RevenueExpense = "REVENUE_EXPENSE";
  public const string Dividend = "DIVIDEND";
  public const string InvestmentEquity = "INVESTMENT_EQUITY";
  public const string GroupJournal = "GROUP_JOURNAL";

  public static bool IsIntercompany(string value) => value is ReceivablePayable or RevenueExpense or Dividend or InvestmentEquity;
  public static bool IsRunKind(string value) => string.IsNullOrEmpty(value) || value == GroupJournal || IsIntercompany(value);
}

public static class ConsolidationService
{
  private sealed record OwnershipEdge(Guid ParentClientId, Guid ChildClientId, DateOnly EffectiveFrom, DateOnly? EffectiveTo);

  private sealed record ConsolidationBuild(
    ConsolidationCalculation Calculation,
    IReadOnlyList<(ConsolidationElimination Elimination, Guid? JournalId)> EliminationSources);

  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateGroupAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientGroupRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Group code and name are required.");
    var auth = await FirmAuthAsync(db, actor, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var code = request.Code.Trim();
    if (await db.ClientGroups.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == code, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The group code already exists.");
    var group = new ClientGroup
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = code, Name = request.Name.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientGroups.Add(group);
    db.GroupAccessGrants.Add(new GroupAccessGrant
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = group.Id, UserId = actor.UserId,
      Role = "Partner", GrantedAt = group.CreatedAt, GrantedByUserId = actor.UserId
    });
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(group.Id);
  }

  public static async Task<CommandResult> AddMembershipAsync(
    IClientAccountingDbContext db, ActorContext actor, GroupMembershipRequest request,
    CancellationToken ct = default)
  {
    if (request.GroupId == Guid.Empty || request.ClientId == Guid.Empty || request.EffectiveTo < request.EffectiveFrom ||
        string.IsNullOrWhiteSpace(request.ControlMethod) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        request.OwnershipPercent is < 0 or > 100 || request.EconomicInterestPercent is < 0 or > 100)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A group membership needs valid dates, method, ownership and evidence.");
    var auth = await GroupAuthAsync(db, actor, request.GroupId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return auth;
    if (!await db.PracticeClients.AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.ClientId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The legal entity is outside the firm scope.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var group = await db.ClientGroups.FromSqlInterpolated($"SELECT * FROM client_groups WHERE id = {request.GroupId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (group is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var overlaps = await db.ClientGroupMemberships.AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId &&
      x.ClientId == request.ClientId && x.EffectiveFrom <= (request.EffectiveTo ?? DateOnly.MaxValue) &&
      request.EffectiveFrom <= (x.EffectiveTo ?? DateOnly.MaxValue), ct);
    if (overlaps)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Overlapping historical group memberships are not allowed.");
    db.ClientGroupMemberships.Add(new ClientGroupMembership
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = request.GroupId, ClientId = request.ClientId,
      EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo, ControlMethod = request.ControlMethod.Trim().ToUpperInvariant(),
      OwnershipPercent = MoneyPolicy.Normalize(request.OwnershipPercent), EconomicInterestPercent = MoneyPolicy.Normalize(request.EconomicInterestPercent),
      EvidenceReference = request.EvidenceReference.Trim(), Status = AccountingWorkflowStates.Approved,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    group.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> AddOwnershipInterestAsync(
    IClientAccountingDbContext db, ActorContext actor, OwnershipInterestRequest request,
    CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || request.ParentClientId == Guid.Empty || request.ChildClientId == Guid.Empty ||
        request.ParentClientId == request.ChildClientId || request.EffectiveTo < request.EffectiveFrom ||
        request.OwnershipPercent is < 0 or > 100 || request.EconomicInterestPercent is < 0 or > 100 ||
        string.IsNullOrWhiteSpace(request.ControlAssessment) || string.IsNullOrWhiteSpace(request.Method) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An ownership interest needs distinct entities, valid dates, percentages, control assessment, method and evidence.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ScopeVersionId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, ["Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Draft)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Ownership can only be changed on a draft consolidation scope.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");

    var memberIds = await db.ClientGroupMemberships.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.Status == AccountingWorkflowStates.Approved &&
      x.EffectiveTo == null).Select(x => x.ClientId).ToHashSetAsync(ct);
    if (!memberIds.Contains(request.ParentClientId) || !memberIds.Contains(request.ChildClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Both ownership entities must be approved members of the group perimeter.");

    var existing = await db.OwnershipInterestVersions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
      x.Status != AccountingWorkflowStates.Rejected).Select(x => new OwnershipEdge(x.ParentClientId, x.ChildClientId,
        x.EffectiveFrom, x.EffectiveTo)).ToListAsync(ct);
    if (existing.Any(x => x.ParentClientId == request.ParentClientId && x.ChildClientId == request.ChildClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This ownership relationship already exists in the scope; create a new scope for a historical change.");
    if (CreatesOwnershipCycle(existing, request.ParentClientId, request.ChildClientId))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "The ownership relationship would create a circular hierarchy.");

    var ownership = new OwnershipInterestVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ParentClientId = request.ParentClientId, ChildClientId = request.ChildClientId,
      EffectiveFrom = request.EffectiveFrom, EffectiveTo = request.EffectiveTo,
      OwnershipPercent = MoneyPolicy.Normalize(request.OwnershipPercent),
      EconomicInterestPercent = MoneyPolicy.Normalize(request.EconomicInterestPercent),
      ControlAssessment = request.ControlAssessment.Trim().ToUpperInvariant(), Method = request.Method.Trim().ToUpperInvariant(),
      EvidenceReference = request.EvidenceReference.Trim(), Status = AccountingWorkflowStates.Approved,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.OwnershipInterestVersions.Add(ownership);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(ownership.Id);
  }

  public static async Task<CommandResult<Guid>> CreateScopeAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.ReportingCurrency) ? AccountingDefaults.DefaultCurrency : request.ReportingCurrency).Trim().ToUpperInvariant();
    var method = request.Method.Trim().ToUpperInvariant();
    if (request.GroupId == Guid.Empty || request.PeriodId == Guid.Empty || currency.Length != 3 ||
        currency.Any(c => c is < 'A' or > 'Z') || method is not (ConsolidationCalculator.RestrictedMethod or ConsolidationCalculator.ForeignOperationMethod) ||
        string.IsNullOrWhiteSpace(request.OpeningBasis))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only the approved bounded consolidation profiles are enabled.");
    var auth = await GroupAuthAsync(db, actor, request.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var group = await db.ClientGroups.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == request.GroupId, ct);
    if (group is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var members = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).ToListAsync(ct);
    if (members.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved group membership is required before a perimeter can be created.");
    if (method == ConsolidationCalculator.ForeignOperationMethod)
    {
      if (request.ExchangeRateSetVersionId is null || request.TranslationPolicyVersionId is null || request.TranslationRateDate is null ||
          string.IsNullOrWhiteSpace(request.TranslationRateType))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Foreign-operation translation requires an approved rate set, policy, date and rate type.");
      var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == request.ExchangeRateSetVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == request.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      if (rateSet is null || policy is null || policy.PresentationCurrency != currency ||
          !TranslationPolicyRules.AllowsRateType(policy, request.TranslationRateType))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The selected approved translation policy and rate set do not match the reporting currency.");
    }
    var openingRunHash = string.Empty;
    var openingTranslationManifestHash = string.Empty;
    var openingTranslationReserve = 0m;
    var recurringEliminationManifest = string.Empty;
    if (request.PriorScopeVersionId is { } priorScopeId)
    {
      var priorScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.Id == priorScopeId && x.GroupId == request.GroupId, ct);
      if (priorScope is null || priorScope.Status != AccountingWorkflowStates.Approved)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A group roll-forward requires an approved prior consolidation scope.");
      var priorRun = await db.ConsolidationRuns.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.ScopeVersionId == priorScope.Id && x.Status == AccountingWorkflowStates.Approved)
        .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
      if (priorRun is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A group roll-forward requires an approved prior consolidation run.");
      openingRunHash = priorRun.RunHash;

      var priorTranslations = await db.TranslationResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.ScopeVersionId == priorScope.Id && x.Status == AccountingWorkflowStates.Approved)
        .OrderBy(x => x.Id).ToListAsync(ct);
      openingTranslationReserve = MoneyPolicy.Normalize(priorTranslations.Sum(x => x.TranslationReserve));
      openingTranslationManifestHash = Hashing.Sha256Hex(string.Join('\n', priorTranslations.Select(x => string.Join('|',
        x.ComponentId.ToString("D"), x.RateSetVersionId.ToString("D"), x.TranslationPolicyVersionId.ToString("D"),
        x.SourcePackageHash ?? string.Empty, x.RateDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
        x.RateType, x.AppliedRate?.ToString("0.000000", CultureInfo.InvariantCulture) ?? string.Empty,
        x.FromCurrency, x.ToCurrency, x.TranslatedAmount.ToString("0.000000", CultureInfo.InvariantCulture),
        x.TranslationReserve.ToString("0.000000", CultureInfo.InvariantCulture)))));

      var priorJournals = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.GroupId == request.GroupId && x.ScopeVersionId == priorScope.Id && x.Status == AccountingWorkflowStates.Approved)
        .OrderBy(x => x.Id).ToListAsync(ct);
      var priorJournalIds = priorJournals.Select(x => x.Id).ToArray();
      var priorJournalLines = await db.ConsolidationJournalLines.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && priorJournalIds.Contains(x.ConsolidationJournalId)).OrderBy(x => x.Id).ToListAsync(ct);
      recurringEliminationManifest = Hashing.Sha256Hex(string.Join('\n',
        priorJournals.Select(x => string.Join('|', x.Id.ToString("D"), x.JournalNumber, x.JournalType,
          x.Currency, x.TotalDebits.ToString("0.000000", CultureInfo.InvariantCulture),
          x.TotalCreditsAbs.ToString("0.000000", CultureInfo.InvariantCulture), x.EvidenceReference))
        .Concat(priorJournalLines.Select(x => string.Join('|', x.Id.ToString("D"), x.ConsolidationJournalId.ToString("D"),
          x.IntercompanyMatchId?.ToString("D") ?? string.Empty, x.TaxonomyCode,
          x.Debit.ToString("0.000000", CultureInfo.InvariantCulture), x.Credit.ToString("0.000000", CultureInfo.InvariantCulture),
          x.Currency, x.Description)))));
    }
    var version = (await db.ConsolidationScopeVersions.Where(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId && x.PeriodId == request.PeriodId)
      .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var scope = new ConsolidationScopeVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = request.GroupId, PeriodId = request.PeriodId,
      GroupRevision = group.Revision,
      PriorScopeVersionId = request.PriorScopeVersionId, OpeningRunHash = openingRunHash,
      OpeningTranslationManifestHash = openingTranslationManifestHash, OpeningTranslationReserve = openingTranslationReserve,
      RecurringEliminationManifest = recurringEliminationManifest,
      Version = version, ReportingCurrency = currency, Method = method,
      OpeningBasis = request.OpeningBasis.Trim(), ExchangeRateSetVersionId = request.ExchangeRateSetVersionId,
      TranslationPolicyVersionId = request.TranslationPolicyVersionId, TranslationRateDate = request.TranslationRateDate,
      TranslationRateType = request.TranslationRateType.Trim().ToUpperInvariant(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationScopeVersions.Add(scope);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(scope.Id);
  }

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
    if (request.OwnershipPercent != 100m || !request.ControlMethod.Equals("CONTROLLED", StringComparison.OrdinalIgnoreCase))
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
      Currency = package.Currency, OwnershipPercent = 100m, ControlMethod = "CONTROLLED",
      SubmittedByUserId = actor.UserId, SubmittedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationComponents.Add(component);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(component.Id);
  }

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

  public static async Task<CommandResult> ApproveScopeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (scope.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft perimeter can be approved.");
    var currentGroupRevision = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentGroupRevision is null || currentGroupRevision.Value != scope.GroupRevision)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    var ownershipEdges = await db.OwnershipInterestVersions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == scope.GroupId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).Select(x => new OwnershipEdge(x.ParentClientId, x.ChildClientId,
        x.EffectiveFrom, x.EffectiveTo)).ToListAsync(ct);
    if (HasOwnershipCycle(ownershipEdges))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation perimeter contains a circular ownership hierarchy.");
    if (ownershipEdges.Count > 0)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Ownership hierarchy data is recorded but intermediate and nested consolidation is not enabled for this calculation profile.");
    if (!await HasMethodOwnerAcceptanceAsync(db, actor.FirmId, scope.GroupId, scope.Method, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An independently accepted group capability profile is required before perimeter approval.");
    var memberships = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).Select(x => x.ClientId).ToListAsync(ct);
    var components = await db.ConsolidationComponents.Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id).ToListAsync(ct);
    if (memberships.Count == 0 || components.Count != memberships.Distinct().Count() ||
        memberships.Any(x => components.All(c => c.ClientId != x)) || components.Any(x => x.Status != AccountingWorkflowStates.Approved ||
          x.OwnershipPercent != 100m || x.ControlMethod != "CONTROLLED"))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Every approved perimeter member needs one approved compatible component package.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && components.Any(x => x.Currency != scope.ReportingCurrency))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The restricted profile requires same-currency component packages.");
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
    {
      if (scope.ExchangeRateSetVersionId is null || scope.TranslationPolicyVersionId is null || scope.TranslationRateDate is null ||
          string.IsNullOrWhiteSpace(scope.TranslationRateType))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "The foreign-operation scope is missing its pinned translation inputs.");
      var policy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
        x.Id == scope.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      var translations = await db.TranslationResults.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
        x.ScopeVersionId == scope.Id && x.RateSetVersionId == scope.ExchangeRateSetVersionId &&
        x.TranslationPolicyVersionId == scope.TranslationPolicyVersionId && x.RateDate == scope.TranslationRateDate &&
        x.RateType == scope.TranslationRateType && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
      if (policy is null || components.Any(x => (x.Currency != scope.ReportingCurrency &&
          (x.Currency != policy.FunctionalCurrency || translations.All(t => t.ComponentId != x.Id || t.SourcePackageHash != x.PackageHash))) ||
        (x.Currency == scope.ReportingCurrency && x.Currency != policy.PresentationCurrency)))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Every foreign component needs the pinned approved translation result.");
    }
    scope.Status = AccountingWorkflowStates.Approved;
    scope.ApprovedByUserId = actor.UserId;
    scope.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> AddIntercompanyMatchAsync(
    IClientAccountingDbContext db, ActorContext actor, IntercompanyMatchRequest request,
    CancellationToken ct = default)
  {
    var sellerAmount = MoneyPolicy.Normalize(request.SellerAmount);
    var buyerAmount = MoneyPolicy.Normalize(request.BuyerAmount);
    var matchedAmount = MoneyPolicy.Normalize(request.MatchedAmount);
    var accountNature = request.AccountNature.Trim().ToUpperInvariant();
    var sellerTaxonomy = string.IsNullOrWhiteSpace(request.SellerTaxonomyCode) ? accountNature : request.SellerTaxonomyCode;
    var buyerTaxonomy = string.IsNullOrWhiteSpace(request.BuyerTaxonomyCode) ? accountNature : request.BuyerTaxonomyCode;
    var matchMode = request.MatchMode.Trim().ToUpperInvariant();
    var matchGroupReference = request.MatchGroupReference.Trim();
    var difference = MoneyPolicy.Normalize(sellerAmount + buyerAmount);
    if (request.SellerClientId == request.BuyerClientId || string.IsNullOrWhiteSpace(request.AccountNature) ||
        string.IsNullOrWhiteSpace(sellerTaxonomy) || string.IsNullOrWhiteSpace(buyerTaxonomy) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.MatchedAmount < 0m ||
        matchMode is not (IntercompanyMatchModes.OneToOne or IntercompanyMatchModes.Grouped) ||
        (matchMode == IntercompanyMatchModes.Grouped && string.IsNullOrWhiteSpace(matchGroupReference)) ||
        request.SellerAmount != sellerAmount || request.BuyerAmount != buyerAmount || request.MatchedAmount != matchedAmount ||
        (difference != 0m && string.IsNullOrWhiteSpace(request.DifferenceReason)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "An intercompany match needs precise signed amounts, both taxonomy sides, evidence and a difference reason when unmatched.");
    if (!request.OutsidePerimeterReview && !ConsolidationEliminationKinds.IsIntercompany(accountNature))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "An automatic intercompany elimination needs an approved receivable/payable, revenue/expense, dividend or investment/equity nature.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (currency != scope.ReportingCurrency || matchedAmount > Math.Min(Math.Abs(sellerAmount), Math.Abs(buyerAmount)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The match must fit both signed counterparty balances in the scope currency.");
    if (!await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.SellerClientId, ct) ||
        !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.BuyerClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Both counterparties must be legal entities in the firm scope.");
    var memberIds = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).Select(x => x.ClientId).ToHashSetAsync(ct);
    if (request.OutsidePerimeterReview)
    {
      if (memberIds.Contains(request.SellerClientId) == memberIds.Contains(request.BuyerClientId))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An outside-perimeter review must pair one in-perimeter entity with one related party outside the perimeter.");
    }
    else
    {
      var components = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        (x.ClientId == request.SellerClientId || x.ClientId == request.BuyerClientId)).ToListAsync(ct);
      if (components.Count != 2 || components.Select(x => x.ClientId).Distinct().Count() != 2 ||
          components.Any(x => x.Status != AccountingWorkflowStates.Approved))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Both counterparties must have approved component packages before matching.");
    }
    var match = new IntercompanyMatch
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      SellerClientId = request.SellerClientId, BuyerClientId = request.BuyerClientId, AccountNature = accountNature,
      MatchMode = matchMode, MatchGroupReference = matchGroupReference, OutsidePerimeterReview = request.OutsidePerimeterReview,
      SellerTaxonomyCode = sellerTaxonomy.Trim().ToUpperInvariant(), BuyerTaxonomyCode = buyerTaxonomy.Trim().ToUpperInvariant(),
      PeriodCode = request.PeriodCode.Trim(), Currency = currency, TransactionReference = request.TransactionReference.Trim(),
      SellerAmount = sellerAmount, BuyerAmount = buyerAmount, MatchedAmount = matchedAmount, Difference = difference,
      DifferenceReason = request.DifferenceReason.Trim(), EvidenceReference = request.EvidenceReference.Trim(),
      Status = AccountingWorkflowStates.Submitted, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.IntercompanyMatches.Add(match);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(match.Id);
  }

  public static async Task<CommandResult<Guid>> CreateConsolidationJournalAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationJournalRequest request,
    CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || string.IsNullOrWhiteSpace(request.JournalNumber) ||
        string.IsNullOrWhiteSpace(request.JournalType) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        request.Lines.Count < 2)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A consolidation journal needs a number, purpose, evidence and balanced lines.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, scope.GroupId, scope.GroupRevision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved consolidation perimeter is required before a group-only journal.");
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (currency != scope.ReportingCurrency || request.Lines.Any(x => string.IsNullOrWhiteSpace(x.TaxonomyCode) ||
        string.IsNullOrWhiteSpace(x.Description) || x.Debit < 0m || x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m) ||
        x.Debit != MoneyPolicy.Normalize(x.Debit) || x.Credit != MoneyPolicy.Normalize(x.Credit)))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Group-only journal lines must use the approved scope currency and accounting precision.");
    if (MoneyPolicy.Normalize(request.Lines.Sum(x => x.Debit)) != MoneyPolicy.Normalize(request.Lines.Sum(x => x.Credit)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "The consolidation journal must balance.");
    if (await db.ConsolidationJournals.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        x.JournalNumber == request.JournalNumber.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The consolidation journal number already exists in this perimeter.");
    var matchIds = request.Lines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).Distinct().ToArray();
    if (request.Lines.Where(x => x.IntercompanyMatchId.HasValue).GroupBy(x => x.IntercompanyMatchId!.Value).Any(x => x.Count() > 1))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A consolidation journal may link an intercompany match only once.");
    if (matchIds.Length > 0 && await db.IntercompanyMatches.CountAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        matchIds.Contains(x.Id) && x.Status == AccountingWorkflowStates.Approved && !x.OutsidePerimeterReview, ct) != matchIds.Length)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Every linked intercompany match must be approved in the same perimeter.");
    if (matchIds.Length > 0 && await db.ConsolidationJournalLines.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ScopeVersionId == scope.Id && x.IntercompanyMatchId.HasValue && matchIds.Contains(x.IntercompanyMatchId.Value) &&
        db.ConsolidationJournals.Any(j => j.FirmId == actor.FirmId && j.Id == x.ConsolidationJournalId &&
          j.Status == AccountingWorkflowStates.Approved), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "An approved consolidation journal already uses one of the linked intercompany matches.");
    var journal = new ConsolidationJournal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      JournalNumber = request.JournalNumber.Trim(), JournalType = request.JournalType.Trim().ToUpperInvariant(), Currency = currency,
      TotalDebits = MoneyPolicy.Normalize(request.Lines.Sum(x => x.Debit)), TotalCreditsAbs = MoneyPolicy.Normalize(request.Lines.Sum(x => x.Credit)),
      EvidenceReference = request.EvidenceReference.Trim(), Status = AccountingWorkflowStates.Submitted,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationJournals.Add(journal);
    db.ConsolidationJournalLines.AddRange(request.Lines.Select(x => new ConsolidationJournalLine
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      ConsolidationJournalId = journal.Id, IntercompanyMatchId = x.IntercompanyMatchId,
      TaxonomyCode = x.TaxonomyCode.Trim(), Debit = MoneyPolicy.Normalize(x.Debit), Credit = MoneyPolicy.Normalize(x.Credit),
      Currency = currency, Description = x.Description.Trim(), CreatedAt = journal.CreatedAt
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(journal.Id);
  }

  public static async Task<CommandResult> ApproveConsolidationJournalAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default)
  {
    var journal = await db.ConsolidationJournals.SingleOrDefaultAsync(x => x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, journal.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var journalScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == journal.ScopeVersionId && x.GroupId == journal.GroupId, ct);
    if (journalScope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, journalScope.GroupId, journalScope.GroupRevision, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (journal.Status != AccountingWorkflowStates.Submitted || journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Only a separate reviewer can approve a submitted consolidation journal.");
    var lines = await db.ConsolidationJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ConsolidationJournalId == journal.Id).ToListAsync(ct);
    if (lines.Count < 2 || MoneyPolicy.Normalize(lines.Sum(x => x.Debit)) != journal.TotalDebits ||
        MoneyPolicy.Normalize(lines.Sum(x => x.Credit)) != journal.TotalCreditsAbs || journal.TotalDebits != journal.TotalCreditsAbs)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation journal is no longer balanced.");
    var matchIds = lines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).Distinct().ToArray();
    if (matchIds.Length > 0 && await db.IntercompanyMatches.CountAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == journal.ScopeVersionId &&
        matchIds.Contains(x.Id) && x.Status == AccountingWorkflowStates.Approved && !x.OutsidePerimeterReview, ct) != matchIds.Length)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "A linked intercompany match changed; rebuild the group journal.");
    journal.Status = AccountingWorkflowStates.Approved;
    journal.ApprovedByUserId = actor.UserId;
    journal.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveIntercompanyMatchAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid matchId,
    CancellationToken ct = default)
  {
    var match = await db.IntercompanyMatches.SingleOrDefaultAsync(x => x.Id == matchId && x.FirmId == actor.FirmId, ct);
    if (match is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, match.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var matchScope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == match.ScopeVersionId && x.GroupId == match.GroupId, ct);
    if (matchScope is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!await ConsolidationScopeGuards.IsCurrentAsync(db, actor.FirmId, matchScope.GroupId, matchScope.GroupRevision, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The group perimeter changed; create a new scope version.");
    if (match.Status != AccountingWorkflowStates.Submitted || match.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Only a submitted match prepared by another user can be approved.");
    if (match.MatchMode == IntercompanyMatchModes.Grouped)
    {
      var group = await db.IntercompanyMatches.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
        x.ScopeVersionId == match.ScopeVersionId && x.MatchMode == IntercompanyMatchModes.Grouped &&
        x.MatchGroupReference == match.MatchGroupReference && x.AccountNature == match.AccountNature &&
        x.PeriodCode == match.PeriodCode && x.Currency == match.Currency).ToListAsync(ct);
      if (group.Count < 2)
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "A grouped match needs at least two reviewed source rows.");
      var sellerTotal = MoneyPolicy.Normalize(group.Sum(x => Math.Abs(x.SellerAmount)));
      var buyerTotal = MoneyPolicy.Normalize(group.Sum(x => Math.Abs(x.BuyerAmount)));
      var matchedTotal = MoneyPolicy.Normalize(group.Sum(x => x.MatchedAmount));
      if (matchedTotal > Math.Min(sellerTotal, buyerTotal))
        return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Grouped matched amounts exceed the reviewed counterparty totals.");
    }
    match.Status = AccountingWorkflowStates.Approved;
    match.ReviewedByUserId = actor.UserId;
    match.ReviewedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> RunAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved perimeter is required before calculation.");
    var build = await BuildCalculationAsync(db, actor.FirmId, scope, ct);
    if (!build.Succeeded)
      return CommandResult<Guid>.Fail(build.ErrorCode!, build.Message!);
    var calculation = build.Value!.Calculation;
    var eliminationSources = build.Value.EliminationSources;
    var existing = await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ScopeVersionId == scope.Id && x.RunHash == calculation.RunHash, ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);
    var run = new ConsolidationRun
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      EngineVersion = ConsolidationCalculator.EngineVersion, InputManifest = calculation.InputManifest,
      RunHash = calculation.RunHash, ReportingCurrency = scope.ReportingCurrency, SignedTotal = calculation.SignedTotal,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ConsolidationRuns.Add(run);
    foreach (var line in calculation.DetailLines)
    {
      Guid? intercompanyMatchId = null;
      Guid? consolidationJournalId = null;
      if (line.MatchId is { } matchId)
      {
        var source = eliminationSources.First(x => x.Elimination.MatchId == matchId);
        intercompanyMatchId = source.Elimination.IntercompanyMatchId;
        consolidationJournalId = source.JournalId;
      }
      db.ConsolidationRunLines.Add(new ConsolidationRunLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
        RunId = run.Id, ComponentId = line.ComponentId, SourceLineId = line.SourceLineId,
        IntercompanyMatchId = intercompanyMatchId, ConsolidationJournalId = consolidationJournalId,
        TaxonomyCode = line.TaxonomyCode, ComponentAmount = line.ComponentAmount, AlignmentAmount = 0m, EliminationAmount = line.EliminationAmount,
        ConsolidatedAmount = line.ConsolidatedAmount, Currency = line.Currency, CreatedAt = run.CreatedAt
      });
    }
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(run.Id);
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
    var existing = await db.AdvancedConsolidationMethodSchedules.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.Method == method &&
      x.InputSnapshotDigest == Hashing.Sha256Hex(inputSnapshot), ct);
    if (existing is not null)
      return CommandResult<Guid>.Ok(existing.Id);
    var schedule = new AdvancedConsolidationMethodSchedule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      GroupRevision = scope.GroupRevision, Method = method, Framework = "IFRS",
      SourceManifestJson = sourceManifest, SourceManifestDigest = Hashing.Sha256Hex(sourceManifest),
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
    if (scope is null || scope.GroupRevision != schedule.GroupRevision || scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult.Fail(ErrorCodes.GenerationStale,
        "The consolidation perimeter changed; rebuild the advanced method schedule.");
    schedule.Status = AdvancedConsolidationMethodScheduleStates.Approved;
    schedule.ApprovedByUserId = actor.UserId;
    schedule.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveRunAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid runId,
    CancellationToken ct = default)
  {
    var run = await db.ConsolidationRuns.SingleOrDefaultAsync(x => x.Id == runId && x.FirmId == actor.FirmId, ct);
    if (run is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, run.GroupId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (run.Status != AccountingWorkflowStates.Submitted || run.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Only a separate reviewer can approve a submitted balanced run.");
    if (run.SignedTotal != 0m)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation run is not balanced.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == run.ScopeVersionId && x.FirmId == actor.FirmId && x.GroupId == run.GroupId, ct);
    if (scope is null || scope.Status != AccountingWorkflowStates.Approved)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The consolidation perimeter changed; rebuild the group run.");
    var current = await BuildCalculationAsync(db, actor.FirmId, scope, ct);
    if (!current.Succeeded)
      return CommandResult.Fail(current.ErrorCode!, current.Message!);
    if (current.Value!.Calculation.RunHash != run.RunHash || current.Value.Calculation.InputManifest != run.InputManifest)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "A component, match or group journal changed; rebuild the group run.");
    run.Status = AccountingWorkflowStates.Approved;
    run.ApprovedByUserId = actor.UserId;
    run.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult<ConsolidationBuild>> BuildCalculationAsync(
    IClientAccountingDbContext db, Guid firmId, ConsolidationScopeVersion scope, CancellationToken ct)
  {
    var currentGroupRevision = await db.ClientGroups.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == scope.GroupId)
      .Select(x => (long?)x.Revision).SingleOrDefaultAsync(ct);
    if (currentGroupRevision is null || currentGroupRevision.Value != scope.GroupRevision)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale,
        "The group perimeter changed; rebuild the consolidation scope before calculating.");
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == firmId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).OrderBy(x => x.Id).ToListAsync(ct);
    if (components.Count == 0)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked, "At least one approved component package is required.");
    var packageComponents = components.Where(x => x.SourceType == ConsolidationComponentSources.InternalPackage && x.PackageId.HasValue).ToList();
    var externalComponents = components.Where(x => x.SourceType == ConsolidationComponentSources.ExternalPack && x.ExternalComponentPackId.HasValue).ToList();
    if (packageComponents.Count + externalComponents.Count != components.Count)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale, "A component source is incomplete; resubmit the affected component.");
    var packageIds = packageComponents.Select(x => x.PackageId!.Value).ToArray();
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == firmId && packageIds.Contains(x.Id)).ToListAsync(ct);
    if (packages.Count != packageComponents.Count || packageComponents.Any(component =>
        packages.All(package => package.Id != component.PackageId || package.CalculationHash != component.PackageHash ||
          package.Status != AccountingPackageStates.PackageValidated || package.Currency != component.Currency)))
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale,
        "A component package changed; rebuild the group run from current approved packages.");
    var externalPackIds = externalComponents.Select(x => x.ExternalComponentPackId!.Value).ToArray();
    var externalPacks = await db.ExternalComponentPacks.AsNoTracking().Where(x => x.FirmId == firmId && externalPackIds.Contains(x.Id)).ToListAsync(ct);
    var externalLines = await db.ExternalComponentPackLines.AsNoTracking().Where(x => x.FirmId == firmId && externalPackIds.Contains(x.ExternalComponentPackId)).ToListAsync(ct);
    if (externalPacks.Count != externalComponents.Count || externalComponents.Any(component =>
        externalPacks.All(pack => pack.Id != component.ExternalComponentPackId || pack.PackDigest != component.PackageHash ||
          pack.Status != ExternalComponentPackStates.Approved || pack.ReconciliationStatus != ExternalComponentReconciliationStates.Reconciled ||
          pack.ReportingCurrency != component.Currency || !ExternalPackLinesMatch(pack, externalLines.Where(line => line.ExternalComponentPackId == pack.Id).ToArray()))))
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale,
        "An external component pack changed; rebuild the group run from the current approved pack.");
    if (scope.Method == ConsolidationCalculator.RestrictedMethod && components.Any(x => x.Currency != scope.ReportingCurrency))
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
        "The restricted profile requires same-currency component packages.");
    var translationResults = new List<TranslationResult>();
    TranslationPolicyVersion? translationPolicy = null;
    if (scope.Method == ConsolidationCalculator.ForeignOperationMethod)
    {
      if (scope.ExchangeRateSetVersionId is null || scope.TranslationPolicyVersionId is null || scope.TranslationRateDate is null ||
          string.IsNullOrWhiteSpace(scope.TranslationRateType))
        return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
          "The foreign-operation scope is missing its pinned translation inputs.");
      var rateSet = await db.ExchangeRateSetVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
        x.Id == scope.ExchangeRateSetVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      translationPolicy = await db.TranslationPolicyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
        x.Id == scope.TranslationPolicyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
      if (rateSet is null || translationPolicy is null || translationPolicy.PresentationCurrency != scope.ReportingCurrency)
        return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
          "The pinned translation policy and rate set are not approved for this group scope.");
      translationResults = await db.TranslationResults.AsNoTracking().Where(x => x.FirmId == firmId && x.GroupId == scope.GroupId &&
        x.ScopeVersionId == scope.Id && x.RateSetVersionId == scope.ExchangeRateSetVersionId &&
        x.TranslationPolicyVersionId == scope.TranslationPolicyVersionId && x.RateDate == scope.TranslationRateDate &&
        x.RateType == scope.TranslationRateType && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
    }
    var packageLines = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == firmId &&
      packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var componentByPackage = packageComponents.ToDictionary(x => x.PackageId!.Value);
    var componentByExternalPack = externalComponents.ToDictionary(x => x.ExternalComponentPackId!.Value);
    var balances = new List<ConsolidationComponentBalance>(packageLines.Count + externalLines.Count);
    try
    {
      void AddBalance(ConsolidationComponent component, string destinationCode, decimal amount, string currency, Guid lineId)
      {
        if (currency != component.Currency)
          throw new InvalidOperationException("A component line changed currency or no longer belongs to the selected component.");
        if (component.Currency == scope.ReportingCurrency)
        {
          balances.Add(new ConsolidationComponentBalance(component.Id, component.ClientId, destinationCode,
            amount, scope.ReportingCurrency, component.OwnershipPercent, component.ControlMethod, component.PackageHash,
            component.PeriodBasis, component.TaxonomyVersion, component.MappingVersion, lineId, component.Currency));
          return;
        }
        var translation = translationResults.SingleOrDefault(x => x.ComponentId == component.Id &&
          x.SourcePackageHash == component.PackageHash && x.FromCurrency == component.Currency &&
          x.ToCurrency == scope.ReportingCurrency && x.AppliedRate is > 0m);
        if (translation is null || translationPolicy is null || translationPolicy.FunctionalCurrency != component.Currency)
          throw new InvalidOperationException("A foreign component is missing its current approved translation result.");
        var translated = CurrencyTranslationCalculator.Translate(amount, component.Currency, scope.ReportingCurrency,
          translation.AppliedRate!.Value);
        balances.Add(new ConsolidationComponentBalance(component.Id, component.ClientId, destinationCode,
          translated, scope.ReportingCurrency, component.OwnershipPercent, component.ControlMethod, component.PackageHash,
          component.PeriodBasis, component.TaxonomyVersion, component.MappingVersion, lineId, component.Currency,
          translation.Id, translation.RateSetVersionId, translation.TranslationPolicyVersionId, translation.RateDate,
          translation.RateType, translation.AppliedRate.Value));
      }

      foreach (var line in packageLines)
      {
        if (!componentByPackage.TryGetValue(line.FinancialPackageId, out var component))
          throw new InvalidOperationException("A component package line no longer belongs to the selected component.");
        AddBalance(component, line.DestinationCode, line.Amount, line.Currency, line.Id);
      }
      foreach (var line in externalLines)
      {
        if (!componentByExternalPack.TryGetValue(line.ExternalComponentPackId, out var component))
          throw new InvalidOperationException("An external component line no longer belongs to the selected component.");
        AddBalance(component, line.TaxonomyCode, line.Amount, line.Currency, line.Id);
      }
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GenerationStale, ex.Message);
    }
    var pendingGrouped = await db.IntercompanyMatches.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
      x.ScopeVersionId == scope.Id && x.MatchMode == IntercompanyMatchModes.Grouped &&
      x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
    if (pendingGrouped)
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked,
        "Every row in a grouped intercompany match must be reviewed before the group run.");
    var matches = await db.IntercompanyMatches.AsNoTracking().Where(x => x.FirmId == firmId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved && !x.OutsidePerimeterReview).ToListAsync(ct);
    var approvedJournals = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
    var journalIds = approvedJournals.Select(x => x.Id).ToArray();
    var journalLines = await db.ConsolidationJournalLines.AsNoTracking().Where(x => x.FirmId == firmId &&
      journalIds.Contains(x.ConsolidationJournalId)).ToListAsync(ct);
    var linkedMatches = journalLines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).ToHashSet();
    var eliminationSources = matches.Where(x => !linkedMatches.Contains(x.Id))
      .SelectMany(x => new[]
      {
        (new ConsolidationElimination(x.Id, x.SellerTaxonomyCode, Math.Sign(x.SellerAmount) * x.MatchedAmount * -1m, x.Currency, "SELLER", x.Id, x.AccountNature), (Guid?)null),
        (new ConsolidationElimination(x.Id, x.BuyerTaxonomyCode, Math.Sign(x.BuyerAmount) * x.MatchedAmount * -1m, x.Currency, "BUYER", x.Id, x.AccountNature), (Guid?)null)
      })
      .Concat(journalLines.Select(x => (new ConsolidationElimination(x.Id, x.TaxonomyCode, x.Debit - x.Credit, x.Currency, "", x.IntercompanyMatchId, ConsolidationEliminationKinds.GroupJournal), (Guid?)x.ConsolidationJournalId)))
      .ToList();
    try
    {
      var calculation = ConsolidationCalculator.Compute(scope.ReportingCurrency, scope.Method, scope.OpeningBasis, balances,
        eliminationSources.Select(x => x.Item1).ToList(), scope.GroupRevision);
      return CommandResult<ConsolidationBuild>.Ok(new ConsolidationBuild(calculation, eliminationSources));
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<ConsolidationBuild>.Fail(ErrorCodes.GateBlocked, ex.Message);
    }
  }

  private static async Task<CommandResult> FirmAuthAsync(
    IClientAccountingDbContext db, ActorContext actor, IReadOnlyList<string> roles, CancellationToken ct) =>
    await Application.Security.AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles.ToArray(), InternalOnly: true), ct);

  private static async Task<CommandResult> GroupAuthAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId, IReadOnlyList<string> roles, CancellationToken ct)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && !x.Disabled, ct);
    if (user is null || user.SessionEpoch != actor.SessionEpoch)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var allowed = await db.GroupAccessGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == groupId &&
      x.UserId == actor.UserId && x.RevokedAt == null && roles.Contains(x.Role), ct);
    return allowed ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Explicit group access is required.");
  }

  private static Task<bool> HasMethodOwnerAcceptanceAsync(
    IClientAccountingDbContext db, Guid firmId, Guid groupId, string method, CancellationToken ct) =>
    (from profile in db.AccountingCapabilityProfiles.AsNoTracking()
     join acceptance in db.AccountingCapabilityAcceptances.AsNoTracking()
       on new { profile.FirmId, CapabilityProfileId = profile.Id }
       equals new { acceptance.FirmId, acceptance.CapabilityProfileId }
     where profile.FirmId == firmId && profile.GroupId == groupId &&
       profile.ServiceKind == "GROUP_REPORTING" && profile.Status != AccountingWorkflowStates.Retired &&
       profile.ConsolidationMethod == method.Trim().ToUpperInvariant() &&
       acceptance.Stage == AccountingCapabilityAcceptanceStages.MethodOwnerApproval &&
     acceptance.Status == AccountingWorkflowStates.Approved
     select acceptance.Id).AnyAsync(ct);

  private static bool IsSha256(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

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

  private static bool CreatesOwnershipCycle(IReadOnlyCollection<OwnershipEdge> existing,
    Guid parentClientId, Guid childClientId)
  {
    var adjacency = existing.GroupBy(x => x.ParentClientId).ToDictionary(x => x.Key,
      x => x.Select(y => y.ChildClientId).ToArray());
    var pending = new Stack<Guid>([childClientId]);
    var visited = new HashSet<Guid>();
    while (pending.TryPop(out var current) && visited.Add(current))
    {
      if (current == parentClientId)
        return true;
      if (adjacency.TryGetValue(current, out var children))
        foreach (var child in children) pending.Push(child);
    }
    return false;
  }

  private static bool HasOwnershipCycle(IReadOnlyCollection<OwnershipEdge> edges) =>
    edges.Any(edge => CreatesOwnershipCycle(edges.Where(x => x != edge).ToArray(), edge.ParentClientId, edge.ChildClientId));
}

internal static class ConsolidationScopeGuards
{
  public static Task<bool> IsCurrentAsync(
    IClientAccountingDbContext db, Guid firmId, Guid groupId, long pinnedRevision, CancellationToken ct) =>
    db.ClientGroups.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.Id == groupId && x.Revision == pinnedRevision, ct);
}
