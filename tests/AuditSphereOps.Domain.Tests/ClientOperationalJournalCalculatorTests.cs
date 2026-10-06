using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientOperationalJournalCalculatorTests
{
  [Fact]
  public void PreservesExactSixDecimalAmountsAndMaximumStorageValue()
  {
    var maximum = ClientOperationalJournalCalculator.MaximumLineAmount;
    var result = ClientOperationalJournalCalculator.Calculate([new("A", "", maximum, 0m), new("B", "", 0m, maximum)]);
    Assert.True(result.Valid);
    Assert.Equal(maximum, result.TotalDebit);
    Assert.Equal(maximum, result.TotalCredit);
    Assert.Equal(0m, result.Imbalance);
    Assert.True(ClientOperationalJournalCalculator.Calculate([new("A", "", .000001m, 0m), new("B", "", 0m, .000001m)]).Valid);
  }

  [Theory]
  [InlineData("10000000000000")]
  [InlineData("79228162514264337593543950335")]
  [InlineData("-1")]
  [InlineData("0.0000001")]
  public void RejectsUnsupportedAmountsWithoutOverflowOrSilentRounding(string text)
  {
    var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
    var result = ClientOperationalJournalCalculator.Calculate([new("A", "", value, 0m), new("B", "", 0m, value)]);
    Assert.False(result.Valid);
  }

  [Fact]
  public void RejectsZeroBothSidesUnbalancedAndInvalidLineCounts()
  {
    Assert.False(ClientOperationalJournalCalculator.Calculate(null).Valid);
    Assert.False(ClientOperationalJournalCalculator.Calculate([new("A", "", 1m, 0m)]).Valid);
    Assert.False(ClientOperationalJournalCalculator.Calculate([new("A", "", 0m, 0m), new("B", "", 0m, 1m)]).Valid);
    Assert.False(ClientOperationalJournalCalculator.Calculate([new("A", "", 1m, 1m), new("B", "", 0m, 1m)]).Valid);
    var unequal = ClientOperationalJournalCalculator.Calculate([new("A", "", 1m, 0m), new("B", "", 0m, .999999m)]);
    Assert.False(unequal.Valid);
    Assert.Equal(.000001m, unequal.Imbalance);
    Assert.False(ClientOperationalJournalCalculator.Calculate(Enumerable.Repeat(new ClientOperationalJournalLineInput("A", "", 1m, 0m), 101).ToArray()).Valid);
  }
}
