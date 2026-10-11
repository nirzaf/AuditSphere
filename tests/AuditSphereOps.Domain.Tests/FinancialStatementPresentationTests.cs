using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using DocumentFormat.OpenXml.Packaging;
using PdfSharp.Pdf.IO;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Statement presentation (FSR-002, FSR-010, FSR-011 first slice). The fixture is SYNTHETIC: its balances are built to match
/// the reference totals in the NAQAHA story so the arithmetic and layout can be exercised. Account names and the
/// splits are illustrative and are not an accountant-approved ledger or policy.
/// </summary>
public sealed class FinancialStatementPresentationTests
{
  private const string Fp = StatementLayoutSections.FinancialPosition;
  private const string Pl = StatementLayoutSections.ProfitOrLoss;
  private const string Signed = StatementLayoutDisplaySigns.Signed;
  private const string Inverted = StatementLayoutDisplaySigns.Inverted;

  private static StatementLineDefinition Heading(string code, string label, string section, int order) =>
    new(code, label, StatementLayoutLineKinds.Heading, section, order);

  private static StatementLineDefinition Mapped(string code, string label, string section, int order, string node, string sign) =>
    new(code, label, StatementLayoutLineKinds.MappedTaxonomyBalance, section, order, sign, TaxonomyNodeCode: node);

  private static StatementLineDefinition Sum(string code, string label, string section, int order, string sign, params string[] children) =>
    new(code, label, StatementLayoutLineKinds.SumChildLines, section, order, sign, ChildLineCodes: children, IsSubtotal: true);

  private static StatementLineDefinition Total(string code, string label, string section, int order, string sign, string[] references, string[] operations) =>
    new(code, label, StatementLayoutLineKinds.TotalReferencedLines, section, order, sign, ReferencedLineCodes: references, Operations: operations);

  /// <summary>The synthetic layout. Equity and profit are presented in the financial position; profit is also shown in its own statement.</summary>
  private static List<StatementLineDefinition> Layout(string revenueSign = Inverted, bool withProfitStatement = true)
  {
    var lines = new List<StatementLineDefinition>
    {
      Heading("H_ASSETS", "Assets", Fp, 1),
      Mapped("PPE", "Property and equipment", Fp, 2, "PPE", Signed),
      Sum("NCA", "Non-current assets", Fp, 3, Signed, "PPE"),
      Mapped("RECV", "Receivables and other receivables", Fp, 4, "RECEIVABLES", Signed),
      Mapped("CASH", "Cash and cash equivalents", Fp, 5, "CASH", Signed),
      Mapped("OTHER", "Other current assets", Fp, 6, "OTHER_CA", Signed),
      Sum("CA", "Current assets", Fp, 7, Signed, "RECV", "CASH", "OTHER"),
      Sum("TA", "Total assets", Fp, 8, Signed, "NCA", "CA"),
      Heading("H_EQL", "Equity and liabilities", Fp, 9),
      Heading("H_EQ", "Equity", Fp, 10),
      Mapped("SHARE", "Share capital", Fp, 11, "SHARE_CAPITAL", Inverted),
      Mapped("REBF", "Retained earnings brought forward", Fp, 12, "RETAINED_EARNINGS", Inverted),
      Total("PROFIT_FP", "Profit for the year", Fp, 13, Inverted, ["REV", "GA", "DEP"], ["ADD", "ADD", "ADD"]),
      Sum("TEQ", "Total equity", Fp, 14, Inverted, "SHARE", "REBF", "PROFIT_FP"),
      Heading("H_LIAB", "Liabilities", Fp, 15),
      Mapped("LTL", "Long-term loan", Fp, 16, "LONG_TERM_LOAN", Inverted),
      Mapped("PAY", "Trade and other payables", Fp, 17, "PAYABLES", Inverted),
      Sum("TLIAB", "Total liabilities", Fp, 18, Inverted, "LTL", "PAY"),
      Sum("TLE", "Total equity and liabilities", Fp, 19, Inverted, "TEQ", "TLIAB"),
      Heading("H_REV", "Revenue", Pl, 1),
      Mapped("REV", "Revenue", Pl, 2, "REVENUE", revenueSign),
      Heading("H_EXP", "Expenses", Pl, 3),
      Mapped("GA", "General and administrative expenses", Pl, 4, "GENERAL_ADMIN", Signed),
      Mapped("DEP", "Depreciation", Pl, 5, "DEPRECIATION", Signed),
      Sum("TEXP", "Total expenses", Pl, 6, Signed, "GA", "DEP"),
    };
    if (withProfitStatement)
      lines.Add(Total("PROFIT_PL", "Profit for the year", Pl, 7, Inverted, ["REV", "GA", "DEP"], ["ADD", "ADD", "ADD"]));
    return lines;
  }

