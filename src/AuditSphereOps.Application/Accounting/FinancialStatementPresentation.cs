using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Accounting;

/// <summary>
/// Presentation policy for the typed statement slice (FSR-002, FSR-010, FSR-011). Display amounts are whole units
/// unless an approved design says otherwise; the status is always an unapproved draft until review, sealing and
/// approval exist (FSR-013 and FSR-014).
/// </summary>
public static class FinancialStatementPresentationPolicy
{
  public const string BuilderVersion = "auditsphere.statement-presentation.v1";
  public const string DisplayPolicy = "whole units; half-even rounding of displayed amounts; negatives in brackets; zero shown as a dash";
  public const string DraftStatus = "UNAPPROVED_DRAFT";
  public const int DefaultDisplayDecimals = 0;
  public const int MaxBalanceRows = 20000;

  /// <summary>Sections this presentation renders. Equity movements, cash flows and notes are separate stories.</summary>
  public static readonly IReadOnlySet<string> SupportedSections = new HashSet<string>(StringComparer.Ordinal)
  {
    StatementLayoutSections.FinancialPosition, StatementLayoutSections.ProfitOrLoss, StatementLayoutSections.OtherComprehensiveIncome
  };

  /// <summary>
  /// The presentation sign implied by a statement section under the canonical convention, where debit balances are
  /// positive: assets and expenses are shown as they are; liabilities, equity, income and OCI are credit-normal and
  /// are shown inverted. Null for a section the presentation does not understand.
  /// </summary>
  public static string? ExpectedDisplaySign(string statementSection) => statementSection switch
  {
    "ASSETS" or "EXPENSE" => StatementLayoutDisplaySigns.Signed,
    "LIABILITIES" or "EQUITY" or "INCOME" or "OCI" => StatementLayoutDisplaySigns.Inverted,
    _ => null
  };
}

/// <summary>A canonical balance of one mapped taxonomy destination, as persisted on a financial package line.</summary>
public sealed record PresentationBalance(string DestinationCode, string StatementSection, decimal Amount);

/// <summary>A persisted note cross-cast: its amount must equal the magnitude of the face line it points to.</summary>
public sealed record PresentationNoteLine(string NoteCode, string FaceDestinationCode, decimal Amount);

public sealed record PresentationHeader(
  string Entity, string Framework, string Currency, string CurrentPeriodEnd, string? PriorPeriodEnd, string Reference);

/// <summary>Identities that let a reader prove exactly which inputs produced the presented statements.</summary>
public sealed record PresentationManifest(
  Guid PackageId, string CalculationHash, Guid LayoutVersionId, string LayoutHash,
  Guid? ComparativePackageId, string? ComparativeCalculationHash, string BuilderVersion, string DisplayPolicy);

public static class PresentationRowKinds
{
  public const string Heading = "HEADING";
  public const string Line = "LINE";
  public const string Subtotal = "SUBTOTAL";
  public const string Total = "TOTAL";
}

/// <summary>One presented row. Amounts are display values; the canonical values stay in the package.</summary>
public sealed record PresentationRow(
  string Kind, string Code, string Label, int Depth, string? NoteReference, decimal? Current, decimal? Prior);

public sealed record PresentationStatement(string Code, string Title, IReadOnlyList<PresentationRow> Rows);

public sealed record PresentationCheck(string Code, bool Passed, string Detail);

public sealed record PresentationWarning(string Code, string Detail);

public sealed record FinancialStatementPresentation(
  string Status,
  PresentationHeader Header,
  PresentationManifest Manifest,
  int DisplayDecimals,
  IReadOnlyList<PresentationStatement> Statements,
  IReadOnlyList<PresentationCheck> Checks,
  IReadOnlyList<PresentationWarning> Warnings);

