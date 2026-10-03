using System.Globalization;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Acceptance;

public sealed record EngagementActivationRequest(Guid RequestId, string ReviewBasis, bool Reviewed = false, string? ExpectedRequestHash = null);
public sealed record ActivationDecisionView(Guid Id, string Decision, string Path, string Generation, DateTimeOffset? DecidedAt);
public sealed record ActivationBlocker(string Code, string Message);
public sealed record EngagementActivationState(Guid EngagementId, Guid ClientId, string ClientName, string ServiceRoute,
  string ServiceProfileId, string PeriodStart, string PeriodEnd, string Status, string EngagementGeneration,
  string ClientGeneration, ActivationDecisionView? Decision, int ActiveHolds, bool Eligible, string ReviewBasis,
  IReadOnlyList<ActivationBlocker> Blockers);
public sealed record EngagementActivationPreview(Guid EngagementId, Guid RequestId, string ReviewBasis, string RequestHash);
public sealed record EngagementActivationReceipt(Guid Id, Guid EngagementId, Guid ClientId, Guid ActorId, Guid RequestId,
  string RequestHash, string ReviewBasis, Guid AcceptanceDecisionId, string AcceptancePath, string ClientGeneration,
  string EngagementGeneration, string ResultGeneration, DateTimeOffset ActivatedAt);
public sealed record EngagementActivationLookup(bool Found, EngagementActivationReceipt? Receipt);

