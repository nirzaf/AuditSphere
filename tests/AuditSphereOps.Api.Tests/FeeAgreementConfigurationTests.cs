using AuditSphereOps.Api.Ui;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

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

  // STE-GAP-002: the isolated worker enforces AutomaticFeeInvoices:Enabled while the API host reports it to the fee
  // workspace. Each host reads its own appsettings file, so automation could be switched on in the worker while the
  // UI still said "automation disabled". This pins the section in every host and refuses a drifted value.
  [Fact]
  public void EveryHostDeclaresTheSameAutomaticFeeInvoicesPolicy()
  {
    static (bool Enabled, string FinanceUserId, string ApprovingAdministratorId) Read(string path)
    {
      using var document = JsonDocument.Parse(File.ReadAllText(path));
      Assert.True(document.RootElement.TryGetProperty("AutomaticFeeInvoices", out var section),
        $"{path} must declare AutomaticFeeInvoices so the reported state matches the worker that enforces it.");
      return (section.GetProperty("Enabled").GetBoolean(),
        section.GetProperty("FinanceUserId").GetString() ?? string.Empty,
        section.GetProperty("ApprovingAdministratorId").GetString() ?? string.Empty);
    }

    var root = RepositoryRoot();
    var worker = Read(Path.Combine(root, "src", "AuditSphereOps.Worker", "appsettings.json"));
    Assert.Equal(worker, Read(Path.Combine(root, "src", "AuditSphereOps.Api", "appsettings.json")));
    Assert.Equal(worker, Read(Path.Combine(root, "src", "AuditSphereOps.Web", "appsettings.json")));

    // The reporting host must read the declared value, not fall back to the absent-key default.
    var settings = new Dictionary<string, string?> { ["AutomaticFeeInvoices:Enabled"] = worker.Enabled ? "true" : "false" };
    Assert.Equal(worker.Enabled,
      UiEndpoints.AutomaticDraftingEnabled(new ConfigurationBuilder().AddInMemoryCollection(settings).Build()));
  }

  private static string RepositoryRoot()
  {
    var root = new DirectoryInfo(AppContext.BaseDirectory);
    while (root is not null && !File.Exists(Path.Combine(root.FullName, "AuditSphereOps.slnx"))) root = root.Parent;
    return root?.FullName ?? throw new InvalidOperationException("Repository root is required for the host configuration check.");
  }
}
