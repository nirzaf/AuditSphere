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

public static partial class ClientAccountingService
{
  private const int MaxGlTransactions = 100_000;

  private const int MaxGlLines = 500_000;

  private const int MaxGlChunkTransactions = 10_000;

  private const int MaxGlChunkLines = 50_000;

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
    var validation = ValidateGeneralLedgerTransactions(request.Transactions, period, currency,
      accounts, dimensionCodes, MaxGlTransactions, MaxGlLines);
    if (!validation.Succeeded)
      return CommandResult<Guid>.Fail(validation.ErrorCode!, validation.Message!);
    var normalizedHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(
      "gl-import.single.v2\n" + ComputeGeneralLedgerChunkDigest(currency, request.Transactions)));
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
        PostingDate = input.PostingDate, DocumentDate = input.DocumentDate, ServiceDate = input.ServiceDate,
        SourceUser = input.SourceUser.Trim(),
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
          OriginalCurrency = line.OriginalCurrency.Trim().ToUpperInvariant(), OriginalAmount = MoneyPolicy.Normalize(line.OriginalAmount),
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
          PostingDate = input.PostingDate, DocumentDate = input.DocumentDate, ServiceDate = input.ServiceDate,
          SourceUser = input.SourceUser.Trim(),
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
            Debit = MoneyPolicy.Normalize(line.Debit), Credit = MoneyPolicy.Normalize(line.Credit),
            OriginalCurrency = line.OriginalCurrency.Trim().ToUpperInvariant(),
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
        "gl-import.stream.v2\n" + string.Join('\n', finalDigests)));
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
      Transactions = transactions.OrderBy(x => x.StableJournalId.Trim(), StringComparer.Ordinal).Select(x => new
      {
        StableJournalId = x.StableJournalId.Trim(), DocumentNumber = x.DocumentNumber.Trim(),
        x.PostingDate, x.DocumentDate, x.ServiceDate, SourceUser = x.SourceUser.Trim(), SourceSystem = x.SourceSystem.Trim(),
        ReversalReference = x.ReversalReference?.Trim(), x.IsManual, x.IsYearEnd,
        Lines = x.Lines.OrderBy(y => y.StableLineId.Trim(), StringComparer.Ordinal).Select(y => new
        {
          StableLineId = y.StableLineId.Trim(), AccountCode = y.AccountCode.Trim(),
          Debit = MoneyPolicy.Normalize(y.Debit), Credit = MoneyPolicy.Normalize(y.Credit),
          OriginalCurrency = y.OriginalCurrency.Trim().ToUpperInvariant(),
          OriginalAmount = MoneyPolicy.Normalize(y.OriginalAmount), FunctionalAmount = MoneyPolicy.Normalize(y.FunctionalAmount),
          PartyIdentifier = y.PartyIdentifier.Trim(), Branch = y.Branch.Trim(), CostCentre = y.CostCentre.Trim(),
          Department = y.Department.Trim(), Project = y.Project.Trim(), IntercompanyCounterparty = y.IntercompanyCounterparty.Trim()
        }).ToArray()
      }).ToArray()
    };
    return Hashing.Sha256Hex(Encoding.UTF8.GetBytes("gl-import.chunk.v2\n" + JsonSerializer.Serialize(canonical)));
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
          !accounts.ContainsKey(x.AccountCode.Trim()) || !IsCurrencyCode(x.OriginalCurrency) ||
          MoneyPolicy.Normalize(x.OriginalAmount) != x.OriginalAmount ||
          MoneyPolicy.Normalize(x.FunctionalAmount) != x.FunctionalAmount ||
          x.FunctionalAmount != MoneyPolicy.Normalize(x.Debit - x.Credit) ||
          (string.Equals(x.OriginalCurrency.Trim(), currency, StringComparison.OrdinalIgnoreCase) && x.OriginalAmount != x.FunctionalAmount) ||
          (x.OriginalAmount != 0m && Math.Sign(x.OriginalAmount) != Math.Sign(x.FunctionalAmount))))
        return CommandResult<int>.Fail(ErrorCodes.Accounting.ImportRejected,
          "GL lines need a published account, valid original currency, normalized amounts, and signed functional value equal to the debit/credit posting.");
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

  private static bool IsCurrencyCode(string value)
  {
    var currency = value.Trim();
    return currency.Length == 3 && currency.All(char.IsAsciiLetter);
  }

  private sealed class StringTupleComparer : IEqualityComparer<(string SourceSystem, string AliasCode)>
  {
    public static StringTupleComparer Instance { get; } = new();
    public bool Equals((string SourceSystem, string AliasCode) x, (string SourceSystem, string AliasCode) y) =>
      StringComparer.OrdinalIgnoreCase.Equals(x.SourceSystem, y.SourceSystem) && StringComparer.OrdinalIgnoreCase.Equals(x.AliasCode, y.AliasCode);
    public int GetHashCode((string SourceSystem, string AliasCode) value) => HashCode.Combine(
      StringComparer.OrdinalIgnoreCase.GetHashCode(value.SourceSystem), StringComparer.OrdinalIgnoreCase.GetHashCode(value.AliasCode));
  }
}
