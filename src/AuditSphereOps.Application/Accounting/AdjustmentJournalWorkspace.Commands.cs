using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record JournalActionPreview(string Action, string ReviewBasis, string RequestHash,
  bool CanProceed, string? Blocker, IReadOnlyList<string> Errors,
  decimal TotalDebit, decimal TotalCredit, IReadOnlyList<JournalLineView> Lines);

public static partial class AdjustmentJournalWorkspace
{
  private static string Hash(JournalActionRequest r) => Hashing.Sha256Hex(JsonSerializer.Serialize(new {
    r.RequestId, r.Action, r.ReviewBasis, r.Reason, r.EvidenceReference, r.ReversalNumber, r.Lines
  }));
  private static bool Valid(JournalActionRequest? r) => r is not null && r.RequestId != Guid.Empty &&
    r.Action is "UPDATE" or "SUBMIT" or "RETURN" or "POST" or "REVERSE" &&
    SourceAcceptanceWorkspace.ValidHash(r.ReviewBasis) && r.Reason is { Length: > 0 and <= 4000 } &&
    r.Reason.Trim().Length > 0 && r.EvidenceReference is { Length: > 0 and <= 2000 } && r.EvidenceReference.Trim().Length > 0 &&
    r.Lines is not null && r.Lines.Count <= MaximumLines &&
    (r.Action == "UPDATE" || r.Lines.Count == 0) &&
    (r.Action == "REVERSE" ? r.ReversalNumber is { Length: > 0 and <= 32 } && r.ReversalNumber.Trim().Length > 0 : r.ReversalNumber == "");

