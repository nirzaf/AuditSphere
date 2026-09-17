// Abstractions: only real provider/persistence/actor boundaries (§28.2). No generic repositories.
namespace AuditSphereOps.Application.Abstractions;

/// <summary>Authenticated actor resolved from the Blazor circuit (§28.4): user + firm + session epoch + roles.</summary>
public sealed record ActorContext(
  Guid UserId,
  Guid FirmId,
  long SessionEpoch,
  IReadOnlyList<string> Roles,
  Guid? ClientId = null,
  Guid? EngagementId = null);

/// <summary>Persistence boundary: one guarded transaction per command (§§28.6, 29.6).</summary>
public interface IUnitOfWork
{
  Task<int> SaveChangesAsync(CancellationToken ct = default);
  Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken ct = default);
}

/// <summary>Clock boundary for deterministic tests.</summary>
public interface IClock
{
  DateTimeOffset UtcNow { get; }
}
