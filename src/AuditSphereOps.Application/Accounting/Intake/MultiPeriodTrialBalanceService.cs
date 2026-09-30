using System.Text;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record MultiPeriodPreviewPeriod(string PeriodCode, Guid? PeriodId, int RowCount, string? Currency, decimal? NetTotal, bool Balanced, string? Error);
public sealed record MultiPeriodPreview(string FileSha256, IReadOnlyList<MultiPeriodPreviewPeriod> Periods)
{
  public bool CanImport => Periods.Count > 0 && Periods.All(x => x.Error is null && x.PeriodId is not null && x.Balanced);
}
public sealed record MultiPeriodImportResult(IReadOnlyList<(string PeriodCode, Guid DatasetId)> Datasets);

/// <summary>
/// One Excel or CSV file holding several periods, identified by a PeriodCode column. The file is split into one
/// single-period source per period, each parsed and validated independently by the existing importer (so balances,
/// identities and digests never cross periods), then imported as its own dataset against the client's reporting period.
/// Nothing is imported unless every period validates.
/// </summary>
public static class MultiPeriodTrialBalanceService
{
  public const string PeriodColumn = "PeriodCode";

  /// <summary>Pure split: header without the period column plus the rows of each period, as canonical CSV text.</summary>
  public static IReadOnlyDictionary<string, string> Split(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
  {
    var periodIndex = header.Select((h, i) => (h: h.Trim(), i)).Where(x => string.Equals(x.h, PeriodColumn, StringComparison.OrdinalIgnoreCase)).Select(x => x.i).DefaultIfEmpty(-1).First();
    if (periodIndex < 0) throw new InvalidOperationException($"The file needs a '{PeriodColumn}' column to split periods.");
    var keep = Enumerable.Range(0, header.Count).Where(i => i != periodIndex && header[i].Trim().Length > 0).ToArray();
    var groups = rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
      .GroupBy(r => (periodIndex < r.Count ? r[periodIndex] : string.Empty).Trim().ToUpperInvariant(), StringComparer.Ordinal).ToList();
    if (groups.Any(g => g.Key.Length == 0)) throw new InvalidOperationException("Every row needs a period code.");
    if (groups.Count > 24) throw new InvalidOperationException("A multi-period file is limited to 24 periods.");
    static string Csv(IEnumerable<string> cells) => string.Join(',', cells.Select(c => c.Contains(',') || c.Contains('"') || c.Contains('\n') ? "\"" + c.Replace("\"", "\"\"") + "\"" : c));
    return groups.OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g =>
    {
      var text = new StringBuilder(Csv(keep.Select(i => header[i].Trim()))).Append('\n');
      foreach (var row in g) text.Append(Csv(keep.Select(i => i < row.Count ? row[i] : string.Empty))).Append('\n');
      return text.ToString();
    }, StringComparer.Ordinal);
  }

  /// <summary>Reads CSV text or an XLSX workbook into header and cells.</summary>
  public static (IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows) ReadTable(string fileName, byte[] content)
  {
    if (fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) return TrialBalanceXlsxImporter.ReadTable(content);
    if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Upload an .xlsx or .csv file.");
    if (content.Length > 10_000_000) throw new InvalidOperationException("Trial-balance file exceeds the 10 MB intake limit.");
    var lines = ParseCsv(Encoding.UTF8.GetString(content).TrimStart('﻿'));
    if (lines.Count < 2) throw new InvalidOperationException("The file has no data rows.");
    return (lines[0], lines.Skip(1).ToList());
  }

