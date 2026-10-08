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
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void FormulaAndMacroWorkbooksAreRejected()
  {
    Assert.Throws<InvalidOperationException>(() => TrialBalanceXlsxImporter.Parse(Workbook("=HYPERLINK(\"x\")", "10.25")));
    Assert.Throws<InvalidOperationException>(() => TrialBalanceXlsxImporter.Parse(Workbook("Cash", "10.25", macro: true)));
  }

  private static byte[] Workbook(string accountName, string amount, bool macro = false)
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
          <row r="1">{Inline("A1", "AccountCode")}{Inline("B1", "AccountName")}{Inline("C1", "NetClosingBalance")}{Inline("D1", "Currency")}{Inline("E1", "Entity")}{Inline("F1", "MappingCode")}</row>
          <row r="2">{Inline("A2", "00100")}{Inline("B2", accountName)}<c r="C2"><v>{amount}</v></c>{Inline("D2", "QAR")}{Inline("E2", "ENTITY-A")}{Inline("F2", "ASSET")}</row>
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
