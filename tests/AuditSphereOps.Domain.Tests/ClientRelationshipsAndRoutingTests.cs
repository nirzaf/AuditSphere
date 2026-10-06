using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
    var revoke = await PracticeCrmService.RevokeClientRelationshipAsync(db, partner, new RevokeClientRelationshipRequest(
      rAff.Value, "Joint venture dissolved by mutual consent."));
    Assert.True(revoke.Succeeded);

    var treeAfterRevoke = await PracticeCrmService.GetClientHierarchyAsync(db, partner, holdingId);
    Assert.Empty(treeAfterRevoke.Value!.Affiliates);
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
  }

  [Fact]
  public async Task RecipientResolution_RequiresCurrentCommercialScope_AndOverridesDoNotBypass()
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
  }

  [Fact]
  public async Task OrganizationHierarchy_HidesCounterpartNodesOutsideCurrentCommercialScope()
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
    db.RoleGrants.AddRange(
      PbcSeed.Grant(seed.FirmId, scoped, "Manager", clientId: seed.ClientId),
      PbcSeed.Grant(seed.FirmId, scoped, "Manager", clientId: visibleSubId));
    await db.SaveChangesAsync();
    var scopedActor = PbcSeed.Actor(scoped, "Manager");

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
  }
}
