// Guarded trial-balance read: the caller supplies only the dataset ID; the server
// resolves firm/client/engagement from the stored row and enforces assignment inside
// the command (§§8.3, 28.2). Guessed IDs fail with a nondisclosing scope error, and
// counts/metadata follow the same rule because they go through this path.
using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record TrialBalanceDatasetDto(
  Guid Id, Guid FirmId, Guid ClientId, Guid EngagementId,
  string Currency, string ValidationStatus, bool Balanced, long Revision);

public sealed record TrialBalanceRowDto(
  Guid Id, Guid DatasetId, string AccountCode, string AccountName,
  decimal Amount, decimal? SourceDebit, decimal? SourceCredit,
  string Currency, string Entity, string? MappingCode);

public sealed record TrialBalanceRowsPage(
  IReadOnlyList<TrialBalanceRowDto> Items, int TotalCount, int Page, int PageSize,
  decimal TotalAmount, decimal? TotalDebit, decimal? TotalCredit);

public sealed record TrialBalanceExportDto(string FileName, string Csv, long Revision);

public static class TrialBalanceDatasetQuery
{
  public static async Task<CommandResult<TrialBalanceDatasetDto>> GetDatasetAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid datasetId,
    CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == datasetId &&
        d.ImportState == TrialBalanceImportStates.Sealed, ct);
    if (dataset is null)
      return CommandResult<TrialBalanceDatasetDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<TrialBalanceDatasetDto>.Fail(auth.ErrorCode!, auth.Message!);

    return CommandResult<TrialBalanceDatasetDto>.Ok(new TrialBalanceDatasetDto(
      dataset.Id, dataset.FirmId, dataset.ClientId, dataset.EngagementId,
      dataset.Currency, dataset.ValidationStatus, dataset.Balanced, dataset.Revision));
  }

  public static async Task<CommandResult<TrialBalanceRowsPage>> GetTrialBalanceRowsAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid datasetId,
    string? accountCodeFilter = null,
    int page = 1,
    int pageSize = 100,
    CancellationToken ct = default)
  {
    if (page < 1 || pageSize is < 1 or > 1000)
      return CommandResult<TrialBalanceRowsPage>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid page number and size (1-1000) are required.");

    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == datasetId &&
        d.ImportState == TrialBalanceImportStates.Sealed, ct);
    if (dataset is null)
      return CommandResult<TrialBalanceRowsPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<TrialBalanceRowsPage>.Fail(auth.ErrorCode!, auth.Message!);

    var query = db.TrialBalanceRows.AsNoTracking().Where(r => r.DatasetId == dataset.Id);
    if (!string.IsNullOrWhiteSpace(accountCodeFilter))
    {
      var filter = accountCodeFilter.Trim();
      query = query.Where(r => r.AccountCode.StartsWith(filter));
    }

    var totalCount = await query.CountAsync(ct);
    var totalAmount = totalCount > 0 ? await query.SumAsync(r => r.Amount, ct) : 0m;
    var totalDebit = totalCount > 0 ? await query.SumAsync(r => r.SourceDebit, ct) : null;
    var totalCredit = totalCount > 0 ? await query.SumAsync(r => r.SourceCredit, ct) : null;

    var skip = (page - 1) * pageSize;
    var rows = await query.OrderBy(r => r.AccountCode)
      .Skip(skip)
      .Take(pageSize)
      .Select(r => new TrialBalanceRowDto(
        r.Id, r.DatasetId, r.AccountCode, r.AccountName,
        r.Amount, r.SourceDebit, r.SourceCredit, r.Currency, r.Entity, r.MappingCode))
      .ToListAsync(ct);

    return CommandResult<TrialBalanceRowsPage>.Ok(new TrialBalanceRowsPage(
      rows, totalCount, page, pageSize,
      MoneyPolicy.Normalize(totalAmount),
      totalDebit.HasValue ? MoneyPolicy.Normalize(totalDebit.Value) : null,
      totalCredit.HasValue ? MoneyPolicy.Normalize(totalCredit.Value) : null));
  }

  public static async Task<CommandResult<TrialBalanceExportDto>> ExportTrialBalanceCsvAsync(
    IAuditSphereDbContext db,
    ActorContext actor,
    Guid datasetId,
    CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == datasetId &&
        d.ImportState == TrialBalanceImportStates.Sealed, ct);
    if (dataset is null)
      return CommandResult<TrialBalanceExportDto>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId), ct);
    if (!auth.Succeeded)
      return CommandResult<TrialBalanceExportDto>.Fail(auth.ErrorCode!, auth.Message!);

    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(r => r.DatasetId == dataset.Id)
      .OrderBy(r => r.AccountCode)
      .ToListAsync(ct);

    var headers = new[]
    {
      "account_code", "account_name", "amount", "debit", "credit",
      "currency", "entity", "mapping_code", "dataset_id", "dataset_revision"
    };

    var lines = new List<string> { string.Join(',', headers.Select(CsvEscape)) };
    foreach (var r in rows)
    {
      var fields = new[]
      {
        CsvEscape(r.AccountCode),
        CsvEscape(r.AccountName),
        r.Amount.ToString("0.######", CultureInfo.InvariantCulture),
        r.SourceDebit.HasValue ? r.SourceDebit.Value.ToString("0.######", CultureInfo.InvariantCulture) : string.Empty,
        r.SourceCredit.HasValue ? r.SourceCredit.Value.ToString("0.######", CultureInfo.InvariantCulture) : string.Empty,
        CsvEscape(r.Currency),
        CsvEscape(r.Entity),
        CsvEscape(r.MappingCode),
        CsvEscape(dataset.Id.ToString("D")),
        dataset.Revision.ToString(CultureInfo.InvariantCulture)
      };
      lines.Add(string.Join(',', fields));
    }

    var fileName = $"auditsphere-tb-{dataset.Id:D}-r{dataset.Revision}.csv";
    return CommandResult<TrialBalanceExportDto>.Ok(new TrialBalanceExportDto(
      fileName, string.Join('\n', lines) + '\n', dataset.Revision));
  }

  private static string CsvEscape(string? value)
  {
    var text = value ?? string.Empty;
    if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@') text = "'" + text;
    return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
  }
}

