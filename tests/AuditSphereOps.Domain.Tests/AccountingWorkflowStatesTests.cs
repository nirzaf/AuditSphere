using AuditSphereOps.Domain.Accounting;

namespace AuditSphereOps.Domain.Tests;

public sealed class AccountingWorkflowStatesTests
{
  [Theory]
  [InlineData(AccountingWorkflowStates.Draft, true)]
  [InlineData(AccountingWorkflowStates.Active, true)]
  [InlineData(AccountingWorkflowStates.Closed, false)]
  [InlineData(AccountingWorkflowStates.Retired, false)]
  [InlineData("UNKNOWN", false)]
  [InlineData(null, false)]
  public void IsOpenReportingPeriodStatus_AcceptsOnlyDraftAndActive(string? status, bool expected)
  {
    Assert.Equal(expected, AccountingWorkflowStates.IsOpenReportingPeriodStatus(status));
  }
}