  public static async Task<CommandResult<MultiPeriodPreview>> PreviewAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, string fileName, byte[] content, CancellationToken ct = default)
  {
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, clientId,
      RequiredRoles: ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator", "Senior", "Staff"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MultiPeriodPreview>.Fail(auth.ErrorCode!, auth.Message!);
    IReadOnlyDictionary<string, string> split;
    try
    {
      var (header, rows) = ReadTable(fileName, content);
      split = Split(header, rows);
    }
    catch (InvalidOperationException ex) { return CommandResult<MultiPeriodPreview>.Fail(ErrorCodes.Accounting.ImportRejected, ex.Message); }
    var codes = split.Keys.ToArray();
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId && codes.Contains(x.PeriodCode.ToUpper()))
      .ToListAsync(ct);
    var result = new List<MultiPeriodPreviewPeriod>();
    foreach (var (code, csv) in split)
    {
      var period = periods.FirstOrDefault(p => string.Equals(p.PeriodCode, code, StringComparison.OrdinalIgnoreCase));
      try
      {
        var parsed = TrialBalanceCsvImporter.Parse(csv, ProfileFor(csv));
        var net = MoneyPolicy.Normalize(parsed.Rows.Sum(x => x.Amount));
        result.Add(new(code, period?.Id, parsed.Rows.Count, parsed.Currency, net, net == 0m,
          period is null ? $"No reporting period {code} exists for this client." : net != 0m ? $"Period {code} does not balance (net {net:N2})." : null));
      }
      catch (InvalidOperationException ex) { result.Add(new(code, period?.Id, 0, null, null, false, ex.Message)); }
    }
    return CommandResult<MultiPeriodPreview>.Ok(new(Hashing.Sha256Hex(content), result));
  }

  public static async Task<CommandResult<MultiPeriodImportResult>> ImportAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId, Guid engagementId, string fileName, byte[] content, CancellationToken ct = default)
  {
    var preview = await PreviewAsync(db, actor, clientId, fileName, content, ct);
    if (!preview.Succeeded) return CommandResult<MultiPeriodImportResult>.Fail(preview.ErrorCode!, preview.Message!);
    if (!preview.Value!.CanImport)
      return CommandResult<MultiPeriodImportResult>.Fail(ErrorCodes.Accounting.ImportRejected,
        string.Join(" ", preview.Value.Periods.Where(x => x.Error is not null).Select(x => x.Error)));
    var (header, rows) = ReadTable(fileName, content);
    var split = Split(header, rows);
    var periods = await db.ClientReportingPeriods.AsNoTracking().Where(x => preview.Value.Periods.Select(p => p.PeriodId).Contains(x.Id)).ToListAsync(ct);
    var imported = new List<(string, Guid)>();
    foreach (var item in preview.Value.Periods)
    {
      var period = periods.Single(x => x.Id == item.PeriodId);
      var csv = split[item.PeriodCode];
      var result = await TrialBalanceImportService.ImportWithProfileAsync(db, actor, clientId, engagementId, csv, ProfileFor(csv),
        new TrialBalanceImportContext(period.Id, null, period.Basis), null, ct);
      if (!result.Succeeded)
        return CommandResult<MultiPeriodImportResult>.Fail(result.ErrorCode!, $"Period {item.PeriodCode}: {result.Message} Earlier periods in this file were imported: {string.Join(", ", imported.Select(x => x.Item1))}.");
      imported.Add((item.PeriodCode, result.Value));
    }
    return CommandResult<MultiPeriodImportResult>.Ok(new(imported));
  }

  private static TrialBalanceImportProfile ProfileFor(string csv)
  {
    var header = csv[..csv.IndexOf('\n')];
    return header.Split(',').Any(h => h.Trim().Equals("Debit", StringComparison.OrdinalIgnoreCase))
      ? TrialBalanceImportProfile.DebitCreditV1 : TrialBalanceImportProfile.SignedNetV1;
  }

  private static List<IReadOnlyList<string>> ParseCsv(string text)
  {
    var rows = new List<IReadOnlyList<string>>();
    var row = new List<string>();
    var cell = new StringBuilder();
    var quoted = false;
    for (var i = 0; i < text.Length; i++)
    {
      var c = text[i];
      if (quoted)
      {
        if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
        else if (c == '"') quoted = false;
        else cell.Append(c);
      }
      else if (c == '"') quoted = true;
      else if (c == ',') { row.Add(cell.ToString()); cell.Clear(); }
      else if (c is '\n' or '\r')
      {
        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
        row.Add(cell.ToString()); cell.Clear();
        rows.Add(row); row = [];
      }
      else cell.Append(c);
    }
    if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row); }
    return rows.Where(r => r.Any(x => x.Length > 0)).ToList();
  }
}
