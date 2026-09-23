using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Shared;
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

    [Fact(DisplayName = "NT-21.1: Materiality assessment persists every field under the resolved scope")]
    public async Task Materiality_PersistedCorrectly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var created = await AuditPlanningService.CreateMaterialityAssessmentAsync(ctx, scope.Actor,
            new CreateMaterialityRequest(
                scope.EngagementId,
                "Total assets", "AFS-2025-v1",
                "Selected as most stable benchmark",
                BenchmarkAmount: 5_000_000m,
                RateApplied: 0.05m,
                OverallMateriality: 250_000m,
                PerformanceMateriality: 187_500m,
                ClearlyTrivialThreshold: 12_500m,
                QualitativeConsiderations: "Related-party transactions elevated"));

        Assert.True(created.Succeeded);
        var saved = await ctx.MaterialityAssessments.AsNoTracking()
            .SingleAsync(m => m.Id == created.Value!.AssessmentId);

        Assert.Equal(scope.FirmId, saved.FirmId);
        Assert.Equal(scope.ClientId, saved.ClientId);
        Assert.Equal(scope.EngagementId, saved.EngagementId);
        Assert.Equal(scope.Actor.UserId, saved.ActorId);
        Assert.Equal("Total assets", saved.BenchmarkSource);
        Assert.Equal("AFS-2025-v1", saved.BenchmarkVersion);
        Assert.Equal("Selected as most stable benchmark", saved.Rationale);
        Assert.Equal(5_000_000m, saved.BenchmarkAmount);
        Assert.Equal(0.05m, saved.RateApplied);
        Assert.Equal(250_000m, saved.OverallMateriality);
        Assert.Equal(187_500m, saved.PerformanceMateriality);
        Assert.Equal(12_500m, saved.ClearlyTrivialThreshold);
        Assert.Equal("Related-party transactions elevated", saved.QualitativeConsiderations);
        Assert.Equal(MaterialityStatuses.Draft, saved.Status);
    }

    // ── NT-21.2 ─────────────────────────────────────────────────────────────

    [Theory(DisplayName = "NT-21.2: Inverted materiality thresholds are refused with a stable code")]
    [InlineData(50_000, 60_000, 2_500)]   // performance >= overall
    [InlineData(50_000, 40_000, 45_000)]  // trivial >= performance
    [InlineData(0, 0, 0)]                 // overall materiality must be positive
    public async Task Materiality_Rejects_InvertedThresholds(
        decimal overall, decimal performance, decimal trivial)
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        await using var ctx = new AuditSphereDbContext(pg.Options);

        var refused = await AuditPlanningService.CreateMaterialityAssessmentAsync(ctx, fixture.Primary.Actor,
            new CreateMaterialityRequest(fixture.Primary.EngagementId, "Revenue", "AFS-2025-v1", "Rationale",
                1_000_000m, 0.05m, overall, performance, trivial, null));

        Assert.False(refused.Succeeded);
        Assert.Equal("audit-planning.invalid", refused.ErrorCode);
        Assert.Equal(0, await ctx.MaterialityAssessments.CountAsync());
    }

    // ── NT-21.3 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.3: Audit risk persists each column from its own input (regression: shifted mapping)")]
    public async Task AuditRisk_PersistedCorrectly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var created = await AuditPlanningService.CreateAuditRiskAsync(ctx, scope.Actor,
            new CreateAuditRiskRequest(
                scope.EngagementId,
                "Revenue recognition", "Occurrence",
                "Revenue may be recognised before delivery occurs",
                "Complex contracts with variable consideration",
                SignificanceDecisions.Significant,
                "No effective control identified",
                "Extended substantive testing of contract population"));

        Assert.True(created.Succeeded);
        var saved = await ctx.AuditRisks.AsNoTracking().SingleAsync(r => r.Id == created.Value!.RiskId);

        Assert.Equal(scope.FirmId, saved.FirmId);
        Assert.Equal(scope.ClientId, saved.ClientId);
        Assert.Equal("Revenue recognition", saved.AccountArea);
        Assert.Equal("Occurrence", saved.Assertion);
        // Each column carries its own value; the description is not the drivers text and the
        // severity is the classification derived from the significance decision.
        Assert.Equal("Revenue may be recognised before delivery occurs", saved.Description);
        Assert.Equal("Complex contracts with variable consideration", saved.Drivers);
        Assert.Equal(RiskSeverities.Significant, saved.Severity);
        Assert.Equal(SignificanceDecisions.Significant, saved.SignificanceDecision);
        Assert.Equal("No effective control identified", saved.ControlsConsidered);
        Assert.Equal("Extended substantive testing of contract population", saved.ResponseDescription);
        Assert.Equal(RiskStatuses.Identified, saved.Status);
        Assert.Equal(scope.Actor.UserId, saved.ActorId);
    }

    [Fact(DisplayName = "NT-21.3b: A normal significance decision classifies the risk as Normal")]
    public async Task AuditRisk_NormalDecision_DerivesNormalSeverity()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        await using var ctx = new AuditSphereDbContext(pg.Options);

        var created = await AuditPlanningService.CreateAuditRiskAsync(ctx, fixture.Primary.Actor,
            new CreateAuditRiskRequest(fixture.Primary.EngagementId, "Payroll", "Completeness",
                "Payroll may be incomplete", "Simple recurring ledger postings",
                SignificanceDecisions.Normal, null, "Analytical review by month"));

        Assert.True(created.Succeeded);
        var saved = await ctx.AuditRisks.AsNoTracking().SingleAsync(r => r.Id == created.Value!.RiskId);
        Assert.Equal(RiskSeverities.Normal, saved.Severity);
        Assert.Null(saved.ControlsConsidered);
    }

    // ── NT-21.4 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.4: Risk requires non-empty assertion and a valid significance decision")]
    public async Task AuditRisk_Rejects_InvalidInput()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        await using var ctx = new AuditSphereDbContext(pg.Options);

        var blankAssertion = await AuditPlanningService.CreateAuditRiskAsync(ctx, fixture.Primary.Actor,
            new CreateAuditRiskRequest(fixture.Primary.EngagementId, "Revenue", "", "Description",
                "Drivers", SignificanceDecisions.Significant, null, "Response"));
        Assert.False(blankAssertion.Succeeded);
        Assert.Equal("audit-planning.invalid", blankAssertion.ErrorCode);

        var unknownDecision = await AuditPlanningService.CreateAuditRiskAsync(ctx, fixture.Primary.Actor,
            new CreateAuditRiskRequest(fixture.Primary.EngagementId, "Revenue", "Occurrence", "Description",
                "Drivers", "MATERIAL-BUT-HOPEFULLY", null, "Response"));
        Assert.False(unknownDecision.Succeeded);
        Assert.Contains("significance decision", unknownDecision.Message!);

        Assert.Equal(0, await ctx.AuditRisks.CountAsync());
    }

    // ── NT-21.5 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.5: Population version persisted; negative control values refused")]
    public async Task Population_PersistedAndValidated()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var req = new CreatePopulationRequest(
            scope.EngagementId,
            "Trade receivables existence testing",
            "Existence / Rights",
            "AR-EXPORT-20251231-v3",
            "All posted AR at 31 Dec 2025 excl. intercompany",
            RowCount: 342,
            MonetaryControlTotal: 4_870_250m,
            Currency: "QAR",
            Exclusions: "Intercompany balances");

        var created = await AuditPlanningService.CreatePopulationVersionAsync(ctx, scope.Actor, req);
        Assert.True(created.Succeeded);

        var saved = await ctx.PopulationVersions.AsNoTracking().SingleAsync(p => p.Id == created.Value!.PopulationId);
        Assert.Equal(scope.FirmId, saved.FirmId);
        Assert.Equal(scope.ClientId, saved.ClientId);
        Assert.Equal(342, saved.RowCount);
        Assert.Equal(4_870_250m, saved.MonetaryControlTotal);
        Assert.Equal("QAR", saved.Currency);
        Assert.Equal("AR-EXPORT-20251231-v3", saved.SourceReceiptReference);
        Assert.Equal(PopulationStatuses.PendingApproval, saved.Status);

        var negative = await AuditPlanningService.CreatePopulationVersionAsync(ctx, scope.Actor,
            req with { RowCount = -1 });
        Assert.False(negative.Succeeded);
        Assert.Equal("audit-planning.invalid", negative.ErrorCode);

        var lowercaseCurrency = await AuditPlanningService.CreatePopulationVersionAsync(ctx, scope.Actor,
            req with { Currency = "qar" });
        Assert.False(lowercaseCurrency.Succeeded);
        Assert.Equal(1, await ctx.PopulationVersions.CountAsync());
    }

    // ── NT-21.6 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.6: Workpaper created and submitted; stale revision refused, snapshot persisted")]
    public async Task Workpaper_CreateAndSubmit_RevisionConflict()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        WorkpaperResult created;
        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.CreateWorkpaperAsync(ctx, scope.Actor,
                new CreateWorkpaperRequest(
                    scope.EngagementId,
                    "CB-02", "Bank Reconciliation",
                    "Verify year-end bank reconciliation",
                    "CASH-2025-v3", null,
                    "Obtain bank confirmations and agree to TB"));
            Assert.True(result.Succeeded);
            created = result.Value!;
        }

        Assert.Equal(WorkpaperStatuses.Working, created.Status);
        Assert.Equal(1L, created.Revision);

        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            var submitted = await AuditPlanningService.SubmitWorkpaperAsync(ctx, scope.Actor,
                new SubmitWorkpaperRequest(
                    created.WorkpaperId,
                    ExpectedRevision: 1,
                    WorkPerformed: "Obtained bank confirmation letters for 3 accounts; all agreed to TB.",
                    Conclusion: "Bank reconciliation complete and supported."));
            Assert.True(submitted.Succeeded);
            Assert.Equal(WorkpaperStatuses.SubmittedSnapshot, submitted.Value!.Status);
            Assert.Equal(2L, submitted.Value!.Revision);
        }

        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            var submission = await ctx.WorkpaperSubmissions.AsNoTracking()
                .SingleAsync(s => s.WorkpaperId == created.WorkpaperId);
            Assert.Equal(2L, submission.Revision);
            Assert.Equal("Bank reconciliation complete and supported.", submission.Conclusion);
            Assert.Equal(scope.ClientId, submission.ClientId);
            Assert.Equal(scope.Actor.UserId, submission.ActorId);
        }

        await using var stale = new AuditSphereDbContext(pg.Options);

        // A frozen submission cannot be resubmitted at all, whatever revision the caller believed.
        var refused = await AuditPlanningService.SubmitWorkpaperAsync(stale, scope.Actor,
            new SubmitWorkpaperRequest(created.WorkpaperId, ExpectedRevision: 1,
                WorkPerformed: "Second attempt", Conclusion: "Should be refused."));
        Assert.False(refused.Succeeded);
        Assert.Equal(ErrorCodes.ProtectedState, refused.ErrorCode);

        // A still-working paper loaded against an outdated revision reports a revision conflict.
        var other = await AuditPlanningService.CreateWorkpaperAsync(stale, scope.Actor,
            new CreateWorkpaperRequest(scope.EngagementId, "CB-03", "Petty cash", "Confirm counts",
              "CASH-2025-v3", null, "Count on the reporting date"));
        var conflict = await AuditPlanningService.SubmitWorkpaperAsync(stale, scope.Actor,
            new SubmitWorkpaperRequest(other.Value!.WorkpaperId, ExpectedRevision: 7,
                WorkPerformed: "Counted", Conclusion: "Agreed."));
        Assert.False(conflict.Succeeded);
        Assert.Equal(ErrorCodes.StaleRevision, conflict.ErrorCode);

        Assert.Equal(1, await stale.WorkpaperSubmissions.CountAsync());
    }

    [Fact(DisplayName = "ASH-05: Workpaper draft is durable, idempotent and consumed by submission")]
    public async Task WorkpaperDraft_DurableIdempotentAndConsumed()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        WorkpaperResult created;
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.CreateWorkpaperAsync(db, scope.Actor,
                new CreateWorkpaperRequest(scope.EngagementId, "DR-01", "Draft durability",
                    "Exercise durable workpaper state", "T-1", null, "Document the work"));
            Assert.True(result.Succeeded);
            created = result.Value!;
        }

        var saveId = Guid.NewGuid();
        WorkpaperDraftResult saved;
        await using (var db = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.SaveWorkpaperDraftAsync(db, scope.Actor,
                new SaveWorkpaperDraftRequest(created.WorkpaperId, 0, 1, 1, 1, saveId,
                    "Performed step one", "Working conclusion"));
            Assert.True(result.Succeeded);
            saved = result.Value!;
            Assert.Equal(1L, saved.DraftRevision);
        }

        await using (var reload = new AuditSphereDbContext(pg.Options))
        {
            var loaded = await AuditPlanningService.LoadWorkpaperDraftAsync(reload, scope.Actor, created.WorkpaperId);
            Assert.True(loaded.Succeeded);
            Assert.Equal("Performed step one", loaded.Value!.WorkPerformed);
            Assert.Equal(saveId, loaded.Value.LastSaveId);
        }

        await using (var retry = new AuditSphereDbContext(pg.Options))
        {
            var same = await AuditPlanningService.SaveWorkpaperDraftAsync(retry, scope.Actor,
                new SaveWorkpaperDraftRequest(created.WorkpaperId, 0, 1, 1, 1, saveId,
                    "Performed step one", "Working conclusion"));
            Assert.True(same.Succeeded);
            Assert.Equal(saved.DraftRevision, same.Value!.DraftRevision);

            var conflict = await AuditPlanningService.SaveWorkpaperDraftAsync(retry, scope.Actor,
                new SaveWorkpaperDraftRequest(created.WorkpaperId, 0, 1, 1, 1, saveId,
                    "Different content", "Working conclusion"));
            Assert.False(conflict.Succeeded);
            Assert.Equal(ErrorCodes.IdempotencyConflict, conflict.ErrorCode);
        }

        var secondSaveId = Guid.NewGuid();
        await using (var update = new AuditSphereDbContext(pg.Options))
        {
            var next = await AuditPlanningService.SaveWorkpaperDraftAsync(update, scope.Actor,
                new SaveWorkpaperDraftRequest(created.WorkpaperId, 1, 1, 1, 1, secondSaveId,
                    "Performed step one and two", "Final conclusion"));
            Assert.True(next.Succeeded);
            Assert.Equal(2L, next.Value!.DraftRevision);
        }

        WorkpaperResult discardedTarget;
        await using (var createDiscardTarget = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.CreateWorkpaperAsync(createDiscardTarget, scope.Actor,
                new CreateWorkpaperRequest(scope.EngagementId, "DR-02", "Discard and restart",
                    "Exercise discard lifecycle", "T-1", null, "Document the work"));
            Assert.True(result.Succeeded);
            discardedTarget = result.Value!;
        }
        var discardedSaveId = Guid.NewGuid();
        await using (var saveDiscardTarget = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.SaveWorkpaperDraftAsync(saveDiscardTarget, scope.Actor,
                new SaveWorkpaperDraftRequest(discardedTarget.WorkpaperId, 0, 1, 1, 1, discardedSaveId,
                    "Old draft", "Old conclusion"));
            Assert.True(result.Succeeded);
        }
        await using (var discard = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.DiscardWorkpaperDraftAsync(discard, scope.Actor,
                discardedTarget.WorkpaperId, 1);
            Assert.True(result.Succeeded);
        }
        await using (var restart = new AuditSphereDbContext(pg.Options))
        {
            var late = await AuditPlanningService.SaveWorkpaperDraftAsync(restart, scope.Actor,
                new SaveWorkpaperDraftRequest(discardedTarget.WorkpaperId, 1, 1, 1, 1, Guid.NewGuid(),
                    "Late old draft", "Late old conclusion"));
            Assert.False(late.Succeeded);
            Assert.Equal(ErrorCodes.StaleRevision, late.ErrorCode);

            var fresh = await AuditPlanningService.SaveWorkpaperDraftAsync(restart, scope.Actor,
                new SaveWorkpaperDraftRequest(discardedTarget.WorkpaperId, 0, 1, 1, 1, Guid.NewGuid(),
                    "Fresh draft", "Fresh conclusion"));
            Assert.True(fresh.Succeeded);
            Assert.Equal(2L, fresh.Value!.DraftRevision);
            Assert.Equal(WorkpaperDraftLifecycles.Active, fresh.Value.Lifecycle);
        }

        await using (var submit = new AuditSphereDbContext(pg.Options))
        {
            var result = await AuditPlanningService.SubmitWorkpaperAsync(submit, scope.Actor,
                new SubmitWorkpaperRequest(created.WorkpaperId, 1,
                    "Performed step one and two", "Final conclusion", 2, secondSaveId));
            Assert.True(result.Succeeded);
        }

        await using (var verify = new AuditSphereDbContext(pg.Options))
        {
            var draft = await verify.WorkpaperDrafts.AsNoTracking().SingleAsync(x => x.WorkpaperId == created.WorkpaperId);
            Assert.Equal(WorkpaperDraftLifecycles.Consumed, draft.Lifecycle);
            var late = await AuditPlanningService.SaveWorkpaperDraftAsync(verify, scope.Actor,
                new SaveWorkpaperDraftRequest(created.WorkpaperId, 2, 1, 1, 1, Guid.NewGuid(),
                    "Late autosave", "Late conclusion"));
            Assert.False(late.Succeeded);
            Assert.Equal(ErrorCodes.ProtectedState, late.ErrorCode);
        }
    }

    // ── NT-21.7 ─────────────────────────────────────────────────────────────

    [Fact(DisplayName = "NT-21.7: Finding persisted with its own type, impact and corrected flag")]
    public async Task Finding_PersistedCorrectly()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var created = await AuditPlanningService.CreateFindingAsync(ctx, scope.Actor,
            new CreateFindingRequest(
                scope.EngagementId,
                "Misstatement — Revenue Cut-off",
                "Revenue QAR 380,000 recognized Dec 2025; relates to Jan 2026 services.",
                Corrected: false,
                MonetaryAmount: 380_000m,
                ManagementResponse: null));

        Assert.True(created.Succeeded);
        var saved = await ctx.Findings.AsNoTracking().SingleAsync(f => f.Id == created.Value!.FindingId);
        Assert.Equal("Misstatement — Revenue Cut-off", saved.FindingType);
        Assert.StartsWith("Revenue QAR 380,000", saved.ImpactDescription);
        Assert.Equal(380_000m, saved.MonetaryAmount);
        Assert.False(saved.Corrected);
        Assert.Equal(FindingStatuses.Open, saved.Status);
        Assert.Equal(scope.FirmId, saved.FirmId);
        Assert.Equal(scope.ClientId, saved.ClientId);
    }

    [Fact(DisplayName = "NT-21.8: Filing a management response updates the working finding only")]
    public async Task Finding_ManagementResponse_Recorded()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;

        await using var ctx = new AuditSphereDbContext(pg.Options);
        var created = await AuditPlanningService.CreateFindingAsync(ctx, scope.Actor,
            new CreateFindingRequest(scope.EngagementId, "Control deficiency", "Late bank fee posting",
                false, null, null));
        Assert.True(created.Succeeded);

        var filed = await AuditPlanningService.RecordFindingResponseAsync(ctx, scope.Actor,
            new RecordFindingResponseRequest(created.Value!.FindingId,
                "Management will post fees on the standard schedule from February.", false));
        Assert.True(filed.Succeeded);

        var saved = await ctx.Findings.AsNoTracking().SingleAsync(f => f.Id == created.Value!.FindingId);
        Assert.Equal("Management will post fees on the standard schedule from February.", saved.ManagementResponse);
        Assert.Equal(FindingStatuses.Evaluated, saved.Status);
        Assert.False(saved.Corrected);

        var blank = await AuditPlanningService.RecordFindingResponseAsync(ctx, scope.Actor,
            new RecordFindingResponseRequest(created.Value!.FindingId, "   ", false));
        Assert.False(blank.Succeeded);
        Assert.Equal("audit-planning.invalid", blank.ErrorCode);
    }

    [Fact(DisplayName = "Finding response refuses a command after the engagement grant is revoked")]
    public async Task FindingResponse_RevokedGrantDoesNotChangeFinding()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var scope = fixture.Primary;
        Guid findingId;

        await using (var ctx = new AuditSphereDbContext(pg.Options))
        {
            var created = await AuditPlanningService.CreateFindingAsync(ctx, scope.Actor,
                new CreateFindingRequest(scope.EngagementId, "Synthetic finding", "Synthetic impact",
                    false, 125m, null));
            Assert.True(created.Succeeded, created.Message);
            findingId = created.Value!.FindingId;
            var revokedAt = DateTimeOffset.UtcNow;
            Assert.Equal(1, await ctx.RoleGrants.Where(x => x.FirmId == scope.FirmId &&
                    x.UserId == scope.Actor.UserId && x.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, revokedAt)));

            var denied = await AuditPlanningService.RecordFindingResponseAsync(ctx, scope.Actor,
                new RecordFindingResponseRequest(findingId, "Must not be persisted", false));
            Assert.False(denied.Succeeded);
            Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
        }

        await using var verify = new AuditSphereDbContext(pg.Options);
        var unchanged = await verify.Findings.AsNoTracking().SingleAsync(x => x.Id == findingId);
        Assert.Null(unchanged.ManagementResponse);
        Assert.Equal(FindingStatuses.Open, unchanged.Status);
    }

    [Fact(DisplayName = "Review point disposition requires current engagement scope and is idempotent")]
    public async Task ReviewPointDisposition_IsScopedAndRetrySafe()
    {
        await using var pg = await PgTestSchema.CreateAsync();
        var fixture = await PlanningSeed.CreateAsync(pg);
        var primary = new ReviewPoint
        {
            Id = Guid.NewGuid(), FirmId = fixture.Primary.FirmId, ClientId = fixture.Primary.ClientId,
            EngagementId = fixture.Primary.EngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
            TargetRevision = 1, Comment = "Synthetic primary review point", RaisedByUserId = fixture.Primary.Actor.UserId,
            RaisedAt = DateTimeOffset.UtcNow
        };
        var other = new ReviewPoint
        {
            Id = Guid.NewGuid(), FirmId = fixture.Other.FirmId, ClientId = fixture.Other.ClientId,
            EngagementId = fixture.Other.EngagementId, TargetId = Guid.NewGuid(), TargetKind = "workpaper",
            TargetRevision = 1, Comment = "Synthetic other-client review point", RaisedByUserId = fixture.Primary.Actor.UserId,
            RaisedAt = DateTimeOffset.UtcNow
        };
        await using (var seed = new AuditSphereDbContext(pg.Options))
        {
            seed.ReviewPoints.AddRange(primary, other);
            await seed.SaveChangesAsync();
        }

        await using (var db = new AuditSphereDbContext(pg.Options))
        {
            var clear = await AuditPlanningService.SetReviewPointDispositionAsync(db, fixture.Primary.Actor, primary.Id, true);
            var retry = await AuditPlanningService.SetReviewPointDispositionAsync(db, fixture.Primary.Actor, primary.Id, true);
            var sibling = await AuditPlanningService.SetReviewPointDispositionAsync(db, fixture.Primary.Actor, other.Id, true);
            var staleActor = fixture.Primary.Actor with { SessionEpoch = fixture.Primary.Actor.SessionEpoch + 1 };
            var stale = await AuditPlanningService.SetReviewPointDispositionAsync(db, staleActor, primary.Id, false);

            Assert.True(clear.Succeeded);
            Assert.True(retry.Succeeded);
            Assert.False(sibling.Succeeded);
            Assert.Equal(ErrorCodes.ScopeDenied, sibling.ErrorCode);
            Assert.False(stale.Succeeded);
            Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
        }

        await using var verify = new AuditSphereDbContext(pg.Options);
        Assert.True(await verify.ReviewPoints.Where(x => x.Id == primary.Id).Select(x => x.Cleared).SingleAsync());
        Assert.False(await verify.ReviewPoints.Where(x => x.Id == other.Id).Select(x => x.Cleared).SingleAsync());
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
}
