using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalSummary(Guid Id, Guid PeriodId, string JournalNumber, string Description,
  string PostingDate, string Currency, string Status, string Revision, Guid CreatedByUserId);
public sealed record ClientOperationalJournalList(Guid ClientId, Guid? PeriodId, string? Status, int Page, int PageSize,
  int TotalJournals, bool BookkeepingActive, IReadOnlyList<ClientOperationalJournalSummary> Journals);

public static partial class ClientOperationalLedgerWorkspace
{
  /// <summary>Scoped saved-work discovery. Stopping the service blocks commands, not retained history.</summary>
  public static async Task<CommandResult<ClientOperationalJournalList>> ListAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid? periodId = null, string? status = null, int page = 0, int pageSize = 25,
    CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is < 1 or > 100 || (status is not null && status is not ("DRAFT" or "SUBMITTED" or "RETURNED" or "POSTED")))
      return CommandResult<ClientOperationalJournalList>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported journal status and page.");
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOperationalJournalList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (periodId.HasValue && !await db.ClientReportingPeriods.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == periodId, ct))
      return CommandResult<ClientOperationalJournalList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    var query = db.ClientOperationalJournals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId);
    if (periodId.HasValue) query = query.Where(x => x.PeriodId == periodId);
    if (status is not null) query = query.Where(x => x.Status == status);
    var total = await query.CountAsync(ct);
    var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    await snapshot.CommitAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOperationalJournalList>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var active = (await NativeProfileAsync(db, actor, clientId, ct)).Succeeded;
    return CommandResult<ClientOperationalJournalList>.Ok(new(clientId, periodId, status, page, pageSize, total, active,
      rows.Select(x => new ClientOperationalJournalSummary(x.Id, x.PeriodId, x.JournalNumber, x.Description,
        x.PostingDate.ToString("yyyy-MM-dd"), x.Currency, x.Status, x.Revision.ToString(CultureInfo.InvariantCulture), x.CreatedByUserId)).ToArray()));
  }
}
