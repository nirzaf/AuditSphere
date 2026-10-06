using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record CounterpartyAmendmentRequest(long ExpectedRevision, string DisplayName, string Address,
  string TaxIdentifier, string ContactDetails, string PaymentTerms, string Reason);
public sealed record CounterpartyAmendmentView(Guid Id, Guid CounterpartyId, string Revision, string DisplayName,
  string Address, string TaxIdentifier, string ContactDetails, string PaymentTerms, string Reason,
  Guid ProposedByUserId, string CreatedAt, string? Decision, string? ReviewReason, Guid? ReviewedByUserId);
public sealed record CounterpartyHistory(ClientCounterpartyView Current, bool BookkeepingActive, int Page, int PageSize, int Total,
  CounterpartyAmendmentView? EffectiveAmendment, IReadOnlyList<CounterpartyAmendmentView> Amendments);

public static partial class ClientBookkeepingCounterpartyWorkspace
{
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> AuthorizeReview(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: Reviewers, InternalOnly: true), ct);
  private static async Task<long> CurrentRevision(IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid partyId, CancellationToken ct) =>
    await db.ClientCounterpartyAmendmentDecisions.Where(x => x.FirmId == firmId && x.ClientId == clientId && x.CounterpartyId == partyId && x.Decision == "APPROVE")
      .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 1;
  private static async Task<bool> LockClient(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").SingleOrDefaultAsync(ct) is not null;

  public static async Task<CommandResult<Guid>> ProposeAmendmentAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid partyId, CounterpartyAmendmentRequest request, CancellationToken ct = default)
  {
    var display = Trim(request.DisplayName); var address = Trim(request.Address); var tax = Trim(request.TaxIdentifier);
    var contact = Trim(request.ContactDetails); var terms = Trim(request.PaymentTerms); var reason = Trim(request.Reason);
    if (request.ExpectedRevision < 1 || request.ExpectedRevision == long.MaxValue || display.Length is 0 or > 300 ||
        address.Length > 2000 || tax.Length > 200 || contact.Length > 1000 || terms.Length > 500 || reason.Length is 0 or > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide the current revision, bounded contact details and an amendment reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!await Active(db, actor, clientId, ct)) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An active accepted bookkeeping service is required.");
    if (!await db.ClientBookkeepingCounterparties.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == partyId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await CurrentRevision(db, actor.FirmId, clientId, partyId, ct) != request.ExpectedRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The effective party revision changed. Read the current profile before proposing again.");
    var amendment = new ClientCounterpartyAmendment { Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      CounterpartyId = partyId, Revision = request.ExpectedRevision + 1, DisplayName = display, Address = address,
      TaxIdentifier = tax, ContactDetails = contact, PaymentTerms = terms, Reason = reason,
      ProposedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow };
    db.ClientCounterpartyAmendments.Add(amendment); await db.SaveChangesAsync(ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded || !await Active(db, actor, clientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Authority or bookkeeping service changed.");
    await tx.CommitAsync(ct); return CommandResult<Guid>.Ok(amendment.Id);
  }

  public static async Task<CommandResult> ReviewAmendmentAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid partyId, Guid amendmentId, long expectedRevision, string decision, string reason, CancellationToken ct = default)
  {
    reason = Trim(reason);
    if (expectedRevision < 2 || decision is not ("APPROVE" or "REJECT") || reason.Length is 0 or > 2000)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Review an exact proposed revision with an explicit decision and reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!(await AuthorizeReview(db, actor, clientId, ct)).Succeeded || !await LockClient(db, actor, clientId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var amendment = await db.ClientCounterpartyAmendments.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.CounterpartyId == partyId && x.Id == amendmentId, ct);
    if (amendment is null || amendment.ProposedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "An independent assigned reviewer must decide the proposed amendment.");
    if (!await Active(db, actor, clientId, ct)) return CommandResult.Fail(ErrorCodes.GateBlocked, "An active accepted bookkeeping service is required.");
    if (amendment.Revision != expectedRevision || await db.ClientCounterpartyAmendmentDecisions.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.AmendmentId == amendmentId, ct) ||
        (decision == "APPROVE" && await CurrentRevision(db, actor.FirmId, clientId, partyId, ct) != expectedRevision - 1))
      return CommandResult.Fail(ErrorCodes.StaleRevision, "This proposal is decided or no longer based on the current effective revision.");
    db.ClientCounterpartyAmendmentDecisions.Add(new ClientCounterpartyAmendmentDecision { Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId, ClientId = clientId, CounterpartyId = partyId, AmendmentId = amendmentId, Revision = expectedRevision,
      Decision = decision, Reason = reason, ReviewedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct);
    if (!(await AuthorizeReview(db, actor, clientId, ct)).Succeeded || !await Active(db, actor, clientId, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Authority or bookkeeping service changed.");
    await tx.CommitAsync(ct); return CommandResult.Ok();
  }

  internal static async Task<ClientCounterpartyView> EffectiveView(IClientAccountingDbContext db, ClientBookkeepingCounterparty party, CancellationToken ct)
  {
    var amendment = await (from a in db.ClientCounterpartyAmendments.AsNoTracking()
      join d in db.ClientCounterpartyAmendmentDecisions.AsNoTracking() on a.Id equals d.AmendmentId
      where a.FirmId == party.FirmId && a.ClientId == party.ClientId && a.CounterpartyId == party.Id &&
        d.FirmId == party.FirmId && d.ClientId == party.ClientId && d.Decision == "APPROVE"
      orderby a.Revision descending select a).FirstOrDefaultAsync(ct);
    return new(party.Id, party.ClientId, party.LegalName, amendment?.DisplayName ?? party.DisplayName, party.Role,
      amendment?.Address ?? party.Address, party.Country, amendment?.TaxIdentifier ?? party.TaxIdentifier,
      amendment?.ContactDetails ?? party.ContactDetails, amendment?.PaymentTerms ?? party.PaymentTerms,
      party.DefaultCurrency, party.ExternalSystem, party.ExternalReference, party.CreatedByUserId,
      party.CreatedAt.ToUniversalTime().ToString("O"), (amendment?.Revision ?? 1).ToString(System.Globalization.CultureInfo.InvariantCulture), amendment?.Id);
  }

  public static async Task<CommandResult<CounterpartyHistory>> HistoryAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid partyId, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is < 1 or > 100) return CommandResult<CounterpartyHistory>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported history page.");
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<CounterpartyHistory>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct);
    var party = await db.ClientBookkeepingCounterparties.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == partyId, ct);
    if (party is null) return CommandResult<CounterpartyHistory>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var current = await EffectiveView(db, party, ct);
    var query = db.ClientCounterpartyAmendments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CounterpartyId == partyId);
    var total = await query.CountAsync(ct);
    var proposals = await query.OrderByDescending(x => x.Revision).ThenByDescending(x => x.Id).Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    var effective = current.EffectiveAmendmentId is { } effectiveId ? await query.SingleAsync(x => x.Id == effectiveId, ct) : null;
    var ids = proposals.Select(x => x.Id).ToList(); if (effective is not null) ids.Add(effective.Id);
    var decisions = await db.ClientCounterpartyAmendmentDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.CounterpartyId == partyId && ids.Contains(x.AmendmentId)).ToDictionaryAsync(x => x.AmendmentId, ct);
    CounterpartyAmendmentView View(ClientCounterpartyAmendment a) { decisions.TryGetValue(a.Id, out var d); return new(a.Id, partyId,
      a.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), a.DisplayName, a.Address, a.TaxIdentifier, a.ContactDetails,
      a.PaymentTerms, a.Reason, a.ProposedByUserId, a.CreatedAt.ToUniversalTime().ToString("O"), d?.Decision, d?.Reason, d?.ReviewedByUserId); }
    var history = proposals.Select(View).ToArray();
    var effectiveView = effective is null ? null : View(effective);
    await tx.CommitAsync(ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<CounterpartyHistory>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var active = await Active(db, actor, clientId, ct);
    if (!(await Authorize(db, actor, clientId, ct)).Succeeded) return CommandResult<CounterpartyHistory>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<CounterpartyHistory>.Ok(new(current, active, page, pageSize, total, effectiveView, history));
  }
}
