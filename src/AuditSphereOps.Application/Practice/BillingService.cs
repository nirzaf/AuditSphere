using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuditSphereOps.Application.Practice;

public sealed record CreateBillingAccountRequest(Guid PracticeClientId, string Currency);

public sealed record InvoiceLineRequest(
  string Description,
  decimal Quantity,
  decimal UnitPrice,
  string? SourceKind = null,
  Guid? SourceId = null,
  long? SourceRevision = null);

public sealed record CreateInvoiceDraftRequest(
  Guid BillingAccountId,
  string InvoiceNumber,
  IReadOnlyList<InvoiceLineRequest> Lines,
  decimal Tax = 0);

public sealed record RecordReceiptRequest(
  Guid BillingAccountId,
  decimal Amount,
  string Reference,
  DateTimeOffset? ReceivedAt = null);

public sealed record AllocateReceiptRequest(Guid ReceiptId, Guid InvoiceId, decimal Amount);

public sealed record IssueCreditNoteRequest(
  Guid InvoiceId,
  string NoteNumber,
  decimal Amount,
  string Reason);

public sealed record InvoiceBalance(
  Guid InvoiceId,
  string Currency,
  decimal Total,
  decimal Credited,
  decimal Allocated,
  decimal Outstanding);

/// <summary>
/// Local billing artifact commands. Posting is a billing-state transition gated by an
/// approved finance profile; firm-ledger journal creation remains the next slice.
/// </summary>
public static class BillingService
{
  private static readonly string[] BillingRoles = ["FinanceManager", "FinanceReviewer"];
  private static readonly string[] ReviewerRoles = ["FinanceReviewer"];
  private static readonly string[] ManagerRoles = ["FinanceManager"];

