using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientManualSettlementDraftRequest(Guid CommandId, Guid ClientId, Guid PeriodId,
  Guid CounterpartyId, string SourceKind, string JournalNumber, string Description, DateOnly PostingDate,
  string CashAccountCode, decimal Amount, string Reference, string EvidenceReference);
public sealed record ClientManualSettlementPreview(Guid ClientId, Guid PeriodId, Guid CounterpartyId,
  string SourceKind, string Currency, string Amount, string ControlAccountCode, string CashAccountCode,
  string Reference, string EvidenceReference, string Digest);
public sealed record ClientManualSettlementOptions(IReadOnlyList<ClientReportingPeriodOption> Periods,
  IReadOnlyList<ClientCounterpartyOption> Counterparties, IReadOnlyList<ClientCashAccountOption> CashAccounts);
public sealed record ClientReportingPeriodOption(Guid Id, string Code, DateOnly StartDate, DateOnly EndDate, string Currency);
public sealed record ClientCounterpartyOption(Guid Id, string Role, string DisplayName);
public sealed record ClientCashAccountOption(string AccountCode, string AccountName);
public sealed record ClientManualSettlementReceipt(Guid CommandId, Guid JournalId, string Outcome);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult<ClientManualSettlementOptions>> SettlementOptionsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, DateOnly postingDate, string sourceKind,
    CancellationToken ct = default)
  {
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientManualSettlementOptions>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var profile = await NativeProfileAsync(db, actor, clientId, ct);
    if (!profile.Succeeded) return CommandResult<ClientManualSettlementOptions>.Fail(profile.ErrorCode!, profile.Message!);
    var kind = (sourceKind ?? string.Empty).Trim().ToUpperInvariant();
    if (kind is not ("SALES_RECEIPT" or "SUPPLIER_PAYMENT"))
      return CommandResult<ClientManualSettlementOptions>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported settlement type.");
    var role = kind == "SALES_RECEIPT" ? "CUSTOMER" : "SUPPLIER";
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
        x.Status != AccountingWorkflowStates.Closed && x.Currency == profile.Value!.FunctionalCurrency && x.StartDate <= postingDate && x.EndDate >= postingDate)
      .OrderBy(x => x.StartDate).Select(x => new ClientReportingPeriodOption(x.Id, x.PeriodCode, x.StartDate, x.EndDate, x.Currency)).ToListAsync(ct);
    var counterparties = await db.ClientBookkeepingCounterparties.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
        (x.Role == role || x.Role == "BOTH"))
      .OrderBy(x => x.DisplayName).Select(x => new ClientCounterpartyOption(x.Id, x.Role, x.DisplayName)).ToListAsync(ct);
    var chart = await ActiveChartAsync(db, actor.FirmId, clientId, postingDate, ct);
    var cash = chart is null ? [] : await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
        x.ChartVersionId == chart.Id && x.IsPosting && x.Status == AccountingWorkflowStates.Active && x.AccountType == "ASSET")
      .OrderBy(x => x.AccountCode).Select(x => new ClientCashAccountOption(x.AccountCode, x.AccountName)).ToListAsync(ct);
    var validCash = new List<ClientCashAccountOption>();
    foreach (var account in cash)
    {
      var row = await db.ClientAccounts.AsNoTracking().SingleAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == chart!.Id && x.AccountCode == account.AccountCode, ct);
      if (!await ClientAccountRoleWorkspace.UsesControlAsync(db, actor.FirmId, clientId, postingDate, [row.Id], ct)) validCash.Add(account);
    }
    return CommandResult<ClientManualSettlementOptions>.Ok(new(periods, counterparties, validCash));
  }

  public static async Task<CommandResult<ClientManualSettlementPreview>> PreviewSettlementAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientManualSettlementDraftRequest request,
    CancellationToken ct = default)
  {
    var validated = await ValidateSettlementRequestAsync(db, actor, request, ct);
    if (!validated.Succeeded) return CommandResult<ClientManualSettlementPreview>.Fail(validated.ErrorCode!, validated.Message!);
    var source = request.SourceKind.Trim().ToUpperInvariant();
    var control = validated.Value!.Control;
    var cash = validated.Value.Cash;
    var digest = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "client-manual-settlement-v1", actor.FirmId, actor.UserId, request.CommandId,
      request.ClientId, request.PeriodId, request.CounterpartyId, SourceKind = source,
      request.JournalNumber, request.Description, request.PostingDate, CashAccountId = cash.Id,
      control.Id, request.Amount, validated.Value.Currency, request.Reference, request.EvidenceReference
    })));
    return CommandResult<ClientManualSettlementPreview>.Ok(new(request.ClientId, request.PeriodId,
      request.CounterpartyId, source, validated.Value.Currency, request.Amount.ToString("F6", System.Globalization.CultureInfo.InvariantCulture),
      control.AccountCode, cash.AccountCode, request.Reference.Trim(), request.EvidenceReference.Trim(), digest));
  }

  public static async Task<CommandResult<ClientManualSettlementReceipt>> CreateSettlementDraftAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientManualSettlementDraftRequest request,
    string previewDigest, CancellationToken ct = default)
  {
    if (request.CommandId == Guid.Empty)
      return CommandResult<ClientManualSettlementReceipt>.Fail(ErrorCodes.IdempotencyConflict, "A stable command id is required.");
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var auth = await AuthorizeAsync(db, actor, request.ClientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<ClientManualSettlementReceipt>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={request.ClientId} FOR UPDATE").SingleOrDefaultAsync(ct) is null)
      return CommandResult<ClientManualSettlementReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var intent = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { actor.FirmId, actor.UserId, request, previewDigest })));
    var existing = await db.ClientManualSettlementOrigins.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (existing is not null)
    {
      if (existing.IntentHash != intent)
        return CommandResult<ClientManualSettlementReceipt>.Fail(ErrorCodes.IdempotencyConflict, "The settlement command id was reused for different intent.");
      await tx.CommitAsync(ct);
      return CommandResult<ClientManualSettlementReceipt>.Ok(new(request.CommandId, existing.JournalId, "DRAFT"));
    }
    var preview = await PreviewSettlementAsync(db, actor, request, ct);
    if (!preview.Succeeded || preview.Value!.Digest != previewDigest)
      return CommandResult<ClientManualSettlementReceipt>.Fail(ErrorCodes.StaleRevision, "The settlement preview is stale or invalid.");
    var settlementKind = request.SourceKind.Trim().ToUpperInvariant();
    var customerReceipt = settlementKind == "SALES_RECEIPT";
    var controlCode = preview.Value.ControlAccountCode;
    var amount = request.Amount;
    var journalRequest = new ClientOperationalJournalCreateRequest(request.ClientId, request.PeriodId,
      request.JournalNumber.Trim(), request.Description.Trim(), request.PostingDate,
      customerReceipt
        ? [new(preview.Value.CashAccountCode, "Already received client funds", amount, 0m), new(controlCode, "Settle customer receivable", 0m, amount)]
        : [new(controlCode, "Settle supplier payable", amount, 0m), new(preview.Value.CashAccountCode, "Already paid client funds", 0m, amount)]);
    var origin = new ClientManualSettlementOrigin
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      CounterpartyId = request.CounterpartyId, SourceKind = settlementKind, Amount = amount,
      Reference = request.Reference.Trim(), EvidenceReference = request.EvidenceReference.Trim(),
      CommandId = request.CommandId, IntentHash = intent, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    var created = await CreateDraftCoreAsync(db, actor, journalRequest, false, ct, origin);
    if (!created.Succeeded) return CommandResult<ClientManualSettlementReceipt>.Fail(created.ErrorCode!, created.Message!);
    if (!(await AuthorizeAsync(db, actor, request.ClientId, Preparers, ct)).Succeeded ||
        !(await NativeProfileAsync(db, actor, request.ClientId, ct)).Succeeded)
      return CommandResult<ClientManualSettlementReceipt>.Fail(ErrorCodes.GateBlocked, "Bookkeeping authority changed while preparing the settlement.");
    await tx.CommitAsync(ct);
    return CommandResult<ClientManualSettlementReceipt>.Ok(new(request.CommandId, created.Value, "DRAFT"));
  }

  private sealed record SettlementAccounts(string Currency, (Guid Id, string AccountCode, string AccountName, string AccountType) Control,
    (Guid Id, string AccountCode, string AccountName, string AccountType) Cash);

  private static async Task<CommandResult<SettlementAccounts>> ValidateSettlementRequestAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientManualSettlementDraftRequest request, CancellationToken ct)
  {
    var kind = (request.SourceKind ?? string.Empty).Trim().ToUpperInvariant();
    var number = (request.JournalNumber ?? string.Empty).Trim();
    var description = (request.Description ?? string.Empty).Trim();
    var cashCode = (request.CashAccountCode ?? string.Empty).Trim();
    var reference = (request.Reference ?? string.Empty).Trim();
    var evidence = (request.EvidenceReference ?? string.Empty).Trim();
    if (request.CommandId == Guid.Empty || request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || request.CounterpartyId == Guid.Empty ||
        kind is not ("SALES_RECEIPT" or "SUPPLIER_PAYMENT") || number.Length is 0 or > 100 || description.Length is 0 or > 1000 ||
        cashCode.Length is 0 or > 100 || request.Amount <= 0 || request.Amount != MoneyPolicy.Normalize(request.Amount) ||
        reference.Length is 0 or > 200 || evidence.Length is 0 or > 1000)
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide a completed settlement, exact amount, cash account, external reference and evidence reference.");
    if (!(await AuthorizeAsync(db, actor, request.ClientId, Preparers, ct)).Succeeded)
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var profile = await NativeProfileAsync(db, actor, request.ClientId, ct);
    if (!profile.Succeeded) return CommandResult<SettlementAccounts>.Fail(profile.ErrorCode!, profile.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.Id == request.PeriodId, ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || request.PostingDate < period.StartDate || request.PostingDate > period.EndDate ||
        period.Currency != profile.Value!.FunctionalCurrency)
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.GateBlocked, "Choose an open functional-currency client period containing the settlement date.");
    var chart = await ActiveChartAsync(db, actor.FirmId, request.ClientId, request.PostingDate, ct);
    if (chart is null) return CommandResult<SettlementAccounts>.Fail(ErrorCodes.GateBlocked, "Exactly one approved chart version must cover the settlement date.");
    var controlRole = kind == "SALES_RECEIPT" ? "AR" : "AP";
    var controls = await (from role in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { role.FirmId, role.ClientId, ConfigurationId = role.Id } equals new { decision.FirmId, decision.ClientId, decision.ConfigurationId }
      join account in db.ClientAccounts.AsNoTracking() on new { role.FirmId, role.ClientId, AccountId = role.AccountId } equals new { account.FirmId, account.ClientId, AccountId = account.Id }
      where role.FirmId == actor.FirmId && role.ClientId == request.ClientId && role.ChartVersionId == chart.Id && role.Role == controlRole &&
        decision.Decision == "APPROVE" && role.EffectiveFrom <= request.PostingDate && (role.EffectiveTo == null || role.EffectiveTo >= request.PostingDate) &&
        account.IsPosting && account.Status == AccountingWorkflowStates.Active
      select new { account.Id, account.AccountCode, account.AccountName, account.AccountType }).ToListAsync(ct);
    if (controls.Count != 1)
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.GateBlocked, "Exactly one approved effective AR/AP control account is required.");
    var control = controls.Single();
    var cash = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.ChartVersionId == chart.Id && x.AccountCode == cashCode && x.IsPosting && x.Status == AccountingWorkflowStates.Active)
      .Select(x => new { x.Id, x.AccountCode, x.AccountName, x.AccountType }).SingleOrDefaultAsync(ct);
    if (cash is null || cash.AccountType != "ASSET" || cash.Id == control.Id ||
        await ClientAccountRoleWorkspace.UsesControlAsync(db, actor.FirmId, request.ClientId, request.PostingDate, [cash.Id], ct))
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose an active cash/bank asset account that is not an AR/AP control account.");
    var party = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == request.ClientId && x.Id == request.CounterpartyId, ct);
    var partyRole = kind == "SALES_RECEIPT" ? "CUSTOMER" : "SUPPLIER";
    if (party is null || party.Role != partyRole && party.Role != "BOTH")
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a matching customer or supplier in this client.");
    if (await db.ClientManualSettlementOrigins.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.Reference.ToLower() == reference.ToLower() && x.SourceKind == kind, ct))
      return CommandResult<SettlementAccounts>.Fail(ErrorCodes.IdempotencyConflict, "This external settlement reference is already recorded for the client.");
    return CommandResult<SettlementAccounts>.Ok(new(profile.Value.FunctionalCurrency,
      (control.Id, control.AccountCode, control.AccountName, control.AccountType), (cash.Id, cash.AccountCode, cash.AccountName, cash.AccountType)));
  }

  private static async Task<bool> ValidateManualSettlementJournalAsync(IClientAccountingDbContext db, Guid firmId, Guid clientId,
    Guid journalId, DateOnly postingDate, IReadOnlyList<ClientOperationalJournalLine> lines, CancellationToken ct)
  {
    var origin = await db.ClientManualSettlementOrigins.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.JournalId == journalId, ct);
    if (origin is null || lines.Count != 2) return false;
    var role = origin.SourceKind == "SALES_RECEIPT" ? "AR" : "AP";
    var controlRows = await (from configuration in db.ClientAccountRoleConfigurations.AsNoTracking()
      join decision in db.ClientAccountRoleDecisions.AsNoTracking() on new { configuration.FirmId, configuration.ClientId, ConfigurationId = configuration.Id } equals new { decision.FirmId, decision.ClientId, decision.ConfigurationId }
      where configuration.FirmId == firmId && configuration.ClientId == clientId && configuration.Role == role && decision.Decision == "APPROVE" &&
        configuration.EffectiveFrom <= postingDate && (configuration.EffectiveTo == null || configuration.EffectiveTo >= postingDate)
      select configuration.AccountId).Distinct().ToListAsync(ct);
    if (controlRows.Count != 1) return false;
    var controlId = controlRows[0];
    var control = lines.SingleOrDefault(x => x.ClientAccountId == controlId);
    var cash = lines.SingleOrDefault(x => x.ClientAccountId != controlId);
    if (control is null || cash is null) return false;
    var account = await db.ClientAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.ClientId == clientId && x.Id == cash.ClientAccountId, ct);
    if (account is null || account.AccountType != "ASSET" ||
        await ClientAccountRoleWorkspace.UsesControlAsync(db, firmId, clientId, postingDate, [cash.ClientAccountId], ct)) return false;
    var amount = origin.Amount;
    var directionValid = origin.SourceKind == "SALES_RECEIPT"
      ? control.Debit == 0m && control.Credit == amount && cash.Debit == amount && cash.Credit == 0m
      : control.Debit == amount && control.Credit == 0m && cash.Debit == 0m && cash.Credit == amount;
    var partyRole = origin.SourceKind == "SALES_RECEIPT" ? "CUSTOMER" : "SUPPLIER";
    return directionValid && await db.ClientBookkeepingCounterparties.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.Id == origin.CounterpartyId && (x.Role == partyRole || x.Role == "BOTH"), ct);
  }
}
