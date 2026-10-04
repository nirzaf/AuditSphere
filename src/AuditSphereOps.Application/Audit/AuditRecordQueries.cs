using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record PopulationSelectionView(string Method, string Status, string Rationale, int ItemCount, int TestCount, int ReviewedTestCount);
public sealed record PopulationEvidenceView(string Purpose, string Assertion, string ReceiptToken);
public sealed record PopulationRecord(Guid Id, Guid EngagementId, string Purpose, string Assertion, string SourceReceiptReference, string ExtractionParameters, int RowCount,
  decimal MonetaryControlTotal, string Currency, string? Exclusions, string Status, DateTimeOffset CreatedAt, IReadOnlyList<PopulationSelectionView> Selections,
  IReadOnlyList<PopulationEvidenceView> Evidence);
public sealed record FindingRecordView(Guid Id, Guid EngagementId, string FindingType, string ImpactDescription, decimal? MonetaryAmount, bool Corrected,
  string? ManagementResponse, string Status, DateTimeOffset CreatedAt, bool ProfessionalWorkBlocked,
  DateTimeOffset? LetterDesignatedAt, string? LetterRecommendation);
public sealed record ReviewPointView(Guid Id, Guid EngagementId, string TargetKind, Guid TargetId, long TargetRevision, Guid RaisedByUserId, DateTimeOffset RaisedAt,
  bool Significant, bool Cleared, string Comment, string Status);
public sealed record ReleaseGateView(string State, string Detail);
public sealed record ReleaseCandidateView(Guid Id, string Status, string TargetKind, long TargetRevision, long Revision, string ManifestDigest, DateTimeOffset CreatedAt,
  ReleaseGateView Checkpoint, ReleaseGateView Attestation, ReleaseGateView Lineage, bool PreflightReady);
public sealed record ArchiveEntryView(int Ordinal, string EntryKind, string RelativeName, string ContentHash, long ByteCount);
public sealed record ArchiveRecordView(Guid Id, Guid EngagementId, string Status, DateTimeOffset CreatedAt, string ProfileId, long ProfileVersion, long? ManifestVersion,
  string? ManifestDigest, string? CompletenessStatus, string? CompletenessException, IReadOnlyList<ArchiveEntryView> Entries, int ActiveHoldCount,
  string? ObservedProtection, string? DesiredLabel, string? ObservedLabel, string? ActionState, string? ExternalReference);

public sealed record WorkpaperSubmissionView(long Revision, DateTimeOffset SubmittedAt, string? Conclusion);
public sealed record WorkpaperRecordView(Guid Id, Guid EngagementId, string Index, string Title, string Objective, string TemplateVersion, string Procedure,
  string? LinkedProcedureTitle, long Revision, string Status, string? WorkPerformed, string? Conclusion, DateTimeOffset CreatedAt, DateTimeOffset? SubmittedAt,
  IReadOnlyList<WorkpaperSubmissionView> Submissions, WorkpaperDraftResult Draft);

/// <summary>
/// Exact-record projections for audit populations, findings, review points, release candidates and records archives.
/// Each authorizes the record's own client and engagement; an out-of-scope record is indistinguishable from a missing one.
/// </summary>
public static class AuditRecordQueries
{
  private static readonly string[] AuditRoles = ["Administrator", "Partner", "Manager", "SeniorManager", "Senior", "Staff", "EngagementLeader", "Auditor"];

