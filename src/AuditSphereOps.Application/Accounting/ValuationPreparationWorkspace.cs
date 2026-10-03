using System.Data;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record ValuationFields(string Method, string MethodologyVersion, string AssumptionsHash,
  string? ProbabilityOfDefault, string? LossGivenDefault, string? ManagementOverlay, string? ManagementExpectedLoss,
  string? BookedAmount, string? Quantity, string? UnitCost, string? NrvPerUnit, string? ObsolescenceReserve,
  string? BookAmount, string Reason, string EvidenceReference);
public sealed record ValuationPreparationRequest(Guid RequestId, string ReviewBasis, ValuationFields Fields, bool Reviewed = false);
public sealed record ValuationPreparationState(string Kind, ReconciliationReview Context, string ReviewBasis,
  bool CanPrepare, IReadOnlyList<string> Blockers);
public sealed record ValuationPreparationPreview(string Kind, Guid ReconciliationId, string ReviewBasis,
  string RequestHash, string Method, decimal EligibleExposure, decimal CalculatedAmount, decimal BookedAmount,
  decimal Difference, string Currency, bool CanProceed, IReadOnlyList<string> Blockers);
public sealed record ValuationPreparationReceipt(Guid Id, Guid RequestId, string RequestHash, Guid ReconciliationId,
  string Kind, Guid EvidenceId, Guid ActorId, string Reason, string EvidenceReference, DateTimeOffset CreatedAt);
public sealed record ValuationPreparationLookup(bool Found, ValuationPreparationReceipt? Receipt);

