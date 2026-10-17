using System.Data.Common;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditProcedureSubmissionConcurrencyTests
{
  [Fact]
  public async Task DistinctProceduresOverlapUnderSharedGenerationFence_AndDuplicateSubmissionIsRefused()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await PlanningSeed.CreateAsync(pg, role: "Partner");
    var scope = fixture.Primary;
    Guid[] procedures;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var published = await AuditProgramTestHelpers.PublishAsync(db, scope.Actor,
        new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
      Assert.True(published.Succeeded, published.Message);
      Assert.True((await AuditProgramService.AdoptAsync(db, scope.Actor,
        new AdoptAuditProgramRequest(scope.EngagementId, published.Value!.ProgramVersionId))).Succeeded);
      procedures = await db.AuditProcedures.Where(x => x.EngagementId == scope.EngagementId &&
        (x.SourceProcedureId == "AWP-08-01" || x.SourceProcedureId == "AWP-08-02")).OrderBy(x => x.SourceProcedureId).Select(x => x.Id).ToArrayAsync();
      Assert.Equal(2, procedures.Length);
      foreach (var id in procedures)
        Assert.True((await AuditProgramTestHelpers.MarkApplicableAsync(db, scope.Actor, id, null)).Succeeded);
      var approver = new AppUser { Id = Guid.NewGuid(), FirmId = scope.FirmId, Subject = "parallel-basis-" + Guid.NewGuid().ToString("N"),
        TenantId = "tenant-planning", Email = "parallel-basis@example.test", DisplayName = "Independent basis approver", UserKind = "Staff",
        SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow };
      db.Users.Add(approver);
      db.RoleGrants.Add(new RoleGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = approver.Id, Role = "Partner",
        ClientId = scope.ClientId, EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Actor.UserId });
      await db.SaveChangesAsync();
      await PlanningBasisSeed.EstablishAsync(db, scope.FirmId, scope.ClientId, scope.EngagementId, scope.Actor,
        new ActorContext(approver.Id, scope.FirmId, 1, ["Partner"]));
    }
    var overlap = new BothGenerationReads();
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(overlap).Options;
    SubmitProcedureResultRequest Request(Guid id) => new(id, 1, "Agreed the source to the ledger.", "{}", ["synthetic:parallel-source"], "No exceptions.");
    var writes = await Task.WhenAll(procedures.Select(async id => {
      await using var db = new AuditSphereDbContext(options);
      return await AuditProgramService.SubmitResultAsync(db, scope.Actor, Request(id));
    }));
    Assert.Equal(2, overlap.Arrivals);
    Assert.All(writes, x => Assert.True(x.Succeeded, x.Message));
    Assert.Equal(2, writes.Select(x => x.Value!.AuditProcedureResultId).Distinct().Count());
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await verify.AuditProcedureResults.CountAsync(x => x.EngagementId == scope.EngagementId));
    var duplicate = await AuditProgramService.SubmitResultAsync(verify, scope.Actor, Request(procedures[0]));
    Assert.Equal(ErrorCodes.ProtectedState, duplicate.ErrorCode);
    Assert.Equal(2, await verify.AuditProcedureResults.CountAsync(x => x.EngagementId == scope.EngagementId));
  }

  private sealed class BothGenerationReads : DbCommandInterceptor
  {
    private int arrivals;
    private readonly TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Arrivals => Volatile.Read(ref arrivals);
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("client_safety_states", StringComparison.Ordinal) &&
          command.CommandText.Contains("FOR SHARE", StringComparison.Ordinal))
      {
        if (Interlocked.Increment(ref arrivals) == 2) both.TrySetResult();
        await both.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
      }
      return result;
    }
  }
}
