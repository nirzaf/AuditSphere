using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using static AuditSphereOps.Domain.Tests.SupportedArchiveSourceFixture;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private static Task<SupportedArchiveSource> PrepareSupportedArchiveSourceAsync(PgTestSchema pg, World w, byte[] bytes)
    => PrepareAsync(pg, new(w.FirmId, w.ClientId, w.EngagementId, w.U["senior"], w.U["manager"], w.U["client"], w.U["administrator"]),
      w.A("administrator", "Administrator"), w.A("senior", "Senior"), w.A("client", "ClientUser"), bytes);

  private sealed record SupportedSecondaryArchive(Guid DraftId, long DraftRevision, Guid VerificationId, Guid ConnectionId,
    string SiteUrl, string SiteId, string DriveId, string RootId);

  private static async Task<SupportedSecondaryArchive> PrepareSupportedSecondaryArchiveAsync(PgTestSchema pg, World w)
  {
    var admin = w.A("administrator", "Administrator");
    var user = w.U["administrator"];
    const string siteUrl = "https://example.sharepoint.com/sites/secondary-archive-copy-test";
    var siteId = $"example.sharepoint.com,{Guid.NewGuid():D},{Guid.NewGuid():D}";
    const string driveId = "separate-secondary-archive-drive", rootId = "separate-secondary-archive-root";
    const string syntheticProof = "Synthetic isolated secondary archive installation proof";
    Guid draftId, connectionId;
    string workingBefore;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      workingBefore = JsonSerializer.Serialize(await db.FirmWorkspaceConfigurations.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId).OrderBy(x => x.Id).ToArrayAsync());
      var claimed = await Microsoft365OnboardingService.ClaimAsync(db, w.FirmId,
        "synthetic-secondary-archive-" + Guid.NewGuid().ToString("N"), syntheticProof,
        Hashing.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(syntheticProof)), DateTimeOffset.UtcNow);
      Assert.True(claimed.Succeeded, claimed.Message);
      var bound = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
        new(claimed.Value!.SessionId, claimed.Value.Capability, user.TenantId, user.Subject, user.Email, user.DisplayName),
        user.TenantId, user.Subject, DateTimeOffset.UtcNow);
      Assert.True(bound.Succeeded, bound.Message);
      Assert.Equal(admin.UserId, bound.Value);
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.SetupSessionId == claimed.Value.SessionId);
      var saved = await Microsoft365OnboardingService.SaveDraftAsync(db,
        new(claimed.Value.SessionId, claimed.Value.Capability, draft.Revision, user.TenantId, "Synthetic archive tenant",
          siteUrl, siteId, driveId, rootId, Microsoft365AccessProfiles.AppMediated, "NOT_CONFIGURED", "NOT_CONFIGURED"), DateTimeOffset.UtcNow);
      Assert.True(saved.Succeeded, saved.Message);
      draftId = saved.Value;
      var revision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
      var prepared = await Microsoft365ConfigurationService.PrepareConnectionRevisionAsync(db, admin,
        new(draftId, revision, "slot:test-archive-login", "slot:test-archive-runtime"), DateTimeOffset.UtcNow);
      Assert.True(prepared.Succeeded, prepared.Message);
      connectionId = prepared.Value;
      var consent = await TenantConsentService.BeginAsync(db, admin, draftId, user.TenantId, Guid.NewGuid().ToString("D"), DateTimeOffset.UtcNow);
      Assert.True(consent.Succeeded, consent.Message);
      var callback = await TenantConsentService.CompleteCallbackAsync(db, admin, consent.Value!.State, user.TenantId, true, false, DateTimeOffset.UtcNow);
      Assert.True(callback.Succeeded, callback.Message);
      var challenge = await TenantConsentService.BeginIdentityVerificationAsync(db, admin, callback.Value!.AttemptId, DateTimeOffset.UtcNow);
      Assert.True(challenge.Succeeded, challenge.Message);
      var verified = await TenantConsentService.CompleteIdentityVerificationAsync(db, admin,
        new ArchiveConsentVerifier(user.TenantId, user.Subject, challenge.Value!.Nonce, challenge.Value.State),
        challenge.Value.State, "synthetic-one-use-code", false, DateTimeOffset.UtcNow);
      Assert.True(verified.Succeeded, verified.Message);
    }
    long verifiedRevision;
    await using (var db = new AuditSphereDbContext(pg.Options))
      verifiedRevision = await db.Microsoft365SetupDrafts.Where(x => x.Id == draftId).Select(x => x.Revision).SingleAsync();
    await new SelectedSiteBoundaryVerificationService(new ArchiveCopyTestDbContextFactory(pg.Options),
      new ArchiveSelectedSiteProbe(w.FirmId, new(draftId, verifiedRevision, connectionId, user.TenantId, siteId, driveId, rootId, siteUrl)),
      "https://example.sharepoint.com/sites/synthetic-negative-control").VerifyDraftAsync(admin, draftId, default, verifiedRevision);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var draft = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
      var proof = await db.TenantCapabilityVerifications.AsNoTracking().SingleAsync(x =>
        x.ConnectionRevisionId == connectionId && x.Capability == Microsoft365Capabilities.SelectedSite &&
        x.State == CapabilityVerificationStates.Verified && x.DiagnosticCode == "exact-resource-and-control-denial");
      var activated = await Microsoft365ConfigurationService.ActivateArchiveResourceAsync(db, admin,
        new(draftId, draft.Revision, connectionId, proof.Id, user.TenantId, siteId, driveId, rootId, siteUrl,
          Microsoft365AccessProfiles.AppMediated), DateTimeOffset.UtcNow);
      Assert.True(activated.Succeeded, $"{activated.ErrorCode}: {activated.Message}");
      Assert.Equal(connectionId, activated.Value);
      Assert.Equal(workingBefore, JsonSerializer.Serialize(await db.FirmWorkspaceConfigurations.AsNoTracking()
        .Where(x => x.FirmId == w.FirmId).OrderBy(x => x.Id).ToArrayAsync()));
      Assert.False(await db.ClientWorkspaces.AnyAsync(x => x.FirmId == w.FirmId && x.DriveId == driveId));
      Assert.False(await db.RepositoryBindings.AnyAsync(x => x.FirmId == w.FirmId && x.DriveId == driveId));
      var active = await db.Microsoft365SetupDrafts.AsNoTracking().SingleAsync(x => x.Id == draftId);
      Assert.Equal(Microsoft365RevisionStates.Active, active.State);
      Assert.Equal(draft.Revision + 1, active.Revision);
      return new(draftId, active.Revision, proof.Id, connectionId, siteUrl, siteId, driveId, rootId);
    }
  }

  private static async Task<(Guid ClientId, Guid EngagementId)> CreateSupportedArchiveSiblingAsync(PgTestSchema pg, World w, ActorContext partner)
  {
    var admin = w.A("administrator", "Administrator");
    var relationship = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, (w.FirmId, admin), "RelationshipManager");
    var manager = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(pg, (w.FirmId, admin), "Manager");
    var proposal = await SupportedAuditCompletionWorldBuilder.CreateAcceptedProposalAsync(pg, relationship.Actor, partner,
      manager.Actor, "archive-sibling", "2027-01-01", "2027-12-31", _ => Task.CompletedTask);
    await using var db = new AuditSphereDbContext(pg.Options);
    var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, relationship.Actor,
      new(proposal, "Synthetic second archive-copy client", "Archive sibling"));
    Assert.True(converted.Succeeded, converted.Message);
    var state = await EngagementCreationWorkspace.StateAsync(db, partner, converted.Value);
    Assert.True(state.Succeeded, state.Message);
    var request = new EngagementCreationRequest(Guid.NewGuid(), state.Value!.ReviewBasis, new("Audit", "Audit", "2027-01-01", "2027-12-31"));
    var preview = await EngagementCreationWorkspace.PreviewAsync(db, partner, converted.Value, request);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await EngagementCreationWorkspace.ExecuteAsync(db, partner, converted.Value,
      request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
    Assert.True(created.Succeeded, created.Message);
    var staff = await RoleAdministrationService.EnsureUserAsync(db, admin,
      new(w.U["administrator"].TenantId, Guid.NewGuid().ToString("D"), $"archive-sibling-{Guid.NewGuid():N}@example.test", "Synthetic sibling staff", "Staff", "APPROVED_ROSTER"));
    Assert.True(staff.Succeeded, staff.Message);
    var assigned = await StaffingService.AssignAsync(db, partner, new(created.Value!.EngagementId, staff.Value, StaffingLevels.StaffAssociate));
    Assert.True(assigned.Succeeded, assigned.Message);
    return (converted.Value, created.Value.EngagementId);
  }
}
