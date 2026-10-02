using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MappingApprovalReview(
  Guid MappingId, Guid ClientId, Guid EngagementId, Guid DatasetId, long Version,
  string Status, long MappingGeneration, long InputGeneration, string TaxonomyVersion,
  Guid? ChartVersionId, string PeriodStart, string PeriodEnd, int SourceAccountCount,
  int AllocationCount, Guid PreparerId, Guid? ReviewerId, DateTimeOffset? ReviewedAt,
  string Revision, bool CanApprove, string? Blocker);

/// <summary>Read-only, bounded review of the exact mapping and its applicability.
/// Approval still belongs to the existing serialized FinancialStatementService transaction.</summary>
public static class MappingApprovalWorkspace
{
  private static readonly string[] ReadRoles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  private static readonly string[] ReviewRoles = ["Administrator", "Partner", "Manager", "AccountingReviewer"];
  public const int MaximumAllocations = 5_000;
  public const int MaximumSourceRows = 20_000;

  public static async Task<CommandResult<MappingApprovalReview>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid mappingId, CancellationToken ct = default)
  {
    var first = await ReadAsync(db, actor, mappingId, ct);
    if (!first.Succeeded) return first;
    var final = await ReadAsync(db, actor, mappingId, ct);
    if (!final.Succeeded) return final;
    return first.Value!.Revision == final.Value!.Revision ? final :
      CommandResult<MappingApprovalReview>.Fail(ErrorCodes.StaleRevision, "Mapping review inputs changed. Refresh the current mapping.");
  }

  private static async Task<CommandResult<MappingApprovalReview>> ReadAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid mappingId, CancellationToken ct)
  {
    var mapping = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mappingId && x.FirmId == actor.FirmId, ct);
    if (mapping is null) return Denied();
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, mapping.ClientId, mapping.EngagementId, ReadRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded) return CommandResult<MappingApprovalReview>.Fail(auth.ErrorCode!, auth.Message!);
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == mapping.DatasetId && x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId && x.EngagementId == mapping.EngagementId, ct);
    if (dataset is null) return Denied();
    var allocationsQuery = db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.ClientId == mapping.ClientId && x.EngagementId == mapping.EngagementId && x.MappingVersionId == mapping.Id);
    var rowsQuery = db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == dataset.Id);
    // Refuse an oversized interactive review rather than presenting a partial approvable population.
    if (await allocationsQuery.CountAsync(ct) > MaximumAllocations || await rowsQuery.CountAsync(ct) > MaximumSourceRows)
      return CommandResult<MappingApprovalReview>.Fail(ErrorCodes.GateBlocked, "This mapping exceeds the interactive review limit. Use a separately approved review workflow.");
    var allocations = await allocationsQuery.OrderBy(x => x.SourceAccountCode).ThenBy(x => x.DestinationCode).ThenBy(x => x.Id).ToListAsync(ct);
    var rows = await rowsQuery.OrderBy(x => x.AccountCode).ThenBy(x => x.Id).ToListAsync(ct);
    var input = allocations.Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode, x.StatementSection,
      x.Fraction, x.Rationale, x.AuditArea, x.ResidualPolicy)).ToArray();
    var taxonomy = await db.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Code == mapping.TaxonomyVersion, ct);
    var nodes = taxonomy is null ? [] : await db.ReportingTaxonomyNodes.AsNoTracking().Where(x =>
      x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id).OrderBy(x => x.Code).ThenBy(x => x.Id).ToListAsync(ct);
    var chart = mapping.ClientChartVersionId is { } chartId ? await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == chartId && x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId, ct) : null;
    var period = dataset.PeriodId is { } periodId ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == periodId && x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId, ct) : null;
    var book = dataset.BookId is { } bookId ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == bookId && x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId && x.PeriodId == dataset.PeriodId, ct) : null;
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mapping.ClientId && x.FirmId == actor.FirmId, ct);
    if (firm is null || safety is null) return Denied();
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == mapping.EngagementId && x.State == "FROZEN", ct);
    var reviewer = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, mapping.ClientId, mapping.EngagementId, ReviewRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    var allocationError = input.Length == 0 ? "At least one complete allocation is required." :
      FinancialStatementService.ValidateReviewAllocations(rows.Select(x => new MappingSourceAccount(x.AccountCode, x.AccountName, x.Amount, x.Currency)).ToArray(), input);
    var taxonomyError = await FinancialStatementService.ValidateApprovedTaxonomyAsync(db, actor.FirmId, mapping.TaxonomyVersion, input, ct);
    var missingChartBinding = mapping.ClientChartVersionId is null && await db.ClientChartVersions.AsNoTracking().AnyAsync(x =>
      x.FirmId == actor.FirmId && x.ClientId == mapping.ClientId && x.Status == AccountingWorkflowStates.Approved, ct);
    var chartInvalid = missingChartBinding || mapping.ClientChartVersionId is not null && (chart is null || chart.Status != AccountingWorkflowStates.Approved ||
      !DateOnly.TryParseExact(mapping.PeriodStart, "yyyy-MM-dd", out var start) || !DateOnly.TryParseExact(mapping.PeriodEnd, "yyyy-MM-dd", out var end) ||
      chart.EffectiveFrom > start || chart.EffectiveTo is { } chartEnd && chartEnd < end);
    var blocker = mapping.Generation != safety.InputGeneration ? "Source inputs changed after this mapping was prepared. Create and review a new mapping version." :
      mapping.Status != AccountingPackageStates.MappingDraft ? "This version already has its retained approval or is not a draft." :
      mapping.CreatedByUserId == actor.UserId ? "The mapping preparer cannot approve the same version." :
      !reviewer.Succeeded ? "A current scoped AccountingReviewer, Manager, Partner or Administrator is required." :
      dataset.SourceKind != "Raw" || dataset.ImportState != TrialBalanceImportStates.Sealed || dataset.ValidationStatus != "Accepted" ||
        !dataset.Balanced || dataset.ControlTotal != 0m ? "The exact raw source dataset must be sealed, balanced and validated." :
      dataset.PeriodId is not null && (period is null || period.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft)) ? "The reporting period is unavailable or closed." :
      dataset.BookId is not null && (book is null || book.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft)) ? "The reporting book is unavailable or closed." :
      frozen ? "The engagement file is frozen. An approved amendment is required." :
      chartInvalid ? "The exact client chart is no longer approved or applicable for this period." :
      allocations.Any(x => x.ResidualPolicy != "LAST_DESTINATION" || MoneyPolicy.Normalize(x.Fraction) != x.Fraction) ? "An allocation uses unsupported precision or residual policy." :
      allocationError ?? taxonomyError;
    var revision = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
      actor.FirmId, actor.UserId, actor.SessionEpoch, mapping, dataset, allocations, rows, taxonomy, nodes, chart, missingChartBinding, period, book, firm, safety, frozen
    })));
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new(actor.FirmId, mapping.ClientId, mapping.EngagementId, ReadRoles, InternalOnly: true, RequireProfessionalWork: true), ct);
    if (!auth.Succeeded) return CommandResult<MappingApprovalReview>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<MappingApprovalReview>.Ok(new(mapping.Id, mapping.ClientId, mapping.EngagementId, mapping.DatasetId, mapping.Version,
      mapping.Status, mapping.Generation, safety.InputGeneration, mapping.TaxonomyVersion, mapping.ClientChartVersionId, mapping.PeriodStart,
      mapping.PeriodEnd, rows.Select(x => x.AccountCode).Distinct(StringComparer.Ordinal).Count(), allocations.Count, mapping.CreatedByUserId,
      mapping.ApprovedByUserId, mapping.ApprovedAt, revision, blocker is null, blocker));
  }

  private static CommandResult<MappingApprovalReview> Denied() => CommandResult<MappingApprovalReview>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

  public static async Task<CommandResult<MappingApprovalReview>> ApproveAsync(IClientAccountingDbContext db, ActorContext actor,
    Guid mappingId, string revision, bool reviewed, CancellationToken ct = default)
  {
    var plan = await GetAsync(db, actor, mappingId, ct);
    if (!plan.Succeeded) return plan;
    var result = await FinancialStatementService.ApproveMappingAsync(db, actor, mappingId, plan.Value!.Version, ct, revision, reviewed);
    return result.Succeeded ? await GetAsync(db, actor, mappingId, ct) : CommandResult<MappingApprovalReview>.Fail(result.ErrorCode!, result.Message!);
  }
}
