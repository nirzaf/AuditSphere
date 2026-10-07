using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
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
    if (request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem) ||
        !ClientAccountingSourceModes.IsSupported(sourceMode))
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

    var profile = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      Jurisdiction = request.Jurisdiction.Trim(), FunctionalCurrency = currency,
      FiscalYearStartMonth = request.FiscalYearStartMonth, FiscalYearStartDay = request.FiscalYearStartDay,
      SourceSystem = request.SourceSystem.Trim(), SourceSystemIdentifier = request.SourceSystemIdentifier.Trim(),
      SourceMode = sourceMode,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientAccountingProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static async Task<CommandResult<long>> ReviseProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid profileId, ClientAccountingProfileRequest request,
    long expectedRevision, CancellationToken ct = default)
  {
    var sourceMode = (request.SourceMode ?? string.Empty).Trim().ToUpperInvariant();
    var currency = (string.IsNullOrWhiteSpace(request.FunctionalCurrency) ? AccountingDefaults.DefaultCurrency : request.FunctionalCurrency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem) ||
        !ClientAccountingSourceModes.IsSupported(sourceMode))
      return CommandResult<long>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid client accounting profile is required.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {actor.FirmId} FOR UPDATE").SingleOrDefaultAsync(ct);
    var profile = await db.ClientAccountingProfiles.SingleOrDefaultAsync(x => x.Id == profileId && x.FirmId == actor.FirmId, ct);
    if (profile is null)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (profile.ClientId != request.ClientId)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "The profile belongs to a different client.");
    var roles = sourceMode == ClientAccountingSourceModes.NativeBookkeeping ? ReviewerRoles : PreparerRoles;
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    var bookIdentityChanged = profile.Jurisdiction != request.Jurisdiction.Trim() ||
      profile.FunctionalCurrency != currency || profile.FiscalYearStartMonth != request.FiscalYearStartMonth ||
      profile.FiscalYearStartDay != request.FiscalYearStartDay || profile.SourceSystem != request.SourceSystem.Trim() ||
      profile.SourceSystemIdentifier != request.SourceSystemIdentifier.Trim() || profile.SourceMode != sourceMode;
    if (bookIdentityChanged && await db.ClientReportingPeriods.AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<long>.Fail(ErrorCodes.GateBlocked,
        "This client book has reporting periods. Changing its currency, fiscal calendar, source identity, jurisdiction or source mode requires a reviewed cutover, which is not configured.");
    if (profile.SourceMode == ClientAccountingSourceModes.NativeBookkeeping && sourceMode != profile.SourceMode)
      return CommandResult<long>.Fail(ErrorCodes.GateBlocked, "Changing a native bookkeeping book back to external-source mode requires a reviewed cutover.");
    var modeGate = await ValidateSourceModeAsync(db, actor, request.ClientId, sourceMode, ct);
    if (!modeGate.Succeeded)
      return CommandResult<long>.Fail(modeGate.ErrorCode!, modeGate.Message!);
    if (profile.Revision != expectedRevision)
      return CommandResult<long>.Fail(ErrorCodes.StaleRevision, "The profile revision is outdated.");

    profile.Jurisdiction = request.Jurisdiction.Trim();
    profile.FunctionalCurrency = currency;
    profile.FiscalYearStartMonth = request.FiscalYearStartMonth;
    profile.FiscalYearStartDay = request.FiscalYearStartDay;
    profile.SourceSystem = request.SourceSystem.Trim();
    profile.SourceSystemIdentifier = request.SourceSystemIdentifier.Trim();
    profile.SourceMode = sourceMode;
    profile.Revision++;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<long>.Ok(profile.Revision);
  }

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
