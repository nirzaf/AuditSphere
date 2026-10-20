using System.Data;
using System.Linq.Expressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record AccountingPeriodEvidenceCategory(string Kind, int TotalCount, int PageSize);
public sealed record AccountingPeriodEvidenceIndex(Guid PeriodId, Guid ClientId,
  IReadOnlyList<AccountingPeriodEvidenceCategory> Categories, IReadOnlyList<string> UnrepresentedCategories);
public sealed record AccountingPeriodEvidencePage<T>(Guid PeriodId, Guid ClientId, string Kind, int Page,
  int PageSize, int TotalCount, IReadOnlyList<T> Items);

public sealed record PeriodReferenceEvidence(Guid Id, string PeriodCode, string Status, string Relation,
  DateOnly StartDate, DateOnly EndDate);
public sealed record PeriodBookEvidence(Guid Id, string Code, string Basis, string InclusionRule,
  string Currency, string Status, string Revision);
/// <summary>LatestAcceptanceReceiptId is retained history for this exact source row, not a current-selection flag.</summary>
public sealed record PeriodTrialBalanceEvidence(Guid Id, Guid EngagementId, Guid? BookId, string? Basis,
  string SourceKind, string Revision, string LegalEntityKey, string Currency, string RawFileSha256Hex,
  string NormalizedDatasetDigest, string Sha256Hex, bool Balanced, string ValidationStatus, string ImportState,
  decimal ControlTotal, Guid? LatestAcceptanceReceiptId, DateTimeOffset ImportedAt, Guid ImportedByUserId);
/// <summary>LatestAcceptanceReceiptId is retained receipt history for this exact batch, not a current-selection flag.
/// AcceptedChunkCount, AcceptedTransactionCount and AcceptedLineCount are importer validation counts, not professional acceptance.</summary>
public sealed record PeriodGeneralLedgerEvidence(Guid Id, Guid EngagementId, Guid? BookId, string SourceKind,
  string ProfileVersion, string ParserVersion, string RawFileSha256Hex, string NormalizedDatasetDigest,
  string LegalEntityKey, string Currency, int RowCount, int ExpectedChunkCount, int ExpectedTransactionCount,
  int ExpectedLineCount, int AcceptedChunkCount, int AcceptedTransactionCount, int AcceptedLineCount,
  string Status, string ReceiptReference, Guid? LatestAcceptanceReceiptId, Guid CreatedByUserId,
  DateTimeOffset CreatedAt);
