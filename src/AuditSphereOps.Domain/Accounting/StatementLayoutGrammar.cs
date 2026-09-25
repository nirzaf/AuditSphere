// Statement layout grammar (M24). Pure and deterministic: no EF Core, no clock, no
// non-deterministic IDs. A layout is a bounded, ordered set of typed lines whose
// references must be acyclic and must not double-count the same mapped balance, so a
// reviewer can trace every presented figure back to a taxonomy balance or a subtotal.
namespace AuditSphereOps.Domain.Accounting;

public static class StatementLayoutLineKinds
{
  /// <summary>A presentational caption with no numeric value.</summary>
  public const string Heading = "HEADING";
  /// <summary>The signed balance mapped to one taxonomy node.</summary>
  public const string MappedTaxonomyBalance = "MAPPED_TAXONOMY_BALANCE";
  /// <summary>The sum of this line's child lines.</summary>
  public const string SumChildLines = "SUM_CHILD_LINES";
  /// <summary>The signed sum of explicitly referenced lines.</summary>
  public const string TotalReferencedLines = "TOTAL_REFERENCED_LINES";
  /// <summary>A ratio of two referenced lines with a defined divide-by-zero behaviour.</summary>
  public const string Ratio = "RATIO";
}

public static class StatementLayoutOperations
{
  public const string Add = "ADD";
  public const string Subtract = "SUBTRACT";
}

public static class StatementLayoutDisplaySigns
{
  /// <summary>The canonical amount is presented unchanged.</summary>
  public const string Signed = "SIGNED";
  /// <summary>The canonical amount is negated for presentation only.</summary>
  public const string Inverted = "INVERTED";
  /// <summary>The canonical amount is shown as an absolute value (contra presentation).</summary>
  public const string Absolute = "ABSOLUTE";
}

public static class StatementLayoutSections
{
  public const string FinancialPosition = "FINANCIAL_POSITION";
  public const string ProfitOrLoss = "PROFIT_OR_LOSS";
  public const string OtherComprehensiveIncome = "OCI";
  public const string ChangesInEquity = "CHANGES_IN_EQUITY";
  public const string CashFlows = "CASH_FLOWS";
  public const string Notes = "NOTES";
  public static readonly string[] All =
    [FinancialPosition, ProfitOrLoss, OtherComprehensiveIncome, ChangesInEquity, CashFlows, Notes];
}

/// <summary>One line of a statement layout. Order is explicit; references are by line code.</summary>
public sealed record StatementLineDefinition(
  string LineCode,
  string Label,
  string Kind,
  string Section,
  int Order,
  string DisplaySign = StatementLayoutDisplaySigns.Signed,
  /// <summary>For MAPPED_TAXONOMY_BALANCE: the taxonomy node code whose balance is presented.</summary>
  string? TaxonomyNodeCode = null,
  /// <summary>For SUM_CHILD_LINES: the child line codes whose amounts are summed.</summary>
  IReadOnlyList<string>? ChildLineCodes = null,
  /// <summary>For TOTAL_REFERENCED_LINES and RATIO: the referenced line codes.</summary>
  IReadOnlyList<string>? ReferencedLineCodes = null,
  /// <summary>For TOTAL_REFERENCED_LINES: the sign applied to each referenced line.</summary>
  IReadOnlyList<string>? Operations = null,
  /// <summary>For RATIO: true when a zero denominator yields zero rather than an error.</summary>
  bool ZeroDenominatorYieldsZero = false,
  /// <summary>For RATIO: the denominator line code.</summary>
  string? DenominatorLineCode = null,
  /// <summary>True when the line is a subtotal presented above its contributing leaves.</summary>
  bool IsSubtotal = false);

public sealed record StatementLayoutValidationError(string LineCode, string Code, string Message);

