using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Client portal onboarding with real role identities: uploads stay closed until the first-sign-in requirement for the
/// identity path is satisfied, and the client's primary contact can delegate a request to a colleague of the same
/// client only, with immediate revocation and no reach into sibling clients or staff privileges.
/// </summary>
[Trait("Profile", "Database")]
public sealed class ClientPortalOnboardingTests
{
  private static StartPbcUploadRequest Upload(Guid requestId) =>
    new(requestId, "ledger.csv", "text/csv", 10, new string('a', 64));

  private static async Task<Guid> SentRequestAsync(PgTestSchema pg, PbcSeed.Fixture f, Guid clientOwner)
  {
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    Guid id;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        f.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF or CSV", "12 months",
        clientOwner, f.Staff.Id, f.Reviewer.Id, "2027-01-31", "Confidential", "Complete period."));
      Assert.True(created.Succeeded, created.Message);
      id = created.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new PbcStateChangeRequest(id, PbcStates.Sent, 1))).Succeeded);
    return id;
  }

  private static async Task<AppUser> AddClientUserAsync(PgTestSchema pg, PbcSeed.Fixture f, Guid clientId, Guid engagementId, string? email = null, bool onboarded = false)
  {
    var user = PbcSeed.User(f.FirmId, "Client");
    if (email is not null) user.Email = email;
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, user, "ClientUser", clientId, engagementId));
    if (onboarded) db.ClientPortalFirstSignIns.Add(PbcSeed.FirstSignIn(user));
    await db.SaveChangesAsync();
    return user;
  }

  [Fact]
  public async Task Uploads_AreWithheld_UntilTheFirstSignInRequirementForEachIdentityPathIsMet()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var member = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId);
    var guest = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId);
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      foreach (var (user, source) in new[] { (member, "GRAPH_PROVISIONED"), (guest, "GRAPH_INVITED") })
        seed.DirectoryUserObservations.Add(new DirectoryUserObservation
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, TenantId = "tenant-test", ObjectId = user.Subject, DisplayName = user.DisplayName,
          Mail = user.Email, EnabledState = "ENABLED", UserType = source == "GRAPH_INVITED" ? "Guest" : "Member", Source = source,
          ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
        });
      await seed.SaveChangesAsync();
    }
    var requestForMember = await SentRequestAsync(pg, f, member.Id);
    var requestForGuest = await SentRequestAsync(pg, f, guest.Id);
    var memberActor = PbcSeed.Actor(member, "ClientUser");
    var guestActor = PbcSeed.Actor(guest, "ClientUser");

    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.Equal("portal.first-sign-in.required", (await PbcService.StartUploadAsync(db, memberActor, Upload(requestForMember))).ErrorCode);
    Assert.Equal("portal.first-sign-in.required", (await PbcService.StartUploadAsync(db, guestActor, Upload(requestForGuest))).ErrorCode);

    // Provisioned member: the temporary password must first be changed at a Microsoft sign-in the application observes.
    var status = await ClientPortalService.GetFirstSignInStatusAsync(db, memberActor);
    Assert.Equal((ClientIdentityPaths.ProvisionedMember, false), (status.IdentityPath, status.CanComplete));
    Assert.Equal("portal.first-sign-in.pending", (await ClientPortalService.CompleteFirstSignInAsync(db, memberActor, true)).ErrorCode);
    await db.Users.Where(x => x.Id == member.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSignInAt, DateTimeOffset.UtcNow));
    Assert.Equal("portal.first-sign-in.acknowledgement", (await ClientPortalService.CompleteFirstSignInAsync(db, memberActor, false)).ErrorCode);
    Assert.True((await ClientPortalService.CompleteFirstSignInAsync(db, memberActor, true)).Succeeded);
    var memberRecord = await db.ClientPortalFirstSignIns.AsNoTracking().SingleAsync(x => x.UserId == member.Id);
    Assert.Equal(ClientIdentityPaths.ProvisionedMember, memberRecord.IdentityPath);
    Assert.NotNull(memberRecord.SignInObservedAt);
    Assert.True((await PbcService.StartUploadAsync(db, memberActor, Upload(requestForMember))).Succeeded);

    // External identity: the home organisation owns the password; the portal acknowledgement is recorded.
    Assert.Equal(ClientIdentityPaths.ExternalIdentity, (await ClientPortalService.GetFirstSignInStatusAsync(db, guestActor)).IdentityPath);
    Assert.True((await ClientPortalService.CompleteFirstSignInAsync(db, guestActor, true)).Succeeded);
    Assert.True((await ClientPortalService.CompleteFirstSignInAsync(db, guestActor, true)).Succeeded); // idempotent
    Assert.True((await PbcService.StartUploadAsync(db, guestActor, Upload(requestForGuest))).Succeeded);

    // Staff are not client identities and cannot record a portal sign-in; the record itself is immutable.
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalService.CompleteFirstSignInAsync(db, PbcSeed.Actor(f.Staff, "Staff"), true)).ErrorCode);
    await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM client_portal_first_sign_ins WHERE id = {memberRecord.Id}"));
  }

  [Fact]
  public async Task PrimaryContact_DelegatesWithinTheClientOnly_AndRevocationIsImmediate()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var primaryEmail = $"primary-{Guid.NewGuid():N}@example.test";
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      await seed.Users.Where(x => x.Id == f.Client.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Email, primaryEmail));
      seed.ClientContacts.Add(new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId, FullName = "Primary", Email = primaryEmail.ToUpperInvariant(), Role = "Finance director", Primary = true });
      await seed.SaveChangesAsync();
    }
    var colleague = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    var nonPrimary = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    // A client user of a sibling client, and a staff user with a client grant, are never eligible.
    var siblingClientId = Guid.NewGuid();
    var siblingEngagementId = Guid.NewGuid();
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.PracticeClients.Add(new PracticeClient { Id = siblingClientId, FirmId = f.FirmId, LegalName = "SIBLING", CreatedAt = DateTimeOffset.UtcNow });
      seed.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement { Id = siblingEngagementId, FirmId = f.FirmId, PracticeClientId = siblingClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      await seed.SaveChangesAsync();
    }
    var sibling = await AddClientUserAsync(pg, f, siblingClientId, siblingEngagementId, onboarded: true);
    var requestId = await SentRequestAsync(pg, f, f.Client.Id);
    var otherRequest = await SentRequestAsync(pg, f, nonPrimary.Id);
    var primary = PbcSeed.Actor(f.Client, "ClientUser");
    var colleagueActor = PbcSeed.Actor(colleague, "ClientUser");

    await using var db = new AuditSphereDbContext(pg.Options);
    var request = await db.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
    var candidates = await ClientPortalService.DelegateCandidatesAsync(db, primary, request);
    Assert.Contains(candidates, x => x.UserId == colleague.Id);
    Assert.DoesNotContain(candidates, x => x.UserId == sibling.Id || x.UserId == f.Staff.Id || x.UserId == f.Client.Id);
    Assert.Equal("pbc.delegation.ineligible", (await ClientPortalService.DelegateRequestAsync(db, primary, requestId, sibling.Id)).ErrorCode);
    Assert.Equal("pbc.delegation.ineligible", (await ClientPortalService.DelegateRequestAsync(db, primary, requestId, f.Staff.Id)).ErrorCode);
    // Only the primary contact who owns the request can delegate; a non-primary owner and a staff member cannot.
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalService.DelegateRequestAsync(db, PbcSeed.Actor(nonPrimary, "ClientUser"), otherRequest, colleague.Id)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalService.DelegateRequestAsync(db, PbcSeed.Actor(f.Staff, "Staff"), requestId, colleague.Id)).ErrorCode);

    // Before delegation the colleague cannot reach the request.
    Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.ReplyAsync(db, colleagueActor, requestId, "Here is the file.")).ErrorCode);
    var delegated = await ClientPortalService.DelegateRequestAsync(db, primary, requestId, colleague.Id);
    Assert.True(delegated.Succeeded, delegated.Message);
    Assert.Equal(delegated.Value, (await ClientPortalService.DelegateRequestAsync(db, primary, requestId, colleague.Id)).Value);
    Assert.Contains(await ClientPortalService.ParticipantRequests(db, colleagueActor).Select(x => x.Id).ToListAsync(), x => x == requestId);
    Assert.DoesNotContain(await ClientPortalService.ParticipantRequests(db, colleagueActor).Select(x => x.Id).ToListAsync(), x => x == otherRequest);
    Assert.True((await PbcService.ReplyAsync(db, colleagueActor, requestId, "Here is the file.")).Succeeded);
    Assert.True((await PbcService.StartUploadAsync(db, colleagueActor, Upload(requestId))).Succeeded);
    // The delegate cannot re-delegate.
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalService.DelegateRequestAsync(db, colleagueActor, requestId, nonPrimary.Id)).ErrorCode);

    // Revocation takes effect on the next command.
    Assert.True((await ClientPortalService.RevokeDelegationAsync(db, primary, delegated.Value)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.ReplyAsync(db, colleagueActor, requestId, "Another file.")).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.StartUploadAsync(db, colleagueActor, Upload(requestId))).ErrorCode);
    Assert.Empty(await ClientPortalService.ParticipantRequests(db, colleagueActor).ToListAsync());
  }
}
