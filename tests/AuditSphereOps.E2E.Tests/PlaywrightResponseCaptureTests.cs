using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

public sealed class PlaywrightResponseCaptureTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task UnmatchedRequestsRetainOlderRoutesWhileCapturingTheExactResponse(bool multiple)
  {
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseUrls("http://127.0.0.1:0");
    builder.Logging.ClearProviders();
    await using var app = builder.Build();
    app.MapGet("/", () => "synthetic capture test");
    app.MapGet("/older", () => "network bypassed the older handler");
    var targetRequests = 0;
    app.MapGet("/captured", () => { Interlocked.Increment(ref targetRequests); return "exact captured response"; });
    await app.StartAsync();
    try
    {
      using var playwright = await Playwright.CreateAsync();
      await using var browser = await PlaywrightBrowser.LaunchAsync(playwright);
      await using var context = await browser.NewContextAsync();
      var page = await context.NewPageAsync();
      var origin = app.Urls.Single();
      await page.GotoAsync(origin);
      var olderRouteCalls = 0;
      await page.RouteAsync("**/older", async route =>
      {
        Interlocked.Increment(ref olderRouteCalls);
        await route.FulfillAsync(new() { Status = 200, ContentType = "text/plain", Body = "older handler preserved" });
      });
      async Task ActAsync()
      {
        // This request must reach the earlier route while the capture route is still installed.
        Assert.Equal("older handler preserved", await page.EvaluateAsync<string>("async () => (await fetch('/older')).text()"));
        Assert.Equal("exact captured response", await page.EvaluateAsync<string>("async () => (await fetch('/captured')).text()"));
      }
      bool Matches(IRequest request) => request.Method == "GET" && request.Url == origin + "/captured";
      var response = multiple
        ? Assert.Single(await PlaywrightResponseCapture.RunMultipleAsync(page, [Matches], ActAsync))
        : await PlaywrightResponseCapture.RunAsync(page, Matches, ActAsync);
      Assert.Equal(200, response.Status);
      Assert.Equal("exact captured response", response.Body);
      Assert.Equal(1, olderRouteCalls);
      Assert.Equal(1, targetRequests);
    }
    finally
    {
      await app.StopAsync();
    }
  }
}
