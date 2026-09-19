using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Records;

public sealed record CreateRecordsProfileRequest(
  string ProfileCode, long Version, string RecordClass, string Jurisdiction, string ServiceRoute,
  string RetentionTrigger, int? RetentionDurationDays, string ProtectionMode, string LabelId,
  string LegalHoldBehavior, string AmendmentRoute, string DispositionOwner, string BackupRequirements);

public sealed record RecordsProfileResult(Guid ProfileId, string ProfileCode, long Version, bool Approved);

public sealed record ArchiveManifestResult(
  Guid ArchiveId, Guid ManifestId, string Status, string Digest, int EntryCount, string CompletenessStatus);

public sealed record RequestRecordsActionRequest(Guid ArchiveId, string? ExternalReference);

public sealed record ObserveRecordsActionRequest(
  Guid ArchiveId, string ObservedLabel, string ObservedProtection, string ObservedBy, string? ExternalReference);

public sealed record RequestLegalHoldRequest(Guid ArchiveId, string HoldReference, string? Notes);

public sealed record ObserveLegalHoldRequest(Guid ArchiveId, string HoldReference, string? ExternalReference);

/// <summary>
/// Local records-control commands. Microsoft Purview calls are deliberately absent: a requested
/// action is not treated as observed protection or a legal hold (§25.4, §25.6).
/// </summary>
public static class RecordsArchiveService
{
  private static readonly string[] CustodianRoles = ["Administrator", "Partner", "RecordsCustodian"];

  public static async Task<CommandResult<RecordsProfileResult>> CreateProfileAsync(
    IAuditSphereDbContext db, ActorContext actor, CreateRecordsProfileRequest request,
    CancellationToken ct = default)
  {
    var invalid = ValidateProfile(request);
    if (invalid is not null)
      return CommandResult<RecordsProfileResult>.Fail("records-profile.invalid", invalid);

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: CustodianRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<RecordsProfileResult>.Fail(auth.ErrorCode!, auth.Message!);

    var profile = new RecordsProfile
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId,
      ProfileCode = request.ProfileCode.Trim(), Version = request.Version,
      RecordClass = request.RecordClass.Trim(), Jurisdiction = request.Jurisdiction.Trim(),
      ServiceRoute = request.ServiceRoute.Trim(), RetentionTrigger = request.RetentionTrigger.Trim(),
      RetentionDurationDays = request.RetentionDurationDays, ProtectionMode = request.ProtectionMode.Trim(),
      LabelId = request.LabelId.Trim(), LegalHoldBehavior = request.LegalHoldBehavior.Trim(),
      AmendmentRoute = request.AmendmentRoute.Trim(), DispositionOwner = request.DispositionOwner.Trim(),
      BackupRequirements = request.BackupRequirements.Trim(), CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.RecordsProfiles.Add(profile);
    await db.SaveChangesAsync(ct);
    return CommandResult<RecordsProfileResult>.Ok(new(profile.Id, profile.ProfileCode, profile.Version, false));
  }

