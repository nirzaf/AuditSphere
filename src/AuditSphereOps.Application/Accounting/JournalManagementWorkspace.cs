using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record JournalManagementRequest(Guid RequestId, string ReviewBasis, string Decision,
  string Reason, string EvidenceReference, bool Reviewed);
public sealed record JournalManagementDecisionView(Guid Id, long JournalRevision, string Decision,
  string EvidenceMode, string EvidenceReference, Guid? DecidedByUserId, DateTimeOffset DecidedAt);
public sealed record JournalManagementView(Guid JournalId, Guid ClientId, Guid EngagementId,
  string JournalNumber, long Revision, string Status, string Purpose, Guid DatasetId, long DatasetRevision,
  string DatasetDigest, string PeriodStart, string PeriodEnd, string Currency, string Reason,
  string EvidenceReference, IReadOnlyList<JournalLineView> Lines, decimal TotalDebit, decimal TotalCredit,
  string ReviewBasis, string EvidenceMode, bool CanDecide, string? Blocker, JournalManagementDecisionView? Management);
public sealed record JournalManagementPreview(string ReviewBasis, string RequestHash, string Decision,
  string EvidenceMode, string Reason, string EvidenceReference, bool CanProceed, string? Blocker);
public sealed record JournalManagementReceipt(JournalActionReceipt Action, JournalManagementDecisionView Management);
public sealed record JournalManagementLookup(bool Found, JournalManagementReceipt? Receipt);
public sealed record PortalJournalItem(Guid Id, string Number, long Revision, string Purpose, string Status, string Currency);
public sealed record PortalJournalPage(IReadOnlyList<PortalJournalItem> Items, int Page, bool HasMore);

