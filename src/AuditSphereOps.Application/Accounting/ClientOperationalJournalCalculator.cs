namespace AuditSphereOps.Application.Accounting;

public sealed record ClientOperationalJournalCalculation(bool Valid, decimal TotalDebit, decimal TotalCredit,
  decimal Imbalance, string? Error);

/// <summary>Exact native journal rules shared by preparation and posting. No currency rounding is inferred.</summary>
public static class ClientOperationalJournalCalculator
{
  public const decimal MaximumLineAmount = ClientNativeAmountRules.MaximumStoredAmount;

  public static ClientOperationalJournalCalculation Calculate(IReadOnlyList<ClientOperationalJournalLineInput>? lines)
  {
    if (lines is null || lines.Count is < 2 or > 100)
      return new(false, 0m, 0m, 0m, "A journal needs 2 to 100 substantive lines.");
    decimal debit = 0m, credit = 0m;
    foreach (var line in lines)
    {
      if (line is null || !ValidAmount(line.Debit) || !ValidAmount(line.Credit) ||
          (line.Debit == 0m) == (line.Credit == 0m))
        return new(false, 0m, 0m, 0m, "Each line needs one positive debit or credit within numeric(19,6), without rounding.");
      // At most 100 bounded lines cannot overflow decimal.
      debit += line.Debit;
      credit += line.Credit;
    }
    return new(debit == credit, debit, credit, debit - credit,
      debit == credit ? null : "Exact total debits must equal total credits.");
  }

  public static bool ValidAmount(decimal amount) => ClientNativeAmountRules.ValidStoredAmount(amount);
}
