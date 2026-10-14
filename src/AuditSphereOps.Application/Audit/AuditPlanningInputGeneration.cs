using AuditSphereOps.Application.Operations;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Audit;

/// <summary>
/// Acquires a current, database-locked read of the shared client input generation. Risk and materiality revisions are
/// retained separately in each immutable procedure-result basis; advancing this broad generation would invalidate
/// unrelated approved accounting mappings.
/// </summary>
internal static class AuditPlanningInputGeneration
{
  public static async Task<long?> LockAndReadAsync(
    IAuditSphereDbContext db,
    Guid firmId,
    Guid clientId,
    CancellationToken ct)
  {
    var state = await db.ClientSafetyStates.FromSqlInterpolated($"""
      SELECT * FROM client_safety_states
      WHERE firm_id = {firmId} AND id = {clientId}
      FOR UPDATE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    return state?.InputGeneration;
  }
  /// <summary>Submissions share this fence; input-generation writers still serialize against it.</summary>
  public static async Task<long?> ReadSharedAsync(IAuditSphereDbContext db, Guid firmId, Guid clientId, CancellationToken ct)
  {
    var state = await db.ClientSafetyStates.FromSqlInterpolated($"""
      SELECT * FROM client_safety_states WHERE firm_id = {firmId} AND id = {clientId} FOR SHARE
      """).AsNoTracking().SingleOrDefaultAsync(ct);
    return state?.InputGeneration;
  }

}
