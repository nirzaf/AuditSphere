using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Domain.Records;
using Microsoft.Extensions.Logging.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  private static async Task<World> SeedHistoricalScopeSpecimenAsync(PgTestSchema pg)
  {
    // Approved synthetic historical specimens only. Never evidence of current completion readiness.
    await using (var boundary = new AuditSphereDbContext(pg.Options))
    {
      var applied = (await boundary.Database.GetAppliedMigrationsAsync()).ToArray();
      Assert.Contains(applied[^1], new[]
      {
        "20261027232538_WorkflowTaskApplicabilityEvidence",
        "20261029134026_CompletionDecisionParentScope",
        "20261101074826_PbcRequestDelegationScope"
      });
    }
    return await SeedSchemaEraRetentionSubstrateAsync(pg);
  }

  private static async Task<World> SeedHistoricalOpinionRetentionAsync(PgTestSchema pg, World sourceScope)
  {
    await using (var boundary = new AuditSphereDbContext(pg.Options))
      Assert.Equal("20261029124914_ArchiveCopyOperationScope", (await boundary.Database.GetAppliedMigrationsAsync()).Last());
    // The program/TB/materiality graph is independent schema-era retention data, not a reconstruction
    // of the current-source SRM digest. Scope and actor identities match only to keep FK probes readable.
    return await SeedSchemaEraRetentionSubstrateAsync(pg, sourceScope);
  }

  private static async Task<World> SeedSchemaEraRetentionSubstrateAsync(PgTestSchema pg, World? sourceScope = null)
  {
    Guid firmId, clientId, engagementId;
    if (sourceScope is null) (firmId, clientId, engagementId) = await pg.SeedScopeAsync();
    else
    {
      (firmId, clientId, engagementId) = (sourceScope.FirmId, sourceScope.ClientId, sourceScope.EngagementId);
      await using var scopeDb = new AuditSphereDbContext(pg.Options);
      scopeDb.PracticeClients.Add(new PracticeClient { Id = clientId, FirmId = firmId, LegalName = "Historical retention scope", CreatedAt = DateTimeOffset.UtcNow });
      scopeDb.Engagements.Add(new Engagement { Id = engagementId, FirmId = firmId, PracticeClientId = clientId, CreatedAt = DateTimeOffset.UtcNow });
      scopeDb.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
      scopeDb.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
      await scopeDb.SaveChangesAsync();
    }

    var u = new[] { "partner", "partner2", "manager", "senior", "associate", "associate2" }.ToDictionary(x => x, x => sourceScope is null ? NewUser(firmId, x) : sourceScope.U[x]);
    u["client"] = sourceScope is null ? NewUser(firmId, "client", "Client") : sourceScope.U["client"];
    var now = DateTimeOffset.UtcNow;
    await using var db = new AuditSphereDbContext(pg.Options);
    await db.Engagements.Where(x => x.Id == engagementId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Active").SetProperty(x => x.ProfessionalWorkBlocked, false)
      .SetProperty(x => x.ServiceRoute, "FinancialStatementAudit").SetProperty(x => x.PeriodEnd, "2026-12-31"));
    db.Users.AddRange(u.Values);
    db.ClientPortalFirstSignIns.Add(PbcSeed.FirstSignIn(u["client"]));
    RoleGrant G(string name, string role, Guid? client = null, Guid? engagement = null) => new()
    { Id = Guid.NewGuid(), FirmId = firmId, UserId = u[name].Id, Role = role, ClientId = client, EngagementId = engagement, GrantedAt = now, GrantedByUserId = u[name].Id };
    db.RoleGrants.AddRange(G("partner", "Partner"), G("partner2", "Partner"), G("client", "ClientUser", clientId, engagementId));
    db.StaffCertifications.AddRange(new[] { "partner", "manager" }.Select(n => new StaffCertification { Id = Guid.NewGuid(), FirmId = firmId, UserId = u[n].Id, Name = "ACCA", RecordedAt = now, RecordedByUserId = u["partner"].Id }));
    var versionId = Guid.NewGuid();
    db.AuditProgramVersions.Add(new AuditProgramVersion { Id = versionId, FirmId = firmId, Version = "2026.1", SourceHash = Hashing.Sha256Hex("p"), Status = AuditProgramStatuses.Published,
      CreatedByUserId = u["partner"].Id, ApprovedByUserId = u["partner"].Id, CreatedAt = now, ApprovedAt = now });
    await db.SaveChangesAsync();
    var administrator = await StaffingCertificationTestEvidence.CreateAdministratorAsync(db, firmId);
    foreach (var certificationId in db.StaffCertifications.Local.Select(x => x.Id).ToArray())
      await StaffingCertificationTestEvidence.ReviewAsync(db, administrator, certificationId);
    var programId = Guid.NewGuid();
    db.EngagementAuditPrograms.Add(new EngagementAuditProgram { Id = programId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, ProgramVersionId = versionId, AdoptedByUserId = u["partner"].Id, AdoptedAt = now });
    var procedure = new AuditProcedure { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, EngagementProgramId = programId, SourceProcedureId = "REV-01",
      SourceSectionTitle = "Revenue", Title = "Revenue cut-off", ApplicabilityStatus = AuditApplicabilityStatuses.Applicable, Status = AuditProcedureStatuses.Submitted, CurrentResultRevision = 1, CreatedAt = now };
    db.AuditProcedures.Add(procedure);
    var result = new AuditProcedureResult { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, AuditProcedureId = procedure.Id, Revision = 1, InputGeneration = 1,
      WorkPerformed = "Selected 25 invoices around year end and agreed them to dispatch notes.", StructuredResultJson = "{}", Conclusion = "Revenue is recorded in the correct period.",
      PreparedByUserId = u["associate"].Id, SubmittedAt = now };
    db.Findings.Add(new Finding { Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, ActorId = u["senior"].Id, FindingType = "Control deficiency",
      ImpactDescription = "Credit notes approved without review", MonetaryAmount = 12_000m, ManagementResponse = "Approval workflow introduced from March.", CreatedAt = now });
    await CompletionTestFixtures.SeedTaxonomyAsync(db, firmId, u["partner"].Id);
    var datasetId = Guid.NewGuid();
    var mappingId = Guid.NewGuid();
    var accounts = new (string Code, string Name, decimal Amount, string Destination, string Section)[]
    {
      ("1000", "Cash", 100m, "CASH", "ASSETS"),
      ("4000", "Revenue", -100m, "REVENUE", "INCOME")
    };
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw", Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Loading, NormalizedDatasetDigest = Hashing.Sha256Hex(datasetId.ToString()),
      ImportedAt = now, ImportedByUserId = u["partner2"].Id
    });
    db.TrialBalanceRows.AddRange(accounts.Select(a => new TrialBalanceRow
    {
      Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = a.Code, AccountName = a.Name, Amount = a.Amount, Currency = "QAR", Entity = "TEST"
    }));
    await db.SaveChangesAsync();
    await db.TrialBalanceDatasets.Where(x => x.Id == datasetId).ExecuteUpdateAsync(s => s.SetProperty(x => x.ImportState, TrialBalanceImportStates.Sealed));
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId, TaxonomyVersion = "TEST-IFRS",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved, CreatedByUserId = u["partner2"].Id,
      ApprovedByUserId = u["partner"].Id, ApprovedAt = now, CreatedAt = now
    });
    db.MappingAllocations.AddRange(accounts.Select(a => new MappingAllocation
    {
      Id = Guid.NewGuid(), FirmId = firmId, ClientId = clientId, EngagementId = engagementId, MappingVersionId = mappingId,
      SourceAccountCode = a.Code, DestinationCode = a.Destination, StatementSection = a.Section, Fraction = 1m, Rationale = "Mapped", CreatedAt = now
    }));
    await db.SaveChangesAsync();
    var calc = await MaterialityEngineService.CalculateAsync(db, new ActorContext(u["partner2"].Id, firmId, u["partner2"].SessionEpoch, ["Partner"]),
      new(engagementId, MaterialityBenchmarks.Revenue, null, 1m, 75m, 5m, "Revenue-driven trading entity"));
    Assert.True(calc.Succeeded, calc.Message);
    var apprv = await AuditPlanningService.ApproveMaterialityAssessmentAsync(db, new ActorContext(u["partner"].Id, firmId, u["partner"].SessionEpoch, ["Partner"]), calc.Value!.AssessmentId);
    Assert.True(apprv.Succeeded, apprv.Message);
    result.InputGeneration = await db.ClientSafetyStates.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.Id == clientId).Select(x => x.InputGeneration).SingleAsync();
    db.AuditProcedureResults.Add(result);
    await db.SaveChangesAsync();
    var w = new World(firmId, clientId, engagementId, u, procedure.Id, result.Id, procedure.SourceProcedureId, result.InputGeneration);
    foreach (var (name, level) in new[] { ("partner", StaffingLevels.EngagementPartner), ("manager", StaffingLevels.AuditManager), ("senior", StaffingLevels.SeniorAuditor),
      ("associate", StaffingLevels.StaffAssociate), ("associate2", StaffingLevels.StaffAssociate) })
    {
      await using var staffDb = new AuditSphereDbContext(pg.Options);
      var assigned = await StaffingService.AssignAsync(staffDb, w.A("partner2", "Partner"), new(engagementId, u[name].Id, level));
      Assert.True(assigned.Succeeded, $"{name}: {assigned.Message}");
    }
    return w;
  }

}
