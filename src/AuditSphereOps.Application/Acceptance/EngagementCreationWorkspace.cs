using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
namespace AuditSphereOps.Application.Acceptance;

public sealed record EngagementCreationFields(string ServiceRoute,string ServiceProfile,string PeriodStart,string PeriodEnd);
public sealed record EngagementCreationRequest(Guid RequestId,string ReviewBasis,EngagementCreationFields Fields,bool Reviewed=false,string? ExpectedRequestHash=null);
public sealed record EngagementCreationState(Guid ClientId,string ClientName,string ClientGeneration,string ReviewBasis);
public sealed record EngagementCreationPreview(Guid ClientId,Guid RequestId,string ReviewBasis,string RequestHash,EngagementCreationFields Fields,
  Guid? ExistingEngagementId = null);
public sealed record EngagementCreationReceipt(Guid Id,Guid ClientId,Guid EngagementId,Guid ActorId,Guid RequestId,string RequestHash,string ReviewBasis,
  string ClientGeneration,string EngagementGeneration,EngagementCreationFields Fields,DateTimeOffset CreatedAt);
public sealed record EngagementCreationLookup(bool Found,EngagementCreationReceipt? Receipt);

/// <summary>Reviewed blocked-shell creation, exact client scope and actor-owned immutable request recovery.</summary>
public static class EngagementCreationWorkspace
{
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db,ActorContext a,Guid id,CancellationToken ct)=>
    AuthorizationDecision.AuthorizeAsync(db,a,new(a.FirmId,ClientId:id,RequiredRoles:["Partner","Manager"],InternalOnly:true),ct);
  private static CommandResult<T> Unavailable<T>()=>CommandResult<T>.Fail(ErrorCodes.ScopeDenied,"This engagement creation workspace is unavailable.");
  private static bool HashValid(string? s)=>s is {Length:64}&&s.All(c=>c is >= 'a' and <= 'f' or >= '0' and <= '9');
  private static string Exact(long n)=>n.ToString(CultureInfo.InvariantCulture);
  private static EngagementCreationFields? Canonical(EngagementCreationFields? f)
  {
    if(f is null)return null;
    var route=f.ServiceRoute?.Trim();var profile=f.ServiceProfile?.Trim();var start=f.PeriodStart?.Trim();var end=f.PeriodEnd?.Trim();
    if(route is null||route.Length is <2 or >50||profile is null||profile.Length is <1 or >100||route.Any(char.IsControl)||profile.Any(char.IsControl)||
      !DateOnly.TryParseExact(start,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var first)||
      !DateOnly.TryParseExact(end,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var last)||first>last)return null;
    return new(route,profile,start!,end!);
  }
  private static string RequestHash(ActorContext a,Guid id,EngagementCreationRequest r,EngagementCreationFields f)=>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new{a.FirmId,a.UserId,a.SessionEpoch,id,r.RequestId,Action="BLOCKED_ENGAGEMENT_CREATION",Fields=f}));
  private static EngagementCreationReceipt Receipt(EngagementCreation row)=>new(row.Id,row.ClientId,row.EngagementId,row.ActorId,row.RequestId,row.RequestHash,row.ReviewBasis,
    Exact(row.ClientGeneration),Exact(row.EngagementGeneration),JsonSerializer.Deserialize<EngagementCreationFields>(row.InputJson)!,row.CreatedAt);
  public static async Task<CommandResult<EngagementCreationState>> StateAsync(IAuditSphereDbContext db,ActorContext a,Guid id,CancellationToken ct=default)
  {
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationState>();
    var name=await db.PracticeClients.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==id).Select(x=>x.LegalName).SingleOrDefaultAsync(ct);
    var generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==id).Select(x=>(long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if(name is null||generation is null)return Unavailable<EngagementCreationState>();
    var basis=Hashing.Sha256Hex(JsonSerializer.Serialize(new{a.FirmId,a.UserId,a.SessionEpoch,id,name,Generation=generation.Value}));
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationState>();
    return CommandResult<EngagementCreationState>.Ok(new(id,name,Exact(generation.Value),basis));
  }
  public static async Task<CommandResult<EngagementCreationPreview>> PreviewAsync(IAuditSphereDbContext db,ActorContext a,Guid id,EngagementCreationRequest? r,CancellationToken ct=default)
  {
    var f=Canonical(r?.Fields);
    if(r is null||r.RequestId==Guid.Empty||!HashValid(r.ReviewBasis)||f is null)return CommandResult<EngagementCreationPreview>.Fail("request.invalid","Enter a service route, profile and valid period before review.");
    var s=await StateAsync(db,a,id,ct);if(!s.Succeeded)return CommandResult<EngagementCreationPreview>.Fail(s.ErrorCode!,s.Message!);
    if(s.Value!.ReviewBasis!=r.ReviewBasis)return CommandResult<EngagementCreationPreview>.Fail(ErrorCodes.GenerationStale,"Client context changed. Refresh and review again.");
    var existingId=await db.Engagements.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.PracticeClientId==id&&
      x.ServiceRoute==f.ServiceRoute&&x.PeriodStart==f.PeriodStart&&x.PeriodEnd==f.PeriodEnd)
      .Select(x=>(Guid?)x.Id).FirstOrDefaultAsync(ct);
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationPreview>();
    return CommandResult<EngagementCreationPreview>.Ok(new(id,r.RequestId,r.ReviewBasis,RequestHash(a,id,r,f),f,existingId));
  }
  public static async Task<CommandResult<EngagementCreationReceipt>> ExecuteAsync(IAuditSphereDbContext db,ActorContext a,Guid id,EngagementCreationRequest? r,CancellationToken ct=default)
  {
    var f=Canonical(r?.Fields);
    if(r is null||!r.Reviewed||r.RequestId==Guid.Empty||!HashValid(r.ReviewBasis)||f is null||!HashValid(r.ExpectedRequestHash)||r.ExpectedRequestHash!=RequestHash(a,id,r,f))
      return CommandResult<EngagementCreationReceipt>.Fail("request.invalid","Preview and explicitly confirm the exact blocked engagement intent.");
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationReceipt>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    var guard=await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if(guard is null)return Unavailable<EngagementCreationReceipt>();
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationReceipt>();
    var prior=await db.EngagementCreations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==r.RequestId,ct);
    if(prior is not null){
      if(prior.ClientId!=id||prior.RequestHash!=r.ExpectedRequestHash)return CommandResult<EngagementCreationReceipt>.Fail(ErrorCodes.IdempotencyConflict,"A changed engagement intent cannot reuse this request.");
      if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationReceipt>();
      await tx.CommitAsync(ct);return CommandResult<EngagementCreationReceipt>.Ok(Receipt(prior));
    }
    var preview=await PreviewAsync(db,a,id,r,ct);if(!preview.Succeeded)return CommandResult<EngagementCreationReceipt>.Fail(preview.ErrorCode!,preview.Message!);
    if(preview.Value!.ExistingEngagementId is not null)
      return CommandResult<EngagementCreationReceipt>.Fail("engagement.conflict","A matching service-period engagement already exists. Inspect it before continuing.");
    var created=await EngagementLifecycleService.CreateDraftAsync(db,a,new(id,f.ServiceRoute,f.PeriodStart,f.PeriodEnd,f.ServiceProfile),ct);
    if(!created.Succeeded)return CommandResult<EngagementCreationReceipt>.Fail(created.ErrorCode!,created.Message!);
    var e=await db.Engagements.AsNoTracking().SingleAsync(x=>x.FirmId==a.FirmId&&x.Id==created.Value,ct);
    var row=new EngagementCreation{Id=Guid.CreateVersion7(),FirmId=a.FirmId,ClientId=id,EngagementId=e.Id,ActorId=a.UserId,ActorEpoch=a.SessionEpoch,
      RequestId=r.RequestId,RequestHash=r.ExpectedRequestHash!,ReviewBasis=r.ReviewBasis,ClientGeneration=guard.InputGeneration,EngagementGeneration=e.Generation,
      InputJson=JsonSerializer.Serialize(f),CreatedAt=e.CreatedAt};
    db.EngagementCreations.Add(row);await db.SaveChangesAsync(ct);
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationReceipt>();
    await tx.CommitAsync(ct);return CommandResult<EngagementCreationReceipt>.Ok(Receipt(row));
  }
  public static async Task<CommandResult<EngagementCreationLookup>> LookupAsync(IAuditSphereDbContext db,ActorContext a,Guid id,Guid requestId,string? requestHash,CancellationToken ct=default)
  {
    if(requestId==Guid.Empty||!HashValid(requestHash)||!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationLookup>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    var guard=await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={id} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    if(guard is null)return Unavailable<EngagementCreationLookup>();
    var row=await db.EngagementCreations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==requestId,ct);
    if(row is not null&&(row.ClientId!=id||row.RequestHash!=requestHash))return CommandResult<EngagementCreationLookup>.Fail(ErrorCodes.IdempotencyConflict,"This reference belongs to a different engagement intent.");
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementCreationLookup>();
    await tx.CommitAsync(ct);return CommandResult<EngagementCreationLookup>.Ok(new(row is not null,row is null?null:Receipt(row)));
  }
}
