// Safe CSV trial-balance parsing (§§16.2, 16.4; NT-09/10). Pure function, no I/O:
// account codes stay strings (leading zeros preserved), amounts are exact decimals
// (never floats), one explicit currency per file, and anything formula-shaped,
// oversized, or ambiguous is rejected with a safe message. XLSX arrives later;
// legacy .xls needs its separately tested adapter and is refused here.
using System.Globalization;
using System.Text;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

public sealed record ParsedCsvFile(
  IReadOnlyList<TbImportRow> Rows,
  string Currency,
  string RawFileSha256Hex,
  string NormalizedDatasetDigest)
{
  // Compatibility alias for callers that used the old reconstructed-source hash.
  public string SourceHash => NormalizedDatasetDigest;
}

public static class TrialBalanceCsvImporter
{
  public const int MaxRows = 20000;
  public const string NormalizedDigestVersion = "tb-csv-normalized.v2";
  private static readonly string[] RequiredColumns =
    ["AccountCode", "AccountName", "NetClosingBalance", "Currency", "Entity", "MappingCode"];

  public static ParsedCsvFile Parse(string csvText)
  {
    if (string.IsNullOrWhiteSpace(csvText))
      throw new InvalidOperationException("Empty trial-balance file cannot be processed.");
    if (csvText.Length > 10_000_000)
      throw new InvalidOperationException("Trial-balance file exceeds the 10 MB intake limit.");

    var lines = csvText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length < 2)
      throw new InvalidOperationException("Trial-balance file has no data rows.");
    if (lines.Length - 1 > MaxRows)
      throw new InvalidOperationException($"Trial-balance file exceeds the {MaxRows:N0}-row intake limit.");

    var header = SplitLine(lines[0]);
    var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < header.Count; i++)
      index.TryAdd(header[i].Trim(), i);
    foreach (var required in RequiredColumns)
      if (!index.ContainsKey(required))
        throw new InvalidOperationException($"Trial-balance file is missing column '{required}'.");

    var rows = new List<TbImportRow>(lines.Length - 1);
    var keys = new HashSet<(string Entity, string AccountCode)>();
    string? currency = null;
    var rawFileSha256Hex = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(csvText));
    for (var n = 1; n < lines.Length; n++)
    {
      var fields = SplitLine(lines[n]);
      string Get(string column) =>
        index[column] < fields.Count ? fields[index[column]].Trim() : string.Empty;
      var code = Get("AccountCode");
      var name = Get("AccountName");
      var amountText = Get("NetClosingBalance");
      var rowCurrency = Get("Currency");
      var entity = Get("Entity");
      var mapping = Get("MappingCode");

      if (code.Length == 0 || code.Length > 32)
        throw new InvalidOperationException($"Row {n}: account code is required (max 32 characters).");
      if (code.StartsWith('=') || code.StartsWith('+') || code.StartsWith('@'))
        throw new InvalidOperationException($"Row {n}: account code looks like a formula and is rejected.");
      if (name.StartsWith('=') || name.StartsWith('+') || name.StartsWith('@'))
        throw new InvalidOperationException($"Row {n}: account name looks like a formula and is rejected.");
      if (entity.Length == 0 || entity.Length > 32)
        throw new InvalidOperationException($"Row {n}: entity is required (max 32 characters).");
      if (rowCurrency.Length is not 3 || !rowCurrency.All(char.IsLetter))
        throw new InvalidOperationException($"Row {n}: currency must be a 3-letter code.");
      rowCurrency = rowCurrency.ToUpperInvariant();
      currency ??= rowCurrency;
      if (!string.Equals(currency, rowCurrency, StringComparison.Ordinal))
        throw new InvalidOperationException("Mixed currencies are never summed: one file, one currency.");
      if (!keys.Add((entity, code)))
        throw new InvalidOperationException($"Row {n}: duplicate entity/account '{entity}/{code}'.");
      var amount = ParseAmount(amountText, n);
      rows.Add(new TbImportRow(code, name, amount, rowCurrency, entity,
        string.IsNullOrEmpty(mapping) ? null : mapping));
    }
    if (rows.Count == 0)
      throw new InvalidOperationException("Trial-balance file has no data rows.");
    return BuildParsed(rows, currency!, rawFileSha256Hex);
  }

  internal static ParsedCsvFile BuildParsed(IReadOnlyList<TbImportRow> rows, string currency, string rawFileSha256Hex)
  {
    var normalized = new StringBuilder(NormalizedDigestVersion).Append('\n');
    foreach (var row in rows.OrderBy(x => x.Entity, StringComparer.Ordinal)
      .ThenBy(x => x.AccountCode, StringComparer.Ordinal)
      .ThenBy(x => x.AccountName, StringComparer.Ordinal)
      .ThenBy(x => x.MappingCode, StringComparer.Ordinal))
    {
      normalized.Append(row.Entity).Append('|').Append(row.AccountCode).Append('|')
        .Append(row.AccountName).Append('|')
        .Append(row.Amount.ToString("0.000000", CultureInfo.InvariantCulture)).Append('|')
        .Append(row.Currency).Append('|').Append(row.MappingCode ?? string.Empty).Append('\n');
    }
    return new ParsedCsvFile(rows, currency, rawFileSha256Hex, Hashing.Sha256Hex(normalized.ToString()));
  }

  private static decimal ParseAmount(string text, int row)
  {
    if (text.StartsWith('='))
      throw new InvalidOperationException($"Row {row}: formula cells are rejected; supply literal values.");
    if (text.Equals("NaN", StringComparison.OrdinalIgnoreCase) ||
        text.EndsWith("Infinity", StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException($"Row {row}: non-finite amounts are rejected.");
    if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
      throw new InvalidOperationException($"Row {row}: amount '{text}' is not a valid invariant-culture decimal.");
    if (decimal.GetBits(amount)[3] >> 16 > MoneyPolicy.MaxScale)
      throw new InvalidOperationException($"Row {row}: amount exceeds 6 decimal places; no silent rounding.");
    return MoneyPolicy.Normalize(amount);
  }

  // Minimal RFC-4180 reader: quoted fields with embedded commas/doubled quotes.
  private static List<string> SplitLine(string line)
  {
    var fields = new List<string>();
    var current = new StringBuilder();
    var quoted = false;
    for (var i = 0; i < line.Length; i++)
    {
      var c = line[i];
      if (quoted)
      {
        if (c == '"')
        {
          if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
          else quoted = false;
        }
        else current.Append(c);
      }
      else if (c == '"') quoted = true;
      else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
      else current.Append(c);
    }
    if (quoted)
      throw new InvalidOperationException("Unterminated quoted field in trial-balance file.");
    fields.Add(current.ToString());
    return fields;
  }
}
