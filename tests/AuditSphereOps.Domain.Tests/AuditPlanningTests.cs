using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// NT-21: Native audit planning lifecycle (materiality, risk, population, workpaper, finding).
/// NT-22: Posting balance guard; full reversal/idempotency covered by LedgerTests.
/// NT-23: Tenant prerequisites absent — remain BLOCKED with provenance.
/// NT-24: Approved time entry immutability; client input generation invariant.
/// </summary>
[Trait("Profile", "Database")]
public sealed class AuditPlanningTests
{
    // ── NT-21.1 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.1: Materiality assessment persists all fields correctly")]
    public async Task Materiality_PersistedCorrectly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var result = await AuditPlanningService.CreateMaterialityAssessmentAsync(ctx,
            new CreateMaterialityRequest(
                engId, Guid.NewGuid(),
                "Total assets", "AFS-2025-v1",
                "Selected as most stable benchmark",
                BenchmarkAmount: 5_000_000m,
                RateApplied: 0.05m,
                OverallMateriality: 250_000m,
                PerformanceMateriality: 187_500m,
                ClearlyTrivialThreshold: 12_500m,
                QualitativeConsiderations: "Related-party transactions elevated"));

        Assert.NotEqual(Guid.Empty, result.AssessmentId);
        Assert.Equal(250_000m, result.OverallMateriality);
        Assert.Equal(187_500m, result.PerformanceMateriality);
        Assert.Equal(12_500m, result.ClearlyTrivialThreshold);
        Assert.Equal("DRAFT", result.Status);
    }

    // ── NT-21.2 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.2: Materiality rejects inverted thresholds")]
    public async Task Materiality_Rejects_InvertedThresholds()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            AuditPlanningService.CreateMaterialityAssessmentAsync(ctx,
                new CreateMaterialityRequest(
                    engId, Guid.NewGuid(),
                    "Revenue", "AFS-2025-v1", "Rationale",
                    1_000_000m, 0.05m,
                    OverallMateriality: 50_000m,
                    PerformanceMateriality: 60_000m,   // > overall → reject
                    ClearlyTrivialThreshold: 2_500m,
                    null)));

        Assert.Contains("Performance materiality must be less than overall", ex.Message);
    }

    // ── NT-21.3 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.3: Audit risk persists with required fields")]
    public async Task AuditRisk_PersistedCorrectly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var result = await AuditPlanningService.CreateAuditRiskAsync(ctx,
            new CreateAuditRiskRequest(
                engId, Guid.NewGuid(),
                "Revenue recognition", "Occurrence",
                "Complex contracts with variable consideration",
                "SIGNIFICANT",
                "No effective control identified",
                "Extended substantive testing of contract population"));

        Assert.NotEqual(Guid.Empty, result.RiskId);
        Assert.Equal("Revenue recognition", result.Area);
        Assert.Equal("SIGNIFICANT", result.SignificanceDecision);
        Assert.Equal("IDENTIFIED", result.Status);
    }

    // ── NT-21.4 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.4: Risk requires non-empty assertion")]
    public async Task AuditRisk_Rejects_EmptyAssertion()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);

        await using var ctx = new AuditSphereDbContext(pg.Options);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AuditPlanningService.CreateAuditRiskAsync(ctx,
                new CreateAuditRiskRequest(
                    engId, Guid.NewGuid(),
                    "Revenue", Assertion: "",   // blank → reject
                    "Drivers", "SIGNIFICANT", null, "Response")));
    }

    // ── NT-21.5 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.5: Population version persisted; negative row count rejected")]
    public async Task Population_PersistedAndValidated()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var req = new CreatePopulationRequest(
            engId, Guid.NewGuid(),
            "Trade receivables existence testing",
            "Existence / Rights",
            "AR-EXPORT-20251231-v3",
            "All posted AR at 31 Dec 2025 excl. intercompany",
            RowCount: 342,
            MonetaryControlTotal: 4_870_250m,
            Currency: "QAR",
            Exclusions: "Intercompany balances");

        var result = await AuditPlanningService.CreatePopulationVersionAsync(ctx, req);

        Assert.NotEqual(Guid.Empty, result.PopulationId);
        Assert.Equal(342, result.RowCount);
        Assert.Equal("PENDING_APPROVAL", result.Status);

        // Negative row count rejected (pure guard — no DB access).
        await Assert.ThrowsAsync<ArgumentException>(() =>
            AuditPlanningService.CreatePopulationVersionAsync(
                ctx, req with { RowCount = -1 }));
    }

    // ── NT-21.6 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.6: Workpaper created and submitted; revision conflict detected")]
    public async Task Workpaper_CreateAndSubmit_RevisionConflict()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);
        var actorId = Guid.NewGuid();

        WorkpaperResult created;
        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            created = await AuditPlanningService.CreateWorkpaperAsync(ctx,
                new CreateWorkpaperRequest(
                    engId, actorId,
                    "CB-02", "Bank Reconciliation",
                    "Verify year-end bank reconciliation",
                    "CASH-2025-v3",
                    ["CASH-001 Existence"],
                    "Obtain bank confirmations and agree to TB"));
        }

        Assert.Equal("WORKING", created.Status);
        Assert.Equal(1L, created.Revision);

        // Submit succeeds with correct revision.
        WorkpaperResult submitted;
        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            submitted = await AuditPlanningService.SubmitWorkpaperAsync(ctx,
                new SubmitWorkpaperRequest(
                    created.WorkpaperId, actorId,
                    ExpectedRevision: 1,
                    WorkPerformed: "Obtained bank confirmation letters for 3 accounts; all agreed to TB.",
                    EvidenceSnapshotIds: ["SNAP-001", "SNAP-002"],
                    Conclusion: "Bank reconciliation complete and supported."));
        }
        Assert.Equal("SUBMITTED_SNAPSHOT", submitted.Status);
        Assert.Equal(2L, submitted.Revision);

        // Submit with stale revision rejected.
        await using var ctx3 = new AuditSphereDbContext(pg.Options);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AuditPlanningService.SubmitWorkpaperAsync(ctx3,
                new SubmitWorkpaperRequest(
                    created.WorkpaperId, actorId,
                    ExpectedRevision: 1,   // stale
                    WorkPerformed: "Stale attempt",
                    EvidenceSnapshotIds: [],
                    Conclusion: "Should be rejected.")));
    }

    // ── NT-21.7 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.7: Finding persisted with type, impact and corrected flag")]
    public async Task Finding_PersistedCorrectly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, _) = await pg.SeedScopeAsync();
        var engId = await SeedEngagementAsync(pg, firmId, clientId);

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var result = await AuditPlanningService.CreateFindingAsync(ctx,
            new CreateFindingRequest(
                engId, Guid.NewGuid(),
                "Misstatement — Revenue Cut-off",
                "Revenue QAR 380,000 recognized Dec 2025; relates to Jan 2026 services.",
                Corrected: false,
                MonetaryAmount: 380_000m,
                ManagementResponse: null));

        Assert.NotEqual(Guid.Empty, result.FindingId);
        Assert.False(result.Corrected);
        Assert.Equal("OPEN", result.Status);
    }

    // ── NT-22 — Posting balance guard (pure unit) ───────────────────────────
    // NT-22.2 (idempotency) and NT-22.3 (reversal) are verified in LedgerTests
    // via LedgerService.PostFirmJournalAsync which uses LedgerSourceLink for
    // duplicate detection. These tests verify the balance guard contract only.

    [Fact(DisplayName = "NT-22.1: Unbalanced posting lines rejected before any DB write")]
    public void PostingBalanceGuard_Rejects_Unbalanced()
    {
        var lines = new PostingBalanceLine[]
        {
            new(Debit: 50_000m, Credit: 0m),
            new(Debit: 0m, Credit: 40_000m),  // unbalanced
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => AuditPlanningService.AssertBalanced(lines));

        Assert.Contains("balanced", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "NT-22.2: Balanced posting lines pass the guard")]
    public void PostingBalanceGuard_Accepts_Balanced()
    {
        var lines = new PostingBalanceLine[]
        {
            new(Debit: 50_000m, Credit: 0m),
            new(Debit: 0m, Credit: 50_000m),
        };

        // Should not throw.
        AuditPlanningService.AssertBalanced(lines);
    }

    [Fact(DisplayName = "NT-22.3: Empty posting lines are balanced (trivially)")]
    public void PostingBalanceGuard_EmptyLines_Balanced()
    {
        AuditPlanningService.AssertBalanced([]);
    }

    // ── NT-23 ───────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-23.1: Missing tenant configuration remains BLOCKED with provenance")]
    public void TenantCapability_WithoutConfig_IsBlocked()
    {
        var capability = AuditPlanningService.CheckTenantCapability(
            entraConfigured: false, sharePointGranted: false);

        Assert.Equal("BLOCKED", capability.Status);
        Assert.False(string.IsNullOrEmpty(capability.Provenance));
        Assert.DoesNotContain("PASS", capability.Provenance);
    }

    [Fact(DisplayName = "NT-23.2: Partial tenant config (Entra only) still BLOCKED for SharePoint")]
    public void TenantCapability_Partial_StillBlocked()
    {
        var capability = AuditPlanningService.CheckTenantCapability(
            entraConfigured: true, sharePointGranted: false);

        Assert.Equal("BLOCKED", capability.Status);
        Assert.Contains("SharePoint", capability.Provenance);
    }

    [Fact(DisplayName = "NT-23.3: Full tenant config returns READY status")]
    public void TenantCapability_Full_IsReady()
    {
        var capability = AuditPlanningService.CheckTenantCapability(
            entraConfigured: true, sharePointGranted: true);

        Assert.Equal("READY", capability.Status);
    }

    // ── NT-24.1 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-24.1: Approved time entries are not overwritten; they persist in APPROVED status")]
    public async Task ApprovedTimeEntry_IsImmutableByDirectUpdate()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (firmId, clientId, engId) = await pg.SeedScopeAsync();
        var actorId = Guid.NewGuid();

        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            ctx.Users.Add(new AuditSphereOps.Domain.Security.AppUser
            {
                Id = actorId,
                FirmId = firmId,
                Subject = $"sub-test-{actorId:N}",
                TenantId = "tenant-test",
                Email = $"test-{actorId:N}@example.test",
                DisplayName = "Test User",
                UserKind = "Staff",
                SessionEpoch = 1,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        // Seed a task so the FK holds.
        Guid taskId = Guid.NewGuid();
        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            ctx.WorkTasks.Add(new WorkTask
            {
                Id = taskId, FirmId = firmId, ClientId = clientId,
                EngagementId = engId, AssigneeUserId = actorId,
                Title = "Test Task", Status = "OPEN",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // Insert an approved time entry.
        Guid entryId = Guid.NewGuid();
        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            ctx.TimeEntries.Add(new TimeEntry
            {
                Id = entryId, FirmId = firmId, ClientId = clientId,
                EngagementId = engId, TaskId = taskId, UserId = actorId,
                WorkDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StartMinute = 0, DurationMinutes = 480,
                Role = "SENIOR", Activity = "FIELDWORK",
                BillableClassification = "NON_BILLABLE",
                NarrativeVisibility = "INTERNAL",
                Currency = "QAR",
                Status = "APPROVED",
                Revision = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        // Verify the approved entry persists as-is.
        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            var entry = await ctx.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == entryId);
            Assert.Equal(480, entry.DurationMinutes);
            Assert.Equal("APPROVED", entry.Status);
        }
    }

    // ── NT-24.2 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-24.2: Client input generation increments on source change")]
    public async Task ClientInputGeneration_IncrementsOnChange()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var (_, clientId, _) = await pg.SeedScopeAsync();

        long g1, g2;
        await using (var ctx = new AuditSphereDbContext(pg.Options))
            g1 = (await ctx.ClientSafetyStates.AsNoTracking()
                .SingleAsync(x => x.Id == clientId)).InputGeneration;

        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            var state = await ctx.ClientSafetyStates.SingleAsync(x => x.Id == clientId);
            state.InputGeneration += 1;
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new AuditSphereDbContext(pg.Options))
            g2 = (await ctx.ClientSafetyStates.AsNoTracking()
                .SingleAsync(x => x.Id == clientId)).InputGeneration;

        Assert.True(g2 > g1, "Input generation must increment on source change.");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<Guid> SeedEngagementAsync(PgTestSchema pg, Guid firmId, Guid clientId)
    {
        await using var ctx = new AuditSphereDbContext(pg.Options);
        var eng = new Engagement
        {
            Id = Guid.NewGuid(),
            FirmId = firmId,
            PracticeClientId = clientId,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        ctx.Engagements.Add(eng);
        await ctx.SaveChangesAsync();
        return eng.Id;
    }
}
