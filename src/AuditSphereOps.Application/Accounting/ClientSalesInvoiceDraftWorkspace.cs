using System.Globalization;
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

public sealed record ClientSalesInvoiceDraftRequest(Guid CommandId, Guid? InvoiceId, long ExpectedRevision,
  string DraftReference, string SourceReference, Guid PeriodId, Guid CustomerId, DateOnly DocumentDate,
  DateOnly AccountingDate, DateOnly SupplyDate, DateOnly DueDate, string Currency,
  ClientInvoiceMoneyPolicy Policy, IReadOnlyList<ClientInvoiceLineInput> Lines, string EvidenceReference);
public sealed record ClientInvoiceSellerSnapshot(Guid ClientId, string LegalName, string? RegistrationNumber,
  string? TaxRegistrationNumber, string? Jurisdiction);
public sealed record ClientSalesInvoiceDraftLine(int LineNumber, Guid AccountId, string AccountCode, string AccountName,
  string Description, string Quantity, string UnitPrice, string Discount, string Net, string Gross);
public sealed record ClientSalesInvoiceDraftSnapshot(string Version, string EngineVersion, Guid ProfileId, string ProfileRevision,
  Guid ChartVersionId, string DraftReference, string SourceReference, string Currency, string DocumentDate,
  string AccountingDate, string SupplyDate, string DueDate, ClientInvoiceSellerSnapshot Seller,
  ClientCounterpartyView Customer, ClientInvoiceMoneyPolicy Policy, IReadOnlyList<ClientSalesInvoiceDraftLine> Lines,
  string Net, string Gross, string TaxTreatment, string EvidenceReference, bool PossibleDuplicateSourceReference);
public sealed record ClientSalesInvoiceDraftView(Guid Id, Guid InvoiceId, Guid ClientId, Guid PeriodId, string Revision,
  Guid? PreviousRevisionId, Guid CommandId, Guid CreatedByUserId, string CreatedAt, string SnapshotHash,
  string Status, bool Posted, bool Issued, ClientSalesInvoiceDraftSnapshot Snapshot);

