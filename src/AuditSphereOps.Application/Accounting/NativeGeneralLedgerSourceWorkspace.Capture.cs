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

public static partial class NativeGeneralLedgerSourceWorkspace
{
  public const string ProfileVersion = "native-posted-gl.v1";
  public const string ParserVersion = "native-gl-export.v1";
  public const string BookScope = "PERIOD_LEDGER_NO_BOOK";
  internal static IReadOnlyList<string> UnavailableMetadata => Unavailable;
  private const string Version = "native-gl-source.v1";
  private static readonly string[] Unavailable = ["IsManual", "IsYearEnd", "DocumentDate", "ServiceDate", "ReversalReference",
    "PartyIdentifier", "Branch", "CostCentre", "Department", "Project", "IntercompanyCounterparty"];
  private sealed record NativeManifest(string Version, Guid FirmId, Guid ClientId, Guid EngagementId,
    Guid TrialBalanceDatasetId, Guid PeriodId, string NativeTrialBalanceSourceHash, string NativeTrialBalanceManifestHash,
    string NativeTrialBalanceManifestJson, string AsOfDate, string Currency, string LegalEntityKey, string BookScope,
    long PostingSequence, NativeGeneralLedgerOpeningSource NativeOpening, int TransactionCount, int LineCount,
    decimal TotalDebit, decimal TotalCredit, string GeneratedRowsHash, string EvidenceReference, IReadOnlyList<string> UnavailableMetadata);
  private sealed record Capture(NativeManifest Manifest, string ManifestJson, string RowsJson,
    NativeGeneralLedgerSourceLine[] Lines, NativeGeneralLedgerSourcePreview Preview);
  private static string Money(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
  private static decimal Amount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
  private static string HashText(string value) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(value));
  private static string Hash(object value) => HashText(JsonSerializer.Serialize(value));
  private static string Identity(NativeManifest m) => Hash(m with { EvidenceReference = "" });
  private static string Purpose(ActorContext actor, NativeGeneralLedgerSourceRequest request) =>
    Hash(new { actor.FirmId, actor.UserId, actor.SessionEpoch, Request = request with { Reviewed = false, ReviewBasis = null } });
  private static string Basis(string purpose, string identity, string manifest) => Hash(new { purpose, identity, manifest });
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string? Validate(NativeGeneralLedgerSourceRequest r) =>
    r.RequestId == Guid.Empty || r.ClientId == Guid.Empty || r.EngagementId == Guid.Empty || r.TrialBalanceDatasetId == Guid.Empty ||
    !SourceAcceptanceWorkspace.ValidHash(r.ExpectedTrialBalanceSourceHash) || !r.PeriodLedgerReviewed ||
    string.IsNullOrWhiteSpace(r.EvidenceReference) || r.EvidenceReference.Length > 300
      ? "Review the explicit whole-period ledger without book membership, exact native TB identity and an evidence reference of 1 to 300 characters." : null;

