using System.Text;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Practice;
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
  public void CurrentProcedureProjectionRequiresVerifiedCurrentNotApplicableEvidence()
  {
    var now = DateTimeOffset.UtcNow;
    var notApplicable = new AuditProcedure
    {
      ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicableApproved,
      Status = AuditProcedureStatuses.Planned,
      CurrentResultRevision = 0
    };
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(notApplicable, null, null));
    Assert.True(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(notApplicable, null, null,
      currentNotApplicableApprovalVerified: true));

    var applicable = new AuditProcedure
    {
      ApplicabilityStatus = AuditApplicabilityStatuses.Applicable,
      Status = AuditProcedureStatuses.Submitted,
      CurrentResultRevision = 2
    };
    var submitted = new AuditProcedureResult { Revision = 2, Status = AuditProcedureResultStatuses.Submitted };
    Assert.True(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable, submitted, null));
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable,
      new AuditProcedureResult { Revision = 1, Status = AuditProcedureResultStatuses.Submitted }, null));

    var returned = new AuditProcedureResult { Revision = 2, Status = AuditProcedureResultStatuses.ChangesRequired };
    var returnReview = new AuditProcedureReview { ResultRevision = 2, Decision = AuditProcedureReviewDecisions.ChangesRequired, CreatedAt = now };
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable, returned, returnReview));
    Assert.False(EngagementLifecycleQuery.IsCurrentProcedureSubmittedForReview(applicable, submitted, returnReview));
  }

  [Fact]
  public void SignatureClockKeepsDeliveryPhaseUntilExactBundleIsAvailableAndPartnerPhaseDoesNotNeedOpinion()
  {
    Assert.Equal(CanonicalEngagementStages.DeliverableRelease, EngagementLifecycleQuery.PostSignatureStage(false, false));
    Assert.Equal(CanonicalEngagementStages.DeliverableRelease, EngagementLifecycleQuery.PostSignatureStage(true, false));
    Assert.Equal(CanonicalEngagementStages.DeliverableRelease, EngagementLifecycleQuery.PostSignatureStage(false, true));
    Assert.Equal(CanonicalEngagementStages.ComplianceCountdown, EngagementLifecycleQuery.PostSignatureStage(true, true));
    Assert.True(EngagementLifecycleQuery.PartnerHandoverReady(completionBlockerCount: 0));
    Assert.False(EngagementLifecycleQuery.PartnerHandoverReady(completionBlockerCount: 1));
  }

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
      proposal.Status = CrmStates.ProposalInternalReview;
      await db.SaveChangesAsync();
      var dispatch = await SeedDispatchedOfferAsync(db, proposal, partner.UserId, now, "director@lifecycle.example.test");
      await db.SaveChangesAsync();
      var sentOffer = dispatch.OfferSha256!;

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

      // A real immutable response to the exact dispatch opens the client key.
      var accepted = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposal.Id,
        new("ACCEPTED", null, sentOffer, "Synthetic director", dispatch.Recipient,
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(accepted.Succeeded, accepted.Message);

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);

      // A tampered legacy projection that cites another offer identity cannot shadow its immutable receipt.
      proposal = await db.Proposals.SingleAsync(x => x.FirmId == seed.FirmId && x.Id == proposal.Id);
      var acceptedOffer = proposal.ResponseOfferSha256;
      proposal.ResponseOfferSha256 = new string('d', 64);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DualKeyPending, report.Value!.Summary.CanonicalStage);
      Assert.Contains("Client acceptance does not cite the current dispatched offer or quotation revision; re-record acceptance against the exact current offer.",
        report.Value.Summary.BlockedReasons);

      // Restoring the projection to the exact immutable receipt returns the workflow to AdvanceBilling.
      proposal.ResponseOfferSha256 = acceptedOffer;
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
      var approvedNotApplicable = new AuditProcedure
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        SourceProcedureId = "NA-APPROVED", Title = "Synthetic approved not-applicable procedure",
        Status = AuditProcedureStatuses.Planned, ApplicabilityStatus = AuditApplicabilityStatuses.NotApplicableApproved,
        ApplicabilityRationale = "Outside this engagement's approved scope.", ApplicabilityDecidedByUserId = seed.Admin.Id,
        ApplicabilityDecidedAt = now, CreatedAt = now
      };
      db.AuditProcedures.AddRange(proc, approvedNotApplicable);
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.FieldworkExecution, report.Value!.Summary.CanonicalStage);
      Assert.Equal(6, report.Value.Summary.StageIndex);

      // 6. Execute procedures -> ManagerialReview
      proc.CurrentResultRevision = 1;
      proc.Status = AuditProcedureStatuses.Submitted;
      db.AuditProcedureResults.Add(new AuditProcedureResult
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = proc.Id, Revision = 1, InputGeneration = 1, WorkPerformed = "Synthetic procedure work submitted.",
        StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Awaiting manager review.",
        Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = seed.Admin.Id, SubmittedAt = now
      });
      await db.SaveChangesAsync();

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);

      // Approved N/A contributes no fictitious result. An unreviewed submitted result remains in Managerial Review.
      Assert.DoesNotContain(report.Value.Summary.BlockedReasons, x => x.Contains("not-applicable", StringComparison.OrdinalIgnoreCase));

      // A current returned result routes back to preparer fieldwork, even though an earlier revision was submitted.
      var returned = new AuditProcedureResult
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = proc.Id, Revision = 2, InputGeneration = 1, WorkPerformed = "Synthetic rework required.",
        StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Revise the evidence linkage.",
        Status = AuditProcedureResultStatuses.ChangesRequired, PreparedByUserId = seed.Admin.Id, SubmittedAt = now.AddSeconds(1)
      };
      proc.CurrentResultRevision = 2;
      proc.Status = AuditProcedureStatuses.ChangesRequired;
      db.AuditProcedureResults.Add(returned);
      db.AuditProcedureReviews.Add(new AuditProcedureReview
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureResultId = returned.Id, AuditProcedureId = proc.Id, ResultRevision = returned.Revision,
        Decision = AuditProcedureReviewDecisions.ChangesRequired, Comment = "Revise the evidence linkage.",
        ReviewerUserId = seed.Admin.Id, CreatedAt = now.AddSeconds(2)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.FieldworkExecution, report.Value!.Summary.CanonicalStage);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.Contains("returned for preparer rework", StringComparison.Ordinal));

      // A new submitted revision returns to Managerial Review; the historical returned revision does not control the projection.
      proc.CurrentResultRevision = 3;
      proc.Status = AuditProcedureStatuses.Submitted;
      db.AuditProcedureResults.Add(new AuditProcedureResult
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId,
        AuditProcedureId = proc.Id, Revision = 3, InputGeneration = 1, WorkPerformed = "Synthetic corrected procedure work submitted.",
        StructuredResultJson = "{}", EvidenceReferencesJson = "[]", Conclusion = "Resubmitted for review.",
        Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = seed.Admin.Id, SubmittedAt = now.AddSeconds(3)
      });
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
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

      // A hand-built clearance bound to a stale synthetic SRM is not current and cannot hand over the workflow.
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.ManagerialReview, report.Value!.Summary.CanonicalStage);
      Assert.Equal(7, report.Value.Summary.StageIndex);
      Assert.Contains(report.Value.Summary.BlockedReasons,
        x => x.StartsWith("A current Summary Review Memorandum is required for Partner handover.", StringComparison.Ordinal));

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

      // A signature starts the clock, but Report & Delivery remains current until the exact final bundle is available in the client portal.
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
      Assert.Equal(CanonicalEngagementStages.DeliverableRelease, report.Value!.Summary.CanonicalStage);
      Assert.Equal(9, report.Value.Summary.StageIndex);
      Assert.NotNull(report.Value.Summary.ComplianceCountdownDays);
      Assert.True(report.Value.Summary.ComplianceCountdownDays <= 51);
      Assert.Contains(report.Value.Summary.BlockedReasons, x => x.Contains("final bundle available in the client portal", StringComparison.Ordinal));
      Assert.Equal(FileFreezeStates.Scheduled, report.Value.Summary.LocalArchiveState);
      Assert.Equal(ExternalReadOnlyStates.NotRequested, report.Value.Summary.ProviderProtectionState);
      Assert.Contains("does not claim provider immutability", report.Value.Summary.ComplianceWarning!);

      // The dashboard names the people staffed at the level that owns the step. A revoked assignment and another level are not named.
      var unstaffed = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(("EngagementPartner", 0), (unstaffed.Value!.ResponsibleRole, unstaffed.Value.ResponsibleUsers.Count));
      Assert.Equal("REPORT_DELIVERY", unstaffed.Value.CurrentPhase.Code);
      var staffedPartner = PbcSeed.User(seed.FirmId, "Staff"); staffedPartner.DisplayName = "Staffed Partner";
      var formerPartner = PbcSeed.User(seed.FirmId, "Staff"); formerPartner.DisplayName = "Former Partner";
      var staffedManager = PbcSeed.User(seed.FirmId, "Staff"); staffedManager.DisplayName = "Staffed Manager";
      db.Users.AddRange(staffedPartner, formerPartner, staffedManager);
      foreach (var (user, level, revoked) in new[]
      {
        (staffedPartner, StaffingLevels.EngagementPartner, false), (formerPartner, StaffingLevels.EngagementPartner, true),
        (staffedManager, StaffingLevels.AuditManager, false)
      })
      {
        var grant = PbcSeed.Grant(seed.FirmId, user, "Partner", seed.ClientId, engagementId);
        db.RoleGrants.Add(grant);
        db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
        {
          Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = engagementId, UserId = user.Id,
          RoleGrantId = grant.Id, StaffingLevel = level, AssignedByUserId = seed.Admin.Id, AssignedAt = now,
          RevokedAt = revoked ? now : null, RevokedByUserId = revoked ? seed.Admin.Id : null
        });
      }
      await db.SaveChangesAsync();
      var staffed = await EngagementWorkflowWorkspaceQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(["Staffed Partner"], staffed.Value!.ResponsibleUsers);
      Assert.Equal(report.Value.Summary.ComplianceCountdownDays, staffed.Value.Archive!.ComplianceCountdownDays);

      // An expired clock without final delivery or a committed freeze stays in Report & Delivery; elapsed time is not archive evidence.
      freeze.ReportSignedAt = now.AddDays(-61);
      freeze.DueAt = freeze.ReportSignedAt.AddDays(60);
      await db.SaveChangesAsync();
      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.Equal(CanonicalEngagementStages.DeliverableRelease, report.Value!.Summary.CanonicalStage);
      Assert.Equal(0, report.Value.Summary.ComplianceCountdownDays.GetValueOrDefault());
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
      var proposal = new Proposal
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = opportunity.Id,
        Status = CrmStates.ProposalInternalReview, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        CreatedAt = now
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
      await db.SaveChangesAsync();
      var dispatch = await SeedDispatchedOfferAsync(db, proposal, seed.Admin.Id, now, "owner@matrix.example.test");
      await db.SaveChangesAsync();
      var offerHash = dispatch.OfferSha256!;
      var response = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposal.Id,
        new("ACCEPTED", null, offerHash, "Synthetic director", dispatch.Recipient,
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(response.Succeeded, response.Message);
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

      // A later immutable decline is the current offer and never inherits the prior proposal's acceptance.
      var declineLead = new Lead { Id = Guid.NewGuid(), FirmId = seed.FirmId, Name = "Synthetic declined proposal", Source = "Referral", CreatedAt = now.AddSeconds(5) };
      var declineOpportunity = new Opportunity { Id = Guid.NewGuid(), FirmId = seed.FirmId, LeadId = declineLead.Id, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Currency = "QAR", CreatedAt = now.AddSeconds(5) };
      var declined = new Proposal { Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = declineOpportunity.Id,
        Status = CrmStates.ProposalInternalReview, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = now.AddSeconds(5) };
      db.Leads.Add(declineLead);
      db.Opportunities.Add(declineOpportunity);
      db.Proposals.Add(declined);
      await db.SaveChangesAsync();
      var declineDispatch = await SeedDispatchedOfferAsync(db, declined, seed.Admin.Id, now.AddSeconds(5), "owner@matrix.example.test");
      await db.SaveChangesAsync();
      var declineResponse = await PracticeCrmService.RecordProposalResponseAsync(db, partner, declined.Id,
        new("DECLINED", "Revise scope", declineDispatch.OfferSha256!, "Synthetic director", declineDispatch.Recipient,
          "Synthetic decline evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(declineResponse.Succeeded, declineResponse.Message);
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

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Admin, "Partner"));
      db.Engagements.Add(new Engagement
      {
        Id = engagementId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, ServiceRoute = "Audit",
        PeriodStart = "2027-01-01", PeriodEnd = "2027-12-31", ServiceProfileId = "CommercialAudit",
        Status = "Draft", ProfessionalWorkBlocked = true, CreatedAt = now
      });
      var lead = new Lead
      {
        Id = leadId, FirmId = seed.FirmId, Name = "Synthetic annual workflow client", Source = "Referral", CreatedAt = now
      };
      var opportunity = new Opportunity
      {
        Id = opportunityId, FirmId = seed.FirmId, LeadId = leadId, PracticeClientId = seed.ClientId,
        ServiceRoute = "Audit", EntityScope = "TEST", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Currency = "QAR", CreatedAt = now
      };
      var proposal = new Proposal
      {
        Id = proposalId, FirmId = seed.FirmId, PracticeClientId = seed.ClientId, OpportunityId = opportunityId,
        Status = CrmStates.ProposalInternalReview, Fee = 50000m, Currency = "QAR", ServiceProfileId = "AUDIT-2026",
        Scope = "Statutory Financial Audit", Deliverables = "Audit Report", PeriodStart = "2026-01-01",
        PeriodEnd = "2026-12-31", CreatedAt = now
      };
      db.Leads.Add(lead);
      db.Opportunities.Add(opportunity);
      db.Proposals.Add(proposal);
      await db.SaveChangesAsync();
      var dispatch = await SeedDispatchedOfferAsync(db, proposal, seed.Admin.Id, now, "owner@annual.example.test");
      await db.SaveChangesAsync();
      var accepted = await PracticeCrmService.RecordProposalResponseAsync(db, partner, proposalId,
        new("ACCEPTED", null, dispatch.OfferSha256!, "Synthetic director", dispatch.Recipient,
          "Synthetic signed acceptance evidence", Channel: ProposalResponseChannels.Email));
      Assert.True(accepted.Succeeded, accepted.Message);
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

  private static async Task<CommercialNotification> SeedDispatchedOfferAsync(
    AuditSphereDbContext db, Proposal proposal, Guid actorUserId, DateTimeOffset createdAt, string recipient)
  {
    var routed = await (from routing in db.ClientContactRoutings.AsNoTracking()
      join contact in db.ClientContacts.AsNoTracking() on new { routing.FirmId, routing.PracticeClientId, Id = routing.ClientContactId }
        equals new { contact.FirmId, contact.PracticeClientId, Id = contact.Id }
      where routing.FirmId == proposal.FirmId && routing.PracticeClientId == proposal.PracticeClientId &&
        routing.Purpose == CorrespondencePurposes.Commercial && routing.IsPrimaryForPurpose && routing.RevokedAt == null && contact.IsActive
      orderby routing.CreatedAt descending, routing.Id descending
      select new { Routing = routing, Contact = contact }).FirstOrDefaultAsync();
    var contactId = routed?.Contact.Id ?? Guid.NewGuid();
    var routingId = routed?.Routing.Id ?? Guid.NewGuid();
    if (routed is null)
    {
      db.ClientContacts.Add(new ClientContact
      {
        Id = contactId, FirmId = proposal.FirmId, PracticeClientId = proposal.PracticeClientId!.Value,
        FullName = "Synthetic authorized commercial contact", Email = recipient, Role = "Director", Title = "Finance Director",
        SignatoryAuthority = "Authorized commercial contact", Primary = true, IsActive = true
      });
      db.ClientContactRoutings.Add(new ClientContactRouting
      {
        Id = routingId, FirmId = proposal.FirmId, PracticeClientId = proposal.PracticeClientId.Value,
        ClientContactId = contactId, Purpose = CorrespondencePurposes.Commercial, IsPrimaryForPurpose = true,
        CreatedByUserId = actorUserId, CreatedAt = createdAt
      });
    }

    var quotationId = Guid.NewGuid();
    var documentId = Guid.NewGuid();
    var bytes = Encoding.UTF8.GetBytes($"Synthetic exact rendered offer for proposal {proposal.Id:D}.");
    var offerHash = Hashing.Sha256Hex(bytes);
    db.QuotationVersions.Add(new QuotationVersion
    {
      Id = quotationId, FirmId = proposal.FirmId, ProposalId = proposal.Id, Revision = 1,
      Currency = proposal.Currency, BaseAmount = proposal.Fee, Fee = proposal.Fee,
      InputHash = Hashing.Sha256Hex($"synthetic quotation {proposal.Id:D}"),
      Status = QuotationStates.Approved, RequiredApprovalsJson = "[]", CreatedByUserId = actorUserId,
      CreatedAt = createdAt, ApprovedAt = createdAt
    });
    db.CommercialDocuments.Add(new CommercialDocument
    {
      Id = documentId, FirmId = proposal.FirmId, ProposalId = proposal.Id, QuotationVersionId = quotationId,
      Kind = CommercialDocumentKinds.Quotation, TemplateVersion = "SYNTHETIC-QUOTATION-v1", ProfileVersion = 1,
      FileName = $"Quotation-{proposal.Id:N}.docx", ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
      Bytes = bytes, Sha256Hex = offerHash, CreatedByUserId = actorUserId, CreatedAt = createdAt
    });
    proposal.Status = CrmStates.ProposalSent;
    proposal.SentAt = createdAt;
    proposal.SentOfferSha256 = offerHash;
    var dispatch = new CommercialNotification
    {
      Id = Guid.NewGuid(), FirmId = proposal.FirmId, Kind = CommercialNotificationKinds.Proposal,
      ProposalId = proposal.Id, PracticeClientId = proposal.PracticeClientId, QuotationVersionId = quotationId,
      DocumentId = documentId, OfferSha256 = offerHash, RecipientContactId = contactId, RecipientRoutingId = routingId,
      RecipientAuthorityBasis = ProposalRecipientAuthorityBases.ClientCommercialRouting, Recipient = recipient,
      Subject = "Synthetic exact rendered offer", Body = "Synthetic dispatch evidence", CreatedAt = createdAt
    };
    db.CommercialNotifications.Add(dispatch);
    return dispatch;
  }
}
