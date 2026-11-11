using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NativeSourceWorker = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.E2E.Tests;

internal sealed record SupportedTimeScopeFixture(AppUser Manager, Guid AssignedTaskId, IReadOnlyList<Guid> PagedEntryIds)
{
  internal static async Task<SupportedTimeScopeFixture> CreateAsync(ITestPostgresDatabase database,
    string periodCode, string assignedTitle, string siblingTitle, string assignedNarrative, string siblingNarrative)
  {
    var world = await SupportedAuditCompletionWorldBuilder.CreateAsync(database, includeSibling: true);
    var scope = world.Primary;
    var accounting = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(database,
      (scope.FirmId, world.Administrator), "AccountingPreparer");
    var reviewer = await SupportedAuditCompletionWorldBuilder.ProvisionFirmRoleAsync(database,
      (scope.FirmId, world.Administrator), "AccountingReviewer");
    var mappingId = await PrepareMappingAsync(database, world, accounting.Actor, reviewer.Actor, periodCode);
    await using var db = new AuditSphereDbContext(database.Options);
    var administrator = await db.Users.AsNoTracking().SingleAsync(x => x.Id == world.Administrator.UserId);
    var ensured = await RoleAdministrationService.EnsureUserAsync(db, world.Administrator,
      new(administrator.TenantId, Guid.NewGuid().ToString("D"), $"time-author-{Guid.NewGuid():N}@example.test",
        "Synthetic time author", "Staff", "APPROVED_ROSTER"));
    Assert.True(ensured.Succeeded, ensured.Message);
    foreach (var engagementId in new[] { scope.EngagementId, world.Sibling.EngagementId })
    {
      var grant = await RoleAdministrationService.ApplyRoleGrantAsync(db, world.Administrator,
        new(ensured.Value, "Staff", "ENGAGEMENT", scope.ClientId, engagementId));
      Assert.True(grant.Succeeded, grant.Message);
    }
    var authorUser = await db.Users.AsNoTracking().SingleAsync(x => x.Id == ensured.Value);
    var author = new ActorContext(authorUser.Id, scope.FirmId, authorUser.SessionEpoch, ["Staff"]);
    var assigned = await PracticeTimeService.CreateTaskAsync(db, author,
      new(assignedTitle, scope.ClientId, scope.EngagementId, author.UserId, MappingVersionId: mappingId, FsliCode: "CASH"));
    Assert.True(assigned.Succeeded, assigned.Message);
    var sibling = await PracticeTimeService.CreateTaskAsync(db, author,
      new(siblingTitle, scope.ClientId, world.Sibling.EngagementId, author.UserId));
    Assert.True(sibling.Succeeded, sibling.Message);
    async Task<Guid> SubmitAsync(Guid taskId, DateOnly date, int start, int minutes, string activity, string narrative)
    {
      var draft = await PracticeTimeService.SaveTimeDraftAsync(db, author,
        new(taskId, date, start, minutes, "Staff", activity, PracticeTimeStates.NonBillable, narrative));
      Assert.True(draft.Succeeded, draft.Message);
      var submitted = await PracticeTimeService.SubmitTimeAsync(db, author, draft.Value);
      Assert.True(submitted.Succeeded, submitted.Message);
      return draft.Value;
    }
    await SubmitAsync(assigned.Value, new(2026, 9, 10), 540, 60, "Fieldwork", assignedNarrative);
    await SubmitAsync(sibling.Value, new(2026, 9, 11), 540, 60, "Review", siblingNarrative);
    var pagedIds = new List<Guid>();
    for (var index = 0; index < 50; index++)
      pagedIds.Add(await SubmitAsync(assigned.Value, new(2026, 9, 11), 600 + index * 2, 1,
        "Fieldwork", $"SYN-PAGING-TIME-{index}"));

    Assert.Equal(52, await db.TimeEntries.CountAsync(x => x.FirmId == scope.FirmId && x.UserId == author.UserId &&
      x.Status == PracticeTimeStates.TimeSubmitted));
    Assert.Equal(2, await db.WorkTasks.CountAsync(x => x.FirmId == scope.FirmId && x.AssigneeUserId == author.UserId));
    var manager = await db.Users.AsNoTracking().SingleAsync(x => x.Id == scope.Preparer.UserId);
    var managerGrant = Assert.Single(await db.RoleGrants.AsNoTracking()
      .Where(x => x.UserId == manager.Id && x.RevokedAt == null).ToListAsync());
    Assert.Equal("Manager", managerGrant.Role);
    Assert.Equal(scope.EngagementId, managerGrant.EngagementId);
    Assert.Equal(scope.ClientId, managerGrant.ClientId);
    return new(manager, assigned.Value, pagedIds);
  }

