using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>Original workpaper command acknowledgements; current draft history is never used as a substitute receipt.</summary>
public static class WorkpaperCommandWorkspace
{
  private static CommandResult<T> Fail<T>(string code,string message)=>CommandResult<T>.Fail(code,message);
  private static string L(long n)=>n.ToString(CultureInfo.InvariantCulture);
  private static bool Hash(string? s)=>s is {Length:64} && s.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
  private static bool Number(string? s,bool zero=false)=>long.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out var n) && n >= (zero?0:1) && L(n)==s;
  private static long N(string s)=>long.Parse(s,CultureInfo.InvariantCulture);
  private static bool Text(string? s,int max)=>s is not null && s.Trim().Length<=max;
  internal static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db,ActorContext a,Workpaper w,bool write,CancellationToken ct)=>
    AuthorizationDecision.AuthorizeAsync(db,a,new(a.FirmId,w.ClientId,w.EngagementId,AuditPlanningService.PlanningRoles,InternalOnly:true,RequireProfessionalWork:write),ct);
  private static WorkpaperCommandFields? Canonical(WorkpaperCommandFields? f,bool legacy)=>f?.Action switch
  {
    WorkpaperCommandActions.SaveDraft when f.Save is {} s && f.Submit is null && f.Discard is null && Number(s.ExpectedDraftRevision,true) && Number(s.BaseWorkpaperRevision) && Number(s.BaseInputGeneration) && Number(s.BasePolicyGeneration) && Text(s.WorkPerformed,100000) && Text(s.Conclusion,20000)=>
      f with{Save=s with{WorkPerformed=s.WorkPerformed.Trim(),Conclusion=s.Conclusion.Trim()}},
    WorkpaperCommandActions.DiscardDraft when f.Discard is {} d && f.Save is null && f.Submit is null && Number(d.ExpectedDraftRevision)=>f,
    WorkpaperCommandActions.Submit when f.Submit is {} s && f.Save is null && f.Discard is null && Number(s.ExpectedRevision) &&
      (Number(s.ExpectedDraftRevision) && s.DraftSaveId is {} id && id!=Guid.Empty || legacy && (s.ExpectedDraftRevision is null || Number(s.ExpectedDraftRevision)) && (s.DraftSaveId is null || s.DraftSaveId!=Guid.Empty)) &&
      Text(s.WorkPerformed,100000) && Text(s.Conclusion,20000) && !string.IsNullOrWhiteSpace(s.WorkPerformed) && !string.IsNullOrWhiteSpace(s.Conclusion)=>
      f with{Submit=s with{WorkPerformed=s.WorkPerformed.Trim(),Conclusion=s.Conclusion.Trim()}},
    _=>null
  };
  private static string RequestHash(ActorContext a,Workpaper w,WorkpaperCommandRequest r,WorkpaperCommandFields f,bool legacy)=>Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {Version=1,a.FirmId,a.UserId,a.SessionEpoch,w.ClientId,w.EngagementId,WorkpaperId=w.Id,r.RequestId,Fields=f,ReviewBasis=f.Action==WorkpaperCommandActions.SaveDraft?null:r.ReviewBasis,Legacy=f.Action==WorkpaperCommandActions.SaveDraft?false:legacy}));
  private static async Task BarrierAsync(IAuditSphereDbContext db,ActorContext a,Guid requestId,bool write,CancellationToken ct)
  {
    var hash=Hashing.Sha256Hex(JsonSerializer.Serialize(new{Namespace="workpaper-command-v1",a.FirmId,a.UserId,requestId}));var key=unchecked((long)Convert.ToUInt64(hash[..16],16));
    if(write)await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})",ct);
    else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock_shared({key})",ct);
  }
  internal static WorkpaperDraftView DraftView(WorkpaperDraftResult d)=>new(d.WorkpaperId,d.DraftId,L(d.BaseWorkpaperRevision),L(d.BaseInputGeneration),L(d.BasePolicyGeneration),L(d.DraftRevision),d.WorkPerformed,d.Conclusion,d.LastSaveId,d.LastSavedAt?.ToUniversalTime(),d.Lifecycle);
  private static WorkpaperOwnedDraft? Draft(WorkpaperDraft? d)=>d is null?null:new(d.Id,L(d.BaseWorkpaperRevision),L(d.BaseInputGeneration),L(d.BasePolicyGeneration),L(d.DraftRevision),d.LastSaveId,d.WorkPerformed,d.Conclusion,d.Lifecycle,d.LastSavedAt.ToUniversalTime());
  private static async Task<CommandResult<WorkpaperCommandSnapshot>> SnapshotAsync(IAuditSphereDbContext db,ActorContext a,Workpaper w,CancellationToken ct)
  {
    var client=await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==w.ClientId,ct);
    var firm=await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==a.FirmId,ct);
    if(client is null || firm is null || client.InputGeneration<1 || firm.PolicyGeneration<1)return Fail<WorkpaperCommandSnapshot>(ErrorCodes.GateBlocked,"Workpaper safety state is unavailable.");
    var d=await db.WorkpaperDrafts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ClientId==w.ClientId && x.EngagementId==w.EngagementId && x.WorkpaperId==w.Id && x.OwnerUserId==a.UserId,ct);
    return CommandResult<WorkpaperCommandSnapshot>.Ok(new(w.Id,w.ClientId,w.EngagementId,w.Index,w.Title,w.ProcedureId,L(w.Revision),w.Status,L(client.InputGeneration),L(firm.PolicyGeneration),w.WorkPerformed,w.Conclusion,w.SubmittedAt?.ToUniversalTime(),Draft(d)));
  }
  public static async Task<CommandResult<WorkpaperCommandState>> StateAsync(IAuditSphereDbContext db,ActorContext a,Guid id,CancellationToken ct=default)
  {
    var w=await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if(w is null)return Fail<WorkpaperCommandState>(ErrorCodes.ScopeDenied,"Access denied.");
    var auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandState>(auth.ErrorCode!,auth.Message!);
    var source=await SnapshotAsync(db,a,w,ct);if(!source.Succeeded)return Fail<WorkpaperCommandState>(source.ErrorCode!,source.Message!);
    var blockers=new List<string>();var write=await AuthorizeAsync(db,a,w,true,ct);if(!write.Succeeded)blockers.Add(write.Message??"Professional work is blocked.");
    if(await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x=>x.FirmId==a.FirmId && x.EngagementId==w.EngagementId && x.State==AuditSphereOps.Domain.Records.FileFreezeStates.Frozen,ct))blockers.Add("The frozen original workpaper remains read-only.");
    var latest=await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if(latest is null)return Fail<WorkpaperCommandState>(ErrorCodes.ScopeDenied,"Access denied.");
    var end=await SnapshotAsync(db,a,latest,ct);if(!end.Succeeded)return Fail<WorkpaperCommandState>(end.ErrorCode!,end.Message!);
    if(JsonSerializer.Serialize(source.Value)!=JsonSerializer.Serialize(end.Value))return Fail<WorkpaperCommandState>(ErrorCodes.StaleRevision,"The workpaper or draft changed while its state was read.");
    auth=await AuthorizeAsync(db,a,latest,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandState>(auth.ErrorCode!,auth.Message!);
    var s=source.Value!;var json=JsonSerializer.Serialize(new{Version=1,a.FirmId,a.UserId,a.SessionEpoch,ClientId=w.ClientId,EngagementId=w.EngagementId,Workpaper=s,Blockers=blockers});
    var active=s.Draft?.Lifecycle==WorkpaperDraftLifecycles.Active;var working=w.Status==WorkpaperStatuses.Working;
    var currentBasis=active && s.Draft!.BaseWorkpaperRevision==s.Revision && s.Draft.BaseInputGeneration==s.InputGeneration && s.Draft.BasePolicyGeneration==s.PolicyGeneration;
    return CommandResult<WorkpaperCommandState>.Ok(new(s,Hashing.Sha256Hex(json),blockers.Count==0 && working && s.Draft?.Lifecycle!=WorkpaperDraftLifecycles.Consumed && s.Draft?.DraftRevision!=L(long.MaxValue),blockers.Count==0 && active,blockers.Count==0 && working && currentBasis && w.Revision<long.MaxValue,blockers,json));
  }
  public static async Task<CommandResult<WorkpaperSaveIntent>> PrepareSaveAsync(IAuditSphereDbContext db,ActorContext a,Guid id,WorkpaperCommandRequest? r,CancellationToken ct=default)
  {
    var f=Canonical(r?.Fields,false);
    if(r is null || r.RequestId==Guid.Empty || f?.Save is not {} save || f.Action!=WorkpaperCommandActions.SaveDraft || JsonSerializer.Serialize(f).Length>WorkpaperCommandReceipt.MaximumInputLength)
      return Fail<WorkpaperSaveIntent>(ErrorCodes.Drafts.Conflict,"Prepare the exact bounded draft save intent.");
    var state=await StateAsync(db,a,id,ct);if(!state.Succeeded)return Fail<WorkpaperSaveIntent>(state.ErrorCode!,state.Message!);
    var s=state.Value!;var w=s.Workpaper;
    if(!s.CanSaveDraft)return Fail<WorkpaperSaveIntent>(ErrorCodes.GateBlocked,s.Blockers.FirstOrDefault()??"This draft cannot be saved.");
    if(save.BaseWorkpaperRevision!=w.Revision || save.BaseInputGeneration!=w.InputGeneration || save.BasePolicyGeneration!=w.PolicyGeneration ||
      (w.Draft?.Lifecycle==WorkpaperDraftLifecycles.Active && save.ExpectedDraftRevision!=w.Draft.DraftRevision) ||
      (w.Draft?.Lifecycle==WorkpaperDraftLifecycles.Discarded && save.ExpectedDraftRevision!="0") || (w.Draft is null && save.ExpectedDraftRevision is not ("0" or "1")))
      return Fail<WorkpaperSaveIntent>(ErrorCodes.StaleRevision,"The save basis changed; reload before preparing a new save.");
    var target=new Workpaper{Id=id,FirmId=a.FirmId,ClientId=w.ClientId,EngagementId=w.EngagementId};
    var auth=await AuthorizeAsync(db,a,target,true,ct);if(!auth.Succeeded)return Fail<WorkpaperSaveIntent>(auth.ErrorCode!,auth.Message!);
    return CommandResult<WorkpaperSaveIntent>.Ok(new(r.RequestId,RequestHash(a,target,r,f,false),f));
  }
  public static async Task<CommandResult<WorkpaperCommandPreview>> PreviewAsync(IAuditSphereDbContext db,ActorContext a,Guid id,WorkpaperCommandRequest? r,CancellationToken ct=default)
  {
    var f=Canonical(r?.Fields,false);
    if(r is null || r.RequestId==Guid.Empty || f is null || !Hash(r.ReviewBasis) || f.Action==WorkpaperCommandActions.SaveDraft)return Fail<WorkpaperCommandPreview>(ErrorCodes.Drafts.Conflict,"Review the exact submit or discard intent.");
    var state=await StateAsync(db,a,id,ct);if(!state.Succeeded)return Fail<WorkpaperCommandPreview>(state.ErrorCode!,state.Message!);
    var s=state.Value!;if(s.ReviewBasis!=r.ReviewBasis)return Fail<WorkpaperCommandPreview>(ErrorCodes.StaleRevision,"The workpaper, draft or safety basis changed; review again.");
    var d=s.Workpaper.Draft;
    if(d is null || d.Lifecycle!=WorkpaperDraftLifecycles.Active || d.DraftRevision!=(f.Submit?.ExpectedDraftRevision??f.Discard!.ExpectedDraftRevision))return Fail<WorkpaperCommandPreview>(ErrorCodes.StaleRevision,"The acknowledged draft changed.");
    if(f.Submit is {} sub && (sub.ExpectedRevision!=s.Workpaper.Revision || sub.DraftSaveId!=d.LastSaveId || sub.WorkPerformed!=d.WorkPerformed || sub.Conclusion!=d.Conclusion ||
      d.BaseWorkpaperRevision!=s.Workpaper.Revision || d.BaseInputGeneration!=s.Workpaper.InputGeneration || d.BasePolicyGeneration!=s.Workpaper.PolicyGeneration))return Fail<WorkpaperCommandPreview>(ErrorCodes.Drafts.TargetChanged,"Submission must freeze the exact acknowledged draft on its current basis.");
    var w=new Workpaper{Id=id,FirmId=a.FirmId,ClientId=s.Workpaper.ClientId,EngagementId=s.Workpaper.EngagementId};
    var blockers=s.Blockers.ToList();var canProceed=f.Action==WorkpaperCommandActions.Submit?s.CanSubmit:s.CanDiscardDraft;
    if(!canProceed && blockers.Count==0)blockers.Add("The current workpaper or owned draft cannot accept this final action.");
    var preview=new WorkpaperCommandPreview(r.RequestId,RequestHash(a,w,r,f,false),r.ReviewBasis!,f,s.Workpaper,canProceed,blockers,
      f.Action==WorkpaperCommandActions.Submit?"Freeze the exact acknowledged work performed and conclusion into an immutable submission.":"Discard this exact owned draft revision; its original content remains retained.");
    if(JsonSerializer.Serialize(preview).Length>WorkpaperCommandReceipt.MaximumPreviewLength)return Fail<WorkpaperCommandPreview>(ErrorCodes.Drafts.Conflict,"The retained preview exceeds its evidence limit.");
    var final=await StateAsync(db,a,id,ct);if(!final.Succeeded)return Fail<WorkpaperCommandPreview>(final.ErrorCode!,final.Message!);
    if(final.Value!.ReviewBasis!=r.ReviewBasis)return Fail<WorkpaperCommandPreview>(ErrorCodes.StaleRevision,"The source changed during preview.");
    return CommandResult<WorkpaperCommandPreview>.Ok(preview);
  }
  private static WorkpaperCommandSnapshot Read(string json)
  {
    var s=JsonSerializer.Deserialize<WorkpaperCommandSnapshot>(json)!;
    return s with{SubmittedAt=s.SubmittedAt?.ToUniversalTime(),Draft=s.Draft is {} d?d with{LastSavedAt=d.LastSavedAt.ToUniversalTime()}:null};
  }
  private static async Task<WorkpaperCommandReceiptView> ViewAsync(IAuditSphereDbContext db,WorkpaperCommandReceipt r,CancellationToken ct)
  {
    var e=await db.WorkpaperCommandEvents.AsNoTracking().SingleAsync(x=>x.Id==r.EventId && x.FirmId==r.FirmId && x.ClientId==r.ClientId && x.EngagementId==r.EngagementId && x.WorkpaperId==r.WorkpaperId,ct);
    return new(r.Id,r.WorkpaperId,r.ActorId,L(r.ActorEpoch),r.RequestId,r.RequestHash,r.Action,r.CreatedAt.ToUniversalTime(),r.PreviewJson is null?null:JsonSerializer.Deserialize<WorkpaperCommandPreview>(r.PreviewJson),
      new(e.Id,e.ActorId,L(e.ActorEpoch),e.Action,e.CreatedAt.ToUniversalTime(),Read(e.BeforeJson),Read(e.AfterJson)));
  }
  public static async Task<CommandResult<WorkpaperCommandLookup>> LookupAsync(IAuditSphereDbContext db,ActorContext a,Guid id,Guid requestId,string? requestHash,CancellationToken ct=default)
  {
    if(requestId==Guid.Empty || !Hash(requestHash))return Fail<WorkpaperCommandLookup>(ErrorCodes.Drafts.Conflict,"Use the exact retained request reference.");
    var w=await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);if(w is null)return Fail<WorkpaperCommandLookup>(ErrorCodes.ScopeDenied,"Access denied.");
    var auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandLookup>(auth.ErrorCode!,auth.Message!);
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    auth=await AuditPlanningCommandWorkspace.LockAsync(db,a,w.EngagementId,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandLookup>(auth.ErrorCode!,auth.Message!);
    await BarrierAsync(db,a,requestId,false,ct);auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandLookup>(auth.ErrorCode!,auth.Message!);
    var r=await db.WorkpaperCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ActorId==a.UserId && x.RequestId==requestId,ct);
    if(r is not null && r.ActorEpoch!=a.SessionEpoch)return Fail<WorkpaperCommandLookup>(ErrorCodes.GenerationStale,"This request belongs to a previous session lifetime.");
    if(r is not null && (r.WorkpaperId!=id || r.RequestHash!=requestHash))return Fail<WorkpaperCommandLookup>(ErrorCodes.IdempotencyConflict,"This request belongs to another exact intent.");
    var view=r is null?null:await ViewAsync(db,r,ct);auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandLookup>(auth.ErrorCode!,auth.Message!);
    await tx.CommitAsync(ct);return CommandResult<WorkpaperCommandLookup>.Ok(new(r is not null,view));
  }
  public static async Task<CommandResult<WorkpaperCommandLookup>> RecoverAsync(IAuditSphereDbContext db,ActorContext a,Guid id,WorkpaperCommandRequest? r,CancellationToken ct=default)
  {
    var f=Canonical(r?.Fields,false);
    if(r is null || r.RequestId==Guid.Empty || f is null || JsonSerializer.Serialize(f).Length>WorkpaperCommandReceipt.MaximumInputLength)
      return Fail<WorkpaperCommandLookup>(ErrorCodes.Drafts.Conflict,"Retain the exact original workpaper request.");
    var w=await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);
    if(w is null)return Fail<WorkpaperCommandLookup>(ErrorCodes.ScopeDenied,"Access denied.");
    return await LookupAsync(db,a,id,r.RequestId,RequestHash(a,w,r,f,false),ct);
  }
  public static Task<CommandResult<WorkpaperCommandReceiptView>> ExecuteAsync(IAuditSphereDbContext db,ActorContext a,Guid id,WorkpaperCommandRequest? r,CancellationToken ct=default)=>ExecuteCoreAsync(db,a,id,r,false,ct);
  private static async Task<CommandResult<WorkpaperCommandReceiptView>> ExecuteCoreAsync(IAuditSphereDbContext db,ActorContext a,Guid id,WorkpaperCommandRequest? r,bool legacy,CancellationToken ct)
  {
    var f=Canonical(r?.Fields,legacy);
    if(r is null || r.RequestId==Guid.Empty || f is null || JsonSerializer.Serialize(f).Length>WorkpaperCommandReceipt.MaximumInputLength ||
      (!legacy && (!Hash(r.ExpectedRequestHash) || f.Action!=WorkpaperCommandActions.SaveDraft && (!r.Reviewed || !Hash(r.ReviewBasis)))))return Fail<WorkpaperCommandReceiptView>(ErrorCodes.Drafts.Conflict,"Provide the exact bounded workpaper intent and reviewed final-action preview.");
    var w=await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.Id==id,ct);if(w is null)return Fail<WorkpaperCommandReceiptView>(ErrorCodes.ScopeDenied,"Access denied.");
    var auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandReceiptView>(auth.ErrorCode!,auth.Message!);
    await using var tx=db.Database.CurrentTransaction is null?await db.Database.BeginTransactionAsync(ct):null;
    // A command savepoint protects the caller's ambient transaction from partial procedure/draft/submission publication.
    var currentTx=db.Database.CurrentTransaction!;var savepoint="workpaper_command_"+Guid.NewGuid().ToString("N");await currentTx.CreateSavepointAsync(savepoint,ct);var savepointActive=true;
    try
    {
      auth=await AuditPlanningCommandWorkspace.LockAsync(db,a,w.EngagementId,true,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandReceiptView>(auth.ErrorCode!,auth.Message!);
      await BarrierAsync(db,a,r.RequestId,true,ct);auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandReceiptView>(auth.ErrorCode!,auth.Message!);
      var hash=RequestHash(a,w,r,f,legacy);
      var old=await db.WorkpaperCommandReceipts.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId && x.ActorId==a.UserId && x.RequestId==r.RequestId,ct);
      if(old is not null)
      {
        if(old.ActorEpoch!=a.SessionEpoch)return Fail<WorkpaperCommandReceiptView>(ErrorCodes.GenerationStale,"This request belongs to a previous session lifetime.");
        if(old.WorkpaperId!=id || old.Action!=f.Action || old.RequestHash!=hash || (!legacy && old.RequestHash!=r.ExpectedRequestHash))
        {
          // The retained successful submission proves the original is frozen. Preserve the
          // legacy caller's refusal classification without moving write gates ahead of replay.
          if(legacy && f.Action==WorkpaperCommandActions.Submit && old.WorkpaperId==id && old.Action==WorkpaperCommandActions.Submit)
            return Fail<WorkpaperCommandReceiptView>(ErrorCodes.ProtectedState,"A submitted workpaper cannot be changed; a corrected conclusion requires a new workpaper revision.");
          return Fail<WorkpaperCommandReceiptView>(ErrorCodes.IdempotencyConflict,"Changed intent cannot reuse this request identity.");
        }
        var replay=await ViewAsync(db,old,ct);auth=await AuthorizeAsync(db,a,w,false,ct);if(!auth.Succeeded)return Fail<WorkpaperCommandReceiptView>(auth.ErrorCode!,auth.Message!);
        await currentTx.ReleaseSavepointAsync(savepoint,ct);savepointActive=false;if(tx is not null)await tx.CommitAsync(ct);return CommandResult<WorkpaperCommandReceiptView>.Ok(replay);
      }
      if(!legacy && hash!=r.ExpectedRequestHash)return Fail<WorkpaperCommandReceiptView>(ErrorCodes.IdempotencyConflict,"The submitted intent differs from its exact preview hash.");
      var scope=await AuditPlanningService.LockedEngagementAsync(db,a,w.EngagementId,ct,w.ClientId);if(scope.Denied is not null)return Fail<WorkpaperCommandReceiptView>(scope.Denied,scope.Message);
      if(await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x=>x.FirmId==a.FirmId && x.EngagementId==w.EngagementId && x.State==AuditSphereOps.Domain.Records.FileFreezeStates.Frozen,ct))
        return Fail<WorkpaperCommandReceiptView>(ErrorCodes.ProtectedState,"The frozen original workpaper remains read-only.");
      w=await db.Workpapers.FromSqlInterpolated($"SELECT * FROM workpapers WHERE firm_id={a.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleAsync(ct);
      WorkpaperCommandPreview? preview=null;
      if(!legacy && f.Action!=WorkpaperCommandActions.SaveDraft){var p=await PreviewAsync(db,a,id,r with{Fields=f},ct);if(!p.Succeeded)return Fail<WorkpaperCommandReceiptView>(p.ErrorCode!,p.Message!);if(!p.Value!.CanProceed)return Fail<WorkpaperCommandReceiptView>(ErrorCodes.GateBlocked,p.Value.Blockers.First());preview=p.Value;}
      var eventId=Guid.CreateVersion7();var actor=a.UserId.ToString();var epoch=L(a.SessionEpoch);var eid=eventId.ToString();var rid=r.RequestId.ToString();var legacyText=legacy?"true":"false";
      await db.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('audit.workpaper_event_id',{eid},true),set_config('audit.workpaper_actor_id',{actor},true),set_config('audit.workpaper_actor_epoch',{epoch},true),set_config('audit.workpaper_request_id',{rid},true),set_config('audit.workpaper_request_hash',{hash},true),set_config('audit.workpaper_action',{f.Action},true),set_config('audit.workpaper_legacy',{legacyText},true)",ct);
      CommandResult result;
      if(f.Save is {} s){var saved=await AuditPlanningService.SaveWorkpaperDraftCoreAsync(db,a,new(id,N(s.ExpectedDraftRevision),N(s.BaseWorkpaperRevision),N(s.BaseInputGeneration),N(s.BasePolicyGeneration),r.RequestId,s.WorkPerformed,s.Conclusion),ct);result=saved.Succeeded?CommandResult.Ok():CommandResult.Fail(saved.ErrorCode!,saved.Message!);}
      else if(f.Submit is {} sub){var submitted=await AuditPlanningService.SubmitWorkpaperCoreAsync(db,a,new(id,N(sub.ExpectedRevision),sub.WorkPerformed,sub.Conclusion,sub.ExpectedDraftRevision is null?null:N(sub.ExpectedDraftRevision),sub.DraftSaveId),ct);result=submitted.Succeeded?CommandResult.Ok():CommandResult.Fail(submitted.ErrorCode!,submitted.Message!);}
      else result=await AuditPlanningService.DiscardWorkpaperDraftCoreAsync(db,a,id,N(f.Discard!.ExpectedDraftRevision),ct);
      if(!result.Succeeded){await currentTx.RollbackToSavepointAsync(savepoint,CancellationToken.None);DetachPublication(db);return Fail<WorkpaperCommandReceiptView>(result.ErrorCode!,result.Message!);}
      var e=await db.WorkpaperCommandEvents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==eventId && x.FirmId==a.FirmId,ct);
      if(e is null){await currentTx.RollbackToSavepointAsync(savepoint,CancellationToken.None);DetachPublication(db);return Fail<WorkpaperCommandReceiptView>(ErrorCodes.IdempotencyConflict,"A pre-receipt save cannot fabricate historical acknowledgement; use a fresh save identity for new work.");}
      var row=new WorkpaperCommandReceipt{Id=Guid.CreateVersion7(),FirmId=a.FirmId,ClientId=w.ClientId,EngagementId=w.EngagementId,WorkpaperId=id,EventId=e.Id,ActorId=a.UserId,ActorEpoch=a.SessionEpoch,RequestId=r.RequestId,RequestHash=hash,Action=f.Action,InputJson=JsonSerializer.Serialize(f),PreviewJson=preview is null?null:JsonSerializer.Serialize(preview),CreatedAt=DateTimeOffset.UtcNow};
      db.WorkpaperCommandReceipts.Add(row);await db.SaveChangesAsync(ct);
      var stored=await db.WorkpaperCommandReceipts.AsNoTracking().SingleAsync(x=>x.Id==row.Id && x.FirmId==a.FirmId,ct);var view=await ViewAsync(db,stored,ct);
      auth=await AuthorizeAsync(db,a,w,true,ct);if(!auth.Succeeded){await currentTx.RollbackToSavepointAsync(savepoint,CancellationToken.None);DetachPublication(db);return Fail<WorkpaperCommandReceiptView>(auth.ErrorCode!,auth.Message!);}
      await currentTx.ReleaseSavepointAsync(savepoint,ct);savepointActive=false;if(tx is not null)await tx.CommitAsync(ct);return CommandResult<WorkpaperCommandReceiptView>.Ok(view);
    }
    catch(Exception ex)
    {
      if(savepointActive)await currentTx.RollbackToSavepointAsync(savepoint,CancellationToken.None);
      DetachPublication(db);
      if(ex.GetBaseException() is DbException cause && cause.GetType().GetProperty("SqlState")?.GetValue(cause) is "40001" or "40P01" or "23505")return Fail<WorkpaperCommandReceiptView>(ErrorCodes.IdempotencyConflict,"Concurrent publication changed the outcome; recover the retained request before retrying.");
      throw;
    }
  }
  private static void DetachPublication(IAuditSphereDbContext db)
  {
    if(db is DbContext context)foreach(var entry in context.ChangeTracker.Entries().Where(x=>x.Entity is WorkpaperDraft or WorkpaperSubmission or WorkpaperCommandReceipt).ToArray())entry.State=EntityState.Detached;
  }
  private static Guid CompatibilityId(ActorContext a,Guid id,string action,string revision)=>new(Convert.FromHexString(Hashing.Sha256Hex(JsonSerializer.Serialize(new{Namespace="workpaper-compatibility-v1",a.FirmId,a.UserId,WorkpaperId=id,Action=action,Revision=revision}))[..32]));
  internal static async Task<CommandResult<WorkpaperDraftResult>> SaveLegacyAsync(IAuditSphereDbContext db,ActorContext a,SaveWorkpaperDraftRequest r,CancellationToken ct)
  {
    var result=await ExecuteCoreAsync(db,a,r.WorkpaperId,new(r.SaveId,new(WorkpaperCommandActions.SaveDraft,Save:new(L(r.ExpectedDraftRevision),L(r.BaseWorkpaperRevision),L(r.BaseInputGeneration),L(r.BasePolicyGeneration),r.WorkPerformed,r.Conclusion))),true,ct);
    if(!result.Succeeded)return Fail<WorkpaperDraftResult>(result.ErrorCode!,result.Message!);var d=result.Value!.CommandEvent.After.Draft!;
    return CommandResult<WorkpaperDraftResult>.Ok(new(r.WorkpaperId,d.Id,N(d.BaseWorkpaperRevision),N(d.BaseInputGeneration),N(d.BasePolicyGeneration),N(d.DraftRevision),d.WorkPerformed,d.Conclusion,d.LastSaveId,d.LastSavedAt,d.Lifecycle));
  }
  internal static async Task<CommandResult<WorkpaperResult>> SubmitLegacyAsync(IAuditSphereDbContext db,ActorContext a,SubmitWorkpaperRequest r,CancellationToken ct)
  {
    var id=CompatibilityId(a,r.WorkpaperId,WorkpaperCommandActions.Submit,L(r.ExpectedRevision));
    var result=await ExecuteCoreAsync(db,a,r.WorkpaperId,new(id,new(WorkpaperCommandActions.Submit,Submit:new(L(r.ExpectedRevision),r.ExpectedDraftRevision is {} n?L(n):null,r.DraftSaveId,r.WorkPerformed,r.Conclusion))),true,ct);
    if(!result.Succeeded)return Fail<WorkpaperResult>(result.ErrorCode!,result.Message!);var w=result.Value!.CommandEvent.After;return CommandResult<WorkpaperResult>.Ok(new(w.WorkpaperId,w.Index,w.Title,w.Status,N(w.Revision)));
  }
  internal static async Task<CommandResult> DiscardLegacyAsync(IAuditSphereDbContext db,ActorContext a,Guid id,long revision,CancellationToken ct)
  {
    var result=await ExecuteCoreAsync(db,a,id,new(CompatibilityId(a,id,WorkpaperCommandActions.DiscardDraft,L(revision)),new(WorkpaperCommandActions.DiscardDraft,Discard:new(L(revision)))),true,ct);
    return result.Succeeded?CommandResult.Ok():CommandResult.Fail(result.ErrorCode!,result.Message!);
  }
}
