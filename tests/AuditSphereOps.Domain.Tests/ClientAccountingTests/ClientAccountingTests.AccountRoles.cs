using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  public async Task AccountRolesRequireIndependentReviewAndBlockGenericControlJournals()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var s = await SeedAsync(pg);
    var maker = Actor(s.Preparer, "AccountingPreparer"); var reviewer = Actor(s.Reviewer, "AccountingReviewer");
    Guid periodId, chartId, assetId, expenseId, roleId, draftId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new() { Id = Guid.CreateVersion7(), FirmId = s.FirmId, PracticeClientId = s.ClientA, ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Synthetic service", EvaluationTemplateVersion = "TEST-1", EvaluationSnapshotDigest = new string('f',64), DecidedByUserId = s.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, reviewer, new(s.ClientA,"QA","QAR",1,1,"AUDITSPHERE","NATIVE",ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
      var period = await ClientAccountingService.CreatePeriodAsync(db,maker,new(s.ClientA,"2026",new(2026,1,1),new(2026,12,31),"STATUTORY","QAR")); Assert.True(period.Succeeded);periodId=period.Value;
      var chart = await ClientAccountingService.CreateChartVersionAsync(db,maker,s.ClientA,"AUDITSPHERE",new(2026,1,1));Assert.True(chart.Succeeded);chartId=chart.Value;
      Assert.True((await ClientAccountingService.AddAccountsAsync(db,maker,chartId,[new("ar","1100","Receivables","ASSET","DEBIT",true),new("expense","6000","Expense","EXPENSE","DEBIT",true)])).Succeeded);
      Assert.True((await ClientAccountingService.PublishChartVersionAsync(db,reviewer,chartId)).Succeeded);
      assetId=await db.ClientAccounts.Where(x=>x.ChartVersionId==chartId&&x.AccountCode=="1100").Select(x=>x.Id).SingleAsync();
      expenseId=await db.ClientAccounts.Where(x=>x.ChartVersionId==chartId&&x.AccountCode=="6000").Select(x=>x.Id).SingleAsync();
      var draft=await ClientOperationalLedgerWorkspace.CreateDraftAsync(db,maker,new(s.ClientA,periodId,"BEFORE","Prepared before control approval",new(2026,1,10),[new("1100","Unexplained receivable",10,0),new("6000","Credit",0,10)]));Assert.True(draft.Succeeded);draftId=draft.Value;
      var request=new ClientAccountRoleRequest(chartId,assetId,"AR",new(2026,1,1),null,"Explicit receivable control");
      Assert.False((await ClientAccountRoleWorkspace.ProposeAsync(db,maker,s.ClientB,request)).Succeeded);
      Assert.False((await ClientAccountRoleWorkspace.ProposeAsync(db,maker,s.ClientA,request with {AccountId=expenseId})).Succeeded);
      var proposed=await ClientAccountRoleWorkspace.ProposeAsync(db,maker,s.ClientA,request);Assert.True(proposed.Succeeded,proposed.Message);roleId=proposed.Value;
      Assert.False(await ClientAccountRoleWorkspace.UsesControlAsync(db,s.FirmId,s.ClientA,new(2026,1,10),[assetId],default));
      Assert.False((await ClientAccountRoleWorkspace.ReviewAsync(db,maker,s.ClientA,roleId,"APPROVE","Self review")).Succeeded);
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db,reviewer,s.ClientA,roleId,"APPROVE","Independent control check")).Succeeded);
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db,reviewer,s.ClientA,roleId,"APPROVE","Independent control check")).Succeeded);
      var history=await ClientAccountRoleWorkspace.ListAsync(db,reviewer,s.ClientA);Assert.True(history.Succeeded,history.Message);Assert.True(history.Value!.CanReview);Assert.Single(history.Value.Configurations);Assert.Equal("APPROVE",history.Value.Configurations[0].Decision);
      Assert.False((await ClientAccountRoleWorkspace.ListAsync(db,maker,s.ClientB)).Value!.Configurations.Any());
      Assert.True(await ClientAccountRoleWorkspace.UsesControlAsync(db,s.FirmId,s.ClientA,new(2026,1,10),[assetId],default));
      Assert.False(await ClientAccountRoleWorkspace.UsesControlAsync(db,s.FirmId,s.ClientA,new(2025,12,31),[assetId],default));
      Assert.False(await ClientAccountRoleWorkspace.UsesControlAsync(db,s.FirmId,s.ClientB,new(2026,1,10),[assetId],default));
      Assert.False((await ClientOperationalLedgerWorkspace.PreviewAsync(db,maker,s.ClientA,draftId)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.CreateDraftAsync(db,maker,new(s.ClientA,periodId,"AFTER","Unexplained control",new(2026,1,10),[new("1100","AR",10,0),new("6000","Credit",0,10)]))).Succeeded);
      var overlap=await ClientAccountRoleWorkspace.ProposeAsync(db,maker,s.ClientA,request);Assert.True(overlap.Succeeded);
      Assert.False((await ClientAccountRoleWorkspace.ReviewAsync(db,reviewer,s.ClientA,overlap.Value,"APPROVE","Overlapping control")).Succeeded);
      Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db,reviewer,s.ClientA,overlap.Value,"REJECT","Overlap refused")).Succeeded);
      Assert.Equal(0,await db.ClientOperationalJournals.CountAsync(x=>x.Status=="POSTED"));
      Assert.Equal(0,await db.FirmJournals.CountAsync());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pending = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, s.ClientA, new(chartId, expenseId, "ROUNDING", new(2026,1,1), null, "Optional explicit rounding role"));
      Assert.True(pending.Succeeded);
      var self = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO client_account_role_decisions (id,firm_id,client_id,configuration_id,decision,reason,reviewed_by_user_id,reviewed_at) VALUES ({Guid.CreateVersion7()},{s.FirmId},{s.ClientA},{pending.Value},'APPROVE','Forged self review',{s.Preparer.Id},now())"));
      Assert.Equal("23514", self.SqlState);
      var overlap = await ClientAccountRoleWorkspace.ProposeAsync(db, maker, s.ClientA, new(chartId, assetId, "AR", new(2026,1,1), null, "Overlapping SQL attempt"));
      Assert.True(overlap.Succeeded);
      var denied = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO client_account_role_decisions (id,firm_id,client_id,configuration_id,decision,reason,reviewed_by_user_id,reviewed_at) VALUES ({Guid.CreateVersion7()},{s.FirmId},{s.ClientA},{overlap.Value},'APPROVE','Forged overlap',{s.Reviewer.Id},now())"));
      Assert.Equal("23514", denied.SqlState);
      var control = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE client_operational_journals SET status='POSTED' WHERE id={draftId}"));
      Assert.Equal("23514", control.SqlState); Assert.Contains("Generic journal cannot create unexplained AR/AP", control.MessageText);
      Assert.Equal("DRAFT", (await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x => x.Id == draftId)).Status);
    }
    foreach(var sql in new[]{"UPDATE client_account_role_configurations SET role='AP' WHERE id={0}","DELETE FROM client_account_role_configurations WHERE id={0}","UPDATE client_account_role_decisions SET reason='rewrite' WHERE configuration_id={0}"})
    {
      await using var db=new AuditSphereDbContext(pg.Options); var ex=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql,roleId));Assert.Equal("23514",ex.SqlState);
    }
  }
}