  public static async Task<CommandResult<ArchiveManifestResult>> BuildManifestAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid archiveId, CancellationToken ct = default)
  {
    var authorized = await AuthorizeArchiveAsync(db, actor, archiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult<ArchiveManifestResult>.Fail(authorized.ErrorCode!, authorized.Message!);

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var archive = await db.Archives.FromSqlInterpolated($"""
      SELECT * FROM archives WHERE id = {archiveId} AND firm_id = {actor.FirmId} FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    if (archive is null)
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (archive.Status is not (ArchiveStates.Issued or ArchiveStates.AssemblyInProgress))
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.ProtectedState,
        "The archive is not in an assembly state.");
    var profile = await db.RecordsProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ProfileCode == archive.ProfileId && x.Version == archive.ProfileVersion, ct);
    if (profile is null || !profile.Approved)
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.GateBlocked,
        "An approved records profile version is required.");

    var snapshots = await db.DocumentSnapshots.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).ToListAsync(ct);
    var manifestVersion = (await db.ArchiveManifests
      .Where(x => x.FirmId == archive.FirmId && x.ArchiveId == archive.Id)
      .Select(x => (long?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var references = await db.DocumentReferences.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .Select(x => x.Id).ToHashSetAsync(ct);

    var entries = snapshots.Select((snapshot, index) => new ArchiveManifestEntry
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, Ordinal = index + 1, EntryKind = "DOCUMENT_SNAPSHOT",
      SourceKind = "DocumentSnapshot", SourceId = snapshot.Id,
      RelativeName = $"documents/{index + 1:000000}-{snapshot.ItemId}",
      ContentHash = snapshot.Sha256Hex, ByteCount = snapshot.ByteCount, Required = true,
      MetadataJson = JsonSerializer.Serialize(new { snapshot.DriveId, snapshot.ItemId, snapshot.VersionId, snapshot.CapturedBy })
    }).ToList();

    var structured = await BuildStructuredExportAsync(db, archive, ct);
    var structuredBytes = Encoding.UTF8.GetBytes(structured);
    entries.Add(new ArchiveManifestEntry
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, Ordinal = entries.Count + 1, EntryKind = "STRUCTURED_EXPORT",
      SourceKind = "AuditSphereOps", RelativeName = "structured/records-export.v1.json",
      ContentHash = Hashing.Sha256Hex(structuredBytes), ByteCount = structuredBytes.Length, Required = true,
      MetadataJson = "{\"schema\":\"records-export.v1\",\"storage\":\"archive_structured_exports\"}"
    });

    var missingReferences = snapshots.Count(x => !references.Contains(x.DocumentReferenceId));
    var complete = missingReferences == 0;
    var entryDigest = JsonSerializer.Serialize(entries.Select(x => new
    {
      x.Ordinal, x.EntryKind, x.SourceKind, x.SourceId, x.RelativeName, x.ContentHash, x.ByteCount, x.Required, x.MetadataJson
    }));
    var digest = Hashing.Sha256Hex(entryDigest);
    var manifest = new ArchiveManifest
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, ArchiveId = archive.Id, Version = manifestVersion,
      Status = "BUILT", ManifestDigest = digest, EntryCount = entries.Count,
      CompletenessStatus = complete ? "COMPLETE" : "INCOMPLETE",
      CompletenessException = complete ? null : $"{missingReferences} snapshot reference(s) are missing.",
      BuiltAt = DateTimeOffset.UtcNow
    };
    db.ArchiveManifests.Add(manifest);
    db.ArchiveManifestEntries.AddRange(entries.Select(x => { x.ArchiveManifestId = manifest.Id; return x; }));
    db.ArchiveStructuredExports.Add(new ArchiveStructuredExport
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, ArchiveId = archive.Id, ArchiveManifestId = manifest.Id,
      Version = manifestVersion, Schema = "records-export.v1", PayloadJson = structured,
      ContentHash = Hashing.Sha256Hex(structuredBytes), ByteCount = structuredBytes.Length,
      CreatedAt = DateTimeOffset.UtcNow
    });
    if (complete)
      await db.Archives.Where(x => x.Id == archive.Id && x.FirmId == archive.FirmId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ArchiveStates.ManifestBuilt), ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);

    var result = new ArchiveManifestResult(archive.Id, manifest.Id, manifest.Status, manifest.ManifestDigest,
      manifest.EntryCount, manifest.CompletenessStatus);
    return complete
      ? CommandResult<ArchiveManifestResult>.Ok(result)
      : CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.GateBlocked,
        "The archive manifest was stored but is incomplete; missing references must be resolved.");
  }

  public static async Task<CommandResult<ArchiveManifestResult>> ReviewManifestAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid archiveId, CancellationToken ct = default)
  {
    var authorized = await AuthorizeArchiveAsync(db, actor, archiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult<ArchiveManifestResult>.Fail(authorized.ErrorCode!, authorized.Message!);
    var archive = authorized.Value!;
    if (archive.Status != ArchiveStates.ManifestBuilt)
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.ProtectedState,
        "Only a complete built manifest can be reviewed.");

    var manifest = await db.ArchiveManifests.Where(x => x.Id == archiveId || x.ArchiveId == archiveId)
      .Where(x => x.FirmId == actor.FirmId && x.ArchiveId == archive.Id)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (manifest is null || manifest.CompletenessStatus != "COMPLETE")
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.GateBlocked, "A complete archive manifest is required.");

    manifest.Status = "REVIEWED";
    manifest.ReviewedAt = DateTimeOffset.UtcNow;
    manifest.ReviewedByUserId = actor.UserId;
    await db.Archives.Where(x => x.Id == archive.Id && x.FirmId == archive.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ArchiveStates.AssemblyReviewed), ct);
    await db.SaveChangesAsync(ct);
    return CommandResult<ArchiveManifestResult>.Ok(new(
      archive.Id, manifest.Id, manifest.Status, manifest.ManifestDigest, manifest.EntryCount, manifest.CompletenessStatus));
  }

  public static async Task<CommandResult> RequestRecordsActionAsync(
    IAuditSphereDbContext db, ActorContext actor, RequestRecordsActionRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.ExternalReference) && request.ExternalReference is not null)
      return CommandResult.Fail("records-action.invalid", "An external reference cannot be blank.");
    var authorized = await AuthorizeArchiveAsync(db, actor, request.ArchiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult.Fail(authorized.ErrorCode!, authorized.Message!);
    var archive = authorized.Value!;
    if (archive.Status != ArchiveStates.AssemblyReviewed)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The archive must be reviewed before records action.");
    var manifest = await db.ArchiveManifests.AsNoTracking().Where(x => x.ArchiveId == archive.Id)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (manifest is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An archive manifest is required.");
    var profile = await db.RecordsProfiles.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == archive.FirmId && x.ProfileCode == archive.ProfileId && x.Version == archive.ProfileVersion, ct);
    if (profile is null || !profile.Approved)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "An approved records profile version is required.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var requestedAt = DateTimeOffset.UtcNow;
    var action = new RecordsAction
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, ArchiveId = archive.Id, ArchiveManifestId = manifest.Id,
      DesiredLabel = profile.LabelId, DesiredProtection = profile.ProtectionMode,
      ExternalReference = request.ExternalReference?.Trim(), RequestedByUserId = actor.UserId,
      RequestedAt = requestedAt
    };
    db.RecordsActions.Add(action);
    db.RecordsActionEvidences.Add(new RecordsActionEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, RecordsActionId = action.Id, ArchiveId = archive.Id,
      ArchiveManifestId = manifest.Id, Sequence = 1, EventKind = "REQUESTED",
      DesiredLabel = action.DesiredLabel, DesiredProtection = action.DesiredProtection,
      ExternalReference = action.ExternalReference, ActorUserId = actor.UserId, OccurredAt = requestedAt
    });
    await db.Archives.Where(x => x.Id == archive.Id && x.FirmId == archive.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ArchiveStates.RecordsActionRequested), ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ObserveRecordsActionAsync(
    IAuditSphereDbContext db, ActorContext actor, ObserveRecordsActionRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.ObservedLabel) || string.IsNullOrWhiteSpace(request.ObservedProtection) ||
        string.IsNullOrWhiteSpace(request.ObservedBy))
      return CommandResult.Fail("records-action.invalid", "Observed label, protection and operator are required.");
    var authorized = await AuthorizeArchiveAsync(db, actor, request.ArchiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult.Fail(authorized.ErrorCode!, authorized.Message!);
    var archive = authorized.Value!;
    if (archive.Status != ArchiveStates.RecordsActionRequested)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The archive is not awaiting a records observation.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var action = await db.RecordsActions.SingleOrDefaultAsync(x => x.ArchiveId == archive.Id && x.FirmId == actor.FirmId, ct);
    if (action is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "No records action request exists.");

    var observedAt = DateTimeOffset.UtcNow;
    action.ObservedLabel = request.ObservedLabel.Trim();
    action.ObservedProtection = request.ObservedProtection.Trim();
    action.ObservedBy = request.ObservedBy.Trim();
    action.ExternalReference = request.ExternalReference?.Trim() ?? action.ExternalReference;
    action.ObservedAt = observedAt;
    action.State = "OBSERVED";
    var nextSequence = (await db.RecordsActionEvidences
      .Where(x => x.RecordsActionId == action.Id && x.FirmId == actor.FirmId)
      .Select(x => (long?)x.Sequence).MaxAsync(ct) ?? 0) + 1;
    db.RecordsActionEvidences.Add(new RecordsActionEvidence
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, RecordsActionId = action.Id, ArchiveId = archive.Id,
      ArchiveManifestId = action.ArchiveManifestId, Sequence = nextSequence, EventKind = "OBSERVED",
      DesiredLabel = action.DesiredLabel, DesiredProtection = action.DesiredProtection,
      ObservedLabel = action.ObservedLabel, ObservedProtection = action.ObservedProtection,
      ExternalReference = action.ExternalReference, ObservedBy = action.ObservedBy,
      ActorUserId = actor.UserId, OccurredAt = observedAt
    });
    await db.Archives.Where(x => x.Id == archive.Id && x.FirmId == archive.FirmId)
      .ExecuteUpdateAsync(s => s
        .SetProperty(x => x.Status, ArchiveStates.ProtectionObserved)
        .SetProperty(x => x.ObservedProtectionState, action.ObservedProtection)
        .SetProperty(x => x.ObservedProtectionAt, observedAt), ct);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> VerifyArchiveAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid archiveId, CancellationToken ct = default)
  {
    var authorized = await AuthorizeArchiveAsync(db, actor, archiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult.Fail(authorized.ErrorCode!, authorized.Message!);
    var archive = authorized.Value!;
    if (archive.Status != ArchiveStates.ProtectionObserved)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Observed records protection is required before verification.");
    var action = await db.RecordsActions.AsNoTracking().SingleOrDefaultAsync(x => x.ArchiveId == archive.Id, ct);
    var manifest = await db.ArchiveManifests.AsNoTracking().SingleOrDefaultAsync(x => x.ArchiveId == archive.Id, ct);
    if (action?.State != "OBSERVED" || manifest?.CompletenessStatus != "COMPLETE")
      return CommandResult.Fail(ErrorCodes.GateBlocked, "A complete manifest and observed protection are required.");
    await db.Archives.Where(x => x.Id == archive.Id && x.FirmId == archive.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ArchiveStates.ArchiveVerified), ct);
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> RequestLegalHoldAsync(
    IAuditSphereDbContext db, ActorContext actor, RequestLegalHoldRequest request,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.HoldReference))
      return CommandResult.Fail("legal-hold.invalid", "A hold reference is required.");
    var authorized = await AuthorizeArchiveAsync(db, actor, request.ArchiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult.Fail(authorized.ErrorCode!, authorized.Message!);
    var archive = authorized.Value!;
    db.LegalHolds.Add(new LegalHold
    {
      Id = Guid.CreateVersion7(), FirmId = archive.FirmId, ClientId = archive.ClientId,
      EngagementId = archive.EngagementId, ArchiveId = archive.Id, HoldReference = request.HoldReference.Trim(),
      RequestedByUserId = actor.UserId, RequestedAt = DateTimeOffset.UtcNow, Notes = request.Notes?.Trim()
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ObserveLegalHoldAsync(
    IAuditSphereDbContext db, ActorContext actor, ObserveLegalHoldRequest request,
    CancellationToken ct = default)
  {
    var authorized = await AuthorizeArchiveAsync(db, actor, request.ArchiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult.Fail(authorized.ErrorCode!, authorized.Message!);
    var hold = await db.LegalHolds.SingleOrDefaultAsync(x => x.ArchiveId == request.ArchiveId &&
      x.FirmId == actor.FirmId && x.HoldReference == request.HoldReference, ct);
    if (hold is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (hold.State == "RELEASED")
      return CommandResult.Fail(ErrorCodes.ProtectedState, "A released legal hold cannot be reactivated.");
    hold.State = "OBSERVED";
    hold.ExternalReference = request.ExternalReference?.Trim();
    hold.AppliedAt ??= DateTimeOffset.UtcNow;
    hold.ObservedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static async Task<CommandResult<Archive>> AuthorizeArchiveAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid archiveId, CancellationToken ct)
  {
    var archive = await db.Archives.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == archiveId && x.FirmId == actor.FirmId, ct);
    if (archive is null)
      return CommandResult<Archive>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, archive.ClientId, archive.EngagementId,
        RequiredRoles: CustodianRoles, InternalOnly: true), ct);
    return auth.Succeeded
      ? CommandResult<Archive>.Ok(archive)
      : CommandResult<Archive>.Fail(auth.ErrorCode!, auth.Message!);
  }

  private static async Task<string> BuildStructuredExportAsync(
    IAuditSphereDbContext db, Archive archive, CancellationToken ct)
  {
    var scope = new { archive.FirmId, archive.ClientId, archive.EngagementId };

    var documentReferences = await db.DocumentReferences.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.RepositoryBindingId, x.Provider, x.DriveId, x.ItemId, x.Path, x.Purpose, x.CreatedAt }).ToListAsync(ct);
    var documentSnapshots = await db.DocumentSnapshots.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.DocumentReferenceId, x.DriveId, x.ItemId, x.VersionId, x.Sha256Hex, x.ByteCount, x.CapturedBy, x.CapturedAt }).ToListAsync(ct);

    var sourceReceipts = await db.SourceReceipts.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceType, x.ReceiptToken, x.Sha256Digest, x.ByteCount, x.OriginalFileName, x.AcquiredAt }).ToListAsync(ct);
    var evidenceLinks = await db.EvidenceLinks.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceReceiptId, x.WorkpaperId, x.Purpose, x.Assertion, x.RelevanceReliabilityAssessment, x.CreatedAt }).ToListAsync(ct);

    var trialBalanceDatasets = await db.TrialBalanceDatasets.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SourceKind, x.Revision, x.Currency, x.Sha256Hex, x.Balanced, x.ValidationStatus, x.ControlTotal, x.ImportedAt }).ToListAsync(ct);
    var datasetIds = trialBalanceDatasets.Select(x => x.Id).ToArray();
    var trialBalanceRows = await db.TrialBalanceRows.AsNoTracking().Where(x => datasetIds.Contains(x.DatasetId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.DatasetId, x.AccountCode, x.AccountName, x.Amount, x.Currency, x.Entity, x.MappingCode }).ToListAsync(ct);
    var mappingRules = await db.MappingRules.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.MappingCode, x.SourcePattern, x.Revision, x.CreatedAt }).ToListAsync(ct);
    var mappingVersions = await db.MappingVersions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.DatasetId, x.Version, x.Generation, x.TaxonomyVersion, x.PeriodStart, x.PeriodEnd, x.Status, x.CreatedAt, x.ApprovedAt }).ToListAsync(ct);
    var mappingVersionIds = mappingVersions.Select(x => x.Id).ToArray();
    var mappingAllocations = await db.MappingAllocations.AsNoTracking().Where(x => mappingVersionIds.Contains(x.MappingVersionId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.MappingVersionId, x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.AuditArea, x.Fraction, x.Rationale, x.CreatedAt }).ToListAsync(ct);
    var adjustedSnapshots = await db.AdjustedTrialBalanceSnapshots.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BaseDatasetId, x.AdjustmentPlanId, x.Revision, x.Currency, x.ResultHash, x.CreatedAt }).ToListAsync(ct);
    var adjustedSnapshotIds = adjustedSnapshots.Select(x => x.Id).ToArray();
    var adjustedRows = await db.AdjustedTrialBalanceRows.AsNoTracking().Where(x => adjustedSnapshotIds.Contains(x.SnapshotId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SnapshotId, x.AccountCode, x.Amount, x.Currency }).ToListAsync(ct);

    var adjustmentPlans = await db.AdjustmentPlans.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BaseDatasetId, x.Status, x.ResultHash, x.AppliedDebits, x.AppliedCreditsAbs, x.AppliedJournalCount, x.CreatedAt }).ToListAsync(ct);
    var adjustmentPlanIds = adjustmentPlans.Select(x => x.Id).ToArray();
    var adjustmentPlanLines = await db.AdjustmentPlanLines.AsNoTracking().Where(x => adjustmentPlanIds.Contains(x.PlanId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.PlanId, x.LogicalJournalNumber, x.JournalRevision, x.Layer, x.ReflectionState }).ToListAsync(ct);
    var adjustmentJournals = await db.AdjustmentJournals.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BaseDatasetId, x.JournalNumber, x.Status, x.Revision, x.CreatedAt }).ToListAsync(ct);
    var adjustmentJournalIds = adjustmentJournals.Select(x => x.Id).ToArray();
    var adjustmentLines = await db.AdjustmentLines.AsNoTracking().Where(x => adjustmentJournalIds.Contains(x.JournalId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.JournalId, x.AccountCode, x.Debit, x.Credit }).ToListAsync(ct);
    var journalReconciliations = await db.JournalSourceReconciliations.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BaseDatasetId, x.LogicalJournalNumber, x.JournalRevision, x.State, x.Evidence, x.ReviewedAt, x.CreatedAt }).ToListAsync(ct);

    var packages = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AdjustedDatasetId, x.MappingVersionId, x.AdjustmentPlanId, x.Framework, x.PeriodStart, x.PeriodEnd, x.TaxonomyVersion, x.TemplateVersion, x.CalculationEngineVersion, x.CalculationHash, x.Currency, x.Revision, x.Generation, x.Status, x.SupplementaryHash, x.CreatedAt }).ToListAsync(ct);
    var packageIds = packages.Select(x => x.Id).ToArray();
    var packageLines = await db.FinancialPackageLines.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Amount, x.Fraction, x.Currency, x.AdjustedSnapshotId, x.CreatedAt }).ToListAsync(ct);
    var packageValidations = await db.FinancialPackageValidations.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.Code, x.Passed, x.Detail, x.CreatedAt }).ToListAsync(ct);
    var cashFlowLines = await db.FinancialPackageCashFlowLines.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.Section, x.Description, x.Amount, x.Currency, x.CreatedAt }).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.Code, x.Response, x.NotApplicable, x.Rationale, x.CreatedAt }).ToListAsync(ct);

    var materiality = await db.MaterialityAssessments.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BenchmarkSource, x.BenchmarkVersion, x.Rationale, x.BenchmarkAmount, x.RateApplied, x.OverallMateriality, x.PerformanceMateriality, x.ClearlyTrivialThreshold, x.QualitativeConsiderations, x.Status, x.CreatedAt }).ToListAsync(ct);
    var risks = await db.AuditRisks.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AccountArea, x.Assertion, x.Description, x.Drivers, x.Severity, x.SignificanceDecision, x.ControlsConsidered, x.ResponseDescription, x.Status, x.CreatedAt }).ToListAsync(ct);
    var riskIds = risks.Select(x => x.Id).ToArray();
    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => riskIds.Contains(x.RiskId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.RiskId, x.Title, x.Status, x.CreatedAt }).ToListAsync(ct);
    var populations = await db.PopulationVersions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Purpose, x.Assertion, x.SourceReceiptReference, x.ExtractionParameters, x.RowCount, x.MonetaryControlTotal, x.Currency, x.Exclusions, x.Status, x.CreatedAt }).ToListAsync(ct);
    var workpapers = await db.Workpapers.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProcedureId, x.Index, x.Title, x.Objective, x.TemplateVersion, x.Procedure, x.WorkPerformed, x.Conclusion, x.Revision, x.Status, x.SubmittedAt, x.CreatedAt }).ToListAsync(ct);
    var workpaperIds = workpapers.Select(x => x.Id).ToArray();
    var submissions = await db.WorkpaperSubmissions.AsNoTracking().Where(x => workpaperIds.Contains(x.WorkpaperId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.WorkpaperId, x.Revision, x.WorkPerformed, x.Conclusion, x.SubmittedAt }).ToListAsync(ct);
    var findings = await db.Findings.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FindingType, x.ImpactDescription, x.Corrected, x.MonetaryAmount, x.ManagementResponse, x.Status, x.CreatedAt }).ToListAsync(ct);
    var reviewPoints = await db.ReviewPoints.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.TargetId, x.TargetKind, x.TargetRevision, x.Comment, x.Significant, x.Cleared, x.RaisedAt }).ToListAsync(ct);

    var evaluationResponses = await db.EvaluationResponses.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.PracticeClientId == archive.ClientId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Bank, x.QuestionId, x.Answer, x.Revision, x.AnsweredAt }).ToListAsync(ct);
    var acceptanceDecisions = await db.AcceptanceDecisions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.PracticeClientId == archive.ClientId &&
        (x.EngagementId == null || x.EngagementId == archive.EngagementId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.EngagementId, x.Decision, x.ServiceRoute, x.Generation, x.DecidedAt }).ToListAsync(ct);
    var specialistClearances = await db.SpecialistClearances.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.PracticeClientId == archive.ClientId &&
        (x.EngagementId == null || x.EngagementId == archive.EngagementId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.EngagementId, x.Area, x.SpecialistName, x.Status, x.EvidenceReference, x.Conditions, x.ClearedAt, x.CreatedAt }).ToListAsync(ct);
    var questionnaireTemplates = await db.QuestionnaireTemplates.AsNoTracking().OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.Bank, x.Version, x.Name, x.IsActive, x.CreatedAt }).ToListAsync(ct);
    var questionnaireTemplateIds = questionnaireTemplates.Select(x => x.Id).ToArray();
    var questionDefinitions = await db.QuestionDefinitions.AsNoTracking().Where(x => questionnaireTemplateIds.Contains(x.TemplateId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.TemplateId, x.QuestionCode, x.Section, x.PromptText, x.Category, x.AnswerType, x.RequiresEvidence, x.SortOrder }).ToListAsync(ct);

    var approvals = await db.Approvals.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.TargetKind, x.TargetId, x.TargetRevision, x.InputGeneration, x.PolicyGeneration, x.ManifestDigest, x.Decision, x.DecidedAt }).ToListAsync(ct);
    var approvalIds = approvals.Select(x => x.Id).ToArray();
    var approvalApplicability = await db.ApprovalApplicabilities.AsNoTracking().Where(x => approvalIds.Contains(x.ApprovalId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ApprovalId, x.Status, x.Reason, x.CurrentTargetRevision, x.CurrentInputGeneration, x.CurrentPolicyGeneration, x.EvaluatedAt }).ToListAsync(ct);
    var engagementHolds = await db.EngagementHolds.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.HoldKind, x.Reason, x.Released, x.CreatedAt, x.ReleasedAt }).ToListAsync(ct);
    var assignments = await db.EngagementAssignments.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.UserId, x.Role, x.StartDate, x.EndDate, x.AllocatedHours, x.Active, x.CreatedAt }).ToListAsync(ct);
    var eqrCases = await db.EqrCases.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.EqrPartnerUserId, x.Status, x.ConcurrenceDate, x.FindingsDiscussed, x.CompletedAt, x.CreatedAt }).ToListAsync(ct);
    var writtenRepresentations = await db.WrittenRepresentations.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.Code, x.Title, x.Narrative, x.Obtained, x.ObtainedAt, x.SignatoryName }).ToListAsync(ct);

    var releaseCandidates = await db.ReleaseCandidates.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.TargetKind, x.TargetId, x.TargetRevision, x.Revision, x.InputGeneration, x.PolicyGeneration, x.ApprovalId, x.ManifestDigest, x.Status, x.CreatedAt }).ToListAsync(ct);
    var releases = await db.Releases.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ReleaseCandidateId, x.PackageId, x.PackageRevision, x.ManifestDigest, x.CheckpointId, x.ReleasedAt }).ToListAsync(ct);
    var releaseCheckpoints = await db.ReleaseCheckpoints.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ReleaseCandidateId, x.CandidateRevision, x.AuthorizedReleaseKey, x.ManifestDigest, x.StoredReference, x.ReadBackDigest, x.VerifiedStatus, x.VerifiedAt, x.CreatedAt }).ToListAsync(ct);
    var signatureLineages = await db.SignatureLineages.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.CandidateId, x.PreSignArtifactHash, x.SignedArtifactHash, x.SigningMethod, x.VerificationOutcome, x.Verifier, x.VerifiedAt, x.CreatedAt }).ToListAsync(ct);
    var protectionAttestations = await db.ProtectionAttestations.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ArtifactId, x.ArtifactHash, x.Binding, x.ProfileId, x.ProfileVersion, x.ObservedState, x.VerificationTime, x.Verifier, x.ExpiryTime, x.CreatedAt }).ToListAsync(ct);

    var operations = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationKind, x.TargetId, x.ExpectedRevision, x.Status, x.RequestDigest, x.ResultIdentity, x.ResultDigest, x.CreatedAt, x.CompletedAt }).ToListAsync(ct);
    var operationIds = operations.Select(x => x.Id).ToArray();
    var activityEvents = await db.OperationEvents.AsNoTracking().Where(x => operationIds.Contains(x.OperationId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.OperationId, x.Token, x.Kind, x.Executor, x.OccurredAt }).ToListAsync(ct);
    var recordsActions = await db.RecordsActions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.ArchiveId == archive.Id)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ArchiveManifestId, x.DesiredLabel, x.DesiredProtection, x.ObservedLabel, x.ObservedProtection, x.State, x.ExternalSystem, x.ExternalReference, x.RequestedAt, x.ObservedAt, x.ObservedBy, x.Exception }).ToListAsync(ct);
    var recordsActionIds = recordsActions.Select(x => x.Id).ToArray();
    var recordsActionEvidence = await db.RecordsActionEvidences.AsNoTracking().Where(x => recordsActionIds.Contains(x.RecordsActionId))
      .OrderBy(x => x.RecordsActionId).ThenBy(x => x.Sequence)
      .Select(x => new { x.Id, x.RecordsActionId, x.Sequence, x.EventKind, x.DesiredLabel, x.DesiredProtection, x.ObservedLabel, x.ObservedProtection, x.ExternalReference, x.ObservedBy, x.Exception, x.ActorUserId, x.OccurredAt }).ToListAsync(ct);
    var legalHolds = await db.LegalHolds.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && x.ArchiveId == archive.Id)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.HoldReference, x.State, x.ExternalSystem, x.ExternalReference, x.RequestedAt, x.AppliedAt, x.ObservedAt, x.ReleasedAt, x.Notes }).ToListAsync(ct);

    var export = new
    {
      schema = "records-export.v1",
      scope,
      documents = new { references = documentReferences, snapshots = documentSnapshots },
      sourceReceipts,
      evidenceLinks,
      assessments = new
      {
        materiality,
        questionnaireTemplates,
        questionDefinitions,
        evaluationResponses,
        acceptanceDecisions,
        specialistClearances
      },
      accounting = new
      {
        trialBalanceDatasets,
        trialBalanceRows,
        mappingRules,
        mappingVersions,
        mappingAllocations,
        adjustedSnapshots,
        adjustedRows,
        adjustmentPlans,
        adjustmentPlanLines,
        adjustmentJournals,
        adjustmentLines,
        journalReconciliations,
        packages,
        packageLines,
        packageValidations,
        cashFlowLines,
        disclosures
      },
      audit = new { risks, procedures, populations, workpapers, submissions, findings },
      review = new { reviewPoints, approvals, approvalApplicability, assignments, eqrCases, writtenRepresentations },
      controls = new { engagementHolds, recordsActions, recordsActionEvidence, legalHolds },
      release = new { releaseCandidates, releases, releaseCheckpoints, signatureLineages, protectionAttestations },
      activityEvents
    };
    return JsonSerializer.Serialize(export);
  }

  private static string? ValidateProfile(CreateRecordsProfileRequest request)
  {
    if (request.Version < 1 || request.RetentionDurationDays is <= 0)
      return "Version and retention duration must be positive when supplied.";
    var fields = new[] { request.ProfileCode, request.RecordClass, request.Jurisdiction, request.ServiceRoute,
      request.RetentionTrigger, request.ProtectionMode, request.LabelId, request.LegalHoldBehavior,
      request.AmendmentRoute, request.DispositionOwner, request.BackupRequirements };
    return fields.Any(string.IsNullOrWhiteSpace) ? "All records profile fields are required." : null;
  }
}
