using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalPreview(Guid JournalId, Guid ClientId, Guid PeriodId, string Revision,
  string Status, string Currency, string TotalDebit, string TotalCredit, string Digest,
  IReadOnlyList<ClientOperationalJournalLineView> Lines);

public static partial class ClientOperationalLedgerWorkspace
{
  public static async Task<CommandResult<ClientOperationalJournalPreview>> PreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid journalId, CancellationToken ct = default)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<ClientOperationalJournalPreview>.Fail(auth.ErrorCode!, "Access denied.");
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated(
      $"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={journalId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (journal is null) return CommandResult<ClientOperationalJournalPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var lines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.JournalId == journalId).OrderBy(x => x.LineNumber).ToListAsync(ct);
    var result = await BuildPreviewAsync(db, actor, journal, lines, ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOperationalJournalPreview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct);
    return result;
  }

  private static async Task<CommandResult<ClientOperationalJournalPreview>> BuildPreviewAsync(IClientAccountingDbContext db,
    ActorContext actor, ClientOperationalJournal journal, IReadOnlyList<ClientOperationalJournalLine> lines, CancellationToken ct)
  {
    var profile = await NativeProfileAsync(db, actor, journal.ClientId, ct);
    if (!profile.Succeeded) return CommandResult<ClientOperationalJournalPreview>.Fail(profile.ErrorCode!, profile.Message!);
    if (journal.Status is not ("DRAFT" or "RETURNED" or "SUBMITTED"))
      return CommandResult<ClientOperationalJournalPreview>.Fail(ErrorCodes.ProtectedState, "Only an unposted journal can be previewed.");
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={journal.ClientId} AND id={journal.PeriodId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || journal.PostingDate < period.StartDate ||
        journal.PostingDate > period.EndDate || period.Currency != journal.Currency || profile.Value!.FunctionalCurrency != journal.Currency)
      return CommandResult<ClientOperationalJournalPreview>.Fail(ErrorCodes.GateBlocked, "The journal needs an open matching functional-currency period.");
    if (!await ValidatePostingLinesAsync(db, actor.FirmId, journal.ClientId, journal.PostingDate, lines, ct))
      return CommandResult<ClientOperationalJournalPreview>.Fail(ErrorCodes.Accounting.MappingInvalid, "The journal needs balanced, valid approved-chart posting accounts.");
    var chart = await ActiveChartAsync(db, actor.FirmId, journal.ClientId, journal.PostingDate, ct);
    var mandate = await db.AcceptanceDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.PracticeClientId == journal.ClientId && x.EngagementId == null && x.ServiceRoute == "BOOKKEEPING")
      .OrderByDescending(x => x.Generation).ThenByDescending(x => x.DecidedAt).ThenByDescending(x => x.Id)
      .Select(x => new { x.Id, x.Generation, x.Decision, x.Conditions }).FirstOrDefaultAsync(ct);
    if (chart is null || mandate is not { Decision: "Accepted", Conditions: null or "" })
      return CommandResult<ClientOperationalJournalPreview>.Fail(ErrorCodes.GateBlocked, "Current accepted bookkeeping authority and chart are required.");
    var calculation = ClientOperationalJournalCalculator.Calculate(lines.Select(x =>
      new ClientOperationalJournalLineInput(x.AccountCode, x.Description, x.Debit, x.Credit)).ToArray());
    var normalized = View(journal, lines).Lines.Select(x => x with {
      Debit = decimal.Parse(x.Debit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture),
      Credit = decimal.Parse(x.Credit, CultureInfo.InvariantCulture).ToString("F6", CultureInfo.InvariantCulture) }).ToArray();
    var bytes = JsonSerializer.SerializeToUtf8Bytes(new { Version = "native-journal-preview-v1", actor.FirmId,
      journal.ClientId, journal.Id, journal.PeriodId, journal.Revision, journal.Status, journal.JournalNumber,
      journal.Description, journal.PostingDate, journal.Currency, journal.CreatedByUserId,
      ProfileId = profile.Value!.Id, profile.Value.SourceMode, ChartId = chart.Id, chart.EffectiveFrom, chart.EffectiveTo,
      PeriodStatus = period.Status, period.StartDate, period.EndDate, period.Basis, Mandate = mandate, Lines = normalized });
    var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
    return CommandResult<ClientOperationalJournalPreview>.Ok(new(journal.Id, journal.ClientId, journal.PeriodId,
      journal.Revision.ToString(CultureInfo.InvariantCulture), journal.Status, journal.Currency,
      calculation.TotalDebit.ToString("F6", CultureInfo.InvariantCulture), calculation.TotalCredit.ToString("F6", CultureInfo.InvariantCulture),
      digest, normalized));
  }
}
