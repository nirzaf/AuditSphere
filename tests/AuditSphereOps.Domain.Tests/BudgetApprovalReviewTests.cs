using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class BudgetApprovalReviewTests
{
  [Fact]
  public async Task ConcurrentExactApprovalHasOneImmutableActorOwnedReceiptAndCurrentAccessRecovery()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var budgetId = await BudgetApprovalReviewSeed.PopulateAsync(db, f);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var r = new BudgetApprovalRequest(Guid.NewGuid(), new(budgetId, "1"));
    var p = (await BudgetApprovalWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    Assert.Equal("801.000000", p.ForecastCost);
    r = r with { Reviewed = true, RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis };
    async Task<string> Save()
    {
      await using var c = new AuditSphereDbContext(pg.Options);
      var result = await BudgetApprovalWorkspace.ExecuteAsync(c, actor, f.EngagementId, r);
      Assert.True(result.Succeeded, result.Message); return JsonSerializer.Serialize(result.Value);
    }
    var results = await Task.WhenAll(Save(), Save()); Assert.Equal(results[0], results[1]);
    var receipt = await db.BudgetApprovals.AsNoTracking().SingleAsync();
    var budget = await db.EngagementBudgets.AsNoTracking().SingleAsync();
    Assert.Equal(PracticeTimeStates.BudgetApproved, budget.Status); Assert.Equal(f.Admin.Id, budget.ApprovedByUserId);
    Assert.Equal(budget.ApprovedAt, receipt.CreatedAt);
    Assert.True((await BudgetApprovalWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    Assert.False((await BudgetApprovalWorkspace.LookupAsync(db, actor, f.EngagementId, Guid.NewGuid(), r.RequestHash)).Value!.Found);
    Assert.False((await BudgetApprovalWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, new string('f', 64))).Succeeded);
    await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    Assert.True((await BudgetApprovalWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).Succeeded);
    Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE budget_approvals SET preview_json='{{}}' WHERE id={receipt.Id}"))).SqlState);
    Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM budget_approvals WHERE id={receipt.Id}"))).SqlState);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Reviewer, "Manager", f.ClientId)); await db.SaveChangesAsync();
    var other = PbcSeed.Actor(f.Reviewer, "Manager");
    Assert.False((await BudgetApprovalWorkspace.LookupAsync(db, other, f.EngagementId, r.RequestId, r.RequestHash)).Value!.Found);
    Assert.False((await BudgetApprovalWorkspace.ExecuteAsync(db, other, f.EngagementId, r)).Succeeded);
    await db.Users.Where(x => x.Id == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.Disabled, true));
    Assert.False((await BudgetApprovalWorkspace.LookupAsync(db, actor, f.EngagementId, r.RequestId, r.RequestHash)).Succeeded);
  }

  [Fact]
  public async Task SelfApprovalStaleContextWrongScopeAndUnreviewedRequestsNeverApprove()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var budgetId = await BudgetApprovalReviewSeed.PopulateAsync(db, f);
    var actor = PbcSeed.Actor(f.Admin, "Administrator"); var r = new BudgetApprovalRequest(Guid.NewGuid(), new(budgetId, "1"));
    Assert.False((await BudgetApprovalWorkspace.PreviewAsync(db, PbcSeed.Actor(f.Staff, "Manager"), f.EngagementId, r)).Succeeded);
    Assert.False((await BudgetApprovalWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).Succeeded);
    var p = (await BudgetApprovalWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    r = r with { Reviewed = true, RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis };
    Assert.Equal((await BudgetApprovalWorkspace.PreviewAsync(db, actor, Guid.NewGuid(), r)).ErrorCode,
      (await BudgetApprovalWorkspace.PreviewAsync(db, actor, foreign.EngagementId, r)).ErrorCode);
    Assert.False((await BudgetApprovalWorkspace.PreviewAsync(db, actor, f.EngagementId, r with { Fields = r.Fields with { ExpectedVersion = "2" } })).Succeeded);
    await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, x => x.InputGeneration + 1));
    Assert.Equal("revision.stale", (await BudgetApprovalWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).ErrorCode);
    Assert.Empty(await db.BudgetApprovals.ToListAsync()); Assert.Null((await db.EngagementBudgets.SingleAsync()).ApprovedAt);
    await db.RoleGrants.Where(x => x.UserId == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    Assert.False((await BudgetApprovalWorkspace.ExecuteAsync(db, actor, f.EngagementId, r)).Succeeded);
  }

  [Fact]
  public async Task DeferredGuardRejectsReceiptWithoutExactIndependentApproval()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var budgetId = await BudgetApprovalReviewSeed.PopulateAsync(db, f);
    var actor = PbcSeed.Actor(f.Admin, "Administrator"); var r = new BudgetApprovalRequest(Guid.NewGuid(), new(budgetId, "1"));
    var p = (await BudgetApprovalWorkspace.PreviewAsync(db, actor, f.EngagementId, r)).Value!;
    db.BudgetApprovals.Add(new() { Id = Guid.NewGuid(), FirmId = f.FirmId, EngagementId = f.EngagementId,
      BudgetId = budgetId, ActorId = f.Admin.Id, ActorEpoch = actor.SessionEpoch, RequestId = r.RequestId,
      RequestHash = p.RequestHash, ReviewBasis = p.ReviewBasis, PreviewJson = JsonSerializer.Serialize(p), CreatedAt = DateTimeOffset.UtcNow });
    var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    Assert.Equal("23514", Assert.IsType<PostgresException>(error.InnerException).SqlState);
  }
}
