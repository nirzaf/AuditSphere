using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class GeneralLedgerCompletenessApiTests
{
  [Fact]
  public void LegacyQueuedCompletenessOperation_RetainsItsOriginalRequestDigest()
  {
    var firm = Guid.NewGuid(); var client = Guid.NewGuid(); var engagement = Guid.NewGuid();
    var period = Guid.NewGuid(); var dataset = Guid.NewGuid(); var batch = Guid.NewGuid(); var user = Guid.NewGuid();
    var handler = new GeneralLedgerCompletenessHandler();
    var legacyPayload = JsonSerializer.Serialize(new
    {
      clientId = client.ToString("D"), engagementId = engagement.ToString("D"), periodId = period.ToString("D"),
      bookId = (string?)null, trialBalanceDatasetId = dataset.ToString("D"), importBatchId = batch.ToString("D"),
      evidenceReference = "retained legacy intent", openingTrialBalanceDatasetId = (string?)null
    });
    var request = new OperationRequest(firm, client, engagement, handler.Definition.Kind, dataset, 1,
      "legacy-completeness-key", legacyPayload, user);
    var originalCanonicalPayload = JsonSerializer.Serialize(new
    {
      clientId = client.ToString("D"), engagementId = engagement.ToString("D"), periodId = period.ToString("D"),
      bookId = (string?)null, trialBalanceDatasetId = dataset.ToString("D"), importBatchId = batch.ToString("D"),
      evidenceReference = "retained legacy intent", openingTrialBalanceDatasetId = (string?)null
    });
    var requestBytes = OperationEncoding.Encode(request, handler.Definition, originalCanonicalPayload);
    var operation = new DurableOperation
    {
      OperationKind = handler.Definition.Kind, FirmId = firm, ClientId = client, EngagementId = engagement,
      TargetId = dataset, ExpectedRevision = 1, IdempotencyKey = request.IdempotencyKey, OriginatorId = user,
      PayloadJson = legacyPayload, RequestBytes = requestBytes, RequestDigest = Hashing.Sha256Hex(requestBytes)
    };

    Assert.DoesNotContain("calculationVersion", handler.NormalizePayload(request));
    Assert.True(OperationEncoding.Matches(operation, handler));
  }

  [Fact]
  public async Task ReviewedConcurrentPreparation_DurableWorker_ExactResidualPages_IndependentImmutableReview()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-COMPLETE");var f=await PbcSeed.SeedAsync(pg);GeneralLedgerCompletenessSeed.Pair pair;
    await using(var db=new AuditSphereDbContext(pg.Options))pair=await GeneralLedgerCompletenessSeed.SeedAsync(db,f,accounts:52);
    using var sf=Factory(pg,f.Staff);using var staff=sf.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(staff);
    var w=await Read(staff,Path(pair.BatchId));Assert.Single(w.GetProperty("closingSources").GetProperty("items").EnumerateArray());Assert.Single(w.GetProperty("openingSources").GetProperty("items").EnumerateArray());Assert.Empty(w.GetProperty("bridges").EnumerateArray());
    var p=await Read(staff,Plan(pair));var revision=p.GetProperty("revision").GetString()!;
    Assert.Equal(HttpStatusCode.Forbidden,(await Prepare(staff,"wrong",pair,revision)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Prepare(staff,csrf,pair,revision,false)).StatusCode);
    var writes=await Task.WhenAll(Prepare(staff,csrf,pair,revision),Prepare(staff,csrf,pair,revision));Assert.Single(writes,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(writes,x=>x.StatusCode==HttpStatusCode.Conflict);
    p=await Read(staff,Plan(pair));Assert.False(p.GetProperty("canPrepare").GetBoolean());Assert.Single(p.GetProperty("operations").EnumerateArray());
    Assert.True(await GeneralLedgerCompletenessSeed.RunWorkerAsync(pg.Options,f.FirmId));Assert.False(await GeneralLedgerCompletenessSeed.RunWorkerAsync(pg.Options,f.FirmId));
    var bridgeId=(await Read(staff,Path(pair.BatchId))).GetProperty("bridges")[0].GetProperty("id").GetGuid();
    var self=await Read(staff,ReviewPath(bridgeId));Assert.False(self.GetProperty("canReject").GetBoolean());Assert.Contains("preparer",self.GetProperty("reviewBlocker").GetString());
    using var rf=Factory(pg,f.Reviewer);using var reviewer=rf.CreateClient(new(){AllowAutoRedirect=false});var rc=await SignIn(reviewer);
    var r=await Read(reviewer,ReviewPath(bridgeId));Assert.True(r.GetProperty("canApprove").GetBoolean());Assert.Equal(52,r.GetProperty("residuals").GetProperty("totalCount").GetInt32());Assert.Equal(50,r.GetProperty("residuals").GetProperty("items").GetArrayLength());
    Assert.Equal("100.123456",r.GetProperty("residuals").GetProperty("items")[0].GetProperty("movement").GetString());Assert.Equal("0.000000",r.GetProperty("residuals").GetProperty("items")[0].GetProperty("opening").GetString());
    Assert.Equal(2,(await Read(reviewer,ReviewPath(bridgeId)+"?page=2")).GetProperty("residuals").GetProperty("items").GetArrayLength());
    Assert.Equal(HttpStatusCode.Forbidden,(await Decide(reviewer,"wrong",bridgeId,r.GetProperty("revision").GetString()!,true)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Decide(reviewer,rc,bridgeId,r.GetProperty("revision").GetString()!,true,false)).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict,(await Write(reviewer,rc,ReviewPath(bridgeId)+"/review",new{revision=(string?)null,approve=true,reviewed=false})).StatusCode);
    var decisions=await Task.WhenAll(Decide(reviewer,rc,bridgeId,r.GetProperty("revision").GetString()!,true),Decide(reviewer,rc,bridgeId,r.GetProperty("revision").GetString()!,true));Assert.Single(decisions,x=>x.StatusCode==HttpStatusCode.OK);Assert.Single(decisions,x=>x.StatusCode==HttpStatusCode.Conflict);
    var observed=await Read(reviewer,ReviewPath(bridgeId));Assert.Equal("APPROVED",observed.GetProperty("bridge").GetProperty("status").GetString());Assert.Equal(f.Reviewer.Id,observed.GetProperty("bridge").GetProperty("reviewedByUserId").GetGuid());Assert.False(observed.GetProperty("canReject").GetBoolean());
    await using var proof=new AuditSphereDbContext(pg.Options);Assert.Single(await proof.GeneralLedgerCompletenessBridges.ToListAsync());Assert.Single(await proof.DurableOperations.ToListAsync());Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());Assert.Equal(1,await proof.ClientSafetyStates.Where(x=>x.Id==f.ClientId).Select(x=>x.InputGeneration).SingleAsync());
  }
  [Fact]
  public async Task MissingOpeningCoverageIsUnknown_NotZero_AndBlocksApprovalButAllowsRejection()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-COMPLETE-OPENING");var f=await PbcSeed.SeedAsync(pg);GeneralLedgerCompletenessSeed.Pair pair;
    var sources = new[]
    {
      new GeneralLedgerCompletenessSeed.AccountSource("1000", 100m, 50m, 150m),
      new GeneralLedgerCompletenessSeed.AccountSource("2000", -100m, -50m, -150m),
      new GeneralLedgerCompletenessSeed.AccountSource("3000", null, 25m, null),
      new GeneralLedgerCompletenessSeed.AccountSource("4000", 10m, null, null),
      new GeneralLedgerCompletenessSeed.AccountSource("5000", null, -25m, null),
      new GeneralLedgerCompletenessSeed.AccountSource("6000", -10m, null, null),
    };
    await using(var db=new AuditSphereDbContext(pg.Options))pair=await GeneralLedgerCompletenessSeed.SeedAsync(db,f,sourceRows:sources);
    using var sf=Factory(pg,f.Staff);using var staff=sf.CreateClient(new(){AllowAutoRedirect=false});var sc=await SignIn(staff);var p=await Read(staff,Plan(pair));var prepared=await Prepare(staff,sc,pair,p.GetProperty("revision").GetString()!);Assert.True(prepared.StatusCode==HttpStatusCode.OK,await prepared.Content.ReadAsStringAsync());Assert.True(await GeneralLedgerCompletenessSeed.RunWorkerAsync(pg.Options,f.FirmId));
    var bridge=(await Read(staff,Path(pair.BatchId))).GetProperty("bridges")[0].GetProperty("id").GetGuid();using var rf=Factory(pg,f.Reviewer);using var reviewer=rf.CreateClient(new(){AllowAutoRedirect=false});var rc=await SignIn(reviewer);var r=await Read(reviewer,ReviewPath(bridge));
    Assert.True(r.GetProperty("bridge").GetProperty("incompleteExtract").GetBoolean());Assert.Contains("OPENING_ACCOUNT_COVERAGE_MISSING",r.GetProperty("bridge").GetProperty("completenessDisclosure").GetString());Assert.False(r.GetProperty("canApprove").GetBoolean());Assert.True(r.GetProperty("canReject").GetBoolean());
    var items=r.GetProperty("residuals").GetProperty("items");Assert.Equal(6,r.GetProperty("residuals").GetProperty("totalCount").GetInt32());
    var glOnly=items[2];Assert.Equal("3000",glOnly.GetProperty("accountCode").GetString());Assert.Equal(JsonValueKind.Null,glOnly.GetProperty("opening").ValueKind);Assert.Equal(JsonValueKind.Null,glOnly.GetProperty("movementClosingDifference").ValueKind);Assert.Equal(JsonValueKind.Null,glOnly.GetProperty("openingMovementDifference").ValueKind);
    var openingOnly=items[3];Assert.Equal("4000",openingOnly.GetProperty("accountCode").GetString());Assert.Equal("10.000000",openingOnly.GetProperty("opening").GetString());Assert.Equal("10.000000",openingOnly.GetProperty("movementClosingDifference").GetString());Assert.Equal("-10.000000",openingOnly.GetProperty("openingMovementDifference").GetString());
    Assert.Equal(HttpStatusCode.Conflict,(await Decide(reviewer,rc,bridge,r.GetProperty("revision").GetString()!,true)).StatusCode);
    Assert.Equal(HttpStatusCode.OK,(await Decide(reviewer,rc,bridge,r.GetProperty("revision").GetString()!,false)).StatusCode);
    Assert.Equal("REJECTED",(await Read(reviewer,ReviewPath(bridge))).GetProperty("bridge").GetProperty("status").GetString());
  }

  [Fact]
  public async Task OpeningPlusMovementVersion2_ApprovesSignedCoverageAndPreservesLegacyV1History()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-COMPLETE-VERSIONS");var f=await PbcSeed.SeedAsync(pg);GeneralLedgerCompletenessSeed.Pair pair;Guid legacyId;
    var sources = new[]
    {
      new GeneralLedgerCompletenessSeed.AccountSource("1000", 100m, 50m, 150m),
      new GeneralLedgerCompletenessSeed.AccountSource("2000", -100m, -50m, -150m),
    };
    await using(var db=new AuditSphereDbContext(pg.Options))pair=await GeneralLedgerCompletenessSeed.SeedAsync(db,f,sourceRows:sources);
    var legacyDigest=Hashing.Sha256Hex("retained version 1 proof");
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      var closingHash=pair.ClosingId.ToString("N")+pair.ClosingId.ToString("N");
      var openingHash=pair.OpeningId.ToString("N")+pair.OpeningId.ToString("N");
      var batchHash=pair.BatchId.ToString("N")+pair.BatchId.ToString("N");
      var legacy=new GeneralLedgerCompletenessBridge
      {
        Id=Guid.NewGuid(),FirmId=f.FirmId,ClientId=f.ClientId,EngagementId=f.EngagementId,PeriodId=pair.PeriodId,BookId=pair.BookId,
        TrialBalanceDatasetId=pair.ClosingId,ImportBatchId=pair.BatchId,OpeningTrialBalanceDatasetId=pair.OpeningId,
        CalculationVersion=GeneralLedgerCompletenessBridge.LegacyCalculationVersion,TrialBalanceHash=closingHash,
        GeneralLedgerHash=batchHash,OpeningTrialBalanceHash=openingHash,AccountResidualDigest=legacyDigest,
        OpeningMovementResidualDigest=legacyDigest,TrialBalanceAccountCount=2,GeneralLedgerAccountCount=2,MatchedAccountCount=0,
        MismatchedAccountCount=2,OpeningMovementMismatchedAccountCount=0,JournalExceptionCount=0,
        OpeningAmount=0m,MovementAmount=0m,ClosingAmount=0m,OpeningMovementResidual=0m,AbsoluteResidual=200m,
        CoverageStart=new DateOnly(2026,6,30),CoverageEnd=new DateOnly(2026,6,30),Status="UNRECONCILED",
        IncompleteExtract=false,CompletenessDisclosure="",EvidenceReference="Retained historical calculation v1",
        CreatedByUserId=f.Staff.Id,CreatedAt=DateTimeOffset.UtcNow
      };
      db.GeneralLedgerCompletenessBridges.Add(legacy);await db.SaveChangesAsync();legacyId=legacy.Id;
    }
    using var sf=Factory(pg,f.Staff);using var staff=sf.CreateClient(new(){AllowAutoRedirect=false});var sc=await SignIn(staff);
    var plan=await Read(staff,Plan(pair));Assert.True(plan.GetProperty("canPrepare").GetBoolean());Assert.Equal(JsonValueKind.Null,plan.GetProperty("existingBridgeId").ValueKind);
    using var rf=Factory(pg,f.Reviewer);using var reviewer=rf.CreateClient(new(){AllowAutoRedirect=false});var rc=await SignIn(reviewer);
    var legacyReview=await Read(reviewer,ReviewPath(legacyId));var legacyBridge=legacyReview.GetProperty("bridge");
    Assert.Equal(1,legacyBridge.GetProperty("calculationVersion").GetInt32());Assert.Equal("200.000000",legacyBridge.GetProperty("absoluteResidual").GetString());
    Assert.Equal(2,legacyBridge.GetProperty("mismatchedAccountCount").GetInt32());Assert.False(legacyReview.GetProperty("canApprove").GetBoolean());
    Assert.Contains("Historical calculation version 1",legacyReview.GetProperty("approvalBlocker").GetString());
    var legacyRows=legacyReview.GetProperty("residuals").GetProperty("items");Assert.Equal("-100.000000",legacyRows[0].GetProperty("movementClosingDifference").GetString());
    var legacyApproval=await Decide(reviewer,rc,legacyId,legacyReview.GetProperty("revision").GetString()!,true);
    Assert.Equal(HttpStatusCode.BadRequest,legacyApproval.StatusCode);
    using var legacyError=await JsonDocument.ParseAsync(await legacyApproval.Content.ReadAsStreamAsync());
    Assert.Equal(ErrorCodes.GateBlocked,legacyError.RootElement.GetProperty("code").GetString());

    Assert.Equal(HttpStatusCode.OK,(await Prepare(staff,sc,pair,plan.GetProperty("revision").GetString()!)).StatusCode);
    Assert.True(await GeneralLedgerCompletenessSeed.RunWorkerAsync(pg.Options,f.FirmId));
    var workspace=await Read(staff,Path(pair.BatchId));var summaries=workspace.GetProperty("bridges");Assert.Equal(2,summaries.GetArrayLength());
    var currentId=summaries.EnumerateArray().Single(x=>x.GetProperty("calculationVersion").GetInt32()==2).GetProperty("id").GetGuid();
    var currentReview=await Read(reviewer,ReviewPath(currentId));var currentBridge=currentReview.GetProperty("bridge");
    Assert.Equal(2,currentBridge.GetProperty("calculationVersion").GetInt32());Assert.Equal("RECONCILED",currentBridge.GetProperty("status").GetString());
    Assert.Equal("0.000000",currentBridge.GetProperty("absoluteResidual").GetString());Assert.Equal(0,currentBridge.GetProperty("mismatchedAccountCount").GetInt32());
    Assert.True(currentReview.GetProperty("canApprove").GetBoolean());
    var currentRows=currentReview.GetProperty("residuals").GetProperty("items");
    Assert.Equal("0.000000",currentRows[0].GetProperty("movementClosingDifference").GetString());
    Assert.Equal("0.000000",currentRows[0].GetProperty("openingMovementDifference").GetString());
    Assert.Equal(HttpStatusCode.OK,(await Decide(reviewer,rc,currentId,currentReview.GetProperty("revision").GetString()!,true)).StatusCode);
    await using var verify=new AuditSphereDbContext(pg.Options);var retained=await verify.GeneralLedgerCompletenessBridges.OrderBy(x=>x.CalculationVersion).ToListAsync();
    Assert.Equal(new[]{1,2},retained.Select(x=>x.CalculationVersion));Assert.Equal("UNRECONCILED",retained[0].Status);
    Assert.Equal(legacyDigest,retained[0].AccountResidualDigest);Assert.Equal(legacyDigest,retained[0].OpeningMovementResidualDigest);
    Assert.Equal(200m,retained[0].AbsoluteResidual);Assert.NotEqual(retained[0].AccountResidualDigest,retained[1].AccountResidualDigest);
  }
  [Fact]
  public async Task CalculationVersionMigration_PreservesOldRowsAsVersion1AndDefaultsNewRowsToVersion2()
  {
    const string previousMigration="20261126145049_FirmExpenseJournalCancellation";
    await using var pg=await PgTestSchema.CreateAsync(previousMigration);var f=await PbcSeed.SeedAsync(pg);GeneralLedgerCompletenessSeed.Pair pair;
    await using(var db=new AuditSphereDbContext(pg.Options))pair=await GeneralLedgerCompletenessSeed.SeedAsync(db,f);
    var legacyId=Guid.NewGuid();var digest=Hashing.Sha256Hex("pre-versioned completeness proof");
    var closingHash=pair.ClosingId.ToString("N")+pair.ClosingId.ToString("N");
    var openingHash=pair.OpeningId.ToString("N")+pair.OpeningId.ToString("N");
    var batchHash=pair.BatchId.ToString("N")+pair.BatchId.ToString("N");
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO general_ledger_completeness_bridges
          (id,firm_id,client_id,engagement_id,period_id,book_id,trial_balance_dataset_id,import_batch_id,
           trial_balance_hash,general_ledger_hash,account_residual_digest,trial_balance_account_count,
           general_ledger_account_count,matched_account_count,mismatched_account_count,absolute_residual,
           coverage_start,coverage_end,status,evidence_reference,created_by_user_id,reviewed_by_user_id,
           created_at,reviewed_at,opening_trial_balance_dataset_id,opening_trial_balance_hash,
           opening_movement_residual_digest,opening_movement_mismatched_account_count,journal_exception_count,
           opening_amount,movement_amount,closing_amount,opening_movement_residual,incomplete_extract,completeness_disclosure)
        VALUES ({legacyId},{f.FirmId},{f.ClientId},{f.EngagementId},{pair.PeriodId},{pair.BookId},
          {pair.ClosingId},{pair.BatchId},{closingHash},{batchHash},{digest},2,2,2,0,0,
          DATE '2026-06-30',DATE '2026-06-30','RECONCILED','Pre-versioned retained proof',
          {f.Staff.Id},NULL,{DateTimeOffset.UtcNow},NULL,{pair.OpeningId},{openingHash},{digest},0,0,
          0,0,0,0,false,'')
        """);
      await db.Database.MigrateAsync();
    }
    var futureId=Guid.NewGuid();
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO general_ledger_completeness_bridges
          (id,firm_id,client_id,engagement_id,period_id,book_id,trial_balance_dataset_id,import_batch_id,
           trial_balance_hash,general_ledger_hash,account_residual_digest,trial_balance_account_count,
           general_ledger_account_count,matched_account_count,mismatched_account_count,absolute_residual,
           coverage_start,coverage_end,status,evidence_reference,created_by_user_id,reviewed_by_user_id,
           created_at,reviewed_at,opening_trial_balance_dataset_id,opening_trial_balance_hash,
           opening_movement_residual_digest,opening_movement_mismatched_account_count,journal_exception_count,
           opening_amount,movement_amount,closing_amount,opening_movement_residual,incomplete_extract,completeness_disclosure)
        SELECT {futureId},firm_id,client_id,engagement_id,period_id,book_id,trial_balance_dataset_id,import_batch_id,
          trial_balance_hash,general_ledger_hash,account_residual_digest,trial_balance_account_count,
          general_ledger_account_count,matched_account_count,mismatched_account_count,absolute_residual,
          coverage_start,coverage_end,status,evidence_reference,created_by_user_id,reviewed_by_user_id,
          created_at,reviewed_at,opening_trial_balance_dataset_id,opening_trial_balance_hash,
          opening_movement_residual_digest,opening_movement_mismatched_account_count,journal_exception_count,
          opening_amount,movement_amount,closing_amount,opening_movement_residual,incomplete_extract,completeness_disclosure
        FROM general_ledger_completeness_bridges WHERE id={legacyId}
        """);
      var rows=await db.GeneralLedgerCompletenessBridges.OrderBy(x=>x.CalculationVersion).ToListAsync();
      Assert.Equal(new[]{1,2},rows.Select(x=>x.CalculationVersion));Assert.Equal(legacyId,rows[0].Id);Assert.Equal(futureId,rows[1].Id);
      Assert.Equal(digest,rows[0].AccountResidualDigest);Assert.Equal(0m,rows[0].AbsoluteResidual);Assert.Equal(0m,rows[1].AbsoluteResidual);
    }
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      var rollback=await Assert.ThrowsAsync<InvalidOperationException>(()=>db.Database.MigrateAsync(previousMigration));
      var databaseError=Assert.IsType<PostgresException>(rollback.InnerException);
      Assert.Contains("Version 2 completeness proofs prevent rollback",databaseError.Message);
    }
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(new[]{1,2},await db.GeneralLedgerCompletenessBridges.OrderBy(x=>x.CalculationVersion).Select(x=>x.CalculationVersion).ToArrayAsync());
    }
  }

  [Fact]
  public async Task ScopeBounds_StaleGeneration_ClosedBookAndEpochRefuseReadsAndPreparation()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-COMPLETE-GATES");var f=await PbcSeed.SeedAsync(pg);var foreign=await PbcSeed.SeedAsync(pg);GeneralLedgerCompletenessSeed.Pair pair,other;
    await using(var db=new AuditSphereDbContext(pg.Options)){pair=await GeneralLedgerCompletenessSeed.SeedAsync(db,f);other=await GeneralLedgerCompletenessSeed.SeedAsync(db,foreign);}
    using var af=Factory(pg,f.Admin);using var admin=af.CreateClient(new(){AllowAutoRedirect=false});var csrf=await SignIn(admin);var p=await Read(admin,Plan(pair));var revision=p.GetProperty("revision").GetString()!;
    foreach(var url in new[]{Path(other.BatchId),Path(Guid.NewGuid()),Plan(pair with{ClosingId=other.ClosingId}),Plan(pair with{OpeningId=other.OpeningId})}){var denied=await admin.GetAsync(url);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);Assert.DoesNotContain("totalCount",await denied.Content.ReadAsStringAsync());}
    using var cf=Factory(pg,f.Client);using var client=cf.CreateClient(new(){AllowAutoRedirect=false});await SignIn(client);Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Path(pair.BatchId))).StatusCode);
    foreach(var suffix in new[]{"?page=0","?openingPage=0","?page=2147483647"})Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync(Path(pair.BatchId)+suffix)).StatusCode);
    await using(var db=new AuditSphereDbContext(pg.Options))await db.ClientSafetyStates.Where(x=>x.Id==f.ClientId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.InputGeneration,x=>x.InputGeneration+1));
    Assert.Equal(HttpStatusCode.Conflict,(await Prepare(admin,csrf,pair,revision)).StatusCode);
    p=await Read(admin,Plan(pair));await using(var db=new AuditSphereDbContext(pg.Options))await db.ClientReportingBooks.Where(x=>x.Id==pair.BookId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"CLOSED"));
    Assert.Equal(HttpStatusCode.BadRequest,(await Prepare(admin,csrf,pair,p.GetProperty("revision").GetString()!)).StatusCode);Assert.False((await Read(admin,Path(pair.BatchId))).GetProperty("context").GetProperty("canPrepare").GetBoolean());
    await using(var db=new AuditSphereDbContext(pg.Options))await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));Assert.Equal(HttpStatusCode.Unauthorized,(await admin.GetAsync(Path(pair.BatchId))).StatusCode);
    await using var proof=new AuditSphereDbContext(pg.Options);Assert.Empty(await proof.DurableOperations.ToListAsync());
  }
  [Fact]
  public async Task FinalEpochRevocationRollsBackEnqueueAndReview()
  {
    await using var pg=await OwnedPostgresDatabase.CreateAsync("API-GL-COMPLETE-LATE");var f=await PbcSeed.SeedAsync(pg);GeneralLedgerCompletenessSeed.Pair pair;var actor=PbcSeed.Actor(f.Admin,"Administrator");
    await using(var db=new AuditSphereDbContext(pg.Options))pair=await GeneralLedgerCompletenessSeed.SeedAsync(db,f);
    var revoke=new AfterMutation("INSERT INTO durable_operations",async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.Users.Where(x=>x.Id==f.Admin.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));});
    var store=new PostgresOperationStore(new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options)));
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revoke).Options)){var p=await GeneralLedgerCompletenessWorkspace.PlanAsync(db,actor,pair.BatchId,pair.ClosingId,pair.OpeningId);var r=await GeneralLedgerCompletenessWorkspace.PrepareAsync(db,actor,pair.BatchId,pair.ClosingId,pair.OpeningId,p.Value!.Revision,"late epoch",true,store,new());Assert.Equal(ErrorCodes.GenerationStale,r.ErrorCode);}
    await using(var db=new AuditSphereDbContext(pg.Options))Assert.Empty(await db.DurableOperations.ToListAsync());
    var preparer=PbcSeed.Actor(f.Staff,"AccountingPreparer");Guid bridge;
    await using(var db=new AuditSphereDbContext(pg.Options)){var p=await GeneralLedgerCompletenessWorkspace.PlanAsync(db,preparer,pair.BatchId,pair.ClosingId,pair.OpeningId);Assert.True((await GeneralLedgerCompletenessWorkspace.PrepareAsync(db,preparer,pair.BatchId,pair.ClosingId,pair.OpeningId,p.Value!.Revision,"prepared",true,store,new())).Succeeded);}
    Assert.True(await GeneralLedgerCompletenessSeed.RunWorkerAsync(pg.Options,f.FirmId));await using(var db=new AuditSphereDbContext(pg.Options))bridge=await db.GeneralLedgerCompletenessBridges.Select(x=>x.Id).SingleAsync();
    var reviewer=PbcSeed.Actor(f.Reviewer,"AccountingReviewer");var revokeReview=new AfterMutation("UPDATE general_ledger_completeness_bridges",async()=>{await using var db=new AuditSphereDbContext(pg.Options);await db.Users.Where(x=>x.Id==f.Reviewer.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.SessionEpoch,x=>x.SessionEpoch+1));});
    await using(var db=new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(revokeReview).Options)){var r=await GeneralLedgerCompletenessWorkspace.ReviewAsync(db,reviewer,bridge);var result=await GeneralLedgerCompletenessWorkspace.DecideAsync(db,reviewer,bridge,true,r.Value!.Revision,true);Assert.Equal(ErrorCodes.GenerationStale,result.ErrorCode);}
    await using var proof=new AuditSphereDbContext(pg.Options);var retained=await proof.GeneralLedgerCompletenessBridges.SingleAsync();Assert.Equal("RECONCILED",retained.Status);Assert.Null(retained.ReviewedByUserId);
  }
  private sealed class AfterMutation(string marker,Func<Task> mutation):DbCommandInterceptor
  {private bool done;private async Task Change(DbCommand c){if(!done && c.CommandText.Contains(marker,StringComparison.Ordinal)){done=true;await mutation();}}public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand c,CommandExecutedEventData e,DbDataReader r,CancellationToken ct=default){await Change(c);return r;}public override async ValueTask<int> NonQueryExecutedAsync(DbCommand c,CommandExecutedEventData e,int r,CancellationToken ct=default){await Change(c);return r;}}
  private static string Path(Guid id)=>$"/api/ui/gl-sources/{id}/completeness";
  private static string Plan(GeneralLedgerCompletenessSeed.Pair p)=>Path(p.BatchId)+$"/plan?trialBalanceId={p.ClosingId}&openingId={p.OpeningId}";
  private static string ReviewPath(Guid id)=>$"/api/ui/gl-completeness/{id}";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg,AppUser u)=>new(new Dictionary<string,string?>{["ConnectionStrings:AuditSphere"]=pg.ConnectionString,["DevelopmentIdentity:Enabled"]="true",["DevelopmentIdentity:TenantId"]=u.TenantId,["DevelopmentIdentity:Subject"]=u.Subject,["Application:AllowSimulationAdapters"]="true",["ExternalEffects:Enabled"]="false"});
  private static async Task<string> SignIn(HttpClient c){await c.GetAsync("/auth/sign-in");using var r=await c.GetAsync("/api/ui/session");return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x=>x.StartsWith("XSRF-TOKEN=",StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);}
  private static async Task<JsonElement> Read(HttpClient c,string path){using var r=await c.GetAsync(path);Assert.Equal(HttpStatusCode.OK,r.StatusCode);using var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync());return json.RootElement.Clone();}
  private static Task<HttpResponseMessage> Write(HttpClient c,string csrf,string url,object body){var r=new HttpRequestMessage(HttpMethod.Post,url){Content=JsonContent.Create(body)};r.Headers.Add("X-XSRF-TOKEN",csrf);return c.SendAsync(r);}
  private static Task<HttpResponseMessage> Prepare(HttpClient c,string csrf,GeneralLedgerCompletenessSeed.Pair p,string revision,bool reviewed=true)=>Write(c,csrf,Path(p.BatchId),new{trialBalanceId=p.ClosingId,openingId=p.OpeningId,revision,reviewed,evidenceReference="Synthetic independent completeness evidence"});
  private static Task<HttpResponseMessage> Decide(HttpClient c,string csrf,Guid bridge,string revision,bool approve,bool reviewed=true)=>Write(c,csrf,ReviewPath(bridge)+"/review",new{revision,approve,reviewed});
}
