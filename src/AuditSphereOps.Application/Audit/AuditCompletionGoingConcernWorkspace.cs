using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record GoingConcernAssessmentFields(Guid? ProcedureId, DateOnly AssessmentDate, DateOnly PeriodCoveredTo,
  string ForecastReviewOutcome, bool? MaterialUncertaintyIdentified, bool? DisclosureAdequate, string Currency,
  string Rationale, IReadOnlyList<string> EvidenceReferences);

public sealed record GoingConcernAssessmentCommandRequest(Guid RequestId, string ExpectedRevision, string ReviewBasis,
  GoingConcernAssessmentFields? Fields = null, string? ExpectedRequestHash = null, bool Reviewed = false);

public sealed record GoingConcernAssessmentSnapshot(Guid Id, Guid ClientId, Guid EngagementId, Guid? ProcedureId,
  DateOnly AssessmentDate, DateOnly PeriodCoveredTo, string ForecastReviewOutcome, bool MaterialUncertaintyIdentified,
  bool DisclosureAdequate, string Conclusion, string Currency, string Rationale, IReadOnlyList<string> EvidenceReferences,
  string Revision, Guid RecordedByUserId, Guid? ReviewedByUserId, DateTimeOffset RecordedAt,
  DateTimeOffset? ReviewedAt, string? ReviewedRevision);

public sealed record GoingConcernAssessmentCommandState(Guid EngagementId, Guid ClientId, DateOnly AssessmentDate, string ExpectedRevision, string ReviewBasis, GoingConcernAssessmentSnapshot? Record,
  bool CanPrepare, bool CanReview, IReadOnlyList<string> Blockers);

public sealed record GoingConcernAssessmentCommandPreview(Guid RequestId, string Action, string RequestHash,
  string ReviewBasis, string ExpectedRevision, bool CanProceed, IReadOnlyList<string> Blockers,
  GoingConcernAssessmentSnapshot? CurrentRecord, string Summary);

public sealed record GoingConcernAssessmentCommandReceiptView(Guid Id, Guid EngagementId, Guid GoingConcernAssessmentId,
  Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash, string Action, DateTimeOffset CreatedAt,
  GoingConcernAssessmentCommandPreview Preview, GoingConcernAssessmentSnapshot Result);

public sealed record GoingConcernAssessmentCommandLookup(bool Found, GoingConcernAssessmentCommandReceiptView? Receipt);
public sealed record GoingConcernReviewReceiptContext(Guid RequestId, string RequestHash, string ReviewBasis,
  string InputJson, string PreviewJson, long ExpectedRevision, string? EvidenceIdentityHash);

/// <summary>Exact-scope product commands and immutable recovery for the existing going-concern completion record.</summary>
public static class AuditCompletionGoingConcernWorkspace
{
  private const string Prepare = "PREPARE";
  private const string Review = "REVIEW";
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private const int MaxEvidenceJsonLength = 20_000;
  private const int MaxReceiptInputJsonLength = AuditCompletionGoingConcernCommandReceipt.MaximumInputJsonLength;

  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Revision(string? value, out long revision) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision >= 0;

  internal static GoingConcernAssessmentSnapshot Snapshot(GoingConcernAssessment row)
  {
    string[] evidence;
    try { evidence = JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? []; }
    catch (JsonException) { evidence = []; }
    return new(row.Id, row.ClientId, row.EngagementId, row.ProcedureId, row.AssessmentDate, row.PeriodCoveredTo,
      row.ForecastReviewOutcome, row.MaterialUncertaintyIdentified, row.DisclosureAdequate, row.Conclusion,
      row.Currency, row.Rationale, evidence, Long(row.Revision), row.RecordedByUserId, row.ReviewedByUserId,
      row.RecordedAt, row.ReviewedAt, row.ReviewedRevision is { } reviewed ? Long(reviewed) : null);
  }

  internal static string[] EvidenceReferences(GoingConcernAssessment row)
  {
    try { return JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? []; }
    catch (JsonException) { return []; }
  }

