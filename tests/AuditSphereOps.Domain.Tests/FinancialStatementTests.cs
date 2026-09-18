using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class FinancialStatementTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId,
    AppUser Preparer, AppUser Reviewer);

  [Fact]
  public async Task MappingPlanAndPackage_AreScopedDeterministicAndReviewGated()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var incomplete = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping")
        ]));
      Assert.False(incomplete.Succeeded);
      Assert.Equal("mapping.incomplete", incomplete.ErrorCode);
    }

    Guid mappingId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await FinancialStatementService.CreateMappingVersionAsync(db, preparer,
        new CreateMappingVersionRequest(fixture.DatasetId, "tax-v1", "2026-01-01", "2026-12-31", [
          new("1000", "CASH", "ASSETS", 1m, "Cash mapping"),
          new("4000", "REVENUE", "INCOME", 1m, "Revenue mapping")
        ]));
      Assert.True(created.Succeeded);
      mappingId = created.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await FinancialStatementService.ApproveMappingAsync(db, reviewer, mappingId, 1)).Succeeded);

    Guid planId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-001", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-001", 1, ReflectionStates.NotApplicable, string.Empty)).Succeeded);
      planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-001", 1)])).Value;
      Assert.True((await AdjustmentPlanService.FinalizeAsync(db, preparer, planId)).Succeeded);
    }

    var request = new BuildFinancialPackageRequest(planId, mappingId, "IFRS", "2026-01-01", "2026-12-31", "template-v1");
    FinancialPackageBuildResult first;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var built = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(built.Succeeded);
      first = built.Value!;
      Assert.Equal(AccountingPackageStates.PackageReviewRequired, first.Status);
      Assert.Equal(100m, first.StatementTotals["ASSETS"]);
      Assert.Equal(-100m, first.StatementTotals["INCOME"]);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var repeat = await FinancialStatementService.BuildFinancialPackageAsync(db, preparer, request);
      Assert.True(repeat.Succeeded);
      Assert.Equal(first.PackageId, repeat.Value!.PackageId);
      Assert.Equal(first.CalculationHash, repeat.Value.CalculationHash);
      Assert.Equal(2, await db.AdjustedTrialBalanceRows.CountAsync());
      var checks = await db.FinancialPackageValidations.AsNoTracking()
        .Where(x => x.FinancialPackageId == first.PackageId).ToListAsync();
      Assert.Contains(checks, x => x.Code == "SUPPLEMENTARY_INFORMATION" && !x.Passed);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var lineId = await db.FinancialPackageLines.Select(x => x.Id).FirstAsync();
      await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE financial_package_lines SET amount = amount + 1 WHERE id = {lineId}"));
    }
  }

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var preparer = User(firmId);
    var reviewer = User(firmId);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "FINANCIAL STATEMENT TEST",
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"));
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, preparer, reviewer);
  }

  private static AppUser User(Guid firmId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId,
    Subject = "sub-" + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = $"{Guid.NewGuid():N}@example.test",
    DisplayName = "Synthetic", CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
