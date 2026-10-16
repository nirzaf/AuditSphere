using System.Data.Common;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditRecordPublicationTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RevocationDuringRecordCompositionRefusesTheWholeProjection(bool population)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Guid recordId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      if (population)
      {
        var created = await AuditPlanningService.CreatePopulationVersionAsync(db, actor,
          new CreatePopulationRequest(f.EngagementId, "Synthetic population", "Existence", "Synthetic receipt", "All rows", 1, 10m, "QAR", null));
        Assert.True(created.Succeeded, created.Message);
        recordId = created.Value!.PopulationId;
      }
      else
      {
        var created = await AuditPlanningService.CreateFindingAsync(db, actor,
          new CreateFindingRequest(f.EngagementId, "Synthetic finding", "Synthetic impact", false, 10m, null));
        Assert.True(created.Succeeded, created.Message);
        recordId = created.Value!.FindingId;
      }
    }
    var interceptor = new RevokeDuringRead(population ? "FROM audit_selections" : "SELECT e.professional_work_blocked", async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      await revoke.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "Staff")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    if (population)
    {
      var result = await AuditRecordQueries.PopulationAsync(reader, actor, recordId);
      Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
      Assert.Null(result.Value);
    }
    else
    {
      var result = await AuditRecordQueries.FindingAsync(reader, actor, recordId);
      Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
      Assert.Null(result.Value);
    }
    Assert.Equal(1, interceptor.Calls);
  }

  [Theory]
  [InlineData("Administrator")]
  [InlineData("Partner")]
  [InlineData("Manager")]
  [InlineData("SeniorManager")]
  [InlineData("Senior")]
  [InlineData("Staff")]
  [InlineData("EngagementLeader")]
  [InlineData("Auditor")]
  public async Task CurrentExactEngagementAuditRolesCanReadBothRecordsWithoutMutatingThem(string role)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var preparer = PbcSeed.Actor(f.Staff, "Staff");
    await using var db = new AuditSphereDbContext(pg.Options);
    var finding = await AuditPlanningService.CreateFindingAsync(db, preparer,
      new CreateFindingRequest(f.EngagementId, "Synthetic finding", "Synthetic impact", false, 10m, null));
    var population = await AuditPlanningService.CreatePopulationVersionAsync(db, preparer,
      new CreatePopulationRequest(f.EngagementId, "Synthetic population", "Existence", "Synthetic receipt", "All rows", 1, 10m, "QAR", null));
    Assert.True(finding.Succeeded, finding.Message);
    Assert.True(population.Succeeded, population.Message);
    var readerUser = PbcSeed.User(f.FirmId, "Staff");
    db.Users.Add(readerUser);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, readerUser, role, f.ClientId, f.EngagementId));
    await db.SaveChangesAsync();
    var actor = PbcSeed.Actor(readerUser, role);
    var fRead = await AuditRecordQueries.FindingAsync(db, actor, finding.Value!.FindingId);
    var pRead = await AuditRecordQueries.PopulationAsync(db, actor, population.Value!.PopulationId);
    Assert.True(fRead.Succeeded, fRead.Message);
    Assert.True(pRead.Succeeded, pRead.Message);
    Assert.Equal(finding.Value.FindingId, fRead.Value!.Id);
    Assert.Equal(population.Value.PopulationId, pRead.Value!.Id);
    Assert.Equal("Synthetic impact", fRead.Value.ImpactDescription);
    Assert.Empty(pRead.Value.Selections);
    Assert.Empty(pRead.Value.Evidence);
    Assert.Null(fRead.Value.ManagementResponse);
    Assert.Equal(1, await db.Findings.CountAsync());
    Assert.Equal(1, await db.PopulationVersions.CountAsync());
  }

  private sealed class RevokeDuringRead(string sql, Func<Task> revoke) : DbCommandInterceptor
  {
    private int calls;
    public int Calls => calls;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains(sql, StringComparison.Ordinal) && Interlocked.CompareExchange(ref calls, 1, 0) == 0)
        await revoke();
      return result;
    }
  }
}
