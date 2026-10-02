using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalDocumentComment(string Body, bool FromClient, string? Resolution);
public sealed record PortalSharedDocument(Guid ReviewId, Guid DeliverableId, string Kind, string Title, string Version, string Sha256,
  DateTimeOffset? AcknowledgedAt, IReadOnlyList<PortalDocumentComment> Comments);
public sealed record PortalReviewDocuments(IReadOnlyList<PortalSharedDocument> Documents, IReadOnlyList<SignedLetterView> SignedLetters, IReadOnlyList<BundleView> Bundles);
public static class ClientPortalReviewQuery
{
  public static async Task<CommandResult<PortalReviewDocuments>> GetAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" && !x.Disabled, ct))
      return CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.ScopeDenied, "Documents unavailable.");
    var ids = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    var reviewIds = await db.ClientDeliverableReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.EngagementId)).OrderBy(x => x.Id).Take(201).Select(x => x.Id).ToListAsync(ct);
    if (reviewIds.Count > 200 || await db.ClientDeliverableComments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && reviewIds.Contains(x.ReviewId)).Take(1001).CountAsync(ct) > 1000)
      return CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.GateBlocked, "Contact your audit team for a bounded document archive review.");
    var documents = new List<PortalSharedDocument>(); var scans = new List<SignedLetterView>(); var bundles = new List<BundleView>();
    foreach (var engagementId in ids)
    {
      var shared = await AuditDeliverableService.SharedAsync(db, actor, engagementId, ct);
      documents.AddRange(shared.Select(x => new PortalSharedDocument(x.ReviewId, x.DeliverableId, x.Kind, x.Title,
        x.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), x.ContentSha256, x.AcknowledgedAt,
        x.Comments.Select(c => new PortalDocumentComment(c.Body, c.FromClient, c.Resolution)).ToArray())));
      scans.AddRange(await AuditDeliverableService.SignedRepresentationsAsync(db, actor, engagementId, ct));
      bundles.AddRange(await AuditDeliverableService.ClientBundlesAsync(db, actor, engagementId, ct));
      if (documents.Count > 200 || scans.Count > 100 || bundles.Count > 100 || documents.Any(x => x.Comments.Count > 1000))
        return CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.GateBlocked, "Contact your audit team for a bounded document archive review.");
    }
    var current = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    if (!ids.Order().SequenceEqual(current.Order())) return CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.ScopeDenied, "Documents unavailable.");
    return CommandResult<PortalReviewDocuments>.Ok(new(documents, scans, bundles));
  }
}
