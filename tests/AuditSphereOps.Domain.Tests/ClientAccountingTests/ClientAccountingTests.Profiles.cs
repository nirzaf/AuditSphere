using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

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