/// <summary>
/// Deterministic layout evaluation. Validation is separate from evaluation so a draft can
/// be checked before publication and a published layout is always evaluable.
/// </summary>
public static class StatementLayoutEngine
{
  /// <summary>Validates the layout grammar: unique ordered codes, resolvable references,
  /// an acyclic reference graph, and no double counting of a mapped taxonomy balance
  /// through both a subtotal and one of its own leaves.</summary>
  public static IReadOnlyList<StatementLayoutValidationError> Validate(
    IReadOnlyList<StatementLineDefinition> lines)
  {
    ArgumentNullException.ThrowIfNull(lines);
    var errors = new List<StatementLayoutValidationError>();
    if (lines.Count == 0)
    {
      errors.Add(new StatementLayoutValidationError(string.Empty, "layout.empty", "A layout must contain at least one line."));
      return errors;
    }
    if (lines.Select(x => x.LineCode.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() != lines.Count)
      errors.Add(new StatementLayoutValidationError(string.Empty, "layout.duplicate-code", "Line codes must be unique."));
    if (lines.GroupBy(x => new { Section = x.Section.Trim().ToUpperInvariant(), x.Order }).Any(g => g.Count() > 1))
      errors.Add(new StatementLayoutValidationError(string.Empty, "layout.duplicate-order", "Line order must be unique within a section."));

    var byCode = new Dictionary<string, StatementLineDefinition>(StringComparer.Ordinal);
    foreach (var line in lines)
      byCode[line.LineCode.Trim().ToUpperInvariant()] = line;

    foreach (var line in lines)
    {
      var code = line.LineCode.Trim().ToUpperInvariant();
      if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(line.Label))
        errors.Add(new StatementLayoutValidationError(code, "line.missing-label", "Every line needs a code and a label."));
      if (!StatementLayoutSections.All.Contains(line.Section.Trim().ToUpperInvariant()))
        errors.Add(new StatementLayoutValidationError(code, "line.section", $"Section '{line.Section}' is not a supported statement section."));
      if (line.DisplaySign is not (StatementLayoutDisplaySigns.Signed or StatementLayoutDisplaySigns.Inverted or StatementLayoutDisplaySigns.Absolute))
        errors.Add(new StatementLayoutValidationError(code, "line.display-sign", $"Display sign '{line.DisplaySign}' is not supported."));

      switch (line.Kind)
      {
        case StatementLayoutLineKinds.MappedTaxonomyBalance:
          if (string.IsNullOrWhiteSpace(line.TaxonomyNodeCode))
            errors.Add(new StatementLayoutValidationError(code, "line.missing-taxonomy", "A mapped balance line needs a taxonomy node code."));
          break;
        case StatementLayoutLineKinds.SumChildLines:
          if (line.ChildLineCodes is null || line.ChildLineCodes.Count == 0)
            errors.Add(new StatementLayoutValidationError(code, "line.missing-children", "A subtotal line needs at least one child line."));
          else
            foreach (var child in line.ChildLineCodes)
              if (!byCode.ContainsKey(child.Trim().ToUpperInvariant()))
                errors.Add(new StatementLayoutValidationError(code, "line.unknown-child", $"Child line '{child}' does not exist in this layout."));
          break;
        case StatementLayoutLineKinds.TotalReferencedLines:
          ValidateReferences(code, line, byCode, errors);
          if (line.ReferencedLineCodes is { Count: > 0 } refs)
          {
            if (line.Operations is null || line.Operations.Count != refs.Count)
              errors.Add(new StatementLayoutValidationError(code, "line.operation-count",
                "Every referenced line needs exactly one add/subtract operation."));
            else if (line.Operations.Any(op => op.Trim().ToUpperInvariant() is not (StatementLayoutOperations.Add or StatementLayoutOperations.Subtract)))
              errors.Add(new StatementLayoutValidationError(code, "line.operation", "Operations must be ADD or SUBTRACT."));
          }
          break;
        case StatementLayoutLineKinds.Ratio:
          ValidateReferences(code, line, byCode, errors);
          if (string.IsNullOrWhiteSpace(line.DenominatorLineCode))
            errors.Add(new StatementLayoutValidationError(code, "line.missing-denominator", "A ratio line needs a denominator line."));
          else if (!byCode.ContainsKey(line.DenominatorLineCode.Trim().ToUpperInvariant()))
            errors.Add(new StatementLayoutValidationError(code, "line.unknown-denominator",
              $"Denominator line '{line.DenominatorLineCode}' does not exist in this layout."));
          break;
        case StatementLayoutLineKinds.Heading:
          break;
        default:
          errors.Add(new StatementLayoutValidationError(code, "line.kind", $"Line kind '{line.Kind}' is not supported."));
          break;
      }
    }

    if (errors.Count > 0) return errors;

    // Cycles: every numeric dependency edge must be acyclic.
    var state = new Dictionary<string, int>(StringComparer.Ordinal);
    bool Visit(string code)
    {
      if (state.GetValueOrDefault(code) == 1) return true;
      if (state.GetValueOrDefault(code) == 2) return false;
      state[code] = 1;
      var line = byCode[code];
      var dependencies = (line.Kind == StatementLayoutLineKinds.SumChildLines ? line.ChildLineCodes : line.ReferencedLineCodes)
        ?? [];
      foreach (var dependency in dependencies)
        if (Visit(dependency.Trim().ToUpperInvariant())) return true;
      if (line.Kind == StatementLayoutLineKinds.Ratio && line.DenominatorLineCode is { } denominator &&
          Visit(denominator.Trim().ToUpperInvariant())) return true;
      state[code] = 2;
      return false;
    }
    foreach (var code in byCode.Keys)
      if (Visit(code))
      {
        errors.Add(new StatementLayoutValidationError(code, "line.cycle", "The layout contains a reference cycle."));
        break;
      }
    if (errors.Count > 0) return errors;

    // Double counting: a mapped taxonomy balance must not reach the same total through
    // both a subtotal and one of that subtotal's own leaves.
    foreach (var line in lines.Where(x => x.Kind is StatementLayoutLineKinds.SumChildLines or StatementLayoutLineKinds.TotalReferencedLines))
    {
      var leaves = CollectLeaves(line, byCode);
      var duplicates = leaves.GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToArray();
      if (duplicates.Length > 0)
        errors.Add(new StatementLayoutValidationError(line.LineCode.Trim().ToUpperInvariant(), "line.double-count",
          $"Taxonomy balances are counted more than once: {string.Join(", ", duplicates)}."));
    }
    return errors;
  }

