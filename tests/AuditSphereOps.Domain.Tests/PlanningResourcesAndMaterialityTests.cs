using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// STE package 3 with real role identities: four-level staffing mapped to engagement roles, the resource grid,
/// phase/risk-area budgets that reconcile, automatic three-tier materiality from the mapped trial balance with
/// staleness, and green/amber/red routing whose colour cannot be edited apart from its inputs.
/// </summary>
[Trait("Profile", "Database")]
public sealed partial class PlanningResourcesAndMaterialityTests
{
  private sealed record World(Guid FirmId, Guid ClientId, Guid EngagementId, Dictionary<string, AppUser> Users)
  {
    public ActorContext Actor(string name, params string[] roles) => new(Users[name].Id, FirmId, Users[name].SessionEpoch, roles);
  }

  private static AppUser NewUser(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = name + "-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-planning",
    Email = $"{name}-{Guid.NewGuid():N}@example.test", DisplayName = name, UserKind = "Staff", CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role, Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role, ClientId = clientId, EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<World> SeedAsync(PgTestSchema pg)
  {
    var (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    var users = new[] { "partner", "partner2", "manager", "senior", "associate", "outsider" }.ToDictionary(x => x, x => NewUser(firmId, x));
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.Id == engagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Active")
      .SetProperty(x => x.ProfessionalWorkBlocked, false).SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
    db.Users.AddRange(users.Values);
    db.RoleGrants.AddRange(Grant(firmId, users["partner"], "Partner"), Grant(firmId, users["partner2"], "Partner"),
      Grant(firmId, users["manager"], "Manager"));
    await db.SaveChangesAsync();
    return new(firmId, clientId, engagementId, users);
  }

  [Fact]
  public async Task Staffing_ConcurrentAssignmentPublishesOneGrantAndAssignment()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    async Task<Guid> Assign()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var result = await StaffingService.AssignAsync(db, w.Actor("partner", "Partner"),
        new(w.EngagementId, w.Users["associate"].Id, StaffingLevels.StaffAssociate));
      Assert.True(result.Succeeded, result.Message);
      return result.Value;
    }
    var results = await Task.WhenAll(Assign(), Assign());
    Assert.Equal(results[0], results[1]);
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.Single(await check.EngagementStaffAssignments.Where(x => x.UserId == w.Users["associate"].Id).ToListAsync());
    Assert.Single(await check.RoleGrants.Where(x => x.UserId == w.Users["associate"].Id).ToListAsync());
    Assert.Single(await check.RoleGrantChangeEvidences.Where(x => x.TargetUserId == w.Users["associate"].Id).ToListAsync());
  }

