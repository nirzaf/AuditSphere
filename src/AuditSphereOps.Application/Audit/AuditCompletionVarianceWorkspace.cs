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

public sealed record VarianceInvestigationFields(Guid? ProcedureId, string AccountArea, string PeriodReference,
  decimal ExpectedAmount, decimal ActualAmount, decimal InvestigationThreshold, string Currency,
  string? Explanation, IReadOnlyList<string> EvidenceReferences);

public sealed record VarianceInvestigationCommandRequest(Guid RequestId, string ExpectedRevision, string ReviewBasis,
  VarianceInvestigationFields? Fields = null, string? ExpectedRequestHash = null, bool Reviewed = false);

public sealed record VarianceInvestigationSnapshot(Guid Id, Guid ClientId, Guid EngagementId, Guid? ProcedureId,
  string AccountArea, string PeriodReference, string ExpectedAmount, string ActualAmount, string DifferenceAmount,
  string? DifferencePercent, string InvestigationThreshold, bool ExceedsThreshold, string? Explanation,
  string Conclusion, string Currency, IReadOnlyList<string> EvidenceReferences, string Revision,
  Guid RecordedByUserId, Guid? ReviewedByUserId, DateTimeOffset RecordedAt, DateTimeOffset? ReviewedAt,
  string? ReviewedRevision);

public sealed record VarianceInvestigationCommandState(Guid EngagementId, Guid ClientId, string AccountArea,
  string PeriodReference, string ExpectedRevision, string ReviewBasis, VarianceInvestigationSnapshot? Record,
  bool CanPrepare, bool CanReview, IReadOnlyList<string> Blockers);

public sealed record VarianceInvestigationCommandPreview(Guid RequestId, string Action, string RequestHash,
  string ReviewBasis, string ExpectedRevision, bool CanProceed, IReadOnlyList<string> Blockers,
  VarianceInvestigationSnapshot? CurrentRecord, string Summary);

public sealed record VarianceInvestigationCommandReceiptView(Guid Id, Guid EngagementId, Guid VarianceInvestigationId,
  Guid ActorId, string ActorEpoch, Guid RequestId, string RequestHash, string Action, DateTimeOffset CreatedAt,
  VarianceInvestigationCommandPreview Preview, VarianceInvestigationSnapshot Result);

public sealed record VarianceInvestigationCommandLookup(bool Found, VarianceInvestigationCommandReceiptView? Receipt);
public sealed record VarianceReviewReceiptContext(Guid RequestId, string RequestHash, string ReviewBasis,
  string InputJson, string PreviewJson, long ExpectedRevision, string? EvidenceIdentityHash);

/// <summary>Exact-scope product commands and immutable recovery for the existing variance completion record.</summary>
public static class AuditCompletionVarianceWorkspace
{
  private const string Prepare = "PREPARE";
  private const string Review = "REVIEW";
  private static readonly string[] PlanningRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
  private const decimal MaxAmount = 9_999_999_999_999.999999m;
  private const int MaxEvidenceJsonLength = 20_000;
  private const int MaxReceiptInputJsonLength = AuditCompletionVarianceCommandReceipt.MaximumInputJsonLength;

  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static string Long(long value) => value.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Revision(string? value, out long revision) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision >= 0;
  private static string Amount(decimal value) => value.ToString("0.000000", CultureInfo.InvariantCulture);

  internal static VarianceInvestigationSnapshot Snapshot(AnalyticalReviewVarianceInvestigation row)
  {
    string[] evidence;
    try { evidence = JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? []; }
    catch (JsonException) { evidence = []; }
    return new(row.Id, row.ClientId, row.EngagementId, row.ProcedureId, row.AccountArea, row.PeriodReference,
      Amount(row.ExpectedAmount), Amount(row.ActualAmount), Amount(row.DifferenceAmount),
      row.DifferencePercent is { } percent ? percent.ToString("0.0000", CultureInfo.InvariantCulture) : null,
      Amount(row.InvestigationThreshold), row.ExceedsThreshold, row.Explanation, row.Conclusion, row.Currency,
      evidence, Long(row.Revision), row.RecordedByUserId, row.ReviewedByUserId, row.RecordedAt, row.ReviewedAt,
      row.ReviewedRevision is { } reviewed ? Long(reviewed) : null);
  }

  internal static string[] EvidenceReferences(AnalyticalReviewVarianceInvestigation row)
  {
    try { return JsonSerializer.Deserialize<string[]>(row.EvidenceReferencesJson) ?? []; }
    catch (JsonException) { return []; }
  }

