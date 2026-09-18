using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Documents;

public sealed record CaptureDocumentSnapshotRequest(
  Guid DocumentReferenceId,
  string VersionId,
  byte[] Content);

public sealed record DocumentSnapshotReceipt(
  Guid SnapshotId,
  string VersionId,
  string Sha256Hex,
  long ByteCount);

/// <summary>
/// Captures the exact bytes supplied by the trusted document boundary. Provider
/// retrieval and artifact storage remain outside this local integrity slice.
/// </summary>
public static class DocumentSnapshotService
{
  private const long MaxSnapshotBytes = 250L * 1024 * 1024;

  public static async Task<CommandResult<DocumentSnapshotReceipt>> CaptureAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    CaptureDocumentSnapshotRequest request,
    CancellationToken ct = default)
  {
    var validation = Validate(request);
    if (validation is not null)
      return CommandResult<DocumentSnapshotReceipt>.Fail("documents.invalid", validation);

    var reference = await db.DocumentReferences.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.DocumentReferenceId && x.FirmId == actor.FirmId, ct);
    if (reference is null)
      return CommandResult<DocumentSnapshotReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, reference.ClientId, reference.EngagementId,
        InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<DocumentSnapshotReceipt>.Fail(auth.ErrorCode!, auth.Message!);

    var hash = Hashing.Sha256Hex(request.Content);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var current = await db.DocumentReferences.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == request.DocumentReferenceId && x.FirmId == actor.FirmId, ct);
    if (current is null)
      return CommandResult<DocumentSnapshotReceipt>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    if (await db.DocumentSnapshots.AnyAsync(x => x.FirmId == actor.FirmId &&
        x.DocumentReferenceId == current.Id && x.VersionId == request.VersionId, ct))
      return CommandResult<DocumentSnapshotReceipt>.Fail("documents.snapshot-duplicate",
        "That document version already has a snapshot.");

    var snapshot = new DocumentSnapshot
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ClientId = current.ClientId,
      EngagementId = current.EngagementId,
      DocumentReferenceId = current.Id,
      DriveId = current.DriveId,
      ItemId = current.ItemId,
      VersionId = request.VersionId,
      Sha256Hex = hash,
      ByteCount = request.Content.LongLength,
      CapturedBy = actor.UserId.ToString("D"),
      CapturedAt = DateTimeOffset.UtcNow
    };
    db.DocumentSnapshots.Add(snapshot);
    try
    {
      await db.SaveChangesAsync(ct);
      await tx.CommitAsync(ct);
    }
    catch (DbUpdateException)
    {
      return CommandResult<DocumentSnapshotReceipt>.Fail("documents.snapshot-conflict",
        "The snapshot identity changed; retry from the current document version.");
    }

    return CommandResult<DocumentSnapshotReceipt>.Ok(new DocumentSnapshotReceipt(
      snapshot.Id, snapshot.VersionId, snapshot.Sha256Hex, snapshot.ByteCount));
  }

  private static string? Validate(CaptureDocumentSnapshotRequest request)
  {
    if (request.DocumentReferenceId == Guid.Empty)
      return "A document reference is required.";
    if (string.IsNullOrWhiteSpace(request.VersionId) || request.VersionId != request.VersionId.Trim() ||
        request.VersionId.Length > 200)
      return "The provider version identity is required and bounded.";
    if (request.Content is null)
      return "Snapshot bytes are required.";
    if (request.Content.LongLength > MaxSnapshotBytes)
      return "Snapshot bytes exceed the bounded capture limit.";
    return null;
  }
}