public sealed record PeriodCompletenessEvidence(Guid Id, Guid EngagementId, Guid? BookId,
  Guid TrialBalanceDatasetId, Guid ImportBatchId, Guid? OpeningTrialBalanceDatasetId, string TrialBalanceHash,
  string GeneralLedgerHash, string OpeningTrialBalanceHash, string AccountResidualDigest,
  string OpeningMovementResidualDigest, int TrialBalanceAccountCount, int GeneralLedgerAccountCount,
  int MatchedAccountCount, int MismatchedAccountCount, int OpeningMovementMismatchedAccountCount,
  int JournalExceptionCount, decimal OpeningAmount, decimal MovementAmount, decimal ClosingAmount,
  decimal OpeningMovementResidual, decimal AbsoluteResidual, DateOnly CoverageStart, DateOnly CoverageEnd,
  string Status, bool IncompleteExtract, string CompletenessDisclosure, string EvidenceReference,
  Guid CreatedByUserId, Guid? ReviewedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record PeriodReconciliationProofEvidence(Guid Id, string FormulaVersion, decimal SourceTotal,
  decimal GlTotal, decimal ItemsSignedTotal, decimal Residual, bool IsReconciled, int ItemCount,
  string ItemManifestDigest, string SourceHash, string InputGeneration, Guid CreatedByUserId,
  DateTimeOffset CreatedAt);
public sealed record PeriodReconciliationEvidence(Guid Id, Guid EngagementId, Guid? BookId, string Area,
  Guid? TrialBalanceDatasetId, Guid? ImportBatchId, string AccountSelection, DateOnly AsOfDate,
  string AgingBasis, string AgingBucketRuleVersion, decimal SourceTotal, decimal GlTotal, decimal Residual,
  string SourceHash, string Status, string Revision, Guid? SupersedesReconciliationId, string InputGeneration,
  Guid CreatedByUserId, Guid? ReviewedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt,
  PeriodReconciliationProofEvidence? LatestProof);
public sealed record PeriodSpecialistScheduleEvidence(Guid Id, Guid EngagementId, string Revision,
  Guid? SupersedesScheduleId, string Area, string MethodologyVersion, decimal ManagementAmount,
  decimal CalculatedAmount, decimal Difference, string AssumptionsHash, string EvidenceReference,
  string ReviewConclusion, string Status, Guid CreatedByUserId, Guid? ReviewedByUserId,
  DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt);
public sealed record PeriodPbcEvidence(Guid Id, Guid EngagementId, string Objective, string PeriodStart,
  string PeriodEnd, string Area, Guid ClientOwnerUserId, Guid FirmOwnerUserId, Guid ReviewerUserId,
  string DueDate, string State, string Revision, Guid? AcceptedByUserId, DateTimeOffset? AcceptedAt,
  Guid? LatestAcceptanceReceiptId, DateTimeOffset UpdatedAt);
public sealed record PeriodTaskEvidence(Guid Id, Guid? EngagementId, string Title, string Status,
  Guid? AssigneeUserId, DateOnly? DueDate, string Phase, Guid? MappingVersionId, string? FsliCode,
  DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt);

/// <summary>Client-wide, exact-period evidence index and independently paginated persisted summaries.</summary>
public static class AccountingPeriodEvidenceQuery
{
  public const int PageSize = 25;
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private static readonly IReadOnlyList<string> Unrepresented = Array.AsReadOnly(new[]
  {
    "bank-schedules", "receivables-schedules", "payables-schedules", "monthly-handover"
  });

  private sealed record PeriodScope(Guid FirmId, Guid Id, Guid ClientId, string PeriodCode, DateOnly StartDate,
    DateOnly EndDate, string Basis, string Currency, string Status, long Revision, Guid? PriorPeriodId);
  private sealed record ReconciliationProjection(Guid Id, Guid EngagementId, Guid? BookId, string Area,
    Guid? TrialBalanceDatasetId, Guid? ImportBatchId, string AccountSelection, DateOnly AsOfDate,
    string AgingBasis, string AgingBucketRuleVersion, decimal SourceTotal, decimal GlTotal, decimal Residual,
    string SourceHash, string Status, long Revision, Guid? SupersedesReconciliationId, long InputGeneration,
    Guid CreatedByUserId, Guid? ReviewedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt,
    Guid? LatestProofId);

  public static async Task<CommandResult<AccountingPeriodEvidenceIndex>> IndexAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid periodId, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null) return SnapshotRequired<AccountingPeriodEvidenceIndex>();
    var scoped = await ScopeAsync(db, actor, periodId, ct);
    if (!scoped.Succeeded || scoped.Value is null) return Denied<AccountingPeriodEvidenceIndex>();
    var scope = scoped.Value;
    return await ReadSnapshotAsync(db, actor, scope, async snapshotScope =>
    {
      var categories = new[]
      {
        new AccountingPeriodEvidenceCategory("period-references", await PeriodReferences(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("books", await Books(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("trial-balances", await TrialBalances(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("general-ledgers", await GeneralLedgers(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("completeness", await Completeness(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("reconciliations", await Reconciliations(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("specialist-schedules", await SpecialistSchedules(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("pbc", await Pbc(db, snapshotScope).CountAsync(ct), PageSize),
        new AccountingPeriodEvidenceCategory("tasks", await Tasks(db, snapshotScope).CountAsync(ct), PageSize)
      };
      return CommandResult<AccountingPeriodEvidenceIndex>.Ok(
        new(snapshotScope.Id, snapshotScope.ClientId, categories, Unrepresented));
    }, ct);
  }

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodReferenceEvidence>>> PeriodReferencesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "period-references", page, s => PeriodReferences(db, s),
      q => q.OrderBy(x => x.StartDate).ThenBy(x => x.Id),
      x => new PeriodReferenceEvidence(x.Id, x.PeriodCode, x.Status,
        x.PriorPeriodId == periodId ? "SUBSEQUENT" : "PRIOR", x.StartDate, x.EndDate), ct);

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodBookEvidence>>> BooksAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "books", page, s => Books(db, s),
      q => q.OrderBy(x => x.Code).ThenBy(x => x.Id),
      x => new PeriodBookEvidence(x.Id, x.Code, x.Basis, x.InclusionRule, x.Currency, x.Status, Wire(x.Revision)), ct);

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodTrialBalanceEvidence>>> TrialBalancesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "trial-balances", page, s => TrialBalances(db, s),
      q => q.OrderBy(x => x.Id), x => new PeriodTrialBalanceEvidence(x.Id, x.EngagementId, x.BookId, x.Basis,
        x.SourceKind, Wire(x.Revision), x.LegalEntityKey, x.Currency, x.RawFileSha256Hex, x.NormalizedDatasetDigest,
        x.Sha256Hex, x.Balanced, x.ValidationStatus, x.ImportState, x.ControlTotal,
        db.SourceAcceptanceDecisions.AsNoTracking().Where(d => d.FirmId == actor.FirmId && d.ClientId == x.ClientId &&
          d.EngagementId == x.EngagementId && d.SourceKind == AccountingSourceKinds.TrialBalance &&
          d.TrialBalanceDatasetId == x.Id && d.Decision == "ACCEPTED").OrderByDescending(d => d.CreatedAt)
          .ThenByDescending(d => d.Id).Select(d => (Guid?)d.Id).FirstOrDefault(), x.ImportedAt, x.ImportedByUserId), ct);

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodGeneralLedgerEvidence>>> GeneralLedgersAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "general-ledgers", page, s => GeneralLedgers(db, s),
      q => q.OrderBy(x => x.Id), x => new PeriodGeneralLedgerEvidence(x.Id, x.EngagementId, x.BookId,
        x.SourceKind, x.ProfileVersion, x.ParserVersion, x.RawFileSha256Hex, x.NormalizedDatasetDigest,
        x.LegalEntityKey, x.Currency, x.RowCount, x.ExpectedChunkCount, x.ExpectedTransactionCount,
        x.ExpectedLineCount, x.AcceptedChunkCount, x.AcceptedTransactionCount, x.AcceptedLineCount,
        x.Status, x.ReceiptReference,
        db.SourceAcceptanceDecisions.AsNoTracking().Where(d => d.FirmId == actor.FirmId && d.ClientId == x.ClientId &&
          d.EngagementId == x.EngagementId && d.SourceKind == AccountingSourceKinds.GeneralLedger &&
          d.ImportBatchId == x.Id && d.Decision == "ACCEPTED").OrderByDescending(d => d.CreatedAt)
          .ThenByDescending(d => d.Id).Select(d => (Guid?)d.Id).FirstOrDefault(), x.CreatedByUserId, x.CreatedAt), ct);

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodCompletenessEvidence>>> CompletenessAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "completeness", page, s => Completeness(db, s),
      q => q.OrderBy(x => x.Id), x => new PeriodCompletenessEvidence(x.Id, x.EngagementId, x.BookId,
        x.TrialBalanceDatasetId, x.ImportBatchId, x.OpeningTrialBalanceDatasetId, x.TrialBalanceHash,
        x.GeneralLedgerHash, x.OpeningTrialBalanceHash, x.AccountResidualDigest, x.OpeningMovementResidualDigest,
        x.TrialBalanceAccountCount, x.GeneralLedgerAccountCount, x.MatchedAccountCount, x.MismatchedAccountCount,
        x.OpeningMovementMismatchedAccountCount, x.JournalExceptionCount, x.OpeningAmount, x.MovementAmount,
        x.ClosingAmount, x.OpeningMovementResidual, x.AbsoluteResidual, x.CoverageStart, x.CoverageEnd,
        x.Status, x.IncompleteExtract, x.CompletenessDisclosure, x.EvidenceReference, x.CreatedByUserId,
        x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt), ct);

  public static async Task<CommandResult<AccountingPeriodEvidencePage<PeriodReconciliationEvidence>>> ReconciliationsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null) return SnapshotRequired<AccountingPeriodEvidencePage<PeriodReconciliationEvidence>>();
    var invalidPage = ValidatePage<PeriodReconciliationEvidence>(page);
    if (invalidPage is not null) return invalidPage;
    var scoped = await ScopeAsync(db, actor, periodId, ct);
    if (!scoped.Succeeded || scoped.Value is null) return Denied<AccountingPeriodEvidencePage<PeriodReconciliationEvidence>>();
    var scope = scoped.Value;
    return await ReadSnapshotAsync(db, actor, scope, async snapshotScope =>
    {
      var projected = await PageAsync(Reconciliations(db, snapshotScope), q => q.OrderBy(x => x.Id),
        x => new ReconciliationProjection(x.Id, x.EngagementId, x.BookId, x.Area, x.TrialBalanceDatasetId,
          x.ImportBatchId, x.AccountSelection, x.AsOfDate, x.AgingBasis, x.AgingBucketRuleVersion,
          x.SourceTotal, x.GlTotal, x.Residual, x.SourceHash, x.Status, x.Revision, x.SupersedesReconciliationId,
          x.InputGeneration, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt,
          db.AccountingReconciliationProofs.AsNoTracking().Where(p => p.FirmId == actor.FirmId && p.ClientId == x.ClientId &&
            p.EngagementId == x.EngagementId && p.ReconciliationId == x.Id).OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id).Select(p => (Guid?)p.Id).FirstOrDefault()),
        snapshotScope, "reconciliations", page, ct);
      if (!projected.Succeeded || projected.Value is null)
        return CommandResult<AccountingPeriodEvidencePage<PeriodReconciliationEvidence>>.Fail(projected.ErrorCode!, projected.Message!);

      var proofIds = projected.Value.Items.Where(x => x.LatestProofId.HasValue).Select(x => x.LatestProofId!.Value).Distinct().ToArray();
      PeriodReconciliationProofEvidence[] proofs = proofIds.Length == 0 ? [] : await db.AccountingReconciliationProofs.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == snapshotScope.ClientId && proofIds.Contains(x.Id))
        .Select(x => new PeriodReconciliationProofEvidence(x.Id, x.FormulaVersion, x.SourceTotal, x.GlTotal,
          x.ItemsSignedTotal, x.Residual, x.IsReconciled, x.ItemCount, x.ItemManifestDigest, x.SourceHash,
          Wire(x.InputGeneration), x.CreatedByUserId, x.CreatedAt)).ToArrayAsync(ct);
      var proofsById = proofs.ToDictionary(x => x.Id);
      var value = projected.Value;
      return CommandResult<AccountingPeriodEvidencePage<PeriodReconciliationEvidence>>.Ok(
        new(value.PeriodId, value.ClientId, value.Kind, value.Page, value.PageSize, value.TotalCount,
          value.Items.Select(x => new PeriodReconciliationEvidence(
            x.Id, x.EngagementId, x.BookId, x.Area, x.TrialBalanceDatasetId, x.ImportBatchId, x.AccountSelection,
            x.AsOfDate, x.AgingBasis, x.AgingBucketRuleVersion, x.SourceTotal, x.GlTotal, x.Residual, x.SourceHash,
            x.Status, Wire(x.Revision), x.SupersedesReconciliationId, Wire(x.InputGeneration), x.CreatedByUserId,
            x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt,
            x.LatestProofId is { } proofId ? proofsById.GetValueOrDefault(proofId) : null)).ToArray()));
    }, ct);
  }

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodSpecialistScheduleEvidence>>> SpecialistSchedulesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "specialist-schedules", page, s => SpecialistSchedules(db, s),
      q => q.OrderBy(x => x.Id), x => new PeriodSpecialistScheduleEvidence(x.Id, x.EngagementId, Wire(x.Revision),
        x.SupersedesScheduleId, x.Area, x.MethodologyVersion, x.ManagementAmount, x.CalculatedAmount, x.Difference,
        x.AssumptionsHash, x.EvidenceReference, x.ReviewConclusion, x.Status, x.CreatedByUserId,
        x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt), ct);

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodPbcEvidence>>> PbcAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "pbc", page, s => Pbc(db, s),
      q => q.OrderBy(x => x.Id), x => new PeriodPbcEvidence(x.Id, x.EngagementId, x.Objective,
        x.PeriodStart, x.PeriodEnd, x.Area, x.ClientOwnerUserId, x.FirmOwnerUserId, x.ReviewerUserId,
        x.DueDate, x.State, Wire(x.Revision), x.AcceptedByUserId, x.AcceptedAt,
        db.PbcAcceptanceReceipts.AsNoTracking().Where(r => r.FirmId == actor.FirmId && r.ClientId == x.ClientId &&
          r.EngagementId == x.EngagementId && r.PbcRequestId == x.Id).OrderByDescending(r => r.AcceptedAt)
          .ThenByDescending(r => r.Id).Select(r => (Guid?)r.Id).FirstOrDefault(), x.UpdatedAt), ct);

  public static Task<CommandResult<AccountingPeriodEvidencePage<PeriodTaskEvidence>>> TasksAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, int page = 1, CancellationToken ct = default) =>
    PageForAsync(db, actor, periodId, "tasks", page, s => Tasks(db, s),
      q => q.OrderBy(x => x.Id), x => new PeriodTaskEvidence(x.Id, x.EngagementId, x.Title,
        x.Status, x.AssigneeUserId, x.DueDate, x.Phase, x.MappingVersionId, x.FsliCode, x.CreatedAt, x.CompletedAt), ct);

  private static async Task<CommandResult<AccountingPeriodEvidencePage<T>>> PageForAsync<TEntity, T>(
    IClientAccountingDbContext db, ActorContext actor, Guid periodId, string kind, int page,
    Func<PeriodScope, IQueryable<TEntity>> source, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order,
    Expression<Func<TEntity, T>> projection, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is not null) return SnapshotRequired<AccountingPeriodEvidencePage<T>>();
    var scoped = await ScopeAsync(db, actor, periodId, ct);
    if (!scoped.Succeeded || scoped.Value is null) return Denied<AccountingPeriodEvidencePage<T>>();
    var scope = scoped.Value;
    var invalidPage = ValidatePage<T>(page);
    if (invalidPage is not null) return invalidPage;
    return await ReadSnapshotAsync(db, actor, scope, snapshotScope => PageAsync(
      source(snapshotScope), order, projection, snapshotScope, kind, page, ct), ct);
  }

  private static async Task<CommandResult<AccountingPeriodEvidencePage<T>>> PageAsync<TEntity, T>(
    IQueryable<TEntity> source, Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> order,
    Expression<Func<TEntity, T>> projection, PeriodScope scope, string kind, int page, CancellationToken ct)
  {
    var offset = ((long)page - 1) * PageSize;
    var totalCount = await source.CountAsync(ct);
    var items = await order(source).Skip((int)offset).Take(PageSize).Select(projection).ToArrayAsync(ct);
    return CommandResult<AccountingPeriodEvidencePage<T>>.Ok(new(scope.Id, scope.ClientId, kind, page,
      PageSize, totalCount, items));
  }

  private static async Task<CommandResult<PeriodScope>> ScopeAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid periodId, CancellationToken ct)
  {
    var row = await ReadPeriodScopeAsync(db, actor.FirmId, periodId, ct);
    if (row is null || !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == row.ClientId, ct))
      return Denied<PeriodScope>();
    var auth = await AuthorizeAsync(db, actor, row.ClientId, ct);
    return auth.Succeeded ? CommandResult<PeriodScope>.Ok(row) : Denied<PeriodScope>();
  }

  private static async Task<PeriodScope?> ReadPeriodScopeAsync(IClientAccountingDbContext db, Guid firmId,
    Guid periodId, CancellationToken ct)
  {
    var row = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == firmId && x.Id == periodId)
      .Select(x => new { x.FirmId, x.Id, x.ClientId, x.PeriodCode, x.StartDate, x.EndDate, x.Basis, x.Currency,
        x.Status, x.Revision, x.PriorPeriodId }).SingleOrDefaultAsync(ct);
    return row is null ? null : new PeriodScope(row.FirmId, row.Id, row.ClientId, row.PeriodCode,
      row.StartDate, row.EndDate, row.Basis, row.Currency, row.Status, row.Revision, row.PriorPeriodId);
  }

  private static async Task<CommandResult<T>> ReadSnapshotAsync<T>(IClientAccountingDbContext db, ActorContext actor,
    PeriodScope scope, Func<PeriodScope, Task<CommandResult<T>>> read, CancellationToken ct)
  {
    if (db.Database.CurrentTransaction is not null) return SnapshotRequired<T>();
    CommandResult<T> result;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var snapshotScope = await ReadPeriodScopeAsync(db, actor.FirmId, scope.Id, ct);
      if (snapshotScope is null || snapshotScope != scope) return Stale<T>();
      if (!await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == scope.ClientId, ct))
        return Stale<T>();
      result = await read(snapshotScope);
      if (!result.Succeeded || result.Value is null) return result;
      await snapshot.CommitAsync(ct);
    }
    // Dispose/commit the repeatable-read transaction before checking current authorization.
    return await CompleteAsync(db, actor, scope, result.Value!, ct);
  }

  private static async Task<CommandResult<T>> CompleteAsync<T>(IClientAccountingDbContext db, ActorContext actor,
    PeriodScope scope, T value, CancellationToken ct)
  {
    var row = await ReadPeriodScopeAsync(db, actor.FirmId, scope.Id, ct);
    if (row is null || row != scope ||
        !await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == scope.ClientId, ct))
      return Stale<T>();
    var auth = await AuthorizeAsync(db, actor, scope.ClientId, ct);
    return auth.Succeeded ? CommandResult<T>.Ok(value) : Denied<T>();
  }

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, CancellationToken ct) => AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, ClientId: clientId, RequiredRoles: Roles, InternalOnly: true), ct);

  private static CommandResult<AccountingPeriodEvidencePage<T>>? ValidatePage<T>(int page)
  {
    var offset = ((long)page - 1) * PageSize;
    return page < 1 || offset > int.MaxValue
      ? CommandResult<AccountingPeriodEvidencePage<T>>.Fail("accounting.page-invalid", "Choose a valid 1-based evidence page.")
      : null;
  }

  private static CommandResult<T> SnapshotRequired<T>() =>
    CommandResult<T>.Fail(ErrorCodes.GateBlocked, "Period evidence must be read outside an ambient transaction.");

  private static CommandResult<T> Stale<T>() =>
    CommandResult<T>.Fail(ErrorCodes.GenerationStale, "The reporting period changed during evidence inspection. Refresh the page.");

  private static IQueryable<ClientReportingPeriod> PeriodReferences(IClientAccountingDbContext db, PeriodScope s)
  {
    return db.ClientReportingPeriods.AsNoTracking()
      .Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId &&
        (x.Id == s.PriorPeriodId || x.PriorPeriodId == s.Id));
  }

  private static IQueryable<ClientReportingBook> Books(IClientAccountingDbContext db, PeriodScope s) =>
    db.ClientReportingBooks.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.PeriodId == s.Id);
  private static IQueryable<TrialBalanceDataset> TrialBalances(IClientAccountingDbContext db, PeriodScope s) =>
    db.TrialBalanceDatasets.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.PeriodId == s.Id);
  private static IQueryable<SourceImportBatch> GeneralLedgers(IClientAccountingDbContext db, PeriodScope s) =>
    db.SourceImportBatches.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.PeriodId == s.Id &&
      x.SourceKind == AccountingSourceKinds.GeneralLedger);
  private static IQueryable<GeneralLedgerCompletenessBridge> Completeness(IClientAccountingDbContext db, PeriodScope s) =>
    db.GeneralLedgerCompletenessBridges.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.PeriodId == s.Id);
  private static IQueryable<AccountingReconciliation> Reconciliations(IClientAccountingDbContext db, PeriodScope s) =>
    db.AccountingReconciliations.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.PeriodId == s.Id);
  private static IQueryable<SpecialistAccountingSchedule> SpecialistSchedules(IClientAccountingDbContext db, PeriodScope s) =>
    db.SpecialistAccountingSchedules.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.PeriodId == s.Id);
  private static IQueryable<PbcRequest> Pbc(IClientAccountingDbContext db, PeriodScope s)
  {
    var start = s.StartDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    var end = s.EndDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    return db.PbcRequests.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId &&
      x.PeriodStart == start && x.PeriodEnd == end);
  }
  private static IQueryable<WorkTask> Tasks(IClientAccountingDbContext db, PeriodScope s) =>
    db.WorkTasks.AsNoTracking().Where(x => x.FirmId == s.FirmId && x.ClientId == s.ClientId && x.ReportingPeriodId == s.Id);

  private static CommandResult<T> Denied<T>() =>
    CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "This period evidence is not visible under the current client accounting grant.");

  private static string Wire(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
