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

      // 2. Add proposal -> DualKeyPending
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

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.AdvanceBilling, report.Value!.Summary.CanonicalStage);
      Assert.Equal(4, report.Value.Summary.StageIndex);

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
      db.MaterialityAssessments.Add(new MaterialityAssessment
      {
        Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = seed.ClientId,
        EngagementId = engagementId, Status = MaterialityStatuses.Approved,
        BenchmarkSource = "TrialBalance", BenchmarkVersion = "1.0",
        Rationale = "Standard revenue benchmark applied",
        BenchmarkAmount = 1000000m, RateApplied = 0.01m,
        OverallMateriality = 10000m, PerformanceMateriality = 7500m,
        ClearlyTrivialThreshold = 500m, ActorId = seed.Admin.Id, CreatedAt = now
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

      report = await EngagementLifecycleQuery.GetAsync(db, partner, engagementId);
      Assert.True(report.Succeeded);
      Assert.Equal(CanonicalEngagementStages.PartnerApproval, report.Value!.Summary.CanonicalStage);
      Assert.Equal(8, report.Value.Summary.StageIndex);

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

      // 11. Unauthorized actor is scope-denied
      var denied = await EngagementLifecycleQuery.GetAsync(db, unauthorized, engagementId);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }
}