  private static async Task<Guid> PrepareMappingAsync(ITestPostgresDatabase database,
    SupportedAuditCompletionWorld world, ActorContext accounting, ActorContext reviewer, string periodCode)
  {
    var scope = world.Primary;
    Guid datasetId;
    await using (var db = new AuditSphereDbContext(database.Options))
    {
      // A genuine client-level Draft period is deliberately invisible to the engagement-only manager.
      var period = await ClientAccountingService.CreatePeriodAsync(db, accounting,
        new(scope.ClientId, periodCode, new(2026, 1, 1), new(2026, 12, 31), "STATUTORY", "QAR"));
      Assert.True(period.Succeeded, period.Message);
      var imported = await TrialBalanceImportService.ImportAsync(db, accounting, scope.ClientId, scope.EngagementId,
        "AccountCode,AccountName,NetClosingBalance,Currency,Entity,MappingCode\n1000,Cash,100,QAR,TEST,CASH\n4000,Revenue,-100,QAR,TEST,REVENUE\n",
        new(period.Value, null, "STATUTORY"));
      Assert.True(imported.Succeeded, imported.Message);
      datasetId = imported.Value;
    }
    var factory = new OperationContextFactory(new TimeDbContextFactory(database.Options));
    var store = new PostgresOperationStore(factory);
    var options = new WorkerOptions(scope.FirmId, "Test");
    var handler = new TrialBalanceValidationHandler();
    var worker = new NativeSourceWorker(new OperationDispatcher(store,
      new DurableOperationRegistry([handler], options), options),
      [new TrialBalanceDiscovery(factory, store, handler, options)], NullLogger<NativeSourceWorker>.Instance);
    Assert.True(await worker.ProcessNextAsync());
    await using var reviewDb = new AuditSphereDbContext(database.Options);
    var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(reviewDb, reviewer,
      new(scope.ClientId, scope.EngagementId, AccountingSourceKinds.TrialBalance, datasetId, null,
        "Synthetic independently reviewed time-scope source", Reviewed: true));
    Assert.True(accepted.Succeeded, accepted.Message);
    var taxonomy = await ClientAccountingService.CreateTaxonomyVersionAsync(reviewDb, world.Administrator,
      "time-e2e-v1", "IFRS", "Synthetic time-scope taxonomy", new(2026, 1, 1));
    Assert.True(taxonomy.Succeeded, taxonomy.Message);
    var nodes = await ClientAccountingService.AddTaxonomyNodesAsync(reviewDb, world.Administrator, taxonomy.Value,
    [
      new("CASH", "Cash", "ASSETS", "SIGNED", "DEBIT", "BALANCE_SHEET", true, "ALL"),
      new("REVENUE", "Revenue", "INCOME", "SIGNED", "CREDIT", "INCOME_STATEMENT", true, "ALL")
    ]);
    Assert.True(nodes.Succeeded, nodes.Message);
    var published = await ClientAccountingService.PublishTaxonomyVersionAsync(reviewDb, reviewer, taxonomy.Value);
    Assert.True(published.Succeeded, published.Message);
    var mapping = await FinancialStatementService.CreateMappingVersionAsync(reviewDb, scope.Preparer,
      new(datasetId, "time-e2e-v1", "2026-01-01", "2026-12-31",
      [new("1000", "CASH", "ASSETS", 1m, "Mapped"), new("4000", "REVENUE", "INCOME", 1m, "Mapped")]));
    Assert.True(mapping.Succeeded, mapping.Message);
    var basis = await MappingApprovalWorkspace.GetAsync(reviewDb, reviewer, mapping.Value);
    Assert.True(basis.Succeeded, basis.Message);
    var approved = await FinancialStatementService.ApproveMappingAsync(reviewDb, reviewer, mapping.Value, 1,
      reviewRevision: basis.Value!.Revision, reviewed: true);
    Assert.True(approved.Succeeded, approved.Message);
    return mapping.Value;
  }

  private sealed class TimeDbContextFactory(DbContextOptions<AuditSphereDbContext> options) : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
    public Task<AuditSphereDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
      => Task.FromResult(new AuditSphereDbContext(options));
  }
}