  private static bool Decimal(string s, out decimal v) =>
    decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v) &&
    s.Length <= 20 && Regex.IsMatch(s, @"^(0|[1-9][0-9]{0,12})(\.[0-9]{1,6})?$", RegexOptions.CultureInvariant);

  private sealed record CheckedLines(IReadOnlyList<JournalLineView> Lines, IReadOnlyList<string> Errors);
  private static async Task<CommandResult<CheckedLines>> CheckEditLinesAsync(IClientAccountingDbContext db,
    Guid datasetId, IReadOnlyList<JournalEditLine> inputs, CancellationToken ct)
  {
    var allowed = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == datasetId)
      .OrderBy(x => x.Id).Take(20_001).Select(x => x.AccountCode).ToListAsync(ct);
    if (allowed.Count > 20_000) return Fail<CheckedLines>(ErrorCodes.GateBlocked, "The source exceeds the interactive account limit.");
    var codes = allowed.ToHashSet(StringComparer.Ordinal);
    var parsed = new List<JournalLineView>(); var errors = new List<string>();
    for (var i = 0; i < inputs.Count; i++)
    {
      var l = inputs[i];
      if (l is null || l.AccountCode is not { Length: > 0 and <= 32 } || !codes.Contains(l.AccountCode.Trim()))
      { errors.Add($"Line {i + 1}: choose an account in this exact source."); continue; }
      if (l.Debit is null || l.Credit is null || !Decimal(l.Debit, out var debit) || !Decimal(l.Credit, out var credit))
      { errors.Add($"Line {i + 1}: use nonnegative plain decimals with at most six fractional digits."); continue; }
      if (debit > 0m && credit > 0m || debit == 0m && credit == 0m)
      { errors.Add($"Line {i + 1}: enter a positive amount on exactly one side."); continue; }
      parsed.Add(new(l.AccountCode.Trim(), debit, credit));
    }
    if (inputs.Count < 2) errors.Add("A journal needs at least two lines.");
    if (errors.Count == 0)
    {
      var error = AdjustmentJournalService.CheckLines(parsed.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
      if (error is not null) errors.Add(error);
    }
    return CommandResult<CheckedLines>.Ok(new(parsed, errors));
  }

  public static async Task<CommandResult<JournalActionPreview>> PreviewAsync(IClientAccountingDbContext db,
    IAdjustmentJournalDbContext evidenceDb, ActorContext actor, Guid id, JournalActionRequest? request, CancellationToken ct = default)
  {
    if (!Valid(request)) return Fail<JournalActionPreview>(ErrorCodes.Accounting.JournalRejected, "Choose a supported action, exact review, reason, evidence and bounded lines.");
    var r = request!;
    var current = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    if (!current.Succeeded) return Fail<JournalActionPreview>(current.ErrorCode!, current.Message!);
    var v = current.Value!;
    if (r.ReviewBasis != v.ReviewBasis) return Fail<JournalActionPreview>(ErrorCodes.StaleRevision, "The reviewed journal changed. Refresh and preview again.");
    var errors = new List<string>();
    var lines = v.Lines.ToArray();
    if (r.Action == "UPDATE")
    {
      var checkedLines = await CheckEditLinesAsync(db, v.DatasetId, r.Lines, ct);
      if (!checkedLines.Succeeded) return Fail<JournalActionPreview>(checkedLines.ErrorCode!, checkedLines.Message!);
      errors.AddRange(checkedLines.Value!.Errors); lines = checkedLines.Value.Lines.ToArray();
    }
    else if (r.Action == "REVERSE")
      lines = v.Lines.Select(x => new JournalLineView(x.AccountCode, x.Credit, x.Debit)).ToArray();
    var enabled = r.Action switch {
      "UPDATE" => v.CanEdit, "SUBMIT" => v.CanSubmit, "RETURN" => v.CanReturn,
      "POST" => v.CanPost, "REVERSE" => v.CanReverse, _ => false
    };
    if (r.Action != "UPDATE")
    {
      var lineError = AdjustmentJournalService.CheckLines(lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray());
      if (lineError is not null) errors.Add(lineError);
    }
    if (r.Action == "RETURN" && r.Reason.Trim().Length > 2000) errors.Add("A return reason is bounded to 2000 characters.");
    var final = await GetAsync(db, evidenceDb, actor, id, ct: ct);
    if (!final.Succeeded) return Fail<JournalActionPreview>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != v.ReviewBasis) return Fail<JournalActionPreview>(ErrorCodes.StaleRevision, "The review changed during validation. Refresh and preview again.");
    return CommandResult<JournalActionPreview>.Ok(new(r.Action, v.ReviewBasis, Hash(r), enabled && errors.Count == 0,
      v.Blocker ?? (!enabled ? "This action is unavailable for your current authority and journal state. An independent reviewer is required for review." : null),
      errors, lines.Sum(x => x.Debit), lines.Sum(x => x.Credit), lines));
  }

  public static async Task<CommandResult<JournalActionReceipt>> ExecuteAsync(IClientAccountingDbContext db,
    IAdjustmentJournalDbContext evidenceDb, ActorContext actor, Guid id, JournalActionRequest? request, CancellationToken ct = default)
  {
    if (!Valid(request) || !request!.Reviewed) return Fail<JournalActionReceipt>(ErrorCodes.Accounting.JournalRejected, "Preview and explicitly review this exact journal action.");
    var r = request!;
    var current = await ReadAsync(db, evidenceDb, actor, id, 1, ct);
    if (!current.Succeeded) return Fail<JournalActionReceipt>(current.ErrorCode!, current.Message!);
    var roles = r.Action is "POST" or "RETURN" ? ReviewRoles : PrepareRoles;
    var auth = await Auth(db, actor, current.Value!.Journal, roles, ct);
    if (!auth.Succeeded) return Fail<JournalActionReceipt>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, current.Value.Journal.ClientId, current.Value.Journal.EngagementId, ct);
    if (!locked.Succeeded) return Fail<JournalActionReceipt>(locked.ErrorCode!, locked.Message!);
    await db.AdjustmentJournals.FromSqlInterpolated($"SELECT * FROM adjustment_journals WHERE firm_id = {actor.FirmId} AND id = {id} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    var prior = await evidenceDb.AdjustmentJournalActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == r.RequestId, ct);
    if (prior is not null)
    {
      if (prior.JournalId != id || prior.RequestHash != Hash(r)) return Fail<JournalActionReceipt>(ErrorCodes.IdempotencyConflict, "A different intent cannot reuse this request identity.");
      auth = await Auth(db, actor, current.Value.Journal, roles, ct);
      if (!auth.Succeeded) return Fail<JournalActionReceipt>(auth.ErrorCode!, auth.Message!);
      await tx.CommitAsync(ct);
      return CommandResult<JournalActionReceipt>.Ok(Receipt(prior));
    }
    // Lock the source metadata before checking the exact current review.
    await db.TrialBalanceDatasets.FromSqlInterpolated($"SELECT * FROM trial_balance_datasets WHERE firm_id = {actor.FirmId} AND id = {current.Value.Journal.BaseDatasetId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var proof = await PreviewAsync(db, evidenceDb, actor, id, r, ct);
    if (!proof.Succeeded) return Fail<JournalActionReceipt>(proof.ErrorCode!, proof.Message!);
    if (!proof.Value!.CanProceed) return Fail<JournalActionReceipt>(ErrorCodes.GateBlocked, proof.Value.Errors.FirstOrDefault() ?? proof.Value.Blocker!);
    var before = await ReadAsync(db, evidenceDb, actor, id, 1, ct);
    if (!before.Succeeded) return Fail<JournalActionReceipt>(before.ErrorCode!, before.Message!);
    var j = before.Value!.Journal;
    if (j.PeriodId is not { } periodId) return Fail<JournalActionReceipt>(ErrorCodes.GateBlocked, "Bind the exact reporting period before writing.");
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, j.ClientId, j.EngagementId, periodId, j.BookId, ct);
    if (!mutable.Succeeded) return Fail<JournalActionReceipt>(mutable.ErrorCode!, mutable.Message!);
    var resultId = id;
    CommandResult result;
    if (r.Action == "UPDATE")
    {
      var edit = await AdjustmentJournalService.UpdateDraftAsync(db, actor, id,
        proof.Value.Lines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToArray(), r.Reason, r.EvidenceReference, j.Revision, ct);
      result = edit.Succeeded ? CommandResult.Ok() : CommandResult.Fail(edit.ErrorCode!, edit.Message!);
    }
    else if (r.Action == "REVERSE")
    {
      var reversal = await AdjustmentJournalService.CreateReversalDraftAsync(db, actor, id, r.ReversalNumber, ct, r.Reason, r.EvidenceReference);
      resultId = reversal.Value;
      result = reversal.Succeeded ? CommandResult.Ok() : CommandResult.Fail(reversal.ErrorCode!, reversal.Message!);
    }
    else if (r.Action == "POST") result = await AdjustmentJournalService.PostAsync(db, actor, id, ct);
    else
    {
      var transition = r.Action == "SUBMIT" ? await AdjustmentJournalService.SubmitDraftAsync(db, actor, id, ct) :
        await AdjustmentJournalService.ReturnSubmissionAsync(db, actor, id, r.Reason, ct);
      result = transition.Succeeded ? CommandResult.Ok() : CommandResult.Fail(transition.ErrorCode!, transition.Message!);
    }
    if (!result.Succeeded) return Fail<JournalActionReceipt>(result.ErrorCode!, result.Message!);
    var after = await ReadAsync(db, evidenceDb, actor, resultId, 1, ct);
    if (!after.Succeeded) return Fail<JournalActionReceipt>(after.ErrorCode!, after.Message!);
    var a = new AdjustmentJournalAction {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = j.ClientId, EngagementId = j.EngagementId,
      JournalId = id, ResultJournalId = resultId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
      RequestId = r.RequestId, RequestHash = Hash(r), ReviewBasis = r.ReviewBasis, Action = r.Action,
      OldRevision = j.Revision, NewRevision = after.Value!.View.Revision, OldStatus = j.Status, NewStatus = after.Value.View.Status,
      Reason = r.Reason.Trim(), EvidenceReference = r.EvidenceReference.Trim(), BeforeJson = before.Value.Json, AfterJson = after.Value.Json,
      CreatedAt = DateTimeOffset.UtcNow
    };
    evidenceDb.AdjustmentJournalActions.Add(a);
    await db.SaveChangesAsync(ct);
    auth = await Auth(db, actor, j, roles, ct);
    if (!auth.Succeeded) return Fail<JournalActionReceipt>(auth.ErrorCode!, auth.Message!);
    mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, j.ClientId, j.EngagementId, periodId, j.BookId, ct);
    if (!mutable.Succeeded) return Fail<JournalActionReceipt>(mutable.ErrorCode!, mutable.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<JournalActionReceipt>.Ok(Receipt(a));
  }
}
