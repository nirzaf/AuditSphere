using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class TenantSetupMetadataWorkspaceTests
{
  [Fact]
  public async Task ReviewedSaveHasActorOwnedImmutableReceiptAndExactReplay()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == f.DraftId);
    var request = new TenantSetupEditRequest(Guid.NewGuid(), draft.Id, draft.Revision.ToString(),
      new("Reviewed synthetic label", "CONFIGURED", "NOT_CONFIGURED"));
    Assert.False((await TenantSetupMetadataWorkspace.SaveAsync(db, f.AdminActor, request, f.TenantId, DateTimeOffset.UtcNow)).Succeeded);
    var preview = await TenantSetupMetadataWorkspace.PreviewAsync(db, f.AdminActor, request, f.TenantId);
    Assert.True(preview.Succeeded, preview.Message);
    var reviewed = request with { Reviewed = true, RequestHash = preview.Value!.RequestHash, ReviewBasis = preview.Value.ReviewBasis };
    var saved = await TenantSetupMetadataWorkspace.SaveAsync(db, f.AdminActor, reviewed, f.TenantId, DateTimeOffset.UtcNow);
    Assert.True(saved.Succeeded, saved.Message);
    var replay = await TenantSetupMetadataWorkspace.SaveAsync(db, f.AdminActor, reviewed, f.TenantId, DateTimeOffset.UtcNow);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(saved.Value, replay.Value);
    Assert.Equal(1, await db.Microsoft365AdministrationEvents.CountAsync(x => x.SetupRequestId == request.RequestId));
    var persisted = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draft.Id);
    Assert.Equal(draft.Revision + 1, persisted.Revision);
    Assert.Equal("Reviewed synthetic label", persisted.TenantDisplayName);
    Assert.Equal(draft.State, persisted.State);
    var lookup = await TenantSetupMetadataWorkspace.LookupAsync(db, f.AdminActor, request.RequestId, reviewed.RequestHash!);
    Assert.True(lookup.Succeeded, lookup.Message);
    Assert.Equal(saved.Value, lookup.Value!.Receipt);
    var other = new AuditSphereOps.Application.Abstractions.ActorContext(f.SecondAdmin.Id, f.FirmId, f.SecondAdmin.SessionEpoch, ["Administrator"]);
    var absent = await TenantSetupMetadataWorkspace.LookupAsync(db, other, request.RequestId, reviewed.RequestHash!);
    Assert.True(absent.Succeeded, absent.Message);
    Assert.False(absent.Value!.Found);
  }

  [Fact]
  public async Task ChangedReviewWrongTenantAndProtectedDraftFailClosed()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == f.DraftId);
    var request = new TenantSetupEditRequest(Guid.NewGuid(), draft.Id, draft.Revision.ToString(),
      new("Synthetic", "NOT_CONFIGURED", "NOT_CONFIGURED"));
    Assert.False((await TenantSetupMetadataWorkspace.PreviewAsync(db, f.AdminActor, request, Guid.NewGuid().ToString())).Succeeded);
    var preview = await TenantSetupMetadataWorkspace.PreviewAsync(db, f.AdminActor, request, f.TenantId);
    Assert.True(preview.Succeeded, preview.Message);
    var reviewed = request with { Reviewed = true, RequestHash = preview.Value!.RequestHash, ReviewBasis = preview.Value.ReviewBasis };
    Assert.False((await TenantSetupMetadataWorkspace.SaveAsync(db, f.AdminActor,
      reviewed with { Fields = request.Fields with { MailState = "CONFIGURED" } }, f.TenantId, DateTimeOffset.UtcNow)).Succeeded);
    await db.Microsoft365SetupDrafts.Where(x => x.Id == draft.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.Revision, d => d.Revision + 1));
    Assert.False((await TenantSetupMetadataWorkspace.SaveAsync(db, f.AdminActor, reviewed, f.TenantId, DateTimeOffset.UtcNow)).Succeeded);
    await db.Microsoft365SetupDrafts.Where(x => x.Id == draft.Id).ExecuteUpdateAsync(x => x.SetProperty(d => d.State, Microsoft365RevisionStates.Active));
    Assert.False((await TenantSetupMetadataWorkspace.PreviewAsync(db, f.AdminActor,
      request with { ExpectedRevision = (draft.Revision + 1).ToString() }, f.TenantId)).Succeeded);
    Assert.Equal(0, await db.Microsoft365AdministrationEvents.CountAsync(x => x.SetupRequestId == request.RequestId));
  }
  [Fact]
  public async Task StaleAdministratorAndMismatchedConnectionTenantCannotReview()
  {
    await using var f = await TenantAdministrationFixture.CreateAsync();
    await using var db = f.Db();
    var draft = await db.Microsoft365SetupDrafts.SingleAsync(x => x.Id == f.DraftId);
    var actor = f.AdminActor;
    var request = new TenantSetupEditRequest(Guid.NewGuid(), draft.Id, draft.Revision.ToString(),
      new("Synthetic", "NOT_CONFIGURED", "NOT_CONFIGURED"));
    await db.Microsoft365ConnectionRevisions.Where(x => x.Id == f.ConnectionId)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.TenantId, Guid.NewGuid().ToString()));
    Assert.False((await TenantSetupMetadataWorkspace.PreviewAsync(db, actor, request, f.TenantId)).Succeeded);
    await db.Microsoft365ConnectionRevisions.Where(x => x.Id == f.ConnectionId)
      .ExecuteUpdateAsync(x => x.SetProperty(d => d.TenantId, f.TenantId));
    await db.Users.Where(x => x.Id == actor.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.False((await TenantSetupMetadataWorkspace.PreviewAsync(db, actor, request, f.TenantId)).Succeeded);
  }

}
