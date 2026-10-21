using System.Data;
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuditSphereOps.Application.Practice;

public sealed record ProposalHistory(Guid Id, string Revision, string Status, string Fee, string Currency,
  DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? ResponseAt);
public sealed record ProposalDispatchPreview(string Purpose, string ResolutionStatus, string? RecipientName,
  string? RecipientEmail, string? AuthorityBasis, string ResolvedAsOf, string QuotationStatus,
  DateTimeOffset? QuotationValidUntil, string ArtifactStatus, string? OfferSha256, bool CanQueue, string? Message);
public sealed record ProposalWorkspace(Guid Id, Guid OpportunityId, string LeadName, string ServiceRoute, string EntityScope,
  string Stage, string Revision, string Status, string ServiceProfile, string Scope, string Exclusions,
  string Deliverables, string Dependencies, string Fee, string Currency, string PeriodStart, string PeriodEnd,
  string? ResponseReason, Guid? ClientId, bool CanApprove, string OwnerName, string AuthorName, string ReviewerName,
  DateTimeOffset? ApprovedAt, DateTimeOffset? SentAt, DateTimeOffset? ResponseAt, Guid? SupersedesId,
  string? SentOfferSha256, string? DispatchState, DateTimeOffset? DispatchedAt, string? DispatchRecipient,
  string? ResponseOfferSha256, string? RespondentName, string? RespondentEmail, string? ResponseEvidenceReference,
  IReadOnlyList<ProposalHistory> Versions, string? ResponseChannel = null, string Timeline = "",
  string? ResponseAuthorityBasis = null, string? ResponseRecordedByName = null,
  ProposalDispatchPreview? DispatchPreview = null);

public sealed record OpportunityItem(Guid Id, string ServiceRoute, string EntityScope, string Stage,
  string ExpectedFee, string Currency, string PeriodStart, string PeriodEnd, Guid? ProposalId, string Revision,
  DateTimeOffset CreatedAt);
public sealed record LeadActivityItem(Guid Id, string Action, DateTimeOffset OccurredAt, string ActorName,
  Guid? ActorUserId, bool IsBackfill, string? FromState, string? ToState, string? SourceChannel,
  string? OwnerName, Guid? OwnerUserId, string? ServiceRoute, Guid? OpportunityId, Guid? ProposalId,
  string? ProposalRevision, Guid? ClientId);
public sealed record LeadWorkspace(Guid Id, string Name, string Status, string Source, Guid? OwnerUserId,
  string OwnerName, DateTimeOffset CreatedAt, IReadOnlyList<OpportunityItem> Opportunities,
  IReadOnlyList<LeadActivityItem> History, bool HistoryHasMore, bool HasLegacyHistoryGap,
  bool OpportunitiesHasMore);

