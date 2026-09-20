using System.Globalization;
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

public sealed record ConsolidationScopeRequest(
  Guid GroupId, Guid PeriodId, string ReportingCurrency, string Method, string OpeningBasis);

public sealed record ConsolidationComponentRequest(
  Guid ScopeVersionId, Guid ClientId, Guid EngagementId, Guid PackageId,
  decimal OwnershipPercent, string ControlMethod, string PeriodBasis,
  string TaxonomyVersion, string MappingVersion);

public sealed record IntercompanyMatchRequest(
  Guid ScopeVersionId, Guid SellerClientId, Guid BuyerClientId,
  string AccountNature, string PeriodCode, string Currency, string TransactionReference,
  decimal SellerAmount, decimal BuyerAmount, decimal MatchedAmount, string EvidenceReference);

public sealed record ConsolidationJournalLineInput(
  Guid? IntercompanyMatchId, string TaxonomyCode, decimal Debit, decimal Credit, string Description);

public sealed record ConsolidationJournalRequest(
  Guid ScopeVersionId, string JournalNumber, string JournalType, string Currency,
  string EvidenceReference, IReadOnlyList<ConsolidationJournalLineInput> Lines);

public static class ConsolidationService
{
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
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateScopeAsync(
    IClientAccountingDbContext db, ActorContext actor, ConsolidationScopeRequest request,
    CancellationToken ct = default)
  {
    var currency = request.ReportingCurrency.Trim().ToUpperInvariant();
    if (request.GroupId == Guid.Empty || request.PeriodId == Guid.Empty || currency.Length != 3 ||
        currency.Any(c => c is < 'A' or > 'Z') || request.Method.Trim().ToUpperInvariant() != ConsolidationCalculator.RestrictedMethod ||
        string.IsNullOrWhiteSpace(request.OpeningBasis))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only the approved same-currency, fully-owned first consolidation profile is enabled.");
    var auth = await GroupAuthAsync(db, actor, request.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await db.ClientGroups.AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.GroupId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var members = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).ToListAsync(ct);
    if (members.Count == 0)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved group membership is required before a perimeter can be created.");
    var version = (await db.ConsolidationScopeVersions.Where(x => x.FirmId == actor.FirmId && x.GroupId == request.GroupId && x.PeriodId == request.PeriodId)
      .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var scope = new ConsolidationScopeVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = request.GroupId, PeriodId = request.PeriodId,
      Version = version, ReportingCurrency = currency, Method = request.Method.Trim().ToUpperInvariant(),
      OpeningBasis = request.OpeningBasis.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
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
    if (package.Status != AccountingPackageStates.PackageValidated || package.Currency != scope.ReportingCurrency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only an approved same-currency component package can enter consolidation.");
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
    if (component.Status != AccountingWorkflowStates.Submitted)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted component can be approved.");
    if (component.SubmittedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The component preparer cannot approve the same submission.");
    var packageHash = await db.FinancialPackages.Where(x => x.FirmId == actor.FirmId && x.Id == component.PackageId)
      .Select(x => new { x.CalculationHash, x.Status, x.Currency }).SingleOrDefaultAsync(ct);
    if (packageHash is null || packageHash.CalculationHash != component.PackageHash || packageHash.Status != AccountingPackageStates.PackageValidated ||
        packageHash.Currency != component.Currency)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The component package changed; resubmit the current approved package.");
    var packageReview = await FinancialPackageReviewService.RequireCurrentAsync(db, actor, component.PackageId, requirePartner: true, ct);
    if (!packageReview.Succeeded)
      return packageReview;
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
    if (!await HasMethodOwnerAcceptanceAsync(db, actor.FirmId, scope.GroupId, scope.Method, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An independently accepted group capability profile is required before perimeter approval.");
    var memberships = await db.ClientGroupMemberships.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.GroupId == scope.GroupId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveTo == null).Select(x => x.ClientId).ToListAsync(ct);
    var components = await db.ConsolidationComponents.Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id).ToListAsync(ct);
    if (memberships.Count == 0 || components.Count != memberships.Distinct().Count() ||
        memberships.Any(x => components.All(c => c.ClientId != x)) || components.Any(x => x.Status != AccountingWorkflowStates.Approved ||
          x.Currency != scope.ReportingCurrency || x.OwnershipPercent != 100m || x.ControlMethod != "CONTROLLED"))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Every approved perimeter member needs one approved compatible component package.");
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
    if (request.SellerClientId == request.BuyerClientId || string.IsNullOrWhiteSpace(request.AccountNature) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || request.MatchedAmount < 0m)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "An intercompany match needs two legal entities, evidence and a non-negative matched amount.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (currency != scope.ReportingCurrency || request.MatchedAmount > Math.Min(Math.Abs(request.SellerAmount), Math.Abs(request.BuyerAmount)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The match must fit both signed counterparty balances in the scope currency.");
    if (!await db.ConsolidationComponents.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.ClientId == request.SellerClientId, ct) ||
        !await db.ConsolidationComponents.AnyAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.ClientId == request.BuyerClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Both counterparties must be submitted to the perimeter before matching.");
    var match = new IntercompanyMatch
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
      SellerClientId = request.SellerClientId, BuyerClientId = request.BuyerClientId, AccountNature = request.AccountNature.Trim(),
      PeriodCode = request.PeriodCode.Trim(), Currency = currency, TransactionReference = request.TransactionReference.Trim(),
      SellerAmount = MoneyPolicy.Normalize(request.SellerAmount), BuyerAmount = MoneyPolicy.Normalize(request.BuyerAmount),
      MatchedAmount = MoneyPolicy.Normalize(request.MatchedAmount), Difference = MoneyPolicy.Normalize(request.SellerAmount + request.BuyerAmount),
      EvidenceReference = request.EvidenceReference.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
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
    if (matchIds.Length > 0 && await db.IntercompanyMatches.CountAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
        matchIds.Contains(x.Id) && x.Status == AccountingWorkflowStates.Approved, ct) != matchIds.Length)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Every linked intercompany match must be approved in the same perimeter.");
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
    if (journal.Status != AccountingWorkflowStates.Submitted || journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Only a separate reviewer can approve a submitted consolidation journal.");
    var lines = await db.ConsolidationJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ConsolidationJournalId == journal.Id).ToListAsync(ct);
    if (lines.Count < 2 || MoneyPolicy.Normalize(lines.Sum(x => x.Debit)) != journal.TotalDebits ||
        MoneyPolicy.Normalize(lines.Sum(x => x.Credit)) != journal.TotalCreditsAbs || journal.TotalDebits != journal.TotalCreditsAbs)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The consolidation journal is no longer balanced.");
    var matchIds = lines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).Distinct().ToArray();
    if (matchIds.Length > 0 && await db.IntercompanyMatches.CountAsync(x => x.FirmId == actor.FirmId && x.ScopeVersionId == journal.ScopeVersionId &&
        matchIds.Contains(x.Id) && x.Status == AccountingWorkflowStates.Approved, ct) != matchIds.Length)
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
    if (match.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected, "The match preparer cannot independently approve it.");
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
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).OrderBy(x => x.Id).ToListAsync(ct);
    var componentIds = components.Select(x => x.Id).ToHashSet();
    var packageLines = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      components.Select(c => c.PackageId).Contains(x.FinancialPackageId)).ToListAsync(ct);
    var balances = packageLines.Join(components, line => line.FinancialPackageId, component => component.PackageId,
      (line, component) => new ConsolidationComponentBalance(component.Id, component.ClientId, line.DestinationCode,
        line.Amount, line.Currency, component.OwnershipPercent, component.ControlMethod)).ToList();
    var matches = await db.IntercompanyMatches.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id &&
      x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
    var approvedJournals = await db.ConsolidationJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved).ToListAsync(ct);
    var journalIds = approvedJournals.Select(x => x.Id).ToArray();
    var journalLines = await db.ConsolidationJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      journalIds.Contains(x.ConsolidationJournalId)).ToListAsync(ct);
    var linkedMatches = journalLines.Where(x => x.IntercompanyMatchId.HasValue).Select(x => x.IntercompanyMatchId!.Value).ToHashSet();
    var eliminationSources = matches.Where(x => !linkedMatches.Contains(x.Id))
      .Select(x => (new ConsolidationElimination(x.Id, x.AccountNature, -x.MatchedAmount, x.Currency), (Guid?)null))
      .Concat(journalLines.Select(x => (new ConsolidationElimination(x.Id, x.TaxonomyCode, x.Debit - x.Credit, x.Currency), (Guid?)x.ConsolidationJournalId)))
      .ToList();
    ConsolidationCalculation calculation;
    try
    {
      calculation = ConsolidationCalculator.Compute(scope.ReportingCurrency, scope.Method, scope.OpeningBasis, balances,
        eliminationSources.Select(x => x.Item1).ToList());
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, ex.Message);
    }
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
      db.ConsolidationRunLines.Add(new ConsolidationRunLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, GroupId = scope.GroupId, ScopeVersionId = scope.Id,
        RunId = run.Id, ComponentId = line.ComponentId,
        ConsolidationJournalId = line.MatchId is { } matchId
          ? eliminationSources.FirstOrDefault(x => x.Item1.MatchId == matchId).Item2
          : null,
        TaxonomyCode = line.TaxonomyCode, ComponentAmount = line.ComponentAmount, AlignmentAmount = 0m, EliminationAmount = line.EliminationAmount,
        ConsolidatedAmount = line.ConsolidatedAmount, Currency = line.Currency, CreatedAt = run.CreatedAt
      });
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(run.Id);
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
    run.Status = AccountingWorkflowStates.Approved;
    run.ApprovedByUserId = actor.UserId;
    run.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
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
}
