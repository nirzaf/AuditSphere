using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class SteChargeOutRateBaselineTests
{
  [Theory]
  [InlineData("Senior", "Audit Supervisor")]
  [InlineData("Supervisor", "Audit Supervisor")]
  [InlineData("Junior", "Audit Associate")]
  [InlineData("Associate", "Audit Associate")]
  [InlineData("Audit Manager", "Audit Manager")]
  public void ExplicitAliasesMapToTheirBaselineRole(string name, string canonical)
  {
    Assert.Equal(canonical, SteChargeOutRateBaseline.CanonicalRole(name));
  }

  [Theory]
  [InlineData("Intern")]
  [InlineData("Partner")] // not a baseline name; only "Engagement Partner" carries the rate
  [InlineData("")]
  public void UnknownRolesInheritNoRate(string name)
  {
    Assert.Null(SteChargeOutRateBaseline.CanonicalRole(name));
  }

  [Fact]
  public void BaselineMatchesTheSpecifiedQarSchedule()
  {
    Assert.Equal(
      [("Engagement Partner", 1000m), ("Audit Manager", 750m), ("Audit Supervisor", 500m), ("Audit Associate", 200m)],
      SteChargeOutRateBaseline.Rates);
  }

  [Fact]
  public async Task BaselineInitialisesDraftsApprovedBySeparateApprover_AndResolvesAliasesToExactRates()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var (firmId, _, _) = await pg.SeedScopeAsync();
    var drafter = PbcSeed.User(firmId, "Staff");
    var approver = PbcSeed.User(firmId, "Staff");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.AddRange(drafter, approver);
      db.RoleGrants.AddRange(PbcSeed.Grant(firmId, drafter, "Manager"), PbcSeed.Grant(firmId, approver, "Partner"));
      await db.SaveChangesAsync();
    }
    var drafterActor = PbcSeed.Actor(drafter, "Manager");
    var approverActor = PbcSeed.Actor(approver, "Partner");

    await using var db2 = new AuditSphereDbContext(pg.Options);
    var drafts = await SteChargeOutRateBaseline.InitializeDraftsAsync(db2, drafterActor);
    Assert.True(drafts.Succeeded, drafts.Message);
    Assert.Equal(4, drafts.Value!.Count);
    // Repeating initialisation while drafts await approval creates nothing new.
    Assert.Empty((await SteChargeOutRateBaseline.InitializeDraftsAsync(db2, drafterActor)).Value!);

    // The preparer cannot approve their own versions; a separate approver can.
    foreach (var id in drafts.Value!)
      Assert.Equal(ErrorCodes.ProtectedState, (await PracticeTimeService.ApproveRateCardAsync(db2, drafterActor, id)).ErrorCode);
    foreach (var id in drafts.Value!)
      Assert.True((await PracticeTimeService.ApproveRateCardAsync(db2, approverActor, id)).Succeeded);
    Assert.Empty((await SteChargeOutRateBaseline.InitializeDraftsAsync(db2, drafterActor)).Value!);

    // The approved approver is recorded on each card.
    var approved = await db2.RateCardVersions.AsNoTracking().Where(x => x.FirmId == firmId && x.Status == PracticeTimeStates.RateApproved).ToListAsync();
    Assert.Equal(4, approved.Count);
    Assert.All(approved, card => Assert.Equal(approver.Id, card.ApprovedByUserId));

    // Aliases resolve to the baseline card's rate; an unknown role has no rate and fails closed.
    var senior = await PracticeTimeService.ResolveRateAsync(db2, firmId, "Senior", SteChargeOutRateBaseline.Activity, "QAR", PracticeTimeStates.Billable, default);
    Assert.Equal(500m, senior.Value!.RatePerHour);
    var junior = await PracticeTimeService.ResolveRateAsync(db2, firmId, "Junior", SteChargeOutRateBaseline.Activity, "QAR", PracticeTimeStates.Billable, default);
    Assert.Equal(200m, junior.Value!.RatePerHour);
    var intern = await PracticeTimeService.ResolveRateAsync(db2, firmId, "Intern", SteChargeOutRateBaseline.Activity, "QAR", PracticeTimeStates.Billable, default);
    Assert.Equal("time.rate-missing", intern.ErrorCode);
  }
}
