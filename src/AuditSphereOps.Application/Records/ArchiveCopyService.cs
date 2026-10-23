using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

/// <summary>Commands and readback for the secondary copy protocol; this does not change RecordsAction or Archive state.</summary>
public static partial class ArchiveCopyService
{
  private static readonly string[] CustodianRoles = ["Administrator", "Partner", "RecordsCustodian"];
  private static readonly IOperationHandler QueueOnlyHandler = new QueueOnlyArchiveCopyHandler();

  private sealed class QueueOnlyArchiveCopyHandler : IOperationHandler
  {
    public OperationDefinition Definition { get; } = new(ArchiveCopyHandler.Kind, OperationMode.LIVE,
      OperationAuthority.LIVE_PROVIDER, Group: ArchiveCopyHandler.Group);

    public string NormalizePayload(OperationRequest request) => ArchiveCopyPayloadRules.Normalize(request);

    public Task LockTargetAsync(IAuditSphereDbContext db, DurableOperation operation, CancellationToken ct) =>
      throw new InvalidOperationException("The queue-only archive descriptor cannot run in a worker.");

    public Task<OperationResult> PublishAsync(IAuditSphereDbContext db, DurableOperation operation,
      OperationResult? verifiedRemoteResult, CancellationToken ct) =>
      throw new InvalidOperationException("The queue-only archive descriptor cannot publish worker results.");

    public Task<OperationResult> ExecuteEffectAsync(DurableOperation operation, CancellationToken ct) =>
      throw new InvalidOperationException("The queue-only archive descriptor cannot execute provider effects.");

    public Task<OperationResult> ReconcileAsync(DurableOperation operation, CancellationToken ct) =>
      throw new InvalidOperationException("The queue-only archive descriptor cannot reconcile provider effects.");
  }

  public static async Task<CommandResult<ArchiveCopyTargetRevisionView>> CreateTargetRevisionAsync(
    IRecordsArchiveCopyDbContext db, ActorContext actor, CreateArchiveCopyTargetRevisionRequest request,
    CancellationToken ct = default)
  {
    if (request is null)
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail("archive-copy-target.invalid", "Select one exact client-owned archive site or verified shared library namespace.");
    var archiveDriveId = request.ArchiveLibraryDriveId?.Trim();
    var libraryRootItemId = request.LibraryRootItemId?.Trim();
    var requestedStaffGroupId = request.StaffGroupId?.Trim();
    var legacySiteBinding = request.ClientSharePointSiteId is { } clientSiteId && clientSiteId != Guid.Empty &&
      request.SelectedResourceDraftId is null && string.IsNullOrWhiteSpace(request.StaffGroupId);
    var selectedResourceBinding = request.ClientSharePointSiteId is null &&
      request.SelectedResourceDraftId is { } selectedDraftId && selectedDraftId != Guid.Empty &&
      !string.IsNullOrWhiteSpace(requestedStaffGroupId) && requestedStaffGroupId.Length <= 20;
    if (request.ClientId == Guid.Empty || legacySiteBinding == selectedResourceBinding ||
        string.IsNullOrWhiteSpace(archiveDriveId) || archiveDriveId.Length > 2000 ||
        string.IsNullOrWhiteSpace(libraryRootItemId) || libraryRootItemId.Length > 2000)
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail("archive-copy-target.invalid", "Select one exact client-owned archive site or verified shared library namespace.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'", ct);
    // Serialize target revisions per client using the existing client safety lock. A firm-wide
    // update lock would unnecessarily serialize unrelated clients' archive destinations.
    if (!await ClientSharePointSites.LockIntentScopeAsync(db, actor.FirmId, request.ClientId, ct))
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.ScopeDenied, "The firm or client is unavailable for configuration.");

