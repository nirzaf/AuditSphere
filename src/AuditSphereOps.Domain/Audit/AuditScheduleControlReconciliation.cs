namespace AuditSphereOps.Domain.Audit;

public sealed record AuditScheduleAccountAmount(string AccountCode, decimal SignedAmount);

public sealed record AuditScheduleAccountTieOut(
  string AccountCode, decimal ListingAmount, decimal TrialBalanceAmount, decimal Difference,
  int ListingRowCount, int TrialBalanceRowCount);

/// <summary>
/// Exact closing-listing tie-outs for an explicitly selected account scope. Selection and source
/// acceptance belong to the caller; a zero difference does not establish professional completeness.
/// </summary>
public static class AuditScheduleControlReconciliation
{
  private const decimal MaxAmount = 9_999_999_999_999.999999m;

  public static IReadOnlyList<AuditScheduleAccountTieOut> Reconcile(
    IReadOnlyList<string> selectedAccountCodes,
    IReadOnlyList<AuditScheduleAccountAmount> listingRows,
    IReadOnlyList<AuditScheduleAccountAmount> trialBalanceRows)
  {
    ArgumentNullException.ThrowIfNull(selectedAccountCodes);
    ArgumentNullException.ThrowIfNull(listingRows);
    ArgumentNullException.ThrowIfNull(trialBalanceRows);
    if (selectedAccountCodes.Count is < 1 or > 20_000 || listingRows.Count is < 1 or > 20_000)
      throw new ArgumentException("Select accounts and supply a nonempty bounded listing.");

    var selected = new HashSet<string>(StringComparer.Ordinal);
    foreach (var code in selectedAccountCodes)
    {
      ValidateCode(code);
      if (!selected.Add(code)) throw new ArgumentException("Selected accounts must be distinct.");
    }

    var listing = new Dictionary<string, (decimal Amount, int Count)>(StringComparer.Ordinal);
    var controls = new Dictionary<string, (decimal Amount, int Count)>(StringComparer.Ordinal);
    foreach (var row in listingRows)
    {
      ArgumentNullException.ThrowIfNull(row);
      ValidateCode(row.AccountCode);
      if (!selected.Contains(row.AccountCode))
        throw new ArgumentException("Every listing row must belong to the selected account scope.");
      Add(listing, row);
    }
    foreach (var row in trialBalanceRows)
    {
      ArgumentNullException.ThrowIfNull(row);
      ValidateCode(row.AccountCode);
      // The caller may pass the complete exact-context TB, including unrelated accounts.
      if (selected.Contains(row.AccountCode)) Add(controls, row);
    }

    var result = new List<AuditScheduleAccountTieOut>(selected.Count);
    foreach (var code in selected.Order(StringComparer.Ordinal))
    {
      if (!controls.TryGetValue(code, out var control))
        throw new ArgumentException($"Selected account {code} has no trial-balance source row.");
      var actual = listing.GetValueOrDefault(code);
      ValidateAmount(actual.Amount);
      ValidateAmount(control.Amount);
      result.Add(new(code, actual.Amount, control.Amount, actual.Amount - control.Amount,
        actual.Count, control.Count));
    }
    return result.AsReadOnly();
  }

  private static void Add(Dictionary<string, (decimal Amount, int Count)> totals, AuditScheduleAccountAmount row)
  {
    ValidateAmount(row.SignedAmount);
    var previous = totals.GetValueOrDefault(row.AccountCode);
    // At most 20,000 listing rows, each numeric(19,6), fit decimal without intermediate rounding.
    totals[row.AccountCode] = (checked(previous.Amount + row.SignedAmount), checked(previous.Count + 1));
  }

  private static void ValidateCode(string code)
  {
    if (string.IsNullOrWhiteSpace(code) || code.Length > 100 || code != code.Trim() || code.Contains('\0'))
      throw new ArgumentException("An exact nonempty account code of at most 100 characters is required.");
  }

  private static void ValidateAmount(decimal amount)
  {
    if (amount is < -MaxAmount or > MaxAmount || decimal.Round(amount, 6) != amount)
      throw new ArgumentException("Account amounts must be exactly representable as numeric(19,6).");
  }
}
