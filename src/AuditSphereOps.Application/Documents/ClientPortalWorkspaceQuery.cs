using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

/// <summary>CanUpload is request-level eligibility; each upload still validates its specific file and active intent.</summary>
public sealed record PortalRequestItem(Guid Id, string Area, string Objective, string PeriodStart, string PeriodEnd, string DueDate, string State, bool Delegated,
  string? ClarificationReason = null, bool ClarificationReasonTruncated = false, bool CanWrite = false, bool CanUpload = false, bool CanReply = false);
public sealed record PortalPackageItem(Guid Id, string Framework, string PeriodStart, string PeriodEnd, string Currency);
public sealed record PortalWorkspace(FirstSignInStatus FirstSignIn, bool PendingOnboarding, int EngagementCount,
  IReadOnlyList<PortalRequestItem> Requests, bool HasMoreRequests, IReadOnlyList<PortalPackageItem> Packages,
  DateOnly? AsOfDateUtc = null, bool HasMorePackages = false, int PackagePage = 0, int PackagePageSize = 10);
public sealed record PortalUploadItem(Guid Id, string FileName, string ReceivedByteCount, string DeclaredByteCount, string State,
  string? TransferState, string Sha256, string CreatedAt);
public sealed record PortalConversationItem(Guid Id, string At, string Speaker, string Body);
public sealed record PortalPbcAcceptance(Guid Id, Guid UploadIntentId, string AcceptedAt, string AcceptedRequestRevision,
  string SourceRequestRevision, string UploadIntentRevision, string FileName, string ByteCount, string Sha256);
public sealed record PortalRequestWorkspace(Guid Id, string Area, string Objective, string Instructions, string RequestedFormat,
  string DueDate, string State, string Revision, FirstSignInStatus FirstSignIn, bool CanWrite, bool CanDelegate,
  IReadOnlyList<PbcDelegationRow> Delegations, IReadOnlyList<PbcDelegateCandidate> Candidates,
  IReadOnlyList<PortalUploadItem> Uploads, IReadOnlyList<PortalConversationItem> Conversation,
  string? ClarificationReason = null, bool HasMoreUploads = false, bool HasMoreConversation = false, bool CanManageDelegations = false,
  PortalPbcAcceptance? Acceptance = null, bool CanUpload = false, bool CanReply = false);
public sealed record PortalRequestHistory(Guid RequestId, int UploadPage, int ConversationPage, int PageSize,
  IReadOnlyList<PortalUploadItem> Uploads, bool HasMoreUploads,
  IReadOnlyList<PortalConversationItem> Conversation, bool HasMoreConversation);

