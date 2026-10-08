using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Domain.Tests;

public sealed class FirmReceivablesAgingPolicyTests
{
  private static readonly DateOnly AsOf = new(2026, 10, 6);

  [Theory]
  [InlineData(1, FirmReceivablesAgingPolicy.Overdue1To30)]
  [InlineData(30, FirmReceivablesAgingPolicy.Overdue1To30)]
  [InlineData(31, FirmReceivablesAgingPolicy.Overdue31To60)]
  [InlineData(60, FirmReceivablesAgingPolicy.Overdue31To60)]
  [InlineData(61, FirmReceivablesAgingPolicy.Overdue61To90)]
  [InlineData(90, FirmReceivablesAgingPolicy.Overdue61To90)]
  [InlineData(91, FirmReceivablesAgingPolicy.OverdueOver90)]
  public void OverdueBoundariesUseInclusiveCalendarDayBands(int days, string expected)
  {
    var due = AsOf.AddDays(-days);
    Assert.Equal(days, FirmReceivablesAgingPolicy.DaysOverdue(due, AsOf));
    Assert.Equal(expected, FirmReceivablesAgingPolicy.Bucket(due, AsOf, 1m));
  }

  [Fact]
  public void DueOnAsOfDateAndFutureDueDatesAreCurrentAndNotOverdue()
  {
    Assert.Equal(0, FirmReceivablesAgingPolicy.DaysOverdue(AsOf, AsOf));
    Assert.Equal(FirmReceivablesAgingPolicy.Current, FirmReceivablesAgingPolicy.Bucket(AsOf, AsOf, 1m));
    Assert.Equal(0, FirmReceivablesAgingPolicy.DaysOverdue(AsOf.AddDays(12), AsOf));
    Assert.Equal(FirmReceivablesAgingPolicy.Current, FirmReceivablesAgingPolicy.Bucket(AsOf.AddDays(12), AsOf, 1m));
  }

  [Fact]
  public void UndatedReceivableAndZeroBalanceAreExplicit()
  {
    Assert.Null(FirmReceivablesAgingPolicy.DaysOverdue(null, AsOf));
    Assert.Equal(FirmReceivablesAgingPolicy.Undated, FirmReceivablesAgingPolicy.Bucket(null, AsOf, 20m));
    Assert.Equal(FirmReceivablesAgingPolicy.Settled, FirmReceivablesAgingPolicy.Bucket(AsOf.AddDays(-200), AsOf, 0m));
  }
}
