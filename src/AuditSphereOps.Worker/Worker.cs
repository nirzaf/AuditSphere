using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;

namespace AuditSphereOps.Worker;

// Durable outbox + claim/lease/fence (ND-03, §29). Operation kinds are typed by name; only
// claimed operations are executed, and retry is idempotent by (firm, idempotency_key).
public sealed class Worker(
  OperationDispatcher dispatcher,
  IEnumerable<IPendingOperationDiscovery> discoveries,
  ILogger<Worker> logger) : BackgroundService
{
  public async Task<int> DrainAsync(CancellationToken ct = default)
  {
    var count = 0;
    while (await ProcessNextAsync(ct)) count++;
    return count;
  }

  /// <summary>
  /// Enqueues every registered slice's pending durable work, then claims and processes one
  /// queued operation; returns false when nothing was claimed.
  /// </summary>
  public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
  {
    foreach (var discovery in discoveries)
      await discovery.EnqueuePendingAsync(ct);
    return await dispatcher.ProcessNextAsync(ct);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        if (!await ProcessNextAsync(stoppingToken))
          await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
      catch (Exception)
      {
        logger.LogError("Operation polling failed. Durable state will be reconciled on retry.");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
      }
    }
  }
}
