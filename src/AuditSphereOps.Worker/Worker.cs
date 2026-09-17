using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Worker;

// Local database-only validation; no provider effects or professional approval.
public sealed class Worker(IDbContextFactory<AuditSphereDbContext> contexts, ILogger<Worker> logger) : BackgroundService
{
  /// <summary>Processes until no pending dataset remains; returns how many were processed in this call.</summary>
  public async Task<int> DrainAsync(CancellationToken ct = default)
  {
    var count = 0;
    while (await ProcessNextAsync(ct)) count++;
    return count;
  }

  public async Task<bool> ProcessNextAsync(CancellationToken ct = default)
  {    await using var db = await contexts.CreateDbContextAsync(ct);
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    // Rows are preserved. Serialize against row writers for this bounded proving slice.
    await db.Database.ExecuteSqlRawAsync("LOCK TABLE trial_balance_rows IN SHARE MODE", ct);
    var pending = await db.TrialBalanceDatasets.FromSqlRaw("""
      SELECT * FROM trial_balance_datasets
      WHERE validation_status = 'Pending' AND source_kind = 'Raw'
      ORDER BY imported_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
      """).ToListAsync(ct);
    var dataset = pending.SingleOrDefault();
    if (dataset is null) return false;

    var rows = await db.TrialBalanceRows.AsNoTracking()
      .Where(x => x.DatasetId == dataset.Id).ToListAsync(ct);
    var total = rows.Sum(x => x.Amount);
    var valid = rows.Count > 0 && total == 0 &&
      !string.IsNullOrWhiteSpace(dataset.Currency) &&
      rows.All(x => x.Currency == dataset.Currency && !string.IsNullOrWhiteSpace(x.AccountCode)) &&
      rows.Select(x => (x.Entity, x.AccountCode)).Distinct().Count() == rows.Count;
    dataset.ControlTotal = total;
    dataset.Balanced = rows.Count > 0 && total == 0;
    dataset.ValidationStatus = valid ? "Accepted" : "Rejected";
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    logger.LogInformation("Trial balance {DatasetId} validation: {Status}", dataset.Id, dataset.ValidationStatus);
    return true;
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
        logger.LogError("Trial-balance validation failed; transaction rolled back. Retrying after delay.");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
      }
    }
  }
}

