using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record SetInvoicePaymentTermsRequest(
  DateOnly DueDate, string Basis, string TermsDescription, string EvidenceReference, long ExpectedRevision, bool Reviewed);

public sealed record ReviewInvoicePaymentTermsRequest(bool Approve, string Reason, bool Reviewed);

/// <summary>Maker/checker commands for immutable, evidence-backed invoice payment-term revisions.</summary>
public static class InvoicePaymentTermsService
{
  private static readonly string[] MakerRoles = ["FinanceManager"];
  private static readonly string[] ReviewerRoles = ["FinanceReviewer"];

  public static async Task<CommandResult<Guid>> SubmitAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId,
    SetInvoicePaymentTermsRequest request, CancellationToken ct = default)
  {
    var basis = request.Basis.Trim().ToUpperInvariant();
    var description = request.TermsDescription.Trim();
    var evidence = request.EvidenceReference.Trim();
    if (!request.Reviewed || request.ExpectedRevision < 0 || request.ExpectedRevision == long.MaxValue ||
        basis is not (InvoicePaymentTermsKinds.ContractualDueDate or InvoicePaymentTermsKinds.ReviewedTermsSnapshot) ||
        description.Length is < 1 or > 300 || evidence.Length is < 1 or > 500 ||
        Uri.TryCreate(evidence, UriKind.Absolute, out _) || evidence.Contains('?') || evidence.Contains('#'))
      return CommandResult<Guid>.Fail("billing.terms-invalid",
        "Confirm the proposed terms and provide a supported basis, bounded description and evidence reference (document references only; no URLs).");

    var context = await ResolveInvoiceAsync(db, actor, invoiceId, MakerRoles, ct);
    if (!context.Succeeded) return CommandResult<Guid>.Fail(context.ErrorCode!, context.Message!);
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (!await LockClientAsync(db, actor.FirmId, context.Value!.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var invoice = await db.Invoices.FromSqlInterpolated(
      $"SELECT * FROM invoices WHERE id = {invoiceId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (invoice is null || invoice.Status == BillingStates.InvoiceCancelled)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var currentAuthority = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, context.Value.ClientId, RequiredRoles: MakerRoles, InternalOnly: true), ct);
    if (!currentAuthority.Succeeded)
      return CommandResult<Guid>.Fail(currentAuthority.ErrorCode!, currentAuthority.Message!);

    var pending = await db.InvoicePaymentTermsRevisions.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == invoiceId && x.Status == InvoicePaymentTermsStates.PendingReview, ct);
    var requestRevision = request.ExpectedRevision + 1;
    var priorRequest = await db.InvoicePaymentTermsRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == invoiceId && x.Revision == requestRevision &&
      x.SubmittedByUserId == actor.UserId && x.DueDate == request.DueDate && x.Basis == basis &&
      x.TermsDescription == description && x.EvidenceReference == evidence, ct);
    if (priorRequest is not null) return await CommitValueAsync(tx, priorRequest.Id, ct);
    if (pending is not null)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Another payment-terms revision is awaiting independent review.");
    }

    var latestRevision = await db.InvoicePaymentTermsRevisions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == invoiceId).Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    if (latestRevision != request.ExpectedRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "Payment terms changed. Refresh the invoice and review the current revision.");

    var revision = new InvoicePaymentTermsRevision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, InvoiceId = invoiceId,
      Revision = latestRevision + 1, DueDate = request.DueDate, Basis = basis,
      TermsDescription = description, EvidenceReference = evidence,
      Status = InvoicePaymentTermsStates.PendingReview, SubmittedByUserId = actor.UserId,
      SubmittedAt = DateTimeOffset.UtcNow
    };
    db.InvoicePaymentTermsRevisions.Add(revision);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor,
          new AuthorizationRequest(actor.FirmId, context.Value.ClientId, RequiredRoles: MakerRoles, InternalOnly: true), ct)).Succeeded)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    try
    {
      await db.SaveChangesAsync(ct);
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<Guid>.Ok(revision.Id);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "Payment terms changed. Refresh the invoice and review the current revision.");
    }
  }

  public static async Task<CommandResult> ReviewAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid revisionId,
    ReviewInvoicePaymentTermsRequest request, CancellationToken ct = default)
  {
    var reason = request.Reason.Trim();
    if (!request.Reviewed || reason.Length is < 1 or > 1000)
      return CommandResult.Fail("billing.terms-invalid", "Confirm the independent review and provide a bounded decision reason.");
    var revision = await db.InvoicePaymentTermsRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == actor.FirmId, ct);
    if (revision is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var context = await ResolveInvoiceAsync(db, actor, revision.InvoiceId, ReviewerRoles, ct);
    if (!context.Succeeded) return CommandResult.Fail(context.ErrorCode!, context.Message!);

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (!await LockClientAsync(db, actor.FirmId, context.Value!.ClientId, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Client safety state is unavailable.");
    var invoice = await db.Invoices.FromSqlInterpolated(
      $"SELECT * FROM invoices WHERE id = {revision.InvoiceId} AND firm_id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if (invoice is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var current = await db.InvoicePaymentTermsRevisions.SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == actor.FirmId && x.InvoiceId == invoice.Id, ct);
    if (current is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var currentAuthority = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, context.Value.ClientId, RequiredRoles: ReviewerRoles, InternalOnly: true), ct);
    if (!currentAuthority.Succeeded)
      return CommandResult.Fail(currentAuthority.ErrorCode!, currentAuthority.Message!);

    var finalState = request.Approve ? InvoicePaymentTermsStates.Approved : InvoicePaymentTermsStates.Rejected;
    if (current.Status == finalState && current.ReviewedByUserId == actor.UserId && current.ReviewReason == reason)
      return await CommitAsync(tx, ct);
    if (current.Status != InvoicePaymentTermsStates.PendingReview || current.SubmittedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Only a different FinanceReviewer can decide the latest pending payment-terms revision.");
    var latestRevision = await db.InvoicePaymentTermsRevisions.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.InvoiceId == invoice.Id).Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    if (latestRevision != current.Revision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the latest payment-terms revision can be reviewed.");

    current.Status = finalState;
    current.ReviewedByUserId = actor.UserId;
    current.ReviewedAt = DateTimeOffset.UtcNow;
    current.ReviewReason = reason;
    try
    {
      await db.SaveChangesAsync(ct);
      return await CommitAsync(tx, ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult.Fail(ErrorCodes.StaleRevision, "The payment-terms decision changed. Refresh the invoice.");
    }
  }

  private sealed record InvoiceAuthorizationContext(Guid ClientId);

  private static async Task<CommandResult<InvoiceAuthorizationContext>> ResolveInvoiceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid invoiceId, string[] roles, CancellationToken ct)
  {
    var account = await (from invoice in db.Invoices.AsNoTracking()
      join billing in db.BillingAccounts.AsNoTracking() on new { invoice.FirmId, invoice.BillingAccountId } equals new { billing.FirmId, BillingAccountId = billing.Id }
      where invoice.FirmId == actor.FirmId && invoice.Id == invoiceId
      select billing).SingleOrDefaultAsync(ct);
    if (account is null) return CommandResult<InvoiceAuthorizationContext>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, account.PracticeClientId, RequiredRoles: roles, InternalOnly: true), ct);
    return authorization.Succeeded
      ? CommandResult<InvoiceAuthorizationContext>.Ok(new(account.PracticeClientId))
      : CommandResult<InvoiceAuthorizationContext>.Fail(authorization.ErrorCode!, authorization.Message!);
  }

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);

  private static async Task<bool> LockClientAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE id = {clientId} AND firm_id = {firmId} FOR UPDATE")
      .SingleOrDefaultAsync(ct) is not null;

  private static async Task<CommandResult<Guid>> CommitValueAsync(
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx, Guid value, CancellationToken ct)
  {
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(value);
  }

  private static async Task<CommandResult> CommitAsync(
    Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? tx, CancellationToken ct)
  {
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
