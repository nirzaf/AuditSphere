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

  public static async Task<CommandResult<GroupDeliverableResult>> BuildGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AdministratorOversightReadAuthorization.AuthorizeGroupReadAsync(db, actor, scope.GroupId,
      ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(auth.ErrorCode!, auth.Message!);

    var reportResult = await ReadLatestReportAsync(db, actor, scopeVersionId, ct);
    if (!reportResult.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(reportResult.ErrorCode!, reportResult.Message!);

    var report = reportResult.Value!;
    if (report.State != "CURRENT_APPROVED" || !report.RunId.HasValue)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "An approved, current consolidation run is required to build a group deliverable.");

    var run = await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == report.RunId.Value && x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id, ct);
    if (run is null || run.Status != AccountingWorkflowStates.Approved)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "The approved consolidation run is unavailable.");

    var components = await db.ConsolidationComponents.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ScopeVersionId == scope.Id && x.Status == AccountingWorkflowStates.Approved)
      .OrderBy(x => x.Id).ToListAsync(ct);

    var clientIds = components.Select(x => x.ClientId).Distinct().ToArray();
    var clientNames = await db.PracticeClients.AsNoTracking().Where(x => x.FirmId == actor.FirmId && clientIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.LegalName, ct);

    var payload = new
    {
      formatVersion = "GROUP_DELIVERABLE_V1",
      firmId = actor.FirmId,
      groupId = scope.GroupId,
      scopeVersionId = scope.Id,
      runId = run.Id,
      runHash = run.RunHash,
      inputManifest = run.InputManifest,
      reportingCurrency = scope.ReportingCurrency,
      method = scope.Method,
      openingBasis = scope.OpeningBasis,
      runApprovedAt = run.ApprovedAt,
      builtAt = DateTimeOffset.UtcNow,
      builtByUserId = actor.UserId,
      components = components.Select(c => new
      {
        componentId = c.Id,
        clientId = c.ClientId,
        clientName = clientNames.GetValueOrDefault(c.ClientId, string.Empty),
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
    var deliverableSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    var inputManifestDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(run.InputManifest))).ToLowerInvariant();

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
      CreatedAt = DateTimeOffset.UtcNow
    };

    db.GroupDeliverables.Add(deliverable);
    await db.SaveChangesAsync(ct);

    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableResult>> IssueGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, IssueGroupDeliverableRequest request, CancellationToken ct = default)
  {
    if (request.DeliverableId == Guid.Empty)
      return CommandResult<GroupDeliverableResult>.Fail("deliverable.invalid", "A deliverable identity is required.");

    var deliverable = await db.GroupDeliverables.SingleOrDefaultAsync(x =>
      x.Id == request.DeliverableId && x.FirmId == actor.FirmId, ct);
    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AdministratorOversightReadAuthorization.AuthorizeGroupReadAsync(db, actor, deliverable.GroupId,
      ["Partner", "Administrator"], ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(auth.ErrorCode!, auth.Message!);

    if (deliverable.Status != GroupDeliverableStates.Built)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Only a built group deliverable can be issued.");

    var run = await db.ConsolidationRuns.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == deliverable.RunId && x.FirmId == actor.FirmId, ct);
    if (run is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked, "The associated run is unavailable.");

    if (run.CreatedByUserId == actor.UserId)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "The preparer of the consolidation run cannot issue the group deliverable.");

    var reportResult = await ReadLatestReportAsync(db, actor, deliverable.ScopeVersionId, ct);
    if (!reportResult.Succeeded || reportResult.Value?.State != "CURRENT_APPROVED" || reportResult.Value.RunId != deliverable.RunId)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GenerationStale,
        "The consolidation run is stale; rebuild the group deliverable.");

    var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deliverable.DeliverableJson))).ToLowerInvariant();
    if (actualSha != deliverable.DeliverableSha256)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed.");

    var releaseKey = !string.IsNullOrWhiteSpace(request.AuthorizedReleaseKey)
      ? request.AuthorizedReleaseKey.Trim()
      : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{deliverable.Id}:{actor.UserId}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"))).ToLowerInvariant();

    deliverable.Status = GroupDeliverableStates.Issued;
    deliverable.IssuedByUserId = actor.UserId;
    deliverable.IssuedAt = DateTimeOffset.UtcNow;
    deliverable.AuthorizedReleaseKey = releaseKey;

    await db.SaveChangesAsync(ct);
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableResult>> ArchiveGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, ArchiveGroupDeliverableRequest request, CancellationToken ct = default)
  {
    if (request.DeliverableId == Guid.Empty)
      return CommandResult<GroupDeliverableResult>.Fail("deliverable.invalid", "A deliverable identity is required.");

    var deliverable = await db.GroupDeliverables.SingleOrDefaultAsync(x =>
      x.Id == request.DeliverableId && x.FirmId == actor.FirmId, ct);
    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AdministratorOversightReadAuthorization.AuthorizeGroupReadAsync(db, actor, deliverable.GroupId,
      ["Partner", "Administrator", "RecordsCustodian"], ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(auth.ErrorCode!, auth.Message!);

    if (deliverable.Status != GroupDeliverableStates.Issued)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Only an issued group deliverable can be archived.");

    var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deliverable.DeliverableJson))).ToLowerInvariant();
    if (actualSha != deliverable.DeliverableSha256)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed.");

    deliverable.Status = GroupDeliverableStates.Archived;
    deliverable.ArchivedByUserId = actor.UserId;
    deliverable.ArchivedAt = DateTimeOffset.UtcNow;
    deliverable.ArchiveReason = request.Reason?.Trim() ?? string.Empty;

    await db.SaveChangesAsync(ct);
    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableResult>> GetIssuedGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, CancellationToken ct = default)
  {
    var scope = await db.ConsolidationScopeVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == scopeVersionId && x.FirmId == actor.FirmId, ct);
    if (scope is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AdministratorOversightReadAuthorization.AuthorizeGroupReadAsync(db, actor, scope.GroupId,
      ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator", "RecordsCustodian"], ct);
    if (!auth.Succeeded)
      return CommandResult<GroupDeliverableResult>.Fail(auth.ErrorCode!, auth.Message!);

    var deliverable = await db.GroupDeliverables.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ScopeVersionId == scopeVersionId &&
        (x.Status == GroupDeliverableStates.Issued || x.Status == GroupDeliverableStates.Archived))
      .OrderByDescending(x => x.IssuedAt).FirstOrDefaultAsync(ct);

    if (deliverable is null)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.ScopeDenied,
        "No issued group deliverable was found for this scope.");

    var actualSha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deliverable.DeliverableJson))).ToLowerInvariant();
    if (actualSha != deliverable.DeliverableSha256)
      return CommandResult<GroupDeliverableResult>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed.");

    return CommandResult<GroupDeliverableResult>.Ok(MapDeliverable(deliverable));
  }

  public static async Task<CommandResult<GroupDeliverableExport>> DownloadIssuedGroupDeliverableAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid scopeVersionId, CancellationToken ct = default)
  {
    var issued = await GetIssuedGroupDeliverableAsync(db, actor, scopeVersionId, ct);
    if (!issued.Succeeded)
      return CommandResult<GroupDeliverableExport>.Fail(issued.ErrorCode!, issued.Message!);

    var deliverable = issued.Value!;
    var bytes = Encoding.UTF8.GetBytes(deliverable.DeliverableJson);
    var contentSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    if (contentSha != deliverable.DeliverableSha256)
      return CommandResult<GroupDeliverableExport>.Fail(ErrorCodes.GateBlocked,
        "Deliverable byte integrity check failed on download.");

    var fileName = $"group-deliverable-{scopeVersionId:N}-{deliverable.RunHash[..8]}.json";
    return CommandResult<GroupDeliverableExport>.Ok(new GroupDeliverableExport(
      fileName, bytes, contentSha, deliverable.DeliverableSha256, deliverable.Status));
  }

  private static GroupDeliverableResult MapDeliverable(GroupDeliverable d) => new(
    d.Id, d.GroupId, d.ScopeVersionId, d.RunId, d.RunHash, d.InputManifestDigest, d.DeliverableSha256,
    d.FormatVersion, d.Status, d.CreatedByUserId, d.CreatedAt, d.IssuedByUserId, d.IssuedAt,
    d.AuthorizedReleaseKey, d.ArchivedByUserId, d.ArchivedAt, d.ArchiveReason, d.DeliverableJson);
}