/// <summary>Exact Partner review and recovery around the existing activation gate. No professional decision is inferred.</summary>
public static class EngagementActivationWorkspace
{
  private static Task<CommandResult> Authorize(IAuditSphereDbContext db, ActorContext a, Guid id, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, EngagementId:id, RequiredRoles:["Partner"], InternalOnly:true), ct);
  private static CommandResult<T> Unavailable<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied,"This activation workspace is unavailable.");
  private static bool HashValid(string? s) => s is {Length:64} && s.All(c=>c is >= 'a' and <= 'f' or >= '0' and <= '9');
  private static string Exact(long n) => n.ToString(CultureInfo.InvariantCulture);
  // A request identifies the fixed actor/session/target/action. A retry requires a fresh, exact review basis.
  // Changed acceptance cannot be approved silently; the actual committed basis remains in the immutable receipt.
  private static string RequestHash(ActorContext a, Guid id, Guid requestId) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId,a.UserId,a.SessionEpoch,id,requestId,Action="PARTNER_ACTIVATION" }));
  private static EngagementActivationReceipt Receipt(EngagementActivation row) => new(row.Id,row.EngagementId,row.PracticeClientId,
    row.ActivatedByUserId,row.RequestId!.Value,row.RequestHash!,row.ReviewBasis!,row.AcceptanceDecisionId,row.AcceptancePath,
    Exact(row.ClientGeneration),Exact(row.EngagementGeneration!.Value),Exact(row.ResultGeneration!.Value),row.ActivatedAt);

  public static async Task<CommandResult<EngagementActivationState>> StateAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, CancellationToken ct=default)
  {
    if (!(await Authorize(db,a,id,ct)).Succeeded) return Unavailable<EngagementActivationState>();
    var e=await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.Id==id,ct);
    if(e is null) return Unavailable<EngagementActivationState>();
    var name=await db.PracticeClients.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==e.PracticeClientId).Select(x=>x.LegalName).SingleOrDefaultAsync(ct);
    var generation=await db.ClientSafetyStates.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==e.PracticeClientId).Select(x=>(long?)x.InputGeneration).SingleOrDefaultAsync(ct);
    if(name is null || generation is null) return Unavailable<EngagementActivationState>();
    var d=await db.AcceptanceDecisions.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.PracticeClientId==e.PracticeClientId&&x.ServiceRoute==e.ServiceRoute&&x.Decision!="Pending")
      .OrderByDescending(x=>x.Generation).ThenByDescending(x=>x.DecidedAt).ThenByDescending(x=>x.Id)
      .Select(x=>new ActivationDecisionView(x.Id,x.Decision,x.Path,Exact(x.Generation),x.DecidedAt)).FirstOrDefaultAsync(ct);
    var holds=await db.EngagementHolds.AsNoTracking().CountAsync(x=>x.FirmId==a.FirmId&&x.EngagementId==id&&!x.Released,ct);
    var blockers=new List<ActivationBlocker>();
    if(e.Status!="Draft") blockers.Add(new("engagement.not-draft","Only a draft engagement can be activated. Inspect retained activation evidence if it is already active."));
    if(e.Generation==long.MaxValue) blockers.Add(new("engagement.revision-exhausted","Engagement revision capacity is exhausted. Contact an administrator."));
    if(d is null) blockers.Add(new("acceptance.missing","Record a Partner acceptance decision for this client and exact service route."));
    else {
      if(d.Generation!=Exact(generation.Value)) blockers.Add(new("acceptance.stale","Client evaluation changed. Record a current acceptance decision."));
      if(d.Decision!="Accepted") blockers.Add(new("acceptance.not-unconditional","Resolve conditions or adverse decisions through a new unconditional Partner decision."));
    }
    if(holds>0) blockers.Add(new("holds.active","Resolve every unreleased engagement hold before activation."));
    var basis=Hashing.Sha256Hex(JsonSerializer.Serialize(new {a.FirmId,a.UserId,a.SessionEpoch,id,e.PracticeClientId,name,e.ServiceRoute,
      e.ServiceProfileId,e.PeriodStart,e.PeriodEnd,e.Status,e.Generation,ClientGeneration=generation.Value,Decision=d,ActiveHolds=holds}));
    if (!(await Authorize(db,a,id,ct)).Succeeded) return Unavailable<EngagementActivationState>();
    return CommandResult<EngagementActivationState>.Ok(new(id,e.PracticeClientId,name,e.ServiceRoute,e.ServiceProfileId,e.PeriodStart,e.PeriodEnd,
      e.Status,Exact(e.Generation),Exact(generation.Value),d,holds,blockers.Count==0,basis,blockers));
  }
  public static async Task<CommandResult<EngagementActivationPreview>> PreviewAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, EngagementActivationRequest? r, CancellationToken ct=default)
  {
    if(r is null || r.RequestId==Guid.Empty || !HashValid(r.ReviewBasis))
      return CommandResult<EngagementActivationPreview>.Fail("request.invalid","Review the exact engagement and acceptance prerequisites.");
    var s=await StateAsync(db,a,id,ct);
    if(!s.Succeeded)return CommandResult<EngagementActivationPreview>.Fail(s.ErrorCode!,s.Message!);
    if(s.Value!.ReviewBasis!=r.ReviewBasis)return CommandResult<EngagementActivationPreview>.Fail(ErrorCodes.GenerationStale,"Activation context changed. Refresh and review again.");
    if(!s.Value.Eligible)return CommandResult<EngagementActivationPreview>.Fail(ErrorCodes.GateBlocked,"Resolve the displayed activation prerequisites first.");
    return CommandResult<EngagementActivationPreview>.Ok(new(id,r.RequestId,r.ReviewBasis,RequestHash(a,id,r.RequestId)));
  }
  public static async Task<CommandResult<EngagementActivationReceipt>> ExecuteAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, EngagementActivationRequest? r, CancellationToken ct=default)
  {
    if(r is null || !r.Reviewed || r.RequestId==Guid.Empty || !HashValid(r.ReviewBasis) || !HashValid(r.ExpectedRequestHash) || r.ExpectedRequestHash!=RequestHash(a,id,r.RequestId))
      return CommandResult<EngagementActivationReceipt>.Fail("request.invalid","Preview and explicitly confirm this exact activation.");
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementActivationReceipt>();
    var e=await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.Id==id,ct);
    if(e is null)return Unavailable<EngagementActivationReceipt>();
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    var guard=await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={e.PracticeClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if(guard is null)return Unavailable<EngagementActivationReceipt>();
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var prior=await db.EngagementActivations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActivatedByUserId==a.UserId&&x.RequestId==r.RequestId,ct);
    if(prior is not null) {
      if(prior.EngagementId!=id || prior.RequestHash!=r.ExpectedRequestHash)return CommandResult<EngagementActivationReceipt>.Fail(ErrorCodes.IdempotencyConflict,"This request belongs to a different activation intent.");
      if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementActivationReceipt>();
      await tx.CommitAsync(ct);return CommandResult<EngagementActivationReceipt>.Ok(Receipt(prior));
    }
    var result=await EngagementLifecycleService.ActivateAsync(db,a,id,ct,r);
    if(!result.Succeeded)return CommandResult<EngagementActivationReceipt>.Fail(result.ErrorCode!,result.Message!);
    var row=await db.EngagementActivations.AsNoTracking().SingleAsync(x=>x.FirmId==a.FirmId&&x.Id==result.Value,ct);
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementActivationReceipt>();
    await tx.CommitAsync(ct);return CommandResult<EngagementActivationReceipt>.Ok(Receipt(row));
  }
  public static async Task<CommandResult<EngagementActivationLookup>> LookupAsync(IAuditSphereDbContext db, ActorContext a,
    Guid id, Guid requestId, string? requestHash, CancellationToken ct=default)
  {
    if(requestId==Guid.Empty || !HashValid(requestHash) || !(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementActivationLookup>();
    var client=await db.Engagements.AsNoTracking().Where(x=>x.FirmId==a.FirmId&&x.Id==id).Select(x=>x.PracticeClientId).SingleAsync(ct);
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    // Wait for a dispatched activation's guarded transaction before reporting an absent receipt.
    await db.ClientSafetyStates.FromSqlInterpolated($"SELECT * FROM client_safety_states WHERE firm_id={a.FirmId} AND id={client} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var row=await db.EngagementActivations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActivatedByUserId==a.UserId&&x.RequestId==requestId,ct);
    if(row is not null && (row.EngagementId!=id || row.RequestHash!=requestHash))return CommandResult<EngagementActivationLookup>.Fail(ErrorCodes.IdempotencyConflict,"This reference belongs to a different activation intent.");
    if(!(await Authorize(db,a,id,ct)).Succeeded)return Unavailable<EngagementActivationLookup>();
    await tx.CommitAsync(ct);return CommandResult<EngagementActivationLookup>.Ok(new(row is not null,row is null?null:Receipt(row)));
  }
}
