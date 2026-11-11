using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class EngagementArchiveHandoffTests
{
  [Fact]
  public async Task IneligibleEngagement_ReadBlocksWithoutChangingStatusOrCreatingApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg);
    var scope = fixture.Primary;
    await using var db = new AuditSphereDbContext(pg.Options);
    var before = await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == scope.EngagementId);
    Assert.NotEqual("Completed", before.Status);
    Assert.NotEqual("Closed", before.Status);
    var result = await EngagementArchiveHandoffQuery.GetAsync(db, fixture.Administrator, scope.EngagementId);
    Assert.True(result.Succeeded, result.Message);
    Assert.False(result.Value!.CanCreate);
    Assert.Equal("The engagement must be issued and completed before an archive can be prepared.", result.Value.BlockedReason);
    Assert.Empty(result.Value.Archives);
    Assert.Empty(result.Value.Profiles);
    Assert.Empty(await db.Archives.ToListAsync());
    Assert.Empty(await db.RecordsProfiles.ToListAsync());
    Assert.Equal(before.Status, (await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == scope.EngagementId)).Status);
    var allowed = await EngagementArchiveHandoffQuery.GetAsync(db, scope.Preparer, scope.EngagementId);
    Assert.True(allowed.Succeeded, allowed.Message);
    var denied = await EngagementArchiveHandoffQuery.GetAsync(db, scope.Preparer, fixture.Sibling.EngagementId);
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    Assert.Null(denied.Value);
  }
}
