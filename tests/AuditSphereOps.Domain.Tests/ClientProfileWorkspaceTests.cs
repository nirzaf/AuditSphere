using System.Data.Common;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed class ClientProfileWorkspaceTests
{
  [Fact]
  public async Task OffboardingReadiness_ReadsEachSectionUnderItsOwnAuthority_ShowsUnreadableEvidenceAsUnknown_AndChangesNothing()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var manager = PbcSeed.User(f.FirmId, "Staff");
    var relationshipManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingManager = PbcSeed.User(f.FirmId, "Staff");
    var siblingClientId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = siblingClientId, FirmId = f.FirmId, LegalName = "Sibling Client", CreatedAt = DateTimeOffset.UtcNow });
    db.Users.AddRange(manager, relationshipManager, siblingManager);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(f.FirmId, manager, "Manager", f.ClientId),
      PbcSeed.Grant(f.FirmId, relationshipManager, "RelationshipManager", f.ClientId),
      PbcSeed.Grant(f.FirmId, siblingManager, "Manager", siblingClientId),
      PbcSeed.Grant(f.FirmId, f.Admin, "FinanceManager"),
      PbcSeed.Grant(f.FirmId, f.Admin, "RecordsCustodian"));
    var accountId = Guid.NewGuid(); var archiveId = Guid.NewGuid();
    db.BillingAccounts.Add(new BillingAccount { Id = accountId, FirmId = f.FirmId, PracticeClientId = f.ClientId, Currency = "USD", CreatedAt = DateTimeOffset.UtcNow });
    db.Invoices.AddRange(
      new Invoice { Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId, InvoiceNumber = "INV-OFF-1", Currency = "USD",
        Subtotal = 100.25m, Total = 100.25m, Status = BillingStates.InvoicePosted, PostedAt = DateTimeOffset.UtcNow.AddDays(-2), CreatedAt = DateTimeOffset.UtcNow.AddDays(-3) },
      new Invoice { Id = Guid.NewGuid(), FirmId = f.FirmId, BillingAccountId = accountId, InvoiceNumber = "INV-OFF-2", Currency = "USD",
        Subtotal = 10m, Total = 10m, Status = BillingStates.InvoiceDraft, CreatedAt = DateTimeOffset.UtcNow });
    db.WorkTasks.Add(new WorkTask { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, Title = "Open", CreatedAt = DateTimeOffset.UtcNow });
    db.Archives.Add(new AuditSphereOps.Domain.Completion.Archive { Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ProfileId = "profile", CreatedAt = DateTimeOffset.UtcNow });
    db.LegalHolds.Add(new AuditSphereOps.Domain.Records.LegalHold { Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId, ArchiveId = archiveId,
      HoldReference = "HOLD-1", RequestedByUserId = f.Admin.Id, RequestedAt = DateTimeOffset.UtcNow });
    await db.SaveChangesAsync();
    var grantsBefore = await db.RoleGrants.CountAsync(); var invoicesBefore = await db.Invoices.CountAsync();

    static ClientOffboardingItem Item(ClientOffboardingReadiness r, string key) => r.Sections.SelectMany(s => s.Items).Single(i => i.Key == key);

    // Full authority: every section is read and counted, and nothing is claimed to be complete.
    var full = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.True(full.Succeeded, full.Message);
    Assert.Equal(1, Item(full.Value!, "OPEN_ENGAGEMENTS").Count);
    Assert.Equal(1, Item(full.Value!, "OPEN_TASKS").Count);
    var owed = Item(full.Value!, "OUTSTANDING_INVOICES");
    Assert.Equal(1, owed.Count); Assert.Equal("USD 100.25", owed.Detail);
    Assert.Equal(1, Item(full.Value!, "UNPOSTED_INVOICES").Count);
    Assert.Equal(5, Item(full.Value!, "ACTIVE_GRANTS").Count); // staff, reviewer, client, manager, relationshipManager
    Assert.Equal(1, Item(full.Value!, "UNVERIFIED_ARCHIVES").Count);
    Assert.Equal(1, Item(full.Value!, "UNRELEASED_LEGAL_HOLDS").Count);
    Assert.Equal(ClientOffboardingStates.Unknown, Item(full.Value!, "EXTERNAL_ACCESS").State);
    Assert.Null(Item(full.Value!, "EXTERNAL_ACCESS").Count);
    Assert.Equal(ClientOffboardingStates.ActionRequired, full.Value!.OverallState);

    // A client Manager reads work, but finance, access and records are UNKNOWN rather than zero or clear.
    var partial = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, PbcSeed.Actor(manager, "Manager"), f.ClientId);
    Assert.True(partial.Succeeded, partial.Message);
    Assert.Equal(1, Item(partial.Value!, "OPEN_ENGAGEMENTS").Count);
    foreach (var key in new[] { "OUTSTANDING_INVOICES", "UNPOSTED_INVOICES", "ACTIVE_GRANTS", "UNVERIFIED_ARCHIVES", "UNRELEASED_LEGAL_HOLDS" })
    {
      Assert.Equal(ClientOffboardingStates.Unknown, Item(partial.Value!, key).State);
      Assert.Null(Item(partial.Value!, key).Count);
    }
    Assert.All(partial.Value!.Sections.Where(s => s.State == ClientOffboardingStates.Unknown), s => Assert.Null(s.OwnerRoute));

    // Commercial authority alone grants no operational visibility: everything is UNKNOWN, never NO_FINDINGS.
    var commercial = await PracticeCrmService.GetClientOffboardingReadinessAsync(db, PbcSeed.Actor(relationshipManager, "RelationshipManager"), f.ClientId);
    Assert.True(commercial.Succeeded, commercial.Message);
    Assert.All(commercial.Value!.Sections.SelectMany(s => s.Items), i => { Assert.Equal(ClientOffboardingStates.Unknown, i.State); Assert.Null(i.Count); });
    Assert.Equal(ClientOffboardingStates.Unknown, commercial.Value.OverallState);

    // Client users, sibling-client managers and engagement-scoped staff cannot open the assessment.
    foreach (var denied in new[] { PbcSeed.Actor(f.Client, "ClientUser"), PbcSeed.Actor(siblingManager, "Manager"), PbcSeed.Actor(f.Staff, "Staff") })
      Assert.Equal(ErrorCodes.ScopeDenied, (await PracticeCrmService.GetClientOffboardingReadinessAsync(db, denied, f.ClientId)).ErrorCode);

    // Read-only: no archive event, grant or invoice was written.
    Assert.Equal(0, await db.ClientLifecycleEvents.CountAsync());
    Assert.Equal(grantsBefore, await db.RoleGrants.CountAsync());
    Assert.Equal(invoicesBefore, await db.Invoices.CountAsync());
  }

  [Fact]
  public async Task ClientArchiveIsReversibleSearchableScopedAndAppendOnly()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, count: 1);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Manager", f.ClientId));
    await db.SaveChangesAsync();
    var manager = PbcSeed.Actor(f.Staff, "Manager");

    var before = await WorkspaceQuery.ClientAsync(db, manager, f.ClientId);
    Assert.True(before.Succeeded, before.Message);
    Assert.False(before.Value!.IsArchived);
    Assert.Equal("0", before.Value.LifecycleRevision);

    var archived = await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, true, 0));
    Assert.True(archived.Succeeded, archived.Message);
    Assert.True(archived.Value!.IsArchived);
    Assert.Equal(1, archived.Value.Revision);
    Assert.NotNull(archived.Value.LastArchivedAt);

    var replay = await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, true, 1));
    Assert.True(replay.Succeeded, replay.Message);
    Assert.Equal(1, replay.Value!.Revision);
    Assert.Single(await db.ClientLifecycleEvents.Where(e => e.ClientId == f.ClientId).ToListAsync());
    Assert.Equal(ErrorCodes.GenerationStale,
      (await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, false, 0))).ErrorCode);

    var visible = await PortfolioQuery.ListAsync(db, manager, "PBC TEST CLIENT");
    Assert.True(visible.Succeeded, visible.Message);
    Assert.True(Assert.Single(visible.Value!.Items).IsArchived);
    var profile = await WorkspaceQuery.ClientAsync(db, manager, f.ClientId);
    Assert.True(profile.Succeeded, profile.Message);
    Assert.True(profile.Value!.IsArchived);
    Assert.Single(profile.Value.Engagements);

    var denied = await PracticeCrmService.SetClientArchivedAsync(db,
      PbcSeed.Actor(f.Client, "ClientUser"), new(f.ClientId, false, 1));
    Assert.False(denied.Succeeded);
    Assert.Equal(1, await db.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));

    var restored = await PracticeCrmService.SetClientArchivedAsync(db, manager, new(f.ClientId, false, 1));
    Assert.True(restored.Succeeded, restored.Message);
    Assert.False(restored.Value!.IsArchived);
    Assert.Equal(2, restored.Value.Revision);
    Assert.NotNull(restored.Value.LastArchivedAt);
    Assert.Equal(2, await db.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));
    var history = await db.ClientLifecycleEvents.Where(e => e.ClientId == f.ClientId).OrderBy(e => e.Revision).ToListAsync();
    Assert.Equal(new[] { ClientLifecycleEventKinds.Archived, ClientLifecycleEventKinds.Restored }, history.Select(e => e.EventKind).ToArray());
    var refusal = await Assert.ThrowsAnyAsync<Exception>(async () =>
      await db.ClientLifecycleEvents.Where(e => e.ClientId == f.ClientId)
        .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.EventKind, ClientLifecycleEventKinds.Archived)));
    Assert.Equal("55000", Assert.IsType<PostgresException>(refusal.GetBaseException()).SqlState);
    var migrations = db.Database.GetMigrations().ToArray();
    var archiveMigration = Array.FindIndex(migrations, name => name.Contains("_ClientLifecycleArchiveHistory", StringComparison.Ordinal));
    Assert.True(archiveMigration > 0, "The archive history migration must be discoverable.");
    var downgrade = await Assert.ThrowsAnyAsync<Exception>(() => db.GetService<IMigrator>().MigrateAsync(migrations[archiveMigration - 1]));
    Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade.GetBaseException()).SqlState);
    Assert.Equal(2, await db.ClientLifecycleEvents.CountAsync(e => e.ClientId == f.ClientId));
  }

  [Fact]
  public async Task MetadataFullScopedCountsAndIndependentPagesNeverTruncateAtTheOldWindow()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, 105);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    var result = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new());
    Assert.True(result.Succeeded, result.Message); var v = result.Value!;
    Assert.Equal(new ClientWorkspaceMetrics(105, 52, 105), v.Metrics);
    Assert.Equal(10, v.Engagements.Count); Assert.Equal(10, v.Contacts.Count);
    var retainedWindow = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!;
    Assert.Equal(100, retainedWindow.Engagements.Count); Assert.Equal(100, retainedWindow.Contacts.Count);
    Assert.Equal(v.Metrics, retainedWindow.Metrics);
    Assert.Equal("Synthetic trading name", v.CommercialName); Assert.Equal("SYNTHETIC-REG-001", v.RegistrationNumber);
    Assert.Equal("QA", v.Jurisdiction); Assert.Equal("9007199254740993", v.SafetyGeneration);
    Assert.Equal("AWAITING_ACCEPTANCE", v.PortalIntent!.State); Assert.False(v.PortalIntent.ContactHasClientAccess);
    var last = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(2, 50, 4, 25))).Value!;
    Assert.Equal(5, last.Engagements.Count); Assert.Equal(5, last.Contacts.Count); Assert.Equal(v.Metrics, last.Metrics);
    Assert.Empty(last.Engagements.Select(e => e.Id).Intersect(v.Engagements.Select(e => e.Id)));
    Assert.Empty(last.Contacts.Select(c => c.Id).Intersect(v.Contacts.Select(c => c.Id)));
    Assert.Equal("request.invalid", (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(-1))).ErrorCode);
    Assert.Equal("request.invalid", (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId, paging: new(ContactPageSize: 100))).ErrorCode);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, foreign.ClientId)).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, Guid.NewGuid())).Succeeded);
    Assert.False((await WorkspaceQuery.ClientAsync(db, PbcSeed.Actor(f.Client, "ClientUser"), f.ClientId)).Succeeded);
    Assert.DoesNotContain("EXCLUDED PRIVATE PROFILE", System.Text.Json.JsonSerializer.Serialize(v));
    await db.Users.Where(u => u.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Disabled, true));
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);
  }

  [Fact]
  public async Task ClientCommercialAuthorityDoesNotWidenEngagementRoleOrAggregateSiblingData()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await ClientProfileWorkspaceSeed.PopulateAsync(db, f, grantClientStaff: false);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "CommercialManager", f.ClientId)); await db.SaveChangesAsync();
    var result = await WorkspaceQuery.ClientAsync(db, actor, f.ClientId);
    Assert.True(result.Succeeded, result.Message);
    Assert.Equal(new ClientWorkspaceMetrics(1, 0, 27), result.Value!.Metrics);
    Assert.Equal(f.EngagementId, Assert.Single(result.Value.Engagements).Id);
    Assert.False(result.Value.CanCreateEngagement); Assert.False(result.Value.CanManageContacts);
    await db.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.Role == "Staff")
      .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    var expired = PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId, f.EngagementId);
    expired.GrantedAt = DateTimeOffset.UtcNow.AddHours(-2);
    expired.ExpiresAt = DateTimeOffset.UtcNow.AddHours(-1);
    db.RoleGrants.Add(expired); await db.SaveChangesAsync();
    var withoutEngagement = (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!;
    Assert.Equal(0, withoutEngagement.Metrics.Engagements); Assert.Empty(withoutEngagement.Engagements);
  }

  [Fact]
  public async Task MidProjectionEngagementRevocationRefusesOldCountsWhileClientGrantRemains()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      await ClientProfileWorkspaceSeed.PopulateAsync(setup, f, grantClientStaff: false);
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "CommercialManager", f.ClientId)); await setup.SaveChangesAsync();
    }
    var interceptor = new RevokeDuringContacts(async () => {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    Assert.False((await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Succeeded); Assert.True(interceptor.Fired);
    Assert.Equal(0, (await WorkspaceQuery.ClientAsync(db, actor, f.ClientId)).Value!.Metrics.Engagements);
  }

  private sealed class RevokeDuringContacts(Func<Task> revoke) : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains("FROM client_contacts", StringComparison.Ordinal))
      { Fired = true; await revoke(); }
      return result;
    }
  }
}
