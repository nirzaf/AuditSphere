using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientAccountingProfileRequest(
  Guid ClientId, string Jurisdiction, string FunctionalCurrency,
  int FiscalYearStartMonth, int FiscalYearStartDay, string SourceSystem,
  string SourceSystemIdentifier);

public sealed record ReportingPeriodRequest(
  Guid ClientId, string PeriodCode, DateOnly StartDate, DateOnly EndDate,
  string Basis, string Currency, Guid? PriorPeriodId = null);

public sealed record RollForwardPeriodRequest(
  Guid ClientId, Guid PriorPeriodId, string PeriodCode, DateOnly StartDate, DateOnly EndDate,
  string Basis, string Currency, string SourceHash, decimal PriorClosingAmount,
  decimal CurrentOpeningAmount, string EvidenceReference, Guid? SourcePackageId = null);

public sealed record ReportingBookRequest(
  Guid ClientId, Guid PeriodId, string Code, string Basis,
  string InclusionRule, string Currency);

public sealed record OpeningBalanceBridgeRequest(
  Guid ClientId, Guid CurrentPeriodId, Guid? PriorPeriodId, Guid? SourcePackageId,
  string SourceHash, decimal PriorClosingAmount, decimal CurrentOpeningAmount,
  string EvidenceReference);

public sealed record CreatePeriodRestatementRequest(
  Guid ClientId, Guid PeriodId, Guid OriginalPackageId, Guid RevisedPackageId,
  string RevisedBasis, string Reason, string EvidenceReference);

public sealed record ClientAccountInput(
  string StableIdentity, string AccountCode, string AccountName, string AccountType,
  string NormalBalance, bool IsPosting, string? ParentStableIdentity = null);

public sealed record SourceAccountAliasInput(
  Guid ClientAccountId, string SourceSystem, string AliasCode, string AliasName);

public sealed record AccountingDimensionInput(string DimensionType, string Code, string Name);

public sealed record TaxonomyNodeInput(
  string Code, string Name, string StatementSection, string DisplaySign,
  string NormalBalance, string DisclosureArea, bool IsPosting,
  string Applicability, string? ParentCode = null);

public sealed record CapabilityProfileRequest(
  Guid? ClientId, Guid? GroupId, string ServiceKind, string Framework,
  string Edition, string PeriodRule, string ReportingCurrency,
  string AccountingMethod, string ConsolidationMethod, string ReviewHierarchy,
  string TemplateFamily);

public sealed record GeneralLedgerLineInput(
  string StableLineId, string AccountCode, decimal Debit, decimal Credit,
  string OriginalCurrency, decimal OriginalAmount, decimal FunctionalAmount,
  string PartyIdentifier = "", string Branch = "", string CostCentre = "",
  string Department = "", string Project = "", string IntercompanyCounterparty = "");

public sealed record GeneralLedgerTransactionInput(
  string StableJournalId, string DocumentNumber, DateOnly PostingDate,
  DateOnly? DocumentDate, string SourceUser, string SourceSystem,
  string? ReversalReference, bool IsManual, bool IsYearEnd,
  IReadOnlyList<GeneralLedgerLineInput> Lines);

public sealed record GeneralLedgerImportRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
  string ProfileVersion, string ParserVersion, string RawFileSha256Hex,
  string LegalEntityKey, string Currency, string ReceiptReference,
  IReadOnlyList<GeneralLedgerTransactionInput> Transactions);

public sealed record GeneralLedgerImportStartRequest(
  Guid ClientId, Guid EngagementId, Guid PeriodId, Guid? BookId,
  string ProfileVersion, string ParserVersion, string RawFileSha256Hex,
  string LegalEntityKey, string Currency, string ReceiptReference,
  int ExpectedChunkCount, int ExpectedTransactionCount, int ExpectedLineCount);

public sealed record GeneralLedgerImportChunkRequest(
  Guid ImportBatchId, int ChunkNumber, string ChunkDigest,
  IReadOnlyList<GeneralLedgerTransactionInput> Transactions, bool Finalize);

public sealed record GeneralLedgerImportBatchSummary(
  Guid ImportBatchId, string Status, int AcceptedChunkCount, int AcceptedTransactionCount,
  int AcceptedLineCount, int ExpectedChunkCount, int ExpectedTransactionCount,
  int ExpectedLineCount, string? NormalizedDatasetDigest);

