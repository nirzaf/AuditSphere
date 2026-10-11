using System.Globalization;
using System.Text;
using AuditSphereOps.Domain.Shared;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Pdf;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Genuine PDF, Word and Excel renderings of the typed statement presentation. Every format reads the same presentation
/// model, so the figures, signs, note references and draft banner are identical across formats. Output is deterministic:
/// the same presentation always yields the same bytes.
/// </summary>
public static class FinancialStatementPresentationRenderer
{
  public const string PdfFormat = "pdf";
  public const string WordFormat = "docx";
  public const string ExcelFormat = "xlsx";
  public const string PdfContentType = "application/pdf";
  public const string WordContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
  public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
  public const string Banner = "UNAPPROVED PRESENTATION DRAFT — not reviewed, not sealed, not for issue";

  public static string? ContentTypeFor(string format) => format switch
  {
    PdfFormat => PdfContentType,
    WordFormat => WordContentType,
    ExcelFormat => ExcelContentType,
    _ => null
  };

  public static byte[] Render(FinancialStatementPresentation presentation, string format)
  {
    ArgumentNullException.ThrowIfNull(presentation);
    return format switch
    {
      PdfFormat => RenderPdf(presentation),
      WordFormat => RenderWord(presentation),
      ExcelFormat => RenderExcel(presentation),
      _ => throw new ArgumentOutOfRangeException(nameof(format), "Unsupported statement presentation format.")
    };
  }

  /// <summary>Display text for an amount: grouped, negatives in brackets, zero as a dash.</summary>
  public static string FormatAmount(decimal? value, int decimals)
  {
    if (value is null) return "Not available";
    var amount = value.Value;
    if (amount == 0m) return "–";
    var magnitude = Math.Abs(amount).ToString(
      decimals == 0 ? "#,##0" : "#,##0." + new string('0', decimals), CultureInfo.InvariantCulture);
    return amount < 0m ? $"({magnitude})" : magnitude;
  }

