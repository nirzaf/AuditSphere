using System.Net;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class AdjustmentPlanReviewApiTests
{
  [Fact]
  public async Task CurrentMembershipNeverFallsBackToLostOrChangedSourceReflection()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PLAN-CURRENT");
    var (f, s) = await Seed(pg);
    using var factory = Factory(pg, f.Reviewer); using var c = factory.CreateClient(); await c.GetAsync("/auth/sign-in");
    var first = await Read(c, Url(s.PlanId));
    Assert.Equal(1, first.GetProperty("eligibleCount").GetInt32());
    Assert.Equal(1, first.GetProperty("excludedCount").GetInt32());
    Assert.Equal(2, first.GetProperty("blockedCount").GetInt32());
    Assert.Equal("FY26", first.GetProperty("periodCode").GetString());
    Assert.Equal("Draft", first.GetProperty("status").GetString());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await SourceReconciliationService.ResolveAsync(db, PbcSeed.Actor(f.Reviewer,"AccountingReviewer"), s.SourceId, "AJ-SYN", 1, ReflectionStates.Reflected, "New synthetic source evidence")).Succeeded);
    }
    var changed = await Read(c, Url(s.PlanId));
    var row = changed.GetProperty("journals").EnumerateArray().Single(x => x.GetProperty("journalNumber").GetString() == "AJ-SYN");
    Assert.Equal("NOT_REFLECTED", row.GetProperty("plannedReflectionState").GetString());
    Assert.Equal("REFLECTED", row.GetProperty("reflectionState").GetString());
    Assert.Equal("reflection.changed-since-plan", row.GetProperty("reasonCode").GetString());
    Assert.NotEqual(first.GetProperty("reviewBasis").GetString(), changed.GetProperty("reviewBasis").GetString());
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.JournalSourceReconciliations.Where(r => r.BaseDatasetId == s.SourceId && r.LogicalJournalNumber == "AJ-SYN").ExecuteDeleteAsync();
    var missing = await Read(c, Url(s.PlanId));
    Assert.Equal(0, missing.GetProperty("eligibleCount").GetInt32());
    Assert.Equal("UNKNOWN", missing.GetProperty("journals").EnumerateArray().Single(x => x.GetProperty("journalNumber").GetString() == "AJ-SYN").GetProperty("reflectionState").GetString());
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var duplicateSource = await db.TrialBalanceDatasets.AsNoTracking().SingleAsync(d => d.Id == s.SourceId);
      duplicateSource.Id = Guid.NewGuid(); duplicateSource.ImportState = TrialBalanceImportStates.Loading;
      duplicateSource.RawFileSha256Hex = new('e',64); duplicateSource.NormalizedDatasetDigest = new('f',64); duplicateSource.Sha256Hex = new('f',64);
      db.TrialBalanceDatasets.Add(duplicateSource);
      var duplicate = await db.AdjustmentJournals.AsNoTracking().SingleAsync(j => j.Id == s.EligibleId);
      duplicate.Id = Guid.NewGuid(); duplicate.BaseDatasetId = duplicateSource.Id; db.AdjustmentJournals.Add(duplicate);
      await db.SaveChangesAsync();
    }
    var ambiguous = await Read(c,Url(s.PlanId));
    var ambiguousRow = ambiguous.GetProperty("journals").EnumerateArray().Single(x=>x.GetProperty("journalNumber").GetString()=="AJ-SYN");
    Assert.Equal("journal.ambiguous-identity",ambiguousRow.GetProperty("reasonCode").GetString());
    Assert.Equal(JsonValueKind.Null,ambiguousRow.GetProperty("journalId").ValueKind);
    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal("Draft", (await verify.AdjustmentPlans.SingleAsync(p => p.Id == s.PlanId)).Status);
    Assert.Equal(0, await verify.AdjustmentJournalActions.CountAsync());
  }

  [Fact]
  public async Task RetainedCalculationIsSeparateFromCurrentEligibilityAndEpochLoss()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PLAN-RETAINED"); var (f,s) = await Seed(pg); Guid id; string hash;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AdjustmentPlanService.CreatePlanAsync(db, PbcSeed.Actor(f.Staff,"AccountingPreparer"), s.SourceId, [new("AJ-SYN",1)]);
      Assert.True(created.Succeeded); id = created.Value;
      var result = await AdjustmentPlanService.FinalizeAsync(db, PbcSeed.Actor(f.Staff,"AccountingPreparer"), id);
      Assert.True(result.Succeeded,result.Message); hash = result.Value!.ResultHash;
    }
    using var factory = Factory(pg,f.Staff); using var c = factory.CreateClient(); await c.GetAsync("/auth/sign-in");
    var current = await Read(c,Url(id)); Assert.Equal("Finalized",current.GetProperty("status").GetString());
    Assert.Equal("200.246912",current.GetProperty("retainedDebits").GetString()); Assert.Equal(1,current.GetProperty("retainedAppliedCount").GetInt32());
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await SourceReconciliationService.ResolveAsync(db,PbcSeed.Actor(f.Reviewer,"AccountingReviewer"),s.SourceId,"AJ-SYN",1,ReflectionStates.Reflected,"Replacement source bridge")).Succeeded);
    var stale = await Read(c,Url(id)); Assert.Equal(hash,stale.GetProperty("resultHash").GetString()); Assert.Equal(1,stale.GetProperty("blockedCount").GetInt32());
    Assert.NotEmpty(stale.GetProperty("blockers").EnumerateArray());
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(u=>u.Id==f.Staff.Id).ExecuteUpdateAsync(x=>x.SetProperty(u=>u.SessionEpoch,u=>u.SessionEpoch+1));
    Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync(Url(id))).StatusCode);
  }

  [Fact]
  public async Task PagedQueueAndDetailsExcludeSiblingsMalformedParentsForeignFirmAndClients()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PLAN-SCOPE"); var (f,s) = await Seed(pg);
    var sibling = Guid.NewGuid(); var malformed = Guid.NewGuid(); var siblingPlan = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new Engagement{Id=sibling,FirmId=f.FirmId,PracticeClientId=f.ClientId,Status="Active",CreatedAt=DateTimeOffset.UtcNow});
      db.AdjustmentPlans.Add(new(){Id=siblingPlan,FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=sibling,BaseDatasetId=s.SourceId,CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
      db.AdjustmentPlans.Add(new(){Id=malformed,FirmId=f.FirmId,ClientId=Guid.NewGuid(),EngagementId=f.EngagementId,BaseDatasetId=s.SourceId,CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
      for(var i=0;i<26;i++) db.AdjustmentPlans.Add(new(){Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,BaseDatasetId=s.SourceId,CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow});
      await db.SaveChangesAsync();
    }
    var foreign = await PbcSeed.SeedAsync(pg); AdjustmentPlanReviewSeed.Result other;
    await using(var db=new AuditSphereDbContext(pg.Options)) other=await AdjustmentPlanReviewSeed.SeedAsync(db,foreign);
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
    var first=await Read(c,"/api/ui/accounting/adjustment-plans?page=0");Assert.Equal(25,first.GetProperty("items").GetArrayLength());Assert.True(first.GetProperty("hasMore").GetBoolean());
    var second=await Read(c,"/api/ui/accounting/adjustment-plans?page=1");Assert.Equal(2,second.GetProperty("items").GetArrayLength());Assert.False(second.GetProperty("hasMore").GetBoolean());
    Assert.False(first.TryGetProperty("total",out _));
    foreach(var id in new[]{siblingPlan,malformed,other.PlanId,Guid.NewGuid()})
      Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync(Url(id))).StatusCode);
    using var clientFactory=Factory(pg,f.Client);using var client=clientFactory.CreateClient();await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Url(s.PlanId))).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/api/ui/accounting/adjustment-plans")).StatusCode);
    var holdId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.EngagementHolds.Add(new() { Id = holdId, FirmId = f.FirmId, EngagementId = f.EngagementId,
        HoldKind = "Independence", Reason = "Synthetic scope guard", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
    }
    var held = await Read(c, "/api/ui/accounting/adjustment-plans");
    Assert.Empty(held.GetProperty("items").EnumerateArray());
    Assert.False(held.GetProperty("hasMore").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Url(s.PlanId))).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.EngagementHolds.Where(h => h.Id == holdId).ExecuteUpdateAsync(x => x
        .SetProperty(h => h.Released, true).SetProperty(h => h.ReleasedAt, DateTimeOffset.UtcNow));
      await db.Engagements.Where(e => e.Id == f.EngagementId).ExecuteUpdateAsync(x => x.SetProperty(e => e.ProfessionalWorkBlocked, true));
    }
    Assert.Empty((await Read(c, "/api/ui/accounting/adjustment-plans")).GetProperty("items").EnumerateArray());
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(Url(s.PlanId))).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/ui/accounting/adjustment-plans")).StatusCode);
  }

  [Fact]
  public async Task OversizedMembershipFailsClosedBeforeReturningPartialCounts()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-PLAN-BOUNDS");var(f,s)=await Seed(pg);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      for(var i=0;i<1001;i++) db.AdjustmentPlanLines.Add(new(){Id=Guid.NewGuid(),PlanId=s.PlanId,LogicalJournalNumber="BOUND-"+i,JournalRevision=1});
      await db.SaveChangesAsync();
    }
    using var factory=Factory(pg,f.Staff);using var c=factory.CreateClient();await c.GetAsync("/auth/sign-in");
    using var result=await c.GetAsync(Url(s.PlanId));Assert.Equal(HttpStatusCode.BadRequest,result.StatusCode);
    var body=await result.Content.ReadAsStringAsync();Assert.DoesNotContain("eligibleCount",body);
  }
  private static async Task<(PbcSeed.Fixture,AdjustmentPlanReviewSeed.Result)> Seed(OwnedPostgresDatabase pg){var f=await PbcSeed.SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);return(f,await AdjustmentPlanReviewSeed.SeedAsync(db,f));}
  private static string Url(Guid id)=>"/api/ui/accounting/adjustment-plans/"+id;
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<JsonElement> Read(HttpClient c,string url){using var r=await c.GetAsync(url);Assert.Equal(HttpStatusCode.OK,r.StatusCode);return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();}
}
