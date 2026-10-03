using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class TenantAdministrationWorkspaceTests
{
  [Fact]
  public async Task SavedSetupMetadataIsNotCapabilityVerificationAndRequiresCurrentFirmAuthority()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == f.DraftId);
    draft.TenantDisplayName = "Synthetic tenant label";
    draft.MailState = "CONFIGURED";
    draft.RecordsState = "NOT_CONFIGURED";
    await db.SaveChangesAsync();
    var result = await TenantAdministrationWorkspaceQuery.GetAsync(db, f.AdminActor, f.Options, DateTimeOffset.UtcNow);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(new TenantSetupMetadata("Synthetic tenant label", "CONFIGURED", "NOT_CONFIGURED"), result.Value!.SetupMetadata);
    Assert.False(result.Value.SetupProgress!.ConsentVerified);
    Assert.DoesNotContain(result.Value.Capabilities, x => x.Capability != "SIGN_IN" && x.State == "VERIFIED");
    Assert.Equal("DISABLED", Assert.Single(result.Value.Capabilities, x => x.Capability == "OUTBOUND_MAIL").State);
    var outsider = TenantAdministrationFixture.User(f.FirmId, f.TenantId, "synthetic@example.test", "Scoped user");
    db.Users.Add(outsider);
    db.RoleGrants.Add(new RoleGrant { Id = Guid.CreateVersion7(), FirmId = f.FirmId, UserId = outsider.Id,
      Role = "Staff", ClientId = f.ClientId, GrantedByUserId = f.Admin.Id, GrantedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var denied = await TenantAdministrationWorkspaceQuery.GetAsync(db,
      new(outsider.Id, f.FirmId, outsider.SessionEpoch, ["Administrator"]), f.Options, DateTimeOffset.UtcNow);
    Assert.False(denied.Succeeded);
    Assert.Null(denied.Value);
    var stale = f.AdminActor;
    await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.False((await TenantAdministrationWorkspaceQuery.GetAsync(db, stale, f.Options, DateTimeOffset.UtcNow)).Succeeded);
  }
}
