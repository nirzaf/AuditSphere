using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Records;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

public sealed record ConfirmationRow(Guid Id, string AreaCode, string SourceRecordId, string Respondent, decimal BookedAmount,
  string Currency, DateOnly ConfirmationDate, string Status, DateTimeOffset? DispatchedAt, string Monitoring,
  bool Critical, string? CriticalityRationale, Guid OwnerId);
public sealed record ConfirmationPage(Guid EngagementId, int Page, bool HasMore, int OutstandingCritical, string CreateReviewToken,
  bool CanPrepare, bool CanReview, bool CanSetCriticality, IReadOnlyList<ConfirmationRow> Items);
public sealed record ConfirmationResponseView(Guid Id, string Revision, string Origin, string Channel, string Reference,
  decimal? ConfirmedAmount, decimal? DifferenceAmount, string AuthenticityAssessment, string Decision, DateTimeOffset ReceivedAt,
  bool PreparedByMe, Guid? ReviewerId, DateTimeOffset? ReviewedAt);
public sealed record ConfirmationAlternativeView(Guid Id, string Purpose, IReadOnlyList<string> EvidenceReferences, string Conclusion,
  string Status, bool PreparedByMe, Guid? ReviewerId, DateTimeOffset? ReviewedAt);
public sealed record ConfirmationClosureView(Guid Id, string Conclusion, string EvidenceSha256, Guid ClosedByUserId, DateTimeOffset ClosedAt);
public sealed record ConfirmationDetail(ConfirmationRow Case, string ReviewToken, string ContactValidationSource,
  string? DispatchReference, bool PreparedByMe, IReadOnlyList<ConfirmationResponseView> Responses, IReadOnlyList<ConfirmationAlternativeView> Alternatives, ConfirmationClosureView? Closure);
public sealed record ConfirmationAction(string Action, string ReviewToken, bool Reviewed, Guid? EvidenceId = null,
  string? Reference = null, string? Origin = null, string? Channel = null, decimal? ConfirmedAmount = null,
  string? AuthenticityAssessment = null, string? Decision = null, string? Purpose = null,
  IReadOnlyList<string>? EvidenceReferences = null, string? Conclusion = null, bool? Critical = null, string? Rationale = null);

