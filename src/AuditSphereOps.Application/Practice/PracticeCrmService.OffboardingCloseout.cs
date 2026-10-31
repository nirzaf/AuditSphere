using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ClientOffboardingCloseoutRequest(
  Guid ClientId,
  string DispositionNotes,
  IReadOnlyList<string>? AcknowledgedActionItems = null,
  long ExpectedRevision = 0,
  bool ArchiveClientOnCloseout = false);

public sealed record ClientOffboardingCloseoutResult(
  Guid RecordId,
  Guid ClientId,
  string Status,
  string ReadinessOverallState,
  string ReadinessDigest,
  string DispositionNotes,
  IReadOnlyList<string> AcknowledgedActionItems,
  Guid DecidedByUserId,
  DateTimeOffset DecidedAt,
  bool IsArchived,
  long ArchiveRevision);

public static partial class PracticeCrmService
{
  private static readonly JsonSerializerOptions OffboardingJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false
  };

  public static async Task<CommandResult<ClientOffboardingCloseoutResult>> RecordOffboardingCloseoutAsync(
    IAuditSphereDbContext db, ActorContext actor, ClientOffboardingCloseoutRequest request, CancellationToken ct = default)
  {
    if (request.ClientId == Guid.Empty)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid", "A client is required.");
    if (string.IsNullOrWhiteSpace(request.DispositionNotes))
      return CommandResult<ClientOffboardingCloseoutResult>.Fail("crm.invalid", "Disposition notes are required to record offboarding closeout.");

    var gate = new AuthorizationRequest(actor.FirmId, ClientId: request.ClientId, RequiredRoles: CommercialRoles, InternalOnly: true);
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, gate, ct);
    if (!auth.Succeeded)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(auth.ErrorCode!, auth.Message!);

    var clientExists = await db.PracticeClients.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == request.ClientId, ct);
    if (!clientExists)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.ScopeDenied, "Client unavailable.");

    var readinessResult = await GetClientOffboardingReadinessAsync(db, actor, request.ClientId, ct);
    if (!readinessResult.Succeeded)
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(readinessResult.ErrorCode!, readinessResult.Message!);

    var readiness = readinessResult.Value!;
    if (readiness.OverallState == ClientOffboardingStates.Unknown ||
        readiness.Sections.Any(s => s.State == ClientOffboardingStates.Unknown))
    {
      return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GateBlocked,
        "Cannot record offboarding closeout while obligations remain unassessed (UNKNOWN). Complete required reviews or verify viewer authority.");
    }

    if (readiness.OverallState == ClientOffboardingStates.ActionRequired)
    {
      var requiredKeys = readiness.Sections.SelectMany(s => s.Items)
        .Where(i => i.State == ClientOffboardingStates.ActionRequired)
        .Select(i => i.Key).ToHashSet();
      var ackedKeys = (request.AcknowledgedActionItems ?? Array.Empty<string>()).ToHashSet();

      if (!requiredKeys.IsSubsetOf(ackedKeys))
      {
        return CommandResult<ClientOffboardingCloseoutResult>.Fail(ErrorCodes.GateBlocked,
          "Every action-required obligation must be explicitly acknowledged in the closeout request.");
      }
    }

    var snapshotJson = JsonSerializer.Serialize(readiness, OffboardingJsonOptions);
    var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshotJson))).ToLowerInvariant();
    var ackedList = request.AcknowledgedActionItems ?? Array.Empty<string>();

    var record = new ClientOffboardingRecord
    {
      Id = Guid.CreateVersion7(),
      FirmId = actor.FirmId,
      ClientId = request.ClientId,
      Status = ClientOffboardingRecordStates.Completed,
      ReadinessOverallState = readiness.OverallState,
      ReadinessSnapshotJson = snapshotJson,
      ReadinessDigest = digest,
      DispositionNotes = request.DispositionNotes.Trim(),
      AcknowledgedActionItemsJson = JsonSerializer.Serialize(ackedList, OffboardingJsonOptions),
      DecidedByUserId = actor.UserId,
      DecidedAt = DateTimeOffset.UtcNow
    };

    db.ClientOffboardingRecords.Add(record);
    await db.SaveChangesAsync(ct);

    var isArchived = readiness.IsArchived;
    var archiveRevision = long.TryParse(readiness.ArchiveRevision, out var rev) ? rev : 0;

    if (request.ArchiveClientOnCloseout && !isArchived)
    {
      var archiveResult = await SetClientArchivedAsync(db, actor,
        new SetClientArchiveRequest(request.ClientId, true, request.ExpectedRevision), ct);
      if (archiveResult.Succeeded)
      {
        isArchived = archiveResult.Value!.IsArchived;
        archiveRevision = archiveResult.Value.Revision;
      }
    }

    return CommandResult<ClientOffboardingCloseoutResult>.Ok(new ClientOffboardingCloseoutResult(
      record.Id, record.ClientId, record.Status, record.ReadinessOverallState, record.ReadinessDigest,
      record.DispositionNotes, ackedList, record.DecidedByUserId, record.DecidedAt, isArchived, archiveRevision));
  }
}