  /// <summary>Synthetic 2025 balances: they net to zero and reproduce the reference totals (assets 6,172,821; equity 4,797,471; profit 3,818,172).</summary>
  private static List<PresentationBalance> Current() =>
  [
    new("PPE", "ASSETS", 70406m), new("RECEIVABLES", "ASSETS", 2651857m), new("CASH", "ASSETS", 1090993m), new("OTHER_CA", "ASSETS", 2359565m),
    new("SHARE_CAPITAL", "EQUITY", -500000m), new("RETAINED_EARNINGS", "EQUITY", -479299m),
    new("LONG_TERM_LOAN", "LIABILITIES", -349594m), new("PAYABLES", "LIABILITIES", -1025756m),
    new("REVENUE", "INCOME", -11986252m), new("GENERAL_ADMIN", "EXPENSE", 8154593m), new("DEPRECIATION", "EXPENSE", 13487m),
  ];

  /// <summary>Synthetic 2024 comparative: assets 3,196,351; equity 2,687,078 (with an accumulated deficit brought forward); profit 2,695,325.</summary>
  private static List<PresentationBalance> Prior() =>
  [
    new("PPE", "ASSETS", 34101m), new("RECEIVABLES", "ASSETS", 2258345m), new("CASH", "ASSETS", 58372m), new("OTHER_CA", "ASSETS", 845533m),
    new("SHARE_CAPITAL", "EQUITY", -500000m), new("RETAINED_EARNINGS", "EQUITY", 508247m),
    new("LONG_TERM_LOAN", "LIABILITIES", 0m), new("PAYABLES", "LIABILITIES", -509273m),
    new("REVENUE", "INCOME", -6442992m), new("GENERAL_ADMIN", "EXPENSE", 3700000m), new("DEPRECIATION", "EXPENSE", 47667m),
  ];

  private static List<PresentationNoteLine> Notes() =>
  [
    new("5", "PPE", 70406m), new("6", "RECEIVABLES", 2651857m), new("7", "CASH", 1090993m),
    new("9", "PAYABLES", 1025756m), new("10", "GENERAL_ADMIN", 8154593m), new("11", "DEPRECIATION", 13487m),
  ];

  private static PresentationHeader Header(bool withPrior) => new("SYNTHETIC FIXTURE CLIENT", "IFRS", "QAR", "2025-12-31",
    withPrior ? "2024-12-31" : null, "FSP-SYNTHETIC-2025");

  private static PresentationManifest Manifest(bool withPrior) => new(
    Guid.Parse("11111111-1111-4111-8111-111111111111"), new string('a', 64),
    Guid.Parse("22222222-2222-4222-8222-222222222222"), new string('b', 64),
    withPrior ? Guid.Parse("33333333-3333-4333-8333-333333333333") : null, withPrior ? new string('c', 64) : null,
    FinancialStatementPresentationPolicy.BuilderVersion, FinancialStatementPresentationPolicy.DisplayPolicy);

  private static CommandResult<FinancialStatementPresentation> Build(
    IReadOnlyList<StatementLineDefinition>? layout = null, IReadOnlyList<PresentationBalance>? current = null,
    IReadOnlyList<PresentationBalance>? prior = null, IReadOnlyList<PresentationNoteLine>? notes = null,
    bool withPrior = true) =>
    FinancialStatementPresentationBuilder.Build(layout ?? Layout(), current ?? Current(), withPrior ? prior ?? Prior() : null,
      notes ?? Notes(), Header(withPrior), Manifest(withPrior));

