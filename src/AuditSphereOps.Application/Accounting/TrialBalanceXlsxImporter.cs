using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Bounded, read-only XLSX intake. It accepts one worksheet containing literal
/// values, rejects macros/formulas/external links, and produces the same typed
/// rows and normalized identity as CSV intake.
/// </summary>
public static class TrialBalanceXlsxImporter
{
  public const int MaxBytes = 25_000_000;
  public const int MaxUncompressedBytes = 100_000_000;
  private const int MaxZipEntries = 256;
  private static readonly string[] RequiredColumns =
    ["AccountCode", "AccountName", "NetClosingBalance", "Currency", "Entity", "MappingCode"];

  public static ParsedCsvFile Parse(byte[] xlsxBytes)
  {
    if (xlsxBytes is null || xlsxBytes.Length == 0)
      throw new InvalidOperationException("Empty trial-balance workbook cannot be processed.");
    if (xlsxBytes.Length > MaxBytes)
      throw new InvalidOperationException("Trial-balance workbook exceeds the 25 MB intake limit.");

    using var stream = new MemoryStream(xlsxBytes, writable: false);
    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
    if (archive.Entries.Count > MaxZipEntries)
      throw new InvalidOperationException("Trial-balance workbook contains too many ZIP entries.");
    var uncompressed = archive.Entries.Sum(x => Math.Max(0, x.Length));
    if (uncompressed > MaxUncompressedBytes)
      throw new InvalidOperationException("Trial-balance workbook exceeds the decompressed intake limit.");
    if (archive.Entries.Any(x => x.FullName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase) ||
        x.FullName.EndsWith(".xlam", StringComparison.OrdinalIgnoreCase) ||
        x.FullName.Contains("vbaProject", StringComparison.OrdinalIgnoreCase) ||
        x.FullName.Contains("externalLinks", StringComparison.OrdinalIgnoreCase)))
      throw new InvalidOperationException("Macro-enabled and externally linked workbooks are rejected.");

    var contentTypes = ReadXml(archive, "[Content_Types].xml");
    if (contentTypes.Descendants().Any(x => x.Attributes().Any(a =>
        a.Name.LocalName == "ContentType" && (a.Value.Contains("macro", StringComparison.OrdinalIgnoreCase) ||
          a.Value.Contains("externalLink", StringComparison.OrdinalIgnoreCase)))))
      throw new InvalidOperationException("Macro-enabled and externally linked workbooks are rejected.");

    var workbookEntry = archive.GetEntry("xl/workbook.xml")
      ?? throw new InvalidOperationException("The workbook metadata is missing.");
    var workbook = ReadXml(workbookEntry);
    var sheets = workbook.Descendants().Where(x => x.Name.LocalName == "sheet").ToArray();
    if (sheets.Length != 1)
      throw new InvalidOperationException("The XLSX profile requires exactly one worksheet per controlled import.");
    if (workbook.Descendants().Any(x => x.Name.LocalName is "externalReference" or "externalReferences"))
      throw new InvalidOperationException("Externally linked worksheets are rejected.");

