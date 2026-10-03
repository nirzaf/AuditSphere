using AuditSphereOps.Infrastructure.Persistence;
namespace AuditSphereOps.Domain.Tests;
internal static class EngagementCreationReviewSeed
{
  internal static async Task PopulateAsync(AuditSphereDbContext db,PbcSeed.Fixture f)
  {db.RoleGrants.Add(PbcSeed.Grant(f.FirmId,f.Staff,"Manager",f.ClientId));await db.SaveChangesAsync();}
}
