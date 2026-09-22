using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialPackageOfficeArtifact(
  string ArtifactVersion,
  string ContentType,
  string FileExtension,
  string ArtifactSha256Hex,
  byte[] ArtifactBytes,
  Guid ArtifactId = default);

public static class FinancialPackageOfficeRenderer
{
  public static FinancialPackageOfficeArtifact Render(string artifactVersion, string canonicalPackageText)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPackageText);
    var (bytes, contentType, extension) = artifactVersion switch
    {
      FinancialPackageArtifactVersions.Workbook =>
        (RenderWorkbook(canonicalPackageText), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
      FinancialPackageArtifactVersions.Word =>
        (RenderWord(canonicalPackageText), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
      _ => throw new ArgumentOutOfRangeException(nameof(artifactVersion), "Unsupported Office artifact version.")
    };
    return new(artifactVersion, contentType, extension, Hashing.Sha256Hex(bytes), bytes);
  }

  private static byte[] RenderWorkbook(string text)
  {
    using var stream = new MemoryStream();
    using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
    {
      var workbook = document.AddWorkbookPart();
      workbook.Workbook = new S.Workbook();
      var worksheet = workbook.AddNewPart<WorksheetPart>("rId2");
      var rows = new S.SheetData();
      foreach (var line in Lines(text))
        rows.Append(new S.Row(TextCell(line)));
      worksheet.Worksheet = new S.Worksheet(rows);
      workbook.Workbook.Append(new S.Sheets(new S.Sheet
      {
        Id = "rId2",
        SheetId = 1,
        Name = "Financial package"
      }));
      workbook.Workbook.Save();
    }
    return NormalizeZip(stream.ToArray());
  }

  private static byte[] RenderWord(string text)
  {
    using var stream = new MemoryStream();
    using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
    {
      var main = document.AddMainDocumentPart();
      var body = new W.Body();
      foreach (var line in Lines(text))
        body.Append(new W.Paragraph(new W.Run(new W.Text(line) { Space = SpaceProcessingModeValues.Preserve })));
      main.Document = new W.Document(body);
      main.Document.Save();
    }
    return NormalizeZip(stream.ToArray());
  }

  private static S.Cell TextCell(string value) => new()
  {
    DataType = S.CellValues.InlineString,
    InlineString = new S.InlineString(new S.Text(value) { Space = SpaceProcessingModeValues.Preserve })
  };

  private static IEnumerable<string> Lines(string text) =>
    text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

  private static byte[] NormalizeZip(byte[] source)
  {
    using var input = new MemoryStream(source);
    using var archive = new ZipArchive(input, ZipArchiveMode.Read);
    using var output = new MemoryStream();
    using (var normalized = new ZipArchive(output, ZipArchiveMode.Create, true))
    {
      foreach (var entry in archive.Entries.OrderBy(x => x.FullName, StringComparer.Ordinal))
      {
        var copy = normalized.CreateEntry(entry.FullName, CompressionLevel.SmallestSize);
        copy.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var sourceStream = entry.Open();
        using var destination = copy.Open();
        if (entry.FullName == "_rels/.rels")
        {
          using var reader = new StreamReader(sourceStream);
          var relationshipXml = Regex.Replace(reader.ReadToEnd(), "Id=\"[^\"]+\"", "Id=\"rId1\"");
          destination.Write(Encoding.UTF8.GetBytes(relationshipXml));
        }
        else
        {
          sourceStream.CopyTo(destination);
        }
      }
    }
    return output.ToArray();
  }
}
