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
    var currency = (string.IsNullOrWhiteSpace(request.FunctionalCurrency) ? AccountingDefaults.DefaultCurrency : request.FunctionalCurrency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid client accounting profile is required.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.ClientAccountingProfiles.AnyAsync(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The client already has an accounting profile.");

    var profile = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId,
      Jurisdiction = request.Jurisdiction.Trim(), FunctionalCurrency = currency,
      FiscalYearStartMonth = request.FiscalYearStartMonth, FiscalYearStartDay = request.FiscalYearStartDay,
      SourceSystem = request.SourceSystem.Trim(), SourceSystemIdentifier = request.SourceSystemIdentifier.Trim(),
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientAccountingProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static async Task<CommandResult<long>> ReviseProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid profileId, ClientAccountingProfileRequest request,
    long expectedRevision, CancellationToken ct = default)
  {
    var currency = (string.IsNullOrWhiteSpace(request.FunctionalCurrency) ? AccountingDefaults.DefaultCurrency : request.FunctionalCurrency).Trim().ToUpperInvariant();
    if (request.ClientId == Guid.Empty || currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        request.FiscalYearStartMonth is < 1 or > 12 || request.FiscalYearStartDay is < 1 or > 31 ||
        string.IsNullOrWhiteSpace(request.Jurisdiction) || string.IsNullOrWhiteSpace(request.SourceSystem))
      return CommandResult<long>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid client accounting profile is required.");
    var profile = await db.ClientAccountingProfiles.SingleOrDefaultAsync(x => x.Id == profileId && x.FirmId == actor.FirmId, ct);
    if (profile is null)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (profile.ClientId != request.ClientId)
      return CommandResult<long>.Fail(ErrorCodes.ScopeDenied, "The profile belongs to a different client.");
    var auth = await AuthorizeClientAsync(db, actor, request.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<long>.Fail(auth.ErrorCode!, auth.Message!);
    if (profile.Revision != expectedRevision)
      return CommandResult<long>.Fail(ErrorCodes.StaleRevision, "The profile revision is outdated.");

    profile.Jurisdiction = request.Jurisdiction.Trim();
    profile.FunctionalCurrency = currency;
    profile.FiscalYearStartMonth = request.FiscalYearStartMonth;
    profile.FiscalYearStartDay = request.FiscalYearStartDay;
    profile.SourceSystem = request.SourceSystem.Trim();
    profile.SourceSystemIdentifier = request.SourceSystemIdentifier.Trim();
    profile.Revision++;
    await db.SaveChangesAsync(ct);
    return CommandResult<long>.Ok(profile.Revision);
  }
}
