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
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static async Task<CommandResult> AuthorizeClientAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, RequiredRoles: roles.ToArray(), InternalOnly: true), ct);

  private static async Task<CommandResult> AuthorizeFirmAsync(
    IClientAccountingDbContext db, ActorContext actor, IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles.ToArray(), InternalOnly: true, RequireFirmWide: true), ct);

  private static async Task<CommandResult> AuthorizeGroupAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid groupId, IReadOnlyList<string> roles, CancellationToken ct)
    => await AuthorizationDecision.AuthorizeGroupAsync(db, actor, groupId, roles, ct);
}
