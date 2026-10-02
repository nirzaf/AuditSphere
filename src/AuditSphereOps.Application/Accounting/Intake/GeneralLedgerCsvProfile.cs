using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AuditSphereOps.Application.Accounting;

/// <summary>Explicit bounded GL CSV profile. No spreadsheet inference, currency conversion or amount rounding.</summary>
public static class GeneralLedgerCsvProfile
{
    public const int MaxBytes = 10_000_000;
    public const int MaxLines = 5_000;
    public const int MaxJournals = 1_000;
    public const string Version = "gl-csv-v1";
    public const string RequiredColumns = "PeriodCode,Entity,Currency,JournalId,LineId,DocumentNumber,PostingDate,SourceSystem,SourceUser,AccountCode,Debit,Credit,OriginalCurrency,OriginalAmount,FunctionalAmount";
    private static readonly string[] Optional = ["BookCode", "DocumentDate", "ServiceDate", "ReversalReference", "IsManual", "IsYearEnd", "PartyIdentifier", "Branch", "CostCentre", "Department", "Project", "IntercompanyCounterparty"];
    public sealed record Parsed(string PeriodCode, string BookCode, string Entity, string Currency, IReadOnlyList<GeneralLedgerTransactionInput> Transactions);

    public static Parsed Parse(string name, byte[] bytes)
    {
        if (!name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) || bytes.Length is < 1 or > MaxBytes)
            throw new FormatException("Choose a GL CSV file up to 10,000,000 bytes. Other file profiles require a separately supported importer.");
        var rows = Table(new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF'));
        if (rows.Count < 2 || rows.Count > MaxLines + 1) throw new FormatException("The GL CSV must contain 1 to 5,000 data rows.");
        var header = rows[0]; var required = RequiredColumns.Split(',');
        if (header.Distinct(StringComparer.Ordinal).Count() != header.Count || required.Any(x => !header.Contains(x)) || header.Any(x => !required.Contains(x) && !Optional.Contains(x)))
            throw new FormatException("Use the exact GL CSV v1 columns. Missing, duplicate or unsupported columns are refused.");
        var index = header.Select((x, i) => (x, i)).ToDictionary(x => x.x, x => x.i, StringComparer.Ordinal);
        var groups = new Dictionary<string, GeneralLedgerTransactionInput>(StringComparer.OrdinalIgnoreCase);
        string? period = null, book = null, entity = null, currency = null;
        for (var n = 1; n < rows.Count; n++)
        {
            var row = rows[n];
            if (row.Count != header.Count) throw new FormatException($"CSV row {n + 1} has an unexpected number of cells.");
            string Cell(string key, int max = 100)
            {
                var value = index.TryGetValue(key, out var i) ? row[i].Trim() : "";
                if (value.Length > max || value.Any(c => char.IsControl(c))) throw new FormatException($"CSV row {n + 1}, {key}: unsupported text or length.");
                return value;
            }
            string Required(string key, int max = 100) { var v = Cell(key, max); if (v.Length == 0) throw new FormatException($"CSV row {n + 1}, {key}: a value is required."); return v; }
            decimal Amount(string key)
            {
                var value = Required(key, 24);
                if (!Regex.IsMatch(value, @"^-?\d{1,14}(\.\d{1,6})?$", RegexOptions.CultureInvariant) || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
                    throw new FormatException($"CSV row {n + 1}, {key}: use an exact decimal with at most six decimal places; no exponent or separators.");
                return amount;
            }
            DateOnly? Date(string key, bool requiredDate = false)
            {
                var value = Cell(key); if (value.Length == 0 && !requiredDate) return null;
                if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new FormatException($"CSV row {n + 1}, {key}: use a real ISO date."); return date;
            }
            bool Flag(string key) { var value = Cell(key); return value switch { "" or "false" => false, "true" => true, _ => throw new FormatException($"CSV row {n + 1}, {key}: use true or false.") }; }
            var p = Required("PeriodCode"); var b = Cell("BookCode"); var e = Required("Entity", 200); var c = Required("Currency", 3).ToUpperInvariant();
            if (period is not null && (p != period || b != book || e != entity || c != currency)) throw new FormatException($"CSV row {n + 1}: one file must describe one exact period, book, entity and functional currency.");
            period = p; book = b; entity = e; currency = c;
            var journal = Required("JournalId", 200);
            var line = new GeneralLedgerLineInput(Required("LineId", 200), Required("AccountCode"), Amount("Debit"), Amount("Credit"), Required("OriginalCurrency", 3).ToUpperInvariant(), Amount("OriginalAmount"), Amount("FunctionalAmount"), Cell("PartyIdentifier", 200), Cell("Branch"), Cell("CostCentre"), Cell("Department"), Cell("Project"), Cell("IntercompanyCounterparty", 200));
            var transaction = new GeneralLedgerTransactionInput(journal, Required("DocumentNumber", 200), Date("PostingDate", true)!.Value, Date("DocumentDate"), Required("SourceUser", 200), Required("SourceSystem"), Cell("ReversalReference", 200) is { Length: > 0 } reversal ? reversal : null, Flag("IsManual"), Flag("IsYearEnd"), [line], Date("ServiceDate"));
            if (groups.TryGetValue(journal, out var prior))
            {
                if (prior with { Lines = transaction.Lines } != transaction) throw new FormatException($"CSV row {n + 1}: journal metadata differs between its lines.");
                groups[journal] = prior with { Lines = prior.Lines.Concat([line]).ToArray() };
            }
            else groups.Add(journal, transaction);
            if (groups.Count > MaxJournals) throw new FormatException("The interactive GL CSV profile supports at most 1,000 journals. Use the approved chunk importer for larger sources.");
        }
        return new(period!, book!, entity!, currency!, groups.Values.ToArray());
    }

    private static List<List<string>> Table(string text)
    {
        var rows = new List<List<string>>(); var row = new List<string>(); var cell = new StringBuilder();
        var quoted = false; var afterQuote = false;
        void EndCell() { row.Add(cell.ToString()); cell.Clear(); afterQuote = false; }
        void EndRow() { EndCell(); if (row.Any(x => x.Length > 0)) rows.Add(row); row = []; if (rows.Count > MaxLines + 1) throw new FormatException("The GL CSV exceeds 5,000 rows."); }
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted) { if (c == '"') { if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; } else { quoted = false; afterQuote = true; } } else cell.Append(c); }
            else if (c == ',') EndCell();
            else if (c is '\r' or '\n') { if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; EndRow(); }
            else if (c == '"' && cell.Length == 0 && !afterQuote) quoted = true;
            else { if (c == '"' || afterQuote) throw new FormatException("Malformed CSV quoting. Correct the file before preview."); cell.Append(c); }
            if (cell.Length > 1000) throw new FormatException("A GL CSV cell exceeds its supported length.");
        }
        if (quoted) throw new FormatException("An unterminated CSV quote blocks import.");
        if (cell.Length > 0 || row.Count > 0 || afterQuote) EndRow();
        return rows;
    }
}
