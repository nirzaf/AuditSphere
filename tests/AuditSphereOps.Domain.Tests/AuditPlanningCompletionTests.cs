using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T060 completion: ISA 510 opening-balance verification and the planning summary.</summary>
[Trait("Profile", "Database")]
public sealed class AuditPlanningCompletionTests
{
  [Fact(DisplayName = "Opening-balance verification derives its conclusion and blocks unexplained differences")]
  public async Task OpeningBalanceVerification_DerivesConclusionAndBlocksUnresolved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    Guid reviewerId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reviewerId = Guid.NewGuid();
      db.Users.Add(new AppUser
      {
        Id = reviewerId, FirmId = scope.FirmId, Subject = "plan-reviewer-" + reviewerId.ToString("N"),
        TenantId = "tenant-planning", Email = "plan-reviewer@example.test", DisplayName = "Planning Reviewer",
        UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = reviewerId, Role = "Reviewer",
        ClientId = scope.ClientId, EngagementId = scope.EngagementId,
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId
      });
      await db.SaveChangesAsync();
    }
    var reviewer = new ActorContext(reviewerId, scope.FirmId, 1, ["Reviewer", "Partner"]);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Missing evidence is refused outright.
      var noEvidence = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Actor,
        new OpeningBalanceVerificationRequest(scope.EngagementId, null, null, "FY2025 audited FS",
          new DateOnly(2026, 1, 1), "QAR", 1000m, 1000m, true, "Agreed to prior-year audited statements.", []));
      Assert.False(noEvidence.Succeeded);

      // Agreed figures with consistent policies conclude AGREED.
      var agreed = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Actor,
        new OpeningBalanceVerificationRequest(scope.EngagementId, null, null, "FY2025 audited FS",
          new DateOnly(2026, 1, 1), "QAR", 1000m, 1000m, true,
          "Opening balances agree to the prior-year audited financial statements.",
          ["prior-year-audited-fs", "ledger-opening-tb"]));
      Assert.True(agreed.Succeeded, agreed.Message);
      Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, agreed.Value!.Conclusion);
      Assert.Equal(0m, agreed.Value.DifferenceAmount);
      Assert.True((await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, reviewer,
        agreed.Value.VerificationId, "Agreed to prior-year file.")).Succeeded);

      // A difference cannot conclude as agreed and cannot be reviewed away.
      var unresolved = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Actor,
        new OpeningBalanceVerificationRequest(scope.EngagementId, null, null, "FY2025 audited FS",
          new DateOnly(2026, 1, 1), "QAR", 1000m, 975m, true,
          "Opening ledger differs from the prior-year audited figures pending client explanation.",
          ["prior-year-audited-fs", "difference-schedule"]));
      Assert.True(unresolved.Succeeded);
      Assert.Equal(OpeningBalanceVerificationConclusions.DifferencesUnresolved, unresolved.Value!.Conclusion);
      Assert.Equal(25m, unresolved.Value.DifferenceAmount);
      var blockedReview = await AuditPlanningCompletionService.ReviewOpeningBalanceVerificationAsync(db, reviewer,
        unresolved.Value.VerificationId, "Reviewed.");
      Assert.False(blockedReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blockedReview.ErrorCode);

      // Re-recording the same as-of date revises the row and clears the prior review.
      var resolved = await AuditPlanningCompletionService.RecordOpeningBalanceVerificationAsync(db, scope.Actor,
        new OpeningBalanceVerificationRequest(scope.EngagementId, null, null, "FY2025 audited FS",
          new DateOnly(2026, 1, 1), "QAR", 1000m, 1000m, true,
          "Difference cleared after agreeing the prior-year audit adjustment.",
          ["prior-year-audited-fs", "difference-schedule"]));
      Assert.True(resolved.Succeeded);
      Assert.Equal(3, resolved.Value!.Revision); // created, then revised twice
      Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, resolved.Value.Conclusion);
      Assert.Equal(1, await db.OpeningBalanceVerifications.CountAsync()); // one row per engagement/as-of date
    }

    // The planning summary reports the planning facts.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var materiality = await AuditPlanningService.CreateMaterialityAssessmentAsync(db, scope.Actor, new CreateMaterialityRequest(
        scope.EngagementId, "Revenue", "2026-Q1", "Revenue benchmark", 1000m, 0.01m, 10m, 5m, 0.5m, null));
      Assert.True(materiality.Succeeded, materiality.Message);

      var summary = await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(db, scope.Actor, scope.EngagementId);
      Assert.True(summary.Succeeded, summary.Message);
      var view = summary.Value!;
      Assert.NotNull(view.LatestMaterialityId);
      Assert.Equal(10m, view.OverallMateriality);
      Assert.Equal("Revenue", view.MaterialityBenchmarkSource);
      Assert.NotNull(view.OpeningBalanceVerificationId);
      Assert.Equal(OpeningBalanceVerificationConclusions.Agreed, view.OpeningBalanceConclusion);
      Assert.True(view.OpeningBalancesResolved);
      Assert.Equal(0, view.RiskCount);

      // The actor's grant covers only the primary engagement: the sibling engagement is
      // denied rather than summarised, and an unknown engagement id is denied too.
      var sibling = await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(db, scope.Actor, fixture.Other.EngagementId);
      Assert.False(sibling.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, sibling.ErrorCode);
      var unknown = await AuditPlanningCompletionService.GetAuditPlanningSummaryAsync(db, scope.Actor, Guid.NewGuid());
      Assert.False(unknown.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, unknown.ErrorCode);
    }
  }
}