  public static async Task<CommandResult<Guid>> CreateBillingAccountAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateBillingAccountRequest request,
    CancellationToken ct = default)
  {
    var currencyError = CurrencyError(request.Currency);
    if (currencyError is not null)
      return CommandResult<Guid>.Fail("billing.invalid", currencyError);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.PracticeClientId, RequiredRoles: BillingRoles,
        InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var clientGuard = await LockClientAsync(db, actor.FirmId, request.PracticeClientId, ct);
    if (!clientGuard.Succeeded) return CommandResult<Guid>.Fail(clientGuard.ErrorCode!, clientGuard.Message!);
    var client = await db.PracticeClients.SingleOrDefaultAsync(x =>
      x.Id == request.PracticeClientId && x.FirmId == actor.FirmId, ct);
    if (client is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var currency = request.Currency.Trim().ToUpperInvariant();
    var existing = await db.BillingAccounts.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PracticeClientId == client.Id, ct);
    if (existing is not null)
      return string.Equals(existing.Currency, currency, StringComparison.OrdinalIgnoreCase)
        ? await CommitValueAsync(tx, existing.Id, ct)
        : CommandResult<Guid>.Fail("billing.currency-conflict", "The client billing account already has another currency.");

    var account = new BillingAccount
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PracticeClientId = client.Id,
      Currency = currency, CreatedAt = DateTimeOffset.UtcNow
    };
    db.BillingAccounts.Add(account);
    client.BillingAccountId = account.Id;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(account.Id);
  }

  public static async Task<CommandResult<Guid>> CreateInvoiceDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateInvoiceDraftRequest request,
    CancellationToken ct = default)
  {
    var validation = ValidateInvoice(request);
    if (validation is not null) return CommandResult<Guid>.Fail("billing.invalid", validation);
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, BillingRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var normalized = request.Lines.Select(line =>
      (Request: line, Total: MoneyPolicy.Normalize(line.Quantity * line.UnitPrice))).ToList();
    var subtotal = MoneyPolicy.Normalize(normalized.Sum(x => x.Total));
    var total = MoneyPolicy.Normalize(subtotal + request.Tax);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, account.PracticeClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    var lockedAccount = await LoadBillingAccountForUpdateAsync(db, actor.FirmId, account.Id, ct);
    if (lockedAccount is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await db.Invoices.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.InvoiceNumber == request.InvoiceNumber.Trim(), ct))
      return CommandResult<Guid>.Fail("billing.duplicate", "Invoice identity is already used.");

    var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var item in normalized.Where(x => x.Request.SourceId.HasValue))
    {
      var line = item.Request;
      var key = SourceKey(line.SourceKind!, line.SourceId!.Value, line.SourceRevision!.Value);
      if (!sourceKeys.Add(key) || await db.BillingSourceAllocations.AnyAsync(x =>
          x.FirmId == actor.FirmId && x.SourceKind == line.SourceKind!.Trim().ToUpperInvariant() &&
          x.SourceId == line.SourceId.Value && x.SourceRevision == line.SourceRevision.Value, ct))
        return CommandResult<Guid>.Fail("billing.source-duplicate", "An approved source is already allocated to an invoice.");
      var source = await ValidateSourceAsync(db, actor.FirmId, lockedAccount.Currency, line, item.Total, ct);
      if (source is not null) return CommandResult<Guid>.Fail(source.ErrorCode!, source.Message!);
    }

    var invoice = new Invoice
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, BillingAccountId = lockedAccount.Id,
      InvoiceNumber = request.InvoiceNumber.Trim(), Currency = lockedAccount.Currency,
      Subtotal = subtotal, Tax = request.Tax, Total = total, CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Invoices.Add(invoice);
    foreach (var item in normalized)
    {
      var line = item.Request;
      var entity = new InvoiceLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, InvoiceId = invoice.Id,
        Description = line.Description.Trim(), Quantity = line.Quantity, UnitPrice = line.UnitPrice,
        LineTotal = item.Total, SourceKind = line.SourceId.HasValue
          ? line.SourceKind!.Trim().ToUpperInvariant() : string.Empty,
        SourceId = line.SourceId, SourceRevision = line.SourceId.HasValue ? line.SourceRevision : null
      };
      db.InvoiceLines.Add(entity);
      if (line.SourceId.HasValue)
        db.BillingSourceAllocations.Add(new BillingSourceAllocation
        {
          Id = Guid.CreateVersion7(), FirmId = actor.FirmId, InvoiceLineId = entity.Id,
          SourceKind = entity.SourceKind, SourceId = line.SourceId.Value,
          SourceRevision = line.SourceRevision!.Value, Quantity = line.Quantity,
          Amount = item.Total, CreatedAt = DateTimeOffset.UtcNow
        });
    }
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail("billing.conflict", "The billing identity or source allocation changed; retry from current state.");
    }
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(invoice.Id);
  }

  public static Task<CommandResult> SubmitInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, CancellationToken ct = default) =>
    TransitionInvoiceAsync(db, actor, invoiceId, BillingStates.InvoiceDraft,
      BillingStates.InvoiceReviewRequired, BillingRoles, null, ct);

  public static Task<CommandResult> ApproveInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, CancellationToken ct = default) =>
    TransitionInvoiceAsync(db, actor, invoiceId, BillingStates.InvoiceReviewRequired,
      BillingStates.InvoiceApproved, ReviewerRoles, actor.UserId, ct);

  public static Task<CommandResult> PostInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, CancellationToken ct = default) =>
    PostInvoiceCoreAsync(db, actor, invoiceId, ct);

  public static Task<CommandResult> SendInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, CancellationToken ct = default) =>
    TransitionInvoiceAsync(db, actor, invoiceId, BillingStates.InvoicePosted,
      BillingStates.InvoiceSent, BillingRoles, null, ct);

  public static async Task<CommandResult<Guid>> RecordReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, RecordReceiptRequest request,
    CancellationToken ct = default)
  {
    if (request.Amount <= 0 || MoneyPolicy.Normalize(request.Amount) != request.Amount)
      return CommandResult<Guid>.Fail("billing.invalid", "Receipt amount must be positive and have at most 6 decimals.");
    if (string.IsNullOrWhiteSpace(request.Reference) || request.Reference.Trim().Length > 200)
      return CommandResult<Guid>.Fail("billing.invalid", "Receipt reference is required and bounded.");
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, BillingRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, account.PracticeClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    var lockedAccount = await LoadBillingAccountForUpdateAsync(db, actor.FirmId, account.Id, ct);
    if (lockedAccount is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var receipt = new Receipt
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, BillingAccountId = lockedAccount.Id,
      Amount = request.Amount, Currency = lockedAccount.Currency, Reference = request.Reference.Trim(),
      RecordedByUserId = actor.UserId, ReceivedAt = request.ReceivedAt ?? DateTimeOffset.UtcNow
    };
    db.Receipts.Add(receipt);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(receipt.Id);
  }

  public static async Task<CommandResult> AllocateReceiptAsync(
    IAuditSphereDbContext db, ActorContext actor, AllocateReceiptRequest request,
    CancellationToken ct = default)
  {
    if (request.Amount <= 0 || MoneyPolicy.Normalize(request.Amount) != request.Amount)
      return CommandResult.Fail("billing.invalid", "Allocation amount must be positive and have at most 6 decimals.");
    var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.ReceiptId && x.FirmId == actor.FirmId, ct);
    var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.InvoiceId && x.FirmId == actor.FirmId, ct);
    if (receipt is null || invoice is null || receipt.BillingAccountId != invoice.BillingAccountId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == receipt.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, BillingRoles, ct);
    if (!auth.Succeeded) return auth;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, account.PracticeClientId, ct);
    if (!guard.Succeeded) return guard;
    var lockedAccount = await LoadBillingAccountForUpdateAsync(db, actor.FirmId, account.Id, ct);
    if (lockedAccount is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var lockedReceipt = await LoadReceiptForUpdateAsync(db, actor.FirmId, request.ReceiptId, ct);
    var lockedInvoice = await LoadInvoiceForUpdateAsync(db, actor.FirmId, request.InvoiceId, ct);
    if (lockedReceipt is null || lockedInvoice is null || lockedReceipt.BillingAccountId != lockedInvoice.BillingAccountId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedReceipt.Status != BillingStates.ReceiptRecorded ||
        lockedInvoice.Status is not (BillingStates.InvoicePosted or BillingStates.InvoiceSent) ||
        !string.Equals(lockedInvoice.Currency, lockedAccount.Currency, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Receipt allocation requires a posted invoice in the billing currency.");

    var receiptAllocated = await db.ReceiptAllocations.Where(x =>
      x.FirmId == actor.FirmId && x.ReceiptId == lockedReceipt.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    var invoiceAllocated = await db.ReceiptAllocations.Where(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == lockedInvoice.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    var credited = await db.CreditNotes.Where(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == lockedInvoice.Id && x.Status == BillingStates.CreditIssued)
      .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    if (request.Amount > lockedReceipt.Amount - receiptAllocated ||
        request.Amount > lockedInvoice.Total - invoiceAllocated - credited)
      return CommandResult.Fail("billing.over-allocation", "Allocation exceeds the receipt or invoice balance.");
    db.ReceiptAllocations.Add(new ReceiptAllocation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ReceiptId = lockedReceipt.Id,
      InvoiceId = lockedInvoice.Id, Amount = request.Amount, CreatedAt = DateTimeOffset.UtcNow
    });
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult.Fail("billing.conflict", "The receipt allocation changed; reload the current balance.");
    }
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> IssueCreditNoteAsync(
    IAuditSphereDbContext db, ActorContext actor, IssueCreditNoteRequest request,
    CancellationToken ct = default)
  {
    if (request.Amount <= 0 || MoneyPolicy.Normalize(request.Amount) != request.Amount)
      return CommandResult<Guid>.Fail("billing.invalid", "Credit amount must be positive and have at most 6 decimals.");
    if (string.IsNullOrWhiteSpace(request.NoteNumber) || string.IsNullOrWhiteSpace(request.Reason))
      return CommandResult<Guid>.Fail("billing.invalid", "Credit note number and reason are required.");
    var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.InvoiceId && x.FirmId == actor.FirmId, ct);
    if (invoice is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoice.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, ManagerRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, account.PracticeClientId, ct);
    if (!guard.Succeeded) return CommandResult<Guid>.Fail(guard.ErrorCode!, guard.Message!);
    var lockedAccount = await LoadBillingAccountForUpdateAsync(db, actor.FirmId, account.Id, ct);
    var lockedInvoice = await LoadInvoiceForUpdateAsync(db, actor.FirmId, invoice.Id, ct);
    if (lockedAccount is null || lockedInvoice is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedInvoice.Status is not (BillingStates.InvoicePosted or BillingStates.InvoiceSent) ||
        lockedInvoice.Currency is null || !string.Equals(lockedInvoice.Currency, lockedAccount.Currency, StringComparison.OrdinalIgnoreCase))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A credit note requires a posted invoice in the billing currency.");
    var credited = await db.CreditNotes.Where(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == lockedInvoice.Id && x.Status == BillingStates.CreditIssued)
      .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    if (request.Amount > lockedInvoice.Total - credited)
      return CommandResult<Guid>.Fail("billing.over-credit", "Credit notes cannot exceed the invoice value.");
    if (await db.CreditNotes.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.NoteNumber == request.NoteNumber.Trim(), ct))
      return CommandResult<Guid>.Fail("billing.duplicate", "Credit note identity is already used.");
    var note = new CreditNote
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, BillingAccountId = lockedAccount.Id,
      InvoiceId = lockedInvoice.Id, NoteNumber = request.NoteNumber.Trim(), Currency = lockedAccount.Currency,
      Amount = request.Amount, Reason = request.Reason.Trim(), CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.CreditNotes.Add(note);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(note.Id);
  }

  public static async Task<CommandResult<InvoiceBalance>> GetInvoiceBalanceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, CancellationToken ct = default)
  {
    var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
    if (invoice is null) return CommandResult<InvoiceBalance>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoice.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult<InvoiceBalance>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, BillingRoles, ct);
    if (!auth.Succeeded) return CommandResult<InvoiceBalance>.Fail(auth.ErrorCode!, auth.Message!);
    var credited = await db.CreditNotes.Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId &&
      x.Status == BillingStates.CreditIssued).SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    var allocated = await db.ReceiptAllocations.Where(x => x.FirmId == actor.FirmId && x.InvoiceId == invoiceId)
      .SumAsync(x => (decimal?)x.Amount, ct) ?? 0;
    var currency = invoice.Currency ?? account.Currency;
    return CommandResult<InvoiceBalance>.Ok(new InvoiceBalance(invoice.Id, currency, invoice.Total,
      MoneyPolicy.Normalize(credited), MoneyPolicy.Normalize(allocated),
      MoneyPolicy.Normalize(invoice.Total - credited - allocated)));
  }

  private static async Task<CommandResult> PostInvoiceCoreAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, CancellationToken ct)
  {
    var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
    if (invoice is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoice.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, ManagerRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, account.PracticeClientId, ct);
    if (!guard.Succeeded) return guard;
    var profile = await db.FirmFinanceProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Approved, ct);
    if (profile is null || !string.Equals(profile.FunctionalCurrency, account.Currency, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An approved finance profile in the billing currency is required.");
    var lockedAccount = await LoadBillingAccountForUpdateAsync(db, actor.FirmId, account.Id, ct);
    var lockedInvoice = await LoadInvoiceForUpdateAsync(db, actor.FirmId, invoiceId, ct);
    if (lockedAccount is null || lockedInvoice is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedInvoice.Status == BillingStates.InvoicePosted) return CommandResult.Ok();
    if (lockedInvoice.Status != BillingStates.InvoiceApproved)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an approved invoice can be posted.");
    lockedInvoice.Status = BillingStates.InvoicePosted;
    lockedInvoice.PostedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> TransitionInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, string expected, string next,
    string[] roles, Guid? forbidApprover, CancellationToken ct)
  {
    var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoiceId && x.FirmId == actor.FirmId, ct);
    if (invoice is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var account = await db.BillingAccounts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == invoice.BillingAccountId && x.FirmId == actor.FirmId, ct);
    if (account is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeBillingAsync(db, actor, account.PracticeClientId, roles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var guard = await LockClientAsync(db, actor.FirmId, account.PracticeClientId, ct);
    if (!guard.Succeeded) return guard;
    var lockedAccount = await LoadBillingAccountForUpdateAsync(db, actor.FirmId, account.Id, ct);
    var lockedInvoice = await LoadInvoiceForUpdateAsync(db, actor.FirmId, invoiceId, ct);
    if (lockedAccount is null || lockedInvoice is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (lockedInvoice.Status == next) return CommandResult.Ok();
    if (lockedInvoice.Status != expected)
      return CommandResult.Fail(ErrorCodes.ProtectedState, $"Only an invoice in {expected} can enter {next}.");
    if (forbidApprover.HasValue && lockedInvoice.CreatedByUserId == forbidApprover.Value)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Invoice preparers cannot approve their own invoice.");
    lockedInvoice.Status = next;
    if (next == BillingStates.InvoiceApproved)
    {
      lockedInvoice.ApprovedByUserId = actor.UserId;
      lockedInvoice.ApprovedAt = DateTimeOffset.UtcNow;
    }
    if (next == BillingStates.InvoiceSent) lockedInvoice.SentAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> AuthorizeBillingAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, string[] roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);

  private static async Task<CommandResult?> ValidateSourceAsync(
    IAuditSphereDbContext db, Guid firmId, string currency, InvoiceLineRequest request,
    decimal lineTotal, CancellationToken ct)
  {
    if (!string.Equals(request.SourceKind?.Trim(), "TIME", StringComparison.OrdinalIgnoreCase)) return null;
    var entry = await db.TimeEntries.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == request.SourceId && x.Revision == request.SourceRevision &&
      x.Status == PracticeTimeStates.TimeApproved && x.BillableClassification == PracticeTimeStates.Billable, ct);
    if (entry is null || !string.Equals(entry.Currency, currency, StringComparison.OrdinalIgnoreCase))
      return CommandResult.Fail("billing.source-invalid", "Only approved billable time in the account currency can be billed.");
    var expectedQuantity = entry.DurationMinutes / 60m;
    var expectedAmount = MoneyPolicy.Normalize(entry.DurationMinutes * (entry.RatePerHour ?? 0m) / 60m);
    return request.Quantity != expectedQuantity || lineTotal != expectedAmount
      ? CommandResult.Fail("billing.source-mismatch", "The invoice line does not match the approved time snapshot.")
      : null;
  }

  private static string? ValidateInvoice(CreateInvoiceDraftRequest request)
  {
    if (string.IsNullOrWhiteSpace(request.InvoiceNumber) || request.InvoiceNumber.Trim().Length > 64)
      return "Invoice number is required and bounded.";
    if (request.Lines is null || request.Lines.Count is < 1 or > 200)
      return "An invoice needs 1 to 200 lines.";
    if (request.Tax < 0 || MoneyPolicy.Normalize(request.Tax) != request.Tax)
      return "Tax must be non-negative and have at most 6 decimals.";
    foreach (var line in request.Lines)
    {
      if (string.IsNullOrWhiteSpace(line.Description) || line.Description.Trim().Length > 500)
        return "Invoice line descriptions are required and bounded.";
      if (line.Quantity <= 0 || line.UnitPrice < 0 ||
          MoneyPolicy.Normalize(line.Quantity) != line.Quantity || MoneyPolicy.Normalize(line.UnitPrice) != line.UnitPrice)
        return "Invoice quantities and prices must be exact non-negative decimal values.";
      var hasSource = line.SourceId.HasValue || !string.IsNullOrWhiteSpace(line.SourceKind) || line.SourceRevision.HasValue;
      if (hasSource && (line.SourceId is null || string.IsNullOrWhiteSpace(line.SourceKind) || line.SourceRevision is < 1))
        return "Source allocations require kind, ID and positive revision together.";
    }
    return null;
  }

  private static string? CurrencyError(string currency) =>
    currency.Trim().Length == 3 && currency.All(char.IsLetter)
      ? null : "Currency must be a three-letter code.";

  private static string SourceKey(string kind, Guid id, long revision) =>
    $"{kind.Trim().ToUpperInvariant()}:{id:D}:{revision}";

  private static Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static async Task<CommandResult> LockClientAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
  {
    var guard = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    return guard is null
      ? CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.")
      : CommandResult.Ok();
  }

  private static Task<BillingAccount?> LoadBillingAccountForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid accountId, CancellationToken ct) =>
    db.BillingAccounts.FromSqlInterpolated(
      $"SELECT * FROM billing_accounts WHERE id = {accountId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<Invoice?> LoadInvoiceForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid invoiceId, CancellationToken ct) =>
    db.Invoices.FromSqlInterpolated(
      $"SELECT * FROM invoices WHERE id = {invoiceId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<Receipt?> LoadReceiptForUpdateAsync(
    IAuditSphereDbContext db, Guid firmId, Guid receiptId, CancellationToken ct) =>
    db.Receipts.FromSqlInterpolated(
      $"SELECT * FROM receipts WHERE id = {receiptId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static async Task<CommandResult<Guid>> CommitValueAsync(
    IDbContextTransaction tx, Guid value, CancellationToken ct)
  {
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(value);
  }
}
