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

public sealed record ReleaseLegalHoldRequest(Guid ArchiveId, string HoldReference, string? Notes);

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
    if (archive.Status is not (ArchiveStates.Issued or ArchiveStates.AssemblyInProgress or ArchiveStates.ManifestBuilt))
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
    // Predecessor: the current latest manifest (if any) becomes this version's predecessor.
    var predecessorManifest = manifestVersion > 1
      ? await db.ArchiveManifests
          .Where(x => x.FirmId == archive.FirmId && x.ArchiveId == archive.Id)
          .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct)
      : null;
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
      PredecessorManifestId = predecessorManifest?.Id,
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
    // Mark the prior manifest as superseded now that a new version exists.
    if (predecessorManifest is not null)
      predecessorManifest.SupersededByManifestId = manifest.Id;
    await db.SaveChangesAsync(ct);
    if (complete)
      await db.Archives.Where(x => x.Id == archive.Id && x.FirmId == archive.FirmId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ArchiveStates.ManifestBuilt), ct);
    await tx.CommitAsync(ct);

    var result = new ArchiveManifestResult(archive.Id, manifest.Id, manifest.Status, manifest.ManifestDigest,
      manifest.EntryCount, manifest.CompletenessStatus);
    return complete
      ? CommandResult<ArchiveManifestResult>.Ok(result)
      : CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.GateBlocked,
        "The archive manifest was stored but is incomplete; missing references must be resolved.");
  }

  public static async Task<CommandResult<ArchiveManifestResult>> ReviewManifestAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid archiveId, Guid? manifestId = null,
    CancellationToken ct = default)
  {
    var authorized = await AuthorizeArchiveAsync(db, actor, archiveId, ct);
    if (!authorized.Succeeded)
      return CommandResult<ArchiveManifestResult>.Fail(authorized.ErrorCode!, authorized.Message!);
    var archive = authorized.Value!;
    if (archive.Status != ArchiveStates.ManifestBuilt)
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.ProtectedState,
        "Only a complete built manifest can be reviewed.");

    var query = db.ArchiveManifests
      .Where(x => x.FirmId == actor.FirmId && x.ArchiveId == archive.Id);
    if (manifestId.HasValue)
      query = query.Where(x => x.Id == manifestId.Value);
    var manifest = await query.OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (manifest is null || manifest.CompletenessStatus != "COMPLETE")
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.GateBlocked, "A complete archive manifest is required.");
    if (manifest.SupersededByManifestId is not null)
      return CommandResult<ArchiveManifestResult>.Fail(ErrorCodes.ProtectedState,
        "This manifest has been superseded by a newer version and cannot be reviewed.");

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
    // Disposition is blocked while any active (applied/observed but not released) legal hold exists.
    var activeHold = await db.LegalHolds.AsNoTracking()
      .Where(x => x.ArchiveId == archiveId && x.FirmId == actor.FirmId && x.State == "OBSERVED" && x.ReleasedAt == null)
      .AnyAsync(ct);
    if (activeHold)
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "An active legal hold prevents archive verification. Release the hold before proceeding.");
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

  public static async Task<CommandResult> ReleaseLegalHoldAsync(
    IAuditSphereDbContext db, ActorContext actor, ReleaseLegalHoldRequest request,
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
      return CommandResult.Fail(ErrorCodes.ProtectedState, "This legal hold has already been released.");
    hold.State = "RELEASED";
    hold.ReleasedAt = DateTimeOffset.UtcNow;
    if (request.Notes is not null)
      hold.Notes = request.Notes.Trim();
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
      .OrderBy(x => x.Id).Select(x => new
      {
        x.Id, x.BaseDatasetId, x.JournalNumber, x.Status, x.Revision, x.Purpose, x.BookId, x.Origin,
        x.Reason, x.EvidenceReference, x.SupersedesJournalId, x.ReversalOfJournalId, x.CreatedAt
      }).ToListAsync(ct);
    var adjustmentJournalIds = adjustmentJournals.Select(x => x.Id).ToArray();
    var adjustmentLines = await db.AdjustmentLines.AsNoTracking().Where(x => adjustmentJournalIds.Contains(x.JournalId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.JournalId, x.AccountCode, x.Debit, x.Credit }).ToListAsync(ct);
    var journalReconciliations = await db.JournalSourceReconciliations.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BaseDatasetId, x.LogicalJournalNumber, x.JournalRevision, x.State, x.Evidence, x.ReviewedAt, x.CreatedAt }).ToListAsync(ct);

    var packages = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AdjustedDatasetId, x.MappingVersionId, x.AdjustmentPlanId, x.Framework, x.PeriodStart, x.PeriodEnd, x.TaxonomyVersion, x.TemplateVersion, x.CalculationEngineVersion, x.CalculationHash, x.Currency, x.Revision, x.Generation, x.Status, x.SupplementaryHash, x.EquityHash, x.ComparativePackageId, x.ComparativeBasis, x.ComparativeEvidenceReference, x.CreatedAt }).ToListAsync(ct);
    var packageIds = packages.Select(x => x.Id).ToArray();
    var packageLines = await db.FinancialPackageLines.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Amount, x.Fraction, x.Currency, x.AdjustedSnapshotId, x.CreatedAt }).ToListAsync(ct);
    var packageValidations = await db.FinancialPackageValidations.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.Code, x.Passed, x.Detail, x.CreatedAt }).ToListAsync(ct);
    var cashFlowLines = await db.FinancialPackageCashFlowLines.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.Section, x.Description, x.Amount, x.Currency, x.CreatedAt }).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking().Where(x => packageIds.Contains(x.FinancialPackageId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.FinancialPackageId, x.Code, x.Response, x.NotApplicable, x.Rationale, x.CreatedAt }).ToListAsync(ct);
    var typedAccounting = db as IClientAccountingDbContext;
    var clientProfiles = typedAccounting is null ? [] : await typedAccounting.ClientAccountingProfiles.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ClientId, x.Jurisdiction, x.FunctionalCurrency, x.FiscalYearStartMonth, x.FiscalYearStartDay, x.SourceSystem, x.SourceSystemIdentifier, x.Status, x.Revision, x.CreatedAt }).ToListAsync(ct);
    var reportingPeriods = typedAccounting is null ? [] : await typedAccounting.ClientReportingPeriods.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodCode, x.StartDate, x.EndDate, x.Basis, x.Currency, x.Status, x.PriorPeriodId, x.Revision, x.ClosedAt, x.CloseReason, x.CreatedAt }).ToListAsync(ct);
    var periodAmendments = typedAccounting is null ? [] : await typedAccounting.ClientPeriodAmendments.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.PreviousRevision, x.AmendmentRevision, x.Reason, x.CreatedByUserId, x.CreatedAt }).ToListAsync(ct);
    var reportingBooks = typedAccounting is null ? [] : await typedAccounting.ClientReportingBooks.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.Code, x.Basis, x.InclusionRule, x.Currency, x.Status, x.Revision, x.CreatedAt }).ToListAsync(ct);
    var openingBalanceBridges = typedAccounting is null ? [] : await typedAccounting.OpeningBalanceBridges.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.CurrentPeriodId, x.PriorPeriodId, x.SourcePackageId, x.SourceHash, x.PriorClosingAmount, x.CurrentOpeningAmount, x.Residual, x.Status, x.EvidenceReference, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt }).ToListAsync(ct);
    var periodRestatements = typedAccounting is null ? [] : await typedAccounting.ClientPeriodRestatements.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.OriginalPackageId, x.RevisedPackageId, x.OriginalPackageHash, x.RevisedPackageHash, x.RevisedBasis, x.Reason, x.EvidenceReference, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt }).ToListAsync(ct);
    var sourceImportBatches = typedAccounting is null ? [] : await typedAccounting.SourceImportBatches.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.BookId, x.SourceKind, x.ProfileVersion, x.ParserVersion, x.RawFileSha256Hex, x.NormalizedDatasetDigest, x.LegalEntityKey, x.Currency, x.RowCount, x.ExpectedChunkCount, x.ExpectedTransactionCount, x.ExpectedLineCount, x.AcceptedChunkCount, x.AcceptedTransactionCount, x.AcceptedLineCount, x.Status, x.ReceiptReference, x.CreatedAt }).ToListAsync(ct);
    var importBatchIds = sourceImportBatches.Select(x => x.Id).ToArray();
    var importChunks = typedAccounting is null ? [] : await typedAccounting.GeneralLedgerImportChunks.AsNoTracking()
      .Where(x => importBatchIds.Contains(x.ImportBatchId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ImportBatchId, x.ChunkNumber, x.ChunkDigest, x.TransactionCount, x.LineCount, x.CreatedAt }).ToListAsync(ct);
    var glTransactions = typedAccounting is null ? [] : await typedAccounting.GeneralLedgerTransactions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ImportBatchId, x.StableJournalId, x.DocumentNumber, x.PostingDate, x.DocumentDate, x.ServiceDate, x.SourceUser, x.SourceSystem, x.ReversalReference, x.Currency, x.IsManual, x.IsYearEnd, x.CreatedAt }).ToListAsync(ct);
    var glTransactionIds = glTransactions.Select(x => x.Id).ToArray();
    var glLines = typedAccounting is null ? [] : await typedAccounting.GeneralLedgerLines.AsNoTracking()
      .Where(x => glTransactionIds.Contains(x.TransactionId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ImportBatchId, x.TransactionId, x.StableLineId, x.AccountCode, x.ClientAccountId, x.Debit, x.Credit, x.OriginalCurrency, x.OriginalAmount, x.FunctionalAmount, x.PartyIdentifier, x.Branch, x.CostCentre, x.Department, x.Project, x.IntercompanyCounterparty, x.IsManual, x.IsYearEnd, x.CreatedAt }).ToListAsync(ct);
    var reconciliations = typedAccounting is null ? [] : await typedAccounting.AccountingReconciliations.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.BookId, x.Area, x.TrialBalanceDatasetId, x.ImportBatchId, x.AccountSelection, x.AsOfDate, x.SourceTotal, x.GlTotal, x.Residual, x.SourceHash, x.Status, x.InputGeneration, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var reconciliationIds = reconciliations.Select(x => x.Id).ToArray();
    var reconciliationItems = typedAccounting is null ? [] : await typedAccounting.AccountingReconciliationItems.AsNoTracking()
      .Where(x => reconciliationIds.Contains(x.ReconciliationId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ReconciliationId, x.StableItemId, x.SignedAmount, x.Currency, x.ItemDate, x.AgeDays, x.Reason, x.EvidenceReference, x.Disposition, x.CreatedAt }).ToListAsync(ct);
    var eclAssessments = typedAccounting is null ? [] : await typedAccounting.EclAssessments.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ReconciliationId, x.ReconciliationSourceHash, x.InputGeneration, x.Version, x.AsOfDate, x.Method, x.MethodologyVersion, x.EligibleExposure, x.ProbabilityOfDefault, x.LossGivenDefault, x.ManagementOverlay, x.CalculatedExpectedLoss, x.ManagementExpectedLoss, x.Difference, x.AssumptionsHash, x.Status, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var inventoryAssessments = typedAccounting is null ? [] : await typedAccounting.InventoryValuationAssessments.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ReconciliationId, x.ReconciliationSourceHash, x.InputGeneration, x.Version, x.AsOfDate, x.Quantity, x.UnitCost, x.NrvPerUnit, x.ObsolescenceReserve, x.BookAmount, x.CalculatedAmount, x.Difference, x.MethodologyVersion, x.AssumptionsHash, x.Status, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var specialistSchedules = typedAccounting is null ? [] : await typedAccounting.SpecialistAccountingSchedules.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.InputGeneration, x.Area, x.MethodologyVersion, x.DepreciationMethod, x.UsefulLifeMonths, x.PayrollGrossAmount, x.PayrollDeductionsAmount, x.PayrollNetAmount, x.PayrollContractReference, x.PayrollBankPaymentReference, x.LoanRepaymentAmount, x.LoanMaturityDate, x.LoanCovenantReference, x.EquityProfitOrLossAmount, x.EquityOciAmount, x.RelatedPartyDisclosureReference, x.TaxJurisdiction, x.TaxRuleVersion, x.TaxBaseAmount, x.TaxRate, x.TaxReturnEvidenceReference, x.TaxPaymentEvidenceReference, x.TaxCorrespondenceReference, x.ForecastOwner, x.ForecastHorizonEnd, x.ForecastCashInputAmount, x.ForecastDebtInputAmount, x.ForecastSensitivityReference, x.ForecastSensitivityResult, x.OpeningAmount, x.AdditionsAmount, x.DisposalsAmount, x.DepreciationAmount, x.ImpairmentAmount, x.InterestAmount, x.CurrentPortion, x.NonCurrentPortion, x.CapitalMovement, x.Dividends, x.TaxPaid, x.ManagementAmount, x.CalculatedAmount, x.ClosingAmount, x.Difference, x.AssumptionsHash, x.EvidenceReference, x.ReviewConclusion, x.Status, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var analyticalReviews = typedAccounting is null ? [] : await typedAccounting.AnalyticalReviews.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.PeriodId, x.InputGeneration, x.ComparisonPeriodId, x.Area, x.Measure, x.CurrentAmount, x.PriorAmount, x.BudgetAmount, x.Ratio, x.DenominatorBasis, x.FormulaVersion, x.InputHash, x.Explanation, x.Status, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var journalRiskFlags = typedAccounting is null ? [] : await typedAccounting.JournalRiskFlags.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.ImportBatchId, x.TransactionId, x.RuleCode, x.Reason, x.Score, x.Status, x.Disposition, x.EvidenceReference, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var accountingEvidenceAuditLinks = typedAccounting is null ? [] : await typedAccounting.AccountingEvidenceAuditLinks.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.EvidenceKind, x.EvidenceId, x.AuditProcedureResultId, x.LinkedByUserId, x.CreatedAt }).ToListAsync(ct);
    var packageReviewDecisions = typedAccounting is null ? [] : await typedAccounting.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.FinancialPackageId, x.PackageRevision, x.PackageGeneration, x.PackageHash, x.Stage, x.Decision, x.EvidenceMode, x.EvidenceReference, x.Comment, x.DecidedByUserId, x.DecidedAt }).ToListAsync(ct);
    var equityLines = typedAccounting is null ? [] : await typedAccounting.FinancialPackageEquityLines.AsNoTracking()
      .Where(x => packageIds.Contains(x.FinancialPackageId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.FinancialPackageId, x.LineCode, x.Description, x.OpeningAmount, x.ProfitOrLossAmount, x.OciAmount, x.CapitalMovementAmount, x.DividendsAmount, x.ClosingAmount, x.Currency, x.EvidenceReference, x.CreatedAt }).ToListAsync(ct);
    var noteLines = typedAccounting is null ? [] : await typedAccounting.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => packageIds.Contains(x.FinancialPackageId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.FinancialPackageId, x.NoteCode, x.FaceDestinationCode, x.Amount, x.Currency, x.EvidenceReference, x.CreatedAt }).ToListAsync(ct);

    var groupMembershipIds = typedAccounting is null ? [] : await typedAccounting.ClientGroupMemberships.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId)
      .Select(x => x.GroupId).Distinct().ToArrayAsync(ct);
    var componentGroupIds = typedAccounting is null ? [] : await typedAccounting.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .Select(x => x.GroupId).Distinct().ToArrayAsync(ct);
    var groupIds = groupMembershipIds.Concat(componentGroupIds).Distinct().ToArray();
    var clientGroups = typedAccounting is null ? [] : await typedAccounting.ClientGroups.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && groupIds.Contains(x.Id)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.Code, x.Name, x.Status, x.Revision, x.CreatedByUserId, x.CreatedAt }).ToListAsync(ct);
    var groupMemberships = typedAccounting is null ? [] : await typedAccounting.ClientGroupMemberships.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && groupIds.Contains(x.GroupId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ClientId, x.EffectiveFrom, x.EffectiveTo, x.ControlMethod, x.OwnershipPercent, x.EconomicInterestPercent, x.EvidenceReference, x.Status, x.Revision, x.CreatedByUserId, x.CreatedAt }).ToListAsync(ct);
    var scopeVersions = typedAccounting is null ? [] : await typedAccounting.ConsolidationScopeVersions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && groupIds.Contains(x.GroupId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.GroupRevision, x.PeriodId, x.PriorScopeVersionId, x.OpeningRunHash, x.OpeningTranslationManifestHash, x.OpeningTranslationReserve, x.RecurringEliminationManifest, x.Version, x.ReportingCurrency, x.Method, x.Status, x.OpeningBasis, x.ExchangeRateSetVersionId, x.TranslationPolicyVersionId, x.TranslationRateDate, x.TranslationRateType, x.CreatedByUserId, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt }).ToListAsync(ct);
    var scopeIds = scopeVersions.Select(x => x.Id).ToArray();
    var consolidationComponents = typedAccounting is null ? [] : await typedAccounting.ConsolidationComponents.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && scopeIds.Contains(x.ScopeVersionId) && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.ClientId, x.EngagementId, x.PackageId, x.PackageHash, x.PeriodBasis, x.TaxonomyVersion, x.MappingVersion, x.Currency, x.OwnershipPercent, x.ControlMethod, x.Status, x.SubmittedByUserId, x.ApprovedByUserId, x.SubmittedAt, x.ApprovedAt }).ToListAsync(ct);
    var componentIds = consolidationComponents.Select(x => x.Id).ToArray();
    var ownershipInterests = typedAccounting is null ? [] : await typedAccounting.OwnershipInterestVersions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && scopeIds.Contains(x.ScopeVersionId) && (x.ParentClientId == archive.ClientId || x.ChildClientId == archive.ClientId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.ParentClientId, x.ChildClientId, x.EffectiveFrom, x.EffectiveTo, x.OwnershipPercent, x.EconomicInterestPercent, x.ControlAssessment, x.Method, x.EvidenceReference, x.Status, x.CreatedAt }).ToListAsync(ct);
    var intercompanyMatches = typedAccounting is null ? [] : await typedAccounting.IntercompanyMatches.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && scopeIds.Contains(x.ScopeVersionId) && (x.SellerClientId == archive.ClientId || x.BuyerClientId == archive.ClientId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.SellerClientId, x.BuyerClientId, x.AccountNature, x.SellerTaxonomyCode, x.BuyerTaxonomyCode, x.PeriodCode, x.Currency, x.TransactionReference, x.SellerAmount, x.BuyerAmount, x.MatchedAmount, x.Difference, x.Status, x.DifferenceReason, x.EvidenceReference, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var consolidationJournals = typedAccounting is null ? [] : await typedAccounting.ConsolidationJournals.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && scopeIds.Contains(x.ScopeVersionId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.JournalNumber, x.JournalType, x.Currency, x.TotalDebits, x.TotalCreditsAbs, x.EvidenceReference, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt }).ToListAsync(ct);
    var consolidationJournalIds = consolidationJournals.Select(x => x.Id).ToArray();
    var consolidationJournalLines = typedAccounting is null ? [] : await typedAccounting.ConsolidationJournalLines.AsNoTracking()
      .Where(x => consolidationJournalIds.Contains(x.ConsolidationJournalId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.ConsolidationJournalId, x.IntercompanyMatchId, x.TaxonomyCode, x.Debit, x.Credit, x.Currency, x.Description, x.CreatedAt }).ToListAsync(ct);
    var consolidationRuns = typedAccounting is null ? [] : await typedAccounting.ConsolidationRuns.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && scopeIds.Contains(x.ScopeVersionId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.EngineVersion, x.InputManifest, x.RunHash, x.ReportingCurrency, x.SignedTotal, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt }).ToListAsync(ct);
    var consolidationRunIds = consolidationRuns.Select(x => x.Id).ToArray();
    var consolidationRunLines = typedAccounting is null ? [] : await typedAccounting.ConsolidationRunLines.AsNoTracking()
      .Where(x => consolidationRunIds.Contains(x.RunId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.RunId, x.ComponentId, x.SourceLineId, x.IntercompanyMatchId, x.ConsolidationJournalId, x.TaxonomyCode, x.ComponentAmount, x.AlignmentAmount, x.EliminationAmount, x.ConsolidatedAmount, x.Currency, x.CreatedAt }).ToListAsync(ct);
    var rateSetIds = scopeVersions.Where(x => x.ExchangeRateSetVersionId.HasValue).Select(x => x.ExchangeRateSetVersionId!.Value).Distinct().ToArray();
    var translationPolicyIds = scopeVersions.Where(x => x.TranslationPolicyVersionId.HasValue).Select(x => x.TranslationPolicyVersionId!.Value).Distinct().ToArray();
    var exchangeRateSets = typedAccounting is null ? [] : await typedAccounting.ExchangeRateSetVersions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && rateSetIds.Contains(x.Id)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.Code, x.Source, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt }).ToListAsync(ct);
    var exchangeRates = typedAccounting is null ? [] : await typedAccounting.ExchangeRates.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && rateSetIds.Contains(x.RateSetVersionId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.RateSetVersionId, x.FromCurrency, x.ToCurrency, x.RateDate, x.RateType, x.Rate, x.Direction, x.CreatedAt }).ToListAsync(ct);
    var translationPolicies = typedAccounting is null ? [] : await typedAccounting.TranslationPolicyVersions.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && translationPolicyIds.Contains(x.Id)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.Code, x.FunctionalCurrency, x.PresentationCurrency, x.ClosingRateRule, x.AverageRateRule, x.HistoricalRateRule, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt }).ToListAsync(ct);
    var translationResults = typedAccounting is null ? [] : await typedAccounting.TranslationResults.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && scopeIds.Contains(x.ScopeVersionId) && componentIds.Contains(x.ComponentId)).OrderBy(x => x.Id)
      .Select(x => new { x.Id, x.GroupId, x.ScopeVersionId, x.ComponentId, x.RateSetVersionId, x.TranslationPolicyVersionId, x.SourcePackageHash, x.RateDate, x.RateType, x.AppliedRate, x.FromCurrency, x.ToCurrency, x.TranslatedAmount, x.TranslationReserve, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.ApprovedAt, x.CreatedAt }).ToListAsync(ct);

    var materiality = await db.MaterialityAssessments.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.BenchmarkSource, x.BenchmarkVersion, x.Rationale, x.BenchmarkAmount, x.RateApplied, x.OverallMateriality, x.PerformanceMateriality, x.ClearlyTrivialThreshold, x.QualitativeConsiderations, x.Status, x.CreatedAt }).ToListAsync(ct);
    var risks = await db.AuditRisks.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AccountArea, x.Assertion, x.Description, x.Drivers, x.Severity, x.SignificanceDecision, x.ControlsConsidered, x.ResponseDescription, x.Status, x.CreatedAt }).ToListAsync(ct);
    var engagementPrograms = await db.EngagementAuditPrograms.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProgramVersionId, x.Status, x.AdoptedByUserId, x.AdoptedAt }).ToListAsync(ct);
    var programVersionIds = engagementPrograms.Select(x => x.ProgramVersionId).Distinct().ToArray();
    var programVersions = await db.AuditProgramVersions.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && programVersionIds.Contains(x.Id))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProgramCode, x.Version, x.SourceHash, x.Status, x.CreatedByUserId, x.ApprovedByUserId, x.CreatedAt, x.ApprovedAt }).ToListAsync(ct);
    var programProcedures = await db.AuditProgramProcedures.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && programVersionIds.Contains(x.ProgramVersionId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProgramVersionId, x.SourceProcedureId, x.SectionNumber, x.SectionTitle, x.Ordinal, x.SourceWording, x.ApplicabilityCondition, x.ExpectedEvidence, x.CreatedAt }).ToListAsync(ct);
    var procedures = await db.AuditProcedures.AsNoTracking()
      .Where(x => x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.RiskId, x.EngagementProgramId, x.ProgramProcedureId, x.SourceProcedureId, x.SourceSectionNumber, x.SourceSectionTitle, x.SourceWording, x.ApplicabilityStatus, x.ApplicabilityRationale, x.Title, x.Status, x.CurrentResultRevision, x.CreatedAt }).ToListAsync(ct);
    var procedureIds = procedures.Select(x => x.Id).ToArray();
    var procedureResults = await db.AuditProcedureResults.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && procedureIds.Contains(x.AuditProcedureId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AuditProcedureId, x.WorkpaperId, x.Revision, x.InputGeneration, x.WorkPerformed, x.StructuredResultJson, x.EvidenceReferencesJson, x.Conclusion, x.Status, x.PreparedByUserId, x.ReviewedByUserId, x.SubmittedAt, x.ReviewedAt }).ToListAsync(ct);
    var procedureReviews = await db.AuditProcedureReviews.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && procedureIds.Contains(x.AuditProcedureId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.AuditProcedureResultId, x.AuditProcedureId, x.ResultRevision, x.Decision, x.Comment, x.ReviewerUserId, x.CreatedAt }).ToListAsync(ct);
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
    var schedules = await db.AuditSchedules.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ScheduleType, x.EntityIdentifier, x.SourceReceiptReference, x.AsOfDate, x.PeriodStart, x.PeriodEnd, x.Currency, x.SignConvention, x.SourceHash, x.RowCount, x.SignedControlTotal, x.GlControlTotal, x.Residual, x.InputGeneration, x.CompletenessDecision, x.Status, x.CreatedByUserId, x.CreatedAt }).ToListAsync(ct);
    var scheduleIds = schedules.Select(x => x.Id).ToArray();
    var scheduleRows = await db.AuditScheduleRows.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && scheduleIds.Contains(x.ScheduleId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ScheduleId, x.StableRowId, x.SourceLineNumber, x.AccountCode, x.Description, x.SignedAmount, x.Currency, x.TransactionDate, x.PostingDate, x.DeliveryDate, x.ServiceDate, x.OriginalValuesJson, x.CreatedAt }).ToListAsync(ct);
    var selections = await db.AuditSelections.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ScheduleId, x.PopulationVersionId, x.ProcedureId, x.Method, x.Rationale, x.SelectedCount, x.SelectedSignedTotal, x.Status, x.InputGeneration, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var selectionIds = selections.Select(x => x.Id).ToArray();
    var selectionItems = await db.AuditSelectionItems.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && selectionIds.Contains(x.SelectionId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SelectionId, x.ScheduleRowId, x.StableRowId, x.SignedAmount, x.Currency, x.InclusionReason, x.CreatedAt }).ToListAsync(ct);
    var selectionItemIds = selectionItems.Select(x => x.Id).ToArray();
    var itemTests = await db.AuditItemTests.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && selectionItemIds.Contains(x.SelectionItemId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SelectionId, x.SelectionItemId, x.ProcedureId, x.Revision, x.WorkPerformed, x.EvidenceReferencesJson, x.Result, x.ExceptionAmount, x.ContradictoryEvidence, x.FollowUp, x.InputGeneration, x.TestedByUserId, x.TestedAt }).ToListAsync(ct);
    var itemTestIds = itemTests.Select(x => x.Id).ToArray();
    var itemTestReviews = await db.AuditItemTestReviews.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && itemTestIds.Contains(x.AuditItemTestId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.SelectionItemId, x.AuditItemTestId, x.TestRevision, x.Decision, x.Comment, x.ReviewerUserId, x.CreatedAt }).ToListAsync(ct);
    var confirmationCases = await db.AuditConfirmationCases.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProcedureId, x.AreaCode, x.SourceRecordId, x.BookedAmount, x.Currency, x.ConfirmationDate, x.Respondent, x.ContactValidationSource, x.InputGeneration, x.Status, x.DispatchReference, x.CreatedByUserId, x.CreatedAt }).ToListAsync(ct);
    var confirmationIds = confirmationCases.Select(x => x.Id).ToArray();
    var confirmationResponses = await db.AuditConfirmationResponses.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && confirmationIds.Contains(x.ConfirmationCaseId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ConfirmationCaseId, x.Revision, x.Origin, x.Channel, x.ReceivedAt, x.ResponseReference, x.ConfirmedAmount, x.DifferenceAmount, x.AuthenticityAssessment, x.Decision, x.CreatedByUserId, x.ReviewedByUserId, x.ReviewedAt, x.CreatedAt }).ToListAsync(ct);
    var alternatives = await db.AuditAlternativeProcedures.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId && confirmationIds.Contains(x.ConfirmationCaseId))
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ConfirmationCaseId, x.Purpose, x.EvidenceReferencesJson, x.Conclusion, x.Status, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var areaAssessments = await db.AuditAreaAssessments.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new { x.Id, x.ProcedureId, x.AreaCode, x.AssessmentKind, x.MethodologyReference, x.InputSnapshotJson, x.BookedAmount, x.AuditedAmount, x.ResidualAmount, x.VariancePercent, x.Currency, x.PeriodStart, x.PeriodEnd, x.EvidenceReferencesJson, x.InputGeneration, x.Revision, x.Conclusion, x.Status, x.CreatedByUserId, x.ReviewedByUserId, x.CreatedAt, x.ReviewedAt }).ToListAsync(ct);
    var differences = await db.AuditDifferences.AsNoTracking().Where(x =>
      x.FirmId == archive.FirmId && x.ClientId == archive.ClientId && x.EngagementId == archive.EngagementId)
      .OrderBy(x => x.Id).Select(x => new
      {
        x.Id, x.ProcedureId, x.AccountArea, x.DifferenceType, x.Description, x.Amount, x.Currency, x.Corrected,
        x.ManagementResponse, x.CorrectionReference, x.ProposedJournalId, x.ProposedJournalRevision,
        x.SourceReflectionReconciliationId, x.VerifiedAdjustedSnapshotId, x.CorrectionState, x.JournalImpactJson,
        x.JournalImpactHash, x.Evaluation, x.InputGeneration, x.Status, x.CreatedByUserId, x.EvaluatedByUserId,
        x.CreatedAt, x.EvaluatedAt
      }).ToListAsync(ct);
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
        clientProfiles,
        reportingPeriods,
        periodAmendments,
        reportingBooks,
        openingBalanceBridges,
        periodRestatements,
        sourceImportBatches,
        importChunks,
        glTransactions,
        glLines,
        reconciliations,
        reconciliationItems,
        eclAssessments,
        inventoryAssessments,
        specialistSchedules,
        analyticalReviews,
        journalRiskFlags,
        accountingEvidenceAuditLinks,
        packageReviewDecisions,
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
        disclosures,
        equityLines,
        noteLines,
        clientGroups,
        groupMemberships,
        scopeVersions,
        consolidationComponents,
        ownershipInterests,
        intercompanyMatches,
        consolidationJournals,
        consolidationJournalLines,
        consolidationRuns,
        consolidationRunLines,
        exchangeRateSets,
        exchangeRates,
        translationPolicies,
        translationResults
      },
      audit = new
      {
        risks,
        programVersions,
        programProcedures,
        engagementPrograms,
        procedures,
        procedureResults,
        procedureReviews,
        populations,
        workpapers,
        submissions,
        findings,
        schedules,
        scheduleRows,
        selections,
        selectionItems,
        itemTests,
        itemTestReviews,
        confirmationCases,
        confirmationResponses,
        alternatives,
        areaAssessments,
        differences
      },
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