public sealed record TrialBalanceIssueDto(
  Guid Id, string RowKey, string Severity, string Code, string Message, DateTimeOffset CreatedAt);

public sealed record TrialBalanceIssuesPage(
  IReadOnlyList<TrialBalanceIssueDto> Items, int TotalCount, int Page, int PageSize);

public static class TrialBalanceValidationIssueQuery
{
  /// <summary>Paged row-level validation issues for one dataset revision, so a rejected
  /// import can be explained without the original upload.</summary>
  public static async Task<CommandResult<TrialBalanceIssuesPage>> GetIssuesAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid datasetId,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (datasetId == Guid.Empty || page < 1 || pageSize is < 1 or > 500)
      return CommandResult<TrialBalanceIssuesPage>.Fail(ErrorCodes.Accounting.ImportRejected,
        "The issue page request is invalid.");
    var dataset = await db.TrialBalanceDatasets.AsNoTracking()
      .SingleOrDefaultAsync(d => d.Id == datasetId && d.FirmId == actor.FirmId, ct);
    if (dataset is null)
      return CommandResult<TrialBalanceIssuesPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(dataset.FirmId, dataset.ClientId, dataset.EngagementId, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<TrialBalanceIssuesPage>.Fail(auth.ErrorCode!, auth.Message!);

    var issues = db.TrialBalanceValidationIssues.AsNoTracking()
      .Where(x => x.FirmId == dataset.FirmId && x.DatasetId == dataset.Id);
    var totalCount = await issues.CountAsync(ct);
    var items = await issues
      .OrderBy(x => x.CreatedAt).ThenBy(x => x.RowKey).ThenBy(x => x.Id)
      .Skip((page - 1) * pageSize).Take(pageSize)
      .Select(x => new TrialBalanceIssueDto(
        x.Id, x.RowKey, x.Severity, x.Code, x.Message, x.CreatedAt))
      .ToListAsync(ct);
    return CommandResult<TrialBalanceIssuesPage>.Ok(new TrialBalanceIssuesPage(items, totalCount, page, pageSize));
  }
}
