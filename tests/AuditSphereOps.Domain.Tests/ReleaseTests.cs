using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Reviews;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ReleaseTests
{
  [Fact]
  public async Task ReleaseGate_RejectsMissingCheckpointAndWrongManifest_ThenIsIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var digest = new string('a', 64);
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded);

      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-001", false));
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      Assert.Empty(await db.Releases.ToListAsync());

      var wrongManifest = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, new string('b', 64), "release-002", true));
      Assert.False(wrongManifest.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, wrongManifest.ErrorCode);
      Assert.Empty(await db.Releases.ToListAsync());

      var issued = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-001", true));
      Assert.True(issued.Succeeded);
      var repeated = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-001", true));
      Assert.True(repeated.Succeeded);
      Assert.Equal(issued.Value, repeated.Value);
      Assert.Single(await db.Releases.ToListAsync());
      Assert.Single(await db.DurableOperations.Where(x => x.OperationKind == "ReleaseDelivery.v1").ToListAsync());
      Assert.Equal(ReleaseStates.Issued, (await db.ReleaseCandidates.SingleAsync(x => x.Id == candidateId)).Status);

      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        UPDATE releases SET manifest_digest = {new string('c', 64)} WHERE firm_id = {fixture.FirmId} AND id = {issued.Value}
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        DELETE FROM releases WHERE firm_id = {fixture.FirmId} AND id = {issued.Value}
        """));
    }
  }

  [Fact]
  public async Task ReleaseGate_BlocksChangedInputsAndCandidateRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var digest = new string('d', 64);
    Guid candidateId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approval = await ApprovalService.CreateAsync(db, fixture.Reviewer,
        new CreateApprovalRequest("WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(approval.Succeeded);
      var candidate = await ReleaseService.CreateCandidateAsync(db, fixture.Partner,
        new CreateReleaseCandidateRequest(approval.Value, "WORKPAPER", fixture.WorkpaperId, 1, 1, 1, digest));
      Assert.True(candidate.Succeeded);
      candidateId = candidate.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.ClientSafetyStates.Where(x => x.FirmId == fixture.FirmId && x.Id == fixture.ClientId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
      var staleGeneration = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 1, digest, "release-generation", true));
      Assert.False(staleGeneration.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleGeneration.ErrorCode);
      Assert.Empty(await db.Releases.ToListAsync());
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staleCandidate = await ReleaseService.IssueAsync(db, fixture.Partner,
        new IssueReleaseRequest(candidateId, 2, digest, "release-revision", true));
      Assert.False(staleCandidate.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleCandidate.ErrorCode);
    }
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var scope = await pg.SeedScopeAsync();
    var reviewer = NewUser(scope.FirmId, "reviewer");
    var partner = NewUser(scope.FirmId, "partner");
    var workpaperId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.AddRange(reviewer, partner);
    db.RoleGrants.AddRange(
      NewGrant(scope, reviewer, "Reviewer"), NewGrant(scope, partner, "Partner"));
    db.Workpapers.Add(new Workpaper
    {
      Id = workpaperId, FirmId = scope.FirmId, ClientId = scope.ClientId,
      EngagementId = scope.EngagementId, ProcedureId = Guid.NewGuid(),
      Title = "Release workpaper", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.Engagements.Where(x => x.FirmId == scope.FirmId && x.Id == scope.EngagementId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProfessionalWorkBlocked, false));
    await db.SaveChangesAsync();
    return new Fixture(scope.FirmId, scope.ClientId, workpaperId,
      Actor(reviewer, "Reviewer"), Actor(partner, "Partner"));
  }

  private static AppUser NewUser(Guid firmId, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = role + "-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-" + Guid.NewGuid().ToString("N"), Email = role + "@example.test",
    DisplayName = role, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant NewGrant((Guid FirmId, Guid ClientId, Guid EngagementId) scope,
    AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = role,
    ClientId = scope.ClientId, EngagementId = scope.EngagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid WorkpaperId,
    ActorContext Reviewer, ActorContext Partner);
}