  private sealed record CurrentBasis(Guid ClientId, long EngagementGeneration, long ClientInputGeneration,
    bool ProfessionalWorkBlocked, bool ActiveHold, bool Frozen, GoingConcernAssessment? Record,
    IReadOnlyList<GoingConcernAssessment> Candidates);

  private static async Task<CommandResult<CurrentBasis>> ReadBasisAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid engagementId, DateOnly assessmentDate, CancellationToken ct)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<CurrentBasis>(ErrorCodes.ScopeDenied, "Access denied.");
    var read = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!read.Succeeded) return Fail<CurrentBasis>(read.ErrorCode!, read.Message!);
    var clientGeneration = await db.ClientSafetyStates.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Id == engagement.PracticeClientId)
      .Select(x => (long?)x.InputGeneration).SingleOrDefaultAsync(ct) ?? 0;
    var held = await db.EngagementHolds.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && !x.Released, ct);
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId &&
      x.State == AuditSphereOps.Domain.Records.FileFreezeStates.Frozen, ct);
    var candidates = await db.GoingConcernAssessments.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId).ToListAsync(ct);
    var record = candidates.SingleOrDefault(x => x.AssessmentDate == assessmentDate);
    return CommandResult<CurrentBasis>.Ok(new(engagement.PracticeClientId, engagement.Generation, clientGeneration,
      engagement.ProfessionalWorkBlocked, held, frozen, record, candidates));
  }

  private static string BasisHash(ActorContext actor, Guid engagementId, DateOnly assessmentDate, CurrentBasis basis) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch,
      engagementId, basis.ClientId, basis.EngagementGeneration, basis.ClientInputGeneration,
      basis.ProfessionalWorkBlocked, basis.ActiveHold, basis.Frozen, assessmentDate,
      Candidates = basis.Candidates.OrderBy(x => x.Id).Select(Snapshot),
      Record = basis.Record is null ? null : Snapshot(basis.Record) }));

  public static async Task<CommandResult<GoingConcernAssessmentCommandState>> StateAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, DateOnly assessmentDate, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || assessmentDate == default)
      return Fail<GoingConcernAssessmentCommandState>(ErrorCodes.Accounting.MappingInvalid, "Select an exact assessment date.");
    var first = await ReadBasisAsync(db, actor, engagementId, assessmentDate, ct);
    if (!first.Succeeded) return Fail<GoingConcernAssessmentCommandState>(first.ErrorCode!, first.Message!);
    var basis = first.Value!;
    var planning = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, basis.ClientId, engagementId, PlanningRoles, InternalOnly: true), ct);
    var review = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, basis.ClientId, engagementId, ReviewRoles, InternalOnly: true), ct);
    var writable = !basis.ProfessionalWorkBlocked && !basis.ActiveHold && !basis.Frozen;
    var evidenceReady = false;
    if (basis.Record is { } evidenceRecord)
    {
      var references = EvidenceReferences(evidenceRecord);
      if (references.Length > 0)
      {
        var evidence = await ValidateEvidenceAsync(db, actor, basis.ClientId, engagementId,
          Fields(evidenceRecord), ct);
        evidenceReady = evidence.Succeeded;
      }
    }
    var canPrepare = planning.Succeeded && writable && (basis.Record is null || basis.Record.RecordedByUserId == actor.UserId);
    var canReview = review.Succeeded && writable && basis.Record is { } row && row.RecordedByUserId != actor.UserId &&
      row.Conclusion is not (GoingConcernConclusions.NotAssessed or GoingConcernConclusions.InadequateDisclosure) && row.ReviewedByUserId is null && evidenceReady;
    var blockers = new List<string>();
    if (GoingConcernAssessmentSelection.Select(basis.Candidates).Ambiguous)
      blockers.Add(GoingConcernAssessmentSelection.AmbiguousBlocker);
    if (!writable) blockers.Add(basis.Frozen ? "The engagement file is frozen." : basis.ActiveHold ? "Professional work is held on this engagement." : "Professional work is blocked on this engagement.");
    if (basis.Record is { } current)
    {
      if (current.RecordedByUserId != actor.UserId) blockers.Add("Only the recording auditor may revise this assessment.");
      if (current.ReviewedByUserId is not null) blockers.Add("A reviewed assessment must be revised against a new current basis.");
      if (current.Conclusion is GoingConcernConclusions.NotAssessed or GoingConcernConclusions.InadequateDisclosure) blockers.Add("An assessed and adequately disclosed conclusion is required before independent review.");
      if (current.RecordedByUserId == actor.UserId) blockers.Add("The preparer cannot independently review this assessment.");
      if (!evidenceReady) blockers.Add("Current exact-engagement evidence is required before independent review.");
    }
    var basisHash = BasisHash(actor, engagementId, assessmentDate, basis);
    var second = await ReadBasisAsync(db, actor, engagementId, assessmentDate, ct);
    if (!second.Succeeded) return Fail<GoingConcernAssessmentCommandState>(second.ErrorCode!, second.Message!);
    if (BasisHash(actor, engagementId, assessmentDate, second.Value!) != basisHash)
      return Fail<GoingConcernAssessmentCommandState>(ErrorCodes.StaleRevision, "The going-concern basis changed while it was read; refresh the exact engagement state.");
    var expected = basis.Record?.Revision ?? 0;
    return CommandResult<GoingConcernAssessmentCommandState>.Ok(new(engagementId, basis.ClientId, assessmentDate,
      Long(expected), basisHash, basis.Record is null ? null : Snapshot(basis.Record), canPrepare, canReview, blockers.Distinct().ToArray()));
  }

  internal static GoingConcernAssessmentFields Fields(GoingConcernAssessment row) => new(row.ProcedureId,
    row.AssessmentDate, row.PeriodCoveredTo, row.ForecastReviewOutcome, row.MaterialUncertaintyIdentified,
    row.DisclosureAdequate, row.Currency, row.Rationale, EvidenceReferences(row));

  private static GoingConcernAssessmentFields? Canonical(GoingConcernAssessmentFields? f)
  {
    if (f is null || f.AssessmentDate == default || f.PeriodCoveredTo < f.AssessmentDate ||
        f.MaterialUncertaintyIdentified is null || f.DisclosureAdequate is null ||
        string.IsNullOrWhiteSpace(f.ForecastReviewOutcome) || f.ForecastReviewOutcome.Trim().Length > 4000 ||
        string.IsNullOrWhiteSpace(f.Rationale) || f.Rationale.Trim().Length > 4000 ||
        string.IsNullOrWhiteSpace(f.Currency) || f.Currency.Trim().Length != 3 ||
        f.Currency.Trim().ToUpperInvariant().Any(c => c is < 'A' or > 'Z') ||
        f.EvidenceReferences is null || f.EvidenceReferences.Count is < 1 or > 100 ||
        f.EvidenceReferences.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 2000)) return null;
    var evidence = f.EvidenceReferences.Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    if (evidence.Length != f.EvidenceReferences.Count || JsonSerializer.Serialize(evidence).Length > MaxEvidenceJsonLength) return null;
    return f with { Currency = f.Currency.Trim().ToUpperInvariant(), ForecastReviewOutcome = f.ForecastReviewOutcome.Trim(),
      Rationale = f.Rationale.Trim(), EvidenceReferences = evidence };
  }

  internal static async Task<CommandResult> ValidateEvidenceAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, GoingConcernAssessmentFields fields, CancellationToken ct)
  {
    var result = await CurrentEvidenceIdentityAsync(db, actor, clientId, engagementId, fields, ct);
    return result.Succeeded ? CommandResult.Ok() : CommandResult.Fail(result.ErrorCode!, result.Message!);
  }

  internal static Task<CommandResult<string>> CurrentEvidenceIdentityAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid engagementId, GoingConcernAssessmentFields fields, CancellationToken ct) =>
    AuditCompletionProcedureEvidence.CurrentAsync(db, actor, clientId, engagementId, fields.ProcedureId, fields.EvidenceReferences, ct);

  private static string RequestHash(ActorContext actor, Guid engagementId, Guid clientId, string action,
    GoingConcernAssessmentCommandRequest request, GoingConcernAssessmentFields? fields, string evidenceIdentityHash = "") => Hashing.Sha256Hex(JsonSerializer.Serialize(new
    { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch, clientId, engagementId, request.RequestId,
      Action = action, request.ExpectedRevision, request.ReviewBasis, Fields = fields, EvidenceIdentityHash = evidenceIdentityHash }));

  public static async Task<CommandResult<GoingConcernAssessmentCommandPreview>> PreviewPrepareAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, GoingConcernAssessmentCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out _) || fields is null)
      return Fail<GoingConcernAssessmentCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Provide a bounded exact going-concern preparation request.");
    var state = await StateAsync(db, actor, engagementId, fields.AssessmentDate, ct);
    if (!state.Succeeded) return Fail<GoingConcernAssessmentCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis || request.ExpectedRevision != s.ExpectedRevision)
      return Fail<GoingConcernAssessmentCommandPreview>(ErrorCodes.GenerationStale, "The going-concern basis changed; refresh before preparing.");
    var evidence = await ValidateEvidenceAsync(db, actor, s.ClientId, engagementId, fields, ct);
    if (!evidence.Succeeded) return Fail<GoingConcernAssessmentCommandPreview>(evidence.ErrorCode!, evidence.Message!);
    var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, s.ClientId, engagementId, fields, ct);
    if (!evidenceIdentity.Succeeded) return Fail<GoingConcernAssessmentCommandPreview>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
    var blockers = s.CanPrepare ? Array.Empty<string>() : s.Blockers.Count > 0 ? s.Blockers.ToArray() : ["Current exact-scope preparation authority is required."];
    var hash = RequestHash(actor, engagementId, s.ClientId, Prepare, request, fields, evidenceIdentity.Value!);
    return CommandResult<GoingConcernAssessmentCommandPreview>.Ok(new(request.RequestId, Prepare, hash, s.ReviewBasis,
      s.ExpectedRevision, blockers.Length == 0, blockers, s.Record,
      $"Prepare the assessment dated {fields.AssessmentDate:yyyy-MM-dd}, covering through {fields.PeriodCoveredTo:yyyy-MM-dd}."));
  }

  public static async Task<CommandResult<GoingConcernAssessmentCommandPreview>> PreviewReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid assessmentId, GoingConcernAssessmentCommandRequest? request, CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out var revision))
      return Fail<GoingConcernAssessmentCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Provide an exact going-concern review request.");
    var row = await db.GoingConcernAssessments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == assessmentId, ct);
    if (row is null) return Fail<GoingConcernAssessmentCommandPreview>(ErrorCodes.ScopeDenied, "Access denied.");
    // Resolve authorization for this exact engagement before returning any record/evidence-specific result.
    var state = await StateAsync(db, actor, engagementId, row.AssessmentDate, ct);
    if (!state.Succeeded) return Fail<GoingConcernAssessmentCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis || request.ExpectedRevision != s.ExpectedRevision || row.Revision != revision ||
        s.Record?.Id != assessmentId)
      return Fail<GoingConcernAssessmentCommandPreview>(ErrorCodes.GenerationStale, "The going-concern basis changed; refresh the exact record before reviewing.");
    var evidenceReferences = EvidenceReferences(row);
    if (evidenceReferences.Length == 0)
      return Fail<GoingConcernAssessmentCommandPreview>(ErrorCodes.GateBlocked, "At least one current exact-engagement evidence reference is required before review.");
    var evidence = await ValidateEvidenceAsync(db, actor, row.ClientId, engagementId,
      Fields(row), ct);
    if (!evidence.Succeeded) return Fail<GoingConcernAssessmentCommandPreview>(evidence.ErrorCode!, evidence.Message!);
    var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, s.ClientId, engagementId,
      Fields(row), ct);
    if (!evidenceIdentity.Succeeded) return Fail<GoingConcernAssessmentCommandPreview>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
    var blockers = s.CanReview ? Array.Empty<string>() : s.Blockers.Count > 0 ? s.Blockers.ToArray() : ["Current independent review authority is required."];
    var hash = RequestHash(actor, engagementId, s.ClientId, Review, request with { Fields = null }, null, evidenceIdentity.Value!);
    return CommandResult<GoingConcernAssessmentCommandPreview>.Ok(new(request.RequestId, Review, hash, s.ReviewBasis,
      s.ExpectedRevision, blockers.Length == 0, blockers, s.Record, "Independently review this exact going-concern revision."));
  }

  internal static async Task LockRequestAsync(IClientAccountingDbContext db, ActorContext actor, Guid requestId, bool write, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "audit-completion-going-concern-command-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  private static GoingConcernAssessmentCommandReceiptView View(AuditCompletionGoingConcernCommandReceipt receipt) =>
    new(receipt.Id, receipt.EngagementId, receipt.GoingConcernAssessmentId, receipt.ActorId, Long(receipt.ActorEpoch),
      receipt.RequestId, receipt.RequestHash, receipt.Action, receipt.CreatedAt,
      JsonSerializer.Deserialize<GoingConcernAssessmentCommandPreview>(receipt.PreviewJson, Json)!,
      JsonSerializer.Deserialize<GoingConcernAssessmentSnapshot>(receipt.ResultJson, Json)!);

  private static async Task<CommandResult<GoingConcernAssessmentCommandReceiptView>> ExistingAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid clientId, Guid requestId, string requestHash, string action, CancellationToken ct)
  {
    var prior = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (prior is null) return Fail<GoingConcernAssessmentCommandReceiptView>("receipt.absent", "No retained exact request was found.");
    if (prior.ActorEpoch != actor.SessionEpoch) return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (prior.EngagementId != engagementId || prior.ClientId != clientId || prior.RequestHash != requestHash || prior.Action != action)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
    return CommandResult<GoingConcernAssessmentCommandReceiptView>.Ok(View(prior));
  }

  private static async Task<CommandResult> LockEngagementAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid engagementId, Guid clientId, CancellationToken ct)
  {
    var locked = await AuditPlanningCommandWorkspace.LockAsync(db, actor, engagementId, true, ct);
    if (!locked.Succeeded) return locked;
    var engagement = await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND id={engagementId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    return engagement is not null && engagement.PracticeClientId == clientId
      ? CommandResult.Ok() : CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  }

  public static async Task<CommandResult<GoingConcernAssessmentCommandReceiptView>> ExecutePrepareAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, GoingConcernAssessmentCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !Revision(request.ExpectedRevision, out _) || fields is null)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "Preview and confirm the exact going-concern preparation intent.");
    var initial = await StateAsync(db, actor, engagementId, fields.AssessmentDate, ct);
    if (!initial.Succeeded) return Fail<GoingConcernAssessmentCommandReceiptView>(initial.ErrorCode!, initial.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var locked = await LockEngagementAsync(db, actor, engagementId, initial.Value!.ClientId, ct);
      if (!locked.Succeeded) return Fail<GoingConcernAssessmentCommandReceiptView>(locked.ErrorCode!, locked.Message!);
      await LockRequestAsync(db, actor, request.RequestId, true, ct);
      var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, initial.Value.ClientId, engagementId, PlanningRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded) { await tx.CommitAsync(ct); return Fail<GoingConcernAssessmentCommandReceiptView>(auth.ErrorCode!, auth.Message!); }
      var retained = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      if (retained is not null)
      {
        if (retained.ActorEpoch != actor.SessionEpoch)
          return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (retained.EngagementId != engagementId || retained.ClientId != initial.Value.ClientId || retained.Action != Prepare ||
            retained.ReviewBasis != request.ReviewBasis || retained.InputJson != JsonSerializer.Serialize(fields) ||
            View(retained).Preview.ExpectedRevision != request.ExpectedRevision ||
            retained.RequestHash != request.ExpectedRequestHash)
          return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        await tx.CommitAsync(ct);
        return CommandResult<GoingConcernAssessmentCommandReceiptView>.Ok(View(retained));
      }
      var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, initial.Value.ClientId, engagementId, fields, ct);
      if (!evidenceIdentity.Succeeded)
        return Fail<GoingConcernAssessmentCommandReceiptView>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
      var hash = RequestHash(actor, engagementId, initial.Value.ClientId, Prepare, request, fields, evidenceIdentity.Value!);
      var prior = await ExistingAsync(db, actor, engagementId, initial.Value.ClientId, request.RequestId, hash, Prepare, ct);
      if (prior.Succeeded) { await tx.CommitAsync(ct); return prior; }
      if (prior.ErrorCode != "receipt.absent") return prior;
      if (request.ExpectedRequestHash != hash) return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted request differs from its exact preview.");
      var preview = await PreviewPrepareAsync(db, actor, engagementId, request, ct);
      if (!preview.Succeeded) return Fail<GoingConcernAssessmentCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (preview.Value!.RequestHash != request.ExpectedRequestHash)
        return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GenerationStale,
          "The exact going-concern evidence changed after preview; refresh and review the current evidence again.");
      if (!preview.Value.CanProceed) return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.Blockers.First());
      var revision = long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture);
      var recorded = await AuditCompletionService.RecordGoingConcernAssessmentAsync(db, actor,
        new(engagementId, fields.ProcedureId, fields.AssessmentDate, fields.PeriodCoveredTo,
          fields.ForecastReviewOutcome, fields.MaterialUncertaintyIdentified!.Value, fields.DisclosureAdequate!.Value,
          fields.Currency, fields.Rationale, fields.EvidenceReferences, revision), ct);
      if (!recorded.Succeeded) return Fail<GoingConcernAssessmentCommandReceiptView>(recorded.ErrorCode!, recorded.Message!);
      var result = await db.GoingConcernAssessments.AsNoTracking().SingleAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == initial.Value.ClientId && x.EngagementId == engagementId && x.Id == recorded.Value!.AssessmentId, ct);
      var row = NewReceipt(actor, initial.Value.ClientId, engagementId, result.Id, request.RequestId, hash, Prepare,
        request.ReviewBasis, JsonSerializer.Serialize(fields), JsonSerializer.Serialize(preview.Value), JsonSerializer.Serialize(Snapshot(result)));
      db.AuditCompletionGoingConcernCommandReceipts.Add(row);
      await db.SaveChangesAsync(ct);
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, initial.Value.ClientId, engagementId, PlanningRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded) { await tx.RollbackAsync(ct); return Fail<GoingConcernAssessmentCommandReceiptView>(auth.ErrorCode!, auth.Message!); }
      var persisted = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == row.Id, ct);
      await tx.CommitAsync(ct);
      return CommandResult<GoingConcernAssessmentCommandReceiptView>.Ok(View(persisted));
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      await tx.RollbackAsync(ct);
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent going-concern preparation changed the outcome. Look up the retained request, then refresh before any new intent.");
    }
  }

  private static AuditCompletionGoingConcernCommandReceipt NewReceipt(ActorContext actor, Guid clientId, Guid engagementId,
    Guid assessmentId, Guid requestId, string hash, string action, string basis, string input, string preview, string result) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, EngagementId = engagementId,
    GoingConcernAssessmentId = assessmentId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
    RequestId = requestId, RequestHash = hash, ReviewBasis = basis, Action = action, InputJson = input,
    PreviewJson = preview, ResultJson = result, CreatedAt = DateTimeOffset.UtcNow
  };

  public static async Task<CommandResult<GoingConcernAssessmentCommandReceiptView>> ExecuteReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid assessmentId, GoingConcernAssessmentCommandRequest? request, CancellationToken ct = default)
  {
    var validRevision = request is not null && Revision(request.ExpectedRevision, out _);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !validRevision)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "Preview and confirm the exact going-concern review intent.");
    var initial = await db.GoingConcernAssessments.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == assessmentId, ct);
    if (initial is null) return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
    // Resolve current exact-scope authority before deriving a record-dependent request hash.
    var authorizedState = await StateAsync(db, actor, engagementId, initial.AssessmentDate, ct);
    if (!authorizedState.Succeeded)
      return Fail<GoingConcernAssessmentCommandReceiptView>(authorizedState.ErrorCode!, authorizedState.Message!);
    var inputJson = JsonSerializer.Serialize(request with { Fields = null });
    if (inputJson.Length > MaxReceiptInputJsonLength)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "The exact going-concern review request exceeds its retained evidence limit.");
    var retained = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (retained is not null)
    {
      if (retained.ActorEpoch != actor.SessionEpoch)
        return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
      if (retained.EngagementId != engagementId || retained.GoingConcernAssessmentId != assessmentId || retained.Action != Review ||
          retained.ReviewBasis != request.ReviewBasis || retained.InputJson != inputJson ||
          retained.RequestHash != request.ExpectedRequestHash)
        return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      var replayContext = new GoingConcernReviewReceiptContext(retained.RequestId, retained.RequestHash,
        retained.ReviewBasis, retained.InputJson, retained.PreviewJson,
        long.Parse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture), null);
      var replay = await AuditCompletionService.ReviewCompletionRecordAsync(db, actor, "GOING_CONCERN", assessmentId,
        long.Parse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture), ct, goingConcernReceiptContext: replayContext);
      if (!replay.Succeeded)
        return Fail<GoingConcernAssessmentCommandReceiptView>(replay.ErrorCode!, replay.Message!);
      var replayed = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      return replayed is null
        ? Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The retained review outcome is unavailable; look up the request before retrying.")
        : CommandResult<GoingConcernAssessmentCommandReceiptView>.Ok(View(replayed));
    }
    var evidence = await CurrentEvidenceIdentityAsync(db, actor, initial.ClientId, engagementId,
      Fields(initial), ct);
    if (!evidence.Succeeded) return Fail<GoingConcernAssessmentCommandReceiptView>(evidence.ErrorCode!, evidence.Message!);
    var hash = RequestHash(actor, engagementId, initial.ClientId, Review, request with { Fields = null }, null, evidence.Value!);
    if (request.ExpectedRequestHash != hash)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted request differs from its exact preview.");
    var preview = await PreviewReviewAsync(db, actor, engagementId, assessmentId, request, ct);
    if (!preview.Succeeded)
      return Fail<GoingConcernAssessmentCommandReceiptView>(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.RequestHash != request.ExpectedRequestHash)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GenerationStale,
        "The exact going-concern evidence changed after preview; refresh and review the current evidence again.");
    if (!preview.Value.CanProceed)
      return Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.GateBlocked,
        preview.Value.Blockers.FirstOrDefault() ?? "Current independent review authority is required.");
    var context = new GoingConcernReviewReceiptContext(request.RequestId, hash, request.ReviewBasis,
      inputJson,
      JsonSerializer.Serialize(preview.Value),
      long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture), evidence.Value!);
    var reviewed = await AuditCompletionService.ReviewCompletionRecordAsync(db, actor, "GOING_CONCERN", assessmentId,
      long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture), ct, goingConcernReceiptContext: context);
    if (!reviewed.Succeeded) return Fail<GoingConcernAssessmentCommandReceiptView>(reviewed.ErrorCode!, reviewed.Message!);
    var receipt = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    return receipt is null
      ? Fail<GoingConcernAssessmentCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The review outcome was not retained; look up the request before retrying.")
      : CommandResult<GoingConcernAssessmentCommandReceiptView>.Ok(View(receipt));
  }

  public static async Task<CommandResult<GoingConcernAssessmentCommandLookup>> LookupAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<GoingConcernAssessmentCommandLookup>(ErrorCodes.Accounting.MappingInvalid, "Use the exact retained request reference.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<GoingConcernAssessmentCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<GoingConcernAssessmentCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockRequestAsync(db, actor, requestId, false, ct);
    var row = await db.AuditCompletionGoingConcernCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != actor.SessionEpoch) return Fail<GoingConcernAssessmentCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (row is not null && (row.EngagementId != engagementId || row.ClientId != engagement.PracticeClientId || row.RequestHash != requestHash))
      return Fail<GoingConcernAssessmentCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<GoingConcernAssessmentCommandLookup>(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<GoingConcernAssessmentCommandLookup>.Ok(new(row is not null, row is null ? null : View(row)));
  }
}
