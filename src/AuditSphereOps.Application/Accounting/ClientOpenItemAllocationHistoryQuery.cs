using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOpenItemAllocationHistoryLine(Guid AllocationLineId, Guid SubmissionId,
  string SourceKind, Guid SourceItemId, string TargetKind, Guid TargetOpenItemId, Guid CounterpartyId,
  string CounterpartyName, string Currency, string OriginalAmount, string ReversedAmount, string ReversibleAmount,
  string Reference, string Reason, string ManifestHash, Guid CreatedByUserId, Guid ApprovedByUserId, DateTimeOffset ApprovedAt);
public sealed record ClientOpenItemAllocationHistoryPage(Guid ClientId, int Page, int PageSize, int Total,
  bool BookkeepingActive, IReadOnlyList<ClientOpenItemAllocationHistoryLine> Lines);

/// <summary>Approved original allocations and their current approved reversal balances; no ledger mutation.</summary>
public static class ClientOpenItemAllocationHistoryQuery
{
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  public static async Task<CommandResult<ClientOpenItemAllocationHistoryPage>> GetAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, int page = 0, int pageSize = 25, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is not (10 or 25 or 50))
      return CommandResult<ClientOpenItemAllocationHistoryPage>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a supported history page.");
    if (!(await AuthorizeAsync(db, actor, clientId, ct)).Succeeded) return Denied();
    await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
    var originals = from line in db.ClientOpenItemAllocationLines.AsNoTracking()
      join submission in db.ClientOpenItemAllocationSubmissions.AsNoTracking()
        on new { line.FirmId, line.ClientId, line.SubmissionId } equals new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id }
      join decision in db.ClientOpenItemAllocationDecisions.AsNoTracking()
        on new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id } equals new { decision.FirmId, decision.ClientId, decision.SubmissionId }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && line.ReversesAllocationLineId == null &&
        submission.Disposition == "ALLOCATE" && decision.Decision == "APPROVE"
      select new { Line = line, Submission = submission, Decision = decision };
    var total = await originals.CountAsync(ct);
    var rows = await originals.OrderByDescending(x => x.Decision.CreatedAt).ThenByDescending(x => x.Line.Id)
      .Skip(page * pageSize).Take(pageSize).ToListAsync(ct);
    var lineIds = rows.Select(x => x.Line.Id).ToArray();
    var reversed = await (from line in db.ClientOpenItemAllocationLines.AsNoTracking()
      join submission in db.ClientOpenItemAllocationSubmissions.AsNoTracking()
        on new { line.FirmId, line.ClientId, line.SubmissionId } equals new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id }
      join decision in db.ClientOpenItemAllocationDecisions.AsNoTracking()
        on new { submission.FirmId, submission.ClientId, SubmissionId = submission.Id } equals new { decision.FirmId, decision.ClientId, decision.SubmissionId }
      where line.FirmId == actor.FirmId && line.ClientId == clientId && line.ReversesAllocationLineId.HasValue &&
        lineIds.Contains(line.ReversesAllocationLineId.Value) && submission.Disposition == "UNALLOCATE" && decision.Decision == "APPROVE"
      group line by line.ReversesAllocationLineId!.Value into effects
      select new { Id = effects.Key, Amount = effects.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Id, x => x.Amount, ct);
    var partyIds = rows.Select(x => x.Submission.CounterpartyId).Distinct().ToArray();
    var parties = await db.ClientBookkeepingCounterparties.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && partyIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    var result = new List<ClientOpenItemAllocationHistoryLine>();
    foreach (var row in rows)
    {
      var submission = row.Submission;
      if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(submission.ManifestJson))) != submission.ManifestHash)
        return InvalidEvidence();
      ClientOpenItemAllocationPreview? manifest;
      try { manifest = JsonSerializer.Deserialize<ClientOpenItemAllocationPreview>(submission.ManifestJson); }
      catch (JsonException) { return InvalidEvidence(); }
      var manifestLines = manifest?.Lines?.Where(x => x.LineNumber == row.Line.LineNumber).Take(2).ToArray();
      var manifestLine = manifestLines is { Length: 1 } ? manifestLines[0] : null;
      if (manifest is null || manifest.Digest != submission.PreviewDigest || manifest.Disposition != "ALLOCATE" ||
          manifest.SourceKind != submission.SourceKind || manifest.SourceItemId != submission.SourceItemId ||
          manifest.CounterpartyId != submission.CounterpartyId || manifest.Currency != submission.Currency ||
          row.Decision.PreviewDigest != submission.PreviewDigest || row.Decision.ActorUserId == submission.CreatedByUserId ||
          !decimal.TryParse(manifest.SourceOriginalAmount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var originalSourceAmount) ||
          originalSourceAmount != submission.SourceAmount ||
          manifestLine is null || manifestLine.TargetKind != row.Line.TargetKind || manifestLine.TargetOpenItemId != row.Line.TargetOpenItemId ||
          manifestLine.ReversesAllocationLineId is not null || !decimal.TryParse(manifestLine.Amount, NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var manifestAmount) || manifestAmount != row.Line.Amount ||
          !parties.TryGetValue(submission.CounterpartyId, out var partyName)) return InvalidEvidence();
      var reversedAmount = reversed.GetValueOrDefault(row.Line.Id);
      if (row.Line.Amount <= 0m || reversedAmount < 0m || reversedAmount > row.Line.Amount) return InvalidEvidence();
      result.Add(new(row.Line.Id, submission.Id, submission.SourceKind, submission.SourceItemId, row.Line.TargetKind,
        row.Line.TargetOpenItemId, submission.CounterpartyId, partyName, submission.Currency, Exact(row.Line.Amount),
        Exact(reversedAmount), Exact(row.Line.Amount - reversedAmount), submission.Reference, submission.Reason,
        submission.ManifestHash, submission.CreatedByUserId, row.Decision.ActorUserId, row.Decision.CreatedAt));
    }
    var active = await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    if (!(await AuthorizeAsync(db, actor, clientId, ct)).Succeeded) return Denied();
    await snapshot.CommitAsync(ct);
    return CommandResult<ClientOpenItemAllocationHistoryPage>.Ok(new(clientId, page, pageSize, total, active, result));
  }

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, Guid clientId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, RequiredRoles: ReadRoles, InternalOnly: true), ct);
  private static string Exact(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);
  private static CommandResult<ClientOpenItemAllocationHistoryPage> Denied() =>
    CommandResult<ClientOpenItemAllocationHistoryPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static CommandResult<ClientOpenItemAllocationHistoryPage> InvalidEvidence() =>
    CommandResult<ClientOpenItemAllocationHistoryPage>.Fail(ErrorCodes.ProtectedState, "Approved allocation evidence or its reversible balance is inconsistent.");
}
