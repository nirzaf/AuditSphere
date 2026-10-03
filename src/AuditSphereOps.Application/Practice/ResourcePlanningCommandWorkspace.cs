using AuditSphereOps.Application.Abstractions;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record ResourcePlanningFields(string Kind, Guid UserId, Guid? EngagementId=null,
  string? Department=null, string? Skills=null, int? WeeklyCapacityMinutes=null, string? TargetUtilizationPercent=null,
  string? Name=null, DateOnly? ExpiresOn=null, DateOnly? StartDate=null, DateOnly? EndDate=null,
  string? AvailabilityKind=null, int? MinutesPerDay=null, DateOnly? WeekStart=null, int? PlannedMinutes=null);
public sealed record ResourcePlanningCommandRequest(Guid RequestId, ResourcePlanningFields Fields,
  string? RequestHash=null, string? ReviewBasis=null, bool Reviewed=false);
public sealed record ResourcePlanningBefore(string UserName, string? Department, string? Skills,
  int? WeeklyCapacityMinutes, string? TargetUtilizationPercent, int? PlannedMinutes, string? EngagementLabel,
  DateOnly? WeekStart, int? CapacityMinutes, int? UnavailableMinutes, int? TotalPlannedMinutes);
public sealed record ResourcePlanningPreview(Guid RequestId, string RequestHash, string ReviewBasis,
  ResourcePlanningFields Fields, ResourcePlanningBefore Before, string Effect);
public sealed record ResourcePlanningReceiptView(Guid Id, Guid ActorId, Guid TargetUserId, Guid RequestId,
  string RequestHash, string ReviewBasis, string Kind, Guid? ResourceId, ResourcePlanningPreview Preview, DateTimeOffset CreatedAt);
public sealed record ResourcePlanningReceiptLookup(bool Found, ResourcePlanningReceiptView? Receipt);

