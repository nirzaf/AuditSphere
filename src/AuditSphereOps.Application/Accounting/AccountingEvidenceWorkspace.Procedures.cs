using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class AccountingEvidenceWorkspace
{
  private static async Task<EvidenceProcedureCandidate?> CandidateAsync(IClientAccountingDbContext db, ActorContext a,
    AccountingAnalysisReview v, Guid resultId, CancellationToken ct)
  {
    var r = await db.AuditProcedureResults.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId &&
      x.ClientId == v.ClientId && x.EngagementId == v.EngagementId && x.Id == resultId, ct);
    if (r is null || r.WorkpaperId is not { } paperId || r.Status is not ("SUBMITTED" or "REVIEWED") ||
        r.InputGeneration != v.CurrentGeneration) return null;
    var p = await db.AuditProcedures.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId &&
      x.ClientId == v.ClientId && x.EngagementId == v.EngagementId && x.Id == r.AuditProcedureId, ct);
    var paper = await db.Workpapers.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == a.FirmId &&
      x.ClientId == v.ClientId && x.EngagementId == v.EngagementId && x.Id == paperId, ct);
    if (p is null || paper?.ProcedureId != p.Id || p.CurrentResultRevision != r.Revision || p.Status != r.Status) return null;
    var linked = await db.AccountingEvidenceAuditLinks.AsNoTracking().AnyAsync(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
      x.EngagementId == v.EngagementId && x.EvidenceId == v.Id && x.EvidenceKind == v.Kind && x.AuditProcedureResultId == r.Id, ct);
    var reviewed = r.Status == "REVIEWED" && r.ReviewedByUserId is not null && r.ReviewedAt is not null && r.ReviewedByUserId != r.PreparedByUserId;
    return new(r.Id, paperId, p.Id, p.SourceProcedureId, r.Status, r.Revision, r.InputGeneration, reviewed, linked,
      Hashing.Sha256Hex(JsonSerializer.Serialize(new { a.FirmId, a.UserId, a.SessionEpoch, v.Kind, v.Id, v.CurrentGeneration, r, p, paper, linked })));
  }

  public static async Task<CommandResult<EvidenceProcedurePage>> ProceduresAsync(IClientAccountingDbContext db, ActorContext a,
    string kind, Guid id, int page = 0, CancellationToken ct = default)
  {
    if (page is < 0 or >= 20) return Fail<EvidenceProcedurePage>(ErrorCodes.Accounting.ReconciliationRejected, "Choose a bounded procedure page.");
    var view = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!view.Succeeded) return Fail<EvidenceProcedurePage>(view.ErrorCode!, view.Message!);
    var v = view.Value!;
    var auth = await Auth(db, a, v, PrepareRoles, ct);
    if (!auth.Succeeded) return Fail<EvidenceProcedurePage>(auth.ErrorCode!, auth.Message!);
    var eligible = from r in db.AuditProcedureResults.AsNoTracking()
      join p in db.AuditProcedures.AsNoTracking() on r.AuditProcedureId equals p.Id
      join paper in db.Workpapers.AsNoTracking() on r.WorkpaperId equals paper.Id
      where r.FirmId == a.FirmId && r.ClientId == v.ClientId && r.EngagementId == v.EngagementId && r.InputGeneration == v.CurrentGeneration &&
        (r.Status == "SUBMITTED" || r.Status == "REVIEWED") && p.FirmId == a.FirmId && p.ClientId == v.ClientId && p.EngagementId == v.EngagementId &&
        paper.FirmId == a.FirmId && paper.ClientId == v.ClientId && paper.EngagementId == v.EngagementId && paper.ProcedureId == p.Id &&
        p.CurrentResultRevision == r.Revision && p.Status == r.Status
      orderby r.Id select r.Id;
    var ids = await eligible.Take(501).ToListAsync(ct);
    if (ids.Count > 500) return Fail<EvidenceProcedurePage>(ErrorCodes.GateBlocked, "The eligible result set exceeds the supported selection bound.");
    // Scoped joins filter malformed/superseded parents before counting. Load full metadata only for this page.
    var selected = new List<EvidenceProcedureCandidate>();
    foreach (var resultId in ids.Skip(page * 25).Take(25))
    {
      if (await CandidateAsync(db, a, v, resultId, ct) is not { } candidate)
        return Fail<EvidenceProcedurePage>(ErrorCodes.StaleRevision, "A selected result changed during inspection.");
      selected.Add(candidate);
    }
    foreach (var candidate in selected)
      if ((await CandidateAsync(db, a, v, candidate.ResultId, ct))?.ResultBasis != candidate.ResultBasis)
        return Fail<EvidenceProcedurePage>(ErrorCodes.StaleRevision, "A selected procedure result changed during inspection.");
    var final = await AccountingAnalysisReviewQuery.GetAsync(db, a, kind, id, ct: ct);
    if (!final.Succeeded) return Fail<EvidenceProcedurePage>(final.ErrorCode!, final.Message!);
    if (final.Value!.ReviewBasis != v.ReviewBasis || !ids.SequenceEqual(await eligible.Take(501).ToListAsync(ct)))
      return Fail<EvidenceProcedurePage>(ErrorCodes.StaleRevision, "The evidence or eligible result set changed during selection.");
    auth = await Auth(db, a, v, PrepareRoles, ct);
    if (!auth.Succeeded) return Fail<EvidenceProcedurePage>(auth.ErrorCode!, auth.Message!);
    return CommandResult<EvidenceProcedurePage>.Ok(new(kind, id, v.ReviewBasis, page, ids.Count,
      ids.Count > (page + 1) * 25, selected));
  }

  private static Task<int> LockEvidenceAsync(IClientAccountingDbContext db, Guid firm, string kind, Guid id, CancellationToken ct) =>
    db.Database.ExecuteSqlInterpolatedAsync(kind switch {
      "ECL" => $"SELECT id FROM ecl_assessments WHERE firm_id={firm} AND id={id} FOR UPDATE",
      "INVENTORY" => $"SELECT id FROM inventory_valuation_assessments WHERE firm_id={firm} AND id={id} FOR UPDATE",
      "SPECIALIST" => $"SELECT id FROM specialist_accounting_schedules WHERE firm_id={firm} AND id={id} FOR UPDATE",
      "ANALYTICAL" => $"SELECT id FROM analytical_reviews WHERE firm_id={firm} AND id={id} FOR UPDATE",
      "JOURNAL_RISK" => $"SELECT id FROM journal_risk_flags WHERE firm_id={firm} AND id={id} FOR UPDATE",
      _ => throw new InvalidOperationException("Unsupported accounting evidence kind.")
    }, ct);

  private static async Task<Guid?> BookAsync(IClientAccountingDbContext db, ActorContext a, AccountingAnalysisReview v, CancellationToken ct)
  {
    if (v.Source.ReconciliationId is { } reconciliation)
      return await db.AccountingReconciliations.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
        x.EngagementId == v.EngagementId && x.Id == reconciliation).Select(x => x.BookId).SingleOrDefaultAsync(ct);
    if (v.Source.Kind == "GENERAL_LEDGER" && v.Source.Id is { } batch)
      return await db.SourceImportBatches.AsNoTracking().Where(x => x.FirmId == a.FirmId && x.ClientId == v.ClientId &&
        x.EngagementId == v.EngagementId && x.Id == batch).Select(x => x.BookId).SingleOrDefaultAsync(ct);
    return null;
  }
}
