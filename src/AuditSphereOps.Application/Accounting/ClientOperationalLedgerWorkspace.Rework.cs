using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalReworkRequest(long ExpectedRevision, string Description, DateOnly PostingDate,
  IReadOnlyList<ClientOperationalJournalLineInput> Lines);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult> ReworkAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    Guid journalId, ClientOperationalJournalReworkRequest request, CancellationToken ct = default)
  {
    var description = (request.Description ?? string.Empty).Trim();
    if (request.ExpectedRevision < 1 || description.Length is 0 or > 1000 ||
        !ClientOperationalJournalCalculator.Calculate(request.Lines).Valid ||
        request.Lines.Any(x => string.IsNullOrWhiteSpace(x.AccountCode) || x.AccountCode.Trim().Length > 100 || x.Description?.Trim().Length > 1000))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Rework requires a valid description and exact balanced posting lines.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return auth;
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated(
      $"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={journalId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (journal is null || journal.CreatedByUserId != actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the original assigned preparer can rework this journal.");
    if (journal.Status != "RETURNED" || journal.Revision != request.ExpectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Rework requires the current returned revision.");
    var profile = await NativeProfileAsync(db, actor, clientId, ct);
    if (!profile.Succeeded) return CommandResult.Fail(profile.ErrorCode!, profile.Message!);
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={journal.PeriodId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || request.PostingDate < period.StartDate ||
        request.PostingDate > period.EndDate || period.Currency != journal.Currency || journal.Currency != profile.Value!.FunctionalCurrency)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Choose a date in the open matching client period.");
    if (!await db.ClientOperationalJournalSnapshots.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == clientId && x.JournalId == journalId && x.JournalRevision == journal.Revision - 1, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The returned content has no preserved submission; historical content cannot be reconstructed for editing.");
    var chart = await ActiveChartAsync(db, actor.FirmId, clientId, request.PostingDate, ct);
    if (chart is null) return CommandResult.Fail(ErrorCodes.GateBlocked, "An approved chart covering the date is required.");
    var codes = request.Lines.Select(x => x.AccountCode.Trim()).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
      x.ChartVersionId == chart.Id && codes.Contains(x.AccountCode) && x.IsPosting && x.Status == AccountingWorkflowStates.Active).ToListAsync(ct);
    if (accounts.Count != codes.Length) return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every line needs an active approved-chart posting account.");
    // First preserve the reviewed return boundary; line replacement follows under the same row lock and transaction.
    journal.Status = "DRAFT";
    journal.Revision++;
    journal.Description = description;
    journal.PostingDate = request.PostingDate;
    await db.SaveChangesAsync(ct);
    await db.ClientOperationalJournalLines.Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.JournalId == journalId).ExecuteDeleteAsync(ct);
    for (var i = 0; i < request.Lines.Count; i++)
    {
      var line = request.Lines[i]; var account = accounts.Single(x => x.AccountCode == line.AccountCode.Trim());
      db.ClientOperationalJournalLines.Add(new ClientOperationalJournalLine {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, JournalId = journalId, LineNumber = i + 1,
        ClientAccountId = account.Id, AccountCode = account.AccountCode, AccountName = account.AccountName,
        Description = (line.Description ?? string.Empty).Trim(), Debit = line.Debit, Credit = line.Credit });
    }
    await db.SaveChangesAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (!(await NativeProfileAsync(db, actor, clientId, ct)).Succeeded)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The accepted bookkeeping service changed during rework.");
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }
}