/// <summary>Reviewed preparation over exact source-bound evidence. Reuses the current calculators
/// and creation services, retaining local publication and its receipt in one guarded transaction.</summary>
public static class ValuationPreparationWorkspace
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private const decimal MaxAmount = 9999999999999.999999m;
  private static bool Kind(string kind) => kind is "ECL" or "INVENTORY";
  private static CommandResult<T> Fail<T>(string code, string message) => CommandResult<T>.Fail(code, message);
  private static ValuationPreparationReceipt Receipt(ValuationPreparation p) => new(p.Id, p.RequestId, p.RequestHash,
    p.ReconciliationId, p.Kind, p.EvidenceId, p.ActorId, p.Reason, p.EvidenceReference, p.CreatedAt);
  private static Task<CommandResult> Authorize(IClientAccountingDbContext db, ActorContext a, ReconciliationReview v, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, a, new(a.FirmId, v.ClientId, v.EngagementId, Roles, InternalOnly: true), ct);
  private static string Hash(ActorContext a, string kind, Guid id, ValuationPreparationRequest r) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, kind, id, r.RequestId, r.ReviewBasis, r.Fields }));

  public static async Task<CommandResult<ValuationPreparationState>> StateAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, CancellationToken ct = default)
  {
    if (!Kind(kind)) return Fail<ValuationPreparationState>(ErrorCodes.ScopeDenied, "This valuation method is unavailable.");
    var query = await ReconciliationWorkspaceQuery.GetAsync(db, a, id, ct: ct);
    if (!query.Succeeded) return Fail<ValuationPreparationState>(query.ErrorCode!, query.Message!);
    var v = query.Value!;
    var periodOpen = await db.ClientReportingPeriods.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
      x.Id == v.PeriodId && (x.Status == "DRAFT" || x.Status == "ACTIVE"), ct);
    var bookOpen = v.BookId is null || await db.ClientReportingBooks.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
      x.Id == v.BookId && x.PeriodId == v.PeriodId && (x.Status == "DRAFT" || x.Status == "ACTIVE"), ct);
    var freezes = await db.EngagementFileFreezes.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.EngagementId == v.EngagementId && x.State == "FROZEN")
      .OrderBy(x => x.Id).Select(x => x.Id).ToListAsync(ct);
    var blockers = v.Blockers.ToList();
    // Preserve the creation service's reconciled-state contract. Retained approval is never silently reset.
    if (v.Status != "RECONCILED") blockers.Add("Preparation requires a reconciled schedule. An approved historical schedule must be revised through the reconciliation workflow.");
    if (v.LatestProof is not { MatchesCurrentInputs: true }) blockers.Add("The complete current source/item proof is required.");
    if (v.LatestProof is not { IsReconciled: true }) blockers.Add("An unexplained residual blocks valuation preparation.");
    if (!periodOpen || !bookOpen || freezes.Count > 0) blockers.Add("The reporting period/book is closed or the engagement file is frozen.");
    var final = await ReconciliationWorkspaceQuery.GetAsync(db, a, id, ct: ct);
    if (!final.Succeeded) return Fail<ValuationPreparationState>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != v.ReviewBasis) return Fail<ValuationPreparationState>(ErrorCodes.StaleRevision, "The exact source context changed during inspection.");
    var basis = Hashing.Sha256Hex(JsonSerializer.Serialize(new { kind, v.ReviewBasis, periodOpen, bookOpen, freezes }));
    return CommandResult<ValuationPreparationState>.Ok(new(kind, v, basis, blockers.Count == 0, blockers.Distinct().ToArray()));
  }

  private static bool Exact(string? value, out decimal amount)
  {
    amount = 0m;
    return value is { Length: > 0 and <= 21 } && Regex.IsMatch(value, @"^-?[0-9]{1,13}(?:\.[0-9]{1,6})?$", RegexOptions.CultureInvariant) &&
      decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount) && Math.Abs(amount) <= MaxAmount;
  }
  private sealed record Values(decimal Pd, decimal Lgd, decimal Overlay, decimal Management, decimal Booked,
    decimal Quantity, decimal Cost, decimal Nrv, decimal Reserve, decimal Book);
  private static bool Parse(string kind, ValuationFields? f, out Values v)
  {
    v = new(0,0,0,0,0,0,0,0,0,0);
    if (f is null || f.MethodologyVersion is not { Length: > 0 and <= 100 } || string.IsNullOrWhiteSpace(f.MethodologyVersion) ||
      !SourceAcceptanceWorkspace.ValidHash(f.AssumptionsHash) || f.Reason is not { Length: > 0 and <= 4000 } || string.IsNullOrWhiteSpace(f.Reason) ||
      f.EvidenceReference is not { Length: > 0 and <= 2000 } || string.IsNullOrWhiteSpace(f.EvidenceReference)) return false;
    if (kind == "ECL")
    {
      if (f.Method != "PROVISION_MATRIX_V1" || f.Quantity is not null || f.UnitCost is not null || f.NrvPerUnit is not null || f.ObsolescenceReserve is not null || f.BookAmount is not null ||
        !Exact(f.ProbabilityOfDefault,out var pd) || pd is < 0 or > 1 || !Exact(f.LossGivenDefault,out var lgd) || lgd is < 0 or > 1 ||
        !Exact(f.ManagementOverlay,out var overlay) || overlay < 0 || !Exact(f.ManagementExpectedLoss,out var management) || management < 0 ||
        !Exact(f.BookedAmount,out var booked) || booked < 0) return false;
      v = v with { Pd=pd, Lgd=lgd, Overlay=overlay, Management=management, Booked=booked };
    }
    else
    {
      if (kind != "INVENTORY" || f.Method != "LOWER_COST_NRV_V1" || f.ProbabilityOfDefault is not null || f.LossGivenDefault is not null || f.ManagementOverlay is not null ||
        f.ManagementExpectedLoss is not null || f.BookedAmount is not null || !Exact(f.Quantity,out var quantity) || quantity < 0 ||
        !Exact(f.UnitCost,out var cost) || cost < 0 || !Exact(f.NrvPerUnit,out var nrv) || nrv < 0 ||
        !Exact(f.ObsolescenceReserve,out var reserve) || reserve < 0 || !Exact(f.BookAmount,out var book) || book < 0) return false;
      v = v with { Quantity=quantity, Cost=cost, Nrv=nrv, Reserve=reserve, Book=book };
    }
    return true;
  }

  public static async Task<CommandResult<ValuationPreparationPreview>> PreviewAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, ValuationPreparationRequest? request, CancellationToken ct = default)
  {
    if (request is null || request.RequestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(request.ReviewBasis) || !Parse(kind,request.Fields,out var values))
      return Fail<ValuationPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected, "Use the supported method and all explicit bounded six-decimal inputs, assumptions, reason and evidence reference.");
    var state = await StateAsync(db,a,kind,id,ct);
    if (!state.Succeeded) return Fail<ValuationPreparationPreview>(state.ErrorCode!,state.Message!);
    var s = state.Value!;
    if (request.ReviewBasis != s.ReviewBasis) return Fail<ValuationPreparationPreview>(ErrorCodes.StaleRevision,"Refresh the exact source and preview its current basis.");
    decimal result, exposure, booked, difference;
    try
    {
      exposure = kind == "ECL" ? Math.Max(0m,decimal.Parse(s.Context.SourceTotal,CultureInfo.InvariantCulture)) : 0m;
      result = kind == "ECL" ? AccountingAnalysisService.CalculateEcl(exposure,values.Pd,values.Lgd,values.Overlay) :
        AccountingAnalysisService.CalculateInventory(values.Quantity,values.Cost,values.Nrv,values.Reserve);
      booked = kind == "ECL" ? values.Booked : values.Book;
      difference = MoneyPolicy.Normalize(result-booked);
      if (Math.Abs(result) > MaxAmount || Math.Abs(difference) > MaxAmount) throw new OverflowException();
    }
    catch(OverflowException) { return Fail<ValuationPreparationPreview>(ErrorCodes.Accounting.ReconciliationRejected,"The calculated amount exceeds the supported accounting precision. No result is published."); }
    var final = await StateAsync(db,a,kind,id,ct);
    if (!final.Succeeded) return Fail<ValuationPreparationPreview>(final.ErrorCode!,final.Message!);
    if (final.Value!.ReviewBasis != s.ReviewBasis) return Fail<ValuationPreparationPreview>(ErrorCodes.StaleRevision,"The exact source changed during calculation preview.");
    return CommandResult<ValuationPreparationPreview>.Ok(new(kind,id,s.ReviewBasis,Hash(a,kind,id,request),request.Fields.Method,
      exposure,result,booked,difference,s.Context.Currency,s.CanPrepare,s.Blockers));
  }

  public static async Task<CommandResult<ValuationPreparationReceipt>> ExecuteAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, ValuationPreparationRequest? request, CancellationToken ct = default)
  {
    if (request is null || !request.Reviewed || request.RequestId == Guid.Empty || !SourceAcceptanceWorkspace.ValidHash(request.ReviewBasis) || !Parse(kind,request.Fields,out var n))
      return Fail<ValuationPreparationReceipt>(ErrorCodes.Accounting.ReconciliationRejected,"Preview and explicitly confirm this exact valuation intent.");
    var outside=await StateAsync(db,a,kind,id,ct);
    if(!outside.Succeeded)return Fail<ValuationPreparationReceipt>(outside.ErrorCode!,outside.Message!);
    var context=outside.Value!.Context;
    var auth=await Authorize(db,a,context,ct);if(!auth.Succeeded)return Fail<ValuationPreparationReceipt>(auth.ErrorCode!,auth.Message!);
    await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
    var locked=await GeneralLedgerCompletenessWorkspace.LockAsync(db,a,context.ClientId,context.EngagementId,ct);
    if(!locked.Succeeded)return Fail<ValuationPreparationReceipt>(locked.ErrorCode!,locked.Message!);
    await db.Users.FromSqlInterpolated($"SELECT * FROM users WHERE firm_id={a.FirmId} AND id={a.UserId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var hash=Hash(a,kind,id,request);
    var prior=await db.ValuationPreparations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==request.RequestId,ct);
    if(prior is not null)
    {
      if(prior.Kind!=kind||prior.ReconciliationId!=id||prior.RequestHash!=hash)return Fail<ValuationPreparationReceipt>(ErrorCodes.IdempotencyConflict,"A changed intent cannot reuse this request identity.");
      auth=await Authorize(db,a,context,ct);if(!auth.Succeeded)return Fail<ValuationPreparationReceipt>(auth.ErrorCode!,auth.Message!);
      await tx.CommitAsync(ct);return CommandResult<ValuationPreparationReceipt>.Ok(Receipt(prior));
    }
    var mutable=await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db,a,context.ClientId,context.EngagementId,context.PeriodId,context.BookId,ct);
    if(!mutable.Succeeded)return Fail<ValuationPreparationReceipt>(mutable.ErrorCode!,mutable.Message!);
    await db.AccountingReconciliations.FromSqlInterpolated($"SELECT * FROM accounting_reconciliations WHERE id={id} AND firm_id={a.FirmId} FOR SHARE").AsNoTracking().SingleAsync(ct);
    var preview=await PreviewAsync(db,a,kind,id,request,ct);
    if(!preview.Succeeded)return Fail<ValuationPreparationReceipt>(preview.ErrorCode!,preview.Message!);
    if(!preview.Value!.CanProceed)return Fail<ValuationPreparationReceipt>(ErrorCodes.GateBlocked,preview.Value.Blockers.First());
    CommandResult<Guid> created=kind=="ECL" ?
      await AccountingAnalysisService.CreateEclAssessmentAsync(db,a,new(id,context.AsOfDate,request.Fields.Method,request.Fields.MethodologyVersion,
        n.Pd,n.Lgd,n.Overlay,n.Management,request.Fields.AssumptionsHash,n.Booked),ct) :
      await AccountingAnalysisService.CreateInventoryValuationAsync(db,a,new(id,context.AsOfDate,n.Quantity,n.Cost,n.Nrv,n.Reserve,n.Book,
        request.Fields.MethodologyVersion,request.Fields.AssumptionsHash),ct);
    if(!created.Succeeded)return Fail<ValuationPreparationReceipt>(created.ErrorCode!,created.Message!);
    var result=await AccountingAnalysisReviewQuery.GetAsync(db,a,kind,created.Value,ct:ct);
    if(!result.Succeeded)return Fail<ValuationPreparationReceipt>(result.ErrorCode!,result.Message!);
    var row=new ValuationPreparation { Id=Guid.CreateVersion7(),FirmId=a.FirmId,ClientId=context.ClientId,EngagementId=context.EngagementId,
      ReconciliationId=id,Kind=kind,EvidenceId=created.Value,ActorId=a.UserId,ActorEpoch=a.SessionEpoch,RequestId=request.RequestId,
      RequestHash=hash,ReviewBasis=request.ReviewBasis,InputJson=JsonSerializer.Serialize(request.Fields),ContextJson=JsonSerializer.Serialize(context),
      ResultJson=JsonSerializer.Serialize(result.Value),Reason=request.Fields.Reason.Trim(),EvidenceReference=request.Fields.EvidenceReference.Trim(),CreatedAt=DateTimeOffset.UtcNow };
    db.ValuationPreparations.Add(row);await db.SaveChangesAsync(ct);
    auth=await Authorize(db,a,context,ct);if(!auth.Succeeded)return Fail<ValuationPreparationReceipt>(auth.ErrorCode!,auth.Message!);
    mutable=await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db,a,context.ClientId,context.EngagementId,context.PeriodId,context.BookId,ct);
    if(!mutable.Succeeded)return Fail<ValuationPreparationReceipt>(mutable.ErrorCode!,mutable.Message!);
    var final=await StateAsync(db,a,kind,id,ct);
    if(!final.Succeeded)return Fail<ValuationPreparationReceipt>(final.ErrorCode!,final.Message!);
    if(final.Value!.ReviewBasis!=request.ReviewBasis)return Fail<ValuationPreparationReceipt>(ErrorCodes.StaleRevision,"Source context changed before publication.");
    await tx.CommitAsync(ct);return CommandResult<ValuationPreparationReceipt>.Ok(Receipt(row));
  }

  public static async Task<CommandResult<ValuationPreparationLookup>> LookupAsync(IClientAccountingDbContext db,ActorContext a,
    string kind,Guid id,Guid requestId,string requestHash,CancellationToken ct=default)
  {
    if(!Kind(kind)||requestId==Guid.Empty||!SourceAcceptanceWorkspace.ValidHash(requestHash))return Fail<ValuationPreparationLookup>(ErrorCodes.ScopeDenied,"The retained request is unavailable.");
    var context=await ReconciliationWorkspaceQuery.GetAsync(db,a,id,ct:ct);
    if(!context.Succeeded)return Fail<ValuationPreparationLookup>(context.ErrorCode!,context.Message!);
    var row=await db.ValuationPreparations.AsNoTracking().SingleOrDefaultAsync(x=>x.FirmId==a.FirmId&&x.ActorId==a.UserId&&x.RequestId==requestId,ct);
    if(row is not null&&(row.Kind!=kind||row.ReconciliationId!=id||row.RequestHash!=requestHash))return Fail<ValuationPreparationLookup>(ErrorCodes.IdempotencyConflict,"This reference belongs to a different exact intent.");
    var auth=await Authorize(db,a,context.Value!,ct);
    return auth.Succeeded ? CommandResult<ValuationPreparationLookup>.Ok(new(row is not null,row is null?null:Receipt(row))) : Fail<ValuationPreparationLookup>(auth.ErrorCode!,auth.Message!);
  }
}
