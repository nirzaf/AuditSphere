// Controlled adjustment journals (§§8.3, 17.3): technical treatment, management
// authorization, application, and posting stay distinct decisions. The preparer drafts;
// a different reviewer posts. Posted lines freeze via the database trigger, and a
// correction is a new linked journal — never an edit.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static class AdjustmentJournalService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "Staff", "Partner", "Manager"];
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Partner", "Manager"];

  public static async Task<CommandResult<Guid>> CreateDraftAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid baseDatasetId,
    string journalNumber,
    IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit)> lines,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(journalNumber) || journalNumber.Length > 32)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, "Journal number is required (max 32 characters).");
    journalNumber = journalNumber.Trim();
    if (lines.Count < 2)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, "A journal needs at least two lines.");

    var check = CheckLines(lines);
    if (check is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, check);

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == baseDatasetId, ct);
    if (dataset is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (dataset.ValidationStatus != "Accepted")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Journals require a validated base dataset.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId, PreparerRoles), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    var journal = new AdjustmentJournal
    {
      Id = Guid.CreateVersion7(),
      FirmId = dataset.FirmId,
      ClientId = dataset.ClientId,
      EngagementId = dataset.EngagementId,
      BaseDatasetId = dataset.Id,
      JournalNumber = journalNumber,
      Status = "Draft",
      Revision = 1,
      CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.AdjustmentJournals.Add(journal);
    foreach (var (code, debit, credit) in lines)
    {
      db.AdjustmentLines.Add(new AdjustmentLine
      {
        Id = Guid.CreateVersion7(), JournalId = journal.Id,
        AccountCode = code.Trim(), Debit = debit, Credit = credit
      });
    }
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
        "This journal number already exists for the base dataset.");
    }
    return CommandResult<Guid>.Ok(journal.Id);
  }

  public static async Task<CommandResult> PostAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid journalId,
    CancellationToken ct = default)
  {
    var journal = await db.AdjustmentJournals.SingleOrDefaultAsync(j => j.Id == journalId, ct);
    if (journal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(journal.FirmId, journal.ClientId, journal.EngagementId, ReviewerRoles,
        RequireProfessionalWork: true), ct);
    if (!auth.Succeeded)
      return CommandResult.Fail(auth.ErrorCode!, auth.Message!);

    if (journal.Status != "Draft")
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft journal can be posted.");
    if (journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied,
        "Separation of duties: the preparer cannot post their own journal.");

    var lines = await db.AdjustmentLines.AsNoTracking()
      .Where(l => l.JournalId == journal.Id).ToListAsync(ct);
    var check = CheckLines(lines.Select(l => (l.AccountCode, l.Debit, l.Credit)).ToList());
    if (check is not null)
      return CommandResult.Fail(ErrorCodes.Accounting.JournalRejected, check);

    journal.Status = "Posted";
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static string? CheckLines(IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit)> lines)
  {
    decimal debits = 0, credits = 0;
    foreach (var line in lines)
    {
      var (code, rawDebit, rawCredit) = line;
      if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
        return "Every journal line needs an account code (max 32 characters).";
      if (rawDebit < 0 || rawCredit < 0)
        return "Journal amounts cannot be negative.";
      if (rawDebit > 0 && rawCredit > 0)
        return "A journal line carries either a debit or a credit, never both.";
      if (rawDebit == 0 && rawCredit == 0)
        return "A journal line cannot be zero on both sides.";
      decimal debit, credit;
      try
      {
        debit = MoneyPolicy.Normalize(rawDebit);
        credit = MoneyPolicy.Normalize(rawCredit);
      }
      catch (ArgumentOutOfRangeException)
      {
        return "Journal amounts exceed 6 decimal places; no silent rounding.";
      }
      debits += debit;
      credits += credit;
    }
    if (MoneyPolicy.Normalize(debits) != MoneyPolicy.Normalize(credits))
      return "Unbalanced journal cannot be posted.";
    if (MoneyPolicy.Normalize(debits) == 0)
      return "A journal cannot total zero.";
    return null;
  }
}
