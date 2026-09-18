using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PracticeCrmTests
{
  private sealed record Fixture(Guid FirmId, AppUser User, ActorContext Actor);

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var user = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId,
      Subject = "crm-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-" + Guid.NewGuid().ToString("N"),
      Email = $"crm-{Guid.NewGuid():N}@example.test", DisplayName = "CRM operator",
      CreatedAt = DateTimeOffset.UtcNow
    };
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.Add(user);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = "RelationshipManager",
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
    });
    await db.SaveChangesAsync();
    return new Fixture(firmId, user, new ActorContext(user.Id, firmId, user.SessionEpoch, ["RelationshipManager"]));
  }

  private static CreateOpportunityRequest Opportunity(Guid leadId) =>
    new(leadId, "AccountingOnly", "DEMO-ENTITY", "2026-01-01", "2026-12-31", 12000m, "QAR", 60m,
      NextAction: "Complete discovery");

  private static ReviseProposalRequest Proposal(Guid opportunityId, long? expectedRevision = null) =>
    new(opportunityId, "ACCOUNTING-2026", "Monthly accounting and year-end package", "Tax filing",
      "Trial balance, management accounts and year-end package", "Client supplies source ledgers",
      12000m, "QAR", "2026-01-01", "2026-12-31", expectedRevision);

  [Fact]
  public async Task CommercialWorkflow_ConvertsIdempotently_AndLeavesAcceptancePending()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid leadId, opportunityId, proposalId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      leadId = (await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Example Trading LLC", "Referral", "A. Owner", "owner@example.test"))).Value;
      var duplicate = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("EXAMPLE TRADING LLC", "Web", "A. Owner", "OWNER@EXAMPLE.TEST"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal("crm.duplicate", duplicate.ErrorCode);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, leadId)).Succeeded);
      opportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId))).Value;
      proposalId = (await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId))).Value;
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, fixture.Actor, proposalId)).Succeeded);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, proposalId)).Succeeded);
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposalId,
        new ProposalResponseRequest(CrmStates.ProposalAccepted))).Succeeded);
    }

    Guid clientId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var conversion = await PracticeCrmService.ConvertToClientDraftAsync(db, fixture.Actor,
        new ConvertToClientDraftRequest(proposalId, "Example Trading LLC", Jurisdiction: "QA"));
      Assert.True(conversion.Succeeded);
      clientId = conversion.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var repeat = await PracticeCrmService.ConvertToClientDraftAsync(db, fixture.Actor,
        new ConvertToClientDraftRequest(proposalId, "EXAMPLE TRADING LLC"));
      Assert.True(repeat.Succeeded);
      Assert.Equal(clientId, repeat.Value);
      Assert.Equal(1, await db.PracticeClients.CountAsync(c => c.FirmId == fixture.FirmId));
      Assert.Equal(0, await db.Engagements.CountAsync(e => e.FirmId == fixture.FirmId));
      var acceptance = await db.AcceptanceDecisions.SingleAsync(a => a.PracticeClientId == clientId);
      Assert.Equal("Pending", acceptance.Decision);
      Assert.Null(acceptance.DecidedByUserId);
      Assert.Null(acceptance.DecidedAt);
      Assert.Equal(CrmStates.ClientProspect,
        (await db.PracticeClients.SingleAsync(c => c.Id == clientId)).Status);
      Assert.True(await db.ClientSafetyStates.AnyAsync(c => c.Id == clientId && c.FirmId == fixture.FirmId));
      Assert.Equal(clientId, (await db.Proposals.SingleAsync(p => p.Id == proposalId)).PracticeClientId);
      Assert.Equal(clientId, (await db.Opportunities.SingleAsync(o => o.Id == opportunityId)).PracticeClientId);
    }
  }

  [Fact]
  public async Task ClientContactCommand_IsScopedAndSerializesPrimaryContact()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var clientId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = clientId, FirmId = fixture.FirmId, LegalName = "Contact Client",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = fixture.FirmId });
      await db.SaveChangesAsync();

      var invalid = await PracticeCrmService.CreateClientContactAsync(db, fixture.Actor,
        new CreateClientContactRequest(clientId, "Contact", "not-an-email", "Finance"));
      Assert.False(invalid.Succeeded);
      Assert.Equal("crm.invalid", invalid.ErrorCode);

      var first = await PracticeCrmService.CreateClientContactAsync(db, fixture.Actor,
        new CreateClientContactRequest(clientId, "First", "first@example.test", "Finance", Primary: true));
      Assert.True(first.Succeeded);
      var second = await PracticeCrmService.CreateClientContactAsync(db, fixture.Actor,
        new CreateClientContactRequest(clientId, "Second", "second@example.test", "Director", Primary: true));
      Assert.True(second.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var contacts = await db.ClientContacts.Where(c => c.PracticeClientId == clientId).ToListAsync();
      Assert.Equal(2, contacts.Count);
      Assert.Single(contacts, c => c.Primary && c.Email == "second@example.test");
      Assert.DoesNotContain(contacts, c => c.Primary && c.Email == "first@example.test");
      Assert.Equal(3, (await db.ClientSafetyStates.SingleAsync(c => c.Id == clientId)).InputGeneration);

      var foreign = fixture.Actor with { FirmId = Guid.NewGuid() };
      var denied = await PracticeCrmService.CreateClientContactAsync(db, foreign,
        new CreateClientContactRequest(clientId, "Foreign", "foreign@example.test", "Finance"));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  [Fact]
  public async Task ProposalRevision_PreservesSentVersion_AndRejectsStaleRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid leadId, opportunityId, firstProposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      leadId = (await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Revision Client", "Referral"))).Value;
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, leadId);
      opportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId))).Value;
      firstProposalId = (await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId))).Value;
      await PracticeCrmService.ApproveProposalAsync(db, fixture.Actor, firstProposalId);
      await PracticeCrmService.SendProposalAsync(db, fixture.Actor, firstProposalId);
      var second = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId, 1));
      Assert.True(second.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var proposals = await db.Proposals.Where(p => p.OpportunityId == opportunityId)
        .OrderBy(p => p.Revision).ToListAsync();
      Assert.Equal(2, proposals.Count);
      Assert.Equal(CrmStates.ProposalSuperseded, proposals[0].Status);
      Assert.Equal(CrmStates.ProposalDraft, proposals[1].Status);
      Assert.Equal(firstProposalId, proposals[0].Id);
      Assert.Equal(2, proposals[1].Revision);

      var stale = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId, 1));
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, stale.ErrorCode);
    }
  }

  [Fact]
  public async Task CommercialCommands_EnforceOrder_AndCrossFirmActorIsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor, new CreateLeadRequest("Order Client", "Web"));
    Assert.True(lead.Succeeded);
    var beforeQualification = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
    Assert.False(beforeQualification.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, beforeQualification.ErrorCode);

    var foreign = fixture.Actor with { FirmId = Guid.NewGuid() };
    var denied = await PracticeCrmService.CreateLeadAsync(db, foreign, new CreateLeadRequest("Foreign", "Web"));
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

    db.FirmSafetyStates.Remove(await db.FirmSafetyStates.SingleAsync(s => s.Id == fixture.FirmId));
    await db.SaveChangesAsync();
    var missingGuard = await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value);
    Assert.False(missingGuard.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, missingGuard.ErrorCode);
  }

  [Fact]
  public async Task CommercialInputs_RejectInvalidDatesProbability_AndForeignOwner()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var foreignOwner = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Foreign Owner", "Web", OwnerUserId: Guid.NewGuid()));
    Assert.False(foreignOwner.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, foreignOwner.ErrorCode);

    var invalidContact = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Invalid Contact", "Web", PrimaryContactEmail: "not-an-email"));
    Assert.False(invalidContact.Succeeded);
    Assert.Equal("crm.invalid", invalidContact.ErrorCode);

    var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Date Client", "Web"));
    Assert.True(lead.Succeeded);
    Assert.True((await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value)).Succeeded);

    var invalidPeriod = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor,
      new CreateOpportunityRequest(lead.Value, "AccountingOnly", "ENTITY", "2026-12-31", "2026-01-01",
        100m, "QAR"));
    Assert.False(invalidPeriod.Succeeded);
    Assert.Equal("crm.invalid", invalidPeriod.ErrorCode);

    var invalidProbability = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor,
      new CreateOpportunityRequest(lead.Value, "AccountingOnly", "ENTITY", "2026-01-01", "2026-12-31",
        100m, "QAR", 100.0000001m));
    Assert.False(invalidProbability.Succeeded);
    Assert.Equal("crm.invalid", invalidProbability.ErrorCode);
  }

  [Fact]
  public async Task Conversion_RejectsConflictingCanonicalIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid proposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var client = new PracticeClient
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, LegalName = "Candidate LLC",
        RegistrationNumber = "QA-1", Jurisdiction = "QA", CreatedAt = DateTimeOffset.UtcNow
      };
      db.PracticeClients.Add(client);
      await db.SaveChangesAsync();

      var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Candidate LLC", "Referral"));
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunity.Value));
      await PracticeCrmService.ApproveProposalAsync(db, fixture.Actor, proposal.Value);
      await PracticeCrmService.SendProposalAsync(db, fixture.Actor, proposal.Value);
      await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposal.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted));
      proposalId = proposal.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await PracticeCrmService.ConvertToClientDraftAsync(db, fixture.Actor,
        new ConvertToClientDraftRequest(proposalId, "Candidate LLC", RegistrationNumber: "QA-2", Jurisdiction: "QA"));
      Assert.False(result.Succeeded);
      Assert.Equal("crm.duplicate", result.ErrorCode);
      Assert.Null((await db.Proposals.SingleAsync(p => p.Id == proposalId)).PracticeClientId);
    }
  }

  [Fact]
  public async Task Conversion_BindsAcceptanceToCurrentExistingClientGeneration()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid clientId, proposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      clientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient
      {
        Id = clientId, FirmId = fixture.FirmId, LegalName = "Existing Client",
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState
      {
        Id = clientId, FirmId = fixture.FirmId, InputGeneration = 4
      });
      await db.SaveChangesAsync();

      var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Existing Client", "Referral"));
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunity.Value));
      await PracticeCrmService.ApproveProposalAsync(db, fixture.Actor, proposal.Value);
      await PracticeCrmService.SendProposalAsync(db, fixture.Actor, proposal.Value);
      await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposal.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted));
      proposalId = proposal.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var result = await PracticeCrmService.ConvertToClientDraftAsync(db, fixture.Actor,
        new ConvertToClientDraftRequest(proposalId, "Existing Client"));
      Assert.True(result.Succeeded);
      Assert.Equal(clientId, result.Value);
      Assert.Equal(5, (await db.ClientSafetyStates.SingleAsync(x => x.Id == clientId)).InputGeneration);
      Assert.Equal(5, (await db.AcceptanceDecisions.SingleAsync(x => x.PracticeClientId == clientId)).Generation);
    }
  }
}
