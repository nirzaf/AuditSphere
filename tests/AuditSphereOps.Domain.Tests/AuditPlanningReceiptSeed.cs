using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Shared synthetic source/actor fixture; no professional or live approval is established.</summary>
internal static class AuditPlanningReceiptSeed
{
  internal sealed record World(PbcSeed.Fixture Fixture, Guid ProcedureId, AppUser OtherPartner);
  internal static async Task<World> SeedAsync(ITestPostgresDatabase pg)
  {
    var f = await PbcSeed.SeedAsync(pg); var other = PbcSeed.User(f.FirmId, "Staff");
    var dataset = Guid.NewGuid(); var mapping = Guid.NewGuid(); var procedure = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(other);
    var managerGrant = PbcSeed.Grant(f.FirmId, f.Reviewer, "Manager", f.ClientId, f.EngagementId);
    db.RoleGrants.AddRange(managerGrant, PbcSeed.Grant(f.FirmId, f.Admin, "Partner"), PbcSeed.Grant(f.FirmId, other, "Partner"));
    db.EngagementStaffAssignments.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      UserId = f.Reviewer.Id, StaffingLevel = StaffingLevels.AuditManager, RoleGrantId = managerGrant.Id, AssignedByUserId = f.Admin.Id, AssignedAt = DateTimeOffset.UtcNow });
    db.TrialBalanceDatasets.Add(new() { Id = dataset, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      SourceKind = "Raw", Currency = "QAR", Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading,
      NormalizedDatasetDigest = Hashing.Sha256Hex(dataset.ToString()), ImportedByUserId = f.Staff.Id, ImportedAt = DateTimeOffset.UtcNow });
    db.TrialBalanceRows.AddRange(new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = dataset, AccountCode = "1000", AccountName = "Cash", Amount = 100000m, Currency = "QAR", Entity = "SYNTHETIC" },
      new TrialBalanceRow { Id = Guid.NewGuid(), DatasetId = dataset, AccountCode = "4000", AccountName = "Revenue", Amount = -100000m, Currency = "QAR", Entity = "SYNTHETIC" });
    db.AuditProcedures.Add(new() { Id = procedure, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      SourceProcedureId = "SYN-PLANNING", Title = "Synthetic procedure", Status = AuditProcedureStatuses.Planned, CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == dataset).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    db.MappingVersions.Add(new() { Id = mapping, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, DatasetId = dataset,
      TaxonomyVersion = "SYNTHETIC", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
      CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow });
    db.MappingAllocations.AddRange(new MappingAllocation { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      MappingVersionId = mapping, SourceAccountCode = "1000", DestinationCode = "CASH", StatementSection = "ASSETS", Fraction = 1m, Rationale = "SYNTHETIC", CreatedAt = DateTimeOffset.UtcNow },
      new MappingAllocation { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      MappingVersionId = mapping, SourceAccountCode = "4000", DestinationCode = "REVENUE", StatementSection = "INCOME", Fraction = 1m, Rationale = "SYNTHETIC", CreatedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync(); return new(f, procedure, other);
  }
  internal static async Task<(ActorContext Actor, AuditPlanningCommandFields Fields)> FieldsAsync(ITestPostgresDatabase pg, World w, string action)
  {
    var f = w.Fixture; var staff = PbcSeed.Actor(f.Staff, "Staff"); var manager = PbcSeed.Actor(f.Reviewer, "Manager"); var partner = PbcSeed.Actor(f.Admin, "Partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    if (action == AuditPlanningActions.CalculateMateriality) return (staff, new(action, Calculate: new("REVENUE", null, "1", "75", "5", "SYNTHETIC benchmark")));
    if (action is AuditPlanningActions.RoundMateriality or AuditPlanningActions.ApproveMateriality)
    {
      var calc = await MaterialityEngineService.CalculateAsync(db, staff, new(f.EngagementId, "REVENUE", null, 1m, 75m, 5m, "SYNTHETIC benchmark"));
      Assert.True(calc.Succeeded, calc.Message);
      return action == AuditPlanningActions.RoundMateriality
        ? (manager, new(action, Round: new(calc.Value!.AssessmentId, "1000", "750", "50", "SYNTHETIC rounding")))
        : (partner, new(action, Approve: new(calc.Value!.AssessmentId)));
    }
    if (action == AuditPlanningActions.CreateRisk) return (staff, new(action, CreateRisk: new("CASH", "Existence", "SYNTHETIC risk", "SYNTHETIC driver", "SIGNIFICANT", "SYNTHETIC response")));
    var created = await AuditPlanningService.CreateAuditRiskAsync(db, staff, new(f.EngagementId, "CASH", "Existence", "SYNTHETIC risk", "SYNTHETIC driver", "SIGNIFICANT", null, "SYNTHETIC response"));
    Assert.True(created.Succeeded, created.Message); var riskId = created.Value!.RiskId;
    if (action == AuditPlanningActions.AssessRisk) return (staff, new(action, AssessRisk: new(riskId, 3, 3, false, false, "SYNTHETIC assessment")));
    if (action == AuditPlanningActions.LinkProcedureRisk) return (staff, new(action, LinkProcedureRisk: new(w.ProcedureId, riskId, null)));
    var assessed = await RiskBandService.AssessAsync(db, staff, new(riskId, 3, 3, false, "SYNTHETIC assessment")); Assert.True(assessed.Succeeded, assessed.Message);
    return action == AuditPlanningActions.AssignRiskOwner
      ? (manager, new(action, AssignRiskOwner: new(riskId, assessed.Value, f.Reviewer.Id)))
      : (partner, new(action, ClearRisk: new(riskId, assessed.Value, "SYNTHETIC independent review")));
  }
  internal static async Task<AuditPlanningCommandRequest> ReviewAsync(AuditSphereDbContext db, ActorContext a, Guid engagementId, AuditPlanningCommandFields fields)
  {
    var state = await AuditPlanningCommandWorkspace.StateAsync(db, a, engagementId, fields.Action, fields.Round?.AssessmentId ?? fields.Approve?.AssessmentId,
      fields.AssessRisk?.RiskId ?? fields.AssignRiskOwner?.RiskId ?? fields.ClearRisk?.RiskId ?? fields.LinkProcedureRisk?.RiskId, fields.LinkProcedureRisk?.ProcedureId);
    Assert.True(state.Succeeded, state.Message);
    using var basis = JsonDocument.Parse(state.Value!.StateJson);
    Assert.Equal(state.Value.ClientId, basis.RootElement.GetProperty("ClientId").GetGuid());
    Assert.Equal(Hashing.Sha256Hex(state.Value.StateJson), state.Value.ReviewBasis);
    var request = new AuditPlanningCommandRequest(Guid.NewGuid(), state.Value!.ReviewBasis, fields);
    var preview = await AuditPlanningCommandWorkspace.PreviewAsync(db, a, engagementId, request); Assert.True(preview.Succeeded, preview.Message);
    Assert.True(preview.Value!.State.CanProceed, string.Join(';', preview.Value.State.Blockers));
    return request with { Fields = preview.Value.CanonicalInput, ExpectedRequestHash = preview.Value.RequestHash, Reviewed = true };
  }
}
