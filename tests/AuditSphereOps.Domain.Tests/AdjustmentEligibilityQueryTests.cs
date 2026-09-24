using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 22 read-side eligibility report: the plan membership is classified
/// with reason codes and a contribution-set digest, mirroring the finalize rules.</summary>
public sealed class AdjustmentEligibilityQueryTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task EligibilityReport_ClassifiesBlockedExcludedAndEligibleJournals()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var first = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-ELIG-1", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, first)).Succeeded);
      var second = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-ELIG-2", [("1000", 5m, 0m), ("4000", 0m, 5m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, second)).Succeeded);
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-ELIG-1", 1, ReflectionStates.NotReflected, "Not yet in the client ledger.")).Succeeded);
      // AJ-ELIG-2 has no reconciliation record yet: its reflection is Unknown.

      var planId = (await AdjustmentPlanService.CreatePlanAsync(db, preparer, fixture.DatasetId,
        [new PlanLineInput("AJ-ELIG-1", 1), new PlanLineInput("AJ-ELIG-2", 1)])).Value;

      var denied = await AdjustmentEligibilityQuery.GetEligibilityAsync(db,
        Actor(fixture.Outsider, "AccountingPreparer"), planId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      var report = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, preparer, planId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal("Draft", report.Value!.PlanStatus);
      Assert.Equal(1, report.Value.EligibleCount);
      Assert.Equal(0, report.Value.ExcludedCount);
      Assert.Equal(1, report.Value.BlockedCount);
      var eligible = report.Value.Journals.Single(x => x.JournalNumber == "AJ-ELIG-1");
      Assert.Equal(AdjustmentEligibilityQuery.Eligible, eligible.Classification);
      Assert.Equal(ReflectionStates.NotReflected, eligible.ReflectionState);
      var blocked = report.Value.Journals.Single(x => x.JournalNumber == "AJ-ELIG-2");
      Assert.Equal(AdjustmentEligibilityQuery.Blocked, blocked.Classification);
      Assert.Equal("reflection.unresolved", blocked.ReasonCode);
      var digestBefore = report.Value.MembershipDigest;
      Assert.Matches("^[0-9a-f]{64}$", digestBefore);

      // Resolving the second journal as already reflected reclassifies it as
      // excluded with a different contribution-set digest.
      Assert.True((await SourceReconciliationService.ResolveAsync(db, reviewer, fixture.DatasetId,
        "AJ-ELIG-2", 1, ReflectionStates.Reflected, "Client ledger already includes this entry.")).Succeeded);
      var after = await AdjustmentEligibilityQuery.GetEligibilityAsync(db, preparer, planId);
      Assert.True(after.Succeeded, after.Message);
      var excluded = after.Value!.Journals.Single(x => x.JournalNumber == "AJ-ELIG-2");
      Assert.Equal(AdjustmentEligibilityQuery.Excluded, excluded.Classification);
      Assert.Equal("reflection.already-in-source", excluded.ReasonCode);
      Assert.Equal(1, after.Value.EligibleCount);
      Assert.Equal(1, after.Value.ExcludedCount);
      Assert.Equal(0, after.Value.BlockedCount);
      Assert.NotEqual(digestBefore, after.Value.MembershipDigest);
    }
  }

  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId,
    AppUser Preparer, AppUser Reviewer, AppUser Outsider);

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var outsider = User(firmId, "outsider");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new AuditSphereOps.Domain.Practice.PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "ELIGIBILITY REPORT TEST", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, outsider);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"));
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
    return new Fixture(firmId, clientId, engagementId, datasetId, preparer, reviewer, outsider);
  }
}
