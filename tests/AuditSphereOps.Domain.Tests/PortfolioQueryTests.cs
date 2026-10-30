using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

public sealed class PortfolioQueryTests
{
  [Fact]
  public async Task EngagementScopeExcludesSiblingCountsAndOtherClients_AndRevocationDenies()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Engagements.Add(new Engagement { Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      PracticeClientId = fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(fixture.Staff, "Staff");
    var result = await PortfolioQuery.ListAsync(db, actor);
    Assert.True(result.Succeeded, result.Message);
    var row = Assert.Single(result.Value!.Items);
    Assert.Equal(fixture.ClientId, row.Id);
    Assert.Equal(1, row.Engagements);
    Assert.NotEqual(foreign.ClientId, row.Id);
    Assert.Equal(1, result.Value.Total);
    Assert.Single((await PortfolioQuery.ListAsync(db, actor, "pbc test")).Value!.Items);
    Assert.Single((await PortfolioQuery.ListAsync(db, actor, fixture.ClientId.ToString())).Value!.Items);
    Assert.Empty((await PortfolioQuery.ListAsync(db, actor, foreign.ClientId.ToString())).Value!.Items);
    Assert.False((await PortfolioQuery.ListAsync(db, actor, pageSize: 101)).Succeeded);
    Assert.False((await PortfolioQuery.ListAsync(db, PbcSeed.Actor(fixture.Client, "ClientUser"))).Succeeded);
    var grant = await db.RoleGrants.SingleAsync(g => g.UserId == fixture.Staff.Id);
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    Assert.False((await PortfolioQuery.ListAsync(db, actor)).Succeeded);
  }

  [Fact]
  public async Task ContextChoicesUseExactDestinationAuthorityWithoutWideningReviewerOrSiblingScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sibling = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = sibling, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
      ServiceRoute = "HIDDEN-SIBLING", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    var clientWide = PbcSeed.User(fixture.FirmId, "Staff");
    db.Users.Add(clientWide);
    db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, clientWide, "Staff", fixture.ClientId));
    await db.SaveChangesAsync();

    var staff = PbcSeed.Actor(fixture.Staff, "Staff");
    var scoped = await PortfolioQuery.ContextChoicesAsync(db, staff, fixture.ClientId);
    Assert.True(scoped.Succeeded, scoped.Message);
    Assert.Equal(fixture.ClientId, scoped.Value!.ClientId);
    Assert.False(scoped.Value.CanOpenClient);
    Assert.Equal(fixture.EngagementId, Assert.Single(scoped.Value.Items).EngagementId);
    Assert.False(scoped.Value.HasMore);
    Assert.Null(scoped.Value.NextAfterId);
    Assert.True((await WorkspaceQuery.EngagementAsync(db, staff, fixture.EngagementId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, staff, fixture.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, staff, sibling)).Succeeded);
    Assert.False((await PortfolioQuery.ContextChoicesAsync(db, staff, foreign.ClientId)).Succeeded);

    var reviewer = PbcSeed.Actor(fixture.Reviewer, "Reviewer");
    var reviewChoices = await PortfolioQuery.ContextChoicesAsync(db, reviewer, fixture.ClientId);
    Assert.True(reviewChoices.Succeeded, reviewChoices.Message);
    Assert.False(reviewChoices.Value!.CanOpenClient);
    Assert.Empty(reviewChoices.Value.Items);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, reviewer, fixture.EngagementId)).Succeeded);
    db.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, fixture.Reviewer, "sTaFf", fixture.ClientId, fixture.EngagementId));
    await db.SaveChangesAsync();
    var mixedCase = await PortfolioQuery.ContextChoicesAsync(db, reviewer, fixture.ClientId);
    Assert.True(mixedCase.Succeeded, mixedCase.Message);
    Assert.Equal(fixture.EngagementId, Assert.Single(mixedCase.Value!.Items).EngagementId);
    Assert.True((await WorkspaceQuery.EngagementAsync(db, reviewer, fixture.EngagementId)).Succeeded);

    var broad = await PortfolioQuery.ContextChoicesAsync(db, PbcSeed.Actor(clientWide, "Staff"), fixture.ClientId);
    Assert.True(broad.Succeeded, broad.Message);
    Assert.True(broad.Value!.CanOpenClient);
    Assert.Equal(2, broad.Value.Items.Count);
    var firmWide = await PortfolioQuery.ContextChoicesAsync(db, PbcSeed.Actor(fixture.Admin, "Administrator"), fixture.ClientId);
    Assert.True(firmWide.Succeeded, firmWide.Message);
    Assert.True(firmWide.Value!.CanOpenClient);
    Assert.Equal(2, firmWide.Value.Items.Count);
  }

  [Fact]
  public async Task ContextChoicesPageWithinHundredAndRejectForeignOrMalformedContinuations()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var added = Enumerable.Range(0, 101).Select(_ => new Engagement { Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      PracticeClientId = fixture.ClientId, ServiceRoute = "AUDIT", Status = "Active", CreatedAt = DateTimeOffset.UtcNow }).ToArray();
    db.Engagements.AddRange(added);
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(fixture.Admin, "Administrator");

    var counter = new CountReads();
    await using var boundedDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(counter).Options);
    var first = await PortfolioQuery.ContextChoicesAsync(boundedDb, actor, fixture.ClientId, 100);
    Assert.True(first.Succeeded, first.Message);
    // One broad owner grant must not cause a database round trip for each of 101 candidates.
    Assert.InRange(counter.Reads, 1, 100);
    Assert.Equal(100, first.Value!.Items.Count);
    Assert.True(first.Value.HasMore);
    Assert.NotNull(first.Value.NextAfterId);
    Assert.InRange(first.Value.ValidUntilUtc, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddSeconds(30));
    var cursor = new PortfolioContextChoiceCursor(actor.FirmId, actor.UserId, actor.SessionEpoch, fixture.ClientId, 100,
      first.Value.NextAfterId!.Value, first.Value.ScopeStamp);
    var second = await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 100, cursor);
    Assert.True(second.Succeeded, second.Message);
    Assert.Equal(2, second.Value!.Items.Count);
    Assert.False(second.Value.HasMore);
    Assert.Null(second.Value.NextAfterId);
    var ids = first.Value.Items.Concat(second.Value.Items).Select(x => x.EngagementId).ToArray();
    Assert.Equal(102, ids.Distinct().Count());
    Assert.Contains(fixture.EngagementId, ids);
    Assert.All(added, engagement => Assert.Contains(engagement.Id, ids));
    foreach (var invalid in new[] { cursor with { FirmId = Guid.NewGuid() }, cursor with { UserId = Guid.NewGuid() },
      cursor with { Epoch = actor.SessionEpoch + 1 }, cursor with { ClientId = Guid.NewGuid() },
      cursor with { PageSize = 99 }, cursor with { AfterId = Guid.Empty }, cursor with { ScopeStamp = "invalid" } })
    {
      var rejected = await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 100, invalid);
      Assert.False(rejected.Succeeded);
      Assert.Equal("request.invalid", rejected.ErrorCode);
      Assert.Null(rejected.Value);
    }
    Assert.False((await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 101)).Succeeded);
    Assert.False((await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 0)).Succeeded);
  }

  [Fact]
  public async Task ContextChoicesRejectAClientLinkChangedDuringTheSnapshotEvenWithFirmWideAccess()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var otherClientId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.PracticeClients.Add(new PracticeClient { Id = otherClientId, FirmId = fixture.FirmId,
        LegalName = "Other client", CreatedAt = DateTimeOffset.UtcNow });
      await setup.SaveChangesAsync();
    }
    var change = new ChangeDuringContextRead(async () =>
    {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.Engagements.Where(e => e.Id == fixture.EngagementId)
        .ExecuteUpdateAsync(update => update.SetProperty(e => e.PracticeClientId, otherClientId));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(change).Options);
    var result = await PortfolioQuery.ContextChoicesAsync(db, PbcSeed.Actor(fixture.Admin, "Administrator"), fixture.ClientId);
    Assert.True(change.Fired);
    Assert.False(result.Succeeded);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task ContextChoicesRejectAClientLinkChangedAfterTheFirstBatchWithClientWideAccess()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    var otherClientId = Guid.NewGuid();
    var clientWide = PbcSeed.User(fixture.FirmId, "Staff");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.PracticeClients.Add(new PracticeClient { Id = otherClientId, FirmId = fixture.FirmId,
        LegalName = "Other client", CreatedAt = DateTimeOffset.UtcNow });
      setup.Users.Add(clientWide);
      setup.RoleGrants.Add(PbcSeed.Grant(fixture.FirmId, clientWide, "Staff", fixture.ClientId));
      await setup.SaveChangesAsync();
    }
    var change = new ChangeAfterFirstCandidateBatch(async () =>
    {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.Engagements.Where(e => e.Id == fixture.EngagementId)
        .ExecuteUpdateAsync(update => update.SetProperty(e => e.PracticeClientId, otherClientId));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(change).Options);
    var result = await PortfolioQuery.ContextChoicesAsync(db, PbcSeed.Actor(clientWide, "Staff"), fixture.ClientId);
    Assert.True(change.Fired);
    Assert.False(result.Succeeded);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task ContextChoicesRefuseRevokedAndExpiredScopeAndBindContinuationToCurrentGrants()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var sibling = Guid.NewGuid();
    db.Engagements.Add(new Engagement { Id = sibling, FirmId = fixture.FirmId, PracticeClientId = fixture.ClientId,
      Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    var siblingGrant = PbcSeed.Grant(fixture.FirmId, fixture.Staff, "Staff", fixture.ClientId, sibling);
    siblingGrant.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
    siblingGrant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
    db.RoleGrants.Add(siblingGrant);
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(fixture.Staff, "Staff");
    var first = await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 1);
    Assert.True(first.Succeeded, first.Message);
    Assert.True(first.Value!.HasMore);
    var cursor = new PortfolioContextChoiceCursor(actor.FirmId, actor.UserId, actor.SessionEpoch, fixture.ClientId, 1,
      first.Value.NextAfterId!.Value, first.Value.ScopeStamp);
    siblingGrant.RevokedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync();
    var revoked = await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 1, cursor);
    Assert.False(revoked.Succeeded); Assert.Null(revoked.Value);
    siblingGrant.RevokedAt = null;
    siblingGrant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    await db.SaveChangesAsync();
    var expired = await PortfolioQuery.ContextChoicesAsync(db, actor, fixture.ClientId, 1, cursor);
    Assert.False(expired.Succeeded); Assert.Null(expired.Value);
    Assert.False((await WorkspaceQuery.EngagementAsync(db, actor, sibling)).Succeeded);
    Assert.True((await WorkspaceQuery.EngagementAsync(db, actor, fixture.EngagementId)).Succeeded);
  }

  [Fact]
  public async Task ContextChoicesRejectExpiryDuringSnapshotAndDoNotReturnAStalePage()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var grant = await setup.RoleGrants.SingleAsync(g => g.UserId == fixture.Staff.Id);
      grant.GrantedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
      grant.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5);
      await setup.SaveChangesAsync();
    }
    var expiry = new ChangeDuringContextRead(async () =>
    {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == fixture.Staff.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(g => g.ExpiresAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(expiry).Options);
    var result = await PortfolioQuery.ContextChoicesAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"), fixture.ClientId);
    Assert.True(expiry.Fired);
    Assert.False(result.Succeeded);
    Assert.Null(result.Value);
  }

  [Fact]
  public async Task ContextChoicesFreshnessEndsNoLaterThanTheEarliestActiveGrantExpiry()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var grant = await db.RoleGrants.SingleAsync(g => g.UserId == fixture.Staff.Id);
    grant.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(15);
    await db.SaveChangesAsync();
    var persistedExpiry = await db.RoleGrants.AsNoTracking().Where(g => g.Id == grant.Id).Select(g => g.ExpiresAt).SingleAsync();
    Assert.NotNull(persistedExpiry);
    var started = DateTimeOffset.UtcNow;
    var result = await PortfolioQuery.ContextChoicesAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"), fixture.ClientId);
    Assert.True(result.Succeeded, result.Message);
    Assert.True(result.Value!.ValidUntilUtc <= persistedExpiry.Value);
    Assert.True(result.Value.ValidUntilUtc <= started.AddSeconds(30));
    Assert.True(result.Value.ValidUntilUtc > DateTimeOffset.UtcNow);
  }

  private sealed class ChangeDuringContextRead(Func<Task> change) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("FROM engagements", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase))
      { Fired = true; await change(); }
      return result;
    }
  }

  private sealed class CountReads : DbCommandInterceptor
  {
    public int Reads { get; private set; }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      Reads++;
      return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
  }

  private sealed class ChangeAfterFirstCandidateBatch(Func<Task> change) : DbCommandInterceptor
  {
    private bool _batchExecuted;
    public bool Fired { get; private set; }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("PortfolioContextChoicesCandidateLinkage", StringComparison.Ordinal))
        _batchExecuted = true;
      return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (_batchExecuted && !Fired)
      {
        Fired = true;
        await change();
      }
      return result;
    }
  }
}
