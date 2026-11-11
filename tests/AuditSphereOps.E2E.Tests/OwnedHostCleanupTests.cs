namespace AuditSphereOps.E2E.Tests;

public sealed class OwnedHostCleanupTests
{
  [Theory]
  [InlineData(false, false, false)]
  [InlineData(true, false, false)]
  [InlineData(true, true, false)]
  [InlineData(false, false, true)]
  public async Task CleanupAttemptsEveryOwnedActionInOrderAndRetainsFailures(bool failWait, bool failDispose, bool failDirectory)
  {
    List<string> attempts = [];
    var timeout = new TimeoutException("Synthetic owned process wait timeout.");
    var disposeFailure = new InvalidOperationException("Synthetic owned process dispose failure.");
    var directoryFailure = new IOException("Synthetic owned staging directory failure.");
    Action Action(string name, Exception? failure = null) => () =>
    {
      attempts.Add(name);
      if (failure is not null) throw failure;
    };
    Func<Task> AsyncAction(string name, Exception? failure = null) => async () =>
    {
      Action(name)();
      await Task.CompletedTask;
      if (failure is not null) throw failure;
    };
    var first = new OwnedHost.ProcessCleanup(Action("first.before"), Action("first.stop"),
      AsyncAction("first.wait"), Action("first.after"), Action("first.dispose"));
    var last = new OwnedHost.ProcessCleanup(Action("last.before"), Action("last.stop"),
      AsyncAction("last.wait", failWait ? timeout : null), Action("last.after"),
      Action("last.dispose", failDispose ? disposeFailure : null));

    var error = await Record.ExceptionAsync(() => OwnedHost.CleanupOwnedResourcesAsync([first, last],
      AsyncAction("database"), Action("staging", failDirectory ? directoryFailure : null), Action("provider"), Action("checkpoints")));

    Assert.Equal(new[] { "last.before", "last.stop", "last.wait", "last.after", "last.dispose",
      "first.before", "first.stop", "first.wait", "first.after", "first.dispose",
      "database", "staging", "provider", "checkpoints" }, attempts);
    if (failDispose)
    {
      var aggregate = Assert.IsType<AggregateException>(error);
      Assert.Equal(2, aggregate.InnerExceptions.Count);
      Assert.Same(timeout, aggregate.InnerExceptions[0]);
      Assert.Same(disposeFailure, aggregate.InnerExceptions[1]);
    }
    else if (failWait) Assert.Same(timeout, error);
    else if (failDirectory) Assert.Same(directoryFailure, error);
    else Assert.Null(error);
  }
}