  /// <summary>Evaluates the layout against mapped taxonomy balances. Returns the canonical
  /// signed amount per line code; the display sign is presentation only and never changes
  /// the canonical value.</summary>
  public static IReadOnlyDictionary<string, decimal> Evaluate(
    IReadOnlyList<StatementLineDefinition> lines,
    IReadOnlyDictionary<string, decimal> taxonomyBalances)
  {
    var validation = Validate(lines);
    if (validation.Count > 0)
      throw new ArgumentException($"The layout is not evaluable: {validation[0].Code} {validation[0].Message}");
    ArgumentNullException.ThrowIfNull(taxonomyBalances);

    var byCode = lines.ToDictionary(x => x.LineCode.Trim().ToUpperInvariant(), StringComparer.Ordinal);
    var amounts = new Dictionary<string, decimal>(StringComparer.Ordinal);
    decimal Resolve(string code)
    {
      var trimmed = code.Trim().ToUpperInvariant();
      if (amounts.TryGetValue(trimmed, out var cached)) return cached;
      var line = byCode[trimmed];
      var value = line.Kind switch
      {
        StatementLayoutLineKinds.Heading => 0m,
        StatementLayoutLineKinds.MappedTaxonomyBalance =>
          taxonomyBalances.TryGetValue(line.TaxonomyNodeCode!.Trim().ToUpperInvariant(), out var mapped) ? mapped : 0m,
        StatementLayoutLineKinds.SumChildLines =>
          (line.ChildLineCodes ?? []).Sum(Resolve),
        StatementLayoutLineKinds.TotalReferencedLines =>
          (line.ReferencedLineCodes ?? []).Select((reference, index) =>
            (line.Operations![index].Trim().ToUpperInvariant() == StatementLayoutOperations.Subtract ? -1m : 1m) * Resolve(reference)).Sum(),
        StatementLayoutLineKinds.Ratio => ComputeRatio(line, Resolve),
        _ => 0m
      };
      value = Money(value);
      amounts[trimmed] = value;
      return value;
    }
    foreach (var code in byCode.Keys) Resolve(code);
    return amounts;
  }

  /// <summary>Applies the presentation sign without changing the canonical amount.</summary>
  public static decimal ToDisplayAmount(decimal canonicalAmount, string displaySign) => displaySign switch
  {
    StatementLayoutDisplaySigns.Inverted => -canonicalAmount,
    StatementLayoutDisplaySigns.Absolute => canonicalAmount < 0m ? -canonicalAmount : canonicalAmount,
    _ => canonicalAmount
  };

  private static decimal ComputeRatio(StatementLineDefinition line, Func<string, decimal> resolve)
  {
    var numerator = (line.ReferencedLineCodes ?? []).Count == 0 ? 0m : resolve(line.ReferencedLineCodes![0]);
    var denominator = resolve(line.DenominatorLineCode!);
    if (denominator == 0m)
      return line.ZeroDenominatorYieldsZero ? 0m
        : throw new InvalidOperationException($"Ratio line '{line.LineCode}' has a zero denominator.");
    return numerator / denominator;
  }

