using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record RunSamplingRequest(Guid EngagementId, Guid ProcedureId, Guid ScheduleId, string Method, decimal? Interval, decimal? KeyItemThreshold,
  int? SampleSize, int? Seed, string Rationale, IReadOnlyList<string>? AttributeFields = null, string? ExpectedPreviewDigest = null);
public sealed record SamplingPreviewView(string PreviewDigest, string SourceDigest, SamplingOutcome Outcome);
public sealed record SamplingRunView(AuditSamplingRun Run, IReadOnlyList<AuditSelectionItem> Items, bool Reproduces);
public sealed record EvidenceCandidate(Guid UploadIntentId, Guid PbcRequestId, string RequestArea, string FileName, string ContentSha256, DateTimeOffset ReceivedAt, bool Superseded);
public sealed record ProcedureEvidenceView(Guid LinkId, Guid UploadIntentId, string FileName, string ContentSha256, string? Note, DateTimeOffset LinkedAt);
public sealed record PhysicalItemView(Guid Id, string FileIndex, string BoxReference, string Description, string CurrentLocation,
  IReadOnlyList<PhysicalEvidenceMovement> Movements, IReadOnlyList<(Guid ProcedureId, string Title)> Procedures);
public sealed record InsertAdHocProcedureRequest(Guid EngagementId, string Title, string Wording, string Reason, string? SectionTitle, Guid? RiskId);

public static partial class AuditFieldworkService
{
  public const string SamplingEngineVersion = "audit-sampling-engine.v1";
  public const string AttributeSamplingEngineVersion = "audit-attribute-strata-engine.v1";
  public const string SamplingOrderingPolicy = "SCHEDULE_SOURCE_LINE_THEN_STABLE_ROW_ID";

  private sealed record PreparedSampling(Guid ClientId, AuditSchedule Schedule, List<AuditScheduleRow> Rows, SamplingPlan Plan,
    SamplingOutcome Outcome, string SourceDigest, string PreviewDigest, string? AttributeFieldsJson);

  // ── Integrated sampling tool (3.2-02) ─────────────────────────────────────────────────────────────

  /// <summary>Previews the exact deterministic selection from an approved schedule without persisting it.</summary>
  public static async Task<CommandResult<SamplingPreviewView>> PreviewSamplingAsync(
    IAuditSphereDbContext db, ActorContext actor, RunSamplingRequest request, CancellationToken ct = default)
  {
    var prepared = await PrepareSamplingAsync(db, actor, request, ct);
    return !prepared.Succeeded
      ? CommandResult<SamplingPreviewView>.Fail(prepared.ErrorCode!, prepared.Message!)
      : CommandResult<SamplingPreviewView>.Ok(new(prepared.Value!.PreviewDigest, prepared.Value.SourceDigest, prepared.Value.Outcome));
  }

