using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Owned synthetic sibling-client evidence for authorization regression.</summary>
internal static class AccountingEvidenceQueueSeed
{
  internal static async Task<(PbcSeed.Fixture, (Guid Client, Guid Engagement) Sibling)> SeedAsync(ITestPostgresDatabase pg)
  {
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var client = Guid.NewGuid(); var engagement = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = client, FirmId = f.FirmId, LegalName = "SYNTHETIC SIBLING", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.Add(new Engagement { Id = engagement, FirmId = f.FirmId, PracticeClientId = client, Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = client, FirmId = f.FirmId });
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId, f.EngagementId));
    foreach (var (clientId, engagementId, reference) in new[] { (f.ClientId, f.EngagementId, "SYNTHETIC-A"), (client, engagement, "SYNTHETIC-B") })
    {
      var period = Guid.NewGuid();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod { Id = period, FirmId = f.FirmId, ClientId = clientId,
        PeriodCode = "FY26", StartDate = new(2026, 1, 1), EndDate = new(2026, 12, 31), Basis = "IFRS", Currency = "QAR",
        CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
      db.SpecialistAccountingSchedules.Add(new SpecialistAccountingSchedule { Id = Guid.NewGuid(), FirmId = f.FirmId,
        ClientId = clientId, EngagementId = engagementId, PeriodId = period, Area = "ASSETS", MethodologyVersion = "SYNTHETIC-V1",
        DepreciationMethod = "STRAIGHT_LINE", UsefulLifeMonths = 12, AssumptionsHash = new('a', 64),
        EvidenceReference = reference, CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow });
    }
    await db.SaveChangesAsync(); return (f, (client, engagement));
  }
}
