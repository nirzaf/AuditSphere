using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalRequestItem(Guid Id, string Area, string Objective, string PeriodStart, string PeriodEnd, string DueDate, string State, bool Delegated);
public sealed record PortalPackageItem(Guid Id, string Framework, string PeriodStart, string PeriodEnd, string Currency);
public sealed record PortalWorkspace(FirstSignInStatus FirstSignIn, bool PendingOnboarding, int EngagementCount,
  IReadOnlyList<PortalRequestItem> Requests, bool HasMoreRequests, IReadOnlyList<PortalPackageItem> Packages);
public sealed record PortalUploadItem(Guid Id, string FileName, string ReceivedByteCount, string DeclaredByteCount, string State,
  string? TransferState, string Sha256, string CreatedAt);
public sealed record PortalConversationItem(Guid Id, string At, string Speaker, string Body);
public sealed record PortalRequestWorkspace(Guid Id, string Area, string Objective, string Instructions, string RequestedFormat,
  string DueDate, string State, string Revision, FirstSignInStatus FirstSignIn, bool CanWrite, bool CanDelegate,
  IReadOnlyList<PbcDelegationRow> Delegations, IReadOnlyList<PbcDelegateCandidate> Candidates,
  IReadOnlyList<PortalUploadItem> Uploads, IReadOnlyList<PortalConversationItem> Conversation,
  string? ClarificationReason = null);

/// <summary>Client-only DTOs. Participation is additional to current role, scope, commercial and session authorization.</summary>
public static class ClientPortalWorkspaceQuery
{
  public static Task<CommandResult<PortalWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, int page = 0, CancellationToken ct = default) =>
    GetAsync(db, actor, page, 50, ct);

  public static async Task<CommandResult<PortalWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, int page,
    int pageSize, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is not (10 or 25 or 50) || !await IsClientAsync(db, actor, ct))
      return CommandResult<PortalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Portal unavailable.");
    var engagements = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    var requests = await ClientPortalService.ParticipantRequests(db, actor).Where(x => engagements.Contains(x.EngagementId))
      .OrderBy(x => x.DueDate).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize + 1)
      .Select(x => new PortalRequestItem(x.Id, x.Area, x.Objective, x.PeriodStart, x.PeriodEnd, x.DueDate, x.State, x.ClientOwnerUserId != actor.UserId)).ToListAsync(ct);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId && engagements.Contains(x.EngagementId) && x.Status == AccountingPackageStates.PackageValidated)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(100)
      .Select(x => new PortalPackageItem(x.Id, x.Framework, x.PeriodStart, x.PeriodEnd, x.Currency)).ToListAsync(ct);
    // Re-evaluate participation and grants before releasing the snapshot; cookie epoch is also checked by the façade.
    var currentEngagements = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    var requestIds = requests.Select(x => x.Id).ToArray();
    var stillAssigned = await ClientPortalService.ParticipantRequests(db, actor).Where(x => requestIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
    if (!await IsClientAsync(db, actor, ct) || !engagements.Order().SequenceEqual(currentEngagements.Order()) || !requestIds.Order().SequenceEqual(stillAssigned.Order()))
      return CommandResult<PortalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Portal unavailable.");
    return CommandResult<PortalWorkspace>.Ok(new(await ClientPortalService.GetFirstSignInStatusAsync(db, actor, ct),
      await ClientPortalService.HasPendingCommercialOnboardingAsync(db, actor, ct), engagements.Count,
      requests.Take(pageSize).ToArray(), requests.Count > pageSize, packages));
  }

  public static async Task<CommandResult<PortalRequestWorkspace>> RequestAsync(IAuditSphereDbContext db, ActorContext actor, Guid requestId, CancellationToken ct = default)
  {
    if (!await IsClientAsync(db, actor, ct)) return Denied();
    var request = await ClientPortalService.ParticipantRequests(db, actor).SingleOrDefaultAsync(x => x.Id == requestId, ct);
    if (request is null || !await AuthorizedAsync(db, actor, request, ct)) return Denied();
    var uploads = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PbcRequestId == requestId)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(501).ToListAsync(ct);
    var messages = await db.PbcCommunications.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PbcRequestId == requestId)
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(2001).ToListAsync(ct);
    if (uploads.Count > 500 || messages.Count > 2000)
      return CommandResult<PortalRequestWorkspace>.Fail(ErrorCodes.GateBlocked, "This request requires a paged archive review. Contact your audit team.");
    var operations = uploads.Where(x => x.TransferOperationId.HasValue).Select(x => x.TransferOperationId!.Value).ToArray();
    var states = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && operations.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Status.ToString(), ct);
    var firstSignIn = await ClientPortalService.GetFirstSignInStatusAsync(db, actor, ct);
    var window = await ClientPortalService.RequireUploadWindowAsync(db, actor, request.EngagementId, ct);
    var canDelegate = request.ClientOwnerUserId == actor.UserId && await ClientPortalService.IsPrimaryContactAsync(db, actor, request.ClientId, ct);
    var delegations = canDelegate ? await ClientPortalService.DelegationsAsync(db, actor, request, ct) : [];
    var candidates = canDelegate ? await ClientPortalService.DelegateCandidatesAsync(db, actor, request, ct) : [];
    if (!await AuthorizedAsync(db, actor, request, ct)) return Denied();
    return CommandResult<PortalRequestWorkspace>.Ok(new(request.Id, request.Area, request.Objective, request.AcceptanceCriteria, request.RequestedFormat,
      request.DueDate, request.State, request.Revision.ToString(CultureInfo.InvariantCulture), firstSignIn,
      window.Succeeded && request.State is not (PbcStates.Accepted or PbcStates.Closed), canDelegate,
      delegations, candidates.Where(x => delegations.All(d => d.DelegateUserId != x.UserId)).Take(500).ToArray(),
      uploads.Select(x => new PortalUploadItem(x.Id, x.FileName, x.ReceivedByteCount.ToString(CultureInfo.InvariantCulture), x.DeclaredByteCount.ToString(CultureInfo.InvariantCulture),
        x.State, x.TransferOperationId is { } op && states.TryGetValue(op, out var state) ? state : null, x.DeclaredSha256Hex, x.CreatedAt.ToUniversalTime().ToString("O"))).ToArray(),
      messages.Select(x => new PortalConversationItem(x.Id, x.CreatedAt.ToUniversalTime().ToString("O"),
        x.Kind == PbcCommunicationKinds.ClientMessage ? "Client" : x.Kind == PbcCommunicationKinds.Email ? "Email notification" : "Audit team", x.Body)).ToArray(),
      request.ClarificationReason));
  }
  private static Task<bool> IsClientAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct);
  private static async Task<bool> AuthorizedAsync(IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, request.ClientId, request.EngagementId, ["ClientUser"]), ct)).Succeeded &&
    await ClientPortalService.IsRequestParticipantAsync(db, actor, request, ct);
  private static CommandResult<PortalRequestWorkspace> Denied() => CommandResult<PortalRequestWorkspace>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
}
