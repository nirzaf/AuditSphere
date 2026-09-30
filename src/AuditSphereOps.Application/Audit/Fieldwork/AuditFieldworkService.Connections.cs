using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record RunSamplingRequest(Guid EngagementId, Guid ProcedureId, Guid ScheduleId, string Method, decimal? Interval, decimal? KeyItemThreshold,
  int? SampleSize, int? Seed, string Rationale);
public sealed record SamplingRunView(AuditSamplingRun Run, IReadOnlyList<AuditSelectionItem> Items, bool Reproduces);
public sealed record EvidenceCandidate(Guid UploadIntentId, Guid PbcRequestId, string RequestArea, string FileName, string ContentSha256, DateTimeOffset ReceivedAt, bool Superseded);
public sealed record ProcedureEvidenceView(Guid LinkId, Guid UploadIntentId, string FileName, string ContentSha256, string? Note, DateTimeOffset LinkedAt);
public sealed record PhysicalItemView(Guid Id, string FileIndex, string BoxReference, string Description, string CurrentLocation,
  IReadOnlyList<PhysicalEvidenceMovement> Movements, IReadOnlyList<(Guid ProcedureId, string Title)> Procedures);
public sealed record InsertAdHocProcedureRequest(Guid EngagementId, string Title, string Wording, string Reason, string? SectionTitle, Guid? RiskId);

public static partial class AuditFieldworkService
{
  public const string SamplingEngineVersion = "audit-sampling-engine.v1";

  // ── Integrated sampling tool (3.2-02) ─────────────────────────────────────────────────────────────

  /// <summary>
  /// Runs the pure sampling engine over an approved schedule's rows, persists the resulting selection through the
  /// normal selection command, and logs every parameter, the seed and the exact source digest so the selection can be
  /// re-performed and compared after reload.
  /// </summary>
  public static async Task<CommandResult<SamplingRunView>> RunSamplingAsync(IAuditSphereDbContext db, ActorContext actor, RunSamplingRequest request, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(request.Rationale)) return Invalid<SamplingRunView>("A sampling rationale is required.");
    var auth = await AuthorizeEngagementAsync(db, actor, request.EngagementId, PlanningRoles, ct);
    if (!auth.Succeeded) return CommandResult<SamplingRunView>.Fail(auth.ErrorCode!, auth.Message!);
    var schedule = await db.AuditSchedules.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ScheduleId && x.FirmId == actor.FirmId &&
      x.ClientId == auth.ClientId && x.EngagementId == request.EngagementId, ct);
    if (schedule is null) return Denied<SamplingRunView>();
    if (schedule.Status != AuditScheduleStatuses.Approved) return CommandResult<SamplingRunView>.Fail(ErrorCodes.GateBlocked, "Sample only from an approved schedule.");
    var rows = await ScheduleRowsAsync(db, actor.FirmId, schedule.Id, ct);
    var plan = new SamplingPlan(request.Method.Trim().ToUpperInvariant(), request.Interval, request.KeyItemThreshold, request.SampleSize, request.Seed);
    SamplingOutcome outcome;
    try { outcome = AuditSamplingEngine.Select(rows.Select(x => new SamplingPopulationItem(x.StableRowId, x.SignedAmount)).ToList(), plan); }
    catch (ArgumentException ex) { return Invalid<SamplingRunView>(ex.Message); }
    if (outcome.SelectedCount == 0) return CommandResult<SamplingRunView>.Fail(ErrorCodes.GateBlocked, "The parameters select no items; adjust them before recording a selection.");
    var byStable = rows.ToDictionary(x => x.StableRowId, StringComparer.Ordinal);
    var selection = await CreateSelectionAsync(db, actor, new CreateSelectionRequest(request.EngagementId, request.ProcedureId, schedule.Id, null,
      plan.Method, request.Rationale, outcome.Items.Select(i => new SelectionItemInput(i.StableRowId, i.SignedAmount, byStable[i.StableRowId].Currency,
        i.InclusionReason, byStable[i.StableRowId].Id)).ToList()), ct);
    if (!selection.Succeeded) return CommandResult<SamplingRunView>.Fail(selection.ErrorCode!, selection.Message!);
    var run = new AuditSamplingRun
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = auth.ClientId, EngagementId = request.EngagementId, SelectionId = selection.Value!.SelectionId,
      ProcedureId = request.ProcedureId, ScheduleId = schedule.Id, Method = plan.Method, Interval = plan.Interval, KeyItemThreshold = plan.KeyItemThreshold,
      SampleSize = plan.SampleSize, Seed = plan.Seed, PopulationCount = outcome.PopulationCount, PopulationAbsoluteTotal = outcome.PopulationAbsoluteTotal,
      SelectedCount = outcome.SelectedCount, SelectedAbsoluteTotal = outcome.SelectedAbsoluteTotal, CoveragePercent = outcome.CoveragePercent,
      SourceDigest = SourceDigest(rows), SelectionDigest = SelectionDigest(outcome.Items.Select(x => x.StableRowId)), EngineVersion = SamplingEngineVersion,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.AuditSamplingRuns.Add(run);
    await db.SaveChangesAsync(ct);
    return await GetSamplingRunAsync(db, actor, run.Id, ct);
  }

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
    var again = AuditSamplingEngine.Select(rows.Select(x => new SamplingPopulationItem(x.StableRowId, x.SignedAmount)).ToList(),
      new SamplingPlan(run.Method, run.Interval, run.KeyItemThreshold, run.SampleSize, run.Seed));
    var reproduces = SourceDigest(rows) == run.SourceDigest && SelectionDigest(again.Items.Select(x => x.StableRowId)) == run.SelectionDigest;
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

  private static string SourceDigest(IEnumerable<AuditScheduleRow> rows) =>
    Hashing.Sha256Hex(string.Join('\n', rows.OrderBy(x => x.StableRowId, StringComparer.Ordinal)
      .Select(x => $"{x.StableRowId}|{x.SignedAmount.ToString("0.000000", CultureInfo.InvariantCulture)}|{x.Currency}")));

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
