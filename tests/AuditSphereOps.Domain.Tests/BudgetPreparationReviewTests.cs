using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace AuditSphereOps.Domain.Tests;
[Trait("Profile","Database")]
public sealed class BudgetPreparationReviewTests
{
  private static BudgetPreparationRequest Request()=>new(Guid.NewGuid(),new("qar","0",[new(" Senior ","AUDIT",480,"PLANNING","Revenue")]));
  [Fact]
  public async Task ExactRateReviewAndConcurrentRetryPublishOneDraftAndImmutableActorReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);await BudgetPreparationReviewSeed.PopulateAsync(db,f);var a=PbcSeed.Actor(f.Staff,"Manager");var request=Request();
    var preview=(await BudgetPreparationWorkspace.PreviewAsync(db,a,f.EngagementId,request)).Value!;Assert.Equal("801.000000",preview.ForecastCost);Assert.Equal("Senior",preview.Fields.Lines[0].Role);
    request=request with{Fields=preview.Fields,Reviewed=true,ReviewBasis=preview.ReviewBasis,RequestHash=preview.RequestHash};
    async Task<string> Save(){await using var c=new AuditSphereDbContext(pg.Options);var r=await BudgetPreparationWorkspace.ExecuteAsync(c,a,f.EngagementId,request);Assert.True(r.Succeeded,r.Message);return JsonSerializer.Serialize(r.Value);}
    var results=await Task.WhenAll(Save(),Save());Assert.Equal(results[0],results[1]);var receipt=await db.BudgetPreparations.AsNoTracking().SingleAsync();var budget=await db.EngagementBudgets.AsNoTracking().SingleAsync();
    Assert.Equal(PracticeTimeStates.BudgetDraft,budget.Status);Assert.Null(budget.ApprovedByUserId);Assert.Equal(receipt.BudgetId,budget.Id);
    var lookup=(await BudgetPreparationWorkspace.LookupAsync(db,a,f.EngagementId,request.RequestId,request.RequestHash)).Value!;Assert.True(lookup.Found);Assert.Equal(receipt.Id,lookup.Receipt!.Id);
    await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.InputGeneration,y=>y.InputGeneration+1));
    Assert.True((await BudgetPreparationWorkspace.ExecuteAsync(db,a,f.EngagementId,request)).Succeeded);
    Assert.False((await BudgetPreparationWorkspace.ExecuteAsync(db,a,f.EngagementId,request with{Fields=request.Fields with{Currency="USD"}})).Succeeded);
    Assert.Equal("P0001",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE budget_preparations SET preview_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
  }
  [Fact]
  public async Task StaleRateOrRevisionUnreviewedAndUnauthorizedRequestsNeverCreateBudgets()
  {
    await using var pg=await PgTestSchema.CreateAsync();var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);await BudgetPreparationReviewSeed.PopulateAsync(db,f);var a=PbcSeed.Actor(f.Staff,"Manager");var r=Request();
    Assert.False((await BudgetPreparationWorkspace.ExecuteAsync(db,a,f.EngagementId,r)).Succeeded);
    var p=(await BudgetPreparationWorkspace.PreviewAsync(db,a,f.EngagementId,r)).Value!;r=r with{Reviewed=true,ReviewBasis=p.ReviewBasis,RequestHash=p.RequestHash};
    await db.RateCardVersions.Where(x=>x.FirmId==f.FirmId).ExecuteUpdateAsync(x=>x.SetProperty(y=>y.RatePerHour,200m));
    Assert.Equal("revision.stale",(await BudgetPreparationWorkspace.ExecuteAsync(db,a,f.EngagementId,r)).ErrorCode);
    Assert.Equal((await BudgetPreparationWorkspace.PreviewAsync(db,a,Guid.NewGuid(),r)).ErrorCode,(await BudgetPreparationWorkspace.PreviewAsync(db,a,foreign.EngagementId,r)).ErrorCode);
    Assert.False((await BudgetPreparationWorkspace.PreviewAsync(db,PbcSeed.Actor(f.Client,"ClientUser"),f.EngagementId,r)).Succeeded);
    Assert.False((await BudgetPreparationWorkspace.PreviewAsync(db,a,f.EngagementId,r with{Fields=r.Fields with{ExpectedVersion="1"}})).Succeeded);
    Assert.Empty(await db.EngagementBudgets.ToListAsync());Assert.Empty(await db.BudgetPreparations.ToListAsync());
    await db.RoleGrants.Where(x=>x.UserId==f.Staff.Id&&x.Role=="Manager").ExecuteUpdateAsync(x=>x.SetProperty(y=>y.RevokedAt,DateTimeOffset.UtcNow));
    Assert.False((await BudgetPreparationWorkspace.LookupAsync(db,a,f.EngagementId,r.RequestId,p.RequestHash)).Succeeded);
  }
}
