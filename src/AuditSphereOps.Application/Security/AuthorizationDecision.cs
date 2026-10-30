// Authorization decision enforced inside commands (§§8.1, 8.3, 8.6, 28.3).
// Presentation layers (UI/HTTP) call commands; they never authorize by themselves.
// Every protected command resolves the actor from the trusted session, re-reads current
// access/assignment/holds under the command transaction, and denies cross-firm/client
// access with a nondisclosing scope error. Workers declare an authority mode instead.
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace AuditSphereOps.Application.Security;

public sealed record AuthorizationRequest(
  Guid FirmId,
  Guid? ClientId = null,
  Guid? EngagementId = null,
  string[]? RequiredRoles = null,
  bool InternalOnly = false,
  bool RequireProfessionalWork = false,
  bool RequireFirmWide = false);

internal sealed record EngagementProjectionAuthorization(CommandResult Read, bool CanViewDetails, bool CanOpenAction);

public static class AuthorizationDecision
{
  private sealed record GrantFact(string Role, Guid? ClientId, Guid? EngagementId, DateTimeOffset? ExpiresAt);

  internal sealed class ReadSnapshot(Func<DateTimeOffset, IReadOnlyList<CommandResult>> evaluate)
  {
    internal IReadOnlyList<CommandResult> Evaluate(DateTimeOffset at) => evaluate(at);
  }

  internal sealed class EngagementSnapshot(Func<DateTimeOffset, EngagementProjectionAuthorization> evaluate)
  {
    internal EngagementProjectionAuthorization Evaluate(DateTimeOffset at) => evaluate(at);
  }

  internal static bool HasCallerTransaction(IAuditSphereDbContext db) =>
    db.Database.CurrentTransaction is not null || System.Transactions.Transaction.Current is not null ||
    System.Transactions.TransactionsDatabaseFacadeExtensions.GetEnlistedTransaction(db.Database) is not null;

  private static CommandResult ValidateIdentity(ActorContext actor, Guid firmId, Guid? userFirmId, bool disabled, long epoch)
  {
    if (actor.UserId == Guid.Empty || actor.FirmId == Guid.Empty || firmId == Guid.Empty)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (actor.FirmId != firmId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Cross-firm access denied.");
    if (userFirmId != firmId) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (disabled) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access disabled.");
    if (epoch != actor.SessionEpoch) return CommandResult.Fail(ErrorCodes.GenerationStale, "Session is stale; sign in again.");
    return CommandResult.Ok();
  }

  private static CommandResult EvaluateGrants(AuthorizationRequest request, Guid? effectiveClientId,
    IReadOnlyList<GrantFact> grants, DateTimeOffset authorizedAt)
  {
    bool CoversScope(GrantFact g)
    {
      if (request.EngagementId.HasValue)
        return (g.ClientId is null && g.EngagementId is null)
          || (g.ClientId == effectiveClientId && g.EngagementId == request.EngagementId)
          || (g.ClientId.HasValue && g.ClientId == effectiveClientId && g.EngagementId is null);
      if (effectiveClientId.HasValue)
        return (g.ClientId is null && g.EngagementId is null)
          || (g.ClientId == effectiveClientId && g.EngagementId is null);
      return true;
    }
    var covering = grants.Where(g => (g.ExpiresAt is null || g.ExpiresAt > authorizedAt) && CoversScope(g)).ToList();
    if (covering.Count == 0) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (request.RequireFirmWide && !covering.Any(g => g.ClientId is null && g.EngagementId is null))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Firm-wide administration requires an unscoped grant.");
    if (request.RequiredRoles is { Length: > 0 })
    {
      var allowed = new HashSet<string>(request.RequiredRoles, StringComparer.OrdinalIgnoreCase);
      var candidates = request.RequireFirmWide ? covering.Where(g => g.ClientId is null && g.EngagementId is null) : covering;
      if (!candidates.Any(g => allowed.Contains(g.Role))) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }
    return CommandResult.Ok();
  }

  private static bool IsClientActor(ActorContext actor, string userKind) =>
    userKind.Equals("Client", StringComparison.OrdinalIgnoreCase) || actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase);

