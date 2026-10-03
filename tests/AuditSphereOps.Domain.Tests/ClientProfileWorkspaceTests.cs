using System.Data.Common;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientProfileWorkspaceTests
{
  [Fact]
  public async Task MetadataFullScopedCountsAndIndependentPagesNeverTruncateAtTheOldWindow()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, 105);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var result = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new());
    Assert.True(result.Succeeded, result.Message); var v = result.Value!;
    Assert.Equal(new ClientWorkspaceMetrics(105, 52, 105), v.Metrics);
    Assert.Equal(10, v.Engagements.Count); Assert.Equal(10, v.Contacts.Count);
    var retainedWindow = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!;
    Assert.Equal(100, retainedWindow.Engagements.Count); Assert.Equal(100, retainedWindow.Contacts.Count);
    Assert.Equal(v.Metrics, retainedWindow.Metrics);
    Assert.Equal("Synthetic trading name", v.CommercialName); Assert.Equal("SYNTHETIC-REG-001", v.RegistrationNumber);
    Assert.Equal("QA", v.Jurisdiction); Assert.Equal("9007199254740993", v.SafetyGeneration);
    Assert.Equal("AWAITING_ACCEPTANCE", v.PortalIntent!.State); Assert.False(v.PortalIntent.ContactHasClientAccess);
    var last = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(2, 50, 4, 25))).Value!;
    Assert.Equal(5, last.Engagements.Count); Assert.Equal(5, last.Contacts.Count); Assert.Equal(v.Metrics, last.Metrics);
    Assert.Empty(last.Engagements.Select(e => e.Id).Intersect(v.Engagements.Select(e => e.Id)));
    Assert.Empty(last.Contacts.Select(c => c.Id).Intersect(v.Contacts.Select(c => c.Id)));
    Assert.Equal("request.invalid", (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(-1))).ErrorCode);
    Assert.Equal("request.invalid", (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(ContactPageSize: 100))).ErrorCode);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, foreign.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, Guid.NewGuid())).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, PbcSeed.Actor(f.Client, "ClientUser"), f.ClientId)).Succeeded);
    Assert.DoesNotContain("EXCLUDED PRIVATE PROFILE", System.Text.Json.JsonSerializer.Serialize(v));
    await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Disabled, true));
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);
  }

  [Fact]
  public async Task ClientCommercialAuthorityDoesNotWidenEngagementRoleOrAggregateSiblingData()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, grantClientStaff: false);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "CommercialManager", f.ClientId)); await db.SaveChangesAsync();
    var result = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(new ClientWorkspaceMetrics(1, 0, 27), result.Value!.Metrics);
    Assert.Equal(f.EngagementId, Assert.Single(result.Value.Engagements).Id);
    Assert.False(result.Value.CanCreateEngagement); Assert.False(result.Value.CanManageContacts);
    await db.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.Role == "Staff")
      .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    var expired = PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId, f.EngagementId);
    expired.GrantedAt = DateTimeOffset.UtcNow.AddHours(-2);
    expired.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
    db.RoleGrants.Add(expired); await db.SaveChangesAsync();
    var withoutEngagement = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!;
    Assert.Equal(0, withoutEngagement.Metrics.Engagements); Assert.Empty(withoutEngagement.Engagements);
  }

  [Fact]
  public async Task MidProjectionEngagementRevocationRefusesOldCountsWhileClientGrantRemains()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      await ClientProfileWorkspaceSeed.PopulateAsync(setup, f, grantClientStaff: false);
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "CommercialManager", f.ClientId)); await setup.SaveChangesAsync();
    }
    var interceptor = new RevokeDuringContacts(async () => {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded); Assert.True(interceptor.Fired);
    Assert.Equal(0, (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!.Metrics.Engagements);
  }

  private sealed class RevokeDuringContacts(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("FROM client_contacts", StringComparison.Ordinal))
      { Fired = true; await revoke(); }
      return result;
    }
  }
}
