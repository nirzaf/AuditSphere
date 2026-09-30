using System.Globalization;
using AuditSphereOps.Application.Practice;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AuditSphereOps.Application.Completion;

public sealed record DeliverableSignature(byte[] Png, int WidthPixels, int HeightPixels, string SignatoryName, string SignatoryRole, DateOnly Date);

public sealed record AuditDeliverableModel(
  string FirmName, string Title, string Reference, DateOnly Date, string AddressedTo,
  IReadOnlyList<DocumentSection> Sections, string? SignatureLabel = null, DeliverableSignature? Signature = null);

/// <summary>
/// Renders audit deliverables as Word documents from a data model; optionally embeds a validated PNG signature image
/// in the signature block. Embedding a picture is not a cryptographic signature; the stored document hash is the
/// evidence of what was issued.
/// </summary>
public static class AuditDeliverableRenderer
{
  public const string TemplateVersion = "AUDIT-DELIVERABLE-v1";
  public const string DocxContentType = CommercialDocumentRenderer.DocxContentType;

  public static byte[] RenderDocx(AuditDeliverableModel model)
  {
    using var stream = new MemoryStream();
    using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
    {
      var main = document.AddMainDocumentPart();
      var body = new W.Body();
      body.Append(Paragraph(model.FirmName, bold: true, size: 30, spaceAfter: 40));
      body.Append(Paragraph(model.Title.ToUpperInvariant(), bold: true, size: 28, spaceBefore: 200, spaceAfter: 60));
      body.Append(Paragraph($"Reference: {model.Reference}", size: 18));
      body.Append(Paragraph($"Date: {model.Date.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture)}", size: 18));
      body.Append(Paragraph($"To: {model.AddressedTo}", size: 18));
      foreach (var section in model.Sections)
      {
        body.Append(Paragraph(section.Heading, bold: true, size: 22, spaceBefore: 240, spaceAfter: 80));
        foreach (var text in section.Paragraphs) body.Append(Paragraph(text, size: 20));
        if (section.Table is { } table) body.Append(Table(table));
      }
      if (model.SignatureLabel is not null)
      {
        body.Append(Paragraph(model.SignatureLabel, bold: true, size: 20, spaceBefore: 360));
        if (model.Signature is { } signature)
        {
          var image = main.AddImagePart(ImagePartType.Png);
          using (var png = new MemoryStream(signature.Png)) image.FeedData(png);
          body.Append(new W.Paragraph(new W.Run(Picture(main.GetIdOfPart(image), signature.WidthPixels, signature.HeightPixels))));
          body.Append(Paragraph($"{signature.SignatoryName}, {signature.SignatoryRole}", size: 20));
          body.Append(Paragraph(signature.Date.ToString("dd MMMM yyyy", CultureInfo.InvariantCulture), size: 18));
        }
        else
          body.Append(Paragraph("Signature: ______________________________   (unsigned draft)", size: 20, spaceBefore: 200));
      }
      body.Append(new W.SectionProperties(new W.PageSize { Width = 11906, Height = 16838 },
        new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134 }));
      main.Document = new W.Document(body);
      main.Document.Save();
    }
    return stream.ToArray();
  }

  /// <summary>Validated PNG header: signature bytes and IHDR dimensions. Returns null when the bytes are not a PNG.</summary>
  public static (int Width, int Height)? ReadPngSize(byte[] bytes)
  {
    ReadOnlySpan<byte> magic = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(magic) || bytes[12] != (byte)'I' || bytes[13] != (byte)'H' || bytes[14] != (byte)'D' || bytes[15] != (byte)'R')
      return null;
    int Big(int offset) => bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
    return (Big(16), Big(20));
  }

  private static W.Drawing Picture(string relationshipId, int widthPixels, int heightPixels)
  {
    // Scale to at most 6 cm wide; 1 px at 96 dpi = 9525 EMU.
    const long maxWidth = 6L * 360000;
    var cx = (long)widthPixels * 9525;
    var cy = (long)heightPixels * 9525;
    if (cx > maxWidth) { cy = cy * maxWidth / cx; cx = maxWidth; }
    return new W.Drawing(new DW.Inline(
      new DW.Extent { Cx = cx, Cy = cy },
      new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
      new DW.DocProperties { Id = 1U, Name = "Signature" },
      new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
      new A.Graphic(new A.GraphicData(new PIC.Picture(
        new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0U, Name = "signature.png" }, new PIC.NonVisualPictureDrawingProperties()),
        new PIC.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
        new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = cx, Cy = cy }),
          new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
      { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
    { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U });
  }

  private static W.Paragraph Paragraph(string text, bool bold = false, int size = 20, int spaceBefore = 0, int spaceAfter = 80)
  {
    var properties = new W.RunProperties(new W.FontSize { Val = size.ToString(CultureInfo.InvariantCulture) });
    if (bold) properties.Append(new W.Bold());
    return new W.Paragraph(new W.ParagraphProperties(new W.SpacingBetweenLines { Before = spaceBefore.ToString(CultureInfo.InvariantCulture), After = spaceAfter.ToString(CultureInfo.InvariantCulture) }),
      new W.Run(properties, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
  }

  private static W.Table Table(DocumentTable data)
  {
    var table = new W.Table(new W.TableProperties(new W.TableWidth { Type = W.TableWidthUnitValues.Pct, Width = "5000" },
      new W.TableBorders(new W.TopBorder { Val = W.BorderValues.Single, Size = 4 }, new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
        new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 2 })));
    W.TableRow Row(IEnumerable<string> cells, bool header) => new(cells.Select(c => new W.TableCell(Paragraph(c, bold: header, size: 18, spaceAfter: 20))));
    table.Append(Row(data.Header, true));
    foreach (var row in data.Rows) table.Append(Row(row, false));
    return table;
  }
}
