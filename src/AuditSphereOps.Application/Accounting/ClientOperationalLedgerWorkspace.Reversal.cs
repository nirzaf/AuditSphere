using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalReversalRequest(long ExpectedRevision, Guid PeriodId, string JournalNumber,
  DateOnly PostingDate, string Reason, string EvidenceReference);
public sealed record ClientOperationalJournalReversalView(Guid OriginalJournalId, Guid ReversalJournalId, string OriginalRevision,
  string Reason, string EvidenceReference, Guid PreparedByUserId, string PreparedAt, string ReversalStatus);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult<Guid>> CreateReversalAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid originalJournalId, ClientOperationalJournalReversalRequest request, CancellationToken ct = default)
  {
    var reason = (request.Reason ?? string.Empty).Trim(); var evidence = (request.EvidenceReference ?? string.Empty).Trim();
    var number = (request.JournalNumber ?? string.Empty).Trim();
    if (request.ExpectedRevision < 1 || request.PeriodId == Guid.Empty || number.Length is 0 or > 100 ||
        reason.Length is 0 or > 2000 || evidence.Length is 0 or > 1000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A reversal needs an explicit period, date, number, reason and evidence reference.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, "Access denied.");
    var original = await db.ClientOperationalJournals.FromSqlInterpolated(
      $"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={originalJournalId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (original is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (original.Status != "POSTED" || original.Revision != request.ExpectedRevision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "A reversal requires the exact immutable posted original.");
    var hash = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new {
      Version = "native-full-reversal-v1", actor.FirmId, ClientId = clientId, OriginalJournalId = originalJournalId,
      actor.UserId, request.ExpectedRevision, request.PeriodId, JournalNumber = number, request.PostingDate, Reason = reason, EvidenceReference = evidence })));
    var existing = await db.ClientOperationalJournalReversals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.OriginalJournalId == originalJournalId, ct);
    if (existing is not null)
    {
      if (existing.IntentHash != hash) return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "A full reversal already exists for this original. Open its linked journal.");
      if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      await tx.CommitAsync(ct); return CommandResult<Guid>.Ok(existing.ReversalJournalId);
    }
    var sourceLines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == originalJournalId).OrderBy(x => x.LineNumber).ToListAsync(ct);
    if (!await ValidatePostingLinesAsync(db, actor.FirmId, clientId, request.PostingDate, sourceLines, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The original frozen posting accounts must still be approved for the correction date.");
    var profile = await NativeProfileAsync(db, actor, clientId, ct);
    if (!profile.Succeeded || profile.Value!.FunctionalCurrency != original.Currency)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The accepted native book must retain the original functional currency.");
    var draft = await CreateDraftCoreAsync(db, actor, new(clientId, request.PeriodId, number,
      "Reversal of " + original.JournalNumber, request.PostingDate,
      sourceLines.Select(x => new ClientOperationalJournalLineInput(x.AccountCode, x.Description, x.Credit, x.Debit)).ToArray()), false, ct);
    if (!draft.Succeeded) return draft;
    db.ClientOperationalJournalReversals.Add(new ClientOperationalJournalReversal {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, OriginalJournalId = originalJournalId,
      ReversalJournalId = draft.Value, OriginalRevision = original.Revision, Reason = reason, EvidenceReference = evidence,
      IntentHash = hash, PreparedByUserId = actor.UserId, PreparedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!(await NativeProfileAsync(db, actor, clientId, ct)).Succeeded)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The accepted bookkeeping service changed during preparation.");
    await tx.CommitAsync(ct);
    return draft;
  }

  private static async Task<(ClientOperationalJournalReversalView? Original, ClientOperationalJournalReversalView? Reversal)>
    ReversalRelationshipsAsync(IClientAccountingDbContext db, Guid firmId, Guid clientId, Guid journalId, CancellationToken ct)
  {
    var links = await (from link in db.ClientOperationalJournalReversals.AsNoTracking()
      join journal in db.ClientOperationalJournals.AsNoTracking()
        on new { link.FirmId, link.ClientId, Id = link.ReversalJournalId } equals new { journal.FirmId, journal.ClientId, Id = journal.Id }
      where link.FirmId == firmId && link.ClientId == clientId && (link.OriginalJournalId == journalId || link.ReversalJournalId == journalId)
      select new { Link = link, journal.Status }).ToListAsync(ct);
    ClientOperationalJournalReversalView ViewLink(ClientOperationalJournalReversal l, string status) => new(l.OriginalJournalId, l.ReversalJournalId,
      l.OriginalRevision.ToString(System.Globalization.CultureInfo.InvariantCulture), l.Reason, l.EvidenceReference, l.PreparedByUserId,
      l.PreparedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture), status);
    var parent = links.SingleOrDefault(x => x.Link.ReversalJournalId == journalId);
    var child = links.SingleOrDefault(x => x.Link.OriginalJournalId == journalId);
    return (parent is null ? null : ViewLink(parent.Link, parent.Status), child is null ? null : ViewLink(child.Link, child.Status));
  }
}
