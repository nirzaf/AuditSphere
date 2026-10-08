using AuditSphereOps.Application.Security;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Domain.Tests;
public sealed class AdministrationRuntimeQueryTests
{
  [Fact]
  public async Task RuntimeRequiresCurrentFirmAdministratorAndReturnsExactPersistedState()
  {
    await using var f=await TenantAdministrationFixture.CreateAsync();await using var db=f.Db();
    var actor=f.AdminActor;var input=new AdministrationRuntimeInput("private-hostname",false,true);
    var safety=await db.FirmSafetyStates.SingleAsync(x=>x.Id==f.FirmId);
    var result=await FirmAdministrationQuery.RuntimeAsync(db,actor,input);
    Assert.True(result.Succeeded,result.Message);Assert.Equal(f.FirmId,result.Value!.FirmId);
    Assert.Equal(safety.OperatingMode,result.Value.OperatingMode);Assert.Equal(safety.DeploymentEpoch.ToString(),result.Value.DeploymentEpoch);
    Assert.Equal("CUSTOM",result.Value.Environment);Assert.False(result.Value.ExternalEffectsEnabled);Assert.True(result.Value.SimulationAdaptersAllowed);
    Assert.False((await FirmAdministrationQuery.RuntimeAsync(db,actor with {FirmId=Guid.NewGuid()},input)).Succeeded);
    await db.Users.Where(x=>x.Id==actor.UserId).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.False((await FirmAdministrationQuery.RuntimeAsync(db,actor,input)).Succeeded);
  }
}
