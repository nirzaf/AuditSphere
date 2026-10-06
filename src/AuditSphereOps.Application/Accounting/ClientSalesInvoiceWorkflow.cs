using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientSalesInvoiceCommandReceipt(Guid CommandId, Guid InvoiceId, Guid SubmissionId, string Kind,
  Guid ActorUserId, string IntentHash, string Outcome, Guid? DecisionId);
public sealed record ClientSalesInvoiceReviewPreview(Guid InvoiceId, Guid SubmissionId, Guid JournalId, string JournalRevision,
  string Digest, bool CanPost, string? PostingBlock, string ReviewContextJson, ClientSalesInvoiceManifest Manifest);

public static partial class ClientSalesInvoiceWorkflow
{
  private static async Task<bool> LockClient(IClientAccountingDbContext db,ActorContext actor,Guid clientId,CancellationToken ct)=>
    await db.PracticeClients.FromSqlInterpolated($"SELECT * FROM practice_clients WHERE firm_id={actor.FirmId} AND id={clientId} FOR UPDATE").AnyAsync(ct);
  private static Task<ClientSalesInvoiceDraft?> LatestDraft(IClientAccountingDbContext db,Guid firmId,Guid clientId,Guid invoiceId,CancellationToken ct)=>
    db.ClientSalesInvoiceDrafts.AsNoTracking().Where(x=>x.FirmId==firmId&&x.ClientId==clientId&&x.InvoiceId==invoiceId).OrderByDescending(x=>x.Revision).FirstOrDefaultAsync(ct);
  private static Task<ClientSalesInvoiceSubmission?> LatestSubmission(IClientAccountingDbContext db,Guid firmId,Guid clientId,Guid invoiceId,CancellationToken ct)=>
    db.ClientSalesInvoiceSubmissions.AsNoTracking().Where(x=>x.FirmId==firmId&&x.ClientId==clientId&&x.InvoiceId==invoiceId).OrderByDescending(x=>x.DraftRevision).FirstOrDefaultAsync(ct);
  private static ClientSalesInvoiceCommandReceipt Receipt(ClientSalesInvoiceSubmission s)=>new(s.CommandId,s.InvoiceId,s.Id,"SUBMIT",s.CreatedByUserId,s.IntentHash,"SUBMITTED",null);
  private static ClientSalesInvoiceCommandReceipt Receipt(ClientSalesInvoiceDecision d,ClientSalesInvoiceSubmission s)=>new(d.CommandId,s.InvoiceId,s.Id,"REVIEW",d.ActorUserId,d.IntentHash,d.Decision=="APPROVE"?"POSTED":"RETURNED",d.Id);

