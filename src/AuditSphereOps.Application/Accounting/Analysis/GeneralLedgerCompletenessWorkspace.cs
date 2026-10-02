using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record CompletenessSourceContext(GeneralLedgerSourceContext Source, string PeriodStatus,
  string Basis, DateOnly StartDate, DateOnly EndDate, Guid? PriorPeriodId, string? BookCode,
  long InputGeneration, SelectedSourceDto? SelectedTrialBalance, string Revision, bool CanPrepare, string? Blocker);
public sealed record CompletenessTbSource(Guid Id, Guid PeriodId, string PeriodCode, Guid? BookId,
  long Revision, string SourceHash, DateTimeOffset ImportedAt);
public sealed record CompletenessSourcePage(IReadOnlyList<CompletenessTbSource> Items, int TotalCount, int Page, int PageSize);
public sealed record CompletenessBridgeSummary(Guid Id, Guid TrialBalanceDatasetId, Guid? OpeningTrialBalanceDatasetId,
  string Status, bool IncompleteExtract, decimal AbsoluteResidual, DateTimeOffset CreatedAt);
public sealed record CompletenessWorkspace(CompletenessSourceContext Context, CompletenessSourcePage ClosingSources,
  CompletenessSourcePage OpeningSources, IReadOnlyList<CompletenessBridgeSummary> Bridges, bool MoreBridges);
public sealed record CompletenessOperationObservation(Guid Id, string Status, Guid? OriginatorId,
  string EvidenceReference, Guid? OpeningTrialBalanceDatasetId, string? ResultIdentity, DateTimeOffset CreatedAt);
public sealed record CompletenessPlan(CompletenessSourceContext Context, CompletenessTbSource Closing,
  CompletenessTbSource? Opening, Guid? ExistingBridgeId, IReadOnlyList<CompletenessOperationObservation> Operations,
  string Revision, bool CanPrepare, string? Blocker);

