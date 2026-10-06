using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult> ReturnAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    Guid journalId, long expectedRevision, string reason, CancellationToken ct = default)
  {
    reason = (reason ?? string.Empty).Trim();
    if (expectedRevision < 1 || reason.Length is 0 or > 2000)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Returning work requires its exact submitted revision and a reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var auth = await AuthorizeAsync(db, actor, clientId, Reviewers, ct);
    if (!auth.Succeeded) return auth;
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated(
      $"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={journalId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (journal is null || journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "An independent assigned reviewer must return the journal.");
    var profile = await NativeProfileAsync(db, actor, clientId, ct);
    if (!profile.Succeeded) return CommandResult.Fail(profile.ErrorCode!, profile.Message!);
    if (journal.Status != "SUBMITTED" || journal.Revision != expectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the current submitted journal can be returned.");
    db.ClientOperationalJournalDecisions.Add(new ClientOperationalJournalDecision {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, JournalId = journalId,
      JournalRevision = expectedRevision, Decision = "RETURN", Reason = reason, ActorUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow });
    journal.Status = "RETURNED";
    journal.Revision++;
    await db.SaveChangesAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Reviewers, ct)).Succeeded)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
