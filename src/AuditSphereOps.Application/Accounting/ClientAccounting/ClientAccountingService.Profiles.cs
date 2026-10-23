using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientAccountingService
{
  public static async Task<CommandResult<Guid>> CreateProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientAccountingProfileRequest request,
    CancellationToken ct = default)
  {
    var sourceMode = (request.SourceMode ?? string.Empty).Trim().ToUpperInvariant();
    var currency = (string.IsNullOrWhiteSpace(request.FunctionalCurrency) ? AccountingDefaults.DefaultCurrency : request.FunctionalCurrency).Trim().ToUpperInvariant();
    var reason = NormalizeProfileReason(request.Reason);
    if (request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem) ||
        !ClientAccountingSourceModes.IsSupported(sourceMode) || IsInvalidProfileReason(request.Reason))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid client accounting profile is required.");
    var roles = sourceMode == ClientAccountingSourceModes.NativeBookkeeping ? ReviewerRoles : PreparerRoles;
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (sourceMode == ClientAccountingSourceModes.NativeBookkeeping &&
        await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A client with existing reporting periods requires a reviewed cutover before native bookkeeping can be enabled.");
    var modeGate = await ValidateSourceModeAsync(db, actor, request.ClientId, sourceMode, ct);
    if (!modeGate.Succeeded)
      return CommandResult<Guid>.Fail(modeGate.ErrorCode!, modeGate.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    auth = await AuthorizeClientAsync(db, actor, request.ClientId, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (sourceMode == ClientAccountingSourceModes.NativeBookkeeping &&
        await db.ClientReportingPeriods.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A client with existing reporting periods requires a reviewed cutover before native bookkeeping can be enabled.");
    if (await db.ClientAccountingProfiles.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The client already has an accounting profile.");

    var createdAt = DateTimeOffset.UtcNow;
    var profile = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      Jurisdiction = request.Jurisdiction.Trim(), FunctionalCurrency = currency,
      FiscalYearStartMonth = request.FiscalYearStartMonth, FiscalYearStartDay = request.FiscalYearStartDay,
      SourceSystem = request.SourceSystem.Trim(), SourceSystemIdentifier = request.SourceSystemIdentifier.Trim(),
      SourceMode = sourceMode,
      CreatedByUserId = actor.UserId, CreatedAt = createdAt
    };
    db.ClientAccountingProfiles.Add(profile);
    db.ClientAccountingProfileRevisionRecords.Add(ClientAccountingProfileRevisionRecord.FromProfile(
      profile, Guid.CreateVersion7(), previousRevisionId: null, reason, actor.UserId, createdAt));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static Task<CommandResult<long>> ReviseProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid profileId, ClientAccountingProfileRequest request,
    long expectedRevision, CancellationToken ct = default) =>
    ReviseProfileAsync(db, actor, new ClientAccountingProfileRevisionRef(profileId, expectedRevision), request, ct);

  public static async Task<CommandResult<long>> ReviseProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, ClientAccountingProfileRevisionRef expectedProfileRevision,
    ClientAccountingProfileRequest request, CancellationToken ct = default)
  {
    var sourceMode = (request.SourceMode ?? string.Empty).Trim().ToUpperInvariant();
    var currency = (string.IsNullOrWhiteSpace(request.FunctionalCurrency) ? AccountingDefaults.DefaultCurrency : request.FunctionalCurrency).Trim().ToUpperInvariant();
    var reason = NormalizeProfileReason(request.Reason);
    if (!expectedProfileRevision.IsValid || request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem) ||
        !ClientAccountingSourceModes.IsSupported(sourceMode) || IsInvalidProfileReason(request.Reason))
      return CommandResult<long>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid client accounting profile is required.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    var profile = await db.ClientAccountingProfiles.SingleOrDefaultAsync(
      x => x.Id == expectedProfileRevision.ProfileId && x.FirmId == actor.FirmId, ct);
    if (profile is null)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (profile.ClientId != request.ClientId)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "The profile belongs to a different client.");
    if (profile.Revision != expectedProfileRevision.Revision)
      return CommandResult<long>.Fail(ErrorCodes.StaleRevision, "The profile revision is outdated.");
    var roles = sourceMode == ClientAccountingSourceModes.NativeBookkeeping ? ReviewerRoles : PreparerRoles;
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    var bookIdentityChanged = profile.Jurisdiction != request.Jurisdiction.Trim() ||
      profile.FunctionalCurrency != currency || profile.FiscalYearStartMonth != request.FiscalYearStartMonth ||
      profile.FiscalYearStartDay != request.FiscalYearStartDay || profile.SourceSystem != request.SourceSystem.Trim() ||
      profile.SourceSystemIdentifier != request.SourceSystemIdentifier.Trim() || profile.SourceMode != sourceMode;
    var accountingBasisChanged = profile.Jurisdiction != request.Jurisdiction.Trim() ||
      profile.FunctionalCurrency != currency || profile.FiscalYearStartMonth != request.FiscalYearStartMonth ||
      profile.FiscalYearStartDay != request.FiscalYearStartDay;
    if (accountingBasisChanged && reason is null)
      return CommandResult<long>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A reason is required when revising jurisdiction, functional currency or fiscal calendar.");
    if (bookIdentityChanged && await db.ClientReportingPeriods.AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<long>.Fail(ErrorCodes.GateBlocked,
        "This client book has reporting periods. Changing its currency, fiscal calendar, source identity, jurisdiction or source mode requires a reviewed cutover, which is not configured.");
    if (profile.SourceMode == ClientAccountingSourceModes.NativeBookkeeping && sourceMode != profile.SourceMode)
      return CommandResult<long>.Fail(ErrorCodes.GateBlocked, "Changing a native bookkeeping book back to external-source mode requires a reviewed cutover.");
    var modeGate = await ValidateSourceModeAsync(db, actor, request.ClientId, sourceMode, ct);
    if (!modeGate.Succeeded)
      return CommandResult<long>.Fail(modeGate.ErrorCode!, modeGate.Message!);

    var predecessor = await db.ClientAccountingProfileRevisionRecords.SingleOrDefaultAsync(
      x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
           x.ProfileId == profile.Id && x.Revision == profile.Revision, ct);
    if (predecessor is not null && !MatchesProfileHead(predecessor, profile))
      return CommandResult<long>.Fail(ErrorCodes.StaleRevision,
        "The profile head no longer matches its immutable revision snapshot.");

    profile.Jurisdiction = request.Jurisdiction.Trim();
    profile.FunctionalCurrency = currency;
    profile.FiscalYearStartMonth = request.FiscalYearStartMonth;
    profile.FiscalYearStartDay = request.FiscalYearStartDay;
    profile.SourceSystem = request.SourceSystem.Trim();
    profile.SourceSystemIdentifier = request.SourceSystemIdentifier.Trim();
    profile.SourceMode = sourceMode;
    profile.Revision++;
    var revisedAt = DateTimeOffset.UtcNow;
    db.ClientAccountingProfileRevisionRecords.Add(ClientAccountingProfileRevisionRecord.FromProfile(
      profile, Guid.CreateVersion7(), predecessor?.Id, reason, actor.UserId, revisedAt));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<long>.Ok(profile.Revision);
  }

  /// <summary>
  /// Resolves the exact immutable profile revision in firm/client scope. Legacy revisions without
  /// a persisted snapshot remain unavailable; this method does not reconstruct or backfill them.
  /// </summary>
  public static async Task<CommandResult<ResolvedClientAccountingProfileRevisionDto>> ResolveProfileRevisionAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid clientId,
    ClientAccountingProfileRevisionRef reference,
    CancellationToken ct = default)
  {
    if (clientId == Guid.Empty || !reference.IsValid)
      return CommandResult<ResolvedClientAccountingProfileRevisionDto>.Fail(
        ErrorCodes.Accounting.MappingInvalid, "An exact client profile revision reference is required.");

    var auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ResolvedClientAccountingProfileRevisionDto>.Fail(auth.ErrorCode!, auth.Message!);

    var profile = await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(
      x => x.Id == reference.ProfileId && x.FirmId == actor.FirmId, ct);
    if (profile is null || profile.ClientId != clientId)
      return CommandResult<ResolvedClientAccountingProfileRevisionDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var revision = await db.ClientAccountingProfileRevisionRecords.AsNoTracking().SingleOrDefaultAsync(
      x => x.FirmId == actor.FirmId && x.ClientId == clientId &&
           x.ProfileId == reference.ProfileId && x.Revision == reference.Revision, ct);
    if (revision is null)
      return CommandResult<ResolvedClientAccountingProfileRevisionDto>.Fail(
        ErrorCodes.GateBlocked, "The immutable snapshot for this legacy profile revision is unavailable; no history was backfilled.");

    auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ResolvedClientAccountingProfileRevisionDto>.Fail(auth.ErrorCode!, auth.Message!);

    return CommandResult<ResolvedClientAccountingProfileRevisionDto>.Ok(
      new ResolvedClientAccountingProfileRevisionDto(
        revision.FirmId,
        revision.ClientId,
        new ClientAccountingProfileRevisionRef(revision.ProfileId, revision.Revision),
        revision.Id,
        revision.PreviousRevisionId,
        revision.CreatedByUserId,
        revision.CreatedAt,
        revision.Reason,
        revision.Status,
        revision.Jurisdiction,
        revision.FunctionalCurrency,
        revision.FiscalYearStartMonth,
        revision.FiscalYearStartDay,
        revision.SourceSystem,
        revision.SourceSystemIdentifier,
        revision.SourceMode));
  }

  private static bool MatchesProfileHead(ClientAccountingProfileRevisionRecord revision, ClientAccountingProfile profile) =>
    revision.FirmId == profile.FirmId && revision.ClientId == profile.ClientId &&
    revision.ProfileId == profile.Id && revision.Revision == profile.Revision &&
    revision.Status == profile.Status && revision.Jurisdiction == profile.Jurisdiction &&
    revision.FunctionalCurrency == profile.FunctionalCurrency &&
    revision.FiscalYearStartMonth == profile.FiscalYearStartMonth &&
    revision.FiscalYearStartDay == profile.FiscalYearStartDay &&
    revision.SourceSystem == profile.SourceSystem &&
    revision.SourceSystemIdentifier == profile.SourceSystemIdentifier &&
    revision.SourceMode == profile.SourceMode;

  private static string? NormalizeProfileReason(string? reason) =>
    string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

  private static bool IsInvalidProfileReason(string? reason) =>
    reason is not null && reason.Trim().Length > 2000;

  private static async Task<CommandResult> ValidateSourceModeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, string sourceMode, CancellationToken ct)
  {
    if (sourceMode == ClientAccountingSourceModes.ExternalSource)
      return CommandResult.Ok();

    var accepted = await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db, actor.FirmId, clientId, ct: ct);
    return accepted
      ? CommandResult.Ok()
      : CommandResult.Fail(ErrorCodes.GateBlocked,
        "Native bookkeeping requires an accepted client-level BOOKKEEPING service decision without unresolved conditions.");
  }
}