/// <summary>
/// Pure, deterministic presentation of canonical package balances through a published statement layout. It evaluates
/// the layout with the layout engine, applies presentation signs and display rounding, and refuses to present anything
/// that does not balance, map exactly once, carry the expected display sign, cross-cast its notes or foot in display.
/// It performs no EF, network, clock or random work.
/// </summary>
public static class FinancialStatementPresentationBuilder
{
  public static CommandResult<FinancialStatementPresentation> Build(
    IReadOnlyList<StatementLineDefinition> layout,
    IReadOnlyList<PresentationBalance> current,
    IReadOnlyList<PresentationBalance>? prior,
    IReadOnlyList<PresentationNoteLine> notes,
    PresentationHeader header,
    PresentationManifest manifest,
    int displayDecimals = FinancialStatementPresentationPolicy.DefaultDisplayDecimals)
  {
    ArgumentNullException.ThrowIfNull(layout);
    ArgumentNullException.ThrowIfNull(current);
    ArgumentNullException.ThrowIfNull(notes);
    ArgumentNullException.ThrowIfNull(header);
    ArgumentNullException.ThrowIfNull(manifest);
    if (displayDecimals is < 0 or > 2)
      throw new ArgumentOutOfRangeException(nameof(displayDecimals), "Display decimals must be 0 to 2.");

    var checks = new List<PresentationCheck>();
    var failures = new List<string>();
    void Pass(string code, string detail) => checks.Add(new PresentationCheck(code, true, detail));
    void Fail(string code, string detail)
    {
      checks.Add(new PresentationCheck(code, false, detail));
      failures.Add(detail);
    }
    CommandResult<FinancialStatementPresentation> Refuse() => CommandResult<FinancialStatementPresentation>.Fail(
      ErrorCodes.Accounting.PackageInvalid,
      "The statement presentation is blocked: " + string.Join(" ", failures.Take(6)));

    if (current.Count > FinancialStatementPresentationPolicy.MaxBalanceRows ||
        (prior?.Count ?? 0) > FinancialStatementPresentationPolicy.MaxBalanceRows)
    {
      Fail("BOUNDS", "The package has more balance rows than the presentation bound allows.");
      return Refuse();
    }

    // 1. The layout must be a valid, supported, published-grade layout.
    var grammar = StatementLayoutEngine.Validate(layout);
    if (grammar.Count > 0)
    {
      Fail("LAYOUT_VALID", $"The statement layout is not valid: {grammar[0].Code} {grammar[0].Message}");
      return Refuse();
    }
    Pass("LAYOUT_VALID", "The statement layout passes the grammar checks.");

    var outOfScope = layout
      .Where(x => !FinancialStatementPresentationPolicy.SupportedSections.Contains(Normal(x.Section)))
      .Select(x => $"{Normal(x.LineCode)} in {Normal(x.Section)}")
      .Concat(layout.Where(x => x.Kind == StatementLayoutLineKinds.Ratio).Select(x => $"{Normal(x.LineCode)} is a ratio line"))
      .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (outOfScope.Length > 0)
    {
      Fail("LAYOUT_SCOPE", "This presentation covers the financial position, profit or loss and other comprehensive income without ratio lines. Out of scope: " + string.Join("; ", outOfScope) + ".");
      return Refuse();
    }
    Pass("LAYOUT_SCOPE", "Every layout line belongs to a supported statement and kind.");

    // 2. Balances: each destination has one statement section, and each side nets to zero.
    var currentBalances = Aggregate(current, Fail);
    var priorBalances = prior is null ? null : Aggregate(prior, Fail);
    var currentNet = MoneyPolicy.Normalize(currentBalances.Values.Sum(x => x.Amount));
    if (currentNet == 0m)
      Pass("TB_BALANCED", "The current balances net to zero at six-decimal precision.");
    else
      Fail("TB_BALANCED", $"The current balances do not net to zero (difference {currentNet.ToString("0.000000", System.Globalization.CultureInfo.InvariantCulture)}); the statements cannot be presented.");
    if (priorBalances is not null)
    {
      var priorNet = MoneyPolicy.Normalize(priorBalances.Values.Sum(x => x.Amount));
      if (priorNet == 0m) Pass("PRIOR_TB_BALANCED", "The comparative balances net to zero at six-decimal precision.");
      else Fail("PRIOR_TB_BALANCED", "The comparative balances do not net to zero; the comparative column cannot be presented.");
    }

    // 3. Mapping: every mapped taxonomy balance feeds exactly one layout line, and every non-zero balance is mapped.
    var mapped = layout.Where(x => x.Kind == StatementLayoutLineKinds.MappedTaxonomyBalance).ToList();
    var leafByDestination = new Dictionary<string, string>(StringComparer.Ordinal);
    var duplicated = new List<string>();
    foreach (var leaf in mapped)
    {
      var destination = Normal(leaf.TaxonomyNodeCode!);
      if (leafByDestination.ContainsKey(destination)) duplicated.Add(destination);
      else leafByDestination[destination] = Normal(leaf.LineCode);
    }
    if (duplicated.Count > 0)
      Fail("MAPPING_SINGLE_USE", "Taxonomy balances appear on more than one layout line: " + string.Join(", ", duplicated.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)) + ".");
    else
      Pass("MAPPING_SINGLE_USE", "Each taxonomy balance appears on at most one layout line.");