  private static void ValidateReferences(
    string code, StatementLineDefinition line,
    IReadOnlyDictionary<string, StatementLineDefinition> byCode,
    List<StatementLayoutValidationError> errors)
  {
    if (line.ReferencedLineCodes is null || line.ReferencedLineCodes.Count == 0)
    {
      errors.Add(new StatementLayoutValidationError(code, "line.missing-references", "A referencing line needs at least one referenced line."));
      return;
    }
    foreach (var reference in line.ReferencedLineCodes)
      if (!byCode.ContainsKey(reference.Trim().ToUpperInvariant()))
        errors.Add(new StatementLayoutValidationError(code, "line.unknown-reference", $"Referenced line '{reference}' does not exist in this layout."));
  }

  private static List<string> CollectLeaves(
    StatementLineDefinition line, IReadOnlyDictionary<string, StatementLineDefinition> byCode)
  {
    var leaves = new List<string>();
    void Walk(string code)
    {
      var current = byCode[code.Trim().ToUpperInvariant()];
      if (current.Kind == StatementLayoutLineKinds.MappedTaxonomyBalance)
      {
        leaves.Add(current.TaxonomyNodeCode!.Trim().ToUpperInvariant());
        return;
      }
      var next = current.Kind == StatementLayoutLineKinds.SumChildLines ? current.ChildLineCodes
        : current.Kind == StatementLayoutLineKinds.TotalReferencedLines ? current.ReferencedLineCodes
        : null;
      foreach (var child in next ?? [])
      {
        if (byCode.TryGetValue(child.Trim().ToUpperInvariant(), out var childLine) &&
            childLine.Kind == StatementLayoutLineKinds.Heading) continue;
        Walk(child);
      }
    }
    var start = line.Kind == StatementLayoutLineKinds.SumChildLines ? line.ChildLineCodes : line.ReferencedLineCodes;
    foreach (var child in start ?? []) Walk(child);
    return leaves;
  }

  private static decimal Money(decimal value) => decimal.Round(value, 6, MidpointRounding.ToEven);
}

public static class StatementLayoutStatuses
{
  public const string Draft = "DRAFT";
  public const string Validated = "VALIDATED";
  public const string Published = "PUBLISHED";
}

/// <summary>An independently versioned statement-layout definition. Drafts may be
/// validated freely; a published version is immutable and changes require a successor.</summary>
public sealed class StatementLayoutVersion
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid ClientId { get; set; }
  /// <summary>Framework policy the layout was authored against, e.g. IFRS-2026.</summary>
  public string FrameworkPolicy { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public int VersionNumber { get; set; } = 1;
  public string Status { get; set; } = StatementLayoutStatuses.Draft;
  /// <summary>Digest over the ordered line definitions of this version.</summary>
  public string LayoutHash { get; set; } = string.Empty;
  public int LineCount { get; set; }
  public string? ValidationSummary { get; set; }
  public Guid? SupersedesLayoutId { get; set; }
  public string? PublishReason { get; set; }
  public Guid CreatedByUserId { get; set; }
  public Guid? PublishedByUserId { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
  public DateTimeOffset? PublishedAt { get; set; }
}

/// <summary>One typed line of a statement-layout version. Shape is validated by the
/// pure layout engine before publication.</summary>
public sealed class StatementLayoutLine
{
  public Guid Id { get; set; }
  public Guid FirmId { get; set; }
  public Guid LayoutVersionId { get; set; }
  public string LineCode { get; set; } = string.Empty;
  public string Label { get; set; } = string.Empty;
  public string LineKind { get; set; } = string.Empty;
  public string Section { get; set; } = string.Empty;
  public int LineOrder { get; set; }
  public string DisplaySign { get; set; } = StatementLayoutDisplaySigns.Signed;
  public string? TaxonomyNodeCode { get; set; }
  /// <summary>JSON array of child line codes for a subtotal line.</summary>
  public string? ChildLineCodesJson { get; set; }
  /// <summary>JSON array of referenced line codes for a total or ratio line.</summary>
  public string? ReferencedLineCodesJson { get; set; }
  /// <summary>JSON array of ADD/SUBTRACT operations aligned to the referenced codes.</summary>
  public string? OperationsJson { get; set; }
  public string? DenominatorLineCode { get; set; }
  public bool ZeroDenominatorYieldsZero { get; set; }
  public bool IsSubtotal { get; set; }
  public DateTimeOffset CreatedAt { get; set; }
}