  private static async Task<bool> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, Guid clientId, Guid engagementId, string[] roles, CancellationToken ct) =>
    (await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, clientId, engagementId, roles, InternalOnly: true), ct)).Succeeded;

  public static async Task<CommandResult<PopulationRecord>> PopulationAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var p = await db.PopulationVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (p is null || !await AuthorizeAsync(db, actor, p.ClientId, p.EngagementId, AuditRoles, ct)) return CommandResult<PopulationRecord>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var evidence = await db.EvidenceLinks.AsNoTracking().Where(e => e.FirmId == actor.FirmId && e.ClientId == p.ClientId && e.EngagementId == p.EngagementId)
      .OrderByDescending(e => e.CreatedAt).ToListAsync(ct);
    var receiptIds = evidence.Select(e => e.SourceReceiptId).Distinct().ToArray();
    var tokens = await db.SourceReceipts.AsNoTracking().Where(r => r.FirmId == actor.FirmId && r.ClientId == p.ClientId && r.EngagementId == p.EngagementId && receiptIds.Contains(r.Id))
      .ToDictionaryAsync(r => r.Id, r => r.ReceiptToken, ct);
    var selections = await db.AuditSelections.AsNoTracking().Where(s => s.FirmId == actor.FirmId && s.ClientId == p.ClientId && s.EngagementId == p.EngagementId &&
      s.PopulationVersionId == p.Id).OrderBy(s => s.CreatedAt).ToListAsync(ct);
    var selectionIds = selections.Select(s => s.Id).ToArray();
    var items = await db.AuditSelectionItems.AsNoTracking().Where(i => i.FirmId == actor.FirmId && i.ClientId == p.ClientId && i.EngagementId == p.EngagementId &&
      selectionIds.Contains(i.SelectionId)).Select(i => new { i.Id, i.SelectionId }).ToListAsync(ct);
    var itemIds = items.Select(i => i.Id).ToArray();
    var tests = await db.AuditItemTests.AsNoTracking().Where(t => t.FirmId == actor.FirmId && t.ClientId == p.ClientId && t.EngagementId == p.EngagementId &&
      itemIds.Contains(t.SelectionItemId)).Select(t => new { t.Id, t.SelectionId }).ToListAsync(ct);
    var testIds = tests.Select(t => t.Id).ToArray();
    var reviewed = await db.AuditItemTestReviews.AsNoTracking().Where(r => r.FirmId == actor.FirmId && r.ClientId == p.ClientId && r.EngagementId == p.EngagementId &&
      testIds.Contains(r.AuditItemTestId) && r.Decision == AuditItemTestReviewDecisions.Reviewed).Select(r => r.AuditItemTestId).Distinct().ToListAsync(ct);
    return CommandResult<PopulationRecord>.Ok(new(p.Id, p.EngagementId, p.Purpose, p.Assertion, p.SourceReceiptReference, p.ExtractionParameters, p.RowCount,
      p.MonetaryControlTotal, p.Currency, p.Exclusions, p.Status, p.CreatedAt,
      selections.Select(s => new PopulationSelectionView(s.Method, s.Status, s.Rationale, items.Count(i => i.SelectionId == s.Id), tests.Count(t => t.SelectionId == s.Id),
        tests.Count(t => t.SelectionId == s.Id && reviewed.Contains(t.Id)))).ToList(),
      evidence.Select(e => new PopulationEvidenceView(e.Purpose, e.Assertion, tokens.GetValueOrDefault(e.SourceReceiptId, "unavailable"))).ToList()));
  }

  public static async Task<CommandResult<WorkpaperRecordView>> WorkpaperAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var w = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (w is null || !await AuthorizeAsync(db, actor, w.ClientId, w.EngagementId, AuditRoles, ct)) return CommandResult<WorkpaperRecordView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var draft = await AuditPlanningService.LoadWorkpaperDraftAsync(db, actor, w.Id, ct);
    if (!draft.Succeeded || draft.Value is null) return CommandResult<WorkpaperRecordView>.Fail(draft.ErrorCode ?? ErrorCodes.ScopeDenied, draft.Message ?? "Access denied.");
    var procedure = w.ProcedureId is { } pid ? await db.AuditProcedures.AsNoTracking()
      .Where(p => p.Id == pid && p.FirmId == actor.FirmId && p.ClientId == w.ClientId && p.EngagementId == w.EngagementId).Select(p => p.Title).FirstOrDefaultAsync(ct) : null;
    var submissions = await db.WorkpaperSubmissions.AsNoTracking().Where(s => s.FirmId == actor.FirmId && s.ClientId == w.ClientId && s.EngagementId == w.EngagementId &&
      s.WorkpaperId == w.Id).OrderByDescending(s => s.Revision).Select(s => new WorkpaperSubmissionView(s.Revision, s.SubmittedAt, s.Conclusion)).ToListAsync(ct);
    return CommandResult<WorkpaperRecordView>.Ok(new(w.Id, w.EngagementId, w.Index, w.Title, w.Objective, w.TemplateVersion, w.Procedure, procedure, w.Revision, w.Status,
      w.WorkPerformed, w.Conclusion, w.CreatedAt, w.SubmittedAt, submissions, draft.Value));
  }

  public static async Task<CommandResult<FindingRecordView>> FindingAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var f = await db.Findings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (f is null || !await AuthorizeAsync(db, actor, f.ClientId, f.EngagementId, AuditRoles, ct)) return CommandResult<FindingRecordView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var blocked = await db.Engagements.AsNoTracking().Where(e => e.Id == f.EngagementId && e.FirmId == actor.FirmId).Select(e => e.ProfessionalWorkBlocked).SingleOrDefaultAsync(ct);
    return CommandResult<FindingRecordView>.Ok(new(f.Id, f.EngagementId, f.FindingType, f.ImpactDescription, f.MonetaryAmount, f.Corrected, f.ManagementResponse, f.Status, f.CreatedAt, blocked,
      f.LetterDesignatedAt, f.LetterRecommendation));
  }

  public static async Task<CommandResult<ReviewPointView>> ReviewPointAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var r = await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (r is null || !await AuthorizeAsync(db, actor, r.ClientId, r.EngagementId, ["Administrator", "Partner", "Manager", "Senior", "Staff", "EngagementLeader", "Auditor"], ct))
      return CommandResult<ReviewPointView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<ReviewPointView>.Ok(new(r.Id, r.EngagementId, r.TargetKind, r.TargetId, r.TargetRevision, r.RaisedByUserId, r.RaisedAt, r.Significant, r.Cleared,
      r.Comment, r.Cleared ? "CLEARED" : r.Significant ? "BLOCKING" : "OPEN"));
  }

  public static async Task<CommandResult<ReleaseCandidateView>> ReleaseCandidateAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, ReleaseSafetyOptions safety,
    DateTimeOffset now, CancellationToken ct = default)
  {
    var c = await db.ReleaseCandidates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (c is null || !await AuthorizeAsync(db, actor, c.ClientId, c.EngagementId, ["Administrator", "Partner", "Manager", "Reviewer", "Staff"], ct))
      return CommandResult<ReleaseCandidateView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var checkpoint = await db.ReleaseCheckpoints.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ReleaseCandidateId == id &&
      x.CandidateRevision == c.Revision && x.ManifestDigest == c.ManifestDigest, ct);
    var attestation = await db.ProtectionAttestations.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ArtifactHash == c.ManifestDigest, ct);
    var lineage = await db.SignatureLineages.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.CandidateId == id, ct);
    var checkpointGate = checkpoint is null ? new ReleaseGateView("ABSENT", "No external checkpoint recorded")
      : checkpoint.VerifiedStatus == "VERIFIED" ? new ReleaseGateView("VERIFIED", "Stored reference: " + checkpoint.StoredReference) : new ReleaseGateView(checkpoint.VerifiedStatus, checkpoint.VerifiedStatus);
    var attestationCurrent = attestation is { ObservedState: "PROTECTED" } && (attestation.ExpiryTime is null || attestation.ExpiryTime > now);
    var attestationGate = attestation is null ? new ReleaseGateView("ABSENT", "No protection attestation recorded for this release profile")
      : attestation.ObservedState == "PROTECTED"
        ? attestationCurrent ? new ReleaseGateView("VERIFIED", $"Profile {attestation.ProfileId} (v{attestation.ProfileVersion})")
          : new ReleaseGateView("EXPIRED", $"Profile {attestation.ProfileId} (v{attestation.ProfileVersion})")
        : new ReleaseGateView(attestation.ObservedState, attestation.ObservedState);
    var lineageGate = lineage is null ? new ReleaseGateView("ABSENT", "Unsigned candidate (optional in local mode)")
      : lineage.VerificationOutcome == "VERIFIED" ? new ReleaseGateView("VERIFIED", lineage.SigningMethod) : new ReleaseGateView(lineage.VerificationOutcome, lineage.VerificationOutcome);
    var ready = checkpoint?.VerifiedStatus == "VERIFIED" && (!safety.RequireProtectionAttestation || attestationCurrent) &&
      (!safety.RequireSignatureLineage || lineage?.VerificationOutcome == "VERIFIED");
    return CommandResult<ReleaseCandidateView>.Ok(new(c.Id, c.Status, c.TargetKind, c.TargetRevision, c.Revision, c.ManifestDigest, c.CreatedAt, checkpointGate, attestationGate,
      lineageGate, ready));
  }

  public static async Task<CommandResult<ArchiveRecordView>> ArchiveAsync(IAuditSphereDbContext db, ActorContext actor, Guid id, CancellationToken ct = default)
  {
    var a = await db.Archives.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (a is null || !await AuthorizeAsync(db, actor, a.ClientId, a.EngagementId,
        ["Administrator", "Partner", "Manager", "EngagementLeader", "Senior", "Staff", "Auditor", "RecordsCustodian"], ct))
      return CommandResult<ArchiveRecordView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var manifest = await db.ArchiveManifests.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ArchiveId == a.Id).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    var entries = manifest is null ? [] : await db.ArchiveManifestEntries.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ArchiveManifestId == manifest.Id)
      .OrderBy(x => x.Ordinal).Select(x => new ArchiveEntryView(x.Ordinal, x.EntryKind, x.RelativeName, x.ContentHash, x.ByteCount)).ToListAsync(ct);
    var action = await db.RecordsActions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ArchiveId == a.Id, ct);
    var holds = await db.LegalHolds.AsNoTracking().CountAsync(x => x.FirmId == actor.FirmId && x.ArchiveId == a.Id && x.State != "RELEASED", ct);
    return CommandResult<ArchiveRecordView>.Ok(new(a.Id, a.EngagementId, a.Status, a.CreatedAt, a.ProfileId, a.ProfileVersion, manifest?.Version, manifest?.ManifestDigest,
      manifest?.CompletenessStatus, manifest?.CompletenessException, entries, holds, action?.ObservedProtection, action?.DesiredLabel, action?.ObservedLabel, action?.State,
      action?.ExternalReference));
  }
}
