using System.IO.Compression;
using System.Text;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Domain.Tests;

public sealed class TrialBalanceXlsxImporterTests
{
  [Fact]
  [Trait("Profile", "Unit")]
  public void LiteralWorkbook_UsesRawBytesAndSharedNormalizedIdentity()
  {
    var bytes = Workbook("Cash", "10.25");
    var parsed = TrialBalanceXlsxImporter.Parse(bytes);

    Assert.Single(parsed.Rows);
    Assert.Equal("00100", parsed.Rows[0].AccountCode);
    Assert.Equal("QAR", parsed.Currency);
    Assert.Equal(Hashing.Sha256Hex(bytes), parsed.RawFileSha256Hex);
    Assert.Equal(parsed.NormalizedDatasetDigest, parsed.SourceHash);

    const string maximum = "9999999999999.999999";
    Assert.Equal(ClientNativeAmountRules.MaximumStoredAmount,
      TrialBalanceXlsxImporter.Parse(Workbook("Cash", maximum)).Rows[0].Amount);
    Assert.Equal(-ClientNativeAmountRules.MaximumStoredAmount,
      TrialBalanceXlsxImporter.Parse(Workbook("Cash", $"-{maximum}")).Rows[0].Amount);
    Assert.Throws<InvalidOperationException>(() => TrialBalanceXlsxImporter.Parse(Workbook("Cash", "10000000000000")));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceXlsxImporter.Parse(Workbook("Cash", "-10000000000000")));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void FormulaAndMacroWorkbooksAreRejected()
  {
    Assert.Throws<InvalidOperationException>(() => TrialBalanceXlsxImporter.Parse(Workbook("=HYPERLINK(\"x\")", "10.25")));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceXlsxImporter.Parse(Workbook("Cash", "10.25", macro: true)));
  }

  [Theory]
  [Trait("Profile", "Unit")]
  [InlineData("10000000000000", "9999999999999.999999", "amount exceeds the supported storage range.")]
  [InlineData("9999999999999.999999", "10000000000000", "amount exceeds the supported storage range.")]
  [InlineData("10000000000000", "10000000000000", "amount exceeds the supported storage range.")]
  [InlineData("1.0000001", "1", "amount exceeds 6 decimal places; no silent rounding.")]
  [InlineData("1", "1.0000001", "amount exceeds 6 decimal places; no silent rounding.")]
  public void SourceComponents_AreCheckedBeforeNetting(string debit, string credit, string error)
  {
    var bytes = Workbook([
      ["AccountCode", "AccountName", "Debit", "Credit", "Currency", "Entity", "MappingCode"],
      ["00100", "Cash", debit, credit, "QAR", "ENTITY-A", "ASSET"]]);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      TrialBalanceXlsxImporter.Parse(bytes, TrialBalanceImportProfile.DebitCreditV1));
    Assert.Equal($"Row 2: {error}", exception.Message);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void ExactSourceLimits_PreserveBothSignsAndComponents()
  {
    var bytes = Workbook([
      ["AccountCode", "AccountName", "Debit", "Credit", "Currency", "Entity", "MappingCode"],
      ["00100", "Cash", "9999999999999.999999", "0", "QAR", "ENTITY-A", "ASSET"],
      ["00200", "Equity", "0", "9999999999999.999999", "QAR", "ENTITY-A", "EQUITY"]]);
    var parsed = TrialBalanceXlsxImporter.Parse(bytes, TrialBalanceImportProfile.DebitCreditV1);

    Assert.Equal(new[]
    {
      new TbImportRow("00100", "Cash", 9_999_999_999_999.999999m, "QAR", "ENTITY-A", "ASSET", 9_999_999_999_999.999999m, 0m),
      new TbImportRow("00200", "Equity", -9_999_999_999_999.999999m, "QAR", "ENTITY-A", "EQUITY", 0m, 9_999_999_999_999.999999m)
    }, parsed.Rows);
    Assert.True(TrialBalanceCalculator.Parse(parsed.Rows).Balanced);
  }

  private static byte[] Workbook(string accountName, string amount, bool macro = false) => Workbook([
    ["AccountCode", "AccountName", "NetClosingBalance", "Currency", "Entity", "MappingCode"],
    ["00100", accountName, amount, "QAR", "ENTITY-A", "ASSET"]], macro);

  internal static byte[] Workbook(IReadOnlyList<IReadOnlyList<string>> rows, bool macro = false)
  {
    using var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
      Add(archive, "[Content_Types].xml", """
        <?xml version="1.0" encoding="UTF-8"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
          <Default Extension="xml" ContentType="application/xml" />
          <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml" />
          <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml" />
        </Types>
        """);
      Add(archive, "xl/workbook.xml", """
        <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
          <sheets><sheet name="TB" sheetId="1" r:id="rId1" /></sheets>
        </workbook>
        """);
      Add(archive, "xl/_rels/workbook.xml.rels", """
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
          <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml" />
        </Relationships>
        """);
      Add(archive, "xl/worksheets/sheet1.xml", $"""
        <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>
          {string.Join("", rows.Select((row, r) => $"<row r=\"{r + 1}\">" + string.Join("", row.Select((value, c) =>
            r > 0 && rows[0][c] is "NetClosingBalance" or "Debit" or "Credit"
              ? $"<c r=\"{(char)('A' + c)}{r + 1}\"><v>{value}</v></c>"
              : Inline($"{(char)('A' + c)}{r + 1}", value))) + "</row>"))}
        </sheetData></worksheet>
        """);
      if (macro)
      {
        Add(archive, "xl/vbaProject.bin", "not a real macro");
      }
    }
    return stream.ToArray();
  }

  private static string Inline(string cell, string value) => $"<c r=\"{cell}\" t=\"inlineStr\"><is><t>{System.Security.SecurityElement.Escape(value)}</t></is></c>";

  private static void Add(ZipArchive archive, string name, string value)
  {
    using var writer = new StreamWriter(archive.CreateEntry(name).Open(), Encoding.UTF8);
    writer.Write(value);
  }
}
