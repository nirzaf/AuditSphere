using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PbcPerson(Guid Id, string Name, string? Email);
public sealed record PbcIntentRow(Guid Id, string FileName, long RequestRevision, long Revision, long ReceivedByteCount,
  long DeclaredByteCount, string State, string? TransferState, string DeclaredSha256Hex, string? ContentSha256Hex,
  string? ProviderIdentity, string? ProviderReceiptDigest, DateTimeOffset? ProviderRegisteredAt);
public sealed record PbcAcceptanceRow(Guid UploadIntentId, string FileName, long SourceRequestRevision, long UploadIntentRevision,
  long ByteCount, string ContentSha256Hex, string ProviderIdentity, string ProviderReceiptDigest,
  Guid ReviewerUserId, DateTimeOffset AcceptedAt, string SourceDigest);
public sealed record PbcTimelineItem(DateTimeOffset At, string Label, string Body);
public sealed record PbcRequestRow(Guid Id, string Area, string Objective, string DueDate, string State, long Revision, IReadOnlyList<PbcIntentRow> Intents,
  IReadOnlyList<PbcTimelineItem> Timeline, bool CanRequestMore, bool CanStartReview, bool CanAccept, PbcAcceptanceRow? Acceptance);
public sealed record PbcInboxWorkspace(Guid EngagementId, string ServiceRoute, string PeriodStart, string PeriodEnd, IReadOnlyList<PbcRequestRow> Requests,
  IReadOnlyList<PbcPerson> ClientOwners, IReadOnlyList<PbcPerson> Reviewers);

