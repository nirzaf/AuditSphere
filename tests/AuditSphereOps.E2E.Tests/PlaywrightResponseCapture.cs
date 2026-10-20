using System.Collections.Concurrent;
using Microsoft.Playwright;

namespace AuditSphereOps.E2E.Tests;

internal static class PlaywrightResponseCapture
{
  public static async Task<IReadOnlyList<(int Status, string Body)>> RunMultipleAsync(
    IPage page, IReadOnlyList<Func<IRequest, bool>> matchers, Func<Task> action)
  {
    ArgumentNullException.ThrowIfNull(page);
    ArgumentNullException.ThrowIfNull(matchers);
    ArgumentNullException.ThrowIfNull(action);
    if (matchers.Count == 0)
      throw new ArgumentException("At least one response matcher is required.", nameof(matchers));

    var captured = new ConcurrentDictionary<int, (int Status, string Body)>();
    var allCaptured = new TaskCompletionSource<IReadOnlyList<(int Status, string Body)>>(
      TaskCreationOptions.RunContinuationsAsynchronously);
    var observed = new ConcurrentQueue<string>();

    async Task OnRoute(IRoute route)
    {
      observed.Enqueue($"{route.Request.Method} {route.Request.Url}");
      var matcherIndex = -1;
      for (var index = 0; index < matchers.Count; index++)
      {
        if (matchers[index](route.Request))
        {
          matcherIndex = index;
          break;
        }
      }

      if (matcherIndex < 0 || captured.ContainsKey(matcherIndex))
      {
        await route.ContinueAsync();
        return;
      }

      try
      {
        var response = await route.FetchAsync();
        var body = await response.TextAsync();
        if (captured.TryAdd(matcherIndex, (response.Status, body)) && captured.Count == matchers.Count)
          allCaptured.TrySetResult(Enumerable.Range(0, matchers.Count).Select(index => captured[index]).ToArray());
        await route.FulfillAsync(new() { Response = response });
      }
      catch (Exception exception)
      {
        allCaptured.TrySetException(exception);
        await route.AbortAsync();
      }
    }

    await page.RouteAsync("**/*", OnRoute);
    try
    {
      await action();
      try { return await allCaptured.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
      catch (TimeoutException exception)
      {
        throw new TimeoutException($"Not all matching API responses were captured. Observed: {string.Join("; ", observed)}", exception);
      }
    }
    finally
    {
      await page.UnrouteAsync("**/*", OnRoute);
    }
  }

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
