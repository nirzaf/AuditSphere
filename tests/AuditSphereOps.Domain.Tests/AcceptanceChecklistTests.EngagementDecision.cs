using System.Text.Json;
using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AcceptanceChecklistTests
{
  [Fact]
  public async Task ReviewedScopedDecisionRetainsExactPeriodAndReceiptWithoutActivatingEngagement()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await SeedAsync(pg);
    await AnswerAllAsync(pg, w); await using var db = new AuditSphereDbContext(pg.Options);
    var created = await EngagementLifecycleService.CreateDraftAsync(db, w.Partner,
      new(w.ClientId, "FinancialStatementAudit", "2027-01-01", "2027-12-31", "SYNTHETIC"));
    Assert.True(created.Succeeded, created.Message);
    var fields = new AssessmentCommandFields("DECISION", "1", ServiceRoute: "FinancialStatementAudit",
      Decision: "Accepted", Rationale: "Synthetic exact-period human review", EngagementId: created.Value);
    var request = await ReviewAsync(db, w.Partner, w.ClientId, fields);
    var preview = await AssessmentCommandWorkspace.PreviewAsync(db, w.Partner, w.ClientId, request);
    Assert.Contains("2027-01-01 to 2027-12-31", preview.Value!.Effect);
    await db.Engagements.Where(x => x.Id == created.Value).ExecuteUpdateAsync(x => x.SetProperty(e => e.PeriodEnd, "2027-12-30"));
    var stalePeriod = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId, request);
    Assert.False(stalePeriod.Succeeded);
    Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.StaleRevision, stalePeriod.ErrorCode);
    Assert.Empty(await db.AcceptanceDecisions.ToListAsync());
    await db.Engagements.Where(x => x.Id == created.Value).ExecuteUpdateAsync(x => x.SetProperty(e => e.PeriodEnd, "2027-12-31"));
    request = await ReviewAsync(db, w.Partner, w.ClientId, fields);
    Assert.False((await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId,
      request with { Fields = fields with { EngagementId = Guid.NewGuid() } })).Succeeded);
    var result = await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId, request);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(created.Value, (await db.AcceptanceDecisions.AsNoTracking().SingleAsync()).EngagementId);
    Assert.Equal(created.Value, result.Value!.Preview.Fields.EngagementId);
    Assert.Equal(result.Value.Id, (await AssessmentCommandWorkspace.ExecuteAsync(db, w.Partner, w.ClientId, request)).Value!.Id);
    Assert.Equal(result.Value.Id, (await AssessmentCommandWorkspace.LookupAsync(db, w.Partner, w.ClientId,
      request.RequestId, request.RequestHash)).Value!.Receipt!.Id);
    Assert.Empty(await db.EngagementActivations.ToListAsync());
    Assert.True((await db.Engagements.AsNoTracking().SingleAsync(x => x.Id == created.Value)).ProfessionalWorkBlocked);
    var migrations = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    var scopedMigration = Assert.Single(migrations, x => x.EndsWith("EngagementScopedAssessmentReceipts", StringComparison.Ordinal));
    await db.GetService<IMigrator>().MigrateAsync(migrations[Array.IndexOf(migrations, scopedMigration) - 1]);
    Assert.True(await db.AssessmentCommandReceipts.AnyAsync(x => x.Id == result.Value.Id));
    await db.GetService<IMigrator>().MigrateAsync();
    Assert.Equal(created.Value, (await AssessmentCommandWorkspace.LookupAsync(db, w.Partner, w.ClientId,
      request.RequestId, request.RequestHash)).Value!.Receipt!.Preview.Fields.EngagementId);
  }

  [Fact]
  public async Task ScopedDecisionRefusesForeignMissingWrongServiceAndNonPartnerTargets()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var w = await SeedAsync(pg); var foreign = await SeedAsync(pg);
    await AnswerAllAsync(pg, w); await using var db = new AuditSphereDbContext(pg.Options);
    var own = await EngagementLifecycleService.CreateDraftAsync(db, w.Partner,
      new(w.ClientId, "FinancialStatementAudit", "2027-01-01", "2027-12-31", "SYNTHETIC"));
    var other = await EngagementLifecycleService.CreateDraftAsync(db, foreign.Partner,
      new(foreign.ClientId, "FinancialStatementAudit", "2027-01-01", "2027-12-31", "SYNTHETIC"));
    var fields = new AssessmentCommandFields("DECISION", "1", ServiceRoute: "FinancialStatementAudit",
      Decision: "Accepted", Rationale: "Synthetic human review", EngagementId: own.Value);
    foreach (var f in new[] { fields with { EngagementId = other.Value }, fields with { EngagementId = Guid.NewGuid() },
      fields with { ServiceRoute = "InternalAudit" }, fields with { EngagementId = Guid.Empty } })
      Assert.False((await AssessmentCommandWorkspace.PreviewAsync(db, w.Partner, w.ClientId, new(Guid.NewGuid(), f))).Succeeded);
    Assert.False((await AssessmentCommandWorkspace.PreviewAsync(db, w.Manager, w.ClientId, new(Guid.NewGuid(), fields))).Succeeded);
    var answer = new AssessmentCommandFields("ANSWER", "1", "0", "CE-001", "Yes", EngagementId: own.Value);
    Assert.False((await AssessmentCommandWorkspace.PreviewAsync(db, w.Staff, w.ClientId, new(Guid.NewGuid(), answer))).Succeeded);
    Assert.Empty(await db.AcceptanceDecisions.ToListAsync());
  }

  [Fact]
  public void NullEngagementTargetDoesNotChangeLegacySerializedCommandShape()
  {
    var legacy = new AssessmentCommandFields("DECISION", "1", ServiceRoute: "FinancialStatementAudit",
      Decision: "Declined", Rationale: "Synthetic legacy shape");
    Assert.DoesNotContain("EngagementId", JsonSerializer.Serialize(legacy));
    Assert.Contains("EngagementId", JsonSerializer.Serialize(legacy with { EngagementId = Guid.NewGuid() }));
  }
}
