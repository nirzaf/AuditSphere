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

  public sealed record ManagementDecisionRequest(
    Guid JournalId, string Decision, string EvidenceMode, string EvidenceReference);

  public static async Task<CommandResult<Guid>> CreateDraftAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid baseDatasetId,
    string journalNumber,
    IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit)> lines,
    CancellationToken ct = default,
    string purpose = AdjustmentJournalPurposes.ReportingAdjustment,
    Guid? bookId = null,
    string origin = AdjustmentJournalOrigins.AuditProposed,
    string reason = "LEGACY_ADJUSTMENT",
    string evidenceReference = "LOCAL_COMMAND",
    Guid? supersedesJournalId = null,
    Guid? reversalOfJournalId = null)
  {
    if (string.IsNullOrWhiteSpace(journalNumber) || journalNumber.Length > 32)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, "Journal number is required (max 32 characters).");
    journalNumber = journalNumber.Trim();
    if (lines.Count < 2)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, "A journal needs at least two lines.");

    var check = CheckLines(lines);
    if (check is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, check);
    purpose = purpose.Trim().ToUpperInvariant();
    origin = origin.Trim().ToUpperInvariant();
    if (!AdjustmentJournalPurposes.All.Contains(purpose) || purpose == AdjustmentJournalPurposes.GroupOnlyElimination)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, "The journal purpose is not enabled for entity adjustments.");
    if (!AdjustmentJournalOrigins.All.Contains(origin) || string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 4000 ||
        string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Trim().Length > 2000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.JournalRejected, "Journal purpose, origin, reason and evidence are required.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == baseDatasetId, ct);
    if (dataset is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (dataset.ValidationStatus != "Accepted")
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Journals require a validated base dataset.");
    if (bookId.HasValue && db is IClientAccountingDbContext clientDb &&
        !await clientDb.ClientReportingBooks.AnyAsync(x => x.Id == bookId.Value && x.FirmId == dataset.FirmId &&
          x.ClientId == dataset.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting book is outside the client scope.");
    if (supersedesJournalId.HasValue && !await db.AdjustmentJournals.AnyAsync(x => x.Id == supersedesJournalId.Value &&
        x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId && x.EngagementId == dataset.EngagementId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The superseded journal is outside the engagement scope.");
    if (reversalOfJournalId.HasValue && !await db.AdjustmentJournals.AnyAsync(x => x.Id == reversalOfJournalId.Value &&
        x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId && x.EngagementId == dataset.EngagementId &&
        x.Status == "Posted", ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A reversal must reference a posted journal in the same engagement.");

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
      Purpose = purpose,
      BookId = bookId,
      Origin = origin,
      Reason = reason.Trim(),
      EvidenceReference = evidenceReference.Trim(),
      SupersedesJournalId = supersedesJournalId,
      ReversalOfJournalId = reversalOfJournalId,
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

  public static async Task<CommandResult<Guid>> CreateReversalDraftAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid postedJournalId, string journalNumber,
    CancellationToken ct = default)
  {
    var original = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == postedJournalId &&
      x.FirmId == actor.FirmId && x.Status == "Posted", ct);
    if (original is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The posted journal is outside the authorized scope.");
    var rawLines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == original.Id)
      .Select(x => new { x.AccountCode, Debit = x.Credit, Credit = x.Debit }).ToListAsync(ct);
    var lines = rawLines.Select(x => (x.AccountCode, x.Debit, x.Credit)).ToList();
    return await CreateDraftAsync(db, actor, original.BaseDatasetId, journalNumber, lines, ct,
      original.Purpose, original.BookId, original.Origin, "REVERSAL_OF:" + original.JournalNumber,
      original.EvidenceReference, reversalOfJournalId: original.Id);
  }

  public static async Task<CommandResult> RecordManagementDecisionAsync(
    IAdjustmentJournalDbContext db, ActorContext actor, ManagementDecisionRequest request,
    CancellationToken ct = default)
  {
    var decision = request.Decision.Trim().ToUpperInvariant();
    var evidenceMode = request.EvidenceMode.Trim().ToUpperInvariant();
    var evidence = request.EvidenceReference.Trim();
    if (!ManagementDecisionStates.All.Contains(decision) ||
        evidenceMode is not (ManagementDecisionEvidenceModes.SignedIn or ManagementDecisionEvidenceModes.Offline) ||
        evidence.Length is < 1 or > 2000)
      return CommandResult.Fail(ErrorCodes.Accounting.JournalRejected, "Management decisions need a supported disposition and evidence mode.");
    var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.JournalId &&
      x.FirmId == actor.FirmId, ct);
    if (journal is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (journal.Status != "Draft")
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Management decisions are version-bound to a draft journal.");
    var roles = evidenceMode == ManagementDecisionEvidenceModes.SignedIn
      ? new[] { "ClientUser" }
      : new[] { "AccountingPreparer", "AccountingReviewer", "Manager", "Partner" };
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(journal.FirmId, journal.ClientId, journal.EngagementId, roles,
        InternalOnly: evidenceMode == ManagementDecisionEvidenceModes.Offline), ct);
    if (!auth.Succeeded)
      return auth;
    if (await db.AdjustmentJournalManagementDecisions.AnyAsync(x => x.FirmId == journal.FirmId &&
        x.ClientId == journal.ClientId && x.EngagementId == journal.EngagementId && x.JournalId == journal.Id &&
        x.JournalRevision == journal.Revision, ct))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "This journal revision already has a management decision.");
    db.AdjustmentJournalManagementDecisions.Add(new AdjustmentJournalManagementDecision
    {
      Id = Guid.CreateVersion7(), FirmId = journal.FirmId, ClientId = journal.ClientId,
      EngagementId = journal.EngagementId, JournalId = journal.Id, JournalRevision = journal.Revision,
      Decision = decision, EvidenceMode = evidenceMode, EvidenceReference = evidence,
      DecidedByUserId = evidenceMode == ManagementDecisionEvidenceModes.SignedIn ? actor.UserId : null,
      DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
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
    if (journal.Purpose == AdjustmentJournalPurposes.GroupOnlyElimination)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Group-only eliminations must use the consolidation journal workflow.");
    if (journal.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied,
        "Separation of duties: the preparer cannot post their own journal.");
    if (journal.Purpose == AdjustmentJournalPurposes.ClientBookCorrection)
    {
      if (db is not IAdjustmentJournalDbContext decisionDb ||
          !await decisionDb.AdjustmentJournalManagementDecisions.AnyAsync(x => x.FirmId == journal.FirmId &&
            x.ClientId == journal.ClientId && x.EngagementId == journal.EngagementId && x.JournalId == journal.Id &&
            x.JournalRevision == journal.Revision &&
            (x.Decision == ManagementDecisionStates.Accepted || x.Decision == ManagementDecisionStates.Partial), ct))
        return CommandResult.Fail(ErrorCodes.GateBlocked, "A client-book correction requires accepted or partial management evidence before posting.");
    }

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
