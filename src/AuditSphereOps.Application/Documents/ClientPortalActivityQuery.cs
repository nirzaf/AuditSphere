using System.Data;
using System.Globalization;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PortalActivityContext(string ClientLegalName, string? ServiceRoute, string? PeriodStart, string? PeriodEnd);
public sealed record PortalActivityItem(string Key, string At, string Kind, string Label, string Title, string? ActorLabel,
  PortalActivityContext Context, string TargetKind, Guid TargetId);
public sealed record PortalActivityPage(string SnapshotAtUtc, int PageSize, IReadOnlyList<PortalActivityItem> Items,
  bool HasMore, string? NextCursor);

/// <summary>
/// A bounded projection of retained client-visible facts. The cursor binds the continuation to the current
/// authorization/request scope; its UTC watermark filters event timestamps but is not a durable database snapshot.
/// </summary>
public static class ClientPortalActivityQuery
{
  private const int PageSize = 25;
  private const int MaxCursorLength = 4096;
  private const int MaxGrants = 500;
  private const int MaxRequests = 1000;
  private const int ReadAhead = PageSize + 1;

  private sealed record ContextRow(Guid EngagementId, Guid ClientId, PortalActivityContext Context);
  private sealed record GrantRow(Guid Id, Guid ClientId, Guid? EngagementId, DateTimeOffset GrantedAt, DateTimeOffset? ExpiresAt);
  private sealed record RequestRow(Guid Id, Guid ClientId, Guid EngagementId, Guid OwnerId, string State, long Revision, string Area);
  private sealed record DelegationRow(Guid Id, Guid ClientId, Guid EngagementId, Guid RequestId, Guid DelegateId, DateTimeOffset CreatedAt);
  private sealed record ScopeState(Guid[] EngagementIds, Guid[] RequestIds, GrantRow[] Grants, RequestRow[] Requests,
    DelegationRow[] Delegations, PortalActivityContext[] ClientContexts, ContextRow[] EngagementContexts,
    string AuthorizationDigest, string ContextDigest);
  private sealed record Cursor(DateTimeOffset Watermark, DateTimeOffset LastAt, string LastKind, Guid LastId,
    string AuthorizationDigest, string ContextDigest);
  private sealed record Candidate(DateTimeOffset At, int Rank, string Kind, Guid SourceId, string Label, string Title,
    string? ActorLabel, string? RawKind, string TargetKind, Guid TargetId, Guid ClientId, Guid? EngagementId);
  private sealed record ClientRow(Guid Id, string LegalName);
  private sealed record EngagementContextRow(Guid Id, Guid ClientId, string ServiceRoute, string PeriodStart, string PeriodEnd);

