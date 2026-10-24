using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class NativeTrialBalanceSourceWorkspace
{
  private sealed record SourcePins(string ProfileHash, string MandateHash, string? OpeningHash,
    string JournalsHash, string AccountsHash, long PostingSequence, string Currency, string MembershipJson);
  private sealed record Manifest(string Version, Guid FirmId, Guid ClientId, Guid EngagementId, Guid PeriodId,
    long PeriodRevision, DateOnly PeriodStart, DateOnly PeriodEnd, DateOnly AsOfDate, string Basis, string Currency,
    string LegalEntityKey, string BookScope, bool ActivityOnly, SourcePins Pins, string RowsDigest, string GeneratedRowsHash,
    ClientOperationalTrialBalanceView TrialBalance, string EvidenceReference);
  private sealed record Capture(Manifest Manifest, string ManifestJson, string RowsJson,
    IReadOnlyList<TbImportRow> Rows, NativeTrialBalanceSourcePreview Preview);
  private static string Identity(Manifest manifest) => Hash(manifest with { EvidenceReference = "" });

  private static async Task<CommandResult<Capture>> CaptureAsync(IClientAccountingDbContext db,
    ActorContext actor, NativeTrialBalanceSourceRequest request, CancellationToken ct)
  {
    var auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    if (!auth.Succeeded) return Fail<Capture>(auth.ErrorCode!, auth.Message!);
    var scope = await ReportingScopeResolver.ResolveAsync(db, actor.FirmId, request.ClientId, request.EngagementId,
      request.PeriodId, null, ct);
    if (!scope.Succeeded) return Fail<Capture>(scope.ErrorCode!, scope.Message!);
    var period = scope.Value!.Period;
    if (period.Revision != request.ExpectedPeriodRevision || period.Status is not ("ACTIVE" or "DRAFT") ||
        request.AsOfDate < period.StartDate || request.AsOfDate > period.EndDate)
      return Fail<Capture>(ErrorCodes.StaleRevision, "The exact open period revision and an as-of date inside it are required.");
    var proofScope = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      db.ClientOperationalOpeningBalances.Any(o => o.Id == x.OpeningId && o.PeriodId == period.Id))
      .Select(x => (Guid?)x.EngagementId).SingleOrDefaultAsync(ct);
    if (proofScope.HasValue && proofScope.Value != request.EngagementId)
      return Fail<Capture>(ErrorCodes.ScopeDenied, "The successor opening belongs to another source engagement.");
    var pins = await ReadPinsAsync(db, actor.FirmId, request.ClientId, period.Id, ct);
    if (!pins.Succeeded) return Fail<Capture>(pins.ErrorCode!, pins.Message!);
    if (period.Currency != pins.Value!.Currency)
      return Fail<Capture>(ErrorCodes.GateBlocked, "The native period must use its actual functional currency.");
    var activityOnly = pins.Value.OpeningHash is null;
    if (activityOnly && !request.AllowActivityOnly)
      return Fail<Capture>(ErrorCodes.GateBlocked, "No approved opening is recorded. Explicit activity-only capture may retain movements, but cannot assert complete opening coverage.");
    // Participate in this capture's transaction; the live reader owns a snapshot only when called independently.
    var ledger = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, actor, request.ClientId, period.Id,
      ct: ct, fromDate: period.StartDate, toDate: request.AsOfDate);
    if (!ledger.Succeeded) return Fail<Capture>(ledger.ErrorCode!, ledger.Message!);
    var tb = ledger.Value!.TrialBalance;
    if (tb.Rows.Count is 0 or > 250_000 || tb.Rows.Select(x => x.AccountCode).Distinct(StringComparer.Ordinal).Count() != tb.Rows.Count)
      return Fail<Capture>(ErrorCodes.Accounting.ImportRejected, "Native source rows must be nonempty, bounded and have unambiguous account codes.");
    var rows = tb.Rows.Select(x => new TbImportRow(x.AccountCode, x.AccountName,
      Amount(x.ClosingDebit) - Amount(x.ClosingCredit), period.Currency, request.LegalEntityKey, null,
      Amount(x.ClosingDebit), Amount(x.ClosingCredit))).ToArray();
    if (rows.Sum(x => x.Amount) != 0 || Amount(tb.OpeningDebit) != Amount(tb.OpeningCredit) ||
        Amount(tb.PeriodDebit) != Amount(tb.PeriodCredit) || Amount(tb.ClosingDebit) != Amount(tb.ClosingCredit))
      return Fail<Capture>(ErrorCodes.Accounting.ImportRejected, "Native source rows and separate opening/movement/closing control totals must balance exactly.");
    var parsed = TrialBalanceCsvImporter.BuildParsed(rows, period.Currency, "", NativeProfile);
    var rowsJson = JsonSerializer.Serialize(tb.Rows);
    var rowsHash = HashText(rowsJson);
    var manifest = new Manifest(Version, actor.FirmId, request.ClientId, request.EngagementId, period.Id,
      period.Revision, period.StartDate, period.EndDate, request.AsOfDate, period.Basis, period.Currency,
      request.LegalEntityKey, BookScope, activityOnly, pins.Value, parsed.NormalizedDatasetDigest, rowsHash, tb,
      request.EvidenceReference);
    var json = JsonSerializer.Serialize(manifest);
    if (System.Text.Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024 || System.Text.Encoding.UTF8.GetByteCount(rowsJson) > 32 * 1024 * 1024)
      return Fail<Capture>(ErrorCodes.Accounting.ImportRejected, "The native source exceeds the retained manifest or artifact bound.");
    var identity = Identity(manifest);
    var manifestHash = HashText(json);
    var preview = new NativeTrialBalanceSourcePreview(Basis(Purpose(actor, request), identity, manifestHash), identity,
      manifestHash, rowsHash, rows.Length, BookScope, activityOnly, period.Currency,
      pins.Value.PostingSequence.ToString(CultureInfo.InvariantCulture), tb);
    return CommandResult<Capture>.Ok(new(manifest, json, rowsJson, rows, preview));
  }

  private static async Task<CommandResult<SourcePins>> ReadPinsAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, Guid periodId, CancellationToken ct, bool checkSuccessor = true)
  {
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId, ct);
    var mandate = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == clientId &&
      x.EngagementId == null && x.ServiceRoute == "BOOKKEEPING").OrderByDescending(x => x.Generation)
      .ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (profile is null || profile.SourceMode != ClientAccountingSourceModes.NativeBookkeeping ||
        mandate is not { Decision: "Accepted", Conditions: null or "" })
      return Fail<SourcePins>(ErrorCodes.GateBlocked, "Current native profile and accepted BOOKKEEPING decision are required.");
    var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.PeriodId == periodId, ct);
    if (opening is not null && (opening.ApprovedByUserId is null || opening.ApprovedAt is null || !ClientOperationalOpeningBalanceWorkspace.ValidManifest(opening)))
      return Fail<SourcePins>(ErrorCodes.GateBlocked, "The exact opening source must retain its independent approval and valid manifest.");
    if (opening is not null && checkSuccessor)
    {
      var successor = await CheckOpeningSuccessorChainAsync(db, opening, ct);
      if (!successor.Succeeded) return Fail<SourcePins>(successor.ErrorCode!, successor.Message!);
    }
    var journals = await db.ClientOperationalJournals.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.PeriodId == periodId && x.Status == "POSTED").OrderBy(x => x.PostingSequence).ThenBy(x => x.Id).Take(250_001).ToListAsync(ct);
    if (journals.Count > 250_000) return Fail<SourcePins>(ErrorCodes.GateBlocked, "The native posting population exceeds the bounded source capture.");
    var ids = journals.Select(x => x.Id).ToArray();
    var lines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId))
      .OrderBy(x => x.JournalId).ThenBy(x => x.LineNumber).ThenBy(x => x.Id).Take(250_001).ToListAsync(ct);
    if (lines.Count > 250_000) return Fail<SourcePins>(ErrorCodes.GateBlocked, "The native line population exceeds the bounded source capture.");
    var submissions = await db.ClientOperationalJournalSnapshots.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId))
      .OrderBy(x => x.JournalId).ThenBy(x => x.JournalRevision).Take(250_001).ToListAsync(ct);
    var decisions = await db.ClientOperationalJournalDecisions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId))
      .OrderBy(x => x.JournalId).ThenBy(x => x.JournalRevision).ThenBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var receipts = await db.ClientOperationalPostingReceipts.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId))
      .OrderBy(x => x.JournalId).ThenBy(x => x.Id).Take(250_001).ToListAsync(ct);
    if (submissions.Count > 250_000 || decisions.Count > 250_000 || receipts.Count > 250_000)
      return Fail<SourcePins>(ErrorCodes.GateBlocked, "The native source decision/receipt population exceeds its capture bound.");
    foreach (var j in journals)
    {
      var submitted = submissions.SingleOrDefault(x => x.JournalId == j.Id && x.JournalRevision == j.Revision - 1);
      if (j.PostingSequence is null or <= 0 || j.Currency != profile.FunctionalCurrency || submitted is null ||
          !ValidSubmission(submitted, j, lines.Where(x => x.JournalId == j.Id).ToArray()) ||
          !decisions.Any(x => x.JournalId == j.Id && x.JournalRevision == j.Revision - 1 && x.Decision == "APPROVE" &&
            x.ActorUserId == j.PostedByUserId && x.ActorUserId != j.CreatedByUserId && x.CreatedAt == j.PostedAt) ||
          !receipts.Any(x => x.JournalId == j.Id && x.SubmittedRevision == j.Revision - 1 && x.PostedRevision == j.Revision &&
            x.ActorUserId == j.PostedByUserId && x.RecordedAt == j.PostedAt))
        return Fail<SourcePins>(ErrorCodes.GateBlocked, "A posted journal lacks its exact immutable submitted content, sequence, independent decision or posting receipt. Legacy evidence is not reconstructed.");
    }
    var accountIds = lines.Select(x => x.ClientAccountId).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && accountIds.Contains(x.Id))
      .OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    // Opening chart membership comes from its verified manifest, not a guessed current chart.
    Guid? openingChart = null;
    if (opening is not null)
    {
      using var document = JsonDocument.Parse(opening.ManifestJson);
      openingChart = document.RootElement.GetProperty("ChartVersionId").GetGuid();
      var additional = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && x.ChartVersionId == openingChart)
        .OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
      accounts = accounts.Concat(additional).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToList();
    }
    var chartIds = accounts.Select(x => x.ChartVersionId).Distinct().ToArray();
    var charts = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && chartIds.Contains(x.Id)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var sales = await db.ClientSalesInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var purchases = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var salesCredits = await db.ClientSalesCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var purchaseCredits = await db.ClientPurchaseCreditNoteSubmissions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var settlements = await db.ClientManualSettlementOrigins.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.JournalId)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    var reversals = await db.ClientOperationalJournalReversals.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.ReversalJournalId)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    if (accounts.Count > 250_000 || charts.Count > 250_000 || sales.Count > 250_000 || purchases.Count > 250_000 ||
        salesCredits.Count > 250_000 || purchaseCredits.Count > 250_000 || settlements.Count > 250_000 || reversals.Count > 250_000)
      return Fail<SourcePins>(ErrorCodes.GateBlocked, "The native source account/origin population exceeds its capture bound.");
    // Retain recoverable membership, not merely a hash of an undisclosed live population.
    // Original submission bytes remain in their immutable snapshots, identified here by ID/revision/hash.
    var membership = JsonSerializer.Serialize(new
    {
      Profile = profile, Mandate = mandate,
      Opening = opening is null ? null : new { opening.Id, opening.PeriodRevision, opening.AsOfDate,
        opening.Currency, opening.EvidenceSha256, opening.ManifestSha256, opening.CreatedByUserId,
        opening.ApprovedByUserId, opening.ApprovedAt },
      Journals = journals, Lines = lines, Accounts = accounts, Charts = charts,
      Submissions = submissions.Select(x => new { x.Id, x.JournalId, x.JournalRevision, x.CaptureKind,
        SnapshotHash = HashText(x.SnapshotJson) }),
      Decisions = decisions, Receipts = receipts,
      Sales = sales.Select(x => new { x.Id, x.JournalId, SourceHash = Hash(x) }),
      Purchases = purchases.Select(x => new { x.Id, x.JournalId, SourceHash = Hash(x) }),
      SalesCredits = salesCredits.Select(x => new { x.Id, x.JournalId, SourceHash = Hash(x) }),
      PurchaseCredits = purchaseCredits.Select(x => new { x.Id, x.JournalId, SourceHash = Hash(x) }),
      Settlements = settlements.Select(x => new { x.Id, x.JournalId, SourceHash = Hash(x) }),
      Reversals = reversals.Select(x => new { x.Id, x.ReversalJournalId, SourceHash = Hash(x) })
    });
    if (System.Text.Encoding.UTF8.GetByteCount(membership) > 16 * 1024 * 1024)
      return Fail<SourcePins>(ErrorCodes.GateBlocked, "The native source membership exceeds its retained evidence bound.");
    var successorProof = opening is null ? null : await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking()
      .SingleOrDefaultAsync(x => x.OpeningId == opening.Id, ct);
    if (successorProof is not null)
    {
      var extended = System.Text.Json.Nodes.JsonNode.Parse(membership)!.AsObject();
      extended.Add("OpeningSuccessorProof", JsonSerializer.SerializeToNode(new { successorProof.OpeningId, successorProof.ProofHash }));
      membership = extended.ToJsonString();
    }
    var postingHash = Hash(new { journals, lines, submissions, decisions, receipts, sales, purchases, salesCredits, purchaseCredits, settlements, reversals });
    return CommandResult<SourcePins>.Ok(new(Hash(profile), Hash(mandate), opening is null ? null : Hash(opening),
      postingHash, Hash(new { accounts, charts }), journals.Select(x => x.PostingSequence ?? 0).DefaultIfEmpty().Max(), profile.FunctionalCurrency, membership));
  }

  private static bool ValidSubmission(ClientOperationalJournalSnapshot snapshot, ClientOperationalJournal journal,
    IReadOnlyList<ClientOperationalJournalLine> lines)
  {
    try
    {
      using var json = JsonDocument.Parse(snapshot.SnapshotJson);
      var header = json.RootElement.GetProperty("journal");
      if (header.GetProperty("id").GetGuid() != journal.Id || header.GetProperty("firm_id").GetGuid() != journal.FirmId ||
          header.GetProperty("client_id").GetGuid() != journal.ClientId || header.GetProperty("revision").GetInt64() != snapshot.JournalRevision)
        return false;
      var saved = json.RootElement.GetProperty("lines").EnumerateArray().OrderBy(x => x.GetProperty("line_number").GetInt32()).ToArray();
      var actual = lines.OrderBy(x => x.LineNumber).ToArray();
      return saved.Length == actual.Length && saved.Zip(actual).All(x => x.First.GetProperty("id").GetGuid() == x.Second.Id &&
        x.First.GetProperty("client_account_id").GetGuid() == x.Second.ClientAccountId &&
        x.First.GetProperty("account_code").GetString() == x.Second.AccountCode &&
        x.First.GetProperty("account_name").GetString() == x.Second.AccountName &&
        x.First.GetProperty("debit").GetDecimal() == x.Second.Debit && x.First.GetProperty("credit").GetDecimal() == x.Second.Credit);
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return false; }
  }
}
