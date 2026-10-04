using System.Net;

namespace AuditSphereOps.Api.Tests;

public sealed class RequestCorrelationTests
{
  [Fact]
  public async Task ResponsesUseDistinctServerOwnedCorrelationIdsIncludingFailures()
  {
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["Application:AllowSimulationAdapters"] = "false",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    using var firstRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
    firstRequest.Headers.Add("X-Correlation-Id", "caller-controlled");
    using var first = await client.SendAsync(firstRequest);
    using var second = await client.GetAsync("/__missing__");

    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);
    var firstId = Assert.Single(first.Headers.GetValues("X-Correlation-Id"));
    var secondId = Assert.Single(second.Headers.GetValues("X-Correlation-Id"));
    Assert.Matches("^[0-9a-f]{32}$", firstId);
    Assert.Matches("^[0-9a-f]{32}$", secondId);
    Assert.NotEqual(firstId, secondId);
    Assert.NotEqual("caller-controlled", firstId);
  }
}