  public static async Task<CommandResult<PortalActivityPage>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    int pageSize = PageSize, string? cursor = null, CancellationToken ct = default)
  {
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<PortalActivityPage>.Fail(ErrorCodes.GateBlocked, "Client activity requires an independent read snapshot.");
    if (pageSize != PageSize || cursor is { Length: > MaxCursorLength }) return Invalid();
    Cursor? after = null;
    if (!string.IsNullOrEmpty(cursor) && !TryDecodeCursor(cursor, out after)) return Invalid();

    var watermark = after?.Watermark ?? NormalizeUtc(DateTimeOffset.UtcNow);
    if (watermark > NormalizeUtc(DateTimeOffset.UtcNow)) return Invalid();

    ScopeState scope;
    Candidate[] rows;
    Dictionary<(Guid ClientId, Guid? EngagementId), PortalActivityContext> contextMap;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var loaded = await LoadScopeAsync(db, actor, ct);
      if (!loaded.Succeeded || loaded.Value is null)
        return CommandResult<PortalActivityPage>.Fail(loaded.ErrorCode!, loaded.Message ?? "Client activity unavailable.");
      scope = loaded.Value;
      if (after is not null)
      {
        if (after.AuthorizationDigest != scope.AuthorizationDigest) return Denied();
        if (after.ContextDigest != scope.ContextDigest) return Stale();
      }

      var candidates = await ReadCandidatesAsync(db, actor, scope, watermark, after, ct);
      var contextResult = await ContextsForCandidatesAsync(db, actor.FirmId, candidates, ct);
      if (!contextResult.Succeeded || contextResult.Value is null)
        return CommandResult<PortalActivityPage>.Fail(contextResult.ErrorCode!, contextResult.Message ?? "Client activity unavailable.");
      contextMap = contextResult.Value;
      if (candidates.Any(x => !contextMap.ContainsKey((x.ClientId, x.EngagementId)))) return Bounded();
      rows = candidates.OrderByDescending(x => x.At).ThenBy(x => x.Rank).ThenBy(x => x.SourceId).Take(ReadAhead).ToArray();
      await snapshot.CommitAsync(ct);
    }

    // Fresh scope/session checks happen after the owned snapshot is disposed.
    var current = await LoadScopeAsync(db, actor, ct);
    if (!current.Succeeded || current.Value is null) return Denied();
    if (current.Value.AuthorizationDigest != scope.AuthorizationDigest) return Denied();
    if (current.Value.ContextDigest != scope.ContextDigest) return Stale();

    var hasMore = rows.Length > PageSize;
    var selected = rows.Take(PageSize).ToArray();
    var items = selected.Select(x => ToItem(x, contextMap[(x.ClientId, x.EngagementId)])).ToArray();
    var next = hasMore && selected.Length > 0
      ? EncodeCursor(new(watermark, selected[^1].At, selected[^1].Kind, selected[^1].SourceId,
        scope.AuthorizationDigest, scope.ContextDigest))
      : null;
    return CommandResult<PortalActivityPage>.Ok(new(watermark.ToString("O", CultureInfo.InvariantCulture), PageSize, items, hasMore, next));
  }

  private static async Task<CommandResult<ScopeState>> LoadScopeAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId &&
      x.UserKind == "Client" && !x.Disabled && x.SessionEpoch == actor.SessionEpoch, ct);
    if (user is null) return CommandResult<ScopeState>.Fail(ErrorCodes.ScopeDenied, "Client activity unavailable.");

    var now = DateTimeOffset.UtcNow;
    var grants = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.UserId == actor.UserId &&
        x.Role == "ClientUser" && x.ClientId != null && x.RevokedAt == null && (x.ExpiresAt == null || x.ExpiresAt > now))
      .OrderBy(x => x.Id).Take(MaxGrants + 1)
      .Select(x => new GrantRow(x.Id, x.ClientId!.Value, x.EngagementId, x.GrantedAt, x.ExpiresAt)).ToArrayAsync(ct);
    if (grants.Length > MaxGrants) return CommandResult<ScopeState>.Fail(ErrorCodes.GateBlocked, "The client activity scope is too large to review safely.");

    var candidateCount = await db.Engagements.AsNoTracking().Where(e => e.FirmId == actor.FirmId &&
      db.RoleGrants.Any(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId && g.Role == "ClientUser" && g.ClientId == e.PracticeClientId &&
        g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > now) && (g.EngagementId == null || g.EngagementId == e.Id)))
      .Take(MaxGrants + 1).CountAsync(ct);
    if (candidateCount > MaxGrants) return CommandResult<ScopeState>.Fail(ErrorCodes.GateBlocked, "The client activity scope is too large to review safely.");
    var engagementIds = (await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db, actor, ct)).Order().ToArray();
    var contexts = await (from engagement in db.Engagements.AsNoTracking()
      join client in db.PracticeClients.AsNoTracking() on new { engagement.FirmId, Id = engagement.PracticeClientId }
        equals new { client.FirmId, client.Id }
      where engagement.FirmId == actor.FirmId && engagementIds.Contains(engagement.Id)
      orderby engagement.Id
      select new ContextRow(engagement.Id, client.Id, new PortalActivityContext(client.LegalName,
        NullIfBlank(engagement.ServiceRoute), NullIfBlank(engagement.PeriodStart), NullIfBlank(engagement.PeriodEnd))))
      .ToArrayAsync(ct);
    if (contexts.Length != engagementIds.Length) return CommandResult<ScopeState>.Fail(ErrorCodes.ScopeDenied, "Client activity unavailable.");

    var eligibleRequests = from request in db.PbcRequests.AsNoTracking()
      join engagement in db.Engagements.AsNoTracking() on new { request.FirmId, request.EngagementId }
        equals new { engagement.FirmId, EngagementId = engagement.Id }
      join client in db.PracticeClients.AsNoTracking() on new { request.FirmId, Id = request.ClientId }
        equals new { client.FirmId, client.Id }
      where request.FirmId == actor.FirmId && engagementIds.Contains(request.EngagementId) && request.State != PbcStates.Draft &&
        engagement.PracticeClientId == request.ClientId &&
        (request.ClientOwnerUserId == actor.UserId || db.PbcRequestDelegations.Any(d => d.FirmId == request.FirmId &&
          d.ClientId == request.ClientId && d.EngagementId == request.EngagementId && d.PbcRequestId == request.Id &&
          d.DelegateUserId == actor.UserId && d.RevokedAt == null))
      select new { request.Id, request.ClientId, request.EngagementId, OwnerId = request.ClientOwnerUserId,
        request.State, request.Revision, request.Area };
    var requestRows = await eligibleRequests.OrderBy(x => x.Id).Take(MaxRequests + 1)
      .Select(x => new RequestRow(x.Id, x.ClientId, x.EngagementId, x.OwnerId, x.State, x.Revision, x.Area)).ToArrayAsync(ct);
    var requests = requestRows;
    if (requests.Length > MaxRequests) return CommandResult<ScopeState>.Fail(ErrorCodes.GateBlocked, "The client activity request scope is too large to review safely.");
    var requestIds = requests.Select(x => x.Id).ToArray();
    DelegationRow[] delegationRows = requestIds.Length == 0 ? [] : await db.PbcRequestDelegations.AsNoTracking()
      .Where(d => d.FirmId == actor.FirmId && d.DelegateUserId == actor.UserId && d.RevokedAt == null && requestIds.Contains(d.PbcRequestId))
      .OrderBy(d => d.Id).Select(d => new DelegationRow(d.Id, d.ClientId, d.EngagementId, d.PbcRequestId, d.DelegateUserId, d.CreatedAt))
      .ToArrayAsync(ct);
    var delegations = delegationRows.Where(d => requests.Any(r => r.Id == d.RequestId && r.ClientId == d.ClientId && r.EngagementId == d.EngagementId)).ToArray();

    var clientIds = grants.Select(x => x.ClientId).Distinct().Order().ToArray();
    PortalActivityContext[] clientContexts = clientIds.Length == 0 ? [] : await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id)).OrderBy(x => x.Id)
      .Select(x => new PortalActivityContext(x.LegalName, null, null, null)).ToArrayAsync(ct);
    if (clientContexts.Length != clientIds.Length) return CommandResult<ScopeState>.Fail(ErrorCodes.ScopeDenied, "Client activity unavailable.");

    var authDigest = Hash(writer =>
    {
      Add(writer, actor.FirmId); Add(writer, actor.UserId); Add(writer, actor.SessionEpoch);
      foreach (var grant in grants) { Add(writer, grant.Id); Add(writer, grant.ClientId); Add(writer, grant.EngagementId); Add(writer, grant.GrantedAt); Add(writer, grant.ExpiresAt); }
      foreach (var id in engagementIds) Add(writer, id);
      foreach (var request in requests) { Add(writer, request.Id); Add(writer, request.ClientId); Add(writer, request.EngagementId); Add(writer, request.OwnerId); }
      foreach (var delegation in delegations) { Add(writer, delegation.Id); Add(writer, delegation.ClientId); Add(writer, delegation.EngagementId); Add(writer, delegation.RequestId); Add(writer, delegation.DelegateId); Add(writer, delegation.CreatedAt); }
    });
    var contextDigest = Hash(writer =>
    {
      foreach (var context in clientContexts) Add(writer, context.ClientLegalName);
      foreach (var context in contexts) { Add(writer, context.EngagementId); Add(writer, context.ClientId); AddContext(writer, context.Context); }
      foreach (var request in requests) { Add(writer, request.Id); Add(writer, request.State); Add(writer, request.Revision); Add(writer, request.Area); }
    });
    return CommandResult<ScopeState>.Ok(new(engagementIds, requestIds, grants, requests, delegations, clientContexts, contexts, authDigest, contextDigest));
  }

  private static async Task<Candidate[]> ReadCandidatesAsync(IAuditSphereDbContext db, ActorContext actor, ScopeState scope,
    DateTimeOffset watermark, Cursor? after, CancellationToken ct)
  {
    var rows = new List<Candidate>(ReadAhead * 11);
    var engagementIds = scope.EngagementIds;

    var shared = from review in db.ClientDeliverableReviews.AsNoTracking()
      join deliverable in db.AuditDeliverables.AsNoTracking() on new { review.FirmId, review.ClientId, review.EngagementId, Id = review.DeliverableId }
        equals new { deliverable.FirmId, deliverable.ClientId, deliverable.EngagementId, deliverable.Id }
      join engagement in db.Engagements.AsNoTracking() on new { review.FirmId, review.ClientId, review.EngagementId }
        equals new { engagement.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id }
      where review.FirmId == actor.FirmId && engagementIds.Contains(review.EngagementId) && review.SharedAt <= watermark
      select new { review.Id, review.ClientId, review.EngagementId, review.SharedAt, deliverable.Kind };
    var sharedPage = ApplyAfter(shared.OrderByDescending(x => x.SharedAt).ThenBy(x => x.Id), after, "DOCUMENT_SHARED", x => x.SharedAt, x => x.Id);
    rows.AddRange(await sharedPage.Take(ReadAhead).Select(x => new Candidate(x.SharedAt, 0, "DOCUMENT_SHARED", x.Id,
      "Document shared", DeliverableKinds.Title(x.Kind), "Audit team", null, "document-review", x.Id, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

    var acknowledged = from review in db.ClientDeliverableReviews.AsNoTracking()
      join deliverable in db.AuditDeliverables.AsNoTracking() on new { review.FirmId, review.ClientId, review.EngagementId, Id = review.DeliverableId }
        equals new { deliverable.FirmId, deliverable.ClientId, deliverable.EngagementId, deliverable.Id }
      join engagement in db.Engagements.AsNoTracking() on new { review.FirmId, review.ClientId, review.EngagementId }
        equals new { engagement.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id }
      where review.FirmId == actor.FirmId && engagementIds.Contains(review.EngagementId) && review.AcknowledgedAt != null && review.AcknowledgedAt <= watermark
      select new { review.Id, review.ClientId, review.EngagementId, At = review.AcknowledgedAt!.Value, deliverable.Kind };
    var ackPage = ApplyAfter(acknowledged.OrderByDescending(x => x.At).ThenBy(x => x.Id), after, "DOCUMENT_ACKNOWLEDGED", x => x.At, x => x.Id);
    rows.AddRange(await ackPage.Take(ReadAhead).Select(x => new Candidate(x.At, 1, "DOCUMENT_ACKNOWLEDGED", x.Id,
      "Document acknowledgment recorded", DeliverableKinds.Title(x.Kind), "Client", null, "document-review", x.Id, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

    var scans = from scan in db.SignedRepresentationLetters.AsNoTracking()
      join deliverable in db.AuditDeliverables.AsNoTracking() on new { scan.FirmId, scan.ClientId, scan.EngagementId, Id = scan.DeliverableId }
        equals new { deliverable.FirmId, deliverable.ClientId, deliverable.EngagementId, deliverable.Id }
      join review in db.ClientDeliverableReviews.AsNoTracking() on new { deliverable.FirmId, deliverable.ClientId, deliverable.EngagementId, DeliverableId = deliverable.Id }
        equals new { review.FirmId, review.ClientId, review.EngagementId, review.DeliverableId }
      join engagement in db.Engagements.AsNoTracking() on new { scan.FirmId, scan.ClientId, scan.EngagementId }
        equals new { engagement.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id }
      where scan.FirmId == actor.FirmId && engagementIds.Contains(scan.EngagementId) && scan.UploadedAt <= watermark &&
        deliverable.Kind == DeliverableKinds.RepresentationLetter && scan.DeliverableSha256 == deliverable.ContentSha256 && review.SharedAt <= watermark
      select new { scan.Id, scan.ClientId, scan.EngagementId, scan.UploadedAt };
    var scanPage = ApplyAfter(scans.OrderByDescending(x => x.UploadedAt).ThenBy(x => x.Id), after, "SIGNED_SCAN_UPLOADED", x => x.UploadedAt, x => x.Id);
    rows.AddRange(await scanPage.Take(ReadAhead).Select(x => new Candidate(x.UploadedAt, 2, "SIGNED_SCAN_UPLOADED",
      x.Id, "Signed representation letter scan uploaded", "Management representation letter", "Client", null,
      "signed-scan", x.Id, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

    var bundles = from bundle in db.CommercialDeliverableBundles.AsNoTracking()
      join engagement in db.Engagements.AsNoTracking() on new { bundle.FirmId, bundle.ClientId, bundle.EngagementId }
        equals new { engagement.FirmId, ClientId = engagement.PracticeClientId, EngagementId = engagement.Id }
      where bundle.FirmId == actor.FirmId && engagementIds.Contains(bundle.EngagementId) && bundle.AssembledAt <= watermark
      select new { bundle.Id, bundle.ClientId, bundle.EngagementId, bundle.AssembledAt };
    var bundlePage = ApplyAfter(bundles.OrderByDescending(x => x.AssembledAt).ThenBy(x => x.Id), after, "BUNDLE_ASSEMBLED", x => x.AssembledAt, x => x.Id);
    rows.AddRange(await bundlePage.Take(ReadAhead).Select(x => new Candidate(x.AssembledAt, 3, "BUNDLE_ASSEMBLED", x.Id,
      "Final bundle assembled", "Final bundle", "Audit team", null, "bundle", x.Id, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

    var bundleAcks = from ack in db.ClientBundleAcknowledgements.AsNoTracking()
      join bundle in db.CommercialDeliverableBundles.AsNoTracking() on new { ack.FirmId, ack.ClientId, ack.EngagementId, Id = ack.BundleId }
        equals new { bundle.FirmId, bundle.ClientId, bundle.EngagementId, bundle.Id }
      where ack.FirmId == actor.FirmId && ack.AcknowledgedByUserId == actor.UserId && engagementIds.Contains(ack.EngagementId) &&
        ack.AcknowledgedAt <= watermark && ack.ContentSha256 == bundle.ContentSha256 && ack.ManifestSha256 == bundle.ManifestSha256
      select new { ack.Id, ack.ClientId, ack.EngagementId, ack.BundleId, ack.AcknowledgedAt };
    var bundleAckPage = ApplyAfter(bundleAcks.OrderByDescending(x => x.AcknowledgedAt).ThenBy(x => x.Id), after, "BUNDLE_ACKNOWLEDGED", x => x.AcknowledgedAt, x => x.Id);
    rows.AddRange(await bundleAckPage.Take(ReadAhead).Select(x => new Candidate(x.AcknowledgedAt, 4, "BUNDLE_ACKNOWLEDGED", x.Id,
      "Final bundle receipt acknowledged", "Final bundle", "Client", null, "bundle", x.BundleId, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

    if (scope.RequestIds.Length > 0)
    {
      var communications = from communication in db.PbcCommunications.AsNoTracking()
        join request in db.PbcRequests.AsNoTracking() on new { communication.FirmId, communication.ClientId, communication.EngagementId, Id = communication.PbcRequestId }
          equals new { request.FirmId, request.ClientId, request.EngagementId, request.Id }
        where communication.FirmId == actor.FirmId && scope.RequestIds.Contains(communication.PbcRequestId) &&
          communication.CreatedAt <= watermark && engagementIds.Contains(communication.EngagementId)
        select new { communication.Id, communication.ClientId, communication.EngagementId, communication.PbcRequestId, communication.CreatedAt, communication.Kind, request.Area };
      var communicationPage = ApplyAfter(communications.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), after, "PBC_COMMUNICATION", x => x.CreatedAt, x => x.Id);
      rows.AddRange(await communicationPage.Take(ReadAhead).Select(x => new Candidate(x.CreatedAt, 5, "PBC_COMMUNICATION", x.Id,
        "PBC update recorded", x.Area, null, x.Kind, "pbc-request", x.PbcRequestId, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

      var uploads = from upload in db.PbcUploadIntents.AsNoTracking()
        join request in db.PbcRequests.AsNoTracking() on new { upload.FirmId, upload.ClientId, upload.EngagementId, Id = upload.PbcRequestId }
          equals new { request.FirmId, request.ClientId, request.EngagementId, request.Id }
        where upload.FirmId == actor.FirmId && scope.RequestIds.Contains(upload.PbcRequestId) && upload.CreatedAt <= watermark &&
          engagementIds.Contains(upload.EngagementId)
        select new { upload.Id, upload.ClientId, upload.EngagementId, upload.PbcRequestId, upload.CreatedAt, request.Area };
      var uploadPage = ApplyAfter(uploads.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), after, "PBC_UPLOAD_STARTED", x => x.CreatedAt, x => x.Id);
      rows.AddRange(await uploadPage.Take(ReadAhead).Select(x => new Candidate(x.CreatedAt, 6, "PBC_UPLOAD_STARTED", x.Id,
        "File upload started", x.Area, null, null, "pbc-request", x.PbcRequestId, x.ClientId, x.EngagementId)).ToArrayAsync(ct));

      var acceptances = from receipt in db.PbcAcceptanceReceipts.AsNoTracking()
        join request in db.PbcRequests.AsNoTracking() on new { receipt.FirmId, receipt.ClientId, receipt.EngagementId, Id = receipt.PbcRequestId }
          equals new { request.FirmId, request.ClientId, request.EngagementId, request.Id }
        where receipt.FirmId == actor.FirmId && scope.RequestIds.Contains(receipt.PbcRequestId) && receipt.AcceptedAt <= watermark &&
          engagementIds.Contains(receipt.EngagementId)
        select new { receipt.Id, receipt.ClientId, receipt.EngagementId, receipt.PbcRequestId, receipt.AcceptedAt, request.Area };
      var acceptancePage = ApplyAfter(acceptances.OrderByDescending(x => x.AcceptedAt).ThenBy(x => x.Id), after, "PBC_ACCEPTANCE", x => x.AcceptedAt, x => x.Id);
      rows.AddRange(await acceptancePage.Take(ReadAhead).Select(x => new Candidate(x.AcceptedAt, 7, "PBC_ACCEPTANCE", x.Id,
        "PBC upload accepted", x.Area, "Audit team", null, "pbc-request", x.PbcRequestId, x.ClientId, x.EngagementId)).ToArrayAsync(ct));
    }

    var authorizedInvoices = from agreement in db.EngagementFeeAgreements.AsNoTracking()
      join milestone in db.FeeMilestones.AsNoTracking() on new { agreement.FirmId, AgreementId = agreement.Id }
        equals new { milestone.FirmId, milestone.AgreementId }
      join invoice in db.Invoices.AsNoTracking() on new { milestone.FirmId, Id = milestone.InvoiceId }
        equals new { invoice.FirmId, Id = (Guid?)invoice.Id }
      join account in db.BillingAccounts.AsNoTracking() on new { invoice.FirmId, Id = invoice.BillingAccountId }
        equals new { account.FirmId, account.Id }
      where agreement.FirmId == actor.FirmId && account.PracticeClientId == agreement.PracticeClientId &&
        (agreement.EngagementId == null || db.Engagements.Any(e => e.FirmId == agreement.FirmId && e.Id == agreement.EngagementId &&
          e.PracticeClientId == agreement.PracticeClientId)) &&
        db.RoleGrants.Any(g => g.FirmId == actor.FirmId && g.UserId == actor.UserId && g.Role == "ClientUser" &&
          g.ClientId == agreement.PracticeClientId && g.RevokedAt == null && (g.ExpiresAt == null || g.ExpiresAt > DateTimeOffset.UtcNow) &&
          (g.EngagementId == null || g.EngagementId == agreement.EngagementId))
      select new { agreement.PracticeClientId, agreement.EngagementId, Invoice = invoice };

    var posted = authorizedInvoices.Where(x => x.Invoice.PostedAt != null && x.Invoice.PostedAt <= watermark &&
      (x.Invoice.Status == BillingStates.InvoicePosted || x.Invoice.Status == BillingStates.InvoiceSent));
    var postedPage = ApplyAfter(posted.OrderByDescending(x => x.Invoice.PostedAt).ThenBy(x => x.Invoice.Id), after,
      "INVOICE_POSTED", x => x.Invoice.PostedAt!.Value, x => x.Invoice.Id);
    rows.AddRange(await postedPage.Take(ReadAhead).Select(x => new Candidate(x.Invoice.PostedAt!.Value, 8, "INVOICE_POSTED", x.Invoice.Id,
      "Invoice posted", x.Invoice.InvoiceNumber, null, null, "invoice", x.Invoice.Id, x.PracticeClientId, x.EngagementId)).ToArrayAsync(ct));

    var sent = authorizedInvoices.Where(x => x.Invoice.Status == BillingStates.InvoiceSent && x.Invoice.SentAt != null && x.Invoice.SentAt <= watermark);
    var sentPage = ApplyAfter(sent.OrderByDescending(x => x.Invoice.SentAt).ThenBy(x => x.Invoice.Id), after,
      "INVOICE_SENT", x => x.Invoice.SentAt!.Value, x => x.Invoice.Id);
    rows.AddRange(await sentPage.Take(ReadAhead).Select(x => new Candidate(x.Invoice.SentAt!.Value, 9, "INVOICE_SENT", x.Invoice.Id,
      "Invoice marked sent", x.Invoice.InvoiceNumber, null, null, "invoice", x.Invoice.Id, x.PracticeClientId, x.EngagementId)).ToArrayAsync(ct));

    var receipts = from receipt in db.Receipts.AsNoTracking()
      join account in db.BillingAccounts.AsNoTracking() on new { receipt.FirmId, Id = receipt.BillingAccountId }
        equals new { account.FirmId, account.Id }
      where receipt.FirmId == actor.FirmId && receipt.Status == BillingStates.ReceiptRecorded && receipt.ReceivedAt <= watermark &&
        db.ReceiptAllocations.Any(allocation => allocation.FirmId == actor.FirmId && allocation.ReceiptId == receipt.Id &&
          allocation.CreatedAt <= watermark && authorizedInvoices.Any(authorized =>
            authorized.Invoice.Id == allocation.InvoiceId && authorized.Invoice.BillingAccountId == receipt.BillingAccountId))
      select new { receipt.Id, receipt.ReceivedAt, ClientId = account.PracticeClientId };
    var receiptPage = ApplyAfter(receipts.OrderByDescending(x => x.ReceivedAt).ThenBy(x => x.Id), after,
      "PAYMENT_RECEIPT", x => x.ReceivedAt, x => x.Id);
    rows.AddRange(await receiptPage.Take(ReadAhead).Select(x => new Candidate(x.ReceivedAt, 10, "PAYMENT_RECEIPT", x.Id,
      "Payment receipt date recorded", "Payment receipt", null, null, "payment-receipt", x.Id, x.ClientId, null)).ToArrayAsync(ct));

    return rows.ToArray();
  }

  private static IOrderedQueryable<T> ApplyAfter<T>(IOrderedQueryable<T> query, Cursor? after, string kind,
    Expression<Func<T, DateTimeOffset>> at, Expression<Func<T, Guid>> id)
  {
    if (after is null) return query;
    var rank = Rank(kind);
    var lastRank = Rank(after.LastKind);
    var atBody = at.Body;
    var parameter = at.Parameters[0];
    var idBody = new ParameterSwap(id.Parameters[0], parameter).Visit(id.Body)!;
    var lastAt = Expression.Constant(after.LastAt);
    var older = Expression.LessThan(atBody, lastAt);
    if (rank > lastRank) older = Expression.OrElse(older, Expression.Equal(atBody, lastAt));
    else if (rank == lastRank)
    {
      var sameAt = Expression.Equal(atBody, lastAt);
      var compare = Expression.GreaterThan(Expression.Call(idBody, typeof(Guid).GetMethod(nameof(Guid.CompareTo), [typeof(Guid)])!, Expression.Constant(after.LastId)), Expression.Constant(0));
      older = Expression.OrElse(older, Expression.AndAlso(sameAt, compare));
    }
    return query.Where(Expression.Lambda<Func<T, bool>>(older, parameter)).OrderByDescending(at).ThenBy(id);
  }

  private sealed class ParameterSwap(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
  {
    protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
  }

  private static async Task<CommandResult<Dictionary<(Guid ClientId, Guid? EngagementId), PortalActivityContext>>> ContextsForCandidatesAsync(
    IAuditSphereDbContext db, Guid firmId, Candidate[] candidates, CancellationToken ct)
  {
    var clientIds = candidates.Select(x => x.ClientId).Distinct().ToArray();
    var engagementIds = candidates.Where(x => x.EngagementId != null).Select(x => x.EngagementId!.Value).Distinct().ToArray();
    ClientRow[] clients = clientIds.Length == 0 ? [] : await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == firmId && clientIds.Contains(x.Id))
      .Select(x => new ClientRow(x.Id, x.LegalName)).ToArrayAsync(ct);
    EngagementContextRow[] engagements = engagementIds.Length == 0 ? [] : await db.Engagements.AsNoTracking()
      .Where(x => x.FirmId == firmId && engagementIds.Contains(x.Id) && clientIds.Contains(x.PracticeClientId))
      .Select(x => new EngagementContextRow(x.Id, x.PracticeClientId, x.ServiceRoute, x.PeriodStart, x.PeriodEnd)).ToArrayAsync(ct);
    var clientMap = clients.ToDictionary(x => x.Id);
    var result = new Dictionary<(Guid, Guid?), PortalActivityContext>();
    foreach (var client in clients) result[(client.Id, null)] = new(client.LegalName, null, null, null);
    foreach (var engagement in engagements)
      if (clientMap.TryGetValue(engagement.ClientId, out var client))
        result[(client.Id, engagement.Id)] = new(client.LegalName, NullIfBlank(engagement.ServiceRoute),
          NullIfBlank(engagement.PeriodStart), NullIfBlank(engagement.PeriodEnd));
    return CommandResult<Dictionary<(Guid, Guid?), PortalActivityContext>>.Ok(result);
  }

  private static PortalActivityItem ToItem(Candidate row, PortalActivityContext context)
  {
    var (label, actor) = row.Kind == "PBC_COMMUNICATION" ? row.RawKind switch
    {
      PbcCommunicationKinds.Request => ("PBC request recorded", (string?)"Audit team"),
      PbcCommunicationKinds.StaffMessage => ("Audit team message recorded", (string?)"Audit team"),
      PbcCommunicationKinds.ClientMessage => ("Client message recorded", (string?)"Client"),
      PbcCommunicationKinds.ClientAction => ("Client action recorded", (string?)"Client"),
      PbcCommunicationKinds.Email => ("Email notification recorded", (string?)null),
      _ => ("PBC update recorded", (string?)null)
    } : (row.Label, row.ActorLabel);
    return new($"{row.Kind}:{row.SourceId:D}", row.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
      row.Kind, label, row.Title, actor, context, row.TargetKind, row.TargetId);
  }

  private static string EncodeCursor(Cursor cursor)
  {
    var value = string.Join('\n', "1", PageSize.ToString(CultureInfo.InvariantCulture),
      cursor.Watermark.ToString("O", CultureInfo.InvariantCulture), cursor.LastAt.ToString("O", CultureInfo.InvariantCulture),
      cursor.LastKind, cursor.LastId.ToString("D"), cursor.AuthorizationDigest, cursor.ContextDigest);
    return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
  }

  private static bool TryDecodeCursor(string value, out Cursor? cursor)
  {
    cursor = null;
    if (value.Length is 0 or > MaxCursorLength || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) return false;
    try
    {
      var base64 = value.Replace('-', '+').Replace('_', '/');
      base64 += new string('=', (4 - base64.Length % 4) % 4);
      var bytes = Convert.FromBase64String(base64);
      if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != value) return false;
      var fields = new UTF8Encoding(false, true).GetString(bytes).Split('\n');
      if (fields.Length != 8 || fields[0] != "1" || fields[1] != PageSize.ToString(CultureInfo.InvariantCulture) ||
          !TryUtc(fields[2], out var watermark) || !TryUtc(fields[3], out var lastAt) || lastAt > watermark ||
          !Ranks.ContainsKey(fields[4]) || !Guid.TryParseExact(fields[5], "D", out var id) || id == Guid.Empty ||
          id.ToString("D") != fields[5] || !IsDigest(fields[6]) || !IsDigest(fields[7])) return false;
      cursor = new(watermark, lastAt, fields[4], id, fields[6], fields[7]);
      return true;
    }
    catch (Exception ex) when (ex is FormatException or DecoderFallbackException) { return false; }
  }

  private static bool TryUtc(string value, out DateTimeOffset result)
  {
    result = default;
    return DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out result) &&
      result.Offset == TimeSpan.Zero && result.Ticks % 10 == 0 && result.ToString("O", CultureInfo.InvariantCulture) == value;
  }

  private static bool IsDigest(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static DateTimeOffset NormalizeUtc(DateTimeOffset value) => new(value.UtcTicks - value.UtcTicks % 10, TimeSpan.Zero);
  private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
  private static int Rank(string kind) => Ranks.TryGetValue(kind, out var rank) ? rank : -1;

  private static readonly IReadOnlyDictionary<string, int> Ranks = new Dictionary<string, int>(StringComparer.Ordinal)
  {
    ["DOCUMENT_SHARED"] = 0, ["DOCUMENT_ACKNOWLEDGED"] = 1, ["SIGNED_SCAN_UPLOADED"] = 2,
    ["BUNDLE_ASSEMBLED"] = 3, ["BUNDLE_ACKNOWLEDGED"] = 4, ["PBC_COMMUNICATION"] = 5,
    ["PBC_UPLOAD_STARTED"] = 6, ["PBC_ACCEPTANCE"] = 7, ["INVOICE_POSTED"] = 8,
    ["INVOICE_SENT"] = 9, ["PAYMENT_RECEIPT"] = 10
  };

  private static string Hash(Action<StringBuilder> write)
  {
    var text = new StringBuilder(); write(text);
    return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
  }
  private static void Add(StringBuilder text, string? value) { var actual = value ?? ""; text.Append(actual.Length).Append(':').Append(actual); }
  private static void Add(StringBuilder text, Guid value) => Add(text, value.ToString("D"));
  private static void Add(StringBuilder text, long value) => Add(text, value.ToString(CultureInfo.InvariantCulture));
  private static void Add(StringBuilder text, DateTimeOffset? value) => Add(text, value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
  private static void Add(StringBuilder text, Guid? value) => Add(text, value?.ToString("D"));
  private static void AddContext(StringBuilder text, PortalActivityContext context)
  { Add(text, context.ClientLegalName); Add(text, context.ServiceRoute); Add(text, context.PeriodStart); Add(text, context.PeriodEnd); }

  private static CommandResult<PortalActivityPage> Invalid() =>
    CommandResult<PortalActivityPage>.Fail("request.invalid", "The client activity page or continuation is invalid.");
  private static CommandResult<PortalActivityPage> Denied() =>
    CommandResult<PortalActivityPage>.Fail(ErrorCodes.ScopeDenied, "Client activity unavailable.");
  private static CommandResult<PortalActivityPage> Stale() =>
    CommandResult<PortalActivityPage>.Fail(ErrorCodes.GenerationStale, "The client activity context changed. Refresh from the beginning.");
  private static CommandResult<PortalActivityPage> Bounded() =>
    CommandResult<PortalActivityPage>.Fail(ErrorCodes.GateBlocked, "Some retained activity context is unavailable for a bounded review.");
}
