using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditFieldworkService
{
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];

  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];

  private sealed record ScopedAuthorization(bool Succeeded, Guid ClientId, string? ErrorCode = null, string? Message = null);

  private static async Task<ScopedAuthorization> AuthorizeEngagementAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid engagementId, IReadOnlyList<string> roles, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null)
      return new(false, Guid.Empty, ErrorCodes.ScopeDenied, "Access denied.");
    var result = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id, roles.ToArray(), true, true), ct);
    return new(result.Succeeded, engagement.PracticeClientId, result.ErrorCode, result.Message);
  }

  private static async Task<CommandResult> AuthorizeEntityAsync<T>(
    IAuditSphereDbContext db, ActorContext actor, T? entity, IReadOnlyList<string> roles, CancellationToken ct) where T : class
  {
    if (entity is null)
      return Denied();
    var (clientId, engagementId) = entity switch
    {
      AuditSchedule x => (x.ClientId, x.EngagementId),
      AuditBankReconciliation x => (x.ClientId, x.EngagementId),
      AuditSelection x => (x.ClientId, x.EngagementId),
      AuditSelectionItem x => (x.ClientId, x.EngagementId),
      AuditItemTest x => (x.ClientId, x.EngagementId),
      AuditConfirmationCase x => (x.ClientId, x.EngagementId),
      AuditConfirmationResponse x => (x.ClientId, x.EngagementId),
      AuditAlternativeProcedure x => (x.ClientId, x.EngagementId),
      AuditAreaAssessment x => (x.ClientId, x.EngagementId),
      AuditDifference x => (x.ClientId, x.EngagementId),
      _ => (Guid.Empty, Guid.Empty)
    };
    if (clientId == Guid.Empty || engagementId == Guid.Empty)
      return Denied();
    return await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, clientId, engagementId, roles.ToArray(), true, true), ct);
  }

  private static async Task<long> CurrentGenerationAsync(IAuditSphereDbContext db, Guid clientId, Guid firmId, CancellationToken ct) =>
    Math.Max(1, await db.ClientSafetyStates.AsNoTracking().Where(x => x.Id == clientId && x.FirmId == firmId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 1);

  private static async Task<bool> IsApplicableProcedureAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid procedureId, CancellationToken ct) =>
    await db.AuditProcedures.AsNoTracking().AnyAsync(x => x.Id == procedureId && x.FirmId == firmId && x.ClientId == clientId &&
      x.EngagementId == engagementId && x.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable, ct);

  private static bool MatchesAsOfDate(DateTimeOffset? value, DateOnly expected) =>
    value.HasValue && DateOnly.FromDateTime(value.Value.UtcDateTime.Date) == expected;

  private static CommandResult<T> Invalid<T>(string message) => CommandResult<T>.Fail(ErrorCodes.AuditPlanning.Invalid, message);

  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

  private static CommandResult Denied() => CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

  private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  private static bool IsCurrency(string value) => value.Length == 3 && value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');

  private static bool IsProfitSection(string section) => section is "INCOME" or "P&L" or "PROFIT_LOSS" or "P_AND_L";

  private static bool IsEquitySection(string section) => section is "EQUITY" or "OCI" or "CHANGES_IN_EQUITY";

  private static bool IsHash(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');

  private static bool JsonObject(string value)
  {
    try
    {
      using var doc = JsonDocument.Parse(value);
      return doc.RootElement.ValueKind == JsonValueKind.Object;
    }
    catch (JsonException) { return false; }
  }
}