  [Fact]
  public async Task Staffing_RevokeInsideCallerTransactionPreservesIndependentGrant()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var independent = Grant(w.FirmId, w.Users["associate"], "Staff", w.ClientId, w.EngagementId);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(independent);
      await db.SaveChangesAsync();
      await using var tx = await db.Database.BeginTransactionAsync();
      var result = await StaffingService.AssignAsync(db, w.Actor("partner", "Partner"),
        new(w.EngagementId, w.Users["associate"].Id, StaffingLevels.StaffAssociate));
      Assert.True(result.Succeeded, result.Message);
      Assert.True((await StaffingService.RevokeAsync(db, w.Actor("partner", "Partner"), result.Value)).Succeeded);
      await tx.CommitAsync();
    }
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.NotNull((await check.EngagementStaffAssignments.SingleAsync(x => x.UserId == w.Users["associate"].Id)).RevokedAt);
    Assert.Null((await check.RoleGrants.SingleAsync(x => x.Id == independent.Id)).RevokedAt);
    Assert.Equal(w.Users["associate"].SessionEpoch,
      (await check.Users.SingleAsync(x => x.Id == w.Users["associate"].Id)).SessionEpoch);
  }

  [Fact]
  public async Task Staffing_ComposesCallerTransaction_AssignmentAndRevocationRollbackTogether()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    Guid assignmentId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await using var tx = await db.Database.BeginTransactionAsync();
      var assigned = await StaffingService.AssignAsync(db, w.Actor("partner", "Partner"),
        new(w.EngagementId, w.Users["associate"].Id, StaffingLevels.StaffAssociate));
      Assert.True(assigned.Succeeded, assigned.Message);
      assignmentId = assigned.Value;
      Assert.True((await StaffingService.RevokeAsync(db, w.Actor("partner", "Partner"), assignmentId)).Succeeded);
      Assert.NotNull(db.Database.CurrentTransaction);
      await tx.RollbackAsync();
    }
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.False(await check.EngagementStaffAssignments.AnyAsync(x => x.Id == assignmentId));
    Assert.False(await check.RoleGrants.AnyAsync(x => x.UserId == w.Users["associate"].Id));
    Assert.False(await check.RoleGrantChangeEvidences.AnyAsync(x => x.TargetUserId == w.Users["associate"].Id));
    Assert.Equal(w.Users["associate"].SessionEpoch,
      (await check.Users.SingleAsync(x => x.Id == w.Users["associate"].Id)).SessionEpoch);
  }

  [Fact]
  public async Task Staffing_ExpiredHigherRoleDoesNotElevateAnActiveManager()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var expired = Grant(w.FirmId, w.Users["manager"], "Partner");
    expired.GrantedAt = DateTimeOffset.UtcNow.AddDays(-1);
    expired.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    db.RoleGrants.Add(expired);
    await db.SaveChangesAsync();
    var manager = w.Actor("manager", "Manager", "Partner");
    var result = await StaffingService.AssignAsync(db, manager,
      new(w.EngagementId, w.Users["outsider"].Id, StaffingLevels.EngagementPartner));
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    Assert.False(await db.EngagementStaffAssignments.AnyAsync(x => x.UserId == w.Users["outsider"].Id));
  }

  [Fact]
  public async Task Staffing_DoesNotReuseExpiredEngagementGrant()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var expired = Grant(w.FirmId, w.Users["associate"], "Staff", w.ClientId, w.EngagementId);
    expired.GrantedAt = DateTimeOffset.UtcNow.AddDays(-1);
    expired.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    db.RoleGrants.Add(expired);
    await db.SaveChangesAsync();
    var result = await StaffingService.AssignAsync(db, w.Actor("partner", "Partner"),
      new(w.EngagementId, w.Users["associate"].Id, StaffingLevels.StaffAssociate));
    Assert.True(result.Succeeded, result.Message);
    var assignment = await db.EngagementStaffAssignments.SingleAsync(x => x.Id == result.Value);
    Assert.NotEqual(expired.Id, assignment.RoleGrantId);
    var grant = await db.RoleGrants.SingleAsync(x => x.Id == assignment.RoleGrantId);
    Assert.Null(grant.ExpiresAt);
    Assert.True(await db.RoleGrantChangeEvidences.AnyAsync(x => x.RoleGrantId == grant.Id));
    Assert.NotNull(expired.RevokedAt);
    Assert.Equal(w.Users["associate"].SessionEpoch + 1,
      (await db.Users.SingleAsync(x => x.Id == w.Users["associate"].Id)).SessionEpoch);
    Assert.True(await db.RoleGrantChangeEvidences.AnyAsync(x => x.RoleGrantId == expired.Id && x.Source == "EXPIRY"));
  }

  private static async Task SeedApprovedMappingAsync(PgTestSchema pg, World w, Guid? replaces = null)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    var datasetId = Guid.NewGuid();
    var mappingId = Guid.NewGuid();
    var accounts = new (string Code, string Name, decimal Amount, string Destination, string Section)[]
    {
      ("1000", "Cash", 600_000m, "CASH", "ASSETS"), ("1100", "Receivables", 400_000m, "RECEIVABLES", "ASSETS"),
      ("2000", "Payables", -300_000m, "PAYABLES", "LIABILITIES"), ("3000", "Equity", -200_000m, "EQUITY", "EQUITY"),
      ("4000", "Revenue", -2_000_000m, "REVENUE", "INCOME"), ("5000", "Cost of sales", 1_300_000m, "COST_OF_SALES", "EXPENSE"),
      ("5100", "Administration", 150_000m, "ADMIN_EXPENSES", "EXPENSE"), ("5900", "Income tax", 50_000m, "INCOME_TAX", "EXPENSE")
    };
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, SourceKind = "Raw", Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading, NormalizedDatasetDigest = Hashing.Sha256Hex(datasetId.ToString()),
      ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = w.Users["senior"].Id
    });
    db.TrialBalanceRows.AddRange(accounts.Select(a => new TrialBalanceRow
    {
      Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = a.Code, AccountName = a.Name, Amount = a.Amount, Currency = "QAR", Entity = "TEST"
    }));
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == datasetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, DatasetId = datasetId, TaxonomyVersion = "tax-v1",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved, CreatedByUserId = w.Users["senior"].Id,
      ApprovedByUserId = w.Users["manager"].Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    db.MappingAllocations.AddRange(accounts.Select(a => new MappingAllocation
    {
      Id = Guid.NewGuid(), FirmId = w.FirmId, ClientId = w.ClientId, EngagementId = w.EngagementId, MappingVersionId = mappingId,
      SourceAccountCode = a.Code, DestinationCode = a.Destination, StatementSection = a.Section, Fraction = 1m, Rationale = "Mapped", CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync();
  }

  [Fact]
  public async Task Materiality_IsCalculatedFromTheMappedTrialBalance_AndGoesStaleWhenTheSourceIsReplaced()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var preparer = w.Actor("manager", "Manager");
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(ErrorCodes.GateBlocked, (await MaterialityEngineService.GetSourceAsync(db, preparer, w.EngagementId)).ErrorCode);
    await SeedApprovedMappingAsync(pg, w);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var source = (await MaterialityEngineService.GetSourceAsync(db, preparer, w.EngagementId)).Value!;
      decimal? Amount(string kind, string? code = null) => source.Options.Single(x => x.Kind == kind && x.DestinationCode == code).Amount;
      Assert.Equal(2_000_000m, Amount(MaterialityBenchmarks.Revenue));
      Assert.Equal(550_000m, Amount(MaterialityBenchmarks.ProfitBeforeTax)); // tax line excluded
      Assert.Equal(1_000_000m, Amount(MaterialityBenchmarks.TotalAssets));
      Assert.Equal(700_000m, Amount(MaterialityBenchmarks.NetAssets));
      Assert.Equal(1_500_000m, Amount(MaterialityBenchmarks.TotalExpenses));
      Assert.Equal(400_000m, Amount(MaterialityBenchmarks.MappedLine, "RECEIVABLES"));
    }

    Guid assessmentId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.AuditPlanning.Invalid, (await MaterialityEngineService.CalculateAsync(db, preparer,
        new(w.EngagementId, MaterialityBenchmarks.Revenue, null, 5m, 75m, 5m, "Revenue-driven entity"))).ErrorCode); // out of policy
      var result = await MaterialityEngineService.CalculateAsync(db, preparer,
        new(w.EngagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Revenue-driven trading entity"));
      Assert.True(result.Succeeded, result.Message);
      var calc = result.Value!.Calculation;
      Assert.Equal((2_000_000m, 20_000m, 15_000m, 1_000m), (calc.BenchmarkAmount, calc.PlanningMateriality, calc.TolerableError, calc.SadThreshold));
      Assert.Equal((1, MaterialityCalculator.PolicyVersion), (calc.SourceLineCount, calc.PolicyVersion));
      assessmentId = result.Value.AssessmentId;
      var assessment = await db.MaterialityAssessments.AsNoTracking().SingleAsync(x => x.Id == assessmentId);
      Assert.Equal((20_000m, 15_000m, 1_000m), (assessment.OverallMateriality, assessment.PerformanceMateriality, assessment.ClearlyTrivialThreshold));
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ScopeDenied, (await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, preparer, assessmentId)).ErrorCode);
      Assert.True((await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, w.Actor("partner", "Partner"), assessmentId)).Succeeded);
      Assert.Equal(MaterialityCalculationStates.Approved, (await MaterialityEngineService.GetLatestAsync(db, w.FirmId, w.EngagementId))!.State);
    }

    // A replacement approved mapping makes the approved calculation stale; a fresh one bound to the old source cannot be approved.
    Guid draftOnOldSource;
    await using (var db = new AuditSphereDbContext(pg.Options))
      draftOnOldSource = (await MaterialityEngineService.CalculateAsync(db, preparer,
        new(w.EngagementId, MaterialityBenchmarks.TotalAssets, null, 1m, 60m, 3m, "Asset-heavy"))).Value!.AssessmentId;
    await Task.Delay(20);
    await SeedApprovedMappingAsync(pg, w);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(MaterialityCalculationStates.Stale, (await MaterialityEngineService.GetLatestAsync(db, w.FirmId, w.EngagementId))!.State);
      Assert.Equal(ErrorCodes.GenerationStale, (await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, w.Actor("partner", "Partner"), draftOnOldSource)).ErrorCode);
      Assert.False(await MaterialityEngineService.IsAssessmentCurrentAsync(db, w.FirmId, assessmentId));
      var completion = await AuditFieldworkService.EvaluateCompletionAsync(db, w.Actor("partner", "Partner"), w.EngagementId);
      Assert.Contains("materiality:stale", completion.Value!.Blockers);
      var calc = await db.MaterialityCalculations.AsNoTracking().FirstAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE materiality_calculations SET planning_materiality = 1 WHERE id = {calc.Id}"));
    }
  }

  [Fact]
  public void MaterialityCalculator_FailsClosedOnLossesAndOutOfPolicyRates()
  {
    MappedBenchmarkLine L(string section, decimal amount, string dest = "X") => new("A", dest, section, amount);
    var loss = MaterialityCalculator.DeriveBenchmark(MaterialityBenchmarks.ProfitBeforeTax, null, [L("INCOME", -100m), L("EXPENSE", 150m)]);
    Assert.Equal(-50m, loss!.Value.Amount);
    Assert.Throws<ArgumentOutOfRangeException>(() => MaterialityCalculator.Calculate(loss.Value.Amount, 2, 5m, 75m, 5m));
    Assert.Null(MaterialityCalculator.DeriveBenchmark(MaterialityBenchmarks.TotalAssets, null, [L("INCOME", -1m)]));
    Assert.NotNull(MaterialityCalculator.Validate(MaterialityBenchmarks.ProfitBeforeTax, 2m, 75m, 5m));
    Assert.NotNull(MaterialityCalculator.Validate(MaterialityBenchmarks.Revenue, 1m, 80m, 5m));
    Assert.Null(MaterialityCalculator.Validate(MaterialityBenchmarks.ProfitBeforeTax, 5m, 50m, 1m));
    Assert.Equal(RiskBands.Green, RiskBandRules.Band(1, 2, false, false));
    Assert.Equal(RiskBands.Amber, RiskBandRules.Band(1, 3, false, false));
    Assert.Equal(RiskBands.Amber, RiskBandRules.Band(2, 2, false, false));
    Assert.Equal(RiskBands.Red, RiskBandRules.Band(2, 3, false, false));
    Assert.Equal(RiskBands.Red, RiskBandRules.Band(1, 1, true, false));
    Assert.Equal(RiskBands.Red, RiskBandRules.Band(1, 1, false, true));
  }

  [Fact]
  public async Task Staffing_MapsFourLevelsToEngagementRoles_AndRiskBandsRouteOwnersAndPartnerReview()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.Actor("partner", "Partner");
    var manager = w.Actor("manager", "Manager");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Partner level needs a current certification; an expired one does not count.
      Assert.True((await ResourcePlanningService.AddCertificationAsync(db, manager, new(w.Users["partner2"].Id, "ACCA", "ACCA", new DateOnly(2020, 1, 1)))).Succeeded);
      Assert.Equal("staffing.certification", (await StaffingService.AssignAsync(db, partner, new(w.EngagementId, w.Users["partner2"].Id, StaffingLevels.EngagementPartner))).ErrorCode);
      Assert.True((await ResourcePlanningService.AddCertificationAsync(db, manager, new(w.Users["partner2"].Id, "CPA", "AICPA", null))).Succeeded);
      Assert.True((await ResourcePlanningService.AddCertificationAsync(db, manager, new(w.Users["manager"].Id, "ACCA", "ACCA", null))).Succeeded);
    }
    Guid AssignOk(AuditSphereDbContext db, ActorContext actor, string user, string level)
    {
      var result = StaffingService.AssignAsync(db, actor, new(w.EngagementId, w.Users[user].Id, level)).GetAwaiter().GetResult();
      Assert.True(result.Succeeded, $"{user}: {result.Message}");
      return result.Value;
    }
    Guid seniorAssignment;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      AssignOk(db, partner, "partner2", StaffingLevels.EngagementPartner);
      AssignOk(db, partner, "manager", StaffingLevels.AuditManager);
      seniorAssignment = AssignOk(db, manager, "senior", StaffingLevels.SeniorAuditor);
      AssignOk(db, manager, "associate", StaffingLevels.StaffAssociate);
      Assert.Equal("staffing.conflict", (await StaffingService.AssignAsync(db, partner, new(w.EngagementId, w.Users["outsider"].Id, StaffingLevels.EngagementPartner))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await StaffingService.AssignAsync(db, partner, new(w.EngagementId, w.Users["partner"].Id, StaffingLevels.SeniorAuditor))).ErrorCode); // self
      Assert.Equal(ErrorCodes.ScopeDenied, (await StaffingService.AssignAsync(db, manager, new(w.EngagementId, w.Users["outsider"].Id, StaffingLevels.EngagementPartner))).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await StaffingService.AssignAsync(db, manager, new(w.EngagementId, w.Users["manager"].Id, StaffingLevels.SeniorAuditor))).ErrorCode);
      var roles = await db.RoleGrants.AsNoTracking().Where(x => x.EngagementId == w.EngagementId && x.RevokedAt == null)
        .ToDictionaryAsync(x => x.UserId, x => x.Role);
      Assert.Equal(("Partner", "Manager", "Senior", "Staff"),
        (roles[w.Users["partner2"].Id], roles[w.Users["manager"].Id], roles[w.Users["senior"].Id], roles[w.Users["associate"].Id]));
      var candidates = await StaffingService.CandidatesAsync(db, partner);
      Assert.Contains(candidates, x => x.UserId == w.Users["manager"].Id && x.Certified);
      Assert.False((await PracticeTimeService.GetBudgetBreakdownAsync(db, partner, w.EngagementId)).Succeeded);
      var list = (await StaffingService.ListAsync(db, w.Actor("senior", "Senior"), w.EngagementId)).Value!;
      Assert.Equal([StaffingLevels.EngagementPartner, StaffingLevels.AuditManager, StaffingLevels.SeniorAuditor, StaffingLevels.StaffAssociate], list.Select(x => x.Level));
      // The Senior grant is engagement-scoped: it never reaches a sibling engagement.
      var sibling = Guid.NewGuid();
      db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement { Id = sibling, FirmId = w.FirmId, PracticeClientId = w.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.False((await AuthorizationDecision.AuthorizeAsync(db, w.Actor("senior", "Senior"),
        new AuthorizationRequest(w.FirmId, w.ClientId, sibling, ["Senior"], InternalOnly: true))).Succeeded);
    }

    // Risks: normal-green, normal-amber, significant-red.
    Guid green, amber, red;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      async Task<Guid> Risk(string area, string decision) => (await AuditPlanningService.CreateAuditRiskAsync(db, w.Actor("senior", "Senior"),
        new CreateAuditRiskRequest(w.EngagementId, area, "Existence", $"{area} risk", "Volume", decision, null, "Substantive testing"))).Value!.RiskId;
      green = await Risk("Prepayments", SignificanceDecisions.Normal);
      amber = await Risk("Inventory", SignificanceDecisions.Normal);
      red = await Risk("Revenue", SignificanceDecisions.Significant);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var senior = w.Actor("senior", "Senior");
      Assert.True((await RiskBandService.AssessAsync(db, senior, new(green, 1, 2, false, "Low volume"))).Succeeded);
      Assert.True((await RiskBandService.AssessAsync(db, senior, new(amber, 2, 2, false, "Moderate"))).Succeeded);
      Assert.True((await RiskBandService.AssessAsync(db, w.Actor("partner2", "Partner"), new(red, 1, 1, false, "Presumed fraud risk in revenue"))).Succeeded);
      var routing = (await RiskBandService.GetRoutingAsync(db, senior, w.EngagementId)).Value!.ToDictionary(x => x.RiskId);
      Assert.Equal((RiskBands.Green, RiskBands.Amber, RiskBands.Red), (routing[green].Band, routing[amber].Band, routing[red].Band));
      Assert.True(routing[red].PartnerReviewRequired);

      // Owner levels: associate may own green but not amber; red needs a manager.
      Assert.True((await RiskBandService.AssignOwnerAsync(db, manager, green, w.Users["associate"].Id)).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await RiskBandService.AssignOwnerAsync(db, manager, amber, w.Users["associate"].Id)).ErrorCode);
      Assert.True((await RiskBandService.AssignOwnerAsync(db, manager, amber, w.Users["senior"].Id)).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await RiskBandService.AssignOwnerAsync(db, manager, red, w.Users["senior"].Id)).ErrorCode);
      Assert.True((await RiskBandService.AssignOwnerAsync(db, manager, red, w.Users["manager"].Id)).Succeeded);

      // Partner review: the assessing partner cannot clear; a manager cannot; the other partner can.
      Assert.Contains($"risk:{red}:red-partner-review", (await AuditFieldworkService.EvaluateCompletionAsync(db, w.Actor("partner", "Partner"), w.EngagementId)).Value!.Blockers);
      Assert.Equal(ErrorCodes.ScopeDenied, (await RiskBandService.PartnerClearAsync(db, w.Actor("partner2", "Partner"), red, "Reviewed")).ErrorCode);
      Assert.Equal(ErrorCodes.ScopeDenied, (await RiskBandService.PartnerClearAsync(db, manager, red, "Reviewed")).ErrorCode);
      Assert.True((await RiskBandService.PartnerClearAsync(db, partner, red, "Revenue cut-off and journal testing are sufficient.")).Succeeded);
      Assert.DoesNotContain((await AuditFieldworkService.EvaluateCompletionAsync(db, partner, w.EngagementId)).Value!.Blockers, x => x.StartsWith($"risk:{red}", StringComparison.Ordinal));

      // No bypass by editing colour: an inconsistent band or significance is rejected by the database.
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO risk_band_assessments (id, firm_id, client_id, engagement_id, risk_id, likelihood_score, magnitude_score, significant, fraud_risk, band, rule_version, rationale, assessed_by_user_id, assessed_at)
        VALUES ({Guid.NewGuid()}, {w.FirmId}, {w.ClientId}, {w.EngagementId}, {red}, 1, 1, false, false, 'GREEN', 'x', 'bypass', {w.Users["senior"].Id}, now())
        """));
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO risk_band_assessments (id, firm_id, client_id, engagement_id, risk_id, likelihood_score, magnitude_score, significant, fraud_risk, band, rule_version, rationale, assessed_by_user_id, assessed_at)
        VALUES ({Guid.NewGuid()}, {w.FirmId}, {w.ClientId}, {w.EngagementId}, {amber}, 3, 3, false, false, 'AMBER', 'x', 'bypass', {w.Users["senior"].Id}, now())
        """));
    }

    // Revoking staffing removes the engagement role and invalidates the session.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var epoch = (await db.Users.AsNoTracking().SingleAsync(x => x.Id == w.Users["senior"].Id)).SessionEpoch;
      Assert.True((await StaffingService.RevokeAsync(db, manager, seniorAssignment)).Succeeded);
      Assert.False(await db.RoleGrants.AnyAsync(x => x.UserId == w.Users["senior"].Id && x.EngagementId == w.EngagementId && x.RevokedAt == null));
      Assert.True((await db.Users.AsNoTracking().SingleAsync(x => x.Id == w.Users["senior"].Id)).SessionEpoch > epoch);
    }
  }

  [Fact]
  public async Task ResourceGrid_ShowsCapacityUnavailabilityAndOverAllocation_AndBudgetsReconcileByPhaseAndRiskArea()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.Actor("partner", "Partner");
    var manager = w.Actor("manager", "Manager");
    var week = new DateOnly(2026, 10, 5); // Monday
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ResourcePlanningService.SaveProfileAsync(db, manager, new(w.Users["senior"].Id, "Audit", "ifrs, sampling, IFRS", 2400, 80m))).Succeeded);
      Assert.True((await ResourcePlanningService.AddAvailabilityAsync(db, manager, new(w.Users["senior"].Id, week.AddDays(1), week.AddDays(2), "LEAVE", 480))).Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, (await ResourcePlanningService.SetAllocationAsync(db, manager, new(w.EngagementId, w.Users["senior"].Id, week, 1800))).ErrorCode);
      Assert.True((await StaffingService.AssignAsync(db, partner, new(w.EngagementId, w.Users["senior"].Id, StaffingLevels.SeniorAuditor))).Succeeded);
      Assert.True((await ResourcePlanningService.SetAllocationAsync(db, manager, new(w.EngagementId, w.Users["senior"].Id, week.AddDays(3), 1800))).Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, (await ResourcePlanningService.SaveProfileAsync(db, w.Actor("associate", "Staff"), new(w.Users["associate"].Id, "Audit", "", 2400, 80m))).ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var grid = (await ResourcePlanningService.GetGridAsync(db, manager, week, 2)).Value!;
      var row = grid.Rows.Single(x => x.UserId == w.Users["senior"].Id);
      Assert.Equal(["IFRS", "SAMPLING"], row.Skills);
      var first = row.Weeks[0];
      Assert.Equal((week, 1440, 960, 1800, true), (first.WeekStart, first.CapacityMinutes, first.UnavailableMinutes, first.PlannedMinutes, first.OverAllocated));
      Assert.Equal(125.0m, first.PlannedUtilizationPercent);
      Assert.Equal((2400, 0, false), (row.Weeks[1].CapacityMinutes, row.Weeks[1].PlannedMinutes, row.Weeks[1].OverAllocated));
      var noProfile = grid.Rows.Single(x => x.UserId == w.Users["associate"].Id);
      Assert.False(noProfile.HasProfile);
      Assert.Null(noProfile.Weeks[0].PlannedUtilizationPercent); // no capacity, so no invented utilization
    }

    // Budget by phase and risk area, reconciled to approved time.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      foreach (var (role, rate) in new[] { ("Senior", 500m), ("Manager", 750m) })
      {
        var card = (await PracticeTimeService.ReviseRateCardAsync(db, manager, new(role, "AUDIT", "QAR", rate))).Value;
        Assert.True((await PracticeTimeService.ApproveRateCardAsync(db, partner, card)).Succeeded);
      }
      var budget = await PracticeTimeService.ReviseBudgetAsync(db, manager, new(w.EngagementId, "QAR",
      [
        new("Senior", "AUDIT", 600, BudgetPhases.Planning, "Revenue"),
        new("Senior", "AUDIT", 1200, BudgetPhases.Fieldwork, "Revenue"),
        new("Senior", "AUDIT", 900, BudgetPhases.Fieldwork, "Inventory"),
        new("Manager", "AUDIT", 300, BudgetPhases.Completion)
      ]));
      Assert.True(budget.Succeeded, budget.Message);
      Assert.Equal("time.invalid", (await PracticeTimeService.ReviseBudgetAsync(db, manager, new(w.EngagementId, "QAR", [new("Senior", "AUDIT", 60, "LUNCH")]))).ErrorCode);
      Assert.True((await PracticeTimeService.ApproveBudgetAsync(db, partner, budget.Value)).Succeeded);
      var task = (await PracticeTimeService.CreateTaskAsync(db, manager, new("Revenue cut-off", w.ClientId, w.EngagementId, w.Users["senior"].Id,
        Phase: BudgetPhases.Fieldwork, RiskArea: "Revenue"))).Value;
      var senior = w.Actor("senior", "Senior");
      var entry = await PracticeTimeService.SaveTimeDraftAsync(db, senior, new(task, new DateOnly(2026, 10, 6), 540, 240, "Senior", "AUDIT", Currency: "QAR"));
      Assert.True(entry.Succeeded, entry.Message);
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, senior, entry.Value)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, manager, entry.Value)).Succeeded);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var breakdown = (await PracticeTimeService.GetBudgetBreakdownAsync(db, manager, w.EngagementId)).Value!;
      var fieldworkRevenue = breakdown.Rows.Single(x => x.Phase == BudgetPhases.Fieldwork && x.RiskArea == "Revenue");
      Assert.Equal((1200, 10_000m, 240, 2_000m), (fieldworkRevenue.ForecastMinutes, fieldworkRevenue.ForecastCost, fieldworkRevenue.ActualMinutes, fieldworkRevenue.ActualCost));
      Assert.Equal(breakdown.ForecastMinutes, breakdown.Rows.Sum(x => x.ForecastMinutes));
      Assert.Equal(breakdown.ForecastCost, breakdown.Rows.Sum(x => x.ForecastCost));
      Assert.Equal(breakdown.ActualMinutes, breakdown.Rows.Sum(x => x.ActualMinutes));
      Assert.Equal(breakdown.ActualCost, breakdown.Rows.Sum(x => x.ActualCost));
      Assert.Equal((3000, 240), (breakdown.ForecastMinutes, breakdown.ActualMinutes));
      var grid = (await ResourcePlanningService.GetGridAsync(db, manager, week, 1)).Value!;
      Assert.Equal(240, grid.Rows.Single(x => x.UserId == w.Users["senior"].Id).Weeks[0].ApprovedActualMinutes);
    }
  }
}
