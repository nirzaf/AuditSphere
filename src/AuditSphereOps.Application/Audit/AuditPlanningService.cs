using System.Diagnostics;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// ---------------------------------------------------------------------------
// Requests and results (§27.7: stable codes, no failure body behind HTTP 200)
// ---------------------------------------------------------------------------

public sealed record CreateMaterialityRequest(
    Guid EngagementId,
    string BenchmarkSource,
    string BenchmarkVersion,
    string Rationale,
    decimal BenchmarkAmount,
    decimal RateApplied,
    decimal OverallMateriality,
    decimal PerformanceMateriality,
    decimal ClearlyTrivialThreshold,
    string? QualitativeConsiderations);

public sealed record MaterialityResult(
    Guid AssessmentId,
    decimal OverallMateriality,
    decimal PerformanceMateriality,
    decimal ClearlyTrivialThreshold,
    string Status);

public sealed record CreateAuditRiskRequest(
    Guid EngagementId,
    string AccountOrDisclosureArea,
    string Assertion,
    string Description,
    string Drivers,
    string SignificanceDecision,
    string? ControlsConsidered,
    string ResponseDescription);

public sealed record AuditRiskResult(
    Guid RiskId,
    string Area,
    string Assertion,
    string SignificanceDecision,
    string Severity,
    string Status);

public sealed record CreatePopulationRequest(
    Guid EngagementId,
    string Purpose,
    string Assertion,
    string SourceReceiptReference,
    string ExtractionParameters,
    int RowCount,
    decimal MonetaryControlTotal,
    string Currency,
    string? Exclusions);

public sealed record PopulationResult(
    Guid PopulationId,
    string Purpose,
    int RowCount,
    decimal MonetaryControlTotal,
    string Status);

public sealed record CreateWorkpaperRequest(
    Guid EngagementId,
    string Index,
    string Title,
    string Objective,
    string TemplateVersion,
    Guid? ProcedureId,
    string Procedure);

public sealed record SubmitWorkpaperRequest(
    Guid WorkpaperId,
    long ExpectedRevision,
    string WorkPerformed,
    string Conclusion,
    long? ExpectedDraftRevision = null,
    Guid? DraftSaveId = null);

public sealed record SaveWorkpaperDraftRequest(
    Guid WorkpaperId,
    long ExpectedDraftRevision,
    long BaseWorkpaperRevision,
    long BaseInputGeneration,
    long BasePolicyGeneration,
    Guid SaveId,
    string WorkPerformed,
    string Conclusion);

public sealed record WorkpaperDraftResult(
    Guid WorkpaperId,
    Guid? DraftId,
    long BaseWorkpaperRevision,
    long BaseInputGeneration,
    long BasePolicyGeneration,
    long DraftRevision,
    string WorkPerformed,
    string Conclusion,
    Guid LastSaveId,
    DateTimeOffset? LastSavedAt,
    string Lifecycle);

public sealed record WorkpaperResult(
    Guid WorkpaperId,
    string Index,
    string Title,
    string Status,
    long Revision);

public sealed record CreateFindingRequest(
    Guid EngagementId,
    string FindingType,
    string ImpactDescription,
    bool Corrected,
    decimal? MonetaryAmount,
    string? ManagementResponse);

public sealed record FindingResult(
    Guid FindingId,
    string FindingType,
    bool Corrected,
    string Status);

/// <summary>
/// Records the management response and correction outcome (§23). A finding is a working record, so
/// the response is revised in place; the professional conclusion it feeds remains immutable evidence.
/// Remediation ownership and dates are a separate future-action record, not part of this command.
/// </summary>
public sealed record RecordFindingResponseRequest(
    Guid FindingId,
    string ManagementResponse,
    bool Corrected);

public sealed record FindingResponseResult(
    Guid FindingId,
    string Status,
    bool Corrected);

// ---------------------------------------------------------------------------
// Posting balance guard record (NT-22.1 unit test support)
// ---------------------------------------------------------------------------

/// <summary>An abstract posting line used for balance checking across any source.</summary>
public sealed record PostingBalanceLine(decimal Debit, decimal Credit);

// ---------------------------------------------------------------------------
// Tenant capability probe (NT-23)
// ---------------------------------------------------------------------------

public sealed record TenantCapabilityResult(string Status, string Provenance);

// ---------------------------------------------------------------------------
// Service
// ---------------------------------------------------------------------------

/// <summary>
/// Guarded audit-planning commands (§§19–23). Each command authorizes the actor against the stored
/// engagement scope inside the command transaction, resolves the client from persisted rows only,
/// and writes through the EF model so the composite scope foreign keys and CHECK constraints are
/// the last word (§42.3). Nothing here records a professional conclusion.
/// </summary>
public static class AuditPlanningService
{
    private const string InvalidCode = ErrorCodes.AuditPlanning.Invalid;
    public const int MaxDraftWorkPerformedLength = 100_000;
    public const int MaxDraftConclusionLength = 20_000;