  private sealed record CurrentBasis(Guid ClientId, long EngagementGeneration, long ClientInputGeneration,
    bool ProfessionalWorkBlocked, bool ActiveHold, bool Frozen, AnalyticalReviewVarianceInvestigation? Record);

  private static async Task<CommandResult<CurrentBasis>> ReadBasisAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid engagementId, string area, string period, CancellationToken ct)
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
    var record = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.AccountArea == area && x.PeriodReference == period, ct);
    return CommandResult<CurrentBasis>.Ok(new(engagement.PracticeClientId, engagement.Generation, clientGeneration,
      engagement.ProfessionalWorkBlocked, held, frozen, record));
  }

  private static string BasisHash(ActorContext actor, Guid engagementId, string area, string period, CurrentBasis basis) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch,
      engagementId, basis.ClientId, basis.EngagementGeneration, basis.ClientInputGeneration,
      basis.ProfessionalWorkBlocked, basis.ActiveHold, basis.Frozen,
      Record = basis.Record is null ? null : Snapshot(basis.Record) }));

  public static async Task<CommandResult<VarianceInvestigationCommandState>> StateAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, string? accountArea, string? periodReference, CancellationToken ct = default)
  {
    var area = accountArea?.Trim().ToUpperInvariant();
    var period = periodReference?.Trim().ToUpperInvariant();
    if (engagementId == Guid.Empty || string.IsNullOrWhiteSpace(area) || area.Length > 80 ||
        string.IsNullOrWhiteSpace(period) || period.Length > 60)
      return Fail<VarianceInvestigationCommandState>(ErrorCodes.Accounting.MappingInvalid, "Select an exact account area and period.");
    var first = await ReadBasisAsync(db, actor, engagementId, area, period, ct);
    if (!first.Succeeded) return Fail<VarianceInvestigationCommandState>(first.ErrorCode!, first.Message!);
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
          new(evidenceRecord.ProcedureId, evidenceRecord.AccountArea, evidenceRecord.PeriodReference,
            evidenceRecord.ExpectedAmount, evidenceRecord.ActualAmount, evidenceRecord.InvestigationThreshold,
            evidenceRecord.Currency, evidenceRecord.Explanation, references), ct);
        evidenceReady = evidence.Succeeded;
      }
    }
    var canPrepare = planning.Succeeded && writable && (basis.Record is null || basis.Record.RecordedByUserId == actor.UserId);
    var canReview = review.Succeeded && writable && basis.Record is { } row && row.RecordedByUserId != actor.UserId &&
      row.Conclusion != VarianceInvestigationConclusions.Unexplained && row.ReviewedByUserId is null && evidenceReady;
    var blockers = new List<string>();
    if (!writable) blockers.Add(basis.Frozen ? "The engagement file is frozen." : basis.ActiveHold ? "Professional work is held on this engagement." : "Professional work is blocked on this engagement.");
    if (basis.Record is { } current)
    {
      if (current.RecordedByUserId != actor.UserId) blockers.Add("Only the recording auditor may revise this investigation.");
      if (current.ReviewedByUserId is not null) blockers.Add("A reviewed investigation must be revised against a new current basis.");
      if (current.Conclusion == VarianceInvestigationConclusions.Unexplained) blockers.Add("An unexplained above-threshold variance must be resolved before independent review.");
      if (current.RecordedByUserId == actor.UserId) blockers.Add("The preparer cannot independently review this investigation.");
      if (!evidenceReady) blockers.Add("Current exact-engagement evidence is required before independent review.");
    }
    var basisHash = BasisHash(actor, engagementId, area, period, basis);
    var second = await ReadBasisAsync(db, actor, engagementId, area, period, ct);
    if (!second.Succeeded) return Fail<VarianceInvestigationCommandState>(second.ErrorCode!, second.Message!);
    if (BasisHash(actor, engagementId, area, period, second.Value!) != basisHash)
      return Fail<VarianceInvestigationCommandState>(ErrorCodes.StaleRevision, "The variance basis changed while it was read; refresh the exact engagement state.");
    var expected = basis.Record?.Revision ?? 0;
    return CommandResult<VarianceInvestigationCommandState>.Ok(new(engagementId, basis.ClientId, area, period,
      Long(expected), basisHash, basis.Record is null ? null : Snapshot(basis.Record), canPrepare, canReview, blockers.Distinct().ToArray()));
  }

  private static VarianceInvestigationFields? Canonical(VarianceInvestigationFields? f)
  {
    if (f is null || string.IsNullOrWhiteSpace(f.AccountArea) || f.AccountArea.Trim().Length > 80 ||
        string.IsNullOrWhiteSpace(f.PeriodReference) || f.PeriodReference.Trim().Length > 60 ||
        f.ExpectedAmount is < -MaxAmount or > MaxAmount || f.ActualAmount is < -MaxAmount or > MaxAmount ||
        f.InvestigationThreshold is < 0 or > MaxAmount || decimal.Round(f.ExpectedAmount, 6) != f.ExpectedAmount ||
        decimal.Round(f.ActualAmount, 6) != f.ActualAmount || decimal.Round(f.InvestigationThreshold, 6) != f.InvestigationThreshold ||
        string.IsNullOrWhiteSpace(f.Currency) || f.Currency.Trim().Length != 3 ||
        f.Currency.Trim().ToUpperInvariant().Any(c => c is < 'A' or > 'Z') ||
        (f.Explanation?.Trim().Length ?? 0) > 4000 || f.EvidenceReferences is null || f.EvidenceReferences.Count > 100 ||
        f.EvidenceReferences.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 2000)) return null;
    var difference = decimal.Round(f.ActualAmount - f.ExpectedAmount, 6, MidpointRounding.ToEven);
    if (difference is < -MaxAmount or > MaxAmount) return null;
    var evidence = f.EvidenceReferences.Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToArray();
    if (evidence.Length != f.EvidenceReferences.Count) return null;
    if (JsonSerializer.Serialize(evidence).Length > MaxEvidenceJsonLength) return null;
    var percentage = f.ExpectedAmount == 0m ? (decimal?)null : decimal.Round(
      difference / Math.Abs(f.ExpectedAmount) * 100m, 4, MidpointRounding.ToEven);
    if (percentage is < -MaxAmount or > MaxAmount) return null;
    return f with { AccountArea = f.AccountArea.Trim().ToUpperInvariant(), PeriodReference = f.PeriodReference.Trim().ToUpperInvariant(),
      Currency = f.Currency.Trim().ToUpperInvariant(), Explanation = string.IsNullOrWhiteSpace(f.Explanation) ? null : f.Explanation.Trim(),
      EvidenceReferences = evidence };
  }

  internal static async Task<CommandResult> ValidateEvidenceAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid engagementId, VarianceInvestigationFields fields, CancellationToken ct)
  {
    var result = await CurrentEvidenceIdentityAsync(db, actor, clientId, engagementId, fields, ct);
    return result.Succeeded ? CommandResult.Ok() : CommandResult.Fail(result.ErrorCode!, result.Message!);
  }

  internal static async Task<CommandResult<string>> CurrentEvidenceIdentityAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid clientId, Guid engagementId, VarianceInvestigationFields fields, CancellationToken ct)
  {
    var references = fields.EvidenceReferences.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    if (fields.ProcedureId is { } requestedProcedureId && !await db.AuditProcedures.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == requestedProcedureId, ct))
      return CommandResult<string>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (references.Length == 0)
      return CommandResult<string>.Ok(Hashing.Sha256Hex(JsonSerializer.Serialize(new { ProcedureId = fields.ProcedureId, References = references })));
    var proceduresQuery = db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == clientId && x.EngagementId == engagementId &&
      (fields.ProcedureId == null || x.Id == fields.ProcedureId));
    if (await proceduresQuery.CountAsync(ct) > 1000)
      return CommandResult<string>.Fail(ErrorCodes.GateBlocked, "The exact procedure evidence set exceeds the bounded currentness check.");
    var procedures = await proceduresQuery.ToListAsync(ct);
    var procedureIds = procedures.Select(x => x.Id).ToArray();
    if (fields.ProcedureId is not null && procedureIds.Length == 0)
      return CommandResult<string>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var applicability = await AuditProgramService.CurrentApplicabilityDecisionsAsync(db, actor.FirmId, clientId,
      engagementId, procedures, ct);
    var results = procedureIds.Length == 0 ? [] : await db.AuditProcedureResults.AsNoTracking().Where(x =>
        x.FirmId == actor.FirmId && x.ClientId == clientId && x.EngagementId == engagementId && procedureIds.Contains(x.AuditProcedureId))
      .OrderByDescending(x => x.Revision).ThenByDescending(x => x.Id).ToListAsync(ct);
    var supported = new HashSet<string>(StringComparer.Ordinal);
    var identities = new List<object>();
    foreach (var group in results.GroupBy(x => x.AuditProcedureId))
    {
      var latest = group.First();
      string[] resultReferences = [];
      try
      {
        resultReferences = JsonSerializer.Deserialize<string[]>(latest.EvidenceReferencesJson) ?? [];
      }
      catch (JsonException)
      {
        continue;
      }
      var matched = resultReferences.Select(x => x?.Trim()).Where(x => x is not null && references.Contains(x, StringComparer.Ordinal))
        .Distinct(StringComparer.Ordinal).Cast<string>().Order(StringComparer.Ordinal).ToArray();
      if (matched.Length == 0) continue;
      var procedure = procedures.Single(x => x.Id == latest.AuditProcedureId);
      var applicabilityDecision = applicability.GetValueOrDefault(procedure.Id);
      if (procedure.ApplicabilityStatus != AuditApplicabilityStatuses.Applicable ||
          procedure.Status is not (AuditProcedureStatuses.Submitted or AuditProcedureStatuses.Reviewed) ||
          procedure.CurrentApplicabilityRevision < 1 || procedure.CurrentApplicabilityDecisionId is null ||
          applicabilityDecision is null || applicabilityDecision.Id != procedure.CurrentApplicabilityDecisionId ||
          applicabilityDecision.Revision != procedure.CurrentApplicabilityRevision ||
          applicabilityDecision.Action != AuditApplicabilityActions.Applicable ||
          applicabilityDecision.DefinitionRevision != procedure.CurrentDefinitionRevision ||
          applicabilityDecision.InputGeneration != latest.InputGeneration) continue;
      var current = await AuditFieldworkService.EvaluateProcedureEvidenceForAuthorizedScopeAsync(db, actor.FirmId,
        clientId, engagementId, latest.AuditProcedureId, ct);
      var currentAndNotRejected = current.Satisfied || current.State == "MISSING_REVIEW";
      var submittedOrReviewed = latest.Status is AuditProcedureResultStatuses.Submitted or AuditProcedureResultStatuses.Reviewed;
      if (!currentAndNotRejected || !submittedOrReviewed || current.ResultId != latest.Id || current.Revision != latest.Revision ||
          current.InputGeneration != latest.InputGeneration) continue;
      foreach (var reference in matched) supported.Add(reference);
      identities.Add(new { latest.Id, latest.AuditProcedureId, latest.Revision, latest.InputGeneration,
        latest.DefinitionRevisionId, latest.Status, CurrentState = current.State, current.ReviewId,
        current.ReviewerUserId, procedure.ApplicabilityStatus, procedure.CurrentApplicabilityRevision,
        procedure.CurrentApplicabilityDecisionId, ApplicabilityAction = applicabilityDecision.Action,
        ApplicabilityDefinitionRevision = applicabilityDecision.DefinitionRevision,
        ApplicabilityInputGeneration = applicabilityDecision.InputGeneration, References = matched });
    }
    if (references.Any(x => !supported.Contains(x)))
      return CommandResult<string>.Fail(ErrorCodes.ScopeDenied,
        "One or more references are not supported by current, non-rejected procedure evidence in this exact engagement.");
    return CommandResult<string>.Ok(Hashing.Sha256Hex(JsonSerializer.Serialize(identities)));
  }

  private static string RequestHash(ActorContext actor, Guid engagementId, Guid clientId, string action,
    VarianceInvestigationCommandRequest request, VarianceInvestigationFields? fields, string evidenceIdentityHash = "") => Hashing.Sha256Hex(JsonSerializer.Serialize(new
    { Version = 1, actor.FirmId, actor.UserId, actor.SessionEpoch, clientId, engagementId, request.RequestId,
      Action = action, request.ExpectedRevision, request.ReviewBasis, Fields = fields, EvidenceIdentityHash = evidenceIdentityHash }));

  public static async Task<CommandResult<VarianceInvestigationCommandPreview>> PreviewPrepareAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, VarianceInvestigationCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out _) || fields is null)
      return Fail<VarianceInvestigationCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Provide a bounded exact variance preparation request.");
    var state = await StateAsync(db, actor, engagementId, fields.AccountArea, fields.PeriodReference, ct);
    if (!state.Succeeded) return Fail<VarianceInvestigationCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis || request.ExpectedRevision != s.ExpectedRevision)
      return Fail<VarianceInvestigationCommandPreview>(ErrorCodes.GenerationStale, "The variance basis changed; refresh before preparing.");
    var evidence = await ValidateEvidenceAsync(db, actor, s.ClientId, engagementId, fields, ct);
    if (!evidence.Succeeded) return Fail<VarianceInvestigationCommandPreview>(evidence.ErrorCode!, evidence.Message!);
    var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, s.ClientId, engagementId, fields, ct);
    if (!evidenceIdentity.Succeeded) return Fail<VarianceInvestigationCommandPreview>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
    var blockers = s.CanPrepare ? Array.Empty<string>() : s.Blockers.Count > 0 ? s.Blockers.ToArray() : ["Current exact-scope preparation authority is required."];
    var difference = decimal.Round(fields.ActualAmount - fields.ExpectedAmount, 6, MidpointRounding.ToEven);
    var exceeds = Math.Abs(difference) > fields.InvestigationThreshold;
    var conclusion = !exceeds ? VarianceInvestigationConclusions.Explained :
      string.IsNullOrWhiteSpace(fields.Explanation) ? VarianceInvestigationConclusions.Unexplained : VarianceInvestigationConclusions.Corroborated;
    var hash = RequestHash(actor, engagementId, s.ClientId, Prepare, request, fields, evidenceIdentity.Value!);
    return CommandResult<VarianceInvestigationCommandPreview>.Ok(new(request.RequestId, Prepare, hash, s.ReviewBasis,
      s.ExpectedRevision, blockers.Length == 0, blockers, s.Record,
      $"Prepare {fields.AccountArea} for {fields.PeriodReference}; difference {Amount(difference)} {fields.Currency}; {conclusion}."));
  }

  public static async Task<CommandResult<VarianceInvestigationCommandPreview>> PreviewReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid varianceId, VarianceInvestigationCommandRequest? request, CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) || !Revision(request.ExpectedRevision, out var revision))
      return Fail<VarianceInvestigationCommandPreview>(ErrorCodes.Accounting.MappingInvalid, "Provide an exact variance review request.");
    var row = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == varianceId, ct);
    if (row is null) return Fail<VarianceInvestigationCommandPreview>(ErrorCodes.ScopeDenied, "Access denied.");
    // Resolve authorization for this exact engagement before returning any record/evidence-specific result.
    var state = await StateAsync(db, actor, engagementId, row.AccountArea, row.PeriodReference, ct);
    if (!state.Succeeded) return Fail<VarianceInvestigationCommandPreview>(state.ErrorCode!, state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis || request.ExpectedRevision != s.ExpectedRevision || row.Revision != revision ||
        s.Record?.Id != varianceId)
      return Fail<VarianceInvestigationCommandPreview>(ErrorCodes.GenerationStale, "The variance basis changed; refresh the exact record before reviewing.");
    var evidenceReferences = EvidenceReferences(row);
    if (evidenceReferences.Length == 0)
      return Fail<VarianceInvestigationCommandPreview>(ErrorCodes.GateBlocked, "At least one current exact-engagement evidence reference is required before review.");
    var evidence = await ValidateEvidenceAsync(db, actor, row.ClientId, engagementId,
      new(row.ProcedureId, row.AccountArea, row.PeriodReference, row.ExpectedAmount, row.ActualAmount,
        row.InvestigationThreshold, row.Currency, row.Explanation, evidenceReferences), ct);
    if (!evidence.Succeeded) return Fail<VarianceInvestigationCommandPreview>(evidence.ErrorCode!, evidence.Message!);
    var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, s.ClientId, engagementId,
      new(row.ProcedureId, row.AccountArea, row.PeriodReference, row.ExpectedAmount, row.ActualAmount,
        row.InvestigationThreshold, row.Currency, row.Explanation, evidenceReferences), ct);
    if (!evidenceIdentity.Succeeded) return Fail<VarianceInvestigationCommandPreview>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
    var blockers = s.CanReview ? Array.Empty<string>() : s.Blockers.Count > 0 ? s.Blockers.ToArray() : ["Current independent review authority is required."];
    var hash = RequestHash(actor, engagementId, s.ClientId, Review, request with { Fields = null }, null, evidenceIdentity.Value!);
    return CommandResult<VarianceInvestigationCommandPreview>.Ok(new(request.RequestId, Review, hash, s.ReviewBasis,
      s.ExpectedRevision, blockers.Length == 0, blockers, s.Record, "Independently review this exact variance revision."));
  }

  internal static async Task LockRequestAsync(IClientAccountingDbContext db, ActorContext actor, Guid requestId, bool write, CancellationToken ct)
  {
    var digest = Hashing.Sha256Hex(JsonSerializer.Serialize(new { Namespace = "audit-completion-variance-command-v1", actor.FirmId, actor.UserId, requestId }));
    var key = unchecked((long)Convert.ToUInt64(digest[..16], 16));
    if (write) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})", ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})", ct);
  }

  private static VarianceInvestigationCommandReceiptView View(AuditCompletionVarianceCommandReceipt receipt) =>
    new(receipt.Id, receipt.EngagementId, receipt.VarianceInvestigationId, receipt.ActorId, Long(receipt.ActorEpoch),
      receipt.RequestId, receipt.RequestHash, receipt.Action, receipt.CreatedAt,
      JsonSerializer.Deserialize<VarianceInvestigationCommandPreview>(receipt.PreviewJson, Json)!,
      JsonSerializer.Deserialize<VarianceInvestigationSnapshot>(receipt.ResultJson, Json)!);

  private static async Task<CommandResult<VarianceInvestigationCommandReceiptView>> ExistingAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid clientId, Guid requestId, string requestHash, string action, CancellationToken ct)
  {
    var prior = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (prior is null) return Fail<VarianceInvestigationCommandReceiptView>("receipt.absent", "No retained exact request was found.");
    if (prior.ActorEpoch != actor.SessionEpoch) return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (prior.EngagementId != engagementId || prior.ClientId != clientId || prior.RequestHash != requestHash || prior.Action != action)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
    return CommandResult<VarianceInvestigationCommandReceiptView>.Ok(View(prior));
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

  public static async Task<CommandResult<VarianceInvestigationCommandReceiptView>> ExecutePrepareAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, VarianceInvestigationCommandRequest? request, CancellationToken ct = default)
  {
    var fields = Canonical(request?.Fields);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !Revision(request.ExpectedRevision, out _) || fields is null)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "Preview and confirm the exact variance preparation intent.");
    var initial = await StateAsync(db, actor, engagementId, fields.AccountArea, fields.PeriodReference, ct);
    if (!initial.Succeeded) return Fail<VarianceInvestigationCommandReceiptView>(initial.ErrorCode!, initial.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var locked = await LockEngagementAsync(db, actor, engagementId, initial.Value!.ClientId, ct);
      if (!locked.Succeeded) return Fail<VarianceInvestigationCommandReceiptView>(locked.ErrorCode!, locked.Message!);
      await LockRequestAsync(db, actor, request.RequestId, true, ct);
      var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, initial.Value.ClientId, engagementId, PlanningRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded) { await tx.CommitAsync(ct); return Fail<VarianceInvestigationCommandReceiptView>(auth.ErrorCode!, auth.Message!); }
      var retained = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      if (retained is not null)
      {
        if (retained.ActorEpoch != actor.SessionEpoch)
          return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
        if (retained.EngagementId != engagementId || retained.ClientId != initial.Value.ClientId || retained.Action != Prepare ||
            retained.ReviewBasis != request.ReviewBasis || retained.InputJson != JsonSerializer.Serialize(fields) ||
            View(retained).Preview.ExpectedRevision != request.ExpectedRevision ||
            retained.RequestHash != request.ExpectedRequestHash)
          return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
        await tx.CommitAsync(ct);
        return CommandResult<VarianceInvestigationCommandReceiptView>.Ok(View(retained));
      }
      var evidenceIdentity = await CurrentEvidenceIdentityAsync(db, actor, initial.Value.ClientId, engagementId, fields, ct);
      if (!evidenceIdentity.Succeeded)
        return Fail<VarianceInvestigationCommandReceiptView>(evidenceIdentity.ErrorCode!, evidenceIdentity.Message!);
      var hash = RequestHash(actor, engagementId, initial.Value.ClientId, Prepare, request, fields, evidenceIdentity.Value!);
      var prior = await ExistingAsync(db, actor, engagementId, initial.Value.ClientId, request.RequestId, hash, Prepare, ct);
      if (prior.Succeeded) { await tx.CommitAsync(ct); return prior; }
      if (prior.ErrorCode != "receipt.absent") return prior;
      if (request.ExpectedRequestHash != hash) return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted request differs from its exact preview.");
      var preview = await PreviewPrepareAsync(db, actor, engagementId, request, ct);
      if (!preview.Succeeded) return Fail<VarianceInvestigationCommandReceiptView>(preview.ErrorCode!, preview.Message!);
      if (preview.Value!.RequestHash != request.ExpectedRequestHash)
        return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GenerationStale,
          "The exact variance evidence changed after preview; refresh and review the current evidence again.");
      if (!preview.Value.CanProceed) return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GateBlocked, preview.Value.Blockers.First());
      var revision = long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture);
      var recorded = await AuditCompletionService.RecordVarianceInvestigationAsync(db, actor,
        new(engagementId, fields.ProcedureId, fields.AccountArea, fields.PeriodReference, fields.ExpectedAmount,
          fields.ActualAmount, fields.InvestigationThreshold, fields.Currency, fields.Explanation, fields.EvidenceReferences,
          revision == 0 ? null : revision), ct);
      if (!recorded.Succeeded) return Fail<VarianceInvestigationCommandReceiptView>(recorded.ErrorCode!, recorded.Message!);
      var result = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleAsync(x =>
        x.FirmId == actor.FirmId && x.ClientId == initial.Value.ClientId && x.EngagementId == engagementId && x.Id == recorded.Value!.InvestigationId, ct);
      var row = NewReceipt(actor, initial.Value.ClientId, engagementId, result.Id, request.RequestId, hash, Prepare,
        request.ReviewBasis, JsonSerializer.Serialize(fields), JsonSerializer.Serialize(preview.Value), JsonSerializer.Serialize(Snapshot(result)));
      db.AuditCompletionVarianceCommandReceipts.Add(row);
      await db.SaveChangesAsync(ct);
      auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new(actor.FirmId, initial.Value.ClientId, engagementId, PlanningRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
      if (!auth.Succeeded) { await tx.RollbackAsync(ct); return Fail<VarianceInvestigationCommandReceiptView>(auth.ErrorCode!, auth.Message!); }
      var persisted = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleAsync(x => x.Id == row.Id, ct);
      await tx.CommitAsync(ct);
      return CommandResult<VarianceInvestigationCommandReceiptView>.Ok(View(persisted));
    }
    catch (Exception ex) when (ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")
    {
      await tx.RollbackAsync(ct);
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Concurrent variance preparation changed the outcome. Look up the retained request, then refresh before any new intent.");
    }
  }

  private static AuditCompletionVarianceCommandReceipt NewReceipt(ActorContext actor, Guid clientId, Guid engagementId,
    Guid varianceId, Guid requestId, string hash, string action, string basis, string input, string preview, string result) => new()
  {
    Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId, EngagementId = engagementId,
    VarianceInvestigationId = varianceId, ActorId = actor.UserId, ActorEpoch = actor.SessionEpoch,
    RequestId = requestId, RequestHash = hash, ReviewBasis = basis, Action = action, InputJson = input,
    PreviewJson = preview, ResultJson = result, CreatedAt = DateTimeOffset.UtcNow
  };

  public static async Task<CommandResult<VarianceInvestigationCommandReceiptView>> ExecuteReviewAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid varianceId, VarianceInvestigationCommandRequest? request, CancellationToken ct = default)
  {
    var validRevision = request is not null && Revision(request.ExpectedRevision, out _);
    if (request is not { Reviewed: true } || request.RequestId == Guid.Empty || !Hash(request.ReviewBasis) ||
        !Hash(request.ExpectedRequestHash) || !validRevision)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "Preview and confirm the exact variance review intent.");
    var initial = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.Id == varianceId, ct);
    if (initial is null) return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.ScopeDenied, "Access denied.");
    // Resolve current exact-scope authority before deriving a record-dependent request hash.
    var authorizedState = await StateAsync(db, actor, engagementId, initial.AccountArea, initial.PeriodReference, ct);
    if (!authorizedState.Succeeded)
      return Fail<VarianceInvestigationCommandReceiptView>(authorizedState.ErrorCode!, authorizedState.Message!);
    var inputJson = JsonSerializer.Serialize(request with { Fields = null });
    if (inputJson.Length > MaxReceiptInputJsonLength)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.Accounting.MappingInvalid, "The exact variance review request exceeds its retained evidence limit.");
    var retained = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    if (retained is not null)
    {
      if (retained.ActorEpoch != actor.SessionEpoch)
        return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
      if (retained.EngagementId != engagementId || retained.VarianceInvestigationId != varianceId || retained.Action != Review ||
          retained.ReviewBasis != request.ReviewBasis || retained.InputJson != inputJson ||
          retained.RequestHash != request.ExpectedRequestHash)
        return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "Changed intent cannot reuse this request identity.");
      var replayContext = new VarianceReviewReceiptContext(retained.RequestId, retained.RequestHash,
        retained.ReviewBasis, retained.InputJson, retained.PreviewJson,
        long.Parse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture), null);
      var replay = await AuditCompletionService.ReviewCompletionRecordAsync(db, actor, "VARIANCE", varianceId,
        long.Parse(request.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture), ct, replayContext);
      if (!replay.Succeeded)
        return Fail<VarianceInvestigationCommandReceiptView>(replay.ErrorCode!, replay.Message!);
      var replayed = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
      return replayed is null
        ? Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The retained review outcome is unavailable; look up the request before retrying.")
        : CommandResult<VarianceInvestigationCommandReceiptView>.Ok(View(replayed));
    }
    var evidence = await CurrentEvidenceIdentityAsync(db, actor, initial.ClientId, engagementId,
      new(initial.ProcedureId, initial.AccountArea, initial.PeriodReference, initial.ExpectedAmount, initial.ActualAmount,
        initial.InvestigationThreshold, initial.Currency, initial.Explanation, EvidenceReferences(initial)), ct);
    if (!evidence.Succeeded) return Fail<VarianceInvestigationCommandReceiptView>(evidence.ErrorCode!, evidence.Message!);
    var hash = RequestHash(actor, engagementId, initial.ClientId, Review, request with { Fields = null }, null, evidence.Value!);
    if (request.ExpectedRequestHash != hash)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The submitted request differs from its exact preview.");
    var preview = await PreviewReviewAsync(db, actor, engagementId, varianceId, request, ct);
    if (!preview.Succeeded)
      return Fail<VarianceInvestigationCommandReceiptView>(preview.ErrorCode!, preview.Message!);
    if (preview.Value!.RequestHash != request.ExpectedRequestHash)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GenerationStale,
        "The exact variance evidence changed after preview; refresh and review the current evidence again.");
    if (!preview.Value.CanProceed)
      return Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.GateBlocked,
        preview.Value.Blockers.FirstOrDefault() ?? "Current independent review authority is required.");
    var context = new VarianceReviewReceiptContext(request.RequestId, hash, request.ReviewBasis,
      inputJson,
      JsonSerializer.Serialize(preview.Value),
      long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture), evidence.Value!);
    var reviewed = await AuditCompletionService.ReviewCompletionRecordAsync(db, actor, "VARIANCE", varianceId,
      long.Parse(request.ExpectedRevision, CultureInfo.InvariantCulture), ct, context);
    if (!reviewed.Succeeded) return Fail<VarianceInvestigationCommandReceiptView>(reviewed.ErrorCode!, reviewed.Message!);
    var receipt = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == request.RequestId, ct);
    return receipt is null
      ? Fail<VarianceInvestigationCommandReceiptView>(ErrorCodes.IdempotencyConflict, "The review outcome was not retained; look up the request before retrying.")
      : CommandResult<VarianceInvestigationCommandReceiptView>.Ok(View(receipt));
  }

  public static async Task<CommandResult<VarianceInvestigationCommandLookup>> LookupAsync(IClientAccountingDbContext db,
    ActorContext actor, Guid engagementId, Guid requestId, string? requestHash, CancellationToken ct = default)
  {
    if (requestId == Guid.Empty || !Hash(requestHash)) return Fail<VarianceInvestigationCommandLookup>(ErrorCodes.Accounting.MappingInvalid, "Use the exact retained request reference.");
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == engagementId, ct);
    if (engagement is null) return Fail<VarianceInvestigationCommandLookup>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<VarianceInvestigationCommandLookup>(auth.ErrorCode!, auth.Message!);
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    await LockRequestAsync(db, actor, requestId, false, ct);
    var row = await db.AuditCompletionVarianceCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.ActorId == actor.UserId && x.RequestId == requestId, ct);
    if (row is not null && row.ActorEpoch != actor.SessionEpoch) return Fail<VarianceInvestigationCommandLookup>(ErrorCodes.GenerationStale, "This request belongs to a previous session lifetime.");
    if (row is not null && (row.EngagementId != engagementId || row.ClientId != engagement.PracticeClientId || row.RequestHash != requestHash))
      return Fail<VarianceInvestigationCommandLookup>(ErrorCodes.IdempotencyConflict, "This request belongs to another exact intent.");
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, engagement.PracticeClientId, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), InternalOnly: true), ct);
    if (!auth.Succeeded) return Fail<VarianceInvestigationCommandLookup>(auth.ErrorCode!, auth.Message!);
    await tx.CommitAsync(ct);
    return CommandResult<VarianceInvestigationCommandLookup>.Ok(new(row is not null, row is null ? null : View(row)));
  }
}
