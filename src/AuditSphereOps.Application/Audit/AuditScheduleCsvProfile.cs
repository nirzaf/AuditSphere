using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuditSphereOps.Application.Audit;

/// <summary>Exact, case-sensitive names after header trimming. Unmapped cells remain in original evidence.</summary>
public sealed record AuditScheduleCsvColumns(
  string AccountCode, string Description, string SignedAmount, string Currency,
  string? StableRowId = null, string? TransactionDate = null, string? PostingDate = null,
  string? DeliveryDate = null, string? ServiceDate = null);

public sealed record AuditScheduleCsvParseResult(
  string ProfileVersion, string Currency, IReadOnlyList<string> Headers, AuditScheduleCsvColumns Columns,
  string RawFileSha256, string NormalizedRowsSha256, bool HasTechnicalStableRowIds,
  IReadOnlyList<ScheduleRowInput> Rows)
{
  /// <summary>The exact canonical UTF-8 hash input; not a reserialization of the original cells.</summary>
  public string NormalizedRowsJson { get; init; } = string.Empty;
}

/// <summary>
/// Pure parsing of exact server-supplied client-listing bytes. This proves neither source acceptance
/// nor party identity. V1 excludes multiline cells and blank records so row numbers are physical lines.
/// </summary>
public static class AuditScheduleCsvProfile
{
  public const string Version = "audit-client-listing-csv.v1";
  public const int MaxBytes = 10_000_000;
  public const int MaxRows = 20_000;
  public const int MaxColumns = 256;
  public const int MaxOriginalValuesChars = 20_000;
  public const int MaxOriginalEvidenceBytes = 8 * 1024 * 1024;
  // Existing persisted schedule rows and controls use numeric(19,6).
  public const decimal MaxMoneyMagnitude = 9_999_999_999_999.999999m;

  public static AuditScheduleCsvParseResult Parse(
    string fileName, byte[] bytes, string currency, AuditScheduleCsvColumns columns)
  {
    if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
        bytes is null || bytes.Length is < 1 or > MaxBytes)
      throw new FormatException("Choose a nonempty CSV file of at most 10,000,000 bytes.");
    if (!IsCurrency(currency) || columns is null)
      throw new FormatException("An explicit uppercase three-letter currency and column map are required.");

    // Hash before decoding: a BOM, quoting or newline change remains a different original file.
    var rawHash = Digest(bytes);
    string text;
    try { text = new UTF8Encoding(false, true).GetString(bytes); }
    catch (DecoderFallbackException error) { throw new FormatException("The CSV must be valid UTF-8.", error); }
    if (text.StartsWith('\uFEFF')) text = text[1..];
    if (text.Contains('\0')) throw new FormatException("The CSV cannot contain NUL characters.");
    var records = ReadRecords(text);
    if (records.Count < 2) throw new FormatException("A header and at least one data record are required.");
    var rawHeaders = records[0];
    var headers = rawHeaders.Select(x => x.Trim()).ToArray();
    if (headers.Any(string.IsNullOrEmpty) || headers.Distinct(StringComparer.Ordinal).Count() != headers.Length)
      throw new FormatException("Headers must be nonempty and unique after trimming.");
    var index = headers.Select((name, position) => (name, position))
      .ToDictionary(x => x.name, x => x.position, StringComparer.Ordinal);
    string?[] selected = [columns.AccountCode, columns.Description, columns.SignedAmount, columns.Currency,
      columns.StableRowId, columns.TransactionDate, columns.PostingDate, columns.DeliveryDate, columns.ServiceDate];
    if (selected.Take(4).Any(string.IsNullOrEmpty) ||
        selected.Where(x => x is not null).Any(x => !index.ContainsKey(x!)) ||
        selected.Where(x => x is not null).Distinct(StringComparer.Ordinal).Count() != selected.Count(x => x is not null))
      throw new FormatException("Map each required field and use a distinct exact header for every selected field.");

    var rows = new List<ScheduleRowInput>(records.Count - 1);
    var stableIds = new HashSet<string>(StringComparer.Ordinal);
    var originalEvidenceBytes = 0;
    for (var i = 1; i < records.Count; i++)
    {
      var cells = records[i];
      var physicalLine = i + 1;
      if (cells.Count != headers.Length)
        throw new FormatException($"CSV line {physicalLine}: the cell count must match the header.");
      string Cell(string name) => cells[index[name]].Trim();
      string Required(string name, int maximum)
      {
        var value = Cell(name);
        if (value.Length is 0 || value.Length > maximum)
          throw new FormatException($"CSV line {physicalLine}: {name} is empty or exceeds {maximum} characters.");
        return value;
      }
      DateOnly? Date(string? name)
      {
        if (name is null || Cell(name).Length == 0) return null;
        if (!DateOnly.TryParseExact(Cell(name), "yyyy-MM-dd", CultureInfo.InvariantCulture,
              DateTimeStyles.None, out var date))
          throw new FormatException($"CSV line {physicalLine}: {name} must be a real ISO date.");
        return date;
      }
      var stableId = columns.StableRowId is null
        ? $"technical:sha256:{rawHash}:record:{physicalLine.ToString(CultureInfo.InvariantCulture)}"
        : Required(columns.StableRowId, 200);
      if (!stableIds.Add(stableId)) throw new FormatException($"CSV line {physicalLine}: duplicate normalized stable row ID.");
      var rowCurrency = Required(columns.Currency, 3);
      if (!IsCurrency(rowCurrency) || rowCurrency != currency)
        throw new FormatException($"CSV line {physicalLine}: every currency must equal {currency}.");
      var amount = Money(Cell(columns.SignedAmount), physicalLine);
      // Preserve every original header and decoded cell verbatim, including unmapped cells and spaces.
      var original = JsonSerializer.Serialize(rawHeaders.Select((name, position) => (name, position))
        .ToDictionary(x => x.name, x => cells[x.position], StringComparer.Ordinal));
      if (original.Length > MaxOriginalValuesChars)
        throw new FormatException($"CSV line {physicalLine}: original cell JSON exceeds 20,000 characters.");
      var originalBytes = Encoding.UTF8.GetByteCount(original);
      if (originalBytes > MaxOriginalEvidenceBytes - originalEvidenceBytes)
        throw new FormatException("The aggregate original cell evidence exceeds 8 MiB.");
      originalEvidenceBytes += originalBytes;
      rows.Add(new(stableId, physicalLine, Required(columns.AccountCode, 100),
        Required(columns.Description, 1000), amount, rowCurrency, Date(columns.TransactionDate),
        Date(columns.PostingDate), Date(columns.DeliveryDate), Date(columns.ServiceDate), original));
    }