  private static async Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, bool write, CancellationToken ct)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, clientId, engagementId, GeneralLedgerQuery.ReadRoles, InternalOnly: true, RequireProfessionalWork: write), ct);
    if (!auth.Succeeded || !write) return auth;
    if (await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == "FROZEN", ct))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen.");
    return await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct)
      ? auth : CommandResult.Fail(ErrorCodes.GateBlocked, "Current accepted BOOKKEEPING authority is required.");
  }

  private static async Task<CommandResult<Capture>> CaptureAsync(IClientAccountingDbContext db, ActorContext actor,
    NativeGeneralLedgerSourceRequest request, CancellationToken ct)
  {
    var auth = await AuthorizeAsync(db, actor, request.ClientId, request.EngagementId, true, ct);
    if (!auth.Succeeded) return Fail<Capture>(auth.ErrorCode!, auth.Message!);
    var tb = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.TrialBalanceDatasetId &&
      x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.EngagementId == request.EngagementId &&
      x.SourceKind == NativeTrialBalanceSourceWorkspace.SourceKind, ct);
    if (tb is null) return Fail<Capture>(ErrorCodes.ScopeDenied, "The exact native TB source is outside this scope.");
    if (tb.Sha256Hex != request.ExpectedTrialBalanceSourceHash)
      return Fail<Capture>(ErrorCodes.StaleRevision, "The reviewed native TB identity differs.");
    var current = await NativeTrialBalanceSourceWorkspace.CheckReportingAsync(db, tb, ct);
    if (!current.Succeeded) return Fail<Capture>(current.ErrorCode!, current.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleAsync(x => x.Id == tb.PeriodId && x.FirmId == actor.FirmId, ct);
    if (period.Status is not ("ACTIVE" or "DRAFT"))
      return Fail<Capture>(ErrorCodes.GateBlocked, "A new native GL source requires an open reporting period.");
    var evidence = await db.NativeTrialBalanceSources.AsNoTracking().SingleAsync(x => x.DatasetId == tb.Id, ct);
    try
    {
      using var document = JsonDocument.Parse(evidence.ManifestJson);
      var root = document.RootElement;
      using var membership = JsonDocument.Parse(root.GetProperty("Pins").GetProperty("MembershipJson").GetString()!);
      var source = membership.RootElement;
      var opening = source.GetProperty("Opening");
      if (opening.ValueKind != JsonValueKind.Object)
        return Fail<Capture>(ErrorCodes.GateBlocked, "A complete native GL source requires the actual approved native opening.");
      var journals = source.GetProperty("Journals").Deserialize<ClientOperationalJournal[]>()!;
      var lines = source.GetProperty("Lines").Deserialize<ClientOperationalJournalLine[]>()!;
      var highwater = root.GetProperty("Pins").GetProperty("PostingSequence").GetInt64();
      // Deserialize DateOnly directly: do not introduce local-time conversion into the cutoff.
      var cutoff = root.GetProperty("AsOfDate").Deserialize<DateOnly>();
      var selected = journals.Where(x => x.PostingDate <= cutoff).ToDictionary(x => x.Id);
      if (selected.Count is < 1 or > 250_000 || lines.Length > 250_000)
        return Fail<Capture>(ErrorCodes.GateBlocked, "Native GL requires a nonempty bounded actual posted journal population; opening is not a journal.");
      var rows = lines.Where(x => selected.ContainsKey(x.JournalId)).OrderBy(x => selected[x.JournalId].PostingSequence)
        .ThenBy(x => x.JournalId).ThenBy(x => x.LineNumber).ThenBy(x => x.Id).Select(x =>
        {
          var j = selected[x.JournalId];
          return new NativeGeneralLedgerSourceLine(j.Id, x.Id, x.ClientAccountId, j.JournalNumber,
            j.PostingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.AccountCode, Money(x.Debit), Money(x.Credit),
            j.Currency, Money(x.Debit - x.Credit), Money(x.Debit - x.Credit), j.Currency,
            j.CreatedByUserId.ToString("D"), "AUDITSPHERE_NATIVE", null, null);
        }).ToArray();
      var byJournal = rows.GroupBy(x => x.JournalId).ToDictionary(x => x.Key, x => x.ToArray());
      if (rows.Length is < 1 or > 250_000 || rows.Select(x => x.LineId).Distinct().Count() != rows.Length ||
          selected.Values.Any(x => x.FirmId != actor.FirmId || x.ClientId != request.ClientId || x.PeriodId != period.Id ||
            x.Status != "POSTED" || x.Currency != tb.Currency || x.PostingDate < period.StartDate || x.PostingSequence > highwater) ||
          selected.Keys.Any(id => !byJournal.TryGetValue(id, out var group) || group.Length < 2) ||
          rows.GroupBy(x => x.JournalId).Any(g => g.Sum(x => Amount(x.Debit)) != g.Sum(x => Amount(x.Credit))))
        return Fail<Capture>(ErrorCodes.ProtectedState, "The exact pinned native journal population does not reproduce balanced posted source facts.");
      var json = JsonSerializer.Serialize(rows);
      var manifest = new NativeManifest(Version, actor.FirmId, request.ClientId, request.EngagementId, tb.Id, period.Id,
        tb.Sha256Hex, evidence.ManifestHash, evidence.ManifestJson, cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        tb.Currency, tb.LegalEntityKey, BookScope, highwater,
        new(opening.GetProperty("Id").GetGuid(), opening.GetProperty("ManifestSha256").GetString()!),
        selected.Count, rows.Length, rows.Sum(x => Amount(x.Debit)), rows.Sum(x => Amount(x.Credit)), HashText(json), request.EvidenceReference, Unavailable);
      var manifestJson = JsonSerializer.Serialize(manifest);
      if (Encoding.UTF8.GetByteCount(json) > 32 * 1024 * 1024 || Encoding.UTF8.GetByteCount(manifestJson) > 16 * 1024 * 1024)
        return Fail<Capture>(ErrorCodes.GateBlocked, "The native GL artifact or manifest exceeds the retained evidence bound.");
      var identity = Identity(manifest); var manifestHash = HashText(manifestJson);
      return CommandResult<Capture>.Ok(new(manifest, manifestJson, json, rows,
        new(Basis(Purpose(actor, request), identity, manifestHash), tb.Id, period.Id, manifest.AsOfDate, tb.Currency,
          tb.LegalEntityKey, BookScope, SourceImportOrigins.NativeLedger, identity, manifestHash, manifest.GeneratedRowsHash,
          selected.Count, rows.Length, Money(manifest.TotalDebit), Money(manifest.TotalCredit),
          highwater.ToString(CultureInfo.InvariantCulture), manifest.NativeOpening, Unavailable, rows)));
    }
    catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
    { return Fail<Capture>(ErrorCodes.ProtectedState, "The retained native TB membership is malformed or unsupported."); }
  }
}
