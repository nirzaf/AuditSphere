using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private const int CarryAllocationSubmissionLimit = 5000;
  private const int CarryAllocationLineLimit = 50000;
  private sealed record CarryAllocationItem(Guid DocumentId, Guid CounterpartyId, string Currency,
    decimal OriginalAmount, DateOnly PostingDate, Guid? SubmissionId = null, Guid? JournalId = null,
    Guid? OpeningId = null, Guid? DraftId = null);

  // The caller owns scope authorization, proof validation and the snapshot/publication locks.
  // Later allocation activity is checked separately; it never changes the captured zero baseline or hash.
  internal static async Task<CommandResult> ValidateSuccessorCarryAllocationsAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, SuccessorCarriedPopulation population,
    DateOnly priorEnd, DateOnly successorStart, DateTimeOffset? originalProofCreatedAt, CancellationToken ct)
  {
    try
    {
      return await ValidateCarryAllocationHistoryAsync(db, firmId, clientId, population,
        priorEnd, successorStart, originalProofCreatedAt, ct);
    }
    catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or
      InvalidOperationException or ArgumentException or KeyNotFoundException)
    {
      return CarryAllocationBlocked("The carried allocation identities or retained evidence are malformed or ambiguous.");
    }
  }

  private static async Task<CommandResult> ValidateCarryAllocationHistoryAsync(
    IClientAccountingDbContext db, Guid firmId, Guid clientId, SuccessorCarriedPopulation population,
    DateOnly priorEnd, DateOnly successorStart, DateTimeOffset? proofCreatedAt, CancellationToken ct)
  {
    if (firmId == Guid.Empty || clientId == Guid.Empty || population is null || population.Items is null ||
        population.Version != SuccessorCarryVersion || population.Count != population.Items.Count ||
        population.Count > SuccessorCarryLimit || priorEnd == DateOnly.MaxValue || priorEnd.AddDays(1) != successorStart ||
        population.Items.Any(x => x is null || x.OriginalOpenItemId == Guid.Empty || x.OriginalDocumentId == Guid.Empty ||
          x.CounterpartyId == Guid.Empty || x.Role is not ("AR" or "AP") ||
          x.Role != (x.OriginalKind is "SALES_INVOICE" or "SALES_CREDIT" or "SALES_RECEIPT" or "OPENING_AR_INVOICE" ? "AR" : "AP") ||
          x.Currency.Length != 3 || x.Currency.Any(c => c is < 'A' or > 'Z') ||
          !CarryAllocationAmount(x.OriginalAmount, out var amount) || amount <= 0m ||
          !CarryAllocationAmount(x.BaselineAppliedAmount, out var applied) || applied != 0m ||
          !CarryAllocationAmount(x.BaselineOpenAmount, out var open) || open != amount ||
          CarryAllocationHash(x.SourceFactsJson) != x.SourceFactsHash))
      return CarryAllocationBlocked("Only the exact contiguous successor population with its original unallocated baseline is supported.");

    var members = population.Items.ToDictionary(x => (x.OriginalKind, x.OriginalOpenItemId));
    var sources = members.Keys.Where(x => CarryAllocationSourceKind(x.OriginalKind)).ToHashSet();
    var targets = members.Keys.Where(x => CarryAllocationTargetKind(x.OriginalKind)).ToHashSet();
    if (sources.Count + targets.Count != members.Count)
      return CarryAllocationBlocked("The carried population contains an unsupported original allocation identity.");
    var initialIds = await CarryAllocationSubmissionIdsAsync(db, firmId, clientId, sources, targets, ct);
    if (initialIds.Count > CarryAllocationSubmissionLimit)
      return CarryAllocationBlocked("The connected allocation history exceeds the supported submission window.");
    // Even returned, pending or uncertain intent is history. Net-zero effects do not prove no history.
    if (!proofCreatedAt.HasValue)
      return initialIds.Count == 0 ? CommandResult.Ok() : CarryAllocationBlocked(
        "A new successor capture requires no allocation history touching any original carried member, including pending or returned requests.");
    if (initialIds.Count == 0) return CommandResult.Ok();

    var submissions = new Dictionary<Guid, ClientOpenItemAllocationSubmission>();
    var lines = new Dictionary<Guid, ClientOpenItemAllocationLine>();
    var frontier = initialIds;
    while (frontier.Count > 0)
    {
      var ids = frontier.Where(x => !submissions.ContainsKey(x)).ToArray();
      if (ids.Length == 0) break;
      if (submissions.Count + ids.Length > CarryAllocationSubmissionLimit)
        return CarryAllocationBlocked("The connected allocation history exceeds the supported submission window.");
      var batch = await db.ClientOpenItemAllocationSubmissions.AsNoTracking().Where(x =>
        x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.Id)).ToListAsync(ct);
      if (batch.Count != ids.Length)
        return CarryAllocationBlocked("An allocation target line has no matching scoped submission.");
      var batchLines = await db.ClientOpenItemAllocationLines.AsNoTracking().Where(x =>
          x.FirmId == firmId && x.ClientId == clientId && ids.Contains(x.SubmissionId))
        .OrderBy(x => x.Id).Take(CarryAllocationLineLimit - lines.Count + 1).ToListAsync(ct);
      if (lines.Count + batchLines.Count > CarryAllocationLineLimit)
        return CarryAllocationBlocked("The connected allocation history exceeds the supported line window.");
      var newSources = new HashSet<(string, Guid)>();
      var newTargets = new HashSet<(string, Guid)>();
      foreach (var submission in batch)
      {
        submissions.Add(submission.Id, submission);
        if (!CarryAllocationSourceKind(submission.SourceKind))
          return CarryAllocationBlocked("An allocation has unsupported source history.");
        if (sources.Add((submission.SourceKind, submission.SourceItemId)))
          newSources.Add((submission.SourceKind, submission.SourceItemId));
      }
      foreach (var line in batchLines)
      {
        lines.Add(line.Id, line);
        if (!CarryAllocationTargetKind(line.TargetKind))
          return CarryAllocationBlocked("An allocation has unsupported target history.");
        if (targets.Add((line.TargetKind, line.TargetOpenItemId)))
          newTargets.Add((line.TargetKind, line.TargetOpenItemId));
      }
      // Include every effect on the connected sources/targets, rather than checking only a selected line.
      frontier = await CarryAllocationSubmissionIdsAsync(db, firmId, clientId, newSources, newTargets, ct);
      if (frontier.Count > CarryAllocationSubmissionLimit)
        return CarryAllocationBlocked("The connected allocation history exceeds the supported submission window.");
    }

    var submissionIds = submissions.Keys.ToArray();
    var decisions = await db.ClientOpenItemAllocationDecisions.AsNoTracking().Where(x =>
        x.FirmId == firmId && x.ClientId == clientId && submissionIds.Contains(x.SubmissionId))
      .OrderBy(x => x.Id).Take(submissions.Count + 1).ToListAsync(ct);
    if (decisions.Count > submissions.Count || decisions.GroupBy(x => x.SubmissionId).Any(x => x.Count() != 1))
      return CarryAllocationBlocked("Allocation decision history is ambiguous.");
    var decisionBySubmission = decisions.ToDictionary(x => x.SubmissionId);
    var sourceFacts = new Dictionary<(string, Guid), CarryAllocationItem>();
    var targetFacts = new Dictionary<(string, Guid), CarryAllocationItem>();
    var linesBySubmission = lines.Values.GroupBy(x => x.SubmissionId).ToDictionary(x => x.Key,
      x => x.OrderBy(y => y.LineNumber).ToArray());
    foreach (var submission in submissions.Values)
    {
      if (!linesBySubmission.TryGetValue(submission.Id, out var submittedLines) || submittedLines.Length is < 1 or > 100 ||
          !CarryAllocationManifestMatches(submission, submittedLines))
        return CarryAllocationBlocked("Allocation manifest or exact target membership failed integrity validation.");
      var touchesOriginal = members.ContainsKey((submission.SourceKind, submission.SourceItemId)) ||
        submittedLines.Any(x => members.ContainsKey((x.TargetKind, x.TargetOpenItemId)));
      // This timestamp establishes causal history only. The economic gate uses actual source posting dates below.
      if (touchesOriginal && submission.CreatedAt < proofCreatedAt.Value)
        return CarryAllocationBlocked("The retained zero baseline is contradicted by an earlier original-member allocation request.");
      var sourceKey = (submission.SourceKind, submission.SourceItemId);
      if (!sourceFacts.TryGetValue(sourceKey, out var source))
      {
        var resolvedSource = await ResolveCarryAllocationItemAsync(db, firmId, clientId,
          sourceKey.SourceKind, sourceKey.SourceItemId, null, ct);
        if (resolvedSource is null || resolvedSource.PostingDate < successorStart)
          return CarryAllocationBlocked("Later consumption needs a genuine posted successor-period source; predecessor or unknown source history is unsupported.");
        source = resolvedSource;
        sourceFacts.Add(sourceKey, source);
      }
      if (source.OriginalAmount != submission.SourceAmount || source.CounterpartyId != submission.CounterpartyId ||
          source.Currency != submission.Currency)
        return CarryAllocationBlocked("Allocation source identity, party, currency or original amount changed.");
      foreach (var line in submittedLines)
      {
        var targetKey = (line.TargetKind, line.TargetOpenItemId);
        if (!targetFacts.TryGetValue(targetKey, out var target))
        {
          members.TryGetValue(targetKey, out var carried);
          var resolvedTarget = await ResolveCarryAllocationItemAsync(db, firmId, clientId,
            line.TargetKind, line.TargetOpenItemId, carried, ct);
          if (resolvedTarget is null || carried is null && resolvedTarget.PostingDate < successorStart ||
              carried is not null && (resolvedTarget.PostingDate > priorEnd || !CarryAllocationOriginalMatches(carried, resolvedTarget)))
            return CarryAllocationBlocked("A target is not the exact carried original or a supported posted successor invoice.");
          target = resolvedTarget;
          targetFacts.Add(targetKey, target);
        }
        if (target.CounterpartyId != source.CounterpartyId || target.Currency != source.Currency ||
            (submission.SourceKind is "SALES_CREDIT" or "SALES_RECEIPT") !=
            (line.TargetKind is "SALES_INVOICE" or "OPENING_AR_INVOICE"))
          return CarryAllocationBlocked("Allocation source and target must retain matching party, role and currency.");
      }
      if (decisionBySubmission.TryGetValue(submission.Id, out var decision) &&
          (decision.Decision is not ("APPROVE" or "RETURN") || decision.ActorUserId == Guid.Empty ||
           decision.ActorUserId == submission.CreatedByUserId || decision.CommandId == Guid.Empty ||
           decision.CreatedAt < submission.CreatedAt || decision.PreviewDigest != submission.PreviewDigest ||
           decision.IntentHash != CarryAllocationHash($"{submission.IntentHash}|{decision.Decision}|{decision.Reason}") ||
           string.IsNullOrWhiteSpace(decision.Reason) ||
           decision.ReviewContextJson != JsonSerializer.Serialize(new { submission.SourceKind, submission.SourceItemId,
             submission.Disposition, lines = submittedLines.ToList(), decision = decision.Decision, reason = decision.Reason })))
        return CarryAllocationBlocked("The exact independent allocation decision does not match its retained source and lines.");
    }

    var reversed = new Dictionary<Guid, decimal>();
    foreach (var line in lines.Values)
    {
      var submission = submissions[line.SubmissionId];
      if (submission.Disposition == "ALLOCATE")
      {
        if (line.ReversesAllocationLineId is not null)
          return CarryAllocationBlocked("An original allocation cannot reverse another line.");
        continue;
      }
      if (line.ReversesAllocationLineId is not { } originalId || !lines.TryGetValue(originalId, out var original) ||
          original.ReversesAllocationLineId is not null || original.TargetKind != line.TargetKind ||
          original.TargetOpenItemId != line.TargetOpenItemId || line.Amount > original.Amount ||
          !submissions.TryGetValue(original.SubmissionId, out var originalSubmission) ||
          originalSubmission.Disposition != "ALLOCATE" || originalSubmission.SourceKind != submission.SourceKind ||
          originalSubmission.SourceItemId != submission.SourceItemId ||
          !decisionBySubmission.TryGetValue(original.SubmissionId, out var originalDecision) ||
          originalDecision.Decision != "APPROVE" || originalDecision.CreatedAt > submission.CreatedAt)
        return CarryAllocationBlocked("Unallocation must identify the exact independently approved original source and target line.");
      if (decisionBySubmission.TryGetValue(submission.Id, out var reversalDecision) && reversalDecision.Decision == "APPROVE")
      {
        reversed[originalId] = reversed.GetValueOrDefault(originalId) + line.Amount;
        if (reversed[originalId] > original.Amount)
          return CarryAllocationBlocked("Approved unallocations exceed the exact original line amount.");
      }
    }

    var sourceApplied = new Dictionary<(string, Guid), decimal>();
    var targetApplied = new Dictionary<(string, Guid), decimal>();
    // Pending/returned requests have no approved effect. Equal persisted timestamps form one causal group;
    // timestamps do not assign an accounting-effective date or rewrite the captured opening baseline.
    foreach (var group in decisions.Where(x => x.Decision == "APPROVE").GroupBy(x => x.CreatedAt).OrderBy(x => x.Key))
    {
      foreach (var decision in group)
      {
        var submission = submissions[decision.SubmissionId];
        var sign = submission.Disposition == "ALLOCATE" ? 1m : -1m;
        var sourceKey = (submission.SourceKind, submission.SourceItemId);
        foreach (var line in linesBySubmission[submission.Id])
        {
          var targetKey = (line.TargetKind, line.TargetOpenItemId);
          sourceApplied[sourceKey] = sourceApplied.GetValueOrDefault(sourceKey) + sign * line.Amount;
          targetApplied[targetKey] = targetApplied.GetValueOrDefault(targetKey) + sign * line.Amount;
        }
      }
      if (sourceApplied.Any(x => x.Value < 0m || x.Value > sourceFacts[x.Key].OriginalAmount) ||
          targetApplied.Any(x => x.Value < 0m || x.Value > targetFacts[x.Key].OriginalAmount))
        return CarryAllocationBlocked("Approved allocation history exceeds a genuine source or original target balance.");
    }
    return CommandResult.Ok();
  }

  private static async Task<List<Guid>> CarryAllocationSubmissionIdsAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, IEnumerable<(string Kind, Guid Id)> sourceKeys,
    IEnumerable<(string Kind, Guid Id)> targetKeys, CancellationToken ct)
  {
    var scoped = db.ClientOpenItemAllocationSubmissions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId);
    IQueryable<Guid> query = scoped.Where(x => false).Select(x => x.Id);
    foreach (var group in sourceKeys.GroupBy(x => x.Kind))
    {
      var kind = group.Key; var ids = group.Select(x => x.Id).ToArray();
      query = query.Union(scoped.Where(x => x.SourceKind == kind && ids.Contains(x.SourceItemId)).Select(x => x.Id));
    }
    foreach (var group in targetKeys.GroupBy(x => x.Kind))
    {
      var kind = group.Key; var ids = group.Select(x => x.Id).ToArray();
      query = query.Union(db.ClientOpenItemAllocationLines.AsNoTracking().Where(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.TargetKind == kind && ids.Contains(x.TargetOpenItemId))
        .Select(x => x.SubmissionId));
    }
    return await query.Distinct().OrderBy(x => x).Take(CarryAllocationSubmissionLimit + 1).ToListAsync(ct);
  }

  private static bool CarryAllocationManifestMatches(ClientOpenItemAllocationSubmission submission,
    IReadOnlyList<ClientOpenItemAllocationLine> lines)
  {
    if (submission.Disposition is not ("ALLOCATE" or "UNALLOCATE") || submission.CreatedByUserId == Guid.Empty ||
        submission.CommandId == Guid.Empty || submission.SourceAmount <= 0m ||
        string.IsNullOrWhiteSpace(submission.Reference) || string.IsNullOrWhiteSpace(submission.Reason) ||
        !IsSha256(submission.IntentHash) || !IsSha256(submission.PreviewDigest) ||
        Encoding.UTF8.GetByteCount(submission.ManifestJson) > 256 * 1024 ||
        CarryAllocationHash(submission.ManifestJson) != submission.ManifestHash ||
        lines.Any(x => x.Amount <= 0m || decimal.Round(x.Amount, 6) != x.Amount || x.LineNumber <= 0) ||
        lines.Select(x => x.LineNumber).Distinct().Count() != lines.Count ||
        lines.Select(x => (x.TargetKind, x.TargetOpenItemId)).Distinct().Count() != lines.Count)
      return false;
    var manifest = JsonSerializer.Deserialize<ClientOpenItemAllocationPreview>(submission.ManifestJson);
    if (manifest is null || manifest.SubmissionId != Guid.Empty || manifest.Lines is null ||
        manifest.TargetAvailableAmounts is null || manifest.Lines.Count != lines.Count ||
        manifest.TargetAvailableAmounts.Count != lines.Count || manifest.Digest != submission.PreviewDigest ||
        manifest.Disposition != submission.Disposition || manifest.SourceKind != submission.SourceKind ||
        manifest.SourceItemId != submission.SourceItemId || manifest.CounterpartyId != submission.CounterpartyId ||
        manifest.Currency != submission.Currency ||
        !CarryAllocationAmount(manifest.SourceOriginalAmount, out var original) || original != submission.SourceAmount ||
        !CarryAllocationAmount(manifest.SourceAvailableAmount, out var available) || available < 0m || available > original ||
        submission.SourceHash != CarryAllocationHash($"{submission.SourceKind}|{submission.SourceItemId:N}|{submission.CounterpartyId:N}|{submission.Currency}|{manifest.SourceOriginalAmount}"))
      return false;
    for (var index = 0; index < lines.Count; index++)
    {
      var line = lines[index]; var pinned = manifest.Lines[index];
      if (pinned is null || pinned.LineNumber != line.LineNumber || pinned.TargetKind != line.TargetKind ||
          pinned.TargetOpenItemId != line.TargetOpenItemId || pinned.ReversesAllocationLineId != line.ReversesAllocationLineId ||
          !CarryAllocationAmount(pinned.Amount, out var amount) || amount != line.Amount ||
          !CarryAllocationAmount(manifest.TargetAvailableAmounts[index], out var targetAvailable) || targetAvailable < 0m ||
          submission.Disposition == "ALLOCATE" && line.Amount > targetAvailable)
        return false;
    }
    return submission.Disposition != "ALLOCATE" || lines.Sum(x => x.Amount) <= available;
  }

  private static bool CarryAllocationOriginalMatches(SuccessorCarriedItem original, CarryAllocationItem actual) =>
    actual.DocumentId == original.OriginalDocumentId && actual.CounterpartyId == original.CounterpartyId &&
    actual.Currency == original.Currency && CarryAllocationAmount(original.OriginalAmount, out var amount) &&
    amount == actual.OriginalAmount && (original.OriginalOpeningId is null || original.OriginalOpeningId == actual.OpeningId) &&
    (original.SubmissionId is null || original.SubmissionId == actual.SubmissionId) &&
    (original.JournalId is null || original.JournalId == actual.JournalId) &&
    (original.DraftId is null || original.DraftId == actual.DraftId);

  private static async Task<CarryAllocationItem?> ResolveCarryAllocationItemAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, string kind, Guid id, SuccessorCarriedItem? carried, CancellationToken ct)
  {
    CarryAllocationItem? item = null;
    if (kind is "OPENING_AR_INVOICE" or "OPENING_AP_INVOICE")
    {
      // A carried opening member has an exact original document id. Never scan every historical opening.
      if (carried is null || carried.OriginalKind != kind || carried.OriginalOpenItemId != id) return null;
      var opening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.Id == carried.OriginalDocumentId, ct);
      if (opening is null || !ValidManifest(opening) || opening.ApprovedByUserId is null || opening.ApprovedAt is null ||
          opening.ApprovedByUserId == opening.CreatedByUserId) return null;
      using var json = JsonDocument.Parse(opening.ManifestJson);
      if (!json.RootElement.TryGetProperty("OpenItems", out var entries) || entries.ValueKind != JsonValueKind.Array) return null;
      var matches = entries.EnumerateArray().Where(x => x.GetProperty("Id").GetGuid() == id).Take(2).ToArray();
      if (matches.Length != 1) return null;
      var row = matches[0];
      if (row.GetProperty("Role").GetString() != (kind == "OPENING_AR_INVOICE" ? "AR" : "AP") ||
          row.GetProperty("AccountId").GetGuid() != carried.AccountId ||
          row.GetProperty("AccountCode").GetString() != carried.AccountCode ||
          row.GetProperty("Reference").GetString() != carried.Reference ||
          !CarryAllocationAmount(row.GetProperty("Amount").GetString(), out var amount)) return null;
      item = new(opening.Id, row.GetProperty("CounterpartyId").GetGuid(), opening.Currency, amount,
        opening.AsOfDate, OpeningId: opening.Id);
    }
    else if (kind == "SALES_INVOICE")
      item = await (from x in db.ClientSalesInvoiceOpenItems.AsNoTracking()
        join s in db.ClientSalesInvoiceSubmissions.AsNoTracking() on x.SubmissionId equals s.Id
        where x.FirmId == firmId && x.ClientId == clientId && x.Id == id && s.FirmId == firmId && s.ClientId == clientId &&
          s.InvoiceId == x.InvoiceId && s.JournalId == x.JournalId && db.ClientSalesInvoiceDecisions.Any(d =>
            d.FirmId == firmId && d.ClientId == clientId && d.SubmissionId == s.Id && d.Decision == "APPROVE" && d.ActorUserId != s.CreatedByUserId)
        select new CarryAllocationItem(x.InvoiceId, x.CustomerId, x.Currency, x.OriginalAmount, default, x.SubmissionId, x.JournalId, null, s.DraftId))
        .SingleOrDefaultAsync(ct);
    else if (kind == "PURCHASE_INVOICE")
      item = await (from x in db.ClientPurchaseInvoiceOpenItems.AsNoTracking()
        join s in db.ClientPurchaseInvoiceSubmissions.AsNoTracking() on x.SubmissionId equals s.Id
        where x.FirmId == firmId && x.ClientId == clientId && x.Id == id && s.FirmId == firmId && s.ClientId == clientId &&
          s.InvoiceId == x.InvoiceId && s.JournalId == x.JournalId && db.ClientPurchaseInvoiceDecisions.Any(d =>
            d.FirmId == firmId && d.ClientId == clientId && d.SubmissionId == s.Id && d.Decision == "APPROVE" && d.ActorUserId != s.CreatedByUserId)
        select new CarryAllocationItem(x.InvoiceId, x.SupplierId, x.Currency, x.OriginalAmount, default, x.SubmissionId, x.JournalId, null, s.DraftId))
        .SingleOrDefaultAsync(ct);
    else if (kind == "SALES_CREDIT")
      item = await (from x in db.ClientSalesCreditNoteOpenItems.AsNoTracking()
        join s in db.ClientSalesCreditNoteSubmissions.AsNoTracking() on x.SubmissionId equals s.Id
        where x.FirmId == firmId && x.ClientId == clientId && x.Id == id && s.FirmId == firmId && s.ClientId == clientId &&
          s.CreditNoteId == x.CreditNoteId && s.JournalId == x.JournalId && s.CustomerId == x.CustomerId &&
          s.Amount == x.OriginalAmount && s.Currency == x.Currency && x.Direction == "CREDIT" &&
          db.ClientSalesCreditNoteDecisions.Any(d => d.FirmId == firmId && d.ClientId == clientId &&
            d.SubmissionId == s.Id && d.Decision == "APPROVE" && d.ActorUserId != s.CreatedByUserId)
        select new CarryAllocationItem(x.CreditNoteId, x.CustomerId, x.Currency, x.OriginalAmount, default, x.SubmissionId, x.JournalId, null, null))
        .SingleOrDefaultAsync(ct);
    else if (kind == "PURCHASE_CREDIT")
      item = await (from x in db.ClientPurchaseCreditNoteOpenItems.AsNoTracking()
        join s in db.ClientPurchaseCreditNoteSubmissions.AsNoTracking() on x.SubmissionId equals s.Id
        where x.FirmId == firmId && x.ClientId == clientId && x.Id == id && s.FirmId == firmId && s.ClientId == clientId &&
          s.CreditNoteId == x.CreditNoteId && s.JournalId == x.JournalId && s.SupplierId == x.SupplierId &&
          s.Amount == x.OriginalAmount && s.Currency == x.Currency && x.Direction == "DEBIT" &&
          db.ClientPurchaseCreditNoteDecisions.Any(d => d.FirmId == firmId && d.ClientId == clientId &&
            d.SubmissionId == s.Id && d.Decision == "APPROVE" && d.ActorUserId != s.CreatedByUserId)
        select new CarryAllocationItem(x.CreditNoteId, x.SupplierId, x.Currency, x.OriginalAmount, default, x.SubmissionId, x.JournalId, null, null))
        .SingleOrDefaultAsync(ct);
    else if (kind is "SALES_RECEIPT" or "SUPPLIER_PAYMENT")
    {
      var origin = await db.ClientManualSettlementOrigins.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.SourceKind == kind && x.Id == id, ct);
      if (origin is null) return await ResolveCarryImportedSettlementAsync(db, firmId, clientId, kind, id, ct);
      item = new(origin.JournalId, origin.CounterpartyId, string.Empty, origin.Amount, default, JournalId: origin.JournalId);
    }
    if (item is null || item.OriginalAmount <= 0m) return null;
    if (item.JournalId is { } journalId)
    {
      var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.Id == journalId && x.Status == "POSTED", ct);
      if (journal is null || journal.PostedAt is null || journal.PostedByUserId is null ||
          journal.PostedByUserId == journal.CreatedByUserId ||
          !await db.ClientOperationalJournalDecisions.AsNoTracking().AnyAsync(d => d.FirmId == firmId &&
            d.ClientId == clientId && d.JournalId == journal.Id && d.JournalRevision == journal.Revision - 1 &&
            d.Decision == "APPROVE" && d.ActorUserId == journal.PostedByUserId && d.CreatedAt == journal.PostedAt, ct) ||
          item.Currency.Length > 0 && item.Currency != journal.Currency) return null;
      item = item with { Currency = journal.Currency, PostingDate = journal.PostingDate };
    }
    var partyRole = kind is "SALES_INVOICE" or "SALES_CREDIT" or "SALES_RECEIPT" or "OPENING_AR_INVOICE" ? "CUSTOMER" : "SUPPLIER";
    var party = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.Id == item.CounterpartyId, ct);
    if (party is null) return null;
    var effective = await ClientBookkeepingCounterpartyWorkspace.EffectiveView(db, party, ct);
    return (effective.Role == partyRole || effective.Role == "BOTH") &&
      (string.IsNullOrEmpty(effective.DefaultCurrency) || effective.DefaultCurrency == item.Currency) ? item : null;
  }

  private static async Task<CarryAllocationItem?> ResolveCarryImportedSettlementAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, string kind, Guid id, CancellationToken ct)
  {
    var candidates = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join transaction in db.GeneralLedgerTransactions.AsNoTracking() on new { line.FirmId, line.ClientId, line.EngagementId, line.ImportBatchId, line.TransactionId }
        equals new { transaction.FirmId, transaction.ClientId, transaction.EngagementId, transaction.ImportBatchId, TransactionId = transaction.Id }
      join batch in db.SourceImportBatches.AsNoTracking() on new { line.FirmId, line.ClientId, line.EngagementId, line.ImportBatchId }
        equals new { batch.FirmId, batch.ClientId, batch.EngagementId, ImportBatchId = batch.Id }
      where line.FirmId == firmId && line.ClientId == clientId && line.Id == id && batch.OriginKind == "EXTERNAL" &&
        batch.SourceKind == "GL" && batch.Status == "SEALED" && batch.ExpectedLineCount == batch.AcceptedLineCount &&
        batch.ExpectedTransactionCount == batch.AcceptedTransactionCount && transaction.Currency == batch.Currency &&
        line.OriginalCurrency == transaction.Currency
      select new { Line = line, Transaction = transaction, Batch = batch }).Take(2).ToListAsync(ct);
    if (candidates.Count != 1 || !IsSha256(candidates[0].Batch.NormalizedDatasetDigest)) return null;
    var candidate = candidates[0]; var ar = kind == "SALES_RECEIPT";
    var roles = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on role.Id equals decision.ConfigurationId
      where role.FirmId == firmId && role.ClientId == clientId && decision.FirmId == firmId && decision.ClientId == clientId &&
        decision.Decision == "APPROVE" && decision.ReviewedByUserId != role.ProposedByUserId &&
        (role.Role == "AR" || role.Role == "AP") && role.EffectiveFrom <= candidate.Transaction.PostingDate &&
        (role.EffectiveTo == null || role.EffectiveTo >= candidate.Transaction.PostingDate)
      select role).OrderBy(x => x.Id).Take(5001).ToListAsync(ct);
    if (roles.Count > 5000 || roles.Count(x => x.AccountId == candidate.Line.ClientAccountId && x.Role == (ar ? "AR" : "AP")) != 1)
      return null;
    var partyIdentifier = candidate.Line.PartyIdentifier.ToLowerInvariant();
    var parties = await db.ClientBookkeepingCounterparties.AsNoTracking().Where(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.Role == (ar ? "CUSTOMER" : "SUPPLIER") &&
      (x.ExternalReference == candidate.Line.PartyIdentifier || x.NormalizedExternalIdentity == candidate.Line.PartyIdentifier ||
       x.Id.ToString() == partyIdentifier || x.Id.ToString().Replace("-", "") == partyIdentifier)).Take(2).ToListAsync(ct);
    if (parties.Count != 1 || string.IsNullOrWhiteSpace(candidate.Line.PartyIdentifier)) return null;
    var legs = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join account in db.ClientAccounts.AsNoTracking() on line.ClientAccountId equals (Guid?)account.Id
      where line.FirmId == firmId && line.ClientId == clientId && line.TransactionId == candidate.Transaction.Id &&
        line.ImportBatchId == candidate.Batch.Id && line.EngagementId == candidate.Transaction.EngagementId &&
        account.FirmId == firmId && account.ClientId == clientId
      select new { line.Id, line.Debit, line.Credit, line.OriginalCurrency, AccountId = account.Id, account.AccountType })
      .Take(10001).ToListAsync(ct);
    var controlIds = roles.Select(x => x.AccountId).ToHashSet();
    var amount = ar ? candidate.Line.Credit : candidate.Line.Debit;
    var cash = legs.Where(x => x.AccountType == "ASSET" && !controlIds.Contains(x.AccountId)).ToArray();
    var actualLineCount = await db.GeneralLedgerLines.AsNoTracking().CountAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.TransactionId == candidate.Transaction.Id && x.ImportBatchId == candidate.Batch.Id &&
      x.EngagementId == candidate.Transaction.EngagementId, ct);
    if (legs.Count is 0 or > 10000 || actualLineCount != legs.Count || amount <= 0m || (ar ? candidate.Line.Debit != 0m : candidate.Line.Credit != 0m) ||
        legs.Any(x => x.OriginalCurrency != candidate.Transaction.Currency) ||
        legs.Count(x => controlIds.Contains(x.AccountId)) != 1 ||
        (ar ? cash.Sum(x => x.Debit) != amount || cash.Sum(x => x.Credit) != 0m :
          cash.Sum(x => x.Credit) != amount || cash.Sum(x => x.Debit) != 0m) ||
        legs.Sum(x => x.Debit) != legs.Sum(x => x.Credit)) return null;
    return new(candidate.Transaction.Id, parties[0].Id, candidate.Transaction.Currency, amount, candidate.Transaction.PostingDate);
  }

  private static bool CarryAllocationSourceKind(string kind) =>
    kind is "SALES_CREDIT" or "PURCHASE_CREDIT" or "SALES_RECEIPT" or "SUPPLIER_PAYMENT";
  private static bool CarryAllocationTargetKind(string kind) =>
    kind is "SALES_INVOICE" or "PURCHASE_INVOICE" or "OPENING_AR_INVOICE" or "OPENING_AP_INVOICE";
  private static bool CarryAllocationAmount(string? text, out decimal amount) =>
    decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
      CultureInfo.InvariantCulture, out amount) && decimal.Round(amount, 6) == amount;
  private static string CarryAllocationHash(string text) =>
    Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
  private static CommandResult CarryAllocationBlocked(string message) => CommandResult.Fail(ErrorCodes.GateBlocked, message);
}