/// <summary>
/// Staff PBC inbox for one engagement: request metadata, upload receipts, durable transfer state and the conversation
/// timeline. An unassigned engagement is indistinguishable from a missing one. No file content is returned.
/// </summary>
public static class PbcInboxWorkspaceQuery
{
  private static readonly string[] StaffRoles = ["Administrator", "Partner", "Manager", "Reviewer", "Senior", "Staff", "Auditor", "Accountant", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<PbcInboxWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<PbcInboxWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
      RequiredRoles: StaffRoles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<PbcInboxWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var reviewerAuth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId,
      engagement.PracticeClientId, engagement.Id, PbcService.ReviewerRoles, InternalOnly: true), ct);
    var canActAsReviewer = reviewerAuth.Succeeded;
    var clientId = engagement.PracticeClientId;
    var clientIds = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId && g.Role == "ClientUser" && g.ClientId == clientId &&
      g.EngagementId == engagementId && g.RevokedAt == null).Select(g => g.UserId).Distinct().ToArrayAsync(ct);
    var reviewerIds = await db.RoleGrants.AsNoTracking().Where(g => g.FirmId == actor.FirmId &&
      PbcService.ReviewerRoles.Contains(g.Role) &&
      ((g.ClientId == null && g.EngagementId == null) ||
       (g.ClientId == clientId && g.EngagementId == engagementId) ||
       (g.ClientId == clientId && g.EngagementId == null)) &&
      g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > DateTimeOffset.UtcNow))
      .Select(g => g.UserId).Distinct().ToArrayAsync(ct);
    var clientOwners = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id) && !x.Disabled).OrderBy(x => x.DisplayName)
      .Select(x => new PbcPerson(x.Id, x.DisplayName, x.Email)).ToListAsync(ct);
    var reviewers = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && reviewerIds.Contains(x.Id) &&
      x.Id != actor.UserId && !x.Disabled).OrderBy(x => x.DisplayName)
      .Select(x => new PbcPerson(x.Id, x.DisplayName, null)).ToListAsync(ct);

    var requests = await db.PbcRequests.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId)
      .OrderBy(x => x.DueDate).ThenBy(x => x.Area).ToListAsync(ct);
    var requestIds = requests.Select(x => x.Id).ToArray();
    var acceptanceRows = await db.PbcAcceptanceReceipts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && requestIds.Contains(x.PbcRequestId))
      .ToDictionaryAsync(x => x.PbcRequestId, x => new PbcAcceptanceRow(x.PbcUploadIntentId, x.FileName,
        x.SourceRequestRevision,
        x.UploadIntentRevision, x.ByteCount, x.ContentSha256Hex, x.ProviderIdentity,
        x.ProviderReceiptDigest, x.ReviewerUserId, x.AcceptedAt, x.SourceDigest), ct);
    var intents = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && requestIds.Contains(x.PbcRequestId)).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
    var operationIds = intents.Where(x => x.TransferOperationId.HasValue).Select(x => x.TransferOperationId!.Value).Distinct().ToArray();
    var operations = await db.DurableOperations.AsNoTracking().Where(o => o.FirmId == actor.FirmId && operationIds.Contains(o.Id))
      .Select(o => new { o.Id, State = o.Status.ToString(), o.ResultIdentity, o.ResultDigest }).ToDictionaryAsync(o => o.Id, ct);
    var communications = await db.PbcCommunications.AsNoTracking().Where(x => x.FirmId == actor.FirmId && requestIds.Contains(x.PbcRequestId)).OrderBy(x => x.CreatedAt).ToListAsync(ct);
    var emailIds = communications.Where(x => x.Kind == PbcCommunicationKinds.Email).Select(x => x.Id).ToArray();
    var emailOperationStates = await db.DurableOperations.AsNoTracking().Where(o =>
        o.FirmId == actor.FirmId && emailIds.Contains(o.TargetId) && o.OperationKind == PbcMailDeliveryHandler.Kind)
      .ToDictionaryAsync(o => o.TargetId, o => o.Status.ToString(), ct);
    var userIds = communications.Select(x => x.AuthorUserId).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && userIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email : x.DisplayName, ct);
    string Name(Guid id) => names.GetValueOrDefault(id, "User");

    var rows = requests.Select(r =>
    {
      var mine = intents.Where(i => i.PbcRequestId == r.Id).ToList();
      var timeline = communications.Where(c => c.PbcRequestId == r.Id).Select(c => new PbcTimelineItem(c.CreatedAt,
          c.Kind == PbcCommunicationKinds.Email ? EmailLabel(c, emailOperationStates) :
          c.Kind is PbcCommunicationKinds.ClientMessage or PbcCommunicationKinds.ClientAction
            ? $"Client — {Name(c.AuthorUserId)}" : $"Staff — {Name(c.AuthorUserId)}", c.Body))
        .Concat(mine.Select(i => new PbcTimelineItem(i.CreatedAt, $"Client uploaded {i.FileName}", $"{i.ReceivedByteCount} of {i.DeclaredByteCount} bytes — {i.State}")))
        .OrderBy(x => x.At).ToList();
      return new PbcRequestRow(r.Id, r.Area, r.Objective, r.DueDate, r.State, r.Revision,
        mine.Select(i =>
        {
          var transfer = i.TransferOperationId is { } op && operations.TryGetValue(op, out var found) ? found : null;
          var verified = i.State == PbcUploadStates.Received && transfer?.State == "COMPLETED";
          return new PbcIntentRow(i.Id, i.FileName, i.RequestRevision, i.Revision, i.ReceivedByteCount,
            i.DeclaredByteCount, i.State, transfer?.State, i.DeclaredSha256Hex,
            verified ? i.FinalSha256Hex : null, verified ? transfer!.ResultIdentity : null,
            verified ? i.ProviderReceiptDigest : null, verified ? i.ProviderRegisteredAt : null);
        }).ToList(),
        timeline, r.State is not (PbcStates.Draft or PbcStates.Accepted or PbcStates.Closed),
        canActAsReviewer && actor.UserId == r.ReviewerUserId && r.State == PbcStates.Received,
        canActAsReviewer && actor.UserId == r.ReviewerUserId && r.State == PbcStates.UnderReview,
        acceptanceRows.GetValueOrDefault(r.Id));
    }).ToList();
    return CommandResult<PbcInboxWorkspace>.Ok(new(engagementId, engagement.ServiceRoute, engagement.PeriodStart, engagement.PeriodEnd, rows, clientOwners, reviewers));
  }

  private static string EmailLabel(PbcCommunication communication, IReadOnlyDictionary<Guid, string> operationStates)
  {
    if (operationStates.TryGetValue(communication.Id, out var operationState))
    {
      var delivery = operationState switch
      {
        "COMPLETED" => "sent",
        "AUTHORIZATION_BLOCKED" or "PROVIDER_BLOCKED" or "CANCELLED_WITH_DISPOSITION" => "blocked before delivery",
        "RESULT_UNCERTAIN" => "delivery uncertain",
        "DEAD_LETTER" => "failed",
        _ => communication.DeliveryState?.ToLowerInvariant() ?? "pending"
      };
      return $"Email {delivery} to client";
    }
    return $"Email {communication.DeliveryState?.ToLowerInvariant() ?? "pending"} to client";
  }

  /// <summary>Opens a request with the engagement's scope and queues the minimal client email in one user action.</summary>
  public static async Task<CommandResult<Guid>> CreateAndSendAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, string description,
    Guid clientOwnerId, Guid reviewerId, string dueDate, string? requestedFormat, string publicBaseUrl,
    CancellationToken ct = default, bool createWorkflowTask = false)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var created = await PbcService.CreateRequestAsync(db, actor, new CreatePbcRequestRequest(engagementId, description, engagement.ServiceRoute, engagement.PeriodStart,
      engagement.PeriodEnd, "Client documents", string.IsNullOrWhiteSpace(requestedFormat) ? "Any safe readable format" : requestedFormat, string.Empty,
      clientOwnerId, actor.UserId, reviewerId, dueDate, "Confidential", "Readable, complete, and relevant to the request description."), ct);
    if (!created.Succeeded) return created;
    var sent = await PbcService.SendRequestAsync(db, actor, created.Value, 1, publicBaseUrl, ct, createWorkflowTask);
    return sent.Succeeded ? created : CommandResult<Guid>.Fail(sent.ErrorCode!, sent.Message!);
  }
}