  /// <summary>Re-performs the reviewed preview and atomically saves its selection and provenance run.</summary>
  public static async Task<CommandResult<SamplingRunView>> RunSamplingAsync(IAuditSphereDbContext db, ActorContext actor,
    RunSamplingRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<SamplingRunView>.Fail(auth.ErrorCode!, auth.Message!);
    if (string.IsNullOrWhiteSpace(request.ExpectedPreviewDigest))
      return Invalid<SamplingRunView>("Preview the sample and review the exact selected rows before recording it.");

    var prior = await ExistingSamplingRunAsync(db, actor.FirmId, request.ExpectedPreviewDigest, ct);
    if (prior is not null)
      return await ExistingRunMatchesRequestAsync(db, actor, request, prior, ct)
        ? await GetSamplingRunAsync(db, actor, prior.Id, ct)
        : CommandResult<SamplingRunView>.Fail(ErrorCodes.IdempotencyConflict,
          "This preview reference is already bound to different sampling inputs. Preview the intended inputs again.");

    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    try
    {
      var procedure = await db.AuditProcedures.FromSqlInterpolated($"SELECT * FROM audit_procedures WHERE firm_id={actor.FirmId} AND id={request.ProcedureId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (procedure is null || procedure.EngagementId != request.EngagementId || procedure.ClientId != auth.ClientId)
        return Denied<SamplingRunView>();

      prior = await ExistingSamplingRunAsync(db, actor.FirmId, request.ExpectedPreviewDigest, ct);
      if (prior is not null)
      {
        var priorMatches = await ExistingRunMatchesRequestAsync(db, actor, request, prior, ct);
        await transaction.CommitAsync(ct);
        return priorMatches
          ? await GetSamplingRunAsync(db, actor, prior.Id, ct)
          : CommandResult<SamplingRunView>.Fail(ErrorCodes.IdempotencyConflict,
            "This preview reference is already bound to different sampling inputs. Preview the intended inputs again.");
      }

      var prepared = await PrepareSamplingAsync(db, actor, request, ct);
      if (!prepared.Succeeded) return CommandResult<SamplingRunView>.Fail(prepared.ErrorCode!, prepared.Message!);
      var sampling = prepared.Value!;
      if (!string.Equals(sampling.PreviewDigest, request.ExpectedPreviewDigest, StringComparison.Ordinal))
        return CommandResult<SamplingRunView>.Fail(ErrorCodes.GenerationStale,
          "The approved population or sampling inputs changed after preview. Refresh and review a new preview.");

      var byStable = sampling.Rows.ToDictionary(x => x.StableRowId.Trim(), StringComparer.Ordinal);
      var selection = await CreateSelectionAsync(db, actor, new CreateSelectionRequest(request.EngagementId, request.ProcedureId,
        sampling.Schedule.Id, null, sampling.Plan.Method, request.Rationale.Trim(), sampling.Outcome.Items.Select(i =>
          new SelectionItemInput(i.StableRowId, i.SignedAmount, byStable[i.StableRowId].Currency, i.InclusionReason, byStable[i.StableRowId].Id)).ToList()), ct);
      if (!selection.Succeeded) return CommandResult<SamplingRunView>.Fail(selection.ErrorCode!, selection.Message!);

      var run = new AuditSamplingRun
      {
        Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = sampling.ClientId, EngagementId = request.EngagementId,
        SelectionId = selection.Value!.SelectionId, ProcedureId = request.ProcedureId, ScheduleId = sampling.Schedule.Id,
        Method = sampling.Plan.Method, Interval = sampling.Plan.Interval, KeyItemThreshold = sampling.Plan.KeyItemThreshold,
        SampleSize = sampling.Plan.SampleSize, Seed = sampling.Plan.Seed, AttributeFields = sampling.AttributeFieldsJson,
        OrderingPolicy = SamplingOrderingPolicy, PreviewDigest = sampling.PreviewDigest, PopulationCount = sampling.Outcome.PopulationCount,
        PopulationAbsoluteTotal = sampling.Outcome.PopulationAbsoluteTotal, SelectedCount = sampling.Outcome.SelectedCount,
        SelectedAbsoluteTotal = sampling.Outcome.SelectedAbsoluteTotal, CoveragePercent = sampling.Outcome.CoveragePercent,
        SourceDigest = sampling.SourceDigest, SelectionDigest = SelectionDigest(sampling.Outcome.Items.Select(x => x.StableRowId)),
        EngineVersion = sampling.Plan.Method switch
        {
          AuditSamplingMethods.Systematic => "audit-systematic-engine.v1",
          AuditSamplingMethods.AttributeStrata => AttributeSamplingEngineVersion,
          _ => SamplingEngineVersion
        },
        CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
      };
      db.AuditSamplingRuns.Add(run);
      await db.SaveChangesAsync(ct);
      await transaction.CommitAsync(ct);
      return await GetSamplingRunAsync(db, actor, run.Id, ct);
    }
    catch (DbUpdateException)
    {
      await transaction.RollbackAsync(ct);
      prior = await ExistingSamplingRunAsync(db, actor.FirmId, request.ExpectedPreviewDigest, ct);
      if (prior is not null && await ExistingRunMatchesRequestAsync(db, actor, request, prior, ct))
        return await GetSamplingRunAsync(db, actor, prior.Id, ct);
      throw;
    }
  }

  private static async Task<CommandResult<PreparedSampling>> PrepareSamplingAsync(
    IAuditSphereDbContext db, ActorContext actor, RunSamplingRequest request, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.Rationale) || request.Rationale.Trim().Length > 4000)
      return Invalid<PreparedSampling>("A sampling rationale of up to 4,000 characters is required.");
    if (string.IsNullOrWhiteSpace(request.Method)) return Invalid<PreparedSampling>("Choose a supported sampling method.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<PreparedSampling>.Fail(auth.ErrorCode!, auth.Message!);
    var schedule = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId &&
      x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId, ct);
    if (schedule is null) return Denied<PreparedSampling>();
    if (schedule.Status != AuditScheduleStatuses.Approved)
      return CommandResult<PreparedSampling>.Fail(ErrorCodes.GateBlocked, "Sample only from an approved schedule.");

