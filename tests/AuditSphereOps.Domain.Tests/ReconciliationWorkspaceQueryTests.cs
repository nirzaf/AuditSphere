using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class ReconciliationWorkspaceQueryTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task LateInputOrEpochChangeReturnsNoProtectedReview(bool epoch)
  {
    await using var pg=await PgTestSchema.CreateAsync();var s=await ReconciliationReviewSeed.SeedAsync(pg);
    var interceptor=new ChangeDuringProof(pg,s.Fixture,epoch);
    var options=new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var db=new AuditSphereDbContext(options);
    var result=await ReconciliationWorkspaceQuery.GetAsync(db,PbcSeed.Actor(s.Fixture.Staff,"AccountingPreparer"),s.ReconciliationId);
    Assert.True(interceptor.Fired);Assert.False(result.Succeeded);Assert.Null(result.Value);
  }
  private sealed class ChangeDuringProof(ITestPostgresDatabase pg,PbcSeed.Fixture f,bool epoch):DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default)
    {
      if(!Fired && command.CommandText.Contains("accounting_reconciliation_proofs",StringComparison.Ordinal))
      {
        Fired=true;await using var other=new AuditSphereDbContext(pg.Options);
        if(epoch)await other.Users.Where(x=>x.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SessionEpoch,v=>v.SessionEpoch+1),ct);
        else await other.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.InputGeneration,v=>v.InputGeneration+1),ct);
      }
      return result;
    }
  }
}
