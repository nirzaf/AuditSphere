using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
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
  static FinancialPackageOfficeRenderer()
  {
    GlobalFontSettings.FontResolver ??= EmbeddedPdfFontResolver.Instance;
  }

  public static FinancialPackageOfficeArtifact Render(string artifactVersion, string canonicalPackageText)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPackageText);
    var (bytes, contentType, extension) = artifactVersion switch
    {
      FinancialPackageArtifactVersions.Workbook =>
        (RenderWorkbook(canonicalPackageText, false), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
      FinancialPackageArtifactVersions.ControlledWorkbook =>
        (RenderWorkbook(canonicalPackageText, true), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
      FinancialPackageArtifactVersions.Word =>
        (RenderWord(canonicalPackageText), "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "docx"),
      FinancialPackageArtifactVersions.Pdf =>
        (RenderPdf(canonicalPackageText), "application/pdf", "pdf"),
      _ => throw new ArgumentOutOfRangeException(nameof(artifactVersion), "Unsupported Office artifact version.")
    };
    return new(artifactVersion, contentType, extension, Hashing.Sha256Hex(bytes), bytes);
  }

  private static byte[] RenderWorkbook(string text, bool controlled)
  {
    var lines = Lines(text).ToArray();
    using var stream = new MemoryStream();
    using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
    {
      var workbook = document.AddWorkbookPart();
      workbook.Workbook = new S.Workbook();
      var worksheet = workbook.AddNewPart<WorksheetPart>("rId2");
      var rows = new S.SheetData();
      foreach (var line in lines)
        rows.Append(new S.Row(TextCell(line)));
      worksheet.Worksheet = new S.Worksheet(rows);
      var sheets = new S.Sheets(new S.Sheet
      {
        Id = "rId2",
        SheetId = 1,
        Name = "Financial package"
      });
      if (controlled)
      {
        var control = workbook.AddNewPart<WorksheetPart>("rId3");
        control.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(
          TextCell("Canonical line count"),
          new S.Cell(new S.CellFormula($"COUNTA('Financial package'!A1:A{lines.Length})")))));
        sheets.Append(new S.Sheet { Id = "rId3", SheetId = 2, Name = "Control" });
        workbook.Workbook.CalculationProperties = new S.CalculationProperties
        {
          CalculationMode = S.CalculateModeValues.Auto,
          ForceFullCalculation = true,
          FullCalculationOnLoad = true
        };
      }
      workbook.Workbook.Append(sheets);
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

  private static byte[] RenderPdf(string text)
  {
    var document = new Document();
    document.Info.Title = "AuditSphereOps controlled financial package export";
    document.Info.Author = "AuditSphereOps";
    document.Info.Subject = "Version-bound financial package artifact";
    var normal = document.Styles[StyleNames.Normal]!;
    normal.Font.Name = EmbeddedPdfFontResolver.FamilyName;
    normal.Font.Size = 8;

    var section = document.AddSection();
    section.PageSetup.PageFormat = PageFormat.A4;
    section.PageSetup.TopMargin = Unit.FromCentimeter(1.8);
    section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
    section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
    section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);

    var header = section.Headers.Primary.AddParagraph("AuditSphereOps — controlled financial package export");
    header.Format.Font.Bold = true;
    header.Format.Font.Size = 10;
    header.Format.SpaceAfter = Unit.FromPoint(8);

    foreach (var line in Lines(text))
    {
      var paragraph = section.AddParagraph(line.Length == 0 ? " " : line.Replace("\t", "    ", StringComparison.Ordinal));
      paragraph.Format.SpaceAfter = Unit.Zero;
      paragraph.Format.KeepTogether = true;
      if (line.StartsWith("===", StringComparison.Ordinal) || line.StartsWith("---", StringComparison.Ordinal))
        paragraph.Format.Font.Bold = true;
    }

    var footer = section.Footers.Primary.AddParagraph();
    footer.Format.Alignment = ParagraphAlignment.Center;
    footer.AddText("Page ");
    footer.AddPageField();
    footer.AddText(" of ");
    footer.AddNumPagesField();

    var renderer = new PdfDocumentRenderer { Document = document };
    renderer.RenderDocument();
    var stableTimestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    renderer.PdfDocument.Info.CreationDate = stableTimestamp;
    renderer.PdfDocument.Info.ModificationDate = stableTimestamp;
    renderer.PdfDocument.Info.Creator = "AuditSphereOps";
    NormalizePdfFontNames(renderer.PdfDocument);
    var artifactHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(text));
    var documentId = artifactHash[..16];
    renderer.PdfDocument.Internals.FirstDocumentID = documentId;
    renderer.PdfDocument.Internals.SecondDocumentID = documentId;
    using var output = new MemoryStream();
    renderer.Save(output, false);
    return NormalizePdfMetadata(output.ToArray(), artifactHash);
  }

  private static byte[] NormalizePdfMetadata(byte[] bytes, string artifactHash)
  {
    var stableIds = new[]
    {
      new Guid(Convert.FromHexString(artifactHash[..32])).ToString("D"),
      new Guid(Convert.FromHexString(artifactHash[32..])).ToString("D")
    };
    var index = 0;
    var pdf = Encoding.Latin1.GetString(bytes);
    pdf = Regex.Replace(pdf, @"uuid:[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}",
      _ => $"uuid:{stableIds[Math.Min(index++, stableIds.Length - 1)]}");
    return Encoding.Latin1.GetBytes(pdf);
  }

  private static void NormalizePdfFontNames(PdfDocument document)
  {
    foreach (var dictionary in document.Internals.GetAllObjects().OfType<PdfDictionary>())
    foreach (var key in new[] { "/BaseFont", "/FontName" })
    {
      var name = dictionary.Elements.GetName(key);
      var plus = name.IndexOf('+');
      if (plus is 6 or 7)
        dictionary.Elements.SetName(key, $"{(plus == 7 ? "/" : string.Empty)}AUDITS{name[plus..]}");
    }
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

  private sealed class EmbeddedPdfFontResolver : IFontResolver
  {
    public const string FamilyName = "AuditSphere Noto Sans";
    private const string FaceName = "AuditSphere-Noto-Sans-Regular";
    private const string ResourceName = "AuditSphereOps.Application.Assets.Fonts.NotoSans-Regular.ttf";
    public static readonly EmbeddedPdfFontResolver Instance = new();
    private static readonly Lazy<byte[]> FontBytes = new(() =>
    {
      using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException("The embedded PDF font is unavailable.");
      using var output = new MemoryStream();
      stream.CopyTo(output);
      return output.ToArray();
    });

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
      new(FaceName, bold, italic);

    public byte[]? GetFont(string faceName) => faceName == FaceName ? FontBytes.Value : null;
  }
}
