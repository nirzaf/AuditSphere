using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class PlanningResourcesAndMaterialityTests
{
  private static async Task SeedRiskBandMappingAsync(PgTestSchema pg, World w, decimal cashBalance)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var datasetId = Guid.NewGuid();
    var mappingId = Guid.NewGuid();
    var accounts = new (string Code, string Name, decimal Amount, string Destination, string Section, string? Area)[]
    {
      ("1000", "Cash", cashBalance, "CASH", "ASSETS", "Cash"),
      ("1100", "Receivables", 1_000_000m - cashBalance, "RECEIVABLES", "ASSETS", null),
      ("2000", "Payables", -300_000m, "PAYABLES", "LIABILITIES", null),
      ("3000", "Equity", -200_000m, "EQUITY", "EQUITY", null),
      ("4000", "Revenue", -2_000_000m, "REVENUE", "INCOME", null),
      ("5000", "Cost of sales", 1_300_000m, "COST_OF_SALES", "EXPENSE", null),
      ("5100", "Administration", 150_000m, "ADMIN_EXPENSES", "EXPENSE", null),
      ("5900", "Income tax", 50_000m, "INCOME_TAX", "EXPENSE", null)
    };
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
      SourceKind = "Raw", Currency = "QAR", Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Loading, NormalizedDatasetDigest = Hashing.Sha256Hex(datasetId.ToString()),
      ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = w.Users["senior"].Id
    });
    db.TrialBalanceRows.AddRange(accounts.Select(a => new TrialBalanceRow
    {
      Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = a.Code, AccountName = a.Name,
      Amount = a.Amount, Currency = "QAR", Entity = "TEST"
    }));
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == datasetId)
      .ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
      DatasetId = datasetId, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = w.Users["senior"].Id,
      ApprovedByUserId = w.Users["manager"].Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    db.MappingAllocations.AddRange(accounts.Select(a => new MappingAllocation
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId,
      MappingVersionId = mappingId, SourceAccountCode = a.Code, DestinationCode = a.Destination,
      StatementSection = a.Section, AuditArea = a.Area, Fraction = 1m, Rationale = "Approved source mapping",
      CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync();
  }

  private static async Task<Guid> AddFieldworkProcedureAsync(PgTestSchema pg, World w, Guid riskId)
  {
    var partner = w.Actor("partner", "Partner");
    var manager = w.Actor("manager", "Manager");
    await using var db = new AuditSphereDbContext(pg.Options);
    Assert.True((await ResourcePlanningService.AddCertificationAsync(db, manager,
      new(w.Users["partner2"].Id, "CPA", "AICPA", null))).Succeeded);
    Assert.True((await ResourcePlanningService.AddCertificationAsync(db, manager,
      new(w.Users["manager"].Id, "ACCA", "ACCA", null))).Succeeded);
    Assert.True((await StaffingService.AssignAsync(db, partner,
      new(w.EngagementId, w.Users["partner2"].Id, StaffingLevels.EngagementPartner))).Succeeded);
    Assert.True((await StaffingService.AssignAsync(db, partner,
      new(w.EngagementId, w.Users["manager"].Id, StaffingLevels.AuditManager))).Succeeded);
    Assert.True((await StaffingService.AssignAsync(db, manager,
      new(w.EngagementId, w.Users["senior"].Id, StaffingLevels.SeniorAuditor))).Succeeded);
    Assert.True((await StaffingService.AssignAsync(db, manager,
      new(w.EngagementId, w.Users["associate"].Id, StaffingLevels.StaffAssociate))).Succeeded);
    var published = await AuditProgramService.PublishAsync(db, partner,
      new PublishAuditProgramRequest("2026.1", AuditProgramCatalog.SourceHash));
    Assert.True(published.Succeeded, published.Message);
    var adopted = await AuditProgramService.AdoptAsync(db, partner,
      new AdoptAuditProgramRequest(w.EngagementId, published.Value!.ProgramVersionId));
    Assert.True(adopted.Succeeded, adopted.Message);
    var procedure = await db.AuditProcedures.Where(x => x.EngagementId == w.EngagementId)
      .OrderBy(x => x.SourceProcedureId).FirstAsync();
    Assert.True((await AuditProgramService.DecideApplicabilityAsync(db, partner,
      new(procedure.Id, AuditApplicabilityStatuses.Applicable, null))).Succeeded);
    Assert.True((await AuditProgramService.LinkRiskAsync(db, partner, procedure.Id, riskId)).Succeeded);
    return procedure.Id;
  }

  private static async Task<Guid> CalculateAndApproveTestMateriality(PgTestSchema pg, World w)
  {
    Guid assessmentId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var calculated = await MaterialityEngineService.CalculateAsync(db, w.Actor("manager", "Manager"),
        new(w.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Risk-routing test materiality"));
      Assert.True(calculated.Succeeded, calculated.Message);
      assessmentId = calculated.Value!.AssessmentId;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var approved = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db,
        w.Actor("partner", "Partner"), assessmentId);
      Assert.True(approved.Succeeded, approved.Message);
    }
    return assessmentId;
  }

  [Fact]
  public async Task AuditProcedure_MappedNegativeFslIControlsRoutingAndStaleApprovalFailsClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await SeedRiskBandMappingAsync(pg, w, -15_000m); // TE is inclusive and the classifier uses absolute signed balance.
    await CalculateAndApproveTestMateriality(pg, w); // PM 20,000; TE 15,000.

    Guid riskId;
    Guid criticalEstimateRiskId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditPlanningService.CreateAuditRiskAsync(db, w.Actor("partner", "Partner"),
        new(w.EngagementId, "Cash", "Existence", "Cash balance and reconciliation", "Cash controls",
          SignificanceDecisions.Normal, null, "Reconcile bank evidence"));
      Assert.True(created.Succeeded, created.Message);
      riskId = created.Value!.RiskId;
      Assert.True((await RiskBandService.AssessAsync(db, w.Actor("partner", "Partner"),
        new(riskId, 1, 1, false, "No qualitative escalation"))).Succeeded);
      var critical = await AuditPlanningService.CreateAuditRiskAsync(db, w.Actor("partner", "Partner"),
        new(w.EngagementId, "Cash", "Valuation", "Critical accounting estimate for expected credit losses", "Estimate uncertainty",
          SignificanceDecisions.Normal, null, "Test model, assumptions and source evidence"));
      Assert.True(critical.Succeeded, critical.Message);
      criticalEstimateRiskId = critical.Value!.RiskId;
    }
    var procedureId = await AddFieldworkProcedureAsync(pg, w, riskId);
    string sourceProcedureId;
    await using (var db = new AuditSphereDbContext(pg.Options))
      sourceProcedureId = await db.AuditProcedures.Where(x => x.Id == procedureId).Select(x => x.SourceProcedureId).SingleAsync();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var workspace = await AuditFieldworkWorkspaceQuery.GetAsync(db, w.Actor("manager", "Manager"), w.EngagementId);
      Assert.True(workspace.Succeeded, workspace.Message);
      var risk = workspace.Value!.Risks.Single(x => x.Id == riskId);
      Assert.Equal(RiskBands.Green, risk.Band);
      Assert.Equal(RiskBands.Amber, risk.EffectiveBand);
      Assert.Equal(-15_000m, risk.Balance);
      Assert.Equal("QAR", risk.Currency);
      Assert.Equal(15_000m, risk.TolerableError);
      Assert.Equal(20_000m, risk.PlanningMateriality);
      Assert.NotNull(risk.RiskAssessmentId);
      Assert.NotNull(risk.MaterialityCalculationId);
      Assert.NotNull(risk.MappingVersionId);
      Assert.NotNull(risk.DatasetDigest);
      var critical = workspace.Value.Risks.Single(x => x.Id == criticalEstimateRiskId);
      Assert.Equal(RiskBands.Red, critical.EffectiveBand);
      Assert.Contains("critical estimate identified", critical.Explanation, StringComparison.OrdinalIgnoreCase);
    }

    Guid amberResultId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staff = await AuditProgramService.SubmitResultAsync(db, w.Actor("associate", "Staff"),
        new(procedureId, 1, "Tested cash", "{\"result\":\"PASS\"}", ["ref:cash-1"], "Cash reconciled."));
      Assert.Equal(ErrorCodes.ScopeDenied, staff.ErrorCode);
      Assert.Contains("Senior Auditor or above", staff.Message);
      var senior = await AuditProgramService.SubmitResultAsync(db, w.Actor("senior", "Senior"),
        new(procedureId, 1, "Tested cash", "{\"result\":\"PASS\"}", ["ref:cash-1"], "Cash reconciled."));
      Assert.True(senior.Succeeded, senior.Message);
      amberResultId = senior.Value!.AuditProcedureResultId;
    }

    // An identified high-inherent-risk assessment forces Red even when the signed balance remains exactly TE.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await RiskBandService.AssessAsync(db, w.Actor("senior", "Senior"),
        new(riskId, 3, 2, false, "High inherent valuation uncertainty"))).Succeeded);
      var escalated = await AuditFieldworkWorkspaceQuery.GetAsync(db, w.Actor("manager", "Manager"), w.EngagementId);
      var risk = escalated.Value!.Risks.Single(x => x.Id == riskId);
      Assert.Equal(RiskBands.Red, risk.Band);
      Assert.Equal(RiskBands.Red, risk.EffectiveBand);
      Assert.Contains("high inherent risk", risk.Explanation, StringComparison.OrdinalIgnoreCase);
      var stale = await AuditProgramService.ReviewResultAsync(db, w.Actor("manager", "Manager"),
        new(amberResultId, AuditProcedureReviewDecisions.Reviewed, null));
      Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
    }

    // A remapping changes both amount and source identity. Stale materiality blocks review until recalculated;
    // the refreshed calculation then requires a new Red Manager result rather than silently blessing Amber work.
    await Task.Delay(20);
    await SeedRiskBandMappingAsync(pg, w, -25_000m);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await AuditProgramService.ReviewResultAsync(db, w.Actor("manager", "Manager"),
        new(amberResultId, AuditProcedureReviewDecisions.Reviewed, null));
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
      var workspace = await AuditFieldworkWorkspaceQuery.GetAsync(db, w.Actor("manager", "Manager"), w.EngagementId);
      Assert.Contains("approved mapping or trial balance changed",
        workspace.Value!.Risks.Single(x => x.Id == riskId).Blocker);
    }
    await CalculateAndApproveTestMateriality(pg, w);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = await AuditProgramService.ReviewResultAsync(db, w.Actor("manager", "Manager"),
        new(amberResultId, AuditProcedureReviewDecisions.Reviewed, null));
      Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
      Assert.Contains("risk, materiality calculation, or approved mapping changed", stale.Message);
      var refreshed = await AuditFieldworkWorkspaceQuery.GetAsync(db, w.Actor("manager", "Manager"), w.EngagementId);
      Assert.Equal(RiskBands.Red, refreshed.Value!.Risks.Single(x => x.Id == riskId).EffectiveBand);

      var senior = await AuditProgramService.SubmitResultAsync(db, w.Actor("senior", "Senior"),
        new(procedureId, 1, "Retested cash", "{\"result\":\"PASS\"}", ["ref:cash-2"], "Updated conclusion."));
      Assert.Equal(ErrorCodes.ScopeDenied, senior.ErrorCode);
      var manager = await AuditProgramService.SubmitResultAsync(db, w.Actor("manager", "Manager"),
        new(procedureId, 1, "Retested cash", "{\"result\":\"PASS\"}", ["ref:cash-2"], "Updated conclusion."));
      Assert.True(manager.Succeeded, manager.Message);
      var submitted = manager.Value!;
      Assert.Equal(2, submitted.Revision);
      var savedResult = await db.AuditProcedureResults.AsNoTracking()
        .SingleAsync(x => x.Id == submitted.AuditProcedureResultId);
      Assert.Contains("_auditSphereRiskBasis", savedResult.StructuredResultJson);
      Assert.Contains("\"Band\":\"RED\"", savedResult.StructuredResultJson);

      var partner = await AuditProgramService.ReviewResultAsync(db, w.Actor("partner2", "Partner"),
        new(submitted.AuditProcedureResultId, AuditProcedureReviewDecisions.Reviewed, null));
      Assert.True(partner.Succeeded, partner.Message);
    }

    // Unlinking the risk cannot make an already reviewed result look current at completion.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await AuditProgramService.LinkRiskAsync(db, w.Actor("partner", "Partner"), procedureId, null)).Succeeded);
      var unlinkedCompletion = await AuditFieldworkService.EvaluateCompletionAsync(db, w.Actor("partner", "Partner"), w.EngagementId);
      Assert.Contains($"procedure:{sourceProcedureId}:risk-basis-stale", unlinkedCompletion.Value!.Blockers);
      Assert.True((await AuditProgramService.LinkRiskAsync(db, w.Actor("partner", "Partner"), procedureId, riskId)).Succeeded);
    }

    // A later qualitative assessment stales an already-reviewed procedure even when the FSLI still keeps it Red.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await RiskBandService.AssessAsync(db, w.Actor("senior", "Senior"),
        new(riskId, 1, 1, false, "Reassessed after new evidence"))).Succeeded);
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, w.Actor("partner", "Partner"), w.EngagementId);
      Assert.Contains($"procedure:{sourceProcedureId}:risk-basis-stale", completion.Value!.Blockers);
    }
  }
}