/// <summary>Append-only unposted client sales preparation. Official numbering, issue and ledger commitment are separate workflows.</summary>
public static class ClientSalesInvoiceDraftWorkspace
{
  private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: Roles, InternalOnly: true), ct);
  private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
  private static string Money(decimal amount, int precision) => amount.ToString("F" + precision, CultureInfo.InvariantCulture);
  private static string Date(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  private static CommandResult<ClientSalesInvoiceDraftView> View(ClientSalesInvoiceDraft row)
  {
    if (Hash(row.SnapshotJson) != row.SnapshotHash) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ProtectedState, "The retained draft snapshot identity is invalid.");
    try
    {
      var snapshot = JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(row.SnapshotJson);
      if (snapshot is null || snapshot.Seller is null || snapshot.Customer is null || snapshot.Policy is null || snapshot.Policy.DecimalPlaces is < 0 or > 6 || snapshot.Lines is null || snapshot.Version != "client-sales-draft-v1" || snapshot.Seller.ClientId != row.ClientId || snapshot.Customer.ClientId != row.ClientId || snapshot.Customer.Id != row.CustomerId ||
          snapshot.ChartVersionId != row.ChartVersionId || snapshot.Currency != row.Currency || snapshot.DraftReference != row.DraftReference || snapshot.SourceReference != row.SourceReference ||
          snapshot.Net != Money(row.NetAmount, snapshot.Policy.DecimalPlaces) || snapshot.Gross != Money(row.GrossAmount, snapshot.Policy.DecimalPlaces))
        return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ProtectedState, "The retained draft does not match its scoped header.");
      return CommandResult<ClientSalesInvoiceDraftView>.Ok(new(row.Id, row.InvoiceId, row.ClientId, row.PeriodId,
        row.Revision.ToString(CultureInfo.InvariantCulture), row.PreviousRevisionId, row.CommandId, row.CreatedByUserId,
        row.CreatedAt.ToUniversalTime().ToString("O"), row.SnapshotHash, "DRAFT", false, false, snapshot));
    }
    catch (JsonException) { return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ProtectedState, "The retained snapshot could not be read."); }
  }

  public static async Task<CommandResult<ClientSalesInvoiceDraftView>> SaveAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, ClientSalesInvoiceDraftRequest request, CancellationToken ct = default)
  {
    var reference = (request.DraftReference ?? "").Trim(); var source = (request.SourceReference ?? "").Trim(); var evidence = (request.EvidenceReference ?? "").Trim();
    if (request.CommandId == Guid.Empty || clientId == Guid.Empty || request.PeriodId == Guid.Empty || request.CustomerId == Guid.Empty ||
        reference.Length is 0 or > 100 || source.Length > 200 || evidence.Length > 2000 || request.DocumentDate == default || request.SupplyDate == default ||
        request.DueDate < request.DocumentDate || request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue ||
        (request.InvoiceId is null ? request.ExpectedRevision != 0 : request.InvoiceId == Guid.Empty || request.ExpectedRevision < 1) ||
        request.Lines is null || request.Lines.Count is < 1 or > 100 || request.Lines.Any(x => x is null))
      return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide an exact draft request, dates, selected client/customer/period and bounded lines.");
    if (request.Lines.Any(x => x.TaxTreatment != "NONE" || x.Taxes is null || x.Taxes.Count != 0))
      return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Tax-bearing drafts require a separately approved client tax profile; that draft path is not yet available.");
    var normalized = request with { DraftReference = reference, SourceReference = source, EvidenceReference = evidence };
    var intent = Hash(JsonSerializer.Serialize(new { Version = "client-sales-draft-intent-v1", actor.FirmId, clientId, ActorId = actor.UserId, Request = normalized }));
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var client = await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (client is null) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var receipt = await db.ClientSalesInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == request.CommandId, ct);
    if (receipt is not null)
    {
      if (receipt.IntentHash != intent) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.IdempotencyConflict, "This request key is already bound to different draft content.");
      if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      await tx.CommitAsync(ct); return View(receipt);
    }
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceMode == ClientAccountingSourceModes.NativeBookkeeping, ct);
    if (profile is null || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "A current accepted native bookkeeping service is required.");
    var calculation = ClientInvoiceCalculator.Calculate("INVOICE", request.Currency, profile.FunctionalCurrency, request.Policy, request.Lines);
    if (!calculation.Valid) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, calculation.Error!);
    var period = await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={request.PeriodId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || request.AccountingDate < period.StartDate || request.AccountingDate > period.EndDate || period.Currency != request.Currency)
      return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Choose an open matching client period containing the accounting date.");
    var customer = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == request.CustomerId && (x.Role == "CUSTOMER" || x.Role == "BOTH"), ct);
    if (customer is null) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Choose a customer belonging to this client book.");
    var charts = await db.ClientChartVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Status == AccountingWorkflowStates.Approved && x.EffectiveFrom <= request.AccountingDate && (x.EffectiveTo == null || x.EffectiveTo >= request.AccountingDate)).Take(2).ToListAsync(ct);
    if (charts.Count != 1) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Exactly one approved client chart must cover the accounting date.");
    var chart = charts[0]; var codes = request.Lines.Select(x => x.AccountCode).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.ChartVersionId == chart.Id && x.IsPosting && x.Status == AccountingWorkflowStates.Active && x.AccountType == "INCOME" && codes.Contains(x.AccountCode)).ToListAsync(ct);
    if (accounts.Count != codes.Length) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Every sales line requires an active income posting account in the approved client chart.");
    ClientSalesInvoiceDraft? previous = null;
    if (request.InvoiceId is { } invoiceId)
    {
      previous = await db.ClientSalesInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId).OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
      if (previous is null || previous.CreatedByUserId != actor.UserId) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Only the assigned draft maker may revise this invoice.");
      if (previous.Revision != request.ExpectedRevision || previous.DraftReference != reference)
        return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.StaleRevision, "Read the latest revision; the draft reference is fixed.");
    }
    else if (await db.ClientSalesInvoiceDrafts.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Revision == 1 && x.DraftReference == reference, ct))
      return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.IdempotencyConflict, "The client draft reference already exists. Open the retained draft.");
    var currentInvoiceId = previous?.InvoiceId ?? Guid.Empty;
    var possibleDuplicateSource = source.Length > 0 && await db.ClientSalesInvoiceDrafts.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.SourceReference == source && x.InvoiceId != currentInvoiceId, ct);
    var customerSnapshot = await ClientBookkeepingCounterpartyWorkspace.EffectiveView(db, customer, ct);
    var lines = calculation.Lines.Select(l => { var account = accounts.Single(x => x.AccountCode == l.AccountCode); return new ClientSalesInvoiceDraftLine(l.LineNumber, account.Id, account.AccountCode, account.AccountName, l.Description,
      Money(l.Quantity, 6), Money(l.UnitPrice, 6), Money(l.Discount, 6), Money(l.Net, request.Policy.DecimalPlaces), Money(l.Gross, request.Policy.DecimalPlaces)); }).ToArray();
    var snapshot = new ClientSalesInvoiceDraftSnapshot("client-sales-draft-v1", calculation.EngineVersion, profile.Id, profile.Revision.ToString(CultureInfo.InvariantCulture), chart.Id,
      reference, source, request.Currency, Date(request.DocumentDate), Date(request.AccountingDate), Date(request.SupplyDate), Date(request.DueDate),
      new(clientId, client.LegalName, client.RegistrationNumber, client.TaxRegistrationNumber, client.Jurisdiction), customerSnapshot, request.Policy, lines,
      Money(calculation.Net, request.Policy.DecimalPlaces), Money(calculation.Gross, request.Policy.DecimalPlaces), "NONE", evidence, possibleDuplicateSource);
    var json = JsonSerializer.Serialize(snapshot);
    if (json.Length > 500000) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "The bounded draft snapshot is too large.");
    var draft = new ClientSalesInvoiceDraft { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, InvoiceId = previous?.InvoiceId ?? Guid.CreateVersion7(),
      Revision = previous is null ? 1 : previous.Revision + 1, PreviousRevisionId = previous?.Id, PreviousRevision = previous?.Revision,
      CommandId = request.CommandId, IntentHash = intent, DraftReference = reference, SourceReference = source, PeriodId = period.Id, CustomerId = customer.Id,
      ChartVersionId = chart.Id, Currency = request.Currency, NetAmount = calculation.Net, GrossAmount = calculation.Gross, SnapshotJson = json, SnapshotHash = Hash(json), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientSalesInvoiceDrafts.Add(draft); await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct))
      return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.GateBlocked, "Authority or bookkeeping service changed before saving.");
    await tx.CommitAsync(ct); return View(draft);
  }

  public static async Task<CommandResult<ClientSalesInvoiceDraftView>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid invoiceId, long? revision = null, CancellationToken ct = default)
  {
    if (revision is < 1) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a positive saved revision.");
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var query = db.ClientSalesInvoiceDrafts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.InvoiceId == invoiceId);
    if (revision is { } number) query = query.Where(x => x.Revision == number);
    var row = await query.OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (row is null || !(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return View(row);
  }

  public static async Task<CommandResult<ClientSalesInvoiceDraftView>> GetByCommandAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid commandId, CancellationToken ct = default)
  {
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var row = await db.ClientSalesInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CreatedByUserId == actor.UserId && x.CommandId == commandId, ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<ClientSalesInvoiceDraftView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return row is null ? CommandResult<ClientSalesInvoiceDraftView>.Fail("command.receipt-not-found", "No saved draft outcome was found for this actor/request.") : View(row);
  }
}