    var method = request.Method.Trim().ToUpperInvariant();
    var fields = request.AttributeFields?.Select(x => x?.Trim().ToUpperInvariant() ?? string.Empty)
      .OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (method == AuditSamplingMethods.AttributeStrata && (fields is null || fields.Length == 0))
      return Invalid<PreparedSampling>("Choose at least one attribute field for stratified attribute sampling.");
    if (method != AuditSamplingMethods.AttributeStrata && fields is { Length: > 0 })
      return Invalid<PreparedSampling>("Attribute fields can only be selected for attribute strata sampling.");

    var rows = await ScheduleRowsAsync(db, actor.FirmId, schedule.Id, ct);
    if (rows.Any(x => string.IsNullOrWhiteSpace(x.Currency)))
      return Invalid<PreparedSampling>("Every approved population row needs a currency before sampling.");
    if (rows.Select(x => x.Currency.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Skip(1).Any())
      return Invalid<PreparedSampling>("This population contains mixed currencies. Create approved currency-specific schedules before sampling.");
    var countBased = method is AuditSamplingMethods.Random or AuditSamplingMethods.Systematic or AuditSamplingMethods.Stratified or AuditSamplingMethods.AttributeStrata;
    var plan = new SamplingPlan(method,
      method == AuditSamplingMethods.MonetaryUnit ? request.Interval : null,
      method is AuditSamplingMethods.KeyItem or AuditSamplingMethods.Stratified ? request.KeyItemThreshold : null,
      countBased ? request.SampleSize : null, countBased ? request.Seed : null,
      method == AuditSamplingMethods.AttributeStrata ? fields : null);
    SamplingOutcome outcome;
    try
    {
      outcome = AuditSamplingEngine.Select(rows.Select(x => new SamplingPopulationItem(x.StableRowId, x.SignedAmount,
        new SamplingRowAttributes(x.AccountCode, x.Currency, x.TransactionDate, x.PostingDate))).ToList(), plan);
    }
    catch (ArgumentException ex) { return Invalid<PreparedSampling>(ex.Message); }
    if (outcome.SelectedCount == 0)
      return CommandResult<PreparedSampling>.Fail(ErrorCodes.GateBlocked, "The parameters select no items; adjust them before recording a selection.");
    var sourceDigest = SourceDigest(rows, preserveOrder: true, includeAttributes: method == AuditSamplingMethods.AttributeStrata);
    var previewDigest = SamplingPreviewDigest(actor.UserId, request, plan, sourceDigest, outcome);
    var attributeFieldsJson = method == AuditSamplingMethods.AttributeStrata ? JsonSerializer.Serialize(fields) : null;
    return CommandResult<PreparedSampling>.Ok(new(auth.ClientId, schedule, rows, plan, outcome, sourceDigest, previewDigest, attributeFieldsJson));
  }