    var unmapped = currentBalances.Where(x => x.Value.Amount != 0m && !leafByDestination.ContainsKey(x.Key)).Select(x => x.Key)
      .Concat(priorBalances?.Where(x => x.Value.Amount != 0m && !leafByDestination.ContainsKey(x.Key)).Select(x => x.Key) ?? [])
      .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (unmapped.Length > 0)
      Fail("BALANCES_MAPPED", "Non-zero balances are not presented by the layout: " + string.Join(", ", unmapped) + ".");
    else
      Pass("BALANCES_MAPPED", "Every non-zero balance is presented by a layout line.");

    // 4. Display signs must match the statement section of the balance they present.
    var signProblems = new List<string>();
    foreach (var leaf in mapped)
    {
      var destination = Normal(leaf.TaxonomyNodeCode!);
      var section = currentBalances.TryGetValue(destination, out var c) ? c.Section
        : priorBalances is not null && priorBalances.TryGetValue(destination, out var p) ? p.Section : null;
      if (section is null) continue; // no activity in either period
      var expected = FinancialStatementPresentationPolicy.ExpectedDisplaySign(section);
      if (expected is null)
        signProblems.Add($"{Normal(leaf.LineCode)} ({destination}) has unsupported section {section}");
      else if (leaf.DisplaySign != expected)
        signProblems.Add($"{Normal(leaf.LineCode)} ({destination}, {section}) displays {leaf.DisplaySign} but must display {expected}");
    }
    if (signProblems.Count > 0)
      Fail("DISPLAY_SIGNS", "Display signs do not match the statement sections: " + string.Join("; ", signProblems) + ".");
    else
      Pass("DISPLAY_SIGNS", "Every mapped line displays with the sign implied by its statement section.");

    // 5. Evaluate the layout canonically (no presentation sign applied here).
    var currentValues = StatementLayoutEngine.Evaluate(layout,
      currentBalances.ToDictionary(x => x.Key, x => x.Value.Amount, StringComparer.Ordinal));
    var priorValues = priorBalances is null ? null : StatementLayoutEngine.Evaluate(layout,
      priorBalances.ToDictionary(x => x.Key, x => x.Value.Amount, StringComparer.Ordinal));

    // 6. Accounting equation: assets equal liabilities, equity, income, expense and OCI (profit included).
    var assets = mapped.Where(x => SectionOf(x, currentBalances, priorBalances) == "ASSETS").Sum(x => currentValues[Normal(x.LineCode)]);
    var others = mapped.Where(x => SectionOf(x, currentBalances, priorBalances) is { } s && s != "ASSETS").Sum(x => currentValues[Normal(x.LineCode)]);
    if (MoneyPolicy.Normalize(assets + others) == 0m)
      Pass("ACCOUNTING_EQUATION", "Total assets equal liabilities, equity and profit for the year at six-decimal precision.");
    else
      Fail("ACCOUNTING_EQUATION", "Total assets do not equal liabilities, equity and profit for the year.");

