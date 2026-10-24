using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalOpeningBalanceWorkspace
{
  private static string SuccessorHash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
  private static string SuccessorHash<T>(T value) => SuccessorHash(JsonSerializer.Serialize(value));
  private static CommandResult<T> SuccessorFail<T>(string message, string? code = null) =>
    CommandResult<T>.Fail(code ?? ErrorCodes.GateBlocked, message);

  internal static CommandResult<SuccessorProof> ParseSuccessorProof(ClientOperationalOpeningBalance opening,
    ClientOperationalOpeningSuccessorProof row, SuccessorFacts? sourceFacts = null)
  {
    try
    {
      if (!ValidManifest(opening) || SuccessorHash(row.ProofJson) != row.ProofHash || row.OpeningId != opening.Id ||
          row.FirmId != opening.FirmId || row.ClientId != opening.ClientId || opening.EvidenceSha256 != row.SourceIdentityHash)
        return SuccessorFail<SuccessorProof>("The retained successor header failed integrity.", ErrorCodes.ProtectedState);
      var p = JsonSerializer.Deserialize<SuccessorProof>(row.ProofJson);
      if (p is null || p.Facts is null || p.Version is not ("native-opening-successor.v1" or "native-opening-successor.v2") || p.OpeningId != opening.Id ||
          p.OpeningManifestHash != opening.ManifestSha256 || p.FirmId != row.FirmId || p.ClientId != row.ClientId ||
          p.EngagementId != row.EngagementId || p.PriorPeriodId != row.PriorPeriodId || p.PriorPeriodRevision != row.PriorPeriodRevision ||
          p.SourceDatasetId != row.SourceDatasetId || p.SourceIdentityHash != row.SourceIdentityHash ||
          p.Facts.TargetPeriodId != opening.PeriodId || p.Facts.TargetRevision != opening.PeriodRevision ||
          p.Facts.PriorPeriodId != row.PriorPeriodId || p.Facts.PriorRevision != row.PriorPeriodRevision)
        return SuccessorFail<SuccessorProof>("The retained successor proof does not bind its exact opening.", ErrorCodes.ProtectedState);
      if (p.Version == "native-opening-successor.v1" ? p.Facts.CarriedPopulation is not null :
          p.Facts.CarriedPopulation is null || !ValidSuccessorCarry(p.Facts.CarriedPopulation, p.Facts))
        return SuccessorFail<SuccessorProof>("The successor proof version does not bind its exact original-item population.", ErrorCodes.ProtectedState);
      // Retained facts authenticate the output even on history reads. Financial suitability
      // supplies freshly resolved source facts as well; no self-hashed arbitrary output is accepted.
      var expectedFacts = sourceFacts ?? p.Facts;
      if (expectedFacts.Lines is null || expectedFacts.Lines.Any(x => x is null) ||
          JsonSerializer.Serialize(expectedFacts) != JsonSerializer.Serialize(p.Facts) ||
          OpeningHash(SuccessorManifest(opening.FirmId, opening.ClientId, opening.AsOfDate,
            expectedFacts, opening.EvidenceReference, row.SourceIdentityHash)) != opening.ManifestSha256)
        return SuccessorFail<SuccessorProof>("The opening rows do not reproduce the exact successor source facts.", ErrorCodes.ProtectedState);
      return CommandResult<SuccessorProof>.Ok(p);
    }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
    { return SuccessorFail<SuccessorProof>("Malformed successor proof.", ErrorCodes.ProtectedState); }
  }

  internal static async Task<CommandResult<SuccessorFacts>> ResolveSuccessorFactsAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, Guid targetId, NativeTrialBalanceSourceWorkspace.SuccessorSource source, CancellationToken ct, DateTimeOffset? originalProofCreatedAt = null)
  {
    try { return await ResolveSuccessorFactsCoreAsync(db, firmId, clientId, targetId, source, ct, originalProofCreatedAt); }
    catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException or KeyNotFoundException)
    { return SuccessorFail<SuccessorFacts>("Malformed or ambiguous successor source membership.", ErrorCodes.ProtectedState); }
  }

  private static async Task<CommandResult<SuccessorFacts>> ResolveSuccessorFactsCoreAsync(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, Guid targetId, NativeTrialBalanceSourceWorkspace.SuccessorSource source, CancellationToken ct, DateTimeOffset? originalProofCreatedAt)
  {
    var prior = source.Period;
    var target = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId &&
      x.ClientId == clientId && x.Id == targetId, ct);
    if (target is null || target.PriorPeriodId != prior.Id || prior.EndDate == DateOnly.MaxValue ||
        target.StartDate != prior.EndDate.AddDays(1) || target.Currency != prior.Currency || target.Basis != prior.Basis ||
        target.Status is not ("DRAFT" or "ACTIVE" or "CLOSED"))
      return SuccessorFail<SuccessorFacts>("Exact contiguous predecessor lineage, unchanged basis/currency and supported period state are required.");
    var revision = target.Revision;
    if (target.Status == "CLOSED")
    {
      if (revision < 2 || target.ClosedByUserId is null || target.ClosedAt is null || string.IsNullOrWhiteSpace(target.CloseReason))
        return SuccessorFail<SuccessorFacts>("The successor close evidence is incomplete.");
      revision--;
    }
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId, ct);
    var mandate = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == firmId && x.PracticeClientId == clientId &&
      x.EngagementId == null && x.ServiceRoute == "BOOKKEEPING").OrderByDescending(x => x.Generation)
      .ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (profile is null || profile.SourceMode != ClientAccountingSourceModes.NativeBookkeeping || profile.FunctionalCurrency != target.Currency ||
        mandate is not { Decision: "Accepted", Conditions: null or "" })
      return SuccessorFail<SuccessorFacts>("Current explicit native profile and unconditional BOOKKEEPING authority are required.");
    var charts = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= target.StartDate &&
      (x.EffectiveTo == null || x.EffectiveTo >= target.StartDate)).Take(2).ToListAsync(ct);
    if (charts.Count != 1) return SuccessorFail<SuccessorFacts>("Exactly one effective approved chart is required.");
    var chart = charts[0];
    using var membership = JsonDocument.Parse(source.MembershipJson);
    // Membership includes the original opening's whole chart, including known zero-control accounts.
    var pinned = membership.RootElement.GetProperty("Accounts").EnumerateArray().ToDictionary(x => x.GetProperty("Id").GetGuid());
    if (pinned.Count > 250_000) return SuccessorFail<SuccessorFacts>("Source account population exceeds its bound.");
    var ids = pinned.Keys.ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      ids.Contains(x.Id)).OrderBy(x => x.Id).Take(250_001).ToListAsync(ct);
    if (accounts.Count != pinned.Count || accounts.Any(x => x.ChartVersionId != chart.Id || !x.IsPosting ||
        x.Status != AccountingWorkflowStates.Active || x.AccountType is not ("ASSET" or "LIABILITY" or "EQUITY" or "INCOME" or "EXPENSE")))
      return SuccessorFail<SuccessorFacts>("The exact source account/chart identities must remain supported; no account remapping is performed.");
    var byId = accounts.ToDictionary(x => x.Id);
    var rows = source.TrialBalance.Rows;
    if (rows.Any(x => !byId.TryGetValue(x.AccountId, out var a) || a.AccountCode != x.AccountCode || a.AccountName != x.AccountName))
      return SuccessorFail<SuccessorFacts>("Closing rows must retain their original native account identities.");
    decimal Amount(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
    foreach (var row in rows)
      if (byId[row.AccountId].AccountType is "INCOME" or "EXPENSE" &&
          Amount(row.ClosingDebit) - Amount(row.ClosingCredit) != 0m)
        return SuccessorFail<SuccessorFacts>("Every individual P&L account must close to zero. No automatic income-closing treatment is authorized.");
    var lines = rows.Where(x => byId[x.AccountId].AccountType is "ASSET" or "LIABILITY" or "EQUITY")
      .Where(x => Amount(x.ClosingDebit) != 0m || Amount(x.ClosingCredit) != 0m)
      .OrderBy(x => x.AccountCode, StringComparer.Ordinal).Select(x => new NativeSuccessorOpeningLine(
        x.AccountId, x.AccountCode, x.AccountName, x.ClosingDebit, x.ClosingCredit)).ToArray();
    if (lines.Length is < 2 or > 5000 || lines.Sum(x => Amount(x.Debit)) != lines.Sum(x => Amount(x.Credit)))
      return SuccessorFail<SuccessorFacts>("A supported successor needs 2 to 5,000 genuine balanced nonzero balance-sheet rows; no empty or balancing rows are invented.");
    var roles = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on role.Id equals decision.ConfigurationId
      where role.FirmId == firmId && role.ClientId == clientId && decision.FirmId == firmId && decision.ClientId == clientId &&
        decision.Decision == "APPROVE" && (role.Role == "AR" || role.Role == "AP") && role.EffectiveFrom <= target.StartDate &&
        (role.EffectiveTo == null || role.EffectiveTo >= prior.StartDate)
      orderby role.Role, role.Id
      select new { Role = role, Decision = decision }).Take(3).ToListAsync(ct);
    if (roles.Count != 2 || roles.Count(x => x.Role.Role == "AR") != 1 || roles.Count(x => x.Role.Role == "AP") != 1 ||
        roles.Any(x => x.Role.ChartVersionId != chart.Id || x.Role.EffectiveFrom > prior.StartDate ||
          x.Role.EffectiveTo != null && x.Role.EffectiveTo < target.StartDate || !byId.ContainsKey(x.Role.AccountId)))
      return SuccessorFail<SuccessorFacts>("Explicit independently approved unambiguous AR and AP controls must cover the predecessor and successor opening and belong to exact source membership.");
    var controls = new List<NativeSuccessorControlProof>();
    foreach (var x in roles)
    {
      var account = byId[x.Role.AccountId];
      if (account.AccountType != (x.Role.Role == "AR" ? "ASSET" : "LIABILITY"))
        return SuccessorFail<SuccessorFacts>("The approved control account type changed.");
      var row = rows.SingleOrDefault(y => y.AccountId == account.Id);
      if (row is not null && (x.Role.Role == "AR" ? Amount(row.ClosingDebit) - Amount(row.ClosingCredit) :
          Amount(row.ClosingCredit) - Amount(row.ClosingDebit)) < 0m)
        return SuccessorFail<SuccessorFacts>("A negative control balance requires an explicitly supported original credit population.");
      // Missing emitted zero row is supported only with exact pinned account membership and no original control activity.
      if (row is null)
      {
        var journals = membership.RootElement.GetProperty("Lines").EnumerateArray();
        if (journals.Any(y => y.GetProperty("ClientAccountId").GetGuid() == account.Id))
          return SuccessorFail<SuccessorFacts>("Control activity without an exact closing row is unsupported.");
        var priorOpening = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(y =>
          y.FirmId == firmId && y.ClientId == clientId && y.PeriodId == prior.Id, ct);
        using var original = JsonDocument.Parse(priorOpening.ManifestJson);
        if (original.RootElement.GetProperty("Lines").EnumerateArray().Any(y => y.GetProperty("AccountCode").GetString() == account.AccountCode))
          return SuccessorFail<SuccessorFacts>("Control opening without an exact closing row is unsupported.");
      }
      controls.Add(new(x.Role.Role, x.Role.Id, x.Decision.Id, account.Id,
        row is null || Amount(row.ClosingDebit) == 0m ? "0" : row.ClosingDebit,
        row is null || Amount(row.ClosingCredit) == 0m ? "0" : row.ClosingCredit));
    }
    var carried = await ResolveSuccessorCarriedItemsAsync(db, firmId, clientId, source, controls, ct);
    if (!carried.Succeeded) return SuccessorFail<SuccessorFacts>(carried.Message!, carried.ErrorCode);
    string populationHash;
    if (carried.Value is { } population)
    {
      var allocations = await ValidateSuccessorCarryAllocationsAsync(db, firmId, clientId, population,
        prior.EndDate, target.StartDate, originalProofCreatedAt, ct);
      if (!allocations.Succeeded) return SuccessorFail<SuccessorFacts>(allocations.Message!, allocations.ErrorCode);
      populationHash = population.PopulationHash;
    }
    else
    {
      var empty = await ProveEmptySuccessorSubledgerAsync(db, firmId, clientId, source, ct);
      if (!empty.Succeeded) return SuccessorFail<SuccessorFacts>(empty.Message!, empty.ErrorCode);
      populationHash = SuccessorHash(new { Version = "empty-native-subledger.v1", prior.Id, prior.EndDate,
        Empty = true, Controls = roles.Select(x => new { x.Role, x.Decision }).ToArray() });
    }
    return CommandResult<SuccessorFacts>.Ok(new(target.Id, revision, prior.Id, prior.Revision, prior.StartDate, prior.EndDate,
      prior.ClosedByUserId!.Value, prior.ClosedAt!.Value, prior.CloseReason!, prior.Basis, prior.Currency, chart.Id,
      source.Evidence.ManifestHash, source.Evidence.GeneratedRowsHash, source.AcceptanceId, source.AcceptanceHash,
      SuccessorHash(profile), SuccessorHash(mandate), populationHash, lines, controls, carried.Value));
  }

  private static async Task<CommandResult> ProveEmptySuccessorSubledgerAsync(IClientAccountingDbContext db,
    Guid firm, Guid client, NativeTrialBalanceSourceWorkspace.SuccessorSource source, CancellationToken ct)
  {
    // Deliberately bounded subset: even settled historical documents are refused. Future-period activity is not inherited.
    var periods = db.ClientReportingPeriods.Where(x => x.FirmId == firm && x.ClientId == client && x.StartDate <= source.Period.EndDate).Select(x => x.Id);
    var journals = db.ClientOperationalJournals.Where(x => x.FirmId == firm && x.ClientId == client && periods.Contains(x.PeriodId)).Select(x => x.Id);
    var found = await db.ClientSalesInvoiceDrafts.AnyAsync(x => x.FirmId == firm && x.ClientId == client && periods.Contains(x.PeriodId), ct) ||
      await db.ClientPurchaseInvoiceDrafts.AnyAsync(x => x.FirmId == firm && x.ClientId == client && periods.Contains(x.PeriodId), ct) ||
      await db.ClientSalesInvoiceSubmissions.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct) ||
      await db.ClientPurchaseInvoiceSubmissions.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct) ||
      await db.ClientSalesCreditNoteSubmissions.AnyAsync(x => x.FirmId == firm && x.ClientId == client && periods.Contains(x.PeriodId), ct) ||
      await db.ClientPurchaseCreditNoteSubmissions.AnyAsync(x => x.FirmId == firm && x.ClientId == client && periods.Contains(x.PeriodId), ct) ||
      await db.ClientSalesInvoiceOpenItems.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct) ||
      await db.ClientPurchaseInvoiceOpenItems.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct) ||
      await db.ClientSalesCreditNoteOpenItems.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct) ||
      await db.ClientPurchaseCreditNoteOpenItems.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct) ||
      await db.ClientManualSettlementOrigins.AnyAsync(x => x.FirmId == firm && x.ClientId == client && journals.Contains(x.JournalId), ct);
    if (found) return CommandResult.Fail(ErrorCodes.GateBlocked, "Historical invoices, credits, settlements or allocations require original-item continuity beyond this empty-subledger subset.");
    var allocationProof = await ProveNoPredecessorAllocationsAsync(db, firm, client, source.Period.EndDate, ct);
    if (!allocationProof.Succeeded) return allocationProof;
    var openings = await db.ClientOperationalOpeningBalances.AsNoTracking().Where(x => x.FirmId == firm && x.ClientId == client &&
      x.AsOfDate <= source.Period.EndDate).OrderBy(x => x.AsOfDate).Take(33).ToListAsync(ct);
    if (openings.Count > 32) return CommandResult.Fail(ErrorCodes.GateBlocked, "Opening history exceeds the supported bound.");
    foreach (var opening in openings)
    {
      if (!ValidManifest(opening)) return CommandResult.Fail(ErrorCodes.ProtectedState, "Historical opening integrity is required.");
      using var json = JsonDocument.Parse(opening.ManifestJson);
      if (json.RootElement.TryGetProperty("OpenItems", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() != 0)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Historical opening items cannot be recreated or erased by successor capture.");
    }
    // Reject all external GL rows in the relevant cutoff, even party-less/unsupported settlement candidates.
    if (await (from line in db.GeneralLedgerLines.AsNoTracking()
      join tx in db.GeneralLedgerTransactions.AsNoTracking() on line.TransactionId equals tx.Id
      join batch in db.SourceImportBatches.AsNoTracking() on line.ImportBatchId equals batch.Id
      where line.FirmId == firm && line.ClientId == client && tx.FirmId == firm && tx.ClientId == client &&
        batch.FirmId == firm && batch.ClientId == client && batch.OriginKind == "EXTERNAL" &&
        batch.SourceKind == "GL" && tx.PostingDate <= source.Period.EndDate
      select line.Id).AnyAsync(ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "External source candidates require an explicit subledger continuity contract.");
    return CommandResult.Ok();
  }
}
