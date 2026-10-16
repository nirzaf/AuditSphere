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

/// <summary>
/// Designates (or un-designates) a finding for the client-facing management letter (§26/R09). The
/// decision is persisted with its author and a required recommendation; internal-only findings never
/// appear in the letter, so designation is itself output-affecting evidence.
/// </summary>
public sealed record RecordManagementLetterDesignationRequest(
    Guid FindingId,
    bool Designated,
    string? Recommendation);

public sealed record ManagementLetterDesignationResult(
    Guid FindingId,
    bool Designated,
    DateTimeOffset? DesignatedAt,
    string? Recommendation);

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
public static partial class AuditPlanningService
{
    private const string InvalidCode = ErrorCodes.AuditPlanning.Invalid;
    public const int MaxDraftWorkPerformedLength = 100_000;
    public const int MaxDraftConclusionLength = 20_000;

    /// <summary>Internal staff roles permitted to plan and document audit work.</summary>
    internal static readonly string[] PlanningRoles =
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
        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var existing = await db.MaterialityAssessments.AsNoTracking().SingleOrDefaultAsync(x =>
            x.Id == assessmentId && x.FirmId == actor.FirmId, ct);
        if (existing is null)
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
        var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, existing.EngagementId, true, ct);
        if (!publication.Succeeded) return CommandResult<MaterialityResult>.Fail(publication.ErrorCode!, publication.Message!);
        var scope = await LockedEngagementAsync(db, actor, existing.EngagementId, ct, existing.ClientId);
        if (scope.Denied is not null)
            return CommandResult<MaterialityResult>.Fail(scope.Denied, scope.Message);
        var approval = await AuthorizationDecision.AuthorizeAsync(db, actor,
            new AuthorizationRequest(actor.FirmId, existing.ClientId, existing.EngagementId,
                ["Partner"], InternalOnly: true), ct);
        if (!approval.Succeeded)
            return CommandResult<MaterialityResult>.Fail(approval.ErrorCode!, approval.Message!);
        if (existing.ActorId == actor.UserId)
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ScopeDenied,
                "The preparer cannot approve the same materiality assessment.");
        var resolved = await MaterialityEngineService.ResolveAsync(db, existing, ct);
        if (resolved.Calculation is not null && !resolved.IsCurrentHead)
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ProtectedState,
                "This materiality was superseded by a later calculation or practical rounding. Approve the current assessment.");
        if (!await MaterialityEngineService.IsAssessmentCurrentAsync(db, existing.FirmId, existing.Id, ct))
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.GenerationStale,
                "The mapping or trial balance this materiality was calculated from has been replaced; recalculate it.");
        if (existing.Status != MaterialityStatuses.Draft || await db.MaterialityApprovals.AnyAsync(x =>
            x.FirmId == existing.FirmId && x.MaterialityAssessmentId == existing.Id, ct))
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.ProtectedState,
                "Only a draft materiality assessment can be approved.");
        var calculation = resolved.Calculation;
        if (calculation is null)
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.GateBlocked,
                "A manually entered or legacy materiality draft cannot be newly approved. Recalculate it from the current approved mapping and sealed trial balance.");
        if (!MaterialityEngineService.MatchesCurrentPolicy(existing, calculation, resolved.Rounding))
            return CommandResult<MaterialityResult>.Fail(ErrorCodes.GateBlocked,
                "The saved materiality inputs or thresholds do not reconcile to the current policy. Recalculate before Partner approval.");
        var approvalRecord = new MaterialityApproval
        {
            Id = Guid.CreateVersion7(), FirmId = existing.FirmId, ClientId = existing.ClientId,
            EngagementId = existing.EngagementId, MaterialityAssessmentId = existing.Id,
            ApprovedByUserId = actor.UserId, ApprovedAt = DateTimeOffset.UtcNow
        };
        db.MaterialityApprovals.Add(approvalRecord);
        await db.SaveChangesAsync(ct);
        var finalAuthority = await AuditPlanningCommandWorkspace.AuthorizeAsync(db, actor, existing.EngagementId, AuditPlanningActions.ApproveMateriality, true, ct);
        if (!finalAuthority.Succeeded) return CommandResult<MaterialityResult>.Fail(finalAuthority.ErrorCode!, finalAuthority.Message!);
        if (tx is not null) await tx.CommitAsync(ct);
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

        await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        var publication = await AuditPlanningCommandWorkspace.LockAsync(db, actor, req.EngagementId, true, ct);
        if (!publication.Succeeded) return CommandResult<AuditRiskResult>.Fail(publication.ErrorCode!, publication.Message!);
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
        var finalAuthority = await AuditPlanningCommandWorkspace.AuthorizeAsync(db, actor, req.EngagementId, AuditPlanningActions.CreateRisk, true, ct);
        if (!finalAuthority.Succeeded) return CommandResult<AuditRiskResult>.Fail(finalAuthority.ErrorCode!, finalAuthority.Message!);
        if (tx is not null) await tx.CommitAsync(ct);

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
        return (client?.InputGeneration ?? 0, firm?.PolicyGeneration ?? 0);
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
    internal sealed record Scope(Guid FirmId, Guid ClientId, string? Denied, string Message = "Access denied.");

    /// <summary>
    /// Resolves the stored engagement, authorizes the actor against that stored scope, and
    /// serializes the command on the engagement row. The client comes from the engagement record,
    /// never from the request (§42.3). Lock order stays firm → client → engagement (§29).
    /// </summary>
    internal static async Task<Scope> LockedEngagementAsync(
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
