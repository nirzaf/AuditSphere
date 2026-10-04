namespace AuditSphereOps.Api.Tests;

public sealed class TelemetryConfigurationTests
{
  [Fact]
  public void ConfiguredInvalidOtlpEndpointRefusesApiStartup()
  {
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["Telemetry:Otlp:Endpoint"] = "ftp://collector.invalid/otlp?token=fixture-secret",
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    });

    var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    Assert.Contains("Telemetry:Otlp:Endpoint", error.ToString());
    Assert.DoesNotContain("fixture-secret", error.ToString());
  }
}
