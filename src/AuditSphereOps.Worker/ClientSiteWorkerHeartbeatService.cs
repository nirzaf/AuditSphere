using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Microsoft365;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Worker;

public sealed record ClientSiteWorkerReadiness(string State, string? DiagnosticCode);

/// <summary>Publishes configuration readiness without claiming Microsoft permission verification.</summary>
public sealed class ClientSiteWorkerHeartbeatService(
  IAuditSphereDbContextFactory factory,
  WorkerOptions options,
  ClientSiteWorkerReadiness readiness,
  ILogger<ClientSiteWorkerHeartbeatService> logger) : BackgroundService
{
  private readonly string instanceId = Guid.CreateVersion7().ToString("N");
  private readonly DateTimeOffset startedAt = DateTimeOffset.UtcNow;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await using var db = await factory.CreateAsync(stoppingToken);
        var row = await db.ClientSiteWorkerHeartbeats.SingleOrDefaultAsync(x => x.FirmId == options.FirmId &&
          x.WorkerGroup == ClientSharePointSiteHandler.Group && x.WorkerInstanceId == instanceId, stoppingToken);
        if (row is null)
        {
          row = new ClientSiteWorkerHeartbeat
          {
            Id = Guid.CreateVersion7(), FirmId = options.FirmId, WorkerGroup = ClientSharePointSiteHandler.Group,
            WorkerInstanceId = instanceId, StartedAt = startedAt
          };
          db.ClientSiteWorkerHeartbeats.Add(row);
        }
        row.DeploymentEpoch = options.DeploymentEpoch;
        row.ReadinessState = readiness.State;
        row.DiagnosticCode = readiness.DiagnosticCode;
        row.LastSeenAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
      catch (Exception)
      {
        logger.LogWarning("Client-sites worker heartbeat could not be persisted; administration will show readiness as stale.");
      }

      try { await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken); }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
    }
  }
}
