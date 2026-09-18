using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcTests
{
  [Fact]
  public async Task PbcStateAndUploadReceipt_AreScopedSequentialAndReviewGated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var staff = Actor(fixture.Staff, "Staff");
    var reviewer = Actor(fixture.Reviewer, "Reviewer");
    var client = Actor(fixture.Client, "ClientUser");

    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        fixture.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF or CSV", "12 months", fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
        "2027-01-31", "Confidential", "Complete period with readable account identity."));
      Assert.True(created.Succeeded);
      requestId = created.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await PbcService.ChangeStateAsync(db, staff,
        new PbcStateChangeRequest(requestId, PbcStates.Sent, 1))).Succeeded);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(requestId, PbcStates.Acknowledged, 2))).Succeeded);

    var declaredHash = new string('a', 64);
    Guid uploadId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await PbcService.StartUploadAsync(db, client, new StartPbcUploadRequest(
        requestId, "bank-statements.csv", "text/csv", 10, declaredHash));
      Assert.True(started.Succeeded);
      uploadId = started.Value;
    }

    var firstHash = new string('b', 64);
    var secondHash = new string('c', 64);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await PbcService.RecordChunkAsync(db, client,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, 6, firstHash))).Succeeded);
      var replay = await PbcService.RecordChunkAsync(db, client,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, 6, firstHash));
      Assert.True(replay.Succeeded);
      Assert.Equal(6, replay.Value!.ReceivedByteCount);
      Assert.False((await PbcService.RecordChunkAsync(db, client,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, 5, firstHash))).Succeeded);
      Assert.True((await PbcService.RecordChunkAsync(db, client,
        new RecordPbcUploadChunkRequest(uploadId, 1, 6, 4, secondHash))).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var mismatch = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(uploadId, new string('d', 64)));
      Assert.False(mismatch.Succeeded);
      Assert.Equal("pbc.hash-mismatch", mismatch.ErrorCode);
      var completed = await PbcService.CompleteUploadAsync(db, staff,
        new CompletePbcUploadRequest(uploadId, declaredHash));
      Assert.True(completed.Succeeded);
      Assert.Equal(PbcUploadStates.Received, completed.Value!.State);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var underReview = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(requestId, PbcStates.UnderReview, 5));
      Assert.True(underReview.Succeeded);
      var accepted = await PbcService.ChangeStateAsync(db, reviewer,
        new PbcStateChangeRequest(requestId, PbcStates.Accepted, 6));
      Assert.True(accepted.Succeeded);
      Assert.False((await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(requestId, PbcStates.Accepted, 7))).Succeeded);
      Assert.False((await PbcService.ChangeStateAsync(db, staff,
        new PbcStateChangeRequest(requestId, PbcStates.Closed, 1))).Succeeded);

      var chunkId = await db.PbcUploadChunks.Select(x => x.Id).FirstAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE pbc_upload_chunks SET byte_count = 7 WHERE id = {chunkId}"));
    }
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var staff = User(firmId, "Staff");
    var reviewer = User(firmId, "Staff");
    var client = User(firmId, "Client");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "PBC TEST CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(staff, reviewer, client);
    db.RoleGrants.AddRange(
      Grant(firmId, staff, "Staff", clientId: clientId, engagementId: engagementId),
      Grant(firmId, reviewer, "Reviewer", clientId: clientId, engagementId: engagementId),
      Grant(firmId, client, "ClientUser", clientId: clientId, engagementId: engagementId));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, staff, reviewer, client);
  }

  private static AppUser User(Guid firmId, string kind) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserKind = kind,
    Subject = "sub-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-test",
    Email = $"{Guid.NewGuid():N}@example.test", DisplayName = kind,
    CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role,
    Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = clientId, EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId,
    AppUser Staff, AppUser Reviewer, AppUser Client);
}
