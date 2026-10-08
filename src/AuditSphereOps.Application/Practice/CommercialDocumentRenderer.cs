using System.Globalization;
using AuditSphereOps.Domain.Practice;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AuditSphereOps.Application.Practice;

public sealed record DocumentTable(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<int>? RightAlignedColumns = null);

public sealed record DocumentSection(string Heading, IReadOnlyList<string> Paragraphs, DocumentTable? Table = null);

public sealed record CommercialDocumentModel(
  FirmCommercialProfile Profile,
  string Title,
  string Reference,
  DateOnly Date,
  string AddressedTo,
  IReadOnlyList<DocumentSection> Sections,
  bool SignatureBlock);

/// <summary>
/// Renders branded commercial documents (quotation, engagement letter, receipt) as Word documents from a data model.
/// Each template has an explicit version recorded with the stored document. Layout is data-driven so an approved
/// firm profile supplies branding and closing text; no business decision is made here.
/// </summary>
public static class CommercialDocumentRenderer
{
  public const string QuotationTemplate = "COMMERCIAL-QUOTATION-v1";
  /// <summary>Identity of letters generated before service-specific templates; existing letters keep it unchanged.</summary>
  public const string LegacyEngagementLetterTemplate = "COMMERCIAL-ENGAGEMENT-LETTER-v1";
  public const string ReceiptTemplate = "COMMERCIAL-RECEIPT-v1";
  public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

  public static byte[] RenderDocx(CommercialDocumentModel model)
  {
    var accent = model.Profile.AccentColorHex.TrimStart('#').ToUpperInvariant();
    using var stream = new MemoryStream();
    using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
    {
      var main = document.AddMainDocumentPart();
      var body = new W.Body();

      body.Append(Paragraph(model.Profile.LegalName, bold: true, size: 32, color: accent, spaceAfter: 40));
      foreach (var line in new[] { model.Profile.Address, JoinContact(model.Profile) }.Where(x => !string.IsNullOrWhiteSpace(x)))
        body.Append(Paragraph(line, size: 18, color: "526176", spaceAfter: 0));
      body.Append(Rule(accent));

      body.Append(Paragraph(model.Title.ToUpperInvariant(), bold: true, size: 28, color: accent, spaceAfter: 60, spaceBefore: 200));
      body.Append(Labeled("Reference", model.Reference));
      body.Append(Labeled("Date", model.Date.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)));
      body.Append(Labeled("Prepared for", model.AddressedTo));

      foreach (var section in model.Sections)
      {
        body.Append(Paragraph(section.Heading, bold: true, size: 22, color: accent, spaceBefore: 260, spaceAfter: 80));
        foreach (var text in section.Paragraphs) body.Append(Paragraph(text, size: 20, spaceAfter: 80));
        if (section.Table is not null) body.Append(Table(section.Table, accent));
      }

      if (!string.IsNullOrWhiteSpace(model.Profile.ClosingText))
      {
        body.Append(Rule(accent));
        foreach (var line in model.Profile.ClosingText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
          body.Append(Paragraph(line.Trim(), size: 18, color: "526176", spaceAfter: 60));
      }

      if (model.SignatureBlock)
      {
        body.Append(Paragraph("Acceptance", bold: true, size: 22, color: accent, spaceBefore: 360, spaceAfter: 80));
        body.Append(Paragraph($"For {model.Profile.LegalName}: ______________________________   Date: ______________", size: 20, spaceAfter: 200, spaceBefore: 200));
        body.Append(Paragraph($"For {model.AddressedTo}: ______________________________   Date: ______________", size: 20, spaceAfter: 80));
      }

      body.Append(new W.SectionProperties(new W.PageSize { Width = 11906, Height = 16838 },
        new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134 }));
      main.Document = new W.Document(body);
      main.Document.Save();
    }
    return stream.ToArray();
  }

  private static string JoinContact(FirmCommercialProfile profile) =>
    string.Join("  ·  ", new[] { profile.ContactEmail, profile.ContactPhone }.Where(x => !string.IsNullOrWhiteSpace(x)));

  private static W.Paragraph Labeled(string label, string value)
  {
    var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines { After = "20" }));
    paragraph.Append(Run(label + ": ", bold: true, size: 20), Run(value, size: 20));
    return paragraph;
  }

  private static W.Paragraph Paragraph(string text, bool bold = false, int size = 20, string? color = null, int spaceBefore = 0, int spaceAfter = 80)
  {
    var paragraph = new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines { Before = spaceBefore.ToString(CultureInfo.InvariantCulture), After = spaceAfter.ToString(CultureInfo.InvariantCulture) }));
    paragraph.Append(Run(text, bold, size, color));
    return paragraph;
  }

  private static W.Run Run(string text, bool bold = false, int size = 20, string? color = null)
  {
    var properties = new W.RunProperties(new W.RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" }, new W.FontSize { Val = size.ToString(CultureInfo.InvariantCulture) });
    if (bold) properties.Append(new W.Bold());
    if (color is not null) properties.Append(new W.Color { Val = color });
    return new W.Run(properties, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve });
  }

  private static W.Paragraph Rule(string accent) => new(new W.ParagraphProperties(
    new W.ParagraphBorders(new W.BottomBorder { Val = W.BorderValues.Single, Size = 12, Color = accent, Space = 1 }),
    new W.SpacingBetweenLines { After = "120" }));

  private static W.Table Table(DocumentTable data, string accent)
  {
    var right = data.RightAlignedColumns ?? [];
    var table = new W.Table(new W.TableProperties(
      new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
      new W.TableBorders(
        new W.TopBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
        new W.BottomBorder { Val = W.BorderValues.Single, Size = 4, Color = "CBD5E1" },
        new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4, Color = "E2E8F0" })));
    W.TableRow Row(IReadOnlyList<string> cells, bool header)
    {
      var row = new W.TableRow();
      for (var i = 0; i < cells.Count; i++)
      {
        var paragraph = new W.Paragraph(new W.ParagraphProperties(
          new W.SpacingBetweenLines { Before = "40", After = "40" },
          new W.Justification { Val = right.Contains(i) ? W.JustificationValues.Right : W.JustificationValues.Left }));
        paragraph.Append(Run(cells[i], bold: header, size: 19, color: header ? "FFFFFF" : null));
        var cell = new W.TableCell(paragraph);
        if (header) cell.PrependChild(new W.TableCellProperties(new W.Shading { Val = W.ShadingPatternValues.Clear, Fill = accent }));
        row.Append(cell);
      }
      return row;
    }
    table.Append(Row(data.Header, true));
    foreach (var row in data.Rows) table.Append(Row(row, false));
    return table;
  }
}
