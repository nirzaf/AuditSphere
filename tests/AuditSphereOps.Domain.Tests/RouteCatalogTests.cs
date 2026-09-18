using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class RouteCatalogTests
{
  private sealed record CatalogFixture(
    Guid FirmId,
    Guid ClientId,
    AppUser Staff,
    AppUser Manager,
    ActorContext StaffActor,
    ActorContext ManagerActor);

  private static async Task<CatalogFixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.FirmSafetyStates.Add(new FirmSafetyState
    {
      Id = firmId, OperatingMode = "LOCAL_ONLY", DeploymentEpoch = 1
    });
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "Catalog Test Client",
      Status = CrmStates.ClientProspect, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientSafetyStates.Add(new ClientSafetyState
    {
      Id = clientId, FirmId = firmId, InputGeneration = 1
    });

    var staff = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = $"sub-staff-{Guid.NewGuid():N}",
      TenantId = "tenant-test", Email = $"staff-{Guid.NewGuid():N}@example.test",
      DisplayName = "Staff User", UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };
    var manager = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = $"sub-mgr-{Guid.NewGuid():N}",
      TenantId = "tenant-test", Email = $"mgr-{Guid.NewGuid():N}@example.test",
      DisplayName = "Manager User", UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.AddRange(staff, manager);
    db.RoleGrants.AddRange(
      new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = staff.Id, Role = "Staff",
        ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = staff.Id
      },
      new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = manager.Id, Role = "Manager",
        ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = manager.Id
      },
      new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = manager.Id, Role = "CommercialManager",
        ClientId = clientId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = manager.Id
      },
      new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = firmId, UserId = manager.Id, Role = "FinanceReviewer",
        ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = manager.Id
      });
    db.RateCardVersions.Add(new RateCardVersion
    {
      Id = Guid.NewGuid(), FirmId = firmId, Role = "Staff", Activity = "Testing",
      Currency = "QAR", RatePerHour = 250m, Version = 1, Status = PracticeTimeStates.RateApproved,
      CreatedByUserId = manager.Id, ApprovedByUserId = manager.Id,
      ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var staffActor = new ActorContext(staff.Id, firmId, staff.SessionEpoch, ["Staff"]);
    var managerActor = new ActorContext(manager.Id, firmId, manager.SessionEpoch, ["Manager", "CommercialManager", "FinanceReviewer"]);
    return new CatalogFixture(firmId, clientId, staff, manager, staffActor, managerActor);
  }

  [Fact]
  public async Task ClientContactWorkflow_IncrementsGeneration_And_EnforcesSinglePrimaryContact()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);

    Guid contact1Id, contact2Id;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var res1 = await PracticeCrmService.CreateClientContactAsync(db, fixture.ManagerActor,
        new CreateClientContactRequest(
          PracticeClientId: fixture.ClientId,
          FullName: "Alice Primary",
          Email: "alice@client.test",
          Role: "Finance Director",
          ApprovedScope: null,
          ValidFrom: DateTimeOffset.UtcNow,
          ValidTo: null,
          Primary: true));
      Assert.True(res1.Succeeded);
      contact1Id = res1.Value;

      var guardAfter1 = await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == fixture.ClientId);
      Assert.Equal(2, guardAfter1.InputGeneration);

      var res2 = await PracticeCrmService.CreateClientContactAsync(db, fixture.ManagerActor,
        new CreateClientContactRequest(
          PracticeClientId: fixture.ClientId,
          FullName: "Bob Second",
          Email: "bob@client.test",
          Role: "Managing Director",
          ApprovedScope: null,
          ValidFrom: DateTimeOffset.UtcNow,
          ValidTo: null,
          Primary: true));
      Assert.True(res2.Succeeded);
      contact2Id = res2.Value;

      var guardAfter2 = await db.ClientSafetyStates.AsNoTracking().SingleAsync(x => x.Id == fixture.ClientId);
      Assert.Equal(3, guardAfter2.InputGeneration);

      var c1 = await db.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == contact1Id);
      var c2 = await db.ClientContacts.AsNoTracking().SingleAsync(x => x.Id == contact2Id);
      Assert.False(c1.Primary);
      Assert.True(c2.Primary);

      // Staff role without CommercialManager is denied
      var denied = await PracticeCrmService.CreateClientContactAsync(db, fixture.StaffActor,
        new CreateClientContactRequest(fixture.ClientId, "Charlie Third", "charlie@client.test", "Officer", null, DateTimeOffset.UtcNow, null, false));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);
    }
  }

  [Fact]
  public async Task PracticeTime_DraftSubmitApprove_EnforcesSegregationOfDuties()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);

    Guid taskId, entryId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var taskRes = await PracticeTimeService.CreateTaskAsync(db, fixture.ManagerActor,
        new CreateTaskRequest("Audit Testing Task", ClientId: fixture.ClientId, AssigneeUserId: fixture.Staff.Id));
      Assert.True(taskRes.Succeeded);
      taskId = taskRes.Value;

      var draftRes = await PracticeTimeService.SaveTimeDraftAsync(db, fixture.StaffActor,
        new SaveTimeDraftRequest(
          TaskId: taskId,
          WorkDate: DateOnly.FromDateTime(DateTime.UtcNow),
          StartMinute: 540,
          DurationMinutes: 120,
          Role: "Staff",
          Activity: "Testing",
          BillableClassification: PracticeTimeStates.Billable,
          Narrative: "Executed test procedures",
          NarrativeVisibility: PracticeTimeStates.NarrativeInternal,
          Currency: "QAR"));
      Assert.True(draftRes.Succeeded);
      entryId = draftRes.Value;

      var submitRes = await PracticeTimeService.SubmitTimeAsync(db, fixture.StaffActor, entryId);
      Assert.True(submitRes.Succeeded);

      // Self-approval denied
      var selfApprove = await PracticeTimeService.ApproveTimeAsync(db, fixture.StaffActor, entryId);
      Assert.False(selfApprove.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfApprove.ErrorCode);

      // Manager approval succeeds
      var managerApprove = await PracticeTimeService.ApproveTimeAsync(db, fixture.ManagerActor, entryId);
      Assert.True(managerApprove.Succeeded);

      var approvedEntry = await db.TimeEntries.AsNoTracking().SingleAsync(x => x.Id == entryId);
      Assert.Equal(PracticeTimeStates.TimeApproved, approvedEntry.Status);
      Assert.Equal(fixture.Manager.Id, approvedEntry.ApprovedByUserId);
    }
  }

  [Fact]
  public async Task FiscalPeriod_CloseWorkflow_RequiresAllJournalsPosted_And_IsIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);

    Guid periodId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = Guid.NewGuid();
      db.FirmPeriods.Add(new FirmPeriod
      {
        Id = periodId, FirmId = fixture.FirmId, PeriodCode = "2026-09",
        Status = LedgerStates.PeriodOpen, Revision = 1
      });
      // Add unposted draft journal
      var draftJournal = new FirmJournal
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, PeriodId = periodId,
        JournalNumber = "FJ-001", SourceKind = "Manual", SourceKey = "MAN-1",
        PostingPurpose = "Adjustment", Currency = "QAR", Status = LedgerStates.JournalDraft,
        CreatedByUserId = fixture.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      };
      db.FirmJournals.Add(draftJournal);
      await db.SaveChangesAsync();

      // Close blocked because of unposted journal
      var blocked = await LedgerService.CloseFiscalPeriodAsync(db, fixture.ManagerActor, periodId, "Month end");
      Assert.False(blocked.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);

      // Post or remove unposted journal
      db.FirmJournals.Remove(draftJournal);
      await db.SaveChangesAsync();

      // Now close succeeds
      var closed = await LedgerService.CloseFiscalPeriodAsync(db, fixture.ManagerActor, periodId, "Month end");
      Assert.True(closed.Succeeded);

      var p = await db.FirmPeriods.AsNoTracking().SingleAsync(x => x.Id == periodId);
      Assert.Equal(LedgerStates.PeriodClosed, p.Status);
      Assert.Equal(2, p.Revision);
      Assert.NotNull(p.ClosedAt);

      // Idempotent re-close succeeds
      var reclose = await LedgerService.CloseFiscalPeriodAsync(db, fixture.ManagerActor, periodId, "Month end duplicate");
      Assert.True(reclose.Succeeded);
    }
  }
}
