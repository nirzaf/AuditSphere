using AuditSphereOps.Domain.Completion;
using Microsoft.Extensions.Logging;

namespace AuditSphereOps.Application.Operations;

public sealed class OperationDispatcher(IOperationStore store, DurableOperationRegistry registry, WorkerOptions options,
  ILogger<OperationDispatcher>? logger = null)
{
  private readonly string owner = Guid.NewGuid().ToString("N");

  public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();
    await store.ReapAsync(options, ct);
    var op = await store.ClaimAsync(options, registry.Definitions, owner, true, ct) ??
      await store.ClaimAsync(options, registry.Definitions, owner, false, ct);
    if (op is null) return false;
    logger?.LogInformation("Claimed operation {OperationId}, token {AttemptToken}, correlation {CorrelationId}",
      op.Id, op.AttemptToken, op.CorrelationId);
    var handler = registry.Resolve(op.OperationKind);
    using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
    using var renewal = new CancellationTokenSource();
    var renewer = RenewAsync(op, work, renewal.Token);
    try
    {
      if (!await store.ValidateAsync(op, options, handler, work.Token)) throw new OperationOwnershipLostException();
      OperationResult? remote = null;
      if (op.ExecutionMode != OperationMode.LOCAL)
      {
        if (op.IsReconciliation)
          remote = await handler.ReconcileAsync(op, work.Token);
        else
        {
          if (!await store.TransitionAsync(op, options, OperationState.REMOTE_STARTED, null, null, work.Token))
            throw new OperationOwnershipLostException();
          remote = await handler.ExecuteEffectAsync(op, work.Token);
          if (!await store.TransitionAsync(op, options, OperationState.VERIFYING, null, null, work.Token))
            throw new OperationOwnershipLostException();
        }
      }
      if (!await store.CompleteAsync(op, options, handler, remote, work.Token))
        throw new OperationOwnershipLostException();
    }
    catch (OperationOwnershipLostException) { /* Recovery belongs to the current owner/reaper. */ }
    catch (SafeRetryException ex)
    {
      var next = op.IsReconciliation ? OperationState.PROVIDER_BLOCKED :
        op.AttemptCount >= options.MaxAttempts ? OperationState.DEAD_LETTER : OperationState.RETRY_WAIT;
      var jittered = Math.Min(300, 5 * Math.Pow(2, Math.Min(op.AttemptCount - 1, 6)) * (1 + Random.Shared.NextDouble() * 0.2));
      var delay = TimeSpan.FromSeconds(Math.Max(jittered, ex.RetryAfter?.TotalSeconds ?? 0));
      await DispositionAsync(op, next, "safe-transient-failure", delay);
    }
    catch (OperationBlockedException ex)
    {
      // Persist only allowlisted classifications, never provider exception messages.
      await DispositionAsync(op, ex.Authorization ? OperationState.AUTHORIZATION_BLOCKED : OperationState.PROVIDER_BLOCKED,
        ex.Authorization ? "authorization-blocked" : "provider-or-integrity-blocked", null);
    }
    catch (OperationCanceledException)
    {
      var next = op.Status == OperationState.CLAIMED
        ? (op.AttemptCount >= options.MaxAttempts ? OperationState.DEAD_LETTER : OperationState.RETRY_WAIT)
        : OperationState.RESULT_UNCERTAIN;
      await DispositionAsync(op, next, "execution-interrupted", TimeSpan.FromSeconds(5));
    }
    catch (Exception)
    {
      var next = op.Status is OperationState.REMOTE_STARTED or OperationState.VERIFYING
        ? (op.IsReconciliation ? OperationState.PROVIDER_BLOCKED : OperationState.RESULT_UNCERTAIN)
        : OperationState.PROVIDER_BLOCKED;
      await DispositionAsync(op, next, "execution-failed", TimeSpan.FromSeconds(5));
    }
    finally
    {
      await renewal.CancelAsync();
      await renewer;
    }
    logger?.LogInformation("Operation {OperationId}, token {AttemptToken}, last observed state {State}",
      op.Id, op.AttemptToken, op.Status);
    return true; // Work was claimed, not necessarily completed successfully.
  }

  private async Task DispositionAsync(DurableOperation op, OperationState state, string code, TimeSpan? delay)
  {
    using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    // If storage is unavailable, leave the durable active lease for the reaper.
    try { await store.TransitionAsync(op, options, state, code, delay, cleanup.Token); }
    catch (Exception)
    {
      logger?.LogWarning("Disposition could not be recorded for operation {OperationId}, token {AttemptToken}; lease recovery required",
        op.Id, op.AttemptToken);
    }
  }

  private async Task RenewAsync(DurableOperation op, CancellationTokenSource work, CancellationToken stop)
  {
    try
    {
      using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.RenewalSeconds));
      while (await timer.WaitForNextTickAsync(stop))
      {
        if (await store.RenewAsync(op, options, stop)) continue;
        await work.CancelAsync();
        return;
      }
    }
    catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    catch (Exception) { await work.CancelAsync(); }
  }
}