    /// <summary>Internal staff roles permitted to plan and document audit work.</summary>
    private static readonly string[] PlanningRoles =
        ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];

    // ── Materiality ─────────────────────────────────────────────────────────

    public static async Task<CommandResult<MaterialityResult>> CreateMaterialityAssessmentAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        CreateMaterialityRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        var invalid = ValidateMateriality(req);
        if (invalid is not null)
            return CommandResult<MaterialityResult>.Fail(InvalidCode, invalid);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scope = await LockedEngagementAsync(db, actor, req.EngagementId, ct);
        if (scope.Denied is not null)
            return CommandResult<MaterialityResult>.Fail(scope.Denied, scope.Message);

        var assessment = new MaterialityAssessment
        {
            Id = Guid.CreateVersion7(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = req.EngagementId,
            ActorId = actor.UserId,
            BenchmarkSource = req.BenchmarkSource.Trim(),
            BenchmarkVersion = req.BenchmarkVersion.Trim(),
            Rationale = req.Rationale.Trim(),
            BenchmarkAmount = req.BenchmarkAmount,
            RateApplied = req.RateApplied,
            OverallMateriality = req.OverallMateriality,
            PerformanceMateriality = req.PerformanceMateriality,
            ClearlyTrivialThreshold = req.ClearlyTrivialThreshold,
            QualitativeConsiderations = Blank(req.QualitativeConsiderations) ? null : req.QualitativeConsiderations!.Trim(),
            Status = MaterialityStatuses.Draft,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.MaterialityAssessments.Add(assessment);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<MaterialityResult>.Ok(new MaterialityResult(
            assessment.Id, assessment.OverallMateriality, assessment.PerformanceMateriality,
            assessment.ClearlyTrivialThreshold, assessment.Status));
    }

    public static async Task<CommandResult<MaterialityResult>> ApproveMaterialityAssessmentAsync(
        IAuditSphereDbContext db, ActorContext actor, Guid assessmentId, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existing = await db.MaterialityAssessments.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == assessmentId && x.FirmId == actor.FirmId, ct);
        if (existing is null)
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        var scope = await LockedEngagementAsync(db, actor, existing.EngagementId, ct, existing.ClientId);
        if (scope.Denied is not null)
            return CommandResult<MaterialityResult>.Fail(scope.Denied, scope.Message);
        var approval = await AuthorizationDecision.AuthorizeAsync(db, actor,
            new AuthorizationRequest(actor.FirmId, existing.ClientId, existing.EngagementId,
                ["Manager", "Partner"], InternalOnly: true), ct);
        if (!approval.Succeeded)
            return CommandResult<MaterialityResult>.Fail(approval.ErrorCode!, approval.Message!);
        if (existing.ActorId == actor.UserId)
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ScopeDenied,
                "The preparer cannot approve the same materiality assessment.");
        if (existing.Status != MaterialityStatuses.Draft || await db.MaterialityApprovals.AnyAsync(x =>
            x.FirmId == existing.FirmId && x.MaterialityAssessmentId == existing.Id, ct))
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ProtectedState,
                "Only a draft materiality assessment can be approved.");
        var approvalRecord = new MaterialityApproval
        {
            Id = Guid.CreateVersion7(), FirmId = existing.FirmId, ClientId = existing.ClientId,
            EngagementId = existing.EngagementId, MaterialityAssessmentId = existing.Id,
            ApprovedByUserId = actor.UserId, ApprovedAt = DateTimeOffset.UtcNow
        };
        db.MaterialityApprovals.Add(approvalRecord);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CommandResult<MaterialityResult>.Ok(new(existing.Id, existing.OverallMateriality,
            existing.PerformanceMateriality, existing.ClearlyTrivialThreshold, MaterialityStatuses.Approved));
    }

    // ── Risk ────────────────────────────────────────────────────────────────

    public static async Task<CommandResult<AuditRiskResult>> CreateAuditRiskAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        CreateAuditRiskRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        var invalid = ValidateRisk(req);
        if (invalid is not null)
            return CommandResult<AuditRiskResult>.Fail(InvalidCode, invalid);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scope = await LockedEngagementAsync(db, actor, req.EngagementId, ct);
        if (scope.Denied is not null)
            return CommandResult<AuditRiskResult>.Fail(scope.Denied, scope.Message);

        var risk = new AuditRisk
        {
            Id = Guid.CreateVersion7(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = req.EngagementId,
            ActorId = actor.UserId,
            AccountArea = req.AccountOrDisclosureArea.Trim(),
            Assertion = req.Assertion.Trim(),
            Description = req.Description.Trim(),
            Drivers = req.Drivers.Trim(),
            // Severity is a classification derived from the §19.3 significance decision, never a
            // free-text field the caller can shift into another column.
            Severity = RiskSeverities.ForDecision(req.SignificanceDecision),
            SignificanceDecision = req.SignificanceDecision,
            ControlsConsidered = Blank(req.ControlsConsidered) ? null : req.ControlsConsidered!.Trim(),
            ResponseDescription = req.ResponseDescription.Trim(),
            Status = RiskStatuses.Identified,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AuditRisks.Add(risk);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<AuditRiskResult>.Ok(new AuditRiskResult(
            risk.Id, risk.AccountArea, risk.Assertion, risk.SignificanceDecision, risk.Severity, risk.Status));
    }

    // ── Population ──────────────────────────────────────────────────────────

    public static async Task<CommandResult<PopulationResult>> CreatePopulationVersionAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        CreatePopulationRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        var invalid = ValidatePopulation(req);
        if (invalid is not null)
            return CommandResult<PopulationResult>.Fail(InvalidCode, invalid);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scope = await LockedEngagementAsync(db, actor, req.EngagementId, ct);
        if (scope.Denied is not null)
            return CommandResult<PopulationResult>.Fail(scope.Denied, scope.Message);

        var population = new PopulationVersion
        {
            Id = Guid.CreateVersion7(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = req.EngagementId,
            ActorId = actor.UserId,
            Purpose = req.Purpose.Trim(),
            Assertion = req.Assertion.Trim(),
            SourceReceiptReference = req.SourceReceiptReference.Trim(),
            ExtractionParameters = req.ExtractionParameters.Trim(),
            RowCount = req.RowCount,
            MonetaryControlTotal = req.MonetaryControlTotal,
            Currency = req.Currency.Trim(),
            Exclusions = Blank(req.Exclusions) ? null : req.Exclusions!.Trim(),
            Status = PopulationStatuses.PendingApproval,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.PopulationVersions.Add(population);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<PopulationResult>.Ok(new PopulationResult(
            population.Id, population.Purpose, population.RowCount,
            population.MonetaryControlTotal, population.Status));
    }

    // ── Workpaper ───────────────────────────────────────────────────────────

    public static async Task<CommandResult<WorkpaperResult>> CreateWorkpaperAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        CreateWorkpaperRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        var invalid = ValidateWorkpaper(req);
        if (invalid is not null)
            return CommandResult<WorkpaperResult>.Fail(InvalidCode, invalid);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scope = await LockedEngagementAsync(db, actor, req.EngagementId, ct);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperResult>.Fail(scope.Denied, scope.Message);

        // A supplied procedure must live in the same scope. The composite foreign key makes an
        // out-of-scope reference unrepresentable; an unknown id is denied without disclosing it (§42.3).
        if (req.ProcedureId is { } procedureId &&
            !await db.AuditProcedures.AnyAsync(p => p.Id == procedureId && p.FirmId == scope.FirmId &&
              p.ClientId == scope.ClientId && p.EngagementId == req.EngagementId, ct))
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var workpaper = new Workpaper
        {
            Id = Guid.CreateVersion7(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = req.EngagementId,
            ProcedureId = req.ProcedureId,
            ActorId = actor.UserId,
            Index = req.Index.Trim(),
            Title = req.Title.Trim(),
            Objective = req.Objective.Trim(),
            TemplateVersion = req.TemplateVersion.Trim(),
            Procedure = req.Procedure.Trim(),
            Revision = 1,
            Status = WorkpaperStatuses.Working,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Workpapers.Add(workpaper);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<WorkpaperResult>.Ok(new WorkpaperResult(
            workpaper.Id, workpaper.Index, workpaper.Title, workpaper.Status, workpaper.Revision));
    }

    /// <summary>
    /// Freezes the working content into an immutable submission (§21.1). A later review or release
    /// targets the frozen revision only; the submission row itself is never updated afterwards.
    /// </summary>
    public static async Task<CommandResult<WorkpaperResult>> SubmitWorkpaperAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        SubmitWorkpaperRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);
        using var activity = AuditDiagnostics.StartCommand("audit.workpaper.submit");
        activity?.SetTag("command", "workpaper.submit");

        if (Blank(req.Conclusion))
            return CommandResult<WorkpaperResult>.Fail(InvalidCode, "Conclusion is required.");
        if (Blank(req.WorkPerformed))
            return CommandResult<WorkpaperResult>.Fail(InvalidCode,
                "The work performed must be recorded before submission.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Snapshot read first, then the fixed lock order (engagement → target), then a locked
        // re-read so a concurrent edit cannot slip between the scope check and the freeze (§22.4).
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == req.WorkpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperResult>.Fail(scope.Denied, scope.Message);

        var target = await db.Workpapers.FromSqlInterpolated($"""
            SELECT * FROM workpapers WHERE id = {req.WorkpaperId} AND firm_id = {actor.FirmId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (target is null || target.ClientId != snapshot.ClientId ||
            target.EngagementId != snapshot.EngagementId)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        if (target.Status != WorkpaperStatuses.Working)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.ProtectedState,
                $"A {target.Status} workpaper cannot be submitted again; a corrected conclusion requires a new workpaper revision.");
        if (target.Revision != req.ExpectedRevision)
            return CommandResult<WorkpaperResult>.Fail(ErrorCodes.StaleRevision,
                "The workpaper changed while you were working; reload the current revision.");

        var generations = await ReadGenerationsAsync(db, target.FirmId, target.ClientId, ct);
        var activeDraft = await db.WorkpaperDrafts.FromSqlInterpolated($"""
            SELECT * FROM workpaper_drafts
            WHERE firm_id = {target.FirmId} AND client_id = {target.ClientId}
              AND engagement_id = {target.EngagementId} AND workpaper_id = {target.Id}
              AND owner_user_id = {actor.UserId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (req.ExpectedDraftRevision is { } expectedDraftRevision)
        {
            if (activeDraft is null)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.Drafts.NotFound,
                    "The acknowledged draft no longer exists; reload before submitting.");
            if (activeDraft.Lifecycle != WorkpaperDraftLifecycles.Active)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.Drafts.Consumed,
                    "The acknowledged draft is no longer active.");
            if (activeDraft.DraftRevision != expectedDraftRevision)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.StaleRevision,
                    "The draft changed; save or reload before submitting.");
            if (req.DraftSaveId is { } saveId && activeDraft.LastSaveId != saveId)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.IdempotencyConflict,
                    "The submitted save acknowledgement does not match the durable draft.");
            if (activeDraft.BaseWorkpaperRevision != target.Revision ||
                activeDraft.BaseInputGeneration != generations.InputGeneration ||
                activeDraft.BasePolicyGeneration != generations.PolicyGeneration)
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.Drafts.TargetChanged,
                    "The workpaper target or its generations changed; reload before submitting.");
            if (!string.Equals(activeDraft.WorkPerformed, req.WorkPerformed.Trim(), StringComparison.Ordinal) ||
                !string.Equals(activeDraft.Conclusion, req.Conclusion.Trim(), StringComparison.Ordinal))
                return CommandResult<WorkpaperResult>.Fail(ErrorCodes.IdempotencyConflict,
                    "Submission content must match the acknowledged durable draft.");
        }

        var revision = target.Revision + 1;
        var now = DateTimeOffset.UtcNow;
        var workPerformed = req.WorkPerformed.Trim();
        var conclusion = req.Conclusion.Trim();

        await db.Workpapers.Where(x => x.Id == req.WorkpaperId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.Status, WorkpaperStatuses.SubmittedSnapshot)
            .SetProperty(x => x.Revision, revision)
            .SetProperty(x => x.WorkPerformed, workPerformed)
            .SetProperty(x => x.Conclusion, conclusion)
            .SetProperty(x => x.SubmittedAt, now), ct);

        db.WorkpaperSubmissions.Add(new WorkpaperSubmission
        {
            Id = Guid.CreateVersion7(),
            FirmId = target.FirmId,
            ClientId = target.ClientId,
            EngagementId = target.EngagementId,
            WorkpaperId = target.Id,
            ActorId = actor.UserId,
            Revision = revision,
            WorkPerformed = workPerformed,
            Conclusion = conclusion,
            SubmittedAt = now
        });
        if (activeDraft?.Lifecycle == WorkpaperDraftLifecycles.Active)
            activeDraft.Lifecycle = WorkpaperDraftLifecycles.Consumed;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<WorkpaperResult>.Ok(new WorkpaperResult(
            target.Id, target.Index, target.Title, WorkpaperStatuses.SubmittedSnapshot, revision));
    }

    /// <summary>
    /// Reads the durable draft for the authenticated owner. The browser circuit is only a
    /// convenience cache; the returned content is always sourced from PostgreSQL.
    /// </summary>
    public static async Task<CommandResult<WorkpaperDraftResult>> LoadWorkpaperDraftAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        Guid workpaperId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        using var activity = AuditDiagnostics.StartCommand("audit.workpaper.draft.load");
        activity?.SetTag("command", "workpaper.draft.load");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == workpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperDraftResult>.Fail(scope.Denied, scope.Message);
        var target = await LockedWorkpaperAsync(db, actor.FirmId, workpaperId, ct);
        if (target is null || target.ClientId != snapshot.ClientId || target.EngagementId != snapshot.EngagementId)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var generations = await ReadGenerationsAsync(db, target.FirmId, target.ClientId, ct);
        var draft = await db.WorkpaperDrafts.AsNoTracking().SingleOrDefaultAsync(x =>
            x.FirmId == target.FirmId && x.ClientId == target.ClientId &&
            x.EngagementId == target.EngagementId && x.WorkpaperId == target.Id &&
            x.OwnerUserId == actor.UserId, ct);

        await tx.CommitAsync(ct);

        if (draft is null || draft.Lifecycle != WorkpaperDraftLifecycles.Active)
            return CommandResult<WorkpaperDraftResult>.Ok(new WorkpaperDraftResult(
                target.Id, null, target.Revision, generations.InputGeneration, generations.PolicyGeneration,
                0, target.WorkPerformed ?? string.Empty, target.Conclusion ?? string.Empty,
                Guid.Empty, null, draft?.Lifecycle ?? "NONE"));

        var targetChanged = draft.BaseWorkpaperRevision != target.Revision ||
                            draft.BaseInputGeneration != generations.InputGeneration ||
                            draft.BasePolicyGeneration != generations.PolicyGeneration;
        return CommandResult<WorkpaperDraftResult>.Ok(new WorkpaperDraftResult(
            draft.WorkpaperId, draft.Id, draft.BaseWorkpaperRevision, draft.BaseInputGeneration,
            draft.BasePolicyGeneration, draft.DraftRevision, draft.WorkPerformed, draft.Conclusion,
            draft.LastSaveId, draft.LastSavedAt,
            targetChanged ? "TARGET_CHANGED" : draft.Lifecycle));
    }

    /// <summary>
    /// Saves one acknowledged editor snapshot. Save IDs make retries idempotent, while the
    /// draft revision prevents an older autosave from overwriting a newer one.
    /// </summary>
    public static async Task<CommandResult<WorkpaperDraftResult>> SaveWorkpaperDraftAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        SaveWorkpaperDraftRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);
        var started = Stopwatch.GetTimestamp();
        using var activity = AuditDiagnostics.StartCommand("audit.workpaper.draft.save");
        activity?.SetTag("command", "workpaper.draft.save");

        var invalid = ValidateDraft(req);
        if (invalid is not null)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.Drafts.Conflict, invalid);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == req.WorkpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<WorkpaperDraftResult>.Fail(scope.Denied, scope.Message);
        var target = await LockedWorkpaperAsync(db, actor.FirmId, req.WorkpaperId, ct);
        if (target is null || target.ClientId != snapshot.ClientId || target.EngagementId != snapshot.EngagementId)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        if (target.Status != WorkpaperStatuses.Working)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.ProtectedState,
                "A submitted workpaper cannot accept a new draft.");
        if (target.Revision != req.BaseWorkpaperRevision)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                "The workpaper changed; reload the current target before saving.");

        var generations = await ReadGenerationsAsync(db, target.FirmId, target.ClientId, ct);
        if (generations.InputGeneration != req.BaseInputGeneration ||
            generations.PolicyGeneration != req.BasePolicyGeneration)
            return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.GenerationStale,
                "The engagement inputs or policy changed; reload the workpaper before saving.");

        var draft = await db.WorkpaperDrafts.FromSqlInterpolated($"""
            SELECT * FROM workpaper_drafts
            WHERE firm_id = {target.FirmId} AND client_id = {target.ClientId}
              AND engagement_id = {target.EngagementId} AND workpaper_id = {target.Id}
              AND owner_user_id = {actor.UserId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);

        var workPerformed = req.WorkPerformed.Trim();
        var conclusion = req.Conclusion.Trim();
        if (draft is not null)
        {
            if (draft.Lifecycle == WorkpaperDraftLifecycles.Consumed)
                return CommandResult<WorkpaperDraftResult>.Fail(
                    ErrorCodes.Drafts.Consumed,
                    "The durable draft is no longer editable.");
            if (draft.Lifecycle == WorkpaperDraftLifecycles.Discarded)
            {
                // A discarded row may be intentionally started again from the clean
                // editor state, but a late autosave from the old revision must never
                // resurrect it. The UI starts a new lifecycle with expected revision 0.
                if (req.ExpectedDraftRevision != 0)
                    return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                        "The discarded draft is stale; start a new draft from the current workpaper.");
                draft.Lifecycle = WorkpaperDraftLifecycles.Active;
                draft.DraftRevision++;
                draft.WorkPerformed = workPerformed;
                draft.Conclusion = conclusion;
                draft.LastSaveId = req.SaveId;
                draft.LastSavedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                if (draft.BaseWorkpaperRevision != req.BaseWorkpaperRevision ||
                    draft.BaseInputGeneration != req.BaseInputGeneration ||
                    draft.BasePolicyGeneration != req.BasePolicyGeneration)
                    return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.Drafts.TargetChanged,
                        "The draft is based on a changed workpaper target; reload it before saving.");
                if (draft.LastSaveId == req.SaveId)
                {
                    if (!string.Equals(draft.WorkPerformed, workPerformed, StringComparison.Ordinal) ||
                        !string.Equals(draft.Conclusion, conclusion, StringComparison.Ordinal))
                        return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.IdempotencyConflict,
                            "The save ID was already used for different draft content.");
                    await tx.CommitAsync(ct);
                    AuditDiagnostics.DraftSaveDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
                        new KeyValuePair<string, object?>[] { new("outcome", "idempotent") });
                    return CommandResult<WorkpaperDraftResult>.Ok(ToDraftResult(draft));
                }
                if (req.ExpectedDraftRevision != draft.DraftRevision)
                    return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                        "A newer draft revision has already been saved; reload before retrying.");

                draft.DraftRevision++;
                draft.WorkPerformed = workPerformed;
                draft.Conclusion = conclusion;
                draft.LastSaveId = req.SaveId;
                draft.LastSavedAt = DateTimeOffset.UtcNow;
            }
        }
        else
        {
            if (req.ExpectedDraftRevision is not 0 and not 1)
                return CommandResult<WorkpaperDraftResult>.Fail(ErrorCodes.StaleRevision,
                    "The initial draft revision is stale; reload the workpaper.");
            draft = new WorkpaperDraft
            {
                Id = Guid.CreateVersion7(),
                FirmId = target.FirmId,
                ClientId = target.ClientId,
                EngagementId = target.EngagementId,
                WorkpaperId = target.Id,
                OwnerUserId = actor.UserId,
                BaseWorkpaperRevision = req.BaseWorkpaperRevision,
                BaseInputGeneration = req.BaseInputGeneration,
                BasePolicyGeneration = req.BasePolicyGeneration,
                DraftRevision = 1,
                WorkPerformed = workPerformed,
                Conclusion = conclusion,
                LastSaveId = req.SaveId,
                LastSavedAt = DateTimeOffset.UtcNow,
                Lifecycle = WorkpaperDraftLifecycles.Active
            };
            db.WorkpaperDrafts.Add(draft);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        AuditDiagnostics.DraftSaveDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds,
            new KeyValuePair<string, object?>[] { new("outcome", "saved") });
        return CommandResult<WorkpaperDraftResult>.Ok(ToDraftResult(draft));
    }

    /// <summary>Marks a draft discarded without deleting its audit-relevant lifecycle row.</summary>
    public static async Task<CommandResult> DiscardWorkpaperDraftAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        Guid workpaperId,
        long expectedDraftRevision,
        CancellationToken ct = default)
    {
        if (workpaperId == Guid.Empty || expectedDraftRevision < 1)
            return CommandResult.Fail(ErrorCodes.Drafts.Conflict, "A workpaper and positive draft revision are required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == workpaperId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult.Fail(scope.Denied, scope.Message);
        var draft = await db.WorkpaperDrafts.FromSqlInterpolated($"""
            SELECT * FROM workpaper_drafts
            WHERE firm_id = {actor.FirmId} AND client_id = {snapshot.ClientId}
              AND engagement_id = {snapshot.EngagementId} AND workpaper_id = {workpaperId}
              AND owner_user_id = {actor.UserId}
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (draft is null)
            return CommandResult.Fail(ErrorCodes.Drafts.NotFound, "No durable draft exists for this workpaper.");
        if (draft.Lifecycle != WorkpaperDraftLifecycles.Active)
            return CommandResult.Fail(ErrorCodes.ProtectedState, "The durable draft is no longer editable.");
        if (draft.DraftRevision != expectedDraftRevision)
            return CommandResult.Fail(ErrorCodes.StaleRevision, "The draft changed; reload it before discarding.");

        draft.Lifecycle = WorkpaperDraftLifecycles.Discarded;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CommandResult.Ok();
    }

    // ── Finding ─────────────────────────────────────────────────────────────

    public static async Task<CommandResult<FindingResult>> CreateFindingAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        CreateFindingRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        var invalid = ValidateFinding(req);
        if (invalid is not null)
            return CommandResult<FindingResult>.Fail(InvalidCode, invalid);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var scope = await LockedEngagementAsync(db, actor, req.EngagementId, ct);
        if (scope.Denied is not null)
            return CommandResult<FindingResult>.Fail(scope.Denied, scope.Message);

        var finding = new Finding
        {
            Id = Guid.CreateVersion7(),
            FirmId = scope.FirmId,
            ClientId = scope.ClientId,
            EngagementId = req.EngagementId,
            ActorId = actor.UserId,
            FindingType = req.FindingType.Trim(),
            ImpactDescription = req.ImpactDescription.Trim(),
            Corrected = req.Corrected,
            MonetaryAmount = req.MonetaryAmount,
            ManagementResponse = Blank(req.ManagementResponse) ? null : req.ManagementResponse!.Trim(),
            Status = req.Corrected ? FindingStatuses.Corrected : FindingStatuses.Open,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Findings.Add(finding);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<FindingResult>.Ok(new FindingResult(
            finding.Id, finding.FindingType, finding.Corrected, finding.Status));
    }

    public static async Task<CommandResult<FindingResponseResult>> RecordFindingResponseAsync(
        IAuditSphereDbContext db,
        ActorContext actor,
        RecordFindingResponseRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (Blank(req.ManagementResponse))
            return CommandResult<FindingResponseResult>.Fail(InvalidCode,
                "The management response must be recorded verbatim before it can be filed.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var snapshot = await db.Findings.AsNoTracking()
            .SingleOrDefaultAsync(f => f.Id == req.FindingId && f.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult<FindingResponseResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct, snapshot.ClientId);
        if (scope.Denied is not null)
            return CommandResult<FindingResponseResult>.Fail(scope.Denied, scope.Message);

        var target = await db.Findings.FromSqlInterpolated($"""
            SELECT * FROM findings WHERE id = {req.FindingId} AND firm_id = {actor.FirmId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (target is null || target.ClientId != snapshot.ClientId || target.EngagementId != snapshot.EngagementId)
            return CommandResult<FindingResponseResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var response = req.ManagementResponse.Trim();
        var status = req.Corrected ? FindingStatuses.Corrected : FindingStatuses.Evaluated;
        await db.Findings.Where(x => x.Id == req.FindingId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.ManagementResponse, response)
            .SetProperty(x => x.Corrected, req.Corrected)
            .SetProperty(x => x.Status, status), ct);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CommandResult<FindingResponseResult>.Ok(new FindingResponseResult(
            target.Id, status, req.Corrected));
    }

    public static async Task<CommandResult> SetReviewPointDispositionAsync(
        IAuditSphereDbContext db, ActorContext actor, Guid reviewPointId, bool cleared,
        CancellationToken ct = default)
    {
        if (reviewPointId == Guid.Empty)
            return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var snapshot = await db.ReviewPoints.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == reviewPointId && x.FirmId == actor.FirmId, ct);
        if (snapshot is null)
            return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        var scope = await LockedEngagementAsync(db, actor, snapshot.EngagementId, ct,
            snapshot.ClientId, requireProfessionalWork: false);
        if (scope.Denied is not null)
            return CommandResult.Fail(scope.Denied, scope.Message);

        var point = await db.ReviewPoints.FromSqlInterpolated($"""
            SELECT * FROM review_points WHERE id = {reviewPointId} AND firm_id = {actor.FirmId} FOR UPDATE
            """).SingleOrDefaultAsync(ct);
        if (point is null || point.ClientId != snapshot.ClientId || point.EngagementId != snapshot.EngagementId)
            return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");

        point.Cleared = cleared;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CommandResult.Ok();
    }

    // ── Posting balance guard (NT-22.1 — pure domain invariant) ─────────────

    /// <summary>
    /// Guards that any collection of posting lines is balanced.
    /// This is a pure guard that runs before any DB write; it enforces §41.5 §22.
    /// </summary>
    public static void AssertBalanced(IEnumerable<PostingBalanceLine> lines)
    {
        var arr = lines.ToArray();
        var totalDebit  = arr.Sum(l => l.Debit);
        var totalCredit = arr.Sum(l => l.Credit);
        if (totalDebit != totalCredit)
            throw new InvalidOperationException(
                $"Posting is not balanced: debits {totalDebit:N2} ≠ credits {totalCredit:N2}.");
    }

    // ── Tenant capability probe (NT-23) ──────────────────────────────────────

    /// <summary>
    /// Returns the tenant capability status and provenance narrative.
    /// ExternalEffects.Enabled=false / AllowSimulationAdapters=true may affect this in local dev.
    /// Production requires Entra tenant, selected SharePoint grants, Purview profile.
    /// </summary>
    public static TenantCapabilityResult CheckTenantCapability(
        bool entraConfigured, bool sharePointGranted)
    {
        var reasons = new List<string>();
        if (!entraConfigured) reasons.Add("Microsoft Entra OIDC configuration absent.");
        if (!sharePointGranted) reasons.Add("SharePoint selected-resource grant not confirmed.");

        return reasons.Count > 0
            ? new("BLOCKED", string.Join(" | ", reasons))
            : new("READY", "All tenant prerequisites confirmed.");
    }

    // ── Scope resolution ────────────────────────────────────────────────────

    private static async Task<Workpaper?> LockedWorkpaperAsync(
        IAuditSphereDbContext db, Guid firmId, Guid workpaperId, CancellationToken ct) =>
        await db.Workpapers.FromSqlInterpolated($"""
            SELECT * FROM workpapers WHERE id = {workpaperId} AND firm_id = {firmId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);

    private static async Task<(long InputGeneration, long PolicyGeneration)> ReadGenerationsAsync(
        IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
    {
        var client = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(
            x => x.FirmId == firmId && x.Id == clientId, ct);
        var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == firmId, ct);
        return (client?.InputGeneration ?? 1, firm?.PolicyGeneration ?? 1);
    }

    private static WorkpaperDraftResult ToDraftResult(WorkpaperDraft draft) =>
        new(draft.WorkpaperId, draft.Id, draft.BaseWorkpaperRevision, draft.BaseInputGeneration,
            draft.BasePolicyGeneration, draft.DraftRevision, draft.WorkPerformed, draft.Conclusion,
            draft.LastSaveId, draft.LastSavedAt, draft.Lifecycle);

    private static string? ValidateDraft(SaveWorkpaperDraftRequest req)
    {
        if (req.WorkpaperId == Guid.Empty || req.SaveId == Guid.Empty)
            return "A workpaper and non-empty save ID are required.";
        if (req.ExpectedDraftRevision < 0 || req.BaseWorkpaperRevision < 1 ||
            req.BaseInputGeneration < 1 || req.BasePolicyGeneration < 1)
            return "Draft and target generations must be positive.";
        if (req.WorkPerformed.Trim().Length > MaxDraftWorkPerformedLength)
            return $"Work performed cannot exceed {MaxDraftWorkPerformedLength:N0} characters.";
        if (req.Conclusion.Trim().Length > MaxDraftConclusionLength)
            return $"Conclusion cannot exceed {MaxDraftConclusionLength:N0} characters.";
        return null;
    }

    /// <summary>Resolved engagement scope, or the nondisclosing denial for an actor who may not write it.</summary>
    private sealed record Scope(Guid FirmId, Guid ClientId, string? Denied, string Message = "Access denied.");

    /// <summary>
    /// Resolves the stored engagement, authorizes the actor against that stored scope, and
    /// serializes the command on the engagement row. The client comes from the engagement record,
    /// never from the request (§42.3). Lock order stays firm → client → engagement (§29).
    /// </summary>
    private static async Task<Scope> LockedEngagementAsync(
        IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct,
        Guid? expectedClientId = null, bool requireProfessionalWork = true)
    {
        var engagement = await db.Engagements.FromSqlInterpolated($"""
            SELECT * FROM engagements WHERE id = {engagementId} AND firm_id = {actor.FirmId} FOR UPDATE
            """).AsNoTracking().SingleOrDefaultAsync(ct);
        if (engagement is null ||
            (expectedClientId is not null && engagement.PracticeClientId != expectedClientId))
            return new Scope(Guid.Empty, Guid.Empty, ErrorCodes.ScopeDenied, "Access denied.");

        var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
            new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagement.Id,
                PlanningRoles, InternalOnly: true, RequireProfessionalWork: requireProfessionalWork), ct);
        return auth.Succeeded
            ? new Scope(engagement.FirmId, engagement.PracticeClientId, null)
            : new Scope(engagement.FirmId, engagement.PracticeClientId, auth.ErrorCode,
                auth.Message ?? "Access denied.");
    }

    // ── Validation (§27.7: malformed input is a 400-class failure, never a silent default) ──

    private static string? ValidateMateriality(CreateMaterialityRequest req)
    {
        if (Blank(req.BenchmarkSource) || Blank(req.BenchmarkVersion) || Blank(req.Rationale))
            return "The benchmark source, benchmark version and rationale are required.";
        if (req.BenchmarkAmount <= 0)
            return "The benchmark amount must be positive.";
        if (req.RateApplied <= 0 || req.RateApplied > 1)
            return "The applied rate must be greater than zero and no more than one.";
        if (req.OverallMateriality <= 0)
            return "Overall materiality must be positive.";
        if (req.PerformanceMateriality >= req.OverallMateriality)
            return "Performance materiality must be less than overall materiality.";
        return req.ClearlyTrivialThreshold >= req.PerformanceMateriality
            ? "Clearly trivial threshold must be less than performance materiality."
            : null;
    }

    private static string? ValidateRisk(CreateAuditRiskRequest req)
    {
        if (Blank(req.AccountOrDisclosureArea))
            return "The account or disclosure area is required.";
        if (Blank(req.Assertion))
            return "Assertion is required.";
        if (Blank(req.Description))
            return "The risk description is required.";
        if (Blank(req.Drivers))
            return "The risk drivers are required.";
        if (Blank(req.ResponseDescription))
            return "Response description is required.";
        return req.SignificanceDecision is not (SignificanceDecisions.Significant or SignificanceDecisions.Normal)
            ? "The significance decision must be SIGNIFICANT or NORMAL."
            : null;
    }

    private static string? ValidatePopulation(CreatePopulationRequest req)
    {
        if (Blank(req.Purpose) || Blank(req.Assertion) || Blank(req.SourceReceiptReference) ||
            Blank(req.ExtractionParameters))
            return "Purpose, assertion, source receipt reference and extraction parameters are required.";
        if (req.RowCount < 0)
            return "Row count cannot be negative.";
        if (req.MonetaryControlTotal < 0)
            return "Monetary control total cannot be negative.";
        var currency = req.Currency.Trim();
        return currency.Length != 3 || !currency.All(char.IsUpper)
            ? "Currency must be a three-character uppercase ISO code."
            : null;
    }

    private static string? ValidateWorkpaper(CreateWorkpaperRequest req) =>
        Blank(req.Index) ? "The workpaper index is required." :
        Blank(req.Title) ? "Title is required." :
        Blank(req.Objective) ? "Objective is required." :
        Blank(req.TemplateVersion) ? "The template version is required." :
        Blank(req.Procedure) ? "The procedure description is required." : null;

    private static string? ValidateFinding(CreateFindingRequest req)
    {
        if (Blank(req.FindingType))
            return "Finding type is required.";
        if (Blank(req.ImpactDescription))
            return "Impact description is required.";
        return req.MonetaryAmount < 0 ? "The finding amount cannot be negative." : null;
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
