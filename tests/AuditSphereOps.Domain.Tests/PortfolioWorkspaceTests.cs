using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

public sealed class PortfolioWorkspaceTests
{
  [Theory]
  [InlineData("detail")]
  [InlineData("client")]
  [InlineData("portfolio")]
  public async Task ProgressRedactsLifecycleDetailsWhenFirmWideDetailGrantIsRevokedAfterSnapshot(string view)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var detailGrant = PbcSeed.Grant(f.FirmId, f.Staff, "SeniorManager");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      var engagement = await setup.Engagements.SingleAsync(e => e.Id == f.EngagementId);
      engagement.ServiceRoute = "FinancialStatementAudit";
      setup.RoleGrants.Add(detailGrant);
      await setup.SaveChangesAsync();
    }
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    await using (var baseline = new AuditSphereDbContext(pg.Options))
    {
      var result = await EngagementProgressQuery.GetAsync(baseline, actor, f.EngagementId);
      Assert.True(result.Succeeded, result.Message);
      Assert.Contains("Commercial proposal preparation and dispatch required", result.Value!.Gates);
      Assert.Equal("Commercial proposal preparation and dispatch required", result.Value.NextAction);
      Assert.True(result.Value.GateOpen);
    }

    var witness = new ProgressLifecycleSnapshotWitness(f.Staff.Id, f.EngagementId);
    var race = new ProgressSnapshotCommittedRace(witness, async () =>
    {
      int updated;
      await using (var writer = new AuditSphereDbContext(pg.Options))
      await using (var transaction = await writer.Database.BeginTransactionAsync())
      {
        updated = await writer.RoleGrants.Where(g => g.Id == detailGrant.Id && g.FirmId == f.FirmId && g.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
        await transaction.CommitAsync();
      }
      await using var verifier = new AuditSphereDbContext(pg.Options);
      var observed = await verifier.RoleGrants.AsNoTracking().AnyAsync(g => g.Id == detailGrant.Id && g.RevokedAt != null);
      return (updated, observed);
    });
    await using var reader = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(witness, race).Options);
    EngagementProgressView? progress;
    bool succeeded;
    if (view == "detail")
    {
      var result = await EngagementProgressQuery.GetAsync(reader, actor, f.EngagementId);
      succeeded = result.Succeeded;
      progress = result.Value;
    }
    else
    {
      var result = view == "client" ? await EngagementProgressQuery.ForClientAsync(reader, actor, f.ClientId)
        : await PortfolioQuery.ProgressAsync(reader, actor);
      succeeded = result.Succeeded;
      progress = result.Value is null ? null : Assert.Single(result.Value.Items);
    }
    Assert.True(race.Fired);
    Assert.True(race.LifecycleReadBeforeCommit);
    Assert.Equal(1, race.UpdatedRows);
    Assert.True(race.ObservedCommitted);
    Assert.True(succeeded);
    Assert.NotNull(progress);
    Assert.True(progress.GateOpen);
    Assert.Equal(["Some engagement readiness details are restricted to firm-wide management."], progress.Gates);
    Assert.Equal("Some engagement readiness details are restricted to firm-wide management.", progress.NextAction);
    Assert.DoesNotContain("Commercial proposal preparation and dispatch required", System.Text.Json.JsonSerializer.Serialize(progress));
  }

  private sealed class ProgressLifecycleSnapshotWitness(Guid userId, Guid engagementId) : DbCommandInterceptor
  {
    public bool Observed { get; private set; }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.Transaction is not null &&
          command.CommandText.Contains("U12 engagement projection final authorization", StringComparison.Ordinal) &&
          command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, userId)) &&
          command.Parameters.Cast<DbParameter>().Any(p => Equals(p.Value, engagementId)))
        Observed = true;
      return ValueTask.FromResult(result);
    }
  }

  private sealed class ProgressSnapshotCommittedRace(ProgressLifecycleSnapshotWitness witness,
    Func<Task<(int Rows, bool Observed)>> change) : DbTransactionInterceptor
  {
    public bool Fired { get; private set; }
    public bool LifecycleReadBeforeCommit { get; private set; }
    public int UpdatedRows { get; private set; }
    public bool ObservedCommitted { get; private set; }

    public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
      CancellationToken cancellationToken = default)
    {
      if (Fired) return;
      Fired = true;
      LifecycleReadBeforeCommit = witness.Observed;
      (UpdatedRows, ObservedCommitted) = await change();
    }
  }

  [Theory]
  [InlineData("detail")]
  [InlineData("client")]
  [InlineData("portfolio")]
  public async Task ProgressObservesOneCommittedTaskAndHoldState(string view)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var taskId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.WorkTasks.Add(new WorkTask { Id = taskId, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, Title = "Synthetic completed work", Status = PracticeTimeStates.TaskCompleted,
        CreatedAt = DateTimeOffset.UtcNow });
      await setup.SaveChangesAsync();
    }
    var interceptor = new RevokeDuringRead(async () =>
    {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await using var tx = await mutation.Database.BeginTransactionAsync();
      await mutation.WorkTasks.Where(x => x.Id == taskId).ExecuteUpdateAsync(x =>
        x.SetProperty(t => t.Status, PracticeTimeStates.TaskOpen));
      mutation.EngagementHolds.Add(new EngagementHold { Id = Guid.NewGuid(), FirmId = f.FirmId,
        EngagementId = f.EngagementId, HoldKind = "Acceptance", Reason = "Synthetic currentness check",
        CreatedAt = DateTimeOffset.UtcNow });
      await mutation.SaveChangesAsync();
      await tx.CommitAsync();
    }, "FROM engagement_holds");
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    EngagementProgressView progress;
    if (view == "detail")
    {
      var result = await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId);
      Assert.True(result.Succeeded, result.Message);
      progress = result.Value!;
    }
    else
    {
      var result = view == "client" ? await EngagementProgressQuery.ForClientAsync(db, actor, f.ClientId)
        : await PortfolioQuery.ProgressAsync(db, actor);
      Assert.True(result.Succeeded, result.Message);
      progress = Assert.Single(result.Value!.Items);
    }
    Assert.True(interceptor.Fired);
    var hasHold = progress.Gates.Contains("An engagement hold remains open.");
    Assert.True((progress.CompletedRequired == 1 && !hasHold) || (progress.CompletedRequired == 0 && hasHold),
      $"Mixed committed states: completed={progress.CompletedRequired}, hold={hasHold}.");
  }

  [Fact]
  public async Task ProgressRefusesCallerOwnedTransactionWithoutCompletingIt()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    await using var callerTransaction = await db.Database.BeginTransactionAsync();
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var detail = await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId);
    var client = await EngagementProgressQuery.ForClientAsync(db, actor, f.ClientId);
    var portfolio = await PortfolioQuery.ProgressAsync(db, actor);
    Assert.False(detail.Succeeded);
    Assert.False(client.Succeeded);
    Assert.False(portfolio.Succeeded);
    Assert.Same(callerTransaction, db.Database.CurrentTransaction);
    await callerTransaction.RollbackAsync();
  }

  [Fact]
  public async Task ProgressDetailRechecksAuthorityAfterSnapshotCloses()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var interceptor = new RevokeDuringRead(async () =>
    {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.Role == "Staff")
        .ExecuteUpdateAsync(update => update.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    }, "FROM releases");
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(interceptor).Options);
    var result = await EngagementProgressQuery.GetAsync(db, PbcSeed.Actor(f.Staff, "Staff"), f.EngagementId);
    Assert.True(interceptor.Fired);
    Assert.False(result.Succeeded);
    Assert.Equal("scope.denied", result.ErrorCode);
    Assert.Null(result.Value);
  }

  [Theory]
  [InlineData("AccountingOnly")]
  [InlineData("InternalAudit")]
  public async Task NonAuditProgressRemainsOpenWhenNoCompletionProfileIsApproved(string serviceRoute)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
    engagement.ServiceRoute = serviceRoute;
    await db.SaveChangesAsync();

    var empty = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
    Assert.Null(empty.PercentComplete);
    Assert.Contains("No issued release exists for this engagement yet.", empty.Gates);

    // Synthetic release evidence tests the read projection; it does not approve this service's completion policy.
    await PortfolioWorkspaceSeed.PopulateAsync(db, f);
    var hold = await db.EngagementHolds.SingleAsync(x => x.EngagementId == f.EngagementId);
    hold.Released = true;
    hold.ReleasedAt = DateTimeOffset.UtcNow;
    db.WorkTasks.Add(new WorkTask
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Title = "Completed synthetic commitment", Status = PracticeTimeStates.TaskCompleted, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var detail = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
    Assert.False(detail.LifecycleRoute);
    Assert.Equal((1, 1, 0, 99, true), (detail.ApplicableRequired, detail.CompletedRequired,
      detail.PendingRequired, detail.PercentComplete, detail.GateOpen));
    Assert.Contains("Completion profile is not approved for this service (D03).", detail.Gates);
    Assert.DoesNotContain("No issued release exists for this engagement yet.", detail.Gates);
    var client = (await EngagementProgressQuery.ForClientAsync(db, actor, f.ClientId)).Value!;
    var portfolio = (await PortfolioQuery.ProgressAsync(db, actor)).Value!;
    Assert.All(new[] { Assert.Single(client.Items), Assert.Single(portfolio.Items) }, item =>
    {
      Assert.Equal((99, true), (item.PercentComplete, item.GateOpen));
      Assert.Contains("Completion profile is not approved for this service (D03).", item.Gates);
    });
    var denied = await EngagementProgressQuery.GetAsync(db, PbcSeed.Actor(f.Client, "ClientUser"), f.EngagementId);
    Assert.False(denied.Succeeded);
    Assert.Null(denied.Value);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task RevocationDuringProjectionRefusesMixedScopesEvenWhenAnotherGrantRemains(bool export)
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    var siblingId = Guid.NewGuid();
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Engagements.Add(new Engagement { Id = siblingId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Staff", f.ClientId, siblingId)); await setup.SaveChangesAsync();
      await PortfolioWorkspaceSeed.PopulateAsync(setup, f, 3);
    }
    var interceptor = new RevokeDuringRead(async () => {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.EngagementId == f.EngagementId)
        .ExecuteUpdateAsync(s => s.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    if (export) Assert.False((await PortfolioQuery.ExportAsync(db, actor)).Succeeded);
    else Assert.False((await PortfolioQuery.WorkspaceAsync(db, actor)).Succeeded);
    Assert.True(interceptor.Fired);
    Assert.True((await PortfolioQuery.ListAsync(db, actor)).Succeeded);
  }

  [Fact]
  public async Task ProgressRefusesRevokedProgressAuthorityEvenWhenPortfolioAuthorityRemains()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Staff, "Staff");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Reviewer", f.ClientId, f.EngagementId));
      await setup.SaveChangesAsync();
      var before = await PortfolioQuery.ProgressAsync(setup, actor);
      Assert.True(before.Succeeded, before.Message);
      Assert.Equal(f.EngagementId, Assert.Single(before.Value!.Items).EngagementId);
    }
    var interceptor = new RevokeDuringRead(async () =>
    {
      await using var mutation = new AuditSphereDbContext(pg.Options);
      await mutation.RoleGrants.Where(g => g.UserId == f.Staff.Id && g.Role == "Staff")
        .ExecuteUpdateAsync(update => update.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    }, "FROM releases");
    await using var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(interceptor).Options);
    var result = await PortfolioQuery.ProgressAsync(db, actor);
    Assert.True(interceptor.Fired);
    Assert.False(result.Succeeded);
    Assert.Equal("scope.denied", result.ErrorCode);
    Assert.Null(result.Value);
    Assert.True((await PortfolioQuery.ListAsync(db, actor)).Succeeded);
  }

  [Fact]
  public async Task ProgressScopeIsAppliedBeforeBoundsAndTotalsStayHonest()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var hidden = Enumerable.Range(1, 50).Select(index => new Engagement
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = f.ClientId,
      PeriodEnd = $"2026-{index / 12 + 1:00}-{index % 12 + 1:00}",
      Status = "Active", CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    db.Engagements.AddRange(hidden);
    // Reviewer can read the ordinary portfolio, but cannot read its progress projection.
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Reviewer", f.ClientId, hidden[0].Id));
    await db.SaveChangesAsync();
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var client = await EngagementProgressQuery.ForClientAsync(db, staff, f.ClientId);
    var portfolio = await PortfolioQuery.ProgressAsync(db, staff);
    var all = await EngagementProgressQuery.ForClientAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId);
    Assert.Equal((true, 1, true, 1, true, 51),
      (client.Succeeded, client.Value?.Total ?? 0, portfolio.Succeeded, portfolio.Value?.Total ?? 0,
        all.Succeeded, all.Value?.Total ?? 0));
    Assert.Equal(f.EngagementId, Assert.Single(client.Value!.Items).EngagementId);
    Assert.Equal(f.EngagementId, Assert.Single(portfolio.Value!.Items).EngagementId);
    Assert.Equal(50, all.Value!.Items.Count);
    Assert.DoesNotContain(client.Value.Items, x => hidden.Any(h => h.Id == x.EngagementId));
    var next = await EngagementProgressQuery.ForClientPageAsync(db, PbcSeed.Actor(f.Admin, "Administrator"), f.ClientId, 1);
    Assert.True(next.Succeeded, next.Message);
    Assert.Equal((51, 1, 50), (next.Value!.Total, next.Value.Page, next.Value.PageSize));
    Assert.Equal(f.EngagementId, Assert.Single(next.Value.Items).EngagementId);
    Assert.Empty(all.Value.Items.Select(x => x.EngagementId).Intersect(next.Value.Items.Select(x => x.EngagementId)));
    var staffNext = await EngagementProgressQuery.ForClientPageAsync(db, staff, f.ClientId, 1);
    Assert.True(staffNext.Succeeded, staffNext.Message);
    Assert.Equal(1, staffNext.Value!.Total); Assert.Empty(staffNext.Value.Items);
    Assert.Equal("request.invalid", (await EngagementProgressQuery.ForClientPageAsync(db, staff, f.ClientId, -1)).ErrorCode);
  }

  private sealed class RevokeDuringRead(Func<Task> revoke, string marker = "FROM release_candidates") : DbCommandInterceptor
  {
    public bool Fired { get; private set; }
    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      if (!Fired && command.CommandText.Contains(marker, StringComparison.Ordinal))
      { Fired = true; await revoke(); }
      return result;
    }
  }

  [Fact]
  public async Task MetricsRecentWindowsExportAndScopeExpansionUseCurrentExplicitGrants()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var root = await PbcSeed.SeedAsync(pg); var foreign = await PbcSeed.SeedAsync(pg);
    var a = await SiblingClientSeed.SeedAsync(pg, root.FirmId, "PORTFOLIO-A");
    var b = await SiblingClientSeed.SeedAsync(pg, root.FirmId, "HIDDEN-PORTFOLIO-B");
    try
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      await PortfolioWorkspaceSeed.PopulateAsync(db, a.Fixture, 26);
      await PortfolioWorkspaceSeed.PopulateAsync(db, b.Fixture, 3);
      await PortfolioWorkspaceSeed.PopulateAsync(db, foreign, 3);
      var sibling = Guid.NewGuid();
      db.Engagements.Add(new Engagement { Id = sibling, FirmId = root.FirmId,
        PracticeClientId = a.Fixture.ClientId, Status = "Active", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      var siblingFixture = a.Fixture with { EngagementId = sibling };
      await PortfolioWorkspaceSeed.PopulateAsync(db, siblingFixture, 3);
      var actor = PbcSeed.Actor(a.Fixture.Staff, "Staff");
      var result = await PortfolioQuery.WorkspaceAsync(db, actor);
      Assert.True(result.Succeeded, result.Message); var v = result.Value!;
      Assert.Equal(new PortfolioMetrics(1, 1, 1, 1, 25, 1), v.Metrics);
      Assert.Equal(26, v.CandidateTotal); Assert.Equal(25, v.Candidates.Count); Assert.Equal(2, v.PackageTotal);
      Assert.All(v.Candidates, r => { Assert.Equal(a.Fixture.EngagementId, r.EngagementId); Assert.Equal("9007199254740993", r.TargetRevision); });
      Assert.All(v.Packages, r => Assert.Equal(a.Fixture.EngagementId, r.EngagementId));
      var empty = (await PortfolioQuery.WorkspaceAsync(db, actor, "NO MATCH")).Value!;
      Assert.Equal(v.Metrics, empty.Metrics); Assert.Empty(empty.Clients.Items); Assert.Empty(empty.Candidates); Assert.Empty(empty.Packages);
      var csv = await PortfolioQuery.ExportAsync(db, actor);
      Assert.True(csv.Succeeded, csv.Message); Assert.Equal(29, csv.Value!.Csv.Split('\n').Length);
      Assert.Contains("9007199254740993", csv.Value.Csv); Assert.DoesNotContain(b.Fixture.ClientId.ToString(), csv.Value.Csv);
      Assert.DoesNotContain(sibling.ToString(), System.Text.Json.JsonSerializer.Serialize(v));
      var grant = await db.RoleGrants.SingleAsync(g => g.UserId == a.Fixture.Staff.Id && g.Role == "Staff");
      grant.EngagementId = null; await db.SaveChangesAsync();
      var expanded = (await PortfolioQuery.WorkspaceAsync(db, actor)).Value!;
      Assert.Equal(2, expanded.Metrics.Engagements); Assert.Equal(4, expanded.Metrics.PendingOperations);
      grant.RevokedAt = DateTimeOffset.UtcNow; await db.SaveChangesAsync();
      Assert.False((await PortfolioQuery.WorkspaceAsync(db, actor)).Succeeded);
      Assert.False((await PortfolioQuery.ExportAsync(db, actor)).Succeeded);
      Assert.False((await PortfolioQuery.WorkspaceAsync(db, PbcSeed.Actor(a.Fixture.Client, "ClientUser"))).Succeeded);
    }
    finally { Directory.Delete(a.StagingRoot, true); Directory.Delete(b.StagingRoot, true); }
  }

  [Fact]
  public async Task ExportEscapesFormulaPrefixesQuotesMultilineNamesAndHonorsExactClientLimitBoundary()
  {
    await using var pg = await PgTestSchema.CreateAsync(); var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options); var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var client = await db.PracticeClients.SingleAsync(c => c.Id == f.ClientId);
    foreach (var value in new[] { "=1+1", "  +cmd", "-1", "@SUM(1)", "\tname", "\nname", "normal\"quoted\nname" })
    {
      client.LegalName = value; await db.SaveChangesAsync();
      var export = await PortfolioQuery.ExportAsync(db, actor);
      Assert.True(export.Succeeded, export.Message);
      var prefix = value.StartsWith("normal", StringComparison.Ordinal) ? "" : "'";
      Assert.Contains("\"" + prefix + value.Replace("\"", "\"\"") + "\"", export.Value!.Csv);
    }
    db.PracticeClients.AddRange(Enumerable.Range(0, PortfolioQuery.ExportClientLimit - 1).Select(i => new PracticeClient {
      Id = Guid.NewGuid(), FirmId = f.FirmId, LegalName = "Bounded synthetic " + i, CreatedAt = DateTimeOffset.UtcNow }));
    await db.SaveChangesAsync();
    var exactLimit = await PortfolioQuery.ExportAsync(db, actor);
    Assert.True(exactLimit.Succeeded, exactLimit.Message);
    Assert.Equal(PortfolioQuery.ExportClientLimit, exactLimit.Value!.ClientCount);
    Assert.Equal(PortfolioQuery.ExportClientLimit, exactLimit.Value.Csv.Split('\n')
      .Count(line => line.StartsWith("\"CLIENT\",", StringComparison.Ordinal)));

    db.PracticeClients.Add(new PracticeClient
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, LegalName = "Bounded synthetic overflow", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    Assert.Equal("export.limit", (await PortfolioQuery.ExportAsync(db, actor)).ErrorCode);
    Assert.True((await PortfolioQuery.ExportAsync(db, actor, "normal")).Succeeded);
    Assert.False((await PortfolioQuery.WorkspaceAsync(db, actor, new string('a', 101))).Succeeded);
  }
}
