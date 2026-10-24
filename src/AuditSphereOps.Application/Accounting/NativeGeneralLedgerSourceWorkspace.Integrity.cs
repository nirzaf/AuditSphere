using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class NativeGeneralLedgerSourceWorkspace
{
  private sealed record Retained(NativeGeneralLedgerSource Evidence, NativeManifest Manifest,
    NativeGeneralLedgerSourceLine[] Rows, TrialBalanceDataset TrialBalance);
  internal sealed record OpeningEvidence(NativeGeneralLedgerOpeningSource Source, IReadOnlyDictionary<string, decimal> Amounts);

  private static async Task<CommandResult<Retained>> RetainedAsync(IClientAccountingDbContext db, SourceImportBatch batch, CancellationToken ct)
  {
    var evidence = await db.NativeGeneralLedgerSources.AsNoTracking().SingleOrDefaultAsync(x => x.ImportBatchId == batch.Id &&
      x.FirmId == batch.FirmId && x.ClientId == batch.ClientId && x.EngagementId == batch.EngagementId, ct);
    if (evidence is null) return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL evidence is missing.");
    try
    {
      var m = JsonSerializer.Deserialize<NativeManifest>(evidence.ManifestJson);
      var rows = JsonSerializer.Deserialize<NativeGeneralLedgerSourceLine[]>(evidence.GeneratedRowsJson);
      var tb = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == evidence.TrialBalanceDatasetId &&
        x.FirmId == batch.FirmId && x.ClientId == batch.ClientId && x.EngagementId == batch.EngagementId, ct);
      var tbEvidence = await db.NativeTrialBalanceSources.AsNoTracking().SingleOrDefaultAsync(x => x.DatasetId == evidence.TrialBalanceDatasetId, ct);
      if (m is null || rows is null || rows.Any(x => x is null) || tb is null || tbEvidence is null ||
          batch.OriginKind != SourceImportOrigins.NativeLedger || batch.SourceKind != AccountingSourceKinds.GeneralLedger ||
          batch.Status != "SEALED" || batch.BookId is not null || batch.RawFileSha256Hex != "" ||
          batch.ProfileVersion != ProfileVersion || batch.ParserVersion != ParserVersion ||
          batch.ExpectedChunkCount != 0 || batch.AcceptedChunkCount != 0 ||
          m.Version != Version || m.UnavailableMetadata is null || !m.UnavailableMetadata.SequenceEqual(Unavailable) || m.FirmId != batch.FirmId || m.ClientId != batch.ClientId || m.EngagementId != batch.EngagementId ||
          m.PeriodId != batch.PeriodId || m.PeriodId != tb.PeriodId || m.Currency != batch.Currency || m.Currency != tb.Currency ||
          m.LegalEntityKey != batch.LegalEntityKey || m.LegalEntityKey != tb.LegalEntityKey || m.BookScope != BookScope ||
          m.TrialBalanceDatasetId != tb.Id || evidence.ActorId != batch.CreatedByUserId ||
          evidence.NativeTrialBalanceSourceHash != tb.Sha256Hex || m.NativeTrialBalanceSourceHash != tb.Sha256Hex ||
          evidence.NativeTrialBalanceManifestHash != tbEvidence.ManifestHash || m.NativeTrialBalanceManifestHash != tbEvidence.ManifestHash ||
          m.NativeTrialBalanceManifestJson != tbEvidence.ManifestJson ||
          HashText(evidence.ManifestJson) != evidence.ManifestHash || HashText(evidence.GeneratedRowsJson) != evidence.GeneratedRowsHash ||
          m.GeneratedRowsHash != evidence.GeneratedRowsHash || Identity(m) != evidence.SourceIdentityHash ||
          batch.NormalizedDatasetDigest != evidence.SourceIdentityHash ||
          evidence.LineCount is < 1 or > 250_000 || evidence.TransactionCount is < 1 or > 250_000 ||
          rows.Length != evidence.LineCount || m.LineCount != evidence.LineCount || m.TransactionCount != evidence.TransactionCount ||
          batch.RowCount != rows.Length || batch.ExpectedLineCount != rows.Length || batch.AcceptedLineCount != rows.Length ||
          batch.ExpectedTransactionCount != evidence.TransactionCount || batch.AcceptedTransactionCount != evidence.TransactionCount ||
          rows.Select(x => x.LineId).Distinct().Count() != rows.Length || rows.Select(x => x.JournalId).Distinct().Count() != evidence.TransactionCount ||
          rows.Sum(x => Amount(x.Debit)) != m.TotalDebit || rows.Sum(x => Amount(x.Credit)) != m.TotalCredit || m.TotalDebit != m.TotalCredit)
        return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL header, source identity or retained canonical bytes failed integrity checks.");
      var integrity = await NativeTrialBalanceSourceWorkspace.CheckIntegrityAsync(db, tb, ct);
      if (!integrity.Succeeded) return Fail<Retained>(integrity.ErrorCode!, integrity.Message!);
      using var sourceDocument = JsonDocument.Parse(tbEvidence.ManifestJson);
      var sourceRoot = sourceDocument.RootElement;
      using var membership = JsonDocument.Parse(sourceRoot.GetProperty("Pins").GetProperty("MembershipJson").GetString()!);
      var opening = membership.RootElement.GetProperty("Opening");
      if (m.NativeOpening is null || opening.ValueKind != JsonValueKind.Object ||
          m.NativeOpening.OpeningId != opening.GetProperty("Id").GetGuid() ||
          m.NativeOpening.ManifestHash != opening.GetProperty("ManifestSha256").GetString() ||
          m.PostingSequence != sourceRoot.GetProperty("Pins").GetProperty("PostingSequence").GetInt64() ||
          m.AsOfDate != sourceRoot.GetProperty("AsOfDate").GetString() ||
          sourceRoot.GetProperty("ActivityOnly").GetBoolean())
        return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL opening, cutoff or posted highwater differs from its exact native TB evidence.");
      var sourceJournals = membership.RootElement.GetProperty("Journals").Deserialize<ClientOperationalJournal[]>()!
        .ToDictionary(x => x.Id);
      var sourceLines = membership.RootElement.GetProperty("Lines").Deserialize<ClientOperationalJournalLine[]>()!
        .ToDictionary(x => x.Id);
      if (sourceLines.Count != rows.Length || sourceJournals.Count != evidence.TransactionCount)
        return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL must retain the complete period-end pinned posted population.");
      foreach (var row in rows)
        if (!sourceJournals.TryGetValue(row.JournalId, out var originalJournal) ||
            !sourceLines.TryGetValue(row.LineId, out var originalLine) || originalLine.JournalId != originalJournal.Id ||
            originalJournal.Status != "POSTED" || originalJournal.Currency != row.Currency ||
            originalJournal.JournalNumber != row.JournalNumber || originalJournal.CreatedByUserId.ToString("D") != row.SourceUser ||
            originalJournal.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != row.PostingDate ||
            originalLine.ClientAccountId != row.ClientAccountId || originalLine.AccountCode != row.AccountCode ||
            originalLine.Debit != Amount(row.Debit) || originalLine.Credit != Amount(row.Credit))
          return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL artifact differs from the original pinned native journal or line.");
      var journals = await db.GeneralLedgerTransactions.AsNoTracking().Where(x => x.FirmId == batch.FirmId &&
        x.ClientId == batch.ClientId && x.EngagementId == batch.EngagementId && x.ImportBatchId == batch.Id).Take(250_001).ToListAsync(ct);
      var lines = await db.GeneralLedgerLines.AsNoTracking().Where(x => x.FirmId == batch.FirmId &&
        x.ClientId == batch.ClientId && x.EngagementId == batch.EngagementId && x.ImportBatchId == batch.Id).Take(250_001).ToListAsync(ct);
      if (journals.Count != evidence.TransactionCount || lines.Count != rows.Length ||
          journals.Select(x => x.StableJournalId).Distinct().Count() != journals.Count || lines.Select(x => x.StableLineId).Distinct().Count() != lines.Count)
        return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL projection membership differs from retained source evidence.");
      var byJournal = journals.ToDictionary(x => x.StableJournalId, StringComparer.Ordinal);
      var byLine = lines.ToDictionary(x => x.StableLineId, StringComparer.Ordinal);
      foreach (var row in rows)
      {
        if (!byJournal.TryGetValue(row.JournalId.ToString("D"), out var j) || !byLine.TryGetValue(row.LineId.ToString("D"), out var l) ||
            j.Id != l.TransactionId || j.DocumentNumber != row.JournalNumber || j.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != row.PostingDate ||
            j.SourceUser != row.SourceUser || j.SourceSystem != "AUDITSPHERE_NATIVE" || row.SourceSystem != j.SourceSystem ||
            j.DocumentDate is not null || j.ServiceDate is not null || j.ReversalReference is not null ||
            j.Currency != batch.Currency || row.Currency != batch.Currency || l.OriginalCurrency != batch.Currency || row.OriginalCurrency != batch.Currency ||
            j.IsManual is not null || j.IsYearEnd is not null || l.IsManual is not null || l.IsYearEnd is not null ||
            row.IsManual is not null || row.IsYearEnd is not null ||
            l.ClientAccountId != row.ClientAccountId || l.AccountCode != row.AccountCode || l.Debit != Amount(row.Debit) || l.Credit != Amount(row.Credit) ||
            l.OriginalAmount != Amount(row.OriginalAmount) || l.FunctionalAmount != Amount(row.FunctionalAmount) ||
            l.FunctionalAmount != l.Debit - l.Credit || l.OriginalAmount != l.FunctionalAmount ||
            l.PartyIdentifier != "" || l.Branch != "" || l.CostCentre != "" || l.Department != "" || l.Project != "" || l.IntercompanyCounterparty != "")
          return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL projection does not reproduce its retained original source facts.");
      }
      return CommandResult<Retained>.Ok(new(evidence, m, rows, tb));
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
    { return Fail<Retained>(ErrorCodes.ProtectedState, "Native GL retained evidence is malformed or unsupported."); }
  }

  internal static async Task<CommandResult> CheckIntegrityAsync(IAuditSphereDbContext db, SourceImportBatch batch, CancellationToken ct = default)
  {
    if (batch.OriginKind == SourceImportOrigins.External) return CommandResult.Ok();
    if (batch.OriginKind != SourceImportOrigins.NativeLedger || db is not IClientAccountingDbContext accounting)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Native GL verification requires supported client accounting capability and provenance.");
    var retained = await RetainedAsync(accounting, batch, ct);
    return retained.Succeeded ? CommandResult.Ok() : CommandResult.Fail(retained.ErrorCode!, retained.Message!);
  }

  internal static async Task<CommandResult> CheckCurrentAsync(IAuditSphereDbContext db, SourceImportBatch batch, CancellationToken ct = default)
  {
    if (batch.OriginKind == SourceImportOrigins.External) return CommandResult.Ok();
    if (batch.OriginKind != SourceImportOrigins.NativeLedger || db is not IClientAccountingDbContext accounting)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Native GL verification requires supported client accounting capability and provenance.");
    var retained = await RetainedAsync(accounting, batch, ct);
    if (!retained.Succeeded) return CommandResult.Fail(retained.ErrorCode!, retained.Message!);
    return await NativeTrialBalanceSourceWorkspace.CheckReportingAsync(db, retained.Value!.TrialBalance, ct);
  }

  internal static async Task<CommandResult> CheckPackagePairAsync(IClientAccountingDbContext db,
    SourceImportBatch batch, Guid mappingVersionId, CancellationToken ct)
  {
    if (batch.OriginKind != SourceImportOrigins.NativeLedger) return CommandResult.Ok();
    var exact = await (from evidence in db.NativeGeneralLedgerSources.AsNoTracking()
      join mapping in db.MappingVersions.AsNoTracking() on evidence.TrialBalanceDatasetId equals mapping.DatasetId
      where evidence.ImportBatchId == batch.Id && evidence.FirmId == batch.FirmId && evidence.ClientId == batch.ClientId &&
        evidence.EngagementId == batch.EngagementId && mapping.Id == mappingVersionId && mapping.FirmId == batch.FirmId &&
        mapping.ClientId == batch.ClientId && mapping.EngagementId == batch.EngagementId
      select evidence.ImportBatchId).AnyAsync(ct);
    return exact ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.GateBlocked,
      "The native GL source must be paired with the exact native TB dataset used by this package mapping.");
  }

  internal static async Task<CommandResult<OpeningEvidence?>> OpeningAsync(IClientAccountingDbContext db,
    SourceImportBatch batch, CancellationToken ct)
  {
    if (batch.OriginKind == SourceImportOrigins.External) return CommandResult<OpeningEvidence?>.Ok(null);
    var current = await CheckCurrentAsync(db, batch, ct);
    if (!current.Succeeded) return Fail<OpeningEvidence?>(current.ErrorCode!, current.Message!);
    var retained = await RetainedAsync(db, batch, ct);
    if (!retained.Succeeded) return Fail<OpeningEvidence?>(retained.ErrorCode!, retained.Message!);
    var r = retained.Value!;
    using var json = JsonDocument.Parse(r.Manifest.NativeTrialBalanceManifestJson);
    var rows = json.RootElement.GetProperty("TrialBalance").GetProperty("Rows").EnumerateArray();
    var amounts = rows.ToDictionary(x => x.GetProperty("AccountCode").GetString()!,
      x => Amount(x.GetProperty("OpeningDebit").GetString()!) - Amount(x.GetProperty("OpeningCredit").GetString()!), StringComparer.Ordinal);
    return CommandResult<OpeningEvidence?>.Ok(new(r.Manifest.NativeOpening, amounts));
  }

  public static async Task<CommandResult<NativeGeneralLedgerSourceView>> ViewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid importBatchId, CancellationToken ct = default)
  {
    var read = await ReadAsync(db, actor, importBatchId, ct);
    if (!read.Succeeded) return Fail<NativeGeneralLedgerSourceView>(read.ErrorCode!, read.Message!);
    var r = read.Value!; var m = r.Manifest; var e = r.Evidence;
    var batch = await db.SourceImportBatches.AsNoTracking().SingleAsync(x => x.Id == importBatchId && x.FirmId == actor.FirmId, ct);
    var current = await CheckCurrentAsync(db, batch, ct);
    var auth = await AuthorizeAsync(db, actor, batch.ClientId, batch.EngagementId, false, ct);
    if (!auth.Succeeded) return Fail<NativeGeneralLedgerSourceView>(auth.ErrorCode!, auth.Message!);
    return CommandResult<NativeGeneralLedgerSourceView>.Ok(new(batch.Id, batch.ClientId, batch.EngagementId,
      e.TrialBalanceDatasetId, m.PeriodId, m.AsOfDate, m.Currency, m.LegalEntityKey, BookScope, batch.OriginKind, batch.Status,
      e.SourceIdentityHash, e.ManifestHash, e.GeneratedRowsHash, e.TransactionCount, e.LineCount, Money(m.TotalDebit), Money(m.TotalCredit),
      m.PostingSequence.ToString(CultureInfo.InvariantCulture), m.NativeOpening, Unavailable,
      current.Succeeded, current.Succeeded ? null : current.Message, e.ActorId, e.CreatedAt));
  }

  private static async Task<CommandResult<Retained>> ReadAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, CancellationToken ct)
  {
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId &&
      x.OriginKind == SourceImportOrigins.NativeLedger && x.Status == "SEALED", ct);
    if (batch is null) return Fail<Retained>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, batch.ClientId, batch.EngagementId, false, ct);
    if (!auth.Succeeded) return Fail<Retained>(auth.ErrorCode!, auth.Message!);
    return await RetainedAsync(db, batch, ct);
  }

  public static async Task<CommandResult<NativeGeneralLedgerSourceArtifact>> ArtifactAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid importBatchId, CancellationToken ct = default)
  {
    var view = await ViewAsync(db, actor, importBatchId, ct);
    if (!view.Succeeded) return Fail<NativeGeneralLedgerSourceArtifact>(view.ErrorCode!, view.Message!);
    var retained = await ReadAsync(db, actor, importBatchId, ct);
    if (!retained.Succeeded) return Fail<NativeGeneralLedgerSourceArtifact>(retained.ErrorCode!, retained.Message!);
    var e = retained.Value!.Evidence;
    var auth = await AuthorizeAsync(db, actor, e.ClientId, e.EngagementId, false, ct);
    if (!auth.Succeeded) return Fail<NativeGeneralLedgerSourceArtifact>(auth.ErrorCode!, auth.Message!);
    return CommandResult<NativeGeneralLedgerSourceArtifact>.Ok(new(importBatchId, $"native-gl-{importBatchId:D}.json", "application/json",
      e.GeneratedRowsJson, e.GeneratedRowsHash, e.ManifestJson, e.ManifestHash, e.SourceIdentityHash, view.Value!.IsCurrent, view.Value.CurrentnessBlocker));
  }
}
