using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using System.Data.Common;
using System.Globalization;
using Xunit.Abstractions;

namespace AuditSphereOps.Domain.Tests;

public sealed class PortfolioWorkspaceTests(ITestOutputHelper output)
{
  private readonly ITestOutputHelper _output = output;

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

  [Fact]
  public async Task NonAuditProgressReachesCompletionWhenProfileIsApprovedAndEvidenceIsCurrent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    await using var db = new AuditSphereDbContext(pg.Options);
    var engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
    engagement.ServiceRoute = "AccountingOnly";
    engagement.PeriodStart = "2026-01-01";
    engagement.PeriodEnd = "2026-12-31";
    await db.SaveChangesAsync();

    await PortfolioWorkspaceSeed.PopulateAsync(db, f);
    var hold = await db.EngagementHolds.SingleAsync(x => x.EngagementId == f.EngagementId);
    hold.Released = true;
    hold.ReleasedAt = DateTimeOffset.UtcNow;
    db.WorkTasks.Add(new WorkTask
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
      Title = "Completed synthetic commitment", Status = PracticeTimeStates.TaskCompleted, CreatedAt = DateTimeOffset.UtcNow
    });

    var profileId = Guid.CreateVersion7();
    db.AccountingCapabilityProfiles.Add(new AccountingCapabilityProfile
    {
      Id = profileId, FirmId = f.FirmId, ClientId = f.ClientId, ServiceRoute = "AccountingOnly",
      ServiceKind = AccountingCapabilityServiceKinds.ClientBookkeeping, Framework = "IFRS",
      Status = AccountingWorkflowStates.Approved, CreatedAt = DateTimeOffset.UtcNow
    });
    db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
    {
      Id = Guid.CreateVersion7(), FirmId = f.FirmId, CapabilityProfileId = profileId,
      Stage = AccountingCapabilityAcceptanceStages.Released, Status = AccountingWorkflowStates.Approved,
      EvidenceReference = "APPROVED-CAPABILITY-001", DecidedByUserId = f.Admin.Id,
      DecidedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });

    db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
    {
      Id = Guid.CreateVersion7(), FirmId = f.FirmId, CapabilityProfileId = profileId,
      Stage = AccountingCapabilityAcceptanceStages.MethodOwnerApproval, Status = AccountingWorkflowStates.Approved,
      EvidenceReference = "APPROVED-METHOD-001", DecidedByUserId = f.Admin.Id,
      DecidedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });

    var periodId = Guid.CreateVersion7();
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "ACCRUAL", Currency = "USD", Status = AccountingWorkflowStates.Closed,
      ClosedByUserId = f.Admin.Id, ClosedAt = DateTimeOffset.UtcNow,
      CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
    });

    // REM-09: Seed unrelated historical reporting periods and unrelated service/client profiles.
    foreach (var year in new[] { 2023, 2024, 2025 })
    {
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = $"FY{year}",
        StartDate = new DateOnly(year, 1, 1), EndDate = new DateOnly(year, 12, 31),
        Basis = "ACCRUAL", Currency = "USD", Status = AccountingWorkflowStates.Closed,
        ClosedByUserId = f.Admin.Id, ClosedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
    }
    var auditClientId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient { Id = auditClientId, FirmId = f.FirmId, LegalName = "Audit Only Client", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.Add(new Engagement
    {
      Id = Guid.NewGuid(), FirmId = f.FirmId, PracticeClientId = auditClientId,
      ServiceRoute = "FinancialStatementAudit", PeriodStart = "2025-01-01", PeriodEnd = "2025-12-31",
      Status = "Active", CreatedAt = DateTimeOffset.UtcNow
    });
    foreach (var (clientId, route) in new[] { (f.ClientId, "TaxCompliance"), (f.ClientId, "Advisory"), (auditClientId, "AccountingOnly") })
    {
      var unrelatedProfileId = Guid.CreateVersion7();
      db.AccountingCapabilityProfiles.Add(new AccountingCapabilityProfile
      {
        Id = unrelatedProfileId, FirmId = f.FirmId, ClientId = clientId, ServiceRoute = route,
        ServiceKind = AccountingCapabilityServiceKinds.ClientBookkeeping, Framework = "IFRS",
        Status = AccountingWorkflowStates.Approved, CreatedAt = DateTimeOffset.UtcNow
      });
      db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, CapabilityProfileId = unrelatedProfileId,
        Stage = AccountingCapabilityAcceptanceStages.Released, Status = AccountingWorkflowStates.Approved,
        EvidenceReference = "UNRELATED-" + route, DecidedByUserId = f.Admin.Id,
        DecidedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
      });
    }

    await db.SaveChangesAsync();

    // Measure the pre-REM-09 candidate predicates on the same isolated detail context.
    // These are the former materialization queries, without the route/date restrictions.
    var broadRecorder = new CandidateQueryRecorder(requireCurrentContextFilters: false);
    (int Profiles, int Periods, int Packages) broadCounts;
    IReadOnlyList<(string Name, string Json)> broadPlans;
    int broadCandidateSelectCount;
    await using (var broadDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(broadRecorder).Options))
    {
      var evaluatedClientIds = new[] { f.ClientId };
      var evaluatedEngagementIds = new[] { f.EngagementId };
      _ = await broadDb.AccountingCapabilityProfiles.AsNoTracking()
        .Where(profile => profile.FirmId == f.FirmId && profile.ClientId.HasValue && evaluatedClientIds.Contains(profile.ClientId.Value) &&
          profile.Status != AccountingWorkflowStates.Retired && broadDb.AccountingCapabilityAcceptances.Any(acceptance =>
            acceptance.FirmId == profile.FirmId && acceptance.CapabilityProfileId == profile.Id &&
            (acceptance.Stage == AccountingCapabilityAcceptanceStages.MethodOwnerApproval ||
             acceptance.Stage == AccountingCapabilityAcceptanceStages.Released) &&
            acceptance.Status == AccountingWorkflowStates.Approved))
        .ToListAsync();
      _ = await broadDb.ClientReportingPeriods.AsNoTracking()
        .Where(period => period.FirmId == f.FirmId && evaluatedClientIds.Contains(period.ClientId)).ToListAsync();
      _ = await broadDb.FinancialPackages.AsNoTracking()
        .Where(package => package.FirmId == f.FirmId && evaluatedEngagementIds.Contains(package.EngagementId)).ToListAsync();
      broadCounts = await broadRecorder.MeasureLastBatchAsync(pg.ConnectionString);
      broadPlans = broadRecorder.LastPlans;
      broadCandidateSelectCount = broadRecorder.CandidateSelectCount;
      Assert.Equal(3, broadRecorder.CandidateSelectCount);
    }

    var candidateRecorder = new CandidateQueryRecorder();
    await using var measuredDb = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
      .AddInterceptors(candidateRecorder).Options);
    var detail = (await EngagementProgressQuery.GetAsync(measuredDb, actor, f.EngagementId)).Value!;
    Assert.False(detail.LifecycleRoute);
    Assert.Equal((1, 1, 0, 100, false), (detail.ApplicableRequired, detail.CompletedRequired,
      detail.PendingRequired, detail.PercentComplete, detail.GateOpen));
    Assert.Empty(detail.Gates);
    Assert.Contains("IFRS", detail.Disclosure);
    var scopedCounts = await candidateRecorder.MeasureLastBatchAsync(pg.ConnectionString);
    Assert.Equal((3, 4, 0), broadCounts);
    Assert.Equal((1, 1, 0), scopedCounts);
    Assert.Equal(3, candidateRecorder.CandidateSelectCount);
    _output.WriteLine($"REM-09 isolated PostgreSQL candidates (profiles, periods, packages): old predicates {broadCounts}; evaluated context {scopedCounts}.");
    _output.WriteLine($"Candidate SELECTs per BuildAsync: old predicates {broadCandidateSelectCount}; evaluated context {candidateRecorder.CandidateSelectCount}; EXPLAIN/count queries are test-only.");
    foreach (var plan in broadPlans)
      _output.WriteLine($"REM-09 old {plan.Name} EXPLAIN JSON: {plan.Json}");
    foreach (var plan in candidateRecorder.LastPlans)
      _output.WriteLine($"REM-09 scoped {plan.Name} EXPLAIN JSON: {plan.Json}");

    var clientProgress = await EngagementProgressQuery.ForClientAsync(measuredDb, actor, f.ClientId);
    Assert.Equal((1, 1, 0), await candidateRecorder.MeasureLastBatchAsync(pg.ConnectionString));
    var portfolioProgress = await PortfolioQuery.ProgressAsync(measuredDb, actor);
    Assert.Equal((1, 1, 0), await candidateRecorder.MeasureLastBatchAsync(pg.ConnectionString));
    Assert.True(clientProgress.Succeeded, clientProgress.Message);
    Assert.True(portfolioProgress.Succeeded, portfolioProgress.Message);
    Assert.Equal(detail.GateOpen, Assert.Single(clientProgress.Value!.Items, x => x.EngagementId == f.EngagementId).GateOpen);
    Assert.Equal(detail.GateOpen, Assert.Single(portfolioProgress.Value!.Items, x => x.EngagementId == f.EngagementId).GateOpen);

      // Genuinely distinct period bases for the same evaluated dates remain ambiguous until profile AccountingMethod narrows them.
      var cashPeriodId = Guid.CreateVersion7();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = cashPeriodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "FY2026-CASH",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "CASH", Currency = "USD", Status = AccountingWorkflowStates.Closed,
        ClosedByUserId = f.Admin.Id, ClosedAt = DateTimeOffset.UtcNow,
        CreatedByUserId = f.Admin.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      var ambiguousPeriodDetail = (await EngagementProgressQuery.GetAsync(measuredDb, actor, f.EngagementId)).Value!;
      Assert.True(ambiguousPeriodDetail.GateOpen);
      Assert.Contains("Ambiguous reporting periods exist for this context; exact period basis selection is required.", ambiguousPeriodDetail.Gates);
      Assert.Equal((1, 2, 0), await candidateRecorder.MeasureLastBatchAsync(pg.ConnectionString));
      db.ClientReportingPeriods.Remove(await db.ClientReportingPeriods.SingleAsync(p => p.Id == cashPeriodId));
      await db.SaveChangesAsync();

      // Negative case 1: Open reporting period blocks completion
      var period = await db.ClientReportingPeriods.SingleAsync(p => p.Id == periodId);
      period.Status = AccountingWorkflowStates.Draft;
      await db.SaveChangesAsync();
      var unclosedDetail = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
      Assert.True(unclosedDetail.GateOpen);
      Assert.Equal(99, unclosedDetail.PercentComplete);
      Assert.Contains("The reporting period is not closed.", unclosedDetail.Gates);

      // Negative case 2: An engagement without an issued release remains open
      period.Status = AccountingWorkflowStates.Closed;
      var unreleasedEngagementId = Guid.NewGuid();
      db.Engagements.Add(new Engagement
      {
        Id = unreleasedEngagementId, FirmId = f.FirmId, PracticeClientId = f.ClientId,
        ServiceRoute = "AccountingOnly", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.Add(new WorkTask
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = unreleasedEngagementId,
        Title = "Synthetic unreleased task", Status = PracticeTimeStates.TaskCompleted, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var unreleasedDetail = (await EngagementProgressQuery.GetAsync(db, actor, unreleasedEngagementId)).Value!;
      Assert.True(unreleasedDetail.GateOpen);
      Assert.Equal(99, unreleasedDetail.PercentComplete);
      Assert.Contains("No issued release exists for this engagement yet.", unreleasedDetail.Gates);

    // T07: Ambiguous profiles without explicit ServiceProfileId selection blocks completion
    var secondProfileId = Guid.CreateVersion7();
    db.AccountingCapabilityProfiles.Add(new AccountingCapabilityProfile
    {
      Id = secondProfileId, FirmId = f.FirmId, ClientId = f.ClientId, ServiceRoute = "AccountingOnly",
      ServiceKind = AccountingCapabilityServiceKinds.ClientBookkeeping, Framework = "US_GAAP",
      Status = AccountingWorkflowStates.Approved, CreatedAt = DateTimeOffset.UtcNow
    });
    db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
    {
      Id = Guid.CreateVersion7(), FirmId = f.FirmId, CapabilityProfileId = secondProfileId,
      Stage = AccountingCapabilityAcceptanceStages.Released, Status = AccountingWorkflowStates.Approved,
      EvidenceReference = "APPROVED-CAPABILITY-002", DecidedByUserId = f.Admin.Id,
      DecidedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var ambiguousProfileDetail = (await EngagementProgressQuery.GetAsync(measuredDb, actor, f.EngagementId)).Value!;
    Assert.True(ambiguousProfileDetail.GateOpen);
    Assert.Contains("Ambiguous completion profiles exist for this service; exact profile selection is required.", ambiguousProfileDetail.Gates);
    Assert.Equal((2, 1, 0), await candidateRecorder.MeasureLastBatchAsync(pg.ConnectionString));

    // Binding exact ServiceProfileId resolves ambiguity
    engagement = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
    engagement.ServiceProfileId = profileId.ToString("D");
    await db.SaveChangesAsync();

    var resolvedProfileDetail = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
    Assert.DoesNotContain("Ambiguous completion profiles exist for this service; exact profile selection is required.", resolvedProfileDetail.Gates);

    // An unavailable explicit pin must not silently fall back to an accepted profile.
    engagement.ServiceProfileId = Guid.CreateVersion7().ToString("D");
    await db.SaveChangesAsync();
    var unavailablePin = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
    Assert.Contains("Completion profile is not approved for this service (D03).", unavailablePin.Gates);
    var unapprovedProfileId = Guid.CreateVersion7();
    db.AccountingCapabilityProfiles.Add(new AccountingCapabilityProfile
    {
      Id = unapprovedProfileId, FirmId = f.FirmId, ClientId = f.ClientId, ServiceRoute = "AccountingOnly",
      ServiceKind = AccountingCapabilityServiceKinds.ClientBookkeeping, Framework = "IFRS",
      Status = AccountingWorkflowStates.Draft, CreatedAt = DateTimeOffset.UtcNow
    });
    engagement.ServiceProfileId = unapprovedProfileId.ToString("D");
    await db.SaveChangesAsync();
    var unapprovedPin = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
    Assert.Contains("Completion profile is not approved for this service (D03).", unapprovedPin.Gates);
    engagement.ServiceProfileId = profileId.ToString("D");
    await db.SaveChangesAsync();

    // T07: a Workpaper release cannot satisfy an EntityReporting profile that requires its exact approved package.
    var entityProfile = await db.AccountingCapabilityProfiles.SingleAsync(x => x.Id == profileId);
    entityProfile.ServiceKind = AccountingCapabilityServiceKinds.EntityReporting;
    await db.SaveChangesAsync();
    var wrongReleaseDetail = (await EngagementProgressQuery.GetAsync(db, actor, f.EngagementId)).Value!;
    Assert.Contains("No issued release of the exact approved financial package exists for this period.", wrongReleaseDetail.Gates);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task EntityReportingProgressUsesExactPackageContextAndAuthorizedReadersSeeReadinessOnly()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await PbcSeed.SeedAsync(pg);
    var users = new Dictionary<string, AppUser>
    {
      ["associate"] = PbcSeed.User(f.FirmId, "Staff"),
      ["senior"] = PbcSeed.User(f.FirmId, "Staff"),
      ["partner"] = PbcSeed.User(f.FirmId, "Staff")
    };
    await using (var seed = new AuditSphereDbContext(pg.Options))
    {
      seed.Users.AddRange(users.Values);
      await seed.SaveChangesAsync();
    }
    var bundleScope = new CompletionBundleScope(f.FirmId, f.ClientId, f.EngagementId, users);
    var (packageId, releaseId) = await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, bundleScope);
    var staff = PbcSeed.Actor(f.Staff, "Staff");
    var preparer = bundleScope.A("associate", "AccountingPreparer");
    var partner = bundleScope.A("partner", "Partner");
    Guid periodId, statutoryProfileId, cashProfileId;
    var now = DateTimeOffset.UtcNow;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var package = await db.FinancialPackages.SingleAsync(x => x.Id == packageId);
      periodId = package.PeriodId!.Value;
      var engagement = await db.Engagements.SingleAsync(x => x.FirmId == f.FirmId && x.Id == f.EngagementId);
      engagement.ServiceRoute = "AccountingOnly";
      engagement.PeriodStart = "2026-01-01";
      engagement.PeriodEnd = "2026-12-31";
      var period = await db.ClientReportingPeriods.SingleAsync(x => x.Id == periodId);
      period.Status = AccountingWorkflowStates.Active;
      period.ClosedByUserId = null;
      period.ClosedAt = null;

      statutoryProfileId = AddApprovedEntityProfile(db, f, "STATUTORY", package.Framework, package.Currency, now);
      var cashPeriodId = Guid.CreateVersion7();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = cashPeriodId, FirmId = f.FirmId, ClientId = f.ClientId, PeriodCode = "BUNDLE-CASH-2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "CASH", Currency = package.Currency, Status = AccountingWorkflowStates.Closed,
        ClosedByUserId = users["partner"].Id, ClosedAt = now,
        CreatedByUserId = users["associate"].Id, CreatedAt = now
      });
      cashProfileId = AddApprovedEntityProfile(db, f, "CASH", package.Framework, package.Currency, now);
      db.Engagements.Single(x => x.Id == f.EngagementId).ServiceProfileId = statutoryProfileId.ToString("D");

      var digest = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex("foreign-cash-context-package");
      var datasetId = Guid.CreateVersion7();
      var planId = Guid.CreateVersion7();
      var mappingId = Guid.CreateVersion7();
      var snapshotId = Guid.CreateVersion7();
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PeriodId = cashPeriodId, Basis = "CASH", SourceKind = "Raw", Revision = 1,
        LegalEntityKey = f.ClientId.ToString("D"), Currency = package.Currency,
        RawFileSha256Hex = digest, NormalizedDatasetDigest = digest, Sha256Hex = digest,
        Balanced = true, ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed,
        ImportedAt = now, ImportedByUserId = users["associate"].Id
      });
      db.AdjustmentPlans.Add(new AdjustmentPlan
      {
        Id = planId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        BaseDatasetId = datasetId, Status = "Finalized", ResultHash = digest,
        CreatedByUserId = users["associate"].Id, CreatedAt = now
      });
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        DatasetId = datasetId, Version = 1, Generation = 1, TaxonomyVersion = "test-taxonomy-v1",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
        CreatedByUserId = users["associate"].Id, ApprovedByUserId = users["senior"].Id,
        ApprovedAt = now, CreatedAt = now
      });
      db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
      {
        Id = snapshotId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        BaseDatasetId = datasetId, AdjustmentPlanId = planId, Currency = package.Currency,
        ResultHash = digest, CreatedByUserId = users["associate"].Id, CreatedAt = now
      });
      db.FinancialPackages.Add(new FinancialPackage
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        AdjustedDatasetId = snapshotId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
        PeriodId = cashPeriodId, Basis = "CASH", Framework = package.Framework,
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", TaxonomyVersion = "test-taxonomy-v1",
        TemplateVersion = "test-template-v1", CalculationEngineVersion = "test-engine-v1",
        CalculationHash = digest, Currency = package.Currency, Status = AccountingPackageStates.PackageReviewRequired,
        CreatedAt = now
      });
      await db.SaveChangesAsync();

      var readiness = await PeriodCloseReadinessQuery.GetReadinessAsync(db, preparer, periodId);
      Assert.True(readiness.Succeeded, readiness.Message);
      Assert.True(readiness.Value!.CanClose, string.Join("; ", readiness.Value.Blockers.Select(x => x.Code)));
      Assert.Equal(1, readiness.Value.PackageCount);

      var deniedClose = await ClientAccountingService.ClosePeriodAsync(db, preparer, periodId, "Readiness is not close authority.");
      Assert.False(deniedClose.Succeeded);
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, deniedClose.ErrorCode);

      period.Status = AccountingWorkflowStates.Closed;
      period.ClosedByUserId = users["partner"].Id;
      period.ClosedAt = now;
      await db.SaveChangesAsync();

      var exact = await EngagementProgressQuery.GetAsync(db, staff, f.EngagementId);
      Assert.True(exact.Succeeded, exact.Message);
      Assert.False(exact.Value!.GateOpen);
      Assert.Empty(exact.Value.Gates);
      Assert.DoesNotContain(packageId.ToString("D"), string.Join(" ", exact.Value.Gates));

      var wrongContext = await db.Engagements.SingleAsync(x => x.Id == f.EngagementId);
      wrongContext.ServiceProfileId = cashProfileId.ToString("D");
      await db.SaveChangesAsync();
      var cash = await EngagementProgressQuery.GetAsync(db, staff, f.EngagementId);
      Assert.True(cash.Succeeded, cash.Message);
      Assert.True(cash.Value!.GateOpen);
      Assert.Contains("Every financial package for the period needs current management, accounting, and partner approval.", cash.Value.Gates);
      Assert.Contains("No issued release of the exact approved financial package exists for this period.", cash.Value.Gates);

      wrongContext.ServiceProfileId = statutoryProfileId.ToString("D");
      await db.SaveChangesAsync();
      var unauthorizedReview = await FinancialPackageReviewService.RecordAsync(db, staff,
        new(packageId, FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewDecisions.Rejected,
          FinancialPackageReviewEvidenceModes.SignedIn, "staff-review-attempt", "A progress reader cannot record package approval."));
      Assert.False(unauthorizedReview.Succeeded);
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, unauthorizedReview.ErrorCode);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    var detail = await EngagementProgressQuery.GetAsync(verify, staff, f.EngagementId);
    var client = await EngagementProgressQuery.ForClientAsync(verify, staff, f.ClientId);
    var portfolio = await PortfolioQuery.ProgressAsync(verify, staff);
    Assert.True(detail.Succeeded, detail.Message);
    Assert.True(client.Succeeded, client.Message);
    Assert.True(portfolio.Succeeded, portfolio.Message);
    var exactClientItem = Assert.Single(client.Value!.Items, x => x.EngagementId == f.EngagementId);
    var exactPortfolioItem = Assert.Single(portfolio.Value!.Items, x => x.EngagementId == f.EngagementId);
    Assert.False(detail.Value!.GateOpen);
    Assert.False(exactClientItem.GateOpen);
    Assert.False(exactPortfolioItem.GateOpen);

    // A retained release only satisfies EntityReporting when both its package and joined candidate
    // still name the exact current package revision and target. These mutations simulate stale
    // retained linkage; they do not alter the append-only Release evidence itself.
    var pins = await verify.Releases.AsNoTracking().Where(x => x.Id == releaseId)
      .Select(x => new { x.ReleaseCandidateId, x.PinnedCompositionId, x.PinnedCompositionManifestDigest }).SingleAsync();
    Assert.NotNull(pins.PinnedCompositionId);
    var packageRevision = await verify.FinancialPackages.AsNoTracking()
      .Where(x => x.Id == packageId).Select(x => x.Revision).SingleAsync();
    const string missingExactRelease = "No issued release of the exact approved financial package exists for this period.";

    async Task AssertStaleLinkKeepsAllProgressSurfacesOpen(
      Guid? currentCompositionId = null, string? currentCompositionDigest = null)
    {
      var staleDetail = await EngagementProgressQuery.GetAsync(verify, staff, f.EngagementId);
      var staleClient = await EngagementProgressQuery.ForClientAsync(verify, staff, f.ClientId);
      var stalePortfolio = await PortfolioQuery.ProgressAsync(verify, staff);
      Assert.True(staleDetail.Succeeded, staleDetail.Message);
      Assert.True(staleClient.Succeeded, staleClient.Message);
      Assert.True(stalePortfolio.Succeeded, stalePortfolio.Message);
      var clientItem = Assert.Single(staleClient.Value!.Items, x => x.EngagementId == f.EngagementId);
      var portfolioItem = Assert.Single(stalePortfolio.Value!.Items, x => x.EngagementId == f.EngagementId);
      Assert.All(new[] { staleDetail.Value!.GateOpen, clientItem.GateOpen, portfolioItem.GateOpen },
        gateOpen => Assert.True(gateOpen));
      Assert.Contains(missingExactRelease, staleDetail.Value.Gates);
      Assert.Contains(missingExactRelease, clientItem.Gates);
      Assert.Contains(missingExactRelease, portfolioItem.Gates);
      var projected = string.Join(" ", staleDetail.Value.Gates.Concat(clientItem.Gates).Concat(portfolioItem.Gates));
      Assert.DoesNotContain(packageId.ToString("D"), projected);
      Assert.DoesNotContain(pins.PinnedCompositionId!.Value.ToString("D"), projected);
      Assert.DoesNotContain(pins.PinnedCompositionManifestDigest!, projected);
      if (currentCompositionId is { } compositionId)
        Assert.DoesNotContain(compositionId.ToString("D"), projected);
      if (currentCompositionDigest is not null)
        Assert.DoesNotContain(currentCompositionDigest, projected);
    }

    async Task AssertExactReleaseKeepsAllProgressSurfacesClosed()
    {
      var exactDetail = await EngagementProgressQuery.GetAsync(verify, staff, f.EngagementId);
      var exactClient = await EngagementProgressQuery.ForClientAsync(verify, staff, f.ClientId);
      var exactPortfolio = await PortfolioQuery.ProgressAsync(verify, staff);
      Assert.True(exactDetail.Succeeded, exactDetail.Message);
      Assert.True(exactClient.Succeeded, exactClient.Message);
      Assert.True(exactPortfolio.Succeeded, exactPortfolio.Message);
      Assert.False(exactDetail.Value!.GateOpen);
      Assert.False(Assert.Single(exactClient.Value!.Items, x => x.EngagementId == f.EngagementId).GateOpen);
      Assert.False(Assert.Single(exactPortfolio.Value!.Items, x => x.EngagementId == f.EngagementId).GateOpen);
      Assert.DoesNotContain(missingExactRelease, exactDetail.Value.Gates);
    }

    var foreignTargetId = Guid.CreateVersion7();
    Assert.Equal(1, await verify.ReleaseCandidates.Where(x => x.Id == pins.ReleaseCandidateId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.TargetId, foreignTargetId)));
    await AssertStaleLinkKeepsAllProgressSurfacesOpen();

    Assert.Equal(1, await verify.ReleaseCandidates.Where(x => x.Id == pins.ReleaseCandidateId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.TargetId, packageId)
        .SetProperty(x => x.TargetRevision, packageRevision + 1)));
    await AssertStaleLinkKeepsAllProgressSurfacesOpen();

    Assert.Equal(1, await verify.ReleaseCandidates.Where(x => x.Id == pins.ReleaseCandidateId)
      .ExecuteUpdateAsync(update => update.SetProperty(x => x.TargetRevision, packageRevision)));
    await AssertExactReleaseKeepsAllProgressSurfacesClosed();

    var issuedCandidate = await verify.ReleaseCandidates.AsNoTracking()
      .SingleAsync(x => x.Id == pins.ReleaseCandidateId);
    var sourceLayoutId = await verify.FinancialStatementPresentationRevisions.AsNoTracking()
      .Where(x => x.Id == issuedCandidate.PresentationRevisionId)
      .Select(x => x.LayoutVersionId).SingleAsync();
    var alternateLayoutId = await CompletionBundleFixture.CreateAlternateStatementLayoutAsync(verify,
      preparer, bundleScope.A("senior", "AccountingReviewer"), f.ClientId, sourceLayoutId);
    var presentationB = await FinancialStatementService.PinPresentationAsync(verify, preparer, packageId, alternateLayoutId);
    Assert.True(presentationB.Succeeded, presentationB.Message);
    Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(verify,
      bundleScope.A("senior", "AccountingReviewer"), presentationB.Value!.RevisionId,
      FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
    Assert.True((await StatementContentReviewCall.RecordAsync(verify, bundleScope.A("senior", "AccountingReviewer"),
      packageId, presentationB.Value.RevisionId)).Succeeded);
    var presentationCandidateB = await FinancialStatementService.PromotePresentationToCandidateAsync(verify,
      bundleScope.A("senior", "AccountingReviewer"), packageId, presentationB.Value.RevisionId);
    Assert.True(presentationCandidateB.Succeeded, presentationCandidateB.Message);
    var compositionB = await FinancialStatementService.SelectCompositionAsync(verify, preparer,
      packageId, presentationCandidateB.Value!.RevisionId);
    Assert.True(compositionB.Succeeded, compositionB.Message);
    Assert.NotEqual(pins.PinnedCompositionId, compositionB.Value!.Id);
    Assert.NotEqual(pins.PinnedCompositionManifestDigest, compositionB.Value.ManifestDigest);
    foreach (var (actor, stage, mode) in new[]
    {
      (preparer, FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewEvidenceModes.Offline),
      (bundleScope.A("senior", "AccountingReviewer"), FinancialPackageReviewStages.AccountingReview,
        FinancialPackageReviewEvidenceModes.SignedIn),
      (partner, FinancialPackageReviewStages.PartnerApproval, FinancialPackageReviewEvidenceModes.SignedIn)
    })
    {
      var decision = await FinancialPackageReviewService.RecordAsync(verify, actor,
        new(packageId, stage, FinancialPackageReviewDecisions.Approved, mode,
          "progress-composition-B-" + stage, "Reviewed the exact current composition B.",
          presentationCandidateB.Value.RevisionId, null, compositionB.Value.Id, compositionB.Value.ManifestDigest));
      Assert.True(decision.Succeeded, decision.Message);
    }
    await AssertStaleLinkKeepsAllProgressSurfacesOpen(compositionB.Value.Id, compositionB.Value.ManifestDigest);

    var packageForReleaseB = await verify.FinancialPackages.AsNoTracking().SingleAsync(x => x.Id == packageId);
    var policyGeneration = await verify.FirmSafetyStates.AsNoTracking()
      .Where(x => x.Id == f.FirmId).Select(x => x.PolicyGeneration).SingleAsync();
    var releaseManifestB = System.Text.Encoding.UTF8.GetBytes("exact current statement composition B release");
    var releaseDigestB = AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(releaseManifestB);
    var approvalB = await ApprovalService.CreateAsync(verify, partner,
      new(ReleaseTargetKinds.FinancialPackage, packageId, packageForReleaseB.Revision,
        packageForReleaseB.Generation, policyGeneration, releaseDigestB));
    Assert.True(approvalB.Succeeded, approvalB.Message);
    var releaseCandidateB = await ReleaseService.CreateCandidateAsync(verify, partner,
      new(approvalB.Value, ReleaseTargetKinds.FinancialPackage, packageId, packageForReleaseB.Revision,
        packageForReleaseB.Generation, policyGeneration, releaseDigestB));
    Assert.True(releaseCandidateB.Succeeded, releaseCandidateB.Message);
    var checkpointRoot = Path.Combine(Path.GetTempPath(), "AuditSphereOps-Tests", Guid.NewGuid().ToString("N"));
    try
    {
      var checkpointB = await ReleaseCheckpointService.RecordCheckpointDirectAsync(verify,
        new LocalAppendOnlyCheckpointStore(checkpointRoot), partner,
        new(releaseCandidateB.Value, 1, "progress-composition-B-release", releaseDigestB, releaseManifestB));
      Assert.True(checkpointB.Succeeded, checkpointB.Message);
      var issuedB = await ReleaseService.IssueAsync(verify, partner,
        new(releaseCandidateB.Value, 1, releaseDigestB, "progress-composition-B-release"));
      Assert.True(issuedB.Succeeded, issuedB.Message);
      var releaseB = await verify.Releases.AsNoTracking().SingleAsync(x => x.Id == issuedB.Value);
      Assert.Equal(compositionB.Value.Id, releaseB.PinnedCompositionId);
      Assert.Equal(compositionB.Value.ManifestDigest, releaseB.PinnedCompositionManifestDigest);
      var retainedReleaseA = await verify.Releases.AsNoTracking().SingleAsync(x => x.Id == releaseId);
      Assert.Equal(packageId, retainedReleaseA.PackageId);
      Assert.Equal(pins.PinnedCompositionId, retainedReleaseA.PinnedCompositionId);
      Assert.Equal(pins.PinnedCompositionManifestDigest, retainedReleaseA.PinnedCompositionManifestDigest);
      await AssertExactReleaseKeepsAllProgressSurfacesClosed();
    }
    finally
    {
      if (Directory.Exists(checkpointRoot)) Directory.Delete(checkpointRoot, recursive: true);
    }
  }

  private static Guid AddApprovedEntityProfile(AuditSphereDbContext db, PbcSeed.Fixture f,
    string basis, string framework, string currency, DateTimeOffset now)
  {
    var id = Guid.CreateVersion7();
    db.AccountingCapabilityProfiles.Add(new AccountingCapabilityProfile
    {
      Id = id, FirmId = f.FirmId, ClientId = f.ClientId, ServiceRoute = "AccountingOnly",
      ServiceKind = AccountingCapabilityServiceKinds.EntityReporting, Framework = framework,
      AccountingMethod = basis, ReportingCurrency = currency, Status = AccountingWorkflowStates.Approved,
      CreatedAt = now
    });
    foreach (var stage in new[] { AccountingCapabilityAcceptanceStages.Released, AccountingCapabilityAcceptanceStages.MethodOwnerApproval })
      db.AccountingCapabilityAcceptances.Add(new AccountingCapabilityAcceptance
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, CapabilityProfileId = id,
        Stage = stage, Status = AccountingWorkflowStates.Approved,
        EvidenceReference = "APPROVED-PROFILE-" + basis, DecidedByUserId = f.Admin.Id,
        DecidedAt = now, CreatedAt = now
      });
    return id;
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

  private sealed class CandidateQueryRecorder : DbCommandInterceptor
  {
    private readonly bool _requireCurrentContextFilters;
    private (string Sql, NpgsqlParameter[] Parameters)? _profiles;
    private (string Sql, NpgsqlParameter[] Parameters)? _periods;
    private (string Sql, NpgsqlParameter[] Parameters)? _packages;
    public int CandidateSelectCount { get; private set; }
    public IReadOnlyList<(string Name, string Json)> LastPlans { get; private set; } = [];

    public CandidateQueryRecorder(bool requireCurrentContextFilters = true) =>
      _requireCurrentContextFilters = requireCurrentContextFilters;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      var sql = command.CommandText;
      var parameters = command.Parameters.Cast<NpgsqlParameter>().Select(p => p.Clone()).ToArray();
      if (sql.Contains("FROM accounting_capability_profiles", StringComparison.Ordinal))
      {
        CandidateSelectCount++;
        if (_requireCurrentContextFilters) Assert.Contains("service_route", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("LIMIT", sql, StringComparison.Ordinal);
        _profiles = (sql, parameters);
      }
      else if (sql.Contains("FROM client_reporting_periods", StringComparison.Ordinal))
      {
        CandidateSelectCount++;
        if (_requireCurrentContextFilters)
        {
          Assert.Contains("start_date", sql, StringComparison.Ordinal);
          Assert.Contains("end_date", sql, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("LIMIT", sql, StringComparison.Ordinal);
        _periods = (sql, parameters);
      }
      else if (sql.Contains("FROM financial_packages", StringComparison.Ordinal))
      {
        CandidateSelectCount++;
        if (_requireCurrentContextFilters)
        {
          Assert.Contains("period_start", sql, StringComparison.Ordinal);
          Assert.Contains("period_end", sql, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("LIMIT", sql, StringComparison.Ordinal);
        _packages = (sql, parameters);
      }
      return ValueTask.FromResult(result);
    }

    public async Task<(int Profiles, int Periods, int Packages)> MeasureLastBatchAsync(string connectionString)
    {
      await using var connection = new NpgsqlConnection(connectionString);
      await connection.OpenAsync();
      var profiles = await CountAndExplainAsync(connection, _profiles!.Value);
      var periods = await CountAndExplainAsync(connection, _periods!.Value);
      var packages = await CountAndExplainAsync(connection, _packages!.Value);
      LastPlans = [("profiles", profiles.Plan), ("periods", periods.Plan), ("packages", packages.Plan)];
      return (profiles.Count, periods.Count, packages.Count);
    }

    private static async Task<(int Count, string Plan)> CountAndExplainAsync(
      NpgsqlConnection connection, (string Sql, NpgsqlParameter[] Parameters) captured)
    {
      string plan;
      await using (var explain = new NpgsqlCommand("EXPLAIN (FORMAT JSON) " + captured.Sql, connection))
      {
        foreach (var parameter in captured.Parameters) explain.Parameters.Add(parameter.Clone());
        plan = Convert.ToString(await explain.ExecuteScalarAsync(), CultureInfo.InvariantCulture)!;
        Assert.False(string.IsNullOrWhiteSpace(plan));
      }
      await using var count = new NpgsqlCommand($"SELECT count(*)::int FROM ({captured.Sql}) AS candidate_rows", connection);
      foreach (var parameter in captured.Parameters) count.Parameters.Add(parameter.Clone());
      return (Convert.ToInt32(await count.ExecuteScalarAsync(), CultureInfo.InvariantCulture), plan);
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
