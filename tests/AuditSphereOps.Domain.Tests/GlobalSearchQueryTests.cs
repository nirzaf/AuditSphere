using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Search;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// UX-029 bounded navigation search: every hit must be openable by the same actor through the route it links to,
/// and records outside the actor's current grants never appear, are never counted and never shape a snippet.
/// </summary>
[Trait("Profile", "Database")]
public sealed class GlobalSearchQueryTests
{
  private const string Marker = "ZQXSEARCH";

  private sealed record World(PgTestSchema Pg, PbcSeed.Fixture Own, SiblingClientSeed.Sibling Sibling) : IAsyncDisposable
  {
    public AuditSphereDbContext Db() => new(Pg.Options);
    public async ValueTask DisposeAsync()
    {
      PbcSeed.DeleteDirectory(Sibling.StagingRoot);
      await Pg.DisposeAsync();
    }
  }

  private static async Task<World> SeedAsync()
  {
    var pg = await PgTestSchema.CreateAsync();
    var own = await PbcSeed.SeedAsync(pg);
    await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, own, PbcSeed.Actor(own.Staff, "Staff"), PbcSeed.Actor(own.Client, "ClientUser"));
    var sibling = await SiblingClientSeed.SeedAsync(pg, own.FirmId, Marker);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Leads.Add(new Lead { Id = Guid.NewGuid(), FirmId = own.FirmId, Name = $"{Marker} Prospect Lead", Source = "Referral", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    return new(pg, own, sibling);
  }

  private static async Task<(AppUser User, ActorContext Actor)> UserAsync(World w, params (string Role, Guid? ClientId, Guid? EngagementId)[] grants)
  {
    var user = PbcSeed.User(w.Own.FirmId, "Staff");
    await using var db = w.Db();
    db.Users.Add(user);
    foreach (var (role, client, engagement) in grants)
      db.RoleGrants.Add(PbcSeed.Grant(w.Own.FirmId, user, role, client, engagement));
    await db.SaveChangesAsync();
    return (user, new ActorContext(user.Id, user.FirmId, user.SessionEpoch, grants.Select(g => g.Role).Distinct().ToArray()));
  }

  private static async Task<GlobalSearchResult> SearchAsync(World w, ActorContext actor, string term)
  {
    await using var db = w.Db();
    var result = await GlobalSearchQuery.SearchAsync(db, actor, term);
    Assert.True(result.Succeeded, result.Message);
    return result.Value!;
  }

  [Fact]
  public async Task ClientScopedUser_FindsOwnClientEngagementAndRequest_NeverTheSibling()
  {
    await using var w = await SeedAsync();
    var (_, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, null), ("Partner", w.Own.ClientId, null));

    var own = await SearchAsync(w, actor, "pbc test");
    Assert.Contains(own.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Client && x.Href == $"/app/clients/{w.Own.ClientId:D}");
    Assert.Contains(own.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Engagement && x.Href == $"/app/engagements/{w.Own.EngagementId:D}");
    Assert.Contains(own.Hits, x => x.Kind == GlobalSearchQuery.Kinds.PbcRequest && x.Href == $"/app/engagements/{w.Own.EngagementId:D}/pbc");

    var sibling = await SearchAsync(w, actor, Marker);
    Assert.Empty(sibling.Hits);
    Assert.False(sibling.Truncated);
    Assert.DoesNotContain(own.Hits, x => x.Title.Contains(Marker, StringComparison.OrdinalIgnoreCase) || x.Detail.Contains(Marker, StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public async Task EngagementOnlyUser_GetsNoClientLevelHitAndNoSiblingEngagement()
  {
    await using var w = await SeedAsync();
    var otherEngagement = Guid.NewGuid();
    await using (var db = w.Db())
    {
      db.Engagements.Add(new Domain.Engagements.Engagement { Id = otherEngagement, FirmId = w.Own.FirmId, PracticeClientId = w.Own.ClientId,
        ServiceRoute = "SiblingEngagementRoute", Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
    }
    var (_, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, w.Own.EngagementId));

    var result = await SearchAsync(w, actor, "pbc test");
    Assert.DoesNotContain(result.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Client);
    Assert.Contains(result.Hits, x => x.Href == $"/app/engagements/{w.Own.EngagementId:D}");
    Assert.DoesNotContain(result.Hits, x => x.Href.Contains(otherEngagement.ToString("D"), StringComparison.Ordinal));
    Assert.DoesNotContain((await SearchAsync(w, actor, "SiblingEngagementRoute")).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);
  }

  [Fact]
  public async Task FirmWideUser_FindsBothClients_AndCommercialRoleGatesLeads()
  {
    await using var w = await SeedAsync();
    var (_, partner) = await UserAsync(w, ("Partner", null, null));
    var both = await SearchAsync(w, partner, "holdings");
    Assert.Contains(both.Hits, x => x.Href == $"/app/clients/{w.Sibling.Fixture.ClientId:D}");
    Assert.Contains((await SearchAsync(w, partner, Marker)).Hits, x => x.Kind == GlobalSearchQuery.Kinds.Lead);

    // A client-scoped Partner is not a firm-wide commercial identity, so leads stay hidden.
    var (_, scoped) = await UserAsync(w, ("Partner", w.Own.ClientId, null));
    Assert.DoesNotContain((await SearchAsync(w, scoped, Marker)).Hits, x => x.Kind == GlobalSearchQuery.Kinds.Lead);
  }

  [Fact]
  public async Task ClientIdentitiesRevokedGrantsAndStaleSessionsGetNothing()
  {
    await using var w = await SeedAsync();
    await using (var db = w.Db())
    {
      var denied = await GlobalSearchQuery.SearchAsync(db, PbcSeed.Actor(w.Own.Client, "ClientUser"), "pbc test");
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }

    var (user, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, null));
    Assert.Contains((await SearchAsync(w, actor, "pbc test")).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);
    await using (var db = w.Db())
    {
      var grant = await db.RoleGrants.SingleAsync(x => x.UserId == user.Id);
      grant.RevokedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync();
    }
    Assert.DoesNotContain((await SearchAsync(w, actor, "pbc test")).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);

    await using (var db = w.Db())
    {
      var stale = await GlobalSearchQuery.SearchAsync(db, actor with { SessionEpoch = actor.SessionEpoch + 1 }, "pbc test");
      Assert.Equal(ErrorCodes.ScopeDenied, stale.ErrorCode);
    }
  }

  [Theory]
  [InlineData("%")]
  [InlineData("%%")]
  [InlineData("__")]
  [InlineData("a")]
  [InlineData("   ")]
  public async Task WildcardsAndShortTermsNeverMatchEverything(string term)
  {
    await using var w = await SeedAsync();
    var (_, partner) = await UserAsync(w, ("Partner", null, null));
    Assert.DoesNotContain((await SearchAsync(w, partner, term)).Hits, x => x.Kind != GlobalSearchQuery.Kinds.Page);
  }

  [Fact]
  public async Task PagesAreNavigationOnlyAndLongTermsAreBounded()
  {
    await using var w = await SeedAsync();
    var (_, actor) = await UserAsync(w, ("Staff", w.Own.ClientId, null));
    var pages = await SearchAsync(w, actor, "consolidation");
    Assert.Contains(pages.Hits, x => x.Kind == GlobalSearchQuery.Kinds.Page && x.Href == "/app/consolidation");
    var longTerm = await SearchAsync(w, actor, new string('x', 500));
    Assert.Equal(GlobalSearchQuery.MaximumTermLength, longTerm.Term.Length);
  }
}
