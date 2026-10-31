using System.Data.Common;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

  private static async Task<Guid> SharedReviewAsync(PgTestSchema pg, PbcSeed.Fixture f)
  {
    var deliverableId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    var content = new byte[] { 1 };
    db.AuditDeliverables.Add(new AuditDeliverable
    {
      Id = deliverableId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Kind = "INDEPENDENT_AUDITORS_REPORT", InputDigest = new string('a', 64), Content = content,
      ContentSha256 = Hashing.Sha256Hex(content), TemplateVersion = "SYNTHETIC", FileName = "synthetic.pdf",
      ContentType = "application/pdf", CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var shared = await AuditDeliverableService.ShareWithClientAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), deliverableId);
    Assert.True(shared.Succeeded, shared.Message);
    return shared.Value;
  }

  [Theory]
  [InlineData("reply")]
  [InlineData("start-upload")]
  [InlineData("resume-upload")]
  [InlineData("record-chunk")]
  [InlineData("acknowledge")]
  public async Task PbcClientCommands_ReauthorizeTheLockedRequestAfterItsOwnerChanges(string operation)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var client = PbcSeed.Actor(f.Client, "ClientUser");
    var requestId = await SentRequestAsync(pg, f, f.Client.Id);
    Guid uploadIntentId = Guid.Empty;
    string? capability = null;

    if (operation is "resume-upload" or "record-chunk")
    {
      await using var startDb = new AuditSphereDbContext(pg.Options);
      var started = await PbcService.StartUploadAsync(startDb, client, Upload(requestId));
      Assert.True(started.Succeeded, started.ErrorCode);
      uploadIntentId = started.Value!.UploadIntentId;
      capability = started.Value.Capability;
    }

    var replacementOwner = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    var requestChangeRows = 0;
    PbcOwnerRaceSnapshot? postFixtureBaseline = null;
    var interceptor = new OwnerChangeAfterPbcRequestRead(async ct =>
    {
      await using (var mutation = new AuditSphereDbContext(pg.Options))
      {
        requestChangeRows = await mutation.PbcRequests.Where(x =>
            x.FirmId == f.FirmId && x.ClientId == f.ClientId && x.EngagementId == f.EngagementId && x.Id == requestId)
          .ExecuteUpdateAsync(update => update
            .SetProperty(x => x.ClientOwnerUserId, replacementOwner.Id)
            .SetProperty(x => x.Revision, x => x.Revision + 1)
            .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), ct);
      }

      await using var snapshotDb = new AuditSphereDbContext(pg.Options);
      postFixtureBaseline = await ReadOwnerRaceSnapshotAsync(snapshotDb, f.FirmId, f.ClientId, f.EngagementId,
        requestId, ct);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(interceptor).Options;
    using var operationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    await using var db = new AuditSphereDbContext(options);
    var trackedRequest = await db.PbcRequests.SingleAsync(x => x.FirmId == f.FirmId && x.Id == requestId,
      operationTimeout.Token);
    var preRaceRevision = trackedRequest.Revision;
    Assert.False(await db.PbcRequestDelegations.AsNoTracking().AnyAsync(x =>
      x.FirmId == f.FirmId && x.PbcRequestId == requestId && x.DelegateUserId == f.Client.Id && x.RevokedAt == null,
      operationTimeout.Token));

    interceptor.Arm();
    var errorCode = operation switch
    {
      "reply" => (await PbcService.ReplyAsync(db, client, requestId, "Owner-race reply.", operationTimeout.Token)).ErrorCode,
      "start-upload" => (await PbcService.StartUploadAsync(db, client, Upload(requestId), operationTimeout.Token)).ErrorCode,
      "resume-upload" => (await PbcService.ResumeUploadAsync(db, client,
        new ResumePbcUploadRequest(requestId, uploadIntentId, "ledger.csv", 10, new string('a', 64)),
        operationTimeout.Token)).ErrorCode,
      "record-chunk" => (await PbcService.RecordChunkAsync(db, client,
        new RecordPbcUploadChunkRequest(uploadIntentId, 0, 0, 10, Hashing.Sha256Hex(new byte[10]),
          capability!, "owner-race.part"), operationTimeout.Token)).ErrorCode,
      "acknowledge" => (await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(requestId, PbcStates.Acknowledged, preRaceRevision), operationTimeout.Token)).ErrorCode,
      _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown PBC owner-race operation.")
    };

    Assert.True(interceptor.OwnerChangeApplied,
      "The owner change must commit after the initial non-locking PBC request read.");
    Assert.True(interceptor.LockedRequestReadObserved,
      "The command must read the PBC request under its row lock before denying the former owner.");
    Assert.Equal(1, requestChangeRows);
    Assert.Equal(ErrorCodes.ScopeDenied, errorCode);

    var expected = Assert.IsType<PbcOwnerRaceSnapshot>(postFixtureBaseline);
    Assert.Equal(replacementOwner.Id, expected.Request.ClientOwnerUserId);
    Assert.Equal(preRaceRevision + 1, expected.Request.Revision);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var actual = await ReadOwnerRaceSnapshotAsync(verify, f.FirmId, f.ClientId, f.EngagementId,
      requestId, operationTimeout.Token);
    Assert.Equal(expected.Request, actual.Request);
    Assert.Equal(expected.Communications, actual.Communications);
    Assert.Equal(expected.UploadIntents, actual.UploadIntents);
    Assert.Equal(expected.Chunks, actual.Chunks);
  }

  private static async Task<PbcOwnerRaceSnapshot> ReadOwnerRaceSnapshotAsync(
    AuditSphereDbContext db, Guid firmId, Guid clientId, Guid engagementId, Guid requestId, CancellationToken ct)
  {
    var request = await db.PbcRequests.AsNoTracking().Where(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.Id == requestId)
      .Select(x => new PbcOwnerRaceRequestSnapshot(x.ClientOwnerUserId, x.State, x.Revision, x.UpdatedAt,
        x.ClarificationReason, x.AcceptedByUserId, x.AcceptedAt))
      .SingleAsync(ct);
    var communications = await db.PbcCommunications.AsNoTracking().Where(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.PbcRequestId == requestId)
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
      .Select(x => new PbcOwnerRaceCommunicationSnapshot(x.Id, x.AuthorUserId, x.Kind, x.Body, x.RecipientEmail,
        x.Subject, x.PortalUrl, x.DeliveryState, x.ProviderCorrelationId, x.DeliveredAt, x.CreatedAt))
      .ToArrayAsync(ct);
    var uploadIntents = await db.PbcUploadIntents.AsNoTracking().Where(x =>
        x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId && x.PbcRequestId == requestId)
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
      .Select(x => new PbcOwnerRaceIntentSnapshot(x.Id, x.RequestRevision, x.UploaderUserId, x.FileName,
        x.ContentType, x.DeclaredByteCount, x.DeclaredSha256Hex, x.CapabilityHash, x.ReceivedByteCount,
        x.State, x.ExpiresAt, x.CreatedAt, x.CompletedAt, x.FinalSha256Hex, x.FailureReason,
        x.TransferOperationId, x.ProviderReceiptDigest, x.ProviderRegisteredAt, x.Revision))
      .ToArrayAsync(ct);
    var uploadIntentIds = uploadIntents.Select(x => x.Id).ToArray();
    PbcOwnerRaceChunkSnapshot[] chunks;
    if (uploadIntentIds.Length == 0)
      chunks = [];
    else
      chunks = await db.PbcUploadChunks.AsNoTracking().Where(x =>
          x.FirmId == firmId && x.ClientId == clientId && x.EngagementId == engagementId &&
          uploadIntentIds.Contains(x.PbcUploadIntentId))
        .OrderBy(x => x.PbcUploadIntentId).ThenBy(x => x.ChunkIndex).ThenBy(x => x.Id)
        .Select(x => new PbcOwnerRaceChunkSnapshot(x.Id, x.PbcUploadIntentId, x.ChunkIndex, x.Offset,
          x.ByteCount, x.Sha256Hex, x.StagedPath, x.ReceivedAt))
        .ToArrayAsync(ct);
    return new PbcOwnerRaceSnapshot(request, communications, uploadIntents, chunks);
  }

  private sealed record PbcOwnerRaceRequestSnapshot(Guid ClientOwnerUserId, string State, long Revision,
    DateTimeOffset UpdatedAt, string? ClarificationReason, Guid? AcceptedByUserId, DateTimeOffset? AcceptedAt);

  private sealed record PbcOwnerRaceCommunicationSnapshot(Guid Id, Guid AuthorUserId, string Kind, string Body,
    string? RecipientEmail, string? Subject, string? PortalUrl, string? DeliveryState,
    string? ProviderCorrelationId, DateTimeOffset? DeliveredAt, DateTimeOffset CreatedAt);

  private sealed record PbcOwnerRaceIntentSnapshot(Guid Id, long RequestRevision, Guid UploaderUserId,
    string FileName, string ContentType, long DeclaredByteCount, string DeclaredSha256Hex, string CapabilityHash,
    long ReceivedByteCount, string State, DateTimeOffset ExpiresAt, DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt, string? FinalSha256Hex, string? FailureReason, Guid? TransferOperationId,
    string? ProviderReceiptDigest, DateTimeOffset? ProviderRegisteredAt, long Revision);

  private sealed record PbcOwnerRaceChunkSnapshot(Guid Id, Guid PbcUploadIntentId, int ChunkIndex, long Offset,
    int ByteCount, string Sha256Hex, string? StagedPath, DateTimeOffset ReceivedAt);

  private sealed record PbcOwnerRaceSnapshot(PbcOwnerRaceRequestSnapshot Request,
    PbcOwnerRaceCommunicationSnapshot[] Communications, PbcOwnerRaceIntentSnapshot[] UploadIntents,
    PbcOwnerRaceChunkSnapshot[] Chunks);

  private sealed class OwnerChangeAfterPbcRequestRead(Func<CancellationToken, Task> changeOwner)
    : DbCommandInterceptor
  {
    private int armed;
    private int ownerChangeApplied;
    private int lockedRequestReadObserved;
    public bool OwnerChangeApplied => Volatile.Read(ref ownerChangeApplied) == 1;
    public bool LockedRequestReadObserved => Volatile.Read(ref lockedRequestReadObserved) == 1;
    public void Arm() => Volatile.Write(ref armed, 1);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      var sql = command.CommandText;
      if (Volatile.Read(ref armed) != 1 || !sql.Contains("pbc_requests", StringComparison.OrdinalIgnoreCase))
        return result;
      if (sql.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
      {
        Interlocked.Exchange(ref lockedRequestReadObserved, 1);
        return result;
      }
      if (!result.HasRows || Interlocked.CompareExchange(ref ownerChangeApplied, 1, 0) != 0)
        return result;

      using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      bounded.CancelAfter(TimeSpan.FromSeconds(15));
      await changeOwner(bounded.Token);
      return result;
    }
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
  public async Task ConcurrentFirstSignInAcknowledgements_RecoverOnlyThePersistedUserRecord()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var client = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId);
    var actor = PbcSeed.Actor(client, "ClientUser");
    var barrier = new ConcurrentFirstSignInSaveBarrier();
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(barrier).Options;

    async Task<CommandResult> AcknowledgeAsync()
    {
      await using var db = new AuditSphereDbContext(options);
      return await ClientPortalService.CompleteFirstSignInAsync(db, actor, acknowledged: true);
    }

    var results = await Task.WhenAll(AcknowledgeAsync(), AcknowledgeAsync());
    Assert.All(results, result => Assert.True(result.Succeeded, result.Message));
    Assert.Equal(2, barrier.Arrivals);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Single(await verify.ClientPortalFirstSignIns.AsNoTracking()
      .Where(x => x.FirmId == f.FirmId && x.UserId == client.Id).ToListAsync());
  }

  [Fact]
  public async Task NonDuplicatePersistenceFailure_DoesNotReportSuccess_AndUploadGateRemainsClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var client = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId);
    var actor = PbcSeed.Actor(client, "ClientUser");
    var failingInterceptor = new FailingSaveInterceptor();
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(failingInterceptor).Options;

    await using (var db = new AuditSphereDbContext(options))
    {
      await Assert.ThrowsAsync<DbUpdateException>(() =>
        ClientPortalService.CompleteFirstSignInAsync(db, actor, acknowledged: true));
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.False(await verify.ClientPortalFirstSignIns.AsNoTracking().AnyAsync(x => x.FirmId == f.FirmId && x.UserId == client.Id));
    Assert.Equal("portal.first-sign-in.required", (await ClientPortalService.RequireFirstSignInAsync(verify, actor, default)).ErrorCode);
  }

  [Fact]
  public async Task ClientPortalWrites_RequireFirstSignInAndActiveEngagement_WhileStaffUploadsRemainAvailable()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var clientUser = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId);
    var requestId = await SentRequestAsync(pg, f, clientUser.Id);
    var staffUploadRequestId = await SentRequestAsync(pg, f, clientUser.Id);
    var reviewId = await SharedReviewAsync(pg, f);
    var client = PbcSeed.Actor(clientUser, "ClientUser");
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var commentRequestId = Guid.NewGuid();

    await using var db = new AuditSphereDbContext(pg.Options);
    var pending = await ClientPortalWorkspaceQuery.RequestAsync(db, client, requestId);
    Assert.True(pending.Succeeded, pending.Message);
    Assert.False(pending.Value!.CanWrite);
    Assert.Equal("portal.first-sign-in.required", (await PbcService.ReplyAsync(db, client, requestId, "Before sign-in")).ErrorCode);
    Assert.Equal("portal.first-sign-in.required",
      (await ClientPortalReviewCommands.CommentAsync(db, client, reviewId, commentRequestId, "Before sign-in")).ErrorCode);
    Assert.Equal("portal.first-sign-in.required",
      (await PbcService.ChangeStateAsync(db, client, new PbcStateChangeRequest(requestId, PbcStates.Acknowledged, 1))).ErrorCode);
    Assert.False(await db.PbcCommunications.AnyAsync(x => x.PbcRequestId == requestId && x.Body == "Before sign-in"));
    Assert.False(await db.ClientDeliverableComments.AnyAsync(x => x.RequestId == commentRequestId));

    // Staff upload authorization remains independent of the client's first-sign-in gate.
    var staffUpload = await PbcService.StartUploadAsync(db, staff, Upload(staffUploadRequestId));
    Assert.True(staffUpload.Succeeded, staffUpload.Message);

    Assert.True((await ClientPortalService.CompleteFirstSignInAsync(db, client, true)).Succeeded);
    var eligible = await ClientPortalWorkspaceQuery.RequestAsync(db, client, requestId);
    Assert.True(eligible.Succeeded, eligible.Message);
    Assert.True(eligible.Value!.CanWrite);
    Assert.True((await PbcService.ChangeStateAsync(db, client, new PbcStateChangeRequest(requestId, PbcStates.Acknowledged, 2))).Succeeded);
    Assert.True((await PbcService.ReplyAsync(db, client, requestId, "After sign-in")).Succeeded);
    Assert.True((await ClientPortalReviewCommands.CommentAsync(db, client, reviewId, commentRequestId, "After sign-in")).Succeeded);
    Assert.True((await PbcService.StartUploadAsync(db, client, Upload(requestId))).Succeeded);

    await db.Engagements.Where(x => x.FirmId == f.FirmId && x.Id == f.EngagementId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, "Completed"));
    var completed = await ClientPortalWorkspaceQuery.RequestAsync(db, client, requestId);
    Assert.True(completed.Succeeded, completed.Message);
    Assert.False(completed.Value!.CanWrite);
    Assert.Equal("pbc.engagement-inactive", (await PbcService.ReplyAsync(db, client, requestId, "After completion")).ErrorCode);
    Assert.Equal("pbc.engagement-inactive",
      (await ClientPortalReviewCommands.CommentAsync(db, client, reviewId, Guid.NewGuid(), "After completion")).ErrorCode);
    Assert.Equal("pbc.engagement-inactive",
      (await PbcService.ChangeStateAsync(db, client, new PbcStateChangeRequest(requestId, PbcStates.Resubmitted, 4))).ErrorCode);
    Assert.Equal("pbc.engagement-inactive", (await PbcService.StartUploadAsync(db, client, Upload(requestId))).ErrorCode);
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
    // The delegate cannot re-delegate.
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalService.DelegateRequestAsync(db, colleagueActor, requestId, nonPrimary.Id)).ErrorCode);

    // An upload already past authorization commits before revocation can acquire the shared client fence.
    var uploadReceipt = await UploadCommitsBeforeDelegationRevocationAsync(pg, f, primary, colleagueActor, requestId, delegated.Value);
    await using (var afterFirstRevocation = new AuditSphereDbContext(pg.Options))
    {
      var retained = await afterFirstRevocation.PbcRequestDelegations.AsNoTracking()
        .SingleAsync(x => x.Id == delegated.Value);
      Assert.Null((await db.PbcRequestDelegations.SingleAsync(x => x.Id == delegated.Value)).RevokedAt);
      Assert.True((await ClientPortalService.RevokeDelegationAsync(db, primary, delegated.Value)).Succeeded);
      var refreshed = await afterFirstRevocation.PbcRequestDelegations.AsNoTracking()
        .SingleAsync(x => x.Id == delegated.Value);
      Assert.Equal(retained.RevokedAt, refreshed.RevokedAt);
      Assert.Equal(retained.RevokedByUserId, refreshed.RevokedByUserId);
      Assert.Equal(retained.RevokedAt, (await db.PbcRequestDelegations.SingleAsync(x => x.Id == delegated.Value)).RevokedAt);

      var capability = Assert.IsType<string>(uploadReceipt.Capability);
      Assert.Equal(64, capability.Length);
      Assert.True(capability.All(Uri.IsHexDigit));
      var intentBeforeDeniedCommands = await afterFirstRevocation.PbcUploadIntents.AsNoTracking()
        .Where(x => x.Id == uploadReceipt.UploadIntentId)
        .Select(x => new { x.FileName, x.DeclaredByteCount, x.DeclaredSha256Hex, x.Revision, x.RequestRevision,
          x.State, x.ReceivedByteCount, x.CapabilityHash })
        .SingleAsync();
      Assert.Equal("ledger.csv", intentBeforeDeniedCommands.FileName);
      Assert.Equal(10L, intentBeforeDeniedCommands.DeclaredByteCount);
      Assert.Equal(new string('a', 64), intentBeforeDeniedCommands.DeclaredSha256Hex);
      Assert.Equal(Hashing.Sha256Hex(capability), intentBeforeDeniedCommands.CapabilityHash);
      var requestRevisionBeforeDeniedCommands = await afterFirstRevocation.PbcRequests.AsNoTracking()
        .Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync();
      var chunksBeforeDeniedCommands = await afterFirstRevocation.PbcUploadChunks.AsNoTracking()
        .Where(x => x.PbcUploadIntentId == uploadReceipt.UploadIntentId).OrderBy(x => x.ChunkIndex)
        .Select(x => new { x.Id, x.ChunkIndex, x.Offset, x.ByteCount, x.Sha256Hex }).ToListAsync();
      Assert.Empty(chunksBeforeDeniedCommands);

      Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.RecordChunkAsync(afterFirstRevocation, colleagueActor,
        new RecordPbcUploadChunkRequest(uploadReceipt.UploadIntentId, 0, 0, 10,
          Hashing.Sha256Hex(new byte[10]), capability))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await PbcService.ResumeUploadAsync(afterFirstRevocation, colleagueActor,
        new ResumePbcUploadRequest(requestId, uploadReceipt.UploadIntentId, "ledger.csv", 10,
          new string('a', 64)))).ErrorCode);

      var intentAfterDeniedCommands = await afterFirstRevocation.PbcUploadIntents.AsNoTracking()
        .Where(x => x.Id == uploadReceipt.UploadIntentId)
        .Select(x => new { x.FileName, x.DeclaredByteCount, x.DeclaredSha256Hex, x.Revision, x.RequestRevision,
          x.State, x.ReceivedByteCount, x.CapabilityHash })
        .SingleAsync();
      Assert.Equal(intentBeforeDeniedCommands, intentAfterDeniedCommands);
      Assert.Equal(requestRevisionBeforeDeniedCommands, await afterFirstRevocation.PbcRequests.AsNoTracking()
        .Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync());
      var chunksAfterDeniedCommands = await afterFirstRevocation.PbcUploadChunks.AsNoTracking()
        .Where(x => x.PbcUploadIntentId == uploadReceipt.UploadIntentId).OrderBy(x => x.ChunkIndex)
        .Select(x => new { x.Id, x.ChunkIndex, x.Offset, x.ByteCount, x.Sha256Hex }).ToListAsync();
      Assert.Equal(chunksBeforeDeniedCommands, chunksAfterDeniedCommands);

      Assert.Equal(ErrorCodes.ScopeDenied,
        (await PbcService.ReplyAsync(afterFirstRevocation, colleagueActor, requestId, "Another file.")).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied,
        (await PbcService.StartUploadAsync(afterFirstRevocation, colleagueActor, Upload(requestId))).ErrorCode);
      Assert.Empty(await ClientPortalService.ParticipantRequests(afterFirstRevocation, colleagueActor).ToListAsync());
    }

    // Conversely, once revocation holds the fence, a later upload waits and then fails its fresh authorization check.
    Guid secondDelegationId;
    await using (var delegateAgain = new AuditSphereDbContext(pg.Options))
    {
      var secondDelegation = await ClientPortalService.DelegateRequestAsync(delegateAgain, primary, requestId, colleague.Id);
      Assert.True(secondDelegation.Succeeded, secondDelegation.Message);
      Assert.NotEqual(delegated.Value, secondDelegation.Value);
      secondDelegationId = secondDelegation.Value;
    }
    await DelegationRevocationCommitsBeforeLaterUploadAsync(pg, f, primary, colleagueActor, requestId, secondDelegationId);
    await using var afterSecondRevocation = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await ClientPortalService.ParticipantRequests(afterSecondRevocation, colleagueActor).ToListAsync());
  }

  private static async Task<PbcUploadReceipt> UploadCommitsBeforeDelegationRevocationAsync(
    PgTestSchema pg, PbcSeed.Fixture f, ActorContext primary, ActorContext delegateActor, Guid requestId, Guid delegationId)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await using var requestBlocker = new AuditSphereDbContext(pg.Options);
    await using var requestBlockerTx = await requestBlocker.Database.BeginTransactionAsync(timeout.Token);
    await requestBlocker.PbcRequests.FromSqlInterpolated($"""
      SELECT * FROM pbc_requests WHERE firm_id = {f.FirmId} AND id = {requestId} FOR UPDATE
      """).AsNoTracking().SingleAsync(timeout.Token);
    var requestBlockerPid = ((NpgsqlConnection)requestBlocker.Database.GetDbConnection()).ProcessID;

    await using var uploadDb = new AuditSphereDbContext(pg.Options);
    await using var revokeDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await uploadDb.Database.OpenConnectionAsync(timeout.Token);
    await revokeDb.Database.OpenConnectionAsync(timeout.Token);
    var uploadPid = ((NpgsqlConnection)uploadDb.Database.GetDbConnection()).ProcessID;
    var revokePid = ((NpgsqlConnection)revokeDb.Database.GetDbConnection()).ProcessID;
    Task<CommandResult<PbcUploadReceipt>>? uploadTask = null;
    Task<CommandResult>? revokeTask = null;
    PbcUploadReceipt? uploadReceipt = null;
    var blockerReleased = false;
    try
    {
      uploadTask = PbcService.StartUploadAsync(uploadDb, delegateActor, Upload(requestId), timeout.Token);
      await WaitUntilBlockedByAsync(observer, uploadPid, requestBlockerPid,
        "The upload must reach the request lock after acquiring the client fence.", timeout.Token);

      revokeTask = ClientPortalService.RevokeDelegationAsync(revokeDb, primary, delegationId, timeout.Token);
      await WaitUntilBlockedByAsync(observer, revokePid, uploadPid,
        "Revocation must wait for the in-flight upload's client fence.", timeout.Token);
      Assert.False(revokeTask.IsCompleted, "Revocation must not complete while the authorized upload is still uncommitted.");

      await requestBlockerTx.CommitAsync(timeout.Token);
      blockerReleased = true;
      var upload = await uploadTask.WaitAsync(timeout.Token);
      Assert.True(upload.Succeeded, upload.Message);
      uploadReceipt = Assert.IsType<PbcUploadReceipt>(upload.Value);
      var revoked = await revokeTask.WaitAsync(timeout.Token);
      Assert.True(revoked.Succeeded, revoked.Message);
    }
    finally
    {
      if (!blockerReleased) await requestBlockerTx.RollbackAsync(CancellationToken.None);
      if (uploadTask is not null && !uploadTask.IsCompleted)
        try { await uploadTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
      if (revokeTask is not null && !revokeTask.IsCompleted)
        try { await revokeTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var persistedIntent = await verify.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.PbcRequestId == requestId, timeout.Token);
    var persistedDelegation = await verify.PbcRequestDelegations.AsNoTracking().SingleAsync(x => x.Id == delegationId, timeout.Token);
    var request = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, timeout.Token);
    Assert.Equal(PbcStates.PartiallyReceived, request.State);
    Assert.NotNull(persistedDelegation.RevokedAt);
    Assert.True(persistedIntent.CreatedAt <= persistedDelegation.RevokedAt!.Value);
    return Assert.IsType<PbcUploadReceipt>(uploadReceipt);
  }

  private static async Task DelegationRevocationCommitsBeforeLaterUploadAsync(
    PgTestSchema pg, PbcSeed.Fixture f, ActorContext primary, ActorContext delegateActor, Guid requestId, Guid delegationId)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
    await using var before = new AuditSphereDbContext(pg.Options);
    var beforeRequest = await before.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, timeout.Token);
    var beforeIntentCount = await before.PbcUploadIntents.CountAsync(x => x.PbcRequestId == requestId, timeout.Token);

    await using var delegationBlocker = new AuditSphereDbContext(pg.Options);
    await using var delegationBlockerTx = await delegationBlocker.Database.BeginTransactionAsync(timeout.Token);
    await delegationBlocker.PbcRequestDelegations.FromSqlInterpolated($"""
      SELECT * FROM pbc_request_delegations WHERE firm_id = {f.FirmId} AND id = {delegationId} FOR UPDATE
      """).AsNoTracking().SingleAsync(timeout.Token);
    var delegationBlockerPid = ((NpgsqlConnection)delegationBlocker.Database.GetDbConnection()).ProcessID;

    await using var revokeDb = new AuditSphereDbContext(pg.Options);
    await using var uploadDb = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await revokeDb.Database.OpenConnectionAsync(timeout.Token);
    await uploadDb.Database.OpenConnectionAsync(timeout.Token);
    var revokePid = ((NpgsqlConnection)revokeDb.Database.GetDbConnection()).ProcessID;
    var uploadPid = ((NpgsqlConnection)uploadDb.Database.GetDbConnection()).ProcessID;
    Task<CommandResult>? revokeTask = null;
    Task<CommandResult<PbcUploadReceipt>>? uploadTask = null;
    var blockerReleased = false;
    try
    {
      revokeTask = ClientPortalService.RevokeDelegationAsync(revokeDb, primary, delegationId, timeout.Token);
      await WaitUntilBlockedByAsync(observer, revokePid, delegationBlockerPid,
        "Revocation must reach the delegation row after acquiring the firm and client fences.", timeout.Token);

      uploadTask = PbcService.StartUploadAsync(uploadDb, delegateActor, Upload(requestId), timeout.Token);
      await WaitUntilBlockedByAsync(observer, uploadPid, revokePid,
        "The later upload must wait for revocation's client fence.", timeout.Token);
      Assert.False(uploadTask.IsCompleted, "The upload must not pass the revocation fence.");

      await delegationBlockerTx.CommitAsync(timeout.Token);
      blockerReleased = true;
      var revoked = await revokeTask.WaitAsync(timeout.Token);
      Assert.True(revoked.Succeeded, revoked.Message);
      var upload = await uploadTask.WaitAsync(timeout.Token);
      Assert.Equal(ErrorCodes.ScopeDenied, upload.ErrorCode);
    }
    finally
    {
      if (!blockerReleased) await delegationBlockerTx.RollbackAsync(CancellationToken.None);
      if (revokeTask is not null && !revokeTask.IsCompleted)
        try { await revokeTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
      if (uploadTask is not null && !uploadTask.IsCompleted)
        try { await uploadTask.WaitAsync(TimeSpan.FromSeconds(10)); } catch { }
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var afterRequest = await verify.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId, timeout.Token);
    Assert.Equal(beforeRequest.Revision, afterRequest.Revision);
    Assert.Equal(beforeIntentCount, await verify.PbcUploadIntents.CountAsync(x => x.PbcRequestId == requestId, timeout.Token));
    Assert.NotNull(await verify.PbcRequestDelegations.AsNoTracking()
      .Where(x => x.Id == delegationId).Select(x => x.RevokedAt).SingleAsync(timeout.Token));
  }

  private static async Task WaitUntilBlockedByAsync(
    AuditSphereDbContext observer, int waiterPid, int blockerPid, string message, CancellationToken ct)
  {
    var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
    while (DateTimeOffset.UtcNow < deadline)
    {
      await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", ct);
      if (await observer.Database.SqlQuery<bool>(
          $"SELECT {blockerPid} = ANY(pg_blocking_pids({waiterPid})) AS \"Value\"").SingleAsync(ct))
        return;
      await Task.Delay(20, ct);
    }
    Assert.Fail(message);
  }
  [Fact]
  public async Task PortalWorkspace_HidesOtherParticipants_AndFrozenFileRefusesClientReplies()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var other = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    var mine = await SentRequestAsync(pg, f, f.Client.Id);
    var sibling = await SentRequestAsync(pg, f, other.Id);
    var actor = PbcSeed.Actor(f.Client, "ClientUser");
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      var now = DateTimeOffset.UtcNow;
      for (var index = 0; index < 11; index++)
        seed.PbcRequests.Add(new PbcRequest
        {
          Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
          Objective = $"Synthetic page objective {index}", EntityScope = "TEST ENTITY", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
          Area = $"Synthetic page request {index}", RequestedFormat = "PDF", ControlTotals = "12 months", ClientOwnerUserId = f.Client.Id,
          FirmOwnerUserId = f.Staff.Id, ReviewerUserId = f.Reviewer.Id, DueDate = $"2027-02-{index + 1:00}",
          Confidentiality = "Confidential", AcceptanceCriteria = "Complete period.", State = PbcStates.Sent, Revision = 1,
          CreatedAt = now.AddMinutes(index), CreatedByUserId = f.Staff.Id, UpdatedAt = now.AddMinutes(index)
        });
      await seed.SaveChangesAsync();
    }
    await using var db = new AuditSphereDbContext(pg.Options);
    var workspace = await ClientPortalWorkspaceQuery.GetAsync(db, actor, page: 0, pageSize: 10);
    Assert.Equal(10, workspace.Value!.Requests.Count);
    Assert.True(workspace.Value.HasMoreRequests);
    Assert.Contains(workspace.Value.Requests, x => x.Id == mine);
    Assert.DoesNotContain(workspace.Value.Requests, x => x.Id == sibling);
    var nextPage = await ClientPortalWorkspaceQuery.GetAsync(db, actor, page: 1, pageSize: 10);
    Assert.Equal(2, nextPage.Value!.Requests.Count);
    Assert.False(nextPage.Value.HasMoreRequests);
    Assert.Empty(workspace.Value.Requests.Select(x => x.Id).Intersect(nextPage.Value.Requests.Select(x => x.Id)));
    var largerPage = await ClientPortalWorkspaceQuery.GetAsync(db, actor, page: 0, pageSize: 25);
    Assert.Equal(12, largerPage.Value!.Requests.Count);
    Assert.False(largerPage.Value.HasMoreRequests);
    Assert.False((await ClientPortalWorkspaceQuery.GetAsync(db, actor, page: 0, pageSize: 20)).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalWorkspaceQuery.RequestAsync(db, actor, sibling)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, (await ClientPortalWorkspaceQuery.RequestAsync(db, PbcSeed.Actor(f.Staff, "Staff"), mine)).ErrorCode);
    Assert.True((await PbcService.ReplyAsync(db, actor, mine, "Before freeze")).Succeeded);
    var reportId = Guid.NewGuid(); var signedAt = DateTimeOffset.UtcNow.AddDays(-70);
    db.AuditDeliverables.Add(new AuditSphereOps.Domain.Completion.AuditDeliverable
    {
      Id = reportId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, Kind = "INDEPENDENT_AUDITORS_REPORT",
      InputDigest = new string('a', 64), Content = [1], ContentSha256 = Hashing.Sha256Hex(new byte[] { 1 }),
      TemplateVersion = "SYNTHETIC", FileName = "synthetic.pdf", ContentType = "application/pdf", CreatedByUserId = f.Admin.Id, CreatedAt = signedAt
    });
    db.EngagementFileFreezes.Add(new AuditSphereOps.Domain.Records.EngagementFileFreeze
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      ReportDeliverableId = reportId, ReportSignedAt = signedAt, DueAt = signedAt.AddDays(60),
      State = "FROZEN", FrozenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    Assert.False((await ClientPortalWorkspaceQuery.RequestAsync(db, actor, mine)).Value!.CanWrite);
    Assert.Equal(ErrorCodes.ProtectedState, (await PbcService.ReplyAsync(db, actor, mine, "After freeze")).ErrorCode);
    Assert.False(await db.PbcCommunications.AnyAsync(x => x.PbcRequestId == mine && x.Body == "After freeze"));
  }

  [Fact]
  public async Task PortalRequestHistory_ContinuesBoundedOrderedPagesAndDeniesUnassignedClient()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var other = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    var requestId = await SentRequestAsync(pg, f, f.Client.Id);
    var otherRequestId = await SentRequestAsync(pg, f, other.Id);
    var occurred = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
    string[] originalMessages;
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      originalMessages = await seed.PbcCommunications.AsNoTracking()
        .Where(x => x.PbcRequestId == requestId).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
        .Select(x => x.Body).ToArrayAsync();
      seed.PbcCommunications.AddRange(Enumerable.Range(0, 31).Select(index => new PbcCommunication
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = requestId, AuthorUserId = f.Staff.Id, Kind = PbcCommunicationKinds.ClientMessage,
        Body = $"Message {index:D2}", CreatedAt = occurred.AddMinutes(index)
      }));
      seed.PbcUploadIntents.AddRange(Enumerable.Range(0, 31).Select(index => new PbcUploadIntent
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = requestId, UploaderUserId = f.Client.Id, FileName = $"upload-{index:D2}.pdf",
        ContentType = "application/pdf", DeclaredByteCount = 1, DeclaredSha256Hex = new string('a', 64),
        CapabilityHash = new string('b', 64), State = PbcUploadStates.Started, ExpiresAt = occurred.AddMinutes(index).AddDays(1),
        CreatedAt = occurred.AddMinutes(index), Revision = 1
      }));
      await seed.SaveChangesAsync();
    }

    var actor = PbcSeed.Actor(f.Client, "ClientUser");
    await using var db = new AuditSphereDbContext(pg.Options);
    var summary = await ClientPortalWorkspaceQuery.RequestAsync(db, actor, requestId);
    Assert.True(summary.Succeeded, summary.Message);
    Assert.Equal(25, summary.Value!.Uploads.Count);
    Assert.Equal(25, summary.Value.Conversation.Count);
    Assert.True(summary.Value.HasMoreUploads);
    Assert.True(summary.Value.HasMoreConversation);

    var page = await ClientPortalWorkspaceQuery.RequestHistoryAsync(db, actor, requestId,
      uploadPage: 1, conversationPage: 2, pageSize: 10);
    Assert.True(page.Succeeded, page.Message);
    Assert.Equal(requestId, page.Value!.RequestId);
    Assert.Equal(10, page.Value.Uploads.Count);
    Assert.Equal("upload-20.pdf", page.Value.Uploads[0].FileName);
    Assert.Equal("upload-11.pdf", page.Value.Uploads[^1].FileName);
    Assert.Equal(10, page.Value.Conversation.Count);
    Assert.Equal("Message 20", page.Value.Conversation[0].Body);
    Assert.Equal("Message 29", page.Value.Conversation[^1].Body);
    Assert.True(page.Value.HasMoreUploads);
    Assert.True(page.Value.HasMoreConversation);
    var last = await ClientPortalWorkspaceQuery.RequestHistoryAsync(db, actor, requestId,
      uploadPage: 3, conversationPage: 3, pageSize: 10);
    Assert.True(last.Succeeded, last.Message);
    Assert.Single(last.Value!.Uploads);
    Assert.Equal("upload-00.pdf", last.Value.Uploads[0].FileName);
    Assert.Equal(new[] { "Message 30" }.Concat(originalMessages), last.Value.Conversation.Select(x => x.Body));
    Assert.False(last.Value.HasMoreUploads);
    Assert.False(last.Value.HasMoreConversation);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await ClientPortalWorkspaceQuery.RequestHistoryAsync(db, actor, otherRequestId, 0, 0, 10)).ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied,
      (await ClientPortalWorkspaceQuery.RequestHistoryAsync(db, PbcSeed.Actor(f.Staff, "Staff"), requestId, 0, 0, 10)).ErrorCode);
  }

  [Fact]
  public async Task PortalRequest_PresentsClarificationReason_WhenAuditorRejectsAndRequestsReUpload()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var clientUser = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    var requestId = await SentRequestAsync(pg, f, clientUser.Id);
    var reviewerActor = PbcSeed.Actor(f.Reviewer, "Staff");
    var clientActor = PbcSeed.Actor(clientUser, "ClientUser");

    // The assigned reviewer transitions the request to UnderReview and then ClarificationRequired.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, PbcStates.UnderReview));
      var current = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      const string reason = "Bank statement pages 3 and 4 are missing from the scanned copy.";
      var reject = await PbcService.ChangeStateAsync(db, reviewerActor, new PbcStateChangeRequest(requestId, PbcStates.ClarificationRequired, current.Revision, ClarificationReason: reason));
      Assert.True(reject.Succeeded, reject.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var query = await ClientPortalWorkspaceQuery.RequestAsync(db, clientActor, requestId);
      Assert.True(query.Succeeded, query.Message);
      var w = query.Value!;
      Assert.Equal(PbcStates.ClarificationRequired, w.State);
      Assert.Equal("Bank statement pages 3 and 4 are missing from the scanned copy.", w.ClarificationReason);
      Assert.True(w.CanWrite, "Client must be permitted to re-upload files when clarification/re-upload is requested");
    }
  }

  [Fact]
  public async Task RequireUploadWindow_AllowsUploadsDuringInternalFinancialPackageRelease_AndFreezesOnFinalBundle()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var clientUser = await AddClientUserAsync(pg, f, f.ClientId, f.EngagementId, onboarded: true);
    var clientActor = PbcSeed.Actor(clientUser, "ClientUser");

    // Seed internal financial package release (which occurs before final deliverable release bundle)
    var approvalId = Guid.NewGuid();
    var releaseCandidateId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Approvals.Add(new AuditSphereOps.Domain.Reviews.Approval
      {
        Id = approvalId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        TargetKind = "FINANCIAL_PACKAGE", TargetId = Guid.NewGuid(), TargetRevision = 1,
        InputGeneration = 1, PolicyGeneration = 1, ManifestDigest = new string('a', 64),
        Decision = "APPROVED", DecidedByUserId = f.Staff.Id, DecidedAt = DateTimeOffset.UtcNow
      });
      db.ReleaseCandidates.Add(new AuditSphereOps.Domain.Completion.ReleaseCandidate
      {
        Id = releaseCandidateId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        TargetKind = "FINANCIAL_PACKAGE", TargetId = Guid.NewGuid(), TargetRevision = 1, Revision = 1,
        InputGeneration = 1, PolicyGeneration = 1, ApprovalId = approvalId, ManifestDigest = new string('b', 64),
        Status = "READY", CreatedAt = DateTimeOffset.UtcNow
      });
      var checkpointId = Guid.NewGuid();
      db.ReleaseCheckpoints.Add(new AuditSphereOps.Domain.Completion.ReleaseCheckpoint
      {
        Id = checkpointId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ReleaseCandidateId = releaseCandidateId, CandidateRevision = 1, AuthorizedReleaseKey = "test-release-key",
        ManifestDigest = new string('a', 64), StoredReference = "ref-1", ReadBackDigest = new string('a', 64),
        VerifiedStatus = "VERIFIED", Verifier = "test", CreatedAt = DateTimeOffset.UtcNow
      });
      db.Releases.Add(new AuditSphereOps.Domain.Completion.Release
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ReleaseCandidateId = releaseCandidateId, PackageId = Guid.NewGuid(), PackageRevision = 1,
        ManifestDigest = new string('a', 64), AuthorizedReleaseKey = "test-release-key",
        CheckpointId = checkpointId, ReleasedAt = DateTimeOffset.UtcNow, ReleasedByUserId = f.Staff.Id
      });
      await db.SaveChangesAsync();
    }

    // Client upload window is NOT closed by internal financial package release (§4.4, AS-COMP-09)
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var window = await ClientPortalService.RequireUploadWindowAsync(db, clientActor, f.EngagementId);
      Assert.True(window.Succeeded, "Internal financial package release must not freeze client uploads prior to final engagement delivery");
    }

    // Now seed the authoritative final file freeze
    var reportId = Guid.NewGuid();
    var signedAt = DateTimeOffset.UtcNow.AddDays(-70);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AuditDeliverables.Add(new AuditSphereOps.Domain.Completion.AuditDeliverable
      {
        Id = reportId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, Kind = "INDEPENDENT_AUDITORS_REPORT",
        InputDigest = new string('a', 64), Content = [1], ContentSha256 = Hashing.Sha256Hex([1]),
        TemplateVersion = "SYNTHETIC", FileName = "synthetic.pdf", ContentType = "application/pdf", CreatedByUserId = f.Admin.Id, CreatedAt = signedAt
      });
      db.EngagementFileFreezes.Add(new AuditSphereOps.Domain.Records.EngagementFileFreeze
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        ReportDeliverableId = reportId, ReportSignedAt = signedAt, DueAt = signedAt.AddDays(60),
        State = "FROZEN", FrozenAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    // Now client upload window is frozen
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var window = await ClientPortalService.RequireUploadWindowAsync(db, clientActor, f.EngagementId);
      Assert.Equal(ErrorCodes.ProtectedState, window.ErrorCode);
    }
  }

  private sealed class ConcurrentFirstSignInSaveBarrier : SaveChangesInterceptor
  {
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int arrivals;
    public int Arrivals => Volatile.Read(ref arrivals);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      if (Interlocked.Increment(ref arrivals) == 2) released.TrySetResult();
      await released.Task.WaitAsync(cancellationToken);
      return result;
    }
  }

  private sealed class FailingSaveInterceptor : SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
      DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      throw new DbUpdateException("Injected non-duplicate persistence failure");
    }
  }
}
