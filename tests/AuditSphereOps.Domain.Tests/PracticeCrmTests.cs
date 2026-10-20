using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using System.Data.Common;
using System.Text.Json;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PracticeCrmTests
{
  private const string PreviousProposalMigration = "20260922140527_M365InvitationEvidenceAction";

  private sealed record Fixture(Guid FirmId, AppUser User, ActorContext Actor, ActorContext Reviewer);

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
    var reviewer = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId,
      Subject = "crm-reviewer-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-" + Guid.NewGuid().ToString("N"),
      Email = $"crm-reviewer-{Guid.NewGuid():N}@example.test", DisplayName = "CRM reviewer",
      CreatedAt = DateTimeOffset.UtcNow
    };
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.Users.AddRange(user, reviewer);
    db.RoleGrants.AddRange(
      new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = "RelationshipManager",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
      },
      new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = reviewer.Id, Role = "Partner",
        GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, user,
      new ActorContext(user.Id, firmId, user.SessionEpoch, ["RelationshipManager"]),
      new ActorContext(reviewer.Id, firmId, reviewer.SessionEpoch, ["Partner"]));
  }

  private static CreateOpportunityRequest Opportunity(Guid leadId) =>
    new(leadId, "AccountingOnly", "DEMO-ENTITY", "2026-01-01", "2026-12-31", 12000m, "QAR", 60m,
      NextAction: "Complete discovery");

  private static async Task<Guid> CreateQualifiedLeadAsync(
    AuditSphereDbContext db, ActorContext actor, string name)
  {
    var lead = await PracticeCrmService.CreateLeadAsync(db, actor, new CreateLeadRequest(name, "Referral"));
    Assert.True(lead.Succeeded, lead.Message);
    var qualification = await PracticeCrmService.QualifyLeadAsync(db, actor, lead.Value);
    Assert.True(qualification.Succeeded, qualification.Message);
    return lead.Value;
  }

  private static Opportunity DiscoveryOpportunity(Guid firmId, Guid leadId, Guid id, DateTimeOffset createdAt,
    string entityScope) => new()
  {
    Id = id, FirmId = firmId, LeadId = leadId, ServiceRoute = "AccountingOnly", EntityScope = entityScope,
    PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ExpectedFee = 12000m,
    Currency = "QAR", Probability = 60m, Stage = CrmStates.OpportunityDiscovery, CreatedAt = createdAt
  };

  private static ReviseProposalRequest Proposal(Guid opportunityId, long? expectedRevision = null) =>
    new(opportunityId, "ACCOUNTING-2026", "Monthly accounting and year-end package", "Tax filing",
      "Trial balance, management accounts and year-end package", "Client supplies source ledgers",
      12000m, "QAR", "2026-01-01", "2026-12-31", expectedRevision);

  [Fact]
  public async Task LeadCreation_ReconcilesExactRetry_AndRejectsRequestIdentityReuse()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var requestId = Guid.NewGuid();
    var request = new CreateLeadRequest("Idempotent Lead", "Referral", "Synthetic Contact",
      "lead@example.test", fixture.User.Id, RequestId: requestId);

    await using (var firstDb = new AuditSphereDbContext(pg.Options))
    {
      var first = await PracticeCrmService.CreateLeadAsync(firstDb, fixture.Actor, request);
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal(requestId, first.Value);
    }

    await using (var retryDb = new AuditSphereDbContext(pg.Options))
    {
      var retry = await PracticeCrmService.CreateLeadAsync(retryDb, fixture.Actor, request);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(requestId, retry.Value);
      Assert.Equal(1, await retryDb.Leads.CountAsync(x => x.FirmId == fixture.FirmId));
      Assert.Equal(1, await retryDb.LeadActivityEvents.CountAsync(x => x.FirmId == fixture.FirmId && x.LeadId == requestId));

      var changedIntent = await PracticeCrmService.CreateLeadAsync(retryDb, fixture.Actor,
        request with { Source = "Email" });
      Assert.False(changedIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedIntent.ErrorCode);

      var newIdentityDuplicate = await PracticeCrmService.CreateLeadAsync(retryDb, fixture.Actor,
        request with { RequestId = Guid.NewGuid() });
      Assert.False(newIdentityDuplicate.Succeeded);
      Assert.Equal("crm.duplicate", newIdentityDuplicate.ErrorCode);
      Assert.Equal(1, await retryDb.Leads.CountAsync(x => x.FirmId == fixture.FirmId));
    }
  }

  [Fact]
  public async Task LeadSourceMustBeASupportedChannelAndIsStoredCanonically()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var unsupported = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Unsupported source lead", "Carrier pigeon", RequestId: Guid.NewGuid()));
    Assert.False(unsupported.Succeeded);
    Assert.Equal("crm.invalid", unsupported.ErrorCode);
    Assert.Equal(0, await db.Leads.CountAsync(x => x.FirmId == fixture.FirmId));

    var canonical = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Canonical channel lead", "web form", RequestId: Guid.NewGuid()));
    Assert.True(canonical.Succeeded, canonical.Message);
    var stored = await db.Leads.AsNoTracking().SingleAsync(x => x.FirmId == fixture.FirmId && x.Name == "Canonical channel lead");
    Assert.Equal(LeadChannels.WebForm, stored.Source);
  }

  [Fact]
  public async Task OpportunityCreation_ReconcilesExactRetry_AndRejectsChangedRequestIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid leadId;
    await using (var setupDb = new AuditSphereDbContext(pg.Options))
    {
      var lead = await PracticeCrmService.CreateLeadAsync(setupDb, fixture.Actor,
        new("Opportunity recovery lead", "Referral"));
      Assert.True(lead.Succeeded, lead.Message);
      leadId = lead.Value;
      Assert.True((await PracticeCrmService.QualifyLeadAsync(setupDb, fixture.Actor, leadId)).Succeeded);
    }

    var requestId = Guid.NewGuid();
    var request = Opportunity(leadId) with { RequestId = requestId, OwnerUserId = fixture.User.Id };
    await using (var firstDb = new AuditSphereDbContext(pg.Options))
    {
      var first = await PracticeCrmService.CreateOpportunityAsync(firstDb, fixture.Actor, request);
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal(requestId, first.Value);
    }

    await using (var retryDb = new AuditSphereDbContext(pg.Options))
    {
      var retry = await PracticeCrmService.CreateOpportunityAsync(retryDb, fixture.Actor, request);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(requestId, retry.Value);
      Assert.Equal(1, await retryDb.Opportunities.CountAsync(x => x.LeadId == leadId));

      var changedIntent = await PracticeCrmService.CreateOpportunityAsync(retryDb, fixture.Actor,
        request with { EntityScope = "Changed entity" });
      Assert.False(changedIntent.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, changedIntent.ErrorCode);
      Assert.Equal(1, await retryDb.Opportunities.CountAsync(x => x.LeadId == leadId));
    }
  }

  [Theory]
  [InlineData(true)]
  [InlineData(false)]
  public async Task OpportunityCreation_RejectsActorRevokedWhileWaitingForFirmLock(bool revokeActor)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid leadId;
    Guid grantId;
    await using (var setupDb = new AuditSphereDbContext(pg.Options))
    {
      var lead = await PracticeCrmService.CreateLeadAsync(setupDb, fixture.Actor,
        new("Revocation wait lead", "Referral"));
      Assert.True(lead.Succeeded, lead.Message);
      leadId = lead.Value;
      Assert.True((await PracticeCrmService.QualifyLeadAsync(setupDb, fixture.Actor, leadId)).Succeeded);
      grantId = await setupDb.RoleGrants.Where(x => x.FirmId == fixture.FirmId &&
          x.UserId == fixture.User.Id && x.Role == "RelationshipManager" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync();
    }

    var requestId = Guid.NewGuid();
    var request = Opportunity(leadId) with { RequestId = requestId, OwnerUserId = fixture.Reviewer.UserId };
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {fixture.FirmId} FOR UPDATE").SingleAsync();

    var lockAttempt = new ObserveFirmLockAttempt();
    var writerOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(lockAttempt).Options;
    await using var writer = new AuditSphereDbContext(writerOptions);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var attempt = PracticeCrmService.CreateOpportunityAsync(writer, fixture.Actor, request, timeout.Token);
    await lockAttempt.Reached.Task.WaitAsync(timeout.Token);

    if (revokeActor)
    {
      await blocker.RoleGrants.Where(x => x.Id == grantId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token);
      await blocker.Users.Where(x => x.Id == fixture.User.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token);
    }
    else
    {
      await blocker.Users.Where(x => x.Id == fixture.Reviewer.UserId)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.Disabled, true), timeout.Token);
    }
    await blockerTransaction.CommitAsync(timeout.Token);

    var result = await attempt.WaitAsync(timeout.Token);
    Assert.True(lockAttempt.Fired);
    Assert.False(result.Succeeded);
    Assert.Equal(revokeActor ? ErrorCodes.GenerationStale : ErrorCodes.ScopeDenied, result.ErrorCode);

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Empty(await verify.Opportunities.Where(x => x.Id == requestId || x.LeadId == leadId).ToListAsync());
    Assert.False(await verify.LeadActivityEvents.AnyAsync(x => x.FirmId == fixture.FirmId &&
      x.LeadId == leadId && x.Action == LeadActivityActions.OpportunityCreated && x.OpportunityId == requestId));
  }

  [Theory]
  [InlineData("CREATE_LEAD")]
  [InlineData("CREATE_LEAD_OWNER_DISABLED")]
  [InlineData("CREATE_LEAD_GRANT_ONLY")]
  [InlineData("QUALIFY_LEAD")]
  [InlineData("REVISE_PROPOSAL")]
  [InlineData("APPROVE_PROPOSAL")]
  [InlineData("SEND_PROPOSAL")]
  [InlineData("RECORD_RESPONSE")]
  public async Task CommercialCoreWriter_RechecksAuthorityAfterFirmLockWait(string action)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var commandActor = action == "APPROVE_PROPOSAL" ? fixture.Reviewer : fixture.Actor;
    var commandRole = action == "APPROVE_PROPOSAL" ? "Partner" : "RelationshipManager";
    if (action is "APPROVE_PROPOSAL" or "CREATE_LEAD_OWNER_DISABLED" or "CREATE_LEAD_GRANT_ONLY")
      Assert.NotEqual(fixture.Actor.UserId, fixture.Reviewer.UserId);

    Guid leadId = default, opportunityId = default, proposalId = default;
    ProposalResponseRequest? responseRequest = null;
    Guid grantId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      if (action != "CREATE_LEAD" && action != "CREATE_LEAD_OWNER_DISABLED" && action != "CREATE_LEAD_GRANT_ONLY")
      {
        var createdLead = await PracticeCrmService.CreateLeadAsync(setup, fixture.Actor,
          new CreateLeadRequest("Lock-wait authority prospect", "Referral", "Synthetic Contact", "contact@example.test"));
        Assert.True(createdLead.Succeeded, createdLead.Message);
        leadId = createdLead.Value;
      }

      if (action is "REVISE_PROPOSAL" or "APPROVE_PROPOSAL" or "SEND_PROPOSAL" or "RECORD_RESPONSE")
      {
        var qualified = await PracticeCrmService.QualifyLeadAsync(setup, fixture.Actor, leadId);
        Assert.True(qualified.Succeeded, qualified.Message);
        var createdOpportunity = await PracticeCrmService.CreateOpportunityAsync(setup, fixture.Actor, Opportunity(leadId));
        Assert.True(createdOpportunity.Succeeded, createdOpportunity.Message);
        opportunityId = createdOpportunity.Value;
      }

      if (action is "APPROVE_PROPOSAL" or "SEND_PROPOSAL" or "RECORD_RESPONSE")
      {
        var drafted = await PracticeCrmService.ReviseProposalAsync(setup, fixture.Actor,
          Proposal(opportunityId, expectedRevision: 0) with { RequestId = Guid.NewGuid() });
        Assert.True(drafted.Succeeded, drafted.Message);
        proposalId = drafted.Value;

        if (action is "SEND_PROPOSAL" or "RECORD_RESPONSE")
        {
          var approved = await PracticeCrmService.ApproveProposalAsync(setup, fixture.Reviewer, proposalId);
          Assert.True(approved.Succeeded, approved.Message);
          await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(setup, fixture.Actor, proposalId);
          var sent = await PracticeCrmService.SendProposalAsync(setup, fixture.Actor, proposalId);
          Assert.True(sent.Succeeded, sent.Message);

          if (action == "RECORD_RESPONSE")
          {
            var offerSha = await setup.Proposals.AsNoTracking().Where(x => x.FirmId == fixture.FirmId && x.Id == proposalId)
              .Select(x => x.SentOfferSha256).SingleAsync();
            responseRequest = new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha,
              "Synthetic Contact", "contact@example.test", "synthetic written response",
              ProposalResponseChannels.Email);
          }
        }
      }

      grantId = await setup.RoleGrants.AsNoTracking().Where(x => x.FirmId == fixture.FirmId &&
          x.UserId == commandActor.UserId && x.Role == commandRole && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync();
    }

    async Task<string> SnapshotAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var leads = await db.Leads.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var activities = await db.LeadActivityEvents.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var opportunities = await db.Opportunities.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var proposals = await db.Proposals.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var quotations = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var quotationApprovals = await db.QuotationApprovals.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var quotationRevocations = await db.QuotationApprovalRevocations.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var documents = await db.CommercialDocuments.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var notifications = await db.CommercialNotifications.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      var responses = await db.ProposalResponseRecords.AsNoTracking().Where(x => x.FirmId == fixture.FirmId)
        .OrderBy(x => x.Id).ToArrayAsync();
      return JsonSerializer.Serialize(new
      {
        leads, activities, opportunities, proposals, quotations, quotationApprovals,
        quotationRevocations, documents, notifications, responses
      });
    }

    var before = await SnapshotAsync();
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {fixture.FirmId} FOR UPDATE").SingleAsync();

    var lockAttempt = new ObserveFirmLockAttempt();
    var writerOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(lockAttempt).Options;
    await using var writer = new AuditSphereDbContext(writerOptions);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    async Task<(bool Succeeded, string? ErrorCode, Guid? Value)> ExecuteWriterAsync()
    {
      switch (action)
      {
        case "CREATE_LEAD":
        case "CREATE_LEAD_OWNER_DISABLED":
        case "CREATE_LEAD_GRANT_ONLY":
        {
          var result = await PracticeCrmService.CreateLeadAsync(writer, commandActor,
            new CreateLeadRequest("Lock-wait new lead", "Referral", "Synthetic Contact", "contact@example.test",
              OwnerUserId: action is "CREATE_LEAD_OWNER_DISABLED" or "CREATE_LEAD_GRANT_ONLY" ? fixture.Reviewer.UserId : null,
              RequestId: Guid.NewGuid()), timeout.Token);
          return (result.Succeeded, result.ErrorCode, result.Value);
        }
        case "QUALIFY_LEAD":
        {
          var result = await PracticeCrmService.QualifyLeadAsync(writer, commandActor, leadId, timeout.Token);
          return (result.Succeeded, result.ErrorCode, null);
        }
        case "REVISE_PROPOSAL":
        {
          var result = await PracticeCrmService.ReviseProposalAsync(writer, commandActor,
            Proposal(opportunityId, expectedRevision: 0) with { RequestId = Guid.NewGuid() }, timeout.Token);
          return (result.Succeeded, result.ErrorCode, result.Value);
        }
        case "APPROVE_PROPOSAL":
        {
          var result = await PracticeCrmService.ApproveProposalAsync(writer, commandActor, proposalId, timeout.Token);
          return (result.Succeeded, result.ErrorCode, null);
        }
        case "SEND_PROPOSAL":
        {
          var result = await PracticeCrmService.SendProposalAsync(writer, commandActor, proposalId, timeout.Token);
          return (result.Succeeded, result.ErrorCode, null);
        }
        case "RECORD_RESPONSE":
        {
          var result = await PracticeCrmService.RecordProposalResponseAsync(writer, commandActor, proposalId,
            responseRequest!, timeout.Token);
          return (result.Succeeded, result.ErrorCode, null);
        }
        default:
          throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown commercial writer.");
      }
    }

    var attempt = ExecuteWriterAsync();
    await lockAttempt.Reached.Task.WaitAsync(timeout.Token);
    Assert.True(lockAttempt.Fired);
    if (action == "CREATE_LEAD_OWNER_DISABLED")
    {
      Assert.Equal(1, await blocker.Users.Where(x => x.Id == fixture.Reviewer.UserId && !x.Disabled)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.Disabled, true), timeout.Token));
    }
    else
    {
      Assert.Equal(1, await blocker.RoleGrants.Where(x => x.Id == grantId && x.RevokedAt == null)
        .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow), timeout.Token));
      if (action != "CREATE_LEAD_GRANT_ONLY")
        Assert.Equal(1, await blocker.Users.Where(x => x.Id == commandActor.UserId)
          .ExecuteUpdateAsync(update => update.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1), timeout.Token));
    }
    await blockerTransaction.CommitAsync(timeout.Token);

    var result = await attempt.WaitAsync(timeout.Token);
    Assert.False(result.Succeeded);
    Assert.Equal(action is "CREATE_LEAD_OWNER_DISABLED" or "CREATE_LEAD_GRANT_ONLY"
        ? ErrorCodes.ScopeDenied : ErrorCodes.GenerationStale,
      result.ErrorCode);
    Assert.True(result.Value is null || result.Value == Guid.Empty);
    Assert.Equal(before, await SnapshotAsync());
  }

  [Fact]
  public async Task ProposalTimeline_IsStoredTrimmedAndOptional()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    async Task<Proposal> Create(string leadName, string timeline)
    {
      var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor, new(leadName, "Referral"));
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      var created = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor,
        Proposal(opportunity.Value, expectedRevision: 0) with { RequestId = Guid.NewGuid(), Timeline = timeline });
      Assert.True(created.Succeeded, created.Message);
      return await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == created.Value);
    }

    var offered = await Create("Timeline lead", "  Fieldwork in March; report by 30 April  ");
    Assert.Equal("Fieldwork in March; report by 30 April", offered.Timeline);
    var withoutTimeline = await Create("No timeline lead", "");
    Assert.Equal(string.Empty, withoutTimeline.Timeline);
  }

  [Fact]
  public async Task ProposalCreation_ReconcilesExactRetry_AndRejectsChangedRequestIdentity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid opportunityId;
    await using (var setupDb = new AuditSphereDbContext(pg.Options))
    {
      var lead = await PracticeCrmService.CreateLeadAsync(setupDb, fixture.Actor,
        new("Proposal recovery lead", "Referral"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(setupDb, fixture.Actor, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(setupDb, fixture.Actor,
        Opportunity(lead.Value));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      opportunityId = opportunity.Value;
    }

    var requestId = Guid.NewGuid();
    var request = Proposal(opportunityId, expectedRevision: 0) with { RequestId = requestId };
    await using (var firstDb = new AuditSphereDbContext(pg.Options))
    {
      var first = await PracticeCrmService.ReviseProposalAsync(firstDb, fixture.Actor, request);
      Assert.True(first.Succeeded, first.Message);
      Assert.Equal(requestId, first.Value);
    }

    await using (var quotationDb = new AuditSphereDbContext(pg.Options))
    {
      // QuotationService may revise this field after proposal creation; it must not rewrite the request receipt.
      var proposal = await quotationDb.Proposals.SingleAsync(x => x.Id == requestId);
      proposal.Fee = 13000m;
      await quotationDb.SaveChangesAsync();
      Assert.Matches("^[a-f0-9]{64}$", proposal.CreateRequestHash!);
      var hashMutation = await Assert.ThrowsAsync<PostgresException>(() => quotationDb.Database
        .ExecuteSqlInterpolatedAsync($"UPDATE proposals SET create_request_hash = {new string('0', 64)} WHERE id = {requestId}"));
      Assert.Equal("P0001", hashMutation.SqlState);
      var deletion = await Assert.ThrowsAsync<PostgresException>(() => quotationDb.Database
        .ExecuteSqlInterpolatedAsync($"DELETE FROM proposals WHERE id = {requestId}"));
      Assert.Equal("P0001", deletion.SqlState);
    }

    await using (var retryDb = new AuditSphereDbContext(pg.Options))
    {
      var retry = await PracticeCrmService.ReviseProposalAsync(retryDb, fixture.Actor, request);
      Assert.True(retry.Succeeded, retry.Message);
      Assert.Equal(requestId, retry.Value);
      Assert.Equal(1, await retryDb.Proposals.CountAsync(x => x.OpportunityId == opportunityId));

      var changedIntent = await PracticeCrmService.ReviseProposalAsync(retryDb, fixture.Actor,
        request with { Deliverables = "Changed deliverables" });
      Assert.False(changedIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedIntent.ErrorCode);

      // The timeline is an offered term: the same request identity with a timeline added is a different offer.
      var addedTimeline = await PracticeCrmService.ReviseProposalAsync(retryDb, fixture.Actor,
        request with { Timeline = "Fieldwork in March; report by 30 April" });
      Assert.Equal(ErrorCodes.IdempotencyConflict, addedTimeline.ErrorCode);
      Assert.Equal(string.Empty, (await retryDb.Proposals.AsNoTracking().SingleAsync(x => x.Id == requestId)).Timeline);
      var tooLong = await PracticeCrmService.ReviseProposalAsync(retryDb, fixture.Actor,
        request with { RequestId = Guid.NewGuid(), ExpectedRevision = 1, Timeline = new string('x', PracticeCrmService.MaxProposalTimelineLength + 1) });
      Assert.False(tooLong.Succeeded);
      Assert.Equal(1, await retryDb.Proposals.CountAsync(x => x.OpportunityId == opportunityId));

      var changedFeeMatchingMutableProposal = await PracticeCrmService.ReviseProposalAsync(retryDb, fixture.Actor,
        request with { Fee = 13000m });
      Assert.False(changedFeeMatchingMutableProposal.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedFeeMatchingMutableProposal.ErrorCode);

      var staleNewRequest = await PracticeCrmService.ReviseProposalAsync(retryDb, fixture.Actor,
        request with { RequestId = Guid.NewGuid() });
      Assert.False(staleNewRequest.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleNewRequest.ErrorCode);
      Assert.Equal(1, await retryDb.Proposals.CountAsync(x => x.OpportunityId == opportunityId));

      var migrations = retryDb.Database.GetMigrations().ToArray();
      var fingerprintMigration = Array.FindIndex(migrations,
        id => id.EndsWith("_CommercialProposalCreateRequestFingerprint", StringComparison.Ordinal));
      Assert.True(fingerprintMigration > 0);
      var refusedMigration = await Assert.ThrowsAnyAsync<Exception>(() => retryDb.GetService<IMigrator>()
        .MigrateAsync(migrations[fingerprintMigration - 1]));
      var postgresRefusal = Assert.IsType<PostgresException>(refusedMigration.GetBaseException());
      Assert.Equal("55000", postgresRefusal.SqlState);
      Assert.Equal(1, await retryDb.Proposals.CountAsync(x => x.OpportunityId == opportunityId));
    }
  }

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
        new CreateLeadRequest("EXAMPLE TRADING LLC", "Web Form", "A. Owner", "OWNER@EXAMPLE.TEST"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal("crm.duplicate", duplicate.ErrorCode);
      var prematureOpportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId));
      Assert.False(prematureOpportunity.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, prematureOpportunity.ErrorCode);
      Assert.Equal(1, await db.LeadActivityEvents.CountAsync(x => x.FirmId == fixture.FirmId && x.LeadId == leadId));
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, leadId)).Succeeded);
      opportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId))).Value;
      proposalId = (await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId))).Value;
      var prematureConversion = await PracticeCrmService.ConvertToClientDraftAsync(db, fixture.Actor,
        new ConvertToClientDraftRequest(proposalId, "Example Trading LLC", Jurisdiction: "QA"));
      Assert.False(prematureConversion.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, prematureConversion.ErrorCode);
      Assert.Empty(await db.PracticeClients.Where(x => x.FirmId == fixture.FirmId).ToListAsync());
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, proposalId)).Succeeded);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, proposalId);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, proposalId)).Succeeded);
      var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).SentOfferSha256;
      // STE v2.2 section 4.1.3 (ADR-0017): an acceptance records the channel it arrived by, from the approved set only.
      Assert.Equal("crm.invalid", (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposalId,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@example.test", "Signed acceptance letter"))).ErrorCode);
      Assert.Equal("crm.invalid", (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposalId,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@example.test", "Signed acceptance letter", Channel: "WHATSAPP"))).ErrorCode);
      Assert.Equal(CrmStates.ProposalSent, (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).Status);
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposalId,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "A. Owner", "owner@example.test", "Signed acceptance letter", Channel: "signed_letter"))).Succeeded);
      Assert.Equal(ProposalResponseChannels.SignedLetter, (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposalId)).ResponseChannel);
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
      // Conversion is the portal trigger: one primary contact and one intent, which grants no access by itself.
      var intent = await db.ClientPortalIntents.SingleAsync(x => x.PracticeClientId == clientId);
      Assert.Equal((AuditSphereOps.Domain.Documents.ClientPortalIntentStates.AwaitingAcceptance, "owner@example.test"), (intent.State, intent.RecipientEmail));
      Assert.True((await db.ClientContacts.SingleAsync(x => x.PracticeClientId == clientId)).Primary);
      Assert.False(await db.RoleGrants.AnyAsync(x => x.ClientId == clientId && x.Role == "ClientUser"));

      var leadWorkspace = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, leadId);
      Assert.True(leadWorkspace.Succeeded);
      Assert.Equal("Referral", leadWorkspace.Value!.Source);
      Assert.Equal(CrmStates.LeadQualified, leadWorkspace.Value.Status);
      Assert.False(leadWorkspace.Value.HistoryHasMore);
      Assert.Equal(
        new[] { LeadActivityActions.Created, LeadActivityActions.Qualified, LeadActivityActions.OpportunityCreated,
          LeadActivityActions.ProposalRevisionCreated, LeadActivityActions.ProposalReviewed,
          LeadActivityActions.ProposalMarkedSent, LeadActivityActions.ProposalAccepted,
          LeadActivityActions.ClientConversionLinked },
        leadWorkspace.Value.History.Select(x => x.Action).ToArray());
      var conversionEvent = leadWorkspace.Value.History.Single(x => x.Action == LeadActivityActions.ClientConversionLinked);
      Assert.Equal(clientId, conversionEvent.ClientId);
      Assert.Equal(proposalId, conversionEvent.ProposalId);
      Assert.Equal(opportunityId, conversionEvent.OpportunityId);
      var updateError = await Assert.ThrowsAnyAsync<Exception>(() => db.LeadActivityEvents
        .Where(x => x.Id == conversionEvent.Id)
        .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ToState, "MUTATED")));
      Assert.Equal("55000", Assert.IsType<PostgresException>(updateError.GetBaseException()).SqlState);
      var deleteError = await Assert.ThrowsAnyAsync<Exception>(() => db.LeadActivityEvents
        .Where(x => x.Id == conversionEvent.Id)
        .ExecuteDeleteAsync());
      Assert.Equal("55000", Assert.IsType<PostgresException>(deleteError.GetBaseException()).SqlState);
    }
  }

  [Fact]
  public async Task LostLeadCannotBeQualifiedOrStartAServiceOpportunity()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid leadId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      leadId = (await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Closed prospect LLC", "Referral"))).Value;
      var lead = await db.Leads.SingleAsync(x => x.Id == leadId && x.FirmId == fixture.FirmId);
      lead.Status = CrmStates.LeadLost;
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var qualification = await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, leadId);
      Assert.False(qualification.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, qualification.ErrorCode);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId));
      Assert.False(opportunity.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, opportunity.ErrorCode);
      Assert.Empty(await db.Opportunities.Where(x => x.FirmId == fixture.FirmId && x.LeadId == leadId).ToListAsync());
      Assert.Empty(await db.PracticeClients.Where(x => x.FirmId == fixture.FirmId).ToListAsync());
      Assert.Empty(await db.Engagements.Where(x => x.FirmId == fixture.FirmId).ToListAsync());
      Assert.Empty(await db.ClientPortalIntents.Where(x => x.FirmId == fixture.FirmId).ToListAsync());
      Assert.False(await db.RoleGrants.AnyAsync(x => x.FirmId == fixture.FirmId && x.Role == "ClientUser"));
    }
  }

  [Fact]
  public async Task LeadActivityHistory_IsBoundedAndPagesOlderEventsInStableOrder()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Long-running prospect LLC", "Referral"));
    Assert.True(lead.Succeeded, lead.Message);

    var firstOccurredAt = DateTimeOffset.UtcNow.AddDays(-201);
    db.LeadActivityEvents.AddRange(Enumerable.Range(0, 201).Select(index => new LeadActivityEvent
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, LeadId = lead.Value,
      Action = LeadActivityActions.Qualified, OccurredAt = firstOccurredAt.AddMinutes(index),
      ActorUserId = fixture.User.Id, FromState = CrmStates.LeadNew, ToState = CrmStates.LeadQualified
    }));
    await db.SaveChangesAsync();

    var firstPage = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, lead.Value);
    Assert.True(firstPage.Succeeded, firstPage.Message);
    Assert.Equal(200, firstPage.Value!.History.Count);
    Assert.True(firstPage.Value.HistoryHasMore);

    var oldest = firstPage.Value.History[0];
    var olderPage = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, lead.Value,
      oldest.OccurredAt, oldest.Id);
    Assert.True(olderPage.Succeeded, olderPage.Message);
    Assert.Equal(2, olderPage.Value!.History.Count);
    Assert.False(olderPage.Value.HistoryHasMore);
    Assert.All(olderPage.Value.History, item => Assert.True(item.OccurredAt < oldest.OccurredAt));

    var combinedIds = olderPage.Value.History.Concat(firstPage.Value.History).Select(x => x.Id).ToArray();
    Assert.Equal(202, combinedIds.Length);
    Assert.Equal(202, combinedIds.Distinct().Count());
    var invalidCursor = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, lead.Value,
      oldest.OccurredAt, null);
    Assert.False(invalidCursor.Succeeded);
    Assert.Equal("crm.invalid", invalidCursor.ErrorCode);
  }

  [Fact]
  public async Task LeadOpportunities_PageThroughAllRowsWithScopedStableCursor()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var leadId = await CreateQualifiedLeadAsync(db, fixture.Actor, "Paged discovery prospect");
    var createdAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(1_234_560);
    var expectedIds = Enumerable.Range(1, 201)
      .Select(index => Guid.Parse($"00000000-0000-0000-0000-{index:D12}"))
      .ToArray();
    db.Opportunities.AddRange(expectedIds.Select((id, index) => DiscoveryOpportunity(
      fixture.FirmId, leadId, id, createdAt, $"DISCOVERY-{index + 1:D3}")));
    await db.SaveChangesAsync();

    var pages = new List<LeadWorkspace>();
    var firstPage = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, leadId);
    Assert.True(firstPage.Succeeded, firstPage.Message);
    pages.Add(firstPage.Value!);
    Assert.Equal(100, pages[0].Opportunities.Count);
    Assert.True(pages[0].OpportunitiesHasMore);

    for (var pageNumber = 1; pageNumber < 2; pageNumber++)
    {
      var previousPage = pages[^1];
      var cursor = previousPage.Opportunities[^1];
      var nextPage = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, leadId,
        opportunityBeforeCreatedAt: cursor.CreatedAt, opportunityBeforeId: cursor.Id);
      Assert.True(nextPage.Succeeded, nextPage.Message);
      if (pageNumber == 1)
      {
        var offsetEquivalentPage = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, leadId,
          opportunityBeforeCreatedAt: cursor.CreatedAt.ToOffset(TimeSpan.FromHours(3)),
          opportunityBeforeId: cursor.Id);
        Assert.True(offsetEquivalentPage.Succeeded, offsetEquivalentPage.Message);
        Assert.Equal(nextPage.Value!.Opportunities.ToArray(),
          offsetEquivalentPage.Value!.Opportunities.ToArray());
        Assert.Equal(nextPage.Value.OpportunitiesHasMore, offsetEquivalentPage.Value.OpportunitiesHasMore);
      }
      pages.Add(nextPage.Value!);
      Assert.Equal(100, pages[pageNumber].Opportunities.Count);
      Assert.True(pages[pageNumber].OpportunitiesHasMore);
    }

    var lastCursor = pages[^1].Opportunities[^1];
    var lastPage = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, leadId,
      opportunityBeforeCreatedAt: lastCursor.CreatedAt, opportunityBeforeId: lastCursor.Id);
    Assert.True(lastPage.Succeeded, lastPage.Message);
    pages.Add(lastPage.Value!);
    Assert.Single(pages[^1].Opportunities);
    Assert.False(pages[^1].OpportunitiesHasMore);

    var allOpportunities = pages.SelectMany(page => page.Opportunities).ToArray();
    Assert.Equal(expectedIds, allOpportunities.Select(item => item.Id).ToArray());
    Assert.Equal(201, allOpportunities.Select(item => item.Id).Distinct().Count());
    Assert.All(allOpportunities, item =>
    {
      Assert.Equal(createdAt, item.CreatedAt);
      Assert.Equal(1_234_560, item.CreatedAt.Ticks % TimeSpan.TicksPerSecond);
    });

    // Advancing the opportunity cursor must leave the independently paged lead history intact.
    var historyIds = pages[0].History.Select(item => item.Id).ToArray();
    Assert.NotEmpty(historyIds);
    Assert.All(pages, page =>
    {
      Assert.Equal(historyIds, page.History.Select(item => item.Id).ToArray());
      Assert.False(page.HistoryHasMore);
    });
  }

  [Fact]
  public async Task LeadOpportunities_CursorsAreCompleteScopedAndFirmWide()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var foreignFixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var leadId = await CreateQualifiedLeadAsync(db, fixture.Actor, "Cursor target prospect");
    var sameFirmOtherLeadId = await CreateQualifiedLeadAsync(db, fixture.Actor, "Same firm other prospect");
    var foreignLeadId = await CreateQualifiedLeadAsync(db, foreignFixture.Actor, "Foreign firm prospect");
    var createdAt = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);
    var targetCursorId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    var sameFirmCursorId = Guid.Parse("00000000-0000-0000-0000-000000000102");
    var foreignCursorId = Guid.Parse("00000000-0000-0000-0000-000000000103");
    db.Opportunities.AddRange(
      DiscoveryOpportunity(fixture.FirmId, leadId, targetCursorId, createdAt, "TARGET"),
      DiscoveryOpportunity(fixture.FirmId, sameFirmOtherLeadId, sameFirmCursorId, createdAt, "SAME-FIRM"),
      DiscoveryOpportunity(foreignFixture.FirmId, foreignLeadId, foreignCursorId, createdAt, "FOREIGN-FIRM"));
    await db.SaveChangesAsync();

    async Task AssertInvalidCursor(DateTimeOffset? beforeCreatedAt, Guid? beforeId)
    {
      var result = await CommercialWorkspaceQuery.LeadAsync(db, fixture.Actor, leadId,
        opportunityBeforeCreatedAt: beforeCreatedAt, opportunityBeforeId: beforeId);
      Assert.False(result.Succeeded);
      Assert.Equal("crm.invalid", result.ErrorCode);
    }

    await AssertInvalidCursor(createdAt, null);
    await AssertInvalidCursor(null, targetCursorId);
    await AssertInvalidCursor(createdAt, Guid.Empty);
    await AssertInvalidCursor(createdAt.AddSeconds(1), targetCursorId);
    await AssertInvalidCursor(createdAt, sameFirmCursorId);
    await AssertInvalidCursor(createdAt, foreignCursorId);

    var scopedClientId = Guid.NewGuid();
    var scopedUser = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      Subject = "crm-opportunity-page-scoped-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-test", Email = $"scoped-{Guid.NewGuid():N}@example.test",
      DisplayName = "Scoped CRM manager", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };
    db.PracticeClients.Add(new PracticeClient
    {
      Id = scopedClientId, FirmId = fixture.FirmId, LegalName = "Scoped CRM client",
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.Users.Add(scopedUser);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = scopedUser.Id,
      Role = "RelationshipManager", ClientId = scopedClientId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.User.Id
    });
    await db.SaveChangesAsync();

    var scopedResult = await CommercialWorkspaceQuery.LeadAsync(db,
      new ActorContext(scopedUser.Id, fixture.FirmId, scopedUser.SessionEpoch, ["RelationshipManager"]), leadId);
    Assert.False(scopedResult.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, scopedResult.ErrorCode);
  }

  [Fact]
  public async Task ClientScopedRelationshipManager_CannotCreateFirmWideLead()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var scoped = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      Subject = "crm-scoped-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-test",
      Email = $"scoped-{Guid.NewGuid():N}@example.test", DisplayName = "Scoped manager",
      UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };

    await using var db = new AuditSphereDbContext(pg.Options);
    var clientId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = fixture.FirmId, LegalName = "Scoped client", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Users.Add(scoped);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = scoped.Id, Role = "RelationshipManager",
      ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.User.Id
    });
    await db.SaveChangesAsync();

    var result = await PracticeCrmService.CreateLeadAsync(db,
      new ActorContext(scoped.Id, fixture.FirmId, scoped.SessionEpoch, ["RelationshipManager"]),
      new CreateLeadRequest("Unauthorized firm-wide lead", "Referral"));

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Empty(await db.Leads.ToListAsync());
  }

  [Fact]
  public async Task ClientClassifiedRelationshipManager_CannotCreateFirmWideLead()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var client = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId,
      Subject = "crm-client-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-test",
      Email = $"client-{Guid.NewGuid():N}@example.test", DisplayName = "Client identity",
      UserKind = "Client", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };

    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(client);
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = client.Id, Role = "RelationshipManager",
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.User.Id
    });
    await db.SaveChangesAsync();

    var result = await PracticeCrmService.CreateLeadAsync(db,
      new ActorContext(client.Id, fixture.FirmId, client.SessionEpoch, ["RelationshipManager"]),
      new CreateLeadRequest("Client-classified firm lead", "Referral"));

    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.Empty(await db.Leads.ToListAsync());
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
        new CreateLeadRequest("Revision Client", "Referral", "R. Owner", "revision-owner@example.test"))).Value;
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, leadId);
      opportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId))).Value;
      firstProposalId = (await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId))).Value;
      await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, firstProposalId);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, firstProposalId);
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
  public async Task ProposalDispatch_BindsExactOffer_AndAcceptanceMustCiteIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid leadId, opportunityId, firstProposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // A proposal cannot be dispatched without an exact recipient (STE 4.1.2): no email, no send.
      leadId = (await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Offer Binding Client", "Referral"))).Value;
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, leadId);
      opportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(leadId))).Value;
      firstProposalId = (await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId))).Value;
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, firstProposalId)).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await PracticeCrmService.SendProposalAsync(db, fixture.Actor, firstProposalId)).ErrorCode);
      Assert.Empty(await db.CommercialNotifications.Where(x => x.ProposalId == firstProposalId).ToListAsync());
      Assert.Equal(CrmStates.ProposalInternalReview,
        (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == firstProposalId)).Status);
    }

    Guid secondLeadId, secondOpportunityId, sentProposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Evidence-bound dispatch: the send queues exactly one durable email bound to the exact offer identity.
      secondLeadId = (await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Offer Bound Sender", "Web Form", "O. Owner", "offer-owner@example.test"))).Value;
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, secondLeadId);
      secondOpportunityId = (await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(secondLeadId))).Value;
      sentProposalId = (await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(secondOpportunityId))).Value;
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, sentProposalId)).Succeeded);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, sentProposalId);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, sentProposalId)).Succeeded);
      var notification = await db.CommercialNotifications.AsNoTracking().SingleAsync(x => x.ProposalId == sentProposalId);
      var proposal = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == sentProposalId);
      Assert.Equal(CommercialNotificationKinds.Proposal, notification.Kind);
      Assert.Equal("offer-owner@example.test", notification.Recipient);
      Assert.Equal("QUEUED", notification.DeliveryState);
      Assert.NotNull(notification.OfferSha256);
      Assert.Equal(notification.OfferSha256, proposal.SentOfferSha256);
      // Replaying the send never queues a second dispatch.
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, sentProposalId);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, sentProposalId)).Succeeded);
      Assert.Equal(1, await db.CommercialNotifications.CountAsync(x => x.FirmId == fixture.FirmId && x.ProposalId == sentProposalId));
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var offerSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == sentProposalId)).SentOfferSha256;
      // A superseded revision: accepting a later revision with the earlier offer's identity is refused.
      var revised = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(secondOpportunityId, 1));
      Assert.True(revised.Succeeded, revised.Message);
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, revised.Value)).Succeeded);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, revised.Value);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, revised.Value)).Succeeded);
      var currentSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == revised.Value)).SentOfferSha256;
      Assert.NotEqual(offerSha, currentSha);
      Assert.Equal("crm.stale-offer", (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, revised.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "O. Owner", "offer-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).ErrorCode);
      Assert.Equal("crm.invalid", (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, revised.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, Channel: ProposalResponseChannels.Email))).ErrorCode);
      Assert.Equal("crm.invalid", (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, revised.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, currentSha, Channel: ProposalResponseChannels.Email))).ErrorCode);
      // The current offer accepted with identity and respondent evidence; replay is safe, contradiction conflicts.
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, revised.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, currentSha, "O. Owner", "offer-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
      var accepted = await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == revised.Value);
      Assert.Equal(currentSha, accepted.ResponseOfferSha256);
      Assert.Equal("O. Owner", accepted.RespondentName);
      Assert.Equal("Signed acceptance letter", accepted.ResponseEvidenceReference);
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, revised.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, currentSha, "O. Owner", "offer-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
      Assert.Equal("crm.conflict", (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, revised.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "O. Owner", "offer-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).ErrorCode);
      // The superseded revision can no longer be accepted through any path.
      Assert.Equal(ErrorCodes.ProtectedState, (await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, sentProposalId,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, offerSha, "O. Owner", "offer-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).ErrorCode);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-PROPOSAL-MAKER-CHECKER-01")]
  public async Task ProposalReview_RequiresIndependentReviewer_AndRejectsUnknownLegacyAuthor()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    Guid opportunityId, proposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
        new CreateLeadRequest("Proposal checker client", "Referral"));
      Assert.True(lead.Succeeded, lead.Message);
      Assert.True((await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value)).Succeeded);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      opportunityId = opportunity.Value;
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId));
      Assert.True(proposal.Succeeded, proposal.Message);
      proposalId = proposal.Value;

      var selfApproval = await PracticeCrmService.ApproveProposalAsync(db, fixture.Actor, proposalId);
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, selfApproval.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var proposal = await db.Proposals.SingleAsync(x => x.Id == proposalId);
      Assert.Equal(fixture.Actor.UserId, proposal.PreparedByUserId);
      Assert.Null(proposal.ApprovedByUserId);
      Assert.Equal(CrmStates.ProposalDraft, proposal.Status);

      proposal.PreparedByUserId = null; // Legacy rows are not assigned a guessed author.
      await db.SaveChangesAsync();
      var unknownAuthor = await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, proposalId);
      Assert.False(unknownAuthor.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, unknownAuthor.ErrorCode);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var prepared = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunityId, 1));
      Assert.True(prepared.Succeeded, prepared.Message);
      var reviewed = await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, prepared.Value);
      Assert.True(reviewed.Succeeded, reviewed.Message);
      var proposal = await db.Proposals.SingleAsync(x => x.Id == prepared.Value);
      Assert.Equal(fixture.Actor.UserId, proposal.PreparedByUserId);
      Assert.Equal(fixture.Reviewer.UserId, proposal.ApprovedByUserId);
      Assert.Equal(CrmStates.ProposalInternalReview, proposal.Status);
      Assert.NotNull(proposal.ApprovedAt);
    }
  }

  [Fact]
  [Trait("CaseId", "AS-PAR-009-PROPOSAL-MIGRATION-01")]
  public async Task ProposalAuthorshipMigration_PreservesLegacyProposalWithoutInventingAuthor()
  {
    await using var pg = await PgTestSchema.CreateAsync(PreviousProposalMigration);
    var firmId = Guid.NewGuid();
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO leads
          (id, firm_id, name, source, status, created_at)
        VALUES
          ({leadId}, {firmId}, 'Legacy proposal lead', 'Migration fixture', 'QUALIFIED', statement_timestamp())
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO opportunities
          (id, firm_id, lead_id, service_route, entity_scope, period_start, period_end,
           expected_fee, currency, stage, created_at)
        VALUES
          ({opportunityId}, {firmId}, {leadId}, 'AccountingOnly', 'LEGACY-ENTITY',
           '2026-01-01', '2026-12-31', 800, 'QAR', 'PROPOSAL', statement_timestamp())
        """);
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO proposals
          (id, firm_id, opportunity_id, revision, status, service_profile_id, scope,
           exclusions, deliverables, dependencies, fee, currency, period_start, period_end, created_at)
        VALUES
          ({proposalId}, {firmId}, {opportunityId}, 1, 'DRAFT', 'LEGACY-PROFILE',
           'Legacy scope', '', 'Legacy deliverables', '', 800, 'QAR',
           '2026-01-01', '2026-12-31', statement_timestamp())
        """);

      await db.Database.MigrateAsync();
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var proposal = await verify.Proposals.SingleAsync(x => x.Id == proposalId);
    Assert.Equal(CrmStates.ProposalDraft, proposal.Status);
    Assert.Equal(800m, proposal.Fee);
    Assert.Null(proposal.PreparedByUserId);
    Assert.Empty(await verify.Database.GetPendingMigrationsAsync());
  }

  [Fact]
  public async Task CommercialCommands_EnforceOrder_AndCrossFirmActorIsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);

    var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor, new CreateLeadRequest("Order Client", "Web Form"));
    Assert.True(lead.Succeeded);
    var beforeQualification = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
    Assert.False(beforeQualification.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, beforeQualification.ErrorCode);

    var foreign = fixture.Actor with { FirmId = Guid.NewGuid() };
    var denied = await PracticeCrmService.CreateLeadAsync(db, foreign, new CreateLeadRequest("Foreign", "Web Form"));
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
      new CreateLeadRequest("Foreign Owner", "Web Form", OwnerUserId: Guid.NewGuid()));
    Assert.False(foreignOwner.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, foreignOwner.ErrorCode);

    var invalidContact = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Invalid Contact", "Web Form", PrimaryContactEmail: "not-an-email"));
    Assert.False(invalidContact.Succeeded);
    Assert.Equal("crm.invalid", invalidContact.ErrorCode);

    var lead = await PracticeCrmService.CreateLeadAsync(db, fixture.Actor,
      new CreateLeadRequest("Date Client", "Web Form"));
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
        new CreateLeadRequest("Candidate LLC", "Referral", "C. Owner", "candidate-owner@example.test"));
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunity.Value));
      await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, proposal.Value);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, proposal.Value);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, proposal.Value)).Succeeded);
      var candidateOfferSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposal.Value)).SentOfferSha256;
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposal.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, candidateOfferSha, "C. Owner", "candidate-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
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
        new CreateLeadRequest("Existing Client", "Referral", "E. Owner", "existing-owner@example.test"));
      await PracticeCrmService.QualifyLeadAsync(db, fixture.Actor, lead.Value);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, fixture.Actor, Opportunity(lead.Value));
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, fixture.Actor, Proposal(opportunity.Value));
      await PracticeCrmService.ApproveProposalAsync(db, fixture.Reviewer, proposal.Value);
      await ProposalDispatchTestSetup.EnsureCurrentQuotationArtifactAsync(db, fixture.Actor, proposal.Value);
      Assert.True((await PracticeCrmService.SendProposalAsync(db, fixture.Actor, proposal.Value)).Succeeded);
      var existingOfferSha = (await db.Proposals.AsNoTracking().SingleAsync(x => x.Id == proposal.Value)).SentOfferSha256;
      Assert.True((await PracticeCrmService.RecordProposalResponseAsync(db, fixture.Actor, proposal.Value,
        new ProposalResponseRequest(CrmStates.ProposalAccepted, null, existingOfferSha, "E. Owner", "existing-owner@example.test", "Signed acceptance letter", Channel: ProposalResponseChannels.Email))).Succeeded);
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

  private sealed class ObserveFirmLockAttempt : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
      DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
      CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.OrdinalIgnoreCase) &&
          command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase))
      {
        Fired = true;
        Reached.TrySetResult();
      }
      return ValueTask.FromResult(result);
    }
  }
}
