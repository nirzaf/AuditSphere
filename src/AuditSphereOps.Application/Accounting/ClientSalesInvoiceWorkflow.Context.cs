using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ClientSalesInvoiceSubmitRequest(Guid CommandId, Guid InvoiceId, long ExpectedDraftRevision, Guid ReceivableRoleId,
  string NoTaxReason, Guid? SourceReceiptId, string SourceBasis, string PreviewDigest);
public sealed record ClientSalesInvoiceReviewRequest(Guid CommandId, Guid SubmissionId, string Decision, string Reason, string PreviewDigest);
public sealed record ClientSalesInvoiceManifest(string Version, Guid DraftId, string DraftHash, Guid ProfileId, string ProfileRevision,
  Guid ReceivableRoleId, Guid ReceivableDecisionId, Guid ReceivableAccountId, string NoTaxReason, Guid? SourceReceiptId,
  string? SourceReceiptHash, string SourceBasis, Guid MandateId, string MandateGeneration, IReadOnlyList<ClientOperationalJournalLineView> Lines);
public sealed record ClientSalesInvoicePreview(Guid InvoiceId, Guid DraftId, string DraftRevision, Guid? SubmissionId, string Currency,
  string Gross, string Digest, ClientSalesInvoiceManifest Manifest);
public sealed record ClientSalesInvoiceLifecycleView(Guid InvoiceId, Guid ClientId, string DraftRevision, string State, bool Posted, bool Issued, string DeliveryState,
  bool BookkeepingActive, bool CanRevise, bool CanSubmit, bool CanReview,
  Guid? SubmissionId, Guid? JournalId, string? JournalRevision, Guid? MakerId, string? Decision, string? DecisionReason,
  string? ManifestHash, ClientSalesInvoiceManifest? Manifest, string? OpenAmount, string? DueDate);

