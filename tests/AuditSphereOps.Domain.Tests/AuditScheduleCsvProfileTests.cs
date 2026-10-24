using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Audit;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditScheduleCsvProfileTests
{
  private static readonly AuditScheduleCsvColumns Columns = new("Code", "Description", "Amount", "Currency",
    "Id", "Transaction", "Posting", "Delivery", "Service");
  private const string Header = "Id, Code ,Description,Amount,Currency,Transaction,Posting,Delivery,Service,Extra";
  private const string Row = "row-a,0010,Invoice line,-12.345678,QAR,2026-01-01,2026-01-02,,2026-01-03, untouched ";
  private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

  [Fact]
  public void HeaderDiscoveryReturnsTrimmedUniqueNamesWithoutChangingOriginalParserEvidence()
  {
    var bytes = Encoding.UTF8.GetPreamble().Concat(Bytes(Header + "\r\n" + Row)).ToArray();
    var discovered = AuditScheduleCsvProfile.DiscoverHeaders("listing.csv", bytes);
    Assert.Equal(new[] { "Id", "Code", "Description", "Amount", "Currency", "Transaction", "Posting", "Delivery", "Service", "Extra" }, discovered);
    var parsed = AuditScheduleCsvProfile.Parse("listing.csv", bytes, "QAR", Columns);
    Assert.Equal(" Code ", parsed.Headers[1]);
  }

  [Theory]
  [InlineData("listing.txt", "Id, Code\nrow,0010")]
  [InlineData("listing.csv", "Id, Code\nrow")]
  [InlineData("listing.csv", "Id,Id\nrow,0010")]
  [InlineData("listing.csv", "Id,Code\n")]
  [InlineData("listing.csv", "Id,Code\nrow,0010\n\n")]
  public void HeaderDiscoveryRejectsUnsupportedOrAmbiguousDocuments(string name, string text)
  {
    Assert.Throws<FormatException>(() => AuditScheduleCsvProfile.DiscoverHeaders(name, Bytes(text)));
  }

  [Fact]
  public void ExactSignedRowsPreserveOriginalCellsPhysicalLinesAndDistinctDeterministicIdentities()
  {
    var text = Header + "\r\n" +
      " row-a ,0010,\" Debit, \"\"quoted\"\" \",-12.345678,QAR,2026-01-01,2026-01-02,,2026-01-03, untouched \r\n" +
      "row-b,0000,Zero,0,QAR,,,,,\r\n" +
      "row-c,0020,Credit,+12.345678,QAR,,2026-01-04,,,\r\n";
    var bytes = Encoding.UTF8.GetPreamble().Concat(Bytes(text)).ToArray();
    var parsed = AuditScheduleCsvProfile.Parse("listing.CSV", bytes, "QAR", Columns);
    Assert.Equal("audit-client-listing-csv.v1", parsed.ProfileVersion);
    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), parsed.RawFileSha256);
    Assert.NotEqual(parsed.RawFileSha256, parsed.NormalizedRowsSha256);
    Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(parsed.NormalizedRowsJson))),
      parsed.NormalizedRowsSha256);
    using (var canonical = JsonDocument.Parse(parsed.NormalizedRowsJson))
    {
      Assert.Equal("audit-client-listing-csv.v1", canonical.RootElement.GetProperty("ProfileVersion").GetString());
      Assert.Equal("QAR", canonical.RootElement.GetProperty("Currency").GetString());
      var canonicalRows = canonical.RootElement.GetProperty("Rows");
      Assert.Equal(3, canonicalRows.GetArrayLength());
      Assert.Equal("row-a", canonicalRows[0].GetProperty("StableRowId").GetString());
      Assert.Equal(2, canonicalRows[0].GetProperty("SourceLineNumber").GetInt32());
      Assert.Equal("0010", canonicalRows[0].GetProperty("AccountCode").GetString());
      Assert.Equal("Debit, \"quoted\"", canonicalRows[0].GetProperty("Description").GetString());
      Assert.Equal("-12.345678", canonicalRows[0].GetProperty("SignedAmount").GetString());
      Assert.Equal("0.000000", canonicalRows[1].GetProperty("SignedAmount").GetString());
      Assert.Equal("12.345678", canonicalRows[2].GetProperty("SignedAmount").GetString());
      Assert.Equal("2026-01-01", canonicalRows[0].GetProperty("TransactionDate").GetString());
      Assert.Equal(JsonValueKind.Null, canonicalRows[0].GetProperty("DeliveryDate").ValueKind);
    }
    Assert.False(parsed.HasTechnicalStableRowIds);
    Assert.Equal(new[] { -12.345678m, 0m, 12.345678m }, parsed.Rows.Select(x => x.SignedAmount));
    Assert.Equal(new[] { "0010", "0000", "0020" }, parsed.Rows.Select(x => x.AccountCode));
    Assert.Equal(new[] { 2, 3, 4 }, parsed.Rows.Select(x => x.SourceLineNumber));
    Assert.Equal("row-a", parsed.Rows[0].StableRowId);
    Assert.Equal("Debit, \"quoted\"", parsed.Rows[0].Description);
    Assert.Equal(new DateOnly(2026, 1, 1), parsed.Rows[0].TransactionDate);
    Assert.Equal(new DateOnly(2026, 1, 2), parsed.Rows[0].PostingDate);
    Assert.Null(parsed.Rows[0].DeliveryDate);
    Assert.Equal(new DateOnly(2026, 1, 3), parsed.Rows[0].ServiceDate);
    Assert.Null(parsed.Rows[1].TransactionDate);
    Assert.Null(parsed.Rows[1].PostingDate);
    Assert.Null(parsed.Rows[1].DeliveryDate);
    Assert.Null(parsed.Rows[1].ServiceDate);
    using var original = JsonDocument.Parse(parsed.Rows[0].OriginalValuesJson);
    Assert.Equal(10, original.RootElement.EnumerateObject().Count());
    Assert.Equal(" row-a ", original.RootElement.GetProperty("Id").GetString());
    Assert.Equal("0010", original.RootElement.GetProperty(" Code ").GetString());
    Assert.Equal(" Debit, \"quoted\" ", original.RootElement.GetProperty("Description").GetString());
    Assert.Equal(" untouched ", original.RootElement.GetProperty("Extra").GetString());
    Assert.Equal(parsed.NormalizedRowsSha256,
      AuditScheduleCsvProfile.Parse("listing.csv", bytes, "QAR", Columns).NormalizedRowsSha256);
    var alternateTransport = AuditScheduleCsvProfile.Parse("listing.csv", Bytes(text.Replace("\r\n", "\n")), "QAR", Columns);
    Assert.NotEqual(parsed.RawFileSha256, alternateTransport.RawFileSha256);
    Assert.Equal(parsed.NormalizedRowsSha256, alternateTransport.NormalizedRowsSha256);
    Assert.Equal(parsed.NormalizedRowsSha256,
      AuditScheduleCsvProfile.Parse("listing.csv", Bytes(text.Replace("Zero,0,QAR", "Zero,0.000000,QAR")), "QAR", Columns).NormalizedRowsSha256);
    Assert.NotEqual(parsed.NormalizedRowsSha256,
      AuditScheduleCsvProfile.Parse("listing.csv", Bytes(text.Replace("-12.345678", "-12.345677")), "QAR", Columns).NormalizedRowsSha256);

    var maximum = AuditScheduleCsvProfile.Parse("listing.csv", Bytes(Header + "\n" + Row.Replace("-12.345678", "9999999999999.999999")), "QAR", Columns);
    Assert.Equal(9_999_999_999_999.999999m, Assert.Single(maximum.Rows).SignedAmount);

    // Missing source IDs get a labelled technical identity, never an invented invoice/customer key.
    var technical = AuditScheduleCsvProfile.Parse("listing.csv", Bytes(Header + "\n" + Row), "QAR", Columns with { StableRowId = null });
    Assert.True(technical.HasTechnicalStableRowIds);
    Assert.Equal($"technical:sha256:{technical.RawFileSha256}:record:2", Assert.Single(technical.Rows).StableRowId);
    Assert.Equal(technical.Rows[0], AuditScheduleCsvProfile.Parse("listing.csv", Bytes(Header + "\n" + Row), "QAR",
      Columns with { StableRowId = null }).Rows[0]);
  }

  [Theory]
  [InlineData("nul")]
  [InlineData("sql-precision")]
  [InlineData("sql-total")]
  [InlineData("precision")]
  [InlineData("precision-zero")]
  [InlineData("overflow")]
  [InlineData("rounding-overflow")]
  [InlineData("exponent")]
  [InlineData("separator")]
  [InlineData("missing-money")]
  [InlineData("missing-account")]
  [InlineData("invalid-date")]
  [InlineData("currency")]
  [InlineData("mixed-currency")]
  [InlineData("currency-argument")]
  [InlineData("duplicate-header")]
  [InlineData("duplicate-id")]
  [InlineData("missing-header")]
  [InlineData("reused-map")]
  [InlineData("empty-map")]
  [InlineData("blank-optional-map")]
  [InlineData("unclosed-quote")]
  [InlineData("unquoted-quote")]
  [InlineData("after-quote")]
  [InlineData("multiline")]
  [InlineData("blank-record")]
  [InlineData("width")]
  [InlineData("columns-limit")]
  [InlineData("rows-limit")]
  [InlineData("raw-json-limit")]
  [InlineData("aggregate-original-evidence")]
  [InlineData("bytes-limit")]
  [InlineData("utf8")]
  [InlineData("extension")]
  [InlineData("empty")]
  public void UnsupportedOrAmbiguousInputFailsWithoutTruncatingOrRounding(string kind)
  {
    var csv = Header + "\n" + Row;
    var map = Columns;
    var currency = "QAR";
    var name = "listing.csv";
    switch (kind)
    {
      case "nul": csv = csv.Replace("Invoice line", "Invoice\0line"); break;
      case "sql-precision": csv = csv.Replace("-12.345678", "10000000000000"); break;
      case "sql-total": csv = csv.Replace("-12.345678", "9999999999999.999999") + "\n" + Row.Replace("row-a", "row-b").Replace("-12.345678", "1"); break;
      case "precision": csv = csv.Replace("-12.345678", "-12.3456789"); break;
      case "precision-zero": csv = csv.Replace("-12.345678", "0.0000000"); break;
      case "overflow": csv = csv.Replace("-12.345678", "79228162514264337593543950336"); break;
      case "rounding-overflow": csv = csv.Replace("-12.345678", "7922816251426433759354395033.51"); break;
      case "exponent": csv = csv.Replace("-12.345678", "1e2"); break;
      case "separator": csv = csv.Replace("-12.345678", "\"1,000\""); break;
      case "missing-money": csv = csv.Replace("-12.345678", ""); break;
      case "missing-account": csv = csv.Replace("0010", " "); break;
      case "invalid-date": csv = csv.Replace("2026-01-01", "2026-02-30"); break;
      case "currency": csv = csv.Replace("QAR", "qar"); break;
      case "mixed-currency": csv += "\n" + Row.Replace("row-a", "row-b").Replace("QAR", "USD"); break;
      case "currency-argument": currency = "qar"; break;
      case "duplicate-header": csv = csv.Replace(",Extra", ", Code"); break;
      case "duplicate-id": csv += "\n" + Row.Replace("row-a", " row-a "); break;
      case "missing-header": map = map with { AccountCode = "Other" }; break;
      case "reused-map": map = map with { Description = "Code" }; break;
      case "empty-map": map = map with { AccountCode = "" }; break;
      case "blank-optional-map": map = map with { DeliveryDate = "" }; break;
      case "unclosed-quote": csv = csv.Replace("Invoice line", "\"Invoice line"); break;
      case "unquoted-quote": csv = csv.Replace("Invoice line", "Invoice\" line"); break;
      case "after-quote": csv = csv.Replace("Invoice line", "\"Invoice\" x"); break;
      case "multiline": csv = csv.Replace("Invoice line", "\"Invoice\nline\""); break;
      case "blank-record": csv = Header + "\n\n" + Row; break;
      case "width": csv += ",extra"; break;
      case "columns-limit": csv = string.Join(',', Enumerable.Range(0, 257).Select(x => $"H{x}")) + "\n" + Row; break;
      case "rows-limit": csv = Header + "\n" + string.Join('\n', Enumerable.Range(0, 20_001).Select(x => Row.Replace("row-a", $"row-{x}"))); break;
      case "raw-json-limit": csv = csv.Replace(" untouched ", new string('x', 20_000)); break;
      case "aggregate-original-evidence":
        var longHeader = Header.Replace("Extra", new string('h', 15_000));
        var single = AuditScheduleCsvProfile.Parse("listing.csv", Bytes(longHeader + "\n" + Row), "QAR", Columns);
        var original = Assert.Single(single.Rows).OriginalValuesJson;
        Assert.InRange(original.Length, 1, 20_000);
        Assert.True(Encoding.UTF8.GetByteCount(original) * 600L > 8 * 1024 * 1024);
        csv = longHeader + "\n" + string.Join('\n', Enumerable.Range(0, 600)
          .Select(x => Row.Replace("row-a", $"row-{x}")));
        Assert.True(Encoding.UTF8.GetByteCount(csv) < 100_000);
        break;
      case "extension": name = "listing.xlsx"; break;
    }
    var bytes = kind switch
    {
      "utf8" => new byte[] { 0xff },
      "bytes-limit" => new byte[10_000_001],
      "empty" => [],
      _ => Bytes(csv)
    };
    var error = Assert.Throws<FormatException>(() => AuditScheduleCsvProfile.Parse(name, bytes, currency, map));
    if (kind == "aggregate-original-evidence")
      Assert.Contains("aggregate original cell evidence", error.Message);
  }
}
