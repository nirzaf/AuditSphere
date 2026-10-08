using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Globalization;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  private static async Task<(Guid Period, Guid Role, ClientSalesInvoiceDraftRequest Request, ClientSalesInvoiceDraftView Draft)> SeedSalesWorkflow(PgTestSchema pg,Scope s,int incomeAccounts=1)
  {
    var maker=Actor(s.Preparer,"AccountingPreparer");var reviewer=Actor(s.Reviewer,"AccountingReviewer");
    await using var db=new AuditSphereDbContext(pg.Options);
    db.AcceptanceDecisions.Add(new(){Id=Guid.CreateVersion7(),FirmId=s.FirmId,PracticeClientId=s.ClientA,ServiceRoute="BOOKKEEPING",Decision="Accepted",Generation=1,
      Rationale="Synthetic invoice service",EvaluationTemplateVersion="TEST-1",EvaluationSnapshotDigest=new string('f',64),DecidedByUserId=s.Reviewer.Id,DecidedAt=DateTimeOffset.UtcNow});
    await db.SaveChangesAsync();
    Assert.True((await ClientAccountingService.CreateProfileAsync(db,reviewer,new(s.ClientA,"QA","QAR",1,1,"AUDITSPHERE","NATIVE",ClientAccountingSourceModes.NativeBookkeeping))).Succeeded);
    var period=await ClientAccountingService.CreatePeriodAsync(db,maker,new(s.ClientA,"2026",new(2026,1,1),new(2026,12,31),"STATUTORY","QAR"));Assert.True(period.Succeeded,period.Message);
    var chart=await ClientAccountingService.CreateChartVersionAsync(db,maker,s.ClientA,"AUDITSPHERE",new(2026,1,1));Assert.True(chart.Succeeded,chart.Message);
    var accounts=new List<ClientAccountInput>{new("cash","1000","Cash","ASSET","DEBIT",true),new("ar","1100","Receivables","ASSET","DEBIT",true)};
    for(var i=0;i<incomeAccounts;i++)accounts.Add(new("income-"+i,(4000+i).ToString(),"Revenue "+i,"INCOME","CREDIT",true));
    Assert.True((await ClientAccountingService.AddAccountsAsync(db,maker,chart.Value,accounts)).Succeeded);
    Assert.True((await ClientAccountingService.PublishChartVersionAsync(db,reviewer,chart.Value)).Succeeded);
    var ar=await db.ClientAccounts.SingleAsync(x=>x.ChartVersionId==chart.Value&&x.AccountCode=="1100");
    var role=await ClientAccountRoleWorkspace.ProposeAsync(db,maker,s.ClientA,new(chart.Value,ar.Id,"AR",new(2026,1,1),null,"Reviewed control"));Assert.True(role.Succeeded,role.Message);
    Assert.True((await ClientAccountRoleWorkspace.ReviewAsync(db,reviewer,s.ClientA,role.Value,"APPROVE","Independent control")).Succeeded);
    var party=await ClientBookkeepingCounterpartyWorkspace.CreateAsync(db,maker,s.ClientA,new("Customer","Customer","CUSTOMER","Client address","QA","","","","","TEST-SYSTEM","CUSTOMER-1"));Assert.True(party.Succeeded,party.Message);
    var request=new ClientSalesInvoiceDraftRequest(Guid.CreateVersion7(),null,0,"DRAFT-1","SOURCE-1",period.Value,party.Value,new(2026,1,10),new(2026,1,10),new(2026,1,10),new(2026,2,10),"QAR",
      new("QAR",2,"AWAY_FROM_ZERO","REJECT",0,""),Enumerable.Range(0,incomeAccounts).Select(i=>new ClientInvoiceLineInput("Service "+i,(4000+i).ToString(),1,125.125m,.125m,"NONE",[])).ToArray(),"Client preparation reference");
    var draft=await ClientSalesInvoiceDraftWorkspace.SaveAsync(db,maker,s.ClientA,request);Assert.True(draft.Succeeded,draft.Message);
    return(period.Value,role.Value,request,draft.Value!);
  }

  [Fact]
  public async Task SalesInvoiceIndependentPostIsAtomicScopedRecoverableAndReconcilesWithNativeGL()
  {
    await using var pg=await PgTestSchema.CreateAsync();var s=await SeedAsync(pg);var f=await SeedSalesWorkflow(pg,s);
    var maker=Actor(s.Preparer,"AccountingPreparer");var reviewer=Actor(s.Reviewer,"AccountingReviewer");
    var submit=new ClientSalesInvoiceSubmitRequest(Guid.CreateVersion7(),f.Draft.InvoiceId,1,f.Role,"",null,"Client-approved service record","");
    ClientSalesInvoiceCommandReceipt submitted;
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await ClientSalesInvoiceWorkflow.PreviewAsync(db,maker,s.ClientB,submit)).Succeeded);
      Assert.True((await ClientSalesInvoiceWorkflow.PreviewAsync(db,maker,s.ClientA,submit with{NoTaxReason=""})).Succeeded);
      var preview=await ClientSalesInvoiceWorkflow.PreviewAsync(db,maker,s.ClientA,submit);Assert.True(preview.Succeeded,preview.Message);
      Assert.Equal("125.00",preview.Value!.Gross);Assert.Equal("125.000000",preview.Value.Manifest.Lines[0].Debit);
      submit=submit with{PreviewDigest=preview.Value.Digest};
      var result=await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,submit);Assert.True(result.Succeeded,result.Message);submitted=result.Value!;
      Assert.Equal(submitted,(await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,submit)).Value);
      Assert.False((await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,submit with{SourceBasis="Changed"})).Succeeded);
      Assert.False((await ClientSalesInvoiceDraftWorkspace.SaveAsync(db,maker,s.ClientA,f.Request with{CommandId=Guid.CreateVersion7(),InvoiceId=f.Draft.InvoiceId,ExpectedRevision=1})).Succeeded);
      var lifecycle=await ClientSalesInvoiceWorkflow.GetLifecycleAsync(db,reviewer,s.ClientA,f.Draft.InvoiceId);Assert.True(lifecycle.Succeeded,lifecycle.Message);
      Assert.Equal("SUBMITTED",lifecycle.Value!.State);Assert.True(lifecycle.Value.CanReview);Assert.False(lifecycle.Value.Posted);Assert.False(lifecycle.Value.Issued);Assert.Equal("NOT_REQUESTED",lifecycle.Value.DeliveryState);
      Assert.Equal(0,await db.ClientSalesInvoiceOpenItems.CountAsync());Assert.Equal(0,(await ClientOperationalGeneralLedgerWorkspace.GetAsync(db,maker,s.ClientA,f.Period)).Value!.TotalEntries);
      Assert.False((await ClientOperationalLedgerWorkspace.PreviewAsync(db,reviewer,s.ClientA,lifecycle.Value.JournalId!.Value)).Succeeded);
      Assert.False((await ClientOperationalLedgerWorkspace.ReturnAsync(db,reviewer,s.ClientA,lifecycle.Value.JournalId.Value,2,"Bypass invoice review")).Succeeded);
      Assert.False((await ClientSalesInvoiceWorkflow.ReviewAsync(db,maker,s.ClientA,new(Guid.CreateVersion7(),submitted.SubmissionId,"APPROVE","Self",""))).Succeeded);
    }
    ClientSalesInvoiceReviewRequest review;
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      var preview=await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db,reviewer,s.ClientA,submitted.SubmissionId);Assert.True(preview.Succeeded,preview.Message);Assert.True(preview.Value!.CanPost);
      review=new(Guid.CreateVersion7(),submitted.SubmissionId,"APPROVE","Independent invoice review",preview.Value.Digest);
    }
    async Task<ClientSalesInvoiceCommandReceipt?> Post()
    {
      await using var db=new AuditSphereDbContext(pg.Options);var result=await ClientSalesInvoiceWorkflow.ReviewAsync(db,reviewer,s.ClientA,review);Assert.True(result.Succeeded,result.Message);return result.Value;
    }
    var competing=await Task.WhenAll(Post(),Post());Assert.Equal(competing[0],competing[1]);
    await using(var db=new AuditSphereDbContext(pg.Options))
    {
      Assert.False((await ClientSalesInvoiceWorkflow.ReviewAsync(db,reviewer,s.ClientA,review with{Reason="Different intent"})).Succeeded);
      Assert.Equal(competing[0],(await ClientSalesInvoiceWorkflow.GetCommandAsync(db,reviewer,s.ClientA,review.CommandId,"REVIEW")).Value);
      Assert.False((await ClientSalesInvoiceWorkflow.GetCommandAsync(db,maker,s.ClientA,review.CommandId,"REVIEW")).Succeeded);
      Assert.False((await ClientSalesInvoiceWorkflow.GetLifecycleAsync(db,reviewer,s.ClientB,f.Draft.InvoiceId)).Succeeded);
      var lifecycle=(await ClientSalesInvoiceWorkflow.GetLifecycleAsync(db,reviewer,s.ClientA,f.Draft.InvoiceId)).Value!;
      Assert.Equal("POSTED",lifecycle.State);Assert.True(lifecycle.Posted);Assert.False(lifecycle.Issued);Assert.Equal("125.000000",lifecycle.OpenAmount);
      Assert.False(lifecycle.CanRevise);Assert.False(lifecycle.CanReview);Assert.False(lifecycle.CanSubmit);
      var gl=(await ClientOperationalGeneralLedgerWorkspace.GetAsync(db,reviewer,s.ClientA,f.Period)).Value!;
      Assert.Equal(2,gl.TotalEntries);Assert.Equal(125m,decimal.Parse(gl.TrialBalance.ClosingDebit,CultureInfo.InvariantCulture));Assert.Equal(125m,decimal.Parse(gl.TrialBalance.ClosingCredit,CultureInfo.InvariantCulture));
      Assert.Equal(125m,decimal.Parse(gl.Accounts.Single(x=>x.AccountCode=="1100").DebitMovement,CultureInfo.InvariantCulture));
      Assert.Equal(125m,decimal.Parse(gl.Accounts.Single(x=>x.AccountCode=="4000").CreditMovement,CultureInfo.InvariantCulture));
      Assert.Single(await db.ClientSalesInvoiceDecisions.ToListAsync());Assert.Single(await db.ClientOperationalPostingReceipts.ToListAsync());Assert.Single(await db.ClientSalesInvoiceOpenItems.ToListAsync());Assert.Empty(await db.FirmJournals.ToListAsync());
      Assert.False((await ClientOperationalLedgerWorkspace.CreateReversalAsync(db,maker,s.ClientA,lifecycle.JournalId!.Value,new(long.Parse(lifecycle.JournalRevision!),f.Period,"REV-1",new(2026,1,20),"Bypass","Evidence"))).Succeeded);
      var submissionDelete=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM client_sales_invoice_submissions"));Assert.Equal("23514",submissionDelete.SqlState);
      var decisionDelete=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM client_sales_invoice_decisions"));Assert.Equal("23514",decisionDelete.SqlState);
      var itemDelete=await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("DELETE FROM client_sales_invoice_open_items"));Assert.Equal("23514",itemDelete.SqlState);
    }
  }

  [Fact]
  public async Task SalesInvoiceReturnRequiresNewDraftAndPreservesExactPriorSubmissions()
  {
    await using var pg=await PgTestSchema.CreateAsync();var s=await SeedAsync(pg);var f=await SeedSalesWorkflow(pg,s);
    var maker=Actor(s.Preparer,"AccountingPreparer");var reviewer=Actor(s.Reviewer,"AccountingReviewer");
    await using var db=new AuditSphereDbContext(pg.Options);
    var submit=new ClientSalesInvoiceSubmitRequest(Guid.CreateVersion7(),f.Draft.InvoiceId,1,f.Role,"Untaxed synthetic service",null,"Service record","");
    var p=await ClientSalesInvoiceWorkflow.PreviewAsync(db,maker,s.ClientA,submit);Assert.True(p.Succeeded,p.Message);submit=submit with{PreviewDigest=p.Value!.Digest};
    var first=await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,submit);Assert.True(first.Succeeded,first.Message);
    var preview=await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db,reviewer,s.ClientA,first.Value!.SubmissionId);Assert.True(preview.Succeeded,preview.Message);
    var returned=await ClientSalesInvoiceWorkflow.ReviewAsync(db,reviewer,s.ClientA,new(Guid.CreateVersion7(),first.Value.SubmissionId,"RETURN","Clarify description",preview.Value!.Digest));Assert.True(returned.Succeeded,returned.Message);
    var lifecycle=(await ClientSalesInvoiceWorkflow.GetLifecycleAsync(db,maker,s.ClientA,f.Draft.InvoiceId)).Value!;
    Assert.Equal("RETURNED",lifecycle.State);Assert.True(lifecycle.CanRevise);Assert.False(lifecycle.CanSubmit);
    Assert.False((await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,submit with{CommandId=Guid.CreateVersion7()})).Succeeded);
    var revised=await ClientSalesInvoiceDraftWorkspace.SaveAsync(db,maker,s.ClientA,f.Request with{CommandId=Guid.CreateVersion7(),InvoiceId=f.Draft.InvoiceId,ExpectedRevision=1,Lines=[new("Clarified","4000",1,50.25m,0,"NONE",[])]});Assert.True(revised.Succeeded,revised.Message);
    var next=submit with{CommandId=Guid.CreateVersion7(),ExpectedDraftRevision=2,PreviewDigest=""};p=await ClientSalesInvoiceWorkflow.PreviewAsync(db,maker,s.ClientA,next);Assert.True(p.Succeeded,p.Message);
    var second=await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,next with{PreviewDigest=p.Value!.Digest});Assert.True(second.Succeeded,second.Message);
    Assert.Equal(2,await db.ClientSalesInvoiceSubmissions.CountAsync());Assert.Single(await db.ClientOperationalJournals.ToListAsync());Assert.Equal(2,await db.ClientOperationalJournalSnapshots.CountAsync());
    var journalId=(await ClientSalesInvoiceWorkflow.GetLifecycleAsync(db,reviewer,s.ClientA,f.Draft.InvoiceId)).Value!.JournalId!.Value;
    var snapshots=await ClientOperationalLedgerWorkspace.GetSnapshotsAsync(db,reviewer,s.ClientA,journalId);Assert.True(snapshots.Succeeded,snapshots.Message);
    Assert.Equal("125.000000",snapshots.Value![0].Lines[0].Debit);Assert.Equal("50.250000",snapshots.Value[1].Lines[0].Debit);
    var sourceOrigin=Assert.Single(snapshots.Value[0].SourceOrigins!);
    Assert.Equal("SALES_INVOICE", sourceOrigin.SourceKind);
    Assert.Equal(f.Draft.InvoiceId, sourceOrigin.SourceId);
    Assert.Equal("1", sourceOrigin.SourceRevision);
    Assert.Matches("^[a-f0-9]{64}$", sourceOrigin.ManifestSha256!);
    Assert.Matches("^[a-f0-9]{64}$", sourceOrigin.IntentSha256!);
    var duplicate=await ClientSalesInvoiceDraftWorkspace.SaveAsync(db,maker,s.ClientA,f.Request with{CommandId=Guid.CreateVersion7(),DraftReference="DUPLICATE"});Assert.True(duplicate.Succeeded,duplicate.Message);
    var duplicateRequest=submit with{CommandId=Guid.CreateVersion7(),InvoiceId=duplicate.Value!.InvoiceId,PreviewDigest=""};p=await ClientSalesInvoiceWorkflow.PreviewAsync(db,maker,s.ClientA,duplicateRequest);Assert.True(p.Succeeded,p.Message);
    Assert.False((await ClientSalesInvoiceWorkflow.SubmitAsync(db,maker,s.ClientA,duplicateRequest with{PreviewDigest=p.Value!.Digest})).Succeeded);
  }
}
