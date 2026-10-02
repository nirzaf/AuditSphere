using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record GeneralLedgerUploadLine(string JournalId, string LineId, DateOnly PostingDate, string AccountCode, decimal Debit, decimal Credit, string OriginalCurrency, decimal OriginalAmount, decimal FunctionalAmount);
public sealed record GeneralLedgerUploadReview(Guid ClientId, Guid EngagementId, Guid PeriodId, string PeriodCode, Guid? BookId, string BookCode, string Entity, string Currency, string FileSha256, string Revision, int JournalCount, int LineCount, decimal Debit, decimal Credit, IReadOnlyList<GeneralLedgerUploadLine> Sample, Guid? ImportBatchId, string? ImportState, string? SourceHash, bool CanImport, string? Blocker);

/// <summary>One bounded explicit CSV profile composes the existing authoritative GL importer.
/// A retained sealed source is not selected, complete or independently accepted.</summary>
public static class GeneralLedgerUploadWorkspace
{
    private static readonly string[] Roles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];
    private sealed record Plan(GeneralLedgerUploadReview Review, GeneralLedgerImportRequest Request);
    private static string Digest(object value) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));

    public static async Task<CommandResult<GeneralLedgerUploadReview>> PreviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, string name, byte[] bytes, CancellationToken ct = default)
    {
        var first = await PlanAsync(db, actor, engagementId, name, bytes, ct);
        if (!first.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(first.ErrorCode!, first.Message!);
        var final = await PlanAsync(db, actor, engagementId, name, bytes, ct);
        if (!final.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(final.ErrorCode!, final.Message!);
        return first.Value!.Review.Revision == final.Value!.Review.Revision ? CommandResult<GeneralLedgerUploadReview>.Ok(final.Value.Review) :
          CommandResult<GeneralLedgerUploadReview>.Fail(ErrorCodes.StaleRevision, "GL upload context changed. Preview the exact file again.");
    }

    private static async Task<CommandResult<Plan>> PlanAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, string name, byte[] bytes, CancellationToken ct)
    {
        var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
        if (engagement is null) return CommandResult<Plan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        var clientId = engagement.PracticeClientId;
        var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, engagementId, Roles, InternalOnly: true, RequireProfessionalWork: true), ct);
        if (!auth.Succeeded) return CommandResult<Plan>.Fail(auth.ErrorCode!, auth.Message!);
        GeneralLedgerCsvProfile.Parsed parsed;
        try { parsed = GeneralLedgerCsvProfile.Parse(name, bytes); }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException or OverflowException) { return CommandResult<Plan>.Fail(ErrorCodes.Accounting.ImportRejected, ex is FormatException ? ex.Message : "The GL CSV must be valid UTF-8 with supported exact values."); }
        var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodCode == parsed.PeriodCode, ct);
        if (period is null) return CommandResult<Plan>.Fail(ErrorCodes.Accounting.ImportRejected, "The file's reporting period is unavailable for this client.");
        var book = parsed.BookCode.Length == 0 ? null : await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.PeriodId == period.Id && x.Code == parsed.BookCode, ct);
        if (parsed.BookCode.Length > 0 && book is null) return CommandResult<Plan>.Fail(ErrorCodes.Accounting.ImportRejected, "The file's reporting book is unavailable for this client period.");
        var context = await ClientAccountingService.ResolveGeneralLedgerImportContextAsync(db, actor, clientId, period.Id, book?.Id, parsed.Currency, ct);
        if (!context.Succeeded) return CommandResult<Plan>.Fail(context.ErrorCode!, context.Message!);
        var valid = ClientAccountingService.ValidateGeneralLedgerTransactions(parsed.Transactions, period, parsed.Currency, context.Value!.Accounts, context.Value.DimensionCodes, GeneralLedgerCsvProfile.MaxJournals, GeneralLedgerCsvProfile.MaxLines);
        if (!valid.Succeeded) return CommandResult<Plan>.Fail(valid.ErrorCode!, valid.Message!);
        var fileHash = Hashing.Sha256Hex(bytes);
        var request = new GeneralLedgerImportRequest(clientId, engagementId, period.Id, book?.Id, GeneralLedgerCsvProfile.Version, GeneralLedgerCsvProfile.Version, fileHash, parsed.Entity, parsed.Currency, "GL CSV v1 file SHA-256 " + fileHash, parsed.Transactions);
        var normalized = Hashing.Sha256Hex(Encoding.UTF8.GetBytes("gl-import.single.v2\n" + ClientAccountingService.ComputeGeneralLedgerChunkDigest(parsed.Currency, parsed.Transactions)));
        var existing = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.RawFileSha256Hex == fileHash, ct);
        if (existing is not null && (existing.SourceKind != AccountingSourceKinds.GeneralLedger || existing.PeriodId != period.Id || existing.BookId != book?.Id || existing.LegalEntityKey != parsed.Entity || existing.Currency != parsed.Currency || existing.NormalizedDatasetDigest != normalized || existing.ProfileVersion != GeneralLedgerCsvProfile.Version))
            return CommandResult<Plan>.Fail(ErrorCodes.IdempotencyConflict, "The exact file already has a different persisted source context. Resolve it before another import.");
        var equivalent = existing is null && await db.SourceImportBatches.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.PeriodId == period.Id && x.NormalizedDatasetDigest == normalized, ct);
        var frozen = await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.State == "FROZEN", ct);
        var blocker = period.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft) ? "The reporting period is closed or unavailable." :
          book is not null && book.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft) ? "The reporting book is closed or unavailable." :
          frozen ? "The engagement file is frozen. An approved amendment is required." :
          existing is not null ? "This exact file already has a retained source. Inspect it; do not import it again." :
          equivalent ? "Equivalent normalized GL rows already exist with different file bytes. Resolve the source identity rather than creating a duplicate." : null;
        var firm = await db.FirmSafetyStates.AsNoTracking().SingleAsync(x => x.Id == actor.FirmId, ct);
        var safety = await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == clientId && x.FirmId == actor.FirmId, ct);
        // Receipt progression is excluded: an exact idempotent replay cannot change the reviewed import intent.
        var revision = Digest(new
        {
            actor.FirmId,
            actor.UserId,
            actor.SessionEpoch,
            engagementId,
            clientId,
            fileHash,
            period,
            book,
            frozen,
            firm,
            safety,
            accounts = context.Value.Accounts.Values.OrderBy(x => x.AccountCode).ToArray(),
            dimensions = context.Value.DimensionCodes.OrderBy(x => x.Key).Select(x => new { type = x.Key, codes = x.Value.OrderBy(v => v).ToArray() }).ToArray()
        });
        auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, clientId, engagementId, Roles, InternalOnly: true, RequireProfessionalWork: true), ct);
        if (!auth.Succeeded) return CommandResult<Plan>.Fail(auth.ErrorCode!, auth.Message!);
        var lines = parsed.Transactions.SelectMany(x => x.Lines.Select(l => new GeneralLedgerUploadLine(x.StableJournalId, l.StableLineId, x.PostingDate, l.AccountCode, l.Debit, l.Credit, l.OriginalCurrency, l.OriginalAmount, l.FunctionalAmount))).ToArray();
        return CommandResult<Plan>.Ok(new(new(clientId, engagementId, period.Id, period.PeriodCode, book?.Id, parsed.BookCode, parsed.Entity, parsed.Currency, fileHash, revision, parsed.Transactions.Count, lines.Length, lines.Sum(x => x.Debit), lines.Sum(x => x.Credit), lines.Take(20).ToArray(), existing?.Id, existing?.Status, existing?.NormalizedDatasetDigest, blocker is null, blocker), request));
    }

    public static async Task<CommandResult<GeneralLedgerUploadReview>> ImportAsync(IClientAccountingDbContext db, ActorContext actor, Guid engagementId, string name, byte[] bytes, string revision, string fileHash, bool reviewed, CancellationToken ct = default)
    {
        var initial = await PlanAsync(db, actor, engagementId, name, bytes, ct);
        if (!initial.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(initial.ErrorCode!, initial.Message!);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var locked = await GeneralLedgerCompletenessWorkspace.LockAsync(db, actor, initial.Value!.Review.ClientId, engagementId, ct);
        if (!locked.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(locked.ErrorCode!, locked.Message!);
        var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, initial.Value.Review.ClientId, engagementId, initial.Value.Review.PeriodId, initial.Value.Review.BookId, ct);
        if (!mutable.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(mutable.ErrorCode!, mutable.Message!);
        var plan = await PlanAsync(db, actor, engagementId, name, bytes, ct);
        if (!plan.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(plan.ErrorCode!, plan.Message!);
        var p = plan.Value!.Review;
        if (!reviewed || !SourceAcceptanceWorkspace.ValidHash(revision) || p.Revision != revision || p.FileSha256 != fileHash)
            return CommandResult<GeneralLedgerUploadReview>.Fail(ErrorCodes.StaleRevision, "The exact GL file or reviewed context changed. Preview and review again.");
        if (p.ImportBatchId is not null)
        {
            if (p.ImportState != "SEALED") return CommandResult<GeneralLedgerUploadReview>.Fail(ErrorCodes.GateBlocked, "The existing source is unsealed. Reconcile its import before proceeding.");
            await tx.CommitAsync(ct); return CommandResult<GeneralLedgerUploadReview>.Ok(p);
        }
        if (!p.CanImport) return CommandResult<GeneralLedgerUploadReview>.Fail(ErrorCodes.GateBlocked, p.Blocker!);
        var imported = await ClientAccountingService.ImportGeneralLedgerAsync(db, actor, plan.Value.Request, ct);
        if (!imported.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(imported.ErrorCode!, imported.Message!);
        var final = await PlanAsync(db, actor, engagementId, name, bytes, ct);
        if (!final.Succeeded) return CommandResult<GeneralLedgerUploadReview>.Fail(final.ErrorCode!, final.Message!);
        if (final.Value!.Review.Revision != revision || final.Value.Review.ImportBatchId != imported.Value)
            return CommandResult<GeneralLedgerUploadReview>.Fail(ErrorCodes.StaleRevision, "GL upload inputs changed before publication. No new source was committed.");
        await tx.CommitAsync(ct);
        return CommandResult<GeneralLedgerUploadReview>.Ok(final.Value.Review);
    }
}
