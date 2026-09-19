using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ApprovalTests
{
  [Fact]
  public async Task ApprovalBindsTargetAndGenerations_ThenBecomesStaleWithoutRewritingHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var digest = new string('a', 64);
    Guid approvalId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await ApprovalService.CreateAsync(db, fixture.Actor,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(created.Succeeded);
      approvalId = created.Value;
      Assert.True((await ApprovalService.RequireCurrentAsync(db, fixture.Actor, approvalId)).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.ClientSafetyStates.Where(x => x.Id == fixture.ClientId && x.FirmId == fixture.FirmId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var evaluated = await ApprovalService.EvaluateAsync(db, fixture.Actor, approvalId);
      Assert.True(evaluated.Succeeded);
      Assert.Equal(ApprovalStates.Stale, evaluated.Value!.Status);
      Assert.Equal(ErrorCodes.GenerationStale, evaluated.Value.FailureCode);
      var blocked = await ApprovalService.RequireCurrentAsync(db, fixture.Actor, approvalId);
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, blocked.ErrorCode);

      var approval = await db.Approvals.SingleAsync(x => x.Id == approvalId);
      Assert.Equal(1, approval.InputGeneration);
      Assert.Equal(digest, approval.ManifestDigest);
      var projection = await db.ApprovalApplicabilities.SingleAsync(x => x.ApprovalId == approvalId);
      Assert.Equal(ApprovalStates.Stale, projection.Status);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE approvals SET manifest_digest = {new string('b', 64)}
        WHERE firm_id = {fixture.FirmId} AND id = {approvalId}
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM approvals WHERE firm_id = {fixture.FirmId} AND id = {approvalId}
        """));
    }
  }

  [Fact]
  public async Task ApprovalCreationRejectsStaleRevisionOrGeneration()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var staleRevision = await ApprovalService.CreateAsync(db, fixture.Actor,
      new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 2, 1, 1, new string('c', 64)));
    Assert.False(staleRevision.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, staleRevision.ErrorCode);

    await db.ClientSafetyStates.Where(x => x.Id == fixture.ClientId && x.FirmId == fixture.FirmId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    var staleGeneration = await ApprovalService.CreateAsync(db, fixture.Actor,
      new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, new string('d', 64)));
    Assert.False(staleGeneration.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, staleGeneration.ErrorCode);
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var scope = await pg.SeedScopeAsync();
    var user = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId,
      Subject = "approval-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-" + Guid.NewGuid().ToString("N"),
      Email = "reviewer@example.test", DisplayName = "Reviewer",
      CreatedAt = DateTimeOffset.UtcNow
    };
    var workpaperId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = "Reviewer",
      ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
    });
    db.Workpapers.Add(new Workpaper
    {
      Id = workpaperId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ActorId = user.Id, Index = "C-01",
      Title = "Cash workpaper", Objective = "Confirm the year-end cash balance",
      TemplateVersion = "CASH-2026-v1", Procedure = "Agree bank confirmations to the ledger",
      Status = "WORKING", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    return new Fixture(scope.FirmId, scope.ClientId, workpaperId,
      new ActorContext(user.Id, scope.FirmId, user.SessionEpoch, ["Reviewer"]));
  }

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid WorkpaperId, ActorContext Actor);
}