  private static PresentationRow Row(FinancialStatementPresentation p, string code) =>
    p.Statements.SelectMany(x => x.Rows).Single(x => x.Code == code);

  private static decimal Amount(decimal? value) => value ?? throw new Xunit.Sdk.XunitException("Expected a presented amount.");

  [Fact]
  public void SyntheticFixture_PresentsReferenceShapedStatements_WithCurrentAndPriorColumns()
  {
    var result = Build();
    Assert.True(result.Succeeded, result.Message);
    var p = result.Value!;

    Assert.Equal(FinancialStatementPresentationPolicy.DraftStatus, p.Status);
    Assert.Equal(new[] { "FINANCIAL_POSITION", "PROFIT_OR_LOSS" }, p.Statements.Select(x => x.Code));
    Assert.All(p.Checks, check => Assert.True(check.Passed, $"{check.Code}: {check.Detail}"));

    // Reference-shaped totals (synthetic fixture built to match the reference assertions).
    Assert.Equal((6172821m, 3196351m), (Amount(Row(p, "TA").Current), Amount(Row(p, "TA").Prior)));
    Assert.Equal((4797471m, 2687078m), (Amount(Row(p, "TEQ").Current), Amount(Row(p, "TEQ").Prior)));
    Assert.Equal((1375350m, 509273m), (Amount(Row(p, "TLIAB").Current), Amount(Row(p, "TLIAB").Prior)));
    Assert.Equal((6172821m, 3196351m), (Amount(Row(p, "TLE").Current), Amount(Row(p, "TLE").Prior)));
    Assert.Equal((3818172m, 2695325m), (Amount(Row(p, "PROFIT_PL").Current), Amount(Row(p, "PROFIT_PL").Prior)));
    Assert.Equal((11986252m, 6442992m), (Amount(Row(p, "REV").Current), Amount(Row(p, "REV").Prior)));
    Assert.Equal((8168080m, 3747667m), (Amount(Row(p, "TEXP").Current), Amount(Row(p, "TEXP").Prior)));
    Assert.Equal((70406m, 34101m), (Amount(Row(p, "NCA").Current), Amount(Row(p, "NCA").Prior)));
    Assert.Equal((6102415m, 3162250m), (Amount(Row(p, "CA").Current), Amount(Row(p, "CA").Prior)));
    Assert.Equal((1090993m, 58372m), (Amount(Row(p, "CASH").Current), Amount(Row(p, "CASH").Prior)));

    // Zero display, and a negative prior-year equity component shown with its sign.
    Assert.Equal(0m, Amount(Row(p, "LTL").Prior));
    Assert.Equal(-508247m, Amount(Row(p, "REBF").Prior));

    // Note references appear on the face lines they point to only.
    Assert.Equal("5", Row(p, "PPE").NoteReference);
    Assert.Equal("9", Row(p, "PAY").NoteReference);
    Assert.Equal("10", Row(p, "GA").NoteReference);
    Assert.Null(Row(p, "REV").NoteReference);
  }

  [Fact]
  public void WithoutAComparative_PriorAmountsAreNotAvailable_NeverZero()
  {
    var result = Build(withPrior: false);
    Assert.True(result.Succeeded, result.Message);
    var p = result.Value!;
    Assert.Null(p.Header.PriorPeriodEnd);
    Assert.All(p.Statements.SelectMany(x => x.Rows), row => Assert.Null(row.Prior));
    Assert.Equal(6172821m, Amount(Row(p, "TA").Current));
    Assert.Null(p.Manifest.ComparativePackageId);
  }