/// <summary>Client-only DTOs. Participation is additional to current role, scope, commercial and session authorization.</summary>
public static class ClientPortalWorkspaceQuery
{
  public static Task<CommandResult<PortalWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, int page = 0, CancellationToken ct = default) =>
    GetAsync(db, actor, page, 50, 0, 10, ct);

  public static Task<CommandResult<PortalWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, int page,
    int pageSize, CancellationToken ct = default) => GetAsync(db, actor, page, pageSize, 0, 10, ct);

  public static async Task<CommandResult<PortalWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor, int page,
    int pageSize, int packagePage, int packagePageSize, CancellationToken ct = default)
  {
    if (page is < 0 or > 10000 || pageSize is not (10 or 25 or 50) || packagePage is < 0 or > 10000 ||
        packagePageSize is not (10 or 25 or 50) || !await IsClientAsync(db, actor, ct))
      return CommandResult<PortalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Portal unavailable.");
    var engagements = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    var requests = await ClientPortalService.ParticipantRequests(db, actor).Where(x => engagements.Contains(x.EngagementId))
      .OrderBy(x => x.DueDate).ThenBy(x => x.Id).Skip(page * pageSize).Take(pageSize + 1)
      .Select(x => new
      {
        Item = new PortalRequestItem(x.Id, x.Area, x.Objective, x.PeriodStart, x.PeriodEnd, x.DueDate, x.State, x.ClientOwnerUserId != actor.UserId),
        x.EngagementId,
        x.ClarificationReason,
        x.Revision
      }).ToListAsync(ct);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId && engagements.Contains(x.EngagementId) && x.Status == AccountingPackageStates.PackageValidated)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(packagePage * packagePageSize).Take(packagePageSize + 1)
      .Select(x => new PortalPackageItem(x.Id, x.Framework, x.PeriodStart, x.PeriodEnd, x.Currency)).ToListAsync(ct);
    var requestPage = requests.Take(pageSize).ToArray();
    var firstSignIn = await ClientPortalService.GetFirstSignInStatusAsync(db, actor, ct);
    var uploadWindows = new Dictionary<Guid, bool>();
    foreach (var engagementId in requestPage.Select(x => x.EngagementId).Distinct())
      uploadWindows[engagementId] = (await ClientPortalService.RequireUploadWindowAsync(db, actor, engagementId, ct)).Succeeded;
    var requestItems = requestPage.Select(row =>
    {
      var clarification = row.Item.State == PbcStates.ClarificationRequired && !string.IsNullOrWhiteSpace(row.ClarificationReason)
        ? row.ClarificationReason
        : null;
      var truncated = clarification is { Length: > 2000 };
      if (truncated) clarification = clarification![..2000];
      var canWrite = uploadWindows.GetValueOrDefault(row.EngagementId) &&
        row.Item.State is (PbcStates.Sent or PbcStates.Acknowledged or PbcStates.PartiallyReceived or PbcStates.ClarificationRequired);
      var canUpload = uploadWindows.GetValueOrDefault(row.EngagementId) && CanUploadInState(row.Item.State);
      var canReply = uploadWindows.GetValueOrDefault(row.EngagementId) && CanReplyInState(row.Item.State);
      return row.Item with
      {
        ClarificationReason = clarification,
        ClarificationReasonTruncated = truncated,
        CanWrite = canWrite,
        CanUpload = canUpload,
        CanReply = canReply
      };
    }).ToArray();
    var packageItems = packages.Take(packagePageSize).ToArray();
    var asOfDateUtc = DateOnly.FromDateTime(DateTime.UtcNow);
    var requestIds = requests.Select(x => x.Item.Id).ToArray();
    var writableRequests = requestPage.Where((_, index) =>
      requestItems[index].CanWrite || requestItems[index].CanUpload || requestItems[index].CanReply).ToArray();
    if (writableRequests.Length > 0)
    {
      var currentUploadWindows = new Dictionary<Guid, bool>();
      foreach (var engagementId in writableRequests.Select(x => x.EngagementId).Distinct())
        currentUploadWindows[engagementId] = (await ClientPortalService.RequireUploadWindowAsync(db, actor, engagementId, ct)).Succeeded;
      requestItems = requestItems.Select((item, index) => item with
      {
        CanWrite = item.CanWrite && currentUploadWindows.GetValueOrDefault(requestPage[index].EngagementId),
        CanUpload = item.CanUpload && currentUploadWindows.GetValueOrDefault(requestPage[index].EngagementId),
        CanReply = item.CanReply && currentUploadWindows.GetValueOrDefault(requestPage[index].EngagementId)
      }).ToArray();
    }
    var pendingOnboarding = await ClientPortalService.HasPendingCommercialOnboardingAsync(db, actor, ct);
    // Re-evaluate participation and grants after the prerequisite reads and before releasing the snapshot; cookie epoch
    // is also checked by the façade. The upload route still makes the authoritative decision for every write.
    var currentEngagements = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    var stillAssigned = await ClientPortalService.ParticipantRequests(db, actor).Where(x => requestIds.Contains(x.Id))
      .Select(x => new { x.Id, x.State, x.Revision }).ToListAsync(ct);
    if (!await IsClientAsync(db, actor, ct) || !engagements.Order().SequenceEqual(currentEngagements.Order()) ||
        !requestIds.Order().SequenceEqual(stillAssigned.Select(x => x.Id).Order()))
      return CommandResult<PortalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Portal unavailable.");
    var currentRequests = stillAssigned.ToDictionary(x => x.Id);
    if (requests.Any(row => !currentRequests.TryGetValue(row.Item.Id, out var current) ||
        row.Item.State != current.State || row.Revision != current.Revision))
      return CommandResult<PortalWorkspace>.Fail(ErrorCodes.GenerationStale, "A request changed while the portal was loading. Reload the current version.");
    return CommandResult<PortalWorkspace>.Ok(new(firstSignIn,
      pendingOnboarding, engagements.Count,
      requestItems, requests.Count > pageSize, packageItems, asOfDateUtc, packages.Count > packagePageSize, packagePage, packagePageSize));
  }

  public static async Task<CommandResult<PortalRequestWorkspace>> RequestAsync(IAuditSphereDbContext db, ActorContext actor, Guid requestId, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortalRequestWorkspace>.Fail(ErrorCodes.GateBlocked, "Request detail requires an independent read snapshot.");

    PbcRequest? request;
    PortalRequestWorkspace workspace;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      if (!await IsClientAsync(db, actor, ct)) return Denied();
      request = await ClientPortalService.ParticipantRequests(db, actor).SingleOrDefaultAsync(x => x.Id == requestId, ct);
      if (request is null || !await AuthorizedAsync(db, actor, request, ct)) return Denied();
      var acceptanceReceipt = await db.PbcAcceptanceReceipts.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId &&
          x.EngagementId == request.EngagementId && x.PbcRequestId == request.Id)
        .Select(x => new
        {
          x.Id, UploadIntentId = x.PbcUploadIntentId, x.AcceptedAt, x.AcceptedRequestRevision,
          x.SourceRequestRevision, x.UploadIntentRevision, x.FileName, x.ByteCount, Sha256 = x.ContentSha256Hex
        })
        .SingleOrDefaultAsync(ct);
      var acceptance = acceptanceReceipt is null ? null : new PortalPbcAcceptance(
        acceptanceReceipt.Id, acceptanceReceipt.UploadIntentId,
        acceptanceReceipt.AcceptedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        acceptanceReceipt.AcceptedRequestRevision.ToString(CultureInfo.InvariantCulture),
        acceptanceReceipt.SourceRequestRevision.ToString(CultureInfo.InvariantCulture),
        acceptanceReceipt.UploadIntentRevision.ToString(CultureInfo.InvariantCulture), acceptanceReceipt.FileName,
        acceptanceReceipt.ByteCount.ToString(CultureInfo.InvariantCulture), acceptanceReceipt.Sha256);
      var history = await ReadRequestHistoryAsync(db, actor, request, 0, 0, 25, ct);
      if (!history.Succeeded || history.Value is null) return Denied();
      var uploads = history.Value.Uploads;
      var messages = history.Value.Conversation;
      var firstSignIn = await ClientPortalService.GetFirstSignInStatusAsync(db, actor, ct);
      var window = await ClientPortalService.RequireUploadWindowAsync(db, actor, request.EngagementId, ct);
      var canUpload = window.Succeeded && CanUploadInState(request.State);
      var canReply = window.Succeeded && CanReplyInState(request.State);
      var canManageDelegations = request.ClientOwnerUserId == actor.UserId &&
        await ClientPortalService.IsPrimaryContactAsync(db, actor, request.ClientId, ct);
      var canDelegate = canManageDelegations && request.State is not (PbcStates.Accepted or PbcStates.Closed);
      var delegations = canManageDelegations ? await ClientPortalService.DelegationsAsync(db, actor, request, ct) : [];
      var candidates = canDelegate ? await ClientPortalService.DelegateCandidatesAsync(db, actor, request, ct) : [];
      workspace = new(request.Id, request.Area, request.Objective, request.AcceptanceCriteria, request.RequestedFormat,
        request.DueDate, request.State, request.Revision.ToString(CultureInfo.InvariantCulture), firstSignIn,
        window.Succeeded && request.State is not (PbcStates.Accepted or PbcStates.Closed), canDelegate,
        delegations, candidates.Where(x => delegations.All(d => d.DelegateUserId != x.UserId)).Take(500).ToArray(),
        uploads, messages, request.ClarificationReason, history.Value.HasMoreUploads, history.Value.HasMoreConversation,
        canManageDelegations, acceptance, canUpload, canReply);
      await snapshot.CommitAsync(ct);
    }

    // Re-evaluate capability inputs outside the snapshot as well. A primary-contact or release-window
    // change must not publish stale candidate identities or writable controls. Recheck scope last.
    var currentWindow = await ClientPortalService.RequireUploadWindowAsync(db, actor, request.EngagementId, ct);
    var currentCanWrite = request.State is not (PbcStates.Accepted or PbcStates.Closed) && currentWindow.Succeeded;
    var currentCanUpload = currentWindow.Succeeded && CanUploadInState(request.State);
    var currentCanReply = currentWindow.Succeeded && CanReplyInState(request.State);
    var currentCanManageDelegations = request.ClientOwnerUserId == actor.UserId &&
      await ClientPortalService.IsPrimaryContactAsync(db, actor, request.ClientId, ct);
    var currentCanDelegate = currentCanManageDelegations && request.State is not (PbcStates.Accepted or PbcStates.Closed);
    var current = await FreshParticipantRequestAsync(db, actor, requestId, ct);
    if (!current.Succeeded || current.Value is null)
      return CommandResult<PortalRequestWorkspace>.Fail(current.ErrorCode!, current.Message!);
    if (!SameRequest(request, current.Value) || workspace.CanWrite != currentCanWrite ||
        workspace.CanUpload != currentCanUpload || workspace.CanReply != currentCanReply ||
        workspace.CanManageDelegations != currentCanManageDelegations || workspace.CanDelegate != currentCanDelegate)
      return CommandResult<PortalRequestWorkspace>.Fail(ErrorCodes.GenerationStale, "The request changed while it was being read. Reload the current version.");
    return CommandResult<PortalRequestWorkspace>.Ok(workspace);
  }

  public static async Task<CommandResult<PortalRequestHistory>> RequestHistoryAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid requestId, int uploadPage, int conversationPage, int pageSize, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortalRequestHistory>.Fail(ErrorCodes.GateBlocked, "Request history requires an independent read snapshot.");
    if (uploadPage is < 0 or > 10000 || conversationPage is < 0 or > 10000 || pageSize is not (10 or 25 or 50) ||
        !await IsClientAsync(db, actor, ct)) return CommandResult<PortalRequestHistory>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");

    PbcRequest? request;
    CommandResult<PortalRequestHistory> history;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      if (!await IsClientAsync(db, actor, ct))
        return CommandResult<PortalRequestHistory>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
      request = await ClientPortalService.ParticipantRequests(db, actor).SingleOrDefaultAsync(x => x.Id == requestId, ct);
      if (request is null || !await AuthorizedAsync(db, actor, request, ct))
        return CommandResult<PortalRequestHistory>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
      history = await ReadRequestHistoryAsync(db, actor, request, uploadPage, conversationPage, pageSize, ct);
      if (!history.Succeeded || history.Value is null)
        return CommandResult<PortalRequestHistory>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
      await snapshot.CommitAsync(ct);
    }

    var current = await FreshParticipantRequestAsync(db, actor, requestId, ct);
    if (!current.Succeeded || current.Value is null)
      return CommandResult<PortalRequestHistory>.Fail(current.ErrorCode!, current.Message!);
    if (!SameRequest(request, current.Value))
      return CommandResult<PortalRequestHistory>.Fail(ErrorCodes.GenerationStale, "The request changed while it was being read. Reload the current version.");
    return history;
  }

  private static async Task<CommandResult<PbcRequest>> FreshParticipantRequestAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid requestId, CancellationToken ct)
  {
    if (!await IsClientAsync(db, actor, ct))
      return CommandResult<PbcRequest>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
    // Use the same non-draft participation query as the initial read. A hidden or withdrawn
    // request remains indistinguishable from an unknown request, including after a draft transition.
    var request = await ClientPortalService.ParticipantRequests(db, actor).SingleOrDefaultAsync(x => x.Id == requestId, ct);
    if (request is null || !await AuthorizedAsync(db, actor, request, ct))
      return CommandResult<PbcRequest>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
    return CommandResult<PbcRequest>.Ok(request);
  }

  private static async Task<CommandResult<PortalRequestHistory>> ReadRequestHistoryAsync(IAuditSphereDbContext db, ActorContext actor,
    PbcRequest request, int uploadPage, int conversationPage, int pageSize, CancellationToken ct)
  {
    var uploads = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PbcRequestId == request.Id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Skip(uploadPage * pageSize).Take(pageSize + 1).ToListAsync(ct);
    var messages = await db.PbcCommunications.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PbcRequestId == request.Id)
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip(conversationPage * pageSize).Take(pageSize + 1).ToListAsync(ct);
    var uploadItems = uploads.Take(pageSize).ToArray();
    var messageItems = messages.Take(pageSize).ToArray();
    var operations = uploadItems.Where(x => x.TransferOperationId.HasValue).Select(x => x.TransferOperationId!.Value).ToArray();
    var states = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && operations.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Status.ToString(), ct);
    var uploadRows = uploadItems.Select(x => new PortalUploadItem(x.Id, x.FileName,
      x.ReceivedByteCount.ToString(CultureInfo.InvariantCulture), x.DeclaredByteCount.ToString(CultureInfo.InvariantCulture), x.State,
      x.TransferOperationId is { } op && states.TryGetValue(op, out var state) ? state : null,
      x.DeclaredSha256Hex, x.CreatedAt.ToUniversalTime().ToString("O"))).ToArray();
    var conversationRows = messageItems.Select(x => new PortalConversationItem(x.Id, x.CreatedAt.ToUniversalTime().ToString("O"),
      x.Kind is PbcCommunicationKinds.ClientMessage or PbcCommunicationKinds.ClientAction ? "Client"
        : x.Kind == PbcCommunicationKinds.Email ? "Email notification" : "Audit team", x.Body)).ToArray();
    return CommandResult<PortalRequestHistory>.Ok(new(request.Id, uploadPage, conversationPage, pageSize,
      uploadRows, uploads.Count > pageSize, conversationRows, messages.Count > pageSize));
  }
  private static bool SameRequest(PbcRequest original, PbcRequest current) =>
    original.Id == current.Id && original.FirmId == current.FirmId && original.ClientId == current.ClientId &&
    original.EngagementId == current.EngagementId && original.ClientOwnerUserId == current.ClientOwnerUserId &&
    original.State == current.State && original.Revision == current.Revision && original.Area == current.Area &&
    original.Objective == current.Objective && original.AcceptanceCriteria == current.AcceptanceCriteria &&
    original.RequestedFormat == current.RequestedFormat && original.DueDate == current.DueDate &&
    original.ClarificationReason == current.ClarificationReason;
  private static bool CanUploadInState(string state) => state is
    (PbcStates.Sent or PbcStates.Acknowledged or PbcStates.PartiallyReceived or PbcStates.Resubmitted);
  private static bool CanReplyInState(string state) => state is
    (PbcStates.Sent or PbcStates.Acknowledged or PbcStates.PartiallyReceived or PbcStates.Received or
      PbcStates.UnderReview or PbcStates.ClarificationRequired or PbcStates.Resubmitted);
  private static Task<bool> IsClientAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" &&
      !x.Disabled && x.SessionEpoch == actor.SessionEpoch, ct);
  private static async Task<bool> AuthorizedAsync(IAuditSphereDbContext db, ActorContext actor, PbcRequest request, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, request.ClientId, request.EngagementId, ["ClientUser"]), ct)).Succeeded &&
    await ClientPortalService.IsRequestParticipantAsync(db, actor, request, ct);
  private static CommandResult<PortalRequestWorkspace> Denied() => CommandResult<PortalRequestWorkspace>.Fail(ErrorCodes.ScopeDenied, "Request unavailable.");
}
