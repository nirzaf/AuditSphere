using System.Security.Cryptography;
using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

internal sealed record SupportedAuditScope(Guid FirmId, Guid ClientId, Guid EngagementId, ActorContext Preparer);

internal sealed record SupportedAuditCompletionWorld(
  SupportedAuditScope Primary, SupportedAuditScope Sibling, ActorContext Reviewer, ActorContext Administrator);

/// <summary>
/// Builds active audit scopes only through the installation, identity, CRM, acceptance, fee, billing,
/// and activation command owners. It intentionally creates no review, hold, grant, or approved profile rows directly.
/// </summary>
internal static class SupportedAuditCompletionWorldBuilder
{
  internal sealed record RoleActor(AppUser User, ActorContext Actor);

  public static Task<SupportedAuditCompletionWorld> CreateAsync(PgTestSchema pg, decimal primaryFee = 20000m, bool includeSibling = true, bool requireCommercialReview = false)
    => CreateAsync((ITestPostgresDatabase)pg, primaryFee, includeSibling, requireCommercialReview);

  public static async Task<SupportedAuditCompletionWorld> CreateAsync(ITestPostgresDatabase pg, decimal primaryFee = 20000m, bool includeSibling = true, bool requireCommercialReview = false,
    string clientLegalName = "Synthetic R02 Audit Client",
    Func<Guid, ActorContext, ActorContext, Task>? recordAcceptance = null)
  {
    var admin = await BootstrapAdminAsync(pg);
    var relationship = await ProvisionFirmRoleAsync(pg, admin, "RelationshipManager");
    var partner = await ProvisionFirmRoleAsync(pg, admin, "Partner");
    var manager = await ProvisionFirmRoleAsync(pg, admin, "Manager");
    var financeMaker = await ProvisionFirmRoleAsync(pg, admin, "FinanceManager");
    var financeReviewer = await ProvisionFirmRoleAsync(pg, admin, "FinanceReviewer");
    var staff = await ProvisionFirmRoleAsync(pg, admin, "Staff");

    // Commercial pricing uses reviewed rate evidence, then the two distinct finance actors
    // configure the currency needed by invoice posting through the approved finance API.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var rate = await PracticeTimeService.ReviseRateCardAsync(db, partner.Actor,
        new RateCardDraftRequest("Partner", "Audit", "QAR", 1000m));
      Assert.True(rate.Succeeded, rate.Message);
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, manager.Actor, rate.Value)).Succeeded);
      var profile = await FirmFinanceProfileService.CreateDraftAsync(db, financeMaker.Actor,
        new("QAR", BillingStates.TestProfile));
      Assert.True(profile.Succeeded, profile.Message);
      var approved = await FirmFinanceProfileService.ApproveDraftAsync(db, financeReviewer.Actor,
        new(profile.Value!.Id, profile.Value.Revision, profile.Value.DraftBasisSha256));
      Assert.True(approved.Succeeded, approved.Message);
    }

    if (requireCommercialReview)
    {
      await using var ruleDb = new AuditSphereDbContext(pg.Options);
      var rule = await QuotationService.SaveRuleAsync(ruleDb, partner.Actor,
        CommercialRuleKinds.NonStandardTerms, null, "Partner");
      Assert.True(rule.Succeeded, rule.Message);
    }
    var commercialProfileSaved = false;
    var primaryProposal = await CreateAcceptedProposalAsync(pg, relationship.Actor, partner.Actor, manager.Actor,
      "Primary", "2026-01-01", "2026-12-31", async db =>
      {
        if (commercialProfileSaved) return;
        var saved = await CommercialDocumentService.SaveProfileAsync(db, partner.Actor,
          new("Synthetic Audit Firm", "Test address", "firm@example.test", "", "#0F766E", "Synthetic test terms"));
        Assert.True(saved.Succeeded, saved.Message);
        commercialProfileSaved = true;
      }, primaryFee, requireCommercialReview);
    Guid clientId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, relationship.Actor,
        new(primaryProposal, clientLegalName, "R02 Client"));
      Assert.True(converted.Succeeded, converted.Message);
      clientId = converted.Value;
    }

    Guid siblingProposal = Guid.Empty;
    if (includeSibling)
    {
      siblingProposal = await CreateAcceptedProposalAsync(pg, relationship.Actor, partner.Actor, manager.Actor,
      "Sibling", "2025-01-01", "2025-12-31", async db =>
      {
        if (commercialProfileSaved) return;
        var saved = await CommercialDocumentService.SaveProfileAsync(db, partner.Actor,
          new("Synthetic Audit Firm", "Test address", "firm@example.test", "", "#0F766E", "Synthetic test terms"));
        Assert.True(saved.Succeeded, saved.Message);
        commercialProfileSaved = true;
      });
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var converted = await PracticeCrmService.ConvertToClientDraftAsync(db, relationship.Actor,
          new(siblingProposal, clientLegalName, "R02 Client"));
        Assert.True(converted.Succeeded, converted.Message);
        Assert.Equal(clientId, converted.Value);
      }

    }

    if (recordAcceptance is null)
      await RecordUnconditionalAcceptanceAsync(pg, clientId, "FinancialStatementAudit", staff.Actor, partner.Actor);
    else
      await recordAcceptance(clientId, staff.Actor, partner.Actor);

    if (!includeSibling)
    {
      // Deliverables exercise portal access, unlike the default completion-only R02 scopes.
      await using var letterDb = new AuditSphereDbContext(pg.Options);
      Assert.True((await AuditDeliverableService.RegisterSignatureAsync(letterDb, partner.Actor,
        CompletionTestFixtures.Png(120, 40))).Succeeded);
      Assert.True((await AuditDeliverableService.RegisterFirmSealAsync(letterDb, partner.Actor,
        CompletionTestFixtures.Png(100, 100))).Succeeded);
      var letter = await CommercialDocumentService.GenerateEngagementLetterAsync(letterDb, partner.Actor, primaryProposal);
      Assert.True(letter.Succeeded, letter.Message);
    }

    var primaryEngagement = await CreateActivatedEngagementAsync(pg, clientId, primaryProposal,
      relationship.Actor, partner.Actor, manager.Actor, financeMaker.Actor, financeReviewer.Actor,
      "2026-01-01", "2026-12-31", "Synthetic R02 primary audit");
    var siblingEngagement = includeSibling ? await CreateActivatedEngagementAsync(pg, clientId, siblingProposal,
      relationship.Actor, partner.Actor, manager.Actor, financeMaker.Actor, financeReviewer.Actor,
      "2025-01-01", "2025-12-31", "Synthetic R02 sibling audit") : primaryEngagement;

    // This primary preparer is also independently eligible to review by role and exact grant;
    // the self-review assertion must reach the separation-of-duties rule, not fail authorization first.
    var preparerA = await ProvisionEngagementRoleAsync(pg, admin, "Manager", clientId, primaryEngagement, "primary-preparer");
    var preparerB = includeSibling
      ? await ProvisionEngagementRoleAsync(pg, admin, "Staff", clientId, siblingEngagement, "sibling-preparer")
      : preparerA; // Single-scope callers consume Primary only; the default retains distinct scopes.
    var reviewer = await ProvisionEngagementRoleAsync(pg, admin, "Manager", clientId, primaryEngagement, "primary-reviewer");
    return new(
      new(admin.FirmId, clientId, primaryEngagement, preparerA.Actor),
      new(admin.FirmId, clientId, siblingEngagement, preparerB.Actor),
      reviewer.Actor, admin.Actor);
  }

  internal static async Task<Guid> CreateAdditionalClientAsync(ITestPostgresDatabase pg, SupportedAuditCompletionWorld world)
  {
    async Task<ActorContext> Actor(string role)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var id = await db.RoleGrants.AsNoTracking().Where(x => x.FirmId == world.Administrator.FirmId && x.Role == role &&
        x.ClientId == null && x.EngagementId == null && x.RevokedAt == null).Select(x => x.UserId).SingleAsync();
      var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == id);
      return new(user.Id, user.FirmId, user.SessionEpoch, [role]);
    }
    var relationship = await Actor("RelationshipManager");
    var proposal = await CreateAcceptedProposalAsync(pg, relationship, await Actor("Partner"), await Actor("Manager"),
      "OtherClient", "2024-01-01", "2024-12-31", _ => Task.CompletedTask);
    await using var db = new AuditSphereDbContext(pg.Options);
    var result = await PracticeCrmService.ConvertToClientDraftAsync(db, relationship,
      new(proposal, "Synthetic R06 separate client", "Separate R06 client"));
    Assert.True(result.Succeeded, result.Message);
    Assert.NotEqual(world.Primary.ClientId, result.Value);
    return result.Value;
  }


  internal static Task<(Guid FirmId, Guid ClientId, ActorContext Actor)> CreateClientBeforeAcceptanceAsync(PgTestSchema pg, string role)
    => CreateClientBeforeAcceptanceAsync((ITestPostgresDatabase)pg, role);

  internal static async Task<(Guid FirmId, Guid ClientId, ActorContext Actor)> CreateClientBeforeAcceptanceAsync(
    ITestPostgresDatabase pg, string role)
  {
    var admin = await BootstrapAdminAsync(pg);
    var relationship = await ProvisionFirmRoleAsync(pg, admin, "RelationshipManager");
    var partner = await ProvisionFirmRoleAsync(pg, admin, "Partner");
    var manager = await ProvisionFirmRoleAsync(pg, admin, "Manager");
    await using var db = new AuditSphereDbContext(pg.Options);
    var rate = await PracticeTimeService.ReviseRateCardAsync(db, partner.Actor,
      new RateCardDraftRequest("Partner", "Audit", "QAR", 1000m));
    Assert.True(rate.Succeeded, rate.Message);
    Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, manager.Actor, rate.Value)).Succeeded);
    var proposal = await CreateAcceptedProposalAsync(pg, relationship.Actor, partner.Actor, manager.Actor,
      "Acceptance", "2026-01-01", "2026-12-31", async profileDb =>
      {
        var saved = await CommercialDocumentService.SaveProfileAsync(profileDb, partner.Actor,
          new("Synthetic Audit Firm", "Test address", "firm@example.test", "", "#0F766E", "Synthetic test terms"));
        Assert.True(saved.Succeeded, saved.Message);
      });
    var client = await PracticeCrmService.ConvertToClientDraftAsync(db, relationship.Actor,
      new(proposal, "Synthetic acceptance client", "Acceptance client"));
    Assert.True(client.Succeeded, client.Message);
    return (admin.FirmId, client.Value, role == "Partner" ? partner.Actor : manager.Actor);
  }

  internal static async Task<Guid> CreateAcceptedProposalAsync(ITestPostgresDatabase pg, ActorContext relationship,
    ActorContext partner, ActorContext manager, string suffix, string periodStart, string periodEnd,
    Func<AuditSphereDbContext, Task> ensureCommercialProfile, decimal fee = 20000m, bool nonStandardTerms = false)
  {
    Guid proposalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lead = await PracticeCrmService.CreateLeadAsync(db, relationship,
        new($"Synthetic R02 {suffix} prospect", "Referral", "Synthetic owner", $"{suffix.ToLowerInvariant()}@example.test"));
      Assert.True(lead.Succeeded, lead.Message);
      var qualified = await PracticeCrmService.QualifyLeadAsync(db, relationship, lead.Value);
      Assert.True(qualified.Succeeded, qualified.Message);
      var opportunity = await PracticeCrmService.CreateOpportunityAsync(db, relationship,
        new(lead.Value, "FinancialStatementAudit", "SYNTHETIC-ENTITY", periodStart, periodEnd, fee, "QAR", 60m));
      Assert.True(opportunity.Succeeded, opportunity.Message);
      var proposal = await PracticeCrmService.ReviseProposalAsync(db, relationship,
        new(opportunity.Value, $"Synthetic R02 {suffix} audit", "Statutory audit", "Tax advisory",
          "Independent auditor report", "Client supplies source records", fee, "QAR", periodStart, periodEnd,
          Timeline: "Synthetic test fieldwork and reporting timeline."));
      Assert.True(proposal.Succeeded, proposal.Message);
      proposalId = proposal.Value;

      var quotation = await QuotationService.SaveAsync(db, relationship,
        new(proposalId, [new("Partner", "Audit", fee / 1000m)], 1m, 0m, 0m, nonStandardTerms,
          nonStandardTerms ? "Synthetic independently reviewed non-standard terms." : null,
          ExpectedRevision: 0, ExpectedProposalRevision: 1));
      Assert.True(quotation.Succeeded, quotation.Message);
      Assert.True((await QuotationService.SubmitAsync(db, relationship, quotation.Value)).Succeeded);
      var quoteState = await db.QuotationVersions.AsNoTracking().SingleAsync(x => x.Id == quotation.Value);
      foreach (var requirement in System.Text.Json.JsonSerializer.Deserialize<List<RequiredApproval>>(quoteState.RequiredApprovalsJson, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)) ?? [])
      {
        var approver = requirement.Role == "Manager" ? manager : partner;
        var approved = await QuotationService.ApproveAsync(db, approver, quotation.Value,
          requirement.RuleKey, "Synthetic approved pricing review for the focused R02 fixture.");
        Assert.True(approved.Succeeded, approved.Message);
      }
      Assert.True((await PracticeCrmService.ApproveProposalAsync(db, partner, proposalId)).Succeeded);
      await ensureCommercialProfile(db);
      var artifact = await CommercialDocumentService.GenerateBriefQuotationAsync(db, relationship, proposalId,
        expectedQuotationId: quotation.Value, expectedProfileVersion: 1);
      Assert.True(artifact.Succeeded, artifact.Message);
      var sent = await PracticeCrmService.SendProposalAsync(db, partner, proposalId);
      Assert.True(sent.Succeeded, sent.Message);
      var offer = await db.Proposals.AsNoTracking().Where(x => x.Id == proposalId).Select(x => x.SentOfferSha256).SingleAsync();
      var response = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposalId,
        new("ACCEPTED", null, offer, "Synthetic owner", $"{suffix.ToLowerInvariant()}@example.test",
          "Synthetic accepted offer evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(response.Succeeded, response.Message);
    }
    return proposalId;
  }

  internal static async Task RecordUnconditionalAcceptanceAsync(ITestPostgresDatabase pg, Guid clientId,
    string route, ActorContext preparer, ActorContext partner, Guid? engagementId = null)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var checklist = await AcceptanceChecklistService.GetAsync(db, preparer, clientId);
    Assert.True(checklist.Succeeded, checklist.Message);
    Assert.NotEmpty(checklist.Value!.Items);
    foreach (var item in checklist.Value.Items)
    {
      var answer = item.Question.AdverseAnswer switch
      {
        "NO" => "Yes",
        "YES" => "No",
        _ => "Yes"
      };
      var result = await AcceptanceChecklistService.RecordAnswerAsync(db, preparer, clientId,
        item.Question.Code, answer, item.Question.RequiresEvidence ? $"synthetic-evidence:{item.Question.Code}" : null,
        expectedGeneration: checklist.Value.Generation, expectedRevision: item.Answer?.Revision ?? 0);
      Assert.True(result.Succeeded, $"{item.Question.Code}: {result.ErrorCode} {result.Message}");
    }
    var current = await AcceptanceChecklistService.GetAsync(db, preparer, clientId);
    Assert.True(current.Succeeded, current.Message);
    Assert.True(current.Value!.Ready, string.Join("; ", current.Value.Blockers.Select(x => x.Message)));
    var decision = await AcceptanceDecisionService.RecordAsync(db, partner,
      new(clientId, engagementId, route, "ACCEPTED", "Synthetic test acceptance review.", null, current.Value.Generation));
    Assert.True(decision.Succeeded, decision.Message);
  }

  private static async Task<Guid> CreateActivatedEngagementAsync(ITestPostgresDatabase pg, Guid clientId, Guid proposalId,
    ActorContext relationship, ActorContext partner, ActorContext manager, ActorContext financeManager,
    ActorContext financeReviewer, string periodStart, string periodEnd, string profile)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var state = await EngagementCreationWorkspace.StateAsync(db, relationship, clientId);
    Assert.True(state.Succeeded, state.Message);
    var createRequest = new EngagementCreationRequest(Guid.NewGuid(), state.Value!.ReviewBasis,
      new("FinancialStatementAudit", profile, periodStart, periodEnd));
    var preview = await EngagementCreationWorkspace.PreviewAsync(db, relationship, clientId, createRequest);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await EngagementCreationWorkspace.ExecuteAsync(db, relationship, clientId,
      createRequest with { Reviewed = true, ExpectedRequestHash = preview.Value!.RequestHash });
    Assert.True(created.Succeeded, created.Message);
    var engagementId = created.Value!.EngagementId;

    var agreement = await FeeAgreementService.CreateAgreementAsync(db, relationship, proposalId);
    Assert.True(agreement.Succeeded, agreement.Message);
    var linked = await FeeAgreementService.LinkEngagementAsync(db, relationship, agreement.Value, engagementId);
    Assert.True(linked.Succeeded, linked.Message);
    var invoice = await FeeAgreementService.IssueAdvanceInvoiceAsync(db, financeManager, agreement.Value);
    Assert.True(invoice.Succeeded, invoice.Message);
    Assert.True((await BillingService.SubmitInvoiceAsync(db, financeManager, invoice.Value)).Succeeded);
    Assert.True((await BillingService.ApproveInvoiceAsync(db, financeReviewer, invoice.Value)).Succeeded);
    Assert.True((await BillingService.PostInvoiceAsync(db, financeManager, invoice.Value)).Succeeded);
    var amount = await db.Invoices.AsNoTracking().Where(x => x.Id == invoice.Value).Select(x => x.Total).SingleAsync();
    var paid = await FeeAgreementService.RecordAdvancePaymentAsync(db, financeManager, agreement.Value, amount,
      $"SYNTHETIC-R02-{engagementId:N}");
    Assert.True(paid.Succeeded, paid.Message);
    Assert.True(paid.Value!.MilestonePaid);

    var stateToActivate = await EngagementActivationWorkspace.StateAsync(db, partner, engagementId);
    Assert.True(stateToActivate.Succeeded, stateToActivate.Message);
    Assert.True(stateToActivate.Value!.Eligible,
      string.Join("; ", stateToActivate.Value.Blockers.Select(x => $"{x.Code}: {x.Message}")));
    var activateRequest = new EngagementActivationRequest(Guid.NewGuid(), stateToActivate.Value.ReviewBasis);
    var activation = await EngagementActivationWorkspace.PreviewAsync(db, partner, engagementId, activateRequest);
    Assert.True(activation.Succeeded, activation.Message);
    var activated = await EngagementActivationWorkspace.ExecuteAsync(db, partner, engagementId,
      activateRequest with { Reviewed = true, ExpectedRequestHash = activation.Value!.RequestHash });
    Assert.True(activated.Succeeded, activated.Message);
    return engagementId;
  }

  internal static async Task<(Guid FirmId, ActorContext Actor)> BootstrapAdminAsync(ITestPostgresDatabase pg)
  {
    var firmId = Guid.NewGuid();
    var tenant = Guid.NewGuid().ToString("D");
    var objectId = Guid.NewGuid().ToString("D");
    var now = DateTimeOffset.UtcNow;
    const string proof = "synthetic-R02-installation-proof";
    var proofHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proof))).ToLowerInvariant();
    await using var db = new AuditSphereDbContext(pg.Options);
    var claim = await Microsoft365OnboardingService.ClaimAsync(db, firmId,
      "r02-install-" + Guid.NewGuid().ToString("N"), proof, proofHash, now);
    Assert.True(claim.Succeeded, claim.Message);
    var completed = await Microsoft365OnboardingService.CompleteInitialAdministratorBootstrapAsync(db,
      new(claim.Value!.SessionId, claim.Value.Capability, tenant, objectId,
        "r02-admin-" + Guid.NewGuid().ToString("N") + "@example.test", "Synthetic R02 administrator"),
      tenant, objectId, now);
    Assert.True(completed.Succeeded, completed.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == completed.Value);
    return (firmId, new(user.Id, firmId, user.SessionEpoch, ["Administrator"]));
  }

  internal static async Task<RoleActor> ProvisionFirmRoleAsync(ITestPostgresDatabase pg,
    (Guid FirmId, ActorContext Actor) admin, string role)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var adminUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Actor.UserId);
    var ensured = await RoleAdministrationService.EnsureUserAsync(db, admin.Actor,
      new(adminUser.TenantId, Guid.NewGuid().ToString("D"), $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test",
        "Synthetic R02 " + role, "Staff", "APPROVED_ROSTER"));
    Assert.True(ensured.Succeeded, ensured.Message);
    var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, admin.Actor,
      new(ensured.Value, role, "FIRM_WIDE"));
    Assert.True(grant.Succeeded, grant.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ensured.Value);
    return new(user, new(user.Id, user.FirmId, user.SessionEpoch, [role]));
  }

  private static async Task<RoleActor> ProvisionEngagementRoleAsync(ITestPostgresDatabase pg,
    (Guid FirmId, ActorContext Actor) admin, string role, Guid clientId, Guid engagementId, string suffix)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var adminUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == admin.Actor.UserId);
    var ensured = await RoleAdministrationService.EnsureUserAsync(db, admin.Actor,
      new(adminUser.TenantId, Guid.NewGuid().ToString("D"), $"{suffix}-{Guid.NewGuid():N}@example.test",
        "Synthetic R02 " + suffix, "Staff", "APPROVED_ROSTER"));
    Assert.True(ensured.Succeeded, ensured.Message);
    var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, admin.Actor,
      new(ensured.Value, role, "ENGAGEMENT", clientId, engagementId));
    Assert.True(grant.Succeeded, grant.Message);
    var user = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ensured.Value);
    return new(user, new(user.Id, user.FirmId, user.SessionEpoch, [role]));
  }
}
