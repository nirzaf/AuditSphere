using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task ProfileRevisionReference_ResolvesImmutableHistoryAndLeavesLegacyGapUnbackfilled()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var created = await ClientAccountingService.CreateProfileAsync(db, preparer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER-A", "SOURCE-A"));
    Assert.True(created.Succeeded, created.Message);

    var firstRef = new ClientAccountingProfileRevisionRef(created.Value, 1);
    var resolved = await ClientAccountingService.ResolveProfileRevisionAsync(db, preparer, scope.ClientA, firstRef);
    Assert.True(resolved.Succeeded, resolved.Message);
    Assert.Equal(firstRef, resolved.Value!.Reference);
    Assert.NotEqual(Guid.Empty, resolved.Value.RevisionId);
    Assert.Null(resolved.Value.PreviousRevisionId);
    Assert.Equal(scope.ClientA, resolved.Value.ClientId);
    Assert.Equal("QAR", resolved.Value.FunctionalCurrency);

    var missingReason = await ClientAccountingService.ReviseProfileAsync(db, preparer, firstRef,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "USD", 1, 1, "LEDGER-A", "SOURCE-A"));
    Assert.False(missingReason.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, missingReason.ErrorCode);

    var revised = await ClientAccountingService.ReviseProfileAsync(db, preparer, firstRef,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "USD", 1, 1, "LEDGER-B", "SOURCE-B",
        Reason: "Test profile revision"));
    Assert.True(revised.Succeeded, revised.Message);
    Assert.Equal(2, revised.Value);

    var historical = await ClientAccountingService.ResolveProfileRevisionAsync(db, preparer, scope.ClientA, firstRef);
    Assert.True(historical.Succeeded, historical.Message);
    Assert.Equal(resolved.Value.RevisionId, historical.Value!.RevisionId);
    Assert.Equal("LEDGER-A", historical.Value.SourceSystem);
    Assert.Equal("QAR", historical.Value.FunctionalCurrency);

    var secondRef = new ClientAccountingProfileRevisionRef(created.Value, 2);
    var second = await ClientAccountingService.ResolveProfileRevisionAsync(db, preparer, scope.ClientA, secondRef);
    Assert.True(second.Succeeded, second.Message);
    Assert.NotEqual(historical.Value.RevisionId, second.Value!.RevisionId);
    Assert.Equal(historical.Value.RevisionId, second.Value.PreviousRevisionId);
    Assert.Equal("Test profile revision", second.Value.Reason);
    Assert.Equal("USD", second.Value.FunctionalCurrency);

    var staleCommand = await ClientAccountingService.ReviseProfileAsync(db, preparer, firstRef,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "USD", 1, 1, "LEDGER-B", "SOURCE-B"));
    Assert.False(staleCommand.Succeeded);
    Assert.Equal(ErrorCodes.StaleRevision, staleCommand.ErrorCode);

    var foreignClient = await ClientAccountingService.ResolveProfileRevisionAsync(db, preparer, scope.ClientB, firstRef);
    Assert.False(foreignClient.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, foreignClient.ErrorCode);

    var foreignFirmId = Guid.CreateVersion7();
    var foreignAuthor = User(foreignFirmId, "foreign-profile-author");
    await using (var foreignUserDb = new AuditSphereDbContext(pg.Options))
    {
      foreignUserDb.FirmSafetyStates.Add(new FirmSafetyState { Id = foreignFirmId });
      foreignUserDb.Users.Add(foreignAuthor);
      await foreignUserDb.SaveChangesAsync();
    }

    var foreignAuthorClientId = Guid.CreateVersion7();
    await using (var clientSeedDb = new AuditSphereDbContext(pg.Options))
    {
      clientSeedDb.PracticeClients.Add(new PracticeClient
      {
        Id = foreignAuthorClientId, FirmId = scope.FirmId, LegalName = "PROFILE FK TEST CLIENT",
        CreatedAt = DateTimeOffset.UtcNow
      });
      clientSeedDb.ClientSafetyStates.Add(new ClientSafetyState { Id = foreignAuthorClientId, FirmId = scope.FirmId });
      await clientSeedDb.SaveChangesAsync();
    }

    var foreignAuthorProfile = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = foreignAuthorClientId,
      Jurisdiction = "QA", FunctionalCurrency = "QAR", FiscalYearStartMonth = 1, FiscalYearStartDay = 1,
      SourceSystem = "LEDGER", SourceSystemIdentifier = "FOREIGN-AUTHOR-CASE",
      CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    await using (var profileSeedDb = new AuditSphereDbContext(pg.Options))
    {
      profileSeedDb.ClientAccountingProfiles.Add(foreignAuthorProfile);
      await profileSeedDb.SaveChangesAsync();
    }

    var foreignAuthoredSnapshot = ClientAccountingProfileRevisionRecord.FromProfile(
      foreignAuthorProfile, Guid.CreateVersion7(), null, null, foreignAuthor.Id, DateTimeOffset.UtcNow);
    await using (var invalidActorDb = new AuditSphereDbContext(pg.Options))
    {
      invalidActorDb.ClientAccountingProfileRevisionRecords.Add(foreignAuthoredSnapshot);
      var foreignAuthorUpdate = await Assert.ThrowsAsync<DbUpdateException>(() =>
        invalidActorDb.SaveChangesAsync());
      var foreignAuthorFailure = Assert.IsType<Npgsql.PostgresException>(foreignAuthorUpdate.InnerException);
      Assert.Equal("23503", foreignAuthorFailure.SqlState);
      Assert.Equal("fk_client_accounting_profile_revision_created_by", foreignAuthorFailure.ConstraintName);
    }

    await using (var tamperDb = new AuditSphereDbContext(pg.Options))
    {
      var immutableUpdate = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
        tamperDb.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE client_accounting_profile_revisions SET source_system = {"ALTERED"} WHERE id = {historical.Value.RevisionId}"));
      Assert.Equal("23514", immutableUpdate.SqlState);
    }

    await using (var deleteDb = new AuditSphereDbContext(pg.Options))
    {
      var immutableDelete = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
        deleteDb.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM client_accounting_profile_revisions WHERE id = {historical.Value.RevisionId}"));
      Assert.Equal("23514", immutableDelete.SqlState);
    }

    var legacyProfileId = Guid.CreateVersion7();
    await using (var legacySeedDb = new AuditSphereDbContext(pg.Options))
    {
      legacySeedDb.ClientAccountingProfiles.Add(new ClientAccountingProfile
      {
        Id = legacyProfileId, FirmId = scope.FirmId, ClientId = scope.ClientB, Revision = 7,
        Jurisdiction = "QA", FunctionalCurrency = "QAR", FiscalYearStartMonth = 1, FiscalYearStartDay = 1,
        SourceSystem = "LEGACY", SourceSystemIdentifier = "LEGACY-7",
        CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await legacySeedDb.SaveChangesAsync();
    }

    var legacyRevised = await ClientAccountingService.ReviseProfileAsync(db, preparer,
      new ClientAccountingProfileRevisionRef(legacyProfileId, 7),
      new ClientAccountingProfileRequest(scope.ClientB, "QA", "QAR", 1, 1, "LEGACY-NEW", "LEGACY-8"));
    Assert.True(legacyRevised.Succeeded, legacyRevised.Message);
    Assert.Equal(8, legacyRevised.Value);
    var legacySnapshots = await db.ClientAccountingProfileRevisionRecords.AsNoTracking()
      .Where(x => x.ProfileId == legacyProfileId).OrderBy(x => x.Revision).ToArrayAsync();
    var onlyNewSnapshot = Assert.Single(legacySnapshots);
    Assert.Equal(8, onlyNewSnapshot.Revision);
    Assert.Null(onlyNewSnapshot.PreviousRevisionId);

    var unavailableLegacyRevision = await ClientAccountingService.ResolveProfileRevisionAsync(db, preparer, scope.ClientB,
      new ClientAccountingProfileRevisionRef(legacyProfileId, 7));
    Assert.False(unavailableLegacyRevision.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, unavailableLegacyRevision.ErrorCode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AccountingSetup_DefaultsBlankCurrencyToQar()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    var profileId = (await ClientAccountingService.CreateProfileAsync(db, preparer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "", 1, 1, "LEDGER-A", "A-1"))).Value;
    var profile = await db.ClientAccountingProfiles.SingleAsync(x => x.Id == profileId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, profile.FunctionalCurrency);

    var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
      new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", ""))).Value;
    var period = await db.ClientReportingPeriods.SingleAsync(x => x.Id == periodId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, period.Currency);

    var bookId = (await ClientAccountingService.CreateBookAsync(db, preparer,
      new ReportingBookRequest(scope.ClientA, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", ""))).Value;
    var book = await db.ClientReportingBooks.SingleAsync(x => x.Id == bookId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, book.Currency);

    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
      ServiceRoute = "AccountingOnly", Decision = "Accepted", Generation = 1,
      Rationale = "Approved test fixture", EvaluationTemplateVersion = "TEST-1",
      EvaluationSnapshotDigest = new string('a', 64),
      DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var missingPermissibility = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "AUDIT_ONLY", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "AUDIT", "FinancialStatementAudit"));
    Assert.False(missingPermissibility.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, missingPermissibility.ErrorCode);
    var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "ENTITY_REPORTING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "ENTITY", "AccountingOnly"))).Value;
    var capability = await db.AccountingCapabilityProfiles.SingleAsync(x => x.Id == capabilityId);
    Assert.Equal(AccountingDefaults.DefaultCurrency, capability.ReportingCurrency);
    Assert.Equal("AccountingOnly", capability.ServiceRoute);

    var invalidKind = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "ENTITY"));
    Assert.False(invalidKind.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidKind.ErrorCode);

    var missingRoute = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientA, null, "BOOKKEEPING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "ENTITY"));
    Assert.False(missingRoute.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, missingRoute.ErrorCode);

    db.AcceptanceDecisions.AddRange(
      new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientB,
        ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
        Rationale = "Approved bookkeeping service", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('d', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      },
      new AcceptanceDecision
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientB,
        ServiceRoute = "BOOKKEEPING_CONDITIONAL", Decision = "AcceptedWithConditions", Conditions = "independence review pending", Generation = 1,
        Rationale = "Conditional test decision", EvaluationTemplateVersion = "TEST-1",
        EvaluationSnapshotDigest = new string('e', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
      });
    await db.SaveChangesAsync();
    var bookkeeping = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientB, null, "BOOKKEEPING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "BOOKKEEPING", "BOOKKEEPING"));
    Assert.True(bookkeeping.Succeeded, bookkeeping.Message);
    var conditional = await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(scope.ClientB, null, "BOOKKEEPING", "IFRS", "2026", "ANNUAL", "",
        "STATUTORY", "", "PARTNER", "BOOKKEEPING", "BOOKKEEPING_CONDITIONAL"));
    Assert.False(conditional.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, conditional.ErrorCode);

    var periodCodeAtLimit = new string('P', 50);
    var periodBasisAtLimit = new string('B', 50);
    var boundaryPeriod = await ClientAccountingService.CreatePeriodAsync(db, preparer,
      new ReportingPeriodRequest(scope.ClientA, $" {periodCodeAtLimit} ", new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31),
        $" {periodBasisAtLimit} ", "QAR"));
    Assert.True(boundaryPeriod.Succeeded, boundaryPeriod.Message);
    var storedBoundaryPeriod = await db.ClientReportingPeriods.SingleAsync(x => x.Id == boundaryPeriod.Value);
    Assert.Equal(periodCodeAtLimit, storedBoundaryPeriod.PeriodCode);
    Assert.Equal(periodBasisAtLimit, storedBoundaryPeriod.Basis);
    var periodsBeforeInvalid = await db.ClientReportingPeriods.CountAsync(x => x.ClientId == scope.ClientA);
    foreach (var invalid in new[]
    {
      (Code: new string('P', 51), Basis: "B"),
      (Code: "P", Basis: new string('B', 51))
    })
    {
      var refused = await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, invalid.Code, new DateOnly(2028, 1, 1), new DateOnly(2028, 12, 31), invalid.Basis, "QAR"));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, refused.ErrorCode);
    }
    Assert.Equal(periodsBeforeInvalid, await db.ClientReportingPeriods.CountAsync(x => x.ClientId == scope.ClientA));

    var bookCodeAtLimit = new string('C', 50);
    var bookBasisAtLimit = new string('B', 50);
    var inclusionRuleAtLimit = new string('I', 100);
    var boundaryBook = await ClientAccountingService.CreateBookAsync(db, preparer,
      new ReportingBookRequest(scope.ClientA, periodId, $" {bookCodeAtLimit} ", $" {bookBasisAtLimit} ", $" {inclusionRuleAtLimit} ", "QAR"));
    Assert.True(boundaryBook.Succeeded, boundaryBook.Message);
    var storedBoundaryBook = await db.ClientReportingBooks.SingleAsync(x => x.Id == boundaryBook.Value);
    Assert.Equal(bookCodeAtLimit, storedBoundaryBook.Code);
    Assert.Equal(bookBasisAtLimit, storedBoundaryBook.Basis);
    Assert.Equal(inclusionRuleAtLimit, storedBoundaryBook.InclusionRule);
    var booksBeforeInvalid = await db.ClientReportingBooks.CountAsync(x => x.ClientId == scope.ClientA);
    foreach (var invalid in new[]
    {
      (Code: new string('C', 51), Basis: "B", Inclusion: "I"),
      (Code: "C", Basis: new string('B', 51), Inclusion: "I"),
      (Code: "C", Basis: "B", Inclusion: new string('I', 101))
    })
    {
      var refused = await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(scope.ClientA, periodId, invalid.Code, invalid.Basis, invalid.Inclusion, "QAR"));
      Assert.False(refused.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, refused.ErrorCode);
    }
    Assert.Equal(booksBeforeInvalid, await db.ClientReportingBooks.CountAsync(x => x.ClientId == scope.ClientA));

    var invalidRollForward = await ClientAccountingService.RollForwardPeriodAsync(db, preparer,
      new RollForwardPeriodRequest(scope.ClientA, Guid.NewGuid(), new string('P', 51), new DateOnly(2028, 1, 1),
        new DateOnly(2028, 12, 31), "B", "QAR", new string('a', 64), 0m, 0m, "Synthetic evidence", null, 1));
    Assert.False(invalidRollForward.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidRollForward.ErrorCode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ProfileFiscalCalendar_RejectsImpossiblePairsAndRetainsRevisionHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);

    static ClientAccountingProfileRequest Request(Guid clientId, int month, int day, string? reason = null) =>
      new(clientId, "QA", "QAR", month, day, "SYNTHETIC-LEDGER", "TEST-IDENTIFIER", Reason: reason);

    var invalidPairs = new[]
    {
      (Month: 2, Day: 30), (Month: 2, Day: 31), (Month: 4, Day: 31),
      (Month: 6, Day: 31), (Month: 9, Day: 31), (Month: 11, Day: 31),
      (Month: 0, Day: 1), (Month: 13, Day: 1), (Month: 1, Day: 0), (Month: 1, Day: 32)
    };
    foreach (var (month, day) in invalidPairs)
    {
      var refusedCreate = await ClientAccountingService.CreateProfileAsync(db, preparer,
        Request(scope.ClientB, month, day));
      Assert.False(refusedCreate.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, refusedCreate.ErrorCode);
      Assert.Equal("A valid client accounting profile is required.", refusedCreate.Message);
    }
    Assert.False(await db.ClientAccountingProfiles.AnyAsync(x => x.ClientId == scope.ClientB));

    var createdJanuary = await ClientAccountingService.CreateProfileAsync(db, preparer,
      Request(scope.ClientA, 1, 31));
    Assert.True(createdJanuary.Succeeded, createdJanuary.Message);
    var januaryProfile = await db.ClientAccountingProfiles.SingleAsync(x => x.Id == createdJanuary.Value);
    var originalRevision = await db.ClientAccountingProfileRevisionRecords.AsNoTracking()
      .SingleAsync(x => x.ProfileId == januaryProfile.Id && x.Revision == 1);

    var createdLeapDay = await ClientAccountingService.CreateProfileAsync(db, preparer,
      Request(scope.ClientB, 2, 29));
    Assert.False(createdLeapDay.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, createdLeapDay.ErrorCode);
    Assert.Contains("approved non-leap-year rollover policy", createdLeapDay.Message);
    Assert.Contains("choose another valid fiscal start date", createdLeapDay.Message);
    Assert.False(await db.ClientAccountingProfiles.AnyAsync(x => x.ClientId == scope.ClientB));
    Assert.False(await db.ClientAccountingProfileRevisionRecords.AnyAsync(x => x.ClientId == scope.ClientB));
    var createdFebruary = await ClientAccountingService.CreateProfileAsync(db, preparer,
      Request(scope.ClientB, 2, 28));
    Assert.True(createdFebruary.Succeeded, createdFebruary.Message);

    foreach (var (month, day) in invalidPairs)
    {
      var refusedRevision = await ClientAccountingService.ReviseProfileAsync(db, preparer,
        new ClientAccountingProfileRevisionRef(januaryProfile.Id, 1),
        Request(scope.ClientA, month, day, "Reviewed fiscal calendar change"));
      Assert.False(refusedRevision.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, refusedRevision.ErrorCode);
    }
    var refusedLeapDay = await ClientAccountingService.ReviseProfileAsync(db, preparer,
      new ClientAccountingProfileRevisionRef(januaryProfile.Id, 1),
      Request(scope.ClientA, 2, 29, "A reason is not an approved rollover policy"));
    Assert.False(refusedLeapDay.Succeeded);
    Assert.Equal(ErrorCodes.Accounting.MappingInvalid, refusedLeapDay.ErrorCode);
    Assert.Equal(createdLeapDay.Message, refusedLeapDay.Message);
    var afterRefusals = await db.ClientAccountingProfiles.AsNoTracking().SingleAsync(x => x.Id == januaryProfile.Id);
    Assert.Equal(1, afterRefusals.Revision);
    Assert.Equal((1, 31), (afterRefusals.FiscalYearStartMonth, afterRefusals.FiscalYearStartDay));
    var historyAfterRefusals = await db.ClientAccountingProfileRevisionRecords.AsNoTracking()
      .SingleAsync(x => x.ProfileId == januaryProfile.Id && x.Revision == 1);
    Assert.Equal(originalRevision.Id, historyAfterRefusals.Id);
    Assert.Equal((1, 31), (historyAfterRefusals.FiscalYearStartMonth, historyAfterRefusals.FiscalYearStartDay));

    var validRevisions = new[]
    {
      (Month: 2, Day: 28), (Month: 4, Day: 30), (Month: 6, Day: 30),
      (Month: 9, Day: 30), (Month: 11, Day: 30), (Month: 12, Day: 31), (Month: 1, Day: 31)
    };
    long expectedRevision = 1;
    foreach (var (month, day) in validRevisions)
    {
      var revised = await ClientAccountingService.ReviseProfileAsync(db, preparer,
        new ClientAccountingProfileRevisionRef(januaryProfile.Id, expectedRevision),
        Request(scope.ClientA, month, day, "Reviewed fiscal calendar change"));
      Assert.True(revised.Succeeded, revised.Message);
      expectedRevision++;
      Assert.Equal(expectedRevision, revised.Value);
    }

    var retainedRevisions = await db.ClientAccountingProfileRevisionRecords.AsNoTracking()
      .Where(x => x.ProfileId == januaryProfile.Id).OrderBy(x => x.Revision).ToArrayAsync();
    Assert.Equal(expectedRevision, retainedRevisions.Length);
    Assert.Equal(originalRevision.Id, retainedRevisions[0].Id);
    Assert.Equal((1, 31), (retainedRevisions[0].FiscalYearStartMonth, retainedRevisions[0].FiscalYearStartDay));

    await using var directWriteDb = new AuditSphereDbContext(pg.Options);
    foreach (var (month, day) in invalidPairs)
    {
      var databaseRefusal = await Assert.ThrowsAsync<PostgresException>(() =>
        directWriteDb.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE client_accounting_profiles SET fiscal_year_start_month = {month}, fiscal_year_start_day = {day} WHERE id = {januaryProfile.Id}"));
      Assert.Equal("23514", databaseRefusal.SqlState);
      Assert.Equal("ck_client_accounting_profile_values", databaseRefusal.ConstraintName);
    }
    var unchanged = await db.ClientAccountingProfiles.AsNoTracking().SingleAsync(x => x.Id == januaryProfile.Id);
    Assert.Equal((1, 31), (unchanged.FiscalYearStartMonth, unchanged.FiscalYearStartDay));
    Assert.Equal(expectedRevision, await db.ClientAccountingProfileRevisionRecords.CountAsync(x => x.ProfileId == januaryProfile.Id));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ProfileFiscalCalendarMigration_RefusesInvalidRetainedPairsAndPreservesLeapDayOnRoundTrip()
  {
    const string baseline = "20261126161139_GeneralLedgerCompletenessCalculationVersion";
    const string calendar = "20261126185024_ClientFiscalCalendarMonthDay";
    const string previous = "20261126233605_AuditBankReconciliationCommandReceipts";
    const string leapDayGuard = "20261128120000_ClientFiscalLeapDayTransitionGuard";
    await using var pg = await PgTestSchema.CreateAsync(baseline);
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var leapDay = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientA,
      Jurisdiction = "QA", FunctionalCurrency = "QAR", FiscalYearStartMonth = 2, FiscalYearStartDay = 29,
      SourceSystem = "SYNTHETIC-RETAINED", SourceSystemIdentifier = "RETAINED-LEAP-DAY",
      CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    var invalid = new ClientAccountingProfile
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, ClientId = scope.ClientB,
      Jurisdiction = "QA", FunctionalCurrency = "QAR", FiscalYearStartMonth = 2, FiscalYearStartDay = 30,
      SourceSystem = "SYNTHETIC-RETAINED", SourceSystemIdentifier = "INVALID-LEGACY",
      CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientAccountingProfiles.AddRange(leapDay, invalid);
    db.ClientAccountingProfileRevisionRecords.AddRange(
      ClientAccountingProfileRevisionRecord.FromProfile(leapDay, Guid.CreateVersion7(), null, null, scope.Preparer.Id, leapDay.CreatedAt),
      ClientAccountingProfileRevisionRecord.FromProfile(invalid, Guid.CreateVersion7(), null, null, scope.Preparer.Id, invalid.CreatedAt));
    await db.SaveChangesAsync();
    var retainedHistory = await db.ClientAccountingProfileRevisionRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
    var migrator = db.GetService<IMigrator>();
    var originalHistory = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
    const string constraintSql = """
      SELECT pg_get_constraintdef(oid) AS "Value" FROM pg_constraint
      WHERE conrelid = 'client_accounting_profiles'::regclass AND conname = 'ck_client_accounting_profile_values'
      """;
    var originalConstraint = await db.Database.SqlQueryRaw<string>(constraintSql).SingleAsync();

    // Only synthetic rows are varied. A failed upgrade must roll back its dropped check
    // and history entry, leaving the retained value for reviewed correction, not guessing it.
    foreach (var (month, day) in new[] { (2, 30), (2, 31), (4, 31), (6, 31), (9, 31), (11, 31) })
    {
      await db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE client_accounting_profiles SET fiscal_year_start_month = {month}, fiscal_year_start_day = {day} WHERE id = {invalid.Id}");
      var failure = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(calendar));
      Assert.Equal("23514", failure.SqlState);
      Assert.Equal("ck_client_accounting_profile_values", failure.ConstraintName);
      Assert.Equal(originalHistory, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
      Assert.Equal(originalConstraint, await db.Database.SqlQueryRaw<string>(constraintSql).SingleAsync());
      var unchanged = await db.ClientAccountingProfiles.AsNoTracking().SingleAsync(x => x.Id == invalid.Id);
      Assert.Equal((month, day), (unchanged.FiscalYearStartMonth, unchanged.FiscalYearStartDay));
      Assert.Equivalent(retainedHistory, await db.ClientAccountingProfileRevisionRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), strict: true);
    }

    // Explicit synthetic fixture correction; production correction requires qualified review.
    await db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE client_accounting_profiles SET fiscal_year_start_month = 2, fiscal_year_start_day = 28 WHERE id = {invalid.Id}");
    await migrator.MigrateAsync(calendar);
    Assert.Contains(calendar, await db.Database.GetAppliedMigrationsAsync());
    await migrator.MigrateAsync(baseline);
    Assert.Equal(originalHistory, (await db.Database.GetAppliedMigrationsAsync()).ToArray());
    Assert.Equal(originalConstraint, await db.Database.SqlQueryRaw<string>(constraintSql).SingleAsync());
    await migrator.MigrateAsync(previous);
    var beforeGuard = await db.ClientAccountingProfiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
    await migrator.MigrateAsync(leapDayGuard);
    Assert.False(db.Database.HasPendingModelChanges());
    Assert.Equivalent(beforeGuard, await db.ClientAccountingProfiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), strict: true);

    var insertRefusal = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
      INSERT INTO client_accounting_profiles
        (id, firm_id, client_id, jurisdiction, functional_currency, fiscal_year_start_month, fiscal_year_start_day,
         source_system, source_system_identifier, source_mode, status, revision, created_by_user_id, created_at)
      SELECT {Guid.CreateVersion7()}, firm_id, client_id, jurisdiction, functional_currency, 2, 29,
        source_system, source_system_identifier, source_mode, status, revision, created_by_user_id, created_at
      FROM client_accounting_profiles WHERE id = {invalid.Id}
      """));
    Assert.Equal("23514", insertRefusal.SqlState);
    Assert.Equal("ck_client_fiscal_leap_day_transition", insertRefusal.ConstraintName);
    Assert.Contains("approved non-leap-year rollover policy", insertRefusal.MessageText);
    var updateRefusal = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE client_accounting_profiles SET fiscal_year_start_day = 29 WHERE id = {invalid.Id}"));
    Assert.Equal("ck_client_fiscal_leap_day_transition", updateRefusal.ConstraintName);
    Assert.Equivalent(beforeGuard, await db.ClientAccountingProfiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), strict: true);
    Assert.Equivalent(retainedHistory, await db.ClientAccountingProfileRevisionRecords.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), strict: true);

    // The migration and its downgrade preserve every current value and immutable snapshot.
    await migrator.MigrateAsync(previous);
    Assert.DoesNotContain(leapDayGuard, await db.Database.GetAppliedMigrationsAsync());
    Assert.Equivalent(beforeGuard, await db.ClientAccountingProfiles.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(), strict: true);
    await migrator.MigrateAsync(leapDayGuard);
    db.ChangeTracker.Clear();
    var revised = await ClientAccountingService.ReviseProfileAsync(db, preparer,
      new ClientAccountingProfileRevisionRef(leapDay.Id, 1),
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 2, 29, "SYNTHETIC-RETAINED", "REVISED-IDENTIFIER"));
    Assert.True(revised.Succeeded, revised.Message);
    Assert.Equal(2, revised.Value);
    var retained = await db.ClientAccountingProfiles.AsNoTracking().SingleAsync(x => x.Id == leapDay.Id);
    Assert.Equal((2, 29), (retained.FiscalYearStartMonth, retained.FiscalYearStartDay));
    Assert.Equal("REVISED-IDENTIFIER", retained.SourceSystemIdentifier);
    var revisions = await db.ClientAccountingProfileRevisionRecords.AsNoTracking()
      .Where(x => x.ProfileId == leapDay.Id).OrderBy(x => x.Revision).ToArrayAsync();
    Assert.Equal(2, revisions.Length);
    Assert.Equal(revisions[0].Id, revisions[1].PreviousRevisionId);
    Assert.All(revisions, x => Assert.Equal((2, 29), (x.FiscalYearStartMonth, x.FiscalYearStartDay)));
    foreach (var historical in retainedHistory)
      Assert.Equivalent(historical, await db.ClientAccountingProfileRevisionRecords.AsNoTracking().SingleAsync(x => x.Id == historical.Id), strict: true);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task NativeBookkeepingModeRequiresAcceptedClientServiceAndReviewer()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
      ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
      Rationale = "Approved bookkeeping service", EvaluationTemplateVersion = "TEST-1",
      EvaluationSnapshotDigest = new string('b', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var denied = await ClientAccountingService.CreateProfileAsync(db, preparer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-1",
        ClientAccountingSourceModes.NativeBookkeeping));
    Assert.False(denied.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

    var created = await ClientAccountingService.CreateProfileAsync(db, reviewer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-1",
        ClientAccountingSourceModes.NativeBookkeeping));
    Assert.True(created.Succeeded, created.Message);
    Assert.Equal(ClientAccountingSourceModes.NativeBookkeeping,
      (await db.ClientAccountingProfiles.SingleAsync(x => x.Id == created.Value)).SourceMode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ExternalSourceProfileCannotSwitchToNativeAfterReportingPeriodsExist()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER", "EXT-1"));
    Assert.True(profile.Succeeded);
    var period = await ClientAccountingService.CreatePeriodAsync(db, reviewer,
      new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "IFRS", "QAR"));
    Assert.True(period.Succeeded);
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
      ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
      Rationale = "Approved bookkeeping service", EvaluationTemplateVersion = "TEST-1",
      EvaluationSnapshotDigest = new string('c', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var result = await ClientAccountingService.ReviseProfileAsync(db, reviewer, profile.Value,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "LEDGER", "EXT-1",
        ClientAccountingSourceModes.NativeBookkeeping), expectedRevision: 1);
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, result.ErrorCode);
    Assert.Equal(ClientAccountingSourceModes.ExternalSource,
      (await db.ClientAccountingProfiles.SingleAsync(x => x.Id == profile.Value)).SourceMode);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task BookIdentityCanChangeBeforePeriodsButRequiresCutoverAfterPeriodSetup()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.AcceptanceDecisions.Add(new AcceptanceDecision
    {
      Id = Guid.CreateVersion7(), FirmId = scope.FirmId, PracticeClientId = scope.ClientA,
      ServiceRoute = "BOOKKEEPING", Decision = "Accepted", Generation = 1,
      Rationale = "Approved bookkeeping service", EvaluationTemplateVersion = "TEST-1",
      EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = scope.Reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    var profile = await ClientAccountingService.CreateProfileAsync(db, reviewer,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "AUDITSPHERE", "NATIVE-1",
        ClientAccountingSourceModes.NativeBookkeeping));
    Assert.True(profile.Succeeded, profile.Message);

    var preCutoverRevision = await ClientAccountingService.ReviseProfileAsync(db, reviewer, profile.Value,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "USD", 4, 1, "AUDITSPHERE", "NATIVE-1",
        ClientAccountingSourceModes.NativeBookkeeping,
        Reason: "Synthetic pre-period currency and fiscal-calendar correction"), expectedRevision: 1);
    Assert.True(preCutoverRevision.Succeeded, preCutoverRevision.Message);
    Assert.Equal(2, preCutoverRevision.Value);
    var period = await ClientAccountingService.CreatePeriodAsync(db, reviewer,
      new ReportingPeriodRequest(scope.ClientA, "FY26", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), "STATUTORY", "USD"));
    Assert.True(period.Succeeded, period.Message);

    var currency = await ClientAccountingService.ReviseProfileAsync(db, reviewer, profile.Value,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 4, 1, "AUDITSPHERE", "NATIVE-1",
        ClientAccountingSourceModes.NativeBookkeeping,
        Reason: "Synthetic post-period currency change without reviewed cutover"), expectedRevision: 2);
    var fiscal = await ClientAccountingService.ReviseProfileAsync(db, reviewer, profile.Value,
      new ClientAccountingProfileRequest(scope.ClientA, "QA", "USD", 1, 1, "AUDITSPHERE", "NATIVE-1",
        ClientAccountingSourceModes.NativeBookkeeping,
        Reason: "Synthetic post-period fiscal-calendar change without reviewed cutover"), expectedRevision: 2);
    Assert.False(currency.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, currency.ErrorCode);
    Assert.False(fiscal.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, fiscal.ErrorCode);
    var retained = await db.ClientAccountingProfiles.SingleAsync(x => x.Id == profile.Value);
    Assert.Equal("USD", retained.FunctionalCurrency);
    Assert.Equal(4, retained.FiscalYearStartMonth);
    Assert.Equal(2, retained.Revision);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task FirmWideAccountingConfiguration_RejectsClientScopedGrants()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var user = User(scope.FirmId, "scoped-config-reviewer");
    var actor = new ActorContext(user.Id, scope.FirmId, user.SessionEpoch, ["Partner", "AccountingReviewer"]);

    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(user);
    db.RoleGrants.AddRange(
      Grant(scope.FirmId, user, "Partner", scope.ClientA),
      Grant(scope.FirmId, user, "AccountingReviewer", scope.ClientA));
    await db.SaveChangesAsync();

    var group = await ConsolidationService.CreateGroupAsync(db, actor,
      new ClientGroupRequest("SCOPED-GROUP", "Must be firm-authorized"));
    var taxonomy = await ClientAccountingService.CreateTaxonomyVersionAsync(db, actor,
      "SCOPED-TAXONOMY", "IFRS", "Must be firm-authorized", new DateOnly(2026, 1, 1));
    var rates = await CurrencyTranslationService.CreateRateSetAsync(db, actor,
      new ExchangeRateSetRequest("SCOPED-FX", "test-source"));

    Assert.Equal(ErrorCodes.ScopeDenied, group.ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, taxonomy.ErrorCode);
    Assert.Equal(ErrorCodes.ScopeDenied, rates.ErrorCode);
    Assert.Empty(await db.ClientGroups.ToListAsync());
    Assert.Empty(await db.ReportingTaxonomyVersions.ToListAsync());
    Assert.Empty(await db.ExchangeRateSetVersions.ToListAsync());
  }
}
