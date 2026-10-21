using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalDocumentComment(string Body, bool FromClient, string? Resolution);
public sealed record PortalDocumentContext(string ClientLegalName, string ServiceRoute, string PeriodStart, string PeriodEnd);
public sealed record PortalSharedDocument(Guid ReviewId, Guid DeliverableId, string Kind, string Title, string Version, string Sha256,
  DateTimeOffset? AcknowledgedAt, IReadOnlyList<PortalDocumentComment> Comments, PortalDocumentContext Context, DateTimeOffset? SharedAt = null);
public sealed record PortalFinalBundleView(Guid Id, string Sha256, DateTimeOffset AssembledAt, string ManifestSha256,
  DateTimeOffset? AcknowledgedAt, PortalDocumentContext Context);
public sealed record PortalReviewDocuments(IReadOnlyList<PortalSharedDocument> Documents, IReadOnlyList<SignedLetterView> SignedLetters,
  IReadOnlyList<PortalFinalBundleView> Bundles);
public static class ClientPortalReviewQuery
{
  private sealed record ContextRow(Guid EngagementId, Guid ClientId, PortalDocumentContext View);

  public static async Task<CommandResult<PortalReviewDocuments>> GetAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.GateBlocked, "Documents require an independent read snapshot.");
    IReadOnlyList<Guid> ids;
    ContextRow[] contexts;
    PortalReviewDocuments value;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      if (!await IsCurrentClientAsync(db, actor, ct)) return Denied();
      ids = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
      contexts = await ContextsAsync(db, actor.FirmId, ids, ct);
      if (contexts.Length != ids.Count) return Denied();
      var content = await ReadAsync(db, actor, ids, contexts.ToDictionary(x => x.EngagementId, x => x.View), ct);
      if (!content.Succeeded || content.Value is null) return content;
      value = content.Value;
      await snapshot.CommitAsync(ct);
    }
    // Fresh authorization and context reads must run after the repeatable-read snapshot is disposed.
    if (!await IsCurrentClientAsync(db, actor, ct)) return Denied();
    var current = await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct);
    if (!ids.Order().SequenceEqual(current.Order())) return Denied();
    var currentContexts = await ContextsAsync(db, actor.FirmId, current, ct);
    if (!contexts.OrderBy(x => x.EngagementId).SequenceEqual(currentContexts.OrderBy(x => x.EngagementId)))
      return CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.GenerationStale, "The document context changed. Refresh your shared documents.");
    return CommandResult<PortalReviewDocuments>.Ok(value);
  }

  private static async Task<CommandResult<PortalReviewDocuments>> ReadAsync(IAuditSphereDbContext db, ActorContext actor,
    IReadOnlyList<Guid> ids, IReadOnlyDictionary<Guid, PortalDocumentContext> contexts, CancellationToken ct)
  {
    var reviewIds = await db.ClientDeliverableReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.EngagementId))
      .OrderBy(x => x.Id).Take(201).Select(x => x.Id).ToListAsync(ct);
    if (reviewIds.Count > 200 || await db.ClientDeliverableComments.AsNoTracking().Where(x => x.FirmId == actor.FirmId && reviewIds.Contains(x.ReviewId)).Take(1001).CountAsync(ct) > 1000)
      return Bounded();
    var documents = new List<PortalSharedDocument>();
    var scans = new List<SignedLetterView>();
    var bundles = new List<PortalFinalBundleView>();
    foreach (var engagementId in ids)
    {
      var context = contexts[engagementId];
      var shared = await AuditDeliverableService.SharedAsync(db, actor, engagementId, ct);
      documents.AddRange(shared.Select(x => new PortalSharedDocument(x.ReviewId, x.DeliverableId, x.Kind, x.Title,
        x.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), x.ContentSha256, x.AcknowledgedAt,
        x.Comments.Select(c => new PortalDocumentComment(c.Body, c.FromClient, c.Resolution)).ToArray(), context, x.SharedAt)));
      var scanLimit = AuditDeliverableService.MaxClientPortalSignedLetters - scans.Count + 1;
      scans.AddRange(await AuditDeliverableService.SignedRepresentationsAsync(db, actor, scanLimit, engagementId, ct));
      var bundleLimit = AuditDeliverableService.MaxClientPortalBundles - bundles.Count + 1;
      var retained = await AuditDeliverableService.ClientBundlesAsync(db, actor, bundleLimit, engagementId, ct);
      bundles.AddRange(retained.Select(x => new PortalFinalBundleView(x.Id, x.Sha256, x.AssembledAt, x.ManifestSha256, x.AcknowledgedAt, context)));
      if (documents.Count > 200 || scans.Count > AuditDeliverableService.MaxClientPortalSignedLetters ||
          bundles.Count > AuditDeliverableService.MaxClientPortalBundles || documents.Any(x => x.Comments.Count > 1000)) return Bounded();
    }
    return CommandResult<PortalReviewDocuments>.Ok(new(documents, scans, bundles));
  }

  private static Task<bool> IsCurrentClientAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    db.Users.AsNoTracking().AnyAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId && x.UserKind == "Client" &&
      !x.Disabled && x.SessionEpoch == actor.SessionEpoch, ct);

  private static Task<ContextRow[]> ContextsAsync(IAuditSphereDbContext db, Guid firmId, IReadOnlyList<Guid> ids, CancellationToken ct) =>
    (from engagement in db.Engagements.AsNoTracking()
     join client in db.PracticeClients.AsNoTracking() on new { engagement.FirmId, Id = engagement.PracticeClientId }
       equals new { client.FirmId, client.Id }
     where engagement.FirmId == firmId && ids.Contains(engagement.Id)
     select new ContextRow(engagement.Id, client.Id,
       new PortalDocumentContext(client.LegalName, engagement.ServiceRoute, engagement.PeriodStart, engagement.PeriodEnd))).ToArrayAsync(ct);

  private static CommandResult<PortalReviewDocuments> Denied() =>
    CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.ScopeDenied, "Documents unavailable.");
  private static CommandResult<PortalReviewDocuments> Bounded() =>
    CommandResult<PortalReviewDocuments>.Fail(ErrorCodes.GateBlocked, "Contact your audit team for a bounded document archive review.");
}