public static class CommercialWorkspaceQuery
{
  public static async Task<CommandResult<LeadWorkspace>> LeadAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, DateTimeOffset? historyBeforeOccurredAt = null,
    Guid? historyBeforeId = null, CancellationToken ct = default,
    DateTimeOffset? opportunityBeforeCreatedAt = null, Guid? opportunityBeforeId = null)
  {
    if (historyBeforeOccurredAt.HasValue != historyBeforeId.HasValue || historyBeforeId == Guid.Empty)
      return CommandResult<LeadWorkspace>.Fail("crm.invalid", "Lead history cursor is invalid.");
    if (opportunityBeforeCreatedAt.HasValue != opportunityBeforeId.HasValue || opportunityBeforeId == Guid.Empty)
      return CommandResult<LeadWorkspace>.Fail("crm.invalid", "Lead opportunity cursor is invalid.");
    if (opportunityBeforeCreatedAt is { } opportunityCursor)
      opportunityBeforeCreatedAt = opportunityCursor.ToUniversalTime();
    var request = new AuthorizationRequest(actor.FirmId,
      RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"], InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<LeadWorkspace>.Fail(ErrorCodes.ScopeDenied, "Lead unavailable.");
    var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (lead is null) return CommandResult<LeadWorkspace>.Fail(ErrorCodes.ScopeDenied, "Lead unavailable.");
    var opportunityQuery = db.Opportunities.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.LeadId == id);
    if (opportunityBeforeCreatedAt is { } opportunityBeforeAt && opportunityBeforeId is { } opportunityBeforeKey)
    {
      var cursorExists = await opportunityQuery.AnyAsync(x =>
        x.CreatedAt == opportunityBeforeAt && x.Id == opportunityBeforeKey, ct);
      if (!cursorExists)
        return CommandResult<LeadWorkspace>.Fail("crm.invalid", "Lead opportunity cursor is invalid.");
      opportunityQuery = opportunityQuery.Where(x => x.CreatedAt < opportunityBeforeAt ||
        (x.CreatedAt == opportunityBeforeAt && x.Id.CompareTo(opportunityBeforeKey) > 0));
    }
    var opportunityRows = await opportunityQuery
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(101).ToListAsync(ct);
    var opportunitiesHasMore = opportunityRows.Count > 100;
    var opportunities = opportunityRows.Take(100).ToList();
    var ids = opportunities.Select(x => x.Id).ToArray();
    var proposals = await db.Proposals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.OpportunityId))
      .GroupBy(x => x.OpportunityId).Select(g => g.OrderByDescending(x => x.Revision).First()).ToListAsync(ct);
    var activityQuery = db.LeadActivityEvents.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.LeadId == id);
    if (historyBeforeOccurredAt is { } beforeAt && historyBeforeId is { } beforeId)
    {
      var cursorExists = await activityQuery.AnyAsync(x => x.OccurredAt == beforeAt && x.Id == beforeId, ct);
      if (!cursorExists)
        return CommandResult<LeadWorkspace>.Fail("crm.invalid", "Lead history cursor is invalid.");
      activityQuery = activityQuery.Where(x => x.OccurredAt < beforeAt ||
        (x.OccurredAt == beforeAt && x.Id.CompareTo(beforeId) < 0));
    }
    var activityRows = await activityQuery
      .OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id).Take(201).ToListAsync(ct);
    var historyHasMore = activityRows.Count > 200;
    var activity = activityRows.Take(200).OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).ToArray();
    var userIds = activity.SelectMany(x => new[] { x.ActorUserId, x.OwnerUserId })
      .Append(lead.OwnerUserId).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && userIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    string UserName(Guid? userId, string missing) => userId.HasValue && names.TryGetValue(userId.Value, out var name)
      ? name : missing;
    var history = activity.Select(x => new LeadActivityItem(x.Id, x.Action, x.OccurredAt,
      UserName(x.ActorUserId, x.IsBackfill ? "Actor not retained" : "Actor unavailable"),
      x.ActorUserId, x.IsBackfill, x.FromState, x.ToState, x.SourceChannel,
      x.OwnerUserId.HasValue ? UserName(x.OwnerUserId, "Owner unavailable") : null,
      x.OwnerUserId, x.ServiceRoute, x.OpportunityId, x.ProposalId,
      x.ProposalRevision?.ToString(CultureInfo.InvariantCulture), x.ClientId)).ToArray();
    var hasLegacyHistoryGap = await db.LeadActivityEvents.AsNoTracking()
      .AnyAsync(x => x.FirmId == actor.FirmId && x.LeadId == id && x.IsBackfill, ct);
    var rows = opportunities.Select(o =>
    {
      var latest = proposals.SingleOrDefault(p => p.OpportunityId == o.Id);
      return new OpportunityItem(o.Id, o.ServiceRoute, o.EntityScope, o.Stage,
        o.ExpectedFee.ToString(CultureInfo.InvariantCulture), o.Currency, o.PeriodStart, o.PeriodEnd,
        latest?.Id, (latest?.Revision ?? 0).ToString(CultureInfo.InvariantCulture), o.CreatedAt);
    }).ToArray();
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<LeadWorkspace>.Fail(ErrorCodes.ScopeDenied, "Lead unavailable.");
    return CommandResult<LeadWorkspace>.Ok(new(lead.Id, lead.Name, lead.Status, lead.Source,
      lead.OwnerUserId, UserName(lead.OwnerUserId, "Not assigned"), lead.CreatedAt, rows,
      history, historyHasMore, hasLegacyHistoryGap, opportunitiesHasMore));
  }

  public static async Task<CommandResult<ProposalWorkspace>> ProposalAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.GateBlocked,
        "Proposal workspace requires an independent read snapshot.");

    CommandResult<ProposalWorkspace> result;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var snapshotStartedAtUtc = DateTimeOffset.UtcNow;
      result = await ReadWithinSnapshotAsync(db, actor, id, snapshotStartedAtUtc, ct);
      if (!result.Succeeded) return result;
      await snapshot.CommitAsync(ct);
    }

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId,
        RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"], InternalOnly: true, RequireFirmWide: true), ct);
    if (!auth.Succeeded)
      return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    return result;
  }

  /// <summary>
  /// Reads the authorized proposal projection within an outer-owned RepeatableRead snapshot. The caller must reauthorize
  /// after disposing that snapshot before returning the projection.
  /// </summary>
  internal static async Task<CommandResult<ProposalWorkspace>> ReadWithinSnapshotAsync(IAuditSphereDbContext db,
    ActorContext actor, Guid id, DateTimeOffset snapshotStartedAtUtc, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.RepeatableRead)
      return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.GateBlocked,
        "Proposal projection requires the caller's RepeatableRead snapshot.");

    var request = new AuthorizationRequest(actor.FirmId,
      RequiredRoles: ["Administrator", "Partner", "Manager", "RelationshipManager"], InternalOnly: true, RequireFirmWide: true);
    if (!(await AuthorizationDecision.AuthorizeAsync(db, actor, request, ct)).Succeeded)
      return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");

    var p = await db.Proposals.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == id, ct);
    if (p is null) return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var opportunity = await db.Opportunities.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == p.OpportunityId, ct);
    if (opportunity is null) return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var lead = await db.Leads.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == opportunity.LeadId, ct);
    if (lead is null) return CommandResult<ProposalWorkspace>.Fail(ErrorCodes.ScopeDenied, "Proposal unavailable.");
    var versions = await db.Proposals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.OpportunityId == p.OpportunityId)
      .OrderByDescending(x => x.Revision).Take(100).ToListAsync(ct);
    var responseRecord = await db.ProposalResponseRecords.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == p.Id, ct);
    var ownerId = opportunity.OwnerUserId ?? lead.OwnerUserId;
    var involved = new[] { ownerId, p.PreparedByUserId, p.ApprovedByUserId, responseRecord?.RecordedByUserId }
      .Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToArray();
    var names = await db.Users.AsNoTracking().Where(x => x.FirmId == actor.FirmId && involved.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.DisplayName, ct);
    string Name(Guid? userId, string fallback) => userId.HasValue && names.TryGetValue(userId.Value, out var n) ? n : fallback;
    // Truthful dispatch evidence: the queued email's provider-outcome state, not the commercial `SENT` status alone.
    var dispatch = await db.CommercialNotifications.AsNoTracking()
      .SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProposalId == p.Id, ct);
    var dispatchPreview = await DispatchPreviewAsync(db, actor, p, opportunity, lead, snapshotStartedAtUtc,
      dispatch is not null, ct);

    var workspace = new ProposalWorkspace(p.Id, p.OpportunityId, lead.Name, opportunity.ServiceRoute, opportunity.EntityScope,
      opportunity.Stage, p.Revision.ToString(CultureInfo.InvariantCulture), p.Status, p.ServiceProfileId, p.Scope, p.Exclusions,
      p.Deliverables, p.Dependencies, p.Fee.ToString(CultureInfo.InvariantCulture), p.Currency, p.PeriodStart, p.PeriodEnd,
      responseRecord?.Reason ?? p.ResponseReason, p.PracticeClientId ?? opportunity.PracticeClientId,
      p.PreparedByUserId.HasValue && p.PreparedByUserId != actor.UserId && p.Status == "DRAFT",
      Name(ownerId, "Not assigned"),
      p.PreparedByUserId.HasValue ? Name(p.PreparedByUserId, "Author unavailable") : "Not recorded (legacy proposal)",
      p.ApprovedByUserId.HasValue ? Name(p.ApprovedByUserId, "Reviewer unavailable") : "Not recorded",
      p.ApprovedAt, p.SentAt, p.ResponseAt, p.SupersedesId,
      p.SentOfferSha256, dispatch?.DeliveryState, dispatch?.DeliveredAt, dispatch?.Recipient,
      responseRecord?.OfferSha256 ?? p.ResponseOfferSha256,
      responseRecord?.RespondentName ?? p.RespondentName, responseRecord?.RespondentEmail ?? p.RespondentEmail,
      responseRecord?.EvidenceReference ?? p.ResponseEvidenceReference,
      versions.Select(v => new ProposalHistory(v.Id, v.Revision.ToString(CultureInfo.InvariantCulture), v.Status,
        v.Fee.ToString(CultureInfo.InvariantCulture), v.Currency, v.CreatedAt, v.SentAt, v.ResponseAt)).ToArray(),
      responseRecord?.Channel ?? p.ResponseChannel, p.Timeline,
      responseRecord?.AuthorityBasis,
      responseRecord is null ? null : Name(responseRecord.RecordedByUserId, "Recorder unavailable"),
      dispatchPreview);
    return CommandResult<ProposalWorkspace>.Ok(workspace);
  }

  private static async Task<ProposalDispatchPreview> DispatchPreviewAsync(IAuditSphereDbContext db, ActorContext actor,
    AuditSphereOps.Domain.Practice.Proposal proposal, Opportunity opportunity, Lead lead,
    DateTimeOffset snapshotStartedAtUtc, bool hasDispatch, CancellationToken ct)
  {
    var now = snapshotStartedAtUtc;
    var asOf = DateOnly.FromDateTime(now.UtcDateTime);
    var clientId = proposal.PracticeClientId ?? opportunity.PracticeClientId;
    string resolutionStatus;
    string? recipientName;
    string? recipientEmail;
    string? authorityBasis;
    string? recipientMessage;

    if (clientId is { } practiceClientId)
    {
      var resolution = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, actor, practiceClientId,
        CorrespondencePurposes.Commercial, asOf, ct: ct);
      resolutionStatus = resolution.Status switch
      {
        RecipientResolutionStatus.Resolved => "RESOLVED",
        RecipientResolutionStatus.NoRecipient => "NO_RECIPIENT",
        RecipientResolutionStatus.Ambiguous => "AMBIGUOUS",
        RecipientResolutionStatus.Overridden => "OVERRIDDEN",
        RecipientResolutionStatus.Denied => "DENIED",
        _ => "UNAVAILABLE"
      };
      recipientName = resolution.FullName;
      recipientEmail = resolution.Email;
      authorityBasis = resolution.Status == RecipientResolutionStatus.Resolved
        ? ProposalRecipientAuthorityBases.ClientCommercialRouting : null;
      recipientMessage = resolution.Message;
      if (resolution.Status == RecipientResolutionStatus.Resolved)
      {
        var name = recipientName?.Trim();
        var email = recipientEmail?.Trim();
        if (resolution.ClientContactId is null || resolution.RoutingId is null ||
            string.IsNullOrWhiteSpace(name) || name.Length > 200 || string.IsNullOrWhiteSpace(email) || email.Length > 320 ||
            !System.Net.Mail.MailAddress.TryCreate(email, out var resolvedAddress))
        {
          resolutionStatus = "INVALID_RECIPIENT";
          authorityBasis = null;
          recipientMessage = "The designated commercial contact must have valid authority, name and email details.";
        }
        else
        {
          recipientName = name;
          recipientEmail = resolvedAddress.Address;
        }
      }
    }
    else
    {
      recipientName = (lead.PrimaryContactName ?? lead.Name).Trim();
      var email = (lead.PrimaryContactEmail ?? string.Empty).Trim();
      if (string.IsNullOrWhiteSpace(recipientName) || string.IsNullOrWhiteSpace(email))
      {
        resolutionStatus = "NO_RECIPIENT";
        recipientEmail = null;
        authorityBasis = null;
        recipientMessage = "Record a valid prospect primary contact before dispatch.";
      }
      else if (recipientName.Length > 200 || email.Length > 320 ||
          !System.Net.Mail.MailAddress.TryCreate(email, out var parsedAddress))
      {
        resolutionStatus = "INVALID_RECIPIENT";
        recipientEmail = null;
        authorityBasis = null;
        recipientMessage = "The prospect primary contact must have a valid name and email address.";
      }
      else
      {
        resolutionStatus = "RESOLVED";
        recipientEmail = parsedAddress.Address;
        authorityBasis = ProposalRecipientAuthorityBases.ProspectPrimaryContact;
        recipientMessage = "The prospect primary contact will be frozen when the proposal is dispatched.";
      }
    }

    var quotation = await db.QuotationVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var quotationApprovalsStand = quotation is { Status: QuotationStates.Approved } &&
      await QuotationService.ApprovalsStandAsync(db, actor.FirmId, quotation.Id, ct);
    var quotationExpired = quotation?.ValidUntil is { } validUntil && validUntil < now;
    var quotationStatus = quotation is null ? "MISSING" : quotation.Status != QuotationStates.Approved ? "NOT_APPROVED" :
      quotationExpired ? "EXPIRED" : !quotationApprovalsStand ? "APPROVAL_REVOKED" :
      quotation.ValidUntil is null ? "NO_EXPIRY" : "CURRENT";

    var document = await db.CommercialDocuments.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id && x.Kind == CommercialDocumentKinds.Quotation)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var artifactStatus = document is null ? "NOT_GENERATED" : quotation is null || document.QuotationVersionId != quotation.Id
      ? "STALE_QUOTATION" : document.Bytes.Length == 0 ||
        !string.Equals(document.Sha256Hex, Hashing.Sha256Hex(document.Bytes), StringComparison.OrdinalIgnoreCase)
          ? "INVALID_DIGEST" : "CURRENT";
    var canQueue = proposal.Status == "INTERNAL_REVIEW" && !hasDispatch && resolutionStatus == "RESOLVED" &&
      quotation is { Status: QuotationStates.Approved } && !quotationExpired && quotationApprovalsStand && artifactStatus == "CURRENT";
    var blockers = new List<string>();
    if (resolutionStatus != "RESOLVED") blockers.Add(recipientMessage ?? "A current commercial recipient is required.");
    if (quotationStatus is "MISSING" or "NOT_APPROVED" or "EXPIRED" or "APPROVAL_REVOKED")
      blockers.Add($"The quotation state is {quotationStatus.ToLowerInvariant().Replace('_', ' ')}.");
    if (artifactStatus != "CURRENT") blockers.Add($"The rendered quotation state is {artifactStatus.ToLowerInvariant().Replace('_', ' ')}.");
    if (proposal.Status != "INTERNAL_REVIEW") blockers.Add("The proposal must pass independent internal review before dispatch.");
    if (hasDispatch) blockers.Add("A prior dispatch exists; reconcile it or create a new proposal revision.");

    return new ProposalDispatchPreview(CorrespondencePurposes.Commercial, resolutionStatus, recipientName, recipientEmail,
      authorityBasis, asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), quotationStatus, quotation?.ValidUntil,
      artifactStatus, artifactStatus == "CURRENT" ? document?.Sha256Hex : null, canQueue,
      blockers.Count == 0 ? "Preview only. The server rechecks recipient, quotation and artifact before dispatch." :
        string.Join(" ", blockers));
  }
}
