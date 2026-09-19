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

/// <summary>Verified release checkpoint storage evidence outside the application database (§24.4, §25.7).</summary>
public sealed record CheckpointReceipt(string Reference, string ContentSha256Hex, long ByteCount);

/// <summary>
/// Trusted external checkpoint storage boundary for release manifests (spec §24.4 step 10, §25.7, §47.5).
/// Writes release evidence outside the application database, then re-reads and compares.
/// </summary>
public interface IReleaseCheckpointStore
{
  Task<CheckpointReceipt> WriteAsync(string manifestDigest, byte[] manifestBytes, CancellationToken ct = default);
  Task<CheckpointReceipt?> VerifyAsync(string reference, string expectedDigest, CancellationToken ct = default);
  Task<CheckpointReceipt?> ProbeAsync(string manifestDigest, CancellationToken ct = default);
}

