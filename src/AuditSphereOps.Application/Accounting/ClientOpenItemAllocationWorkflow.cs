using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOpenItemAllocationLineRequest(int LineNumber, string TargetKind, Guid TargetOpenItemId, decimal Amount, Guid? ReversesAllocationLineId = null);
public sealed record ClientOpenItemAllocationRequest(Guid CommandId, string SourceKind, Guid SourceItemId, string Disposition,
  string Reference, string Reason, IReadOnlyList<ClientOpenItemAllocationLineRequest> Lines, string PreviewDigest);
public sealed record ClientOpenItemAllocationPreviewLine(int LineNumber, string TargetKind, Guid TargetOpenItemId, string Amount, Guid? ReversesAllocationLineId);
public sealed record ClientOpenItemBalance(string Kind, Guid OpenItemId, Guid SourceDocumentId, Guid CounterpartyId,
  string CounterpartyName, string Currency, string OriginalAmount, string AppliedAmount, string OpenAmount,
  DateOnly? DueDate, DateOnly? AsOfDate, string Status);
public sealed record ClientOpenItemControlReconciliationRow(string Role, Guid AccountId, string AccountCode, string AccountName,
  string LedgerBalance, string OpenItemBalance, string Difference, int OpenItemCount, string Status);
public sealed record ClientOpenItemControlReconciliationView(Guid ClientId, Guid PeriodId, string PeriodCode, string Currency,
  string AsOfDate, string LedgerBasis, string AllocationBasis, string OpeningDetailStatus, int UnlinkedOpenItemCount,
  IReadOnlyList<ClientOpenItemControlReconciliationRow> Accounts, bool Reconciled);
public sealed record ClientOpenItemAllocationPreview(Guid SubmissionId, string Digest, string Disposition, string SourceKind,
  Guid SourceItemId, Guid CounterpartyId, string Currency, string SourceOriginalAmount, string SourceAvailableAmount,
  IReadOnlyList<ClientOpenItemAllocationPreviewLine> Lines, IReadOnlyList<string> TargetAvailableAmounts);
public sealed record ClientOpenItemAllocationReceipt(Guid CommandId, Guid SubmissionId, string Outcome, Guid? DecisionId);