  internal static string LongDate(string isoDate) =>
    DateOnly.ParseExact(isoDate, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

  internal static string CurrentColumn(FinancialStatementPresentation p, string statementCode) =>
    IsPeriodStatement(statementCode)
      ? $"Year ended {LongDate(p.Header.CurrentPeriodEnd)}"
      : $"As at {LongDate(p.Header.CurrentPeriodEnd)}";

  internal static string PriorColumn(FinancialStatementPresentation p, string statementCode)
  {
    if (p.Header.PriorPeriodEnd is null) return "Prior period not supplied";
    return IsPeriodStatement(statementCode)
      ? $"Year ended {LongDate(p.Header.PriorPeriodEnd)}"
      : $"As at {LongDate(p.Header.PriorPeriodEnd)}";
  }

  private static bool IsPeriodStatement(string statementCode) =>
    statementCode is "PROFIT_OR_LOSS" or "CASH_FLOWS" or "CHANGES_IN_EQUITY";

  /// <summary>Allocates the printable page width to the label, note and two amount columns.</summary>
  internal static IReadOnlyList<double> PdfColumnWidthsInPoints(
    double pageWidthPoints, double leftMarginPoints, double rightMarginPoints)
  {
    var printableWidth = pageWidthPoints - leftMarginPoints - rightMarginPoints;
    if (!double.IsFinite(printableWidth) || printableWidth <= 0 || leftMarginPoints < 0 || rightMarginPoints < 0)
      throw new ArgumentOutOfRangeException(nameof(pageWidthPoints), "The PDF page must have a positive printable width.");

    var label = printableWidth * 0.55;
    var note = printableWidth * 0.07;
    var current = printableWidth * 0.19;
    // Subtract the preceding widths so the table never exceeds the printable page from rounding drift.
    var prior = printableWidth - label - note - current;
    return [label, note, current, prior];
  }

  /// <summary>Lines that identify exactly what was presented; the same lines appear in every format.</summary>
  internal static IReadOnlyList<string> ManifestLines(FinancialStatementPresentation p)
  {
    var m = p.Manifest;
    var lines = new List<string>
    {
      $"Status: {p.Status} — {Banner}",
      $"Package: {m.PackageId:D} · calculation hash {m.CalculationHash}",
      $"Statement layout: {m.LayoutVersionId:D} · layout hash {m.LayoutHash}",
      m.ComparativePackageId is { } comparative
        ? $"Comparative package: {comparative:D} · calculation hash {m.ComparativeCalculationHash}"
        : "Comparative package: none supplied",
      m.NoteSourceManifestHash is { Length: > 0 } noteManifest
        ? $"Note source manifest: {noteManifest}"
        : "Note source manifest: not available in this preview",
      $"Source context: period {m.PeriodId?.ToString("D") ?? "not linked"} · book {m.BookId?.ToString("D") ?? "not linked"} · basis {m.Basis ?? "not recorded"}",
      $"Policy inputs: framework {m.Framework ?? p.Header.Framework} · taxonomy {m.TaxonomyVersion ?? "not recorded"} · template {m.TemplateVersion ?? "not recorded"} · engine {m.CalculationEngineVersion ?? "not recorded"}",
      $"Reporting period: {m.PeriodStart ?? "not recorded"} to {m.PeriodEnd ?? p.Header.CurrentPeriodEnd} · currency {m.Currency ?? p.Header.Currency}",
      m.ComparativePackageId is not null
        ? $"Comparative basis: {m.ComparativeBasis ?? "not recorded"} · evidence {m.ComparativeEvidenceReference ?? "not recorded"}"
        : "Comparative basis: none supplied",
      $"Presentation: {m.BuilderVersion} · {m.DisplayPolicy}",
      $"Notes and disclosures: {p.Notes?.Count ?? 0} logical note(s); supplied content remains unreviewed preparer input",
      "Note schedules include statement section and current/comparative period amounts; Excel retains exact source amounts as text beside display-rounded numbers",
      $"Framework: {p.Header.Framework} · currency {p.Header.Currency} · reference {p.Header.Reference}"
    };
    lines.AddRange(p.Warnings.Select(x => $"{(x.Code == "DISPLAY_ROUNDING" ? "Rounding note" : "Readiness note")}: {x.Detail}"));
    lines.AddRange(p.Checks.Select(x => $"Check {x.Code}: {(x.Passed ? "PASS" : "FAIL")} — {x.Detail}"));
    return lines;
  }

  /// <summary>The deterministic text that the PDF metadata identifiers are derived from.</summary>
  private static string CanonicalText(FinancialStatementPresentation p)
  {
    var sb = new StringBuilder();
    sb.AppendLine(p.Header.Entity).AppendLine(string.Join('|', p.Header.Framework, p.Header.Currency, p.Header.CurrentPeriodEnd, p.Header.PriorPeriodEnd ?? string.Empty));
    foreach (var line in ManifestLines(p)) sb.AppendLine(line);
    foreach (var statement in p.Statements)
    {
      sb.AppendLine(statement.Title);
      foreach (var row in statement.Rows)
        sb.AppendLine(string.Join('|', row.Kind, row.Code, row.Label, row.Depth, row.NoteReference ?? string.Empty,
          FormatAmount(row.Current, p.DisplayDecimals), FormatAmount(row.Prior, p.DisplayDecimals)));
    }
    foreach (var note in p.Notes ?? [])
    {
      sb.AppendLine(string.Join('|', "NOTE", note.Number, note.Code));
      AppendDisclosure(sb, "CURRENT", note.Current);
      AppendDisclosure(sb, "COMPARATIVE", note.Comparative);
      foreach (var amount in note.Amounts)
        sb.AppendLine(string.Join('|', "NOTE_AMOUNT", amount.FaceDestinationCode, amount.Label, amount.StatementSection, amount.Currency,
          FormatAmount(amount.Current, p.DisplayDecimals), FormatAmount(amount.Comparative, p.DisplayDecimals),
          amount.CurrentEvidenceReference ?? string.Empty, amount.ComparativeEvidenceReference ?? string.Empty));
    }
    return sb.ToString();
  }

  private static byte[] RenderPdf(FinancialStatementPresentation p)
  {
    FinancialPackageOfficeRenderer.EnsurePdfFonts();
    var document = new MigraDoc.DocumentObjectModel.Document();
    document.Info.Title = "Statement presentation (unapproved draft)";
    document.Info.Author = "AuditSphereOps";
    document.Info.Subject = p.Header.Reference;
    var normal = document.Styles[StyleNames.Normal]!;
    normal.Font.Name = FinancialPackageOfficeRenderer.EmbeddedPdfFontResolver.FamilyName;
    normal.Font.Size = 9;
    normal.ParagraphFormat.SpaceAfter = 2;

    var section = document.AddSection();
    section.PageSetup.PageFormat = PageFormat.A4;
    var topMargin = Unit.FromCentimeter(1.8);
    var bottomMargin = Unit.FromCentimeter(1.8);
    var leftMargin = Unit.FromCentimeter(2);
    var rightMargin = Unit.FromCentimeter(2);
    section.PageSetup.TopMargin = topMargin;
    section.PageSetup.BottomMargin = bottomMargin;
    section.PageSetup.LeftMargin = leftMargin;
    section.PageSetup.RightMargin = rightMargin;

    var header = section.Headers.Primary.AddParagraph(Banner);
    header.Format.Font.Bold = true;
    header.Format.Font.Size = 8;
    header.Format.Alignment = ParagraphAlignment.Center;

    var footer = section.Footers.Primary.AddParagraph();
    footer.Format.Alignment = ParagraphAlignment.Center;
    footer.AddText(p.Header.Reference + " · Page ");
    footer.AddPageField();
    footer.AddText(" of ");
    footer.AddNumPagesField();

    var title = section.AddParagraph(p.Header.Entity);
    title.Format.Font.Bold = true;
    title.Format.Font.Size = 13;
    section.AddParagraph($"Framework {p.Header.Framework} · amounts in {p.Header.Currency} · presented in {(p.DisplayDecimals == 0 ? "whole units" : $"{p.DisplayDecimals} decimal places")}");

    for (var index = 0; index < p.Statements.Count; index++)
    {
      var statement = p.Statements[index];
      if (index > 0) section.AddPageBreak();
      var heading = section.AddParagraph(statement.Title);
      heading.Format.Font.Bold = true;
      heading.Format.Font.Size = 11;
      heading.Format.SpaceBefore = 10;
      heading.Format.SpaceAfter = 6;
      heading.Format.KeepWithNext = true;

      var table = section.AddTable();
      table.Format.Font.Size = 9;
      var columnWidths = PdfColumnWidthsInPoints(
        Unit.FromCentimeter(21).Point,
        leftMargin.Point,
        rightMargin.Point);
      foreach (var columnWidth in columnWidths) table.AddColumn(Unit.FromPoint(columnWidth));

      var headerRow = table.AddRow();
      headerRow.HeadingFormat = true;
      headerRow.Format.Font.Bold = true;
      headerRow.Format.Font.Size = 7.5;
      headerRow.Cells[0].AddParagraph(string.Empty);
      headerRow.Cells[1].AddParagraph("Note");
      headerRow.Cells[2].AddParagraph(CurrentColumn(p, statement.Code));
      headerRow.Cells[3].AddParagraph(PriorColumn(p, statement.Code));
      for (var i = 0; i < headerRow.Cells.Count; i++) headerRow.Cells[i].Borders!.Bottom.Width = Unit.FromPoint(0.75);
      headerRow.Cells[1].Format.Alignment = ParagraphAlignment.Right;
      headerRow.Cells[2].Format.Alignment = ParagraphAlignment.Right;
      headerRow.Cells[3].Format.Alignment = ParagraphAlignment.Right;

      foreach (var row in statement.Rows)
      {
        var tableRow = table.AddRow();
        var label = tableRow.Cells[0].AddParagraph(row.Label);
        var availableIndent = Math.Max(0, columnWidths[0] - Unit.FromCentimeter(4.5).Point);
        var requestedIndent = Math.Max(0, row.Depth) * Unit.FromCentimeter(0.4).Point;
        label.Format.LeftIndent = Unit.FromPoint(Math.Min(requestedIndent, availableIndent));
        tableRow.Cells[1].AddParagraph(row.NoteReference ?? string.Empty);
        tableRow.Cells[2].AddParagraph(row.Kind == PresentationRowKinds.Heading ? string.Empty : FormatAmount(row.Current, p.DisplayDecimals));
        tableRow.Cells[3].AddParagraph(row.Kind == PresentationRowKinds.Heading ? string.Empty : FormatAmount(row.Prior, p.DisplayDecimals));
        tableRow.Cells[1].Format.Alignment = ParagraphAlignment.Right;
        tableRow.Cells[2].Format.Alignment = ParagraphAlignment.Right;
        tableRow.Cells[3].Format.Alignment = ParagraphAlignment.Right;
        if (row.Kind == PresentationRowKinds.Heading)
          tableRow.Format.Font.Bold = true;
        else if (row.Kind is PresentationRowKinds.Subtotal or PresentationRowKinds.Total)
        {
          tableRow.Format.Font.Bold = true;
          for (var i = 0; i < tableRow.Cells.Count; i++) tableRow.Cells[i].Borders!.Top.Width = Unit.FromPoint(0.5);
          if (row.Kind == PresentationRowKinds.Total)
            for (var i = 0; i < tableRow.Cells.Count; i++) tableRow.Cells[i].Borders!.Bottom.Width = Unit.FromPoint(1.5);
        }
      }
    }

    AddPdfNotes(section, p, leftMargin, rightMargin);

    var basisHeading = section.AddParagraph("Basis of this presentation");
    basisHeading.Format.Font.Bold = true;
    basisHeading.Format.SpaceBefore = 14;
    foreach (var line in ManifestLines(p)) section.AddParagraph(line).Format.Font.Size = 7.5;

    var renderer = new PdfDocumentRenderer { Document = document };
    renderer.RenderDocument();
    var stable = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    renderer.PdfDocument.Info.CreationDate = stable;
    renderer.PdfDocument.Info.ModificationDate = stable;
    renderer.PdfDocument.Info.Creator = "AuditSphereOps";
    var canonicalHash = Hashing.Sha256Hex(Encoding.UTF8.GetBytes(CanonicalText(p)));
    renderer.PdfDocument.Internals.FirstDocumentID = canonicalHash[..16];
    renderer.PdfDocument.Internals.SecondDocumentID = canonicalHash[..16];
    FinancialPackageOfficeRenderer.NormalizePdfFontNames(renderer.PdfDocument);
    using var output = new MemoryStream();
    renderer.PdfDocument.Save(output, closeStream: false);
    return FinancialPackageOfficeRenderer.NormalizePdfMetadata(output.ToArray(), canonicalHash);
  }

  private static void AddPdfNotes(Section section, FinancialStatementPresentation p, Unit leftMargin, Unit rightMargin)
  {
    section.AddPageBreak();
    var heading = section.AddParagraph("Notes and disclosures");
    heading.Format.Font.Bold = true;
    heading.Format.Font.Size = 12;
    heading.Format.SpaceAfter = 8;
    var notes = p.Notes ?? [];
    if (notes.Count == 0)
    {
      section.AddParagraph("No package note disclosures or note schedules were supplied.");
      return;
    }

    foreach (var note in notes)
    {
      var noteHeading = section.AddParagraph($"Note {note.Number} · {note.Code}");
      noteHeading.Format.Font.Bold = true;
      noteHeading.Format.Font.Size = 10;
      noteHeading.Format.SpaceBefore = 8;
      noteHeading.Format.KeepWithNext = true;
      AddPdfDisclosure(section, $"Current period ending {LongDate(p.Header.CurrentPeriodEnd)}", note.Current);
      AddPdfDisclosure(section, p.Header.PriorPeriodEnd is { } priorEnd ? $"Comparative period ending {LongDate(priorEnd)}" : "Comparative period not supplied", note.Comparative);

      if (note.Amounts.Count == 0)
      {
        var missing = section.AddParagraph("No numeric note schedule was supplied for this note.");
        missing.Format.Font.Italic = true;
      }
      else
      {
        var table = section.AddTable();
        table.Format.Font.Size = 8;
        const double pointsPerCentimeter = 28.3464566929;
        var printableCm = (595.2756d - leftMargin.Point - rightMargin.Point) / pointsPerCentimeter;
        var widths = new[] { printableCm * 0.34, printableCm * 0.17, printableCm * 0.10, printableCm * 0.195, printableCm * 0.195 };
        foreach (var width in widths) table.AddColumn(Unit.FromCentimeter(width));
        var headerRow = table.AddRow();
        headerRow.HeadingFormat = true;
        headerRow.Format.Font.Bold = true;
        headerRow.Format.Font.Size = 7;
        headerRow.Cells[0].AddParagraph("Face line");
        headerRow.Cells[1].AddParagraph("Statement section");
        headerRow.Cells[2].AddParagraph("Currency");
        headerRow.Cells[3].AddParagraph("Current amount");
        headerRow.Cells[4].AddParagraph("Comparative amount");
        foreach (var amount in note.Amounts)
        {
          var row = table.AddRow();
          row.Cells[0].AddParagraph($"{amount.FaceDestinationCode} · {amount.Label}");
          row.Cells[1].AddParagraph(amount.StatementSection);
          row.Cells[2].AddParagraph(amount.Currency);
          row.Cells[3].AddParagraph(FormatAmount(amount.Current, p.DisplayDecimals));
          row.Cells[4].AddParagraph(FormatAmount(amount.Comparative, p.DisplayDecimals));
          row.Cells[3].Format.Alignment = ParagraphAlignment.Right;
          row.Cells[4].Format.Alignment = ParagraphAlignment.Right;
        }
        foreach (var amount in note.Amounts)
        {
          if (!string.IsNullOrWhiteSpace(amount.CurrentEvidenceReference))
            AddPdfReference(section, $"{amount.FaceDestinationCode} · current period", amount.CurrentEvidenceReference);
          if (!string.IsNullOrWhiteSpace(amount.ComparativeEvidenceReference))
            AddPdfReference(section, $"{amount.FaceDestinationCode} · comparative period", amount.ComparativeEvidenceReference);
        }
      }
    }
  }

  private static void AddPdfDisclosure(Section section, string period, PresentationDisclosurePeriod disclosure)
  {
    var label = section.AddParagraph($"{period} · {DisclosureStateLabel(disclosure.State)}");
    label.Format.Font.Bold = true;
    label.Format.Font.Size = 8;
    var content = disclosure.State == PresentationDisclosureStates.PreparerMarkedNotApplicable
      ? disclosure.Rationale is null ? null : $"Preparer rationale: {disclosure.Rationale}"
      : disclosure.Text;
    if (!string.IsNullOrWhiteSpace(content)) section.AddParagraph(content);
  }

  private static void AddPdfReference(Section section, string period, string reference)
  {
    var paragraph = section.AddParagraph($"Preparer reference ({period}; not independently verified): {reference}");
    paragraph.Format.Font.Size = 7;
  }

  private static void AppendDisclosure(StringBuilder sb, string period, PresentationDisclosurePeriod disclosure) =>
    sb.AppendLine(string.Join('|', "DISCLOSURE", period, disclosure.State, disclosure.Text ?? string.Empty, disclosure.Rationale ?? string.Empty));

  private static string DisclosureStateLabel(string state) => state switch
  {
    PresentationDisclosureStates.PreparerSupplied => "preparer-supplied, unreviewed",
    PresentationDisclosureStates.PreparerMarkedNotApplicable => "preparer-marked not applicable, unreviewed",
    _ => "not supplied"
  };

  private static byte[] RenderWord(FinancialStatementPresentation p)
  {
    using var stream = new MemoryStream();
    using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
    {
      var main = document.AddMainDocumentPart();
      var body = new W.Body();
      body.Append(WordParagraph(p.Header.Entity, bold: true, size: 26, spaceAfter: 40));
      body.Append(WordParagraph(Banner, bold: true, size: 16, spaceAfter: 120));
      body.Append(WordParagraph($"Framework {p.Header.Framework} · amounts in {p.Header.Currency} · presented in {(p.DisplayDecimals == 0 ? "whole units" : $"{p.DisplayDecimals} decimal places")}", size: 18));
      foreach (var statement in p.Statements)
      {
        body.Append(WordParagraph(statement.Title, bold: true, size: 22, spaceBefore: 280, spaceAfter: 100));
        body.Append(WordTable(p, statement));
      }
      body.Append(WordParagraph("Notes and disclosures", bold: true, size: 22, spaceBefore: 280, spaceAfter: 100));
      var notes = p.Notes ?? [];
      if (notes.Count == 0)
        body.Append(WordParagraph("No package note disclosures or note schedules were supplied.", size: 18));
      foreach (var note in notes)
      {
        body.Append(WordParagraph($"Note {note.Number} · {note.Code}", bold: true, size: 20, spaceBefore: 180));
        AppendWordDisclosure(body, $"Current period ending {LongDate(p.Header.CurrentPeriodEnd)}", note.Current);
        AppendWordDisclosure(body, p.Header.PriorPeriodEnd is { } priorEnd ? $"Comparative period ending {LongDate(priorEnd)}" : "Comparative period not supplied", note.Comparative);
        if (note.Amounts.Count == 0)
          body.Append(WordParagraph("No numeric note schedule was supplied for this note.", size: 16));
        else
          body.Append(WordNoteTable(p, note));
        foreach (var amount in note.Amounts)
        {
          if (!string.IsNullOrWhiteSpace(amount.CurrentEvidenceReference))
            body.Append(WordParagraph($"Preparer reference ({amount.FaceDestinationCode} · current period; not independently verified): {amount.CurrentEvidenceReference}", size: 14));
          if (!string.IsNullOrWhiteSpace(amount.ComparativeEvidenceReference))
            body.Append(WordParagraph($"Preparer reference ({amount.FaceDestinationCode} · comparative period; not independently verified): {amount.ComparativeEvidenceReference}", size: 14));
        }
      }
      body.Append(WordParagraph("Basis of this presentation", bold: true, size: 18, spaceBefore: 280));
      foreach (var line in ManifestLines(p)) body.Append(WordParagraph(line, size: 14));
      body.Append(new W.SectionProperties(new W.PageSize { Width = 11906, Height = 16838 },
        new W.PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134 }));
      main.Document = new W.Document(body);
      main.Document.Save();
    }
    return FinancialPackageOfficeRenderer.NormalizeZip(stream.ToArray());
  }