/// <summary>Scoped native UI projection and reviewed command transaction. The existing fieldwork services own lifecycle rules.</summary>
public static partial class ConfirmationWorkspace
{
  private static readonly string[] ReadRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator", "Reviewer"];
  private static readonly string[] PrepareRoles = ["Partner", "Manager", "SeniorManager", "Senior", "Staff", "Auditor", "EngagementLeader", "Administrator"];
  private static readonly string[] ReviewRoles = ["Reviewer", "Manager", "Partner", "Administrator"];
  private static Task<CommandResult> Auth(IAuditSphereDbContext db, ActorContext a, Guid engagement, string[] roles, bool write, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db,a,new(a.FirmId,EngagementId:engagement,RequiredRoles:roles,InternalOnly:true,RequireProfessionalWork:write),ct);
  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied,"Access denied.");
  private static string Digest(object value) => TenantAdministration.Fingerprint(JsonSerializer.Serialize(value));
  private static async Task<string> CreateToken(IAuditSphereDbContext db, ActorContext a, Guid engagement, CancellationToken ct)
  {
    var e=await db.Engagements.AsNoTracking().SingleAsync(x=>x.FirmId==a.FirmId && x.Id==engagement,ct);
    var generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.Id==e.PracticeClientId).Select(x=>(long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var cases=db.AuditConfirmationCases.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.EngagementId==engagement);
    return Digest(new { a.FirmId,engagement,a.UserId,a.SessionEpoch,e.Generation,generation,Count=await cases.CountAsync(ct),Latest=await cases.MaxAsync(x=>(DateTimeOffset?)x.CreatedAt,ct) });
  }
  public static async Task<CommandResult<ConfirmationPage>> GetAsync(IAuditSphereDbContext db,ActorContext a,Guid engagement,int page=0,string filter="ALL",CancellationToken ct=default)
  {
    if(!(await Auth(db,a,engagement,ReadRoles,false,ct)).Succeeded)return Denied<ConfirmationPage>();
    if(page is <0 or >100000 || filter is not ("ALL" or "OUTSTANDING" or "CRITICAL"))return CommandResult<ConfirmationPage>.Fail("request.invalid","Choose a bounded page and confirmation filter.");
    var all=db.AuditConfirmationCases.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.EngagementId==engagement);
    var critical=db.ConfirmationCriticalities.AsNoTracking().Where(x=>x.FirmId==a.FirmId);
    var outstanding=await all.CountAsync(x=>x.Status!=AuditConfirmationStatuses.Closed && critical.Where(c=>c.ConfirmationCaseId==x.Id).OrderByDescending(c=>c.SetAt).ThenByDescending(c=>c.Id).Select(c=>(bool?)c.Critical).FirstOrDefault()==true,ct);
    var q=filter=="OUTSTANDING"?all.Where(x=>x.Status!=AuditConfirmationStatuses.Closed):filter=="CRITICAL"?all.Where(x=>critical.Where(c=>c.ConfirmationCaseId==x.Id).OrderByDescending(c=>c.SetAt).ThenByDescending(c=>c.Id).Select(c=>(bool?)c.Critical).FirstOrDefault()==true):all;
    var rows=await q.OrderBy(x=>x.AreaCode).ThenBy(x=>x.Respondent).ThenBy(x=>x.Id).Skip(page*25).Take(26).ToListAsync(ct);
    var result=new List<ConfirmationRow>();foreach(var c in rows.Take(25))result.Add(await Row(db,c,ct));
    var token=await CreateToken(db,a,engagement,ct);var prepare=(await Auth(db,a,engagement,PrepareRoles,true,ct)).Succeeded;var review=(await Auth(db,a,engagement,ReviewRoles,true,ct)).Succeeded;
    if(!(await Auth(db,a,engagement,ReadRoles,false,ct)).Succeeded)return Denied<ConfirmationPage>();
    return CommandResult<ConfirmationPage>.Ok(new(engagement,page,rows.Count>25,outstanding,token,prepare,review,(await Auth(db,a,engagement,["Manager","Partner","Administrator"],true,ct)).Succeeded,result));
  }
  private static async Task<ConfirmationRow> Row(IAuditSphereDbContext db,AuditConfirmationCase c,CancellationToken ct)
  {
    var critical=await db.ConfirmationCriticalities.AsNoTracking().Where(x=>x.FirmId==c.FirmId && x.ConfirmationCaseId==c.Id).OrderByDescending(x=>x.SetAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
    int? days=c.DispatchedAt is {} at?(int)(DateTimeOffset.UtcNow-at).TotalDays:null;
    var monitoring=c.Status switch { "CLOSED"=>"CLOSED","RESPONSE_RECEIVED"=>"RESPONSE_RECEIVED","NO_RESPONSE" or "ALTERNATIVE_REQUIRED"=>"ALTERNATIVE_PROCEDURES",
      "DISPATCHED" when days>=AuditDeliverableService.AlternativeProcedureDays=>"ALTERNATIVE_PROCEDURES_DUE","DISPATCHED" when days>=AuditDeliverableService.FollowUpDays=>"FOLLOW_UP_DUE","DISPATCHED"=>"AWAITING_RESPONSE",_=>"NOT_DISPATCHED" };
    return new(c.Id,c.AreaCode,c.SourceRecordId,c.Respondent,c.BookedAmount,c.Currency,c.ConfirmationDate,c.Status,c.DispatchedAt,monitoring,critical?.Critical??false,critical?.Rationale,c.CreatedByUserId);
  }
  public static async Task<CommandResult<ConfirmationDetail>> DetailAsync(IAuditSphereDbContext db,ActorContext a,Guid engagement,Guid id,CancellationToken ct=default)
  {
    if(!(await Auth(db,a,engagement,ReadRoles,false,ct)).Succeeded)return Denied<ConfirmationDetail>();
    var c=await db.AuditConfirmationCases.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.EngagementId==engagement && x.Id==id,ct);if(c==null)return Denied<ConfirmationDetail>();
    var responses=await db.AuditConfirmationResponses.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.ConfirmationCaseId==id).OrderByDescending(x=>x.Revision).Take(101).ToListAsync(ct);
    var alternatives=await db.AuditAlternativeProcedures.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.ConfirmationCaseId==id).OrderByDescending(x=>x.CreatedAt).ThenByDescending(x=>x.Id).Take(101).ToListAsync(ct);
    if(responses.Count>100 || alternatives.Count>100)return CommandResult<ConfirmationDetail>.Fail("window.exceeded","This case exceeds the interactive evidence window. Use the controlled evidence review process.");
    var critical=await db.ConfirmationCriticalities.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.ConfirmationCaseId==id).OrderByDescending(x=>x.SetAt).ThenByDescending(x=>x.Id).FirstOrDefaultAsync(ct);
    var closure=await db.AuditConfirmationClosures.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.EngagementId==engagement && x.ConfirmationCaseId==id,ct);
    var registerToken=await CreateToken(db,a,engagement,ct);
    var token=Digest(new { a.FirmId,a.UserId,a.SessionEpoch,registerToken,c,responses,alternatives,critical,closure });
    var value=new ConfirmationDetail(await Row(db,c,ct),token,c.ContactValidationSource,c.DispatchReference,c.CreatedByUserId==a.UserId,
      responses.Select(r=>new ConfirmationResponseView(r.Id,r.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),r.Origin,r.Channel,r.ResponseReference,r.ConfirmedAmount,r.DifferenceAmount,r.AuthenticityAssessment,r.Decision,r.ReceivedAt,r.CreatedByUserId==a.UserId,r.ReviewedByUserId,r.ReviewedAt)).ToArray(),
      alternatives.Select(r=>new ConfirmationAlternativeView(r.Id,r.Purpose,JsonSerializer.Deserialize<string[]>(r.EvidenceReferencesJson)??[],r.Conclusion,r.Status,r.CreatedByUserId==a.UserId,r.ReviewedByUserId,r.ReviewedAt)).ToArray(),
      closure is null?null:new(closure.Id,closure.Conclusion,closure.EvidenceSha256,closure.ClosedByUserId,closure.ClosedAt));
    return (await Auth(db,a,engagement,ReadRoles,false,ct)).Succeeded?CommandResult<ConfirmationDetail>.Ok(value):Denied<ConfirmationDetail>();
  }
  // Lock order matches role/acceptance transactions. No external dispatch occurs in these local commands.
  private static async Task Lock(IAuditSphereDbContext db,ActorContext a,Guid engagement,CancellationToken ct)
  {
    await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {a.FirmId} FOR SHARE").LoadAsync(ct);
    var e=await db.Engagements.AsNoTracking().SingleAsync(x=>x.FirmId==a.FirmId && x.Id==engagement,ct);
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id = {a.FirmId} AND id = {e.PracticeClientId} FOR UPDATE").LoadAsync(ct);
    await db.Engagements.FromSqlInterpolated($"SELECT * FROM engagements WHERE firm_id = {a.FirmId} AND id = {engagement} FOR UPDATE").LoadAsync(ct);
  }
  public static async Task<CommandResult<ConfirmationValue>> CreateAsync(IAuditSphereDbContext db,ActorContext a,CreateConfirmationRequest request,string token,bool reviewed,CancellationToken ct=default)
  {
    if(!(await Auth(db,a,request.EngagementId,PrepareRoles,true,ct)).Succeeded)return Denied<ConfirmationValue>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,a,request.EngagementId,ct);
    if(!(await Auth(db,a,request.EngagementId,PrepareRoles,true,ct)).Succeeded)return Denied<ConfirmationValue>();
    if(!reviewed || token!=await CreateToken(db,a,request.EngagementId,ct))return CommandResult<ConfirmationValue>.Fail(ErrorCodes.StaleRevision,"Refresh and review the current confirmation register.");
    var writable=await FileFreezeService.RequireWritableAsync(db,a,request.EngagementId,"prepare confirmation",ct);if(!writable.Succeeded){await tx.CommitAsync(ct);return CommandResult<ConfirmationValue>.Fail(writable.ErrorCode!,writable.Message!);}
    if(await db.AuditConfirmationCases.AnyAsync(x=>x.FirmId==a.FirmId && x.EngagementId==request.EngagementId && x.AreaCode==request.AreaCode.Trim().ToUpperInvariant() && x.SourceRecordId==request.SourceRecordId.Trim() && x.ConfirmationDate==request.ConfirmationDate,ct))return CommandResult<ConfirmationValue>.Fail(ErrorCodes.ProtectedState,"This source/date confirmation already exists. Review its persisted case.");
    var result=await AuditFieldworkService.CreateConfirmationAsync(db,a,request,ct);if(result.Succeeded)await tx.CommitAsync(ct);return result;
  }
  public static async Task<CommandResult> ActAsync(IAuditSphereDbContext db,ActorContext a,Guid engagement,Guid id,ConfirmationAction i,CancellationToken ct=default)
  {
    var roles=i.Action is "APPROVE" or "REVIEW_RESPONSE" or "REVIEW_ALTERNATIVE" or "CLOSE"?ReviewRoles:i.Action=="CRITICALITY"?["Manager","Partner","Administrator"]:PrepareRoles;
    if(!(await Auth(db,a,engagement,roles,true,ct)).Succeeded)return CommandResult.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    await using var tx=await db.Database.BeginTransactionAsync(ct);await Lock(db,a,engagement,ct);
    if(!(await Auth(db,a,engagement,roles,true,ct)).Succeeded)return CommandResult.Fail(ErrorCodes.ScopeDenied,"Access denied.");
    await db.AuditConfirmationCases.FromSqlInterpolated($"SELECT * FROM audit_confirmation_cases WHERE firm_id = {a.FirmId} AND engagement_id = {engagement} AND id = {id} FOR UPDATE").LoadAsync(ct);
    var d=await DetailAsync(db,a,engagement,id,ct);if(!d.Succeeded)return CommandResult.Fail(d.ErrorCode!,d.Message!);
    if(!i.Reviewed || i.ReviewToken!=d.Value!.ReviewToken)return CommandResult.Fail(ErrorCodes.StaleRevision,"Refresh and review the current case and evidence revisions.");
    var writable=await FileFreezeService.RequireWritableAsync(db,a,engagement,"update confirmation",ct);if(!writable.Succeeded){await tx.CommitAsync(ct);return writable;}
    static CommandResult Convert(CommandResult<ConfirmationValue> r)=>r.Succeeded?CommandResult.Ok():CommandResult.Fail(r.ErrorCode!,r.Message!);
    CommandResult result;
    switch(i.Action)
    {
      case "APPROVE": result=Convert(await AuditFieldworkService.ApproveConfirmationAsync(db,a,id,ct));break;
      case "DISPATCH": result=Convert(await AuditFieldworkService.RecordDispatchEvidenceAsync(db,a,new(id,i.Reference??""),ct));break;
      case "RESPONSE" when i.ConfirmedAmount is null || i.ConfirmedAmount.Value-d.Value.Case.BookedAmount is > -100000000000000m and < 100000000000000m: result=Convert(await AuditFieldworkService.RecordConfirmationResponseAsync(db,a,new(id,i.Origin??"",i.Channel??"",i.Reference??"",i.ConfirmedAmount,i.AuthenticityAssessment??"",i.Decision??""),ct));break;
      case "REVIEW_RESPONSE" when d.Value.Responses.FirstOrDefault() is {} r && r.Id==i.EvidenceId && r.ReviewerId==null: result=await AuditFieldworkService.ReviewConfirmationResponseAsync(db,a,new(r.Id),ct);break;
      case "ALTERNATIVE": result=Convert(await AuditFieldworkService.RecordAlternativeProcedureAsync(db,a,new(id,i.Purpose??"",i.EvidenceReferences??[],i.Conclusion??""),ct));break;
      case "REVIEW_ALTERNATIVE" when d.Value.Alternatives.FirstOrDefault() is {} alt && alt.Id==i.EvidenceId && alt.Status!="REVIEWED": result=await AuditFieldworkService.ReviewAlternativeProcedureAsync(db,a,new(alt.Id,null),ct);break;
      case "CLOSE": result=Convert(await AuditFieldworkService.CloseConfirmationAsync(db,a,new(id,i.Conclusion??""),ct));break;
      case "CRITICALITY" when i.Critical.HasValue: result=await AuditDeliverableService.SetConfirmationCriticalityAsync(db,a,id,i.Critical.Value,i.Rationale??"",ct);break;
      default: result=CommandResult.Fail("request.invalid","Select a supported action against the current evidence revision.");break;
    }
    if(result.Succeeded)await tx.CommitAsync(ct);return result;
  }
}
