using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private const int CarrySourceBytes = 16 * 1024 * 1024;
  private sealed class CarrySourceException(string message) : Exception(message) { }
  private sealed record CarryControl(NativeSuccessorControlProof Proof, ClientAccount Account,
    ClientAccountRoleConfiguration Configuration, ClientAccountRoleDecision Decision);
  private sealed record CarryJournal(ClientOperationalJournal Journal, ClientOperationalJournalLine[] Lines,
    ClientOperationalJournalSnapshot Snapshot, ClientOperationalJournalDecision Decision,
    ClientOperationalPostingReceipt Receipt);
  private sealed class CarryContext(IClientAccountingDbContext db, Guid firm, Guid client, string currency,
    IReadOnlyList<CarryControl> controls)
  {
    internal readonly IClientAccountingDbContext Db = db;
    internal readonly Guid Firm = firm;
    internal readonly Guid Client = client;
    internal readonly string Currency = currency;
    internal readonly IReadOnlyList<CarryControl> Controls = controls;
    internal readonly Dictionary<Guid, CarryJournal> Journals = [];
    internal readonly Dictionary<Guid, (ClientOperationalOpeningBalance Row, Manifest Manifest, Dictionary<Guid, ManifestOpenItem> Items)> Openings = [];
    internal readonly Dictionary<Guid, CarryAncestry> Ancestries = [];
  }

  internal static string SuccessorCarriedPopulationHash(IReadOnlyList<SuccessorCarriedItem> items,
    string controlStateHash)
  {
    var orderedItems = items.OrderBy(x => x.OriginalKind, StringComparer.Ordinal).ThenBy(x => x.OriginalOpenItemId).ToArray();
    return SuccessorHash(new { Version = SuccessorCarryVersion, ControlStateHash = controlStateHash, Items = orderedItems });
  }

  /// <summary>Resolves original unallocated invoice identities; allocation eligibility belongs to its own owner.</summary>
  internal static async Task<CommandResult<SuccessorCarriedPopulation?>> ResolveSuccessorCarriedItemsAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, NativeTrialBalanceSourceWorkspace.SuccessorSource source,
    IReadOnlyList<NativeSuccessorControlProof> controls, CancellationToken ct)
  {
    try
    {
      CarryRequire(source.Dataset.FirmId == firmId && source.Dataset.ClientId == clientId &&
        source.Period.FirmId == firmId && source.Period.ClientId == clientId &&
        source.Dataset.PeriodId == source.Period.Id && source.Dataset.Currency == source.Period.Currency &&
        source.Period.Currency.Length == 3 && source.Period.Currency.All(x => x is >= 'A' and <= 'Z') &&
        source.Dataset.SourceKind == "Native" && source.Dataset.ImportState == "SEALED" && source.Dataset.ValidationStatus == "Accepted" &&
        source.Evidence.DatasetId == source.Dataset.Id && source.Evidence.FirmId == firmId && source.Evidence.ClientId == clientId &&
        source.Evidence.EngagementId == source.Dataset.EngagementId && source.Dataset.Sha256Hex == source.Evidence.SourceIdentityHash &&
        SuccessorHash(source.Evidence.ManifestJson) == source.Evidence.ManifestHash &&
        SuccessorHash(source.Evidence.GeneratedRowsJson) == source.Evidence.GeneratedRowsHash &&
        JsonSerializer.Serialize(source.TrialBalance.Rows) == source.Evidence.GeneratedRowsJson,
        "The carried population requires the exact scoped native predecessor.");
      CarryRequire(Encoding.UTF8.GetByteCount(source.MembershipJson) <= CarrySourceBytes,
        "The native source membership exceeds its supported byte bound.");
      using var sourceManifest = JsonDocument.Parse(source.Evidence.ManifestJson);
      CarryRequire(sourceManifest.RootElement.GetProperty("Pins").GetProperty("MembershipJson").GetString() == source.MembershipJson &&
        sourceManifest.RootElement.GetProperty("PeriodId").GetGuid() == source.Period.Id &&
        sourceManifest.RootElement.GetProperty("AsOfDate").GetString() == source.Period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) &&
        !sourceManifest.RootElement.GetProperty("ActivityOnly").GetBoolean(), "The carried population requires the exact complete accepted artifact.");
      var acceptance = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == source.AcceptanceId &&
        x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == source.Dataset.EngagementId &&
        x.TrialBalanceDatasetId == source.Dataset.Id, ct);
      CarryRequire(acceptance is not null && acceptance.Decision == "ACCEPTED" &&
        acceptance.SourceIdentityHash == source.Dataset.Sha256Hex && acceptance.AcceptedByUserId != source.Dataset.ImportedByUserId &&
        SuccessorHash(acceptance) == source.AcceptanceHash, "The exact independent native source acceptance is missing.");
      using var document = JsonDocument.Parse(source.MembershipJson);
      var membership = document.RootElement;
      var resolvedControls = await CarryControlsAsync(db, firmId, clientId, source, controls, ct);
      var context = new CarryContext(db, firmId, clientId, source.Period.Currency, resolvedControls);
      var items = new List<SuccessorCarriedItem>();

      // These are exact members of this accepted source, never all historical client documents.
      foreach (var name in new[] { "SalesCredits", "PurchaseCredits", "Settlements" })
        CarryRequire(CarryArray(membership, name).Length == 0,
          "Credits and settlements in the predecessor require a separately supported carried population.");
      var journalPins = CarryArray(membership, "Journals");
      var journalIds = journalPins.Select(x => x.GetProperty("Id").GetGuid()).ToArray();
      CarryRequire(journalIds.Length <= 250_000 && journalIds.Distinct().Count() == journalIds.Length,
        "Native journal membership is duplicate or over-bound.");
      var journals = await db.ClientOperationalJournals.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && x.PeriodId == source.Period.Id && x.Status == "POSTED")
        .OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
      var journalSet = journalIds.ToHashSet();
      var journalPinById = journalPins.ToDictionary(x => x.GetProperty("Id").GetGuid());
      CarryRequire(journals.Count == journalIds.Length && journals.All(x => journalSet.Contains(x.Id)),
        "The posted predecessor journal population differs from its accepted membership.");
      var sourceLines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && journalIds.Contains(x.JournalId)).Take(250_001).ToArrayAsync(ct);
      var allLinePins = CarryArray(membership, "Lines");
      var linePinById = allLinePins.ToDictionary(x => x.GetProperty("Id").GetGuid());
      CarryRequire(sourceLines.Length <= 250_000 && sourceLines.Length == allLinePins.Length &&
        sourceLines.All(x => linePinById.TryGetValue(x.Id, out var pin) && CarryEntityMatches(pin, x)) &&
        allLinePins.All(x => journalSet.Contains(x.GetProperty("JournalId").GetGuid())),
        "The complete native line population differs from accepted membership.");
      foreach (var journal in journals)
      {
        CarryRequire(CarryEntityMatches(journalPinById[journal.Id], journal) && journal.PostingDate >= source.Period.StartDate &&
          journal.PostingDate <= source.Period.EndDate,
          "A posted journal no longer matches its accepted source identity.");
      }
      var relevantIds = sourceLines.Where(x => resolvedControls.Any(c => c.Account.Id == x.ClientAccountId))
        .Select(x => x.JournalId).Concat(CarryArray(membership, "Sales").Select(x => x.GetProperty("JournalId").GetGuid()))
        .Concat(CarryArray(membership, "Purchases").Select(x => x.GetProperty("JournalId").GetGuid())).Distinct().ToArray();
      CarryRequire(relevantIds.Length <= SuccessorCarryLimit && relevantIds.All(journalSet.Contains),
        "The control-origin population is missing a journal or exceeds 5,000.");
      var snapshotPins = CarryArray(membership, "Submissions").ToDictionary(x => x.GetProperty("Id").GetGuid());
      var decisionPins = CarryArray(membership, "Decisions").ToDictionary(x => x.GetProperty("Id").GetGuid());
      var receiptPins = CarryArray(membership, "Receipts").ToDictionary(x => x.GetProperty("Id").GetGuid());
      foreach (var id in relevantIds)
      {
        var evidence = await CarryJournalAsync(context, id, ct);
        CarryRequire(CarryEntityMatches(journalPinById[id], evidence.Journal) && evidence.Lines.All(x =>
          linePinById.TryGetValue(x.Id, out var linePin) && CarryEntityMatches(linePin, x)) &&
          snapshotPins.TryGetValue(evidence.Snapshot.Id, out var snapshotPin) &&
          snapshotPin.GetProperty("JournalId").GetGuid() == id &&
          snapshotPin.GetProperty("JournalRevision").GetInt64() == evidence.Snapshot.JournalRevision &&
          snapshotPin.GetProperty("CaptureKind").GetString() == evidence.Snapshot.CaptureKind &&
          snapshotPin.GetProperty("SnapshotHash").GetString() == SuccessorHash(evidence.Snapshot.SnapshotJson) &&
          decisionPins.TryGetValue(evidence.Decision.Id, out var decisionPin) && CarryEntityMatches(decisionPin, evidence.Decision) &&
          receiptPins.TryGetValue(evidence.Receipt.Id, out var receiptPin) && CarryEntityMatches(receiptPin, evidence.Receipt),
          "The exact journal submission, independent posting decision or receipt is absent from source membership.");
      }
      CarryRequire(!await db.ClientSalesCreditNoteOpenItems.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
        x.ClientId == clientId && journalIds.Contains(x.JournalId), ct) &&
        !await db.ClientPurchaseCreditNoteOpenItems.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
          x.ClientId == clientId && journalIds.Contains(x.JournalId), ct) &&
        !await db.ClientManualSettlementOrigins.AsNoTracking().AnyAsync(x => x.FirmId == firmId &&
          x.ClientId == clientId && journalIds.Contains(x.JournalId), ct),
        "The predecessor contains unsupported credit or settlement origins.");

      var sales = await db.ClientSalesInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && journalIds.Contains(x.JournalId)).OrderBy(x => x.Id).Take(SuccessorCarryLimit + 1).ToListAsync(ct);
      var purchases = await db.ClientPurchaseInvoiceSubmissions.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && journalIds.Contains(x.JournalId)).OrderBy(x => x.Id).Take(SuccessorCarryLimit + 1).ToListAsync(ct);
      CarryCheckOriginPins(CarryArray(membership, "Sales"), sales.Select(x => (x.Id, x.JournalId, SuccessorHash(x))).ToArray());
      CarryCheckOriginPins(CarryArray(membership, "Purchases"), purchases.Select(x => (x.Id, x.JournalId, SuccessorHash(x))).ToArray());
      var salesItems = await db.ClientSalesInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && journalIds.Contains(x.JournalId)).Take(SuccessorCarryLimit + 1).ToListAsync(ct);
      var purchaseItems = await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == clientId && journalIds.Contains(x.JournalId)).Take(SuccessorCarryLimit + 1).ToListAsync(ct);
      CarryRequire(salesItems.Count + purchaseItems.Count <= SuccessorCarryLimit,
        "The original invoice population exceeds 5,000 items.");
      foreach (var submission in sales)
      {
        CarryRequire(SuccessorHash(submission.ManifestJson) == submission.ManifestHash,
          "A sales submission manifest failed integrity.");
        var journal = context.Journals[submission.JournalId].Journal;
        if (submission.JournalSubmittedRevision == journal.Revision - 1)
        {
          var item = salesItems.SingleOrDefault(x => x.SubmissionId == submission.Id);
          CarryRequire(item is not null, "A posted sales submission has no original open item.");
          items.Add(await CarrySalesAsync(context, item!, ct));
        }
        else
          CarryRequire(await db.ClientSalesInvoiceDecisions.AsNoTracking().CountAsync(x => x.FirmId == firmId &&
            x.ClientId == clientId && x.SubmissionId == submission.Id && x.Decision == "RETURN", ct) == 1 &&
            salesItems.All(x => x.SubmissionId != submission.Id), "An earlier sales submission has ambiguous posted lineage.");
      }
      foreach (var submission in purchases)
      {
        CarryRequire(SuccessorHash(submission.ManifestJson) == submission.ManifestHash,
          "A purchase submission manifest failed integrity.");
        var journal = context.Journals[submission.JournalId].Journal;
        if (submission.JournalSubmittedRevision == journal.Revision - 1)
        {
          var item = purchaseItems.SingleOrDefault(x => x.SubmissionId == submission.Id);
          CarryRequire(item is not null, "A posted purchase submission has no original open item.");
          items.Add(await CarryPurchaseAsync(context, item!, ct));
        }
        else
          CarryRequire(await db.ClientPurchaseInvoiceDecisions.AsNoTracking().CountAsync(x => x.FirmId == firmId &&
            x.ClientId == clientId && x.SubmissionId == submission.Id && x.Decision == "RETURN", ct) == 1 &&
            purchaseItems.All(x => x.SubmissionId != submission.Id), "An earlier purchase submission has ambiguous posted lineage.");
      }
      CarryRequire(salesItems.Count == items.Count(x => x.OriginalKind == "SALES_INVOICE") &&
        purchaseItems.Count == items.Count(x => x.OriginalKind == "PURCHASE_INVOICE"),
        "An original invoice open item has missing, duplicate or orphan posting lineage.");
      foreach (var journal in context.Journals.Values)
      {
        var controlLines = journal.Lines.Where(x => resolvedControls.Any(c => c.Account.Id == x.ClientAccountId)).ToArray();
        var owners = items.Where(x => x.JournalId == journal.Journal.Id).ToArray();
        CarryRequire(controlLines.Length == 0 && owners.Length == 0 || controlLines.Length == 1 && owners.Length == 1,
          "Every control journal must belong to exactly one supported original invoice.");
      }

      var openingPin = membership.GetProperty("Opening");
      CarryRequire(openingPin.ValueKind == JsonValueKind.Object, "The complete predecessor requires its exact approved opening.");
      var openingId = openingPin.GetProperty("Id").GetGuid();
      var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.Id == openingId && x.PeriodId == source.Period.Id, ct);
      CarryRequire(opening is not null && ValidManifest(opening) && opening.ApprovedByUserId.HasValue &&
        opening.ApprovedAt.HasValue && opening.ApprovedByUserId != opening.CreatedByUserId &&
        opening.Currency == context.Currency && opening.AsOfDate == source.Period.StartDate &&
        openingPin.GetProperty("ManifestSha256").GetString() == opening.ManifestSha256 &&
        openingPin.GetProperty("EvidenceSha256").GetString() == opening.EvidenceSha256 &&
        openingPin.GetProperty("PeriodRevision").GetInt64() == opening.PeriodRevision &&
        openingPin.GetProperty("ApprovedByUserId").GetGuid() == opening.ApprovedByUserId &&
        openingPin.GetProperty("ApprovedAt").GetDateTimeOffset() == opening.ApprovedAt,
        "The original predecessor opening is unavailable or differs from accepted membership.");
      var openingItems = await CarryOpeningAsync(context, opening!, membership, ct);
      items.AddRange(openingItems);
      CarryRequire(items.Count <= SuccessorCarryLimit && items.Select(x => (x.OriginalKind, x.OriginalOpenItemId)).Distinct().Count() == items.Count &&
        items.Select(x => x.OriginalOpenItemId).Distinct().Count() == items.Count,
        "The carried originals contain duplicate identities or exceed 5,000 items.");
      foreach (var control in resolvedControls)
      {
        var total = items.Where(x => x.Role == control.Proof.Role && x.AccountId == control.Account.Id)
          .Sum(x => CarryAmount(x.BaselineOpenAmount));
        var closing = CarrySigned(control.Proof.Role, CarryAmount(control.Proof.ClosingDebit), CarryAmount(control.Proof.ClosingCredit));
        CarryRequire(closing >= 0m && total == closing && (total == 0m || closing > 0m),
          "Every nonzero AR/AP control balance must equal its exact positive original invoice population.");
      }
      if (items.Count == 0) return CommandResult<SuccessorCarriedPopulation?>.Ok(null);
      var ordered = items.OrderBy(x => x.OriginalKind, StringComparer.Ordinal).ThenBy(x => x.OriginalOpenItemId).ToArray();
      var controlStateJson = JsonSerializer.Serialize(resolvedControls.OrderBy(x => x.Proof.Role, StringComparer.Ordinal).Select(x => new
      {
        x.Proof.Role, x.Proof.ConfigurationId, x.Proof.DecisionId, x.Proof.AccountId,
        x.Proof.ClosingDebit, x.Proof.ClosingCredit,
        AccountHash = SuccessorHash(x.Account), ConfigurationHash = SuccessorHash(x.Configuration), DecisionHash = SuccessorHash(x.Decision),
        AccountJson = JsonSerializer.Serialize(x.Account), ConfigurationJson = JsonSerializer.Serialize(x.Configuration),
        DecisionJson = JsonSerializer.Serialize(x.Decision)
      }).ToArray());
      var controlHash = SuccessorHash(controlStateJson);
      CarryRequire(Encoding.UTF8.GetByteCount(controlStateJson) <= 512 * 1024, "The complete control evidence exceeds its supported byte bound.");
      CarryRequire(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(ordered)) <= CarrySourceBytes,
        "The complete carried population exceeds its byte bound; no items were truncated.");
      return CommandResult<SuccessorCarriedPopulation?>.Ok(new(SuccessorCarryVersion, ordered.Length,
        SuccessorCarriedPopulationHash(ordered, controlHash), controlHash, ordered, controlStateJson));
    }
    catch (Exception ex) when (ex is CarrySourceException or JsonException or InvalidOperationException or
      ArgumentException or FormatException or OverflowException or KeyNotFoundException or NullReferenceException)
    {
      return CommandResult<SuccessorCarriedPopulation?>.Fail(ErrorCodes.GateBlocked,
        ex is CarrySourceException ? ex.Message : "Malformed, incomplete or ambiguous carried source evidence.");
    }
  }

  private static void CarryRequire([DoesNotReturnIf(false)] bool valid, string message)
  {
    if (!valid) throw new CarrySourceException(message);
  }
  private static decimal CarryAmount(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
  private static string CarryExact(decimal value) => value.ToString("F6", CultureInfo.InvariantCulture);
  private static decimal CarrySigned(string role, decimal debit, decimal credit) => role == "AR" ? debit - credit : credit - debit;
  private static JsonElement[] CarryArray(JsonElement root, string name)
  {
    var value = root.GetProperty(name);
    CarryRequire(value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= 250_000, "Unsupported native membership array.");
    return value.EnumerateArray().ToArray();
  }
  private static bool CarryEntityMatches<T>(JsonElement pin, T actual) => pin.ValueKind == JsonValueKind.Object &&
    JsonSerializer.Serialize(pin.Deserialize<T>()) == JsonSerializer.Serialize(actual);
  private static void CarryCheckOriginPins(JsonElement[] pins, (Guid Id, Guid JournalId, string Hash)[] rows)
  {
    CarryRequire(rows.Length <= SuccessorCarryLimit && pins.Length == rows.Length &&
      pins.Select(x => x.GetProperty("Id").GetGuid()).Distinct().Count() == pins.Length && rows.All(x => pins.Any(p =>
        p.GetProperty("Id").GetGuid() == x.Id && p.GetProperty("JournalId").GetGuid() == x.JournalId &&
        p.GetProperty("SourceHash").GetString() == x.Hash)), "Invoice origin membership differs from the accepted source or exceeds its bound.");
  }

  private static async Task<List<CarryControl>> CarryControlsAsync(IClientAccountingDbContext db, Guid firm, Guid client,
    NativeTrialBalanceSourceWorkspace.SuccessorSource source, IReadOnlyList<NativeSuccessorControlProof> controls, CancellationToken ct)
  {
    CarryRequire(controls.Count == 2 && controls.Count(x => x.Role == "AR") == 1 && controls.Count(x => x.Role == "AP") == 1 &&
      controls.Select(x => x.AccountId).Distinct().Count() == 2, "Exact distinct AR/AP controls are required.");
    var approvedControls = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { role.FirmId, role.ClientId, Id = role.Id }
        equals new { decision.FirmId, decision.ClientId, Id = decision.ConfigurationId }
      where role.FirmId == firm && role.ClientId == client && (role.Role == "AR" || role.Role == "AP") &&
        decision.Decision == "APPROVE" && role.EffectiveFrom <= source.Period.EndDate &&
        (role.EffectiveTo == null || role.EffectiveTo >= source.Period.StartDate)
      select new { ConfigurationId = role.Id, DecisionId = decision.Id }).Take(3).ToArrayAsync(ct);
    CarryRequire(approvedControls.Length == controls.Count && approvedControls.All(x => controls.Any(y =>
      y.ConfigurationId == x.ConfigurationId && y.DecisionId == x.DecisionId)),
      "The predecessor has an unexplained or overlapping approved AR/AP control.");
    using var membership = JsonDocument.Parse(source.MembershipJson);
    var accountPins = CarryArray(membership.RootElement, "Accounts").ToDictionary(x => x.GetProperty("Id").GetGuid());
    var result = new List<CarryControl>();
    foreach (var proof in controls)
    {
      var account = await db.ClientAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firm && x.ClientId == client && x.Id == proof.AccountId, ct);
      var role = await db.ClientAccountRoleConfigurations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firm && x.ClientId == client && x.Id == proof.ConfigurationId, ct);
      var decision = await db.ClientAccountRoleDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firm && x.ClientId == client && x.Id == proof.DecisionId, ct);
      CarryRequire(account is not null && role is not null && decision is not null && account.IsPosting && account.Status == "ACTIVE" &&
        account.AccountType == (proof.Role == "AR" ? "ASSET" : "LIABILITY") && role.Role == proof.Role &&
        role.AccountId == account.Id && role.ChartVersionId == account.ChartVersionId && decision.ConfigurationId == role.Id &&
        decision.Decision == "APPROVE" && decision.ReviewedByUserId != role.ProposedByUserId &&
        role.EffectiveFrom <= source.Period.StartDate && (role.EffectiveTo == null || role.EffectiveTo >= source.Period.EndDate),
        "The original active control account and independent role approval must remain exact.");
      CarryRequire(accountPins.TryGetValue(account.Id, out var accountPin) && CarryEntityMatches(accountPin, account) &&
        ClientNativeAmountRules.ValidStoredAmount(CarryAmount(proof.ClosingDebit)) &&
        ClientNativeAmountRules.ValidStoredAmount(CarryAmount(proof.ClosingCredit)),
        "The active control is absent from source account membership or has unsupported amounts.");
      var row = source.TrialBalance.Rows.SingleOrDefault(x => x.AccountId == proof.AccountId);
      CarryRequire(row is null ? CarryAmount(proof.ClosingDebit) == 0m && CarryAmount(proof.ClosingCredit) == 0m :
        row.AccountCode == account!.AccountCode && row.AccountName == account.AccountName &&
        CarryAmount(row.ClosingDebit) == CarryAmount(proof.ClosingDebit) && CarryAmount(row.ClosingCredit) == CarryAmount(proof.ClosingCredit),
        "The control proof differs from the actual accepted closing row.");
      result.Add(new(proof, account!, role!, decision!));
    }
    return result;
  }

  private static async Task<SuccessorCarriedItem[]> CarryOpeningAsync(CarryContext context,
    ClientOperationalOpeningBalance opening, JsonElement membership, CancellationToken ct)
  {
    var proof = await context.Db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == context.Firm && x.ClientId == context.Client && x.OpeningId == opening.Id, ct);
    SuccessorCarriedItem[] items;
    if (proof is null)
    {
      CarryRequire(!membership.TryGetProperty("OpeningSuccessorProof", out _), "The accepted opening successor proof is missing.");
      using var manifest = JsonDocument.Parse(opening.ManifestJson);
      var rows = manifest.RootElement.TryGetProperty("OpenItems", out var raw) && raw.ValueKind == JsonValueKind.Array
        ? raw.EnumerateArray().ToArray() : [];
      CarryRequire(rows.Length <= SuccessorCarryLimit, "Root opening items exceed 5,000.");
      var list = new List<SuccessorCarriedItem>();
      foreach (var row in rows) list.Add(await CarryManualItemAsync(context, opening, row.GetProperty("Id").GetGuid(), ct));
      items = list.ToArray();
    }
    else
    {
      var pin = membership.GetProperty("OpeningSuccessorProof");
      CarryRequire(pin.GetProperty("OpeningId").GetGuid() == opening.Id && pin.GetProperty("ProofHash").GetString() == proof.ProofHash,
        "The accepted predecessor carry proof changed.");
      var parsed = ParseSuccessorProof(opening, proof);
      CarryRequire(parsed.Succeeded, "The predecessor successor proof failed the parent proof parser.");
      var population = parsed.Value!.Facts.CarriedPopulation;
      if (population is null) items = [];
      else
      {
        CarryRequire(population is not null && population.Items is not null && population.Count == population.Items.Count &&
          population.Count is > 0 and <= SuccessorCarryLimit && population.PopulationHash ==
          SuccessorCarriedPopulationHash(population.Items, population.ControlStateHash), "The predecessor carried population failed integrity.");
        CarryRequire(Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(population!.Items)) <= CarrySourceBytes,
          "The predecessor carried population exceeds its byte bound.");
        var rebuilt = new List<SuccessorCarriedItem>();
        foreach (var item in population!.Items)
        {
          CarryRequire(item is not null && Encoding.UTF8.GetByteCount(item.SourceFactsJson) <= 64 * 1024 &&
            SuccessorHash(item.SourceFactsJson) == item.SourceFactsHash,
            "An inherited original source facts hash is invalid.");
          SuccessorCarriedItem actual;
          if (item!.OriginalKind is "OPENING_AR_INVOICE" or "OPENING_AP_INVOICE")
          {
            var original = context.Openings.TryGetValue(item.OriginalOpeningId ?? Guid.Empty, out var originalCached)
              ? originalCached.Row : await context.Db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
                x.FirmId == context.Firm && x.ClientId == context.Client && x.Id == item.OriginalOpeningId, ct);
            CarryRequire(original is not null, "The original root opening is missing.");
            actual = await CarryManualItemAsync(context, original!, item.OriginalOpenItemId, ct);
          }
          else if (item.OriginalKind == "SALES_INVOICE")
          {
            var original = await context.Db.ClientSalesInvoiceOpenItems.AsNoTracking().SingleOrDefaultAsync(x =>
              x.FirmId == context.Firm && x.ClientId == context.Client && x.Id == item.OriginalOpenItemId, ct);
            CarryRequire(original is not null, "The original sales item is missing.");
            actual = await CarrySalesAsync(context, original!, ct);
          }
          else if (item.OriginalKind == "PURCHASE_INVOICE")
          {
            var original = await context.Db.ClientPurchaseInvoiceOpenItems.AsNoTracking().SingleOrDefaultAsync(x =>
              x.FirmId == context.Firm && x.ClientId == context.Client && x.Id == item.OriginalOpenItemId, ct);
            CarryRequire(original is not null, "The original purchase item is missing.");
            actual = await CarryPurchaseAsync(context, original!, ct);
          }
          else throw new CarrySourceException("Unsupported inherited original item kind.");
          await CarryInheritedAnchorAsync(context, opening, actual, proof.EngagementId, ct);
          CarryRequire(JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(item),
            "The inherited original identity, amount, party or source facts changed.");
          rebuilt.Add(actual);
        }
        items = rebuilt.ToArray();
      }
    }
    using var openingManifest = JsonDocument.Parse(opening.ManifestJson);
    foreach (var control in context.Controls)
    {
      var lines = openingManifest.RootElement.GetProperty("Lines").EnumerateArray()
        .Where(x => x.GetProperty("AccountCode").GetString() == control.Account.AccountCode).ToArray();
      CarryRequire(lines.Length <= 1, "Duplicate root opening control rows.");
      if (lines.Length == 0)
      {
        CarryRequire(!items.Any(x => x.AccountId == control.Account.Id && x.Role == control.Proof.Role),
          "A carried item has no original opening control row.");
        continue;
      }
      var balance = CarrySigned(control.Proof.Role,
        CarryAmount(lines[0].GetProperty("Debit").GetString()!), CarryAmount(lines[0].GetProperty("Credit").GetString()!));
      CarryRequire(balance >= 0m && items.Where(x => x.AccountId == control.Account.Id && x.Role == control.Proof.Role)
        .Sum(x => CarryAmount(x.BaselineOpenAmount)) == balance,
        "Aggregate-only or opposite-side predecessor opening control balances cannot supply original invoice detail.");
    }
    return items;
  }
}
