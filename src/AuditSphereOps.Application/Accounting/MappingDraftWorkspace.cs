using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MappingDraftSplit(string DestinationCode, string Fraction, string Rationale, string? AuditArea = null);
public sealed record MappingBatchChange(string AccountCode, IReadOnlyList<MappingDraftSplit> Splits);
public sealed record MappingDraftIntent(Guid RequestId, string BaseRevision, IReadOnlyList<MappingBatchChange> Changes);
public sealed record MappingCreationFence(Guid BaseMappingId, MappingDraftIntent Intent, string ReviewRevision, bool Reviewed);
public sealed record MappingDraftDestination(string Code, string Name, string StatementSection);
public sealed record MappingDraftAccount(string AccountCode, string AccountName, decimal Amount, string Currency, IReadOnlyList<MappingAllocationInput> Allocations);
public sealed record MappingDraftEditor(Guid BaseMappingId, Guid ClientId, Guid EngagementId, Guid DatasetId, long BaseVersion,
  long InputGeneration, string Revision, string TaxonomyVersion, Guid? ChartVersionId, string PeriodStart, string PeriodEnd,
  int SourceAccountCount, int FilteredCount, int Page, int PageSize, IReadOnlyList<MappingDraftAccount> Accounts,
  IReadOnlyList<MappingDraftDestination> Destinations, bool CanCreate, string? Blocker);
public sealed record MappingDraftAccountChange(string AccountCode, IReadOnlyList<MappingAllocationInput> Before, IReadOnlyList<MappingAllocationInput> After);
public sealed record MappingDraftPreview(Guid BaseMappingId, Guid RequestId, string BaseRevision, string Revision, string RequestHash,
  int ChangedAccountCount, int AllocationCount, IReadOnlyList<MappingDraftAccountChange> Changes, bool CanCreate, string? Blocker);
public sealed record MappingDraftReceipt(Guid BaseMappingId, Guid RequestId, Guid MappingId, long Version, string Status, string RequestHash, Guid CreatedBy, DateTimeOffset CreatedAt);

