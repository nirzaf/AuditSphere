using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalLineInput(string AccountCode, string Description, decimal Debit, decimal Credit);
public sealed record ClientOperationalJournalCreateRequest(Guid ClientId, Guid PeriodId, string JournalNumber,
  string Description, DateOnly PostingDate, IReadOnlyList<ClientOperationalJournalLineInput> Lines);
public sealed record ClientOperationalJournalDecisionRequest(long ExpectedRevision, string Decision, string Reason, string PreviewDigest = "");
public sealed record ClientOperationalJournalLineView(int LineNumber, Guid AccountId, string AccountCode,
  string AccountName, string Description, string Debit, string Credit);
public sealed record ClientOperationalJournalDecisionView(string Revision, string Decision, string Reason, Guid ActorUserId, string CreatedAt);
public sealed record ClientOperationalJournalView(Guid Id, Guid ClientId, Guid PeriodId, string JournalNumber,
  string Description, string PostingDate, string Currency, string Status, string Revision,
  Guid CreatedByUserId, string CreatedAt, Guid? PostedByUserId, string? PostedAt,
  IReadOnlyList<ClientOperationalJournalLineView> Lines, IReadOnlyList<ClientOperationalJournalDecisionView>? Decisions = null);

/// <summary>Native client-book manual journal draft, independent review, and immutable posting.</summary>
public static partial class ClientOperationalLedgerWorkspace
{
  private static readonly string[] Preparers = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] Reviewers = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<Guid>> CreateDraftAsync(IClientAccountingDbContext db, ActorContext actor,
    ClientOperationalJournalCreateRequest request, CancellationToken ct = default)
  {
    var number = (request.JournalNumber ?? string.Empty).Trim();
    var description = (request.Description ?? string.Empty).Trim();
    var calculation = ClientOperationalJournalCalculator.Calculate(request.Lines);
    if (request.ClientId == Guid.Empty || request.PeriodId == Guid.Empty || number.Length is 0 or > 100 ||
        description.Length is 0 or > 1000 || request.Lines is null || request.Lines.Count is < 2 or > 100 ||
        request.Lines.Any(x => x is null || string.IsNullOrWhiteSpace(x.AccountCode) || x.AccountCode.Trim().Length > 100 ||
          x.Description?.Trim().Length > 1000) || !calculation.Valid)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A client journal needs 2 to 100 valid, balanced debit and credit lines.");

    var auth = await AuthorizeAsync(db, actor, request.ClientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var profile = await NativeProfileAsync(db, actor, request.ClientId, ct);
    if (!profile.Succeeded) return CommandResult<Guid>.Fail(profile.ErrorCode!, profile.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id = {actor.FirmId} AND client_id = {request.ClientId} AND id = {request.PeriodId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || request.PostingDate < period.StartDate ||
        request.PostingDate > period.EndDate || !string.Equals(period.Currency, profile.Value!.FunctionalCurrency, StringComparison.Ordinal))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Choose an open client period containing the posting date and matching the functional currency.");

    var chart = await ActiveChartAsync(db, actor.FirmId, request.ClientId, request.PostingDate, ct);
    if (chart is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Exactly one approved chart version must cover the posting date.");
    var inputs = request.Lines.Select(x => new
    {
      Code = x.AccountCode.Trim(), Description = (x.Description ?? string.Empty).Trim(), x.Debit, x.Credit
    }).ToArray();
    var codes = inputs.Select(x => x.Code).Distinct(StringComparer.Ordinal).ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
      x.ChartVersionId == chart.Id && codes.Contains(x.AccountCode) && x.IsPosting && x.Status == AccountingWorkflowStates.Active)
      .Select(x => new { x.Id, x.AccountCode, x.AccountName }).ToListAsync(ct);
    if (accounts.Count != codes.Length)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Every journal line must use an active posting account in the approved client chart.");
    if (await db.ClientOperationalJournals.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
        x.PeriodId == request.PeriodId && x.JournalNumber == number, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The journal number already exists in this period.");

    var journal = new ClientOperationalJournal
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, PeriodId = request.PeriodId,
      JournalNumber = number, Description = description, PostingDate = request.PostingDate,
      Currency = profile.Value.FunctionalCurrency, Status = "DRAFT", Revision = 1,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientOperationalJournals.Add(journal);
    for (var i = 0; i < inputs.Length; i++)
    {
      var line = inputs[i];
      var account = accounts.Single(x => x.AccountCode == line.Code);
      db.ClientOperationalJournalLines.Add(new ClientOperationalJournalLine
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, JournalId = journal.Id,
        LineNumber = i + 1, ClientAccountId = account.Id, AccountCode = account.AccountCode,
        AccountName = account.AccountName, Description = line.Description, Debit = line.Debit, Credit = line.Credit
      });
    }
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(journal.Id);
  }

  public static async Task<CommandResult<ClientOperationalJournalView>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid journalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return CommandResult<ClientOperationalJournalView>.Fail(auth.ErrorCode!, "Access denied.");
    var journal = await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == clientId && x.Id == journalId, ct);
    if (journal is null) return CommandResult<ClientOperationalJournalView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var lines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).OrderBy(x => x.LineNumber).ToListAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOperationalJournalView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var decisions = await db.ClientOperationalJournalDecisions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.JournalId == journalId).OrderBy(x => x.JournalRevision).ThenBy(x => x.CreatedAt).ToListAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult<ClientOperationalJournalView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<ClientOperationalJournalView>.Ok(View(journal, lines) with {
      Decisions = decisions.Select(x => new ClientOperationalJournalDecisionView(x.JournalRevision.ToString(System.Globalization.CultureInfo.InvariantCulture),
        x.Decision, x.Reason, x.ActorUserId, x.CreatedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture))).ToArray() });
  }

  public static async Task<CommandResult> SubmitAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid journalId, long expectedRevision, CancellationToken ct = default, string? previewDigest = null)
  {
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated(
      $"SELECT * FROM client_operational_journals WHERE firm_id = {actor.FirmId} AND client_id = {clientId} AND id = {journalId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (journal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, clientId, Preparers, ct);
    if (!auth.Succeeded) return auth;
    var profile = await NativeProfileAsync(db, actor, clientId, ct);
    if (!profile.Succeeded) return CommandResult.Fail(profile.ErrorCode!, profile.Message!);
    if (journal.CreatedByUserId != actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only the journal preparer can submit this draft.");
    if (journal.Status == "SUBMITTED" && journal.Revision == expectedRevision)
    {
      await tx.CommitAsync(ct);
      return CommandResult.Ok();
    }
    if (journal.Revision != expectedRevision) return CommandResult.Fail(ErrorCodes.StaleRevision, "The journal revision is outdated.");
    if (journal.Status is not ("DRAFT" or "RETURNED"))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft or returned journal can be submitted.");
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id = {actor.FirmId} AND client_id = {clientId} AND id = {journal.PeriodId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || journal.PostingDate < period.StartDate || journal.PostingDate > period.EndDate)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The journal period is closed or no longer covers its posting date.");
    var lines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.JournalId == journalId)
      .OrderBy(x => x.LineNumber).ToListAsync(ct);
    if (!await ValidatePostingLinesAsync(db, actor.FirmId, clientId, journal.PostingDate, lines, ct))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The journal no longer matches a unique approved chart and balanced active posting accounts.");
    var preview = await BuildPreviewAsync(db, actor, journal, lines, ct);
    if (!preview.Succeeded || !string.Equals(preview.Value!.Digest, previewDigest, StringComparison.Ordinal))
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Preview this exact journal and current accounting context before submitting.");
    journal.Status = "SUBMITTED";
    journal.SubmittedAt = DateTimeOffset.UtcNow;
    journal.Revision++;
    await db.SaveChangesAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Preparers, ct)).Succeeded)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ReviewAndPostAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid journalId, ClientOperationalJournalDecisionRequest request, CancellationToken ct = default)
  {
    var reason = (request.Reason ?? string.Empty).Trim();
    var decision = (request.Decision ?? string.Empty).Trim().ToUpperInvariant();
    if (request.ExpectedRevision < 1 || reason.Length is 0 or > 2000 || decision != "APPROVE")
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "An approval requires the current revision and a reason.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var journal = await db.ClientOperationalJournals.FromSqlInterpolated(
      $"SELECT * FROM client_operational_journals WHERE firm_id = {actor.FirmId} AND client_id = {clientId} AND id = {journalId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (journal is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, clientId, Reviewers, ct);
    if (!auth.Succeeded) return auth;
    var profile = await NativeProfileAsync(db, actor, clientId, ct);
    if (!profile.Succeeded) return CommandResult.Fail(profile.ErrorCode!, profile.Message!);
    if (journal.Status == "POSTED")
    {
      await tx.CommitAsync(ct);
      return CommandResult.Ok();
    }
    if (journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "The journal preparer cannot approve their own journal.");
    if (journal.Status != "SUBMITTED" || journal.Revision != request.ExpectedRevision)
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Only the current submitted journal revision can be approved.");
    var period = await db.ClientReportingPeriods.FromSqlInterpolated(
      $"SELECT * FROM client_reporting_periods WHERE firm_id = {actor.FirmId} AND client_id = {clientId} AND id = {journal.PeriodId} FOR UPDATE")
      .SingleOrDefaultAsync(ct);
    if (period is null || period.Status == AccountingWorkflowStates.Closed || journal.PostingDate < period.StartDate || journal.PostingDate > period.EndDate)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The journal period is closed or no longer covers its posting date.");
    var lines = await db.ClientOperationalJournalLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.JournalId == journalId)
      .OrderBy(x => x.LineNumber).ToListAsync(ct);
    if (!await ValidatePostingLinesAsync(db, actor.FirmId, clientId, journal.PostingDate, lines, ct))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The journal no longer matches a unique approved chart and balanced active posting accounts.");
    var preview = await BuildPreviewAsync(db, actor, journal, lines, ct);
    if (!preview.Succeeded || !string.Equals(preview.Value!.Digest, request.PreviewDigest, StringComparison.Ordinal))
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Preview this exact submitted journal and current accounting context before posting.");
    var decisionRecord = new ClientOperationalJournalDecision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, JournalId = journalId,
      JournalRevision = journal.Revision, Decision = "APPROVE", Reason = reason, ActorUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientOperationalJournalDecisions.Add(decisionRecord);
    journal.Status = "POSTED";
    journal.PostedByUserId = actor.UserId;
    journal.PostedAt = DateTimeOffset.UtcNow;
    journal.Revision++;
    await db.SaveChangesAsync(ct);
    if (!(await AuthorizeAsync(db, actor, clientId, Reviewers, ct)).Succeeded)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, string[] roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles, InternalOnly: true), ct);

  private static async Task<CommandResult<ClientAccountingProfile>> NativeProfileAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, CancellationToken ct)
  {
    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId, ct);
    if (profile is null || profile.SourceMode != ClientAccountingSourceModes.NativeBookkeeping)
      return CommandResult<ClientAccountingProfile>.Fail(ErrorCodes.GateBlocked, "This client is not configured for native bookkeeping.");
    var accepted = await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    return accepted ? CommandResult<ClientAccountingProfile>.Ok(profile) :
      CommandResult<ClientAccountingProfile>.Fail(ErrorCodes.GateBlocked, "The accepted bookkeeping service decision is missing or no longer current.");
  }

  private static async Task<ClientChartVersion?> ActiveChartAsync(IClientAccountingDbContext db, Guid firmId,
    Guid clientId, DateOnly date, CancellationToken ct)
  {
    var matches = await db.ClientChartVersions.AsNoTracking().Where(x =>
      x.FirmId == firmId && x.ClientId == clientId && x.Status == AccountingWorkflowStates.Approved &&
      x.EffectiveFrom <= date && (x.EffectiveTo == null || x.EffectiveTo >= date)).Take(2).ToListAsync(ct);
    return matches.Count == 1 ? matches[0] : null;
  }

  private static async Task<bool> ValidatePostingLinesAsync(IClientAccountingDbContext db, Guid firmId, Guid clientId,
    DateOnly postingDate, IReadOnlyList<ClientOperationalJournalLine> lines, CancellationToken ct)
  {
    if (!ClientOperationalJournalCalculator.Calculate(lines.Select(x =>
        new ClientOperationalJournalLineInput(x.AccountCode, x.Description, x.Debit, x.Credit)).ToArray()).Valid) return false;
    var chart = await ActiveChartAsync(db, firmId, clientId, postingDate, ct);
    if (chart is null) return false;
    var ids = lines.Select(x => x.ClientAccountId).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == firmId && x.ClientId == clientId &&
      x.ChartVersionId == chart.Id && ids.Contains(x.Id) && x.IsPosting && x.Status == AccountingWorkflowStates.Active)
      .Select(x => new { x.Id, x.AccountCode, x.AccountName }).ToListAsync(ct);
    return accounts.Count == ids.Length && lines.All(line => accounts.Any(account => account.Id == line.ClientAccountId &&
      account.AccountCode == line.AccountCode && account.AccountName == line.AccountName));
  }

  private static ClientOperationalJournalView View(ClientOperationalJournal j, IReadOnlyList<ClientOperationalJournalLine> lines) =>
    new(j.Id, j.ClientId, j.PeriodId, j.JournalNumber, j.Description, j.PostingDate.ToString("yyyy-MM-dd"), j.Currency,
      j.Status, j.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture), j.CreatedByUserId,
      j.CreatedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture), j.PostedByUserId,
      j.PostedAt?.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
      lines.Select(x => new ClientOperationalJournalLineView(x.LineNumber, x.ClientAccountId, x.AccountCode, x.AccountName,
        x.Description, x.Debit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        x.Credit.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToArray());
}