/// <summary>Management authority is a separate, exact revision decision. Evidence mode is
/// derived from the current identity, never chosen by a browser. Technical state and lines stay unchanged.</summary>
public static class JournalManagementWorkspace
{
  private static readonly string[] StaffReaders = ["Administrator", "AccountingPreparer", "AccountingReviewer", "Staff", "Manager", "Partner"];
  private static readonly string[] StaffWriters = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner"];
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static JournalManagementDecisionView Decision(AdjustmentJournalManagementDecision d) =>
    new(d.Id, d.JournalRevision, d.Decision, d.EvidenceMode, d.EvidenceReference, d.DecidedByUserId, d.DecidedAt);
  private static Task<CommandResult> Auth(IClientAccountingDbContext db, ActorContext actor, AdjustmentJournal j, bool client, bool write, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, j.ClientId, j.EngagementId,
      client ? ["ClientUser"] : write ? StaffWriters : StaffReaders, InternalOnly: !client, RequireProfessionalWork: true), ct);
  private sealed record Snapshot(JournalManagementView View, AdjustmentJournal Journal, JournalRevisionSnapshot Revision);
  private sealed record Retained(JournalRevisionSnapshot Journal, JournalManagementDecisionView Management);
  private static async Task<CommandResult<Snapshot>> ReadAsync(IClientAccountingDbContext db,
    IAdjustmentJournalDbContext evidenceDb, ActorContext actor, Guid id, bool client, CancellationToken ct)
  {
    var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.UserId && x.FirmId == actor.FirmId, ct);
    if (user is null || client != user.UserKind.Equals("Client", StringComparison.OrdinalIgnoreCase)) return Fail<Snapshot>(ErrorCodes.ScopeDenied, "Access denied.");
    var j = await db.AdjustmentJournals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (j is null || j.Purpose == AdjustmentJournalPurposes.GroupOnlyElimination) return Fail<Snapshot>(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await Auth(db, actor, j, client, false, ct);
    if (!auth.Succeeded) return Fail<Snapshot>(auth.ErrorCode!, auth.Message!);
    var source = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == j.BaseDatasetId && x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.EngagementId == j.EngagementId, ct);
    if (source is null) return Fail<Snapshot>(ErrorCodes.ScopeDenied, "Access denied.");
    var lines = await db.AdjustmentLines.AsNoTracking().Where(x => x.JournalId == id).OrderBy(x => x.AccountCode).ThenBy(x => x.Id)
      .Take(AdjustmentJournalWorkspace.MaximumLines + 1).Select(x => new JournalLineView(x.AccountCode, x.Debit, x.Credit)).ToListAsync(ct);
    if (lines.Count > AdjustmentJournalWorkspace.MaximumLines) return Fail<Snapshot>(ErrorCodes.GateBlocked, "This journal exceeds the interactive review limit.");
    var decision = await evidenceDb.AdjustmentJournalManagementDecisions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.EngagementId == j.EngagementId && x.JournalId == id && x.JournalRevision == j.Revision, ct);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == j.PeriodId && x.FirmId == actor.FirmId && x.ClientId == j.ClientId, ct);
    var book = await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == j.BookId && x.FirmId == actor.FirmId && x.ClientId == j.ClientId && x.PeriodId == j.PeriodId, ct);
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == j.ClientId && x.FirmId == actor.FirmId, ct);
    var frozen = await db.EngagementFileFreezes.AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == j.EngagementId && x.State == "FROZEN", ct);
    var firstSignIn = client ? await ClientPortalService.GetFirstSignInStatusAsync(db, actor, ct) : null;
    var write = await Auth(db, actor, j, client, true, ct);
    var blocker = !write.Succeeded ? "Current scoped management-evidence authority is required." :
      client && firstSignIn?.Completed != true ? "Complete the required first sign-in before recording a decision." :
      firm is null || safety is null ? "Safety state is unavailable." :
      source.SourceKind != "Raw" || source.ImportState != TrialBalanceImportStates.Sealed || source.ValidationStatus != "Accepted" || !source.Balanced || source.ControlTotal != 0m || !SourceAcceptanceWorkspace.ValidHash(MappedTrialBalanceSource.Digest(source)) ? "The exact source must be sealed, balanced and validated." :
      period is null || source.PeriodId != j.PeriodId || source.Currency != j.Currency || source.Basis != j.Basis || period.Currency != j.Currency || !string.Equals(period.Basis,j.Basis,StringComparison.OrdinalIgnoreCase) ? "The source reporting context is unbound or inconsistent." :
      period.Status is not ("ACTIVE" or "DRAFT") || j.BookId is not null && (book is null || book.Currency != j.Currency || !string.Equals(book.Basis,j.Basis,StringComparison.OrdinalIgnoreCase) || book.Status is not ("ACTIVE" or "DRAFT")) ? "The reporting period or book is closed or unavailable." :
      frozen ? "The engagement file is frozen. An approved amendment is required." :
      j.Status != "Draft" ? "Management decisions require the exact draft revision." :
      decision is not null ? "A management decision is already retained. A correction requires a new linked journal." :
      AdjustmentJournalService.CheckLines(lines.Select(x => (x.AccountCode,x.Debit,x.Credit)).ToArray());
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { actor.FirmId, actor.UserId, actor.SessionEpoch, client, j, source, lines, decision, period, book, firm, safety, frozen, firstSignIn, blocker }));
    auth = await Auth(db, actor, j, client, false, ct);
    if (!auth.Succeeded) return Fail<Snapshot>(auth.ErrorCode!, auth.Message!);
    return CommandResult<Snapshot>.Ok(new(new(id,j.ClientId,j.EngagementId,j.JournalNumber,j.Revision,j.Status,j.Purpose,
      source.Id,source.Revision,MappedTrialBalanceSource.Digest(source),period?.StartDate.ToString("yyyy-MM-dd") ?? "",period?.EndDate.ToString("yyyy-MM-dd") ?? "",
      j.Currency ?? source.Currency,j.Reason,j.EvidenceReference,lines,lines.Sum(x => x.Debit),lines.Sum(x => x.Credit),basis,
      client ? ManagementDecisionEvidenceModes.SignedIn : ManagementDecisionEvidenceModes.Offline,blocker is null,blocker,decision is null ? null : Decision(decision)),
      j,new(id,j.Revision,j.Status,j.Reason,j.EvidenceReference,lines)));
  }
  public static async Task<CommandResult<JournalManagementView>> GetAsync(IClientAccountingDbContext db, IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor, Guid id, bool client, CancellationToken ct = default)
  {
    var first = await ReadAsync(db,evidenceDb,actor,id,client,ct); if (!first.Succeeded) return Fail<JournalManagementView>(first.ErrorCode!,first.Message!);
    var final = await ReadAsync(db,evidenceDb,actor,id,client,ct); if (!final.Succeeded) return Fail<JournalManagementView>(final.ErrorCode!,final.Message!);
    return first.Value!.View.ReviewBasis == final.Value!.View.ReviewBasis ? CommandResult<JournalManagementView>.Ok(final.Value.View) : Fail<JournalManagementView>(ErrorCodes.StaleRevision,"The journal changed. Refresh and review again.");
  }
  private static bool Valid(JournalManagementRequest? r) => r is not null && r.RequestId != Guid.Empty && SourceAcceptanceWorkspace.ValidHash(r.ReviewBasis) &&
    ManagementDecisionStates.All.Contains(r.Decision ?? "") && r.Reason is { Length: > 0 and <= 4000 } && r.Reason.Trim().Length > 0 && r.EvidenceReference is { Length: > 0 and <= 2000 } && r.EvidenceReference.Trim().Length > 0;
  private static string Hash(ActorContext a, Guid id, bool client, JournalManagementRequest r) => Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId,a.UserId,JournalId=id,client,r.RequestId,r.ReviewBasis,r.Decision,r.Reason,r.EvidenceReference }));
  public static async Task<CommandResult<JournalManagementPreview>> PreviewAsync(IClientAccountingDbContext db,IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor,Guid id,bool client,JournalManagementRequest? request,CancellationToken ct=default)
  {
    if (!Valid(request)) return Fail<JournalManagementPreview>(ErrorCodes.Accounting.JournalRejected,"Provide a supported decision, exact review, rationale and bounded evidence.");
    var current = await GetAsync(db,evidenceDb,actor,id,client,ct); if(!current.Succeeded) return Fail<JournalManagementPreview>(current.ErrorCode!,current.Message!);
    var v=current.Value!;var r=request!;
    if(v.ReviewBasis!=r.ReviewBasis) return Fail<JournalManagementPreview>(ErrorCodes.StaleRevision,"The reviewed journal changed. Refresh before recording management evidence.");
    return CommandResult<JournalManagementPreview>.Ok(new(v.ReviewBasis,Hash(actor,id,client,r),r.Decision,v.EvidenceMode,r.Reason.Trim(),r.EvidenceReference.Trim(),v.CanDecide,v.Blocker));
  }
  private static JournalManagementReceipt Receipt(AdjustmentJournalAction e)
  {
    var d=JsonSerializer.Deserialize<Retained>(e.AfterJson) ?? throw new InvalidOperationException("Retained management evidence is unavailable.");
    return new(new(e.Id,e.RequestId,e.RequestHash,e.JournalId,e.ResultJournalId,e.Action,e.OldRevision,e.NewRevision,e.OldStatus,e.NewStatus,e.ActorId,e.Reason,e.EvidenceReference,e.CreatedAt),d.Management);
  }
  public static async Task<CommandResult<JournalManagementLookup>> LookupAsync(IClientAccountingDbContext db,IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor,Guid id,bool client,Guid requestId,string requestHash,CancellationToken ct=default)
  {
    var view=await GetAsync(db,evidenceDb,actor,id,client,ct);if(!view.Succeeded)return Fail<JournalManagementLookup>(view.ErrorCode!,view.Message!);
    if(requestId==Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(requestHash))return Fail<JournalManagementLookup>(ErrorCodes.Accounting.JournalRejected,"Use the exact pending request identity.");
    var e=await evidenceDb.AdjustmentJournalActions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId && x.ActorId==actor.UserId && x.RequestId==requestId,ct);
    if(e is not null && (e.JournalId!=id || e.Action!="MANAGEMENT" || e.RequestHash!=requestHash))return Fail<JournalManagementLookup>(ErrorCodes.IdempotencyConflict,"This request belongs to another intent.");
    var final=await GetAsync(db,evidenceDb,actor,id,client,ct);
    return final.Succeeded ? CommandResult<JournalManagementLookup>.Ok(new(e is not null,e is null ? null : Receipt(e))) : Fail<JournalManagementLookup>(final.ErrorCode!,final.Message!);
  }
  public static async Task<CommandResult<JournalManagementReceipt>> ExecuteAsync(IClientAccountingDbContext db,IAdjustmentJournalDbContext evidenceDb,
    ActorContext actor,Guid id,bool client,JournalManagementRequest? request,CancellationToken ct=default)
  {
    if(!Valid(request) || !request!.Reviewed)return Fail<JournalManagementReceipt>(ErrorCodes.Accounting.JournalRejected,"Preview and explicitly review this exact management decision.");
    var r=request!;var current=await ReadAsync(db,evidenceDb,actor,id,client,ct);if(!current.Succeeded)return Fail<JournalManagementReceipt>(current.ErrorCode!,current.Message!);
    var j=current.Value!.Journal;var auth=await Auth(db,actor,j,client,true,ct);if(!auth.Succeeded)return Fail<JournalManagementReceipt>(auth.ErrorCode!,auth.Message!);
    await using var tx=await db.Database.BeginTransactionAsync(ct);
    var locked=await GeneralLedgerCompletenessWorkspace.LockAsync(db,actor,j.ClientId,j.EngagementId,ct);if(!locked.Succeeded)return Fail<JournalManagementReceipt>(locked.ErrorCode!,locked.Message!);
    await db.AdjustmentJournals.FromSqlInterpolated($"SELECT * FROM adjustment_journals WHERE firm_id={actor.FirmId} AND id={id} FOR UPDATE").AsNoTracking().SingleAsync(ct);
    var prior=await evidenceDb.AdjustmentJournalActions.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==actor.FirmId && x.ActorId==actor.UserId && x.RequestId==r.RequestId,ct);
    if(prior is not null)
    {
      if(prior.JournalId!=id || prior.Action!="MANAGEMENT" || prior.RequestHash!=Hash(actor,id,client,r))return Fail<JournalManagementReceipt>(ErrorCodes.IdempotencyConflict,"A different intent cannot reuse this request.");
      auth=await Auth(db,actor,j,client,true,ct);if(!auth.Succeeded)return Fail<JournalManagementReceipt>(auth.ErrorCode!,auth.Message!);
      await tx.CommitAsync(ct);return CommandResult<JournalManagementReceipt>.Ok(Receipt(prior));
    }
    await db.TrialBalanceDatasets.FromSqlInterpolated($"SELECT * FROM trial_balance_datasets WHERE firm_id={actor.FirmId} AND id={j.BaseDatasetId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    if(j.PeriodId is not { } periodId)return Fail<JournalManagementReceipt>(ErrorCodes.GateBlocked,"Bind the exact reporting period before recording evidence.");
    var mutable=await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db,actor,j.ClientId,j.EngagementId,periodId,j.BookId,ct);if(!mutable.Succeeded)return Fail<JournalManagementReceipt>(mutable.ErrorCode!,mutable.Message!);
    var preview=await PreviewAsync(db,evidenceDb,actor,id,client,r,ct);if(!preview.Succeeded)return Fail<JournalManagementReceipt>(preview.ErrorCode!,preview.Message!);
    if(!preview.Value!.CanProceed)return Fail<JournalManagementReceipt>(ErrorCodes.GateBlocked,preview.Value.Blocker!);
    var before=await ReadAsync(db,evidenceDb,actor,id,client,ct);if(!before.Succeeded)return Fail<JournalManagementReceipt>(before.ErrorCode!,before.Message!);
    var recorded=await AdjustmentJournalService.RecordManagementDecisionAsync(evidenceDb,actor,new(id,r.Decision,preview.Value.EvidenceMode,r.EvidenceReference),ct);
    if(!recorded.Succeeded)return Fail<JournalManagementReceipt>(recorded.ErrorCode!,recorded.Message!);
    var decision=await evidenceDb.AdjustmentJournalManagementDecisions.AsNoTracking().SingleAsync(x=>x.FirmId==actor.FirmId && x.JournalId==id && x.JournalRevision==j.Revision,ct);
    var revision=before.Value!.Revision;var e=new AdjustmentJournalAction {Id=Guid.CreateVersion7(),FirmId=actor.FirmId,ClientId=j.ClientId,EngagementId=j.EngagementId,
      JournalId=id,ResultJournalId=id,ActorId=actor.UserId,ActorEpoch=actor.SessionEpoch,RequestId=r.RequestId,RequestHash=Hash(actor,id,client,r),ReviewBasis=r.ReviewBasis,Action="MANAGEMENT",
      OldRevision=j.Revision,NewRevision=j.Revision,OldStatus=j.Status,NewStatus=j.Status,Reason=r.Reason.Trim(),EvidenceReference=r.EvidenceReference.Trim(),
      BeforeJson=JsonSerializer.Serialize(revision),AfterJson=JsonSerializer.Serialize(new Retained(revision,Decision(decision))),CreatedAt=DateTimeOffset.UtcNow};
    evidenceDb.AdjustmentJournalActions.Add(e);await db.SaveChangesAsync(ct);
    auth=await Auth(db,actor,j,client,true,ct);if(!auth.Succeeded)return Fail<JournalManagementReceipt>(auth.ErrorCode!,auth.Message!);
    if(client && !(await ClientPortalService.GetFirstSignInStatusAsync(db,actor,ct)).Completed)return Fail<JournalManagementReceipt>(ErrorCodes.GateBlocked,"Complete the required first sign-in before recording a decision.");
    mutable=await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db,actor,j.ClientId,j.EngagementId,periodId,j.BookId,ct);if(!mutable.Succeeded)return Fail<JournalManagementReceipt>(mutable.ErrorCode!,mutable.Message!);
    await tx.CommitAsync(ct);return CommandResult<JournalManagementReceipt>.Ok(Receipt(e));
  }
  private static async Task<IReadOnlyList<Guid>> QueueScopes(IClientAccountingDbContext db,ActorContext actor,CancellationToken ct)
  {
    var candidates=await ClientPortalService.AuthorizedPortalEngagementIdsAsync(db,actor,ct);
    var allowed=new List<Guid>();
    foreach(var id in candidates)
      if((await AuthorizationDecision.AuthorizeAsync(db,actor,new(actor.FirmId,EngagementId:id,RequiredRoles:["ClientUser"],RequireProfessionalWork:true),ct)).Succeeded)allowed.Add(id);
    return allowed;
  }
  public static async Task<CommandResult<PortalJournalPage>> ClientQueueAsync(IClientAccountingDbContext db,ActorContext actor,int page=0,CancellationToken ct=default)
  {
    if(page is <0 or >10000 || !await db.Users.AnyAsync(x=>x.FirmId==actor.FirmId && x.Id==actor.UserId && x.UserKind=="Client" && !x.Disabled && x.SessionEpoch==actor.SessionEpoch,ct))return Fail<PortalJournalPage>(ErrorCodes.ScopeDenied,"Portal unavailable.");
    var scopes=await QueueScopes(db,actor,ct);
    var rows=await db.AdjustmentJournals.AsNoTracking().Where(x=>x.FirmId==actor.FirmId && x.Purpose!=AdjustmentJournalPurposes.GroupOnlyElimination && scopes.Contains(x.EngagementId) &&
      db.Engagements.Any(e=>e.FirmId==actor.FirmId && e.Id==x.EngagementId && e.PracticeClientId==x.ClientId))
      .OrderByDescending(x=>x.CreatedAt).ThenBy(x=>x.Id).Skip(page*25).Take(26).Select(x=>new PortalJournalItem(x.Id,x.JournalNumber,x.Revision,x.Purpose,x.Status,x.Currency ?? "")).ToListAsync(ct);
    var final=await QueueScopes(db,actor,ct);
    if(!scopes.Order().SequenceEqual(final.Order()) || !await db.Users.AnyAsync(x=>x.Id==actor.UserId && x.FirmId==actor.FirmId && !x.Disabled && x.SessionEpoch==actor.SessionEpoch,ct))return Fail<PortalJournalPage>(ErrorCodes.ScopeDenied,"Portal unavailable.");
    return CommandResult<PortalJournalPage>.Ok(new(rows.Take(25).ToArray(),page,rows.Count>25));
  }
}