/// <summary>Bounded source-account editing creates a reviewed new version. Historical allocations are never modified.</summary>
public static class MappingDraftWorkspace
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "AccountingPreparer", "AccountingReviewer"];
  public const int PageSize = 25;
  public const int MaximumChanges = 200;
  private sealed record Context(MappingVersion Mapping, TrialBalanceDataset Dataset, IReadOnlyList<MappingAllocationInput> Allocations,
    IReadOnlyList<MappingSourceAccount> Accounts, IReadOnlyList<ReportingTaxonomyNode> Nodes, string Revision, long Generation, string? Blocker);
  internal sealed record CreationProof(Guid? ExistingId, string RequestHash);
  private sealed record Plan(Context Context, CreateMappingVersionRequest Request, MappingDraftPreview Preview);
  private static string Digest(object value) => Hashing.Sha256Hex(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
  private static CommandResult<T> Denied<T>() => CommandResult<T>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
  private static bool ValidIntentShape(MappingDraftIntent intent) => intent.RequestId != Guid.Empty && SourceAcceptanceWorkspace.ValidHash(intent.BaseRevision) &&
    intent.Changes is { Count: > 0 and <= MaximumChanges } && intent.Changes.All(x => x is not null && x.AccountCode is { Length: > 0 and <= 100 } &&
      x.Splits is { Count: > 0 and <= 20 } && x.Splits.All(s => s is not null && s.DestinationCode is { Length: > 0 and <= 100 } &&
        s.Fraction is { Length: > 0 and <= 8 } && s.Rationale is { Length: > 0 and <= 2000 } && (s.AuditArea?.Length ?? 0) <= 100));

  private static async Task<CommandResult<Context>> ReadAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, CancellationToken ct)
  {
    var m = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (m is null) return Denied<Context>();
    var auth = await AuthorizeAsync(db, actor, m, false, ct);
    if (!auth.Succeeded) return CommandResult<Context>.Fail(auth.ErrorCode!, auth.Message!);
    var d = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == m.DatasetId && x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.EngagementId == m.EngagementId, ct);
    if (d is null) return Denied<Context>();
    var allocations = await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.EngagementId == m.EngagementId && x.MappingVersionId == m.Id)
      .OrderBy(x => x.SourceAccountCode).ThenBy(x => x.DestinationCode).ThenBy(x => x.Id).Take(MappingApprovalWorkspace.MaximumAllocations + 1).ToListAsync(ct);
    var rows = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == d.Id).OrderBy(x => x.AccountCode).ThenBy(x => x.Id)
      .Take(MappingApprovalWorkspace.MaximumSourceRows + 1).ToListAsync(ct);
    if (allocations.Count > MappingApprovalWorkspace.MaximumAllocations || rows.Count > MappingApprovalWorkspace.MaximumSourceRows)
      return CommandResult<Context>.Fail(ErrorCodes.GateBlocked, "This source exceeds the bounded mapping editor. Use a separately approved workflow.");
    var taxonomy = await db.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.Code == m.TaxonomyVersion, ct);
    var nodes = taxonomy is null ? [] : await db.ReportingTaxonomyNodes.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id)
      .OrderBy(x => x.Code).ThenBy(x => x.Id).Take(5_001).ToListAsync(ct);
    if (nodes.Count > 5_000) return CommandResult<Context>.Fail(ErrorCodes.GateBlocked, "This taxonomy exceeds the bounded destination picker.");
    var chart = m.ClientChartVersionId is { } chartId ? await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == chartId && x.FirmId == actor.FirmId && x.ClientId == m.ClientId, ct) : null;
    var period = d.PeriodId is { } periodId ? await db.ClientReportingPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.Id == periodId && x.FirmId == actor.FirmId && x.ClientId == m.ClientId, ct) : null;
    var book = d.BookId is { } bookId ? await db.ClientReportingBooks.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bookId && x.FirmId == actor.FirmId && x.ClientId == m.ClientId && x.PeriodId == d.PeriodId, ct) : null;
    var firm = await db.FirmSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == actor.FirmId, ct);
    var safety = await db.ClientSafetyStates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == m.ClientId && x.FirmId == actor.FirmId, ct);
    if (firm is null || safety is null) return Denied<Context>();
    var frozen = await db.EngagementFileFreezes.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.EngagementId == m.EngagementId && x.State == "FROZEN", ct);
    var latest = await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == m.EngagementId).MaxAsync(x => (long?)x.Version, ct);
    var work = await AuthorizeAsync(db, actor, m, true, ct);
    var blocker = !work.Succeeded ? work.Message :
      d.SourceKind != "Raw" || d.ImportState != TrialBalanceImportStates.Sealed || d.ValidationStatus != "Accepted" || !d.Balanced || d.ControlTotal != 0m ? "A sealed, balanced, validated raw source is required." :
      period is null || period.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft) ? "A current open reporting period is required." :
      m.PeriodStart != period.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) || m.PeriodEnd != period.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? "Rebase this mapping to its exact reporting period before editing." :
      d.BookId is not null && (book is null || book.Status is not (AccountingWorkflowStates.Active or AccountingWorkflowStates.Draft)) ? "The reporting book is closed or unavailable." :
      frozen ? "The engagement file is frozen. An approved amendment is required." :
      chart is null || chart.Status != AccountingWorkflowStates.Approved || chart.EffectiveFrom > period.StartDate || chart.EffectiveTo is { } end && end < period.EndDate ? "The exact approved client chart must apply to this period." :
      taxonomy is null || taxonomy.Status != AccountingWorkflowStates.Approved ? "The exact reporting taxonomy must be approved." : null;
    var revision = Digest(new { actor.FirmId, actor.UserId, actor.SessionEpoch, m, d, allocations, rows, taxonomy, nodes, chart, period, book, firm, safety, frozen, latest, blocker });
    auth = await AuthorizeAsync(db, actor, m, false, ct);
    if (!auth.Succeeded) return CommandResult<Context>.Fail(auth.ErrorCode!, auth.Message!);
    var accounts = rows.GroupBy(x => x.AccountCode, StringComparer.Ordinal).Select(x => new MappingSourceAccount(x.Key, x.First().AccountName, x.Sum(y => y.Amount), x.First().Currency)).ToArray();
    return CommandResult<Context>.Ok(new(m, d, allocations.Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Fraction, x.Rationale, x.AuditArea, x.ResidualPolicy)).ToArray(), accounts, nodes, revision, safety.InputGeneration, blocker));
  }

  private static Task<CommandResult> AuthorizeAsync(IClientAccountingDbContext db, ActorContext actor, MappingVersion m, bool professional, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor, new(actor.FirmId, m.ClientId, m.EngagementId, Roles, InternalOnly: true, RequireProfessionalWork: professional), ct);

  public static async Task<CommandResult<MappingDraftEditor>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, int page = 1, string? search = null, CancellationToken ct = default)
  {
    if (page < 1 || page > 1_000 || (search?.Length ?? 0) > 80) return CommandResult<MappingDraftEditor>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose a bounded account page and search.");
    var first = await ReadAsync(db, actor, id, ct); if (!first.Succeeded) return CommandResult<MappingDraftEditor>.Fail(first.ErrorCode!, first.Message!);
    var final = await ReadAsync(db, actor, id, ct); if (!final.Succeeded) return CommandResult<MappingDraftEditor>.Fail(final.ErrorCode!, final.Message!);
    var c = final.Value!; if (c.Revision != first.Value!.Revision) return CommandResult<MappingDraftEditor>.Fail(ErrorCodes.StaleRevision, "Mapping inputs changed. Refresh before editing.");
    var q = search?.Trim() ?? "";
    var filtered = c.Accounts.Where(x => q.Length == 0 || x.AccountCode.Contains(q, StringComparison.OrdinalIgnoreCase) || x.AccountName.Contains(q, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray();
    if (page > Math.Max(1, (filtered.Length + PageSize - 1) / PageSize)) return CommandResult<MappingDraftEditor>.Fail(ErrorCodes.Accounting.MappingInvalid, "This account page is outside the filtered source.");
    var accounts = filtered.Skip((page - 1) * PageSize).Take(PageSize).Select(x => new MappingDraftAccount(x.AccountCode, x.AccountName, x.Amount, x.Currency, c.Allocations.Where(a => a.SourceAccountCode == x.AccountCode).ToArray())).ToArray();
    return CommandResult<MappingDraftEditor>.Ok(new(c.Mapping.Id, c.Mapping.ClientId, c.Mapping.EngagementId, c.Dataset.Id, c.Mapping.Version, c.Generation, c.Revision, c.Mapping.TaxonomyVersion,
      c.Mapping.ClientChartVersionId, c.Mapping.PeriodStart, c.Mapping.PeriodEnd, c.Accounts.Count, filtered.Length, page, PageSize, accounts,
      c.Nodes.Where(x => x.IsPosting).Select(x => new MappingDraftDestination(x.Code, x.Name, x.StatementSection)).ToArray(), c.Blocker is null, c.Blocker));
  }

  private static async Task<CommandResult<Plan>> PlanAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, MappingDraftIntent intent, CancellationToken ct)
  {
    var read = await ReadAsync(db, actor, id, ct); if (!read.Succeeded) return CommandResult<Plan>.Fail(read.ErrorCode!, read.Message!);
    var c = read.Value!;
    if (!ValidIntentShape(intent))
      return CommandResult<Plan>.Fail(ErrorCodes.Accounting.MappingInvalid, "A request identity, reviewed base and one to 200 explicit account changes are required.");
    if (intent.BaseRevision != c.Revision) return CommandResult<Plan>.Fail(ErrorCodes.StaleRevision, "Source, chart, taxonomy, generation or mapping lineage changed. Retain the draft and review the current base.");
    var changes = new List<MappingDraftAccountChange>(); var changed = new HashSet<string>(StringComparer.Ordinal);
    foreach (var change in intent.Changes)
    {
      if (change is null || string.IsNullOrWhiteSpace(change.AccountCode) || !changed.Add(change.AccountCode) || !c.Accounts.Any(x => x.AccountCode == change.AccountCode) || change.Splits is null || change.Splits.Count is < 1 or > 20)
        return CommandResult<Plan>.Fail(ErrorCodes.Accounting.MappingInvalid, "Changes require distinct exact source accounts and one to 20 explicit destination splits.");
      var after = new List<MappingAllocationInput>();
      foreach (var split in change.Splits)
      {
        if (split is null || string.IsNullOrWhiteSpace(split.DestinationCode) || string.IsNullOrWhiteSpace(split.Rationale) || split.Rationale.Length > 2000 || (split.AuditArea?.Length ?? 0) > 100 ||
          !Regex.IsMatch(split.Fraction ?? "", @"^[01](\.\d{1,6})?$", RegexOptions.CultureInvariant) || !decimal.TryParse(split.Fraction, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var fraction) || fraction <= 0m || fraction > 1m)
          return CommandResult<Plan>.Fail(ErrorCodes.Accounting.MappingInvalid, "Enter exact fractions greater than zero up to one, at most six decimal places, and a bounded rationale.");
        var node = c.Nodes.SingleOrDefault(x => x.IsPosting && x.Code == split.DestinationCode);
        if (node is null) return CommandResult<Plan>.Fail(ErrorCodes.Accounting.MappingInvalid, "Choose an exact posting destination in the approved taxonomy.");
        after.Add(new(change.AccountCode, node.Code, node.StatementSection, fraction, split.Rationale.Trim(), string.IsNullOrWhiteSpace(split.AuditArea) ? null : split.AuditArea.Trim()));
      }
      changes.Add(new(change.AccountCode, c.Allocations.Where(x => x.SourceAccountCode == change.AccountCode).ToArray(), after));
    }
    var allocations = c.Allocations.Where(x => !changed.Contains(x.SourceAccountCode)).Concat(changes.SelectMany(x => x.After)).OrderBy(x => x.SourceAccountCode, StringComparer.Ordinal).ThenBy(x => x.DestinationCode, StringComparer.Ordinal).ToArray();
    if (allocations.Length > MappingApprovalWorkspace.MaximumAllocations) return CommandResult<Plan>.Fail(ErrorCodes.GateBlocked, "The proposed complete mapping exceeds the interactive allocation limit.");
    var error = FinancialStatementService.ValidateReviewAllocations(c.Accounts, allocations) ?? await FinancialStatementService.ValidateApprovedTaxonomyAsync(db, actor.FirmId, c.Mapping.TaxonomyVersion, allocations, ct);
    var hash = Purpose(actor, id, intent);
    var revision = Digest(new { c.Revision, intent.RequestId, hash, allocations });
    var preview = new MappingDraftPreview(id, intent.RequestId, c.Revision, revision, hash, changes.Count, allocations.Length, changes.OrderBy(x => x.AccountCode, StringComparer.Ordinal).ToArray(), c.Blocker is null && error is null, c.Blocker ?? error);
    return CommandResult<Plan>.Ok(new(c, new(c.Dataset.Id, c.Mapping.TaxonomyVersion, c.Mapping.PeriodStart, c.Mapping.PeriodEnd, allocations, c.Mapping.ClientChartVersionId), preview));
  }

  private static string Purpose(ActorContext actor, Guid id, MappingDraftIntent intent) => Digest(new {
    actor.FirmId, actor.UserId, baseMapping = id, intent.RequestId, intent.BaseRevision,
    changes = intent.Changes.OrderBy(x => x.AccountCode, StringComparer.Ordinal).Select(x => new { x.AccountCode, splits = x.Splits.OrderBy(s => s.DestinationCode, StringComparer.Ordinal).Select(s => new { s.DestinationCode, s.Fraction, s.Rationale, s.AuditArea }) })
  });

  public static async Task<CommandResult<MappingDraftPreview>> PreviewAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, MappingDraftIntent intent, CancellationToken ct = default)
  {
    var first = await PlanAsync(db, actor, id, intent, ct); if (!first.Succeeded) return CommandResult<MappingDraftPreview>.Fail(first.ErrorCode!, first.Message!);
    var final = await PlanAsync(db, actor, id, intent, ct); if (!final.Succeeded) return CommandResult<MappingDraftPreview>.Fail(final.ErrorCode!, final.Message!);
    return first.Value!.Preview.Revision == final.Value!.Preview.Revision ? CommandResult<MappingDraftPreview>.Ok(final.Value.Preview) : CommandResult<MappingDraftPreview>.Fail(ErrorCodes.StaleRevision, "Mapping preview inputs changed. Review again.");
  }

  public static async Task<CommandResult<MappingDraftReceipt>> CreateAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, MappingDraftIntent intent, string revision, bool reviewed, CancellationToken ct = default)
  {
    // An exact retained receipt may be reconciled without preparing a second version.
    var receipt = await ReadReceiptAsync(db, actor, id, intent.RequestId, ct);
    if (!receipt.Succeeded) return CommandResult<MappingDraftReceipt>.Fail(receipt.ErrorCode!, receipt.Message!);
    if (!ValidIntentShape(intent)) return CommandResult<MappingDraftReceipt>.Fail(ErrorCodes.Accounting.MappingInvalid, "Provide a bounded exact mapping intent.");
    if (receipt.Value is not null)
    {
      var stored = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.Id == receipt.Value.MappingId && x.FirmId == actor.FirmId, ct);
      var auth = await AuthorizeAsync(db, actor, stored, true, ct);
      if (!auth.Succeeded) return CommandResult<MappingDraftReceipt>.Fail(auth.ErrorCode!, auth.Message!);
      if (!reviewed || stored.CreationReviewRevision != revision || stored.CreationRequestHash != Purpose(actor, id, intent)) return CommandResult<MappingDraftReceipt>.Fail(ErrorCodes.IdempotencyConflict, "This request identity belongs to a different reviewed mapping intent.");
      return CommandResult<MappingDraftReceipt>.Ok(receipt.Value);
    }
    var plan = await PlanAsync(db, actor, id, intent, ct);
    if (!plan.Succeeded)
    {
      // A concurrent exact request can publish between the initial receipt read
      // and the base snapshot. Resolve that immutable receipt without creating again.
      if (plan.ErrorCode == ErrorCodes.StaleRevision)
      {
        var concurrent = await ReadReceiptAsync(db, actor, id, intent.RequestId, ct);
        if (concurrent.Succeeded && concurrent.Value is not null)
          return await CreateAsync(db, actor, id, intent, revision, reviewed, ct);
      }
      return CommandResult<MappingDraftReceipt>.Fail(plan.ErrorCode!, plan.Message!);
    }
    var result = await FinancialStatementService.CreateMappingVersionAsync(db, actor, plan.Value!.Request, ct, new(id, intent, revision, reviewed));
    if (!result.Succeeded) return CommandResult<MappingDraftReceipt>.Fail(result.ErrorCode!, result.Message!);
    var retained = await ReadReceiptAsync(db, actor, id, intent.RequestId, ct);
    return retained.Succeeded && retained.Value is not null ? CommandResult<MappingDraftReceipt>.Ok(retained.Value) : CommandResult<MappingDraftReceipt>.Fail(ErrorCodes.StaleRevision, "Read the retained mapping request before another action.");
  }

  public static async Task<CommandResult<MappingDraftReceipt?>> ReadReceiptAsync(IClientAccountingDbContext db, ActorContext actor, Guid id, Guid requestId, CancellationToken ct = default)
  {
    var parent = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.FirmId == actor.FirmId, ct);
    if (parent is null) return Denied<MappingDraftReceipt?>();
    var auth = await AuthorizeAsync(db, actor, parent, false, ct); if (!auth.Succeeded) return CommandResult<MappingDraftReceipt?>.Fail(auth.ErrorCode!, auth.Message!);
    var m = await db.MappingVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == actor.FirmId && x.CreatedByUserId == actor.UserId && x.CreationRequestId == requestId, ct);
    if (m is not null && m.BaseMappingVersionId != id) return CommandResult<MappingDraftReceipt?>.Fail(ErrorCodes.IdempotencyConflict, "The request identity has another scoped intent.");
    auth = await AuthorizeAsync(db, actor, parent, false, ct); if (!auth.Succeeded) return CommandResult<MappingDraftReceipt?>.Fail(auth.ErrorCode!, auth.Message!);
    return CommandResult<MappingDraftReceipt?>.Ok(m is null ? null : new(id, requestId, m.Id, m.Version, m.Status, m.CreationRequestHash!, m.CreatedByUserId, m.CreatedAt));
  }

  internal static async Task<CommandResult<CreationProof>> ValidateFenceAsync(IClientAccountingDbContext db, ActorContext actor, CreateMappingVersionRequest request, MappingCreationFence fence, CancellationToken ct)
  {
    if (!fence.Reviewed || !SourceAcceptanceWorkspace.ValidHash(fence.ReviewRevision) || !ValidIntentShape(fence.Intent)) return CommandResult<CreationProof>.Fail(ErrorCodes.StaleRevision, "Review the exact proposed mapping before creating a version.");
    var receipt = await ReadReceiptAsync(db, actor, fence.BaseMappingId, fence.Intent.RequestId, ct);
    if (!receipt.Succeeded) return CommandResult<CreationProof>.Fail(receipt.ErrorCode!, receipt.Message!);
    if (receipt.Value is { } retained)
    {
      var m = await db.MappingVersions.AsNoTracking().SingleAsync(x => x.Id == retained.MappingId && x.FirmId == actor.FirmId, ct);
      var auth = await AuthorizeAsync(db, actor, m, true, ct); if (!auth.Succeeded) return CommandResult<CreationProof>.Fail(auth.ErrorCode!, auth.Message!);
      return m.CreationReviewRevision == fence.ReviewRevision && m.CreationRequestHash == Purpose(actor, fence.BaseMappingId, fence.Intent)
        ? CommandResult<CreationProof>.Ok(new(m.Id, m.CreationRequestHash!)) : CommandResult<CreationProof>.Fail(ErrorCodes.IdempotencyConflict, "This request identity belongs to a different mapping intent.");
    }
    var plan = await PlanAsync(db, actor, fence.BaseMappingId, fence.Intent, ct); if (!plan.Succeeded) return CommandResult<CreationProof>.Fail(plan.ErrorCode!, plan.Message!);
    var p = plan.Value!;
    if (p.Preview.Revision != fence.ReviewRevision || Digest(request) != Digest(p.Request)) return CommandResult<CreationProof>.Fail(ErrorCodes.StaleRevision, "The proposed mapping or reviewed context changed.");
    if (!p.Preview.CanCreate) return CommandResult<CreationProof>.Fail(ErrorCodes.GateBlocked, p.Preview.Blocker!);
    var mutable = await GeneralLedgerCompletenessWorkspace.MutablePeriodAsync(db, actor, p.Context.Mapping.ClientId, p.Context.Mapping.EngagementId, p.Context.Dataset.PeriodId!.Value, p.Context.Dataset.BookId, ct);
    return mutable.Succeeded ? CommandResult<CreationProof>.Ok(new(null, p.Preview.RequestHash)) : CommandResult<CreationProof>.Fail(mutable.ErrorCode!, mutable.Message!);
  }
}
