using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuditSphereOps.Application.Practice;

public sealed record RequestReceiptAllocationReversal(
  decimal Amount, string Reference, string Reason, long ExpectedRevision, bool Reviewed);

public sealed record ReviewReceiptAllocationReversal(bool Approve, string Reason, bool Reviewed);

/// <summary>
/// Maker/checker workflow for unapplying a previously allocated receipt. A decision takes effect on the
/// server-recorded review date; rejected and pending requests never change invoice or receipt balances.
/// </summary>
public static class ReceiptAllocationReversalService
{
  private static readonly string[] MakerRoles = ["FinanceManager"];
  private static readonly string[] ReviewerRoles = ["FinanceReviewer"];

  public static async Task<CommandResult<Guid>> SubmitAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid allocationId,
    RequestReceiptAllocationReversal request, CancellationToken ct = default)
  {
    var reference = request.Reference.Trim();
    var reason = request.Reason.Trim();
    if (!request.Reviewed || request.Amount <= 0 || MoneyPolicy.Normalize(request.Amount) != request.Amount ||
        request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue || reference.Length is < 1 or > 200 || reason.Length is < 1 or > 1000 ||
        Uri.TryCreate(reference, UriKind.Absolute, out _) || reference.Contains('?') || reference.Contains('#'))
      return CommandResult<Guid>.Fail("billing.reversal-invalid",
        "Confirm the requested reversal and provide a positive amount with at most six decimals, a bounded evidence reference and a reason.");

    var context = await ResolveAllocationAsync(db, actor, allocationId, MakerRoles, ct);
    if (!context.Succeeded) return CommandResult<Guid>.Fail(context.ErrorCode!, context.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (!await LockClientAsync(db, actor.FirmId, context.Value!.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    if (await LockAccountAsync(db, actor.FirmId, context.Value.BillingAccountId, ct) is null ||
        await LockReceiptAsync(db, actor.FirmId, context.Value.ReceiptId, ct) is null ||
        await LockInvoiceAsync(db, actor.FirmId, context.Value.InvoiceId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var allocation = await db.ReceiptAllocations.FromSqlInterpolated(
      $"SELECT * FROM allocations WHERE id = {allocationId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (allocation is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var current = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, context.Value.ClientId, RequiredRoles: MakerRoles, InternalOnly: true), ct);
    if (!current.Succeeded) return CommandResult<Guid>.Fail(current.ErrorCode!, current.Message!);

    var pending = await db.ReceiptAllocationReversals.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ReceiptAllocationId == allocationId &&
      x.Status == ReceiptAllocationReversalStates.PendingReview, ct);
    var requestRevision = request.ExpectedRevision + 1;
    var priorRequest = await db.ReceiptAllocationReversals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ReceiptAllocationId == allocationId && x.Revision == requestRevision &&
      x.SubmittedByUserId == actor.UserId && x.Amount == request.Amount &&
      x.Reference == reference && x.Reason == reason, ct);
    if (priorRequest is not null) return await CommitValueAsync(tx, priorRequest.Id, ct);
    if (pending is not null)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Another allocation reversal is awaiting independent review.");
    }

    var latestRevision = await db.ReceiptAllocationReversals.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ReceiptAllocationId == allocationId)
      .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    if (latestRevision != request.ExpectedRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "Allocation reversal history changed. Refresh the invoice and review the current balance.");

    var reserved = await db.ReceiptAllocationReversals.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ReceiptAllocationId == allocationId &&
      x.Status == ReceiptAllocationReversalStates.Approved)
      .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
    if (request.Amount > allocation.Amount - reserved)
      return CommandResult<Guid>.Fail("billing.reversal-over-allocation",
        "The requested reversal exceeds the remaining applied amount.");

    var reversal = new ReceiptAllocationReversal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ReceiptAllocationId = allocationId,
      Revision = latestRevision + 1, Amount = request.Amount, Reference = reference, Reason = reason,
      Status = ReceiptAllocationReversalStates.PendingReview, SubmittedByUserId = actor.UserId,
      SubmittedAt = DateTimeOffset.UtcNow
    };
    db.ReceiptAllocationReversals.Add(reversal);
    try
    {
      await db.SaveChangesAsync(ct);
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(reversal.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "The allocation reversal changed. Refresh the invoice and try again.");
    }
  }

  public static async Task<CommandResult> ReviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid reversalId,
    ReviewReceiptAllocationReversal request, CancellationToken ct = default)
  {
    var reason = request.Reason.Trim();
    if (!request.Reviewed || reason.Length is < 1 or > 1000)
      return CommandResult.Fail("billing.reversal-invalid", "Confirm the independent review and provide a bounded decision reason.");
    var snapshot = await db.ReceiptAllocationReversals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == reversalId, ct);
    if (snapshot is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var context = await ResolveAllocationAsync(db, actor, snapshot.ReceiptAllocationId, ReviewerRoles, ct);
    if (!context.Succeeded) return CommandResult.Fail(context.ErrorCode!, context.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (!await LockClientAsync(db, actor.FirmId, context.Value!.ClientId, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    if (await LockAccountAsync(db, actor.FirmId, context.Value.BillingAccountId, ct) is null ||
        await LockReceiptAsync(db, actor.FirmId, context.Value.ReceiptId, ct) is null ||
        await LockInvoiceAsync(db, actor.FirmId, context.Value.InvoiceId, ct) is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var allocation = await db.ReceiptAllocations.FromSqlInterpolated(
      $"SELECT * FROM allocations WHERE id = {snapshot.ReceiptAllocationId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (allocation is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var reversal = await db.ReceiptAllocationReversals.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == reversalId && x.ReceiptAllocationId == allocation.Id, ct);
    if (reversal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var current = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, context.Value.ClientId, RequiredRoles: ReviewerRoles, InternalOnly: true), ct);
    if (!current.Succeeded) return CommandResult.Fail(current.ErrorCode!, current.Message!);

    var finalState = request.Approve ? ReceiptAllocationReversalStates.Approved : ReceiptAllocationReversalStates.Rejected;
    if (reversal.Status == finalState && reversal.ReviewedByUserId == actor.UserId && reversal.ReviewReason == reason)
      return await CommitAsync(tx, ct);
    if (reversal.Status != ReceiptAllocationReversalStates.PendingReview || reversal.SubmittedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Only a different FinanceReviewer can decide a pending allocation reversal.");
    var latestRevision = await db.ReceiptAllocationReversals.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ReceiptAllocationId == allocation.Id)
      .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    if (latestRevision != reversal.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision,
        "Only the latest allocation reversal can be reviewed.");

    if (request.Approve)
    {
      var alreadyReversed = await db.ReceiptAllocationReversals.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ReceiptAllocationId == allocation.Id && x.Id != reversal.Id &&
        x.Status == ReceiptAllocationReversalStates.Approved)
        .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
      if (reversal.Amount > allocation.Amount - alreadyReversed)
        return CommandResult.Fail("billing.reversal-over-allocation",
          "The allocation no longer has enough applied value for this reversal.");
    }

    reversal.Status = finalState;
    reversal.ReviewedByUserId = actor.UserId;
    reversal.ReviewedAt = DateTimeOffset.UtcNow;
    reversal.ReviewReason = reason;
    try
    {
      await db.SaveChangesAsync(ct);
      return await CommitAsync(tx, ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult.Fail(ErrorCodes.StaleRevision,
        "The allocation-reversal decision changed. Refresh the invoice.");
    }
  }

  private sealed record AllocationContext(Guid ClientId, Guid BillingAccountId, Guid ReceiptId, Guid InvoiceId);

  private static async Task<CommandResult<AllocationContext>> ResolveAllocationAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid allocationId, string[] roles, CancellationToken ct)
  {
    var context = await (from allocation in db.ReceiptAllocations.AsNoTracking()
      join invoice in db.Invoices.AsNoTracking() on new { allocation.FirmId, allocation.InvoiceId } equals new { invoice.FirmId, InvoiceId = invoice.Id }
      join account in db.BillingAccounts.AsNoTracking() on new { invoice.FirmId, invoice.BillingAccountId } equals new { account.FirmId, BillingAccountId = account.Id }
      where allocation.FirmId == actor.FirmId && allocation.Id == allocationId
      select new AllocationContext(account.PracticeClientId, account.Id, allocation.ReceiptId, invoice.Id))
      .SingleOrDefaultAsync(ct);
    if (context is null) return CommandResult<AllocationContext>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, context.ClientId, RequiredRoles: roles, InternalOnly: true), ct);
    return auth.Succeeded
      ? CommandResult<AllocationContext>.Ok(context)
      : CommandResult<AllocationContext>.Fail(auth.ErrorCode!, auth.Message!);
  }

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static async Task<bool> LockClientAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct) is not null;

  private static Task<BillingAccount?> LockAccountAsync(IAuditSphereDbContext db, Guid firmId, Guid id, CancellationToken ct) =>
    db.BillingAccounts.FromSqlInterpolated($"SELECT * FROM billing_accounts WHERE id = {id} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<Receipt?> LockReceiptAsync(IAuditSphereDbContext db, Guid firmId, Guid id, CancellationToken ct) =>
    db.Receipts.FromSqlInterpolated($"SELECT * FROM receipts WHERE id = {id} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static Task<Invoice?> LockInvoiceAsync(IAuditSphereDbContext db, Guid firmId, Guid id, CancellationToken ct) =>
    db.Invoices.FromSqlInterpolated($"SELECT * FROM invoices WHERE id = {id} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static async Task<CommandResult<Guid>> CommitValueAsync(IDbContextTransaction? tx, Guid value, CancellationToken ct)
  {
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(value);
  }

  private static async Task<CommandResult> CommitAsync(IDbContextTransaction? tx, CancellationToken ct)
  {
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
