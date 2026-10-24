using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Audit;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace AuditSphereOps.Domain.Tests;

/// <summary>T057 source-binding slice: audit lead schedules bind to the accepted R2R
/// source revision, and a newer accepted revision makes the bound schedules stale so
/// they cannot be relied on until re-prepared.</summary>
[Trait("Profile", "Database")]
public sealed class AuditScheduleSourceBindingTests
{
  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId,
    AppUser Preparer, AppUser Reviewer, Guid PeriodId, Guid BookId);

  [Fact(DisplayName = "Accepted R2R source binds to an audit schedule and supersession makes it stale")]
  public async Task ScheduleBindsToAcceptedSourceAndStalesOnSupersession()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "Partner");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    // 1. Accept the first sealed TB revision through the real Module 21 workflow.
    var firstDatasetId = await SeedSealedDatasetAsync(pg, fixture, "first", accepted: true);
    Guid firstDecisionId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, firstDatasetId, null, "acceptance-evidence-1"));
      Assert.True(accepted.Succeeded, accepted.Message);
      firstDecisionId = accepted.Value;
    }

    // 2. Prepare a lead schedule and bind it to that accepted revision.
    Guid scheduleId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditFieldworkService.CreateScheduleAsync(db, preparer, new CreateScheduleRequest(
        fixture.EngagementId, "RECEIVABLES_LEAD", "debtor-listing-2026", "receipt-debtors-1",
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "debits positive; credits negative",
        Hashing.Sha256Hex("debtor-listing"), 100m,
        [
          new("row-001", 1, "1100", "Trade receivables", 60m, "QAR", null, new DateOnly(2026, 12, 31), null, null, "{\"source\":\"listing\"}"),
          new("row-002", 2, "1100", "Trade receivables", 40m, "QAR", null, new DateOnly(2026, 12, 31), null, null, "{\"source\":\"listing\"}")
        ]));
      Assert.True(created.Succeeded, created.Message);
      scheduleId = created.Value!.ScheduleId;

      var bound = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        db, preparer, scheduleId, firstDecisionId);
      Assert.True(bound.Succeeded, bound.Message);
      Assert.Equal(firstDecisionId, bound.Value!.AcceptedSourceDecisionId);
      Assert.Equal(AccountingSourceKinds.TrialBalance, bound.Value.SourceKind);
      Assert.Equal(firstDatasetId, bound.Value.TrialBalanceDatasetId);

      // A schedule in a different engagement cannot borrow this accepted revision.
      var foreign = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        db, preparer, scheduleId, Guid.NewGuid());
      Assert.False(foreign.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, foreign.ErrorCode);

      // Freshly bound: nothing is stale yet.
      var fresh = await AuditScheduleSourceBindingService.GetSourceStalenessAsync(db, preparer, fixture.EngagementId);
      Assert.True(fresh.Succeeded, fresh.Message);
      Assert.Equal(0, fresh.Value!.StaleCount);
      Assert.Equal(firstDecisionId, fresh.Value.CurrentAcceptedSourceDecisionId);
    }

    // 3. A newer accepted revision supersedes the first and makes the bound schedule stale.
    var secondDatasetId = await SeedSealedDatasetAsync(pg, fixture, "second", accepted: true);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, secondDatasetId, null, "acceptance-evidence-2"));
      Assert.True(accepted.Succeeded, accepted.Message);

      var stale = await AuditScheduleSourceBindingService.GetSourceStalenessAsync(db, preparer, fixture.EngagementId);
      Assert.True(stale.Succeeded, stale.Message);
      Assert.Equal(1, stale.Value!.StaleCount);
      var row = Assert.Single(stale.Value.StaleSchedules);
      Assert.Equal(scheduleId, row.ScheduleId);
      Assert.Equal("RECEIVABLES_LEAD", row.ScheduleType);
      Assert.Equal(firstDecisionId, row.BoundSourceDecisionId);
      Assert.NotEqual(row.BoundSourceHash, row.CurrentSourceHash);

      // Invalidation moves the schedule out of a trusted state with a recorded reason.
      var invalidated = await AuditScheduleSourceBindingService.InvalidateStaleSchedulesAsync(
        db, preparer, fixture.EngagementId, "Accepted TB revision replaced after fieldwork started.");
      Assert.True(invalidated.Succeeded, invalidated.Message);
      Assert.Equal(1, invalidated.Value);
      var updated = await db.AuditSchedules.AsNoTracking().SingleAsync(x => x.Id == scheduleId);
      Assert.Equal(AuditScheduleStatuses.Unreconciled, updated.Status);
      Assert.Contains("SOURCE_SUPERSEDED", updated.CompletenessDecision);

      // Re-binding to the current accepted revision clears the staleness.
      var currentDecision = stale.Value.CurrentAcceptedSourceDecisionId!.Value;
      Assert.True((await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        db, preparer, scheduleId, currentDecision)).Succeeded);
      var clear = await AuditScheduleSourceBindingService.GetSourceStalenessAsync(db, preparer, fixture.EngagementId);
      Assert.Equal(0, clear.Value!.StaleCount);
    }
  }

  [Fact(DisplayName = "Binding refuses an unaccepted source and an approved schedule is immutable")]
  public async Task BindingRefusesPendingSourceAndApprovedSchedule()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "Partner");

    // A sealed but not-yet-accepted revision cannot be bound.
    var pendingDatasetId = await SeedSealedDatasetAsync(pg, fixture, "pending", accepted: false);
    Guid scheduleId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await AuditFieldworkService.CreateScheduleAsync(db, preparer, new CreateScheduleRequest(
        fixture.EngagementId, "PAYABLES_LEAD", "creditor-listing-2026", "receipt-creditors-1",
        new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
        new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "credits positive; debits negative",
        Hashing.Sha256Hex("creditor-listing"), -100m,
        [new("row-001", 1, "2100", "Trade payables", -100m, "QAR", null, new DateOnly(2026, 12, 31), null, null, "{\"source\":\"listing\"}")]));
      Assert.True(created.Succeeded, created.Message);
      scheduleId = created.Value!.ScheduleId;
    }

    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, pendingDatasetId, null, "evidence"));
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, denied.ErrorCode);

      // No accepted revision exists, so the engagement reports a null current source and
      // no stale schedules while nothing is bound.
      var report = await AuditScheduleSourceBindingService.GetSourceStalenessAsync(db, preparer, fixture.EngagementId);
      Assert.True(report.Succeeded);
      Assert.Null(report.Value!.CurrentAcceptedSourceDecisionId);
      Assert.Equal(0, report.Value.StaleCount);

      var unknown = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        db, preparer, scheduleId, Guid.NewGuid());
      Assert.False(unknown.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, unknown.ErrorCode);

      // Approved schedules are immutable.
      var schedule = await db.AuditSchedules.SingleAsync(x => x.Id == scheduleId);
      schedule.Status = AuditScheduleStatuses.Approved;
      await db.SaveChangesAsync();
      var acceptedFirst = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(db, reviewer,
        new AcceptSourceRevisionRequest(fixture.ClientId, fixture.EngagementId,
          AccountingSourceKinds.TrialBalance, pendingDatasetId, null, "evidence"));
      Assert.False(acceptedFirst.Succeeded);
    }
  }

  [Fact]
  public async Task DirectBindingRefusesAuthorityRevokedAfterAuthorizationWithoutChangingExistingBinding()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "Partner");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var administrator = User(fixture.FirmId, "binding-administrator");
    var firstDataset = await SeedSealedDatasetAsync(pg, fixture, "binding-before-revoke", accepted: true);
    var secondDataset = await SeedSealedDatasetAsync(pg, fixture, "binding-after-revoke", accepted: true);
    Guid scheduleId, firstDecisionId, secondDecisionId, grantId;
    string firstSourceHash;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(administrator);
      setup.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = fixture.FirmId, UserId = administrator.Id,
        Role = "Administrator", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = administrator.Id
      });
      await setup.SaveChangesAsync(timeout.Token);
      grantId = await setup.RoleGrants.Where(x => x.FirmId == fixture.FirmId &&
        x.UserId == preparer.UserId && x.Role == "Partner" && x.RevokedAt == null)
        .Select(x => x.Id).SingleAsync(timeout.Token);
      var first = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(setup, reviewer,
        new(fixture.ClientId, fixture.EngagementId, AccountingSourceKinds.TrialBalance,
          firstDataset, null, "Synthetic original binding evidence"), timeout.Token);
      Assert.True(first.Succeeded, first.Message);
      firstDecisionId = first.Value;
      var second = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(setup, reviewer,
        new(fixture.ClientId, fixture.EngagementId, AccountingSourceKinds.TrialBalance,
          secondDataset, null, "Synthetic attempted replacement evidence"), timeout.Token);
      Assert.True(second.Succeeded, second.Message);
      secondDecisionId = second.Value;
      var created = await AuditFieldworkService.CreateScheduleAsync(setup, preparer,
        new(fixture.EngagementId, "RECEIVABLES_LEAD", "binding-authority-listing", "binding-authority-receipt",
          new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
          new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "QAR", "debits positive; credits negative",
          Hashing.Sha256Hex("synthetic-binding-authority-listing"), 100m,
          [new("original-row", 1, "1100", "Synthetic receivables", 100m, "QAR",
            null, new DateOnly(2026, 12, 31), null, null, "{\"source\":\"synthetic-listing\"}")]), timeout.Token);
      Assert.True(created.Succeeded, created.Message);
      scheduleId = created.Value!.ScheduleId;
      var allowed = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        setup, preparer, scheduleId, firstDecisionId, timeout.Token);
      Assert.True(allowed.Succeeded, allowed.Message);
      Assert.Equal(firstDecisionId, allowed.Value!.AcceptedSourceDecisionId);
      firstSourceHash = allowed.Value.AcceptedSourceHash;
    }

    // Refuse a pending deletion without discarding it or flushing it as part of binding.
    await using (var pending = new AuditSphereDbContext(pg.Options))
    {
      var target = await pending.AuditSchedules.SingleAsync(x => x.Id == scheduleId, timeout.Token);
      pending.AuditSchedules.Remove(target);
      var refused = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        pending, preparer, scheduleId, secondDecisionId, timeout.Token);
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, refused.ErrorCode);
      Assert.Equal(EntityState.Deleted, pending.Entry(target).State);
      await using var unchanged = new AuditSphereDbContext(pg.Options);
      Assert.Equal(firstDecisionId, (await unchanged.AuditSchedules.AsNoTracking()
        .SingleAsync(x => x.Id == scheduleId, timeout.Token)).AcceptedSourceDecisionId);
    }

    var hook = new RevokeBeforeBindingDecisionOrScopeLock(async ct =>
    {
      await using var revokeDb = new AuditSphereDbContext(pg.Options);
      var revoked = await RoleAdministrationService.RevokeRoleGrantAsync(revokeDb,
        Actor(administrator, "Administrator"),
        new(grantId, Reason: "Synthetic direct binding authority revocation"), ct);
      Assert.True(revoked.Succeeded, revoked.Message);
    });
    var options = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(hook).Options;
    await using (var attempt = new AuditSphereDbContext(options))
    {
      var result = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceAsync(
        attempt, preparer, scheduleId, secondDecisionId, timeout.Token);
      Assert.True(hook.Invoked);
      await using var verify = new AuditSphereDbContext(pg.Options);
      Assert.NotNull((await verify.RoleGrants.AsNoTracking().SingleAsync(x => x.Id == grantId, timeout.Token)).RevokedAt);
      Assert.True(await verify.Users.Where(x => x.Id == preparer.UserId).Select(x => x.SessionEpoch)
        .SingleAsync(timeout.Token) > preparer.SessionEpoch);
      var retained = await verify.AuditSchedules.AsNoTracking().SingleAsync(x => x.Id == scheduleId, timeout.Token);
      Assert.False(result.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, result.ErrorCode);
      Assert.Equal(firstDecisionId, retained.AcceptedSourceDecisionId);
      Assert.Equal(firstSourceHash, retained.AcceptedSourceHash);
      Assert.Equal(AuditScheduleStatuses.Reconciled, retained.Status);
      Assert.Equal(string.Empty, retained.CompletenessDecision);
      Assert.Equal(100m, retained.SignedControlTotal);
      Assert.Equal(0m, retained.Residual);
      var row = Assert.Single(await verify.AuditScheduleRows.AsNoTracking()
        .Where(x => x.ScheduleId == scheduleId).ToListAsync(timeout.Token));
      Assert.Equal("original-row", row.StableRowId);
      Assert.Equal(100m, row.SignedAmount);
    }
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task ScheduleCompositionCommitsOnlyWithItsOwningTransaction(bool commit)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await SeedAsync(pg);
    var maker = Actor(f.Preparer, "Partner");
    var reviewer = Actor(f.Reviewer, "Reviewer");
    var datasetId = await SeedSealedDatasetAsync(pg, f, "atomic", accepted: true);
    Guid decisionId;
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.RoleGrants.Add(Grant(f.FirmId, f.Reviewer, "Reviewer", f.ClientId, f.EngagementId));
      await setup.SaveChangesAsync();
      var accepted = await AccountingSourceAcceptanceService.AcceptSourceRevisionAsync(setup,
        Actor(f.Reviewer, "AccountingReviewer"), new(f.ClientId, f.EngagementId,
          AccountingSourceKinds.TrialBalance, datasetId, null, "Synthetic independent transaction source"));
      Assert.True(accepted.Succeeded, accepted.Message);
      decisionId = accepted.Value;
    }
    var request = new CreateScheduleRequest(f.EngagementId, "RECEIVABLES_LEAD", "ATOMIC",
      "transaction-owned-listing", new DateTimeOffset(2026, 12, 31, 0, 0, 0, TimeSpan.Zero),
      new(2026, 1, 1), new(2026, 12, 31), "QAR", "debits positive; credits negative",
      Hashing.Sha256Hex("synthetic-atomic-listing"), 100m,
      [new("original", 2, "1100", "Synthetic original", 100m, "QAR", null, null, null, null, "{}")]);
    Guid scheduleId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(ErrorCodes.ProtectedState,
        (await AuditFieldworkService.CreateScheduleInTransactionAsync(db, maker, request)).ErrorCode);
      Assert.Equal(ErrorCodes.ProtectedState,
        (await AuditFieldworkService.ReviewScheduleInTransactionAsync(db, reviewer,
          new(Guid.NewGuid(), "No transaction", true))).ErrorCode);
      await using var tx = await db.Database.BeginTransactionAsync();
      var created = await AuditFieldworkService.CreateScheduleInTransactionAsync(db, maker, request);
      Assert.True(created.Succeeded, created.Message);
      scheduleId = created.Value!.ScheduleId;
      var repeated = await AuditFieldworkService.CreateScheduleInTransactionAsync(db, maker, request);
      Assert.True(repeated.Succeeded, repeated.Message);
      Assert.Equal(scheduleId, repeated.Value!.ScheduleId);
      var bound = await AuditScheduleSourceBindingService.BindScheduleToAcceptedSourceInTransactionAsync(
        db, maker, scheduleId, decisionId);
      Assert.True(bound.Succeeded, bound.Message);
      var reviewed = await AuditFieldworkService.ReviewScheduleInTransactionAsync(db, reviewer,
        new(scheduleId, "Synthetic independent review of the exact original", true));
      Assert.True(reviewed.Succeeded, reviewed.Message);
      Assert.Equal(AuditScheduleStatuses.Approved, reviewed.Value!.Status);
      Assert.Same(tx, db.Database.CurrentTransaction);
      // No child command may commit or dispose the caller's transaction, including dedupe.
      await using (var outside = new AuditSphereDbContext(pg.Options))
      {
        Assert.Empty(await outside.AuditSchedules.AsNoTracking().ToListAsync());
        Assert.Empty(await outside.AuditScheduleRows.AsNoTracking().ToListAsync());
      }
      if (commit) await tx.CommitAsync();
      else await tx.RollbackAsync();
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    var persisted = await verify.AuditSchedules.AsNoTracking().ToListAsync();
    if (!commit)
    {
      Assert.Empty(persisted);
      Assert.Empty(await verify.AuditScheduleRows.AsNoTracking().ToListAsync());
      return;
    }
    var retained = Assert.Single(persisted);
    Assert.Equal(scheduleId, retained.Id);
    Assert.Equal(decisionId, retained.AcceptedSourceDecisionId);
    Assert.Equal(AuditScheduleStatuses.Approved, retained.Status);
    Assert.Equal(f.Preparer.Id, retained.CreatedByUserId);
    Assert.Equal("Synthetic independent review of the exact original", retained.CompletenessDecision);
    var row = Assert.Single(await verify.AuditScheduleRows.AsNoTracking().ToListAsync());
    Assert.Equal("original", row.StableRowId);
    Assert.Equal(100m, row.SignedAmount);
  }

  private sealed class RevokeBeforeBindingDecisionOrScopeLock(Func<CancellationToken, Task> revoke) : DbCommandInterceptor
  {
    private bool authorizationRowsRead;
    public bool Invoked { get; private set; }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (command.CommandText.Contains("FROM role_grants", StringComparison.OrdinalIgnoreCase))
        authorizationRowsRead = true;
      return ValueTask.FromResult(result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
      CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
      // Baseline Bind reads the decision immediately after auth. A guarded Bind first
      // takes the firm lock; revoke BEFORE that query to avoid waiting behind our own lock.
      var decisionRead = command.CommandText.Contains("FROM source_acceptance_decisions", StringComparison.OrdinalIgnoreCase);
      var firmLock = command.CommandText.Contains("FROM firm_safety_states", StringComparison.OrdinalIgnoreCase) &&
        (command.CommandText.Contains("FOR SHARE", StringComparison.OrdinalIgnoreCase) ||
         command.CommandText.Contains("FOR UPDATE", StringComparison.OrdinalIgnoreCase));
      if (!Invoked && authorizationRowsRead && (decisionRead || firmLock))
      {
        Invoked = true;
        await revoke(cancellationToken);
      }
      return result;
    }
  }

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name,
    UserKind = "Staff", SessionEpoch = 1, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role, Guid clientId, Guid engagementId) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = clientId, EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var periodId = Guid.NewGuid();
    var bookId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "SOURCE BINDING CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = firmId, ClientId = clientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
      CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientReportingBooks.Add(new ClientReportingBook
    {
      Id = bookId, FirmId = firmId, ClientId = clientId, PeriodId = periodId,
      Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
      Status = AccountingWorkflowStates.Active, CreatedByUserId = preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "Partner", clientId, engagementId),
      Grant(firmId, reviewer, "AccountingReviewer", clientId, engagementId));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, preparer, reviewer, periodId, bookId);
  }

  private static async Task<Guid> SeedSealedDatasetAsync(
    PgTestSchema pg, Fixture fixture, string suffix, bool accepted)
  {
    var datasetId = Guid.CreateVersion7();
    var hash = Hashing.Sha256Hex("binding-" + suffix);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId,
        EngagementId = fixture.EngagementId, PeriodId = fixture.PeriodId, BookId = fixture.BookId,
        Basis = "STATUTORY", SourceKind = "Raw", Revision = 1,
        LegalEntityKey = suffix.ToUpperInvariant(), Currency = "QAR",
        RawFileSha256Hex = hash, NormalizedDatasetDigest = hash, Sha256Hex = hash, Balanced = true,
        ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = fixture.Preparer.Id
      });
      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1100",
          AccountName = "Trade receivables", Amount = 100m, Currency = "QAR", Entity = suffix.ToUpperInvariant()
        },
        new TrialBalanceRow
        {
          Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
          AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = suffix.ToUpperInvariant()
        });
      await db.SaveChangesAsync();
      var dataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      dataset.ImportState = TrialBalanceImportStates.Sealed;
      dataset.ValidationStatus = accepted ? "Accepted" : "Pending";
      await db.SaveChangesAsync();
    }
    return datasetId;
  }
}
