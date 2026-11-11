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

public sealed record SubsequentEventReviewFields(Guid? ProcedureId, DateOnly PeriodEndDate, DateOnly EventDate,
  string Description, string Classification, bool? AdjustmentRequired, bool? DisclosureRequired,
  string? DisclosureReference, string? Rationale, string Currency, string? FinancialEffect,
  IReadOnlyList<string> EvidenceReferences);

public sealed record SubsequentEventReviewCommandRequest(Guid RequestId, string ExpectedRevision, string ReviewBasis,
  SubsequentEventReviewFields? Fields = null, string? ExpectedRequestHash = null, bool Reviewed = false,
  Guid? SelectedRecordId = null);

public sealed record SubsequentEventReviewSnapshot(Guid Id, Guid ClientId, Guid EngagementId, Guid? ProcedureId,
  DateOnly PeriodEndDate, DateOnly EventDate, string Description, string Classification, bool AdjustmentRequired,
  bool DisclosureRequired, string? DisclosureReference, string? Rationale, string Currency, string? FinancialEffect,
  IReadOnlyList<string> EvidenceReferences, string Revision, Guid RecordedByUserId, Guid? ReviewedByUserId,
  DateTimeOffset RecordedAt, DateTimeOffset? ReviewedAt, string? ReviewedRevision);

public sealed record SubsequentEventReviewCommandState(Guid EngagementId, Guid ClientId, Guid? SelectedRecordId,
  string ExpectedRevision, string ReviewBasis, SubsequentEventReviewSnapshot? Record,
  bool CanPrepare, bool CanReview, IReadOnlyList<string> Blockers);

public sealed record SubsequentEventReviewList(IReadOnlyList<SubsequentEventReviewSnapshot> Records, bool HasMore);

public sealed record SubsequentEventReviewCommandPreview(Guid RequestId, string Action, string RequestHash,
  string ReviewBasis, string ExpectedRevision, bool CanProceed, IReadOnlyList<string> Blockers,
  SubsequentEventReviewSnapshot? CurrentRecord, string Summary);

public sealed record SubsequentEventReviewCommandReceiptView(Guid Id, Guid EngagementId, Guid SubsequentEventReviewId,
  Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash, string Action, DateTimeOffset CreatedAt,
  SubsequentEventReviewCommandPreview Preview, SubsequentEventReviewSnapshot Result);

public sealed record SubsequentEventReviewCommandLookup(bool Found, SubsequentEventReviewCommandReceiptView? Receipt);
public sealed record SubsequentEventReviewReceiptContext(Guid RequestId, string RequestHash, string ReviewBasis,
  string InputJson, string PreviewJson, long ExpectedRevision, string? EvidenceIdentityHash);

/// <summary>Exact-scope product commands and immutable recovery for the existing subsequent-event completion record.</summary>
public static class AuditCompletionSubsequentEventWorkspace
{
  private const string Prepare = "PREPARE";
  private const string Review = "REVIEW";
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private const int MaxReceiptInputJsonLength = AuditCompletionSubsequentEventCommandReceipt.MaximumInputJsonLength;

  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Revision(string? value, out long revision) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision >= 0;

  internal static SubsequentEventReviewSnapshot Snapshot(SubsequentEventReview row)
  {
    string[] evidence;
    try { evidence = JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? []; }
    catch (JsonException) { evidence = []; }
    return new(row.Id, row.ClientId, row.EngagementId, row.ProcedureId, row.PeriodEndDate, row.EventDate,
      row.Description, row.Classification, row.AdjustmentRequired, row.DisclosureRequired, row.DisclosureReference,
      row.Rationale, row.Currency, row.FinancialEffect?.ToString(CultureInfo.InvariantCulture), evidence,
      Long(row.Revision), row.RecordedByUserId, row.ReviewedByUserId, row.RecordedAt, row.ReviewedAt,
      row.ReviewedRevision is { } reviewed ? Long(reviewed) : null);
  }

  internal static string[] EvidenceReferences(SubsequentEventReview row)
  {
    try { return JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? []; }
    catch (JsonException) { return []; }
  }

