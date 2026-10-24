using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Native posted source projection. Technical sealing never supplies independent source acceptance.</summary>
public static partial class NativeGeneralLedgerSourceWorkspace
{
  public static async Task<CommandResult<NativeGeneralLedgerSourcePreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeGeneralLedgerSourceRequest request, CancellationToken ct = default)
  {
    var invalid = Validate(request);
    if (invalid is not null) return Fail<NativeGeneralLedgerSourcePreview>(ErrorCodes.Accounting.ImportRejected, invalid);
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
    var captured = await CaptureAsync(db, actor, request, ct);
    if (!captured.Succeeded) return Fail<NativeGeneralLedgerSourcePreview>(captured.ErrorCode!, captured.Message!);
    await tx.CommitAsync(ct);
    await tx.DisposeAsync();
    var current = await CaptureAsync(db, actor, request, ct);
    if (!current.Succeeded) return Fail<NativeGeneralLedgerSourcePreview>(current.ErrorCode!, current.Message!);
    if (captured.Value!.Preview.ReviewBasis != current.Value!.Preview.ReviewBasis)
      return Fail<NativeGeneralLedgerSourcePreview>(ErrorCodes.StaleRevision, "Native source inputs changed. Preview again.");
    return CommandResult<NativeGeneralLedgerSourcePreview>.Ok(current.Value.Preview);
  }

  public static async Task<CommandResult<NativeGeneralLedgerSourceView>> SealAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeGeneralLedgerSourceRequest request, CancellationToken ct = default)
  {
    var invalid = Validate(request);
    if (invalid is not null) return Fail<NativeGeneralLedgerSourceView>(ErrorCodes.Accounting.ImportRejected, invalid);
    var auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    if (!auth.Succeeded) return Fail<NativeGeneralLedgerSourceView>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var key = long.Parse(Hash(new { actor.FirmId, actor.UserId, actor.SessionEpoch, request.RequestId })[..16],
      NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={request.ClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={request.EngagementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var existing = await db.NativeGeneralLedgerSources.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.ActorEpoch == actor.SessionEpoch && x.RequestId == request.RequestId, ct);
    if (existing is not null)
    {
      if (existing.RequestHash != Purpose(actor, request) || !request.Reviewed ||
          request.ReviewBasis != Basis(existing.RequestHash, existing.SourceIdentityHash, existing.ManifestHash))
        return Fail<NativeGeneralLedgerSourceView>(ErrorCodes.IdempotencyConflict, "This request belongs to different reviewed native GL intent.");
      auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
      if (!auth.Succeeded) return Fail<NativeGeneralLedgerSourceView>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct);
      await tx.DisposeAsync();
      return await ViewAsync(db, actor, existing.ImportBatchId, ct);
    }
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TrialBalanceDatasetId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId, ct);
    if (dataset?.PeriodId is not { } periodId) return Fail<NativeGeneralLedgerSourceView>(ErrorCodes.ScopeDenied, "Access denied.");
    await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={request.ClientId} AND id={periodId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var captured = await CaptureAsync(db, actor, request, ct);
    if (!captured.Succeeded) return Fail<NativeGeneralLedgerSourceView>(captured.ErrorCode!, captured.Message!);
    var c = captured.Value!;
    if (!request.Reviewed || request.ReviewBasis != c.Preview.ReviewBasis)
      return Fail<NativeGeneralLedgerSourceView>(ErrorCodes.StaleRevision, "Review the exact native GL preview before sealing.");
    var duplicate = await db.NativeGeneralLedgerSources.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.EngagementId == request.EngagementId && x.SourceIdentityHash == c.Preview.SourceIdentityHash)
      .Select(x => (Guid?)x.ImportBatchId).SingleOrDefaultAsync(ct);
    if (duplicate is not null) return Fail<NativeGeneralLedgerSourceView>(ErrorCodes.Accounting.ImportDuplicate,
      $"This native GL source is already sealed as batch {duplicate:D}. Reuse that source.");
    var id = Guid.CreateVersion7(); var now = DateTimeOffset.UtcNow;
    var batch = new SourceImportBatch
    {
      Id = id, FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      PeriodId = c.Manifest.PeriodId, BookId = null, SourceKind = AccountingSourceKinds.GeneralLedger,
      OriginKind = SourceImportOrigins.NativeLedger, ProfileVersion = ProfileVersion, ParserVersion = ParserVersion,
      RawFileSha256Hex = "", NormalizedDatasetDigest = c.Preview.SourceIdentityHash,
      LegalEntityKey = c.Manifest.LegalEntityKey, Currency = c.Manifest.Currency, RowCount = c.Lines.Length,
      ExpectedChunkCount = 0, AcceptedChunkCount = 0, ExpectedTransactionCount = c.Manifest.TransactionCount,
      AcceptedTransactionCount = c.Manifest.TransactionCount, ExpectedLineCount = c.Lines.Length, AcceptedLineCount = c.Lines.Length,
      Status = "LOADING", ReceiptReference = request.EvidenceReference, CreatedByUserId = actor.UserId, CreatedAt = now
    };
    db.SourceImportBatches.Add(batch);
    // Origin guards must see the persisted LOADING parent before any generated row.
    // The deferred completeness guard is satisfied only by the final seal in this transaction.
    await db.SaveChangesAsync(ct);
    db.NativeGeneralLedgerSources.Add(new NativeGeneralLedgerSource
    {
      ImportBatchId = id, FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
      TrialBalanceDatasetId = request.TrialBalanceDatasetId, NativeTrialBalanceSourceHash = c.Manifest.NativeTrialBalanceSourceHash,
      NativeTrialBalanceManifestHash = c.Manifest.NativeTrialBalanceManifestHash, RequestId = request.RequestId,
      ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch, RequestHash = Purpose(actor, request),
      SourceIdentityHash = c.Preview.SourceIdentityHash, ManifestHash = c.Preview.ManifestHash, ManifestJson = c.ManifestJson,
      GeneratedRowsHash = c.Preview.GeneratedRowsHash, GeneratedRowsJson = c.RowsJson,
      TransactionCount = c.Manifest.TransactionCount, LineCount = c.Lines.Length, CreatedAt = now
    });
    foreach (var journal in c.Lines.GroupBy(x => x.JournalId))
    {
      var first = journal.First(); var transactionId = Guid.CreateVersion7();
      db.GeneralLedgerTransactions.Add(new GeneralLedgerTransaction
      {
        Id = transactionId, FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
        ImportBatchId = id, StableJournalId = journal.Key.ToString("D"), DocumentNumber = first.JournalNumber,
        PostingDate = DateOnly.ParseExact(first.PostingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture),
        DocumentDate = null, ServiceDate = null, SourceUser = first.SourceUser, SourceSystem = first.SourceSystem,
        ReversalReference = null, Currency = first.Currency, IsManual = null, IsYearEnd = null, CreatedAt = now
      });
      foreach (var line in journal)
        db.GeneralLedgerLines.Add(new GeneralLedgerLine
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, EngagementId = request.EngagementId,
          ImportBatchId = id, TransactionId = transactionId, StableLineId = line.LineId.ToString("D"),
          ClientAccountId = line.ClientAccountId, AccountCode = line.AccountCode, Debit = Amount(line.Debit), Credit = Amount(line.Credit),
          OriginalCurrency = line.OriginalCurrency, OriginalAmount = Amount(line.OriginalAmount), FunctionalAmount = Amount(line.FunctionalAmount),
          IsManual = null, IsYearEnd = null, CreatedAt = now
        });
    }
    await db.SaveChangesAsync(ct);
    await db.SourceImportBatches.Where(x => x.Id == id && x.Status == "LOADING")
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "SEALED"), ct);
    var sealedBatch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == id, ct);
    var current = await CheckCurrentAsync(db, sealedBatch, ct);
    if (!current.Succeeded) return Fail<NativeGeneralLedgerSourceView>(current.ErrorCode!, current.Message!);
    auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    if (!auth.Succeeded) return Fail<NativeGeneralLedgerSourceView>(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct);
    await tx.DisposeAsync();
    return await ViewAsync(db, actor, id, ct);
  }
}
