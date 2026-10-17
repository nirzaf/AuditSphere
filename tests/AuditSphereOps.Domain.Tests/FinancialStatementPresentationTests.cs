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
    new("LONG_TERM_LOAN", "LIABILITIES", -349594m), new("PAYABLES", "LIABILITIES", -1025756m), new("OCI", "OCI", 0m),
    new("REVENUE", "INCOME", -11986252m), new("GENERAL_ADMIN", "EXPENSE", 8154593m), new("DEPRECIATION", "EXPENSE", 13487m),
  ];

  /// <summary>Synthetic 2024 comparative: assets 3,196,351; equity 2,687,078 (with an accumulated deficit brought forward); profit 2,695,325.</summary>
  private static List<PresentationBalance> Prior() =>
  [
    new("PPE", "ASSETS", 34101m), new("RECEIVABLES", "ASSETS", 2258345m), new("CASH", "ASSETS", 58372m), new("OTHER_CA", "ASSETS", 845533m),
    new("SHARE_CAPITAL", "EQUITY", -500000m), new("RETAINED_EARNINGS", "EQUITY", 508247m),
    new("LONG_TERM_LOAN", "LIABILITIES", 0m), new("PAYABLES", "LIABILITIES", -509273m), new("OCI", "OCI", 0m),
    new("REVENUE", "INCOME", -6442992m), new("GENERAL_ADMIN", "EXPENSE", 3700000m), new("DEPRECIATION", "EXPENSE", 47667m),
  ];

  private static List<PresentationNoteLine> Notes() =>
  [
    new("5", "PPE", 70406m, Currency: "QAR", EvidenceReference: "synthetic-note-source"),
    new("6", "RECEIVABLES", 2651857m, Currency: "QAR", EvidenceReference: "synthetic-note-source"),
    new("7", "CASH", 1090993m, Currency: "QAR", EvidenceReference: "synthetic-note-source"),
    new("9", "PAYABLES", 1025756m, Currency: "QAR", EvidenceReference: "synthetic-note-source"),
    new("10", "GENERAL_ADMIN", 8154593m, Currency: "QAR", EvidenceReference: "synthetic-note-source"),
    new("11", "DEPRECIATION", 13487m, Currency: "QAR", EvidenceReference: "synthetic-note-source"),
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
    bool withPrior = true, IReadOnlyList<PresentationDisclosureSource>? disclosures = null) =>
    FinancialStatementPresentationBuilder.Build(layout ?? Layout(), current ?? Current(), withPrior ? prior ?? Prior() : null,
      notes ?? Notes(), Header(withPrior), Manifest(withPrior), disclosures: disclosures);

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
    Assert.Equal(new[] { "FINANCIAL_POSITION", "PROFIT_OR_LOSS", "CHANGES_IN_EQUITY", "CASH_FLOWS" }, p.Statements.Select(x => x.Code));
    Assert.All(p.Checks.Where(x => x.Code is not ("EQUITY_SCHEDULE" or "CASH_FLOW_REQUIRED_SECTIONS" or "CASH_FLOW_BRIDGE" or
      "CASH_FLOW_PACKAGE_RECONCILIATION" or "CASH_FLOW_COMPARATIVE_REQUIRED_SECTIONS" or "CASH_FLOW_COMPARATIVE_BRIDGE" or
      "CASH_FLOW_COMPARATIVE_PACKAGE_RECONCILIATION" or "CASH_FLOW_SCHEDULE" or "CASH_FLOW_COMPARATIVE" or "EQUITY_COMPARATIVE" or "NOTE_CONTENT")),
      check => Assert.True(check.Passed, $"{check.Code}: {check.Detail}"));
    Assert.Contains(p.Checks, x => x.Code == "EQUITY_SCHEDULE" && !x.Passed);
    Assert.Contains(p.Checks, x => x.Code == "CASH_FLOW_REQUIRED_SECTIONS" && !x.Passed);
    Assert.Contains(p.Checks, x => x.Code == "CASH_FLOW_BRIDGE" && !x.Passed);
    Assert.Contains(p.Checks, x => x.Code == "CASH_FLOW_PACKAGE_RECONCILIATION" && !x.Passed);
    Assert.Contains(p.Checks, x => x.Code == "CASH_FLOW_SCHEDULE" && !x.Passed);
    Assert.Contains(p.Checks, x => x.Code == "CASH_FLOW_COMPARATIVE" && !x.Passed);

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
    Assert.Equal("1", Row(p, "PPE").NoteReference);
    Assert.Equal("4", Row(p, "PAY").NoteReference);
    Assert.Equal("5", Row(p, "GA").NoteReference);
    Assert.Null(Row(p, "REV").NoteReference);
  }

  [Fact]
  public void NoteReferences_AreDynamicallyNumberedByPublishedFaceOrder_IndependentOfInputOrder()
  {
    var forward = Build(withPrior: false).Value!;
    var reversedNotes = Notes().OrderByDescending(x => x.NoteCode, StringComparer.Ordinal).ToList();
    var reversed = Build(notes: reversedNotes, withPrior: false).Value!;
    var lineCodes = new[] { "PPE", "RECV", "CASH", "PAY", "GA", "DEP" };
    var forwardReferences = lineCodes.Select(code => Row(forward, code).NoteReference).ToArray();
    var reversedReferences = lineCodes.Select(code => Row(reversed, code).NoteReference).ToArray();

    Assert.Equal(new[] { "1", "2", "3", "4", "5", "6" }, forwardReferences);
    Assert.Equal(forwardReferences, reversedReferences);
    Assert.Contains(forward.Checks, x => x.Code == "NOTE_NUMBERING" && x.Passed);
    Assert.Contains(forward.Checks, x => x.Code == "NOTE_CONTENT" && !x.Passed);
    Assert.Contains(forward.Warnings, x => x.Code == "NOTE_CONTENT_PENDING");
  }

  [Fact]
  public void NoteContentAndAmounts_AreShownForCurrentAndComparativeAsUnreviewedPreparerInputs()
  {
    var manifest = Manifest(withPrior: true);
    var notes = Notes().Select(x => x with { PackageId = manifest.PackageId }).ToList();
    var comparativeId = manifest.ComparativePackageId!.Value;
    notes.AddRange(new[]
    {
      new PresentationNoteLine("5", "PPE", 34101m, comparativeId, "QAR", "comparative-note-source"),
      new PresentationNoteLine("6", "RECEIVABLES", 2258345m, comparativeId, "QAR", "comparative-note-source"),
      new PresentationNoteLine("7", "CASH", 58372m, comparativeId, "QAR", "comparative-note-source"),
      new PresentationNoteLine("9", "PAYABLES", 509273m, comparativeId, "QAR", "comparative-note-source"),
      new PresentationNoteLine("10", "GENERAL_ADMIN", 3700000m, comparativeId, "QAR", "comparative-note-source"),
      new PresentationNoteLine("11", "DEPRECIATION", 47667m, comparativeId, "QAR", "comparative-note-source")
    });
    var disclosures = new[]
    {
      new PresentationDisclosureSource("5", "Property additions are preparer described.", false, null, manifest.PackageId),
      new PresentationDisclosureSource("5", "Comparative property note is preparer described.", false, null, comparativeId),
      new PresentationDisclosureSource("12", string.Empty, true, "No related balance was reported by the preparer.", manifest.PackageId)
    };

    var result = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(), notes, Header(true), manifest,
      disclosures: disclosures);

    Assert.True(result.Succeeded, result.Message);
    var currentNote = Assert.Single(result.Value!.Notes!, x => x.Code == "5");
    Assert.Equal(PresentationDisclosureStates.PreparerSupplied, currentNote.Current.State);
    Assert.Equal("Property additions are preparer described.", currentNote.Current.Text);
    Assert.Equal(PresentationDisclosureStates.PreparerSupplied, currentNote.Comparative.State);
    Assert.Equal("Comparative property note is preparer described.", currentNote.Comparative.Text);
    Assert.Equal((70406m, 34101m), (currentNote.Amounts[0].Current, currentNote.Amounts[0].Comparative));
    Assert.Equal(StatementLayoutSections.FinancialPosition, currentNote.Amounts[0].StatementSection);
    Assert.Equal("synthetic-note-source", currentNote.Amounts[0].CurrentEvidenceReference);
    var notApplicable = Assert.Single(result.Value.Notes!, x => x.Code == "12");
    Assert.Equal(PresentationDisclosureStates.PreparerMarkedNotApplicable, notApplicable.Current.State);
    Assert.Equal("No related balance was reported by the preparer.", notApplicable.Current.Rationale);
    Assert.Contains(result.Value.Checks, x => x.Code == "NOTE_CONTENT" && !x.Passed);
    Assert.Equal(FinancialStatementPresentationPolicy.DraftStatus, result.Value.Status);
  }

  [Fact]
  public void NoteDisclosureDuplicateAndUnsafeContent_AreRefusedWithoutThrowing()
  {
    var manifest = Manifest(withPrior: false);
    var duplicate = new[]
    {
      new PresentationDisclosureSource("TEXT", "First", false, null, manifest.PackageId),
      new PresentationDisclosureSource(" text ", "Second", false, null, manifest.PackageId)
    };
    var duplicateResult = FinancialStatementPresentationBuilder.Build(Layout(), Current(), null, Notes(), Header(false), manifest,
      disclosures: duplicate);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, duplicateResult.ErrorCode);
    Assert.Contains("one disclosure response", duplicateResult.Message, StringComparison.Ordinal);

    var unsafeContent = new[] { new PresentationDisclosureSource("TEXT", "Bad\u0001content", false, null, manifest.PackageId) };
    var unsafeResult = FinancialStatementPresentationBuilder.Build(Layout(), Current(), null, Notes(), Header(false), manifest,
      disclosures: unsafeContent);
    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, unsafeResult.ErrorCode);
    Assert.Contains("unsupported control character", unsafeResult.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void DuplicateNormalizedNotePointer_IsRefused()
  {
    var notes = Notes();
    notes.Add(new("5", " ppe ", 0m, Currency: "QAR", EvidenceReference: "synthetic-note-source"));
    var result = Build(notes: notes, withPrior: false);

    Assert.Equal(ErrorCodes.Accounting.PackageInvalid, result.ErrorCode);
    Assert.Contains("same normalized face destination more than once", result.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void ComparativeNoteCrossCasts_UseTheirOwnPackageBalances()
  {
    var manifest = Manifest(withPrior: true);
    var currentNotes = Notes().Select(x => x with { PackageId = manifest.PackageId }).ToList();
    var comparativeId = manifest.ComparativePackageId!.Value;
    var comparativeNotes = new List<PresentationNoteLine>
    {
      new("5", "PPE", 34101m, comparativeId, "QAR", "comparative-note-source"),
      new("6", "RECEIVABLES", 2258345m, comparativeId, "QAR", "comparative-note-source"),
      new("7", "CASH", 58372m, comparativeId, "QAR", "comparative-note-source"),
      new("9", "PAYABLES", 509273m, comparativeId, "QAR", "comparative-note-source"),
      new("10", "GENERAL_ADMIN", 3700000m, comparativeId, "QAR", "comparative-note-source"),
      new("11", "DEPRECIATION", 47667m, comparativeId, "QAR", "comparative-note-source")
    };

    var valid = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(),
      currentNotes.Concat(comparativeNotes).ToList(), Header(withPrior: true), manifest);
    Assert.True(valid.Succeeded, valid.Message);
    Assert.Contains(valid.Value!.Checks, x => x.Code == "NOTE_CROSS_CAST" && x.Passed);

    comparativeNotes[0] = comparativeNotes[0] with { Amount = 70406m };
    var mismatched = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(),
      currentNotes.Concat(comparativeNotes).ToList(), Header(withPrior: true), manifest);
    Assert.False(mismatched.Succeeded);
    Assert.Contains("comparative PPE notes total", mismatched.Message, StringComparison.Ordinal);
  }

  [Fact]
  public void CashFlowSourceLineage_PassesOnlyWhenEveryMovementIsResolvedFromSource()
  {
    PresentationSupplementarySchedules Schedules(bool secondResolved, bool approved = true) => new(
      58372m, 1090993m,
      [
        new("OPERATING", "Operating receipts and payments", 1200000m, false, "OPERATING_NET", SourceResolved: true),
        new("FINANCING", "New borrowing", 32621m, false, "BORROWING", SourceResolved: secondResolved)
      ],
      [new("USD/QAR", 0m)],
      [new("RETAINED_EARNINGS", "Retained earnings", 979299m, 3818172m, 0m, 0m, 0m, 4797471m)],
      [new("CASH_FLOW_RECONCILED", true, "Cash bridge and selected closing-cash population reconcile to the package build.")],
      CashFlowSourceApproved: approved);

    var resolved = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(), Notes(), Header(withPrior: true), Manifest(withPrior: true),
      currentSupplementary: Schedules(secondResolved: true), priorSupplementary: null);
    Assert.True(resolved.Succeeded, resolved.Message);
    Assert.Contains(resolved.Value!.Checks, x => x.Code == "CASH_FLOW_SOURCE_LINEAGE" && x.Passed);
    Assert.DoesNotContain(resolved.Value.Warnings, x => x.Code == "CASH_FLOW_LINEAGE_PENDING");

    var partial = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(), Notes(), Header(withPrior: true), Manifest(withPrior: true),
      currentSupplementary: Schedules(secondResolved: false), priorSupplementary: null);
    Assert.True(partial.Succeeded, partial.Message);
    Assert.Contains(partial.Value!.Checks, x => x.Code == "CASH_FLOW_SOURCE_LINEAGE" && !x.Passed && x.Detail.Contains("1 cash-flow movement", StringComparison.Ordinal));
    Assert.Contains(partial.Value.Warnings, x => x.Code == "CASH_FLOW_LINEAGE_PENDING");

    // Fully resolved links still read as unapproved until an independent reviewer approves them against the current ledger.
    var unapproved = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(), Notes(), Header(withPrior: true), Manifest(withPrior: true),
      currentSupplementary: Schedules(secondResolved: true, approved: false), priorSupplementary: null);
    Assert.True(unapproved.Succeeded, unapproved.Message);
    Assert.Contains(unapproved.Value!.Checks, x => x.Code == "CASH_FLOW_SOURCE_LINEAGE" && !x.Passed);
    Assert.Contains(unapproved.Value.Warnings, x => x.Code == "CASH_FLOW_LINEAGE_PENDING");
  }

  [Fact]
  public void SupplementaryStatements_ShowPersistedSchedules_ExcludeNoncashFromBridge_AndExposeUnverifiedGates()
  {
    var schedules = new PresentationSupplementarySchedules(
      58372m, 1090993m,
      [
        new("OPERATING", "Operating receipts and payments", 1200000m, false, "OPERATING_NET"),
        new("INVESTING", "Equipment purchases", -200000m, false, "PPE_PURCHASE"),
        new("INVESTING", "Lease asset recognised", -50000m, true, "NONCASH_LEASE"),
        new("FINANCING", "New borrowing", 32621m, false, "BORROWING")
      ],
      [new("USD/QAR", 0m)],
      [new("RETAINED_EARNINGS", "Retained earnings", 979299m, 3818172m, 0m, 0m, 0m, 4797471m)],
      [new("CASH_FLOW_RECONCILED", true, "Cash bridge and selected closing-cash population reconcile to the package build.")]);

    var result = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(), Notes(), Header(withPrior: true), Manifest(withPrior: true),
      currentSupplementary: schedules, priorSupplementary: null);
    Assert.True(result.Succeeded, result.Message);
    var presentation = result.Value!;

    Assert.Equal(1032621m, Amount(Row(presentation, "CF_NET_CHANGE").Current));
    Assert.Equal(58372m, Amount(Row(presentation, "CF_BEGINNING_CASH").Current));
    Assert.Equal(1090993m, Amount(Row(presentation, "CF_ENDING_CASH").Current));
    Assert.Null(Row(presentation, "CF_NET_CHANGE").Prior);
    Assert.Contains(presentation.Statements.Single(x => x.Code == "CASH_FLOWS").Rows,
      x => x.Label.Contains("Noncash transaction", StringComparison.Ordinal) && x.Current == -50000m);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_BRIDGE" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_REQUIRED_SECTIONS" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_PACKAGE_RECONCILIATION" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_SOURCE_LINEAGE" && !x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_ROLLFORWARD" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_PROFIT_CROSS_CAST" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_OCI_CROSS_CAST" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_SFP_CROSS_CAST" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_COMPARATIVE_BRIDGE" && !x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_COMPARATIVE" && !x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_COMPARATIVE" && !x.Passed);
    Assert.Contains(presentation.Warnings, x => x.Code == "CASH_FLOW_LINEAGE_PENDING");
    Assert.Contains(presentation.Warnings, x => x.Code == "EQUITY_READINESS_PENDING");

    var word = FinancialStatementPresentationRenderer.Render(presentation, FinancialStatementPresentationRenderer.WordFormat);
    using var wordStream = new MemoryStream(word);
    using var wordDocument = WordprocessingDocument.Open(wordStream, false);
    var content = wordDocument.MainDocumentPart!.Document!.Body!.InnerText;
    Assert.Contains("Statement of changes in equity", content, StringComparison.Ordinal);
    Assert.Contains("Statement of cash flows", content, StringComparison.Ordinal);
    Assert.Contains("Noncash transaction", content, StringComparison.Ordinal);
    Assert.Contains("Check EQUITY_SFP_CROSS_CAST: PASS", content, StringComparison.Ordinal);
    Assert.Contains("Check CASH_FLOW_PACKAGE_RECONCILIATION: PASS", content, StringComparison.Ordinal);
    Assert.Contains("Not available", content, StringComparison.Ordinal);
  }

  [Fact]
  public void CompleteComparativeCashAndEquitySchedules_CrossCastForBothPeriods()
  {
    var current = new PresentationSupplementarySchedules(
      58372m, 1090993m,
      [new("OPERATING", "Net operating cash", 1200000m, false, "OPERATING"),
       new("INVESTING", "Investment payments", -200000m, false, "INVESTING"),
       new("FINANCING", "New borrowing", 32621m, false, "FINANCING")],
      [new("USD/QAR", 0m)],
      [new("EQUITY", "Total equity", 979299m, 3818172m, 0m, 0m, 0m, 4797471m)],
      [new("CASH_FLOW_RECONCILED", true, "Current bridge and cash population reconcile.")]);
    var prior = new PresentationSupplementarySchedules(
      0m, 58372m,
      [new("OPERATING", "Net operating cash", 58372m, false, "OPERATING"),
       new("INVESTING", "Investment payments", 0m, false, "INVESTING"),
       new("FINANCING", "Financing cash", 0m, false, "FINANCING")],
      [new("USD/QAR", 0m)],
      [new("EQUITY", "Total equity", -8247m, 2695325m, 0m, 0m, 0m, 2687078m)],
      [new("CASH_FLOW_RECONCILED", true, "Comparative bridge and cash population reconcile.")]);

    var result = FinancialStatementPresentationBuilder.Build(Layout(), Current(), Prior(), Notes(),
      Header(withPrior: true), Manifest(withPrior: true), currentSupplementary: current, priorSupplementary: prior);
    Assert.True(result.Succeeded, result.Message);
    var presentation = result.Value!;
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_COMPARATIVE_ROLLFORWARD" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_COMPARATIVE_PROFIT_CROSS_CAST" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_COMPARATIVE_OCI_CROSS_CAST" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_COMPARATIVE_SFP_CROSS_CAST" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_COMPARATIVE_REQUIRED_SECTIONS" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_COMPARATIVE_BRIDGE" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "CASH_FLOW_COMPARATIVE_PACKAGE_RECONCILIATION" && x.Passed);
    Assert.Equal(58372m, presentation.Statements.Single(x => x.Code == "CASH_FLOWS").Rows.Single(x => x.Code == "CF_ENDING_CASH").Prior);
    Assert.Equal(2687078m, presentation.Statements.Single(x => x.Code == "CHANGES_IN_EQUITY").Rows.Single(x => x.Label == "Closing balance").Prior);
  }

  [Fact]
  public void EquityComponentTransfer_PresentsLinkedZeroSumRows_AndReconcilesBothComponents()
  {
    var current = new PresentationSupplementarySchedules(
      58372m, 1090993m,
      [new("OPERATING", "Net operating cash", 1200000m, false, "OPERATING"),
       new("INVESTING", "Investment payments", -200000m, false, "INVESTING"),
       new("FINANCING", "New borrowing", 32621m, false, "FINANCING")],
      [new("USD/QAR", 0m)],
      [new("RETAINED_EARNINGS", "Retained earnings", 979299m, 3818172m, 0m, 0m, 0m, 4797446m, TransferOutAmount: 25m),
       new("GENERAL_RESERVE", "General reserve", 0m, 0m, 0m, 0m, 0m, 25m, TransferInAmount: 25m)],
      [new("CASH_FLOW_RECONCILED", true, "Cash reconciles.")],
      EquityTransfers: [new("TR-01", "RETAINED_EARNINGS", "GENERAL_RESERVE", 25m, "approved-resolution-17")]);

    var result = FinancialStatementPresentationBuilder.Build(Layout(), Current(), null, Notes(), Header(withPrior: false), Manifest(withPrior: false),
      currentSupplementary: current, priorSupplementary: null);
    Assert.True(result.Succeeded, result.Message);
    var presentation = result.Value!;
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_ROLLFORWARD" && x.Passed);
    Assert.Contains(presentation.Checks, x => x.Code == "EQUITY_SFP_CROSS_CAST" && x.Passed);
    var transfers = presentation.Statements.Single(x => x.Code == "CHANGES_IN_EQUITY").Rows
      .Where(x => x.Code.StartsWith("EQ_TRANSFER_", StringComparison.Ordinal)).ToArray();
    Assert.Equal(2, transfers.Length);
    Assert.Equal(-25m, transfers.Single(x => x.Code.EndsWith("_OUT", StringComparison.Ordinal)).Current);
    Assert.Equal(25m, transfers.Single(x => x.Code.EndsWith("_IN", StringComparison.Ordinal)).Current);
    Assert.All(transfers, x => Assert.Contains("approved-resolution-17", x.Label, StringComparison.Ordinal));
    Assert.All(transfers, x => Assert.Null(x.NoteReference));
  }

  [Theory]
  [InlineData(false, false)]
  [InlineData(false, true)]
  [InlineData(true, false)]
  [InlineData(true, true)]
  public void EquityCrossCast_PreservesSignsForPositiveEquityAndDeficits(bool deficit, bool reversedSchedule)
  {
    var sign = deficit ? -1m : 1m;
    var currentClosing = 4797471m * sign * (reversedSchedule ? -1m : 1m);
    var priorClosing = 2687078m * sign * (reversedSchedule ? -1m : 1m);
    var currentProfit = 3818172m * sign; var priorProfit = 2695325m * sign;
    var current = new PresentationSupplementarySchedules(null, null, [], [],
      [new("EQUITY", "Total equity", currentClosing - currentProfit, currentProfit, 0m, 0m, 0m, currentClosing)], []);
    var prior = new PresentationSupplementarySchedules(null, null, [], [],
      [new("EQUITY", "Total equity", priorClosing - priorProfit, priorProfit, 0m, 0m, 0m, priorClosing)], []);
    var currentBalances = Current().Select(x => x with { Amount = x.Amount * sign }).ToArray();
    var priorBalances = Prior().Select(x => x with { Amount = x.Amount * sign }).ToArray();
    var result = FinancialStatementPresentationBuilder.Build(Layout(), currentBalances, priorBalances, Notes(),
      Header(withPrior: true), Manifest(withPrior: true), currentSupplementary: current, priorSupplementary: prior);
    Assert.True(result.Succeeded, result.Message);
    var checks = result.Value!.Checks;
    Assert.True(checks.Single(x => x.Code == "EQUITY_ROLLFORWARD").Passed);
    Assert.True(checks.Single(x => x.Code == "EQUITY_COMPARATIVE_ROLLFORWARD").Passed);
    Assert.Equal(!reversedSchedule, checks.Single(x => x.Code == "EQUITY_SFP_CROSS_CAST").Passed);
    Assert.Equal(!reversedSchedule, checks.Single(x => x.Code == "EQUITY_COMPARATIVE_SFP_CROSS_CAST").Passed);
    // Passing arithmetic must not promote this draft to professional acceptance or prove source lineage.
    Assert.Contains(checks, x => x.Code == "EQUITY_SOURCE_LINEAGE" && !x.Passed);
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
    dangling.Add(new PresentationNoteLine("12", "NOT_PRESENTED", 1m, Currency: "QAR", EvidenceReference: "synthetic-note-source"));
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
    var manifest = Manifest(withPrior: true);
    var disclosures = new[]
    {
      new PresentationDisclosureSource("5", "Property additions are preparer described.", false, null, manifest.PackageId),
      new PresentationDisclosureSource("5", "Comparative property note is preparer described.", false, null, manifest.ComparativePackageId!.Value),
      new PresentationDisclosureSource("12", string.Empty, true, "No related balance was reported by the preparer.", manifest.PackageId)
    };
    var p = Build(disclosures: disclosures).Value!;
    var bytes = FinancialStatementPresentationRenderer.Render(p, FinancialStatementPresentationRenderer.WordFormat);
    using var stream = new MemoryStream(bytes);
    using var document = WordprocessingDocument.Open(stream, false);
    var body = document.MainDocumentPart!.Document!.Body!;
    var tables = body.Descendants<W.Table>().ToList();
    Assert.Equal(10, tables.Count);
    Assert.NotNull(tables[0].Descendants<W.TableHeader>().FirstOrDefault());
    var text = body.InnerText;
    Assert.Contains("6,172,821", text, StringComparison.Ordinal);
    Assert.Contains("(508,247)", text, StringComparison.Ordinal);
    Assert.Contains("–", text, StringComparison.Ordinal);
    Assert.Contains(FinancialStatementPresentationRenderer.Banner, text, StringComparison.Ordinal);
    Assert.Contains("Check CASH_FLOW_PACKAGE_RECONCILIATION: FAIL", text, StringComparison.Ordinal);
    Assert.Contains("Property additions are preparer described.", text, StringComparison.Ordinal);
    Assert.Contains("synthetic-note-source", text, StringComparison.Ordinal);
    Assert.Contains("not independently verified", text, StringComparison.Ordinal);
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
    Assert.Equal(6, sheetNames.Count);
    Assert.All(sheetNames, name => Assert.True(name!.Length <= 31));

    var worksheets = workbookPart.WorksheetParts.Select(x => x.Worksheet!).ToList();
    Assert.Empty(worksheets.SelectMany(x => x.Descendants<S.CellFormula>()));

    var numericCells = worksheets.SelectMany(x => x.Descendants<S.Cell>())
      .Where(x => x.DataType is null && x.CellValue is not null)
      .Select(x => decimal.Parse(x.CellValue!.Text, CultureInfo.InvariantCulture)).ToList();
    Assert.Contains(6172821m, numericCells);
    Assert.Contains(-508247m, numericCells);
    Assert.Contains(3818172m, numericCells);
    var noteSheet = workbookPart.Workbook!.Descendants<S.Sheet>().Single(x => x.Name!.Value == "Notes");
    var noteWorksheet = (WorksheetPart)workbookPart.GetPartById(noteSheet.Id!.Value!);
    Assert.Contains("NOTE_SCHEDULE", noteWorksheet.Worksheet!.Descendants<S.Text>().Select(x => x.Text));
    Assert.Contains("synthetic-note-source", noteWorksheet.Worksheet.Descendants<S.Text>().Select(x => x.Text));
    var firstCheck = p.Checks[0];
    var expectedCheck = $"Check {firstCheck.Code}: {(firstCheck.Passed ? "PASS" : "FAIL")} — {firstCheck.Detail}";
    Assert.Contains(expectedCheck, FinancialStatementPresentationRenderer.ManifestLines(p));
    Assert.Contains(expectedCheck, worksheets[^1].Descendants<S.Text>().Select(x => x.Text));
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
  public void PdfColumns_FitInsideA4PrintableWidth_AndLongLabelsRemainRenderable()
  {
    const double a4WidthPoints = 595.2756;
    const double marginPoints = 56.6929;
    var widths = FinancialStatementPresentationRenderer.PdfColumnWidthsInPoints(a4WidthPoints, marginPoints, marginPoints);
    Assert.Equal(4, widths.Count);
    Assert.All(widths, width => Assert.True(width > 0));
    Assert.Equal(a4WidthPoints - marginPoints * 2, widths.Sum(), precision: 6);
    Assert.Throws<ArgumentOutOfRangeException>(() =>
      FinancialStatementPresentationRenderer.PdfColumnWidthsInPoints(100, 60, 60));

    var original = Build().Value!;
    var longLabel = string.Join(' ', Enumerable.Repeat("Receivables and other long statement label", 120));
    var withLongLabel = original with
    {
      Statements = original.Statements.Select(statement => statement with
      {
        Rows = statement.Rows.Select(row => row.Code == "RECV" ? row with { Label = longLabel } : row).ToList()
      }).ToList()
    };
    using var baselineStream = new MemoryStream(FinancialStatementPresentationRenderer.Render(
      original, FinancialStatementPresentationRenderer.PdfFormat));
    using var baselineDocument = PdfReader.Open(baselineStream, PdfDocumentOpenMode.Import);
    var bytes = FinancialStatementPresentationRenderer.Render(withLongLabel, FinancialStatementPresentationRenderer.PdfFormat);
    using var stream = new MemoryStream(bytes);
    using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
    Assert.True(document.PageCount > baselineDocument.PageCount,
      "A long statement label should wrap and consume printable page space rather than be clipped horizontally.");
    Assert.All(document.Pages.Cast<PdfSharp.Pdf.PdfPage>(), page =>
    {
      Assert.InRange(Math.Abs(page.Width.Point - a4WidthPoints), 0, 0.1);
      Assert.InRange(Math.Abs(page.Height.Point - 841.8898), 0, 0.1);
    });
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
