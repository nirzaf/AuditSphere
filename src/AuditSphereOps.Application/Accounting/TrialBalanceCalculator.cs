// Accounting application service: TB import → mapping completeness → journals → package (§§16–18).
// Pure calculation first (NT-09/NT-10); persistence wiring lands with the DbContext.
namespace AuditSphereOps.Application.Accounting;

using AuditSphereOps.Domain.Shared;

/// <summary>Trial-balance import row (parsed, pre-persistence).</summary>
public sealed record TbImportRow(string AccountCode, string AccountName, decimal Amount, string Currency, string Entity, string? MappingCode);

/// <summary>Parsed TB file: balanced flag + control totals per Appendix D.2.</summary>
public sealed record ParsedTrialBalance(IReadOnlyList<TbImportRow> Rows, bool Balanced, decimal SignedSum, decimal TotalDebits, decimal TotalCreditsAbs);

/// <summary>Expected Appendix D control totals for the 14-account fixture (NT-09).</summary>
public static class TrialBalanceCalculator
{
  public static ParsedTrialBalance Parse(IEnumerable<TbImportRow> rows)
  {
    var list = rows.ToList();
    if (list.Count == 0)
      throw new InvalidOperationException("Empty trial balance cannot be processed.");
    var signed = 0m;
    var debits = 0m;
    var credits = 0m;
    foreach (var r in list)
    {
      var amt = MoneyPolicy.Normalize(r.Amount);
      signed += amt;
      if (amt >= 0) debits += amt; else credits += -amt;
    }
    signed = MoneyPolicy.Normalize(signed);
    return new ParsedTrialBalance(list, signed == 0m, signed, MoneyPolicy.Normalize(debits), MoneyPolicy.Normalize(credits));
  }

  /// <summary>Apply one balanced journal to adjusted amounts; throws on unbalanced journal.</summary>
  public static IReadOnlyDictionary<string, decimal> ApplyJournal(
    IReadOnlyDictionary<string, decimal> balances,
    IEnumerable<(string AccountCode, decimal Debit, decimal Credit)> lines)
  {
    var debitTotal = 0m;
    var creditTotal = 0m;
    var result = new Dictionary<string, decimal>(balances, StringComparer.Ordinal);
    foreach (var (code, debit, credit) in lines)
    {
      debitTotal += MoneyPolicy.Normalize(debit);
      creditTotal += MoneyPolicy.Normalize(credit);
      var delta = MoneyPolicy.Normalize(MoneyPolicy.Normalize(debit) - MoneyPolicy.Normalize(credit));
      result.TryGetValue(code, out var current);
      result[code] = MoneyPolicy.Normalize(current + delta);
    }
    if (MoneyPolicy.Normalize(debitTotal) != MoneyPolicy.Normalize(creditTotal))
      throw new InvalidOperationException("Unbalanced journal cannot be posted.");
    return result;
  }
}
