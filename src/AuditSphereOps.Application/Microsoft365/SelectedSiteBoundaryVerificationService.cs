using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Microsoft365;

public sealed record SelectedSiteBoundaryObservation(
  Guid DraftId, long DraftRevision, Guid ConnectionRevisionId, string TenantId,
  string SiteId, string DriveId, string RootFolderId, string SiteUrl);

public interface ISelectedSiteBoundaryProbe
{
  Task<SelectedSiteBoundaryObservation> ProbeAsync(Guid firmId, Guid draftId,
    string negativeControlSiteUrl, CancellationToken ct);
}

/// <summary>
/// Records trusted, exact-revision selected-site evidence only after both the positive
/// resource read and the synthetic unrelated-site denial succeed. A pending row revokes
/// any prior pass before network I/O, so a failed retry cannot leave an old pass usable.
/// </summary>
public sealed class SelectedSiteBoundaryVerificationService(
  IAuditSphereDbContextFactory factory, ISelectedSiteBoundaryProbe probe,
  string negativeControlSiteUrl)
{
  public async Task VerifyDraftAsync(ActorContext actor, Guid draftId, CancellationToken ct, long? expectedRevision = null)
  {
    var pendingId = Guid.CreateVersion7();
    await using (var db = await factory.CreateAsync(ct))
    {
      await AuthorizeAsync(db, actor, ct);
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == draftId && x.FirmId == actor.FirmId, ct);
      var connection = draft?.ConnectionRevisionId is { } connectionId
        ? await db.Microsoft365ConnectionRevisions.AsNoTracking()
          .SingleOrDefaultAsync(x => x.Id == connectionId && x.FirmId == actor.FirmId, ct)
        : null;
      if (draft is null || connection is null || connection.ConsentState != "VERIFIED" ||
          connection.State == Microsoft365RevisionStates.Active ||
          !string.Equals(draft.ExpectedTenantId, connection.TenantId, StringComparison.Ordinal))
        throw new OperationBlockedException("selected-resource-consent-unverified", authorization: true);
      if (expectedRevision is { } revision && draft.Revision != revision)
        throw new OperationBlockedException("selected-resource-draft-changed", authorization: true);

      db.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
      {
        Id = pendingId, FirmId = actor.FirmId, ConnectionRevisionId = connection.Id,
        TenantId = connection.TenantId, Capability = Microsoft365Capabilities.SelectedSite,
        Permission = "Sites.Selected", State = CapabilityVerificationStates.BlockedExternal,
        DiagnosticCode = "boundary-verification-in-progress", ObservedByUserId = actor.UserId,
        ObservedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync(ct);
    }

    var observed = await probe.ProbeAsync(actor.FirmId, draftId, negativeControlSiteUrl, ct);
    await using var write = await factory.CreateAsync(ct);
    await using var tx = await write.Database.BeginTransactionAsync(ct);
    await AuthorizeAsync(write, actor, ct);
    var current = await write.Microsoft365SetupDrafts.FromSqlInterpolated($"""
      SELECT * FROM m365_setup_drafts WHERE firm_id = {actor.FirmId} AND id = {draftId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    var connectionNow = await write.Microsoft365ConnectionRevisions.FromSqlInterpolated($"""
      SELECT * FROM m365_connection_revisions WHERE firm_id = {actor.FirmId} AND id = {observed.ConnectionRevisionId} FOR UPDATE
      """).SingleOrDefaultAsync(ct);
    if (current is null || connectionNow is null || current.Revision != observed.DraftRevision ||
        current.ConnectionRevisionId != connectionNow.Id || connectionNow.ConsentState != "VERIFIED" ||
        connectionNow.State == Microsoft365RevisionStates.Active ||
        !string.Equals(current.ExpectedTenantId, observed.TenantId, StringComparison.Ordinal) ||
        !string.Equals(connectionNow.TenantId, observed.TenantId, StringComparison.Ordinal) ||
        !string.Equals(current.SiteId, observed.SiteId, StringComparison.Ordinal) ||
        !string.Equals(current.DriveId, observed.DriveId, StringComparison.Ordinal) ||
        !string.Equals(current.RootFolderId, observed.RootFolderId, StringComparison.Ordinal) ||
        !string.Equals(current.SiteUrl, observed.SiteUrl, StringComparison.Ordinal))
      throw new OperationBlockedException("selected-resource-draft-changed", authorization: true);
    var consent = await write.TenantConsentAttempts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.SetupDraftId == draftId &&
        x.ExpectedTenantId == observed.TenantId && x.State == TenantConsentAttemptStates.ConsentVerified)
      .OrderByDescending(x => x.ConsentVerifiedAt).FirstOrDefaultAsync(ct);
    if (consent is null)
      throw new OperationBlockedException("selected-resource-consent-unverified", authorization: true);
    var latest = await write.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.TenantId == observed.TenantId &&
        x.Capability == Microsoft365Capabilities.SelectedSite)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (latest is null || latest.Id != pendingId || latest.ConnectionRevisionId != connectionNow.Id ||
        latest.State != CapabilityVerificationStates.BlockedExternal ||
        latest.DiagnosticCode != "boundary-verification-in-progress" ||
        latest.ObservedByUserId != actor.UserId)
      throw new OperationBlockedException("selected-resource-verification-superseded", authorization: true);

    var now = DateTimeOffset.UtcNow;
    foreach (var (kind, id) in new[]
    {
      ("SITE", observed.SiteId), ("DRIVE", observed.DriveId), ("ROOT", observed.RootFolderId)
    })
      write.IntegrationVerificationEvidences.Add(new IntegrationVerificationEvidence
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, SetupDraftId = draftId,
        ConnectionRevisionId = connectionNow.Id, ResourceKind = kind, ResourceId = id,
        Operation = "GRAPH_BOUNDARY_READ", IdentityReference = connectionNow.RuntimeCredentialReference,
        Result = "PASS", EvidenceReference = $"Exact resource read; denied synthetic control {negativeControlSiteUrl}",
        ObservedAt = now
      });
    write.TenantCapabilityVerifications.Add(new TenantCapabilityVerification
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ConnectionRevisionId = connectionNow.Id,
      ConsentAttemptId = consent.Id, TenantId = observed.TenantId,
      Capability = Microsoft365Capabilities.SelectedSite, Permission = "Sites.Selected",
      State = CapabilityVerificationStates.Verified,
      DiagnosticCode = "exact-resource-and-control-denial", ObservedByUserId = actor.UserId,
      ObservedAt = now
    });
    connectionNow.State = Microsoft365RevisionStates.Verified;
    connectionNow.VerifiedAt = now;
    current.State = Microsoft365RevisionStates.Verified;
    current.Revision++;
    current.UpdatedAt = now;
    TenantAdministration.AddEvent(write, actor, "SELECTED_RESOURCE_BOUNDARY_VERIFIED", now,
      oldState: "UNVERIFIED", newState: "VERIFIED", reason: "Administrator requested exact resource and unrelated-site denial checks",
      result: "exact-resource-and-control-denial", targetTenantId: observed.TenantId);
    await write.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
  }

  private static async Task AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct)
  {
    var result = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"],
        InternalOnly: true, RequireFirmWide: true), ct);
    if (!result.Succeeded)
      throw new UnauthorizedAccessException("Firm-wide administrator access is required.");
  }
}
