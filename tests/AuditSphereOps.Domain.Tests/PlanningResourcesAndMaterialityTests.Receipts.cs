using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;
public sealed partial class PlanningResourcesAndMaterialityTests
{
  private static ResourcePlanningFields PlanningFields(World w,string kind)=>kind switch {
    "PROFILE"=>new(kind,w.Users["associate"].Id,Department:"Audit",Skills:"IFRS,ifrs",WeeklyCapacityMinutes:1200,TargetUtilizationPercent:"75.00"),
    "CERTIFICATION"=>new(kind,w.Users["associate"].Id,Name:"Synthetic certification",ExpiresOn:new DateOnly(2028,12,31)),
    "AVAILABILITY"=>new(kind,w.Users["associate"].Id,StartDate:new DateOnly(2026,10,5),EndDate:new DateOnly(2026,10,6),AvailabilityKind:"LEAVE",MinutesPerDay:480),
    _=>new(kind,w.Users["associate"].Id,EngagementId:w.EngagementId,WeekStart:new DateOnly(2026,10,7),PlannedMinutes:1800,
      Phase:BudgetPhases.Fieldwork,RiskArea:"Revenue") };
  private static Task<ResourcePlanningCommandRequest> PlanningReview(AuditSphereDbContext db,World w,ResourcePlanningFields fields)
    => PlanningReview(db,w,fields,w.Actor("partner","Partner"));
  private static async Task<ResourcePlanningCommandRequest> PlanningReview(AuditSphereDbContext db,World w,ResourcePlanningFields fields,ActorContext reviewer)
  {
    var result=await ResourcePlanningCommandWorkspace.PreviewAsync(db,reviewer,new(Guid.NewGuid(),fields));
    Assert.True(result.Succeeded,result.Message);
    var p=result.Value!;return new(p.RequestId,p.Fields,p.RequestHash,p.ReviewBasis,true);
  }
  [Theory]
  [InlineData("PROFILE")][InlineData("CERTIFICATION")][InlineData("AVAILABILITY")][InlineData("ALLOCATION")]
  public async Task PlanningReviewedCommandsPublishOneReceiptAndMatchingReplay(string kind)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);
    Assert.True((await StaffingService.AssignAsync(db,w.Actor("partner","Partner"),new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
    var r=await PlanningReview(db,w,PlanningFields(w,kind));
    var first=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r);Assert.True(first.Succeeded,first.Message);
    var replay=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r);Assert.True(replay.Succeeded,replay.Message);Assert.Equal(first.Value!.Id,replay.Value!.Id);
    var lookup=await ResourcePlanningCommandWorkspace.LookupAsync(db,w.Actor("partner","Partner"),r.RequestId,r.RequestHash);Assert.True(lookup.Value!.Found);Assert.Equal(first.Value.Id,lookup.Value.Receipt!.Id);
    Assert.Single(await db.ResourcePlanningReceipts.ToListAsync());
    var wrongActor=await ResourcePlanningCommandWorkspace.LookupAsync(db,w.Actor("partner2","Partner"),r.RequestId,r.RequestHash);Assert.False(wrongActor.Value!.Found);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r with {Reviewed=false})).Succeeded);
    await using var mutate=new AuditSphereDbContext(pg.Options);await Assert.ThrowsAsync<PostgresException>(async()=>await mutate.ResourcePlanningReceipts.ExecuteDeleteAsync());
  }
  [Theory]
  [InlineData("PROFILE")][InlineData("CERTIFICATION")][InlineData("AVAILABILITY")][InlineData("ALLOCATION")]
  public async Task PlanningGlobalCommandWorkspaceRequiresFirmWideManagerWhileStandaloneAllocationKeepsExactEngagementScope(string kind)
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var manager=w.Actor("manager","Manager");
    Assert.True((await StaffingService.AssignAsync(db,w.Actor("partner","Partner"),new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);

    // The firm-wide Manager may use the global preview and the corresponding standalone writer.
    switch(kind) {
      case "PROFILE":
        Assert.True((await ResourcePlanningService.SaveProfileAsync(db,manager,new(w.Users["associate"].Id,"Audit","IFRS",1800,75m))).Succeeded);
        break;
      case "CERTIFICATION":
        Assert.True((await ResourcePlanningService.AddCertificationAsync(db,manager,new(w.Users["associate"].Id,"Initial certification",null,null))).Succeeded);
        break;
      case "AVAILABILITY":
        Assert.True((await ResourcePlanningService.AddAvailabilityAsync(db,manager,new(w.Users["associate"].Id,new DateOnly(2026,10,5),new DateOnly(2026,10,6),"LEAVE",480))).Succeeded);
        break;
      default:
        Assert.True((await ResourcePlanningService.SetAllocationAsync(db,manager,new(w.EngagementId,w.Users["associate"].Id,new DateOnly(2026,10,4),600,
          BudgetPhases.Fieldwork,"Revenue"))).Succeeded);
        break;
    }
    var fields=PlanningFields(w,kind);var reviewed=await PlanningReview(db,w,fields,manager);

    // Keep the user's session epoch fixed while replacing the global Manager with an exact engagement Manager
    // and an unrelated firm-wide Staff grant. Neither grant may combine to authorize a global console action.
    var globalManager=await db.RoleGrants.Where(x=>x.FirmId==w.FirmId&&x.UserId==w.Users["manager"].Id&&x.Role=="Manager"&&
      x.ClientId==null&&x.EngagementId==null&&x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    Assert.Equal(1,globalManager);
    db.RoleGrants.AddRange(Grant(w.FirmId,w.Users["manager"],"Manager",w.ClientId,w.EngagementId),Grant(w.FirmId,w.Users["manager"],"Staff"));
    await db.SaveChangesAsync();
    Assert.Equal(w.Users["manager"].SessionEpoch,await db.Users.Where(x=>x.Id==w.Users["manager"].Id).Select(x=>x.SessionEpoch).SingleAsync());

    var scopedManager=w.Actor("manager","Manager","Staff");
    var deniedPreview=await ResourcePlanningCommandWorkspace.PreviewAsync(db,scopedManager,new(Guid.NewGuid(),fields));
    Assert.False(deniedPreview.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,deniedPreview.ErrorCode);Assert.Null(deniedPreview.Value);
    var deniedExecute=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,scopedManager,reviewed);
    Assert.False(deniedExecute.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,deniedExecute.ErrorCode);Assert.Null(deniedExecute.Value);
    var deniedLookup=await ResourcePlanningCommandWorkspace.LookupAsync(db,scopedManager,reviewed.RequestId,reviewed.RequestHash);
    Assert.False(deniedLookup.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,deniedLookup.ErrorCode);Assert.Null(deniedLookup.Value);
    Assert.Empty(await db.ResourcePlanningReceipts.ToListAsync());

    switch(kind) {
      case "PROFILE":
        var profile=await ResourcePlanningService.SaveProfileAsync(db,scopedManager,new(w.Users["associate"].Id,"Tax","",2400,80m));
        Assert.False(profile.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,profile.ErrorCode);
        Assert.Equal(1800,await db.StaffProfiles.Where(x=>x.UserId==w.Users["associate"].Id).Select(x=>x.WeeklyCapacityMinutes).SingleAsync());
        break;
      case "CERTIFICATION":
        var certification=await ResourcePlanningService.AddCertificationAsync(db,scopedManager,new(w.Users["associate"].Id,"Refused certification",null,null));
        Assert.False(certification.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,certification.ErrorCode);
        Assert.Equal("Initial certification",(await db.StaffCertifications.SingleAsync()).Name);
        break;
      case "AVAILABILITY":
        var availability=await ResourcePlanningService.AddAvailabilityAsync(db,scopedManager,new(w.Users["associate"].Id,new DateOnly(2026,10,7),new DateOnly(2026,10,8),"TRAINING",240));
        Assert.False(availability.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,availability.ErrorCode);
        Assert.Single(await db.StaffAvailabilities.ToListAsync());
        break;
      default:
        var ownAllocation=await ResourcePlanningService.SetAllocationAsync(db,scopedManager,new(w.EngagementId,w.Users["associate"].Id,new DateOnly(2026,10,4),900,
          BudgetPhases.Fieldwork,"Revenue"));
        Assert.True(ownAllocation.Succeeded,ownAllocation.Message);
        var siblingId=Guid.NewGuid();
        db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement{Id=siblingId,FirmId=w.FirmId,PracticeClientId=w.ClientId,
          ServiceRoute="Accounting",PeriodStart="2027-01-01",PeriodEnd="2027-12-31",ServiceProfileId="Synthetic",Status="Active",
          ProfessionalWorkBlocked=false,CreatedAt=DateTimeOffset.UtcNow});
        await db.SaveChangesAsync();
        Assert.True((await StaffingService.AssignAsync(db,w.Actor("partner","Partner"),new(siblingId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
        var siblingAllocation=await ResourcePlanningService.SetAllocationAsync(db,scopedManager,new(siblingId,w.Users["associate"].Id,new DateOnly(2026,10,4),600,
          BudgetPhases.Fieldwork,"Revenue"));
        Assert.False(siblingAllocation.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,siblingAllocation.ErrorCode);
        var retained=await db.StaffAllocations.SingleAsync();
        Assert.Equal(w.EngagementId,retained.EngagementId);Assert.Equal(900,retained.PlannedMinutes);
        Assert.False(await db.StaffAllocations.AnyAsync(x=>x.EngagementId==siblingId));
        break;
    }
    Assert.Empty(await db.ResourcePlanningReceipts.ToListAsync());
  }
  [Fact]
  public async Task PlanningReceiptReplayAndLookupRequireCurrentFirmWideRoleAndRecoverTheExactReceiptAfterRestoration()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var manager=w.Actor("manager","Manager");
    Assert.True((await StaffingService.AssignAsync(db,w.Actor("partner","Partner"),new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
    var request=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"),manager);
    var first=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,manager,request);Assert.True(first.Succeeded,first.Message);
    var receiptId=first.Value!.Id;var epoch=manager.SessionEpoch;

    // Retain client-scoped Manager and firm-wide Staff, without advancing the session epoch.
    var revoked=await db.RoleGrants.Where(x=>x.FirmId==w.FirmId&&x.UserId==w.Users["manager"].Id&&x.Role=="Manager"&&
      x.ClientId==null&&x.EngagementId==null&&x.RevokedAt==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    Assert.Equal(1,revoked);
    db.RoleGrants.AddRange(Grant(w.FirmId,w.Users["manager"],"Manager",w.ClientId),Grant(w.FirmId,w.Users["manager"],"Staff"));
    await db.SaveChangesAsync();
    Assert.Equal(epoch,await db.Users.Where(x=>x.Id==w.Users["manager"].Id).Select(x=>x.SessionEpoch).SingleAsync());

    var deniedReplay=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,manager,request);
    Assert.False(deniedReplay.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,deniedReplay.ErrorCode);Assert.Null(deniedReplay.Value);
    var deniedLookup=await ResourcePlanningCommandWorkspace.LookupAsync(db,manager,request.RequestId,request.RequestHash);
    Assert.False(deniedLookup.Succeeded);Assert.Equal(ErrorCodes.ScopeDenied,deniedLookup.ErrorCode);Assert.Null(deniedLookup.Value);
    Assert.Equal(1,await db.StaffCertifications.CountAsync(x=>x.UserId==w.Users["associate"].Id));
    Assert.Equal(1,await db.ResourcePlanningReceipts.CountAsync(x=>x.ActorId==w.Users["manager"].Id));

    db.RoleGrants.Add(Grant(w.FirmId,w.Users["manager"],"Manager"));await db.SaveChangesAsync();
    var recovered=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,manager,request);
    Assert.True(recovered.Succeeded,recovered.Message);Assert.Equal(receiptId,recovered.Value!.Id);
    var lookup=await ResourcePlanningCommandWorkspace.LookupAsync(db,manager,request.RequestId,request.RequestHash);
    Assert.True(lookup.Succeeded,lookup.Message);Assert.True(lookup.Value!.Found);Assert.Equal(receiptId,lookup.Value.Receipt!.Id);
    Assert.Equal(1,await db.StaffCertifications.CountAsync(x=>x.UserId==w.Users["associate"].Id));
    Assert.Equal(1,await db.ResourcePlanningReceipts.CountAsync(x=>x.ActorId==w.Users["manager"].Id));
  }
  [Fact]
  public async Task PlanningLegacyAllocationReceiptReplaysOnlyItsExactPhaseAbsentSnapshotWithoutMutation()
  {
    // Publish the retained snapshot under its actual pre-phase schema, then upgrade normally.
    await using var pg=await PgTestSchema.CreateAsync("20261018052243_TimeEntryReturnWorkflow");var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var manager=w.Actor("manager","Manager");
    Assert.True((await StaffingService.AssignAsync(db,w.Actor("partner","Partner"),new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);

    var requestId=Guid.NewGuid();var requestHash=Hashing.Sha256Hex("legacy-allocation-request");var reviewBasis=Hashing.Sha256Hex("legacy-allocation-review-basis");
    var week=new DateOnly(2026,10,4);
    var legacyFields=new ResourcePlanningFields("ALLOCATION",w.Users["associate"].Id,EngagementId:w.EngagementId,WeekStart:week,PlannedMinutes:0);
    var legacyPreview=new ResourcePlanningPreview(requestId,requestHash,reviewBasis,legacyFields,
      new ResourcePlanningBefore(w.Users["associate"].DisplayName,null,null,null,null,null,"TEST CLIENT",null,null,week,null,null,null),
      "Removes the retained allocation for this engagement and week.");
    var savedPreview=JsonNode.Parse(JsonSerializer.Serialize(legacyPreview))!.AsObject();
    var savedFields=savedPreview["Fields"]!.AsObject();
    Assert.True(savedFields.Remove("Phase"));Assert.True(savedFields.Remove("RiskArea"));
    var savedBefore=savedPreview["Before"]!.AsObject();
    Assert.True(savedBefore.Remove("AllocationPhase"));Assert.True(savedBefore.Remove("AllocationRiskArea"));
    var previewJson=savedPreview.ToJsonString();
    var receiptId=Guid.NewGuid();
    db.ResourcePlanningReceipts.Add(new ResourcePlanningReceipt{Id=receiptId,FirmId=w.FirmId,ActorId=manager.UserId,ActorEpoch=manager.SessionEpoch,
      TargetUserId=w.Users["associate"].Id,RequestId=requestId,Kind="ALLOCATION",RequestHash=requestHash,ReviewBasis=reviewBasis,
      PreviewJson=previewJson,ResourceId=null,CreatedAt=DateTimeOffset.UtcNow});
    await db.SaveChangesAsync();
    await db.GetService<IMigrator>().MigrateAsync();
    Assert.Empty(await db.StaffAllocations.ToListAsync());

    var exact=new ResourcePlanningCommandRequest(requestId,legacyFields,requestHash,reviewBasis,Reviewed:true);
    var replay=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,manager,exact);
    Assert.True(replay.Succeeded,replay.Message);Assert.Equal(receiptId,replay.Value!.Id);
    Assert.Null(replay.Value.ResourceId);Assert.Null(replay.Value.Preview.Fields.Phase);Assert.Null(replay.Value.Preview.Fields.RiskArea);
    var lookup=await ResourcePlanningCommandWorkspace.LookupAsync(db,manager,requestId,requestHash);
    Assert.True(lookup.Succeeded,lookup.Message);Assert.True(lookup.Value!.Found);Assert.Equal(receiptId,lookup.Value.Receipt!.Id);
    Assert.Null(lookup.Value.Receipt.ResourceId);Assert.Equal(legacyFields,lookup.Value.Receipt.Preview.Fields);

    var changed=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,manager,exact with {Fields=legacyFields with {PlannedMinutes=60}});
    Assert.False(changed.Succeeded);Assert.Equal(ErrorCodes.StaleRevision,changed.ErrorCode);Assert.Null(changed.Value);
    Assert.Empty(await db.StaffAllocations.ToListAsync());
    Assert.Equal(1,await db.ResourcePlanningReceipts.CountAsync());
    Assert.Equal(previewJson,await db.ResourcePlanningReceipts.Where(x=>x.Id==receiptId).Select(x=>x.PreviewJson).SingleAsync());
  }
  [Fact]
  public async Task PlanningStaleAllocationOrCapacityCannotOverwriteAndRemovalHasNullResult()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);var actor=w.Actor("partner","Partner");
    Assert.True((await StaffingService.AssignAsync(db,actor,new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
    Assert.Equal("resource.invalid",(await ResourcePlanningService.SetAllocationAsync(db,actor,
      new(w.EngagementId,w.Users["associate"].Id,new DateOnly(2026,10,5),1200,BudgetPhases.Unassigned,"Revenue"))).ErrorCode);
    var r=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION"));
    Assert.True((await ResourcePlanningService.SetAllocationAsync(db,actor,new(w.EngagementId,w.Users["associate"].Id,new DateOnly(2026,10,5),1200,
      BudgetPhases.Fieldwork,"Revenue"))).Succeeded);
    Assert.True((await ResourcePlanningService.SetAllocationAsync(db,actor,new(w.EngagementId,w.Users["associate"].Id,new DateOnly(2026,10,7),600,
      BudgetPhases.Completion,"Cash"))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r)).Succeeded);
    Assert.Equal(2,await db.StaffAllocations.CountAsync());
    Assert.Equal(1800,(await ResourcePlanningService.GetGridAsync(db,actor,new DateOnly(2026,10,4),1)).Value!.Rows
      .Single(x=>x.UserId==w.Users["associate"].Id).Weeks.Single().PlannedMinutes);
    r=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION"));
    Assert.True((await ResourcePlanningService.SaveProfileAsync(db,actor,new(w.Users["associate"].Id,"Audit","",2400,80))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r)).Succeeded);
    var secondLine=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION") with
      {Phase=BudgetPhases.Completion,RiskArea="Cash",PlannedMinutes=300});
    var secondPublished=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,secondLine);
    Assert.True(secondPublished.Succeeded,secondPublished.Message);
    var removal=await PlanningReview(db,w,PlanningFields(w,"ALLOCATION") with {PlannedMinutes=0});
    var result=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,removal);Assert.True(result.Succeeded,result.Message);Assert.Null(result.Value!.ResourceId);
    var remaining=await db.StaffAllocations.SingleAsync();
    Assert.Equal((BudgetPhases.Completion,"Cash",300),(remaining.Phase,remaining.RiskArea,remaining.PlannedMinutes));
    Assert.Single(await db.EngagementStaffAssignments.ToListAsync());
    var retry=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,removal);Assert.Equal(result.Value.Id,retry.Value!.Id);
  }
  [Fact]
  public async Task PlanningScopeIdentityAndChangedIntentAreRefused()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using var db=new AuditSphereDbContext(pg.Options);
    var fields=PlanningFields(w,"CERTIFICATION");var r=await PlanningReview(db,w,fields);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("partner","Partner"),new(Guid.NewGuid(),fields with {Department="Unrelated"}))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("outsider","Partner"),new(Guid.NewGuid(),fields))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,w.Actor("partner","Partner"),new(Guid.NewGuid(),fields with {UserId=Guid.NewGuid()}))).Succeeded);
    var executed=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r);Assert.True(executed.Succeeded,executed.Message);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r with {Fields=r.Fields with {Name="Changed"}})).Succeeded);
    var foreign=await SeedAsync(pg);var hidden=await ResourcePlanningCommandWorkspace.LookupAsync(db,foreign.Actor("partner","Partner"),r.RequestId,r.RequestHash);Assert.False(hidden.Value!.Found);
    await db.Users.Where(x=>x.Id==w.Users["partner"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));
    Assert.False((await ResourcePlanningCommandWorkspace.LookupAsync(db,w.Actor("partner","Partner"),r.RequestId,r.RequestHash)).Succeeded);
  }
  private sealed class RefusePlanningReceipt:SaveChangesInterceptor
  {
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {if(e.Context!.ChangeTracker.Entries<ResourcePlanningReceipt>().Any(x=>x.State==EntityState.Added))throw new InvalidOperationException("Synthetic receipt refusal");return ValueTask.FromResult(result);}
  }
  [Fact]
  public async Task PlanningReceiptFailureRollsBackAdditiveMutation()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(new RefusePlanningReceipt()).Options)) {
      var r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));
      await Assert.ThrowsAsync<InvalidOperationException>(async()=>await ResourcePlanningCommandWorkspace.ExecuteAsync(db,w.Actor("partner","Partner"),r));
    }
    await using var after=new AuditSphereDbContext(pg.Options);Assert.Empty(await after.StaffCertifications.ToListAsync());Assert.Empty(await after.ResourcePlanningReceipts.ToListAsync());
  }
  private sealed class PlanningLockObserver:DbCommandInterceptor
  {
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData e,InterceptionResult<DbDataReader> result,CancellationToken ct=default)
    {if(command.CommandText.Contains("firm_safety_states",StringComparison.Ordinal)&&command.CommandText.Contains("FOR UPDATE",StringComparison.Ordinal))Reached.TrySetResult(true);return ValueTask.FromResult(result);}
  }
  [Fact]
  public async Task PlanningActorRevokedDuringFirmLockWaitPublishesNothing()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);ResourcePlanningCommandRequest r;
    await using(var db=new AuditSphereDbContext(pg.Options))r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));
    await using var blocker=new AuditSphereDbContext(pg.Options);await using var tx=await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={w.FirmId} FOR UPDATE").SingleAsync();
    var observe=new PlanningLockObserver();await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(observe).Options);
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));var attempt=ResourcePlanningCommandWorkspace.ExecuteAsync(writer,w.Actor("partner","Partner"),r,timeout.Token);
    await observe.Reached.Task.WaitAsync(timeout.Token);
    await blocker.RoleGrants.Where(x=>x.UserId==w.Users["partner"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedAt,DateTimeOffset.UtcNow));
    await blocker.Users.Where(x=>x.Id==w.Users["partner"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));await tx.CommitAsync();
    Assert.False((await attempt).Succeeded);await using var after=new AuditSphereDbContext(pg.Options);Assert.Empty(await after.ResourcePlanningReceipts.ToListAsync());Assert.Empty(await after.StaffCertifications.ToListAsync());
  }
  private sealed class PausePlanningReceipt:SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default)
    {
      if(e.Context!.ChangeTracker.Entries<ResourcePlanningReceipt>().Any(x=>x.State==EntityState.Added)){Reached.TrySetResult(true);await Release.Task.WaitAsync(ct);}return result;
    }
  }
  [Fact]
  public async Task PlanningLookupAndConcurrentReplayWaitForPublicationThenReturnOneReceipt()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);ResourcePlanningCommandRequest r;
    await using(var db=new AuditSphereDbContext(pg.Options))r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));
    var pause=new PausePlanningReceipt();var observe=new PlanningLockObserver();
    await using var writer=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(pause).Options);
    await using var reader=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(observe).Options);
    await using var replayDb=new AuditSphereDbContext(pg.Options);using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
    try {
      var first=ResourcePlanningCommandWorkspace.ExecuteAsync(writer,w.Actor("partner","Partner"),r,timeout.Token);await pause.Reached.Task.WaitAsync(timeout.Token);
      var lookup=ResourcePlanningCommandWorkspace.LookupAsync(reader,w.Actor("partner","Partner"),r.RequestId,r.RequestHash,timeout.Token);await observe.Reached.Task.WaitAsync(timeout.Token);Assert.False(lookup.IsCompleted);
      var replay=ResourcePlanningCommandWorkspace.ExecuteAsync(replayDb,w.Actor("partner","Partner"),r,timeout.Token);pause.Release.TrySetResult(true);
      var result=await first;Assert.True(result.Succeeded,result.Message);var recovered=await lookup;Assert.True(recovered.Value!.Found);Assert.Equal(result.Value!.Id,recovered.Value.Receipt!.Id);Assert.Equal(result.Value.Id,(await replay).Value!.Id);
    }finally{pause.Release.TrySetResult(true);}
    await using var after=new AuditSphereDbContext(pg.Options);Assert.Single(await after.StaffCertifications.ToListAsync());Assert.Single(await after.ResourcePlanningReceipts.ToListAsync());
  }
  [Fact]
  public async Task DisabledTargetCannotReceiveNewPlanningActionsButExistingReceiptRemainsRecoverable()
  {
    await using var pg=await PgTestSchema.CreateAsync();var w=await SeedAsync(pg);await using var db=new AuditSphereDbContext(pg.Options);var actor=w.Actor("partner","Partner");
    var r=await PlanningReview(db,w,PlanningFields(w,"CERTIFICATION"));var first=await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r);Assert.True(first.Succeeded,first.Message);
    Assert.True((await StaffingService.AssignAsync(db,actor,new(w.EngagementId,w.Users["associate"].Id,StaffingLevels.StaffAssociate))).Succeeded);
    var allocationFields=PlanningFields(w,"ALLOCATION");
    var allocationReview=await PlanningReview(db,w,allocationFields);
    await db.Users.Where(x=>x.Id==w.Users["associate"].Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Disabled,true));
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,actor,new(Guid.NewGuid(),r.Fields))).Succeeded);
    Assert.False((await ResourcePlanningCommandWorkspace.PreviewAsync(db,actor,new(Guid.NewGuid(),allocationFields))).Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied,(await ResourcePlanningService.SetAllocationAsync(db,actor,
      new(w.EngagementId,w.Users["associate"].Id,allocationFields.WeekStart!.Value,allocationFields.PlannedMinutes!.Value,
        allocationFields.Phase!,allocationFields.RiskArea))).ErrorCode);
    Assert.False((await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,allocationReview)).Succeeded);
    Assert.Empty(await db.StaffAllocations.ToListAsync());
    Assert.False((await ResourcePlanningService.AddCertificationAsync(db,actor,new(w.Users["associate"].Id,"Refused",null,null))).Succeeded);
    Assert.True((await ResourcePlanningCommandWorkspace.LookupAsync(db,actor,r.RequestId,r.RequestHash)).Value!.Found);
    Assert.Equal(first.Value!.Id,(await ResourcePlanningCommandWorkspace.ExecuteAsync(db,actor,r)).Value!.Id);
  }

}