public static partial class GeneralLedgerCompletenessWorkspace
{
  public const int SourcePageSize = 20;
  internal static readonly string[] ReviewerRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  internal static string Digest(object value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
  private static bool ValidPage(int page) => page >= 1 && (long)(page - 1) * SourcePageSize <= int.MaxValue;

  internal static async Task<CommandResult<CompletenessSourceContext>> ContextAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid batchId, CancellationToken ct)
  {
    var batch = await db.SourceImportBatches.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batchId && x.FirmId == actor.FirmId, ct);
    if (batch is null) return CommandResult<CompletenessSourceContext>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var source = await GeneralLedgerWorkspace.ContextAsync(db, actor, batch.EngagementId, batchId, ct);
    if (!source.Succeeded) return CommandResult<CompletenessSourceContext>.Fail(source.ErrorCode!, source.Message!);
    var period = await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batch.PeriodId && x.FirmId == actor.FirmId && x.ClientId == batch.ClientId, ct);
    var book = batch.BookId is { } bookId ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == bookId && x.FirmId == actor.FirmId && x.ClientId == batch.ClientId && x.PeriodId == batch.PeriodId, ct) : null;
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == batch.ClientId && x.FirmId == actor.FirmId, ct);
    var selected = await AccountingSourceAcceptanceService.GetSelectedSourceAsync(db, actor, batch.ClientId, batch.EngagementId, AccountingSourceKinds.TrialBalance, ct);
    if (!selected.Succeeded) return CommandResult<CompletenessSourceContext>.Fail(selected.ErrorCode!, selected.Message!);
    if (period is null || firm is null || safety is null) return CommandResult<CompletenessSourceContext>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == batch.EngagementId && x.State == FileFreezeStates.Frozen, ct);
    var write = await AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, batch.ClientId, batch.EngagementId,
      GeneralLedgerQuery.ReadRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    var blocker = !write.Succeeded ? "Current scoped preparer authority and an unblocked engagement are required." :
      period.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft) ? "The reporting period is closed." :
      batch.BookId is not null && (book is null || book.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft)) ? "The reporting book is unavailable or closed." :
      frozen ? "The engagement file is frozen. An approved amendment is required." : null;
    var final = await GeneralLedgerQuery.CheckCurrentAsync(db, actor, batch, ct);
    if (!final.Succeeded) return CommandResult<CompletenessSourceContext>.Fail(final.ErrorCode!, final.Message!);
    var revision = Digest(new { actor.FirmId, actor.UserId, actor.SessionEpoch, Source = source.Value, period, book, firm, safety, SelectedTb = selected.Value, frozen, blocker });
    return CommandResult<CompletenessSourceContext>.Ok(new(source.Value!, period.Status, period.Basis, period.StartDate, period.EndDate,
      period.PriorPeriodId, book?.Code, safety.InputGeneration, selected.Value, revision, blocker is null, blocker));
  }

  private static IQueryable<TrialBalanceDataset> CompatibleSources(IClientAccountingDbContext db, ActorContext actor,
    CompletenessSourceContext context, bool opening)
  {
    var s = context.Source;
    var query = db.TrialBalanceDatasets.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == s.ClientId &&
      x.EngagementId == s.EngagementId && x.SourceKind == "Raw" && x.ImportState == TrialBalanceImportStates.Sealed &&
      x.ValidationStatus == "Accepted" && x.Balanced && x.Currency == s.Currency && x.LegalEntityKey == s.Entity && x.Basis == context.Basis);
    if (!opening) return query.Where(x => x.PeriodId == s.PeriodId && x.BookId == s.BookId);
    if (context.PriorPeriodId is null || context.BookCode is null) return query.Where(_ => false);
    return query.Where(x => x.PeriodId == context.PriorPeriodId && db.ClientReportingBooks.Any(book =>
      book.Id == x.BookId && book.FirmId == actor.FirmId && book.ClientId == s.ClientId && book.PeriodId == context.PriorPeriodId &&
      book.Code == context.BookCode && book.Basis == context.Basis && book.Currency == s.Currency));
  }

  private static async Task<CompletenessTbSource> ProjectAsync(IClientAccountingDbContext db, TrialBalanceDataset s, CancellationToken ct) =>
    new(s.Id, s.PeriodId!.Value, await db.ClientReportingPeriods.Where(x => x.Id == s.PeriodId && x.FirmId == s.FirmId && x.ClientId == s.ClientId).Select(x => x.PeriodCode).SingleAsync(ct),
      s.BookId, s.Revision, s.NormalizedDatasetDigest.Length == 64 ? s.NormalizedDatasetDigest : s.Sha256Hex, s.ImportedAt);
  private static async Task<CompletenessSourcePage> PageAsync(IClientAccountingDbContext db, IQueryable<TrialBalanceDataset> query, int page, CancellationToken ct)
  {
    var count = await query.CountAsync(ct);
    var sources = await query.OrderByDescending(x => x.ImportedAt).ThenBy(x => x.Id).Skip((page - 1) * SourcePageSize).Take(SourcePageSize).ToListAsync(ct);
    var rows = new List<CompletenessTbSource>();
    foreach (var s in sources) rows.Add(await ProjectAsync(db, s, ct));
    return new(rows, count, page, SourcePageSize);
  }

  public static async Task<CommandResult<CompletenessWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid batchId, int page = 1, int openingPage = 1, CancellationToken ct = default)
  {
    var context = await ContextAsync(db, actor, batchId, ct);
    if (!context.Succeeded) return CommandResult<CompletenessWorkspace>.Fail(context.ErrorCode!, context.Message!);
    if (!ValidPage(page) || !ValidPage(openingPage)) return CommandResult<CompletenessWorkspace>.Fail(ErrorCodes.Accounting.ImportRejected, "The source page is invalid.");
    var closing = await PageAsync(db, CompatibleSources(db, actor, context.Value!, false), page, ct);
    var opening = await PageAsync(db, CompatibleSources(db, actor, context.Value!, true), openingPage, ct);
    if (closing.Items.Concat(opening.Items).Any(x => !SourceAcceptanceWorkspace.ValidHash(x.SourceHash)))
      return CommandResult<CompletenessWorkspace>.Fail(ErrorCodes.GateBlocked, "An immutable source digest is unavailable.");
    var s = context.Value!.Source;
    var bridges = await db.GeneralLedgerCompletenessBridges.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == s.ClientId && x.EngagementId == s.EngagementId && x.ImportBatchId == batchId)
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(21).Select(x => new CompletenessBridgeSummary(x.Id, x.TrialBalanceDatasetId, x.OpeningTrialBalanceDatasetId, x.Status, x.IncompleteExtract, x.AbsoluteResidual, x.CreatedAt)).ToListAsync(ct);
    var final = await ContextAsync(db, actor, batchId, ct);
    if (!final.Succeeded) return CommandResult<CompletenessWorkspace>.Fail(final.ErrorCode!, final.Message!);
    if (context.Value.Revision != final.Value!.Revision) return CommandResult<CompletenessWorkspace>.Fail(ErrorCodes.StaleRevision, "The source context changed. Refresh completeness.");
    return CommandResult<CompletenessWorkspace>.Ok(new(final.Value, closing, opening, bridges.Take(20).ToArray(), bridges.Count > 20));
  }

  public static async Task<CommandResult<CompletenessPlan>> PlanAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid batchId, Guid trialBalanceId, Guid? openingId, CancellationToken ct = default)
  {
    var first = await ReadPlanAsync(db, actor, batchId, trialBalanceId, openingId, ct);
    if (!first.Succeeded) return first;
    var second = await ReadPlanAsync(db, actor, batchId, trialBalanceId, openingId, ct);
    if (!second.Succeeded) return second;
    return first.Value!.Revision == second.Value!.Revision ? second :
      CommandResult<CompletenessPlan>.Fail(ErrorCodes.StaleRevision, "The source pair changed. Refresh and review again.");
  }

  private static async Task<CommandResult<CompletenessPlan>> ReadPlanAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid batchId, Guid trialBalanceId, Guid? openingId, CancellationToken ct)
  {
    var context = await ContextAsync(db, actor, batchId, ct);
    if (!context.Succeeded) return CommandResult<CompletenessPlan>.Fail(context.ErrorCode!, context.Message!);
    var closing = await CompatibleSources(db, actor, context.Value!, false).SingleOrDefaultAsync(x => x.Id == trialBalanceId, ct);
    var opening = openingId is { } id ? await CompatibleSources(db, actor, context.Value!, true).SingleOrDefaultAsync(x => x.Id == id, ct) : null;
    if (closing is null || openingId is not null && opening is null) return CommandResult<CompletenessPlan>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var closingDto = await ProjectAsync(db, closing, ct); var openingDto = opening is null ? null : await ProjectAsync(db, opening, ct);
    if (!SourceAcceptanceWorkspace.ValidHash(closingDto.SourceHash) || openingDto is not null && !SourceAcceptanceWorkspace.ValidHash(openingDto.SourceHash))
      return CommandResult<CompletenessPlan>.Fail(ErrorCodes.GateBlocked, "An immutable source digest is unavailable.");
    var s = context.Value!.Source;
    var bridge = await db.GeneralLedgerCompletenessBridges.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == s.ClientId && x.EngagementId == s.EngagementId && x.ImportBatchId == batchId && x.TrialBalanceDatasetId == trialBalanceId).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
    // A bounded observation, never raw operation payload/credentials. An old or missing observation is not a retry instruction.
    // v1 payloads are normalized by the handler to compact JSON before immutable storage.
    // Filter this exact GL pair before applying the observation limit; other GL batches cannot hide a prior request.
    var batchProperty = $"\"importBatchId\":\"{batchId:D}\"";
    var recent = await db.DurableOperations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == s.ClientId && x.EngagementId == s.EngagementId && x.OperationKind == GeneralLedgerCompletenessHandler.Kind && x.TargetId == trialBalanceId && x.PayloadJson.Contains(batchProperty))
      .OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Take(50).ToListAsync(ct);
    var operations = new List<CompletenessOperationObservation>();
    foreach (var op in recent)
    {
      try
      {
      using var payload = JsonDocument.Parse(op.PayloadJson);
      if (payload.RootElement.GetProperty("importBatchId").GetGuid() != batchId) continue;
      var p = payload.RootElement.GetProperty("openingTrialBalanceDatasetId");
      operations.Add(new(op.Id, op.Status.ToString(), op.OriginatorId, payload.RootElement.GetProperty("evidenceReference").GetString()!, p.ValueKind == JsonValueKind.Null ? null : p.GetGuid(), op.ResultIdentity, op.CreatedAt));
      if (operations.Count == 20) break;
      }
      catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
      {
        return CommandResult<CompletenessPlan>.Fail(ErrorCodes.GateBlocked, "The prior operation metadata is unavailable. Review it through Operations.");
      }
    }
    var final = await ContextAsync(db, actor, batchId, ct);
    if (!final.Succeeded) return CommandResult<CompletenessPlan>.Fail(final.ErrorCode!, final.Message!);
    if (context.Value.Revision != final.Value!.Revision) return CommandResult<CompletenessPlan>.Fail(ErrorCodes.StaleRevision, "The source context changed. Refresh completeness.");
    var blocker = context.Value.Blocker ?? (bridge is not null ? "This source pair already has a retained completeness bridge." :
      operations.Count > 0 ? "A prior operation for this pair must be reviewed through Operations. Do not enqueue a duplicate." : null);
    var revision = Digest(new { context.Value.Revision, closing, opening, bridge, operations, blocker });
    return CommandResult<CompletenessPlan>.Ok(new(final.Value, closingDto, openingDto, bridge, operations, revision, blocker is null, blocker));
  }

  public static async Task<CommandResult<Guid>> PrepareAsync(IClientAccountingDbContext db, ActorContext actor, Guid batchId,
    Guid trialBalanceId, Guid? openingId, string revision, string evidenceReference, bool reviewed,
    IOperationStore store, GeneralLedgerCompletenessHandler handler, CancellationToken ct = default)
  {
    if (!SourceAcceptanceWorkspace.ValidHash(revision)) return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Review the current source pair before preparing completeness.");
    var plan = await PlanAsync(db, actor, batchId, trialBalanceId, openingId, ct);
    if (!plan.Succeeded) return CommandResult<Guid>.Fail(plan.ErrorCode!, plan.Message!);
    var s = plan.Value!.Context.Source;
    return await AccountingAnalysisService.EnqueueGeneralLedgerCompletenessBridgeAsync(db, actor,
      new(s.ClientId, s.EngagementId, s.PeriodId, s.BookId, trialBalanceId, batchId, evidenceReference, openingId), store, handler, ct, revision, reviewed);
  }
}
