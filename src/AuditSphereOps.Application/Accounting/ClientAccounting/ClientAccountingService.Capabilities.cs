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
  public static async Task<CommandResult<Guid>> CreateCapabilityProfileAsync(
    IClientAccountingDbContext db, ActorContext actor, CapabilityProfileRequest request,
    CancellationToken ct = default)
  {
    var serviceKind = (request.ServiceKind ?? string.Empty).Trim().ToUpperInvariant();
    var serviceRoute = (request.ServiceRoute ?? string.Empty).Trim();
    var validScope = serviceKind switch
    {
      AccountingCapabilityServiceKinds.GroupReporting => request.GroupId.HasValue && !request.ClientId.HasValue,
      AccountingCapabilityServiceKinds.EntityReporting or AccountingCapabilityServiceKinds.AuditOnly =>
        request.ClientId.HasValue && !request.GroupId.HasValue,
      _ => false
    };
    if (!validScope || string.IsNullOrWhiteSpace(request.Framework))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A capability needs a supported service kind and its matching client or group scope.");
    var currency = (string.IsNullOrWhiteSpace(request.ReportingCurrency) ? AccountingDefaults.DefaultCurrency : request.ReportingCurrency).Trim().ToUpperInvariant();
    var consolidationMethod = request.ConsolidationMethod.Trim().ToUpperInvariant();
    if (currency.Length != 3 || currency.Any(c => c is < 'A' or > 'Z') ||
        (!string.IsNullOrWhiteSpace(consolidationMethod) &&
         consolidationMethod is not (ConsolidationCalculator.RestrictedMethod or ConsolidationCalculator.ForeignOperationMethod) &&
         !AdvancedConsolidationMethods.All.Contains(consolidationMethod)))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The requested accounting or consolidation method is not enabled.");
    if (serviceKind is AccountingCapabilityServiceKinds.EntityReporting or AccountingCapabilityServiceKinds.AuditOnly)
    {
      if (serviceRoute.Length == 0)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "A client capability must identify its approved service route.");
      var permitted = await db.AcceptanceDecisions.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.PracticeClientId == request.ClientId && x.EngagementId == null &&
        x.ServiceRoute == serviceRoute && (x.Decision == "Accepted" || x.Decision == "AcceptedWithConditions"), ct);
      if (!permitted)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "An affirmative service-permissibility decision is required before enabling client reporting.");
    }
    var auth = request.ClientId.HasValue
      ? await AuthorizeClientAsync(db, actor, request.ClientId.Value, ReviewerRoles, ct)
      : await AuthorizeGroupAsync(db, actor, request.GroupId!.Value, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var profile = new AccountingCapabilityProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = request.ClientId, GroupId = request.GroupId,
      ServiceKind = serviceKind, ServiceRoute = serviceRoute, Framework = request.Framework.Trim(), Edition = request.Edition.Trim(),
      PeriodRule = request.PeriodRule.Trim(), ReportingCurrency = currency, AccountingMethod = request.AccountingMethod.Trim(),
      ConsolidationMethod = consolidationMethod, ReviewHierarchy = request.ReviewHierarchy.Trim(),
      TemplateFamily = request.TemplateFamily.Trim(), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AccountingCapabilityProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(profile.Id);
  }

  public static async Task<CommandResult> RecordCapabilityAcceptanceAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid profileId, string stage,
    string evidenceReference, CancellationToken ct = default)
  {
    stage = stage.Trim().ToUpperInvariant();
    if (stage is not (AccountingCapabilityAcceptanceStages.LocalConstruction or AccountingCapabilityAcceptanceStages.MethodOwnerApproval or
      AccountingCapabilityAcceptanceStages.LiveEvidence or AccountingCapabilityAcceptanceStages.Released) ||
        string.IsNullOrWhiteSpace(evidenceReference))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Capability acceptance needs a known stage and evidence.");
    var profile = await db.AccountingCapabilityProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == profileId && x.FirmId == actor.FirmId, ct);
    if (profile is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = profile.ClientId.HasValue
      ? await AuthorizeClientAsync(db, actor, profile.ClientId.Value, ReviewerRoles, ct)
      : await AuthorizeGroupAsync(db, actor, profile.GroupId!.Value, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (stage is AccountingCapabilityAcceptanceStages.MethodOwnerApproval or AccountingCapabilityAcceptanceStages.Released &&
        profile.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The capability preparer cannot provide independent acceptance.");
    if (await db.AccountingCapabilityAcceptances.AnyAsync(x => x.FirmId == actor.FirmId && x.CapabilityProfileId == profileId && x.Stage == stage, ct))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "This capability stage already has an acceptance record.");
    db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, CapabilityProfileId = profileId,
      Stage = stage, Status = AccountingWorkflowStates.Approved, EvidenceReference = evidenceReference.Trim(),
      DecidedByUserId = actor.UserId, DecidedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }
}
