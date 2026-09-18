using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Engagements;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

// ---------------------------------------------------------------------------
// Materiality
// ---------------------------------------------------------------------------

public sealed record CreateMaterialityRequest(
    Guid EngagementId,
    Guid ActorId,
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

// ---------------------------------------------------------------------------
// Risk
// ---------------------------------------------------------------------------

public sealed record CreateAuditRiskRequest(
    Guid EngagementId,
    Guid ActorId,
    string AccountOrDisclosureArea,
    string Assertion,
    string Drivers,
    string SignificanceDecision,
    string? ControlsConsidered,
    string ResponseDescription);

public sealed record AuditRiskResult(
    Guid RiskId,
    string Area,
    string Assertion,
    string SignificanceDecision,
    string Status);

// ---------------------------------------------------------------------------
// Population
// ---------------------------------------------------------------------------

public sealed record CreatePopulationRequest(
    Guid EngagementId,
    Guid ActorId,
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

// ---------------------------------------------------------------------------
// WorkPaper
// ---------------------------------------------------------------------------

public sealed record CreateWorkpaperRequest(
    Guid EngagementId,
    Guid ActorId,
    string Index,
    string Title,
    string Objective,
    string TemplateVersion,
    string[] RiskAssertionRefs,
    string Procedure);

public sealed record SubmitWorkpaperRequest(
    Guid WorkpaperId,
    Guid ActorId,
    long ExpectedRevision,
    string WorkPerformed,
    string[] EvidenceSnapshotIds,
    string Conclusion);

public sealed record WorkpaperResult(
    Guid WorkpaperId,
    string Index,
    string Title,
    string Status,
    long Revision);

// ---------------------------------------------------------------------------
// Finding
// ---------------------------------------------------------------------------

public sealed record CreateFindingRequest(
    Guid EngagementId,
    Guid ActorId,
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

public static class AuditPlanningService
{
    // ── Materiality ─────────────────────────────────────────────────────────

    public static async Task<MaterialityResult> CreateMaterialityAssessmentAsync(
        IAuditSphereDbContext db,
        CreateMaterialityRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (req.OverallMateriality <= 0)
            throw new ArgumentException("Overall materiality must be positive.");
        if (req.PerformanceMateriality >= req.OverallMateriality)
            throw new ArgumentException("Performance materiality must be less than overall materiality.");
        if (req.ClearlyTrivialThreshold >= req.PerformanceMateriality)
            throw new ArgumentException("Clearly trivial threshold must be less than performance materiality.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        _ = await db.Engagements.FirstOrDefaultAsync(e => e.Id == req.EngagementId, ct)
            ?? throw new InvalidOperationException($"Engagement {req.EngagementId} not found.");

        var assessmentId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO materiality_assessments
                (id, engagement_id, actor_id, benchmark_source, benchmark_version, rationale,
                 benchmark_amount, rate_applied, overall_materiality, performance_materiality,
                 clearly_trivial_threshold, qualitative_considerations, status, created_at)
            VALUES ({assessmentId},{req.EngagementId},{req.ActorId},{req.BenchmarkSource},{req.BenchmarkVersion},{req.Rationale},
                    {req.BenchmarkAmount},{req.RateApplied},{req.OverallMateriality},{req.PerformanceMateriality},
                    {req.ClearlyTrivialThreshold},{req.QualitativeConsiderations},'DRAFT',now())
            """, ct);

        await tx.CommitAsync(ct);

        return new MaterialityResult(
            assessmentId, req.OverallMateriality, req.PerformanceMateriality,
            req.ClearlyTrivialThreshold, "DRAFT");
    }

    // ── Risk ────────────────────────────────────────────────────────────────

    public static async Task<AuditRiskResult> CreateAuditRiskAsync(
        IAuditSphereDbContext db,
        CreateAuditRiskRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(req.Assertion))
            throw new ArgumentException("Assertion is required.");
        if (string.IsNullOrWhiteSpace(req.ResponseDescription))
            throw new ArgumentException("Response description is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var eng = await db.Engagements.FirstOrDefaultAsync(e => e.Id == req.EngagementId, ct)
            ?? throw new InvalidOperationException($"Engagement {req.EngagementId} not found.");

        var riskId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO audit_risks
                (id, firm_id, client_id, engagement_id, actor_id, account_area, assertion,
                 description, drivers, severity, significance_decision,
                 controls_considered, response_description, status, created_at)
            VALUES ({riskId},{eng.FirmId},{eng.PracticeClientId},{req.EngagementId},{req.ActorId},
                    {req.AccountOrDisclosureArea},{req.Assertion},{req.Drivers},{req.Drivers},
                    {req.SignificanceDecision},{req.SignificanceDecision},{req.ControlsConsidered},
                    {req.ResponseDescription},'IDENTIFIED',now())
            """, ct);

        await tx.CommitAsync(ct);

        return new AuditRiskResult(
            riskId, req.AccountOrDisclosureArea, req.Assertion,
            req.SignificanceDecision, "IDENTIFIED");
    }

    // ── Population ──────────────────────────────────────────────────────────

    public static async Task<PopulationResult> CreatePopulationVersionAsync(
        IAuditSphereDbContext db,
        CreatePopulationRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (req.RowCount < 0)
            throw new ArgumentException("Row count cannot be negative.");
        if (req.MonetaryControlTotal < 0)
            throw new ArgumentException("Monetary control total cannot be negative.");
        if (string.IsNullOrWhiteSpace(req.Currency))
            throw new ArgumentException("Currency is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        _ = await db.Engagements.FirstOrDefaultAsync(e => e.Id == req.EngagementId, ct)
            ?? throw new InvalidOperationException($"Engagement {req.EngagementId} not found.");

        var populationId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO population_versions
                (id, engagement_id, actor_id, purpose, assertion, source_receipt_ref,
                 extraction_parameters, row_count, monetary_control_total, currency,
                 exclusions, status, created_at)
            VALUES ({populationId},{req.EngagementId},{req.ActorId},{req.Purpose},{req.Assertion},{req.SourceReceiptReference},
                    {req.ExtractionParameters},{req.RowCount},{req.MonetaryControlTotal},{req.Currency},{req.Exclusions},'PENDING_APPROVAL',now())
            """, ct);

        await tx.CommitAsync(ct);

        return new PopulationResult(
            populationId, req.Purpose, req.RowCount, req.MonetaryControlTotal, "PENDING_APPROVAL");
    }

    // ── Workpaper ───────────────────────────────────────────────────────────

    public static async Task<WorkpaperResult> CreateWorkpaperAsync(
        IAuditSphereDbContext db,
        CreateWorkpaperRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(req.Title))
            throw new ArgumentException("Title is required.");
        if (string.IsNullOrWhiteSpace(req.Objective))
            throw new ArgumentException("Objective is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var eng = await db.Engagements.FirstOrDefaultAsync(e => e.Id == req.EngagementId, ct)
            ?? throw new InvalidOperationException($"Engagement {req.EngagementId} not found.");

        var workpaperId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO workpapers
                (id, firm_id, client_id, engagement_id, procedure_id, actor_id, wp_index, title, objective,
                 template_version, procedure, state, status, revision, generation, created_at)
            VALUES ({workpaperId},{eng.FirmId},{eng.PracticeClientId},{req.EngagementId},{Guid.Empty},{req.ActorId},
                    {req.Index},{req.Title},{req.Objective},{req.TemplateVersion},{req.Procedure},
                    'WORKING','WORKING',1,1,now())
            """, ct);

        await tx.CommitAsync(ct);

        return new WorkpaperResult(workpaperId, req.Index, req.Title, "WORKING", 1);
    }

    public static async Task<WorkpaperResult> SubmitWorkpaperAsync(
        IAuditSphereDbContext db,
        SubmitWorkpaperRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(req.Conclusion))
            throw new ArgumentException("Conclusion is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var rows = await db.Database.SqlQuery<WorkpaperStatusRow>(
            $"SELECT id, revision, status FROM workpapers WHERE id = {req.WorkpaperId} FOR UPDATE").ToListAsync(ct);

        var row = rows.FirstOrDefault()
            ?? throw new InvalidOperationException($"Workpaper {req.WorkpaperId} not found.");

        if (row.Status != "WORKING")
            throw new InvalidOperationException($"Workpaper is in status {row.Status}; cannot submit.");
        if (row.Revision != req.ExpectedRevision)
            throw new InvalidOperationException("Revision conflict — workpaper was modified concurrently.");

        var newRevision = row.Revision + 1;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE workpapers
            SET    status         = 'SUBMITTED_SNAPSHOT',
                   state          = 'SUBMITTED_SNAPSHOT',
                   revision       = {newRevision},
                   work_performed = {req.WorkPerformed},
                   conclusion     = {req.Conclusion},
                   submitted_at   = now()
            WHERE  id = {req.WorkpaperId}
            """, ct);

        var submissionId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO workpaper_submissions
                (id, workpaper_id, actor_id, revision, conclusion, submitted_at)
            VALUES ({submissionId},{req.WorkpaperId},{req.ActorId},{newRevision},{req.Conclusion},now())
            """, ct);

        await tx.CommitAsync(ct);

        return new WorkpaperResult(req.WorkpaperId, string.Empty, string.Empty, "SUBMITTED_SNAPSHOT", newRevision);
    }

    // ── Finding ─────────────────────────────────────────────────────────────

    public static async Task<FindingResult> CreateFindingAsync(
        IAuditSphereDbContext db,
        CreateFindingRequest req,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(req);

        if (string.IsNullOrWhiteSpace(req.FindingType))
            throw new ArgumentException("Finding type is required.");
        if (string.IsNullOrWhiteSpace(req.ImpactDescription))
            throw new ArgumentException("Impact description is required.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var eng = await db.Engagements.FirstOrDefaultAsync(e => e.Id == req.EngagementId, ct)
            ?? throw new InvalidOperationException($"Engagement {req.EngagementId} not found.");

        var findingId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO findings
                (id, firm_id, client_id, engagement_id, actor_id, title, severity, finding_type,
                 impact_description, corrected, monetary_amount, management_response, status, created_at)
            VALUES ({findingId},{eng.FirmId},{eng.PracticeClientId},{req.EngagementId},{req.ActorId},
                    {req.FindingType},{req.FindingType},{req.FindingType},{req.ImpactDescription},
                    {req.Corrected},{req.MonetaryAmount},{req.ManagementResponse},'OPEN',now())
            """, ct);

        await tx.CommitAsync(ct);

        return new FindingResult(findingId, req.FindingType, req.Corrected, "OPEN");
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

    // ── Helpers ─────────────────────────────────────────────────────────────

    private sealed record WorkpaperStatusRow(Guid Id, long Revision, string Status);
}