public static partial class ClientSalesInvoiceWorkflow
{
  private static readonly string[] Preparers=["AccountingPreparer","AccountingReviewer","Manager","Partner","Administrator"];
  private static readonly string[] Reviewers=["AccountingReviewer","Manager","Partner","Administrator"];
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db,ActorContext actor,Guid clientId,string[] roles,CancellationToken ct)=>
    AuthorizationDecision.AuthorizeAsync(db,actor,new(actor.FirmId,clientId,RequiredRoles:roles,InternalOnly:true),ct);
  private static string Hash(string s)=>Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
  private static string Exact(decimal n,int precision=6)=>n.ToString("F"+precision,CultureInfo.InvariantCulture);
  private static bool DecimalInput(string value,out decimal n)=>decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out n);
  private static ClientSalesInvoiceSubmitRequest Normalize(ClientSalesInvoiceSubmitRequest r)=>r with {NoTaxReason=(r.NoTaxReason??"").Trim(),SourceBasis=(r.SourceBasis??"").Trim()};
  private static string Intent(ActorContext actor,Guid clientId,object request)=>Hash(JsonSerializer.Serialize(new{Version="client-sales-command-v1",actor.FirmId,clientId,actor.UserId,Request=request}));

  private static async Task<CommandResult<ClientSalesInvoicePreview>> BuildPreviewAsync(IClientAccountingDbContext db,ActorContext actor,Guid clientId,
    ClientSalesInvoiceDraft draft,ClientSalesInvoiceSubmitRequest request,Guid? submissionId,CancellationToken ct)
  {
    var r=Normalize(request);
    if(r.NoTaxReason.Length>2000 || r.SourceBasis.Length>2000 || (r.SourceReceiptId is null && r.SourceBasis.Length==0) || r.SourceReceiptId==Guid.Empty)
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.Accounting.MappingInvalid,"Select a scoped source receipt or explain the source basis; optional no-tax context may be recorded when useful.");
    if(draft.Revision!=r.ExpectedDraftRevision || draft.InvoiceId!=r.InvoiceId || Hash(draft.SnapshotJson)!=draft.SnapshotHash)
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.StaleRevision,"Read the exact latest invoice preparation.");
    ClientSalesInvoiceDraftSnapshot? saved;
    try { saved=JsonSerializer.Deserialize<ClientSalesInvoiceDraftSnapshot>(draft.SnapshotJson); }
    catch(JsonException) { return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"The retained invoice preparation cannot be read."); }
    if(saved is null || saved.Policy is null || saved.Seller is null || saved.Customer is null || saved.Seller.ClientId!=clientId || saved.Customer.ClientId!=clientId || saved.Customer.Id!=draft.CustomerId || saved.Version!="client-sales-draft-v1" || saved.Currency!=draft.Currency || saved.Policy.DecimalPlaces is <0 or >6 || saved.Lines is null || saved.Lines.Count is <1 or >100 || saved.Lines.Any(x=>x is null) || saved.TaxTreatment!="NONE")
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"The retained invoice preparation is unsupported.");
    var profile=await db.ClientAccountingProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId,ct);
    var mandate=await db.AcceptanceDecisions.AsNoTracking().Where(x=>x.FirmId==actor.FirmId&&x.PracticeClientId==clientId&&x.EngagementId==null&&x.ServiceRoute=="BOOKKEEPING")
      .OrderByDescending(x=>x.Generation).ThenByDescending(x=>x.DecidedAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
    if(profile is null || profile.SourceMode!=ClientAccountingSourceModes.NativeBookkeeping || profile.Id!=saved.ProfileId || profile.Revision.ToString(CultureInfo.InvariantCulture)!=saved.ProfileRevision || profile.FunctionalCurrency!=draft.Currency || mandate is not {Decision:"Accepted",Conditions:null or ""})
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.GateBlocked,"Current accepted native bookkeeping service and unchanged invoice profile are required.");
    if(!DateOnly.TryParseExact(saved.AccountingDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"The retained accounting date is invalid.");
    var period=await db.ClientReportingPeriods.FromSqlInterpolated($"SELECT * FROM client_reporting_periods WHERE firm_id={actor.FirmId} AND client_id={clientId} AND id={draft.PeriodId} FOR UPDATE").SingleOrDefaultAsync(ct);
    if(period is null || period.Status==AccountingWorkflowStates.Closed || date<period.StartDate || date>period.EndDate || period.Currency!=draft.Currency)
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.GateBlocked,"An open matching client period is required.");
    var charts=await db.ClientChartVersions.AsNoTracking().Where(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Status==AccountingWorkflowStates.Approved&&x.EffectiveFrom<=date&&(x.EffectiveTo==null||x.EffectiveTo>=date)).Take(2).ToListAsync(ct);
    if(charts.Count!=1 || charts[0].Id!=draft.ChartVersionId || saved.ChartVersionId!=draft.ChartVersionId)
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.GateBlocked,"The frozen invoice chart must remain uniquely approved for its accounting date.");
    var role=await db.ClientAccountRoleConfigurations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==r.ReceivableRoleId&&x.Role=="AR"&&x.ChartVersionId==draft.ChartVersionId&&x.EffectiveFrom<=date&&(x.EffectiveTo==null||x.EffectiveTo>=date),ct);
    var decision=role is null?null:await db.ClientAccountRoleDecisions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.ConfigurationId==role.Id&&x.Decision=="APPROVE",ct);
    if(role is null || decision is null) return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.GateBlocked,"Choose the independently approved receivables role covering this invoice date.");
    var ids=saved.Lines.Select(x=>x.AccountId).Append(role.AccountId).Distinct().ToArray();
    var accounts=await db.ClientAccounts.AsNoTracking().Where(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.ChartVersionId==draft.ChartVersionId&&ids.Contains(x.Id)&&x.IsPosting&&x.Status==AccountingWorkflowStates.Active).ToListAsync(ct);
    var ar=accounts.SingleOrDefault(x=>x.Id==role.AccountId);
    if(accounts.Count!=ids.Length || ar is null || ar.AccountType!="ASSET" || saved.Lines.Any(l=>!accounts.Any(a=>a.Id==l.AccountId&&a.AccountCode==l.AccountCode&&a.AccountName==l.AccountName&&a.AccountType=="INCOME")))
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.Accounting.MappingInvalid,"Frozen income accounts and the approved receivable account must remain valid.");
    var inputs=new List<ClientInvoiceLineInput>();
    foreach(var l in saved.Lines)
    {
      if(!DecimalInput(l.Quantity,out var q)||!DecimalInput(l.UnitPrice,out var p)||!DecimalInput(l.Discount,out var d))return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"The retained exact invoice inputs are invalid.");
      inputs.Add(new(l.Description,l.AccountCode,q,p,d,"NONE",[]));
    }
    var calc=ClientInvoiceCalculator.Calculate("INVOICE",draft.Currency,profile.FunctionalCurrency,saved.Policy,inputs);
    if(!calc.Valid || calc.Gross<=0 || calc.Gross!=draft.GrossAmount || calc.Net!=draft.NetAmount || saved.Gross!=Exact(calc.Gross,saved.Policy.DecimalPlaces) || saved.Net!=Exact(calc.Net,saved.Policy.DecimalPlaces) || calc.Lines.Where((l,i)=>saved.Lines[i].LineNumber!=l.LineNumber||saved.Lines[i].Net!=Exact(l.Net,saved.Policy.DecimalPlaces)||saved.Lines[i].Gross!=Exact(l.Gross,saved.Policy.DecimalPlaces)).Any())
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"Invoice calculation must reproduce its frozen inputs and positive exact totals.");
    var lines=new List<ClientOperationalJournalLineView>{new(1,ar.Id,ar.AccountCode,ar.AccountName,"Invoice receivable",Exact(calc.Gross),Exact(0))};
    foreach(var group in saved.Lines.GroupBy(l=>l.AccountId).OrderBy(g=>g.First().AccountCode,StringComparer.Ordinal))
    {
      var total=group.Sum(l=>decimal.Parse(l.Net,CultureInfo.InvariantCulture));if(total==0)continue;
      var a=accounts.Single(a=>a.Id==group.Key);lines.Add(new(lines.Count+1,a.Id,a.AccountCode,a.AccountName,"Invoice revenue",Exact(0),Exact(total)));
    }
    if(!ClientOperationalJournalCalculator.CalculateDocument(lines.Select(l=>new ClientOperationalJournalLineInput(l.AccountCode,l.Description,decimal.Parse(l.Debit,CultureInfo.InvariantCulture),decimal.Parse(l.Credit,CultureInfo.InvariantCulture))).ToArray()).Valid)
      return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.Accounting.MappingInvalid,"The invoice ledger effect must balance exactly.");
    string? receiptHash=null;
    if(r.SourceReceiptId is { } receiptId)
    {
      var receipt=await db.SourceReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId&&x.ClientId==clientId&&x.Id==receiptId,ct);
      if(receipt is null || !(await AuthorizationDecision.AuthorizeAsync(db,actor,new(actor.FirmId,clientId,receipt.EngagementId,RequiredRoles:Preparers,InternalOnly:true),ct)).Succeeded)
        return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ScopeDenied,"Select source evidence from the authorized client book.");
      receiptHash=receipt.Sha256Digest;
      if(receiptHash.Length!=64 || receiptHash.Any(c=>!char.IsAsciiHexDigit(c)) || receipt.ByteCount<0)return CommandResult<ClientSalesInvoicePreview>.Fail(ErrorCodes.ProtectedState,"The source receipt identity is invalid.");
    }
    var manifest=new ClientSalesInvoiceManifest("client-sales-submission-v1",draft.Id,draft.SnapshotHash,profile.Id,profile.Revision.ToString(CultureInfo.InvariantCulture),role.Id,decision.Id,ar.Id,r.NoTaxReason,r.SourceReceiptId,receiptHash,r.SourceBasis,mandate.Id,mandate.Generation.ToString(CultureInfo.InvariantCulture),lines);
    var digest=Hash(JsonSerializer.Serialize(new{Version="client-sales-preview-v1",actor.FirmId,clientId,draft.InvoiceId,draft.Revision,submissionId,Manifest=manifest,PeriodStatus=period.Status,period.StartDate,period.EndDate}));
    return CommandResult<ClientSalesInvoicePreview>.Ok(new(draft.InvoiceId,draft.Id,draft.Revision.ToString(CultureInfo.InvariantCulture),submissionId,draft.Currency,Exact(calc.Gross,saved.Policy.DecimalPlaces),digest,manifest));
  }
}
