using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

internal static class PlaywrightResponseCapture
{
  public static async Task<(int Status, string Body)> RunAsync(
    IPage page, Func<IRequest, bool> matches, Func<Task> action)
  {
    var captured = new TaskCompletionSource<(int Status, string Body)>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    var observed = new ConcurrentQueue<string>();

    async Task OnRoute(IRoute route)
    {
      observed.Enqueue($"{route.Request.Method} {route.Request.Url}");
      if (!matches(route.Request) || captured.Task.IsCompleted)
      {
        await route.ContinueAsync();
        return;
      }
      try
      {
        var response = await route.FetchAsync();
        var body = await response.TextAsync();
        captured.TrySetResult((response.Status, body));
        await route.FulfillAsync(new() { Response = response });
      }
      catch (Exception exception)
      {
        captured.TrySetException(exception);
        await route.AbortAsync();
      }
    }

    await page.RouteAsync("**/*", OnRoute);
    try
    {
      await action();
      try { return await captured.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
      catch (TimeoutException exception)
      {
        throw new TimeoutException($"No matching API response. Observed: {string.Join("; ", observed)}", exception);
      }
    }
    finally
    {
      await page.UnrouteAsync("**/*", OnRoute);
    }
  }
}
