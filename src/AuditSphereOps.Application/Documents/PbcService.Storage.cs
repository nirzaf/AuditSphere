using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record PbcReadDispositionRequest(Guid UploadIntentId, Guid ExpectedRegistrationId, long ExpectedRevision, string Reason);

public sealed record PbcStorageDispositionReceipt(Guid DispositionId, Guid UploadIntentId, Guid RegistrationId,
  string ReadSource, string LocalCopy, long Revision);

/// <summary>
/// Per-upload SharePoint-primary read disposition (cohort activation and pre-cleanup rollback). A disposition is
/// storage state, not a business or review decision: it never changes the upload or request revisions an
/// acceptance receipt pins. Remote reads stay off until an administrator activates an exactly registered upload.
/// </summary>
public static partial class PbcService
{
  private const int MaxDispositionReasonLength = 500;

  /// <summary>Activates remote reads for one registered upload. <c>ExpectedRevision</c> is 0 for a first activation.
  /// A replay of an already-active disposition returns its current receipt.</summary>
  public static Task<CommandResult<PbcStorageDispositionReceipt>> ActivateRemoteReadAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcReadDispositionRequest request, CancellationToken ct = default) =>
    ChangeReadSourceAsync(db, actor, request, PbcReadSources.SelectedSite, ct);

  /// <summary>Returns an activated upload to its retained local copy. Refused once that copy has been released.</summary>
  public static Task<CommandResult<PbcStorageDispositionReceipt>> RevertToLocalReadAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcReadDispositionRequest request, CancellationToken ct = default) =>
    ChangeReadSourceAsync(db, actor, request, PbcReadSources.LocalStaged, ct);

  private static async Task<CommandResult<PbcStorageDispositionReceipt>> ChangeReadSourceAsync(
    IAuditSphereDbContext db, ActorContext actor, PbcReadDispositionRequest request, string target, CancellationToken ct)
  {
    var reason = request?.Reason?.Trim();
    if (request is null || request.UploadIntentId == Guid.Empty || request.ExpectedRegistrationId == Guid.Empty ||
        request.ExpectedRevision < 0 || string.IsNullOrEmpty(reason) || reason.Length > MaxDispositionReasonLength)
      return CommandResult<PbcStorageDispositionReceipt>.Fail("pbc.storage-invalid",
        "An exact registered upload, its current disposition revision and a bounded reason are required.");
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<PbcStorageDispositionReceipt>();

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    // Serialize every disposition change for this upload (activation, revert and cleanup) on the upload row.
    var intents = await db.PbcUploadIntents.FromSqlInterpolated($"""
      SELECT * FROM pbc_upload_intents WHERE firm_id = {actor.FirmId} AND id = {request.UploadIntentId} FOR UPDATE
      """).AsNoTracking().ToListAsync(ct);
    if (intents.Count != 1) return CommandResult<PbcStorageDispositionReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var intent = intents[0];
    if (!await TenantAdministration.IsCurrentAdministratorAsync(db, actor, ct))
      return TenantAdministration.Denied<PbcStorageDispositionReceipt>();
    var registration = await db.PbcDocumentRegistrations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PbcUploadIntentId == intent.Id, ct);
    if (registration is null || registration.Id != request.ExpectedRegistrationId || intent.State != PbcUploadStates.Received)
      return CommandResult<PbcStorageDispositionReceipt>.Fail("pbc.storage-unregistered",
        "Only an upload with this exact verified SharePoint registration can change its read source.");

    var disposition = await db.PbcStorageDispositions.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PbcUploadIntentId == intent.Id, ct);
    var currentSource = disposition?.ReadSource ?? PbcReadSources.LocalStaged;
    if (currentSource == target)
      return (disposition?.Revision ?? 0) == request.ExpectedRevision
        ? CommandResult<PbcStorageDispositionReceipt>.Ok(Receipt(disposition, intent, registration, target))
        : Stale();
    if ((disposition?.Revision ?? 0) != request.ExpectedRevision) return Stale();
    if (target == PbcReadSources.LocalStaged && disposition!.LocalCopy != PbcLocalCopyStates.Retained)
      return CommandResult<PbcStorageDispositionReceipt>.Fail("pbc.storage-local-released",
        "The local copy was released; this upload can only be read from its registered SharePoint version.");

    var now = DateTimeOffset.UtcNow;
    if (disposition is null)
    {
      disposition = new PbcStorageDisposition
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = intent.ClientId, EngagementId = intent.EngagementId,
        PbcUploadIntentId = intent.Id, PbcDocumentRegistrationId = registration.Id, ReadSource = target,
        LocalCopy = PbcLocalCopyStates.Retained, Revision = 1, UpdatedAt = now
      };
      db.PbcStorageDispositions.Add(disposition);
    }
    else
    {
      disposition.ReadSource = target;
      disposition.Revision++;
      disposition.UpdatedAt = now;
    }
    db.PbcStorageEvents.Add(new PbcStorageEvent
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = intent.ClientId, EngagementId = intent.EngagementId,
      PbcStorageDispositionId = disposition.Id, Revision = disposition.Revision,
      Kind = target == PbcReadSources.SelectedSite ? PbcStorageEventKinds.RemoteReadActivated : PbcStorageEventKinds.RemoteReadReverted,
      ReadSource = disposition.ReadSource, LocalCopy = disposition.LocalCopy, ActorUserId = actor.UserId, Reason = reason,
      OccurredAt = now
    });
    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
    catch (DbUpdateException)
    {
      return Stale();
    }
    return CommandResult<PbcStorageDispositionReceipt>.Ok(Receipt(disposition, intent, registration, target));

    static CommandResult<PbcStorageDispositionReceipt> Stale() =>
      CommandResult<PbcStorageDispositionReceipt>.Fail(ErrorCodes.GenerationStale,
        "The upload's storage disposition changed; review its current revision.");
  }

  private static PbcStorageDispositionReceipt Receipt(PbcStorageDisposition? disposition, PbcUploadIntent intent,
    PbcDocumentRegistration registration, string readSource) =>
    new(disposition?.Id ?? Guid.Empty, intent.Id, registration.Id, readSource,
      disposition?.LocalCopy ?? PbcLocalCopyStates.Retained, disposition?.Revision ?? 0);
}