    var signedTotal = rows.Sum(x => x.SignedAmount);
    if (signedTotal is < -MaxMoneyMagnitude or > MaxMoneyMagnitude)
      throw new FormatException("The complete signed control total exceeds numeric(19,6).");

    // Canonical parsed values are separate from the exact raw cells/file. F6 never rounds: Money rejected excess scale.
    var canonical = JsonSerializer.Serialize(new
    {
      ProfileVersion = Version, Currency = currency,
      Rows = rows.Select(x => new
      {
        x.StableRowId, x.SourceLineNumber, x.AccountCode, x.Description,
        SignedAmount = x.SignedAmount.ToString("F6", CultureInfo.InvariantCulture), x.Currency,
        TransactionDate = Iso(x.TransactionDate), PostingDate = Iso(x.PostingDate),
        DeliveryDate = Iso(x.DeliveryDate), ServiceDate = Iso(x.ServiceDate)
      })
    });
    return new(Version, currency, Array.AsReadOnly(rawHeaders.ToArray()), columns, rawHash,
      Digest(Encoding.UTF8.GetBytes(canonical)), columns.StableRowId is null, rows.AsReadOnly())
    {
      NormalizedRowsJson = canonical
    };
  }

  private static string? Iso(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
  private static bool IsCurrency(string? value) => value is { Length: 3 } && value.All(c => c is >= 'A' and <= 'Z');
  private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

  private static decimal Money(string value, int line)
  {
    var start = value.StartsWith('-') || value.StartsWith('+') ? 1 : 0;
    var point = value.IndexOf('.');
    var integerEnd = point < 0 ? value.Length : point;
    if (integerEnd <= start || value.Skip(start).Any(c => c is not (>= '0' and <= '9') && c != '.') ||
        point >= 0 && (value.LastIndexOf('.') != point || value.Length - point - 1 is < 1 or > 6) ||
        !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
          CultureInfo.InvariantCulture, out var amount))
      throw new FormatException($"CSV line {line}: use a signed decimal with at most six places, no exponent or separators.");
    // Decimal.TryParse can round long fractional representations near its precision boundary.
    var exact = value[start..].Replace(".", "", StringComparison.Ordinal).TrimStart('0');
    var scale = point < 0 ? 0 : value.Length - point - 1;
    var parsedDigits = amount.ToString($"F{scale}", CultureInfo.InvariantCulture).TrimStart('-')
      .Replace(".", "", StringComparison.Ordinal).TrimStart('0');
    if (exact != parsedDigits || amount is < -MaxMoneyMagnitude or > MaxMoneyMagnitude)
      throw new FormatException($"CSV line {line}: the amount cannot be represented exactly.");
    return amount;
  }

  // The public TB reader drops blank records and accepts loose quoting. V1 needs stricter lexical rules.
  private static List<IReadOnlyList<string>> ReadRecords(string text)
  {
    var records = new List<IReadOnlyList<string>>();
    var row = new List<string>();
    var cell = new StringBuilder();
    var quoted = false;
    var afterQuote = false;
    void EndCell()
    {
      row.Add(cell.ToString()); cell.Clear(); afterQuote = false;
      if (row.Count > MaxColumns) throw new FormatException("CSV records support at most 256 columns.");
    }
    void EndRecord()
    {
      EndCell();
      if (row.All(string.IsNullOrWhiteSpace)) throw new FormatException("Blank CSV records are unsupported.");
      records.Add(row); row = [];
      if (records.Count > MaxRows + 1) throw new FormatException("The CSV supports at most 20,000 data records.");
    }
    for (var i = 0; i < text.Length; i++)
    {
      var c = text[i];
      if (quoted)
      {
        if (c is '\r' or '\n') throw new FormatException("Multiline CSV fields are unsupported.");
        if (c == '"')
        {
          if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
          else { quoted = false; afterQuote = true; }
        }
        else cell.Append(c);
      }
      else if (c == ',') EndCell();
      else if (c is '\r' or '\n')
      {
        if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
        EndRecord();
      }
      else if (c == '"' && cell.Length == 0 && !afterQuote) quoted = true;
      else
      {
        if (c == '"' || afterQuote) throw new FormatException("Malformed CSV quoting.");
        cell.Append(c);
      }
    }
    if (quoted) throw new FormatException("An unterminated CSV field is unsupported.");
    if (cell.Length > 0 || row.Count > 0 || afterQuote) EndRecord();
    return records;
  }
}
