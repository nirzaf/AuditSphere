using AuditSphereOps.Application.Audit;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  public sealed record UiConfirmationCreate(string ReviewToken, bool Reviewed, Guid? ProcedureId, string AreaCode,
    string SourceRecordId, string BookedAmount, string Currency, string ConfirmationDate, string Respondent, string ContactValidationSource);
  public sealed record UiConfirmationAction(string Action,string ReviewToken,bool Reviewed,Guid? EvidenceId=null,
    string? Reference=null,string? Origin=null,string? Channel=null,string? ConfirmedAmount=null,string? AuthenticityAssessment=null,
    string? Decision=null,string? Purpose=null,string[]? EvidenceReferences=null,string? Conclusion=null,bool? Critical=null,string? Rationale=null);
  public sealed record UiConfirmationBatchItem(string SourceRecordId, string BookedAmount, string Respondent, string ContactValidationSource);
  public sealed record UiConfirmationBatch(string ReviewToken, bool Reviewed, Guid? ProcedureId, string AreaCode,
    string Currency, string ConfirmationDate, UiConfirmationBatchItem[] Cases);
  private static void MapConfirmationEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/engagements/{id:guid}/confirmations",(Guid id,int? page,string? filter,HttpContext http)=>
      ReadAsync(http,(db,a,ct)=>ConfirmationWorkspace.GetAsync(db,a,id,page??0,filter??"ALL",ct)));
    group.MapGet("/engagements/{id:guid}/confirmations/{caseId:guid}",(Guid id,Guid caseId,HttpContext http)=>
      ReadAsync(http,(db,a,ct)=>ConfirmationWorkspace.DetailAsync(db,a,id,caseId,ct)));
    group.MapPost("/engagements/{id:guid}/confirmations/batch", (Guid id, UiConfirmationBatch i, HttpContext http) => CommandAsync(http, (db,a,ct) =>
    {
      if (i.Cases is not {Length:>=1 and <=100} || !AuditConfirmationAreaCodes.IsSupported(i.AreaCode) || i.Currency is not {Length:3} ||
          !DateOnly.TryParseExact(i.ConfirmationDate,"yyyy-MM-dd",out var date) || i.ReviewToken is not {Length:64})
        return Task.FromResult(CommandResult<ConfirmationBatchValue>.Fail("request.invalid","Review one to 100 cases with explicit area, currency and date."));
      var cases = new List<ConfirmationBatchItem>(i.Cases.Length);
      foreach (var c in i.Cases)
      {
        if (c is null || !Bound(c.SourceRecordId,200) || !Bound(c.Respondent,500) || !Bound(c.ContactValidationSource,2000) || !TryConfirmationAmount(c.BookedAmount,out var amount))
          return Task.FromResult(CommandResult<ConfirmationBatchValue>.Fail("request.invalid","Every case requires bounded identity, exact amount and validated contact evidence. No cases were saved."));
        cases.Add(new(c.SourceRecordId,amount,c.Respondent,c.ContactValidationSource));
      }
      return ConfirmationWorkspace.CreateBatchAsync(db,a,new(id,i.ProcedureId,i.AreaCode,i.Currency,date,cases),i.ReviewToken,i.Reviewed,ct);
    }));
    group.MapPost("/engagements/{id:guid}/confirmations",(Guid id,UiConfirmationCreate i,HttpContext http)=>CommandAsync(http,(db,a,ct)=>
    {
      if(!TryConfirmationAmount(i.BookedAmount,out var amount) || !DateOnly.TryParseExact(i.ConfirmationDate,"yyyy-MM-dd",out var date) ||
        !AuditConfirmationAreaCodes.IsSupported(i.AreaCode) || !Bound(i.SourceRecordId,200) || !Bound(i.Respondent,500) || !Bound(i.ContactValidationSource,2000) ||
        i.Currency is not {Length:3} || i.ReviewToken is not {Length:64})
        return Task.FromResult(CommandResult<ConfirmationValue>.Fail("request.invalid","Provide a reviewed identity, exact amount, date and bounded contact evidence."));
      return ConfirmationWorkspace.CreateAsync(db,a,new(id,i.ProcedureId,i.AreaCode,i.SourceRecordId,amount,i.Currency,date,i.Respondent,i.ContactValidationSource),i.ReviewToken,i.Reviewed,ct);
    }));
    group.MapPost("/engagements/{id:guid}/confirmations/{caseId:guid}/actions",(Guid id,Guid caseId,UiConfirmationAction i,HttpContext http)=>CommandAsync(http,(db,a,ct)=>
    {
      decimal? amount=null;
      if(i.ConfirmedAmount!=null) { if(!TryConfirmationAmount(i.ConfirmedAmount,out var parsed))return Task.FromResult(CommandResult.Fail("request.invalid","Use an exact decimal amount.")); amount=parsed; }
      if(i.ReviewToken is not {Length:64} || i.Action is not {Length:>0 and <=30} ||
        new[]{i.Reference,i.Origin,i.Channel,i.AuthenticityAssessment,i.Decision,i.Purpose,i.Conclusion,i.Rationale}.Any(x=>x?.Length>4000) ||
        i.Reference?.Length>500 || i.Origin?.Length>40 || i.Channel?.Length>40 || i.Purpose?.Length>2000 ||
        i.EvidenceReferences is {Length:>100} || i.EvidenceReferences?.Any(x=>!Bound(x,1000))==true)
        return Task.FromResult(CommandResult.Fail("request.invalid","Use a supported reviewed action with bounded evidence."));
      return ConfirmationWorkspace.ActAsync(db,a,id,caseId,new(i.Action,i.ReviewToken,i.Reviewed,i.EvidenceId,i.Reference,i.Origin,i.Channel,amount,
        i.AuthenticityAssessment,i.Decision,i.Purpose,i.EvidenceReferences,i.Conclusion,i.Critical,i.Rationale),ct);
    }));
  }
  private static bool TryConfirmationAmount(string? value,out decimal amount)=>ValidConfirmationAmount(value,out amount);
  private static bool ValidConfirmationAmount(string? value,out decimal amount)
  {
    amount=0;
    return value is {Length:>0 and <=22} && System.Text.RegularExpressions.Regex.IsMatch(value,@"^-?\d{1,14}(\.\d{1,6})?$",System.Text.RegularExpressions.RegexOptions.CultureInvariant) && TryDecimal(value,out amount);
  }
  private static bool Bound(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max;
}