public static class ClientAccountingService
{
  private const int MaxGlTransactions = 100_000;
  private const int MaxGlLines = 500_000;
  private const int MaxGlChunkTransactions = 10_000;
  private const int MaxGlChunkLines = 50_000;
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientAccountingProfileRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.FunctionalCurrency) ? AccountingDefaults.DefaultCurrency : request.FunctionalCurrency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid client accounting profile is required.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.ClientAccountingProfiles.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The client already has an accounting profile.");

    var profile = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      Jurisdiction = request.Jurisdiction.Trim(), FunctionalCurrency = currency,
      FiscalYearStartMonth = request.FiscalYearStartMonth, FiscalYearStartDay = request.FiscalYearStartDay,
      SourceSystem = request.SourceSystem.Trim(), SourceSystemIdentifier = request.SourceSystemIdentifier.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientAccountingProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static async Task<CommandResult<Guid>> CreatePeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, ReportingPeriodRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || string.IsNullOrWhiteSpace(request.PeriodCode) ||
        request.StartDate > request.EndDate || string.IsNullOrWhiteSpace(request.Basis) ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid reporting period is required.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.PriorPeriodId.HasValue && !await db.ClientReportingPeriods.AnyAsync(x =>
        x.Id == request.PriorPeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The prior period is outside the client scope.");
    var duplicate = await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.PeriodCode == request.PeriodCode.Trim() && x.Basis == request.Basis.Trim(), ct);
    if (duplicate)
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The reporting period already exists.");
    var period = new ClientReportingPeriod
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      PeriodCode = request.PeriodCode.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
      Basis = request.Basis.Trim(), Currency = currency, PriorPeriodId = request.PriorPeriodId,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientReportingPeriods.Add(period);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(period.Id);
  }

  public static async Task<CommandResult<Guid>> RollForwardPeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, RollForwardPeriodRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    var sourceHash = request.SourceHash.Trim().ToLowerInvariant();
    if (request.ClientId == Guid.Empty || request.PriorPeriodId == Guid.Empty || string.IsNullOrWhiteSpace(request.PeriodCode) ||
        request.StartDate > request.EndDate || string.IsNullOrWhiteSpace(request.Basis) ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        !IsSha256(sourceHash) || string.IsNullOrWhiteSpace(request.EvidenceReference) ||
        request.PriorClosingAmount != MoneyPolicy.Normalize(request.PriorClosingAmount) ||
        request.CurrentOpeningAmount != MoneyPolicy.Normalize(request.CurrentOpeningAmount))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A roll-forward needs valid dates, currency, opening amounts, source hash and evidence.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var prior = await db.ClientReportingPeriods
      .FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE id = {request.PriorPeriodId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (prior is null || prior.ClientId != request.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The prior period is outside the client scope.");
    if (prior.Status != AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only a closed period can be rolled forward.");
    if (request.StartDate <= prior.EndDate || !string.Equals(currency, prior.Currency, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The next period must follow the closed period and use the same reporting currency.");
    if (await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.PeriodCode == request.PeriodCode.Trim() && x.Basis == request.Basis.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The reporting period already exists.");

    if (request.SourcePackageId is { } sourcePackageId)
    {
      var sourcePackage = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == sourcePackageId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
      var priorStart = prior.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      var priorEnd = prior.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
      if (sourcePackage is null || sourcePackage.Status != AccountingPackageStates.PackageValidated ||
          sourcePackage.PeriodStart != priorStart || sourcePackage.PeriodEnd != priorEnd ||
          !string.Equals(sourcePackage.Currency, currency, StringComparison.Ordinal) ||
          !string.Equals(sourcePackage.CalculationHash, sourceHash, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale,
          "The opening source package is outside the closed prior period or has a different hash.");
    }

    var current = new ClientReportingPeriod
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      PeriodCode = request.PeriodCode.Trim(), StartDate = request.StartDate, EndDate = request.EndDate,
      Basis = request.Basis.Trim(), Currency = currency, PriorPeriodId = prior.Id,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientReportingPeriods.Add(current);
    var priorBooks = await db.ClientReportingBooks.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == prior.Id)
      .ToListAsync(ct);
    db.ClientReportingBooks.AddRange(priorBooks.Select(x => new ClientReportingBook
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, PeriodId = current.Id,
      Code = x.Code, Basis = x.Basis, InclusionRule = x.InclusionRule, Currency = x.Currency,
      Status = AccountingWorkflowStates.Draft, Revision = 1, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    }));
    var residual = MoneyPolicy.Normalize(request.CurrentOpeningAmount - request.PriorClosingAmount);
    db.OpeningBalanceBridges.Add(new OpeningBalanceBridge
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      CurrentPeriodId = current.Id, PriorPeriodId = prior.Id, SourcePackageId = request.SourcePackageId,
      SourceHash = sourceHash, PriorClosingAmount = MoneyPolicy.Normalize(request.PriorClosingAmount),
      CurrentOpeningAmount = MoneyPolicy.Normalize(request.CurrentOpeningAmount), Residual = residual,
      Status = residual == 0m ? "RECONCILED" : "UNEXPLAINED",
      EvidenceReference = request.EvidenceReference.Trim(), CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(current.Id);
  }

  public static async Task<CommandResult<Guid>> CreateBookAsync(
    IClientAccountingDbContext db, ActorContext actor, ReportingBookRequest request,
    CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.Currency) ? AccountingDefaults.DefaultCurrency : request.Currency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || string.IsNullOrWhiteSpace(request.Code) ||
        string.IsNullOrWhiteSpace(request.Basis) || string.IsNullOrWhiteSpace(request.InclusionRule) ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z'))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A reporting book must identify its basis and inclusion rule.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "A closed period cannot receive a new book.");
    if (await db.ClientReportingBooks.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.PeriodId == request.PeriodId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The reporting book already exists.");
    var book = new ClientReportingBook
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      PeriodId = request.PeriodId, Code = request.Code.Trim(), Basis = request.Basis.Trim(),
      InclusionRule = request.InclusionRule.Trim(), Currency = currency,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientReportingBooks.Add(book);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(book.Id);
  }

  public static async Task<CommandResult<Guid>> CreateOpeningBalanceBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, OpeningBalanceBridgeRequest request,
    CancellationToken ct = default)
  {
    var sourceHash = request.SourceHash.Trim().ToLowerInvariant();
    if (request.ClientId == Guid.Empty || request.CurrentPeriodId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.EvidenceReference) || !IsSha256(sourceHash))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "An opening bridge needs an exact source hash and evidence reference.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CurrentPeriodId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null || request.PriorPeriodId != period.PriorPeriodId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The opening bridge periods are outside the declared client period lineage.");
    if (request.PriorClosingAmount != MoneyPolicy.Normalize(request.PriorClosingAmount) ||
        request.CurrentOpeningAmount != MoneyPolicy.Normalize(request.CurrentOpeningAmount))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected, "Opening bridge amounts exceed accounting precision.");
    if (request.SourcePackageId is { } packageId)
    {
      var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == packageId &&
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.Status == AccountingPackageStates.PackageValidated, ct);
      if (package is null || !string.Equals(package.CalculationHash, sourceHash, StringComparison.OrdinalIgnoreCase))
        return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "The opening bridge source package is outside the client or has a different hash.");
    }
    if (await db.OpeningBalanceBridges.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.CurrentPeriodId == request.CurrentPeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The current period already has an opening bridge.");
    var residual = MoneyPolicy.Normalize(request.CurrentOpeningAmount - request.PriorClosingAmount);
    var bridge = new OpeningBalanceBridge
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      CurrentPeriodId = request.CurrentPeriodId, PriorPeriodId = request.PriorPeriodId,
      SourcePackageId = request.SourcePackageId, SourceHash = sourceHash,
      PriorClosingAmount = MoneyPolicy.Normalize(request.PriorClosingAmount),
      CurrentOpeningAmount = MoneyPolicy.Normalize(request.CurrentOpeningAmount), Residual = residual,
      Status = residual == 0m ? "RECONCILED" : "UNEXPLAINED",
      EvidenceReference = request.EvidenceReference.Trim(), CreatedAt = DateTimeOffset.UtcNow
    };
    db.OpeningBalanceBridges.Add(bridge);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(bridge.Id);
  }

  public static async Task<CommandResult> ApproveOpeningBalanceBridgeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid bridgeId,
    CancellationToken ct = default)
  {
    var bridge = await db.OpeningBalanceBridges.SingleOrDefaultAsync(x => x.Id == bridgeId && x.FirmId == actor.FirmId, ct);
    if (bridge is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, bridge.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (bridge.Status != "RECONCILED")
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An unexplained opening difference blocks bridge approval.");
    if (bridge.ApprovedByUserId is not null || bridge.CreatedAt == default)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The opening bridge is already approved.");
    if (bridge.SourceHash.Length != 64 || bridge.EvidenceReference.Length == 0)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The opening bridge source evidence is stale.");
    bridge.Status = AccountingWorkflowStates.Approved;
    bridge.ApprovedByUserId = actor.UserId;
    bridge.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreatePeriodRestatementAsync(
    IClientAccountingDbContext db, ActorContext actor, CreatePeriodRestatementRequest request,
    CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || request.OriginalPackageId == Guid.Empty ||
        request.RevisedPackageId == Guid.Empty || request.OriginalPackageId == request.RevisedPackageId ||
        string.IsNullOrWhiteSpace(request.RevisedBasis) || string.IsNullOrWhiteSpace(request.Reason) ||
        string.IsNullOrWhiteSpace(request.EvidenceReference))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A restatement needs distinct issued/revised packages, a revised basis, reason and evidence.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    if (period.Status != AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Restatements require an issued closed reporting period.");
    var periodStart = period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var periodEnd = period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      (x.Id == request.OriginalPackageId || x.Id == request.RevisedPackageId)).ToListAsync(ct);
    var original = packages.SingleOrDefault(x => x.Id == request.OriginalPackageId);
    var revised = packages.SingleOrDefault(x => x.Id == request.RevisedPackageId);
    if (original is null || revised is null || original.Status != AccountingPackageStates.PackageValidated ||
        revised.Status != AccountingPackageStates.PackageValidated || original.EngagementId != revised.EngagementId ||
        original.PeriodStart != periodStart || original.PeriodEnd != periodEnd ||
        revised.PeriodStart != periodStart || revised.PeriodEnd != periodEnd ||
        !string.Equals(original.Currency, period.Currency, StringComparison.Ordinal) ||
        !string.Equals(revised.Currency, period.Currency, StringComparison.Ordinal) ||
        string.Equals(original.CalculationHash, revised.CalculationHash, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "Both packages must be validated, immutable, period-bound and materially different before restatement.");
    if (await db.ClientPeriodRestatements.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.OriginalPackageId == original.Id && x.RevisedPackageId == revised.Id, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This package restatement already exists.");
    var restatement = new ClientPeriodRestatement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      EngagementId = original.EngagementId, PeriodId = period.Id,
      OriginalPackageId = original.Id, RevisedPackageId = revised.Id,
      OriginalPackageHash = original.CalculationHash, RevisedPackageHash = revised.CalculationHash,
      RevisedBasis = request.RevisedBasis.Trim(), Reason = request.Reason.Trim(),
      EvidenceReference = request.EvidenceReference.Trim(), CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientPeriodRestatements.Add(restatement);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(restatement.Id);
  }

  public static async Task<CommandResult> ApprovePeriodRestatementAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid restatementId,
    CancellationToken ct = default)
  {
    var restatement = await db.ClientPeriodRestatements.SingleOrDefaultAsync(x =>
      x.Id == restatementId && x.FirmId == actor.FirmId, ct);
    if (restatement is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, restatement.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (restatement.Status != AccountingWorkflowStates.Submitted || restatement.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.ReconciliationRejected,
        "A submitted restatement requires an independent reviewer.");
    var packages = await db.FinancialPackages.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == restatement.ClientId &&
      (x.Id == restatement.OriginalPackageId || x.Id == restatement.RevisedPackageId)).ToListAsync(ct);
    if (packages.Count != 2 || packages.Any(x => x.Status != AccountingPackageStates.PackageValidated) ||
        packages.Single(x => x.Id == restatement.OriginalPackageId).CalculationHash != restatement.OriginalPackageHash ||
        packages.Single(x => x.Id == restatement.RevisedPackageId).CalculationHash != restatement.RevisedPackageHash)
      return CommandResult.Fail(ErrorCodes.GenerationStale, "Restatement package lineage changed; reload the issued evidence.");
    restatement.Status = AccountingWorkflowStates.Approved;
    restatement.ApprovedByUserId = actor.UserId;
    restatement.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateChartVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    string sourceScope, DateOnly effectiveFrom, CancellationToken ct = default)
  {
    var auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (string.IsNullOrWhiteSpace(sourceScope))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A chart source scope is required.");
    var version = (await db.ClientChartVersions.Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var chart = new ClientChartVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      Version = version, SourceScope = sourceScope.Trim(), EffectiveFrom = effectiveFrom,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientChartVersions.Add(chart);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(chart.Id);
  }

  public static async Task<CommandResult> AddAccountsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    IReadOnlyList<ClientAccountInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x => string.IsNullOrWhiteSpace(x.StableIdentity) ||
        string.IsNullOrWhiteSpace(x.AccountCode) || string.IsNullOrWhiteSpace(x.AccountName) ||
        string.IsNullOrWhiteSpace(x.AccountType) || string.IsNullOrWhiteSpace(x.NormalBalance)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every client account needs typed identity and classification.");
    var chart = await db.ClientChartVersions.SingleOrDefaultAsync(x => x.Id == chartVersionId && x.FirmId == actor.FirmId, ct);
    if (chart is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (chart.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Published chart versions are immutable.");
    var duplicateInput = inputs.GroupBy(x => x.StableIdentity.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1) ||
      inputs.GroupBy(x => x.AccountCode.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1);
    if (duplicateInput)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Account identities and codes must be unique within a chart version.");
    var stable = inputs.ToDictionary(x => x.StableIdentity.Trim(), StringComparer.OrdinalIgnoreCase);
    if (inputs.Any(x => x.ParentStableIdentity is not null && !stable.ContainsKey(x.ParentStableIdentity.Trim()) &&
        !db.ClientAccounts.Any(a => a.FirmId == chart.FirmId && a.ClientId == chart.ClientId && a.ChartVersionId == chart.Id &&
          a.StableIdentity == x.ParentStableIdentity.Trim())))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every parent account must exist in the same client scope.");
    if (HasParentCycle(inputs, stable))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The client chart contains a parent cycle.");
    var existingCodes = await db.ClientAccounts.Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id)
      .Select(x => x.AccountCode).ToListAsync(ct);
    if (inputs.Any(x => existingCodes.Contains(x.AccountCode.Trim(), StringComparer.OrdinalIgnoreCase)))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "An account code already exists in this chart version.");
    var byStable = await db.ClientAccounts.Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id)
      .ToDictionaryAsync(x => x.StableIdentity, StringComparer.OrdinalIgnoreCase, ct);
    if (inputs.Any(x => x.ParentStableIdentity is { } parent &&
        (stable.TryGetValue(parent.Trim(), out var declaredParent) ? declaredParent.IsPosting : byStable[parent.Trim()].IsPosting)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A posting account cannot be a chart parent.");
    foreach (var input in inputs)
    {
      if (input.ParentStableIdentity is null || stable.ContainsKey(input.ParentStableIdentity.Trim()))
        continue;
      Guid? parentId = input.ParentStableIdentity is null ? null :
        (stable.TryGetValue(input.ParentStableIdentity.Trim(), out _) ? Guid.Empty : byStable[input.ParentStableIdentity.Trim()].Id);
      db.ClientAccounts.Add(new ClientAccount
      {
        Id = Guid.CreateVersion7(), FirmId = chart.FirmId, ClientId = chart.ClientId, ChartVersionId = chart.Id,
        StableIdentity = input.StableIdentity.Trim(), AccountCode = input.AccountCode.Trim(), AccountName = input.AccountName.Trim(),
        AccountType = input.AccountType.Trim(), NormalBalance = input.NormalBalance.Trim(), ParentAccountId = parentId,
        IsPosting = input.IsPosting, CreatedAt = DateTimeOffset.UtcNow
      });
    }
    // Parents declared in the same request are inserted after IDs are known so the FK remains exact.
    var pending = inputs.Where(x => x.ParentStableIdentity is null || stable.ContainsKey(x.ParentStableIdentity.Trim())).ToArray();
    var added = pending.Select(input => new ClientAccount
    {
      Id = Guid.CreateVersion7(), FirmId = chart.FirmId, ClientId = chart.ClientId, ChartVersionId = chart.Id,
      StableIdentity = input.StableIdentity.Trim(), AccountCode = input.AccountCode.Trim(), AccountName = input.AccountName.Trim(),
      AccountType = input.AccountType.Trim(), NormalBalance = input.NormalBalance.Trim(), IsPosting = input.IsPosting,
      CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    var addedByStable = added.ToDictionary(x => x.StableIdentity, StringComparer.OrdinalIgnoreCase);
    foreach (var account in added)
      if (stable[account.StableIdentity].ParentStableIdentity is { } parent)
        account.ParentAccountId = addedByStable.TryGetValue(parent.Trim(), out var newParent)
          ? newParent.Id : byStable[parent.Trim()].Id;
    db.ClientAccounts.AddRange(added);
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> AddSourceAccountAliasesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    IReadOnlyList<SourceAccountAliasInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x => x.ClientAccountId == Guid.Empty ||
        string.IsNullOrWhiteSpace(x.SourceSystem) || string.IsNullOrWhiteSpace(x.AliasCode)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every source alias needs a client account, source system and code.");
    var chart = await db.ClientChartVersions.SingleOrDefaultAsync(x => x.Id == chartVersionId && x.FirmId == actor.FirmId, ct);
    if (chart is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (chart.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Published chart versions are immutable.");
    var normalized = inputs.Select(x => new
    {
      x.ClientAccountId, SourceSystem = x.SourceSystem.Trim(), AliasCode = x.AliasCode.Trim(), AliasName = x.AliasName.Trim()
    }).ToArray();
    if (normalized.GroupBy(x => (x.SourceSystem, x.AliasCode), StringTupleComparer.Instance).Any(x => x.Count() > 1))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Source aliases must be unique within a chart and source system.");
    var accountIds = normalized.Select(x => x.ClientAccountId).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId &&
      x.ChartVersionId == chart.Id && accountIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
    if (accounts.Count != accountIds.Length)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Every source alias account must belong to the same chart and client.");
    var existingAliases = await db.SourceAccountAliases.AsNoTracking().Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId &&
      x.ChartVersionId == chart.Id).Select(x => new { x.SourceSystem, x.AliasCode }).ToListAsync(ct);
    if (existingAliases.Any(existing => normalized.Any(input =>
        string.Equals(existing.SourceSystem, input.SourceSystem, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.AliasCode, input.AliasCode, StringComparison.OrdinalIgnoreCase))))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "A source alias already exists in this chart.");
    db.SourceAccountAliases.AddRange(normalized.Select(x => new SourceAccountAlias
    {
      Id = Guid.CreateVersion7(), FirmId = chart.FirmId, ClientId = chart.ClientId,
      ChartVersionId = chart.Id, ClientAccountId = x.ClientAccountId, SourceSystem = x.SourceSystem,
      AliasCode = x.AliasCode, AliasName = x.AliasName, CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> PublishChartVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    CancellationToken ct = default)
  {
    var chart = await db.ClientChartVersions.SingleOrDefaultAsync(x => x.Id == chartVersionId && x.FirmId == actor.FirmId, ct);
    if (chart is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (chart.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft chart can be published.");
    if (chart.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The chart preparer cannot publish the same version.");
    var accounts = await db.ClientAccounts.Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id).ToListAsync(ct);
    if (accounts.Count == 0 || accounts.Any(x => x.ParentAccountId == x.Id))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "A chart needs at least one valid account.");
    chart.Status = AccountingWorkflowStates.Approved;
    chart.PublishedByUserId = actor.UserId;
    chart.PublishedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> AddDimensionDefinitionsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    IReadOnlyList<AccountingDimensionInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x =>
        !AccountingDimensionTypes.All.Contains(x.DimensionType.Trim().ToUpperInvariant()) ||
        string.IsNullOrWhiteSpace(x.Code) || x.Code.Trim().Length > 100 ||
        string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 300))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every accounting dimension needs a supported type, code and name.");
    var auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var normalized = inputs.Select(x => new
    {
      DimensionType = x.DimensionType.Trim().ToUpperInvariant(), Code = x.Code.Trim(), Name = x.Name.Trim()
    }).ToArray();
    if (normalized.GroupBy(x => (x.DimensionType, x.Code), StringTupleComparer.Instance).Any(x => x.Count() > 1))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Accounting dimension codes must be unique within each type.");
    var existing = await db.ClientAccountingDimensionDefinitions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .Select(x => new { x.DimensionType, x.Code }).ToListAsync(ct);
    if (normalized.Any(input => existing.Any(x => x.DimensionType == input.DimensionType && x.Code == input.Code)))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "An accounting dimension code already exists for this client.");
    db.ClientAccountingDimensionDefinitions.AddRange(normalized.Select(x => new ClientAccountingDimensionDefinition
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      DimensionType = x.DimensionType, Code = x.Code, Name = x.Name,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateTaxonomyVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, string code, string framework,
    string name, DateOnly effectiveFrom, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(name))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Taxonomy code, framework and name are required.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.ReportingTaxonomyVersions.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The taxonomy code already exists.");
    var taxonomy = new ReportingTaxonomyVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = code.Trim(), Framework = framework.Trim(),
      Name = name.Trim(), EffectiveFrom = effectiveFrom, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ReportingTaxonomyVersions.Add(taxonomy);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(taxonomy.Id);
  }

  public static async Task<CommandResult> AddTaxonomyNodesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid taxonomyVersionId,
    IReadOnlyList<TaxonomyNodeInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name) ||
        string.IsNullOrWhiteSpace(x.StatementSection)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every taxonomy node needs a typed presentation location.");
    var taxonomy = await db.ReportingTaxonomyVersions.SingleOrDefaultAsync(x => x.Id == taxonomyVersionId && x.FirmId == actor.FirmId, ct);
    if (taxonomy is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (taxonomy.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Approved taxonomy versions are immutable.");
    var map = inputs.ToDictionary(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase);
    var existing = await db.ReportingTaxonomyNodes.Where(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id).ToListAsync(ct);
    var existingByCode = existing.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
    if (map.Count != inputs.Count || inputs.Any(x => x.ParentCode is not null && !map.ContainsKey(x.ParentCode.Trim()) &&
        !existingByCode.ContainsKey(x.ParentCode.Trim())))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Taxonomy parent codes must exist in the same taxonomy version.");
    if (HasTaxonomyCycle(inputs, map))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The taxonomy contains a parent cycle.");
    if (inputs.Any(x => existingByCode.ContainsKey(x.Code.Trim())))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "A taxonomy node code already exists.");
    var nodes = inputs.Select(x => new ReportingTaxonomyNode
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TaxonomyVersionId = taxonomy.Id,
      Code = x.Code.Trim(), Name = x.Name.Trim(), StatementSection = x.StatementSection.Trim().ToUpperInvariant(),
      DisplaySign = x.DisplaySign.Trim(), NormalBalance = x.NormalBalance.Trim(), DisclosureArea = x.DisclosureArea.Trim(),
      IsPosting = x.IsPosting, Applicability = x.Applicability.Trim(), CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    var byCode = nodes.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
    foreach (var node in nodes)
      if (map[node.Code].ParentCode is { } parent)
        node.ParentNodeId = byCode.TryGetValue(parent.Trim(), out var newParent)
          ? newParent.Id : existingByCode[parent.Trim()].Id;
    db.ReportingTaxonomyNodes.AddRange(nodes);
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> PublishTaxonomyVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid taxonomyVersionId,
    CancellationToken ct = default)
  {
    var taxonomy = await db.ReportingTaxonomyVersions.SingleOrDefaultAsync(x => x.Id == taxonomyVersionId && x.FirmId == actor.FirmId, ct);
    if (taxonomy is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (taxonomy.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft taxonomy can be approved.");
    if (taxonomy.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The taxonomy preparer cannot approve the same version.");
    if (!await db.ReportingTaxonomyNodes.AnyAsync(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id, ct))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "A taxonomy version needs at least one node.");
    taxonomy.Status = AccountingWorkflowStates.Approved;
    taxonomy.ApprovedByUserId = actor.UserId;
    taxonomy.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> CreateCapabilityProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, CapabilityProfileRequest request,
    CancellationToken ct = default)
  {
    var serviceKind = (request.ServiceKind ?? string.Empty).Trim().ToUpperInvariant();
    var validScope = serviceKind switch
    {
      AccountingCapabilityServiceKinds.GroupReporting => request.GroupId.HasValue && !request.ClientId.HasValue,
      AccountingCapabilityServiceKinds.EntityReporting or AccountingCapabilityServiceKinds.AuditOnly =>
        request.ClientId.HasValue && !request.GroupId.HasValue,
      _ => false
    };
    if (!validScope || string.IsNullOrWhiteSpace(request.Framework))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A capability needs a supported service kind and its matching client or group scope.");
    var currency = (string.IsNullOrWhiteSpace(request.ReportingCurrency) ? AccountingDefaults.DefaultCurrency : request.ReportingCurrency).Trim().ToUpperInvariant();
    if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        (!string.IsNullOrWhiteSpace(request.ConsolidationMethod) &&
         request.ConsolidationMethod.Trim().ToUpperInvariant() is not (ConsolidationCalculator.RestrictedMethod or ConsolidationCalculator.ForeignOperationMethod)))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The requested accounting or consolidation method is not enabled.");
    var auth = request.ClientId.HasValue
      ? await AuthorizeClientAsync(db, actor, request.ClientId.Value, ReviewerRoles, ct)
      : await AuthorizeGroupAsync(db, actor, request.GroupId!.Value, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var profile = new AccountingCapabilityProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, GroupId = request.GroupId,
      ServiceKind = serviceKind, Framework = request.Framework.Trim(), Edition = request.Edition.Trim(),
      PeriodRule = request.PeriodRule.Trim(), ReportingCurrency = currency, AccountingMethod = request.AccountingMethod.Trim(),
      ConsolidationMethod = request.ConsolidationMethod.Trim().ToUpperInvariant(), ReviewHierarchy = request.ReviewHierarchy.Trim(),
      TemplateFamily = request.TemplateFamily.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingCapabilityProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static async Task<CommandResult> RecordCapabilityAcceptanceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid profileId, string stage,
    string evidenceReference, CancellationToken ct = default)
  {
    stage = stage.Trim().ToUpperInvariant();
    if (stage is not (AccountingCapabilityAcceptanceStages.LocalConstruction or AccountingCapabilityAcceptanceStages.MethodOwnerApproval or
      AccountingCapabilityAcceptanceStages.LiveEvidence or AccountingCapabilityAcceptanceStages.Released) ||
        string.IsNullOrWhiteSpace(evidenceReference))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Capability acceptance needs a known stage and evidence.");
    var profile = await db.AccountingCapabilityProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == profileId && x.FirmId == actor.FirmId, ct);
    if (profile is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = profile.ClientId.HasValue
      ? await AuthorizeClientAsync(db, actor, profile.ClientId.Value, ReviewerRoles, ct)
      : await AuthorizeGroupAsync(db, actor, profile.GroupId!.Value, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (stage is AccountingCapabilityAcceptanceStages.MethodOwnerApproval or AccountingCapabilityAcceptanceStages.Released &&
        profile.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The capability preparer cannot provide independent acceptance.");
    if (await db.AccountingCapabilityAcceptances.AnyAsync(x => x.FirmId == actor.FirmId && x.CapabilityProfileId == profileId && x.Stage == stage, ct))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "This capability stage already has an acceptance record.");
    db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, CapabilityProfileId = profileId,
      Stage = stage, Status = AccountingWorkflowStates.Approved, EvidenceReference = evidenceReference.Trim(),
      DecidedByUserId = actor.UserId, DecidedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> ImportGeneralLedgerAsync(
    IClientAccountingDbContext db, ActorContext actor, GeneralLedgerImportRequest request,
    CancellationToken ct = default)
  {
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.PeriodId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.ProfileVersion) || string.IsNullOrWhiteSpace(request.ParserVersion) ||
        string.IsNullOrWhiteSpace(request.LegalEntityKey) || request.Transactions.Count == 0 ||
        currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') || !IsSha256(request.RawFileSha256Hex) ||
        string.IsNullOrWhiteSpace(request.ReceiptReference))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "The GL import profile or source identity is invalid.");
    var lineCount = request.Transactions.Sum(x => (long)x.Lines.Count);
    if (request.Transactions.Count > MaxGlTransactions || lineCount > MaxGlLines)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "The GL import exceeds the bounded interactive limit; use an approved durable batch profile.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.PeriodId && x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct);
    if (period is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "A closed period cannot receive a GL import.");
    if (!string.Equals(period.Currency, currency, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "The GL currency does not match the selected reporting period.");
    if (request.BookId.HasValue && !await db.ClientReportingBooks.AnyAsync(x => x.Id == request.BookId &&
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.PeriodId == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting book is outside the client period.");
    var chart = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= period.EndDate &&
      (x.EffectiveTo == null || x.EffectiveTo >= period.StartDate)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (chart is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A published client chart is required before GL import.");
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.ChartVersionId == chart.Id)
      .ToDictionaryAsync(x => x.AccountCode, StringComparer.OrdinalIgnoreCase, ct);
    var dimensionCodes = await LoadDimensionCodesAsync(db, actor.FirmId, request.ClientId, ct);
    var journals = request.Transactions.Select(x => x.StableJournalId.Trim()).ToArray();
    if (journals.Any(string.IsNullOrWhiteSpace) || journals.Distinct(StringComparer.OrdinalIgnoreCase).Count() != journals.Length ||
        request.Transactions.SelectMany(x => x.Lines).Select(x => x.StableLineId.Trim()).Any(string.IsNullOrWhiteSpace))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "GL journal and line identities must be unique and non-empty.");
    var lineIds = request.Transactions.SelectMany(x => x.Lines).Select(x => x.StableLineId.Trim()).ToArray();
    if (lineIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != lineIds.Length)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "GL line identities must be unique.");
    foreach (var transaction in request.Transactions)
    {
      if (transaction.PostingDate < period.StartDate || transaction.PostingDate > period.EndDate || transaction.Lines.Count == 0)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "Every GL journal must be populated and inside the selected period.");
      if (transaction.Lines.Any(x => x.Debit < 0m || x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m) ||
          !accounts.ContainsKey(x.AccountCode.Trim()) || x.OriginalCurrency.Trim().ToUpperInvariant() != currency))
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "GL lines must use published accounts, one-sided amounts and the selected currency.");
      if (MoneyPolicy.Normalize(transaction.Lines.Sum(x => x.Debit) - transaction.Lines.Sum(x => x.Credit)) != 0m)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, $"Journal {transaction.StableJournalId} is not balanced.");
    }
    if (ValidateDimensionValues(request.Transactions, dimensionCodes) is { } dimensionError)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, dimensionError);
    var normalized = string.Join('\n', request.Transactions.OrderBy(x => x.StableJournalId, StringComparer.Ordinal)
      .SelectMany(x => x.Lines.OrderBy(y => y.StableLineId, StringComparer.Ordinal).Select(y => string.Join('|',
        x.StableJournalId.Trim(), y.StableLineId.Trim(), y.AccountCode.Trim(),
        y.Debit.ToString("0.000000", CultureInfo.InvariantCulture), y.Credit.ToString("0.000000", CultureInfo.InvariantCulture), currency))));
    var normalizedHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("gl-import.v1\n" + normalized));
    if (await db.SourceImportBatches.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.RawFileSha256Hex == request.RawFileSha256Hex.Trim().ToLowerInvariant(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate, "The source file was already imported for this engagement.");
    if (await db.SourceImportBatches.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.NormalizedDatasetDigest == normalizedHash && x.PeriodId == request.PeriodId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate, "The normalized GL dataset was already imported for this period.");
    var batch = new SourceImportBatch
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, BookId = request.BookId, SourceKind = "GL", ProfileVersion = request.ProfileVersion.Trim(),
      ParserVersion = request.ParserVersion.Trim(), RawFileSha256Hex = request.RawFileSha256Hex.Trim().ToLowerInvariant(),
      NormalizedDatasetDigest = normalizedHash, LegalEntityKey = request.LegalEntityKey.Trim(), Currency = currency,
      RowCount = request.Transactions.Sum(x => x.Lines.Count), Status = "SEALED", ReceiptReference = request.ReceiptReference.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.SourceImportBatches.Add(batch);
    foreach (var input in request.Transactions)
    {
      var transaction = new GeneralLedgerTransaction
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
        ImportBatchId = batch.Id, StableJournalId = input.StableJournalId.Trim(), DocumentNumber = input.DocumentNumber.Trim(),
        PostingDate = input.PostingDate, DocumentDate = input.DocumentDate, SourceUser = input.SourceUser.Trim(),
        SourceSystem = input.SourceSystem.Trim(), ReversalReference = input.ReversalReference?.Trim(), Currency = currency,
        IsManual = input.IsManual, IsYearEnd = input.IsYearEnd, CreatedAt = batch.CreatedAt
      };
      db.GeneralLedgerTransactions.Add(transaction);
      foreach (var line in input.Lines)
        db.GeneralLedgerLines.Add(new GeneralLedgerLine
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
          ImportBatchId = batch.Id, TransactionId = transaction.Id, StableLineId = line.StableLineId.Trim(),
          AccountCode = line.AccountCode.Trim(), ClientAccountId = accounts[line.AccountCode.Trim()].Id,
          Debit = MoneyPolicy.Normalize(line.Debit), Credit = MoneyPolicy.Normalize(line.Credit),
          OriginalCurrency = currency, OriginalAmount = MoneyPolicy.Normalize(line.OriginalAmount),
          FunctionalAmount = MoneyPolicy.Normalize(line.FunctionalAmount), PartyIdentifier = line.PartyIdentifier.Trim(),
          Branch = line.Branch.Trim(), CostCentre = line.CostCentre.Trim(), Department = line.Department.Trim(),
          Project = line.Project.Trim(), IntercompanyCounterparty = line.IntercompanyCounterparty.Trim(),
          IsManual = input.IsManual, IsYearEnd = input.IsYearEnd, CreatedAt = batch.CreatedAt
        });
    }
    try
    {
      await db.SaveChangesAsync(ct);
      return CommandResult<Guid>.Ok(batch.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The GL source identity changed; reload the import preview.");
    }
  }

  public static async Task<CommandResult<Guid>> BeginGeneralLedgerImportAsync(
    IClientAccountingDbContext db, ActorContext actor, GeneralLedgerImportStartRequest request,
    CancellationToken ct = default)
  {
    var currency = request.Currency.Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || request.EngagementId == Guid.Empty || request.PeriodId == Guid.Empty ||
        string.IsNullOrWhiteSpace(request.ProfileVersion) || string.IsNullOrWhiteSpace(request.ParserVersion) ||
        string.IsNullOrWhiteSpace(request.LegalEntityKey) || string.IsNullOrWhiteSpace(request.ReceiptReference) ||
        request.ExpectedChunkCount is < 1 or > 10_000 || request.ExpectedTransactionCount is < 1 or > MaxGlTransactions ||
        request.ExpectedLineCount is < 1 or > MaxGlLines || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        !IsSha256(request.RawFileSha256Hex))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, "The GL import profile or expected counts are invalid.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, request.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var context = await ResolveGeneralLedgerImportContextAsync(db, actor, request.ClientId, request.PeriodId,
      request.BookId, currency, ct);
    if (!context.Succeeded)
      return CommandResult<Guid>.Fail(context.ErrorCode!, context.Message!);
    var rawHash = request.RawFileSha256Hex.Trim().ToLowerInvariant();
    if (await db.SourceImportBatches.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
        x.RawFileSha256Hex == rawHash, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate, "The source file was already imported for this engagement.");
    var batch = new SourceImportBatch
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = request.PeriodId, BookId = request.BookId, SourceKind = "GL", ProfileVersion = request.ProfileVersion.Trim(),
      ParserVersion = request.ParserVersion.Trim(), RawFileSha256Hex = rawHash, LegalEntityKey = request.LegalEntityKey.Trim(),
      Currency = currency, ExpectedChunkCount = request.ExpectedChunkCount,
      ExpectedTransactionCount = request.ExpectedTransactionCount, ExpectedLineCount = request.ExpectedLineCount,
      Status = "LOADING", ReceiptReference = request.ReceiptReference.Trim(), CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.SourceImportBatches.Add(batch);
    try
    {
      await db.SaveChangesAsync(ct);
      return CommandResult<Guid>.Ok(batch.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The GL source identity changed; reload the import preview.");
    }
  }

  public static async Task<CommandResult<GeneralLedgerImportBatchSummary>> AppendGeneralLedgerChunkAsync(
    IClientAccountingDbContext db, ActorContext actor, GeneralLedgerImportChunkRequest request,
    CancellationToken ct = default)
  {
    if (request.ImportBatchId == Guid.Empty || request.ChunkNumber < 0 || !IsSha256(request.ChunkDigest) ||
        request.Transactions.Count == 0 || request.Transactions.Count > MaxGlChunkTransactions ||
        request.Transactions.Sum(x => (long)x.Lines.Count) > MaxGlChunkLines)
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The GL chunk identity or bounded chunk size is invalid.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var batch = await db.SourceImportBatches.FromSqlInterpolated(
      $"SELECT * FROM source_import_batches WHERE id = {request.ImportBatchId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (batch is null)
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, batch.ClientId, batch.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(auth.ErrorCode!, auth.Message!);
    if (batch.SourceKind != "GL" || batch.ExpectedChunkCount < 1 || batch.ExpectedTransactionCount < 1 ||
        batch.ExpectedLineCount < 1 || request.ChunkNumber >= batch.ExpectedChunkCount)
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The GL chunk does not match the declared import batch.");
    var currency = batch.Currency.Trim().ToUpperInvariant();
    var context = await ResolveGeneralLedgerImportContextAsync(db, actor, batch.ClientId, batch.PeriodId,
      batch.BookId, currency, ct);
    if (!context.Succeeded)
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(context.ErrorCode!, context.Message!);
    var validation = ValidateGeneralLedgerTransactions(request.Transactions, context.Value!.Period, currency,
      context.Value.Accounts, context.Value.DimensionCodes, MaxGlChunkTransactions, MaxGlChunkLines);
    if (!validation.Succeeded)
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(validation.ErrorCode!, validation.Message!);
    var lineCount = validation.Value;
    var digest = ComputeGeneralLedgerChunkDigest(currency, request.Transactions);
    var requestedDigest = request.ChunkDigest.Trim().ToLowerInvariant();
    if (!string.Equals(digest, requestedDigest, StringComparison.OrdinalIgnoreCase))
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.ManifestMismatch,
        "The GL chunk digest does not match its canonical content.");
    var existing = await db.GeneralLedgerImportChunks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ImportBatchId == batch.Id && x.ChunkNumber == request.ChunkNumber, ct);
    if (existing is not null && (!string.Equals(existing.ChunkDigest, requestedDigest, StringComparison.OrdinalIgnoreCase) ||
        existing.TransactionCount != request.Transactions.Count || existing.LineCount != lineCount))
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.IdempotencyConflict,
        "The GL chunk identity changed; reload the import preview.");
    if (batch.Status == "SEALED")
      return existing is not null
        ? CommandResult<GeneralLedgerImportBatchSummary>.Ok(Summarize(batch))
        : CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.ProtectedState, "The GL import batch is already sealed.");
    if (batch.Status != "LOADING")
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.ProtectedState, "The GL import batch is not loadable.");

    var isNewChunk = existing is null;
    var acceptedTransactions = batch.AcceptedTransactionCount + (isNewChunk ? request.Transactions.Count : 0);
    var acceptedLines = batch.AcceptedLineCount + (isNewChunk ? lineCount : 0);
    if (acceptedTransactions > batch.ExpectedTransactionCount || acceptedLines > batch.ExpectedLineCount ||
        (isNewChunk && batch.AcceptedChunkCount >= batch.ExpectedChunkCount))
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The GL chunk exceeds the declared import totals.");

    var chunkRows = await db.GeneralLedgerImportChunks.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ImportBatchId == batch.Id).OrderBy(x => x.ChunkNumber).ToListAsync(ct);
    if (request.Finalize)
    {
      if (request.ChunkNumber != batch.ExpectedChunkCount - 1 ||
          acceptedTransactions != batch.ExpectedTransactionCount || acceptedLines != batch.ExpectedLineCount ||
          batch.AcceptedChunkCount + (isNewChunk ? 1 : 0) != batch.ExpectedChunkCount)
        return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.GateBlocked,
          "The final GL chunk cannot seal an incomplete import batch.");
      if (isNewChunk)
        chunkRows.Add(new GeneralLedgerImportChunk { ChunkNumber = request.ChunkNumber, ChunkDigest = requestedDigest });
      var expectedNumbers = Enumerable.Range(0, batch.ExpectedChunkCount);
      if (chunkRows.Count != batch.ExpectedChunkCount || !chunkRows.Select(x => x.ChunkNumber).OrderBy(x => x).SequenceEqual(expectedNumbers))
        return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.GateBlocked,
          "GL chunks must be received once in contiguous order before sealing.");
    }

    if (isNewChunk)
    {
      var journalIds = request.Transactions.Select(x => x.StableJournalId.Trim()).ToArray();
      var lineIds = request.Transactions.SelectMany(x => x.Lines).Select(x => x.StableLineId.Trim()).ToArray();
      if (await db.GeneralLedgerTransactions.AnyAsync(x => x.FirmId == actor.FirmId && x.ImportBatchId == batch.Id && journalIds.Contains(x.StableJournalId), ct) ||
          await db.GeneralLedgerLines.AnyAsync(x => x.FirmId == actor.FirmId && x.ImportBatchId == batch.Id && lineIds.Contains(x.StableLineId), ct))
        return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.Accounting.ImportDuplicate,
          "A GL journal or line identity already exists in this import batch.");
      db.GeneralLedgerImportChunks.Add(new GeneralLedgerImportChunk
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = batch.ClientId, EngagementId = batch.EngagementId,
        ImportBatchId = batch.Id, ChunkNumber = request.ChunkNumber, ChunkDigest = requestedDigest,
        TransactionCount = request.Transactions.Count, LineCount = lineCount, CreatedAt = DateTimeOffset.UtcNow
      });
      foreach (var input in request.Transactions)
      {
        var transaction = new GeneralLedgerTransaction
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = batch.ClientId, EngagementId = batch.EngagementId,
          ImportBatchId = batch.Id, StableJournalId = input.StableJournalId.Trim(), DocumentNumber = input.DocumentNumber.Trim(),
          PostingDate = input.PostingDate, DocumentDate = input.DocumentDate, SourceUser = input.SourceUser.Trim(),
          SourceSystem = input.SourceSystem.Trim(), ReversalReference = input.ReversalReference?.Trim(), Currency = currency,
          IsManual = input.IsManual, IsYearEnd = input.IsYearEnd, CreatedAt = batch.CreatedAt
        };
        db.GeneralLedgerTransactions.Add(transaction);
        foreach (var line in input.Lines)
          db.GeneralLedgerLines.Add(new GeneralLedgerLine
          {
            Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = batch.ClientId, EngagementId = batch.EngagementId,
            ImportBatchId = batch.Id, TransactionId = transaction.Id, StableLineId = line.StableLineId.Trim(),
            AccountCode = line.AccountCode.Trim(), ClientAccountId = context.Value.Accounts[line.AccountCode.Trim()].Id,
            Debit = MoneyPolicy.Normalize(line.Debit), Credit = MoneyPolicy.Normalize(line.Credit), OriginalCurrency = currency,
            OriginalAmount = MoneyPolicy.Normalize(line.OriginalAmount), FunctionalAmount = MoneyPolicy.Normalize(line.FunctionalAmount),
            PartyIdentifier = line.PartyIdentifier.Trim(), Branch = line.Branch.Trim(), CostCentre = line.CostCentre.Trim(),
            Department = line.Department.Trim(), Project = line.Project.Trim(), IntercompanyCounterparty = line.IntercompanyCounterparty.Trim(),
            IsManual = input.IsManual, IsYearEnd = input.IsYearEnd, CreatedAt = batch.CreatedAt
          });
      }
      batch.AcceptedChunkCount++;
      batch.AcceptedTransactionCount = acceptedTransactions;
      batch.AcceptedLineCount = acceptedLines;
      batch.RowCount = acceptedLines;
    }
    if (request.Finalize)
    {
      var finalDigests = chunkRows.OrderBy(x => x.ChunkNumber).Select(x => $"{x.ChunkNumber}:{x.ChunkDigest}");
      batch.NormalizedDatasetDigest = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(
        "gl-import.stream.v1\n" + string.Join('\n', finalDigests)));
      batch.Status = "SEALED";
    }
    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<GeneralLedgerImportBatchSummary>.Ok(Summarize(batch));
    }
    catch (DbUpdateException)
    {
      return CommandResult<GeneralLedgerImportBatchSummary>.Fail(ErrorCodes.IdempotencyConflict,
        "The GL chunk identity changed; reload the import preview.");
    }
  }

  public static string ComputeGeneralLedgerChunkDigest(
    string currency, IReadOnlyList<GeneralLedgerTransactionInput> transactions)
  {
    var canonical = new
    {
      Currency = currency.Trim().ToUpperInvariant(),
      Transactions = transactions.OrderBy(x => x.StableJournalId, StringComparer.Ordinal).Select(x => new
      {
        x.StableJournalId, x.DocumentNumber, x.PostingDate, x.DocumentDate, x.SourceUser, x.SourceSystem,
        x.ReversalReference, x.IsManual, x.IsYearEnd,
        Lines = x.Lines.OrderBy(y => y.StableLineId, StringComparer.Ordinal).Select(y => new
        {
          y.StableLineId, y.AccountCode, y.Debit, y.Credit, y.OriginalCurrency, y.OriginalAmount, y.FunctionalAmount,
          y.PartyIdentifier, y.Branch, y.CostCentre, y.Department, y.Project, y.IntercompanyCounterparty
        }).ToArray()
      }).ToArray()
    };
    return Hashing.Sha256Hex(Encoding.UTF8.GetBytes("gl-import.chunk.v1\n" + JsonSerializer.Serialize(canonical)));
  }

  public static async Task<CommandResult> ClosePeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, string reason,
    CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods
      .FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE id = {periodId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, period.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The reporting period is already closed.");
    if (string.IsNullOrWhiteSpace(reason))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A close decision needs a reason.");
    var unresolved = await db.AccountingReconciliations.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved, ct);
    if (unresolved)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Unapproved accounting reconciliations block period close.");
    var unreviewedEcl = await db.EclAssessments.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected &&
      db.AccountingReconciliations.Any(r => r.Id == x.ReconciliationId && r.PeriodId == period.Id), ct);
    var unreviewedInventory = await db.InventoryValuationAssessments.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected &&
      db.AccountingReconciliations.Any(r => r.Id == x.ReconciliationId && r.PeriodId == period.Id), ct);
    var unreviewedSpecialist = await db.SpecialistAccountingSchedules.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
    var unreviewedAnalytics = await db.AnalyticalReviews.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.PeriodId == period.Id && x.Status != AccountingWorkflowStates.Approved && x.Status != AccountingWorkflowStates.Rejected, ct);
    var openRisk = await db.JournalRiskFlags.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
      x.Status == "OPEN" && db.SourceImportBatches.Any(b => b.Id == x.ImportBatchId && b.PeriodId == period.Id), ct);
    if (unreviewedEcl || unreviewedInventory || unreviewedSpecialist || unreviewedAnalytics || openRisk)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Unreviewed accounting analysis or journal-risk evidence blocks period close.");
    var periodStart = period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var periodEnd = period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var packageIds = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == period.ClientId &&
        x.PeriodStart == periodStart && x.PeriodEnd == periodEnd)
      .Select(x => x.Id).ToListAsync(ct);
    foreach (var packageId in packageIds)
    {
      var packageReview = await FinancialPackageReviewService.RequireCurrentAsync(db, actor, packageId, requirePartner: true, ct);
      if (!packageReview.Succeeded)
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          "Every financial package for the period needs current management, accounting, and partner approval before close.");
    }
    period.Status = AccountingWorkflowStates.Closed;
    period.ClosedByUserId = actor.UserId;
    period.ClosedAt = DateTimeOffset.UtcNow;
    period.CloseReason = reason.Trim();
    period.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ReopenPeriodAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, string reason,
    CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods
      .FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE id = {periodId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, period.ClientId, ["Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return auth;
    if (period.Status != AccountingWorkflowStates.Closed || string.IsNullOrWhiteSpace(reason))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Only a closed period can be reopened with a recorded decision.");
    var previousRevision = period.Revision;
    var amendmentRevision = previousRevision + 1;
    period.Status = AccountingWorkflowStates.Draft;
    period.CloseReason = "REOPENED: " + reason.Trim();
    period.Revision = amendmentRevision;
    period.ClosedAt = null;
    period.ClosedByUserId = null;
    db.ClientPeriodAmendments.Add(new ClientPeriodAmendment
    {
      Id = Guid.CreateVersion7(), FirmId = period.FirmId, ClientId = period.ClientId, PeriodId = period.Id,
      PreviousRevision = previousRevision, AmendmentRevision = amendmentRevision,
      Reason = reason.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> AuthorizeClientAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles.ToArray(), InternalOnly: true), ct);

  private static async Task<CommandResult> AuthorizeFirmAsync(
    IClientAccountingDbContext db, ActorContext actor, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles.ToArray(), InternalOnly: true), ct);

  private static async Task<CommandResult> AuthorizeGroupAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId, IReadOnlyList<string> roles, CancellationToken ct)
  {
    var allowed = await db.GroupAccessGrants.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.GroupId == groupId &&
      x.UserId == actor.UserId && x.RevokedAt == null && roles.Contains(x.Role), ct);
    return allowed ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Explicit group access is required.");
  }

  private sealed record GeneralLedgerImportContext(
    ClientReportingPeriod Period, IReadOnlyDictionary<string, ClientAccount> Accounts,
    IReadOnlyDictionary<string, IReadOnlySet<string>> DimensionCodes);

  private static async Task<CommandResult<GeneralLedgerImportContext>> ResolveGeneralLedgerImportContextAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid periodId, Guid? bookId,
    string currency, CancellationToken ct)
  {
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == periodId && x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    if (period is null)
      return CommandResult<GeneralLedgerImportContext>.Fail(ErrorCodes.ScopeDenied, "The reporting period is outside the client scope.");
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult<GeneralLedgerImportContext>.Fail(ErrorCodes.ProtectedState, "A closed period cannot receive a GL import.");
    if (!string.Equals(period.Currency, currency, StringComparison.Ordinal))
      return CommandResult<GeneralLedgerImportContext>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The GL currency does not match the selected reporting period.");
    if (bookId.HasValue && !await db.ClientReportingBooks.AnyAsync(x => x.Id == bookId && x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.PeriodId == periodId, ct))
      return CommandResult<GeneralLedgerImportContext>.Fail(ErrorCodes.ScopeDenied, "The reporting book is outside the client period.");
    var chart = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= period.EndDate &&
      (x.EffectiveTo == null || x.EffectiveTo >= period.StartDate)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (chart is null)
      return CommandResult<GeneralLedgerImportContext>.Fail(ErrorCodes.GateBlocked, "A published client chart is required before GL import.");
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.ChartVersionId == chart.Id).ToDictionaryAsync(x => x.AccountCode, StringComparer.OrdinalIgnoreCase, ct);
    var dimensionCodes = await LoadDimensionCodesAsync(db, actor.FirmId, clientId, ct);
    return CommandResult<GeneralLedgerImportContext>.Ok(new(period, accounts, dimensionCodes));
  }

  private static CommandResult<int> ValidateGeneralLedgerTransactions(
    IReadOnlyList<GeneralLedgerTransactionInput> transactions, ClientReportingPeriod period, string currency,
    IReadOnlyDictionary<string, ClientAccount> accounts,
    IReadOnlyDictionary<string, IReadOnlySet<string>> dimensionCodes,
    int maxTransactions, int maxLines)
  {
    var lineCount = transactions.Sum(x => (long)x.Lines.Count);
    if (transactions.Count == 0 || transactions.Count > maxTransactions || lineCount > maxLines)
      return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected, "The GL import exceeds its bounded batch limit.");
    var journals = transactions.Select(x => x.StableJournalId.Trim()).ToArray();
    var lineIds = transactions.SelectMany(x => x.Lines).Select(x => x.StableLineId.Trim()).ToArray();
    if (journals.Any(string.IsNullOrWhiteSpace) || journals.Distinct(StringComparer.OrdinalIgnoreCase).Count() != journals.Length ||
        lineIds.Any(string.IsNullOrWhiteSpace) || lineIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != lineIds.Length)
      return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected, "GL journal and line identities must be unique and non-empty.");
    foreach (var transaction in transactions)
    {
      if (transaction.PostingDate < period.StartDate || transaction.PostingDate > period.EndDate || transaction.Lines.Count == 0)
        return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected, "Every GL journal must be populated and inside the selected period.");
      if (transaction.Lines.Any(x => x.Debit < 0m || x.Credit < 0m || (x.Debit > 0m && x.Credit > 0m) ||
          !accounts.ContainsKey(x.AccountCode.Trim()) || x.OriginalCurrency.Trim().ToUpperInvariant() != currency))
        return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected, "GL lines must use published accounts, one-sided amounts and the selected currency.");
      if (MoneyPolicy.Normalize(transaction.Lines.Sum(x => x.Debit) - transaction.Lines.Sum(x => x.Credit)) != 0m)
        return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected, $"Journal {transaction.StableJournalId} is not balanced.");
    }
    if (ValidateDimensionValues(transactions, dimensionCodes) is { } dimensionError)
      return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected, dimensionError);
    return CommandResult<int>.Ok((int)lineCount);
  }

  private static async Task<IReadOnlyDictionary<string, IReadOnlySet<string>>> LoadDimensionCodesAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
  {
    var definitions = await db.ClientAccountingDimensionDefinitions.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.ClientId == clientId && x.Status == AccountingWorkflowStates.Active)
      .Select(x => new { x.DimensionType, x.Code }).ToListAsync(ct);
    return definitions.GroupBy(x => x.DimensionType, StringComparer.Ordinal)
      .ToDictionary(x => x.Key, x => (IReadOnlySet<string>)x.Select(v => v.Code).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
  }

  private static string? ValidateDimensionValues(
    IReadOnlyList<GeneralLedgerTransactionInput> transactions,
    IReadOnlyDictionary<string, IReadOnlySet<string>> dimensionCodes)
  {
    foreach (var line in transactions.SelectMany(x => x.Lines))
    {
      foreach (var (type, value) in new[]
      {
        (AccountingDimensionTypes.Branch, line.Branch),
        (AccountingDimensionTypes.CostCentre, line.CostCentre),
        (AccountingDimensionTypes.Department, line.Department),
        (AccountingDimensionTypes.Project, line.Project),
        (AccountingDimensionTypes.IntercompanyCounterparty, line.IntercompanyCounterparty)
      })
      {
        var code = value.Trim();
        if (code.Length > 0 && (!dimensionCodes.TryGetValue(type, out var allowed) || !allowed.Contains(code)))
          return $"GL dimension '{type}:{code}' is not defined for this client.";
      }
    }
    return null;
  }

  private static GeneralLedgerImportBatchSummary Summarize(SourceImportBatch batch) =>
    new(batch.Id, batch.Status, batch.AcceptedChunkCount, batch.AcceptedTransactionCount, batch.AcceptedLineCount,
      batch.ExpectedChunkCount, batch.ExpectedTransactionCount, batch.ExpectedLineCount,
      string.IsNullOrWhiteSpace(batch.NormalizedDatasetDigest) ? null : batch.NormalizedDatasetDigest);

  private static bool IsSha256(string value) => value.Trim().Length == 64 && value.Trim().All(c =>
    c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

  private sealed class StringTupleComparer : IEqualityComparer<(string SourceSystem, string AliasCode)>
  {
    public static StringTupleComparer Instance { get; } = new();
    public bool Equals((string SourceSystem, string AliasCode) x, (string SourceSystem, string AliasCode) y) =>
      StringComparer.OrdinalIgnoreCase.Equals(x.SourceSystem, y.SourceSystem) && StringComparer.OrdinalIgnoreCase.Equals(x.AliasCode, y.AliasCode);
    public int GetHashCode((string SourceSystem, string AliasCode) value) => HashCode.Combine(
      StringComparer.OrdinalIgnoreCase.GetHashCode(value.SourceSystem), StringComparer.OrdinalIgnoreCase.GetHashCode(value.AliasCode));
  }

  private static bool HasParentCycle(
    IReadOnlyList<ClientAccountInput> inputs, IReadOnlyDictionary<string, ClientAccountInput> map)
  {
    var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool Visit(string identity)
    {
      if (state.GetValueOrDefault(identity) == 1) return true;
      if (state.GetValueOrDefault(identity) == 2) return false;
      state[identity] = 1;
      if (map[identity].ParentStableIdentity is { } parent && map.ContainsKey(parent.Trim()) && Visit(parent.Trim())) return true;
      state[identity] = 2;
      return false;
    }
    return inputs.Any(x => Visit(x.StableIdentity.Trim()));
  }

  private static bool HasTaxonomyCycle(
    IReadOnlyList<TaxonomyNodeInput> inputs, IReadOnlyDictionary<string, TaxonomyNodeInput> map)
  {
    var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool Visit(string code)
    {
      if (state.GetValueOrDefault(code) == 1) return true;
      if (state.GetValueOrDefault(code) == 2) return false;
      state[code] = 1;
      if (map[code].ParentCode is { } parent && map.ContainsKey(parent.Trim()) && Visit(parent.Trim())) return true;
      state[code] = 2;
      return false;
    }
    return inputs.Any(x => Visit(x.Code.Trim()));
  }
}