    var relationships = archive.GetEntry("xl/_rels/workbook.xml.rels") is { } relationshipEntry
      ? ReadXml(relationshipEntry).Descendants().Where(x => x.Name.LocalName == "Relationship")
        .ToDictionary(x => (string?)x.Attribute("Id") ?? string.Empty, x => (string?)x.Attribute("Target") ?? string.Empty,
          StringComparer.Ordinal)
      : new Dictionary<string, string>(StringComparer.Ordinal);
    if (workbookEntry is not null && archive.GetEntry("xl/_rels/workbook.xml.rels") is { } relEntry &&
        ReadXml(relEntry).Descendants().Any(x => x.Name.LocalName == "Relationship" &&
          string.Equals((string?)x.Attribute("TargetMode"), "External", StringComparison.OrdinalIgnoreCase)))
      throw new InvalidOperationException("Externally linked workbook relationships are rejected.");
    var relationshipId = (string?)sheets[0].Attribute(XName.Get("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
    if (relationshipId is null || !relationships.TryGetValue(relationshipId, out var target))
      throw new InvalidOperationException("The worksheet relationship is missing.");
    var sheetEntry = FindEntry(archive, target);
    var sheet = ReadXml(sheetEntry);
    var sharedStrings = archive.GetEntry("xl/sharedStrings.xml") is { } sharedEntry
      ? ReadSharedStrings(sharedEntry)
      : [];
    var rows = sheet.Descendants().Where(x => x.Name.LocalName == "row").ToArray();
    if (rows.Length < 2)
      throw new InvalidOperationException("Trial-balance workbook has no data rows.");
    if (rows.Length - 1 > TrialBalanceCsvImporter.MaxRows)
      throw new InvalidOperationException($"Trial-balance workbook exceeds the {TrialBalanceCsvImporter.MaxRows:N0}-row intake limit.");

    var header = ReadRow(rows[0], sharedStrings, 1);
    var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in header)
      if (!string.IsNullOrWhiteSpace(pair.Value))
        index.TryAdd(pair.Value.Trim(), pair.Key);
    foreach (var required in RequiredColumns)
      if (!index.ContainsKey(required))
        throw new InvalidOperationException($"Trial-balance workbook is missing column '{required}'.");

    var parsedRows = new List<TbImportRow>(rows.Length - 1);
    var keys = new HashSet<(string Entity, string AccountCode)>();
    string? currency = null;
    for (var rowNumber = 1; rowNumber < rows.Length; rowNumber++)
    {
      var values = ReadRow(rows[rowNumber], sharedStrings, rowNumber + 1);
      string Get(string column) => values.GetValueOrDefault(index[column], string.Empty).Trim();
      var code = Get("AccountCode");
      var name = Get("AccountName");
      var amountText = Get("NetClosingBalance");
      var rowCurrency = Get("Currency");
      var entity = Get("Entity");
      var mapping = Get("MappingCode");
      if (code.Length == 0 || code.Length > 32 || entity.Length == 0 || entity.Length > 32)
        throw new InvalidOperationException($"Row {rowNumber + 1}: account code and entity are required within their limits.");
      if (LooksLikeFormula(code) || LooksLikeFormula(name) || LooksLikeFormula(amountText) || LooksLikeFormula(rowCurrency) ||
          LooksLikeFormula(entity) || LooksLikeFormula(mapping))
        throw new InvalidOperationException($"Row {rowNumber + 1}: formula-shaped cells are rejected.");
      if (rowCurrency.Length != 3 || !rowCurrency.All(char.IsLetter))
        throw new InvalidOperationException($"Row {rowNumber + 1}: currency must be a 3-letter code.");
      rowCurrency = rowCurrency.ToUpperInvariant();
      currency ??= rowCurrency;
      if (!string.Equals(currency, rowCurrency, StringComparison.Ordinal))
        throw new InvalidOperationException("Mixed currencies are never summed: one workbook, one currency.");
      if (!keys.Add((entity, code)))
        throw new InvalidOperationException($"Row {rowNumber + 1}: duplicate entity/account '{entity}/{code}'.");
      if (!decimal.TryParse(amountText, System.Globalization.NumberStyles.Number,
          System.Globalization.CultureInfo.InvariantCulture, out var amount))
        throw new InvalidOperationException($"Row {rowNumber + 1}: amount '{amountText}' is not a valid invariant-culture decimal.");
      if ((decimal.GetBits(amount)[3] >> 16) > MoneyPolicy.MaxScale)
        throw new InvalidOperationException($"Row {rowNumber + 1}: amount exceeds 6 decimal places; no silent rounding.");
      parsedRows.Add(new TbImportRow(code, name, MoneyPolicy.Normalize(amount), rowCurrency, entity,
        string.IsNullOrEmpty(mapping) ? null : mapping));
    }

    if (parsedRows.Count == 0)
      throw new InvalidOperationException("Trial-balance workbook has no data rows.");
    return TrialBalanceCsvImporter.BuildParsed(parsedRows, currency!, Hashing.Sha256Hex(xlsxBytes));
  }

  private static Dictionary<int, string> ReadRow(XElement row, IReadOnlyList<string> sharedStrings, int rowNumber)
  {
    var result = new Dictionary<int, string>();
    foreach (var cell in row.Descendants().Where(x => x.Name.LocalName == "c"))
    {
      if (cell.Elements().Any(x => x.Name.LocalName == "f"))
        throw new InvalidOperationException($"Row {rowNumber}: formula cells are rejected; supply literal values.");
      var reference = (string?)cell.Attribute("r") ?? string.Empty;
      var column = ColumnNumber(reference);
      var type = (string?)cell.Attribute("t");
      var value = type == "inlineStr"
        ? string.Concat(cell.Descendants().Where(x => x.Name.LocalName == "t").Select(x => x.Value))
        : (string?)cell.Elements().FirstOrDefault(x => x.Name.LocalName == "v");
      if (type == "s" && int.TryParse(value, out var sharedIndex))
        value = sharedIndex >= 0 && sharedIndex < sharedStrings.Count ? sharedStrings[sharedIndex] : throw new InvalidOperationException("A shared string reference is invalid.");
      result[column] = value ?? string.Empty;
    }
    return result;
  }

  private static IReadOnlyList<string> ReadSharedStrings(ZipArchiveEntry entry) =>
    ReadXml(entry).Descendants().Where(x => x.Name.LocalName == "si")
      .Select(x => string.Concat(x.Descendants().Where(t => t.Name.LocalName == "t").Select(t => t.Value))).ToArray();

  private static XDocument ReadXml(ZipArchive archive, string name) =>
    ReadXml(archive.GetEntry(name) ?? throw new InvalidOperationException($"The workbook part '{name}' is missing."));

  private static XDocument ReadXml(ZipArchiveEntry entry)
  {
    using var stream = entry.Open();
    using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxUncompressedBytes });
    return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
  }

  private static ZipArchiveEntry FindEntry(ZipArchive archive, string target)
  {
    var normalized = target.Replace('\\', '/').TrimStart('/');
    var candidates = normalized.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
      ? new[] { normalized }
      : new[] { "xl/" + normalized, "xl/worksheets/" + Path.GetFileName(normalized), normalized };
    return candidates.Select(archive.GetEntry).FirstOrDefault(x => x is not null)
      ?? throw new InvalidOperationException("The worksheet part is missing.");
  }

  private static int ColumnNumber(string reference)
  {
    var number = 0;
    foreach (var c in reference.TakeWhile(char.IsLetter))
      number = number * 26 + char.ToUpperInvariant(c) - 'A' + 1;
    return number == 0 ? throw new InvalidOperationException("A worksheet cell has no column reference.") : number;
  }

  private static bool LooksLikeFormula(string value) => value.StartsWith('=') || value.StartsWith('+') || value.StartsWith('@');
}
