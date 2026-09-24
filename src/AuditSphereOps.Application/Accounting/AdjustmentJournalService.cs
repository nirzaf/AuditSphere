// Controlled adjustment journals (§§8.3, 17.3): technical treatment, management
// authorization, application, and posting stay distinct decisions. The preparer drafts;
// a different reviewer posts. Posted lines freeze via the database trigger, and a
// correction is a new linked journal — never an edit.
using System.Globalization;
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

  public sealed record AdjustmentInstructionExport(string FileName, string Csv, long JournalRevision);

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
    var effectiveBookId = bookId ?? dataset.BookId;
    if (db is IClientAccountingDbContext clientDb)
    {
      if (dataset.PeriodId is { } periodId)
      {
        var period = await clientDb.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == periodId && x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId, ct);
        if (period is null || string.IsNullOrWhiteSpace(dataset.Basis) ||
            !string.Equals(period.Basis, dataset.Basis.Trim(), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(period.Currency, dataset.Currency, StringComparison.Ordinal))
          return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The journal source reporting context is unavailable or inconsistent.");
        if (effectiveBookId is { } selectedBookId)
        {
          var selectedBook = await clientDb.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == selectedBookId && x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId &&
            x.PeriodId == period.Id, ct);
          if (selectedBook is null || !string.Equals(selectedBook.Basis, dataset.Basis.Trim(), StringComparison.OrdinalIgnoreCase) ||
              !string.Equals(selectedBook.Currency, dataset.Currency, StringComparison.Ordinal))
            return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting book is outside the selected client period.");
        }
      }
      else if (effectiveBookId is { } legacyBookId && !await clientDb.ClientReportingBooks.AnyAsync(x =>
          x.Id == legacyBookId && x.FirmId == dataset.FirmId && x.ClientId == dataset.ClientId, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The reporting book is outside the client scope.");
    }
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
      PeriodId = dataset.PeriodId,
      BookId = effectiveBookId,
      Basis = dataset.Basis?.Trim().ToUpperInvariant(),
      Currency = dataset.Currency,
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

  public static async Task<CommandResult<long>> UpdateDraftAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid journalId,
    IReadOnlyList<(string AccountCode, decimal Debit, decimal Credit)> lines,
    string reason,
    string evidenceReference,
    long expectedRevision,
    CancellationToken ct = default)
  {
    if (lines.Count < 2)
      return CommandResult<long>.Fail(ErrorCodes.Accounting.JournalRejected, "A journal needs at least two lines.");

    var check = CheckLines(lines);
    if (check is not null)
      return CommandResult<long>.Fail(ErrorCodes.Accounting.JournalRejected, check);

    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 4000 ||
        string.IsNullOrWhiteSpace(evidenceReference) || evidenceReference.Trim().Length > 2000)
      return CommandResult<long>.Fail(ErrorCodes.Accounting.JournalRejected, "Journal reason and evidence are required.");

    var journal = await db.AdjustmentJournals.SingleOrDefaultAsync(j => j.Id == journalId, ct);
    if (journal is null)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(journal.FirmId, journal.ClientId, journal.EngagementId, PreparerRoles), ct);
    if (!auth.Succeeded)
      return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);

    if (journal.Status != "Draft")
      return CommandResult<long>.Fail(ErrorCodes.ProtectedState, "Only a draft journal can be updated.");

    if (journal.Revision != expectedRevision)
      return CommandResult<long>.Fail(ErrorCodes.StaleRevision, "The journal revision is outdated.");

    if (db is IAdjustmentJournalDbContext decisionDb &&
        await decisionDb.AdjustmentJournalManagementDecisions.AnyAsync(x =>
          x.FirmId == journal.FirmId && x.ClientId == journal.ClientId && x.EngagementId == journal.EngagementId &&
          x.JournalId == journal.Id && x.JournalRevision == journal.Revision, ct))
    {
      return CommandResult<long>.Fail(ErrorCodes.ProtectedState, "A journal with recorded management decisions cannot be mutated in place.");
    }

    var oldLines = await db.AdjustmentLines.Where(l => l.JournalId == journal.Id).ToListAsync(ct);
    db.AdjustmentLines.RemoveRange(oldLines);

    foreach (var (code, debit, credit) in lines)
    {
      db.AdjustmentLines.Add(new AdjustmentLine
      {
        Id = Guid.CreateVersion7(),
        JournalId = journal.Id,
        AccountCode = code.Trim(),
        Debit = MoneyPolicy.Normalize(debit),
        Credit = MoneyPolicy.Normalize(credit)
      });
    }

    journal.Reason = reason.Trim();
    journal.EvidenceReference = evidenceReference.Trim();
    journal.Revision++;
    await db.SaveChangesAsync(ct);
    return CommandResult<long>.Ok(journal.Revision);
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

  public static async Task<CommandResult<AdjustmentInstructionExport>> BuildInstructionExportAsync(
    IAdjustmentJournalDbContext db, ActorContext actor, Guid journalId, CancellationToken ct = default)
  {
    var journal = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == journalId && x.FirmId == actor.FirmId, ct);
    if (journal is null)
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(journal.FirmId, journal.ClientId, journal.EngagementId,
        ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<AdjustmentInstructionExport>.Fail(auth.ErrorCode!, auth.Message!);

    if (journal.Status is "Void" or "ReflectedInSource")
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.ProtectedState,
        "Only an active draft or posted journal can be exported as instructions.");
    if (journal.PeriodId is not { } periodId)
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.GateBlocked,
        "The journal is not bound to a client reporting period.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == journal.BaseDatasetId && x.FirmId == journal.FirmId && x.ClientId == journal.ClientId &&
      x.EngagementId == journal.EngagementId, ct);
    if (dataset is null || dataset.ValidationStatus != "Accepted" ||
        string.IsNullOrWhiteSpace(dataset.RawFileSha256Hex) ||
        string.IsNullOrWhiteSpace(dataset.NormalizedDatasetDigest))
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.GateBlocked,
        "The validated source receipt and both source digests are required before export.");

    if (db is not IClientAccountingDbContext clientDb)
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.GateBlocked,
        "Client reporting context is unavailable for this export.");
    var period = await clientDb.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == periodId && x.FirmId == journal.FirmId && x.ClientId == journal.ClientId, ct);
    if (period is null || !string.Equals(period.Currency, dataset.Currency, StringComparison.Ordinal) ||
        !string.Equals(period.Basis, dataset.Basis, StringComparison.OrdinalIgnoreCase))
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.GateBlocked,
        "The journal, source and client reporting period do not share the same basis and currency.");

    ClientReportingBook? book = null;
    if (journal.BookId is { } bookId)
    {
      book = await clientDb.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == bookId && x.FirmId == journal.FirmId && x.ClientId == journal.ClientId && x.PeriodId == period.Id, ct);
      if (book is null || !string.Equals(book.Basis, dataset.Basis, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(book.Currency, dataset.Currency, StringComparison.Ordinal))
        return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.ScopeDenied,
          "The journal book is outside the selected reporting period.");
    }

    var decision = await db.AdjustmentJournalManagementDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == journal.FirmId && x.ClientId == journal.ClientId && x.EngagementId == journal.EngagementId &&
      x.JournalId == journal.Id && x.JournalRevision == journal.Revision, ct);
    if (decision is null || decision.Decision is not (ManagementDecisionStates.Accepted or ManagementDecisionStates.Partial))
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.GateBlocked,
        "Accepted or partial management approval evidence is required before export.");

    var lines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == journal.Id)
      .OrderBy(x => x.Id).ToListAsync(ct);
    if (lines.Count < 2)
      return CommandResult<AdjustmentInstructionExport>.Fail(ErrorCodes.Accounting.JournalRejected,
        "A journal needs at least two lines before export.");

    var headers = new[]
    {
      "export_type", "external_posting_status", "journal_id", "journal_number", "journal_revision", "journal_status",
      "base_dataset_id", "source_revision", "source_kind", "source_raw_sha256", "source_normalized_digest",
      "source_profile_version", "legal_entity", "period_id", "period_code", "period_start", "period_end",
      "book_id", "book_code", "basis", "currency", "purpose", "origin", "reason", "journal_evidence_reference",
      "management_decision", "management_evidence_mode", "management_evidence_reference", "management_decided_at",
      "account_code", "debit", "credit"
    };
    var rows = new List<string> { string.Join(',', headers.Select(CsvCell)) };
    foreach (var line in lines)
    {
      var values = new[]
      {
        CsvCell("ADJUSTMENT_INSTRUCTION"), CsvCell("NOT_PROOF_OF_EXTERNAL_POSTING"), CsvCell(journal.Id.ToString("D")),
        CsvCell(journal.JournalNumber), journal.Revision.ToString(CultureInfo.InvariantCulture), CsvCell(journal.Status),
        CsvCell(dataset.Id.ToString("D")), dataset.Revision.ToString(CultureInfo.InvariantCulture), CsvCell(dataset.SourceKind),
        CsvCell(dataset.RawFileSha256Hex), CsvCell(dataset.NormalizedDatasetDigest), CsvCell(dataset.ImportProfileVersion),
        CsvCell(dataset.LegalEntityKey), CsvCell(period.Id.ToString("D")), CsvCell(period.PeriodCode),
        CsvCell(period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), CsvCell(period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
        CsvCell(journal.BookId?.ToString("D")), CsvCell(book?.Code), CsvCell(journal.Basis ?? dataset.Basis), CsvCell(journal.Currency ?? dataset.Currency),
        CsvCell(journal.Purpose), CsvCell(journal.Origin), CsvCell(journal.Reason), CsvCell(journal.EvidenceReference),
        CsvCell(decision.Decision), CsvCell(decision.EvidenceMode), CsvCell(decision.EvidenceReference),
        CsvCell(decision.DecidedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)), CsvCell(line.AccountCode),
        line.Debit.ToString("0.######", CultureInfo.InvariantCulture), line.Credit.ToString("0.######", CultureInfo.InvariantCulture)
      };
      rows.Add(string.Join(',', values));
    }

    var safeNumber = new string(journal.JournalNumber.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
    if (string.IsNullOrWhiteSpace(safeNumber)) safeNumber = "journal";
    return CommandResult<AdjustmentInstructionExport>.Ok(new AdjustmentInstructionExport(
      $"auditsphere-adjustment-{safeNumber}-r{journal.Revision}.csv", string.Join('\n', rows) + '\n', journal.Revision));
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

  private static string CsvCell(string? value)
  {
    var text = value ?? string.Empty;
    if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@') text = "'" + text;
    return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
  }
}
