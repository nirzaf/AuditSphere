using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Security;

public sealed record AdministrationRuntimeInput(string Environment, bool ExternalEffectsEnabled, bool SimulationAdaptersAllowed);
public sealed record AdministrationRuntimeStatus(Guid FirmId, bool SafetyStateRecorded, string? OperatingMode,
  string? DeploymentEpoch, string Environment, bool ExternalEffectsEnabled, bool SimulationAdaptersAllowed);

public static partial class FirmAdministrationQuery
{
  /// <summary>Allowlisted runtime flags and persisted safety state; never configuration dumps or inferred readiness.</summary>
  public static async Task<CommandResult<AdministrationRuntimeStatus>> RuntimeAsync(IAuditSphereDbContext db,
    ActorContext actor, AdministrationRuntimeInput runtime, CancellationToken ct = default)
  {
    var request = new AuthorizationRequest(actor.FirmId, RequiredRoles:["Administrator"], InternalOnly:true, RequireFirmWide:true);
    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded) return CommandResult<AdministrationRuntimeStatus>.Fail(authorization.ErrorCode!,authorization.Message!);
    var safety = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==actor.FirmId,ct);
    var environment = runtime.Environment is "Development" or "Test" or "Acceptance" or "Staging" or "Production" ? runtime.Environment : "CUSTOM";
    authorization = await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct);
    if (!authorization.Succeeded) return CommandResult<AdministrationRuntimeStatus>.Fail(authorization.ErrorCode!,authorization.Message!);
    return CommandResult<AdministrationRuntimeStatus>.Ok(new(actor.FirmId,safety is not null,safety?.OperatingMode,
      safety?.DeploymentEpoch.ToString(CultureInfo.InvariantCulture),environment,runtime.ExternalEffectsEnabled,runtime.SimulationAdaptersAllowed));
  }
}
