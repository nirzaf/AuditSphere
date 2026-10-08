using AuditSphereOps.Api.Ui;
using Microsoft.Extensions.Configuration;

namespace AuditSphereOps.Api.Tests;

public sealed class FeeAgreementConfigurationTests
{
  // STE-NXT-006 criterion 3: AutomaticFeeInvoices:Enabled=false must reach the fee workspace as "off". The domain
  // maps "off" to PENDING_AUTOMATION_DISABLED (CommercialWorkflowTests). Absent means off, so the default fails closed.
  [Theory]
  [InlineData(null, false)]
  [InlineData("false", false)]
  [InlineData("true", true)]
  public void AutomaticFeeInvoicesFlagIsReadAndFailsClosed(string? value, bool expected)
  {
    var settings = new Dictionary<string, string?>();
    if (value is not null) settings["AutomaticFeeInvoices:Enabled"] = value;
    var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    Assert.Equal(expected, UiEndpoints.AutomaticDraftingEnabled(configuration));
  }
}
