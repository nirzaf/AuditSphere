using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record GeneralLedgerAcceptanceReview(
  Guid ImportBatchId, Guid ClientId, Guid EngagementId, Guid PeriodId, string PeriodCode,
  Guid? BookId, string Currency, string Entity, string ImportState, int RowCount,
  string RawFileSha256, string SourceHash, string ProfileVersion, string ParserVersion,
  Guid ImportedByUserId, DateTimeOffset ImportedAt, long InputGeneration,
  SourceAcceptanceDecision? Receipt, SelectedSourceDto? Selected,
  string Revision, bool CanAccept, string? Blocker);

public static partial class SourceAcceptanceWorkspace
{
  // GL has no TB revision/validation/balance fields. Sealing is not a completeness verdict.
  public static async Task<CommandResult<GeneralLedgerAcceptanceReview>> GetGeneralLedgerAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var first = await ReadGeneralLedgerAsync(db, actor, id, ct);
    if (!first.Succeeded) return first;
    var second = await ReadGeneralLedgerAsync(db, actor, id, ct);
    if (!second.Succeeded) return second;
    if (first.Value!.Revision != second.Value!.Revision)
      return CommandResult<GeneralLedgerAcceptanceReview>.Fail(ErrorCodes.StaleRevision, "Source acceptance inputs changed. Refresh and review again.");
    return second;
  }

  private static async Task<CommandResult<GeneralLedgerAcceptanceReview>> ReadGeneralLedgerAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid id, CancellationToken ct)
  {
    var source = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == id && x.SourceKind == AccountingSourceKinds.GeneralLedger && x.Status == "SEALED", ct);
    if (source is null) return CommandResult<GeneralLedgerAcceptanceReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, source.ClientId, source.EngagementId, GeneralLedgerQuery.ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<GeneralLedgerAcceptanceReview>.Fail(auth.ErrorCode!, auth.Message!);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.Id == source.ClientId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == source.EngagementId && x.PracticeClientId == source.ClientId, ct);
    if (generation is null || firm is null || engagement is null)
      return CommandResult<GeneralLedgerAcceptanceReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == source.ClientId && x.Id == source.PeriodId, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == source.EngagementId && x.State == FileFreezeStates.Frozen, ct);
    var receipt = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == source.ClientId && x.EngagementId == source.EngagementId &&
      x.SourceKind == AccountingSourceKinds.GeneralLedger && x.ImportBatchId == id && x.Decision == "ACCEPTED", ct);
    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, actor,
      source.ClientId, source.EngagementId, AccountingSourceKinds.GeneralLedger, ct);
    if (!selected.Succeeded) return CommandResult<GeneralLedgerAcceptanceReview>.Fail(selected.ErrorCode!, selected.Message!);
    var write = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, source.ClientId, source.EngagementId, ReviewerRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    var blocker = receipt is not null ? "This source already has a retained acceptance decision. It will not be accepted again." :
      !write.Succeeded ? "Current scoped reviewer authority and an unblocked engagement are required." :
      source.CreatedByUserId == Guid.Empty || source.CreatedByUserId == actor.UserId ? "A reviewer other than the source importer must accept this revision." :
      !ValidHash(source.RawFileSha256Hex) || !ValidHash(source.NormalizedDatasetDigest) ? "The sealed source has no valid immutable identity digest." :
      period is null || period.Status != AccountingWorkflowStates.Active ? "The source reporting period is unavailable or closed." :
      frozen ? "The engagement file is frozen. An approved amendment is required." : null;
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, source.ClientId, source.EngagementId, GeneralLedgerQuery.ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<GeneralLedgerAcceptanceReview>.Fail(auth.ErrorCode!, auth.Message!);
    var revision = Digest(new { actor.FirmId, actor.UserId, actor.SessionEpoch, source, generation, firm,
      engagement.Generation, engagement.ProfessionalWorkBlocked, period, frozen, receipt, Selected = selected.Value, blocker });
    return CommandResult<GeneralLedgerAcceptanceReview>.Ok(new(source.Id, source.ClientId, source.EngagementId,
      source.PeriodId, period?.PeriodCode ?? "", source.BookId, source.Currency, source.LegalEntityKey, source.Status,
      source.RowCount, source.RawFileSha256Hex, source.NormalizedDatasetDigest, source.ProfileVersion, source.ParserVersion,
      source.CreatedByUserId, source.CreatedAt, generation.Value, receipt, selected.Value, revision, blocker is null, blocker));
  }

  public static async Task<CommandResult<Guid>> AcceptGeneralLedgerAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid id, string revision, string evidenceReference, bool reviewed, CancellationToken ct = default)
  {
    if (!ValidHash(revision)) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Load and review the current source before acceptance.");
    var current = await GetGeneralLedgerAsync(db, actor, id, ct);
    if (!current.Succeeded) return CommandResult<Guid>.Fail(current.ErrorCode!, current.Message!);
    return await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, actor,
      new(current.Value!.ClientId, current.Value.EngagementId, AccountingSourceKinds.GeneralLedger,
        null, id, evidenceReference, revision, reviewed), ct);
  }
}