    var authorization = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, request.ClientId, RequiredRoles: CustodianRoles, InternalOnly: true), ct);
    if (!authorization.Succeeded)
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail(authorization.ErrorCode!, authorization.Message!);

    ClientSharePointSite? site = null;
    Microsoft365SetupDraft? selectedDraft = null;
    Microsoft365ConnectionRevision? connection;
    string staffGroupId;
    string tenantId;
    string siteId;
    string driveId;
    string staffDigest;
    SelectedResourceProof? selectedProof = null;
    if (legacySiteBinding)
    {
      site = await db.ClientSharePointSites.FromSqlInterpolated($"""
        SELECT * FROM client_share_point_sites
        WHERE firm_id = {actor.FirmId} AND client_id = {request.ClientId} AND id = {request.ClientSharePointSiteId}
        FOR UPDATE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (site is null || site.State != "READY" || site.MembershipState != "VERIFIED" || site.VerifiedAt is null ||
          string.IsNullOrWhiteSpace(site.SiteId) || string.IsNullOrWhiteSpace(site.DriveId) ||
          string.IsNullOrWhiteSpace(site.RootItemId) || string.IsNullOrWhiteSpace(site.StaffGroupId))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "A current verified client-owned SharePoint site and staff group are required.");
      connection = await db.Microsoft365ConnectionRevisions.FromSqlInterpolated($"""
        SELECT * FROM m365_connection_revisions WHERE firm_id = {actor.FirmId} AND id = {site.ConnectionRevisionId} FOR SHARE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (connection is null || connection.State != Microsoft365RevisionStates.Active || connection.ConsentState != "VERIFIED" ||
          !string.Equals(connection.TenantId, site.TenantId, StringComparison.OrdinalIgnoreCase))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "The client-owned site must remain bound to its active verified tenant connection.");
      if (string.Equals(archiveDriveId, site.DriveId, StringComparison.Ordinal) ||
          string.Equals(libraryRootItemId, site.RootItemId, StringComparison.Ordinal))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "The secondary archive must use a separate library, not the client's working documents library.");
      staffGroupId = site.StaffGroupId;
      tenantId = site.TenantId;
      siteId = site.SiteId;
      driveId = archiveDriveId;
      var currentStaff = await ClientSharePointSites.DesiredStaffAsync(db, actor.FirmId, request.ClientId, tenantId, ct);
      var observedStaff = DeserializeObjectIds(site.MemberObjectIdsJson);
      staffDigest = ClientSharePointSites.Digest(currentStaff);
      if (currentStaff.Count == 0 || site.DesiredDigest != staffDigest || !currentStaff.SequenceEqual(observedStaff) ||
          await ClientSharePointSites.HasUnverifiedStaffAsync(db, actor.FirmId, request.ClientId, tenantId, ct))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "The current authorized client staff group is not verified against the persisted site membership.");
    }
    else
    {
      selectedDraft = await db.Microsoft365SetupDrafts.FromSqlInterpolated($"""
        SELECT * FROM m365_setup_drafts
        WHERE firm_id = {actor.FirmId} AND id = {request.SelectedResourceDraftId}
        FOR SHARE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (selectedDraft is null || selectedDraft.State != Microsoft365RevisionStates.Active ||
          selectedDraft.ConnectionRevisionId is null || selectedDraft.Revision < 1 ||
          string.IsNullOrWhiteSpace(selectedDraft.ExpectedTenantId) || string.IsNullOrWhiteSpace(selectedDraft.SiteId) ||
          string.IsNullOrWhiteSpace(selectedDraft.DriveId) || string.IsNullOrWhiteSpace(selectedDraft.RootFolderId) ||
          !IsExactSharePointSiteUrl(selectedDraft.SiteUrl))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "An active, exact selected SharePoint resource is required for a shared archive namespace.");
      connection = await db.Microsoft365ConnectionRevisions.FromSqlInterpolated($"""
        SELECT * FROM m365_connection_revisions WHERE firm_id = {actor.FirmId} AND id = {selectedDraft.ConnectionRevisionId} FOR SHARE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (connection is null || connection.State != Microsoft365RevisionStates.Active || connection.ConsentState != "VERIFIED" ||
          !string.Equals(connection.TenantId, selectedDraft.ExpectedTenantId, StringComparison.OrdinalIgnoreCase))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "The selected archive resource must remain bound to its active verified tenant connection.");
      try
      {
        selectedProof = await ArchiveCopySourceGuard.ResolveSelectedResourceProofAsync(db, actor.FirmId,
          selectedDraft, connection.Id, DateTimeOffset.UtcNow, lockRows: true, ct);
      }
      catch (OperationBlockedException)
      {
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "Fresh exact selected-resource verification evidence is required.");
      }
      if (!string.Equals(archiveDriveId, selectedDraft.DriveId, StringComparison.Ordinal) ||
          !string.Equals(libraryRootItemId, selectedDraft.RootFolderId, StringComparison.Ordinal))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "The client-owned archive binding must identify the exact verified separate library root.");
      staffGroupId = requestedStaffGroupId!;
      tenantId = connection.TenantId;
      siteId = selectedDraft.SiteId;
      driveId = selectedDraft.DriveId;
      var currentStaff = await ClientSharePointSites.DesiredStaffAsync(db, actor.FirmId, request.ClientId, tenantId, ct);
      staffDigest = ArchiveCopyPermissionEncoding.StaffDigest(currentStaff);
      if (currentStaff.Count == 0 || currentStaff.Count > 200 ||
          await ClientSharePointSites.HasUnverifiedStaffAsync(db, actor.FirmId, request.ClientId, tenantId, ct))
        return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
          "The current authorized client staff roster is unavailable or unverified.");
    }

    if (await UsesKnownWorkingDriveAsync(db, actor.FirmId, driveId, ct))
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked,
        "The secondary archive library must be separate from configured client working libraries.");

    (string Json, string Sha256) permissions;
    try { permissions = ArchiveCopyPermissionEncoding.Encode(ArchiveCopyPermissionEncoding.ClientStaffRead(staffGroupId)); }
    catch (OperationBlockedException exception)
    {
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail(exception.Code, "The client staff group identity is invalid.");
    }

    var currentRevision = await db.ArchiveCopyTargetRevisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == request.ClientId && x.TargetCode == "SECONDARY_ARCHIVE")
      .Select(x => (long?)x.Revision).MaxAsync(ct) ?? 0;
    if (currentRevision == long.MaxValue)
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail(ErrorCodes.GateBlocked, "The target revision limit has been reached.");

    ArchiveCopyTargetRevision revision;
    try
    {
      revision = selectedDraft is null
        ? ArchiveCopyTargetRevision.Create(Guid.CreateVersion7(), actor.FirmId, request.ClientId, site!.Id,
          staffGroupId, currentRevision + 1, connection!.Id, connection.TenantId, siteId, driveId,
          libraryRootItemId, permissions.Json, permissions.Sha256, staffDigest, actor.UserId, DateTimeOffset.UtcNow)
        : ArchiveCopyTargetRevision.CreateSharedSelectedResource(Guid.CreateVersion7(), actor.FirmId,
          request.ClientId, staffGroupId, currentRevision + 1, connection!.Id, connection.TenantId,
          siteId, driveId, libraryRootItemId, selectedDraft.Id, selectedDraft.Revision,
          selectedProof!.Verification.Id, selectedDraft.SiteUrl!, selectedDraft.RootFolderId!,
          selectedProof.EvidenceSha256, permissions.Json, permissions.Sha256, staffDigest,
          actor.UserId, DateTimeOffset.UtcNow);
    }
    catch (ArgumentException)
    {
      return CommandResult<ArchiveCopyTargetRevisionView>.Fail("archive-copy-target.invalid", "The target revision is incomplete.");
    }

    db.ArchiveCopyTargetRevisions.Add(revision);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<ArchiveCopyTargetRevisionView>.Ok(new(request.ClientId, revision.Id, revision.Revision,
      revision.ConfigurationSha256, revision.ExpectedPermissionsSha256));
  }

  internal static bool IsExactSharePointSiteUrl(string? value) =>
    value is { Length: > 0 and <= 2000 } && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
    uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
    uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
    uri.IdnHost.EndsWith(".sharepoint.com", StringComparison.OrdinalIgnoreCase) && uri.AbsolutePath != "/" &&
    string.Equals(uri.AbsoluteUri.TrimEnd('/'), value.TrimEnd('/'), StringComparison.Ordinal);

  internal static async Task<bool> UsesKnownWorkingDriveAsync(
    IRecordsArchiveCopyDbContext db, Guid firmId, string driveId, CancellationToken ct) =>
    await db.ClientSharePointSites.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.DriveId == driveId, ct) ||
    await db.ClientWorkspaces.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.DriveId == driveId, ct) ||
    await db.FirmWorkspaceConfigurations.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.DriveId == driveId, ct) ||
    await db.RepositoryBindings.AsNoTracking().AnyAsync(x => x.FirmId == firmId && x.DriveId == driveId, ct);

  public static Task<CommandResult<ArchiveCopyQueued>> EnqueueAsync(
    IRecordsArchiveCopyDbContext db, IOperationStore store, ActorContext actor,
    EnqueueArchiveCopyRequest request, CancellationToken ct = default) =>
    EnqueueCoreAsync(db, store, QueueOnlyHandler, actor, request, ct);

  public static Task<CommandResult<ArchiveCopyQueued>> EnqueueAsync(
    IRecordsArchiveCopyDbContext db, IOperationStore store, ArchiveCopyHandler handler,
    ActorContext actor, EnqueueArchiveCopyRequest request, CancellationToken ct = default) =>
    EnqueueCoreAsync(db, store, handler, actor, request, ct);

  private static async Task<CommandResult<ArchiveCopyQueued>> EnqueueCoreAsync(
    IRecordsArchiveCopyDbContext db, IOperationStore store, IOperationHandler handler,
    ActorContext actor, EnqueueArchiveCopyRequest request, CancellationToken ct)
  {
    if (request is null || request.ArchiveId == Guid.Empty || request.ReleaseId == Guid.Empty || request.TargetRevisionId == Guid.Empty)
      return CommandResult<ArchiveCopyQueued>.Fail("archive-copy-request.invalid", "Select an archive, issued release and target revision.");

    var initialArchive = await db.Archives.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.ArchiveId, ct);
    if (initialArchive is null) return CommandResult<ArchiveCopyQueued>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorization = await AuthorizeArchiveAsync(db, actor, initialArchive.ClientId, initialArchive.EngagementId, ct);
    if (!authorization.Succeeded)
      return CommandResult<ArchiveCopyQueued>.Fail(authorization.ErrorCode!, authorization.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (!await ClientSharePointSites.LockIntentScopeAsync(db, actor.FirmId, initialArchive.ClientId, ct))
      return CommandResult<ArchiveCopyQueued>.Fail(ErrorCodes.ScopeDenied, "Archive scope is unavailable.");
    authorization = await AuthorizeArchiveAsync(db, actor, initialArchive.ClientId, initialArchive.EngagementId, ct);
    if (!authorization.Succeeded)
      return CommandResult<ArchiveCopyQueued>.Fail(authorization.ErrorCode!, authorization.Message!);

    var archive = await db.Archives.FromSqlInterpolated($"""
      SELECT * FROM archives WHERE firm_id = {actor.FirmId} AND id = {request.ArchiveId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (archive is null || archive.ClientId != initialArchive.ClientId || archive.EngagementId != initialArchive.EngagementId)
      return CommandResult<ArchiveCopyQueued>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    ArchiveCopyOperationPayload payload;
    try
    {
      payload = await ArchiveCopySourceGuard.CapturePayloadAsync(db, archive, request.ReleaseId,
        request.TargetRevisionId, actor, ct);
      await ArchiveCopySourceGuard.LockRowsAsync(db, actor.FirmId, payload, ct);
      _ = await ArchiveCopySourceGuard.ResolveAsync(db, payload, includeArtifactBytes: false, ct);
    }
    catch (OperationBlockedException exception)
    {
      return CommandResult<ArchiveCopyQueued>.Fail(exception.Code, "The exact approved archive source or current target is unavailable.");
    }

    var idempotencyKey = ArchiveCopyPayloadRules.IdempotencyKey(payload);
    var queued = await store.EnqueueAsync(db, new OperationRequest(actor.FirmId, archive.ClientId,
      archive.EngagementId, ArchiveCopyHandler.Kind, archive.Id, payload.ManifestVersion,
      idempotencyKey, JsonSerializer.Serialize(payload), actor.UserId), handler, ct);
    if (!queued.Succeeded)
      return CommandResult<ArchiveCopyQueued>.Fail(queued.ErrorCode!, queued.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<ArchiveCopyQueued>.Ok(new(queued.Value!, idempotencyKey));
  }

  public static async Task<CommandResult<ArchiveCopyEvidenceReadback>> ReadEvidenceAsync(
    IRecordsArchiveCopyDbContext db, ActorContext actor, Guid operationId, CancellationToken ct = default)
  {
    if (operationId == Guid.Empty)
      return CommandResult<ArchiveCopyEvidenceReadback>.Fail("archive-copy-readback.invalid", "Select an exact archive-copy operation.");
    if (db.Database.CurrentTransaction is not null)
      return CommandResult<ArchiveCopyEvidenceReadback>.Fail(ErrorCodes.GateBlocked,
        "Archive-copy evidence requires an independent read snapshot.");

    ArchiveCopyOperationPayload payload;
    ArchiveCopyEvidenceReadback readback;
    await using (var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct))
    {
      var operation = await db.DurableOperations.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == operationId && x.FirmId == actor.FirmId && x.OperationKind == ArchiveCopyHandler.Kind, ct);
      if (operation is null) return CommandResult<ArchiveCopyEvidenceReadback>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      try
      {
        var request = new OperationRequest(operation.FirmId, operation.ClientId, operation.EngagementId,
          operation.OperationKind, operation.TargetId, operation.ExpectedRevision, operation.IdempotencyKey,
          operation.PayloadJson, operation.OriginatorId);
        var normalizedPayload = ArchiveCopyPayloadRules.Normalize(request);
        if (!OperationEncoding.Matches(operation, QueueOnlyHandler))
          return CommandResult<ArchiveCopyEvidenceReadback>.Fail("archive-copy-readback.invalid", "Operation evidence is unavailable.");
        payload = JsonSerializer.Deserialize<ArchiveCopyOperationPayload>(normalizedPayload)
          ?? throw new JsonException();
      }
      catch (Exception exception) when (exception is JsonException or OperationBlockedException or NotSupportedException)
      {
        return CommandResult<ArchiveCopyEvidenceReadback>.Fail("archive-copy-readback.invalid", "Operation evidence is unavailable.");
      }

      if (payload.FirmId != operation.FirmId || payload.ClientId != operation.ClientId ||
          payload.EngagementId != operation.EngagementId)
        return CommandResult<ArchiveCopyEvidenceReadback>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

      var authorization = await AuthorizeArchiveAsync(db, actor, payload.ClientId, payload.EngagementId, ct);
      if (!authorization.Succeeded)
        return CommandResult<ArchiveCopyEvidenceReadback>.Fail(authorization.ErrorCode!, authorization.Message!);

      ArchiveCopyOperationResult? result = null;
      if (operation.ResultIdentity is not null && operation.ResultDigest is not null &&
          string.Equals(operation.ResultDigest, Hashing.Sha256Hex(Encoding.UTF8.GetBytes(operation.ResultIdentity)), StringComparison.Ordinal))
      {
        try { result = JsonSerializer.Deserialize<ArchiveCopyOperationResult>(operation.ResultIdentity); }
        catch (JsonException) { }
      }
      var receipts = await db.ArchiveCopyFileReceipts.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == payload.ClientId && x.EngagementId == payload.EngagementId && x.OperationId == operation.Id)
        .OrderBy(x => x.AttemptToken).ThenBy(x => x.SourceKeySha256)
        .Select(x => new ArchiveCopyFileReceiptView(x.Id, x.AttemptToken, x.SourceKind, x.SourceKeySha256,
          x.SourceId, x.ManifestEntryId, x.SourceVersion, x.SourceSha256, x.SourceByteCount, x.RelativePath,
          x.DestinationTenantId, x.DestinationSiteId, x.DestinationDriveId, x.DestinationLibraryRootItemId,
          x.HasExactTargetBinding, x.DestinationItemId, x.DestinationVersion, x.ReadBackSha256, x.ReadBackByteCount,
          x.VerificationOutcome, x.ReadBackAt)).ToListAsync(ct);
      var permissions = await db.ArchiveCopyPermissionObservations.AsNoTracking().Where(x =>
          x.FirmId == actor.FirmId && x.ClientId == payload.ClientId && x.EngagementId == payload.EngagementId && x.OperationId == operation.Id)
        .OrderBy(x => x.AttemptToken)
        .Select(x => new ArchiveCopyPermissionObservationView(x.Id, x.AttemptToken, x.ExpectedPermissionsJson,
          x.ExpectedPermissionsSha256, x.ObservedPermissionsJson, x.ObservedPermissionsSha256,
          x.HasUniquePermissionScope, x.HasExactTargetBinding, x.ObservedStaffSha256, x.ObservedTenantId, x.ObservedSiteId,
          x.ObservedDriveId, x.ObservedLibraryRootItemId, x.ComparisonOutcome, x.ObservedAt)).ToListAsync(ct);
      readback = new(operation.Id, payload.FirmId, payload.ClientId, payload.EngagementId, operation.Status.ToString(), operation.ResultDigest, result, receipts, permissions);
      await snapshot.CommitAsync(ct);
    }

    var currentArchive = await db.Archives.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.Id == payload.ArchiveId)
      .Select(x => new { x.ClientId, x.EngagementId }).SingleOrDefaultAsync(ct);
    if (currentArchive is null || currentArchive.ClientId != payload.ClientId || currentArchive.EngagementId != payload.EngagementId)
      return CommandResult<ArchiveCopyEvidenceReadback>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var currentAuthorization = await AuthorizeArchiveAsync(db, actor, payload.ClientId, payload.EngagementId, ct);
    if (!currentAuthorization.Succeeded)
      return CommandResult<ArchiveCopyEvidenceReadback>.Fail(currentAuthorization.ErrorCode!, currentAuthorization.Message!);
    return CommandResult<ArchiveCopyEvidenceReadback>.Ok(readback);
  }

  private static Task<CommandResult> AuthorizeArchiveAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, clientId, engagementId,
      RequiredRoles: CustodianRoles, InternalOnly: true), ct);

  internal static string[] DeserializeObjectIds(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return [];
    try
    {
      var ids = JsonSerializer.Deserialize<string[]>(value) ?? [];
      return ids.Where(x => Guid.TryParse(x, out _)).Select(x => Guid.Parse(x!).ToString("D"))
        .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
    catch (Exception exception) when (exception is JsonException or ArgumentNullException) { return []; }
  }
}

internal sealed record ArchiveCopyDocumentSnapshotSource(
  ArchiveManifestEntry Entry, DocumentSnapshot Snapshot, RepositoryBinding Binding);

internal sealed record ArchiveCopyPersistedByteSource(string Version, byte[] Content);

internal sealed record ArchiveCopySourceSet(
  Archive Archive,
  RecordsProfile Profile,
  ArchiveManifest Manifest,
  IReadOnlyList<ArchiveManifestEntry> Entries,
  ArchiveStructuredExport StructuredExport,
  ReleaseCandidate Candidate,
  Release Release,
  ReleaseCheckpoint Checkpoint,
  FinancialPackageArtifact Artifact,
  ClientSharePointSite? ClientSite,
  ArchiveCopyTargetRevision Target,
  IReadOnlyDictionary<Guid, ArchiveCopyDocumentSnapshotSource> DocumentSnapshots,
  IReadOnlyDictionary<Guid, ArchiveCopyPersistedByteSource> PersistedByteSources,
  byte[]? ReleasedArtifactBytes);

internal sealed record SelectedResourceProof(
  TenantCapabilityVerification Verification,
  IReadOnlyList<IntegrationVerificationEvidence> Evidence,
  string EvidenceSha256);

internal static class ArchiveCopySourceGuard
{
  private static readonly string[] CustodianRoles = ["Administrator", "Partner", "RecordsCustodian"];

  public static async Task<SelectedResourceProof> ResolveSelectedResourceProofAsync(
    IRecordsArchiveCopyDbContext db, Guid firmId, Microsoft365SetupDraft draft, Guid connectionRevisionId,
    DateTimeOffset now, bool lockRows, CancellationToken ct)
  {
    if (lockRows && db.Database.CurrentTransaction is null)
      throw new InvalidOperationException("Selected-resource proof locks require an operation transaction.");
    if (draft.FirmId != firmId || draft.State != Microsoft365RevisionStates.Active ||
        draft.ConnectionRevisionId != connectionRevisionId || draft.Revision < 1 ||
        string.IsNullOrWhiteSpace(draft.ExpectedTenantId) || string.IsNullOrWhiteSpace(draft.SiteId) ||
        string.IsNullOrWhiteSpace(draft.DriveId) || string.IsNullOrWhiteSpace(draft.RootFolderId) ||
        !ArchiveCopyService.IsExactSharePointSiteUrl(draft.SiteUrl))
      throw new OperationBlockedException("archive-copy-selected-resource-invalid", authorization: true);

    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == connectionRevisionId, ct);
    if (connection is null || connection.State != Microsoft365RevisionStates.Active || connection.ConsentState != "VERIFIED" ||
        !string.Equals(connection.TenantId, draft.ExpectedTenantId, StringComparison.Ordinal))
      throw new OperationBlockedException("archive-copy-selected-resource-connection-stale", authorization: true);

    var latest = await db.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.TenantId == connection.TenantId &&
        x.Capability == Microsoft365Capabilities.SelectedSite)
      .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (latest is null || latest.ConnectionRevisionId != connectionRevisionId ||
        latest.Permission != "Sites.Selected" || latest.State != CapabilityVerificationStates.Verified ||
        latest.DiagnosticCode != "exact-resource-and-control-denial" || latest.ObservedAt > now ||
        latest.ObservedAt < now.AddHours(-24) || latest.ConsentAttemptId is not Guid consentAttemptId)
      throw new OperationBlockedException("archive-copy-selected-resource-proof-stale", authorization: true);

    var consent = await db.TenantConsentAttempts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == firmId && x.Id == consentAttemptId && x.SetupDraftId == draft.Id &&
      x.ExpectedTenantId == connection.TenantId && x.State == TenantConsentAttemptStates.ConsentVerified, ct);
    if (consent is null)
      throw new OperationBlockedException("archive-copy-selected-resource-consent-stale", authorization: true);

    var resourceIds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
      ["SITE"] = draft.SiteId,
      ["DRIVE"] = draft.DriveId,
      ["ROOT"] = draft.RootFolderId
    };
    var proofEvidence = new List<IntegrationVerificationEvidence>(resourceIds.Count);
    foreach (var (kind, resourceId) in resourceIds.OrderBy(x => x.Key, StringComparer.Ordinal))
    {
      IntegrationVerificationEvidence? evidence;
      if (lockRows)
      {
        evidence = await db.IntegrationVerificationEvidences.FromSqlInterpolated($"""
          SELECT * FROM m365_verification_evidence
          WHERE firm_id = {firmId} AND setup_draft_id = {draft.Id} AND resource_kind = {kind}
          ORDER BY observed_at DESC, id DESC LIMIT 1 FOR SHARE
          """).AsNoTracking().SingleOrDefaultAsync(ct);
      }
      else
      {
        evidence = await db.IntegrationVerificationEvidences.AsNoTracking().Where(x =>
            x.FirmId == firmId && x.SetupDraftId == draft.Id && x.ResourceKind == kind)
          .OrderByDescending(x => x.ObservedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
      }
      if (evidence is null || evidence.ConnectionRevisionId != connectionRevisionId ||
          evidence.ResourceId != resourceId || evidence.Operation != "GRAPH_BOUNDARY_READ" ||
          evidence.IdentityReference != connection.RuntimeCredentialReference || evidence.Result != "PASS" ||
          evidence.ObservedAt != latest.ObservedAt)
        throw new OperationBlockedException("archive-copy-selected-resource-evidence-stale", authorization: true);
      proofEvidence.Add(evidence);
    }

    if (lockRows)
    {
      var locked = await db.TenantCapabilityVerifications.FromSqlInterpolated($"""
        SELECT * FROM m365_tenant_capability_verifications WHERE firm_id = {firmId} AND id = {latest.Id} FOR SHARE
        """).AsNoTracking().SingleOrDefaultAsync(ct);
      if (locked is null) throw new OperationBlockedException("archive-copy-selected-resource-proof-stale", authorization: true);
    }

    var evidenceHash = Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
    {
      verification = new
      {
        latest.Id, latest.FirmId, latest.ConnectionRevisionId, latest.ConsentAttemptId, latest.TenantId,
        latest.Capability, latest.Permission, latest.State, latest.DiagnosticCode, latest.ObservedByUserId,
        observedAt = latest.ObservedAt.ToUniversalTime()
      },
      evidence = proofEvidence.Select(x => new
      {
        x.Id, x.FirmId, x.SetupDraftId, x.ConnectionRevisionId, x.ResourceKind, x.ResourceId,
        x.Operation, x.IdentityReference, x.Result, x.EvidenceReference,
        observedAt = x.ObservedAt.ToUniversalTime()
      })
    }));
    return new SelectedResourceProof(latest, proofEvidence, evidenceHash);
  }

  public static async Task<ArchiveCopyOperationPayload> CapturePayloadAsync(
    IRecordsArchiveCopyDbContext db, Archive archive, Guid releaseId, Guid targetRevisionId,
    ActorContext actor, CancellationToken ct)
  {
    var profile = await db.RecordsProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == archive.FirmId &&
      x.ProfileCode == archive.ProfileId && x.Version == archive.ProfileVersion, ct);
    var manifest = await db.ArchiveManifests.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ArchiveId == archive.Id)
      .OrderByDescending(x => x.Version).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == archive.FirmId &&
      x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.Id == releaseId, ct);
    var candidate = release is null ? null : await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.Id == release.ReleaseCandidateId, ct);
    var checkpoint = release is null ? null : await db.ReleaseCheckpoints.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.Id == release.CheckpointId, ct);
    var artifact = release?.PinnedArtifactId is Guid artifactId
      ? await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == archive.FirmId &&
        x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.Id == artifactId, ct)
      : null;
    var target = await db.ArchiveCopyTargetRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.Id == targetRevisionId, ct);

    if (profile is null || manifest is null || release is null || candidate is null || checkpoint is null || artifact is null || target is null ||
        profile.ApprovedByUserId is null || profile.ApprovedAt is null || manifest.ReviewedByUserId is null || manifest.ReviewedAt is null ||
        checkpoint.VerifiedAt is null || release.PinnedArtifactId is null || release.PinnedArtifactVersion is null ||
        release.PinnedArtifactSha256Hex is null)
      throw new OperationBlockedException("archive-copy-source-incomplete");

    return new ArchiveCopyOperationPayload(archive.FirmId, archive.Id, archive.ClientId, archive.EngagementId,
      manifest.Id, manifest.Version, manifest.EntryCount, manifest.ManifestDigest,
      manifest.ReviewedByUserId.Value, manifest.ReviewedAt.Value.ToUniversalTime(),
      profile.Id, profile.ProfileCode, profile.Version, profile.ApprovedByUserId.Value,
      profile.ApprovedAt.Value.ToUniversalTime(), ProfileSha256(profile),
      release.Id, candidate.Id, candidate.Revision, release.ManifestDigest,
      checkpoint.Id, checkpoint.ManifestDigest, checkpoint.ReadBackDigest, checkpoint.VerifiedAt.Value.ToUniversalTime(),
      artifact.Id, artifact.ArtifactVersion, artifact.ArtifactSha256Hex, artifact.ArtifactBytes.LongLength,
      release.ReleasedAt.ToUniversalTime(), release.ReleasedByUserId, target.Id, target.ClientSharePointSiteId,
      target.Revision, target.ConfigurationSha256, target.ExpectedPermissionsSha256, target.AuthorizedStaffSha256,
      actor.UserId, actor.SessionEpoch);
  }

  public static async Task LockRowsAsync(IRecordsArchiveCopyDbContext db, Guid firmId,
    ArchiveCopyOperationPayload payload, CancellationToken ct)
  {
    if (firmId != payload.FirmId)
      throw new OperationBlockedException("archive-copy-source-scope-mismatch", authorization: true);
    if (db.Database.CurrentTransaction is null)
      throw new InvalidOperationException("Archive-copy source locks require the operation transaction.");
    await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'; SET LOCAL statement_timeout = '15s'", ct);
    _ = await db.Archives.FromSqlInterpolated($"SELECT * FROM archives WHERE firm_id = {firmId} AND id = {payload.ArchiveId} FOR UPDATE")
      .AsNoTracking().ToListAsync(ct);
    if (payload.ClientSharePointSiteId is Guid clientSiteId)
      _ = await db.ClientSharePointSites.FromSqlInterpolated($"SELECT * FROM client_share_point_sites WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND id = {clientSiteId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    _ = await db.RecordsProfiles.FromSqlInterpolated($"SELECT * FROM records_profiles WHERE firm_id = {firmId} AND id = {payload.RecordsProfileId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.ArchiveManifests.FromSqlInterpolated($"SELECT * FROM archive_manifests WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {payload.ManifestId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.ArchiveManifestEntries.FromSqlInterpolated($"SELECT * FROM archive_manifest_entries WHERE firm_id = {firmId} AND archive_manifest_id = {payload.ManifestId} ORDER BY ordinal FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.ArchiveStructuredExports.FromSqlInterpolated($"SELECT * FROM archive_structured_exports WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND archive_manifest_id = {payload.ManifestId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.ReleaseCandidates.FromSqlInterpolated($"SELECT * FROM release_candidates WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {payload.ReleaseCandidateId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.Releases.FromSqlInterpolated($"SELECT * FROM releases WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {payload.ReleaseId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.ReleaseCheckpoints.FromSqlInterpolated($"SELECT * FROM release_checkpoints WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {payload.CheckpointId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    _ = await db.FinancialPackageArtifacts.FromSqlInterpolated($"SELECT * FROM financial_package_artifacts WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {payload.ReleasedArtifactId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    var bundleIds = await db.ArchiveManifestEntries.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ArchiveManifestId == payload.ManifestId && x.EntryKind == "COMMERCIAL_BUNDLE_ASSEMBLY" &&
        x.SourceKind == "CommercialDeliverableBundle" && x.SourceId != null)
      .Select(x => x.SourceId!.Value).OrderBy(x => x).ToListAsync(ct);
    foreach (var bundleId in bundleIds)
      _ = await db.CommercialDeliverableBundles.FromSqlInterpolated($"SELECT * FROM commercial_deliverable_bundles WHERE firm_id = {firmId} AND id = {bundleId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    var bundles = bundleIds.Count == 0 ? new List<CommercialDeliverableBundle>() : await db.CommercialDeliverableBundles.AsNoTracking()
      .Where(x => x.FirmId == firmId && bundleIds.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(ct);
    foreach (var invoiceId in bundles.Select(x => x.BalanceInvoiceId).Distinct().OrderBy(x => x))
    {
      _ = await db.Invoices.FromSqlInterpolated($"SELECT * FROM invoices WHERE firm_id = {firmId} AND id = {invoiceId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
      _ = await db.FeeMilestones.FromSqlInterpolated($"SELECT * FROM fee_milestones WHERE firm_id = {firmId} AND invoice_id = {invoiceId} ORDER BY id FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    }
    var compositionIds = bundles.Where(x => x.FinancialStatementCompositionId.HasValue)
      .Select(x => x.FinancialStatementCompositionId!.Value).Distinct().OrderBy(x => x).ToArray();
    foreach (var compositionId in compositionIds)
      _ = await db.FinancialStatementCompositions.FromSqlInterpolated($"SELECT * FROM financial_statement_compositions WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {compositionId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    var compositions = compositionIds.Length == 0 ? new List<FinancialStatementComposition>() :
      await db.FinancialStatementCompositions.AsNoTracking().Where(x => x.FirmId == firmId && compositionIds.Contains(x.Id))
        .OrderBy(x => x.Id).ToListAsync(ct);
    var bundleArtifactIds = bundles.Select(x => x.FinancialPackageArtifactId)
      .Concat(compositions.SelectMany(x => new[] { x.PdfArtifactId, x.WordArtifactId, x.ExcelArtifactId }))
      .Distinct().OrderBy(x => x).ToArray();
    foreach (var artifactId in bundleArtifactIds)
      _ = await db.FinancialPackageArtifacts.FromSqlInterpolated($"SELECT * FROM financial_package_artifacts WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {artifactId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    var signedRepresentationIds = bundles.Select(x => x.SignedRepresentationLetterId).Distinct().OrderBy(x => x).ToArray();
    var signedRepresentations = signedRepresentationIds.Length == 0 ? new List<SignedRepresentationLetter>() :
      await db.SignedRepresentationLetters.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == payload.ClientId && x.EngagementId == payload.EngagementId && signedRepresentationIds.Contains(x.Id))
        .OrderBy(x => x.Id).ToListAsync(ct);
    var deliverableIds = bundles.SelectMany(x => new[] { x.SignedReportId, x.ManagementLetterId })
      .Concat(signedRepresentations.Select(x => x.DeliverableId)).Distinct().OrderBy(x => x).ToArray();
    foreach (var deliverableId in deliverableIds)
      _ = await db.AuditDeliverables.FromSqlInterpolated($"SELECT * FROM audit_deliverables WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {deliverableId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    var signedReportIds = bundles.Select(x => x.SignedReportId).Distinct().OrderBy(x => x).ToArray();
    foreach (var signedReportId in signedReportIds)
    {
      var unsignedId = await db.AuditDeliverables.AsNoTracking().Where(x => x.FirmId == firmId &&
        x.ClientId == payload.ClientId && x.EngagementId == payload.EngagementId && x.Id == signedReportId)
        .Select(x => x.SignedFromDeliverableId).SingleOrDefaultAsync(ct);
      if (unsignedId.HasValue)
        _ = await db.AuditDeliverables.FromSqlInterpolated($"SELECT * FROM audit_deliverables WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {unsignedId.Value} FOR SHARE")
          .AsNoTracking().ToListAsync(ct);
      _ = await db.SignatureApplications.FromSqlInterpolated($"SELECT * FROM signature_applications WHERE firm_id = {firmId} AND engagement_id = {payload.EngagementId} AND signed_deliverable_id = {signedReportId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    }
    foreach (var signedRepresentationId in signedRepresentationIds)
    {
      _ = await db.SignedRepresentationLetters.FromSqlInterpolated($"SELECT * FROM signed_representation_letters WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND id = {signedRepresentationId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
      _ = await db.RepresentationLetterVerifications.FromSqlInterpolated($"SELECT * FROM representation_letter_verifications WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND engagement_id = {payload.EngagementId} AND signed_letter_id = {signedRepresentationId} FOR SHARE")
        .AsNoTracking().ToListAsync(ct);
    }
    var lockedTargets = await db.ArchiveCopyTargetRevisions.FromSqlInterpolated($"SELECT * FROM archive_copy_target_revisions WHERE firm_id = {firmId} AND client_id = {payload.ClientId} AND id = {payload.TargetRevisionId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    if (lockedTargets.Count != 1 || lockedTargets[0].ClientSharePointSiteId != payload.ClientSharePointSiteId)
      throw new OperationBlockedException("archive-copy-target-client-scope-mismatch", authorization: true);
    _ = await db.Microsoft365ConnectionRevisions.FromSqlInterpolated($"SELECT * FROM m365_connection_revisions WHERE firm_id = {firmId} AND id = {lockedTargets[0].ConnectionRevisionId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    if (lockedTargets[0].UsesSharedSelectedResource)
    {
      if (lockedTargets[0].SelectedResourceDraftId is not Guid draftId)
        throw new OperationBlockedException("archive-copy-selected-resource-invalid", authorization: true);
      var draft = await db.Microsoft365SetupDrafts.FromSqlInterpolated($"SELECT * FROM m365_setup_drafts WHERE firm_id = {firmId} AND id = {draftId} FOR SHARE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (draft is null)
        throw new OperationBlockedException("archive-copy-selected-resource-invalid", authorization: true);
      _ = await ArchiveCopySourceGuard.ResolveSelectedResourceProofAsync(db, firmId, draft,
        lockedTargets[0].ConnectionRevisionId, DateTimeOffset.UtcNow, lockRows: true, ct);
    }
  }

  public static async Task<ArchiveCopySourceSet> ResolveAsync(
    IRecordsArchiveCopyDbContext db, ArchiveCopyOperationPayload payload, bool includeArtifactBytes, CancellationToken ct)
  {
    var archive = await db.Archives.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == payload.FirmId && x.Id == payload.ArchiveId &&
      x.ClientId == payload.ClientId && x.EngagementId == payload.EngagementId, ct);
    var profile = await db.RecordsProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == payload.RecordsProfileId && x.FirmId == payload.FirmId &&
      x.ProfileCode == payload.RecordsProfileCode && x.Version == payload.RecordsProfileVersion, ct);
    if (archive is null || profile is null || archive.FirmId != profile.FirmId)
      throw new OperationBlockedException("archive-copy-source-scope-mismatch", authorization: true);

    var manifest = await db.ArchiveManifests.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == archive.FirmId &&
      x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.ArchiveId == archive.Id && x.Id == payload.ManifestId, ct);
    var latestManifest = await db.ArchiveManifests.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ArchiveId == archive.Id)
      .OrderByDescending(x => x.Version).ThenByDescending(x => x.Id).Select(x => x.Id).FirstOrDefaultAsync(ct);
    var entries = await db.ArchiveManifestEntries.AsNoTracking().Where(x => x.FirmId == archive.FirmId &&
      x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.ArchiveManifestId == payload.ManifestId)
      .OrderBy(x => x.Ordinal).ToListAsync(ct);
    if (manifest is null || latestManifest != manifest.Id || manifest.Status != "REVIEWED" ||
        manifest.CompletenessStatus != "COMPLETE" || manifest.SupersededByManifestId is not null ||
        manifest.ReviewedByUserId != payload.ManifestReviewedByUserId || manifest.ReviewedAt?.ToUniversalTime() != payload.ManifestReviewedAt.ToUniversalTime() ||
        manifest.Version != payload.ManifestVersion || manifest.EntryCount != payload.ManifestEntryCount || entries.Count != manifest.EntryCount ||
        entries.Any(x => !x.Required) ||
        !string.Equals(manifest.ManifestDigest, payload.ManifestSha256, StringComparison.Ordinal) ||
        !string.Equals(ManifestDigest(entries), manifest.ManifestDigest, StringComparison.Ordinal))
      throw new OperationBlockedException("archive-copy-manifest-stale");

    if (archive.Status is not (ArchiveStates.AssemblyReviewed or ArchiveStates.RecordsActionRequested or
        ArchiveStates.ProtectionObserved or ArchiveStates.ArchiveVerified) ||
        archive.ProfileId != profile.ProfileCode || archive.ProfileVersion != profile.Version || !profile.Approved ||
        profile.ApprovedByUserId != payload.ProfileApprovedByUserId || profile.ApprovedAt?.ToUniversalTime() != payload.ProfileApprovedAt.ToUniversalTime() ||
        !string.Equals(ProfileSha256(profile), payload.ProfileSha256, StringComparison.Ordinal))
      throw new OperationBlockedException("archive-copy-profile-or-archive-stale");

    var exportEntry = entries.Where(x => x.EntryKind == "STRUCTURED_EXPORT" && x.SourceKind == "AuditSphereOps").ToArray();
    if (exportEntry.Length != 1 || exportEntry[0].SourceId is not null ||
        exportEntry[0].RelativeName != "structured/records-export.v1.json" ||
        exportEntry[0].MetadataJson != "{\"schema\":\"records-export.v1\",\"storage\":\"archive_structured_exports\"}")
      throw new OperationBlockedException("archive-copy-structured-export-entry-invalid");
    var structured = await db.ArchiveStructuredExports.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.ArchiveId == archive.Id && x.ArchiveManifestId == manifest.Id && x.Schema == "records-export.v1", ct);
    if (structured is null || structured.ByteCount != Encoding.UTF8.GetByteCount(structured.PayloadJson) ||
        structured.ContentHash != Hashing.Sha256Hex(Encoding.UTF8.GetBytes(structured.PayloadJson)) ||
        exportEntry[0].ContentHash != structured.ContentHash || exportEntry[0].ByteCount != structured.ByteCount)
      throw new OperationBlockedException("archive-copy-structured-export-mismatch");

    var release = await db.Releases.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == archive.FirmId &&
      x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.Id == payload.ReleaseId, ct);
    var candidate = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.Id == payload.ReleaseCandidateId, ct);
    var checkpoint = await db.ReleaseCheckpoints.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.Id == payload.CheckpointId, ct);
    if (release is null || candidate is null || checkpoint is null || release.Id != payload.ReleaseId ||
        release.ReleaseCandidateId != candidate.Id || candidate.Revision != payload.ReleaseCandidateRevision ||
        candidate.Status != ReleaseStates.Issued || candidate.TargetKind != ReleaseTargetKinds.FinancialPackage ||
        candidate.TargetId != release.PackageId || candidate.TargetRevision != release.PackageRevision ||
        release.ManifestDigest != candidate.ManifestDigest || release.ManifestDigest != payload.ReleaseManifestSha256 ||
        release.CheckpointId != checkpoint.Id || checkpoint.ReleaseCandidateId != candidate.Id ||
        checkpoint.CandidateRevision != candidate.Revision || checkpoint.AuthorizedReleaseKey != release.AuthorizedReleaseKey ||
        checkpoint.ManifestDigest != candidate.ManifestDigest || checkpoint.ReadBackDigest != checkpoint.ManifestDigest ||
        checkpoint.VerifiedStatus != "VERIFIED" || checkpoint.VerifiedAt is null ||
        checkpoint.VerifiedAt.Value.ToUniversalTime() != payload.CheckpointVerifiedAt.ToUniversalTime() ||
        checkpoint.ManifestDigest != payload.CheckpointManifestSha256 || checkpoint.ReadBackDigest != payload.CheckpointReadBackSha256 ||
        release.ReleasedAt.ToUniversalTime() != payload.ReleaseReleasedAt.ToUniversalTime() ||
        release.ReleasedByUserId != payload.ReleaseReleasedByUserId ||
        release.PinnedArtifactId != payload.ReleasedArtifactId || release.PinnedArtifactVersion is null ||
        release.PinnedArtifactSha256Hex is null || release.PinnedArtifactVersion != payload.ReleasedArtifactVersion ||
        release.PinnedArtifactSha256Hex != payload.ReleasedArtifactSha256)
      throw new OperationBlockedException("archive-copy-release-or-checkpoint-stale");

    var artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
      x.Id == payload.ReleasedArtifactId && x.FinancialPackageId == release.PackageId &&
      x.PackageRevision == release.PackageRevision && x.ArtifactVersion == payload.ReleasedArtifactVersion &&
      x.ArtifactSha256Hex == payload.ReleasedArtifactSha256, ct);
    if (artifact is null || artifact.ArtifactBytes.LongLength != payload.ReleasedArtifactByteCount ||
        Hashing.Sha256Hex(artifact.ArtifactBytes) != artifact.ArtifactSha256Hex)
      throw new OperationBlockedException("archive-copy-released-artifact-mismatch");

    var bundleEntry = entries.Where(x => x.EntryKind == "COMMERCIAL_BUNDLE_ASSEMBLY" &&
      x.SourceKind == "CommercialDeliverableBundle" && x.SourceId.HasValue).ToArray();
    if (bundleEntry.Length != 1)
      throw new OperationBlockedException("archive-copy-commercial-bundle-required");
    var bundleEvidence = await ArchiveCommercialBundleEvidence.ResolveAsync(db, archive.FirmId, archive.ClientId,
      archive.EngagementId, bundleEntry[0].SourceId!.Value, release.Id, ct);
    var expectedBundleSources = bundleEvidence.CreateSources();
    var actualBundleEntries = entries.Where(x => x.EntryKind is "COMMERCIAL_BUNDLE_ASSEMBLY" or
      "COMMERCIAL_BUNDLE_MANIFEST" or "COMMERCIAL_BUNDLE_PART" or "SIGNED_AUDIT_REPORT" or
      "SIGNED_MANAGEMENT_REPRESENTATION").OrderBy(x => x.Ordinal).ToArray();
    if (actualBundleEntries.Length != expectedBundleSources.Count)
      throw new OperationBlockedException("archive-copy-commercial-bundle-entry-set-incomplete");
    var persistedByteSources = new Dictionary<Guid, ArchiveCopyPersistedByteSource>();
    for (var i = 0; i < expectedBundleSources.Count; i++)
    {
      var expected = expectedBundleSources[i];
      var actual = actualBundleEntries[i];
      if (actual.EntryKind != expected.EntryKind || actual.SourceKind != expected.SourceKind ||
          actual.SourceId != expected.SourceId || actual.RelativeName != expected.RelativeName ||
          actual.ContentHash != expected.ContentHash || actual.ByteCount != expected.ByteCount ||
          actual.MetadataJson != expected.MetadataJson || !actual.Required ||
          Hashing.Sha256Hex(expected.Content) != actual.ContentHash || expected.Content.LongLength != actual.ByteCount)
        throw new OperationBlockedException("archive-copy-commercial-bundle-entry-stale");
      persistedByteSources.Add(actual.Id, new(expected.SourceVersion, expected.Content));
    }

    var target = await db.ArchiveCopyTargetRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.Id == payload.TargetRevisionId &&
      x.ClientSharePointSiteId == payload.ClientSharePointSiteId && x.TargetCode == "SECONDARY_ARCHIVE", ct);
    EnsureTargetClientBinding(archive, target, payload.ClientSharePointSiteId);
    var latestTarget = await db.ArchiveCopyTargetRevisions.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.TargetCode == "SECONDARY_ARCHIVE").OrderByDescending(x => x.Revision)
      .Select(x => x.Id).FirstOrDefaultAsync(ct);
    if (target is null || latestTarget != target.Id || target.Revision != payload.TargetRevision ||
        target.ConfigurationSha256 != payload.TargetConfigurationSha256 ||
        target.ExpectedPermissionsSha256 != payload.ExpectedPermissionsSha256 || !target.HasValidDigests())
      throw new OperationBlockedException("archive-copy-target-configuration-stale", authorization: true);
    if (await ArchiveCopyService.UsesKnownWorkingDriveAsync(db, archive.FirmId, target.DriveId, ct))
      throw new OperationBlockedException("archive-copy-target-overlaps-working-library", authorization: true);

    ClientSharePointSite? clientSite = null;
    string authorizedTenantId;
    if (target.ClientSharePointSiteId is Guid siteId)
    {
      clientSite = await db.ClientSharePointSites.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.Id == siteId, ct);
      if (clientSite is null || clientSite.Id != payload.ClientSharePointSiteId || clientSite.State != "READY" ||
          clientSite.MembershipState != "VERIFIED" || clientSite.VerifiedAt is null ||
          string.IsNullOrWhiteSpace(clientSite.SiteId) || string.IsNullOrWhiteSpace(clientSite.DriveId) ||
          string.IsNullOrWhiteSpace(clientSite.RootItemId) || string.IsNullOrWhiteSpace(clientSite.StaffGroupId) ||
          string.IsNullOrWhiteSpace(clientSite.OwnershipMarker) ||
          target.ConnectionRevisionId != clientSite.ConnectionRevisionId || target.SiteId != clientSite.SiteId ||
          target.TenantId != clientSite.TenantId || target.StaffGroupId != clientSite.StaffGroupId ||
          string.Equals(target.DriveId, clientSite.DriveId, StringComparison.Ordinal) ||
          string.Equals(target.LibraryRootItemId, clientSite.RootItemId, StringComparison.Ordinal))
        throw new OperationBlockedException("archive-copy-client-site-or-library-stale", authorization: true);
      authorizedTenantId = clientSite.TenantId;
    }
    else
    {
      if (payload.ClientSharePointSiteId is not null || target.SelectedResourceDraftId is not Guid selectedDraftId ||
          target.SelectedResourceDraftRevision is not long selectedDraftRevision ||
          target.SelectedResourceVerificationId is not Guid selectedVerificationId ||
          target.SelectedResourceSiteUrl is not { } selectedSiteUrl ||
          target.SelectedResourceRootFolderId is not { } selectedRoot ||
          target.SelectedResourceEvidenceSha256 is not { } selectedEvidenceSha256)
        throw new OperationBlockedException("archive-copy-selected-resource-invalid", authorization: true);
      var selectedDraft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == archive.FirmId && x.Id == selectedDraftId, ct);
      if (selectedDraft is null || selectedDraft.State != Microsoft365RevisionStates.Active ||
          selectedDraft.Revision != selectedDraftRevision || selectedDraft.ConnectionRevisionId != target.ConnectionRevisionId ||
          selectedDraft.SiteId != target.SiteId || selectedDraft.DriveId != target.DriveId ||
          selectedDraft.RootFolderId != selectedRoot || selectedDraft.SiteUrl != selectedSiteUrl ||
          target.LibraryRootItemId != selectedRoot)
        throw new OperationBlockedException("archive-copy-selected-resource-stale", authorization: true);
      var proof = await ResolveSelectedResourceProofAsync(db, archive.FirmId, selectedDraft,
        target.ConnectionRevisionId, DateTimeOffset.UtcNow, lockRows: false, ct);
      if (proof.Verification.Id != selectedVerificationId || proof.EvidenceSha256 != selectedEvidenceSha256)
        throw new OperationBlockedException("archive-copy-selected-resource-proof-changed", authorization: true);
      authorizedTenantId = selectedDraft.ExpectedTenantId!;
    }

    var currentStaff = await ClientSharePointSites.DesiredStaffAsync(db, archive.FirmId, archive.ClientId, authorizedTenantId, ct);
    var staffDigest = ClientSharePointSites.Digest(currentStaff);
    var expectedStaffPermissions = ArchiveCopyPermissionEncoding.Encode(
      ArchiveCopyPermissionEncoding.ClientStaffRead(target.StaffGroupId));
    if (currentStaff.Count is 0 or > 200 || staffDigest != target.AuthorizedStaffSha256 ||
        await ClientSharePointSites.HasUnverifiedStaffAsync(db, archive.FirmId, archive.ClientId, authorizedTenantId, ct) ||
        expectedStaffPermissions.Json != target.ExpectedPermissionsJson ||
        expectedStaffPermissions.Sha256 != target.ExpectedPermissionsSha256 ||
        target.AuthorizedStaffSha256 != payload.AuthorizedStaffSha256)
      throw new OperationBlockedException("archive-copy-client-staff-or-permission-policy-stale", authorization: true);
    if (clientSite is not null && (staffDigest != clientSite.DesiredDigest ||
        !currentStaff.SequenceEqual(ArchiveCopyService.DeserializeObjectIds(clientSite.MemberObjectIdsJson))))
      throw new OperationBlockedException("archive-copy-client-staff-or-permission-policy-stale", authorization: true);

    _ = ArchiveCopyPermissionEncoding.Decode(target.ExpectedPermissionsJson);
    var connection = await db.Microsoft365ConnectionRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == target.FirmId && x.Id == target.ConnectionRevisionId, ct);
    if (connection is null || connection.State != Microsoft365RevisionStates.Active || connection.ConsentState != "VERIFIED" ||
        !string.Equals(connection.TenantId, authorizedTenantId, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(connection.TenantId, target.TenantId, StringComparison.OrdinalIgnoreCase))
      throw new OperationBlockedException("archive-copy-target-connection-stale", authorization: true);

    ValidateStructuredReleaseSnapshot(structured.PayloadJson, candidate, release, checkpoint, artifact);

    var documentSources = new Dictionary<Guid, ArchiveCopyDocumentSnapshotSource>();
    foreach (var entry in entries)
    {
      if (entry.EntryKind == "STRUCTURED_EXPORT" && entry.SourceKind == "AuditSphereOps") continue;
      if (entry.EntryKind == "DOCUMENT_SNAPSHOT" && entry.SourceKind == "DocumentSnapshot" && entry.SourceId is Guid snapshotId)
      {
        var snapshot = await db.DocumentSnapshots.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.Id == snapshotId, ct);
        var reference = snapshot is null ? null : await db.DocumentReferences.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
          x.Id == snapshot.DocumentReferenceId, ct);
        var binding = reference is null ? null : await db.RepositoryBindings.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId &&
          x.Id == reference.RepositoryBindingId, ct);
        if (snapshot is null || reference is null || binding is null || !string.Equals(reference.Provider, "SharePoint", StringComparison.OrdinalIgnoreCase) ||
            snapshot.DriveId != reference.DriveId || snapshot.ItemId != reference.ItemId || snapshot.DriveId != binding.DriveId ||
            snapshot.DriveId == target.DriveId ||
            string.IsNullOrWhiteSpace(binding.TenantId) || string.IsNullOrWhiteSpace(binding.SiteId) ||
            entry.ContentHash != snapshot.Sha256Hex || entry.ByteCount != snapshot.ByteCount ||
            !SnapshotMetadataMatches(entry.MetadataJson, snapshot))
          throw new OperationBlockedException("archive-copy-snapshot-binding-stale");
        documentSources.Add(entry.Id, new(entry, snapshot, binding));
        continue;
      }

      if (!persistedByteSources.ContainsKey(entry.Id))
        throw new OperationBlockedException("archive-copy-manifest-entry-unretrievable");
    }

    return new ArchiveCopySourceSet(archive, profile, manifest, entries, structured, candidate, release,
      checkpoint, artifact, clientSite, target, documentSources, persistedByteSources,
      includeArtifactBytes ? artifact.ArtifactBytes : null);
  }

  internal static void EnsureTargetClientBinding(Archive archive, ArchiveCopyTargetRevision? target,
    Guid? expectedClientSharePointSiteId)
  {
    if (target is null || target.FirmId != archive.FirmId || target.ClientId != archive.ClientId ||
        target.ClientSharePointSiteId != expectedClientSharePointSiteId)
      throw new OperationBlockedException("archive-copy-target-client-scope-mismatch", authorization: true);
  }

  public static string ProfileSha256(RecordsProfile profile) => Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
  {
    profile.Id, profile.FirmId, profile.ProfileCode, profile.Version, profile.RecordClass, profile.Jurisdiction,
    profile.ServiceRoute, profile.RetentionTrigger, profile.RetentionDurationDays, profile.ProtectionMode,
    profile.LabelId, profile.LegalHoldBehavior, profile.AmendmentRoute, profile.DispositionOwner,
    profile.BackupRequirements, profile.CreatedByUserId, profile.Approved,
    ApprovedAt = profile.ApprovedAt?.ToUniversalTime(), profile.ApprovedByUserId,
    CreatedAt = profile.CreatedAt.ToUniversalTime()
  }));

  private static bool IsSha256(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

  private static string ManifestDigest(IReadOnlyList<ArchiveManifestEntry> entries)
  {
    var serialized = JsonSerializer.Serialize(entries.Select(x => new
    {
      x.Ordinal, x.EntryKind, x.SourceKind, x.SourceId, x.RelativeName, x.ContentHash,
      x.ByteCount, x.Required, x.MetadataJson
    }));
    return Hashing.Sha256Hex(serialized);
  }

  private static bool SnapshotMetadataMatches(string metadataJson, DocumentSnapshot snapshot)
  {
    try
    {
      using var document = JsonDocument.Parse(metadataJson);
      var root = document.RootElement;
      return root.GetProperty("DriveId").GetString() == snapshot.DriveId &&
        root.GetProperty("ItemId").GetString() == snapshot.ItemId &&
        root.GetProperty("VersionId").GetString() == snapshot.VersionId &&
        root.GetProperty("CapturedBy").GetString() == snapshot.CapturedBy;
    }
    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
    {
      return false;
    }
  }

  private static void ValidateStructuredReleaseSnapshot(string payloadJson, ReleaseCandidate candidate,
    Release release, ReleaseCheckpoint checkpoint, FinancialPackageArtifact artifact)
  {
    try
    {
      using var document = JsonDocument.Parse(payloadJson);
      var root = document.RootElement;
      var releaseBlock = root.GetProperty("release");
      if (!releaseBlock.GetProperty("releaseCandidates").EnumerateArray().Any(row =>
            GuidValue(row, "Id") == candidate.Id && StringValue(row, "TargetKind") == candidate.TargetKind &&
            GuidValue(row, "TargetId") == candidate.TargetId && LongValue(row, "TargetRevision") == candidate.TargetRevision &&
            LongValue(row, "Revision") == candidate.Revision && StringValue(row, "ManifestDigest") == candidate.ManifestDigest &&
            StringValue(row, "Status") == candidate.Status) ||
          !releaseBlock.GetProperty("releases").EnumerateArray().Any(row =>
            GuidValue(row, "Id") == release.Id && GuidValue(row, "ReleaseCandidateId") == release.ReleaseCandidateId &&
            GuidValue(row, "PackageId") == release.PackageId && LongValue(row, "PackageRevision") == release.PackageRevision &&
            StringValue(row, "ManifestDigest") == release.ManifestDigest && GuidValue(row, "CheckpointId") == release.CheckpointId &&
            DateTimeOffsetValue(row, "ReleasedAt").ToUniversalTime() == release.ReleasedAt.ToUniversalTime()) ||
          !releaseBlock.GetProperty("releaseCheckpoints").EnumerateArray().Any(row =>
            GuidValue(row, "Id") == checkpoint.Id && GuidValue(row, "ReleaseCandidateId") == checkpoint.ReleaseCandidateId &&
            LongValue(row, "CandidateRevision") == checkpoint.CandidateRevision &&
            StringValue(row, "AuthorizedReleaseKey") == checkpoint.AuthorizedReleaseKey &&
            StringValue(row, "ManifestDigest") == checkpoint.ManifestDigest &&
            StringValue(row, "ReadBackDigest") == checkpoint.ReadBackDigest &&
            StringValue(row, "VerifiedStatus") == checkpoint.VerifiedStatus &&
            DateTimeOffsetValue(row, "VerifiedAt").ToUniversalTime() == checkpoint.VerifiedAt?.ToUniversalTime()) ||
          !root.GetProperty("accounting").GetProperty("packageArtifacts").EnumerateArray().Any(row =>
            GuidValue(row, "Id") == artifact.Id && GuidValue(row, "FinancialPackageId") == artifact.FinancialPackageId &&
            LongValue(row, "PackageRevision") == artifact.PackageRevision && LongValue(row, "PackageGeneration") == artifact.PackageGeneration &&
            StringValue(row, "PackageHash") == artifact.PackageHash && StringValue(row, "ArtifactVersion") == artifact.ArtifactVersion &&
            StringValue(row, "ArtifactSha256Hex") == artifact.ArtifactSha256Hex))
        throw new OperationBlockedException("archive-copy-release-not-in-reviewed-manifest");
    }
    catch (OperationBlockedException) { throw; }
    catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
    {
      throw new OperationBlockedException("archive-copy-reviewed-manifest-invalid");
    }
  }

  private static Guid GuidValue(JsonElement row, string property) => row.GetProperty(property).GetGuid();
  private static string StringValue(JsonElement row, string property) => row.GetProperty(property).GetString() ?? "";
  private static long LongValue(JsonElement row, string property) => row.GetProperty(property).GetInt64();
  private static DateTimeOffset DateTimeOffsetValue(JsonElement row, string property) =>
    DateTimeOffset.Parse(row.GetProperty(property).GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
