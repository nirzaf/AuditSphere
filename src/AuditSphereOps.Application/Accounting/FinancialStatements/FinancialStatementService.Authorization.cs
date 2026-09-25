using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class FinancialStatementService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];

  private static readonly string[] PackageReadRoles = ["Administrator", "Partner", "Manager", "Reviewer", "Staff", "AccountingPreparer", "AccountingReviewer"];

  private static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid firmId, Guid clientId, Guid engagementId,
    IReadOnlyList<string> roles, CancellationToken ct) =>
    await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(firmId, clientId, engagementId, roles.ToArray(), InternalOnly: true), ct);

  private static async Task<FirmSafetyState?> LockFirmAsync(
    IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);

  private static async Task<ClientSafetyState?> LockClientAsync(
    IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct) =>
    await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id = {firmId} AND id = {clientId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
