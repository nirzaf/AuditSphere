using AuditSphereOps.Application.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed class TelemetryEndpointTests
{
  [Fact]
  public void UnsetEndpointIsAllowedAndHttpCollectorsAreAccepted()
  {
    Assert.Null(TelemetryEndpoint.Parse(null));
    Assert.Equal("http://collector.internal:4317/",
      TelemetryEndpoint.Parse("http://collector.internal:4317")!.AbsoluteUri);
    Assert.Equal("https://collector.example/otlp",
      TelemetryEndpoint.Parse("https://collector.example/otlp")!.AbsoluteUri);
  }

  [Theory]
  [InlineData("")]
  [InlineData("collector.internal:4317")]
  [InlineData("ftp://collector.internal/otlp")]
  [InlineData("https://user:secret@collector.internal/otlp")]
  [InlineData("https://collector.internal/otlp?token=secret")]
  public void ConfiguredInvalidEndpointFailsWithoutRepeatingItsValue(string configured)
  {
    var error = Assert.Throws<InvalidOperationException>(() => TelemetryEndpoint.Parse(configured));
    Assert.Contains("Telemetry:Otlp:Endpoint", error.Message);
    Assert.DoesNotContain("secret", error.Message, StringComparison.OrdinalIgnoreCase);
  }
}
