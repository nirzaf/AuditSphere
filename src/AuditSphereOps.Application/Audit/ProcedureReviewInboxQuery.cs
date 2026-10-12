using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record ProcedureReviewInboxRequest(DateTimeOffset? AfterSubmittedAt = null,
  Guid? AfterResultId = null, int PageSize = 50);
public sealed record ProcedureReviewInboxCursor(DateTimeOffset SubmittedAt, Guid ResultId);
public sealed record ProcedureReviewInboxRow(Guid ResultId, Guid ProcedureId, Guid EngagementId,
  string ClientName, string ServiceRoute, string EngagementPeriod, string SourceProcedureId,
  string ProcedureTitle, long Revision, string PreparedByDisplayName, DateTimeOffset SubmittedAt);
public sealed record ProcedureReviewInbox(IReadOnlyList<ProcedureReviewInboxRow> Reviews, bool HasMore,
  ProcedureReviewInboxCursor? NextCursor);

/// <summary>
/// Read-only locator for current submitted procedure results that have no operational task for their procedure.
/// It never creates tasks or changes audit review state; the fieldwork workspace and review command recheck all
/// current planning, risk, hold, revision and independence requirements before a decision is accepted.
/// </summary>
public static class ProcedureReviewInboxQuery
{
  private static readonly string[] ReviewRoles = ["Reviewer", "Senior", "Manager", "Partner", "Administrator"];
  private const int ScanBatchSize = 100;

  public static async Task<CommandResult<ProcedureReviewInbox>> GetAsync(
    IAuditSphereDbContext db, ActorContext actor, ProcedureReviewInboxRequest request,
    CancellationToken ct = default)
  {
    if (request.PageSize is < 1 or > 100 ||
        (request.AfterSubmittedAt is null) != (request.AfterResultId is null))
      return CommandResult<ProcedureReviewInbox>.Fail("workflow.review-inbox-invalid",
        "Choose a page size from 1 to 100 and provide both review cursor values together.");

    var roleAccess = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ReviewRoles, InternalOnly: true), ct);
    if (!roleAccess.Succeeded)
      return CommandResult<ProcedureReviewInbox>.Fail(roleAccess.ErrorCode!, roleAccess.Message!);

    var now = DateTimeOffset.UtcNow;
    var query = from result in db.AuditProcedureResults.AsNoTracking()
      join procedure in db.AuditProcedures.AsNoTracking()
        on new { result.FirmId, result.ClientId, result.EngagementId, Id = result.AuditProcedureId }
        equals new { procedure.FirmId, procedure.ClientId, procedure.EngagementId, procedure.Id }
      join engagement in db.Engagements.AsNoTracking()
        on new { result.FirmId, result.ClientId, Id = result.EngagementId }
        equals new { engagement.FirmId, ClientId = engagement.PracticeClientId, engagement.Id }
      join client in db.PracticeClients.AsNoTracking()
        on new { result.FirmId, Id = result.ClientId }
        equals new { client.FirmId, client.Id }
      join preparer in db.Users.AsNoTracking()
        on new { result.FirmId, Id = result.PreparedByUserId }
        equals new { preparer.FirmId, preparer.Id }
      where result.FirmId == actor.FirmId && result.Status == AuditProcedureResultStatuses.Submitted &&
        procedure.ApplicabilityStatus == AuditApplicabilityStatuses.Applicable &&
        (procedure.Status == AuditProcedureStatuses.Submitted || procedure.Status == AuditProcedureStatuses.InReview) &&
        procedure.CurrentResultRevision == result.Revision &&
        !db.WorkflowTaskBindings.Any(binding => binding.FirmId == result.FirmId &&
          binding.ClientId == result.ClientId && binding.EngagementId == result.EngagementId &&
          binding.ReviewProcedureResultId != null && db.AuditProcedureResults.Any(linked =>
            linked.FirmId == binding.FirmId && linked.Id == binding.ReviewProcedureResultId &&
            linked.AuditProcedureId == procedure.Id)) &&
        db.RoleGrants.Any(grant => grant.FirmId == result.FirmId && grant.UserId == actor.UserId &&
          grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now) &&
          ReviewRoles.Contains(grant.Role) &&
          ((grant.ClientId == null && grant.EngagementId == null) ||
           (grant.ClientId == result.ClientId &&
             (grant.EngagementId == null || grant.EngagementId == result.EngagementId))))
      select new
      {
        ResultId = result.Id, ProcedureId = procedure.Id, ClientId = result.ClientId,
        EngagementId = result.EngagementId, ClientName = client.LegalName,
        ServiceRoute = engagement.ServiceRoute, PeriodStart = engagement.PeriodStart,
        PeriodEnd = engagement.PeriodEnd, procedure.SourceProcedureId, ProcedureTitle = procedure.Title,
        result.Revision, result.PreparedByUserId, PreparedByDisplayName = preparer.DisplayName,
        result.SubmittedAt
      };

    var authorizedByEngagement = new Dictionary<Guid, bool>();
    var reviews = new List<ProcedureReviewInboxRow>(request.PageSize + 1);
    var scanSubmittedAt = request.AfterSubmittedAt;
    var scanResultId = request.AfterResultId;
    var exhausted = false;

    while (reviews.Count <= request.PageSize && !exhausted)
    {
      var batchQuery = query;
      if (scanSubmittedAt is { } submittedAt && scanResultId is { } resultId)
        batchQuery = batchQuery.Where(x => x.SubmittedAt < submittedAt ||
          (x.SubmittedAt == submittedAt && x.ResultId.CompareTo(resultId) < 0));

      var batch = await batchQuery.OrderByDescending(x => x.SubmittedAt).ThenByDescending(x => x.ResultId)
        .Take(ScanBatchSize).ToListAsync(ct);
      if (batch.Count == 0)
      {
        exhausted = true;
        break;
      }

      foreach (var item in batch)
      {
        scanSubmittedAt = item.SubmittedAt;
        scanResultId = item.ResultId;
        if (item.PreparedByUserId == actor.UserId) continue;

        if (!authorizedByEngagement.TryGetValue(item.EngagementId, out var authorized))
        {
          var scoped = await AuthorizationDecision.AuthorizeAsync(db, actor,
            new AuthorizationRequest(actor.FirmId, item.ClientId, item.EngagementId,
              ReviewRoles, InternalOnly: true), ct);
          authorized = scoped.Succeeded;
          authorizedByEngagement[item.EngagementId] = authorized;
        }
        if (!authorized) continue;

        var hierarchy = await ReviewNotesService.RequireReviewerAboveAsync(db, actor.FirmId,
          item.EngagementId, item.PreparedByUserId, actor.UserId, ct);
        if (!hierarchy.Succeeded) continue;

        reviews.Add(new ProcedureReviewInboxRow(item.ResultId, item.ProcedureId, item.EngagementId,
          item.ClientName, item.ServiceRoute, $"{item.PeriodStart} – {item.PeriodEnd}",
          item.SourceProcedureId, item.ProcedureTitle, item.Revision, item.PreparedByDisplayName,
          item.SubmittedAt));
        if (reviews.Count > request.PageSize) break;
      }

      exhausted = batch.Count < ScanBatchSize;
    }

    var hasMore = reviews.Count > request.PageSize;
    if (hasMore) reviews.RemoveAt(reviews.Count - 1);
    var last = reviews.LastOrDefault();
    return CommandResult<ProcedureReviewInbox>.Ok(new ProcedureReviewInbox(reviews, hasMore,
      hasMore && last is not null ? new ProcedureReviewInboxCursor(last.SubmittedAt, last.ResultId) : null));
  }
}
