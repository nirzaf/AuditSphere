using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static async Task<CommandResult> FirmAuthAsync(
    IClientAccountingDbContext db, ActorContext actor, IReadOnlyList<string> roles, CancellationToken ct) =>
    await Application.Security.AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles.ToArray(), InternalOnly: true, RequireFirmWide: true), ct);

  private static async Task<CommandResult> GroupAuthAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId, IReadOnlyList<string> roles, CancellationToken ct)
    => await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, roles, ct);

  private static Task<bool> HasMethodOwnerAcceptanceAsync(
    IClientAccountingDbContext db, Guid firmId, Guid groupId, string method, CancellationToken ct) =>
    (from profile in db.AccountingCapabilityProfiles.AsNoTracking()
     join acceptance in db.AccountingCapabilityAcceptances.AsNoTracking()
       on new { profile.FirmId, CapabilityProfileId = profile.Id }
       equals new { acceptance.FirmId, acceptance.CapabilityProfileId }
     where profile.FirmId == firmId && profile.GroupId == groupId &&
       profile.ServiceKind == "GROUP_REPORTING" && profile.Status != AccountingWorkflowStates.Retired &&
       profile.ConsolidationMethod == method.Trim().ToUpperInvariant() &&
       acceptance.Stage == AccountingCapabilityAcceptanceStages.MethodOwnerApproval &&
     acceptance.Status == AccountingWorkflowStates.Approved
     select acceptance.Id).AnyAsync(ct);
}