/// <summary>Reviewed planning mutations, stale-basis fencing and actor-owned response-loss recovery. No automatic retry or external authority.</summary>
public static class ResourcePlanningCommandWorkspace
{
  private static readonly string[] Roles=["Administrator","Partner","Manager"];
  private static AuthorizationRequest Authority(ActorContext a)=>new(a.FirmId,RequiredRoles:Roles,InternalOnly:true);
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db,ActorContext a,CancellationToken ct)=>AuthorizationDecision.AuthorizeAsync(db,a,Authority(a),ct);
  private static CommandResult<T> Denied<T>()=>CommandResult<T>.Fail(ErrorCodes.ScopeDenied,"Planning action unavailable.");
  private static CommandResult<T> Invalid<T>()=>CommandResult<T>.Fail("request.invalid","Review the bounded planning fields before submitting.");
  private static bool Hash(string? s)=>s is {Length:64} && s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static string? Trim(string? s)=>string.IsNullOrWhiteSpace(s)?null:s.Trim();
  private static bool Text(string? s,int max)=>s is null || s.Length<=max && !s.Any(char.IsControl);
  private static ResourcePlanningFields? Canonical(ResourcePlanningFields? f)
  {
    if(f is null || f.UserId==Guid.Empty || !Text(f.Department,100) || !Text(f.Skills,500) || !Text(f.Name,100) || !Text(f.TargetUtilizationPercent,20)) return null;
    var n=f with {Kind=f.Kind?.Trim().ToUpperInvariant() ?? "",Department=Trim(f.Department),Skills=Trim(f.Skills),Name=Trim(f.Name),AvailabilityKind=Trim(f.AvailabilityKind)?.ToUpperInvariant()};
    ResourcePlanningFields? shaped=null;
    switch(n.Kind) {
      case "PROFILE" when n.Department is not null && n.WeeklyCapacityMinutes is >=0 and <=4800 &&
        decimal.TryParse(n.TargetUtilizationPercent,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var target) && target is >=0 and <=100 && decimal.Round(target,2)==target:
        n=n with {TargetUtilizationPercent=target.ToString("0.##",CultureInfo.InvariantCulture), Skills=Trim(string.Join(',',(n.Skills ?? "").Split(',',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x=>x.ToUpperInvariant()).Distinct().Take(30)))};
        shaped=new(n.Kind,n.UserId,Department:n.Department,Skills:n.Skills,WeeklyCapacityMinutes:n.WeeklyCapacityMinutes,TargetUtilizationPercent:n.TargetUtilizationPercent);break;
      case "CERTIFICATION" when n.Name is not null:
        shaped=new(n.Kind,n.UserId,Name:n.Name,ExpiresOn:n.ExpiresOn);break;
      case "AVAILABILITY" when n.StartDate is DateOnly start && n.EndDate is DateOnly end && end>=start &&
        n.MinutesPerDay is >=1 and <=1440 && n.AvailabilityKind is "LEAVE" or "TRAINING" or "PUBLIC_HOLIDAY":
        shaped=new(n.Kind,n.UserId,StartDate:n.StartDate,EndDate:n.EndDate,AvailabilityKind:n.AvailabilityKind,MinutesPerDay:n.MinutesPerDay);break;
      case "ALLOCATION" when n.EngagementId is Guid id && id!=Guid.Empty && n.WeekStart is DateOnly week && n.PlannedMinutes is >=0 and <=4800:
        n=n with {WeekStart=ResourceGridCalculator.WeekStart(week)};
        shaped=new(n.Kind,n.UserId,EngagementId:n.EngagementId,WeekStart:n.WeekStart,PlannedMinutes:n.PlannedMinutes);break;
    }
    return n==shaped?shaped:null;
  }
  private static string RequestHash(ActorContext a,Guid requestId,ResourcePlanningFields f)=>Hashing.Sha256Hex(JsonSerializer.Serialize(new{a.FirmId,a.UserId,a.SessionEpoch,requestId,Fields=f}));
  private static ResourcePlanningReceiptView Receipt(ResourcePlanningReceipt r)=>new(r.Id,r.ActorId,r.TargetUserId,r.RequestId,r.RequestHash,r.ReviewBasis,r.Kind,r.ResourceId,JsonSerializer.Deserialize<ResourcePlanningPreview>(r.PreviewJson)!,r.CreatedAt);
  private static async Task<bool> LockAsync(IAuditSphereDbContext db,ActorContext a,ResourcePlanningFields f,CancellationToken ct)
  {
    // Firm serialization is shared with all standalone planning writers, staffing and role administration.
    return await ResourcePlanningService.LockPublicationAsync(db,a,f.UserId,Authority(a),ct);
  }
  private static async Task<bool> LockRequestAsync(IAuditSphereDbContext db,ActorContext a,CancellationToken ct)
  {
    if(await db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id={a.FirmId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    if(await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct) is null) return false;
    return (await Authorize(db,a,ct)).Succeeded;
  }
  private static async Task<CommandResult<ResourcePlanningPreview>> BuildAsync(IAuditSphereDbContext db,ActorContext a,Guid requestId,ResourcePlanningFields f,CancellationToken ct)
  {
    var user=await db.Users.AsNoTracking().SingleAsync(x=>x.FirmId==a.FirmId && x.Id==f.UserId,ct);
    var profile=await db.StaffProfiles.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.UserId==f.UserId,ct);
    StaffAllocation? existing=null;
    object? basisExtra=null;
    string? label=null;
    ResourceWeekCell? cell=null;
    if(f.Kind=="ALLOCATION") {
      var e=await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==f.EngagementId,ct);
      if(e is null) return Denied<ResourcePlanningPreview>();
      if(e.Status!="Active") return CommandResult<ResourcePlanningPreview>.Fail(ErrorCodes.ProtectedState,"Allocate only to a current active engagement.");
      var staff=await db.EngagementStaffAssignments.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.EngagementId==e.Id && x.UserId==f.UserId && x.RevokedAt==null,ct);
      if(staff is null) return CommandResult<ResourcePlanningPreview>.Fail(ErrorCodes.GateBlocked,"Staff the person on this engagement before allocating time.");
      var week=f.WeekStart!.Value;
      if(week>DateOnly.MaxValue.AddDays(-6)) return Invalid<ResourcePlanningPreview>();
      var end=week.AddDays(6);
      var allocations=await db.StaffAllocations.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.UserId==f.UserId && x.WeekStart==week).OrderBy(x=>x.Id).ToListAsync(ct);
      var availability=await db.StaffAvailabilities.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.UserId==f.UserId && x.EndDate>=week && x.StartDate<=end).OrderBy(x=>x.Id).ToListAsync(ct);
      existing=allocations.SingleOrDefault(x=>x.EngagementId==e.Id);
      cell=ResourceGridCalculator.Build(profile?.WeeklyCapacityMinutes ?? 0,availability,allocations,[],week,1).Single();
      label=await db.PracticeClients.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.Id==e.PracticeClientId).Select(x=>x.CommercialName ?? x.LegalName).SingleAsync(ct);
      label+=" · "+e.ServiceRoute+" "+e.PeriodEnd;
      basisExtra=new {Engagement=e,Assignment=staff,Allocations=allocations,Availability=availability};
    }
    if(f.Kind=="CERTIFICATION") basisExtra=await db.StaffCertifications.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.UserId==f.UserId).OrderBy(x=>x.Id).ToListAsync(ct);
    if(f.Kind=="AVAILABILITY") basisExtra=await db.StaffAvailabilities.AsNoTracking().Where(x=>x.FirmId==a.FirmId && x.UserId==f.UserId && x.EndDate>=f.StartDate && x.StartDate<=f.EndDate).OrderBy(x=>x.Id).ToListAsync(ct);
    var basis=Hashing.Sha256Hex(JsonSerializer.Serialize(new{a.FirmId,a.UserId,a.SessionEpoch,Fields=f,Target=new{user.Id,user.DisplayName,user.Disabled,user.UserKind,user.SessionEpoch},Profile=profile,Extra=basisExtra}));
    var before=new ResourcePlanningBefore(user.DisplayName,profile?.Department,profile?.Skills,profile?.WeeklyCapacityMinutes,
      profile?.TargetUtilizationPercent.ToString("0.##",CultureInfo.InvariantCulture),existing?.PlannedMinutes,label,f.WeekStart,cell?.CapacityMinutes,cell?.UnavailableMinutes,cell?.PlannedMinutes);
    var effect=f.Kind switch {
      "PROFILE"=>"Updates planning department, skills, capacity and utilization target. It changes no authorization role.",
      "CERTIFICATION"=>"Adds a certification record. It does not staff this person or grant access.",
      "AVAILABILITY"=>"Adds an unavailability interval. Capacity is calculated by the existing planning rules; overlapping intervals are not additional leave days.",
      _=>f.PlannedMinutes==0?"Removes this engagement's allocation for this week. Staffing, authorization and SharePoint permissions are unchanged.":"Sets this engagement's weekly allocation. Over-allocation remains visible; staffing, authorization and SharePoint permissions are unchanged."};
    return CommandResult<ResourcePlanningPreview>.Ok(new(requestId,RequestHash(a,requestId,f),basis,f,before,effect));
  }
  public static async Task<CommandResult<ResourcePlanningPreview>> PreviewAsync(IAuditSphereDbContext db,ActorContext a,ResourcePlanningCommandRequest? request,CancellationToken ct=default)
  {
    var f=Canonical(request?.Fields);
    if(request is null || request.RequestId==Guid.Empty || f is null) return Invalid<ResourcePlanningPreview>();
    if(!(await Authorize(db,a,ct)).Succeeded) return Denied<ResourcePlanningPreview>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(!await LockAsync(db,a,f,ct)) return Denied<ResourcePlanningPreview>();
    var result=await BuildAsync(db,a,request.RequestId,f,ct);
    await tx.CommitAsync(ct);return result;
  }
  public static async Task<CommandResult<ResourcePlanningReceiptView>> ExecuteAsync(IAuditSphereDbContext db,ActorContext a,ResourcePlanningCommandRequest? request,CancellationToken ct=default)
  {
    var f=Canonical(request?.Fields);
    if(request is null || request.RequestId==Guid.Empty || f is null || !request.Reviewed || !Hash(request.RequestHash) || !Hash(request.ReviewBasis) || request.RequestHash!=RequestHash(a,request.RequestId,f)) return Invalid<ResourcePlanningReceiptView>();
    if(!(await Authorize(db,a,ct)).Succeeded) return Denied<ResourcePlanningReceiptView>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    // Lock the request namespace before changed-state validation. A matching committed action is recoverable even after target state changed.
    if(!await LockRequestAsync(db,a,ct)) return Denied<ResourcePlanningReceiptView>();
    var prior=await db.ResourcePlanningReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ActorId==a.UserId && x.RequestId==request.RequestId,ct);
    if(prior is not null) {
      if(prior.RequestHash!=request.RequestHash || prior.ReviewBasis!=request.ReviewBasis) return CommandResult<ResourcePlanningReceiptView>.Fail(ErrorCodes.StaleRevision,"This request belongs to a different reviewed intent.");
      await tx.CommitAsync(ct);return CommandResult<ResourcePlanningReceiptView>.Ok(Receipt(prior));
    }
    if(!await LockAsync(db,a,f,ct)) return Denied<ResourcePlanningReceiptView>();
    var preview=await BuildAsync(db,a,request.RequestId,f,ct);
    if(!preview.Succeeded) return CommandResult<ResourcePlanningReceiptView>.Fail(preview.ErrorCode!,preview.Message!);
    if(preview.Value!.ReviewBasis!=request.ReviewBasis) return CommandResult<ResourcePlanningReceiptView>.Fail(ErrorCodes.StaleRevision,"Planning inputs changed. Reload and review a new action.");
    var json=JsonSerializer.Serialize(preview.Value);
    if(json.Length>100000) return Invalid<ResourcePlanningReceiptView>();
    Guid? resource=null;
    CommandResult result;
    switch(f.Kind) {
      case "PROFILE":
        result=await ResourcePlanningService.SaveProfileAsync(db,a,new(f.UserId,f.Department!,f.Skills ?? "",f.WeeklyCapacityMinutes!.Value,decimal.Parse(f.TargetUtilizationPercent!,CultureInfo.InvariantCulture)),ct);
        if(result.Succeeded) resource=await db.StaffProfiles.Where(x=>x.FirmId==a.FirmId && x.UserId==f.UserId).Select(x=>x.Id).SingleAsync(ct);break;
      case "CERTIFICATION":
        var cert=await ResourcePlanningService.AddCertificationAsync(db,a,new(f.UserId,f.Name!,null,f.ExpiresOn),ct);
        result=cert.Succeeded?CommandResult.Ok():CommandResult.Fail(cert.ErrorCode!,cert.Message!);resource=cert.Succeeded?cert.Value:null;break;
      case "AVAILABILITY":
        var absence=await ResourcePlanningService.AddAvailabilityAsync(db,a,new(f.UserId,f.StartDate!.Value,f.EndDate!.Value,f.AvailabilityKind!,f.MinutesPerDay!.Value),ct);
        result=absence.Succeeded?CommandResult.Ok():CommandResult.Fail(absence.ErrorCode!,absence.Message!);resource=absence.Succeeded?absence.Value:null;break;
      default:
        result=await ResourcePlanningService.SetAllocationAsync(db,a,new(f.EngagementId!.Value,f.UserId,f.WeekStart!.Value,f.PlannedMinutes!.Value),ct);
        if(result.Succeeded) resource=await db.StaffAllocations.Where(x=>x.FirmId==a.FirmId && x.UserId==f.UserId && x.EngagementId==f.EngagementId && x.WeekStart==f.WeekStart).Select(x=>(Guid?)x.Id).SingleOrDefaultAsync(ct);break;
    }
    if(!result.Succeeded) return CommandResult<ResourcePlanningReceiptView>.Fail(result.ErrorCode!,result.Message!);
    var receipt=new ResourcePlanningReceipt{Id=Guid.CreateVersion7(),FirmId=a.FirmId,ActorId=a.UserId,ActorEpoch=a.SessionEpoch,TargetUserId=f.UserId,RequestId=request.RequestId,Kind=f.Kind,RequestHash=request.RequestHash!,ReviewBasis=request.ReviewBasis!,PreviewJson=json,ResourceId=resource,CreatedAt=DateTimeOffset.UtcNow};
    db.ResourcePlanningReceipts.Add(receipt);await db.SaveChangesAsync(ct);
    if(!(await Authorize(db,a,ct)).Succeeded) return Denied<ResourcePlanningReceiptView>();
    await tx.CommitAsync(ct);return CommandResult<ResourcePlanningReceiptView>.Ok(Receipt(receipt));
  }
  public static async Task<CommandResult<ResourcePlanningReceiptLookup>> LookupAsync(IAuditSphereDbContext db,ActorContext a,Guid requestId,string? requestHash,CancellationToken ct=default)
  {
    if(requestId==Guid.Empty || !Hash(requestHash)) return Invalid<ResourcePlanningReceiptLookup>();
    if(!(await Authorize(db,a,ct)).Succeeded) return Denied<ResourcePlanningReceiptLookup>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    if(!await LockRequestAsync(db,a,ct)) return Denied<ResourcePlanningReceiptLookup>();
    var r=await db.ResourcePlanningReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ActorId==a.UserId && x.RequestId==requestId,ct);
    if(r is not null && r.RequestHash!=requestHash) return Denied<ResourcePlanningReceiptLookup>();
    await tx.CommitAsync(ct);return CommandResult<ResourcePlanningReceiptLookup>.Ok(new(r is not null,r is null?null:Receipt(r)));
  }
}
