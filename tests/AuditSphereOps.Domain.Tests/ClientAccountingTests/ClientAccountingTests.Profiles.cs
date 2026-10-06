using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Reviews;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using WorkerHost = AuditSphereOps.Worker.Worker;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
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