  // The caller owns a fresh repeatable-read transaction. Resolve every request from that one
  // snapshot and use one expiry instant, including scope requests that justify a page total.
  // This read-only internal path deliberately excludes portal and professional-work requests.
  internal static async Task<IReadOnlyList<CommandResult>> AuthorizeReadSnapshotAsync(
    IAuditSphereDbContext db, ActorContext actor, IReadOnlyList<AuthorizationRequest> requests, CancellationToken ct) =>
    (await CaptureReadSnapshotAsync(db, actor, requests, ct)).Evaluate(DateTimeOffset.UtcNow);

  internal static async Task<ReadSnapshot> CaptureReadSnapshotAsync(
    IAuditSphereDbContext db, ActorContext actor, IReadOnlyList<AuthorizationRequest> requests, CancellationToken ct)
  {
    var capturedRequests = requests.ToArray();
    var denied = CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (db.Database.CurrentTransaction is not { } transaction ||
        transaction.GetDbTransaction().IsolationLevel != IsolationLevel.RepeatableRead ||
        actor.UserId == Guid.Empty || actor.FirmId == Guid.Empty)
      return new ReadSnapshot(at => capturedRequests.Select(_ => denied).ToArray());

    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == actor.UserId, ct);
    var identity = ValidateIdentity(actor, actor.FirmId, user?.FirmId, user?.Disabled ?? false, user?.SessionEpoch ?? 0);
    if (!identity.Succeeded) return new ReadSnapshot(at => capturedRequests.Select(_ => identity).ToArray());
    var grants = await db.RoleGrants.AsNoTracking()
      .Where(g => g.UserId == actor.UserId && g.FirmId == actor.FirmId && g.RevokedAt == null)
      .Select(g => new GrantFact(g.Role, g.ClientId, g.EngagementId, g.ExpiresAt)).ToListAsync(ct);
    var clientIds = capturedRequests.Where(r => r.ClientId.HasValue).Select(r => r.ClientId!.Value).Distinct().ToArray();
    var engagementIds = capturedRequests.Where(r => r.EngagementId.HasValue).Select(r => r.EngagementId!.Value).Distinct().ToArray();
    var clients = (await db.PracticeClients.AsNoTracking().Where(c => c.FirmId == actor.FirmId && clientIds.Contains(c.Id))
      .Select(c => c.Id).ToListAsync(ct)).ToHashSet();
    var engagements = await db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId && engagementIds.Contains(e.Id))
      .ToDictionaryAsync(e => e.Id, e => e.PracticeClientId, ct);
    return new ReadSnapshot(now => capturedRequests.Select(request =>
    {
      if (request.FirmId != actor.FirmId || !request.InternalOnly || request.RequireProfessionalWork ||
          request.ClientId is { } clientId && !clients.Contains(clientId)) return denied;
      var effectiveClient = request.ClientId;
      if (request.EngagementId is { } engagementId)
      {
        if (!engagements.TryGetValue(engagementId, out var storedClient) ||
            request.ClientId.HasValue && request.ClientId != storedClient) return denied;
        effectiveClient ??= storedClient;
      }
      var result = EvaluateGrants(request, effectiveClient, grants, now);
      return result.Succeeded && IsClientActor(actor, user!.UserKind) ? denied : result;
    }).ToArray());
  }

  // One statement supplies a common authorization snapshot for this read-only projection only.
  // A caller-owned repeatable-read transaction supplies its existing snapshot, not a fresh one.
  // The general scalar path below retains its existing professional-work and portal side effects.
  internal static async Task<EngagementProjectionAuthorization> AuthorizeEngagementProjectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId,
    AuthorizationRequest read, AuthorizationRequest detail, AuthorizationRequest? action, string? staffingLevel,
    CancellationToken ct) => (await CaptureEngagementProjectionAsync(db, actor, clientId, engagementId,
      read, detail, action, staffingLevel, ct)).Evaluate(DateTimeOffset.UtcNow);

  internal static async Task<EngagementSnapshot> CaptureEngagementProjectionAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId,
    AuthorizationRequest read, AuthorizationRequest detail, AuthorizationRequest? action, string? staffingLevel,
    CancellationToken ct)
  {
    var denied = CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    bool ValidRequest(AuthorizationRequest request) => request.FirmId == actor.FirmId && request.EngagementId == engagementId &&
      (!request.ClientId.HasValue || request.ClientId == clientId) && request.InternalOnly && !request.RequireProfessionalWork;
    if (actor.UserId == Guid.Empty || actor.FirmId == Guid.Empty || clientId == Guid.Empty || engagementId == Guid.Empty ||
        !ValidRequest(read) || !ValidRequest(detail) || action is not null && !ValidRequest(action))
      return new EngagementSnapshot(_ => new(denied, false, false));

    var rows = await (from user in db.Users.AsNoTracking().Where(u => u.Id == actor.UserId)
      from grant in db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId && g.UserId == user.Id && g.RevokedAt == null).DefaultIfEmpty()
      select new
      {
        user.FirmId, user.Disabled, user.SessionEpoch, user.UserKind,
        GrantId = (Guid?)grant.Id, grant.Role, grant.ClientId, grant.EngagementId, grant.ExpiresAt,
        ExactScope = db.Engagements.Any(e => e.FirmId == actor.FirmId && e.Id == engagementId && e.PracticeClientId == clientId) &&
          db.PracticeClients.Any(c => c.FirmId == actor.FirmId && c.Id == clientId),
        Staffed = staffingLevel != null && db.EngagementStaffAssignments.Any(s => s.FirmId == actor.FirmId &&
          s.ClientId == clientId && s.EngagementId == engagementId && s.UserId == actor.UserId &&
          s.StaffingLevel == staffingLevel && s.RevokedAt == null)
      }).TagWith("U12 engagement projection final authorization").ToListAsync(ct);

    if (rows.Count == 0) return new EngagementSnapshot(_ => new(denied, false, false));
    var first = rows[0];
    var identity = ValidateIdentity(actor, actor.FirmId, first.FirmId, first.Disabled, first.SessionEpoch);
    if (!identity.Succeeded) return new EngagementSnapshot(_ => new(identity, false, false));
    if (!first.ExactScope) return new EngagementSnapshot(_ => new(denied, false, false));
    var grants = rows.Where(x => x.GrantId.HasValue)
      .Select(x => new GrantFact(x.Role, x.ClientId, x.EngagementId, x.ExpiresAt)).ToList();
    return new EngagementSnapshot(now =>
    {
      var readDecision = EvaluateGrants(read, clientId, grants, now);
      if (!readDecision.Succeeded) return new(readDecision, false, false);
      if (IsClientActor(actor, first.UserKind)) return new(denied, false, false);
      var canViewDetails = EvaluateGrants(detail, clientId, grants, now).Succeeded;
      var canOpenAction = action is not null && EvaluateGrants(action, clientId, grants, now).Succeeded &&
        (staffingLevel is null || first.Staffed);
      return new(readDecision, canViewDetails, canOpenAction);
    });
  }

  public static async Task<CommandResult> AuthorizeGroupAsync(
    IClientAccountingDbContext db,
    ActorContext actor,
    Guid groupId,
    IReadOnlyCollection<string> requiredRoles,
    CancellationToken ct = default)
  {
    if (actor.UserId == Guid.Empty || actor.FirmId == Guid.Empty || groupId == Guid.Empty)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var user = await db.Users.AsNoTracking()
      .SingleOrDefaultAsync(u => u.Id == actor.UserId && u.FirmId == actor.FirmId, ct);
    if (user is null || user.Disabled || user.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase) ||
        actor.Roles.Contains("ClientUser", StringComparer.OrdinalIgnoreCase))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (user.SessionEpoch != actor.SessionEpoch)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var groupExists = await db.ClientGroups.AsNoTracking()
      .AnyAsync(x => x.Id == groupId && x.FirmId == actor.FirmId, ct);
    if (!groupExists)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var allowed = await db.GroupAccessGrants.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.GroupId == groupId && x.UserId == actor.UserId && x.RevokedAt == null &&
      requiredRoles.Contains(x.Role), ct);
    return allowed ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Explicit group access is required.");
  }

  public static async Task<CommandResult> AuthorizeAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    AuthorizationRequest request,
    CancellationToken ct = default)
  {
    if (actor.UserId == Guid.Empty || actor.FirmId == Guid.Empty || request.FirmId == Guid.Empty)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (actor.FirmId != request.FirmId)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Cross-firm access denied.");

    var user = await db.Users.AsNoTracking()
      .SingleOrDefaultAsync(u => u.Id == actor.UserId, ct);
    var identity = ValidateIdentity(actor, request.FirmId, user?.FirmId, user?.Disabled ?? false, user?.SessionEpoch ?? 0);
    if (!identity.Succeeded) return identity;

    // Scope linkage is resolved from stored records, never from a browser-supplied name/filter.
    Guid? effectiveClientId = request.ClientId;
    if (request.ClientId.HasValue)
    {
      var clientOk = await db.PracticeClients.AsNoTracking()
        .AnyAsync(c => c.Id == request.ClientId.Value && c.FirmId == request.FirmId, ct);
      if (!clientOk)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    }
    if (request.EngagementId.HasValue)
    {
      var engagement = await db.Engagements.AsNoTracking()
        .SingleOrDefaultAsync(e => e.Id == request.EngagementId.Value && e.FirmId == request.FirmId, ct);
      if (engagement is null)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (request.ClientId.HasValue && engagement.PracticeClientId != request.ClientId.Value)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      effectiveClientId ??= engagement.PracticeClientId;
    }

    // Assignment: at least one active grant must cover the requested scope.
    var grants = await db.RoleGrants.AsNoTracking()
      .Where(g => g.UserId == actor.UserId && g.FirmId == request.FirmId && g.RevokedAt == null)
      .Select(g => new GrantFact(g.Role, g.ClientId, g.EngagementId, g.ExpiresAt))
      .ToListAsync(ct);
    var grantDecision = EvaluateGrants(request, effectiveClientId, grants, DateTimeOffset.UtcNow);
    if (!grantDecision.Succeeded) return grantDecision;

    // Client users never reach internal-only targets through any command path.
    if (request.InternalOnly &&
        IsClientActor(actor, user!.UserKind))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    if (!request.InternalOnly && effectiveClientId is { } portalClient &&
        IsClientActor(actor, user!.UserKind) &&
        !await AuditSphereOps.Application.Documents.ClientPortalService.CommercialOnboardingClearedAsync(db, request.FirmId, portalClient, request.EngagementId, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The client portal opens after current commercial acceptance, Partner risk clearance, activation and advance payment.");

    if (request.RequireProfessionalWork)
    {
      if (!request.EngagementId.HasValue)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Professional work requires an engagement scope.");
      var engagement = await db.Engagements.AsNoTracking()
        .SingleOrDefaultAsync(e => e.Id == request.EngagementId.Value && e.FirmId == request.FirmId, ct);
      if (engagement is null)
        return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
      if (engagement.ProfessionalWorkBlocked)
      {
        // A file frozen after the report reports its frozen state (and records the refused access)
        // ahead of the generic professional-work block, so the refusal is consistent and evidenced
        // across every professional command boundary (STE-REM-09).
        var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x =>
          x.FirmId == request.FirmId && x.EngagementId == engagement.Id && x.State == AuditSphereOps.Domain.Records.FileFreezeStates.Frozen, ct);
        if (frozen)
        {
          db.FrozenAccessAttempts.Add(new AuditSphereOps.Domain.Records.FrozenAccessAttempt
          {
            Id = Guid.CreateVersion7(), FirmId = request.FirmId, EngagementId = engagement.Id,
            ActorUserId = actor.UserId, Action = "professional work on a frozen engagement file", AttemptedAt = DateTimeOffset.UtcNow
          });
          await db.SaveChangesAsync(ct);
          return CommandResult.Fail(ErrorCodes.ProtectedState, "The engagement file is frozen after the report; an approved amendment is required to change it.");
        }
        return CommandResult.Fail(ErrorCodes.GateBlocked, "Professional work is blocked on this engagement.");
      }
      var hold = await db.EngagementHolds.AsNoTracking()
        .AnyAsync(h => h.FirmId == request.FirmId && h.EngagementId == request.EngagementId.Value && !h.Released, ct);
      if (hold)
        return CommandResult.Fail(ErrorCodes.GateBlocked, "An unreleased hold blocks professional work.");
    }

    return CommandResult.Ok();
  }
}