  public static async Task<CommandResult<ClientSalesInvoicePreview>> PreviewAsync(IClientAccountingDbContext db,ActorContext actor,Guid clientId,ClientSalesInvoiceSubmitRequest request,CancellationToken ct=default)
  {
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded || !await LockClient(db,actor,clientId,ct))return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var draft=await LatestDraft(db,actor.FirmId,clientId,request.InvoiceId,ct);
    if(draft is null || draft.CreatedByUserId!=actor.UserId)return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ScopeDenied,"Only the invoice maker may prepare submission.");
    var latest=await LatestSubmission(db,actor.FirmId,clientId,request.InvoiceId,ct);
    if(latest is not null && !await db.ClientSalesInvoiceDecisions.AnyAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.SubmissionId==latest.Id&&x.Decision=="RETURN",ct))return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"Submitted or posted invoice content is frozen.");
    var result=await BuildPreviewAsync(db,actor,clientId,draft,request,null,ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded)return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    await tx.CommitAsync(ct);return result;
  }

  public static async Task<CommandResult<ClientSalesInvoiceCommandReceipt>> SubmitAsync(IClientAccountingDbContext db,ActorContext actor,Guid clientId,ClientSalesInvoiceSubmitRequest request,CancellationToken ct=default)
  {
    var r=Normalize(request);if(r.CommandId==Guid.Empty)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict,"A stable submission command is required.");
    var intent=Intent(actor,clientId,r);await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded || !await LockClient(db,actor,clientId,ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var replay=await db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.CreatedByUserId==actor.UserId&&x.CommandId==r.CommandId,ct);
    if(replay is not null)return replay.IntentHash==intent?CommandResult<ClientSalesInvoiceCommandReceipt>.Ok(Receipt(replay)):CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict,"The submission key is bound to different content.");
    var draft=await LatestDraft(db,actor.FirmId,clientId,r.InvoiceId,ct);
    if(draft is null || draft.CreatedByUserId!=actor.UserId)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ScopeDenied,"Only the invoice maker may submit.");
    var previous=await LatestSubmission(db,actor.FirmId,clientId,r.InvoiceId,ct);
    if(previous is not null && (!await db.ClientSalesInvoiceDecisions.AnyAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.SubmissionId==previous.Id&&x.Decision=="RETURN",ct) || draft.Revision<=previous.DraftRevision))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ProtectedState,"Return and revise the exact invoice before resubmission.");
    var preview=await BuildPreviewAsync(db,actor,clientId,draft,r,null,ct);
    if(!preview.Succeeded || preview.Value!.Digest!=r.PreviewDigest)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.StaleRevision,"Preview the current exact invoice, receivable role, source basis and declared calculation policy before submission.");
    var snapshot=JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(draft.SnapshotJson)!;
    ClientOperationalJournal journal;
    if(previous is null)
    {
      journal=new(){Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,PeriodId=draft.PeriodId,JournalNumber="CLIENT-SALES-"+draft.InvoiceId.ToString("N"),Description="Client sales invoice "+draft.DraftReference,PostingDate=DateOnly.ParseExact(snapshot.AccountingDate,"yyyy-MM-dd",CultureInfo.InvariantCulture),Currency=draft.Currency,CreatedByUserId=actor.UserId,CreatedAt=DateTimeOffset.UtcNow};
      db.ClientOperationalJournals.Add(journal);await db.SaveChangesAsync(ct);
    }
    else
    {
      journal=await db.ClientOperationalJournals.FromSqlInterpolated($"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={previous.JournalId} FOR UPDATE").SingleAsync(ct);
      if(journal.Status!="RETURNED" || journal.CreatedByUserId!=actor.UserId)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.StaleRevision,"The linked journal is no longer returned to this maker.");
      journal.Status="DRAFT";journal.Revision++;await db.SaveChangesAsync(ct);
      journal.PeriodId=draft.PeriodId;journal.PostingDate=DateOnly.ParseExact(snapshot.AccountingDate,"yyyy-MM-dd",CultureInfo.InvariantCulture);journal.Currency=draft.Currency;journal.Description="Client sales invoice "+draft.DraftReference;
      await db.ClientOperationalJournalLines.Where(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.JournalId==journal.Id).ExecuteDeleteAsync(ct);
    }
    foreach(var line in preview.Value.Manifest.Lines)db.ClientOperationalJournalLines.Add(new(){Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,JournalId=journal.Id,LineNumber=line.LineNumber,ClientAccountId=line.AccountId,AccountCode=line.AccountCode,AccountName=line.AccountName,Description=line.Description,Debit=decimal.Parse(line.Debit,CultureInfo.InvariantCulture),Credit=decimal.Parse(line.Credit,CultureInfo.InvariantCulture)});
    await db.SaveChangesAsync(ct);journal.Status="SUBMITTED";journal.SubmittedAt=DateTimeOffset.UtcNow;journal.Revision++;await db.SaveChangesAsync(ct);
    var json=JsonSerializer.Serialize(preview.Value.Manifest);
    var submission=new ClientSalesInvoiceSubmission{Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,InvoiceId=draft.InvoiceId,DraftId=draft.Id,DraftRevision=draft.Revision,JournalId=journal.Id,JournalSubmittedRevision=journal.Revision,CommandId=r.CommandId,IntentHash=intent,ManifestJson=json,ManifestHash=Hash(json),CreatedByUserId=actor.UserId,CreatedAt=DateTimeOffset.UtcNow};
    db.ClientSalesInvoiceSubmissions.Add(submission);await db.SaveChangesAsync(ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db,actor.FirmId,clientId,ct:ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.GateBlocked,"Authority changed before submission.");
    await tx.CommitAsync(ct);return CommandResult<ClientSalesInvoiceCommandReceipt>.Ok(Receipt(submission));
  }

  private static async Task<CommandResult<ClientSalesInvoiceReviewPreview>> ReviewPreviewCore(IClientAccountingDbContext db,ActorContext actor,Guid clientId,ClientSalesInvoiceSubmission source,CancellationToken ct)
  {
    if(Hash(source.ManifestJson)!=source.ManifestHash)return CommandResult<ClientSalesInvoiceReviewPreview>.Fail(ErrorCodes.ProtectedState,"Submission identity is invalid.");
    var m=JsonSerializer.Deserialize<ClientSalesInvoiceManifest>(source.ManifestJson)!;
    var latest=await LatestSubmission(db,actor.FirmId,clientId,source.InvoiceId,ct);
    var journal=await db.ClientOperationalJournals.AsNoTracking().SingleAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==source.JournalId,ct);
    if(latest?.Id!=source.Id || journal.Status!="SUBMITTED" || journal.Revision!=source.JournalSubmittedRevision)return CommandResult<ClientSalesInvoiceReviewPreview>.Fail(ErrorCodes.StaleRevision,"Read the current submitted invoice revision.");
    var draft=await db.ClientSalesInvoiceDrafts.AsNoTracking().SingleAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==source.DraftId,ct);
    var fresh=await BuildPreviewAsync(db,actor,clientId,draft,new(Guid.Empty,source.InvoiceId,source.DraftRevision,m.ReceivableRoleId,m.NoTaxReason,m.SourceReceiptId,m.SourceBasis,""),source.Id,ct);
    var accepted=await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db,actor.FirmId,clientId,ct:ct);
    var context=JsonSerializer.Serialize(new{Version="client-sales-review-v1",actor.FirmId,ClientId=clientId,ActorId=actor.UserId,source.InvoiceId,SubmissionId=source.Id,source.ManifestHash,JournalId=journal.Id,JournalRevision=journal.Revision,JournalStatus=journal.Status,CurrentPreview=fresh.Value?.Digest,PostingBlock=fresh.ErrorCode,AcceptedService=accepted});
    return CommandResult<ClientSalesInvoiceReviewPreview>.Ok(new(source.InvoiceId,source.Id,journal.Id,journal.Revision.ToString(CultureInfo.InvariantCulture),Hash(context),fresh.Succeeded&&accepted,fresh.ErrorCode,context,m));
  }

  public static async Task<CommandResult<ClientSalesInvoiceReviewPreview>> ReviewPreviewAsync(IClientAccountingDbContext db,ActorContext actor,Guid clientId,Guid submissionId,CancellationToken ct=default)
  {
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded || !await LockClient(db,actor,clientId,ct))return CommandResult<ClientSalesInvoiceReviewPreview>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var source=await db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==submissionId,ct);
    if(source is null)return CommandResult<ClientSalesInvoiceReviewPreview>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var result=await ReviewPreviewCore(db,actor,clientId,source,ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded)return CommandResult<ClientSalesInvoiceReviewPreview>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    await tx.CommitAsync(ct);return result;
  }

  public static async Task<CommandResult<ClientSalesInvoiceCommandReceipt>> ReviewAsync(IClientAccountingDbContext db,ActorContext actor,Guid clientId,ClientSalesInvoiceReviewRequest request,CancellationToken ct=default)
  {
    var r=request with {Decision=(request.Decision??"").Trim().ToUpperInvariant(),Reason=(request.Reason??"").Trim()};
    if(r.CommandId==Guid.Empty || r.Decision is not ("APPROVE" or "RETURN") || r.Reason.Length is 0 or >2000)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid,"A stable exact independent review command and reason are required.");
    var intent=Intent(actor,clientId,r);await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(!(await Authorize(db,actor,clientId,Reviewers,ct)).Succeeded || !await LockClient(db,actor,clientId,ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var replay=await db.ClientSalesInvoiceDecisions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.ActorUserId==actor.UserId&&x.CommandId==r.CommandId,ct);
    if(replay is not null)
    {
      if(replay.IntentHash!=intent)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict,"The review key is bound to different intent.");
      var prior=await db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==replay.SubmissionId,ct);return CommandResult<ClientSalesInvoiceCommandReceipt>.Ok(Receipt(replay,prior));
    }
    var source=await db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==r.SubmissionId,ct);
    if(source is null || source.CreatedByUserId==actor.UserId)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ScopeDenied,"An independent assigned reviewer is required.");
    var preview=await ReviewPreviewCore(db,actor,clientId,source,ct);
    if(!preview.Succeeded || preview.Value!.Digest!=r.PreviewDigest || (r.Decision=="APPROVE"&&!preview.Value.CanPost))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.StaleRevision,"Preview the exact submitted invoice and current posting context before deciding.");
    if(!await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db,actor.FirmId,clientId,ct:ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.GateBlocked,"Current accepted bookkeeping service is required.");
    var journal=await db.ClientOperationalJournals.FromSqlInterpolated($"SELECT * FROM client_operational_journals WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={source.JournalId} FOR UPDATE").SingleAsync(ct);
    if(await db.ClientSalesInvoiceDecisions.AnyAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.SubmissionId==source.Id,ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict,"This submitted invoice already has a decision.");
    if(r.Decision=="APPROVE" && await db.ClientOperationalPostingReceipts.AnyAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.CommandId==r.CommandId,ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.IdempotencyConflict,"The posting command key already exists.");
    var now=DateTimeOffset.UtcNow;
    var decision=new ClientSalesInvoiceDecision{Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,SubmissionId=source.Id,CommandId=r.CommandId,IntentHash=intent,Decision=r.Decision,Reason=r.Reason,PreviewDigest=r.PreviewDigest,ReviewContextJson=preview.Value.ReviewContextJson,ActorUserId=actor.UserId,CreatedAt=now};
    db.ClientSalesInvoiceDecisions.Add(decision);db.ClientOperationalJournalDecisions.Add(new(){Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,JournalId=journal.Id,JournalRevision=source.JournalSubmittedRevision,Decision=r.Decision,Reason=r.Reason,ActorUserId=actor.UserId,CreatedAt=now});await db.SaveChangesAsync(ct);
    journal.Status=r.Decision=="APPROVE"?"POSTED":"RETURNED";journal.Revision++;
    if(r.Decision=="APPROVE"){journal.PostedByUserId=actor.UserId;journal.PostedAt=now;}
    await db.SaveChangesAsync(ct);
    if(r.Decision=="APPROVE")
    {
      var draft=await db.ClientSalesInvoiceDrafts.AsNoTracking().SingleAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==source.DraftId,ct);
      var snapshot=JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(draft.SnapshotJson)!;
      db.ClientSalesInvoiceOpenItems.Add(new(){Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,InvoiceId=source.InvoiceId,SubmissionId=source.Id,JournalId=journal.Id,CustomerId=draft.CustomerId,Currency=draft.Currency,OriginalAmount=draft.GrossAmount,DueDate=DateOnly.ParseExact(snapshot.DueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture),PostedAt=now});
      db.ClientOperationalPostingReceipts.Add(new(){Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=clientId,CommandId=r.CommandId,JournalId=journal.Id,ActorUserId=actor.UserId,SubmittedRevision=source.JournalSubmittedRevision,PostedRevision=journal.Revision,IntentHash=intent,PreviewDigest=r.PreviewDigest,RecordedAt=now});await db.SaveChangesAsync(ct);
    }
    if(!(await Authorize(db,actor,clientId,Reviewers,ct)).Succeeded || !await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db,actor.FirmId,clientId,ct:ct))return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.GateBlocked,"Authority changed before review committed.");
    await tx.CommitAsync(ct);return CommandResult<ClientSalesInvoiceCommandReceipt>.Ok(Receipt(decision,source));
  }
}