  private sealed record CurrentBasis(Guid ClientId, long EngagementGeneration, long ClientInputGeneration,
    bool ProfessionalWorkBlocked, bool ActiveHold, bool Frozen, SubsequentEventReview? Record);

  private static async Task<CommandResult<CurrentBasis>> ReadBasisAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid engagementId, Guid? selectedRecordId, CancellationToken ct)
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
    var record = selectedRecordId is null ? null : await db.SubsequentEventReviews.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == engagement.PracticeClientId && x.EngagementId == engagementId && x.Id == selectedRecordId, ct);
    if (selectedRecordId is not null && record is null) return Fail<CurrentBasis>(ErrorCodes.ScopeDenied, "Access denied.");
    return CommandResult<CurrentBasis>.Ok(new(engagement.PracticeClientId, engagement.Generation, clientGeneration,
      engagement.ProfessionalWorkBlocked, held, frozen, record));
  }

  public static async Task<CommandResult<SubsequentEventReviewList>> ListAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, int skip = 0, CancellationToken ct = default)
  {
    if (skip < 0 || skip > 100000) return Fail<SubsequentEventReviewList>(ErrorCodes.Accounting.MappingInvalid, "Select a bounded event page.");
    var basis = await ReadBasisAsync(db, actor, engagementId, null, ct);
    if (!basis.Succeeded) return Fail<SubsequentEventReviewList>(basis.ErrorCode!, basis.Message!);
    var rows = await db.SubsequentEventReviews.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == basis.Value!.ClientId && x.EngagementId == engagementId).OrderBy(x => x.EventDate).ThenBy(x => x.Id)
      .Skip(skip).Take(101).ToListAsync(ct);
    var auth = await ReadBasisAsync(db, actor, engagementId, null, ct);
    if (!auth.Succeeded) return Fail<SubsequentEventReviewList>(auth.ErrorCode!, auth.Message!);
    return CommandResult<SubsequentEventReviewList>.Ok(new(rows.Take(100).Select(Snapshot).ToArray(), rows.Count > 100));
  }

  private static string BasisHash(ActorContext actor, Guid engagementId, Guid? selectedRecordId, CurrentBasis basis) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch,
      engagementId, basis.ClientId, basis.EngagementGeneration, basis.ClientInputGeneration,
      basis.ProfessionalWorkBlocked, basis.ActiveHold, basis.Frozen, selectedRecordId,
      Record = basis.Record is null ? null : Snapshot(basis.Record) }));

  public static async Task<CommandResult<SubsequentEventReviewCommandState>> StateAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid? selectedRecordId, CancellationToken ct = default)
  {
    if (engagementId == Guid.Empty || selectedRecordId == Guid.Empty)
      return Fail<SubsequentEventReviewCommandState>(ErrorCodes.Accounting.MappingInvalid, "Select an exact engagement and optional event record.");
    var first = await ReadBasisAsync(db, actor, engagementId, selectedRecordId, ct);
    if (!first.Succeeded) return Fail<SubsequentEventReviewCommandState>(first.ErrorCode!, first.Message!);
    var basis = first.Value!;
    var planning = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, basis.ClientId, engagementId, PlanningRoles, InternalOnly: true), ct);
    var review = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, basis.ClientId, engagementId, ReviewRoles, InternalOnly: true), ct);
    var writable = !basis.ProfessionalWorkBlocked && !basis.ActiveHold && !basis.Frozen;
    var evidenceReady = false;
    if (basis.Record is { } evidenceRecord)
    {
      {
        var evidence = await ValidateEvidenceAsync(db, actor, basis.ClientId, engagementId,
          Fields(evidenceRecord), ct);
        evidenceReady = evidence.Succeeded;
      }
    }
    var canPrepare = planning.Succeeded && writable && (basis.Record is null || basis.Record.RecordedByUserId == actor.UserId);
    var canReview = review.Succeeded && writable && basis.Record is { } row && row.RecordedByUserId != actor.UserId &&
      row.Classification != SubsequentEventClassifications.PendingAssessment && row.ReviewedByUserId is null && evidenceReady;
    var blockers = new List<string>();
    if (!writable) blockers.Add(basis.Frozen ? "The engagement file is frozen." : basis.ActiveHold ? "Professional work is held on this engagement." : "Professional work is blocked on this engagement.");
    if (basis.Record is { } current)
    {
      if (current.RecordedByUserId != actor.UserId) blockers.Add("Only the recording auditor may revise this assessment.");
      if (current.ReviewedByUserId is not null) blockers.Add("A reviewed assessment must be revised against a new current basis.");
      if (current.Classification == SubsequentEventClassifications.PendingAssessment) blockers.Add("A classified event is required before independent review.");
      if (current.RecordedByUserId == actor.UserId) blockers.Add("The preparer cannot independently review this assessment.");
      if (!evidenceReady) blockers.Add("Current exact-engagement evidence is required before independent review.");
    }
    var basisHash = BasisHash(actor, engagementId, selectedRecordId, basis);
    var second = await ReadBasisAsync(db, actor, engagementId, selectedRecordId, ct);
    if (!second.Succeeded) return Fail<SubsequentEventReviewCommandState>(second.ErrorCode!, second.Message!);
    if (BasisHash(actor, engagementId, selectedRecordId, second.Value!) != basisHash)
      return Fail<SubsequentEventReviewCommandState>(ErrorCodes.StaleRevision, "The subsequent-event basis changed while it was read; refresh the exact engagement state.");
    var expected = basis.Record?.Revision ?? 0;
    return CommandResult<SubsequentEventReviewCommandState>.Ok(new(engagementId, basis.ClientId, selectedRecordId,
      Long(expected), basisHash, basis.Record is null ? null : Snapshot(basis.Record), canPrepare, canReview, blockers.Distinct().ToArray()));
  }

  internal static SubsequentEventReviewFields Fields(SubsequentEventReview row) => new(row.ProcedureId,
    row.PeriodEndDate, row.EventDate, row.Description, row.Classification, row.AdjustmentRequired,
    row.DisclosureRequired, row.DisclosureReference, row.Rationale, row.Currency,
    row.FinancialEffect?.ToString(CultureInfo.InvariantCulture), EvidenceReferences(row));

  private static SubsequentEventReviewFields? Canonical(SubsequentEventReviewFields? f)
  {
    if (f is null || f.AdjustmentRequired is null || f.DisclosureRequired is null || f.EvidenceReferences is null ||
        (f.FinancialEffect is not null && (!System.Text.RegularExpressions.Regex.IsMatch(f.FinancialEffect, @"^[+-]?[0-9]{1,13}(\.[0-9]{1,6})?$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
          !decimal.TryParse(f.FinancialEffect, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)))) return null;
    var normalized = f with { Description = f.Description?.Trim() ?? "", Classification = f.Classification?.Trim().ToUpperInvariant() ?? "",
      Currency = f.Currency?.Trim().ToUpperInvariant() ?? "", Rationale = string.IsNullOrWhiteSpace(f.Rationale) ? null : f.Rationale.Trim(),
      DisclosureReference = string.IsNullOrWhiteSpace(f.DisclosureReference) ? null : f.DisclosureReference.Trim(),
      FinancialEffect = f.FinancialEffect is null ? null : decimal.Parse(f.FinancialEffect, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
      EvidenceReferences = f.EvidenceReferences.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToArray() };
    return AuditCompletionService.ValidateSubsequentEvent(ToRequest(Guid.NewGuid(), normalized, 0, null)).Succeeded &&
      JsonSerializer.Serialize(normalized).Length <= MaxReceiptInputJsonLength ? normalized : null;
  }

  private static SubsequentEventReviewRequest ToRequest(Guid engagementId, SubsequentEventReviewFields f, long revision, Guid? selectedId) =>
    new(engagementId, f.ProcedureId, f.PeriodEndDate, f.EventDate, f.Description, f.Classification,
      f.AdjustmentRequired!.Value, f.DisclosureRequired!.Value, f.DisclosureReference, f.Rationale, f.Currency,
      f.FinancialEffect is null ? null : decimal.Parse(f.FinancialEffect, CultureInfo.InvariantCulture), f.EvidenceReferences, revision, selectedId);

  internal static async Task<CommandResult> ValidateEvidenceAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, SubsequentEventReviewFields fields, CancellationToken ct)
  {
    var result = await CurrentEvidenceIdentityAsync(db, actor, clientId, engagementId, fields, ct);
    return result.Succeeded ? CommandResult.Ok() : CommandResult.Fail(result.ErrorCode!, result.Message!);
  }

  internal static Task<CommandResult<string>> CurrentEvidenceIdentityAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid engagementId, SubsequentEventReviewFields fields, CancellationToken ct) =>
    fields.ProcedureId is null
      ? Task.FromResult(CommandResult<string>.Ok(Hashing.Sha256Hex(JsonSerializer.Serialize(fields.EvidenceReferences))))
      : AuditCompletionProcedureEvidence.CurrentAsync(db, actor, clientId, engagementId, fields.ProcedureId, fields.EvidenceReferences, ct);

  private static string RequestHash(ActorContext actor, Guid engagementId, Guid clientId, string action,
    SubsequentEventReviewCommandRequest request, SubsequentEventReviewFields? fields, string evidenceIdentityHash = "") => Hashing.Sha256Hex(JsonSerializer.Serialize(new
    { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch, clientId, engagementId, request.RequestId,
      Action = action, request.SelectedRecordId, request.ExpectedRevision, request.ReviewBasis, Fields = fields, EvidenceIdentityHash = evidenceIdentityHash }));

  public static async Task<CommandResult<SubsequentEventReviewCommandPreview>> PreviewPrepareAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, SubsequentEventReviewCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out _) || fields is null)
      return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Provide a bounded exact subsequent-event preparation request.");
    var state = await StateAsync(db, actor, engagementId, request.SelectedRecordId, ct);
    if (!state.Succeeded) return Fail<SubsequentEventReviewCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis || request.ExpectedRevision != s.ExpectedRevision)
      return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.GenerationStale, "The subsequent-event basis changed; refresh before preparing.");
    if (s.Record is { } selected && (selected.EventDate != fields.EventDate || selected.Description != fields.Description))
      return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.GenerationStale, "The selected event identity cannot change; explicitly create another event instead.");
    if (request.SelectedRecordId is null && await db.SubsequentEventReviews.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId &&
        x.ClientId == s.ClientId && x.EngagementId == engagementId && x.EventDate == fields.EventDate && x.Description == fields.Description, ct))
      return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.GenerationStale, "This event already exists; select its exact record and revision.");
    var evidence = await ValidateEvidenceAsync(db, actor, s.ClientId, engagementId, fields, ct);
    if (!evidence.Succeeded) return Fail<SubsequentEventReviewCommandPreview>(evidence.ErrorCode!, evidence.Message!);
    var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, s.ClientId, engagementId, fields, ct);
    if (!evidenceIdentity.Succeeded) return Fail<SubsequentEventReviewCommandPreview>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
    var blockers = s.CanPrepare ? Array.Empty<string>() : s.Blockers.Count > 0 ? s.Blockers.ToArray() : ["Current exact-scope preparation authority is required."];
    var hash = RequestHash(actor, engagementId, s.ClientId, Prepare, request, fields, evidenceIdentity.Value!);
    return CommandResult<SubsequentEventReviewCommandPreview>.Ok(new(request.RequestId, Prepare, hash, s.ReviewBasis,
      s.ExpectedRevision, blockers.Length == 0, blockers, s.Record,
      "Prepare this exact subsequent event and its professional assessment."));
  }

  public static async Task<CommandResult<SubsequentEventReviewCommandPreview>> PreviewReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid eventId, SubsequentEventReviewCommandRequest? request, CancellationToken ct = default)
  {
    if (request is null || request.SelectedRecordId != eventId || request.Fields is not null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out var revision))
      return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Provide an exact subsequent-event review request.");
    var row = await db.SubsequentEventReviews.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == eventId, ct);
    if (row is null) return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.ScopeDenied, "Access denied.");
    // Resolve authorization for this exact engagement before returning any record/evidence-specific result.
    var state = await StateAsync(db, actor, engagementId, eventId, ct);
    if (!state.Succeeded) return Fail<SubsequentEventReviewCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis || request.ExpectedRevision != s.ExpectedRevision || row.Revision != revision ||
        s.Record?.Id != eventId)
      return Fail<SubsequentEventReviewCommandPreview>(ErrorCodes.GenerationStale, "The subsequent-event basis changed; refresh the exact record before reviewing.");
    var evidence = await ValidateEvidenceAsync(db, actor, row.ClientId, engagementId,
      Fields(row), ct);
    if (!evidence.Succeeded) return Fail<SubsequentEventReviewCommandPreview>(evidence.ErrorCode!, evidence.Message!);
    var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, s.ClientId, engagementId,
      Fields(row), ct);
    if (!evidenceIdentity.Succeeded) return Fail<SubsequentEventReviewCommandPreview>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
    var blockers = s.CanReview ? Array.Empty<string>() : s.Blockers.Count > 0 ? s.Blockers.ToArray() : ["Current independent review authority is required."];
    var hash = RequestHash(actor, engagementId, s.ClientId, Review, request with { Fields = null }, null, evidenceIdentity.Value!);
    return CommandResult<SubsequentEventReviewCommandPreview>.Ok(new(request.RequestId, Review, hash, s.ReviewBasis,
      s.ExpectedRevision, blockers.Length == 0, blockers, s.Record, "Independently review this exact subsequent-event revision."));
  }

  internal static async Task LockRequestAsync(IClientAccountingDbContext db, ActorContext actor, Guid requestId, bool write, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "audit-completion-subsequent-event-command-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  private static SubsequentEventReviewCommandReceiptView View(AuditCompletionSubsequentEventCommandReceipt receipt) =>
    new(receipt.Id, receipt.EngagementId, receipt.SubsequentEventReviewId, receipt.ActorId, Long(receipt.ActorEpoch),
      receipt.RequestId, receipt.RequestHash, receipt.Action, receipt.CreatedAt,
      JsonSerializer.Deserialize<SubsequentEventReviewCommandPreview>(receipt.PreviewJson, Json)!,
      JsonSerializer.Deserialize<SubsequentEventReviewSnapshot>(receipt.ResultJson, Json)!);

  private static async Task<CommandResult<SubsequentEventReviewCommandReceiptView>> ExistingAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid clientId, Guid requestId, string requestHash, string action, CancellationToken ct)
  {
    var prior = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (prior is null) return Fail<SubsequentEventReviewCommandReceiptView>("receipt.absent", "No retained exact request was found.");
    if (prior.ActorEpoch != actor.SessionEpoch) return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (prior.EngagementId != engagementId || prior.ClientId != clientId || prior.RequestHash != requestHash || prior.Action != action)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
    return CommandResult<SubsequentEventReviewCommandReceiptView>.Ok(View(prior));
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

  public static async Task<CommandResult<SubsequentEventReviewCommandReceiptView>> ExecutePrepareAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, SubsequentEventReviewCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !Revision(request.ExpectedRevision, out _) || fields is null)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "Preview and confirm the exact subsequent-event preparation intent.");
    var initial = await StateAsync(db, actor, engagementId, request.SelectedRecordId, ct);
    if (!initial.Succeeded) return Fail<SubsequentEventReviewCommandReceiptView>(initial.ErrorCode!, initial.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var locked = await LockEngagementAsync(db, actor, engagementId, initial.Value!.ClientId, ct);
      if (!locked.Succeeded) return Fail<SubsequentEventReviewCommandReceiptView>(locked.ErrorCode!, locked.Message!);
      await LockRequestAsync(db, actor, request.RequestId, true, ct);
      var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, initial.Value.ClientId, engagementId, PlanningRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded) { await tx.CommitAsync(ct); return Fail<SubsequentEventReviewCommandReceiptView>(auth.ErrorCode!, auth.Message!); }
      var retained = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      if (retained is not null)
      {
        if (retained.ActorEpoch != actor.SessionEpoch)
          return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (retained.EngagementId != engagementId || retained.ClientId != initial.Value.ClientId || retained.Action != Prepare ||
            retained.ReviewBasis != request.ReviewBasis || retained.InputJson != JsonSerializer.Serialize(new { request.SelectedRecordId, Fields = fields }) ||
            View(retained).Preview.ExpectedRevision != request.ExpectedRevision ||
            retained.RequestHash != request.ExpectedRequestHash)
          return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        await tx.CommitAsync(ct);
        return CommandResult<SubsequentEventReviewCommandReceiptView>.Ok(View(retained));
      }
      var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, initial.Value.ClientId, engagementId, fields, ct);
      if (!evidenceIdentity.Succeeded)
        return Fail<SubsequentEventReviewCommandReceiptView>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
      var hash = RequestHash(actor, engagementId, initial.Value.ClientId, Prepare, request, fields, evidenceIdentity.Value!);
      var prior = await ExistingAsync(db, actor, engagementId, initial.Value.ClientId, request.RequestId, hash, Prepare, ct);
      if (prior.Succeeded) { await tx.CommitAsync(ct); return prior; }
      if (prior.ErrorCode != "receipt.absent") return prior;
      if (request.ExpectedRequestHash != hash) return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted request differs from its exact preview.");
      var preview = await PreviewPrepareAsync(db, actor, engagementId, request, ct);
      if (!preview.Succeeded) return Fail<SubsequentEventReviewCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (preview.Value!.RequestHash != request.ExpectedRequestHash)
        return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GenerationStale,
          "The exact subsequent-event evidence changed after preview; refresh and review the current evidence again.");
      if (!preview.Value.CanProceed) return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.Blockers.First());
      var revision = long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture);
      var recorded = await AuditCompletionService.RecordSubsequentEventAsync(db, actor,
        ToRequest(engagementId, fields, revision, request.SelectedRecordId), ct);
      if (!recorded.Succeeded) return Fail<SubsequentEventReviewCommandReceiptView>(recorded.ErrorCode!, recorded.Message!);
      var result = await db.SubsequentEventReviews.AsNoTracking().SingleAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == initial.Value.ClientId && x.EngagementId == engagementId && x.Id == recorded.Value!.ReviewId, ct);
      var row = NewReceipt(actor, initial.Value.ClientId, engagementId, result.Id, request.RequestId, hash, Prepare,
        request.ReviewBasis, JsonSerializer.Serialize(new { request.SelectedRecordId, Fields = fields }), JsonSerializer.Serialize(preview.Value), JsonSerializer.Serialize(Snapshot(result)));
      db.AuditCompletionSubsequentEventCommandReceipts.Add(row);
      await db.SaveChangesAsync(ct);
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, initial.Value.ClientId, engagementId, PlanningRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded) { await tx.RollbackAsync(ct); return Fail<SubsequentEventReviewCommandReceiptView>(auth.ErrorCode!, auth.Message!); }
      var persisted = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == row.Id, ct);
      await tx.CommitAsync(ct);
      return CommandResult<SubsequentEventReviewCommandReceiptView>.Ok(View(persisted));
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      await tx.RollbackAsync(ct);
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent subsequent-event preparation changed the outcome. Look up the retained request, then refresh before any new intent.");
    }
  }

  private static AuditCompletionSubsequentEventCommandReceipt NewReceipt(ActorContext actor, Guid clientId, Guid engagementId,
    Guid eventId, Guid requestId, string hash, string action, string basis, string input, string preview, string result) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, EngagementId = engagementId,
    SubsequentEventReviewId = eventId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
    RequestId = requestId, RequestHash = hash, ReviewBasis = basis, Action = action, InputJson = input,
    PreviewJson = preview, ResultJson = result, CreatedAt = DateTimeOffset.UtcNow
  };

  public static async Task<CommandResult<SubsequentEventReviewCommandReceiptView>> ExecuteReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid eventId, SubsequentEventReviewCommandRequest? request, CancellationToken ct = default)
  {
    var validRevision = request is not null && Revision(request.ExpectedRevision, out _);
    if (request is not { Reviewed: true } || request.SelectedRecordId != eventId || request.Fields is not null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !validRevision)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "Preview and confirm the exact subsequent-event review intent.");
    var initial = await db.SubsequentEventReviews.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == eventId, ct);
    if (initial is null) return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
    // Resolve current exact-scope authority before deriving a record-dependent request hash.
    var authorizedState = await StateAsync(db, actor, engagementId, eventId, ct);
    if (!authorizedState.Succeeded)
      return Fail<SubsequentEventReviewCommandReceiptView>(authorizedState.ErrorCode!, authorizedState.Message!);
    var inputJson = JsonSerializer.Serialize(request with { Fields = null });
    if (inputJson.Length > MaxReceiptInputJsonLength)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "The exact subsequent-event review request exceeds its retained evidence limit.");
    var retained = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (retained is not null)
    {
      if (retained.ActorEpoch != actor.SessionEpoch)
        return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
      if (retained.EngagementId != engagementId || retained.SubsequentEventReviewId != eventId || retained.Action != Review ||
          retained.ReviewBasis != request.ReviewBasis || retained.InputJson != inputJson ||
          retained.RequestHash != request.ExpectedRequestHash)
        return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      var replayContext = new SubsequentEventReviewReceiptContext(retained.RequestId, retained.RequestHash,
        retained.ReviewBasis, retained.InputJson, retained.PreviewJson,
        long.Parse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture), null);
      var replay = await AuditCompletionService.ReviewCompletionRecordAsync(db, actor, "SUBSEQUENT_EVENT", eventId,
        long.Parse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture), ct, subsequentEventReceiptContext: replayContext);
      if (!replay.Succeeded)
        return Fail<SubsequentEventReviewCommandReceiptView>(replay.ErrorCode!, replay.Message!);
      var replayed = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      return replayed is null
        ? Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The retained review outcome is unavailable; look up the request before retrying.")
        : CommandResult<SubsequentEventReviewCommandReceiptView>.Ok(View(replayed));
    }
    var evidence = await CurrentEvidenceIdentityAsync(db, actor, initial.ClientId, engagementId,
      Fields(initial), ct);
    if (!evidence.Succeeded) return Fail<SubsequentEventReviewCommandReceiptView>(evidence.ErrorCode!, evidence.Message!);
    var hash = RequestHash(actor, engagementId, initial.ClientId, Review, request with { Fields = null }, null, evidence.Value!);
    if (request.ExpectedRequestHash != hash)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted request differs from its exact preview.");
    var preview = await PreviewReviewAsync(db, actor, engagementId, eventId, request, ct);
    if (!preview.Succeeded)
      return Fail<SubsequentEventReviewCommandReceiptView>(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.RequestHash != request.ExpectedRequestHash)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GenerationStale,
        "The exact subsequent-event evidence changed after preview; refresh and review the current evidence again.");
    if (!preview.Value.CanProceed)
      return Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.GateBlocked,
        preview.Value.Blockers.FirstOrDefault() ?? "Current independent review authority is required.");
    var context = new SubsequentEventReviewReceiptContext(request.RequestId, hash, request.ReviewBasis,
      inputJson,
      JsonSerializer.Serialize(preview.Value),
      long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture), evidence.Value!);
    var reviewed = await AuditCompletionService.ReviewCompletionRecordAsync(db, actor, "SUBSEQUENT_EVENT", eventId,
      long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture), ct, subsequentEventReceiptContext: context);
    if (!reviewed.Succeeded) return Fail<SubsequentEventReviewCommandReceiptView>(reviewed.ErrorCode!, reviewed.Message!);
    var receipt = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    return receipt is null
      ? Fail<SubsequentEventReviewCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The review outcome was not retained; look up the request before retrying.")
      : CommandResult<SubsequentEventReviewCommandReceiptView>.Ok(View(receipt));
  }

  public static async Task<CommandResult<SubsequentEventReviewCommandLookup>> LookupAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<SubsequentEventReviewCommandLookup>(ErrorCodes.Accounting.MappingInvalid, "Use the exact retained request reference.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<SubsequentEventReviewCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<SubsequentEventReviewCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockRequestAsync(db, actor, requestId, false, ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<SubsequentEventReviewCommandLookup>(auth.ErrorCode!, auth.Message!);
    var row = await db.AuditCompletionSubsequentEventCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != actor.SessionEpoch) return Fail<SubsequentEventReviewCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (row is not null && (row.EngagementId != engagementId || row.ClientId != engagement.PracticeClientId || row.RequestHash != requestHash))
      return Fail<SubsequentEventReviewCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<SubsequentEventReviewCommandLookup>(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<SubsequentEventReviewCommandLookup>.Ok(new(row is not null, row is null ? null : View(row)));
  }
}
