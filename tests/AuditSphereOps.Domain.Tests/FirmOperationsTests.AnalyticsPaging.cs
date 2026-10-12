using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class FirmOperationsTests
{
  [Fact]
  public async Task Analytics_PagesFirmEconomicsWithoutChangingPeriodSummaryOrLeakingAnotherFirm()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var world = await SeedAsync(pg);
    var foreign = await SeedAsync(pg);
    var today = DateOnly.FromDateTime(DateTime.UtcNow);
    var second = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new Engagement
      {
        Id = second, FirmId = world.FirmId, PracticeClientId = world.ClientId,
        ServiceRoute = "FinancialStatementAudit", ServiceProfileId = "synthetic-paging",
        PeriodStart = $"{today.Year}-01-01", PeriodEnd = $"{today.Year}-12-31",
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    var startMinute = 540;
    async Task AddTime(World owner, Guid engagementId)
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      var task = await PracticeTimeService.CreateTaskAsync(db, owner.A("manager", "Manager"),
        new CreateTaskRequest("Synthetic analytics task", owner.ClientId, engagementId, owner.U["staff"].Id, DueDate: today));
      Assert.True(task.Succeeded, task.Message);
      var draft = await PracticeTimeService.SaveTimeDraftAsync(db, owner.A("staff", "Staff"),
        new SaveTimeDraftRequest(task.Value, today, startMinute, 60, "Staff", "AUDIT", PracticeTimeStates.NonBillable, "Synthetic preparation"));
      Assert.True(draft.Succeeded, draft.Message);
      startMinute += 60;
      Assert.True((await PracticeTimeService.SubmitTimeAsync(db, owner.A("staff", "Staff"), draft.Value)).Succeeded);
      Assert.True((await PracticeTimeService.ApproveTimeAsync(db, owner.A("manager", "Manager"), draft.Value)).Succeeded);
    }
    await AddTime(world, world.EngagementId);
    await AddTime(world, second);
    await AddTime(foreign, foreign.EngagementId);
    await using var query = new AuditSphereDbContext(pg.Options);
    var first = await PracticeAnalyticsQuery.GetAsync(query, world.A("partner", "Partner"), today, today,
      page: new PracticeAnalyticsPageRequest(PageSize: 1));
    Assert.True(first.Succeeded, first.Message);
    Assert.True(first.Value!.HasMoreEngagements);
    Assert.Equal(1, first.Value.EngagementPageSize);
    var firstRow = Assert.Single(first.Value.Engagements);
    Assert.Equal(firstRow.EngagementId, first.Value.NextEngagementId);
    var next = await PracticeAnalyticsQuery.GetAsync(query, world.A("partner", "Partner"), today, today,
      page: new PracticeAnalyticsPageRequest(first.Value.NextEngagementId, 1));
    Assert.True(next.Succeeded, next.Message);
    Assert.False(next.Value!.HasMoreEngagements);
    Assert.Null(next.Value.NextEngagementId);
    var secondRow = Assert.Single(next.Value.Engagements);
    Assert.NotEqual(firstRow.EngagementId, secondRow.EngagementId);
    Assert.Equal(new[] { world.EngagementId, second }.Order(), new[] { firstRow.EngagementId, secondRow.EngagementId }.Order());
    Assert.Equal(2, first.Value.Milestones.Due);
    Assert.Equal(first.Value.Milestones, next.Value.Milestones);
    Assert.Equal(first.Value.Workload, next.Value.Workload);
    Assert.Equal(first.Value.Departments, next.Value.Departments);
    var invalid = await PracticeAnalyticsQuery.GetAsync(query, world.A("partner", "Partner"), today, today,
      page: new PracticeAnalyticsPageRequest(PageSize: 101));
    Assert.False(invalid.Succeeded);
    Assert.Equal("analytics.invalid-page", invalid.ErrorCode);
  }
}
