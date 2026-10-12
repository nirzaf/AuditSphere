using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class EngagementLifecycleQueryTests
{
  [Fact]
  public async Task EngagementTraversesCanonicalStagesAndEnforcesScope()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var unauthorized = PbcSeed.Actor(seed.Staff, "Staff");
    var now = DateTimeOffset.UtcNow;

    Guid engagementId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));

      var engagement = new Engagement
      {
        Id = Guid.NewGuid(),
        FirmId = seed.FirmId,
        PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit",
        PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31",
        ServiceProfileId = "CommercialAudit",
        Status = "Draft",
        ProfessionalWorkBlocked = true,
        CreatedAt = now
      };
      db.Engagements.Add(engagement);
      await db.SaveChangesAsync();
      engagementId = engagement.Id;

      // 1. Initial draft without proposal -> ProposalGeneration
      var report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.Summary.CanonicalStage);
      Assert.Equal(2, report.Value.Summary.StageIndex);
      Assert.Contains("Commercial proposal preparation and dispatch required", report.Value.Summary.BlockedReasons);
      Assert.Null(report.Value.Summary.LocalArchiveState);
      Assert.Null(report.Value.Summary.ProviderProtectionState);

      // 2. A draft proposal remains at ProposalGeneration until it has actually been dispatched.
      var lead = new Lead
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic lifecycle client",
        Source = "Referral", CreatedAt = now
      };
      var opportunity = new Opportunity
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = lead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        OpportunityId = opportunity.Id, Status = CrmStates.ProposalDraft,
        Fee = 50000m, Currency = "QAR",
        ServiceProfileId = "AUDIT-2026", Scope = "Statutory Financial Audit",
        Deliverables = "Audit Report and Management Letter",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31"
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.Summary.CanonicalStage);
      Assert.Equal(2, report.Value.Summary.StageIndex);

      var proposal = await db.Proposals.SingleAsync(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId);
      var sentOffer = new string('c', 64);
      var quotation = new QuotationVersion
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ProposalId = proposal.Id, Revision = 1, Currency = "QAR",
        BaseAmount = proposal.Fee, Fee = proposal.Fee, InputHash = new string('e', 64), Status = QuotationStates.Approved,
        ApprovedAt = now
      };
      db.QuotationVersions.Add(quotation);
      proposal.Status = CrmStates.ProposalSent;
      proposal.SentAt = now;
      proposal.SentOfferSha256 = sentOffer;
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Kind = CommercialNotificationKinds.Proposal,
        ProposalId = proposal.Id, PracticeClientId = seed.ClientId, QuotationVersionId = quotation.Id,
        OfferSha256 = sentOffer, Recipient = "director@lifecycle.example.test", Subject = "Synthetic dispatched offer",
        Body = "Synthetic test evidence", CreatedAt = now
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Equal(3, report.Value.Summary.StageIndex);

      // 3. Add partner unconditional acceptance -> AdvanceBilling
      var decisionId = Guid.NewGuid();
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = decisionId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", Decision = "Accepted", DecidedByUserId = seed.Admin.Id,
        DecidedAt = now, Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Partner assessment passed",
        EvaluationTemplateVersion = "V2",
        EvaluationSnapshotDigest = new string('a', 64)
      });
      await db.SaveChangesAsync();

      // WF-01 correction: a Partner's unconditional clearance alone never advances billing while the client has not accepted.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Equal(3, report.Value.Summary.StageIndex);
      Assert.Contains("Client commercial acceptance of proposal required", report.Value.Summary.BlockedReasons);

      // Acceptance that answers an offer identity other than the dispatched one does not complete the commercial key.
      proposal.Status = CrmStates.ProposalAccepted;
      proposal.ResponseAt = now;
      proposal.SentOfferSha256 = sentOffer;
      proposal.ResponseOfferSha256 = new string('d', 64);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Client acceptance does not cite the current dispatched offer or quotation revision; re-record acceptance against the exact current offer.",
        report.Value.Summary.BlockedReasons);

      // Exact acceptance of the dispatched offer -> AdvanceBilling, still gated on the letter and the fee agreement.
      proposal.ResponseOfferSha256 = sentOffer;
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);
      Assert.Equal(4, report.Value.Summary.StageIndex);
      Assert.Contains("Issue the Engagement Letter for the current accepted offer", report.Value.Summary.BlockedReasons);
      Assert.Contains("Create the fee agreement and its 50% advance milestone", report.Value.Summary.BlockedReasons);

      // An engagement letter can only be issued by the protected command (the database refuses a hand-written letter without
      // a signature, seal and current risk clearance), so the issued-letter branch is covered by the real-command readiness tests.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Issue the Engagement Letter for the current accepted offer", report.Value.Summary.BlockedReasons);

      // 4. Activate engagement -> PortalActivePlanning
      db.EngagementActivations.Add(new EngagementActivation
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
        EngagementId = engagementId, AcceptanceDecisionId = decisionId,
        ClientGeneration = 1, AcceptancePath = "NEW_CLIENT",
        ActivatedByUserId = seed.Admin.Id, ActivatedAt = now
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.PortalActivePlanning, report.Value!.Summary.CanonicalStage);
      Assert.Equal(5, report.Value.Summary.StageIndex);
      Assert.Contains("Trial balance intake and acceptance required", report.Value.Summary.BlockedReasons);

      // 5. Add TB & approve materiality & unblock professional work -> FieldworkExecution
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, SourceKind = "Raw", Revision = 1,
        LegalEntityKey = "PBC TEST CLIENT", Currency = "QAR"
      });
      // A status flag alone does not approve planning (STE-GAP-008 B): the legacy assessment needs a separate Partner's recorded approval.
      var independentPartner = PbcSeed.User(seed.FirmId, "Staff");
      db.Users.Add(independentPartner);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, independentPartner, "Partner"));
      await db.SaveChangesAsync();
      var materialityAssessment = new MaterialityAssessment
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Status = MaterialityStatuses.Approved,
        BenchmarkSource = "TrialBalance", BenchmarkVersion = "1.0",
        Rationale = "Standard revenue benchmark applied",
        BenchmarkAmount = 1000000m, RateApplied = 0.01m,
        OverallMateriality = 10000m, PerformanceMateriality = 7500m,
        ClearlyTrivialThreshold = 500m, ActorId = seed.Admin.Id, CreatedAt = now
      };
      db.MaterialityAssessments.Add(materialityAssessment);
      db.MaterialityApprovals.Add(new MaterialityApproval
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        MaterialityAssessmentId = materialityAssessment.Id, ApprovedByUserId = independentPartner.Id, ApprovedAt = DateTimeOffset.UtcNow
      });
      engagement.ProfessionalWorkBlocked = false;
      engagement.Status = "Active";

      var proc = new AuditProcedure
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Title = "Test Cash & Bank", Status = AuditProcedureStatuses.Planned,
        ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
        CurrentResultRevision = 0, CreatedAt = now
      };
      db.AuditProcedures.Add(proc);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.FieldworkExecution, report.Value!.Summary.CanonicalStage);
      Assert.Equal(6, report.Value.Summary.StageIndex);

      // 6. Execute procedures -> ManagerialReview
      proc.CurrentResultRevision = 1;
      proc.Status = AuditProcedureStatuses.Submitted;
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);

      // 7. Add partner clearance and opinion -> PartnerApproval
      var srm = new AuditDeliverable
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Kind = DeliverableKinds.SummaryReviewMemorandum,
        Version = 1, InputDigest = new string('1', 64), ContentSha256 = new string('2', 64),
        Content = [1, 2, 3], CreatedByUserId = seed.Admin.Id, CreatedAt = now
      };
      db.AuditDeliverables.Add(srm);

      var clearance = new PartnerCompletionClearance
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, SummaryReviewMemorandumId = srm.Id,
        KeyRiskAreasComment = "Key risk areas cleared by partner",
        FinancialStatementNotesComment = "Notes reviewed and cleared",
        PartnerUserId = seed.Admin.Id, ClearedAt = now
      };
      db.PartnerCompletionClearances.Add(clearance);
      db.AuditOpinionDecisions.Add(new AuditOpinionDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, PartnerClearanceId = clearance.Id,
        OpinionType = AuditOpinionTypes.Unmodified, DecidedByUserId = seed.Admin.Id,
        DecidedAt = now
      });
      await db.SaveChangesAsync();

      // STE-GAP-008 C: a partner clearance and opinion do not hand over to Partner Approval while the completion gate is open
      // (the synthetic fixture has no adopted audit program or reviewed procedure record), so the stage stays in Managerial Review.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.StartsWith("Completion gate:", StringComparison.Ordinal));

      // 8. Sign report deliverable -> DeliverableRelease
      var signedReport = new AuditDeliverable
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Kind = DeliverableKinds.IndependentAuditorsReport,
        Version = 1, InputDigest = new string('3', 64), ContentSha256 = new string('4', 64),
        Content = [1, 2, 3], SignedFromDeliverableId = srm.Id, CreatedByUserId = seed.Admin.Id, CreatedAt = now
      };
      db.AuditDeliverables.Add(signedReport);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.DeliverableRelease, report.Value!.Summary.CanonicalStage);
      Assert.Equal(9, report.Value.Summary.StageIndex);

      // 9. Schedule file freeze -> ComplianceCountdown (<60 days)
      var freeze = new EngagementFileFreeze
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, ReportDeliverableId = signedReport.Id,
        ReportSignedAt = now.AddDays(-10), DueAt = now.AddDays(-10).AddDays(60),
        Revision = 1, ExternalReadOnly = "NOT_REQUESTED",
        State = FileFreezeStates.Scheduled, UpdatedAt = now
      };
      db.EngagementFileFreezes.Add(freeze);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ComplianceCountdown, report.Value!.Summary.CanonicalStage);
      Assert.Equal(10, report.Value.Summary.StageIndex);
      Assert.NotNull(report.Value.Summary.ComplianceCountdownDays);
      Assert.True(report.Value.Summary.ComplianceCountdownDays <= 51);
      Assert.Equal(FileFreezeStates.Scheduled, report.Value.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.NotRequested, report.Value.Summary.ProviderProtectionState);
      Assert.Contains("does not claim provider immutability", report.Value.Summary.ComplianceWarning!);

      // A due but uncommitted freeze stays in the countdown state and is not described as locally archived.
      freeze.ReportSignedAt = now.AddDays(-61);
      freeze.DueAt = freeze.ReportSignedAt.AddDays(60);
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.ComplianceCountdown, report.Value!.Summary.CanonicalStage);
      Assert.False(report.Value.Summary.IsArchived);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.Contains("freeze is due but has not committed", StringComparison.Ordinal));

      // 10. Lock / freeze -> ArchivedReadOnly
      freeze.State = FileFreezeStates.Frozen;
      freeze.FrozenAt = now.AddDays(-1);
      freeze.ReportSignedAt = now.AddDays(-61);
      freeze.DueAt = now.AddDays(-61).AddDays(60);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ArchivedReadOnly, report.Value!.Summary.CanonicalStage);
      Assert.Equal(11, report.Value.Summary.StageIndex);
      Assert.True(report.Value.Summary.IsArchived);
      Assert.Equal(FileFreezeStates.Frozen, report.Value.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.NotRequested, report.Value.Summary.ProviderProtectionState);
      Assert.Contains("does not claim provider immutability", report.Value.Summary.ComplianceWarning!);

      freeze.ExternalReadOnly = ExternalReadOnlyStates.Observed;
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(FileFreezeStates.Frozen, report.Value!.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.Observed, report.Value.Summary.ProviderProtectionState);
      Assert.Null(report.Value.Summary.ComplianceWarning);

      // 11. Unauthorized actor is scope-denied
      var denied = await EngagementLifecycleQuery.GetAsync(db, unauthorized, engagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  /// <summary>
  /// Advance billing reads the same evidence as the protected letter guard. Mutating one key at a time shows that a stale client
  /// generation, a conditional Partner decision that supersedes the unconditional one, or a declined proposal returns the
  /// engagement to Dual-Key Pending, and that restoring the evidence restores the stage.
  /// </summary>
  [Fact]
  public async Task AdvanceBillingNeverAdvancesOnStaleConditionalOrDeclinedEvidence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var now = DateTimeOffset.UtcNow;
    Guid engagementId = Guid.NewGuid();
    Guid unconditionalId = Guid.NewGuid();
    var conditionalId = Guid.NewGuid();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
      var lead = new Lead { Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic matrix client", Source = "Referral", CreatedAt = now };
      var opportunity = new Opportunity
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = lead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now
      };
      var quotationId = Guid.NewGuid();
      var offerHash = new string('c', 64);
      var proposal = new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = opportunity.Id,
        Status = CrmStates.ProposalAccepted, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        SentAt = now, SentOfferSha256 = offerHash, ResponseAt = now, ResponseOfferSha256 = offerHash
      };
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", ServiceProfileId = "CommercialAudit", Status = "Draft",
        ProfessionalWorkBlocked = true, CreatedAt = now
      });
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = seed.FirmId, ProposalId = proposal.Id, Revision = 1, Currency = "QAR",
        BaseAmount = 50000m, Fee = 50000m, InputHash = new string('e', 64), Status = QuotationStates.Approved, ApprovedAt = now
      });
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Kind = CommercialNotificationKinds.Proposal,
        ProposalId = proposal.Id, PracticeClientId = seed.ClientId, QuotationVersionId = quotationId,
        OfferSha256 = offerHash, Recipient = "owner@matrix.example.test", Subject = "Synthetic dispatched offer",
        Body = "Synthetic test evidence", CreatedAt = now
      });
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = unconditionalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "Accepted", DecidedByUserId = seed.Admin.Id, DecidedAt = now, Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Partner assessment passed", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('a', 64)
      });
      await db.SaveChangesAsync();

      // Both keys current -> AdvanceBilling (the letter and fee agreement remain blockers, asserted in the traversal test).
      var report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // The client generation moves on: the Partner clearance is stale and must not carry billing.
      await db.ClientSafetyStates.Where(x => x.Id == seed.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, 2));
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Unconditional Partner risk acceptance decision required", report.Value.Summary.BlockedReasons);

      await db.ClientSafetyStates.Where(x => x.Id == seed.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, 1));
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // A later conditional decision supersedes the unconditional one for the same generation: it is not a clearance.
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = conditionalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "AcceptedWithConditions", Conditions = "Obtain bank confirmation before fieldwork",
        DecidedByUserId = seed.Admin.Id, DecidedAt = now.AddSeconds(1), Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Conditional assessment", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('b', 64)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);

      // Acceptance decisions are append-only, so the conditional decision stays; a newer unconditional decision supersedes it.
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "Accepted", DecidedByUserId = seed.Admin.Id, DecidedAt = now.AddSeconds(2), Path = "NEW_CLIENT", Generation = 1,
        Rationale = "Conditions discharged", EvaluationTemplateVersion = "V2", EvaluationSnapshotDigest = new string('d', 64)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // A declined dispatched proposal is never treated as client commercial consent.
      var declined = await db.Proposals.SingleAsync(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId);
      declined.Status = CrmStates.ProposalDeclined;
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("The client declined the dispatched proposal; revise and resend or close the opportunity.", report.Value.Summary.BlockedReasons);
    }
  }

  [Fact]
  public async Task PriorPeriodAcceptedOfferDoesNotAdvanceANewPeriodEngagement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var seed = await PbcSeed.SeedAsync(pg);
    var partner = PbcSeed.Actor(seed.Admin, "Partner");
    var now = DateTimeOffset.UtcNow;
    var engagementId = Guid.NewGuid();
    var leadId = Guid.NewGuid();
    var opportunityId = Guid.NewGuid();
    var proposalId = Guid.NewGuid();
    var quotationId = Guid.NewGuid();
    var offerHash = new string('c', 64);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        PeriodStart = "2027-01-01", PeriodEnd = "2027-12-31", ServiceProfileId = "CommercialAudit",
        Status = "Draft", ProfessionalWorkBlocked = true, CreatedAt = now
      });
      db.Leads.Add(new Lead
      {
        Id = leadId, FirmId = seed.FirmId, Name = "Synthetic annual workflow client", Source = "Referral", CreatedAt = now
      });
      db.Opportunities.Add(new Opportunity
      {
        Id = opportunityId, FirmId = seed.FirmId, LeadId = leadId, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Currency = "QAR", CreatedAt = now
      });
      db.Proposals.Add(new Proposal
      {
        Id = proposalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = opportunityId,
        Status = CrmStates.ProposalAccepted, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", SentAt = now, SentOfferSha256 = offerHash, ResponseAt = now,
        ResponseOfferSha256 = offerHash
      });
      db.QuotationVersions.Add(new QuotationVersion
      {
        Id = quotationId, FirmId = seed.FirmId, ProposalId = proposalId, Revision = 1, Currency = "QAR",
        BaseAmount = 50000m, Fee = 50000m, InputHash = new string('e', 64), Status = QuotationStates.Approved,
        ApprovedAt = now
      });
      db.CommercialNotifications.Add(new CommercialNotification
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, Kind = CommercialNotificationKinds.Proposal,
        ProposalId = proposalId, PracticeClientId = seed.ClientId, QuotationVersionId = quotationId,
        OfferSha256 = offerHash, Recipient = "owner@annual.example.test", Subject = "Prior-period synthetic offer",
        Body = "Synthetic test evidence", CreatedAt = now
      });
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        Decision = "Accepted", DecidedByUserId = seed.Admin.Id, DecidedAt = now, Path = AcceptancePaths.Continuance,
        Generation = 1, Rationale = "Synthetic prior-period clearance", EvaluationTemplateVersion = "V2",
        EvaluationSnapshotDigest = new string('a', 64)
      });
      await db.SaveChangesAsync();

      var report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal(CanonicalEngagementStages.ProposalGeneration, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Commercial proposal preparation and dispatch required", report.Value.Summary.BlockedReasons);
    }
  }
}