    // 7. Notes: every note points to a mapped face line and its amount cross-casts to that line's magnitude.
    var noteGroups = notes.GroupBy(x => Normal(x.FaceDestinationCode), StringComparer.Ordinal).ToList();
    var dangling = noteGroups.Where(g => !leafByDestination.ContainsKey(g.Key)).Select(g => g.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (dangling.Length > 0)
      Fail("NOTE_REFERENCE", "Notes point to face lines that the layout does not present: " + string.Join(", ", dangling) + ".");
    else
      Pass("NOTE_REFERENCE", "Every note points to a presented face line.");
    var mismatchedNotes = new List<string>();
    foreach (var group in noteGroups.Where(g => leafByDestination.ContainsKey(g.Key)))
    {
      var noted = MoneyPolicy.Normalize(group.Sum(x => x.Amount));
      var face = Math.Abs(currentValues[leafByDestination[group.Key]]);
      if (Math.Abs(noted) != face)
        mismatchedNotes.Add($"{group.Key} notes total {noted:0.######} but the face line is {face:0.######}");
    }
    if (mismatchedNotes.Count > 0)
      Fail("NOTE_CROSS_CAST", "Notes do not cross-cast to the statement: " + string.Join("; ", mismatchedNotes) + ".");
    else
      Pass("NOTE_CROSS_CAST", "Every note cross-casts to the magnitude of its face line.");
    var noteReferences = noteGroups.ToDictionary(g => g.Key,
      g => string.Join(", ", g.Select(x => x.NoteCode.Trim()).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)),
      StringComparer.Ordinal);