  private static void AppendWordDisclosure(W.Body body, string period, PresentationDisclosurePeriod disclosure)
  {
    body.Append(WordParagraph($"{period} · {DisclosureStateLabel(disclosure.State)}", bold: true, size: 16));
    var content = disclosure.State == PresentationDisclosureStates.PreparerMarkedNotApplicable
      ? disclosure.Rationale is null ? null : $"Preparer rationale: {disclosure.Rationale}"
      : disclosure.Text;
    if (!string.IsNullOrWhiteSpace(content)) body.Append(WordParagraph(content, size: 18));
  }

  private static W.Table WordNoteTable(FinancialStatementPresentation p, PresentationNote note)
  {
    int[] widths = [3700, 1600, 1000, 1600, 1600];
    var table = new W.Table(
      new W.TableProperties(
        new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = widths.Sum().ToString(CultureInfo.InvariantCulture) },
        new W.TableBorders(new W.TopBorder { Val = W.BorderValues.Single, Size = 4 }, new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 }),
        new W.TableLayout { Type = W.TableLayoutValues.Fixed }),
      new W.TableGrid(widths.Select(w => new W.GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));
    table.Append(new W.TableRow(new W.TableRowProperties(new W.TableHeader()),
      WordCell(widths[0], "Face line", true, 0, false),
      WordCell(widths[1], "Statement", true, 0, false),
      WordCell(widths[2], "Currency", true, 0, false),
      WordCell(widths[3], "Current amount", true, 0, true),
      WordCell(widths[4], "Comparative amount", true, 0, true)));
    foreach (var amount in note.Amounts)
      table.Append(new W.TableRow(
        WordCell(widths[0], $"{amount.FaceDestinationCode} · {amount.Label}", false, 0, false),
        WordCell(widths[1], amount.StatementSection, false, 0, false),
        WordCell(widths[2], amount.Currency, false, 0, false),
        WordCell(widths[3], FormatAmount(amount.Current, p.DisplayDecimals), false, 0, true),
        WordCell(widths[4], FormatAmount(amount.Comparative, p.DisplayDecimals), false, 0, true)));
    return table;
  }

  private static W.Table WordTable(FinancialStatementPresentation p, PresentationStatement statement)
  {
    // Column widths in twentieths of a point; the total fits the A4 text width with 2 cm margins.
    int[] widths = [4900, 800, 1900, 1900];
    var table = new W.Table(
      new W.TableProperties(
        new W.TableWidth { Type = W.TableWidthUnitValues.Dxa, Width = widths.Sum().ToString(CultureInfo.InvariantCulture) },
        new W.TableBorders(
          new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
          new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 }),
        new W.TableLayout { Type = W.TableLayoutValues.Fixed }),
      new W.TableGrid(widths.Select(w => new W.GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));

    table.Append(new W.TableRow(
      new W.TableRowProperties(new W.TableHeader()),
      WordCell(widths[0], string.Empty, bold: true, indent: 0, right: false),
      WordCell(widths[1], "Note", bold: true, indent: 0, right: true),
      WordCell(widths[2], CurrentColumn(p, statement.Code), bold: true, indent: 0, right: true),
      WordCell(widths[3], PriorColumn(p, statement.Code), bold: true, indent: 0, right: true)));

    foreach (var row in statement.Rows)
    {
      var bold = row.Kind != PresentationRowKinds.Line;
      var heading = row.Kind == PresentationRowKinds.Heading;
      table.Append(new W.TableRow(
        WordCell(widths[0], row.Label, bold, indent: row.Depth * 360, right: false),
        WordCell(widths[1], row.NoteReference ?? string.Empty, bold: false, indent: 0, right: true),
        WordCell(widths[2], heading ? string.Empty : FormatAmount(row.Current, p.DisplayDecimals), bold, indent: 0, right: true),
        WordCell(widths[3], heading ? string.Empty : FormatAmount(row.Prior, p.DisplayDecimals), bold, indent: 0, right: true)));
    }
    return table;
  }

  private static W.TableCell WordCell(int width, string text, bool bold, int indent, bool right)
  {
    var run = new W.RunProperties();
    if (bold) run.Append(new W.Bold());
    run.Append(new W.FontSize { Val = "18" });
    return new W.TableCell(
      new W.TableCellProperties(new W.TableCellWidth { Type = W.TableWidthUnitValues.Dxa, Width = width.ToString(CultureInfo.InvariantCulture) }),
      new W.Paragraph(
        new W.ParagraphProperties(
          new W.SpacingBetweenLines { Before = "20", After = "20" },
          new W.Indentation { Left = indent.ToString(CultureInfo.InvariantCulture) },
          new W.Justification { Val = right ? W.JustificationValues.Right : W.JustificationValues.Left }),
        new W.Run(run, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve })));
  }

  private static W.Paragraph WordParagraph(string text, bool bold = false, int size = 20, int spaceBefore = 0, int spaceAfter = 80)
  {
    var run = new W.RunProperties();
    if (bold) run.Append(new W.Bold());
    run.Append(new W.FontSize { Val = size.ToString(CultureInfo.InvariantCulture) });
    return new W.Paragraph(
      new W.ParagraphProperties(new W.SpacingBetweenLines
      {
        Before = spaceBefore.ToString(CultureInfo.InvariantCulture),
        After = spaceAfter.ToString(CultureInfo.InvariantCulture)
      }),
      new W.Run(run, new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
  }

  private static byte[] RenderExcel(FinancialStatementPresentation p)
  {
    var decimals = p.DisplayDecimals;
    using var stream = new MemoryStream();
    using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
    {
      var workbook = document.AddWorkbookPart();
      workbook.Workbook = new S.Workbook();
      // Explicit relationship identifiers keep the package deterministic; generated identifiers differ per save.
      var stylesPart = workbook.AddNewPart<WorkbookStylesPart>("rIdStyles");
      stylesPart.Stylesheet = BuildStylesheet(decimals);
      stylesPart.Stylesheet.Save();

      var sheets = new S.Sheets();
      uint sheetId = 1;
      foreach (var statement in p.Statements)
      {
        var worksheetPart = workbook.AddNewPart<WorksheetPart>($"rIdSheet{sheetId}");
        worksheetPart.Worksheet = new S.Worksheet(new S.SheetData(StatementRows(p, statement)));
        sheets.Append(new S.Sheet
        {
          Id = workbook.GetIdOfPart(worksheetPart),
          SheetId = sheetId++,
          Name = SheetName(statement.Title)
        });
      }
      var notesPart = workbook.AddNewPart<WorksheetPart>($"rIdSheet{sheetId}");
      notesPart.Worksheet = new S.Worksheet(new S.SheetData(NoteRows(p)));
      sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(notesPart), SheetId = sheetId++, Name = "Notes" });
      var manifestPart = workbook.AddNewPart<WorksheetPart>($"rIdSheet{sheetId}");
      manifestPart.Worksheet = new S.Worksheet(new S.SheetData(ManifestRows(p)));
      sheets.Append(new S.Sheet { Id = workbook.GetIdOfPart(manifestPart), SheetId = sheetId, Name = "Basis" });

      workbook.Workbook.Append(sheets);
      workbook.Workbook.Save();
    }
    return FinancialPackageOfficeRenderer.NormalizeZip(stream.ToArray());
  }

  private static IEnumerable<S.Row> NoteRows(FinancialStatementPresentation p)
  {
    const uint Bold = 1, Number = 2;
    var rows = new List<S.Row>();
    var headers = new[] { "Note", "Code", "Record type", "Period", "Content state", "Content or face destination", "Face label", "Statement section", "Currency", "Display amount", "Exact source amount", "Preparer reference", "Reference status" };
    uint rowNumber = 1;
    rows.Add(new S.Row(headers.Select((value, index) => TextCell($"{(char)('A' + index)}1", value, Bold))) { RowIndex = rowNumber });
    var notes = p.Notes ?? [];
    foreach (var note in notes)
    {
      AddDisclosure("Current", note.Current);
      AddDisclosure("Comparative", note.Comparative);
      foreach (var amount in note.Amounts)
      {
        if (amount.Current is not null) AddAmount("Current", amount.Current.Value, amount.CurrentEvidenceReference);
        if (amount.Comparative is not null) AddAmount("Comparative", amount.Comparative.Value, amount.ComparativeEvidenceReference);

        void AddAmount(string period, decimal value, string? reference)
        {
          rowNumber++;
          var column = period == "Current" ? "Current" : "Comparative";
          var cells = new List<S.Cell>
          {
            TextCell($"A{rowNumber}", note.Number.ToString(CultureInfo.InvariantCulture), 0),
            TextCell($"B{rowNumber}", note.Code, 0), TextCell($"C{rowNumber}", "NOTE_SCHEDULE", 0),
            TextCell($"D{rowNumber}", column, 0), TextCell($"E{rowNumber}", "PREPARER_SUPPLIED_UNREVIEWED", 0),
            TextCell($"F{rowNumber}", amount.FaceDestinationCode, 0), TextCell($"G{rowNumber}", amount.Label, 0),
            TextCell($"H{rowNumber}", amount.StatementSection, 0), TextCell($"I{rowNumber}", amount.Currency, 0),
            AmountCell($"J{rowNumber}", value, p.DisplayDecimals, Number), TextCell($"K{rowNumber}", ExactAmount(value), 0),
            TextCell($"L{rowNumber}", reference ?? string.Empty, 0),
            TextCell($"M{rowNumber}", string.IsNullOrWhiteSpace(reference) ? "No reference supplied" : "Preparer reference; not independently verified", 0)
          };
          rows.Add(new S.Row(cells) { RowIndex = rowNumber });
        }
      }

      void AddDisclosure(string period, PresentationDisclosurePeriod disclosure)
      {
        rowNumber++;
        var content = disclosure.State == PresentationDisclosureStates.PreparerMarkedNotApplicable
          ? disclosure.Rationale is null ? string.Empty : $"Preparer rationale: {disclosure.Rationale}"
          : disclosure.Text ?? string.Empty;
        rows.Add(new S.Row(
          TextCell($"A{rowNumber}", note.Number.ToString(CultureInfo.InvariantCulture), 0),
          TextCell($"B{rowNumber}", note.Code, 0), TextCell($"C{rowNumber}", "DISCLOSURE", 0),
          TextCell($"D{rowNumber}", period, 0), TextCell($"E{rowNumber}", disclosure.State, 0),
          TextCell($"F{rowNumber}", content, 0), TextCell($"G{rowNumber}", string.Empty, 0),
          TextCell($"H{rowNumber}", string.Empty, 0), TextCell($"I{rowNumber}", p.Header.Currency, 0),
          TextCell($"J{rowNumber}", string.Empty, 0), TextCell($"K{rowNumber}", string.Empty, 0),
          TextCell($"L{rowNumber}", string.Empty, 0),
          TextCell($"M{rowNumber}", "No external review or applicability decision is bound", 0)) { RowIndex = rowNumber });
      }
    }
    if (notes.Count == 0)
    {
      rowNumber++;
      rows.Add(new S.Row(TextCell($"A{rowNumber}", "No package note disclosures or note schedules were supplied.", 0)) { RowIndex = rowNumber });
    }
    return rows;
  }

  private static string ExactAmount(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);

  private static IEnumerable<S.Row> StatementRows(FinancialStatementPresentation p, PresentationStatement statement)
  {
    const uint Bold = 1, Number = 2, BoldNumber = 3;
    var rows = new List<S.Row>
    {
      new S.Row(TextCell("A1", string.Empty, Bold), TextCell("B1", "Note", Bold),
        TextCell("C1", CurrentColumn(p, statement.Code), Bold), TextCell("D1", PriorColumn(p, statement.Code), Bold)) { RowIndex = 1 }
    };
    uint rowNumber = 1;
    foreach (var row in statement.Rows)
    {
      rowNumber++;
      var bold = row.Kind != PresentationRowKinds.Line;
      var label = new string(' ', row.Depth * 4) + row.Label;
      var cells = new List<S.Cell>
      {
        TextCell($"A{rowNumber}", label, bold ? Bold : 0u),
        TextCell($"B{rowNumber}", row.NoteReference ?? string.Empty, 0u)
      };
      if (row.Kind == PresentationRowKinds.Heading)
      {
        rows.Add(new S.Row(cells) { RowIndex = rowNumber });
        continue;
      }
      cells.Add(AmountCell($"C{rowNumber}", row.Current, decimals: p.DisplayDecimals, bold ? BoldNumber : Number));
      cells.Add(AmountCell($"D{rowNumber}", row.Prior, decimals: p.DisplayDecimals, bold ? BoldNumber : Number));
      rows.Add(new S.Row(cells) { RowIndex = rowNumber });
    }
    return rows;
  }

  private static IEnumerable<S.Row> ManifestRows(FinancialStatementPresentation p)
  {
    var rows = new List<S.Row>();
    uint index = 0;
    foreach (var line in ManifestLines(p))
    {
      index++;
      rows.Add(new S.Row(TextCell($"A{index}", line, 0u)) { RowIndex = index });
    }
    return rows;
  }

  private static S.Cell TextCell(string reference, string value, uint style) => new(
    new S.InlineString(new S.Text(value) { Space = SpaceProcessingModeValues.Preserve }))
  {
    CellReference = reference,
    DataType = S.CellValues.InlineString,
    StyleIndex = style
  };

  /// <summary>A numeric cell holds the displayed (rounded) amount as a number, or the text "Not available".</summary>
  private static S.Cell AmountCell(string reference, decimal? value, int decimals, uint style)
  {
    if (value is null) return TextCell(reference, "Not available", 0u);
    var rounded = decimal.Round(value.Value, decimals, MidpointRounding.ToEven);
    return new S.Cell(new S.CellValue(rounded.ToString(CultureInfo.InvariantCulture)))
    {
      CellReference = reference,
      StyleIndex = style
    };
  }

  private static string SheetName(string title) =>
    title.Length <= 31 ? title : title[..31];

  private static S.Stylesheet BuildStylesheet(int decimals)
  {
    var zeros = decimals == 0 ? string.Empty : "." + new string('0', decimals);
    var formatCode = $"#,##0{zeros};(#,##0{zeros});\"–\"";
    return new S.Stylesheet(
      new S.NumberingFormats(new S.NumberingFormat { NumberFormatId = 164, FormatCode = formatCode }) { Count = 1 },
      new S.Fonts(
        new S.Font(new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" }),
        new S.Font(new S.Bold(), new S.FontSize { Val = 11 }, new S.FontName { Val = "Calibri" })) { Count = 2 },
      new S.Fills(
        new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
        new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })) { Count = 2 },
      new S.Borders(new S.Border(new S.LeftBorder(), new S.RightBorder(), new S.TopBorder(), new S.BottomBorder(), new S.DiagonalBorder())) { Count = 1 },
      new S.CellStyleFormats(new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
      new S.CellFormats(
        new S.CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0 },
        new S.CellFormat { NumberFormatId = 0, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyFont = true },
        new S.CellFormat { NumberFormatId = 164, FontId = 0, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true },
        new S.CellFormat { NumberFormatId = 164, FontId = 1, FillId = 0, BorderId = 0, FormatId = 0, ApplyNumberFormat = true, ApplyFont = true }) { Count = 4 },
      new S.CellStyles(new S.CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
  }
}
