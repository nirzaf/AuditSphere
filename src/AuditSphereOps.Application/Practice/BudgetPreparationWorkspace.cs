using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Application.Practice;

public sealed record BudgetPreparationFields(string Currency,string ExpectedVersion,IReadOnlyList<BudgetLineRequest> Lines);
public sealed record BudgetPreparationRequest(Guid RequestId,BudgetPreparationFields Fields,string? ReviewBasis=null,string? RequestHash=null,bool Reviewed=false);
public sealed record BudgetPreparationLine(string Role,string Activity,string Phase,string? RiskArea,int ForecastMinutes,Guid RateCardId,string RatePerHour,string ForecastCost);
public sealed record BudgetPreparationPreview(Guid EngagementId,Guid RequestId,string RequestHash,string ReviewBasis,BudgetPreparationFields Fields,IReadOnlyList<BudgetPreparationLine> Lines,string ForecastCost);
public sealed record BudgetPreparationReceipt(Guid Id,Guid BudgetId,Guid EngagementId,Guid ActorId,Guid RequestId,string RequestHash,string ReviewBasis,BudgetPreparationPreview Preview,DateTimeOffset CreatedAt);
public sealed record BudgetPreparationLookup(bool Found,BudgetPreparationReceipt? Receipt);

/// <summary>Explicit rate-snapshot review and retained preparation outcome. Never approves a budget.</summary>
public static class BudgetPreparationWorkspace
{
  private static bool HashValid(string? s)=>s is {Length:64} && s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string Exact(decimal value)=>value.ToString(CultureInfo.InvariantCulture);
  private static BudgetPreparationFields? Canonical(BudgetPreparationFields? f)
  {
    if(f is null||f.Currency is null||f.Lines is null||f.Lines.Count is <1 or >200 ||
      !long.TryParse(f.ExpectedVersion,NumberStyles.None,CultureInfo.InvariantCulture,out var version)||version<0)return null;
    var currency=f.Currency.Trim().ToUpperInvariant();if(currency.Length!=3||currency.Any(c=>c is <'A' or >'Z'))return null;
    var lines=new List<BudgetLineRequest>();var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach(var l in f.Lines){
      if(l is null||string.IsNullOrWhiteSpace(l.Role)||string.IsNullOrWhiteSpace(l.Activity)||l.Role.Trim().Length>50||l.Activity.Trim().Length>50||
        l.Role.Any(char.IsControl)||l.Activity.Any(char.IsControl)||l.ForecastMinutes is <1 or >10000000||l.RiskArea?.Length>120||l.RiskArea?.Any(char.IsControl)==true)return null;
      var phase=string.IsNullOrWhiteSpace(l.Phase)?BudgetPhases.Unassigned:l.Phase.Trim().ToUpperInvariant();
      if(phase!=BudgetPhases.Unassigned&&!BudgetPhases.Assignable.Contains(phase))return null;
      var risk=string.IsNullOrWhiteSpace(l.RiskArea)?null:l.RiskArea.Trim();
      if(!keys.Add($"{l.Role.Trim()}\n{l.Activity.Trim()}\n{phase}\n{risk}"))return null;
      lines.Add(new(l.Role.Trim(),l.Activity.Trim(),l.ForecastMinutes,phase==BudgetPhases.Unassigned?null:phase,risk));
    }
    return new(currency,version.ToString(CultureInfo.InvariantCulture),lines);
  }
  private static async Task<bool> Authorized(IAuditSphereDbContext db,ActorContext a,Guid id,CancellationToken ct)=>
    (await AuthorizationDecision.AuthorizeAsync(db,a,new(a.FirmId,EngagementId:id,RequiredRoles:["Partner","Manager"],InternalOnly:true),ct)).Succeeded;
  private static CommandResult<T> Unavailable<T>()=>CommandResult<T>.Fail(ErrorCodes.ScopeDenied,"Budget preparation is unavailable.");
  private static string RequestHash(ActorContext a,Guid id,Guid requestId,BudgetPreparationFields f)=>Hashing.Sha256Hex(JsonSerializer.Serialize(new{a.FirmId,a.UserId,a.SessionEpoch,id,requestId,Action="BUDGET_PREPARATION",Fields=f}));
  private static BudgetPreparationReceipt Receipt(BudgetPreparation row)=>new(row.Id,row.BudgetId,row.EngagementId,row.ActorId,row.RequestId,row.RequestHash,row.ReviewBasis,JsonSerializer.Deserialize<BudgetPreparationPreview>(row.PreviewJson)!,row.CreatedAt);
  public static async Task<CommandResult<BudgetPreparationPreview>> PreviewAsync(IAuditSphereDbContext db,ActorContext a,Guid id,BudgetPreparationRequest? r,CancellationToken ct=default)
  {
    if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationPreview>();
    var fields=Canonical(r?.Fields);if(r is null||r.RequestId==Guid.Empty||fields is null)return CommandResult<BudgetPreparationPreview>.Fail("request.invalid","Enter bounded budget lines and a current revision.");
    var e=await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.Id==id,ct);if(e is null)return Unavailable<BudgetPreparationPreview>();
    var generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==e.PracticeClientId).Select(x=>(long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    var latest=await db.EngagementBudgets.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.EngagementId==id).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct);
    if(generation is null)return Unavailable<BudgetPreparationPreview>();
    if(fields.ExpectedVersion!=(latest?.Version??0).ToString(CultureInfo.InvariantCulture))return CommandResult<BudgetPreparationPreview>.Fail(ErrorCodes.StaleRevision,"Budget changed. Refresh before review.");
    var lines=new List<BudgetPreparationLine>();decimal total=0;
    foreach(var l in fields.Lines){
      var card=await db.RateCardVersions.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Role==l.Role&&x.Activity==l.Activity&&x.Currency==fields.Currency&&x.Status==PracticeTimeStates.RateApproved).OrderByDescending(x=>x.Version).FirstOrDefaultAsync(ct);
      if(card is null)return CommandResult<BudgetPreparationPreview>.Fail("time.rate-missing","An approved rate card is required for every line.");
      var cost=MoneyPolicy.Normalize(l.ForecastMinutes*card.RatePerHour/60m);total+=cost;
      lines.Add(new(l.Role,l.Activity,l.Phase??BudgetPhases.Unassigned,l.RiskArea,l.ForecastMinutes,card.Id,Exact(card.RatePerHour),Exact(cost)));
    }
    var hash=RequestHash(a,id,r.RequestId,fields);
    var basis=Hashing.Sha256Hex(JsonSerializer.Serialize(new{a.FirmId,a.UserId,a.SessionEpoch,id,e.Generation,ClientGeneration=generation.Value,LatestId=latest?.Id,LatestState=latest?.Status,Fields=fields,Lines=lines}));
    if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationPreview>();
    return CommandResult<BudgetPreparationPreview>.Ok(new(id,r.RequestId,hash,basis,fields,lines,Exact(total)));
  }
  public static async Task<CommandResult<BudgetPreparationReceipt>> ExecuteAsync(IAuditSphereDbContext db,ActorContext a,Guid id,BudgetPreparationRequest? r,CancellationToken ct=default)
  {
    if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationReceipt>();
    var fields=Canonical(r?.Fields);
    if(r is null||!r.Reviewed||r.RequestId==Guid.Empty||fields is null||!HashValid(r.RequestHash)||!HashValid(r.ReviewBasis)||r.RequestHash!=RequestHash(a,id,r.RequestId,fields))return CommandResult<BudgetPreparationReceipt>.Fail("request.invalid","Preview and confirm the exact budget preparation.");
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)return Unavailable<BudgetPreparationReceipt>();
    var e=await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.Id==id,ct);if(e is null)return Unavailable<BudgetPreparationReceipt>();
    if(await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={e.PracticeClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null)return Unavailable<BudgetPreparationReceipt>();
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationReceipt>();
    var prior=await db.BudgetPreparations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==r.RequestId,ct);
    if(prior is not null){
      if(prior.EngagementId!=id||prior.RequestHash!=r.RequestHash)return CommandResult<BudgetPreparationReceipt>.Fail(ErrorCodes.IdempotencyConflict,"A changed budget intent cannot reuse the request.");
      if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationReceipt>();
      await tx.CommitAsync(ct);return CommandResult<BudgetPreparationReceipt>.Ok(Receipt(prior));
    }
    var preview=await PreviewAsync(db,a,id,r,ct);if(!preview.Succeeded)return CommandResult<BudgetPreparationReceipt>.Fail(preview.ErrorCode!,preview.Message!);
    if(preview.Value!.ReviewBasis!=r.ReviewBasis)return CommandResult<BudgetPreparationReceipt>.Fail(ErrorCodes.StaleRevision,"Budget context or approved rates changed. Review again.");
    var saved=await PracticeTimeService.ReviseBudgetAsync(db,a,new(id,fields.Currency,fields.Lines,long.Parse(fields.ExpectedVersion,CultureInfo.InvariantCulture)),ct);
    if(!saved.Succeeded)return CommandResult<BudgetPreparationReceipt>.Fail(saved.ErrorCode!,saved.Message!);
    var budget=await db.EngagementBudgets.AsNoTracking().SingleAsync(x=>x.FirmId==a.FirmId&&x.Id==saved.Value,ct);
    var row=new BudgetPreparation{Id=Guid.CreateVersion7(),FirmId=a.FirmId,EngagementId=id,BudgetId=budget.Id,ActorId=a.UserId,ActorEpoch=a.SessionEpoch,RequestId=r.RequestId,RequestHash=r.RequestHash!,ReviewBasis=r.ReviewBasis!,PreviewJson=JsonSerializer.Serialize(preview.Value),CreatedAt=budget.CreatedAt};
    db.BudgetPreparations.Add(row);await db.SaveChangesAsync(ct);
    if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationReceipt>();
    await tx.CommitAsync(ct);return CommandResult<BudgetPreparationReceipt>.Ok(Receipt(row));
  }
  public static async Task<CommandResult<BudgetPreparationLookup>> LookupAsync(IAuditSphereDbContext db,ActorContext a,Guid id,Guid requestId,string? hash,CancellationToken ct=default)
  {
    if(!await Authorized(db,a,id,ct)||requestId==Guid.Empty||!HashValid(hash))return Unavailable<BudgetPreparationLookup>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct) is null)return Unavailable<BudgetPreparationLookup>();
    var row=await db.BudgetPreparations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==requestId,ct);
    if(row is not null&&(row.EngagementId!=id||row.RequestHash!=hash))return CommandResult<BudgetPreparationLookup>.Fail(ErrorCodes.IdempotencyConflict,"This reference belongs to another budget intent.");
    if(!await Authorized(db,a,id,ct))return Unavailable<BudgetPreparationLookup>();
    await tx.CommitAsync(ct);return CommandResult<BudgetPreparationLookup>.Ok(new(row is not null,row is null?null:Receipt(row)));
  }
}
