// Guarded TB import command (§§8.3, 16, 28.3): the server parses, checks scope and
// assignment, stamps the source hash, and promotes the dataset in one transaction.
// A repeated upload of identical bytes returns a duplicate conflict — it never
// appends a second silent copy. Validation itself stays with the outbox worker.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record TrialBalanceImportContext(Guid PeriodId, Guid? BookId, string Basis);

public static class TrialBalanceImportService
{
  public static Task<CommandResult<Guid>> ImportAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    string csvText,
    TrialBalanceImportContext sourceContext,
    CancellationToken ct = default) =>
    ImportAsync(db, actor, clientId, engagementId, csvText, sourceContext, authorizedEntity: null, ct);

  public static async Task<CommandResult<Guid>> ImportAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    string csvText,
    TrialBalanceImportContext sourceContext,
    string? authorizedEntity,
    CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || engagementId == Guid.Empty)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    ParsedCsvFile parsed;
    try
    {
      parsed = TrialBalanceCsvImporter.Parse(csvText);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, ex.Message);
    }

    return await ImportParsedAsync(db, actor, clientId, engagementId, parsed, sourceContext, authorizedEntity, ct);
  }

  public static async Task<CommandResult<Guid>> ImportXlsxAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    byte[] xlsxBytes,
    TrialBalanceImportContext sourceContext,
    string? authorizedEntity = null,
    CancellationToken ct = default)
  {
    ParsedCsvFile parsed;
    try
    {
      parsed = TrialBalanceXlsxImporter.Parse(xlsxBytes);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, ex.Message);
    }

    return await ImportParsedAsync(db, actor, clientId, engagementId, parsed, sourceContext, authorizedEntity, ct);
  }

  public static async Task<CommandResult<Guid>> ImportWithProfileAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    string csvText,
    TrialBalanceImportProfile profile,
    TrialBalanceImportContext sourceContext,
    string? authorizedEntity = null,
    CancellationToken ct = default)
  {
    ParsedCsvFile parsed;
    try
    {
      parsed = TrialBalanceCsvImporter.Parse(csvText, profile);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, ex.Message);
    }
    return await ImportParsedAsync(db, actor, clientId, engagementId, parsed, sourceContext, authorizedEntity, ct);
  }

  public static async Task<CommandResult<Guid>> ImportXlsxWithProfileAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    byte[] xlsxBytes,
    TrialBalanceImportProfile profile,
    TrialBalanceImportContext sourceContext,
    string? authorizedEntity = null,
    CancellationToken ct = default)
  {
    ParsedCsvFile parsed;
    try
    {
      parsed = TrialBalanceXlsxImporter.Parse(xlsxBytes, profile);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected, ex.Message);
    }
    return await ImportParsedAsync(db, actor, clientId, engagementId, parsed, sourceContext, authorizedEntity, ct);
  }

  public static async Task<CommandResult<IReadOnlyList<Guid>>> ImportBatchAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    string csvText,
    TrialBalanceImportProfile profile,
    TrialBalanceImportContext sourceContext,
    CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || engagementId == Guid.Empty)
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    ParsedCsvFile parsed;
    try
    {
      parsed = TrialBalanceCsvImporter.Parse(csvText, profile);
    }
    catch (InvalidOperationException ex)
    {
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Accounting.ImportRejected, ex.Message);
    }

    var entities = parsed.Rows.Select(x => x.Entity).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (entities.Length < 2)
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Accounting.ImportRejected,
        "A controlled trial-balance batch must contain at least two legal entities.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId, ct);
    if (engagement is null || engagement.PracticeClientId != clientId)
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(engagement.FirmId, clientId, engagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<Guid>>.Fail(auth.ErrorCode!, auth.Message!);
    var source = await ResolveSourceContextAsync(db, actor, clientId, sourceContext, parsed.Currency, ct);
    if (!source.Succeeded)
      return CommandResult<IReadOnlyList<Guid>>.Fail(source.ErrorCode!, source.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var lockedEngagement = await db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {engagement.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (lockedEngagement is null || lockedEngagement.PracticeClientId != clientId)
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await db.TrialBalanceImportBatches.AnyAsync(x => x.FirmId == engagement.FirmId &&
        x.EngagementId == engagementId && x.RawFileSha256Hex == parsed.RawFileSha256Hex, ct) ||
        await db.TrialBalanceDatasets.AnyAsync(x => x.FirmId == engagement.FirmId &&
          x.EngagementId == engagementId && x.RawFileSha256Hex == parsed.RawFileSha256Hex, ct))
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Accounting.ImportDuplicate,
        "Identical source bytes were already imported for this engagement; reuse that batch or dataset.");

    var batch = new TrialBalanceImportBatch
    {
      Id = Guid.CreateVersion7(), FirmId = engagement.FirmId, ClientId = clientId, EngagementId = engagementId,
      PeriodId = source.Value!.PeriodId, BookId = source.Value.BookId, Basis = source.Value.Basis,
      RawFileSha256Hex = parsed.RawFileSha256Hex, NormalizedDatasetDigest = parsed.NormalizedDatasetDigest,
      ImportProfileVersion = parsed.ImportProfileVersion, SourceLayout = parsed.SourceLayout,
      EntityCount = entities.Length, Status = TrialBalanceImportStates.Loading,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.TrialBalanceImportBatches.Add(batch);
    var nextRevision = await db.TrialBalanceDatasets
      .Where(x => x.FirmId == engagement.FirmId && x.EngagementId == engagementId)
      .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    var datasetIds = new List<Guid>(entities.Length);
    foreach (var entity in entities)
    {
      var datasetId = Guid.CreateVersion7();
      datasetIds.Add(datasetId);
      var dataset = new TrialBalanceDataset
      {
        Id = datasetId, FirmId = engagement.FirmId, ClientId = clientId, EngagementId = engagementId,
        PeriodId = source.Value.PeriodId, BookId = source.Value.BookId, Basis = source.Value.Basis,
        ImportBatchId = batch.Id, SourceKind = "Raw", Revision = ++nextRevision, LegalEntityKey = entity,
        Currency = parsed.Currency, RawFileSha256Hex = parsed.RawFileSha256Hex,
        NormalizedDatasetDigest = parsed.NormalizedDatasetDigest, Sha256Hex = parsed.NormalizedDatasetDigest,
        ImportProfileVersion = parsed.ImportProfileVersion, SourceLayout = parsed.SourceLayout,
        ImportState = TrialBalanceImportStates.Loading, ValidationStatus = "Pending",
        ImportedAt = batch.CreatedAt, ImportedByUserId = actor.UserId
      };
      db.TrialBalanceDatasets.Add(dataset);
      foreach (var row in parsed.Rows.Where(x => string.Equals(x.Entity, entity, StringComparison.Ordinal)))
        db.TrialBalanceRows.Add(new TrialBalanceRow
        {
          Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = row.AccountCode,
          AccountName = row.AccountName, Amount = row.Amount, SourceDebit = row.SourceDebit,
          SourceCredit = row.SourceCredit, Currency = row.Currency, Entity = row.Entity, MappingCode = row.MappingCode
        });
    }
    try
    {
      await db.SaveChangesAsync(ct);
      await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE trial_balance_datasets SET import_state = 'SEALED' WHERE import_batch_id = {batch.Id}", ct);
      batch.Status = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
      return CommandResult<IReadOnlyList<Guid>>.Ok(datasetIds);
    }
    catch (DbUpdateException)
    {
      return CommandResult<IReadOnlyList<Guid>>.Fail(ErrorCodes.Accounting.ImportDuplicate,
        "The batch identity changed; reload the current import preview.");
    }
  }

  private static async Task<CommandResult<Guid>> ImportParsedAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    Guid engagementId,
    ParsedCsvFile parsed,
    TrialBalanceImportContext sourceContext,
    string? authorizedEntity,
    CancellationToken ct)
  {

    var entities = parsed.Rows.Select(x => x.Entity).Distinct(StringComparer.Ordinal).ToArray();
    if (entities.Length != 1)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected,
        "A trial-balance dataset must contain exactly one legal entity; submit a controlled per-entity batch instead.");
    var entityKey = entities[0];
    if (!string.IsNullOrWhiteSpace(authorizedEntity) &&
        !string.Equals(entityKey, authorizedEntity.Trim(), StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The uploaded legal entity does not match the authorized accounting context.");

    // Resolve the engagement first so the firm scope comes from the stored record.
    var engagement = await db.Engagements.AsNoTracking()
      .SingleOrDefaultAsync(e => e.Id == engagementId, ct);
    if (engagement is null || engagement.PracticeClientId != clientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(engagement.FirmId, clientId, engagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var source = await ResolveSourceContextAsync(db, actor, clientId, sourceContext, parsed.Currency, ct);
    if (!source.Succeeded)
      return CommandResult<Guid>.Fail(source.ErrorCode!, source.Message!);

    // Serialize imports for one engagement while retaining independence across
    // clients/engagements. The parent is first persisted as LOADING, rows are
    // inserted, and only then is the parent promoted to SEALED in this transaction.
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var lockedEngagement = await db.Engagements.FromSqlInterpolated($"""
      SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {engagement.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (lockedEngagement is null || lockedEngagement.PracticeClientId != clientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var duplicate = await db.TrialBalanceDatasets.AsNoTracking().AnyAsync(d =>
      d.FirmId == lockedEngagement.FirmId && d.EngagementId == engagementId &&
      d.RawFileSha256Hex == parsed.RawFileSha256Hex, ct);
    if (duplicate)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate,
        "Identical source bytes were already imported for this engagement; reuse that dataset.");

    var revision = await db.TrialBalanceDatasets
      .Where(d => d.FirmId == lockedEngagement.FirmId && d.EngagementId == engagementId)
      .Select(d => (long?)d.Revision).MaxAsync(ct) ?? 0;
    var dataset = new TrialBalanceDataset
    {
      Id = Guid.CreateVersion7(),
      FirmId = lockedEngagement.FirmId,
      ClientId = clientId,
      EngagementId = engagementId,
      PeriodId = source.Value!.PeriodId,
      BookId = source.Value.BookId,
      Basis = source.Value.Basis,
      SourceKind = "Raw",
      Revision = revision + 1,
      LegalEntityKey = entityKey,
      Currency = parsed.Currency,
      RawFileSha256Hex = parsed.RawFileSha256Hex,
      NormalizedDatasetDigest = parsed.NormalizedDatasetDigest,
      Sha256Hex = parsed.NormalizedDatasetDigest,
      ImportProfileVersion = parsed.ImportProfileVersion,
      SourceLayout = parsed.SourceLayout,
      ImportState = TrialBalanceImportStates.Loading,
      ValidationStatus = "Pending",
      ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = actor.UserId
    };
    db.TrialBalanceDatasets.Add(dataset);
    try
    {
      await db.SaveChangesAsync(ct);
      foreach (var row in parsed.Rows)
      {
        db.TrialBalanceRows.Add(new TrialBalanceRow
        {
          Id = Guid.CreateVersion7(),
          DatasetId = dataset.Id,
          AccountCode = row.AccountCode,
          AccountName = row.AccountName,
          Amount = row.Amount,
          SourceDebit = row.SourceDebit,
          SourceCredit = row.SourceCredit,
          Currency = row.Currency,
          Entity = row.Entity,
          MappingCode = row.MappingCode
        });
      }
      await db.SaveChangesAsync(ct);
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
    catch (DbUpdateException ex) when (IsUniqueViolation(ex))
    {
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.ImportDuplicate,
        "Identical source bytes were already imported for this engagement; reuse that dataset.");
    }
    return CommandResult<Guid>.Ok(dataset.Id);
  }

  private static bool IsUniqueViolation(DbUpdateException exception) =>
    exception.GetBaseException() is { } baseException &&
    string.Equals(baseException.GetType().Name, "PostgresException", StringComparison.Ordinal) &&
    string.Equals(baseException.GetType().GetProperty("SqlState")?.GetValue(baseException)?.ToString(),
      "23505", StringComparison.Ordinal);

  private static async Task<CommandResult<ResolvedTrialBalanceContext>> ResolveSourceContextAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    TrialBalanceImportContext? sourceContext,
    string currency,
    CancellationToken ct)
  {
    var basis = sourceContext?.Basis.Trim().ToUpperInvariant() ?? string.Empty;
    if (sourceContext is null || sourceContext.PeriodId == Guid.Empty || basis.Length == 0)
      return CommandResult<ResolvedTrialBalanceContext>.Fail(ErrorCodes.Accounting.ImportRejected,
        "A trial-balance import must identify a reporting period and basis.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == sourceContext.PeriodId && x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    if (period is null)
      return CommandResult<ResolvedTrialBalanceContext>.Fail(ErrorCodes.ScopeDenied,
        "The reporting period is outside the client scope.");
    if (period.Status == AccountingWorkflowStates.Closed)
      return CommandResult<ResolvedTrialBalanceContext>.Fail(ErrorCodes.ProtectedState,
        "A closed reporting period cannot receive a trial-balance import.");
    if (!string.Equals(period.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(period.Basis, basis, StringComparison.OrdinalIgnoreCase))
      return CommandResult<ResolvedTrialBalanceContext>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The trial-balance currency and basis must match the selected reporting period.");
    if (sourceContext.BookId is { } bookId)
    {
      var book = await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == bookId && x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodId == period.Id, ct);
      if (book is null)
        return CommandResult<ResolvedTrialBalanceContext>.Fail(ErrorCodes.ScopeDenied,
          "The reporting book is outside the selected period.");
      if (!string.Equals(book.Currency, currency, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(book.Basis, basis, StringComparison.OrdinalIgnoreCase))
        return CommandResult<ResolvedTrialBalanceContext>.Fail(ErrorCodes.Accounting.ImportRejected,
          "The trial-balance currency and basis must match the selected reporting book.");
    }
    return CommandResult<ResolvedTrialBalanceContext>.Ok(new(period.Id, sourceContext.BookId, basis));
  }

  private sealed record ResolvedTrialBalanceContext(Guid PeriodId, Guid? BookId, string Basis);
}