    // 8. Presented rows, with display rounding and footing of every subtotal.
    var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var sum in layout.Where(x => x.Kind == StatementLayoutLineKinds.SumChildLines))
      foreach (var child in sum.ChildLineCodes ?? [])
        parentOf.TryAdd(Normal(child), Normal(sum.LineCode));
    int Depth(string code)
    {
      var depth = 0;
      var cursor = code;
      for (var guard = 0; guard < layout.Count && parentOf.TryGetValue(cursor, out var parent); guard++)
      {
        depth++;
        cursor = parent;
      }
      return depth;
    }
    var byCode = layout.ToDictionary(x => Normal(x.LineCode), StringComparer.Ordinal);
    var unit = 1m / (decimal)Math.Pow(10, displayDecimals);
    decimal Display(decimal canonical, string sign) =>
      decimal.Round(StatementLayoutEngine.ToDisplayAmount(canonical, sign), displayDecimals, MidpointRounding.ToEven);

    var footingFailures = new List<string>();
    var rounding = new List<PresentationWarning>();
    void Foot(string period, IReadOnlyDictionary<string, decimal> values)
    {
      foreach (var sum in layout.Where(x => x.Kind == StatementLayoutLineKinds.SumChildLines))
      {
        var code = Normal(sum.LineCode);
        var children = (sum.ChildLineCodes ?? []).Select(Normal)
          .Where(child => byCode[child].Kind != StatementLayoutLineKinds.Heading).ToArray();
        if (children.Length == 0) continue;
        var childTotal = children.Sum(child => Display(values[child], byCode[child].DisplaySign));
        var parent = Display(values[code], sum.DisplaySign);
        var difference = childTotal - parent;
        // Each displayed component may differ by half a unit from its canonical value, so the tolerance grows with the count.
        var tolerance = unit * (0.5m * children.Length + 0.5m);
        if (Math.Abs(difference) > tolerance)
          footingFailures.Add($"{period} {code}: components show {childTotal:0.####} but the total shows {parent:0.####}");
        else if (difference != 0m)
          rounding.Add(new PresentationWarning("DISPLAY_ROUNDING",
            $"{period} {code}: the displayed components differ from the displayed total by {difference:0.####} because of rounding; the canonical amounts foot exactly."));
      }
    }
    Foot("Current period", currentValues);
    if (priorValues is not null) Foot("Prior period", priorValues);
    if (footingFailures.Count > 0)
      Fail("DISPLAY_FOOTING", "Displayed subtotals do not foot: " + string.Join("; ", footingFailures) + ".");
    else
      Pass("DISPLAY_FOOTING", "Every displayed subtotal foots to its components within display rounding.");

    if (failures.Count > 0) return Refuse();

    PresentationRow RowFor(StatementLineDefinition line)
    {
      var code = Normal(line.LineCode);
      var depth = Depth(code);
      var label = line.Label.Trim();
      if (line.Kind == StatementLayoutLineKinds.Heading)
        return new PresentationRow(PresentationRowKinds.Heading, code, label, depth, null, null, null);
      var kind = line.Kind == StatementLayoutLineKinds.MappedTaxonomyBalance ? PresentationRowKinds.Line
        : line.Kind == StatementLayoutLineKinds.SumChildLines ? PresentationRowKinds.Subtotal : PresentationRowKinds.Total;
      string? reference = line.Kind == StatementLayoutLineKinds.MappedTaxonomyBalance
        ? noteReferences.GetValueOrDefault(Normal(line.TaxonomyNodeCode!)) : null;
      if (string.IsNullOrEmpty(reference)) reference = null;
      var currentDisplay = Display(currentValues[code], line.DisplaySign);
      var priorDisplay = priorValues is null ? (decimal?)null : Display(priorValues[code], line.DisplaySign);
      return new PresentationRow(kind, code, label, depth, reference, currentDisplay, priorDisplay);
    }

    var definitions = new (string Code, string Title, string[] Sections)[]
    {
      ("FINANCIAL_POSITION", "Statement of financial position", [StatementLayoutSections.FinancialPosition]),
      ("PROFIT_OR_LOSS", "Statement of profit or loss and other comprehensive income",
        [StatementLayoutSections.ProfitOrLoss, StatementLayoutSections.OtherComprehensiveIncome])
    };
    var statements = new List<PresentationStatement>();
    foreach (var definition in definitions)
    {
      var rows = new List<PresentationRow>();
      foreach (var section in definition.Sections)
        rows.AddRange(layout.Where(x => Normal(x.Section) == section).OrderBy(x => x.Order).Select(RowFor));
      if (rows.Any(x => x.Kind != PresentationRowKinds.Heading))
        statements.Add(new PresentationStatement(definition.Code, definition.Title, rows));
    }
    if (statements.Count == 0)
    {
      Fail("STATEMENTS", "The layout presents no statement lines.");
      return Refuse();
    }
    Pass("STATEMENTS", $"{statements.Count} statement(s) presented from the published layout.");

    return CommandResult<FinancialStatementPresentation>.Ok(new FinancialStatementPresentation(
      FinancialStatementPresentationPolicy.DraftStatus, header, manifest, displayDecimals, statements, checks, rounding));
  }

  private static Dictionary<string, (string Section, decimal Amount)> Aggregate(
    IReadOnlyList<PresentationBalance> rows, Action<string, string> fail)
  {
    var result = new Dictionary<string, (string Section, decimal Amount)>(StringComparer.Ordinal);
    foreach (var row in rows)
    {
      var code = Normal(row.DestinationCode);
      var section = Normal(row.StatementSection);
      if (result.TryGetValue(code, out var existing))
      {
        if (existing.Section != section)
        {
          fail("BALANCE_SECTION", $"{code} is presented in both {existing.Section} and {section}.");
          continue;
        }
        result[code] = (section, MoneyPolicy.Normalize(existing.Amount + row.Amount));
      }
      else
      {
        result[code] = (section, MoneyPolicy.Normalize(row.Amount));
      }
    }
    return result;
  }

  private static string? SectionOf(StatementLineDefinition line,
    IReadOnlyDictionary<string, (string Section, decimal Amount)> current,
    IReadOnlyDictionary<string, (string Section, decimal Amount)>? prior)
  {
    var destination = Normal(line.TaxonomyNodeCode!);
    if (current.TryGetValue(destination, out var c)) return c.Section;
    return prior is not null && prior.TryGetValue(destination, out var p) ? p.Section : null;
  }

  private static string Normal(string value) => value.Trim().ToUpperInvariant();
}
