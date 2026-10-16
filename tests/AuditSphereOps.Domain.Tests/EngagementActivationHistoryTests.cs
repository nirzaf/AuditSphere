using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementActivationHistoryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task FreshStateRetainsOriginalActivationWhenCurrentAcceptanceChanges(bool reviewed)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "Partner");
    Guid originalDecision;
    RetainedActivationView original;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      originalDecision = await EngagementActivationReviewSeed.PopulateAsync(db, f);
      Assert.Null((await EngagementActivationWorkspace.StateAsync(db, actor, f.EngagementId)).Value!.Activation);
      if (reviewed)
      {
        var state = (await EngagementActivationWorkspace.StateAsync(db, actor, f.EngagementId)).Value!;
        var request = new EngagementActivationRequest(Guid.NewGuid(), state.ReviewBasis);
        var preview = (await EngagementActivationWorkspace.PreviewAsync(db, actor, f.EngagementId, request)).Value!;
        var result = await EngagementActivationWorkspace.ExecuteAsync(db, actor, f.EngagementId,
          request with { Reviewed = true, ExpectedRequestHash = preview.RequestHash });
        Assert.True(result.Succeeded, result.Message);
      }
      else
      {
        var result = await EngagementLifecycleService.ActivateAsync(db, actor, f.EngagementId);
        Assert.True(result.Succeeded, result.Message);
      }
      var stored = await db.EngagementActivations.AsNoTracking().SingleAsync();
      Assert.Equal(reviewed, stored.RequestId.HasValue);
      original = new(stored.Id, originalDecision, AcceptancePaths.NewClient, "9007199254740993",
        stored.ActivatedAt.ToUniversalTime());
    }
    var changedDecision = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.ClientSafetyStates.Where(x => x.Id == f.ClientId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, 9007199254740994L));
      db.AcceptanceDecisions.Add(new()
      {
        Id = changedDecision, FirmId = f.FirmId, PracticeClientId = f.ClientId, EngagementId = f.EngagementId,
        ServiceRoute = "FinancialStatementAudit", Decision = "Declined", Path = AcceptancePaths.Continuance,
        Generation = 9007199254740994L, Rationale = "Synthetic subsequent evaluation",
        EvaluationTemplateVersion = "synthetic.v1", EvaluationSnapshotDigest = new string('b', 64),
        DecidedByUserId = f.Staff.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    await using var fresh = new AuditSphereDbContext(pg.Options);
    var read = await EngagementActivationWorkspace.StateAsync(fresh, actor, f.EngagementId);
    Assert.True(read.Succeeded, read.Message);
    var current = read.Value!;
    Assert.Equal(changedDecision, current.Decision!.Id);
    Assert.Equal("Declined", current.Decision.Decision);
    Assert.Equal(AcceptancePaths.Continuance, current.Decision.Path);
    Assert.Equal("9007199254740994", current.ClientGeneration);
    Assert.Equal(original, current.Activation);
    Assert.Equal(TimeSpan.Zero, current.Activation!.ActivatedAt.Offset);
    Assert.False(current.Eligible);
    Assert.Single(await fresh.EngagementActivations.ToListAsync());
    await fresh.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "Partner")
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    var denied = await EngagementActivationWorkspace.StateAsync(fresh, actor, f.EngagementId);
    Assert.False(denied.Succeeded);
    Assert.Null(denied.Value);
  }
}