  private static async Task<AuditSamplingRun?> ExistingSamplingRunAsync(IAuditSphereDbContext db, Guid firmId, string previewDigest, CancellationToken ct) =>
    await db.AuditSamplingRuns.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.PreviewDigest == previewDigest, ct);

  private static async Task<bool> ExistingRunMatchesRequestAsync(IAuditSphereDbContext db, ActorContext actor, RunSamplingRequest request,
    AuditSamplingRun run, CancellationToken ct)
  {
    if (string.IsNullOrWhiteSpace(request.Method)) return false;
    var method = request.Method.Trim().ToUpperInvariant();
    var fields = request.AttributeFields?.Select(x => x?.Trim().ToUpperInvariant() ?? string.Empty).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    var countBased = method is AuditSamplingMethods.Random or AuditSamplingMethods.Systematic or AuditSamplingMethods.Stratified or AuditSamplingMethods.AttributeStrata;
    if (run.CreatedByUserId != actor.UserId || run.EngagementId != request.EngagementId || run.ProcedureId != request.ProcedureId || run.ScheduleId != request.ScheduleId ||
        run.Method != method || run.Interval != (method == AuditSamplingMethods.MonetaryUnit ? request.Interval : null) ||
        run.KeyItemThreshold != (method is AuditSamplingMethods.KeyItem or AuditSamplingMethods.Stratified ? request.KeyItemThreshold : null) ||
        run.SampleSize != (countBased ? request.SampleSize : null) || run.Seed != (countBased ? request.Seed : null) ||
        run.AttributeFields != (method == AuditSamplingMethods.AttributeStrata ? JsonSerializer.Serialize(fields) : null)) return false;
    var selection = await db.AuditSelections.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Id == run.SelectionId, ct);
    return selection?.CreatedByUserId == actor.UserId && selection.Rationale == request.Rationale.Trim();
  }

  private static string SamplingPreviewDigest(Guid actorUserId, RunSamplingRequest request, SamplingPlan plan, string sourceDigest, SamplingOutcome outcome) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      version = "audit-sampling-preview.v1", actorUserId, request.EngagementId, request.ProcedureId, request.ScheduleId, method = plan.Method,
      interval = plan.Interval, keyItemThreshold = plan.KeyItemThreshold, sampleSize = plan.SampleSize, seed = plan.Seed,
      attributeFields = plan.AttributeFields, rationale = request.Rationale.Trim(), sourceDigest, orderingPolicy = SamplingOrderingPolicy,
      selection = outcome.Items.Select(x => x.StableRowId).ToArray()
    }));

  /// <summary>Reloads a run and re-performs it from the logged parameters over the current schedule rows.</summary>
  public static async Task<CommandResult<SamplingRunView>> GetSamplingRunAsync(IAuditSphereDbContext db, ActorContext actor, Guid runId, CancellationToken ct = default)
  {
    var run = await db.AuditSamplingRuns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == runId && x.FirmId == actor.FirmId, ct);
    if (run is null) return Denied<SamplingRunView>();
    var auth = await AuthorizeEngagementAsync(db, actor, run.EngagementId, PlanningRoles.Concat(ReviewRoles).Distinct().ToArray(), ct);
    if (!auth.Succeeded) return CommandResult<SamplingRunView>.Fail(auth.ErrorCode!, auth.Message!);
    var items = await db.AuditSelectionItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.SelectionId == run.SelectionId)
      .OrderBy(x => x.StableRowId).ToListAsync(ct);
    var rows = await ScheduleRowsAsync(db, actor.FirmId, run.ScheduleId, ct);
    var fields = ReadAttributeFields(run.AttributeFields);
    SamplingOutcome again;
    try
    {
      again = AuditSamplingEngine.Select(rows.Select(x => new SamplingPopulationItem(x.StableRowId, x.SignedAmount,
        run.Method == AuditSamplingMethods.AttributeStrata
          ? new SamplingRowAttributes(x.AccountCode, x.Currency, x.TransactionDate, x.PostingDate) : null)).ToList(),
        new SamplingPlan(run.Method, run.Interval, run.KeyItemThreshold, run.SampleSize, run.Seed, fields));
    }
    catch (ArgumentException) { return CommandResult<SamplingRunView>.Ok(new(run, items, false)); }
    var sourceDigest = SourceDigest(rows, preserveOrder: run.OrderingPolicy is not null || run.Method == AuditSamplingMethods.Systematic,
      includeAttributes: run.Method == AuditSamplingMethods.AttributeStrata && run.OrderingPolicy is not null);
    var reproduces = sourceDigest == run.SourceDigest && SelectionDigest(again.Items.Select(x => x.StableRowId)) == run.SelectionDigest;
    return CommandResult<SamplingRunView>.Ok(new(run, items, reproduces));
  }

  public static async Task<IReadOnlyList<AuditSamplingRun>> ListSamplingRunsAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct().ToArray(), ct);
    return !auth.Succeeded ? [] : await db.AuditSamplingRuns.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
  }

  private static async Task<List<AuditScheduleRow>> ScheduleRowsAsync(IAuditSphereDbContext db, Guid firmId, Guid scheduleId, CancellationToken ct) =>
    await db.AuditScheduleRows.AsNoTracking().Where(x => x.FirmId == firmId && x.ScheduleId == scheduleId).OrderBy(x => x.SourceLineNumber).ThenBy(x => x.StableRowId).ToListAsync(ct);

  private static string SourceDigest(IEnumerable<AuditScheduleRow> rows, bool preserveOrder = false, bool includeAttributes = false)
  {
    if (includeAttributes)
    {
      var attributedRows = preserveOrder ? rows : rows.OrderBy(x => x.StableRowId, StringComparer.Ordinal);
      return Hashing.Sha256Hex(JsonSerializer.Serialize(attributedRows.Select(x => new
      {
        x.SourceLineNumber, x.StableRowId, Amount = x.SignedAmount.ToString("0.000000", CultureInfo.InvariantCulture), x.Currency,
        x.AccountCode, TransactionDate = x.TransactionDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        PostingDate = x.PostingDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
      })));
    }

    return preserveOrder
      ? Hashing.Sha256Hex(string.Join('\n', rows.Select(x =>
        $"{x.SourceLineNumber}|{x.StableRowId}|{x.SignedAmount.ToString("0.000000", CultureInfo.InvariantCulture)}|{x.Currency}")))
      : Hashing.Sha256Hex(string.Join('\n', rows.OrderBy(x => x.StableRowId, StringComparer.Ordinal)
        .Select(x => $"{x.StableRowId}|{x.SignedAmount.ToString("0.000000", CultureInfo.InvariantCulture)}|{x.Currency}")));
  }

  private static IReadOnlyList<string>? ReadAttributeFields(string? json)
  {
    if (string.IsNullOrWhiteSpace(json)) return null;
    try { return JsonSerializer.Deserialize<string[]>(json); }
    catch (JsonException) { return null; }
  }

  private static string SelectionDigest(IEnumerable<string> ids) => Hashing.Sha256Hex(string.Join('\n', ids.OrderBy(x => x, StringComparer.Ordinal)));

  // ── Client evidence picker (3.2-03) ───────────────────────────────────────────────────────────────

  /// <summary>Received uploads of this engagement, newest first; an older upload replaced by a newer one of the same name is marked.</summary>
  public static async Task<IReadOnlyList<EvidenceCandidate>> EvidenceCandidatesAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return [];
    var uploads = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == auth.ClientId && x.EngagementId == engagementId &&
      x.State == PbcUploadStates.Received).ToListAsync(ct);
    var requestIds = uploads.Select(x => x.PbcRequestId).Distinct().ToArray();
    var areas = await db.PbcRequests.AsNoTracking().Where(x => requestIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Area, ct);
    return uploads.OrderByDescending(x => x.CreatedAt).Select(x => new EvidenceCandidate(x.Id, x.PbcRequestId, areas.GetValueOrDefault(x.PbcRequestId, ""),
      x.FileName, Digest(x), x.CreatedAt, IsSuperseded(x, uploads))).ToList();
  }

  public static async Task<CommandResult<Guid>> LinkClientEvidenceAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, Guid uploadIntentId, string? note, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    if (procedure is null) return Denied<Guid>();
    var auth = await AuthorizeEngagementAsync(db, actor, procedure.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var upload = await db.PbcUploadIntents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == uploadIntentId && x.FirmId == actor.FirmId, ct);
    // Another client's or engagement's upload is indistinguishable from a missing one.
    if (upload is null || upload.ClientId != procedure.ClientId || upload.EngagementId != procedure.EngagementId) return Denied<Guid>();
    if (upload.State != PbcUploadStates.Received)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Only a received upload with a provider receipt can be linked as evidence.");
    var siblings = await db.PbcUploadIntents.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.PbcRequestId == upload.PbcRequestId && x.State == PbcUploadStates.Received).ToListAsync(ct);
    if (IsSuperseded(upload, siblings))
      return CommandResult<Guid>.Fail(ErrorCodes.GenerationStale, "A newer version of this file was received for the request; link that version instead.");
    var existing = await db.ProcedureEvidenceLinks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProcedureId == procedureId && x.PbcUploadIntentId == uploadIntentId, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var link = new ProcedureEvidenceLink
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = procedure.ClientId, EngagementId = procedure.EngagementId, ProcedureId = procedureId,
      PbcUploadIntentId = upload.Id, PbcRequestId = upload.PbcRequestId, FileName = upload.FileName, ContentSha256 = Digest(upload),
      Note = TrimOrNull(note), LinkedByUserId = actor.UserId, LinkedAt = DateTimeOffset.UtcNow
    };
    db.ProcedureEvidenceLinks.Add(link);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(link.Id);
  }

  public static async Task<IReadOnlyList<ProcedureEvidenceView>> ProcedureEvidenceAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    if (procedure is null || !(await AuthorizeEngagementAsync(db, actor, procedure.EngagementId, PlanningRoles.Concat(ReviewRoles).Distinct().ToArray(), ct)).Succeeded) return [];
    return await db.ProcedureEvidenceLinks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProcedureId == procedureId).OrderBy(x => x.LinkedAt)
      .Select(x => new ProcedureEvidenceView(x.Id, x.PbcUploadIntentId, x.FileName, x.ContentSha256, x.Note, x.LinkedAt)).ToListAsync(ct);
  }

  private static string Digest(PbcUploadIntent x) => x.ProviderReceiptDigest ?? x.FinalSha256Hex ?? x.DeclaredSha256Hex;

  private static bool IsSuperseded(PbcUploadIntent upload, IEnumerable<PbcUploadIntent> received) =>
    received.Any(x => x.Id != upload.Id && x.PbcRequestId == upload.PbcRequestId && x.CreatedAt > upload.CreatedAt &&
      string.Equals(x.FileName, upload.FileName, StringComparison.OrdinalIgnoreCase));

  // ── Physical file index (3.2-04) ──────────────────────────────────────────────────────────────────

  public static async Task<CommandResult<Guid>> RegisterPhysicalItemAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId,
    string fileIndex, string boxReference, string description, string location, CancellationToken ct = default)
  {
    if (new[] { fileIndex, boxReference, description, location }.Any(string.IsNullOrWhiteSpace) || fileIndex.Trim().Length > 40 || boxReference.Trim().Length > 40)
      return Invalid<Guid>("File index, box, description and location are required.");
    var auth = await AuthorizeEngagementAsync(db, actor, engagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.PhysicalEvidenceItems.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId && x.FileIndex == fileIndex.Trim().ToUpperInvariant(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, $"File index {fileIndex.Trim().ToUpperInvariant()} is already registered on this engagement.");
    var now = DateTimeOffset.UtcNow;
    var item = new PhysicalEvidenceItem
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = engagementId, FileIndex = fileIndex.Trim().ToUpperInvariant(),
      BoxReference = boxReference.Trim(), Description = description.Trim(), CurrentLocation = location.Trim(), CreatedByUserId = actor.UserId, CreatedAt = now
    };
    db.PhysicalEvidenceItems.Add(item);
    db.PhysicalEvidenceMovements.Add(new PhysicalEvidenceMovement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PhysicalEvidenceItemId = item.Id, FromLocation = null, ToLocation = item.CurrentLocation,
      Reason = "Registered", MovedByUserId = actor.UserId, MovedAt = now
    });
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(item.Id);
  }

  public static async Task<CommandResult> MovePhysicalItemAsync(IAuditSphereDbContext db, ActorContext actor, Guid itemId, string toLocation, string reason, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(toLocation) || string.IsNullOrWhiteSpace(reason)) return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "A new location and reason are required.");
    var item = await db.PhysicalEvidenceItems.SingleOrDefaultAsync(x => x.Id == itemId && x.FirmId == actor.FirmId, ct);
    if (item is null) return Denied();
    var auth = await AuthorizeEngagementAsync(db, actor, item.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult.Fail(auth.ErrorCode!, auth.Message!);
    db.PhysicalEvidenceMovements.Add(new PhysicalEvidenceMovement
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, PhysicalEvidenceItemId = item.Id, FromLocation = item.CurrentLocation, ToLocation = toLocation.Trim(),
      Reason = reason.Trim(), MovedByUserId = actor.UserId, MovedAt = DateTimeOffset.UtcNow
    });
    item.CurrentLocation = toLocation.Trim();
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<Guid>> LinkPhysicalItemAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, Guid itemId, CancellationToken ct = default)
  {
    var procedure = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    var item = await db.PhysicalEvidenceItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId && x.FirmId == actor.FirmId, ct);
    if (procedure is null || item is null || item.EngagementId != procedure.EngagementId) return Denied<Guid>();
    var auth = await AuthorizeEngagementAsync(db, actor, procedure.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var existing = await db.ProcedurePhysicalLinks.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ProcedureId == procedureId && x.PhysicalEvidenceItemId == itemId, ct);
    if (existing is not null) return CommandResult<Guid>.Ok(existing.Id);
    var link = new ProcedurePhysicalLink
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = procedure.ClientId, EngagementId = procedure.EngagementId, ProcedureId = procedureId,
      PhysicalEvidenceItemId = itemId, LinkedByUserId = actor.UserId, LinkedAt = DateTimeOffset.UtcNow
    };
    db.ProcedurePhysicalLinks.Add(link);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(link.Id);
  }

  public static async Task<IReadOnlyList<PhysicalItemView>> PhysicalItemsAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    if (!(await AuthorizeEngagementAsync(db, actor, engagementId, PlanningRoles.Concat(ReviewRoles).Distinct().ToArray(), ct)).Succeeded) return [];
    var items = await db.PhysicalEvidenceItems.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId).OrderBy(x => x.FileIndex).ToListAsync(ct);
    var ids = items.Select(x => x.Id).ToArray();
    var movements = await db.PhysicalEvidenceMovements.AsNoTracking().Where(x => ids.Contains(x.PhysicalEvidenceItemId)).OrderBy(x => x.MovedAt).ToListAsync(ct);
    var links = await db.ProcedurePhysicalLinks.AsNoTracking().Where(x => ids.Contains(x.PhysicalEvidenceItemId))
      .Join(db.AuditProcedures.AsNoTracking(), l => l.ProcedureId, p => p.Id, (l, p) => new { l.PhysicalEvidenceItemId, p.Id, p.Title }).ToListAsync(ct);
    return items.Select(i => new PhysicalItemView(i.Id, i.FileIndex, i.BoxReference, i.Description, i.CurrentLocation,
      movements.Where(m => m.PhysicalEvidenceItemId == i.Id).ToList(), links.Where(l => l.PhysicalEvidenceItemId == i.Id).Select(l => (l.Id, l.Title)).ToList())).ToList();
  }

  // ── Ad hoc procedure insertion (3.2-05) ───────────────────────────────────────────────────────────

  /// <summary>
  /// Inserts an editable, applicable step into the engagement's adopted programme without touching the controlled
  /// baseline; the step then counts in review and completion gates like any adopted procedure.
  /// </summary>
  public static async Task<CommandResult<Guid>> InsertAdHocProcedureAsync(IAuditSphereDbContext db, ActorContext actor, InsertAdHocProcedureRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Title) || string.IsNullOrWhiteSpace(request.Wording) || string.IsNullOrWhiteSpace(request.Reason) ||
        request.Title.Trim().Length > 300 || request.Wording.Trim().Length > 4000)
      return Invalid<Guid>("A title, the step wording and the reason for the unusual risk or transaction are required.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var program = await db.EngagementAuditPrograms.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId &&
      x.Status == EngagementAuditProgramStatuses.Adopted).OrderByDescending(x => x.AdoptedAt).FirstOrDefaultAsync(ct);
    if (program is null) return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Adopt an audit programme before inserting ad hoc steps.");
    if (request.RiskId is { } riskId && !await db.AuditRisks.AnyAsync(x => x.Id == riskId && x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId, ct))
      return Denied<Guid>();
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    var sequence = await db.AdHocProcedureInsertions.CountAsync(x => x.FirmId == actor.FirmId && x.EngagementId == request.EngagementId, ct) + 1;
    var now = DateTimeOffset.UtcNow;
    var procedure = new AuditProcedure
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId, EngagementProgramId = program.Id,
      ProgramProcedureId = null, RiskId = request.RiskId, SourceProcedureId = $"ADHOC-{sequence:000}", SourceSectionTitle = TrimOrNull(request.SectionTitle) ?? "Ad hoc",
      SourceWording = request.Wording.Trim(), ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
      ApplicabilityRationale = "Inserted ad hoc: " + request.Reason.Trim(), ApplicabilityDecidedByUserId = actor.UserId, ApplicabilityDecidedAt = now,
      Title = request.Title.Trim(), Status = AuditProcedureStatuses.Planned, CreatedAt = now
    };
    db.AuditProcedures.Add(procedure);
    db.AdHocProcedureInsertions.Add(new AdHocProcedureInsertion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId, ProcedureId = procedure.Id,
      EngagementProgramId = program.Id, BaselineProgramVersionId = program.ProgramVersionId, RiskId = request.RiskId, Reason = request.Reason.Trim(),
      InsertedByUserId = actor.UserId, InsertedAt = now
    });
    db.AdHocProcedureRevisions.Add(new AdHocProcedureRevision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProcedureId = procedure.Id, Revision = 1, Wording = procedure.SourceWording!, EditedByUserId = actor.UserId, EditedAt = now
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(procedure.Id);
  }

  /// <summary>Edits an ad hoc step's wording before any result is submitted; each edit is a new revision.</summary>
  public static async Task<CommandResult> EditAdHocProcedureAsync(IAuditSphereDbContext db, ActorContext actor, Guid procedureId, string wording, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(wording) || wording.Trim().Length > 4000) return CommandResult.Fail(ErrorCodes.AuditPlanning.Invalid, "Step wording is required.");
    var procedure = await db.AuditProcedures.SingleOrDefaultAsync(x => x.Id == procedureId && x.FirmId == actor.FirmId, ct);
    if (procedure is null || !await db.AdHocProcedureInsertions.AnyAsync(x => x.FirmId == actor.FirmId && x.ProcedureId == procedureId, ct))
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Only ad hoc steps can be edited; controlled programme wording is never changed.");
    var auth = await AuthorizeEngagementAsync(db, actor, procedure.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult.Fail(auth.ErrorCode!, auth.Message!);
    if (procedure.CurrentResultRevision > 0 || procedure.Status != AuditProcedureStatuses.Planned)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The step already has submitted work; insert a new step instead of rewording it.");
    var revision = await db.AdHocProcedureRevisions.Where(x => x.FirmId == actor.FirmId && x.ProcedureId == procedureId).MaxAsync(x => x.Revision, ct) + 1;
    procedure.SourceWording = wording.Trim();
    db.AdHocProcedureRevisions.Add(new AdHocProcedureRevision
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProcedureId = procedureId, Revision = revision, Wording = procedure.SourceWording, EditedByUserId = actor.UserId, EditedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  // ── Mandatory analytical review and going concern (3.2-06) ────────────────────────────────────────

  /// <summary>
  /// Completion blockers for the standard forms: at least one reviewed analytical review with no unexplained
  /// variance above threshold, and a reviewed going-concern assessment covering at least twelve months from the period end.
  /// </summary>
  internal static async Task<IReadOnlyList<string>> StandardFormBlockersAsync(IAuditSphereDbContext db, Guid firmId, Guid engagementId, CancellationToken ct)
  {
    var blockers = new List<string>();
    var engagement = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == engagementId && x.FirmId == firmId, ct);
    // ISA 570 and substantive analytics apply to audit engagements; accounting-only work is not gated by them.
    if (!engagement.ServiceRoute.Contains("Audit", StringComparison.OrdinalIgnoreCase)) return blockers;
    var analytics = await db.AnalyticalReviewVarianceInvestigations.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId).ToListAsync(ct);
    if (analytics.Count == 0) blockers.Add("analytical-review:missing");
    else
    {
      if (analytics.Any(x => x.ReviewedByUserId is null)) blockers.Add("analytical-review:unreviewed");
      if (analytics.Any(x => x.ExceedsThreshold && x.Conclusion == VarianceInvestigationConclusions.Unexplained)) blockers.Add("analytical-review:unexplained-variance");
    }
    var goingConcern = await db.GoingConcernAssessments.AsNoTracking().Where(x => x.FirmId == firmId && x.EngagementId == engagementId)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (goingConcern is null) blockers.Add("going-concern:missing");
    else
    {
      if (goingConcern.ReviewedByUserId is null) blockers.Add("going-concern:unreviewed");
      if (DateOnly.TryParseExact(engagement.PeriodEnd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var periodEnd) &&
          goingConcern.PeriodCoveredTo < periodEnd.AddMonths(12))
        blockers.Add("going-concern:horizon-under-12-months");
    }
    return blockers;
  }
}
