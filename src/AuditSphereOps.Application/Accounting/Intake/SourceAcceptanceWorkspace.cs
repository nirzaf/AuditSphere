using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record SourceAcceptanceReview(
  Guid DatasetId, Guid ClientId, Guid EngagementId, Guid? PeriodId, string? PeriodCode,
  long SourceRevision, string Currency, string Entity, string ImportState, string ValidationStatus,
  bool Balanced, string SourceHash, Guid ImportedByUserId, DateTimeOffset ImportedAt,
  long InputGeneration, SourceAcceptanceDecision? Receipt, SelectedSourceDto? Selected,
  string Revision, bool CanAccept, string? Blocker);

/// <summary>Review/reconciliation of one immutable TB source. Selection remains per engagement/source kind, not per period.</summary>
public static partial class SourceAcceptanceWorkspace
{
  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  internal static bool ValidHash(string? value) => value?.Length == 64 && value.All(Uri.IsHexDigit);
  private static string Digest(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));

  public static async Task<CommandResult<SourceAcceptanceReview>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var first = await ReadAsync(db, actor, id, ct);
    if (!first.Succeeded) return first;
    var second = await ReadAsync(db, actor, id, ct);
    if (!second.Succeeded) return second;
    if (first.Value!.Revision != second.Value!.Revision)
      return CommandResult<SourceAcceptanceReview>.Fail(ErrorCodes.StaleRevision, "Source acceptance inputs changed. Refresh and review again.");
    return second;
  }

  private static async Task<CommandResult<SourceAcceptanceReview>> ReadAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid id, CancellationToken ct)
  {
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id && x.SourceKind == "Raw" && x.ImportState == TrialBalanceImportStates.Sealed, ct);
    if (source is null) return CommandResult<SourceAcceptanceReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, source.ClientId, source.EngagementId, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<SourceAcceptanceReview>.Fail(auth.ErrorCode!, auth.Message!);
    var generation = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == source.ClientId).Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == source.EngagementId && x.FirmId == actor.FirmId && x.PracticeClientId == source.ClientId, ct);
    if (generation is null || firm is null || engagement is null) return CommandResult<SourceAcceptanceReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == source.ClientId && x.Id == source.PeriodId, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == source.EngagementId && x.State == FileFreezeStates.Frozen, ct);
    var receipt = await db.SourceAcceptanceDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == source.ClientId && x.EngagementId == source.EngagementId && x.TrialBalanceDatasetId == id && x.Decision == "ACCEPTED", ct);
    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, actor, source.ClientId, source.EngagementId, AccountingSourceKinds.TrialBalance, ct);
    if (!selected.Succeeded) return CommandResult<SourceAcceptanceReview>.Fail(selected.ErrorCode!, selected.Message!);
    var write = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, source.ClientId, source.EngagementId, ReviewerRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    var hash = source.NormalizedDatasetDigest.Length == 64 ? source.NormalizedDatasetDigest : source.Sha256Hex;
    var blocker = receipt is not null ? "This source already has a retained acceptance decision. It will not be accepted again." :
      !write.Succeeded ? "Current scoped reviewer authority and an unblocked engagement are required." :
      source.ImportedByUserId == Guid.Empty || source.ImportedByUserId == actor.UserId ? "A reviewer other than the source importer must accept this revision." :
      source.ValidationStatus != "Accepted" || !source.Balanced ? "Worker validation must accept a balanced, sealed source before independent source acceptance." :
      !ValidHash(hash) ? "The sealed source has no valid immutable identity digest." :
      source.PeriodId is not null && (period is null || period.Status != AccountingWorkflowStates.Active) ? "The source reporting period is unavailable or closed." :
      frozen ? "The engagement file is frozen. An approved amendment is required." : null;
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, source.ClientId, source.EngagementId, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<SourceAcceptanceReview>.Fail(auth.ErrorCode!, auth.Message!);
    var revision = Digest(new { actor.FirmId, actor.UserId, actor.SessionEpoch, source, generation, firm, engagement.Generation, engagement.ProfessionalWorkBlocked, period, frozen, receipt, Selected = selected.Value, blocker });
    return CommandResult<SourceAcceptanceReview>.Ok(new(source.Id, source.ClientId, source.EngagementId, source.PeriodId, period?.PeriodCode,
      source.Revision, source.Currency, source.LegalEntityKey, source.ImportState, source.ValidationStatus, source.Balanced,
      hash, source.ImportedByUserId, source.ImportedAt, generation.Value, receipt, selected.Value, revision, blocker is null, blocker));
  }

  public static async Task<CommandResult<Guid>> AcceptAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid id, string revision, string evidenceReference, bool reviewed, CancellationToken ct = default)
  {
    if (!ValidHash(revision)) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Load and review the current source before acceptance.");
    var current = await GetAsync(db, actor, id, ct);
    if (!current.Succeeded) return CommandResult<Guid>.Fail(current.ErrorCode!, current.Message!);
    return await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, actor,
      new(current.Value!.ClientId, current.Value.EngagementId, AccountingSourceKinds.TrialBalance, id, null, evidenceReference, revision, reviewed), ct);
  }
}
