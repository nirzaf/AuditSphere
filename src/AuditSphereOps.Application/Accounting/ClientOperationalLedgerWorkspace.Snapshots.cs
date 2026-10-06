using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalSnapshotView(Guid JournalId, Guid ClientId, string Revision, string CapturedAt, string JournalNumber,
  string Description, string PostingDate, string Currency, IReadOnlyList<ClientOperationalJournalLineView> Lines);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>> GetSnapshotsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid journalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded || !await db.ClientOperationalJournals.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == journalId, ct))
      return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var rows = await db.ClientOperationalJournalSnapshots.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.JournalId == journalId).OrderBy(x => x.JournalRevision).ToListAsync(ct);
    var views = new List<ClientOperationalJournalSnapshotView>(rows.Count);
    foreach (var row in rows)
    {
      using var json = JsonDocument.Parse(row.SnapshotJson);
      var header = json.RootElement.GetProperty("journal");
      if (header.GetProperty("id").GetGuid() != journalId || header.GetProperty("firm_id").GetGuid() != actor.FirmId ||
          header.GetProperty("client_id").GetGuid() != clientId || header.GetProperty("revision").GetInt64() != row.JournalRevision)
        return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Fail(ErrorCodes.ProtectedState, "Stored journal content could not be validated.");
      var lines = json.RootElement.GetProperty("lines").EnumerateArray().Select(x => new ClientOperationalJournalLineView(
        x.GetProperty("line_number").GetInt32(), x.GetProperty("client_account_id").GetGuid(),
        x.GetProperty("account_code").GetString()!, x.GetProperty("account_name").GetString()!,
        x.GetProperty("description").GetString()!, x.GetProperty("debit").GetDecimal().ToString("F6", CultureInfo.InvariantCulture),
        x.GetProperty("credit").GetDecimal().ToString("F6", CultureInfo.InvariantCulture))).ToArray();
      views.Add(new(journalId, clientId, row.JournalRevision.ToString(CultureInfo.InvariantCulture), row.CapturedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        header.GetProperty("journal_number").GetString()!, header.GetProperty("description").GetString()!,
        header.GetProperty("posting_date").GetString()!, header.GetProperty("currency").GetString()!, lines));
    }
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<IReadOnlyList<ClientOperationalJournalSnapshotView>>.Ok(views);
  }
}
