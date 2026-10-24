using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientRelationshipsAndRoutingTests
{
  [Fact]
  public async Task LegalEntityProfile_CapturesTaxIdentityAndSignatoryAuthority_AndEnforcesAuditHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
    await db.SaveChangesAsync();

    // 03.1: Capture legal/registration/tax identity
    var updateResult = await PracticeCrmService.UpdateClientProfileAsync(db, partner, new UpdateClientProfileRequest(
      seed.ClientId,
      LegalName: "Al-Mirqab Holdings Q.P.S.C.",
      CommercialName: "Al-Mirqab Group",
      RegistrationNumber: "CR-987654",
      TaxRegistrationNumber: "TIN-QA-123456789",
      EntityType: "Holding Company",
      Jurisdiction: "Qatar",
      RestrictedProfile: "PEP-Screened",
      ExpectedGeneration: 1));

    Assert.True(updateResult.Succeeded, updateResult.Message);

    var client = await db.PracticeClients.AsNoTracking().SingleAsync(x => x.Id == seed.ClientId);
    Assert.Equal("Al-Mirqab Holdings Q.P.S.C.", client.LegalName);
    Assert.Equal("TIN-QA-123456789", client.TaxRegistrationNumber);
    Assert.Equal("Holding Company", client.EntityType);
    Assert.Equal("CR-987654", client.RegistrationNumber);

    // Stale generation fails closed
    var staleResult = await PracticeCrmService.UpdateClientProfileAsync(db, partner, new UpdateClientProfileRequest(
      seed.ClientId, LegalName: "New Name", ExpectedGeneration: 1));
    Assert.Equal(ErrorCodes.GenerationStale, staleResult.ErrorCode);

    // Add and update contact with signatory authority
    var contactId = Guid.NewGuid();
    db.ClientContacts.Add(new ClientContact
    {
      Id = contactId,
      FirmId = seed.FirmId,
      PracticeClientId = seed.ClientId,
      FullName = "Sheikh Mansoor Al-Thani",
      Email = "mansoor@almirqab.test",
      Role = "Managing Director",
      Primary = true,
      IsActive = true
    });
    await db.SaveChangesAsync();

    var updateContact = await PracticeCrmService.UpdateClientContactAsync(db, partner, new UpdateClientContactRequest(
      contactId,
      FullName: "Sheikh Mansoor Al-Thani",
      Email: "mansoor@almirqab.test",
      Role: "Managing Director",
      Phone: "+974 4400 1122",
      Title: "Managing Director & CEO",
      SignatoryAuthority: "SoleSignatory",
      Primary: true,
      IsActive: true));
    Assert.True(updateContact.Succeeded, updateContact.Message);

    var contact = await db.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == contactId);
    Assert.Equal("SoleSignatory", contact.SignatoryAuthority);
    Assert.Equal("+974 4400 1122", contact.Phone);
    Assert.Equal("Managing Director & CEO", contact.Title);
    Assert.True(contact.IsActive);

    // 03.5: Deactivation preserves contact record without deleting historical evidence
    var deactivate = await PracticeCrmService.UpdateClientContactAsync(db, partner, new UpdateClientContactRequest(
      contactId,
      FullName: "Sheikh Mansoor Al-Thani",
      Email: "mansoor@almirqab.test",
      Role: "Former Managing Director",
      IsActive: false));
    Assert.True(deactivate.Succeeded);

    var deactivatedContact = await db.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == contactId);
    Assert.False(deactivatedContact.IsActive);
  }

  [Theory]
  [InlineData("update-profile", true)]
  [InlineData("set-archived", true)]
  [InlineData("create-relationship", true)]
  [InlineData("revoke-relationship", true)]
  [InlineData("update-profile", false)]
  [Trait("Profile", "Database")]
  public async Task ClientMutators_RefuseAuthorityRevokedWhileWaitingForFirmSafetyFence(
    string mutation, bool advanceSessionEpoch)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(seed.Staff, "Manager");
    var relatedClientId = Guid.NewGuid();
    var relationshipId = Guid.NewGuid();

    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.PracticeClients.Add(new PracticeClient
      {
        Id = relatedClientId, FirmId = seed.FirmId, LegalName = "Related test client",
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      setup.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState
      {
        Id = relatedClientId, FirmId = seed.FirmId
      });
      setup.RoleGrants.AddRange(
        PbcSeed.Grant(seed.FirmId, seed.Staff, "Manager", clientId: seed.ClientId),
        PbcSeed.Grant(seed.FirmId, seed.Staff, "Manager", clientId: relatedClientId));
      if (mutation == "revoke-relationship")
        setup.ClientRelationships.Add(new ClientRelationship
        {
          Id = relationshipId, FirmId = seed.FirmId, PrimaryClientId = seed.ClientId,
          RelatedClientId = relatedClientId, RelationshipKind = ClientRelationshipKinds.Affiliate,
          CreatedByUserId = seed.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
        });
      await setup.SaveChangesAsync();
    }

    ClientProfileSnapshot profileBefore;
    long generationBefore;
    LifecycleSnapshot[] lifecycleBefore;
    RelationshipSnapshot[] relationshipsBefore;
    await using (var before = new AuditSphereDbContext(pg.Options))
    {
      profileBefore = await before.PracticeClients.AsNoTracking().Where(x => x.Id == seed.ClientId)
        .Select(x => new ClientProfileSnapshot(x.LegalName, x.CommercialName, x.RegistrationNumber,
          x.TaxRegistrationNumber, x.EntityType, x.Jurisdiction, x.RestrictedProfile, x.Status))
        .SingleAsync();
      generationBefore = await before.ClientSafetyStates.AsNoTracking()
        .Where(x => x.Id == seed.ClientId).Select(x => x.InputGeneration).SingleAsync();
      lifecycleBefore = await before.ClientLifecycleEvents.AsNoTracking()
        .Where(x => x.FirmId == seed.FirmId && x.ClientId == seed.ClientId)
        .OrderBy(x => x.Revision)
        .Select(x => new LifecycleSnapshot(x.Id, x.Revision, x.EventKind, x.ActorUserId, x.OccurredAt))
        .ToArrayAsync();
      relationshipsBefore = await before.ClientRelationships.AsNoTracking()
        .Where(x => x.FirmId == seed.FirmId &&
          (x.PrimaryClientId == seed.ClientId || x.RelatedClientId == seed.ClientId))
        .OrderBy(x => x.Id)
        .Select(x => new RelationshipSnapshot(x.Id, x.PrimaryClientId, x.RelatedClientId,
          x.RelationshipKind, x.OwnershipPercentage, x.Notes, x.CreatedByUserId, x.CreatedAt,
          x.RevokedByUserId, x.RevokedAt, x.RevocationReason))
        .ToArrayAsync();
    }

    await using var blocker = new AuditSphereDbContext(pg.Options);
    await blocker.Database.OpenConnectionAsync();
    var blockerPid = ((NpgsqlConnection)blocker.Database.GetDbConnection()).ProcessID;
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {seed.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();

    var probe = new FirmLockAttempt();
    var mutationOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(probe).Options;
    await using var mutator = new AuditSphereDbContext(mutationOptions);
    using var mutationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var mutationTask = ExecuteClientMutationAsync(mutation, mutator, actor, seed, relatedClientId,
      relationshipId, mutationTimeout.Token);
    var lockReached = probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    if (await Task.WhenAny(lockReached, mutationTask) != lockReached)
    {
      var early = await mutationTask;
      Assert.Fail($"{mutation} completed before acquiring the firm safety fence " +
        $"(Succeeded={early.Succeeded}, ErrorCode={early.ErrorCode}).");
    }

    var waitingPid = await lockReached;
    await using var observer = new AuditSphereDbContext(pg.Options);
    await WaitForFirmBlockingPidAsync(observer, waitingPid, blockerPid);
    Assert.False(mutationTask.IsCompleted);

    var revokedAt = DateTimeOffset.UtcNow;
    var revokedGrants = await blocker.RoleGrants.Where(g => g.FirmId == seed.FirmId &&
        g.UserId == actor.UserId && g.Role == "Manager" && g.EngagementId == null &&
        (g.ClientId == seed.ClientId || g.ClientId == relatedClientId))
      .ExecuteUpdateAsync(update => update.SetProperty(g => g.RevokedAt, revokedAt));
    Assert.Equal(2, revokedGrants);
    if (advanceSessionEpoch)
      await blocker.Users.Where(u => u.Id == actor.UserId)
        .ExecuteUpdateAsync(update => update.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    await tx.CommitAsync();

    var result = await mutationTask.WaitAsync(TimeSpan.FromSeconds(20));
    Assert.False(result.Succeeded);
    Assert.Equal(advanceSessionEpoch ? ErrorCodes.GenerationStale : ErrorCodes.ScopeDenied, result.ErrorCode);

    await using var after = new AuditSphereDbContext(pg.Options);
    var profileAfter = await after.PracticeClients.AsNoTracking().Where(x => x.Id == seed.ClientId)
      .Select(x => new ClientProfileSnapshot(x.LegalName, x.CommercialName, x.RegistrationNumber,
        x.TaxRegistrationNumber, x.EntityType, x.Jurisdiction, x.RestrictedProfile, x.Status))
      .SingleAsync();
    var generationAfter = await after.ClientSafetyStates.AsNoTracking()
      .Where(x => x.Id == seed.ClientId).Select(x => x.InputGeneration).SingleAsync();
    var lifecycleAfter = await after.ClientLifecycleEvents.AsNoTracking()
      .Where(x => x.FirmId == seed.FirmId && x.ClientId == seed.ClientId)
      .OrderBy(x => x.Revision)
      .Select(x => new LifecycleSnapshot(x.Id, x.Revision, x.EventKind, x.ActorUserId, x.OccurredAt))
      .ToArrayAsync();
    var relationshipsAfter = await after.ClientRelationships.AsNoTracking()
      .Where(x => x.FirmId == seed.FirmId &&
        (x.PrimaryClientId == seed.ClientId || x.RelatedClientId == seed.ClientId))
      .OrderBy(x => x.Id)
      .Select(x => new RelationshipSnapshot(x.Id, x.PrimaryClientId, x.RelatedClientId,
        x.RelationshipKind, x.OwnershipPercentage, x.Notes, x.CreatedByUserId, x.CreatedAt,
        x.RevokedByUserId, x.RevokedAt, x.RevocationReason))
      .ToArrayAsync();

    Assert.Equal(profileBefore, profileAfter);
    Assert.Equal(generationBefore, generationAfter);
    Assert.Equal(lifecycleBefore, lifecycleAfter);
    Assert.Equal(relationshipsBefore, relationshipsAfter);
  }

  [Theory]
  [InlineData("identical")]
  [InlineData("inverse-parent")]
  [Trait("Profile", "Database")]
  public async Task ConcurrentRelationshipCreation_PublishesOnlyOneNonConflictingEdge(string competingEdges)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(seed.Admin, "Administrator");
    var relatedClientId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.PracticeClients.Add(new PracticeClient
      {
        Id = relatedClientId, FirmId = seed.FirmId, LegalName = "Concurrent relationship client",
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      setup.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState
      {
        Id = relatedClientId, FirmId = seed.FirmId
      });
      await setup.SaveChangesAsync();
    }

    var firstRequest = new CreateClientRelationshipRequest(
      seed.ClientId, relatedClientId, ClientRelationshipKinds.Parent);
    var secondRequest = competingEdges == "inverse-parent"
      ? new CreateClientRelationshipRequest(relatedClientId, seed.ClientId, ClientRelationshipKinds.Parent)
      : firstRequest;

    await using var blocker = new AuditSphereDbContext(pg.Options);
    await blocker.Database.OpenConnectionAsync();
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {seed.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();

    var firstProbe = new FirmLockAttempt();
    var secondProbe = new FirmLockAttempt();
    var firstOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(firstProbe).Options;
    var secondOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(secondProbe).Options;
    await using var firstWriter = new AuditSphereDbContext(firstOptions);
    await using var secondWriter = new AuditSphereDbContext(secondOptions);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
    var firstTask = PracticeCrmService.CreateClientRelationshipAsync(firstWriter, actor, firstRequest, timeout.Token);
    var secondTask = PracticeCrmService.CreateClientRelationshipAsync(secondWriter, actor, secondRequest, timeout.Token);

    var firstLockReached = firstProbe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    if (await Task.WhenAny(firstLockReached, firstTask) != firstLockReached)
    {
      var early = await firstTask;
      Assert.Fail($"First relationship command completed before the firm fence " +
        $"(Succeeded={early.Succeeded}, ErrorCode={early.ErrorCode}).");
    }
    var secondLockReached = secondProbe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    if (await Task.WhenAny(secondLockReached, secondTask) != secondLockReached)
    {
      var early = await secondTask;
      Assert.Fail($"Second relationship command completed before the firm fence " +
        $"(Succeeded={early.Succeeded}, ErrorCode={early.ErrorCode}).");
    }

    var firstPid = await firstLockReached;
    var secondPid = await secondLockReached;
    Assert.NotEqual(firstPid, secondPid);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await WaitForAnyFirmLockBlockerAsync(observer, firstPid);
    await WaitForAnyFirmLockBlockerAsync(observer, secondPid);
    Assert.False(firstTask.IsCompleted);
    Assert.False(secondTask.IsCompleted);

    await tx.CommitAsync();
    var results = await Task.WhenAll(firstTask, secondTask).WaitAsync(TimeSpan.FromSeconds(25));
    Assert.Single(results, result => result.Succeeded);
    var rejected = Assert.Single(results, result => !result.Succeeded);
    Assert.Equal(competingEdges == "inverse-parent" ? "crm.relationship-cycle" : "crm.duplicate",
      rejected.ErrorCode);

    await using var after = new AuditSphereDbContext(pg.Options);
    var activeEdges = await after.ClientRelationships.AsNoTracking()
      .Where(x => x.FirmId == seed.FirmId && x.RevokedAt == null &&
        ((x.PrimaryClientId == seed.ClientId && x.RelatedClientId == relatedClientId) ||
         (x.PrimaryClientId == relatedClientId && x.RelatedClientId == seed.ClientId)))
      .Select(x => new { x.PrimaryClientId, x.RelatedClientId, x.RelationshipKind })
      .ToArrayAsync();
    var edge = Assert.Single(activeEdges);
    Assert.Equal(ClientRelationshipKinds.Parent, edge.RelationshipKind);
    if (competingEdges == "identical")
    {
      Assert.Equal(seed.ClientId, edge.PrimaryClientId);
      Assert.Equal(relatedClientId, edge.RelatedClientId);
    }
  }

  [Fact]
  public async Task OrganizationRelationships_BuildsHierarchy_AndEnforcesCycleAndBoundaryInvariants()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));

    // 03.2: Create holding company, two subsidiaries, and an affiliate
    var holdingId = seed.ClientId; // Primary client
    var sub1Id = Guid.NewGuid();
    var sub2Id = Guid.NewGuid();
    var affiliateId = Guid.NewGuid();

    db.PracticeClients.AddRange(
      new PracticeClient { Id = sub1Id, FirmId = seed.FirmId, LegalName = "Doha Logistics W.L.L.", Status = "Active", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = sub2Id, FirmId = seed.FirmId, LegalName = "Lusail Real Estate Co.", Status = "Active", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = affiliateId, FirmId = seed.FirmId, LegalName = "Qatar Energy Partners JV", Status = "Active", CreatedAt = DateTimeOffset.UtcNow }
    );
    await db.SaveChangesAsync();

    // Link Sub1 as Subsidiary of Holding (Holding is Parent)
    var rSub1 = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      PrimaryClientId: holdingId,
      RelatedClientId: sub1Id,
      RelationshipKind: ClientRelationshipKinds.Parent,
      OwnershipPercentage: 100m,
      Notes: "Wholly owned logistics subsidiary"));
    Assert.True(rSub1.Succeeded, rSub1.Message);

    // Link Sub2 as Subsidiary of Holding
    var rSub2 = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      PrimaryClientId: holdingId,
      RelatedClientId: sub2Id,
      RelationshipKind: ClientRelationshipKinds.Parent,
      OwnershipPercentage: 75m,
      Notes: "75% majority owned real estate subsidiary"));
    Assert.True(rSub2.Succeeded, rSub2.Message);

    // Link Affiliate
    var rAff = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      PrimaryClientId: holdingId,
      RelatedClientId: affiliateId,
      RelationshipKind: ClientRelationshipKinds.Affiliate,
      OwnershipPercentage: 30m,
      Notes: "30% joint venture associate"));
    Assert.True(rAff.Succeeded, rAff.Message);

    // Query hierarchy from Holding perspective
    var holdingTree = await PracticeCrmService.GetClientHierarchyAsync(db, partner, holdingId);
    Assert.True(holdingTree.Succeeded);
    Assert.Equal(2, holdingTree.Value!.Subsidiaries.Count);
    Assert.Single(holdingTree.Value.Affiliates);
    Assert.Empty(holdingTree.Value.Parents);

    // Query hierarchy from Sub1 perspective
    var sub1Tree = await PracticeCrmService.GetClientHierarchyAsync(db, partner, sub1Id);
    Assert.True(sub1Tree.Succeeded);
    Assert.Single(sub1Tree.Value!.Parents);
    Assert.Equal("PBC TEST CLIENT", sub1Tree.Value.Parents[0].PrimaryClientName);

    // 03.3 Invariants:
    // 1. Self-relationship is rejected
    var selfRel = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      holdingId, holdingId, ClientRelationshipKinds.Parent));
    Assert.Equal("crm.invalid", selfRel.ErrorCode);

    // 2. Duplicate active relationship is rejected
    var dupRel = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      holdingId, sub1Id, ClientRelationshipKinds.Parent));
    Assert.Equal("crm.duplicate", dupRel.ErrorCode);

    // 3. Ownership-tree cycle detection:
    // Sub1 is subsidiary of Holding. Adding a child SubSub1 under Sub1:
    var subSub1Id = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = subSub1Id, FirmId = seed.FirmId, LegalName = "Al-Wakra Warehousing", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    var rSubSub = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      sub1Id, subSub1Id, ClientRelationshipKinds.Parent));
    Assert.True(rSubSub.Succeeded);

    // Now attempt to make Holding a subsidiary of SubSub1 (SubSub1 is Parent of Holding) -> CYCLE!
    var cycleRel = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      subSub1Id, holdingId, ClientRelationshipKinds.Parent));
    Assert.Equal("crm.relationship-cycle", cycleRel.ErrorCode);

    // 4. Cross-firm relationship is rejected
    var foreignFirmId = Guid.NewGuid();
    var foreignClient = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = foreignClient, FirmId = foreignFirmId, LegalName = "Foreign Corp", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    var crossFirm = await PracticeCrmService.CreateClientRelationshipAsync(db, partner, new CreateClientRelationshipRequest(
      holdingId, foreignClient, ClientRelationshipKinds.Affiliate));
    Assert.Equal(ErrorCodes.ScopeDenied, crossFirm.ErrorCode);

    // 5. Revocation of relationship with reason
    await using var staleContext = new AuditSphereDbContext(pg.Options);
    var staleRelationship = await staleContext.ClientRelationships.SingleAsync(x => x.Id == rAff.Value);
    Assert.Null(staleRelationship.RevokedAt);

    var revoke = await PracticeCrmService.RevokeClientRelationshipAsync(db, partner, new RevokeClientRelationshipRequest(
      holdingId, rAff.Value, "Joint venture dissolved by mutual consent."));
    Assert.True(revoke.Succeeded);

    ClientRelationship firstEvidence;
    await using (var evidenceRead = new AuditSphereDbContext(pg.Options))
      firstEvidence = await evidenceRead.ClientRelationships.AsNoTracking().SingleAsync(x => x.Id == rAff.Value);
    var staleRetry = await PracticeCrmService.RevokeClientRelationshipAsync(staleContext, partner,
      new RevokeClientRelationshipRequest(holdingId, rAff.Value, "A later retry must preserve the first reason."));
    Assert.True(staleRetry.Succeeded, staleRetry.Message);
    ClientRelationship retainedEvidence;
    await using (var evidenceRead = new AuditSphereDbContext(pg.Options))
      retainedEvidence = await evidenceRead.ClientRelationships.AsNoTracking().SingleAsync(x => x.Id == rAff.Value);
    Assert.Equal(firstEvidence.RevokedAt, retainedEvidence.RevokedAt);
    Assert.Equal(firstEvidence.RevokedByUserId, retainedEvidence.RevokedByUserId);
    Assert.Equal(firstEvidence.RevocationReason, retainedEvidence.RevocationReason);

    var treeAfterRevoke = await PracticeCrmService.GetClientHierarchyAsync(db, partner, holdingId);
    var retainedAffiliate = Assert.Single(treeAfterRevoke.Value!.Affiliates);
    Assert.Equal("REVOKED", retainedAffiliate.EffectiveStatus);
    Assert.False(retainedAffiliate.IsActive);
  }

  [Fact]
  public async Task OrganizationRelationships_PreservesEffectivePeriodAndProjectsCurrentStatus()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var futureClientId = Guid.NewGuid();
    var formerClientId = Guid.NewGuid();

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
    db.PracticeClients.AddRange(
      new PracticeClient { Id = futureClientId, FirmId = seed.FirmId, LegalName = "Future Affiliate", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = formerClientId, FirmId = seed.FirmId, LegalName = "Former Affiliate", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    var future = await PracticeCrmService.CreateClientRelationshipAsync(db, partner,
      new CreateClientRelationshipRequest(seed.ClientId, futureClientId, ClientRelationshipKinds.Affiliate,
        EffectiveFrom: today.AddDays(1)));
    var former = await PracticeCrmService.CreateClientRelationshipAsync(db, partner,
      new CreateClientRelationshipRequest(seed.ClientId, formerClientId, ClientRelationshipKinds.Affiliate,
        EffectiveTo: today.AddDays(-1)));
    Assert.True(future.Succeeded, future.Message);
    Assert.True(former.Succeeded, former.Message);

    var hierarchy = await PracticeCrmService.GetClientHierarchyAsync(db, partner, seed.ClientId);
    Assert.True(hierarchy.Succeeded, hierarchy.Message);
    var rows = hierarchy.Value!.Affiliates.ToDictionary(x => x.RelatedClientId);
    Assert.Equal("FUTURE", rows[futureClientId].EffectiveStatus);
    Assert.Equal("EXPIRED", rows[formerClientId].EffectiveStatus);
    Assert.True(rows[futureClientId].IsActive);
    Assert.True(rows[formerClientId].IsActive);
  }

  [Fact]
  public async Task RelationshipAndRoutingRevocation_RequireTheRouteClientToMatchTheStoredRecord()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var otherClientId = Guid.NewGuid();
    var wrongRouteClientId = Guid.NewGuid();
    var contactId = Guid.NewGuid();

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
    db.PracticeClients.Add(new PracticeClient
    {
      Id = otherClientId, FirmId = seed.FirmId, LegalName = "Other Client", Status = "Active", CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientContacts.Add(new ClientContact
    {
      Id = contactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, FullName = "Finance Contact",
      Email = "finance@client.test", Role = "Finance", IsActive = true
    });
    await db.SaveChangesAsync();

    var relationship = await PracticeCrmService.CreateClientRelationshipAsync(db, partner,
      new CreateClientRelationshipRequest(seed.ClientId, otherClientId, ClientRelationshipKinds.Affiliate));
    Assert.True(relationship.Succeeded, relationship.Message);
    var routing = await PracticeCrmService.AssignContactRoutingAsync(db, partner,
      new AssignContactRoutingRequest(seed.ClientId, contactId, CorrespondencePurposes.Finance));
    Assert.True(routing.Succeeded, routing.Message);

    var wrongRelationshipRoute = await PracticeCrmService.RevokeClientRelationshipAsync(db, partner,
      new RevokeClientRelationshipRequest(wrongRouteClientId, relationship.Value, "Wrong client route"));
    var wrongRoutingRoute = await PracticeCrmService.RevokeContactRoutingAsync(db, partner,
      new RevokeContactRoutingRequest(wrongRouteClientId, routing.Value, "Wrong client route"));
    Assert.Equal(ErrorCodes.ScopeDenied, wrongRelationshipRoute.ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, wrongRoutingRoute.ErrorCode);
    Assert.Null(await db.ClientRelationships.AsNoTracking().Where(x => x.Id == relationship.Value).Select(x => x.RevokedAt).SingleAsync());
    Assert.Null(await db.ClientContactRoutings.AsNoTracking().Where(x => x.Id == routing.Value).Select(x => x.RevokedAt).SingleAsync());

    var revokeRelationship = await PracticeCrmService.RevokeClientRelationshipAsync(db, partner,
      new RevokeClientRelationshipRequest(seed.ClientId, relationship.Value, "Relationship ended."));
    var revokeRouting = await PracticeCrmService.RevokeContactRoutingAsync(db, partner,
      new RevokeContactRoutingRequest(seed.ClientId, routing.Value, "Recipient designation changed."));
    Assert.True(revokeRelationship.Succeeded, revokeRelationship.Message);
    Assert.True(revokeRouting.Succeeded, revokeRouting.Message);
    Assert.Equal("Relationship ended.", await db.ClientRelationships.AsNoTracking()
      .Where(x => x.Id == relationship.Value).Select(x => x.RevocationReason).SingleAsync());
    Assert.Equal("Recipient designation changed.", await db.ClientContactRoutings.AsNoTracking()
      .Where(x => x.Id == routing.Value).Select(x => x.RevocationReason).SingleAsync());
  }

  [Fact]
  public async Task CorrespondenceRouting_ResolvesPurposeSpecificContacts_AndRecordsImmutableDispatch()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));

    // Create 3 contacts for different purposes: MD, CFO, Audit Liaison
    var mdContactId = Guid.NewGuid();
    var cfoContactId = Guid.NewGuid();
    var auditContactId = Guid.NewGuid();

    db.ClientContacts.AddRange(
      new ClientContact { Id = mdContactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, FullName = "Fatima Al-Kuwari", Email = "fatima.md@client.test", Role = "Managing Director", Title = "MD", IsActive = true },
      new ClientContact { Id = cfoContactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, FullName = "Hassan Al-Emadi", Email = "hassan.cfo@client.test", Role = "CFO", Title = "Chief Financial Officer", IsActive = true },
      new ClientContact { Id = auditContactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, FullName = "Noor Al-Marri", Email = "noor.audit@client.test", Role = "Audit Liaison", Title = "Chief Accountant", IsActive = true }
    );
    await db.SaveChangesAsync();

    // 04.1: Assign purpose-specific routings
    var r1 = await PracticeCrmService.AssignContactRoutingAsync(db, partner, new AssignContactRoutingRequest(
      seed.ClientId, mdContactId, CorrespondencePurposes.Commercial, IsPrimaryForPurpose: true));
    Assert.True(r1.Succeeded);

    var r2 = await PracticeCrmService.AssignContactRoutingAsync(db, partner, new AssignContactRoutingRequest(
      seed.ClientId, cfoContactId, CorrespondencePurposes.Finance, IsPrimaryForPurpose: true));
    Assert.True(r2.Succeeded);

    var r3 = await PracticeCrmService.AssignContactRoutingAsync(db, partner, new AssignContactRoutingRequest(
      seed.ClientId, auditContactId, CorrespondencePurposes.AuditFieldwork, IsPrimaryForPurpose: true));
    Assert.True(r3.Succeeded);

    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    // Resolves Commercial -> MD
    var resolvedCommercial = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Resolved, resolvedCommercial.Status);
    Assert.Equal(mdContactId, resolvedCommercial.ClientContactId);
    Assert.Equal("fatima.md@client.test", resolvedCommercial.Email);

    // Resolves Finance -> CFO
    var resolvedFinance = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Finance, today);
    Assert.Equal(RecipientResolutionStatus.Resolved, resolvedFinance.Status);
    Assert.Equal(cfoContactId, resolvedFinance.ClientContactId);

    // Resolves AuditFieldwork -> Chief Accountant
    var resolvedAudit = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.AuditFieldwork, today);
    Assert.Equal(RecipientResolutionStatus.Resolved, resolvedAudit.Status);
    Assert.Equal(auditContactId, resolvedAudit.ClientContactId);

    // 04.2: Zero active recipients for Completion -> NoRecipient
    var resolvedCompletion = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Completion, today);
    Assert.Equal(RecipientResolutionStatus.NoRecipient, resolvedCompletion.Status);
    Assert.Null(resolvedCompletion.ClientContactId);

    // 04.2: Conflicting active recipients: Add second contact for Finance with IsPrimary = false
    var financeContact2 = Guid.NewGuid();
    db.ClientContacts.Add(new ClientContact { Id = financeContact2, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, FullName = "Finance Officer", Email = "officer@client.test", Role = "Finance Officer", IsActive = true });
    await db.SaveChangesAsync();

    // Adding second routing without primary
    await PracticeCrmService.AssignContactRoutingAsync(db, partner, new AssignContactRoutingRequest(
      seed.ClientId, financeContact2, CorrespondencePurposes.Finance, IsPrimaryForPurpose: false));

    // Because CFO has IsPrimaryForPurpose = true, it resolves cleanly to CFO
    var resolvedWithPrimary = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Finance, today);
    Assert.Equal(RecipientResolutionStatus.Resolved, resolvedWithPrimary.Status);
    Assert.Equal(cfoContactId, resolvedWithPrimary.ClientContactId);

    // 04.3: Reviewed recipient override records actor and reason
    var overrideResult = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Finance, today,
      overrideContactId: financeContact2, overrideReason: "CFO on medical leave; acting finance officer designated by board");
    Assert.Equal(RecipientResolutionStatus.Overridden, overrideResult.Status);
    Assert.True(overrideResult.WasOverridden);
    Assert.Equal(partner.UserId, overrideResult.OverriddenByUserId);
    Assert.Equal("CFO on medical leave; acting finance officer designated by board", overrideResult.OverrideReason);
    Assert.Equal(financeContact2, overrideResult.ClientContactId);

    // Override cannot target contact belonging to a different client
    var foreignClient = Guid.NewGuid();
    var foreignContact = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = foreignClient, FirmId = seed.FirmId, LegalName = "Other Client", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    db.ClientContacts.Add(new ClientContact { Id = foreignContact, FirmId = seed.FirmId, PracticeClientId = foreignClient, FullName = "Foreign User", Email = "foreign@other.test", Role = "Manager", IsActive = true });
    await db.SaveChangesAsync();

    var invalidOverride = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Finance, today,
      overrideContactId: foreignContact, overrideReason: "Attempted cross-client routing");
    Assert.Equal(RecipientResolutionStatus.NoRecipient, invalidOverride.Status);

    // 04.4: Record correspondence dispatch creates immutable audit receipt
    var docHash = new string('a', 64);
    var dispatchResult = await PracticeCrmService.RecordCorrespondenceDispatchAsync(db, partner, new RecordCorrespondenceDispatchRequest(
      seed.ClientId,
      EngagementId: null,
      Purpose: CorrespondencePurposes.Commercial,
      RecipientContactId: mdContactId,
      DocumentType: "CommercialProposal",
      DocumentReference: "PROP-2026-001",
      DocumentRevision: 1,
      DocumentSha256: docHash));
    Assert.True(dispatchResult.Succeeded, dispatchResult.Message);

    var dispatchRecord = await db.CorrespondenceDispatchRecords.AsNoTracking().SingleAsync(x => x.Id == dispatchResult.Value);
    Assert.Equal("fatima.md@client.test", dispatchRecord.RecipientEmail);
    Assert.Equal("Fatima Al-Kuwari", dispatchRecord.RecipientName);
    Assert.Equal("PROP-2026-001", dispatchRecord.DocumentReference);
    Assert.Equal(docHash, dispatchRecord.DocumentSha256);

    // 04.5: Deactivated contact cannot receive dispatch
    var deactContactId = Guid.NewGuid();
    db.ClientContacts.Add(new ClientContact { Id = deactContactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, FullName = "Ex Employee", Email = "ex@client.test", Role = "Exited", IsActive = false });
    await db.SaveChangesAsync();

    var deactDispatch = await PracticeCrmService.RecordCorrespondenceDispatchAsync(db, partner, new RecordCorrespondenceDispatchRequest(
      seed.ClientId, null, CorrespondencePurposes.Commercial, deactContactId, "Proposal", "PROP-002", 1, docHash));
    Assert.Equal(ErrorCodes.GateBlocked, deactDispatch.ErrorCode);

    var expiredContact = await db.ClientContacts.SingleAsync(x => x.Id == mdContactId);
    expiredContact.ValidTo = DateTimeOffset.UtcNow.AddDays(-1);
    await db.SaveChangesAsync();
    var expiredResolution = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId,
      CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.NoRecipient, expiredResolution.Status);
    Assert.Null(expiredResolution.ClientContactId);
    var expiredDispatch = await PracticeCrmService.RecordCorrespondenceDispatchAsync(db, partner, new RecordCorrespondenceDispatchRequest(
      seed.ClientId, null, CorrespondencePurposes.Commercial, mdContactId, "Proposal", "PROP-EXPIRED", 1, docHash));
    Assert.Equal(ErrorCodes.GateBlocked, expiredDispatch.ErrorCode);

    var futureContactId = Guid.NewGuid();
    db.ClientContacts.Add(new ClientContact
    {
      Id = futureContactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
      FullName = "Future Contact", Email = "future@client.test", Role = "Authorized Contact", IsActive = true,
      ValidFrom = DateTimeOffset.UtcNow.AddDays(1)
    });
    await db.SaveChangesAsync();
    var futureRouting = await PracticeCrmService.AssignContactRoutingAsync(db, partner,
      new AssignContactRoutingRequest(seed.ClientId, futureContactId, CorrespondencePurposes.Completion));
    Assert.True(futureRouting.Succeeded, futureRouting.Message);
    var futureResolution = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId,
      CorrespondencePurposes.Completion, today);
    Assert.Equal(RecipientResolutionStatus.NoRecipient, futureResolution.Status);
    var futureOverride = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId,
      CorrespondencePurposes.Completion, today, overrideContactId: futureContactId, overrideReason: "Future appointment only.");
    Assert.Equal(RecipientResolutionStatus.NoRecipient, futureOverride.Status);
    var futureDispatch = await PracticeCrmService.RecordCorrespondenceDispatchAsync(db, partner, new RecordCorrespondenceDispatchRequest(
      seed.ClientId, null, CorrespondencePurposes.Completion, futureContactId, "CompletionLetter", "PROP-FUTURE", 1, docHash));
    Assert.Equal(ErrorCodes.GateBlocked, futureDispatch.ErrorCode);
    Assert.Single(await db.CorrespondenceDispatchRecords.AsNoTracking().ToListAsync());
  }

  [Fact]
  public async Task ClientContactRouting_ReadsRequireContactOwnerToMatchRouteOwner()
  {
    await using var pg = await PgTestSchema.CreateAsync("20261024014728_FirmExpenseJournalPostingFirmScope");
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
    var siblingClientId = Guid.NewGuid();
    var validContactId = Guid.NewGuid();
    var siblingContactId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient
    {
      Id = siblingClientId, FirmId = seed.FirmId, LegalName = "Sibling Client", Status = "Active",
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientContacts.AddRange(
      new ClientContact
      {
        Id = validContactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        FullName = "Client A Contact", Email = "client-a@example.test", Role = "Manager", IsActive = true
      },
      new ClientContact
      {
        Id = siblingContactId, FirmId = seed.FirmId, PracticeClientId = siblingClientId,
        FullName = "Sibling Client Contact", Email = "sibling@example.test", Role = "Manager", IsActive = true
      });
    await db.SaveChangesAsync();

    var validRouting = await PracticeCrmService.AssignContactRoutingAsync(db, partner,
      new AssignContactRoutingRequest(seed.ClientId, validContactId, CorrespondencePurposes.Commercial,
        IsPrimaryForPurpose: false));
    Assert.True(validRouting.Succeeded, validRouting.Message);

    var siblingRoutingId = Guid.NewGuid();
    db.ClientContactRoutings.Add(new ClientContactRouting
    {
      Id = siblingRoutingId,
      FirmId = seed.FirmId,
      PracticeClientId = seed.ClientId,
      ClientContactId = siblingContactId,
      Purpose = CorrespondencePurposes.Commercial,
      IsPrimaryForPurpose = true,
      CreatedByUserId = partner.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var persistedSiblingRoute = await (
      from routing in db.ClientContactRoutings.AsNoTracking()
      join contact in db.ClientContacts.AsNoTracking() on routing.ClientContactId equals contact.Id
      where routing.Id == siblingRoutingId
      select new { routing.PracticeClientId, ContactClientId = contact.PracticeClientId, contact.Id }
    ).SingleAsync();
    Assert.Equal(seed.ClientId, persistedSiblingRoute.PracticeClientId);
    Assert.Equal(siblingClientId, persistedSiblingRoute.ContactClientId);

    var routings = await PracticeCrmService.GetClientContactRoutingsAsync(db, partner, seed.ClientId);
    Assert.True(routings.Succeeded, routings.Message);
    var routingRows = routings.Value!;
    var siblingListed = routingRows.Any(x => x.ClientContactId == siblingContactId);
    var validListed = routingRows.Any(x => x.ClientContactId == validContactId);
    var resolution = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId,
      CorrespondencePurposes.Commercial, DateOnly.FromDateTime(DateTime.UtcNow));
    Assert.Equal(RecipientResolutionStatus.Resolved, resolution.Status);
    Assert.Equal(validContactId, resolution.ClientContactId);
    var siblingResolved = resolution.Status == RecipientResolutionStatus.Resolved &&
      resolution.ClientContactId == siblingContactId;

    Assert.True(validListed && !siblingListed && !siblingResolved,
      $"Persisted an existing same-firm sibling-client contact against Client A's route. " +
      $"List exposed sibling={siblingListed}; resolver status={resolution.Status}; " +
      $"resolver returned sibling={siblingResolved}; valid same-client route listed={validListed}.");

    var invalidLegacyRouteMigration = await Assert.ThrowsAsync<PostgresException>(() => db.Database.MigrateAsync());
    Assert.Equal("23503", invalidLegacyRouteMigration.SqlState);
    Assert.Equal("fk_client_contact_routing_contact_scope", invalidLegacyRouteMigration.ConstraintName);

    await using var currentPg = await PgTestSchema.CreateAsync();
    var currentSeed = await PbcSeed.SeedAsync(currentPg);
    var foreignSeed = await PbcSeed.SeedAsync(currentPg);
    var currentPartner = PbcSeed.Actor(currentSeed.Admin, "Partner");
    var currentSiblingClientId = Guid.NewGuid();
    var currentContactId = Guid.NewGuid();
    var currentSiblingContactId = Guid.NewGuid();
    var foreignContactId = Guid.NewGuid();
    await using (var currentDb = new AuditSphereDbContext(currentPg.Options))
    {
      currentDb.RoleGrants.Add(PbcSeed.Grant(currentSeed.FirmId, currentSeed.Admin, "Partner"));
      currentDb.PracticeClients.Add(new PracticeClient
      {
        Id = currentSiblingClientId, FirmId = currentSeed.FirmId, LegalName = "Current Sibling Client",
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow
      });
      currentDb.ClientContacts.AddRange(
        new ClientContact
        {
          Id = currentContactId, FirmId = currentSeed.FirmId, PracticeClientId = currentSeed.ClientId,
          FullName = "Current Client A Contact", Email = "current-a@example.test", Role = "Manager", IsActive = true
        },
        new ClientContact
        {
          Id = currentSiblingContactId, FirmId = currentSeed.FirmId, PracticeClientId = currentSiblingClientId,
          FullName = "Current Sibling Contact", Email = "current-sibling@example.test", Role = "Manager", IsActive = true
        });
      await currentDb.SaveChangesAsync();
      var assigned = await PracticeCrmService.AssignContactRoutingAsync(currentDb, currentPartner,
        new AssignContactRoutingRequest(currentSeed.ClientId, currentContactId, CorrespondencePurposes.Commercial));
      Assert.True(assigned.Succeeded, assigned.Message);
    }

    await using (var foreignDb = new AuditSphereDbContext(currentPg.Options))
    {
      foreignDb.ClientContacts.Add(new ClientContact
      {
        Id = foreignContactId, FirmId = foreignSeed.FirmId, PracticeClientId = foreignSeed.ClientId,
        FullName = "Foreign Firm Contact", Email = "foreign-firm@example.test", Role = "Manager", IsActive = true
      });
      await foreignDb.SaveChangesAsync();
    }

    async Task<PostgresException> ExpectOwnerConstraintAsync(Guid contactId)
    {
      await using var attemptDb = new AuditSphereDbContext(currentPg.Options);
      attemptDb.ClientContactRoutings.Add(new ClientContactRouting
      {
        Id = Guid.NewGuid(), FirmId = currentSeed.FirmId, PracticeClientId = currentSeed.ClientId,
        ClientContactId = contactId, Purpose = CorrespondencePurposes.Completion,
        IsPrimaryForPurpose = true, CreatedByUserId = currentPartner.UserId, CreatedAt = DateTimeOffset.UtcNow
      });
      var failure = await Assert.ThrowsAsync<DbUpdateException>(() => attemptDb.SaveChangesAsync());
      return Assert.IsType<PostgresException>(failure.InnerException);
    }

    var siblingRejected = await ExpectOwnerConstraintAsync(currentSiblingContactId);
    Assert.Equal("23503", siblingRejected.SqlState);
    Assert.Equal("fk_client_contact_routing_contact_scope", siblingRejected.ConstraintName);
    var foreignFirmRejected = await ExpectOwnerConstraintAsync(foreignContactId);
    Assert.Equal("23503", foreignFirmRejected.SqlState);
    Assert.Equal("fk_client_contact_routing_contact_scope", foreignFirmRejected.ConstraintName);

    await using var currentReaderDb = new AuditSphereDbContext(currentPg.Options);
    var currentRoutings = await PracticeCrmService.GetClientContactRoutingsAsync(
      currentReaderDb, currentPartner, currentSeed.ClientId);
    Assert.True(currentRoutings.Succeeded, currentRoutings.Message);
    Assert.Equal(currentContactId, Assert.Single(currentRoutings.Value!).ClientContactId);
    var currentResolution = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(currentReaderDb,
      currentPartner, currentSeed.ClientId, CorrespondencePurposes.Commercial,
      DateOnly.FromDateTime(DateTime.UtcNow));
    Assert.Equal(RecipientResolutionStatus.Resolved, currentResolution.Status);
    Assert.Equal(currentContactId, currentResolution.ClientContactId);
  }

  [Theory]
  [InlineData("LIST")]
  [InlineData("ROUTED")]
  [InlineData("OVERRIDE")]
  public async Task RecipientResolution_RequiresCurrentCommercialScope_AndOverridesDoNotBypass(string readPath)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
    var contactId = Guid.NewGuid();
    db.ClientContacts.Add(new ClientContact
    {
      Id = contactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
      FullName = "Fatima Al-Kuwari", Email = "fatima.md@client.test", Role = "Managing Director", IsActive = true
    });
    await db.SaveChangesAsync();
    Assert.True((await PracticeCrmService.AssignContactRoutingAsync(db, partner,
      new AssignContactRoutingRequest(seed.ClientId, contactId, CorrespondencePurposes.Commercial))).Succeeded);

    var today = DateOnly.FromDateTime(DateTime.UtcNow);

    // A firm-wide commercial grant resolves the routing normally.
    var resolved = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, seed.ClientId, CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Resolved, resolved.Status);
    Assert.Equal(contactId, resolved.ClientContactId);
    var routings = await PracticeCrmService.GetClientContactRoutingsAsync(db, partner, seed.ClientId);
    Assert.True(routings.Succeeded, routings.Message);
    Assert.Equal(contactId, Assert.Single(routings.Value!).ClientContactId);

    // An engagement-scoped grant never covers client-level routing configuration.
    var engagementScoped = PbcSeed.User(seed.FirmId, "Staff");
    db.Users.Add(engagementScoped);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, engagementScoped, "Manager", clientId: seed.ClientId, engagementId: seed.EngagementId));
    await db.SaveChangesAsync();
    var engagementResolved = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db,
      PbcSeed.Actor(engagementScoped, "Manager"), seed.ClientId, CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Denied, engagementResolved.Status);
    Assert.Null(engagementResolved.ClientContactId);
    Assert.Null(engagementResolved.Email);

    // A current client-wide commercial grant resolves.
    var clientWide = PbcSeed.User(seed.FirmId, "Staff");
    db.Users.Add(clientWide);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, clientWide, "RelationshipManager", clientId: seed.ClientId));
    await db.SaveChangesAsync();
    var clientWideResolved = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db,
      PbcSeed.Actor(clientWide, "RelationshipManager"), seed.ClientId, CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Resolved, clientWideResolved.Status);
    Assert.Equal(contactId, clientWideResolved.ClientContactId);

    // A revoked grant is denied, and the override path never bypasses the scope check.
    var revokedUser = PbcSeed.User(seed.FirmId, "Staff");
    var revokedGrant = PbcSeed.Grant(seed.FirmId, revokedUser, "Manager", clientId: seed.ClientId);
    revokedGrant.RevokedAt = DateTimeOffset.UtcNow;
    db.Users.Add(revokedUser);
    db.RoleGrants.Add(revokedGrant);
    await db.SaveChangesAsync();
    var revokedActor = PbcSeed.Actor(revokedUser, "Manager");
    var revokedResolved = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, revokedActor, seed.ClientId, CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Denied, revokedResolved.Status);
    var revokedOverride = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, revokedActor, seed.ClientId,
      CorrespondencePurposes.Commercial, today, overrideContactId: contactId, overrideReason: "Board designation");
    Assert.Equal(RecipientResolutionStatus.Denied, revokedOverride.Status);
    Assert.Null(revokedOverride.ClientContactId);
    Assert.False(revokedOverride.WasOverridden);

    // Unknown and foreign client identifiers are refused without existence disclosure.
    var unknown = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, Guid.NewGuid(), CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Denied, unknown.Status);
    Assert.Null(unknown.ClientContactId);
    var foreignFirmClient = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = foreignFirmClient, FirmId = Guid.NewGuid(), LegalName = "Foreign Firm Client", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var foreign = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(db, partner, foreignFirmClient, CorrespondencePurposes.Commercial, today);
    Assert.Equal(RecipientResolutionStatus.Denied, foreign.Status);
    Assert.Null(foreign.ClientContactId);

    // A same-epoch grant revocation after the protected read must suppress its result as well.
    var racingUser = PbcSeed.User(seed.FirmId, "Staff");
    var racingGrant = PbcSeed.Grant(seed.FirmId, racingUser, "Manager", clientId: seed.ClientId);
    db.Users.Add(racingUser);
    db.RoleGrants.Add(racingGrant);
    await db.SaveChangesAsync();
    var racingActor = PbcSeed.Actor(racingUser, "Manager");
    var hook = new RevokeAfterProtectedRead(readPath == "OVERRIDE" ? "client_contacts" : "client_contact_routings", async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      Assert.Equal(1, await revoke.RoleGrants.Where(x => x.Id == racingGrant.Id)
        .ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow)));
    });
    await using var raced = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(hook).Options);
    if (readPath == "LIST")
    {
      var denied = await PracticeCrmService.GetClientContactRoutingsAsync(raced, racingActor, seed.ClientId);
      Assert.True(hook.Fired);
      Assert.Equal(racingActor.SessionEpoch, await db.Users.AsNoTracking().Where(x => x.Id == racingUser.Id)
        .Select(x => x.SessionEpoch).SingleAsync());
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
      Assert.Null(denied.Value);
    }
    else
    {
      var denied = await PracticeCrmService.ResolveCorrespondenceRecipientAsync(raced, racingActor, seed.ClientId,
        CorrespondencePurposes.Commercial, today, readPath == "OVERRIDE" ? contactId : null,
        readPath == "OVERRIDE" ? "Reviewed board designation" : null);
      Assert.True(hook.Fired);
      Assert.Equal(racingActor.SessionEpoch, await db.Users.AsNoTracking().Where(x => x.Id == racingUser.Id)
        .Select(x => x.SessionEpoch).SingleAsync());
      Assert.Equal(RecipientResolutionStatus.Denied, denied.Status);
      Assert.Null(denied.ClientContactId);
      Assert.Null(denied.FullName);
      Assert.Null(denied.Email);
      Assert.Null(denied.Role);
      Assert.Null(denied.RoutingId);
      Assert.False(denied.WasOverridden);
      Assert.DoesNotContain("Fatima", denied.Message ?? "");
    }
  }

  [Theory]
  [InlineData("SOURCE")]
  [InlineData("COUNTERPART")]
  [InlineData("CANDIDATE_COUNT")]
  public async Task OrganizationHierarchy_HidesCounterpartNodesOutsideCurrentCommercialScope(string revokedScope)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var admin = PbcSeed.Actor(seed.Admin, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));

    var visibleSubId = Guid.NewGuid();
    var hiddenSubId = Guid.NewGuid();
    var hiddenAffiliateId = Guid.NewGuid();
    db.PracticeClients.AddRange(
      new PracticeClient { Id = visibleSubId, FirmId = seed.FirmId, LegalName = "Visible Logistics W.L.L.", Status = "Active", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = hiddenSubId, FirmId = seed.FirmId, LegalName = "Restricted Real Estate Co.", Status = "Active", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = hiddenAffiliateId, FirmId = seed.FirmId, LegalName = "Restricted Energy JV", Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();

    Assert.True((await PracticeCrmService.CreateClientRelationshipAsync(db, admin,
      new CreateClientRelationshipRequest(seed.ClientId, visibleSubId, ClientRelationshipKinds.Parent))).Succeeded);
    Assert.True((await PracticeCrmService.CreateClientRelationshipAsync(db, admin,
      new CreateClientRelationshipRequest(seed.ClientId, hiddenSubId, ClientRelationshipKinds.Parent))).Succeeded);
    Assert.True((await PracticeCrmService.CreateClientRelationshipAsync(db, admin,
      new CreateClientRelationshipRequest(seed.ClientId, hiddenAffiliateId, ClientRelationshipKinds.Affiliate))).Succeeded);

    // The scoped manager covers the holding client and one subsidiary only.
    var scoped = PbcSeed.User(seed.FirmId, "Staff");
    db.Users.Add(scoped);
    var sourceGrant = PbcSeed.Grant(seed.FirmId, scoped, "Manager", clientId: seed.ClientId);
    var counterpartGrant = PbcSeed.Grant(seed.FirmId, scoped, "Manager", clientId: visibleSubId);
    db.RoleGrants.AddRange(sourceGrant, counterpartGrant);
    await db.SaveChangesAsync();
    var scopedActor = PbcSeed.Actor(scoped, "Manager");

    var relationshipCandidates = await PracticeCrmService.GetClientRelationshipCandidatesAsync(
      db, scopedActor, seed.ClientId, search: null, page: 0, pageSize: 50);
    Assert.True(relationshipCandidates.Succeeded, relationshipCandidates.Message);
    Assert.Equal(visibleSubId, Assert.Single(relationshipCandidates.Value!.Items).Id);

    var tree = await PracticeCrmService.GetClientHierarchyAsync(db, scopedActor, seed.ClientId);
    Assert.True(tree.Succeeded, tree.Message);
    Assert.Single(tree.Value!.Subsidiaries);
    Assert.Equal(visibleSubId, tree.Value.Subsidiaries[0].RelatedClientId);
    Assert.Equal("Visible Logistics W.L.L.", tree.Value.Subsidiaries[0].RelatedClientName);
    Assert.Empty(tree.Value.Affiliates);
    Assert.Empty(tree.Value.Parents);

    // Inaccessible counterpart nodes leave no name, identifier or existence disclosure anywhere.
    var serialized = System.Text.Json.JsonSerializer.Serialize(tree.Value);
    Assert.DoesNotContain(hiddenSubId.ToString("D"), serialized, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(hiddenAffiliateId.ToString("D"), serialized, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Restricted Real Estate Co.", serialized, StringComparison.Ordinal);
    Assert.DoesNotContain("Restricted Energy JV", serialized, StringComparison.Ordinal);

    // From the authorized subsidiary, the holding parent stays visible.
    var subTree = await PracticeCrmService.GetClientHierarchyAsync(db, scopedActor, visibleSubId);
    Assert.True(subTree.Succeeded);
    Assert.Single(subTree.Value!.Parents);
    Assert.Equal(seed.ClientId, subTree.Value.Parents[0].PrimaryClientId);

    var revokedGrantId = revokedScope == "SOURCE" ? sourceGrant.Id : counterpartGrant.Id;
    var hook = new RevokeAfterProtectedRead("practice_clients", async () =>
    {
      await using var revoke = new AuditSphereDbContext(pg.Options);
      Assert.Equal(1, await revoke.RoleGrants.Where(x => x.Id == revokedGrantId)
        .ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow)));
    }, requiredText: revokedScope == "CANDIDATE_COUNT" ? "count(*)" : "ANY");
    await using var raced = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(hook).Options);
    if (revokedScope == "CANDIDATE_COUNT")
    {
      // A revoked choice on an earlier page must not remain disclosed in Total on an empty later page.
      var currentCandidates = await PracticeCrmService.GetClientRelationshipCandidatesAsync(
        raced, scopedActor, seed.ClientId, search: null, page: 1, pageSize: 10);
      Assert.True(hook.Fired);
      Assert.Equal(scopedActor.SessionEpoch, await db.Users.AsNoTracking().Where(x => x.Id == scoped.Id)
        .Select(x => x.SessionEpoch).SingleAsync());
      Assert.False(currentCandidates.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, currentCandidates.ErrorCode);
      Assert.Null(currentCandidates.Value);
      return;
    }
    var currentTree = await PracticeCrmService.GetClientHierarchyAsync(raced, scopedActor, seed.ClientId);
    Assert.True(hook.Fired);
    Assert.Equal(scopedActor.SessionEpoch, await db.Users.AsNoTracking().Where(x => x.Id == scoped.Id)
      .Select(x => x.SessionEpoch).SingleAsync());
    if (revokedScope == "SOURCE")
    {
      Assert.False(currentTree.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, currentTree.ErrorCode);
      Assert.Null(currentTree.Value);
    }
    else
    {
      Assert.True(currentTree.Succeeded, currentTree.Message);
      Assert.Empty(currentTree.Value!.Subsidiaries);
      Assert.Empty(currentTree.Value.Parents);
      Assert.Empty(currentTree.Value.Affiliates);
      var currentJson = System.Text.Json.JsonSerializer.Serialize(currentTree.Value);
      Assert.DoesNotContain(visibleSubId.ToString("D"), currentJson, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("Visible Logistics W.L.L.", currentJson, StringComparison.Ordinal);
    }
  }

  [Theory]
  [InlineData("assign-routing")]
  [InlineData("revoke-routing")]
  [InlineData("update-contact")]
  [InlineData("create-contact-direct")]
  [InlineData("create-contact-workspace")]
  [Trait("Profile", "Database")]
  public async Task ContactRouteMutatorsWaitForTheFirmSafetyFence(string mutation)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(seed.Admin, "Administrator");
    var contactId = Guid.NewGuid();
    Guid routingId = Guid.Empty;
    ContactCreationRequest? reviewedCreation = null;

    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.ClientContacts.Add(new ClientContact
      {
        Id = contactId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        FullName = "Safety fence contact", Email = "safety-fence@example.test",
        Role = "Finance", IsActive = true
      });
      await setup.SaveChangesAsync();

      if (mutation == "revoke-routing")
      {
        var assigned = await PracticeCrmService.AssignContactRoutingAsync(setup, actor,
          new AssignContactRoutingRequest(seed.ClientId, contactId, CorrespondencePurposes.Commercial));
        Assert.True(assigned.Succeeded, assigned.Message);
        routingId = assigned.Value;
      }

      if (mutation == "create-contact-workspace")
      {
        var state = await ClientContactCreationWorkspace.StateAsync(setup, actor, seed.ClientId);
        Assert.True(state.Succeeded, state.Message);
        var request = new ContactCreationRequest(Guid.NewGuid(), state.Value!.ReviewBasis,
          new ContactCreationFields("Reviewed fence contact", "reviewed-fence@example.test", "Finance", false));
        var preview = await ClientContactCreationWorkspace.PreviewAsync(setup, actor, seed.ClientId, request);
        Assert.True(preview.Succeeded, preview.Message);
        reviewedCreation = request with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash };
      }
    }

    await using var blocker = new AuditSphereDbContext(pg.Options);
    await blocker.Database.OpenConnectionAsync();
    var blockerPid = ((NpgsqlConnection)blocker.Database.GetDbConnection()).ProcessID;
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {seed.FirmId} FOR UPDATE").AsNoTracking().SingleAsync();

    var probe = new FirmLockAttempt();
    var mutationOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(probe).Options;
    await using var mutator = new AuditSphereDbContext(mutationOptions);
    using var mutationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var mutationTask = ExecuteContactMutationAsync(mutation, mutator, actor, seed, contactId, routingId,
      reviewedCreation, mutationTimeout.Token);
    var rootAttempt = probe.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    if (await Task.WhenAny(rootAttempt, mutationTask) != rootAttempt)
    {
      await mutationTask;
      Assert.Fail($"{mutation} completed without first acquiring the firm safety fence.");
    }

    var waitingPid = await rootAttempt;
    await using var observer = new AuditSphereDbContext(pg.Options);
    await WaitForFirmBlockingPidAsync(observer, waitingPid, blockerPid);
    Assert.False(mutationTask.IsCompleted);

    await tx.CommitAsync();
    Assert.True(await mutationTask.WaitAsync(TimeSpan.FromSeconds(20)),
      $"{mutation} should complete successfully once the firm fence is released.");
  }

  private static async Task<(bool Succeeded, string? ErrorCode)> ExecuteClientMutationAsync(
    string mutation,
    AuditSphereDbContext db,
    ActorContext actor,
    PbcSeed.Fixture seed,
    Guid relatedClientId,
    Guid relationshipId,
    CancellationToken ct)
  {
    switch (mutation)
    {
      case "update-profile":
      {
        var result = await PracticeCrmService.UpdateClientProfileAsync(db, actor,
          new UpdateClientProfileRequest(seed.ClientId, "Unauthorized profile update", ExpectedGeneration: 1), ct);
        return (result.Succeeded, result.ErrorCode);
      }
      case "set-archived":
      {
        var result = await PracticeCrmService.SetClientArchivedAsync(db, actor,
          new SetClientArchiveRequest(seed.ClientId, true, ExpectedRevision: 0), ct);
        return (result.Succeeded, result.ErrorCode);
      }
      case "create-relationship":
      {
        var result = await PracticeCrmService.CreateClientRelationshipAsync(db, actor,
          new CreateClientRelationshipRequest(seed.ClientId, relatedClientId, ClientRelationshipKinds.Affiliate,
            OwnershipPercentage: 25m, Notes: "Unauthorized relationship"), ct);
        return (result.Succeeded, result.ErrorCode);
      }
      case "revoke-relationship":
      {
        var result = await PracticeCrmService.RevokeClientRelationshipAsync(db, actor,
          new RevokeClientRelationshipRequest(seed.ClientId, relationshipId, "Authority was revoked."), ct);
        return (result.Succeeded, result.ErrorCode);
      }
      default:
        throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown client mutation.");
    }
  }

  private static async Task<bool> ExecuteContactMutationAsync(
    string mutation,
    AuditSphereDbContext db,
    ActorContext actor,
    PbcSeed.Fixture seed,
    Guid contactId,
    Guid routingId,
    ContactCreationRequest? reviewedCreation,
    CancellationToken ct)
  {
    switch (mutation)
    {
      case "assign-routing":
        return (await PracticeCrmService.AssignContactRoutingAsync(db, actor,
          new AssignContactRoutingRequest(seed.ClientId, contactId, CorrespondencePurposes.Commercial), ct)).Succeeded;
      case "revoke-routing":
        return (await PracticeCrmService.RevokeContactRoutingAsync(db, actor,
          new RevokeContactRoutingRequest(seed.ClientId, routingId, "Recipient designation changed."), ct)).Succeeded;
      case "update-contact":
        return (await PracticeCrmService.UpdateClientContactAsync(db, actor,
          new UpdateClientContactRequest(contactId, "Updated safety fence contact", "updated-safety-fence@example.test",
            "Finance", IsActive: true), ct)).Succeeded;
      case "create-contact-direct":
        return (await PracticeCrmService.CreateClientContactAsync(db, actor,
          new CreateClientContactRequest(seed.ClientId, "Direct safety fence contact",
            "direct-safety-fence@example.test", "Finance"), ct)).Succeeded;
      case "create-contact-workspace":
        return reviewedCreation is not null &&
          (await ClientContactCreationWorkspace.ExecuteAsync(db, actor, seed.ClientId, reviewedCreation, ct)).Succeeded;
      default:
        throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown contact mutation.");
    }
  }

  private sealed record ClientProfileSnapshot(
    string LegalName, string? CommercialName, string? RegistrationNumber, string? TaxRegistrationNumber,
    string? EntityType, string? Jurisdiction, string? RestrictedProfile, string Status);

  private sealed record LifecycleSnapshot(
    Guid Id, long Revision, string EventKind, Guid ActorUserId, DateTimeOffset OccurredAt);

  private sealed record RelationshipSnapshot(
    Guid Id, Guid PrimaryClientId, Guid RelatedClientId, string RelationshipKind, decimal? OwnershipPercentage,
    string? Notes, Guid CreatedByUserId, DateTimeOffset CreatedAt, Guid? RevokedByUserId,
    DateTimeOffset? RevokedAt, string? RevocationReason);

  private static async Task WaitForAnyFirmLockBlockerAsync(AuditSphereDbContext observer, int waitingPid)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try
    {
      while (true)
      {
        await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", timeout.Token);
        var blocked = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database()
              AND waiting.pid = {waitingPid}
              AND waiting.wait_event_type = 'Lock'
              AND waiting.query ILIKE '%firm_safety_states%'
              AND cardinality(pg_blocking_pids(waiting.pid)) > 0
          ) AS "Value"
          """).SingleAsync(timeout.Token);
        if (blocked) return;
        await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
      }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
      Assert.Fail($"PostgreSQL did not show session {waitingPid} waiting on a firm-fence blocker.");
    }
  }

  private static async Task WaitForFirmBlockingPidAsync(
    AuditSphereDbContext observer, int waitingPid, int blockingPid)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try
    {
      while (true)
      {
        await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", timeout.Token);
        var blocked = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database()
              AND waiting.pid = {waitingPid}
              AND waiting.wait_event_type = 'Lock'
              AND waiting.query ILIKE '%firm_safety_states%'
              AND {blockingPid} = ANY(pg_blocking_pids(waiting.pid))
          ) AS "Value"
          """).SingleAsync(timeout.Token);
        if (blocked) return;
        await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
      }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
      Assert.Fail($"PostgreSQL did not show session {waitingPid} blocked by firm-fence holder {blockingPid}.");
    }
  }

  private sealed class RevokeAfterProtectedRead(string table, Func<Task> revoke, string? requiredText = null) : DbCommandInterceptor
  {
    private int fired;
    public bool Fired => Volatile.Read(ref fired) != 0;

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains(table, StringComparison.OrdinalIgnoreCase) &&
          (requiredText is null || command.CommandText.Contains(requiredText, StringComparison.OrdinalIgnoreCase)) &&
          Interlocked.CompareExchange(ref fired, 1, 0) == 0)
        await revoke();
      return result;
    }
  }

  private sealed class FirmLockAttempt : DbCommandInterceptor
  {
    public TaskCompletionSource<int> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
        Reached.TrySetResult(((NpgsqlConnection)command.Connection!).ProcessID);
      return ValueTask.FromResult(result);
    }
  }
}
