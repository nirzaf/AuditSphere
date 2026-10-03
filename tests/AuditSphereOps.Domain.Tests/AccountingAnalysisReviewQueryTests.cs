using System.Data.Common;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class AccountingAnalysisReviewQueryTests
{
  [Theory]
  [InlineData(0)]
  [InlineData(1)]
  [InlineData(2)]
  public async Task LateGenerationEpochOrExactSourceChangePublishesNoProtectedReview(int change)
  {
    await using var pg=await PgTestSchema.CreateAsync();var s=await AccountingAnalysisReviewSeed.SeedAsync(pg);
    var interceptor=new ChangeDuringLinks(pg,s,change);
    var options=new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options;
    await using var db=new AuditSphereDbContext(options);
    var r=await AccountingAnalysisReviewQuery.GetAsync(db,PbcSeed.Actor(s.Fixture.Staff,"AccountingPreparer"),"ECL",s.Evidence["ECL"]);
    Assert.True(interceptor.Fired);Assert.False(r.Succeeded);Assert.Null(r.Value);
    var foreign=PbcSeed.Actor(s.Fixture.Staff,"AccountingPreparer") with { FirmId=Guid.NewGuid() };
    Assert.False((await AccountingAnalysisReviewQuery.GetAsync(db,foreign,"ECL",s.Evidence["ECL"])).Succeeded);
  }
  private sealed class ChangeDuringLinks(ITestPostgresDatabase pg,AccountingAnalysisReviewSeed.Result s,int change):DbCommandInterceptor
  {
    public bool Fired {get;private set;}
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,CommandExecutedEventData data,DbDataReader result,CancellationToken ct=default)
    {
      if(!Fired && command.CommandText.Contains("accounting_evidence_audit_links",StringComparison.Ordinal))
      {
        Fired=true;await using var db=new AuditSphereDbContext(pg.Options);
        if(change==0)await db.ClientSafetyStates.Where(x=>x.Id==s.Fixture.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.InputGeneration,v=>v.InputGeneration+1),ct);
        else if(change==1)await db.Users.Where(x=>x.Id==s.Fixture.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.SessionEpoch,v=>v.SessionEpoch+1),ct);
        else await db.TrialBalanceDatasets.Where(x=>x.Id==s.SourceId).ExecuteUpdateAsync(x=>x.SetProperty(v=>v.NormalizedDatasetDigest,new string('e',64)),ct);
      }
      return result;
    }
  }
}
