using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ConsolidationService
{
  private static readonly JsonSerializerOptions DeliverableJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
  };

  private static readonly string[] GroupDeliverableReadRoles =
    ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator", "RecordsCustodian"];

  public static async Task<CommandResult<GroupDeliverableResult>> BuildGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, BuildGroupDeliverableRequest request, CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || request.RunId == Guid.Empty || request.ExpectedGroupRevision < 1 ||
        !TryNormalizeSha256(request.ExpectedRunHash, out var expectedRunHash) ||
        !TryNormalizeRequestKey(request.RequestKey, out var requestKey))
      return InvalidDeliverable<GroupDeliverableResult>("A scope, run, current group revision, SHA-256 and canonical request identity are required.");

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var scopeLockResult = await LockGroupDeliverableScopeAsync(db, actor, request.ScopeVersionId, PreparerRoles, ct);
    if (!scopeLockResult.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(scopeLockResult.ErrorCode!, scopeLockResult.Message!);
    var locked = scopeLockResult.Value!;
    var scope = locked.Scope;

    if (request.ExpectedGroupRevision != scope.GroupRevision)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The request does not match the group revision pinned to this scope.");

    // Exact retries return the original build receipt even if the deliverable has
    // since been issued or archived. A reused key with different intent conflicts.
    var existing = await db.GroupDeliverables.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.RequestKey == requestKey, ct);
    if (existing is not null)
    {
      if (existing.CreatedByUserId != actor.UserId || existing.RunId != request.RunId ||
          existing.RunHash != expectedRunHash || existing.Revision < 1)
        return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.IdempotencyConflict,
          "This request identity is already bound to a different group deliverable.");
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<GroupDeliverableResult>.Ok(MapBuildReceipt(existing));
    }

    if (locked.Group.Revision != scope.GroupRevision)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The group perimeter changed after this scope was prepared. Review the current group basis before building.");

    var reportResult = await ReadLatestReportAsync(db, actor, scope.Id, ct);
    if (!reportResult.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(reportResult.ErrorCode!, reportResult.Message!);
    var report = reportResult.Value!;
    if (report.State != "CURRENT_APPROVED" || report.RunId != request.RunId || report.RunHash != expectedRunHash)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The approved run changed or is not current. Review the current report before building.");

    var run = await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.RunId && x.FirmId == actor.FirmId && x.GroupId == locked.Group.Id && x.ScopeVersionId == scope.Id, ct);
    if (run is null || run.Status != AccountingWorkflowStates.Approved || run.RunHash != expectedRunHash)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The requested run is not the current approved run for this group scope.");

    // The manifest contains the complete registered perimeter; never silently omit
    // a submitted, returned or otherwise unapproved component.
    var components = await db.ConsolidationComponents.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.GroupId == locked.Group.Id && x.ScopeVersionId == scope.Id)
      .OrderBy(x => x.Id).ToListAsync(ct);
    if (components.Count == 0 || components.Any(x => x.Status != AccountingWorkflowStates.Approved))
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Every component in the group scope must be approved before a deliverable can be built.");

    var clientIds = components.Select(x => x.ClientId).Distinct().ToArray();
    var clientNames = await db.PracticeClients.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);
    if (clientNames.Count != clientIds.Length)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "A component client is unavailable within the authorized firm scope.");

    var builtAt = DateTimeOffset.UtcNow;
    var payload = new
    {
      formatVersion = "GROUP_DELIVERABLE_V1",
      firmId = actor.FirmId,
      groupId = scope.GroupId,
      groupRevision = scope.GroupRevision,
      scopeVersionId = scope.Id,
      runId = run.Id,
      runHash = run.RunHash,
      inputManifest = run.InputManifest,
      reportingCurrency = scope.ReportingCurrency,
      method = scope.Method,
      openingBasis = scope.OpeningBasis,
      runApprovedAt = run.ApprovedAt,
      builtAt,
      builtByUserId = actor.UserId,
      components = components.Select(c => new
      {
        componentId = c.Id,
        clientId = c.ClientId,
        clientName = clientNames[c.ClientId],
        sourceType = c.SourceType,
        packageHash = c.PackageHash,
        currency = c.Currency,
        ownershipPercent = c.OwnershipPercent,
        controlMethod = c.ControlMethod,
        periodBasis = c.PeriodBasis,
        taxonomyVersion = c.TaxonomyVersion,
        mappingVersion = c.MappingVersion
      }).ToArray(),
      lines = report.Lines.Select(l => new
      {
        component = l.Component,
        taxonomyCode = l.TaxonomyCode,
        componentAmount = l.ComponentAmount,
        alignmentAmount = l.AlignmentAmount,
        eliminationAmount = l.EliminationAmount,
        consolidatedAmount = l.ConsolidatedAmount,
        currency = l.Currency
      }).ToArray()
    };

    var json = JsonSerializer.Serialize(payload, DeliverableJsonOptions);
    var deliverableSha256 = ComputeSha256(json);
    var inputManifestDigest = ComputeSha256(run.InputManifest);
    var deliverable = new GroupDeliverable
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      GroupId = scope.GroupId,
      ScopeVersionId = scope.Id,
      RunId = run.Id,
      RunHash = run.RunHash,
      InputManifestDigest = inputManifestDigest,
      DeliverableSha256 = deliverableSha256,
      DeliverableJson = json,
      FormatVersion = "GROUP_DELIVERABLE_V1",
      Status = GroupDeliverableStates.Built,
      CreatedByUserId = actor.UserId,
      CreatedAt = builtAt,
      RequestKey = requestKey,
      Revision = 1
    };

    db.GroupDeliverables.Add(deliverable);
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableResult>> IssueGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, IssueGroupDeliverableRequest request, CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || request.DeliverableId == Guid.Empty ||
        request.ExpectedDeliverableRevision < 1 || request.ExpectedGroupRevision < 1 ||
        !TryNormalizeSha256(request.ExpectedDeliverableSha256, out var expectedSha) ||
        !TryNormalizeRequestKey(request.RequestKey, out var requestKey) ||
        !TryNormalizeReleaseKey(request.AuthorizedReleaseKey, out var releaseKey))
      return InvalidDeliverable<GroupDeliverableResult>("A scope, deliverable, current revisions, SHA-256, release reference and canonical request identity are required.");

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var scopeLockResult = await LockGroupDeliverableScopeAsync(db, actor, request.ScopeVersionId,
      ["Partner", "Administrator"], ct);
    if (!scopeLockResult.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(scopeLockResult.ErrorCode!, scopeLockResult.Message!);
    var locked = scopeLockResult.Value!;
    var scope = locked.Scope;

    if (request.ExpectedGroupRevision != scope.GroupRevision)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The request does not match the group revision pinned to this scope.");

    var prior = await db.GroupDeliverables.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.IssueRequestKey == requestKey, ct);
    if (prior is not null)
    {
      if (prior.Id != request.DeliverableId || prior.IssuedByUserId != actor.UserId ||
          prior.AuthorizedReleaseKey != releaseKey || prior.DeliverableSha256 != expectedSha ||
          request.ExpectedDeliverableRevision != 1 || prior.Status is not (GroupDeliverableStates.Issued or GroupDeliverableStates.Archived))
        return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.IdempotencyConflict,
          "This issue request identity is already bound to a different release action.");
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<GroupDeliverableResult>.Ok(MapIssueReceipt(prior));
    }

    if (locked.Group.Revision != scope.GroupRevision || request.ExpectedGroupRevision != locked.Group.Revision)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The group perimeter changed after this scope was prepared. Review the current group basis before issuing.");

    var deliverable = await LockGroupDeliverableAsync(db, actor.FirmId, scope, request.DeliverableId, ct);
    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (deliverable.Revision != request.ExpectedDeliverableRevision)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The deliverable revision changed; refresh and review the current artifact.");
    if (deliverable.Status != GroupDeliverableStates.Built)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Only a built group deliverable can be issued.");
    if (deliverable.DeliverableSha256 != expectedSha)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The deliverable SHA-256 changed; refresh and review the current artifact.");
    if (!HasValidDeliverableBytes(deliverable))
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed.");

    var run = await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == deliverable.RunId && x.FirmId == actor.FirmId && x.GroupId == locked.Group.Id && x.ScopeVersionId == scope.Id, ct);
    if (run is null || run.Status != AccountingWorkflowStates.Approved || run.RunHash != deliverable.RunHash)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The deliverable run is no longer the approved run for this group scope.");
    if (run.CreatedByUserId == actor.UserId)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The preparer of the consolidation run cannot issue the group deliverable.");

    var reportResult = await ReadLatestReportAsync(db, actor, scope.Id, ct);
    if (!reportResult.Succeeded || reportResult.Value?.State != "CURRENT_APPROVED" ||
        reportResult.Value.RunId != deliverable.RunId || reportResult.Value.RunHash != deliverable.RunHash)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The consolidation run is stale; review the current report before issuing the deliverable.");

    deliverable.Status = GroupDeliverableStates.Issued;
    deliverable.IssuedByUserId = actor.UserId;
    deliverable.IssuedAt = DateTimeOffset.UtcNow;
    deliverable.AuthorizedReleaseKey = releaseKey;
    deliverable.IssueRequestKey = requestKey;
    deliverable.Revision++;

    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableResult>> ArchiveGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, ArchiveGroupDeliverableRequest request, CancellationToken ct = default)
  {
    if (request.ScopeVersionId == Guid.Empty || request.DeliverableId == Guid.Empty ||
        request.ExpectedDeliverableRevision < 1 ||
        !TryNormalizeSha256(request.ExpectedDeliverableSha256, out var expectedSha) ||
        !TryNormalizeRequestKey(request.RequestKey, out var requestKey) ||
        !TryNormalizeArchiveReason(request.Reason, out var reason))
      return InvalidDeliverable<GroupDeliverableResult>("A scope, deliverable, current revision, SHA-256, archive reason and canonical request identity are required.");

    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    var scopeLockResult = await LockGroupDeliverableScopeAsync(db, actor, request.ScopeVersionId,
      ["Partner", "Administrator", "RecordsCustodian"], ct);
    if (!scopeLockResult.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(scopeLockResult.ErrorCode!, scopeLockResult.Message!);
    var locked = scopeLockResult.Value!;
    var scope = locked.Scope;

    var prior = await db.GroupDeliverables.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.ArchiveRequestKey == requestKey, ct);
    if (prior is not null)
    {
      if (prior.Id != request.DeliverableId || prior.ArchivedByUserId != actor.UserId ||
          prior.DeliverableSha256 != expectedSha || prior.ArchiveReason != reason ||
          request.ExpectedDeliverableRevision != 2 || prior.Status != GroupDeliverableStates.Archived)
        return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.IdempotencyConflict,
          "This archive request identity is already bound to a different retention action.");
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(prior));
    }

    var deliverable = await LockGroupDeliverableAsync(db, actor.FirmId, scope, request.DeliverableId, ct);
    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (deliverable.Revision != request.ExpectedDeliverableRevision)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The deliverable revision changed; refresh and review the current artifact.");
    if (deliverable.Status != GroupDeliverableStates.Issued)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Only an issued group deliverable can be archived.");
    if (deliverable.DeliverableSha256 != expectedSha)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The deliverable SHA-256 changed; refresh and review the current artifact.");
    if (!HasValidDeliverableBytes(deliverable))
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed.");

    deliverable.Status = GroupDeliverableStates.Archived;
    deliverable.ArchivedByUserId = actor.UserId;
    deliverable.ArchivedAt = DateTimeOffset.UtcNow;
    deliverable.ArchiveReason = reason;
    deliverable.ArchiveRequestKey = requestKey;
    deliverable.Revision++;

    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverablePage>> ListGroupDeliverablesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    int page = 1, int pageSize = 25, CancellationToken ct = default)
  {
    if (scopeVersionId == Guid.Empty || page < 1 || pageSize is < 1 or > 100 ||
        ((long)page - 1) * pageSize > int.MaxValue)
      return InvalidDeliverable<GroupDeliverablePage>("The deliverable page must be positive and page size must be between 1 and 100.");

    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<GroupDeliverablePage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, GroupDeliverableReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverablePage>.Fail(auth.ErrorCode!, auth.Message!);

    var query = db.GroupDeliverables.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scopeVersionId);
    var totalCount = await query.CountAsync(ct);
    var rows = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new
      {
        x.Id, x.Status, x.RunHash, x.DeliverableSha256, x.CreatedAt, x.CreatedByUserId,
        x.IssuedAt, x.IssuedByUserId, x.ArchivedAt, x.ArchivedByUserId, x.Revision
      }).ToListAsync(ct);
    var items = rows.Select(x => new GroupDeliverableSummary(
      x.Id, x.Status, x.RunHash, x.DeliverableSha256, x.CreatedAt, x.CreatedByUserId,
      x.IssuedAt, x.IssuedByUserId, x.ArchivedAt, x.ArchivedByUserId, x.Revision)).ToArray();
    return CommandResult<GroupDeliverablePage>.Ok(new GroupDeliverablePage(page, pageSize, totalCount, items));
  }

  public static async Task<CommandResult<GroupDeliverableResult>> GetGroupDeliverableByIdAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, Guid deliverableId, CancellationToken ct = default)
  {
    if (scopeVersionId == Guid.Empty || deliverableId == Guid.Empty)
      return InvalidDeliverable<GroupDeliverableResult>("A scope and deliverable identity are required.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, GroupDeliverableReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(auth.ErrorCode!, auth.Message!);

    var deliverable = await db.GroupDeliverables.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == deliverableId && x.ScopeVersionId == scopeVersionId && x.FirmId == actor.FirmId && x.GroupId == scope.GroupId, ct);
    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Deliverable not found.");
    if (!HasValidDeliverableBytes(deliverable))
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked, "Deliverable byte integrity check failed.");
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableExport>> DownloadGroupDeliverableByIdAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, Guid deliverableId, CancellationToken ct = default)
  {
    var deliverableResult = await GetGroupDeliverableByIdAsync(db, actor, scopeVersionId, deliverableId, ct);
    if (!deliverableResult.Succeeded)
      return CommandResult<GroupDeliverableExport>.Fail(deliverableResult.ErrorCode!, deliverableResult.Message!);
    return CreateGroupDeliverableExport(deliverableResult.Value!, scopeVersionId);
  }

  public static async Task<CommandResult<GroupDeliverableResult>> GetIssuedGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, CancellationToken ct = default)
  {
    if (scopeVersionId == Guid.Empty)
      return InvalidDeliverable<GroupDeliverableResult>("A scope identity is required.");
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await GroupAuthAsync(db, actor, scope.GroupId, GroupDeliverableReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(auth.ErrorCode!, auth.Message!);

    var deliverable = await db.GroupDeliverables.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scopeVersionId && x.GroupId == scope.GroupId &&
        (x.Status == GroupDeliverableStates.Issued || x.Status == GroupDeliverableStates.Archived))
      .OrderByDescending(x => x.IssuedAt).ThenByDescending(x => x.Id).FirstOrDefaultAsync(ct);
    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied,
        "No issued group deliverable was found for this scope.");
    if (!HasValidDeliverableBytes(deliverable))
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked, "Deliverable byte integrity check failed.");
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableExport>> DownloadIssuedGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, CancellationToken ct = default)
  {
    var issued = await GetIssuedGroupDeliverableAsync(db, actor, scopeVersionId, ct);
    if (!issued.Succeeded)
      return CommandResult<GroupDeliverableExport>.Fail(issued.ErrorCode!, issued.Message!);
    return CreateGroupDeliverableExport(issued.Value!, scopeVersionId);
  }

  private static async Task<CommandResult<LockedGroupDeliverableScope>> LockGroupDeliverableScopeAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId,
    IReadOnlyList<string> roles, CancellationToken ct)
  {
    var groupId = await db.ConsolidationScopeVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Id == scopeVersionId)
      .Select(x => (Guid?)x.GroupId).SingleOrDefaultAsync(ct);
    if (groupId is null)
      return CommandResult<LockedGroupDeliverableScope>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await GroupAuthAsync(db, actor, groupId.Value, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<LockedGroupDeliverableScope>.Fail(auth.ErrorCode!, auth.Message!);
    if (!await LockGroupAuthorityFenceAsync(db, actor.FirmId, ct))
      return CommandResult<LockedGroupDeliverableScope>.Fail(ErrorCodes.ScopeDenied,
        "The firm safety state is unavailable.");

    var group = await db.ClientGroups.FromSqlInterpolated($"""
      SELECT * FROM client_groups WHERE id = {groupId.Value} AND firm_id = {actor.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    var scope = await db.ConsolidationScopeVersions.FromSqlInterpolated($"""
      SELECT * FROM consolidation_scope_versions
      WHERE id = {scopeVersionId} AND firm_id = {actor.FirmId} AND group_id = {groupId.Value} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (group is null || scope is null)
      return CommandResult<LockedGroupDeliverableScope>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    auth = await GroupAuthAsync(db, actor, group.Id, roles, ct);
    if (!auth.Succeeded)
      return CommandResult<LockedGroupDeliverableScope>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<LockedGroupDeliverableScope>.Ok(new LockedGroupDeliverableScope(group, scope));
  }

  private static Task<GroupDeliverable?> LockGroupDeliverableAsync(
    IClientAccountingDbContext db, Guid firmId, ConsolidationScopeVersion scope, Guid deliverableId, CancellationToken ct) =>
    db.GroupDeliverables.FromSqlInterpolated($"""
      SELECT * FROM group_deliverables
      WHERE id = {deliverableId} AND firm_id = {firmId} AND group_id = {scope.GroupId}
        AND scope_version_id = {scope.Id} FOR UPDATE
      """).SingleOrDefaultAsync(ct);

  private static CommandResult<T> InvalidDeliverable<T>(string message) =>
    CommandResult<T>.Fail("deliverable.invalid", message);

  private static bool TryNormalizeRequestKey(string? value, out string normalized)
  {
    normalized = string.Empty;
    if (!Guid.TryParseExact(value?.Trim(), "D", out var parsed) || parsed == Guid.Empty) return false;
    var candidate = parsed.ToString("D", CultureInfo.InvariantCulture);
    if (candidate[14] is < '1' or > '8' || candidate[19] is not ('8' or '9' or 'a' or 'b')) return false;
    normalized = candidate;
    return true;
  }

  private static bool TryNormalizeSha256(string? value, out string normalized)
  {
    normalized = string.Empty;
    var candidate = value?.Trim();
    if (candidate is null || candidate.Length != 64 || candidate.Any(c => !Uri.IsHexDigit(c))) return false;
    normalized = candidate.ToLowerInvariant();
    return true;
  }

  private static bool TryNormalizeReleaseKey(string? value, out string normalized)
  {
    normalized = value?.Trim() ?? string.Empty;
    return normalized.Length is > 0 and <= 100;
  }

  private static bool TryNormalizeArchiveReason(string? value, out string normalized)
  {
    normalized = value?.Trim() ?? string.Empty;
    return normalized.Length is > 0 and <= 2000;
  }

  private static string ComputeSha256(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

  private static bool HasValidDeliverableBytes(GroupDeliverable deliverable) =>
    ComputeSha256(deliverable.DeliverableJson) == deliverable.DeliverableSha256;

  private static CommandResult<GroupDeliverableExport> CreateGroupDeliverableExport(
    GroupDeliverableResult deliverable, Guid scopeVersionId)
  {
    var bytes = Encoding.UTF8.GetBytes(deliverable.DeliverableJson);
    var contentSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    if (contentSha != deliverable.DeliverableSha256)
      return CommandResult<GroupDeliverableExport>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed on download.");
    var prefix = deliverable.RunHash[..Math.Min(8, deliverable.RunHash.Length)];
    return CommandResult<GroupDeliverableExport>.Ok(new GroupDeliverableExport(
      $"group-deliverable-{scopeVersionId:N}-{prefix}.json", bytes, contentSha,
      deliverable.DeliverableSha256, deliverable.Status));
  }

  private sealed record LockedGroupDeliverableScope(ClientGroup Group, ConsolidationScopeVersion Scope);

  private static GroupDeliverableResult MapDeliverable(
    GroupDeliverable d, string? status = null, bool clearIssue = false, bool clearArchive = false, long? revision = null) => new(
    d.Id, d.GroupId, d.ScopeVersionId, d.RunId, d.RunHash, d.InputManifestDigest, d.DeliverableSha256,
    d.FormatVersion, status ?? d.Status, d.CreatedByUserId, d.CreatedAt,
    clearIssue ? null : d.IssuedByUserId, clearIssue ? null : d.IssuedAt,
    clearIssue ? null : d.AuthorizedReleaseKey,
    clearArchive ? null : d.ArchivedByUserId, clearArchive ? null : d.ArchivedAt,
    clearArchive ? null : d.ArchiveReason, d.DeliverableJson,
    d.RequestKey, clearIssue ? null : d.IssueRequestKey, clearArchive ? null : d.ArchiveRequestKey,
    revision ?? d.Revision);

  private static GroupDeliverableResult MapBuildReceipt(GroupDeliverable d) =>
    MapDeliverable(d, GroupDeliverableStates.Built, clearIssue: true, clearArchive: true, revision: 1);

  private static GroupDeliverableResult MapIssueReceipt(GroupDeliverable d) =>
    MapDeliverable(d, GroupDeliverableStates.Issued, clearArchive: true, revision: 2);

  private static GroupDeliverableSummary MapSummary(GroupDeliverable d) => new(
    d.Id, d.Status, d.RunHash, d.DeliverableSha256, d.CreatedAt, d.CreatedByUserId,
    d.IssuedAt, d.IssuedByUserId, d.ArchivedAt, d.ArchivedByUserId, d.Revision);
}
