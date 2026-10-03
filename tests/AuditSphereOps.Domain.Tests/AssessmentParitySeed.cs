using AuditSphereOps.Application.Acceptance;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;
internal static class AssessmentParitySeed
{
  internal static async Task<Guid> PopulateAsync(AuditSphereDbContext db, PbcSeed.Fixture f)
  {
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Admin, "Partner", clientId: f.ClientId));
    await QuestionnaireSeed.SeedTemplatesAndDefinitionsAsync(db);
    await db.SaveChangesAsync();
    var decided = await AcceptanceDecisionService.RecordAsync(db, PbcSeed.Actor(f.Admin, "Partner"),
      new(f.ClientId, null, "AccountingOnly", "Declined", "Exact synthetic historical decision", null, 1));
    if (!decided.Succeeded) throw new InvalidOperationException(decided.Message);
    await db.ClientSafetyStates.Where(s => s.Id == f.ClientId).ExecuteUpdateAsync(s => s.SetProperty(x => x.InputGeneration, 2));
    return decided.Value;
  }
}