/// <summary>Client-book allocation between approved ledger-backed credit open items and matching posted invoices.</summary>
public static class ClientOpenItemAllocationWorkflow
{
  private static readonly string[] Preparers = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private sealed record OpenItem(string Kind, Guid Id, Guid DocumentId, Guid CounterpartyId, string CounterpartyName,
    string Currency, decimal Original, DateOnly? Due, bool Credit, Guid? JournalId = null,
    DateOnly? PostingDate = null, bool Imported = false);
  private sealed record ImportedSettlementLine(GeneralLedgerLine Line, GeneralLedgerTransaction Transaction,
    SourceImportBatch Batch, string Role);

  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);

  private static async Task<bool> LockClient(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct) is not null;

  public static async Task<CommandResult<IReadOnlyList<ClientOpenItemBalance>>> BalancesAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, DateOnly asOfDate, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<IReadOnlyList<ClientOpenItemBalance>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var items = await OpenItems(db, actor, clientId, ct, asOfDate);
    var applied = await EffectiveLines(db, actor.FirmId, clientId, ct);
    var sourceApplied = await EffectiveSourceLines(db, actor.FirmId, clientId, ct);
    var rows = items.Select(item =>
    {
      var amount = item.Credit ? sourceApplied.GetValueOrDefault((item.Kind, item.Id)) : applied.Where(x => x.Kind == item.Kind && x.Id == item.Id).Sum(x => x.Amount);
      var open = MoneyPolicy.Normalize(Math.Max(0m, item.Original - amount));
      var overdue = !item.Credit && open > 0 && item.Due is not null && item.Due.Value < asOfDate;
      var status = open == 0 && amount > 0 ? "CREDITED" : open == 0 ? "SETTLED" : overdue ? "OVERDUE" : amount > 0 ? "PARTIAL" : "OUTSTANDING";
      return new ClientOpenItemBalance(item.Kind, item.Id, item.DocumentId, item.CounterpartyId, item.CounterpartyName,
        item.Currency, Exact(item.Original), Exact(MoneyPolicy.Normalize(amount)), Exact(open), item.Due, asOfDate, status);
    }).Where(x => decimal.Parse(x.OpenAmount, System.Globalization.CultureInfo.InvariantCulture) > 0 ||
      decimal.Parse(x.AppliedAmount, System.Globalization.CultureInfo.InvariantCulture) > 0).OrderBy(x => x.CounterpartyName).ThenBy(x => x.DueDate).ToArray();
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<IReadOnlyList<ClientOpenItemBalance>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<IReadOnlyList<ClientOpenItemBalance>>.Ok(rows);
  }

  public static async Task<CommandResult<ClientOpenItemControlReconciliationView>> ReconcileControlAccountsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid periodId, DateOnly asOfDate,
    CancellationToken ct = default)
  {
    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOpenItemControlReconciliationView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.Id == periodId, ct);
    if (profile is null || period is null || period.Currency != profile.FunctionalCurrency || asOfDate < period.StartDate || asOfDate > period.EndDate)
      return CommandResult<ClientOpenItemControlReconciliationView>.Fail(ErrorCodes.GateBlocked,
        "Choose a native bookkeeping period and as-of date within its functional-currency coverage.");

    var roleRows = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { role.FirmId, role.ClientId, ConfigurationId = role.Id }
        equals new { decision.FirmId, decision.ClientId, decision.ConfigurationId }
      where role.FirmId == actor.FirmId && role.ClientId == clientId && (role.Role == "AR" || role.Role == "AP") &&
        decision.Decision == "APPROVE" && role.EffectiveFrom <= asOfDate && (role.EffectiveTo == null || role.EffectiveTo >= asOfDate)
      select new { role.AccountId, role.Role, role.EffectiveFrom, role.EffectiveTo }).ToListAsync(ct);
    var journalRows = await (from line in db.ClientOperationalJournalLines.AsNoTracking()
      join journal in db.ClientOperationalJournals.AsNoTracking() on new { line.FirmId, line.ClientId, Id = line.JournalId }
        equals new { journal.FirmId, journal.ClientId, journal.Id }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && journal.Status == "POSTED" && journal.PostingDate <= asOfDate
      select new { Line = line, Journal = journal }).ToListAsync(ct);

    var ledger = new Dictionary<(string Role, Guid AccountId), (string Code, string Name, decimal Balance)>();
    var controlLineCounts = new Dictionary<Guid, int>();
    foreach (var row in journalRows)
    {
      foreach (var role in roleRows.Where(x => x.AccountId == row.Line.ClientAccountId && x.EffectiveFrom <= row.Journal.PostingDate &&
        (x.EffectiveTo == null || x.EffectiveTo >= row.Journal.PostingDate)))
      {
        var key = (Role: role.Role, AccountId: row.Line.ClientAccountId);
        var signed = role.Role == "AR" ? row.Line.Debit - row.Line.Credit : row.Line.Credit - row.Line.Debit;
        var current = ledger.GetValueOrDefault(key, (Code: row.Line.AccountCode, Name: row.Line.AccountName, Balance: 0m));
        ledger[key] = (current.Code, current.Name, current.Balance + signed);
        controlLineCounts[row.Journal.Id] = controlLineCounts.GetValueOrDefault(row.Journal.Id) + 1;
      }
    }

    var items = await OpenItems(db, actor, clientId, ct, asOfDate);
    var applied = await EffectiveLines(db, actor.FirmId, clientId, ct);
    var sourceApplied = await EffectiveSourceLines(db, actor.FirmId, clientId, ct);
    var openByAccount = new Dictionary<(string Role, Guid AccountId), (decimal Balance, int Count)>();
    var unlinked = 0;
    foreach (var item in items.Where(x => !x.Imported && x.JournalId.HasValue))
    {
      var expectedRole = item.Kind.StartsWith("SALES_", StringComparison.Ordinal) ? "AR" : "AP";
      var itemJournalId = item.JournalId.GetValueOrDefault();
      var candidates = journalRows.Where(x => x.Journal.Id == itemJournalId && item.Currency == period.Currency &&
        roleRows.Any(role => role.AccountId == x.Line.ClientAccountId && role.Role == expectedRole && role.EffectiveFrom <= x.Journal.PostingDate &&
          (role.EffectiveTo == null || role.EffectiveTo >= x.Journal.PostingDate))).ToArray();
      if (candidates.Length != 1 || controlLineCounts.GetValueOrDefault(itemJournalId) != 1)
      {
        unlinked++;
        continue;
      }
      var amountApplied = item.Credit ? sourceApplied.GetValueOrDefault((item.Kind, item.Id)) :
        applied.Where(x => x.Kind == item.Kind && x.Id == item.Id).Sum(x => x.Amount);
      if (amountApplied < 0m || amountApplied > item.Original) { unlinked++; continue; }
      var open = MoneyPolicy.Normalize(Math.Max(0m, item.Original - amountApplied));
      var signed = item.Credit ? -open : open;
      var key = (expectedRole, candidates[0].Line.ClientAccountId);
      var current = openByAccount.GetValueOrDefault(key, (Balance: 0m, Count: 0));
      openByAccount[key] = (current.Balance + signed, current.Count + (open > 0m ? 1 : 0));
    }

    var keys = ledger.Keys.Union(openByAccount.Keys).Union(roleRows.Where(x => x.EffectiveFrom <= asOfDate && (x.EffectiveTo == null || x.EffectiveTo >= asOfDate))
      .Select(x => (x.Role, x.AccountId))).OrderBy(x => x.Role).ThenBy(x =>
      ledger.TryGetValue(x, out var value) ? value.Code : string.Empty, StringComparer.Ordinal).ToArray();
    var roleAccounts = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join account in db.ClientAccounts.AsNoTracking() on new { role.FirmId, role.ClientId, role.AccountId } equals new { account.FirmId, account.ClientId, AccountId = account.Id }
      where role.FirmId == actor.FirmId && role.ClientId == clientId && roleRows.Select(x => x.AccountId).Contains(role.AccountId)
      select new { role.Role, role.AccountId, account.AccountCode, account.AccountName }).ToListAsync(ct);
    var accounts = keys.Select(key =>
    {
      var ledgerEntry = ledger.GetValueOrDefault(key, (Code: string.Empty, Name: string.Empty, Balance: 0m));
      if (string.IsNullOrEmpty(ledgerEntry.Code))
      {
        var chartAccount = roleAccounts.FirstOrDefault(x => x.Role == key.Role && x.AccountId == key.AccountId);
        if (chartAccount is not null) ledgerEntry = (chartAccount.AccountCode, chartAccount.AccountName, ledgerEntry.Balance);
      }
      var openEntry = openByAccount.GetValueOrDefault(key, (Balance: 0m, Count: 0));
      var difference = MoneyPolicy.Normalize(ledgerEntry.Balance - openEntry.Balance);
      return new ClientOpenItemControlReconciliationRow(key.Role, key.AccountId, ledgerEntry.Code, ledgerEntry.Name,
        Exact(ledgerEntry.Balance), Exact(openEntry.Balance), Exact(difference), openEntry.Count,
        difference == 0m ? "RECONCILED" : "UNRECONCILED");
    }).ToArray();
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOpenItemControlReconciliationView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await snapshot.CommitAsync(ct);
    var view = new ClientOpenItemControlReconciliationView(clientId, periodId, period.PeriodCode, period.Currency,
      asOfDate.ToString("yyyy-MM-dd"), "POSTED_NATIVE_CLIENT_JOURNALS_TO_AS_OF_DATE",
      "CURRENT_APPROVED_ALLOCATIONS_NO_SEPARATE_EFFECTIVE_DATE", "OPENING_ITEM_DETAIL_NOT_INCLUDED", unlinked, accounts,
      unlinked == 0 && accounts.All(x => x.Status == "RECONCILED"));
    return CommandResult<ClientOpenItemControlReconciliationView>.Ok(view);
  }

  public static async Task<CommandResult<ClientOpenItemAllocationPreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientOpenItemAllocationRequest request, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var result = await BuildPreview(db, actor, clientId, request, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct);
    return result;
  }

  public static async Task<CommandResult<ClientOpenItemAllocationReceipt>> SubmitAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientOpenItemAllocationRequest request, CancellationToken ct = default)
  {
    if (request.CommandId == Guid.Empty) return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.IdempotencyConflict, "A stable command id is required.");
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var intent = Hash(JsonSerializer.Serialize(new { actor.FirmId, clientId, actor.UserId, request }));
    var replay = await db.ClientOpenItemAllocationSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (replay is not null)
      return replay.IntentHash == intent
        ? CommandResult<ClientOpenItemAllocationReceipt>.Ok(new(request.CommandId, replay.Id, "SUBMITTED", null))
        : CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.IdempotencyConflict, "The command id was reused for different allocation intent.");
    var preview = await BuildPreview(db, actor, clientId, request, ct);
    if (!preview.Succeeded || preview.Value!.Digest != request.PreviewDigest)
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.ProtectedState, "Allocation preview is stale or invalid.");
    var p = preview.Value;
    var manifestJson = JsonSerializer.Serialize(p);
    var submission = new ClientOpenItemAllocationSubmission
    {
      Id = Guid.NewGuid(), FirmId = actor.FirmId, ClientId = clientId, SourceKind = p.SourceKind, SourceItemId = p.SourceItemId,
      Disposition = p.Disposition, CounterpartyId = p.CounterpartyId, Currency = p.Currency, SourceAmount = decimal.Parse(p.SourceOriginalAmount, System.Globalization.CultureInfo.InvariantCulture),
      SourceHash = Hash($"{p.SourceKind}|{p.SourceItemId:N}|{p.CounterpartyId:N}|{p.Currency}|{p.SourceOriginalAmount}"),
      Reference = request.Reference.Trim(), Reason = request.Reason.Trim(), CommandId = request.CommandId, IntentHash = intent,
      PreviewDigest = p.Digest, ManifestJson = manifestJson, ManifestHash = Hash(manifestJson), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientOpenItemAllocationSubmissions.Add(submission);
    foreach (var item in request.Lines.OrderBy(x => x.LineNumber)) db.ClientOpenItemAllocationLines.Add(new()
    {
      Id = Guid.NewGuid(), FirmId = actor.FirmId, ClientId = clientId, SubmissionId = submission.Id, LineNumber = item.LineNumber,
      TargetKind = item.TargetKind, TargetOpenItemId = item.TargetOpenItemId, Amount = item.Amount, ReversesAllocationLineId = item.ReversesAllocationLineId
    });
    await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Preparers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.GateBlocked, "Bookkeeping authority changed before the allocation request was saved.");
    await tx.CommitAsync(ct);
    return CommandResult<ClientOpenItemAllocationReceipt>.Ok(new(request.CommandId, submission.Id, "SUBMITTED", null));
  }

  public static async Task<CommandResult<ClientOpenItemAllocationPreview>> ReviewPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid submissionId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded)
      return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var submission = await db.ClientOpenItemAllocationSubmissions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submissionId, ct);
    var decision = await db.ClientOpenItemAllocationDecisions.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submissionId, ct);
    if (submission is null || decision || Hash(submission.ManifestJson) != submission.ManifestHash)
      return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ProtectedState, "The allocation request is unavailable or has changed.");
    try
    {
      var preview = JsonSerializer.Deserialize<ClientOpenItemAllocationPreview>(submission.ManifestJson);
      return preview is null ? CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ProtectedState, "The retained allocation request cannot be read.") :
        CommandResult<ClientOpenItemAllocationPreview>.Ok(preview with { SubmissionId = submission.Id });
    }
    catch (JsonException) { return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ProtectedState, "The retained allocation request cannot be read."); }
  }

  public static async Task<CommandResult<IReadOnlyList<ClientOpenItemAllocationPreview>>> PendingAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded)
      return CommandResult<IReadOnlyList<ClientOpenItemAllocationPreview>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var submissions = await db.ClientOpenItemAllocationSubmissions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.CreatedByUserId != actor.UserId && !db.ClientOpenItemAllocationDecisions.Any(d => d.FirmId == x.FirmId && d.ClientId == x.ClientId && d.SubmissionId == x.Id))
      .OrderBy(x => x.CreatedAt).Take(100).ToListAsync(ct);
    var result = new List<ClientOpenItemAllocationPreview>();
    foreach (var submission in submissions)
    {
      if (Hash(submission.ManifestJson) != submission.ManifestHash) continue;
      try { var preview = JsonSerializer.Deserialize<ClientOpenItemAllocationPreview>(submission.ManifestJson); if (preview is not null) result.Add(preview with { SubmissionId = submission.Id }); }
      catch (JsonException) { }
    }
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded)
      return CommandResult<IReadOnlyList<ClientOpenItemAllocationPreview>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<IReadOnlyList<ClientOpenItemAllocationPreview>>.Ok(result);
  }

  public static async Task<CommandResult<ClientOpenItemAllocationReceipt>> ReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid submissionId, Guid commandId, string decision, string reason, string previewDigest, CancellationToken ct = default)
  {
    if (commandId == Guid.Empty || string.IsNullOrWhiteSpace(reason) || decision is not ("APPROVE" or "RETURN"))
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide a review decision, reason, and stable command id.");
    await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var submission = await db.ClientOpenItemAllocationSubmissions.SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == submissionId, ct);
    if (submission is null || submission.CreatedByUserId == actor.UserId || submission.PreviewDigest != previewDigest || Hash(submission.ManifestJson) != submission.ManifestHash ||
        await db.ClientOpenItemAllocationDecisions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submissionId, ct))
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.ProtectedState, "The allocation is stale, already reviewed, or was prepared by this reviewer.");
    var lines = await db.ClientOpenItemAllocationLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SubmissionId == submissionId).OrderBy(x => x.LineNumber).ToListAsync(ct);
    if (decision == "APPROVE")
    {
      var request = new ClientOpenItemAllocationRequest(submission.CommandId, submission.SourceKind, submission.SourceItemId,
        submission.Disposition, submission.Reference, submission.Reason,
        lines.Select(x => new ClientOpenItemAllocationLineRequest(x.LineNumber, x.TargetKind, x.TargetOpenItemId, x.Amount, x.ReversesAllocationLineId)).ToArray(), previewDigest);
      var current = await BuildPreview(db, actor, clientId, request, ct);
      if (!current.Succeeded || current.Value!.Digest != previewDigest)
        return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.ProtectedState, "Open-item availability changed after review preview.");
    }
    var reviewJson = JsonSerializer.Serialize(new { submission.SourceKind, submission.SourceItemId, submission.Disposition, lines, decision, reason = reason.Trim() });
    var row = new ClientOpenItemAllocationDecision { Id = Guid.NewGuid(), FirmId = actor.FirmId, ClientId = clientId, SubmissionId = submissionId,
      CommandId = commandId, IntentHash = Hash($"{submission.IntentHash}|{decision}|{reason.Trim()}"), Decision = decision, Reason = reason.Trim(),
      PreviewDigest = previewDigest, ReviewContextJson = reviewJson, ActorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientOpenItemAllocationDecisions.Add(row);
    await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, Reviewers, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientOpenItemAllocationReceipt>.Fail(ErrorCodes.GateBlocked, "Bookkeeping authority changed before review was committed.");
    await tx.CommitAsync(ct);
    return CommandResult<ClientOpenItemAllocationReceipt>.Ok(new(commandId, submissionId, decision == "APPROVE" ? "APPROVED" : "RETURNED", row.Id));
  }

  private static async Task<CommandResult<ClientOpenItemAllocationPreview>> BuildPreview(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, ClientOpenItemAllocationRequest request, CancellationToken ct)
  {
    var firmId = actor.FirmId;
    request = request with { Lines = request.Lines.OrderBy(x => x.LineNumber).ToArray() };
    if (request.SourceKind is not ("SALES_CREDIT" or "PURCHASE_CREDIT" or "SALES_RECEIPT" or "SUPPLIER_PAYMENT") || request.Disposition is not ("ALLOCATE" or "UNALLOCATE") ||
        request.Lines.Count is < 1 or > 100 || string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Reason) ||
        request.Lines.Any(x => x.Amount <= 0 || x.LineNumber <= 0) || request.Lines.Select(x => x.LineNumber).Distinct().Count() != request.Lines.Count ||
        request.Lines.Select(x => (x.TargetKind, x.TargetOpenItemId)).Distinct().Count() != request.Lines.Count)
      return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose one eligible source and unique positive target lines.");
    var all = await OpenItems(db, actor, clientId, ct);
    var source = all.SingleOrDefault(x => x.Kind == request.SourceKind && x.Id == request.SourceItemId && x.Credit);
    if (source is null) return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.ProtectedState, "The credit or imported settlement source is unavailable or not eligible.");
    var targets = new List<OpenItem>();
    foreach (var line in request.Lines)
    {
      var expected = request.SourceKind is "SALES_CREDIT" or "SALES_RECEIPT" ? "SALES_INVOICE" : "PURCHASE_INVOICE";
      var target = all.SingleOrDefault(x => x.Kind == expected && x.Id == line.TargetOpenItemId && !x.Credit);
      if (line.TargetKind != expected || target is null || target.CounterpartyId != source.CounterpartyId || target.Currency != source.Currency)
        return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Each target must be a posted invoice for the same client, party, and currency.");
      targets.Add(target);
    }
    var approved = await EffectiveLines(db, firmId, clientId, ct);
    var sourceApplied = await EffectiveSourceApplied(db, firmId, clientId, source.Kind, source.Id, ct);
    var sourceAvailable = MoneyPolicy.Normalize(source.Original - sourceApplied);
    var targetAvailable = targets.Select(target => MoneyPolicy.Normalize(target.Original - approved.Where(x => x.Kind == target.Kind && x.Id == target.Id).Sum(x => x.Amount))).ToArray();
    if (request.Disposition == "ALLOCATE")
    {
      if (request.Lines.Any(x => x.ReversesAllocationLineId is not null) || request.Lines.Sum(x => x.Amount) > sourceAvailable ||
          request.Lines.Select((line, i) => line.Amount <= targetAvailable[i]).Any(x => !x))
        return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Allocation exceeds available source or invoice balance.");
    }
    else
    {
      if (request.Lines.Any(x => x.ReversesAllocationLineId is null))
        return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Unallocation must reference an exact prior allocation line.");
      foreach (var line in request.Lines)
      {
        var original = await db.ClientOpenItemAllocationLines.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.Id == line.ReversesAllocationLineId, ct);
        if (original is null || original.TargetKind != line.TargetKind || original.TargetOpenItemId != line.TargetOpenItemId || line.Amount > original.Amount)
          return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "Unallocation must reverse an exact prior line within its remaining amount.");
        var originalSubmission = await db.ClientOpenItemAllocationSubmissions.AsNoTracking().SingleAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.Id == original.SubmissionId, ct);
        var alreadyReversed = await (from reversalLine in db.ClientOpenItemAllocationLines.AsNoTracking()
          join reversalSubmission in db.ClientOpenItemAllocationSubmissions.AsNoTracking() on new { reversalLine.FirmId, reversalLine.ClientId, reversalLine.SubmissionId } equals new { reversalSubmission.FirmId, reversalSubmission.ClientId, SubmissionId = reversalSubmission.Id }
          join reversalDecision in db.ClientOpenItemAllocationDecisions.AsNoTracking() on new { reversalSubmission.FirmId, reversalSubmission.ClientId, SubmissionId = reversalSubmission.Id } equals new { reversalDecision.FirmId, reversalDecision.ClientId, reversalDecision.SubmissionId }
          where reversalLine.FirmId == firmId && reversalLine.ClientId == clientId && reversalLine.ReversesAllocationLineId == original.Id &&
            reversalSubmission.Disposition == "UNALLOCATE" && reversalDecision.Decision == "APPROVE"
          select (decimal?)reversalLine.Amount).SumAsync(ct) ?? 0m;
        if (originalSubmission.SourceKind != source.Kind || originalSubmission.SourceItemId != source.Id || originalSubmission.Disposition != "ALLOCATE" ||
            line.Amount + alreadyReversed > original.Amount ||
            !await db.ClientOpenItemAllocationDecisions.AnyAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.SubmissionId == original.SubmissionId && x.Decision == "APPROVE", ct))
          return CommandResult<ClientOpenItemAllocationPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "The referenced allocation is not an approved line from this source.");
      }
    }
    var digestData = JsonSerializer.Serialize(new { request.SourceKind, request.SourceItemId, request.Disposition, request.Reference,
      source.CounterpartyId, source.Currency, Original = Exact(source.Original), Available = Exact(sourceAvailable),
      targets = request.Lines.Select((line, i) => new { line.LineNumber, line.TargetKind, line.TargetOpenItemId,
        Amount = Exact(line.Amount), line.ReversesAllocationLineId, Available = Exact(targetAvailable[i]) }) });
    var digest = Hash(digestData);
    return CommandResult<ClientOpenItemAllocationPreview>.Ok(new(Guid.Empty, digest, request.Disposition, source.Kind, source.Id,
      source.CounterpartyId, source.Currency, Exact(source.Original), Exact(sourceAvailable),
      request.Lines.Select(x => new ClientOpenItemAllocationPreviewLine(x.LineNumber, x.TargetKind, x.TargetOpenItemId, Exact(x.Amount), x.ReversesAllocationLineId)).ToArray(),
      targetAvailable.Select(Exact).ToArray()));
  }

  private static async Task<List<OpenItem>> OpenItems(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    CancellationToken ct, DateOnly? asOfDate = null)
  {
    var firmId = actor.FirmId;
    var cps = await db.ClientBookkeepingCounterparties.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId).ToDictionaryAsync(x => x.Id, ct);
    var salesInvoices = await db.ClientSalesInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId).ToListAsync(ct);
    var salesCredits = await db.ClientSalesCreditNoteOpenItems.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId).ToListAsync(ct);
    var purchaseInvoices = await db.ClientPurchaseInvoiceOpenItems.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId).ToListAsync(ct);
    var purchaseCredits = await db.ClientPurchaseCreditNoteOpenItems.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId).ToListAsync(ct);
    var result = new List<OpenItem>();
    foreach (var x in salesInvoices) if (cps.TryGetValue(x.CustomerId, out var party) && await IsPostedSalesInvoice(db, x, ct)) result.Add(new("SALES_INVOICE", x.Id, x.InvoiceId, x.CustomerId, party.DisplayName, x.Currency, x.OriginalAmount, x.DueDate, false, x.JournalId));
    foreach (var x in salesCredits) if (cps.TryGetValue(x.CustomerId, out var party) && await IsPostedSalesCredit(db, x, ct)) result.Add(new("SALES_CREDIT", x.Id, x.CreditNoteId, x.CustomerId, party.DisplayName, x.Currency, x.OriginalAmount, null, true, x.JournalId));
    foreach (var x in purchaseInvoices) if (cps.TryGetValue(x.SupplierId, out var party) && await IsPostedPurchaseInvoice(db, x, ct)) result.Add(new("PURCHASE_INVOICE", x.Id, x.InvoiceId, x.SupplierId, party.DisplayName, x.Currency, x.OriginalAmount, x.DueDate, false, x.JournalId));
    foreach (var x in purchaseCredits) if (cps.TryGetValue(x.SupplierId, out var party) && await IsPostedPurchaseCredit(db, x, ct)) result.Add(new("PURCHASE_CREDIT", x.Id, x.CreditNoteId, x.SupplierId, party.DisplayName, x.Currency, x.OriginalAmount, null, true, x.JournalId));
    var manualSettlements = await (from origin in db.ClientManualSettlementOrigins.AsNoTracking()
      join journal in db.ClientOperationalJournals.AsNoTracking() on new { origin.FirmId, origin.ClientId, Id = origin.JournalId } equals new { journal.FirmId, journal.ClientId, journal.Id }
      where origin.FirmId == firmId && origin.ClientId == clientId && journal.Status == "POSTED" &&
        db.ClientOperationalJournalDecisions.Any(d => d.FirmId == origin.FirmId && d.ClientId == origin.ClientId &&
          d.JournalId == origin.JournalId && d.Decision == "APPROVE")
      select new { Origin = origin, Journal = journal }).ToListAsync(ct);
    foreach (var row in manualSettlements)
      if (cps.TryGetValue(row.Origin.CounterpartyId, out var party))
        result.Add(new(row.Origin.SourceKind, row.Origin.Id, row.Journal.Id, row.Origin.CounterpartyId,
        party.DisplayName, row.Journal.Currency, row.Origin.Amount, null, true, row.Journal.Id));
    await AddImportedSettlementSources(db, actor, clientId, cps.Values, result, ct);
    var journalIds = result.Where(x => x.JournalId.HasValue).Select(x => x.JournalId!.Value).Distinct().ToArray();
    var postingDates = await db.ClientOperationalJournals.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      journalIds.Contains(x.Id) && x.Status == "POSTED").ToDictionaryAsync(x => x.Id, x => x.PostingDate, ct);
    return result.Select(x => x with { PostingDate = x.JournalId.HasValue && postingDates.TryGetValue(x.JournalId.Value, out var postingDate)
        ? postingDate : x.PostingDate })
      .Where(x => !asOfDate.HasValue || x.PostingDate.HasValue && x.PostingDate.Value <= asOfDate.Value).ToList();
  }

  private static async Task AddImportedSettlementSources(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    IEnumerable<ClientBookkeepingCounterparty> parties, List<OpenItem> result, CancellationToken ct)
  {
    var clientParties = parties.ToArray();
    if (clientParties.Length == 0) return;
    var candidateRows = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join transaction in db.GeneralLedgerTransactions.AsNoTracking() on new { line.FirmId, line.ClientId, line.EngagementId, line.ImportBatchId, line.TransactionId } equals new { transaction.FirmId, transaction.ClientId, transaction.EngagementId, transaction.ImportBatchId, TransactionId = transaction.Id }
      join batch in db.SourceImportBatches.AsNoTracking() on new { line.FirmId, line.ClientId, line.EngagementId, line.ImportBatchId } equals new { batch.FirmId, batch.ClientId, batch.EngagementId, ImportBatchId = batch.Id }
      join role in db.ClientAccountRoleConfigurations.AsNoTracking() on new { line.FirmId, line.ClientId, AccountId = line.ClientAccountId } equals new { role.FirmId, role.ClientId, AccountId = (Guid?)role.AccountId }
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { role.FirmId, role.ClientId, ConfigurationId = role.Id } equals new { decision.FirmId, decision.ClientId, decision.ConfigurationId }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && batch.SourceKind == "GL" && batch.Status == "SEALED" &&
        batch.ExpectedTransactionCount == batch.AcceptedTransactionCount && batch.ExpectedLineCount == batch.AcceptedLineCount &&
        batch.NormalizedDatasetDigest.Length == 64 && transaction.Currency == batch.Currency && decision.Decision == "APPROVE" &&
        role.EffectiveFrom <= transaction.PostingDate && (role.EffectiveTo == null || role.EffectiveTo >= transaction.PostingDate) &&
        ((role.Role == "AR" && line.Debit == 0 && line.Credit > 0) || (role.Role == "AP" && line.Debit > 0 && line.Credit == 0)) &&
        line.PartyIdentifier != "" && line.OriginalCurrency == transaction.Currency
      select new { Line = line, Transaction = transaction, Batch = batch, Role = role.Role }).ToListAsync(ct);
    if (candidateRows.Count == 0) return;
    var txIds = candidateRows.Select(x => x.Transaction.Id).Distinct().ToArray();
    var legs = await (from line in db.GeneralLedgerLines.AsNoTracking()
      join account in db.ClientAccounts.AsNoTracking() on new { line.FirmId, line.ClientId, AccountId = line.ClientAccountId } equals new { account.FirmId, account.ClientId, AccountId = (Guid?)account.Id }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && txIds.Contains(line.TransactionId)
      select new { line.TransactionId, line.Debit, line.Credit, line.OriginalCurrency, AccountId = account.Id, account.AccountType }).ToListAsync(ct);
    var controlAccounts = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { role.FirmId, role.ClientId, ConfigurationId = role.Id } equals new { decision.FirmId, decision.ClientId, decision.ConfigurationId }
      where role.FirmId == actor.FirmId && role.ClientId == clientId && (role.Role == "AR" || role.Role == "AP") && decision.Decision == "APPROVE"
      select new { role.AccountId, role.EffectiveFrom, role.EffectiveTo }).ToListAsync(ct);
    var linesByTransaction = legs.GroupBy(x => x.TransactionId).ToDictionary(g => g.Key, g => g.ToArray());
    foreach (var group in candidateRows.GroupBy(x => x.Transaction.Id))
    {
      // A single exact AR/AP control line and matching asset leg avoids splitting a combined receipt or inventing cash.
      if (group.Count() != 1 || !linesByTransaction.TryGetValue(group.Key, out var transactionLines)) continue;
      var candidate = group.Single();
      if (!(await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, clientId, candidate.Transaction.EngagementId, RequiredRoles: Preparers, InternalOnly: true), ct)).Succeeded) continue;
      var isCustomer = candidate.Role == "AR";
      var amount = isCustomer ? candidate.Line.Credit : candidate.Line.Debit;
      var controlsOnDate = controlAccounts.Where(x => x.EffectiveFrom <= candidate.Transaction.PostingDate &&
        (x.EffectiveTo == null || x.EffectiveTo >= candidate.Transaction.PostingDate)).Select(x => x.AccountId).ToHashSet();
      var cashLegs = transactionLines.Where(x => x.AccountType == "ASSET" && x.OriginalCurrency == candidate.Line.OriginalCurrency &&
        !controlsOnDate.Contains(x.AccountId)).ToArray();
      var cashDebit = cashLegs.Sum(x => x.Debit);
      var cashCredit = cashLegs.Sum(x => x.Credit);
      if (amount <= 0 || (isCustomer ? cashDebit != amount || cashCredit != 0m : cashCredit != amount || cashDebit != 0m) ||
          transactionLines.Sum(x => x.Debit) != transactionLines.Sum(x => x.Credit)) continue;
      var party = clientParties.SingleOrDefault(x => x.Role == (isCustomer ? "CUSTOMER" : "SUPPLIER") &&
        (candidate.Line.PartyIdentifier == x.ExternalReference || candidate.Line.PartyIdentifier == x.NormalizedExternalIdentity ||
         candidate.Line.PartyIdentifier.Equals(x.Id.ToString("D"), StringComparison.OrdinalIgnoreCase) ||
         candidate.Line.PartyIdentifier.Equals(x.Id.ToString("N"), StringComparison.OrdinalIgnoreCase)));
      if (party is null) continue;
      var kind = isCustomer ? "SALES_RECEIPT" : "SUPPLIER_PAYMENT";
      result.Add(new(kind, candidate.Line.Id, candidate.Transaction.Id, party.Id, party.DisplayName,
        candidate.Line.OriginalCurrency, amount, null, true, null, candidate.Transaction.PostingDate, true));
    }
  }

  private static async Task<bool> IsPostedSalesInvoice(IClientAccountingDbContext db, ClientSalesInvoiceOpenItem x, CancellationToken ct) =>
    await db.ClientSalesInvoiceDecisions.AnyAsync(d => d.FirmId == x.FirmId && d.ClientId == x.ClientId && d.SubmissionId == x.SubmissionId && d.Decision == "APPROVE", ct) &&
    await db.ClientOperationalJournals.AnyAsync(j => j.FirmId == x.FirmId && j.ClientId == x.ClientId && j.Id == x.JournalId && j.Status == "POSTED", ct);
  private static async Task<bool> IsPostedSalesCredit(IClientAccountingDbContext db, ClientSalesCreditNoteOpenItem x, CancellationToken ct) =>
    await db.ClientSalesCreditNoteDecisions.AnyAsync(d => d.FirmId == x.FirmId && d.ClientId == x.ClientId && d.SubmissionId == x.SubmissionId && d.Decision == "APPROVE", ct) &&
    await db.ClientOperationalJournals.AnyAsync(j => j.FirmId == x.FirmId && j.ClientId == x.ClientId && j.Id == x.JournalId && j.Status == "POSTED", ct);
  private static async Task<bool> IsPostedPurchaseInvoice(IClientAccountingDbContext db, ClientPurchaseInvoiceOpenItem x, CancellationToken ct) =>
    await db.ClientPurchaseInvoiceDecisions.AnyAsync(d => d.FirmId == x.FirmId && d.ClientId == x.ClientId && d.SubmissionId == x.SubmissionId && d.Decision == "APPROVE", ct) &&
    await db.ClientOperationalJournals.AnyAsync(j => j.FirmId == x.FirmId && j.ClientId == x.ClientId && j.Id == x.JournalId && j.Status == "POSTED", ct);
  private static async Task<bool> IsPostedPurchaseCredit(IClientAccountingDbContext db, ClientPurchaseCreditNoteOpenItem x, CancellationToken ct) =>
    await db.ClientPurchaseCreditNoteDecisions.AnyAsync(d => d.FirmId == x.FirmId && d.ClientId == x.ClientId && d.SubmissionId == x.SubmissionId && d.Decision == "APPROVE", ct) &&
    await db.ClientOperationalJournals.AnyAsync(j => j.FirmId == x.FirmId && j.ClientId == x.ClientId && j.Id == x.JournalId && j.Status == "POSTED", ct);

  private static async Task<List<(string Kind, Guid Id, decimal Amount)>> EffectiveLines(IClientAccountingDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
  {
    var query = from line in db.ClientOpenItemAllocationLines.AsNoTracking()
                join submission in db.ClientOpenItemAllocationSubmissions.AsNoTracking() on new { line.FirmId, line.ClientId, line.SubmissionId } equals new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id }
                join decision in db.ClientOpenItemAllocationDecisions.AsNoTracking() on new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id } equals new { decision.FirmId, decision.ClientId, decision.SubmissionId }
                where line.FirmId == firmId && line.ClientId == clientId && decision.Decision == "APPROVE"
                select new { line.TargetKind, line.TargetOpenItemId, line.Amount, submission.Disposition };
    var raw = await query.ToListAsync(ct);
    return raw.GroupBy(x => (x.TargetKind, x.TargetOpenItemId)).Select(g => (g.Key.TargetKind, g.Key.TargetOpenItemId,
      MoneyPolicy.Normalize(g.Sum(x => x.Disposition == "ALLOCATE" ? x.Amount : -x.Amount)))).ToList();
  }

  private static async Task<decimal> EffectiveSourceApplied(IClientAccountingDbContext db, Guid firmId, Guid clientId,
    string sourceKind, Guid sourceId, CancellationToken ct)
  {
    var query = from line in db.ClientOpenItemAllocationLines.AsNoTracking()
                join submission in db.ClientOpenItemAllocationSubmissions.AsNoTracking() on new { line.FirmId, line.ClientId, line.SubmissionId } equals new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id }
                join decision in db.ClientOpenItemAllocationDecisions.AsNoTracking() on new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id } equals new { decision.FirmId, decision.ClientId, decision.SubmissionId }
                where line.FirmId == firmId && line.ClientId == clientId && submission.SourceKind == sourceKind &&
                  submission.SourceItemId == sourceId && decision.Decision == "APPROVE"
                select new { line.Amount, submission.Disposition };
    var rows = await query.ToListAsync(ct);
    return MoneyPolicy.Normalize(rows.Sum(x => x.Disposition == "ALLOCATE" ? x.Amount : -x.Amount));
  }

  private static async Task<Dictionary<(string Kind, Guid Id), decimal>> EffectiveSourceLines(IClientAccountingDbContext db,
    Guid firmId, Guid clientId, CancellationToken ct)
  {
    var query = from line in db.ClientOpenItemAllocationLines.AsNoTracking()
                join submission in db.ClientOpenItemAllocationSubmissions.AsNoTracking() on new { line.FirmId, line.ClientId, line.SubmissionId } equals new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id }
                join decision in db.ClientOpenItemAllocationDecisions.AsNoTracking() on new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id } equals new { decision.FirmId, decision.ClientId, decision.SubmissionId }
                where line.FirmId == firmId && line.ClientId == clientId && decision.Decision == "APPROVE"
                select new { submission.SourceKind, submission.SourceItemId, line.Amount, submission.Disposition };
    return (await query.ToListAsync(ct)).GroupBy(x => (x.SourceKind, x.SourceItemId)).ToDictionary(g => g.Key,
      g => MoneyPolicy.Normalize(g.Sum(x => x.Disposition == "ALLOCATE" ? x.Amount : -x.Amount)));
  }

  private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
  private static string Exact(decimal value) => value.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture);
}
