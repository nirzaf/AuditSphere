using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public static partial class AuditProgramService
{
  public const string EvidenceSnapshotProperty = "_auditEvidenceSnapshot";
  internal const int MaximumEvidenceSnapshotBytes = 50_000;
  private static readonly JsonSerializerOptions EvidenceJson = new(JsonSerializerDefaults.Web);

  internal sealed record ResolvedProcedureEvidence(string Json, string Digest);

  private sealed record EvidenceSnapshotV1(int Version, IReadOnlyList<ResolvedEvidenceEntry> Selections,
    IReadOnlyList<LegacyEvidenceReference> LegacyReferences);
  private sealed record ResolvedEvidenceEntry(string Kind, Guid? LinkId, Guid? UploadIntentId, Guid? PbcRequestId,
    string? FileName, string? ContentSha256, string? Note, Guid? PhysicalLinkId, Guid? PhysicalItemId,
    string? FileIndex, string? BoxReference, string? Description, string? LocationAtSubmission,
    string? ManualReference, string? Observation, Guid RecordedByUserId, DateTimeOffset RecordedAt);
  private sealed record LegacyEvidenceReference(string Kind, string Reference, Guid RecordedByUserId, DateTimeOffset RecordedAt);

  private static bool EvidenceText(string? value, int max) => !string.IsNullOrWhiteSpace(value) && value.Length <= max &&
    !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));

  internal static bool HasReservedEvidenceProperty(string structuredResultJson)
  {
    try
    {
      using var document = JsonDocument.Parse(structuredResultJson);
      return document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.EnumerateObject()
        .Any(x => string.Equals(x.Name, EvidenceSnapshotProperty, StringComparison.OrdinalIgnoreCase));
    }
    catch (JsonException) { return true; }
  }

  internal static string WithEvidenceSnapshot(string structuredResultJson, string snapshotJson)
  {
    using var document = JsonDocument.Parse(structuredResultJson);
    using var snapshot = JsonDocument.Parse(snapshotJson);
    using var stream = new MemoryStream();
    using (var writer = new Utf8JsonWriter(stream))
    {
      writer.WriteStartObject();
      foreach (var property in document.RootElement.EnumerateObject())
        if (!string.Equals(property.Name, EvidenceSnapshotProperty, StringComparison.OrdinalIgnoreCase))
          property.WriteTo(writer);
      writer.WritePropertyName(EvidenceSnapshotProperty);
      snapshot.RootElement.WriteTo(writer);
      writer.WriteEndObject();
    }
    return Encoding.UTF8.GetString(stream.ToArray());
  }

  internal static string? SubmittedEvidence(string structuredResultJson)
  {
    try
    {
      using var document = JsonDocument.Parse(structuredResultJson);
      if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
      var matches = document.RootElement.EnumerateObject()
        .Where(x => string.Equals(x.Name, EvidenceSnapshotProperty, StringComparison.OrdinalIgnoreCase)).ToArray();
      return matches.Length == 1 && matches[0].Value.ValueKind == JsonValueKind.Object ? matches[0].Value.GetRawText() : null;
    }
    catch (JsonException) { return null; }
  }

  internal static string? SubmittedEvidenceDigest(string structuredResultJson)
  {
    var snapshot = SubmittedEvidence(structuredResultJson);
    return snapshot is null ? null : Hashing.Sha256Hex(snapshot);
  }

  internal static async Task<CommandResult<ResolvedProcedureEvidence>> ResolveEvidenceSnapshotAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid firmId, Guid clientId, Guid engagementId, Guid procedureId,
    IReadOnlyList<ProcedureEvidenceSelection>? selections, IReadOnlyList<string> legacyReferences, CancellationToken ct)
  {
    var chosen = selections ?? [];
    if (chosen.Count > 50 || chosen.Any(x => x is null))
      return CommandResult<ResolvedProcedureEvidence>.Fail(ErrorCodes.AuditPlanning.Invalid,
        "Select at most 50 bounded evidence references.");
    var entries = new List<ResolvedEvidenceEntry>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    var now = DateTimeOffset.UtcNow;
    foreach (var selection in chosen)
    {
      var kind = selection.Kind?.Trim().ToUpperInvariant();
      if (kind == ProcedureEvidenceSelectionKinds.StoredLink)
      {
        if (selection.LinkId is not { } linkId || linkId == Guid.Empty || !seen.Add($"L:{linkId}"))
          return InvalidEvidence();
        var link = await db.ProcedureEvidenceLinks.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
          x.ProcedureId == procedureId && x.Id == linkId, ct);
        if (link is null) return InvalidEvidence();
        var upload = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == firmId && x.Id == link.PbcUploadIntentId, ct);
        if (upload is null || upload.ClientId != clientId || upload.EngagementId != engagementId ||
            upload.State != PbcUploadStates.Received)
          return CommandResult<ResolvedProcedureEvidence>.Fail(ErrorCodes.GateBlocked,
            "The selected link is not a current received source.");
        var siblings = await db.PbcUploadIntents.AsNoTracking().Where(x =>
          x.FirmId == firmId && x.PbcRequestId == upload.PbcRequestId && x.State == PbcUploadStates.Received).ToListAsync(ct);
        if (siblings.Any(x => x.Id != upload.Id && x.CreatedAt > upload.CreatedAt &&
            string.Equals(x.FileName, upload.FileName, StringComparison.OrdinalIgnoreCase)))
          return CommandResult<ResolvedProcedureEvidence>.Fail(ErrorCodes.GenerationStale,
            "A newer source version was received; select the current version before submitting.");
        entries.Add(new(ProcedureEvidenceSelectionKinds.StoredLink, link.Id, link.PbcUploadIntentId,
          link.PbcRequestId, link.FileName, link.ContentSha256, link.Note, null, null, null, null, null,
          null, null, null, link.LinkedByUserId, link.LinkedAt));
      }
      else if (kind == ProcedureEvidenceSelectionKinds.PhysicalLink)
      {
        if (selection.PhysicalLinkId is not { } physicalLinkId || physicalLinkId == Guid.Empty ||
            !seen.Add($"P:{physicalLinkId}")) return InvalidEvidence();
        var link = await db.ProcedurePhysicalLinks.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
          x.ProcedureId == procedureId && x.Id == physicalLinkId, ct);
        if (link is null) return InvalidEvidence();
        var item = await db.PhysicalEvidenceItems.AsNoTracking().SingleOrDefaultAsync(x =>
          x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
          x.Id == link.PhysicalEvidenceItemId, ct);
        if (item is null) return InvalidEvidence();
        entries.Add(new(ProcedureEvidenceSelectionKinds.PhysicalLink, null, null, null, null, null, null,
          link.Id, item.Id, item.FileIndex, item.BoxReference, item.Description, item.CurrentLocation,
          null, null, link.LinkedByUserId, link.LinkedAt));
      }
      else if (kind == ProcedureEvidenceSelectionKinds.ManualReference)
      {
        if (!EvidenceText(selection.ManualReference, 500) || !EvidenceText(selection.Observation, 4000))
          return InvalidEvidence();
        var key = "M:" + selection.ManualReference!.Trim() + "|" + selection.Observation!.Trim();
        if (!seen.Add(key)) return InvalidEvidence();
        entries.Add(new(ProcedureEvidenceSelectionKinds.ManualReference, null, null, null, null, null, null,
          null, null, null, null, null, null, selection.ManualReference!.Trim(), selection.Observation!.Trim(),
          actor.UserId, now));
      }
      else return InvalidEvidence();
    }

    var legacy = new List<LegacyEvidenceReference>();
    if (legacyReferences.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 2000))
      return InvalidEvidence();
    foreach (var reference in legacyReferences)
      legacy.Add(new("LEGACY_REFERENCE", reference.Trim(), actor.UserId, now));

    var snapshot = new EvidenceSnapshotV1(1, entries, legacy);
    var json = JsonSerializer.Serialize(snapshot, EvidenceJson);
    if (Encoding.UTF8.GetByteCount(json) > MaximumEvidenceSnapshotBytes)
      return CommandResult<ResolvedProcedureEvidence>.Fail(ErrorCodes.AuditPlanning.Invalid,
        "The selected evidence snapshot is too large.");
    return CommandResult<ResolvedProcedureEvidence>.Ok(new(json, Hashing.Sha256Hex(json)));
  }

  private static CommandResult<ResolvedProcedureEvidence> InvalidEvidence() =>
    CommandResult<ResolvedProcedureEvidence>.Fail(ErrorCodes.ScopeDenied,
      "Selected evidence must belong to this exact procedure and scope, or be a complete bounded manual reference.");
}
