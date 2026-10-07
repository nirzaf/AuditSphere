using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record FirmFinanceSnapshot(
  bool CanCreateSetup, bool CanCreateJournals, bool CanClosePeriod, bool CanReviewJournals, bool CanPostJournals,
  List<FirmPeriod> Periods, List<FirmAccount> Accounts, List<FirmPosting> RecentPostings,
  List<FirmFinanceJournalSnapshot> RecentJournals,
  bool CanReversePostings = false, bool CanReopenPeriod = false);

public sealed record FirmFinanceJournalLineSnapshot(
  Guid JournalId, Guid FirmAccountId, string AccountCode, string AccountName, string Description, decimal Debit, decimal Credit);

public sealed record FirmFinanceJournalSnapshot(
  Guid Id, Guid PeriodId, string PeriodCode, string JournalNumber, string SourceKind, string SourceKey,
  long SourceRevision, string PostingPurpose, string Currency, string Status, Guid CreatedByUserId,
  string? SupportingEvidenceFileName, string? SupportingEvidenceSha256,
  Guid? ApprovedByUserId, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, DateTimeOffset? PostedAt,
  List<FirmFinanceJournalLineSnapshot> Lines);

public sealed record FirmJournalEvidenceDownload(string FileName, string Sha256, byte[] Content);

/// <summary>Firm-ledger projection for a currently authorized finance user.</summary>
public static class FirmFinanceQuery
{
  private static readonly string[] FinanceRoles = ["FinanceManager", "FinanceReviewer"];
  private static readonly string[] ManagerRoles = ["FinanceManager"];

  public static async Task<CommandResult<FirmFinanceSnapshot>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: FinanceRoles,
      InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<FirmFinanceSnapshot>.Fail(auth.ErrorCode!, auth.Message!);

    var managerAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ManagerRoles,
        InternalOnly: true, RequireFirmWide: true), ct);
    var reviewerAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["FinanceReviewer"],
        InternalOnly: true, RequireFirmWide: true), ct);
    var canCreateSetup = managerAuth.Succeeded;
    var canClosePeriod = reviewerAuth.Succeeded;
    var periods = await db.FirmPeriods.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.PeriodCode)
      .ToListAsync(ct);
    var accounts = await db.FirmAccounts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderBy(x => x.Code)
      .ToListAsync(ct);
    var recentPostings = await db.FirmPostings.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.PostedAt)
      .Take(25)
      .ToListAsync(ct);
    var recentJournalRows = await db.FirmJournals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId)
      .OrderByDescending(x => x.CreatedAt)
      .ThenByDescending(x => x.Id)
      .Take(50)
      .ToListAsync(ct);
    var journalIds = recentJournalRows.Select(x => x.Id).ToArray();
    var journalLines = journalIds.Length == 0
      ? new List<FirmFinanceJournalLineSnapshot>()
      : await (from line in db.FirmJournalLines.AsNoTracking()
        join account in db.FirmAccounts.AsNoTracking()
          on new { line.FirmId, line.FirmAccountId } equals new { account.FirmId, FirmAccountId = account.Id }
        where line.FirmId == actor.FirmId && journalIds.Contains(line.JournalId)
        select new FirmFinanceJournalLineSnapshot(
          line.JournalId, line.FirmAccountId, account.Code, account.Name, line.Description, line.Debit, line.Credit))
        .ToListAsync(ct);
    var linesByJournal = journalLines.GroupBy(x => x.JournalId)
      .ToDictionary(g => g.Key, g => g.ToList());
    var periodCodes = periods.ToDictionary(x => x.Id, x => x.PeriodCode);
    var recentJournals = recentJournalRows
      .Where(x => periodCodes.ContainsKey(x.PeriodId))
      .Select(x => new FirmFinanceJournalSnapshot(
        x.Id, x.PeriodId, periodCodes[x.PeriodId], x.JournalNumber, x.SourceKind, x.SourceKey,
        x.SourceRevision, x.PostingPurpose, x.Currency, x.Status, x.CreatedByUserId,
        x.SupportingEvidenceFileName, x.SupportingEvidenceSha256,
        x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt, x.PostedAt,
        linesByJournal.GetValueOrDefault(x.Id, [])))
      .ToList();

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (auth.Succeeded)
    {
      managerAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, RequiredRoles: ManagerRoles,
          InternalOnly: true, RequireFirmWide: true), ct);
      reviewerAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(actor.FirmId, RequiredRoles: ["FinanceReviewer"],
          InternalOnly: true, RequireFirmWide: true), ct);
    }
    canCreateSetup = managerAuth.Succeeded;
    canClosePeriod = reviewerAuth.Succeeded;
    var canReversePostings = reviewerAuth.Succeeded;
    var canReopenPeriod = managerAuth.Succeeded;
    return auth.Succeeded
      ? CommandResult<FirmFinanceSnapshot>.Ok(new FirmFinanceSnapshot(
          canCreateSetup, canCreateSetup, canClosePeriod, reviewerAuth.Succeeded, managerAuth.Succeeded,
          periods, accounts, recentPostings, recentJournals,
          canReversePostings, canReopenPeriod))
      : CommandResult<FirmFinanceSnapshot>.Fail(auth.ErrorCode!, auth.Message!);
  }

  public static async Task<CommandResult<FirmJournalEvidenceDownload>> GetJournalEvidenceAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles: FinanceRoles,
      InternalOnly: true, RequireFirmWide: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!auth.Succeeded)
      return CommandResult<FirmJournalEvidenceDownload>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var evidence = await db.FirmJournals.AsNoTracking()
      .Where(x => x.Id == journalId && x.FirmId == actor.FirmId)
      .Select(x => new
      {
        x.SupportingEvidenceFileName,
        x.SupportingEvidenceContent,
        x.SupportingEvidenceSha256
      })
      .SingleOrDefaultAsync(ct);
    if (evidence?.SupportingEvidenceFileName is null || evidence.SupportingEvidenceContent is not { Length: > 0 } content ||
        evidence.SupportingEvidenceSha256 is not { Length: 64 } sha256 || Hashing.Sha256Hex(content) != sha256)
      return CommandResult<FirmJournalEvidenceDownload>.Fail(ErrorCodes.ScopeDenied, "The supporting document is unavailable.");

    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    return auth.Succeeded
      ? CommandResult<FirmJournalEvidenceDownload>.Ok(new(evidence.SupportingEvidenceFileName, sha256, content))
      : CommandResult<FirmJournalEvidenceDownload>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }
}