  [Fact]
  public void DisplayText_UsesBracketsForNegatives_DashForZero_AndNamesMissingValues()
  {
    Assert.Equal("(508,247)", FinancialStatementPresentationRenderer.FormatAmount(-508247m, 0));
    Assert.Equal("–", FinancialStatementPresentationRenderer.FormatAmount(0m, 0));
    Assert.Equal("3,818,172", FinancialStatementPresentationRenderer.FormatAmount(3818172m, 0));
    Assert.Equal("Not available", FinancialStatementPresentationRenderer.FormatAmount(null, 0));
    Assert.Equal("(1,234.50)", FinancialStatementPresentationRenderer.FormatAmount(-1234.5m, 2));
  }

  [Fact]
  public void UnbalancedBalances_AreRefused_BeforeAnyStatementIsShown()
  {
    var current = Current();
    current[7] = current[7] with { Amount = -1025757m };
    var result = Build(current: current);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, result.ErrorCode);
    Assert.Contains("net to zero", result.Message);
  }

  [Fact]
  public void BalancesThatTheLayoutDoesNotPresent_AreRefused_NotSilentlyDropped()
  {
    var current = Current();
    current.Add(new PresentationBalance("SUNDRY_NODE", "EQUITY", -100m));
    current.Add(new PresentationBalance("CASH", "ASSETS", 100m));
    var result = Build(current: current);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, result.ErrorCode);
    Assert.Contains("SUNDRY_NODE", result.Message);
  }

  [Fact]
  public void DisplaySign_ThatContradictsTheStatementSection_IsRefused()
  {
    var result = Build(layout: Layout(revenueSign: Signed));
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, result.ErrorCode);
    Assert.Contains("REV", result.Message);
    Assert.Contains("Display signs do not match", result.Message);
  }

  [Fact]
  public void Notes_MustPointAtPresentedLines_AndCrossCastToThem()
  {
    var dangling = Notes();
    dangling.Add(new PresentationNoteLine("12", "NOT_PRESENTED", 1m));
    Assert.Contains("NOT_PRESENTED", Build(notes: dangling).Message);

    var offByOne = Notes();
    offByOne[0] = offByOne[0] with { Amount = 70407m };
    Assert.Contains("PPE", Build(notes: offByOne).Message);
  }

  [Fact]
  public void DisplayRounding_IsReportedAsAWarning_WhenCanonicalAmountsStillFoot()
  {
    var layout = new List<StatementLineDefinition>
    {
      Heading("H", "Assets", Fp, 1),
      Mapped("A", "Asset A", Fp, 2, "A", Signed),
      Mapped("B", "Asset B", Fp, 3, "B", Signed),
      Sum("TA", "Total assets", Fp, 4, Signed, "A", "B"),
      Mapped("EQ", "Equity", Fp, 5, "EQ", Inverted),
    };
    var balances = new List<PresentationBalance>
    {
      new("A", "ASSETS", 0.5m), new("B", "ASSETS", 0.5m), new("EQ", "EQUITY", -1m),
    };
    var result = FinancialStatementPresentationBuilder.Build(layout, balances, null, [], Header(false), Manifest(false));
    Assert.True(result.Succeeded, result.Message);
    Assert.Contains(result.Value!.Warnings, warning => warning.Code == "DISPLAY_ROUNDING");
    Assert.Equal(1m, Amount(Row(result.Value, "TA").Current));
  }

  [Fact]
  public void SubtotalsThatDoNotFoot_AreRefused_EvenWhenRoundingIsAllowedFor()
  {
    var layout = new List<StatementLineDefinition>
    {
      Heading("H", "Mixed", Fp, 1),
      Mapped("A", "Asset", Fp, 2, "A", Signed),
      Mapped("EQ", "Equity", Fp, 3, "EQ", Inverted),
      Sum("MIX", "Mixed total", Fp, 4, Signed, "A", "EQ"),
    };
    var balances = new List<PresentationBalance> { new("A", "ASSETS", 100m), new("EQ", "EQUITY", -100m) };
    var result = FinancialStatementPresentationBuilder.Build(layout, balances, null, [], Header(false), Manifest(false));
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, result.ErrorCode);
    Assert.Contains("do not foot", result.Message);
  }

  [Fact]
  public void UnsupportedStatementSections_AreOutOfScope()
  {
    var layout = Layout();
    layout.Add(Heading("H_CF", "Cash flows", StatementLayoutSections.CashFlows, 1));
    var result = Build(layout: layout);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, result.ErrorCode);
    Assert.Contains("CASH_FLOWS", result.Message);
  }

  [Fact]
  public void WordRendering_IsGenuineTables_WithTheSameFiguresAsThePresentation()
  {
    var p = Build().Value!;
    var bytes = FinancialStatementPresentationRenderer.Render(p, FinancialStatementPresentationRenderer.WordFormat);
    using var stream = new MemoryStream(bytes);
    using var document = WordprocessingDocument.Open(stream, false);
    var body = document.MainDocumentPart!.Document!.Body!;
    var tables = body.Descendants<W.Table>().ToList();
    Assert.Equal(2, tables.Count);
    Assert.NotNull(tables[0].Descendants<W.TableHeader>().FirstOrDefault());
    var text = body.InnerText;
    Assert.Contains("6,172,821", text, StringComparison.Ordinal);
    Assert.Contains("(508,247)", text, StringComparison.Ordinal);
    Assert.Contains("–", text, StringComparison.Ordinal);
    Assert.Contains(FinancialStatementPresentationRenderer.Banner, text, StringComparison.Ordinal);
  }

  [Fact]
  public void ExcelRendering_KeepsAmountsAsNumbers_AndHasNoFormulas()
  {
    var p = Build().Value!;
    var bytes = FinancialStatementPresentationRenderer.Render(p, FinancialStatementPresentationRenderer.ExcelFormat);
    using var stream = new MemoryStream(bytes);
    using var document = SpreadsheetDocument.Open(stream, false);
    var workbookPart = document.WorkbookPart!;
    var sheetNames = workbookPart.Workbook!.Descendants<S.Sheet>().Select(x => x.Name!.Value!).ToList();
    Assert.Equal(3, sheetNames.Count);
    Assert.All(sheetNames, name => Assert.True(name!.Length <= 31));

    var worksheets = workbookPart.WorksheetParts.Select(x => x.Worksheet!).ToList();
    Assert.Empty(worksheets.SelectMany(x => x.Descendants<S.CellFormula>()));

    var numericCells = worksheets.SelectMany(x => x.Descendants<S.Cell>())
      .Where(x => x.DataType is null && x.CellValue is not null)
      .Select(x => decimal.Parse(x.CellValue!.Text, CultureInfo.InvariantCulture)).ToList();
    Assert.Contains(6172821m, numericCells);
    Assert.Contains(-508247m, numericCells);
    Assert.Contains(3818172m, numericCells);
  }

  [Fact]
  public void PdfRendering_ParsesAndIsDeterministic_WithoutActiveContent()
  {
    var p = Build().Value!;
    var bytes = FinancialStatementPresentationRenderer.Render(p, FinancialStatementPresentationRenderer.PdfFormat);
    using var stream = new MemoryStream(bytes);
    using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
    Assert.True(document.PageCount >= 1);
    var pdf = System.Text.Encoding.Latin1.GetString(bytes);
    Assert.StartsWith("%PDF-", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/JavaScript", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/OpenAction", pdf, StringComparison.Ordinal);
    Assert.DoesNotContain("/EmbeddedFile", pdf, StringComparison.Ordinal);
  }

  [Fact]
  public void EveryFormat_IsByteIdenticalWhenRepeated_AndSeparatelyDigested()
  {
    var p = Build().Value!;
    foreach (var format in new[] { FinancialStatementPresentationRenderer.PdfFormat, FinancialStatementPresentationRenderer.WordFormat, FinancialStatementPresentationRenderer.ExcelFormat })
    {
      var first = FinancialStatementPresentationRenderer.Render(p, format);
      var second = FinancialStatementPresentationRenderer.Render(p, format);
      Assert.True(first.AsSpan().SequenceEqual(second), $"{format} output differs between two renders.");
      Assert.Equal(64, Hashing.Sha256Hex(first).Length);
    }
    Assert.Throws<ArgumentOutOfRangeException>(() => FinancialStatementPresentationRenderer.Render(p, "html"));
  }
}
