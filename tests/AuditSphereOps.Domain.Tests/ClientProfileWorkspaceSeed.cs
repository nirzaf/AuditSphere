using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Synthetic read-side client records; not acceptance, invitation or Microsoft evidence.</summary>
internal static class ClientProfileWorkspaceSeed
{
  internal static async Task PopulateAsync(AuditSphereDbContext db, PbcSeed.Fixture f, int count = 27,
    bool grantClientStaff = true)
  {
    var now = new DateTimeOffset(2026, 1, 2, 12, 30, 0, TimeSpan.Zero);
    var client = await db.PracticeClients.SingleAsync(c => c.Id == f.ClientId && c.FirmId == f.FirmId);
    client.CommercialName = "Synthetic trading name"; client.RegistrationNumber = "SYNTHETIC-REG-001";
    client.Jurisdiction = "QA"; client.CreatedAt = now; client.RestrictedProfile = "EXCLUDED PRIVATE PROFILE";
    var safety = await db.ClientSafetyStates.SingleAsync(c => c.Id == f.ClientId);
    safety.InputGeneration = 9007199254740993;
    var initial = await db.Engagements.SingleAsync(e => e.Id == f.EngagementId);
    initial.ServiceRoute = "Scoped audit"; initial.PeriodStart = "2026-01-01"; initial.PeriodEnd = "2026-12-31";
    initial.CreatedAt = now.AddMinutes(count); initial.ProfessionalWorkBlocked = false;
    for (var i = 1; i < count; i++)
      db.Engagements.Add(new Engagement { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
        ServiceRoute = $"Synthetic audit {i:000}", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = "Draft",
        ProfessionalWorkBlocked = i % 2 == 0, CreatedAt = now.AddMinutes(count - i) });
    var contacts = Enumerable.Range(0, count).Select(i => new ClientContact { Id = Guid.NewGuid(), FirmId = f.FirmId,
      PracticeClientId = f.ClientId, FullName = $"Synthetic contact {i:000}", Email = $"contact{i:000}@example.test", Role = "Finance", Primary = i == 0 }).ToArray();
    db.ClientContacts.AddRange(contacts);
    db.ClientPortalIntents.Add(new ClientPortalIntent { Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      ClientContactId = contacts[0].Id, RecipientEmail = contacts[0].Email, SourceProposalId = Guid.NewGuid(),
      State = ClientPortalIntentStates.AwaitingAcceptance, CreatedAt = now, UpdatedAt = now });
    if (grantClientStaff) db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId));
    await db.SaveChangesAsync();
  }
}
