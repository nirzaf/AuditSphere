using System.Data;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientSalesInvoiceWorkflow
{
  public static async Task<CommandResult<ClientSalesInvoiceLifecycleView>> GetLifecycleAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid clientId, Guid invoiceId, CancellationToken ct=default)
  {
    await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead,ct);
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded)return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var draft=await LatestDraft(db,actor.FirmId,clientId,invoiceId,ct);
    if(draft is null)return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    var submission=await LatestSubmission(db,actor.FirmId,clientId,invoiceId,ct);
    var decision=submission is null?null:await db.ClientSalesInvoiceDecisions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.SubmissionId==submission.Id,ct);
    var journal=submission is null?null:await db.ClientOperationalJournals.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==submission.JournalId,ct);
    var openItem=await db.ClientSalesInvoiceOpenItems.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.InvoiceId==invoiceId,ct);
    ClientSalesInvoiceManifest? manifest=null;
    if(submission is not null)
    {
      if(Hash(submission.ManifestJson)!=submission.ManifestHash || journal is null)return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ProtectedState,"The retained invoice linkage is invalid.");
      try { manifest=JsonSerializer.Deserialize<ClientSalesInvoiceManifest>(submission.ManifestJson); }
      catch(JsonException) { return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ProtectedState,"The retained submission cannot be read."); }
      if(manifest is null || manifest.DraftId!=submission.DraftId || manifest.Version!="client-sales-submission-v1")return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ProtectedState,"The retained submission identity is invalid.");
    }
    var posted=decision?.Decision=="APPROVE";
    if(posted && (journal?.Status!="POSTED" || openItem?.SubmissionId!=submission?.Id || openItem?.JournalId!=journal.Id))
      return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ProtectedState,"The posted invoice and receivable origin do not reconcile.");
    if(!posted && openItem is not null)return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ProtectedState,"An unposted invoice cannot have an open item.");
    var state=submission is null?"DRAFT":posted?"POSTED":decision?.Decision=="RETURN"?(draft.Revision>submission.DraftRevision?"DRAFT":"RETURNED"):"SUBMITTED";
    var active=await db.ClientAccountingProfiles.AnyAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.SourceMode==ClientAccountingSourceModes.NativeBookkeeping,ct)
      && await ClientBookkeepingAuthorization.IsCurrentDecisionAcceptedAsync(db,actor.FirmId,clientId,ct:ct);
    var sourceDraft=submission is null?draft:await db.ClientSalesInvoiceDrafts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==submission.DraftId,ct);
    ClientSalesInvoiceDraftSnapshot? sourceSnapshot=null;
    if(sourceDraft is not null && Hash(sourceDraft.SnapshotJson)==sourceDraft.SnapshotHash)
      try { sourceSnapshot=JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(sourceDraft.SnapshotJson); } catch(JsonException) { }
    if(posted && (sourceSnapshot is null || sourceSnapshot.TaxTreatment!="NONE"))return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ProtectedState,"The posted untaxed invoice source lines cannot be verified.");
    var sourceLines=(sourceSnapshot?.Lines??[]).Select(x=>new ClientSalesInvoiceSourceLineView(x.LineNumber,x.Description,x.AccountCode,x.Net)).ToArray();
    var maker=draft.CreatedByUserId==actor.UserId;
    var canRevise=active&&maker&&(submission is null||decision?.Decision=="RETURN");
    var canSubmit=active&&maker&&state=="DRAFT";
    var canReview=active&&!maker&&state=="SUBMITTED"&&(await Authorize(db,actor,clientId,Reviewers,ct)).Succeeded;
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded)return CommandResult<ClientSalesInvoiceLifecycleView>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    await tx.CommitAsync(ct);
    // No allocation commands exist yet; the immutable origin is the outstanding amount.
    // The settlement slice must replace this projection with ledger-backed allocations.
    return CommandResult<ClientSalesInvoiceLifecycleView>.Ok(new(invoiceId,clientId,draft.Revision.ToString(CultureInfo.InvariantCulture),state,posted,false,"NOT_REQUESTED",active,canRevise,canSubmit,canReview,
      submission?.Id,journal?.Id,journal?.Revision.ToString(CultureInfo.InvariantCulture),draft.CreatedByUserId,decision?.Decision,decision?.Reason,submission?.ManifestHash,manifest,
      openItem is null?null:Exact(openItem.OriginalAmount),openItem?.DueDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),draft.PeriodId,draft.Currency,sourceLines));
  }

  public static async Task<CommandResult<ClientSalesInvoiceCommandReceipt>> GetCommandAsync(IClientAccountingDbContext db,ActorContext actor,
    Guid clientId,Guid commandId,string kind,CancellationToken ct=default)
  {
    if(kind is not ("SUBMIT" or "REVIEW")||commandId==Guid.Empty)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid,"Choose the exact invoice command kind.");
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    ClientSalesInvoiceCommandReceipt? receipt=null;
    if(kind=="SUBMIT")
    {
      var row=await db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.CreatedByUserId==actor.UserId&&x.CommandId==commandId,ct);
      if(row is not null)receipt=Receipt(row);
    }
    else
    {
      var row=await db.ClientSalesInvoiceDecisions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.ActorUserId==actor.UserId&&x.CommandId==commandId,ct);
      if(row is not null)
      {
        var source=await db.ClientSalesInvoiceSubmissions.AsNoTracking().SingleAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==row.SubmissionId,ct);
        receipt=Receipt(row,source);
      }
    }
    if(!(await Authorize(db,actor,clientId,Preparers,ct)).Succeeded)return CommandResult<ClientSalesInvoiceCommandReceipt>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    return receipt is null?CommandResult<ClientSalesInvoiceCommandReceipt>.Fail("command.receipt-not-found","No invoice outcome was found for this actor and request."):CommandResult<ClientSalesInvoiceCommandReceipt>.Ok(receipt);
  }
}
