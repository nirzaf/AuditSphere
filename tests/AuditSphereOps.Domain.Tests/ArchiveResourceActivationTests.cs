using System.Data.Common;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class ArchiveResourceActivationTests
{
  [Fact]
  public async Task SupportedArchiveActivationRetainsExactProvenanceAndSharedTargetWithoutWorkingConfiguration()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(pg, includeSibling: false);
    var actor = world.Administrator;
    var request = await PrepareAsync(pg, actor, "secondary-archive");
    await using var db = new AuditSphereDbContext(pg.Options);
    var templatesBefore = await db.FolderTemplateVersions.CountAsync();
    var bindingsBefore = await db.RepositoryBindings.CountAsync();
    var workspacesBefore = await db.FirmWorkspaceConfigurations.AsNoTracking().ToListAsync();
    var activated = await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, request, DateTimeOffset.UtcNow);
    Assert.True(activated.Succeeded, activated.Message);
    Assert.Equal(request.ConnectionRevisionId, activated.Value);
    var replay = await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, request, DateTimeOffset.UtcNow);
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(activated.Value, replay.Value);
    var receipt = await db.Microsoft365AdministrationEvents.AsNoTracking().SingleAsync(x => x.Operation == "SELECTED_ARCHIVE_RESOURCE_ACTIVATED");
    Assert.Equal(actor.UserId, receipt.ActorUserId);
    Assert.Equal(request.SetupDraftId.ToString(), receipt.TargetObjectId);
    Assert.Equal(request.ConnectionRevisionId.ToString(), receipt.Result);
    using var retained = JsonDocument.Parse(receipt.RoleScopeChange!);
    Assert.Equal(request.ExpectedDraftRevision, retained.RootElement.GetProperty("revisionBefore").GetInt64());
    Assert.Equal(request.ExpectedDraftRevision + 1, retained.RootElement.GetProperty("revisionAfter").GetInt64());
    Assert.Equal(request.CapabilityVerificationId, retained.RootElement.GetProperty("verificationId").GetGuid());
    Assert.Equal(Hashing.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(request)), retained.RootElement.GetProperty("requestSha256").GetString());
    var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == request.SetupDraftId);
    Assert.Equal(Microsoft365RevisionStates.Active, draft.State);
    Assert.Equal(request.ExpectedDraftRevision + 1, draft.Revision);
    Assert.Equal(templatesBefore, await db.FolderTemplateVersions.CountAsync());
    Assert.Equal(bindingsBefore, await db.RepositoryBindings.CountAsync());
    Assert.Equal(workspacesBefore.Select(x => (x.Id, x.DefaultForFutureClients)),
      (await db.FirmWorkspaceConfigurations.AsNoTracking().ToListAsync()).Select(x => (x.Id, x.DefaultForFutureClients)));

    var partnerId = await db.RoleGrants.Where(x => x.FirmId == actor.FirmId && x.Role == "Partner" && x.RevokedAt == null)
      .Select(x => x.UserId).FirstAsync();
    var partnerUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == partnerId);
    var partner = new ActorContext(partnerId, actor.FirmId, partnerUser.SessionEpoch, ["Partner"]);
    var staff = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, (actor.FirmId, actor), "Staff");
    var assigned = await StaffingService.AssignAsync(db, partner, new(world.Primary.EngagementId, staff.User.Id, StaffingLevels.StaffAssociate));
    Assert.True(assigned.Succeeded, assigned.Message);
    var target = await ArchiveCopyService.CreateTargetRevisionAsync(db, actor,
      new(world.Primary.ClientId, null, request.DriveId, request.RootFolderId, request.SetupDraftId, "42"));
    Assert.True(target.Succeeded, target.Message);
    var pin = await db.ArchiveCopyTargetRevisions.AsNoTracking().SingleAsync(x => x.Id == target.Value!.TargetRevisionId);
    Assert.Equal(request.SetupDraftId, pin.SelectedResourceDraftId);
    Assert.Equal(request.ExpectedDraftRevision + 1, pin.SelectedResourceDraftRevision);
    Assert.Equal(request.CapabilityVerificationId, pin.SelectedResourceVerificationId);
    Assert.Equal(retained.RootElement.GetProperty("evidenceSha256").GetString(), pin.SelectedResourceEvidenceSha256);
    Assert.Equal(world.Primary.ClientId, pin.ClientId);
  }

  [Fact]
  public async Task ActivationRejectsStaleBindingsProofTimesAndSupersededVerificationWithoutMutating()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (_, actor) = await SupportedAuditCompletionWorldBuilder.BootstrapAdminAsync(pg);
    var request = await PrepareAsync(pg, actor, "secondary-archive");
    await using var db = new AuditSphereDbContext(pg.Options);
    var observed = await db.TenantCapabilityVerifications.Where(x => x.Id == request.CapabilityVerificationId).Select(x => x.ObservedAt).SingleAsync();
    foreach (var invalid in new[]
    {
      request with { ExpectedDraftRevision = request.ExpectedDraftRevision - 1 },
      request with { CapabilityVerificationId = Guid.NewGuid() },
      request with { ConnectionRevisionId = Guid.NewGuid() },
      request with { TenantId = Guid.NewGuid().ToString("D") },
      request with { DriveId = "wrong-library" },
      request with { RootFolderId = "wrong-root" },
      request with { AccessProfile = Microsoft365AccessProfiles.DirectStaffCollaboration }
    })
      Assert.False((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, invalid, DateTimeOffset.UtcNow)).Succeeded);
    foreach (var invalidTime in new[] { observed.AddTicks(-10), observed.AddHours(24).AddSeconds(1) })
      Assert.False((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, request, invalidTime)).Succeeded);

    // A failed supported re-probe publishes its pending denial before provider I/O.
    await Assert.ThrowsAsync<OperationBlockedException>(() => new SelectedSiteBoundaryVerificationService(
      new Factory(pg), new FailedProbe(), ControlUrl).VerifyDraftAsync(actor, request.SetupDraftId, default, request.ExpectedDraftRevision));
    Assert.False((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, request, DateTimeOffset.UtcNow)).Succeeded);
    Assert.False(await db.Microsoft365AdministrationEvents.AnyAsync(x => x.Operation == "SELECTED_ARCHIVE_RESOURCE_ACTIVATED"));
    Assert.False(await db.FirmWorkspaceConfigurations.AnyAsync());
    Assert.Equal(Microsoft365RevisionStates.Verified,
      await db.Microsoft365ConnectionRevisions.Where(x => x.Id == request.ConnectionRevisionId).Select(x => x.State).SingleAsync());
  }

  [Fact]
  public async Task WorkingActivationRemainsSeparateAndArchiveCannotReuseItsLibraryOrRepurposeIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (_, actor) = await SupportedAuditCompletionWorldBuilder.BootstrapAdminAsync(pg);
    var working = await PrepareAsync(pg, actor, "working-library");
    await using var db = new AuditSphereDbContext(pg.Options);
    var template = await Microsoft365ConfigurationService.SaveFolderTemplateAsync(db, actor,
      new(FolderTemplatePurposes.ClientWorkspace, Microsoft365ConfigurationService.DefaultManifest(FolderTemplatePurposes.ClientWorkspace)), DateTimeOffset.UtcNow);
    Assert.True(template.Succeeded, template.Message);
    Assert.True((await Microsoft365ConfigurationService.ApproveFolderTemplateAsync(db, actor, template.Value!.Id, DateTimeOffset.UtcNow)).Succeeded);
    var activated = await Microsoft365ConfigurationService.ActivateConnectionAsync(db, actor,
      new(working.SetupDraftId, working.ConnectionRevisionId, template.Value.Id, working.ExpectedDraftRevision,
        working.SiteId, working.DriveId, working.RootFolderId, working.DisplayUrl, working.AccessProfile), DateTimeOffset.UtcNow);
    Assert.True(activated.Succeeded, activated.Message);
    Assert.False((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, working, DateTimeOffset.UtcNow)).Succeeded);
    var overlapping = await PrepareAsync(pg, actor, "working-library");
    Assert.False((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, overlapping, DateTimeOffset.UtcNow)).Succeeded);
    var archive = await PrepareAsync(pg, actor, "separate-library");
    Assert.True((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, actor, archive, DateTimeOffset.UtcNow)).Succeeded);
    await using var fresh = new AuditSphereDbContext(pg.Options);
    Assert.False((await Microsoft365ConfigurationService.ActivateConnectionAsync(fresh, actor,
      new(archive.SetupDraftId, archive.ConnectionRevisionId, template.Value.Id, archive.ExpectedDraftRevision + 1,
        archive.SiteId, archive.DriveId, archive.RootFolderId, archive.DisplayUrl, archive.AccessProfile), DateTimeOffset.UtcNow)).Succeeded);
    var archiveAsWorking = await PrepareAsync(pg, actor, archive.DriveId);
    var overlap = await Microsoft365ConfigurationService.ActivateConnectionAsync(fresh, actor,
      new(archiveAsWorking.SetupDraftId, archiveAsWorking.ConnectionRevisionId, template.Value.Id,
        archiveAsWorking.ExpectedDraftRevision, archiveAsWorking.SiteId, archiveAsWorking.DriveId,
        archiveAsWorking.RootFolderId, archiveAsWorking.DisplayUrl, archiveAsWorking.AccessProfile), DateTimeOffset.UtcNow);
    Assert.False(overlap.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, overlap.ErrorCode);
    var workspace = await fresh.FirmWorkspaceConfigurations.AsNoTracking().SingleAsync();
    Assert.Equal(activated.Value, workspace.Id);
    Assert.True(workspace.DefaultForFutureClients);
    Assert.Equal(working.DriveId, workspace.DriveId);
  }

  [Fact]
  public async Task ActivationRechecksCurrentAdministratorAfterWaitingForRevocationCommit()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var admin = await SupportedAuditCompletionWorldBuilder.BootstrapAdminAsync(pg);
    var second = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, admin, "Administrator");
    var request = await PrepareAsync(pg, admin.Actor, "secondary-archive");
    await using var read = new AuditSphereDbContext(pg.Options);
    var grantId = await read.RoleGrants.Where(x => x.UserId == admin.Actor.UserId && x.Role == "Administrator" && x.RevokedAt == null).Select(x => x.Id).SingleAsync();
    var barrier = new CommitBarrier();
    await using var revoker = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(barrier).Options);
    var revoking = RoleAdministrationService.RevokeRoleGrantAsync(revoker, second.Actor, new(grantId));
    await barrier.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
    await using var commandDb = new AuditSphereDbContext(pg.Options);
    await commandDb.Database.OpenConnectionAsync();
    var commandPid = ((NpgsqlConnection)commandDb.Database.GetDbConnection()).ProcessID;
    var activating = Microsoft365ConfigurationService.ActivateArchiveResourceAsync(commandDb, admin.Actor, request, DateTimeOffset.UtcNow);
    try
    {
      var waiting = false;
      await using var observer = new NpgsqlConnection(pg.ConnectionString);
      await observer.OpenAsync();
      for (var attempt = 0; attempt < 200 && !waiting; attempt++)
      {
        await using var query = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", observer);
        query.Parameters.AddWithValue("pid", commandPid);
        waiting = (bool)(await query.ExecuteScalarAsync())!;
        if (!waiting) await Task.Delay(25);
      }
      Assert.True(waiting, "Activation must actually wait on the authority fence before revocation commits.");
    }
    finally { barrier.Release.TrySetResult(); }
    Assert.True((await revoking).Succeeded);
    Assert.False((await activating).Succeeded);
    var currentEpoch = await read.Users.AsNoTracking().Where(x => x.Id == admin.Actor.UserId).Select(x => x.SessionEpoch).SingleAsync();
    Assert.False((await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(read,
      admin.Actor with { SessionEpoch = currentEpoch }, request, DateTimeOffset.UtcNow)).Succeeded);
    Assert.False(await read.Microsoft365AdministrationEvents.AnyAsync(x => x.Operation == "SELECTED_ARCHIVE_RESOURCE_ACTIVATED"));
    Assert.Equal(Microsoft365RevisionStates.Verified,
      await read.Microsoft365ConnectionRevisions.Where(x => x.Id == request.ConnectionRevisionId).Select(x => x.State).SingleAsync());
  }

  private const string ControlUrl = "https://example.sharepoint.com/sites/unrelated-negative-control";

  private static async Task<ActivateArchiveResourceRequest> PrepareAsync(PgTestSchema pg, ActorContext actor, string drive)
  {
    Guid draftId, connectionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == actor.UserId);
      var now = DateTimeOffset.UtcNow;
      const string proof = "synthetic-archive-installation-proof";
      var claim = await Microsoft365OnboardingService.ClaimAsync(db, actor.FirmId, "archive-" + Guid.NewGuid().ToString("N"),
        proof, Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(proof)), now);
      Assert.True(claim.Succeeded, claim.Message);
      var bootstrap = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
        new(claim.Value!.SessionId, claim.Value.Capability, user.TenantId, user.Subject, user.Email, user.DisplayName), user.TenantId, user.Subject, now);
      Assert.True(bootstrap.Succeeded, bootstrap.Message);
      var saved = await Microsoft365OnboardingService.SaveDraftAsync(db,
        new(claim.Value.SessionId, claim.Value.Capability, 1, user.TenantId, "Synthetic archive tenant",
          "https://example.sharepoint.com/sites/archive", "archive-site", drive, "library-root", Microsoft365AccessProfiles.AppMediated,
          "NOT_CONFIGURED", "NOT_CONFIGURED"), now);
      Assert.True(saved.Succeeded, saved.Message);
      draftId = saved.Value;
      var revision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
      var prepared = await Microsoft365ConfigurationService.PrepareConnectionRevisionAsync(db, actor,
        new(draftId, revision, "slot:synthetic-login", "slot:synthetic-reader"), now);
      Assert.True(prepared.Succeeded, prepared.Message);
      connectionId = prepared.Value;
      var begin = await TenantConsentService.BeginAsync(db, actor, draftId, user.TenantId, Guid.NewGuid().ToString("D"), now);
      Assert.True(begin.Succeeded, begin.Message);
      var returned = await TenantConsentService.CompleteCallbackAsync(db, actor, begin.Value!.State, user.TenantId, true, false, now);
      Assert.True(returned.Succeeded, returned.Message);
      var challenge = await TenantConsentService.BeginIdentityVerificationAsync(db, actor, returned.Value!.AttemptId, now);
      Assert.True(challenge.Succeeded, challenge.Message);
      var microsoft = new SimulatedMicrosoftTenant(user.TenantId, [new(user.Subject, user.DisplayName, user.Email)], []);
      var consent = await TenantConsentService.CompleteIdentityVerificationAsync(db, actor, microsoft,
        challenge.Value!.State, microsoft.IssueCode(user.TenantId, user.Subject, challenge.Value.Nonce), false, now);
      Assert.True(consent.Succeeded, consent.Message);
    }
    await new SelectedSiteBoundaryVerificationService(new Factory(pg), new ExactProbe(pg), ControlUrl)
      .VerifyDraftAsync(actor, draftId, default);
    await using var verified = new AuditSphereDbContext(pg.Options);
    var draft = await verified.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
    var capability = await verified.TenantCapabilityVerifications.AsNoTracking()
      .Where(x => x.ConnectionRevisionId == connectionId && x.State == CapabilityVerificationStates.Verified)
      .OrderByDescending(x => x.ObservedAt).FirstAsync();
    return new(draftId, draft.Revision, connectionId, capability.Id, draft.ExpectedTenantId!, draft.SiteId!,
      draft.DriveId!, draft.RootFolderId!, draft.SiteUrl!, draft.AccessProfile);
  }

  private sealed class Factory(PgTestSchema pg) : IAuditSphereDbContextFactory
  {
    public Task<IAuditSphereDbContext> CreateAsync(CancellationToken ct = default) => Task.FromResult<IAuditSphereDbContext>(new AuditSphereDbContext(pg.Options));
  }

  private sealed class ExactProbe(PgTestSchema pg) : ISelectedSiteBoundaryProbe
  {
    public async Task<SelectedSiteBoundaryObservation> ProbeAsync(Guid firmId, Guid draftId, string negativeControlSiteUrl, CancellationToken ct)
    {
      Assert.Equal(ControlUrl, negativeControlSiteUrl);
      await using var db = new AuditSphereDbContext(pg.Options);
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.FirmId == firmId && x.Id == draftId, ct);
      // Only this exact in-memory positive resource exists; the unrelated control is denied.
      Assert.NotEqual(negativeControlSiteUrl, draft.SiteUrl);
      return new(draftId, draft.Revision, draft.ConnectionRevisionId!.Value, draft.ExpectedTenantId!,
        draft.SiteId!, draft.DriveId!, draft.RootFolderId!, draft.SiteUrl!);
    }
  }

  private sealed class FailedProbe : ISelectedSiteBoundaryProbe
  {
    public Task<SelectedSiteBoundaryObservation> ProbeAsync(Guid firmId, Guid draftId, string negativeControlSiteUrl, CancellationToken ct) =>
      throw new OperationBlockedException("synthetic-negative-control-not-denied", authorization: true);
  }

  private sealed class CommitBarrier : DbTransactionInterceptor
  {
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
      TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
      Entered.TrySetResult();
      await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
      return result;
    }
  }
}
