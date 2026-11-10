using System.Security.Cryptography;
using System.Text;
using System.Data;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FirmFinanceProfileServiceTests
{
  [Fact]
  public async Task SupportedMakerCheckerRecoversApprovalAndPreservesImmutableEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var admin = await BootstrapAdminAsync(pg);
    var maker = await ProvisionRoleAsync(pg, admin, "FinanceManager");
    var staleMaker = await ProvisionRoleAsync(pg, admin, "FinanceManager");
    var checker = await ProvisionRoleAsync(pg, admin, "FinanceReviewer");
    var secondChecker = await ProvisionRoleAsync(pg, admin, "FinanceReviewer");
    var foreignAdmin = await BootstrapAdminAsync(pg);
    var foreignChecker = await ProvisionRoleAsync(pg, foreignAdmin, "FinanceReviewer");

    // A caller's old RepeatableRead snapshot cannot authorize a profile command after
    // the supported revocation command advances the target session and removes the grant.
    await using (var oldSnapshot = new AuditSphereDbContext(pg.Options))
    {
      await using var snapshot = await oldSnapshot.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
      _ = await oldSnapshot.RoleGrants.AsNoTracking().CountAsync(x => x.UserId == staleMaker.User.Id);
      await using (var revoke = new AuditSphereDbContext(pg.Options))
      {
        var grantId = await revoke.RoleGrants.AsNoTracking().Where(x => x.UserId == staleMaker.User.Id &&
          x.Role == "FinanceManager" && x.RevokedAt == null).Select(x => x.Id).SingleAsync();
        var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(revoke, admin.Actor,
          new RevokeRoleGrantRequest(grantId, Reason: "Synthetic finance-profile authorization regression."));
        Assert.True(revoked.Succeeded, revoked.Message);
      }
      var rejectedSnapshot = await FirmFinanceProfileService.CreateDraftAsync(oldSnapshot, staleMaker.Actor,
        new("USD", BillingStates.TestProfile));
      Assert.Equal("finance.profile-transaction-unsupported", rejectedSnapshot.ErrorCode);
    }
    await using (var stale = new AuditSphereDbContext(pg.Options))
    {
      var rejected = await FirmFinanceProfileService.CreateDraftAsync(stale, staleMaker.Actor,
        new("USD", BillingStates.TestProfile));
      Assert.False(rejected.Succeeded);
    }

    FirmFinanceProfileDraft draft;
    await using (var create = new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await FirmFinanceProfileService.CreateDraftAsync(create, maker.Actor,
        new("US1", BillingStates.TestProfile))).Succeeded);
      Assert.False((await FirmFinanceProfileService.CreateDraftAsync(create, maker.Actor,
        new("USD", "LIVE"))).Succeeded);

      var created = await FirmFinanceProfileService.CreateDraftAsync(create, maker.Actor,
        new("qar", BillingStates.TestProfile));
      Assert.True(created.Succeeded, created.Message);
      draft = created.Value!;
      Assert.Equal("QAR", draft.FunctionalCurrency);
      Assert.Equal(1, draft.Revision);

      // A retry after a lost create response conflicts safely; the scoped read recovers
      // the one existing firm profile so the caller can compare the submitted basis.
      Assert.Equal("finance.profile-conflict", (await FirmFinanceProfileService.CreateDraftAsync(create,
        maker.Actor, new("QAR", BillingStates.TestProfile))).ErrorCode);
      var recovered = await FirmFinanceProfileService.ReadAsync(create, checker.Actor);
      Assert.True(recovered.Succeeded, recovered.Message);
      Assert.Equal(draft.Id, recovered.Value!.Id);
      Assert.Equal(draft.DraftBasisSha256, recovered.Value.DraftBasisSha256);
      Assert.False(recovered.Value.Approved);
      var foreignRead = await FirmFinanceProfileService.ReadAsync(create, foreignChecker.Actor);
      Assert.False(foreignRead.Succeeded);
      Assert.Null(foreignRead.Value);

      Assert.False((await FirmFinanceProfileService.ApproveDraftAsync(create, maker.Actor,
        new(draft.Id, draft.Revision, draft.DraftBasisSha256))).Succeeded);
      Assert.False((await FirmFinanceProfileService.ApproveDraftAsync(create, checker.Actor,
        new(draft.Id, draft.Revision, new string('0', 64)))).Succeeded);
      Assert.False((await FirmFinanceProfileService.ApproveDraftAsync(create, foreignChecker.Actor,
        new(draft.Id, draft.Revision, draft.DraftBasisSha256))).Succeeded);
    }

    // Keep a stale tracked draft in one context while another context commits the review.
    // The repeated exact command simulates recovery after the first response was lost.
    await using var replay = new AuditSphereDbContext(pg.Options);
    _ = await replay.FirmFinanceProfiles.SingleAsync(x => x.Id == draft.Id);
    FirmFinanceProfileApproval approval;
    await using (var review = new AuditSphereDbContext(pg.Options))
    {
      var approved = await FirmFinanceProfileService.ApproveDraftAsync(review, checker.Actor,
        new(draft.Id, draft.Revision, draft.DraftBasisSha256));
      Assert.True(approved.Succeeded, approved.Message);
      approval = approved.Value!;
    }
    var repeated = await FirmFinanceProfileService.ApproveDraftAsync(replay, checker.Actor,
      new(draft.Id, draft.Revision, draft.DraftBasisSha256));
    Assert.True(repeated.Succeeded, repeated.Message);
    Assert.Equal(approval, repeated.Value);
    await using (var otherReview = new AuditSphereDbContext(pg.Options))
      Assert.Equal("finance.profile-already-approved", (await FirmFinanceProfileService.ApproveDraftAsync(
        otherReview, secondChecker.Actor, new(draft.Id, draft.Revision, draft.DraftBasisSha256))).ErrorCode);

    await using (var mutation = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<PostgresException>(() => mutation.FirmFinanceProfiles
        .Where(x => x.FirmId == admin.FirmId).ExecuteUpdateAsync(setters => setters
          .SetProperty(x => x.FunctionalCurrency, "USD")));
    await using (var deletion = new AuditSphereDbContext(pg.Options))
      await Assert.ThrowsAsync<PostgresException>(() => deletion.FirmFinanceProfiles
        .Where(x => x.FirmId == admin.FirmId).ExecuteDeleteAsync());

    await using (var migration = new AuditSphereDbContext(pg.Options))
    {
      var migrator = migration.GetService<IMigrator>();
      await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync("20261110093821_R02ReviewedBasisIdentity"));
    }

    await using var inspect = new AuditSphereDbContext(pg.Options);
    var row = await inspect.FirmFinanceProfiles.AsNoTracking().SingleAsync(x => x.FirmId == admin.FirmId);
    Assert.True(row.Approved);
    Assert.Equal("QAR", row.FunctionalCurrency);
    Assert.Equal(maker.User.Id, row.CreatedByUserId);
    Assert.Equal(checker.User.Id, row.ApprovedByUserId);
    Assert.Equal(draft.DraftBasisSha256, row.ApprovedDraftBasisSha256);
    Assert.Equal(approval, new FirmFinanceProfileApproval(row.Id, row.FunctionalCurrency, row.ProfileKind,
      row.ApprovedDraftRevision!.Value, row.ApprovedDraftBasisSha256!, row.ApprovedByUserId!.Value, row.ApprovedAt!.Value));
  }

  private static async Task<(Guid FirmId, ActorContext Actor)> BootstrapAdminAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var tenant = Guid.NewGuid().ToString("D");
    var objectId = Guid.NewGuid().ToString("D");
    var now = DateTimeOffset.UtcNow;
    const string proof = "synthetic-finance-profile-installation-proof";
    await using var db = new AuditSphereDbContext(pg.Options);
    var claim = await Microsoft365OnboardingService.ClaimAsync(db, firmId,
      "finance-profile-install-" + Guid.NewGuid().ToString("N"), proof, Hash(proof), now);
    Assert.True(claim.Succeeded, claim.Message);
    var bootstrapped = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
      new(claim.Value!.SessionId, claim.Value.Capability, tenant, objectId,
        "admin-" + Guid.NewGuid().ToString("N") + "@example.test", "Synthetic setup administrator"), tenant, objectId, now);
    Assert.True(bootstrapped.Succeeded, bootstrapped.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == bootstrapped.Value);
    return (firmId, new(user.Id, firmId, user.SessionEpoch, ["Administrator"]));
  }

  private static async Task<(AppUser User, ActorContext Actor)> ProvisionRoleAsync(
    PgTestSchema pg, (Guid FirmId, ActorContext Actor) admin, string role)
  {
    var tenant = (await ReadUserAsync(pg, admin.Actor.UserId)).TenantId;
    var email = role.ToLowerInvariant() + "-" + Guid.NewGuid().ToString("N") + "@example.test";
    await using var db = new AuditSphereDbContext(pg.Options);
    var ensured = await RoleAdministrationService.EnsureUserAsync(db, admin.Actor,
      new(tenant, Guid.NewGuid().ToString("D"), email, role + " test identity", "Staff", "APPROVED_ROSTER"));
    Assert.True(ensured.Succeeded, ensured.Message);
    var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, admin.Actor,
      new(ensured.Value, role, "FIRM_WIDE"));
    Assert.True(grant.Succeeded, grant.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ensured.Value);
    return (user, new(user.Id, user.FirmId, user.SessionEpoch, [role]));
  }

  private static async Task<AppUser> ReadUserAsync(PgTestSchema pg, Guid id)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
  }

  private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
