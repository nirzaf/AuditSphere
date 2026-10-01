using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Practice;

public sealed record ContractTimeValue(int Minutes, decimal? CapturedRate, string? Currency);
public sealed record ContractContribution(decimal LifetimeStandardValue, decimal FeeLessStandardValue);

/// <summary>Contracted fee less lifetime approved-time standard value. This is not actual staff-cost profit.</summary>
public static class ContractContributionCalculator
{
  public static ContractContribution? Compute(decimal fee, string currency, IReadOnlyList<ContractTimeValue> approvedTime)
  {
    if (fee < 0 || string.IsNullOrWhiteSpace(currency) || approvedTime.Any(x => x.Minutes < 0 || x.CapturedRate is null or < 0 ||
        !string.Equals(x.Currency, currency, StringComparison.Ordinal))) return null;
    var value = MoneyPolicy.Normalize(approvedTime.Sum(x => x.Minutes * x.CapturedRate!.Value / 60m));
    return new(value, MoneyPolicy.Normalize(fee - value));
  }
}
