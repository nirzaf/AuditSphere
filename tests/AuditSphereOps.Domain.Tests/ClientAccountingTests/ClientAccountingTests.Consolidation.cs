using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Fact]
  [Trait("Profile", "Database")]
  public async Task CreateGroup_GrantsCreatorOnlyTheirFirmWideRole()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var manager = User(scope.FirmId, "group-manager");
    var actor = Actor(manager, "Manager");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(manager);
    db.RoleGrants.Add(Grant(scope.FirmId, manager, "Manager"));
    await db.SaveChangesAsync();

    var created = await ConsolidationService.CreateGroupAsync(db, actor,
      new ClientGroupRequest("MANAGER-GROUP", "Manager-owned group"));

    Assert.True(created.Succeeded, created.Message);
    var grant = await db.GroupAccessGrants.SingleAsync(x => x.GroupId == created.Value && x.UserId == manager.Id);
    Assert.Equal("Manager", grant.Role);
    Assert.DoesNotContain(await db.GroupAccessGrants.Where(x => x.GroupId == created.Value).ToListAsync(),
      x => x.Role == "Partner");
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task CreateGroup_WaitsForFirmRoleRevocationAndRejectsStaleAuthority()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var manager = User(scope.FirmId, "group-revoked-manager");
    var actor = Actor(manager, "Manager");
    await using (var setup = new AuditSphereDbContext(pg.Options))
    {
      setup.Users.Add(manager);
      setup.RoleGrants.Add(Grant(scope.FirmId, manager, "Manager"));
      await setup.SaveChangesAsync();
    }

    await using var revocation = new AuditSphereDbContext(pg.Options);
    await using var tx = await revocation.Database.BeginTransactionAsync();
    await revocation.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id = {scope.FirmId} FOR UPDATE").SingleAsync();
    var grant = await revocation.RoleGrants.SingleAsync(x => x.FirmId == scope.FirmId && x.UserId == manager.Id);
    grant.RevokedAt = DateTimeOffset.UtcNow;
    await revocation.SaveChangesAsync();

    var create = Task.Run(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await ConsolidationService.CreateGroupAsync(db, actor,
        new ClientGroupRequest("REVOKED-GROUP", "Must not publish"));
    });
    Assert.NotSame(create, await Task.WhenAny(create, Task.Delay(150)));
    await tx.CommitAsync();

    var result = await create;
    Assert.False(result.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, result.ErrorCode);
    await using var check = new AuditSphereDbContext(pg.Options);
    Assert.False(await check.ClientGroups.AnyAsync(x => x.FirmId == scope.FirmId && x.Code == "REVOKED-GROUP"));
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GroupCapabilityAuthorization_RejectsStaleDisabledAndClientActors()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid groupId, profileId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("GROUP-AUTH", "Group authorization regression"))).Value;
      profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
          "ANNUAL", "QAR", "STATUTORY", "", "PARTNER", "GROUP"))).Value;

      var currentUser = await db.Users.SingleAsync(x => x.Id == scope.Reviewer.Id);
      currentUser.SessionEpoch++;
      await db.SaveChangesAsync();
      var stale = await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "stale-session");
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, stale.ErrorCode);

      currentUser.Disabled = true;
      await db.SaveChangesAsync();
      var disabled = await ClientAccountingService.RecordCapabilityAcceptanceAsync(db,
        new ActorContext(scope.Reviewer.Id, scope.FirmId, currentUser.SessionEpoch, ["Partner"]), profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "disabled-user");
      Assert.False(disabled.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, disabled.ErrorCode);

      var clientUser = User(scope.FirmId, "group-client");
      clientUser.UserKind = "Client";
      db.Users.Add(clientUser);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = clientUser.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      await db.SaveChangesAsync();
      var clientActor = new ActorContext(clientUser.Id, scope.FirmId, clientUser.SessionEpoch, ["Partner"]);
      var clientResult = await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, clientActor, profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "client-classification");
      Assert.False(clientResult.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, clientResult.ErrorCode);
      Assert.Empty(await db.AccountingCapabilityAcceptances.Where(x => x.CapabilityProfileId == profileId).ToListAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task OwnershipInterest_RejectsDuplicateAndCircularHierarchy()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid scopeVersionId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var preparer = Actor(scope.Preparer, "AccountingPreparer");
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientA, "QA", "QAR", 1, 1, "OWNERSHIP-LEDGER", "A-1"))).Succeeded);
      var periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(scope.ClientA, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "STATUTORY", "QAR"))).Value;
      var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("OWNERSHIP-1", "Ownership test group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "CONTROLLED", 100m, 100m, "ownership-a"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "CONTROLLED", 100m, 100m, "ownership-b"))).Succeeded);
      var groupRevision = await db.ClientGroups.Where(x => x.FirmId == scope.FirmId && x.Id == groupId)
        .Select(x => x.Revision).SingleAsync();
      var createdScope = await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, periodId, "QAR", ConsolidationCalculator.RestrictedMethod,
          "OPENING-OWNERSHIP-2026", ExpectedGroupRevision: groupRevision));
      Assert.True(createdScope.Succeeded, createdScope.Message);
      scopeVersionId = createdScope.Value;

      var first = await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(scopeVersionId, scope.ClientA, scope.ClientB,
          new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 100m, 100m, "CONTROLLED", "DIRECT", "ownership-edge-a-b"));
      Assert.True(first.Succeeded, first.Message);

      var duplicate = await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(scopeVersionId, scope.ClientA, scope.ClientB,
          new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 100m, 100m, "CONTROLLED", "DIRECT", "ownership-edge-duplicate"));
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicate.ErrorCode);

      var cycle = await ConsolidationService.AddOwnershipInterestAsync(db, reviewer,
        new OwnershipInterestRequest(scopeVersionId, scope.ClientB, scope.ClientA,
          new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), 100m, 100m, "CONTROLLED", "DIRECT", "ownership-edge-b-a"));
      Assert.False(cycle.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, cycle.ErrorCode);
      Assert.Equal(1, await db.OwnershipInterestVersions.CountAsync(x => x.ScopeVersionId == scopeVersionId));
    }
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void RestrictedConsolidation_IsDeterministicAndFailsClosed()
  {
    var components = new[]
    {
      new ConsolidationComponentBalance(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.NewGuid(), "CASH", 100m, "QAR", 100m, "CONTROLLED", Hashing.Sha256Hex("component-a"), "STATUTORY", "tax-v1", "mapping-a", Guid.Parse("00000000-0000-0000-0000-000000000011")),
      new ConsolidationComponentBalance(Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.NewGuid(), "REVENUE", -100m, "QAR", 100m, "CONTROLLED", Hashing.Sha256Hex("component-b"), "STATUTORY", "tax-v1", "mapping-b", Guid.Parse("00000000-0000-0000-0000-000000000012"))
    };
    var first = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, []);
    var second = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, []);
    Assert.Equal(first.RunHash, second.RunHash);
    Assert.Equal(0m, first.SignedTotal);
    Assert.Equal(first.Lines.Select(x => x.ConsolidatedAmount), new[] { 100m, -100m });
    Assert.Throws<InvalidOperationException>(() => ConsolidationCalculator.Compute(
      "QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026",
      [components[0] with { OwnershipPercent = 80m }, components[1]], []));
    Assert.Throws<InvalidOperationException>(() => ConsolidationCalculator.Compute(
      "QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026",
      [components[0], components[1]], [new ConsolidationElimination(Guid.NewGuid(), "CASH", 1m, "USD")]));
    var receivableEliminations = new[]
    {
      new ConsolidationElimination(Guid.NewGuid(), "CASH", -10m, "QAR", "SELLER", EliminationKind: ConsolidationEliminationKinds.ReceivablePayable),
      new ConsolidationElimination(Guid.NewGuid(), "REVENUE", 10m, "QAR", "BUYER", EliminationKind: ConsolidationEliminationKinds.ReceivablePayable)
    };
    var revenueEliminations = receivableEliminations.Select(x => x with { EliminationKind = ConsolidationEliminationKinds.RevenueExpense }).ToArray();
    var receivableRun = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, receivableEliminations);
    var revenueRun = ConsolidationCalculator.Compute("QAR", ConsolidationCalculator.RestrictedMethod, "OPENING-2026", components, revenueEliminations);
    Assert.NotEqual(receivableRun.RunHash, revenueRun.RunHash);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void ConsolidationEliminationKinds_RequireAnEnabledAccountingNature()
  {
    Assert.All(new[]
    {
      ConsolidationEliminationKinds.ReceivablePayable,
      ConsolidationEliminationKinds.RevenueExpense,
      ConsolidationEliminationKinds.Dividend,
      ConsolidationEliminationKinds.InvestmentEquity
    }, kind => Assert.True(ConsolidationEliminationKinds.IsIntercompany(kind)));
    Assert.False(ConsolidationEliminationKinds.IsIntercompany("INTERCOMPANY"));
    Assert.True(ConsolidationEliminationKinds.IsRunKind(ConsolidationEliminationKinds.GroupJournal));
    Assert.False(ConsolidationEliminationKinds.IsRunKind("UNCLASSIFIED"));
  }

  [Fact]
  [Trait("Profile", "Database")]
  [Trait("CompletionUnit", "WU-013")]
  public async Task GroupWorkflow_UsesApprovedComponentPackagesWithoutMutatingThem()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var groupViewer = User(scope.FirmId, "group-viewer");
    var groupViewerActor = Actor(groupViewer, "AccountingReviewer");
    Guid groupId, consolidationScopeId, matchId, outsideMatchId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer, new ClientGroupRequest("GROUP-A", "Group A"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-a"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-b"))).Succeeded);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      var clientBPartnerGrant = new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = scope.Partner.Id, Role = "Partner",
        ClientId = scope.ClientB, EngagementId = scope.EngagementB, GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = scope.Reviewer.Id, Reason = "Synthetic Engagement Partner fixture for client B"
      };
      db.RoleGrants.Add(clientBPartnerGrant);
      db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientB, EngagementId = scope.EngagementB,
        UserId = scope.Partner.Id, StaffingLevel = StaffingLevels.EngagementPartner,
        RoleGrantId = clientBPartnerGrant.Id, AssignedByUserId = scope.Reviewer.Id, AssignedAt = DateTimeOffset.UtcNow
      });
      db.Users.Add(groupViewer);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = groupViewer.Id,
        Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id
      });
      var periodId = Guid.NewGuid();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = scope.FirmId, ClientId = scope.ClientA, PeriodCode = "FY2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      consolidationScopeId = (await ConsolidationService.CreateScopeAsync(db, preparer,
        new ConsolidationScopeRequest(groupId, periodId, "", ConsolidationCalculator.RestrictedMethod, "OPENING-2026"))).Value;
      Assert.Equal(AccountingDefaults.DefaultCurrency,
        await db.ConsolidationScopeVersions.Where(x => x.Id == consolidationScopeId).Select(x => x.ReportingCurrency).SingleAsync());
      await BuildCurrencyPackageAsync(db, scope, scope.ClientA, scope.EngagementA, Gold07Lines, "group-a", "QAR");
      await BuildCurrencyPackageAsync(db, scope, scope.ClientB, scope.EngagementB,
        [("CASH", "ASSETS", 100m), ("CAPITAL", "EQUITY", 0m), ("REVENUE", "INCOME", -100m), ("EXPENSE", "EXPENSE", 0m)], "group-b", "QAR");
      await db.SaveChangesAsync();
    }

    Guid packageA, packageB, compositionA;
    string sourcePackagesBefore, sourceClientBooksBefore;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      packageA = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientA).Select(x => x.Id).SingleAsync();
      packageB = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientB).Select(x => x.Id).SingleAsync();
      sourcePackagesBefore = await CurrencyPackageSourceManifestAsync(db, [packageA, packageB]);
      sourceClientBooksBefore = await ClientBookSourceManifestAsync(db, scope.FirmId, [scope.ClientA, scope.ClientB]);
      var packageMappings = await db.FinancialPackages.Where(x => x.Id == packageA || x.Id == packageB)
        .ToDictionaryAsync(x => x.Id, x => x.MappingVersionId);
      var invalidLineage = await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageA, 100m,
          "CONTROLLED", "STATUTORY", "tax-v1", "not-a-mapping-id"));
      Assert.False(invalidLineage.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidLineage.ErrorCode);
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageA, 100m, "CONTROLLED", "STATUTORY", "tax-v1", packageMappings[packageA].ToString("D")))).Succeeded);
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientB, scope.EngagementB, packageB, 100m, "CONTROLLED", "STATUTORY", "tax-v1", packageMappings[packageB].ToString("D")))).Succeeded);
      var components = await db.ConsolidationComponents.Where(x => x.ScopeVersionId == consolidationScopeId).Select(x => x.Id).ToListAsync();
      var missingPackageReview = await ConsolidationService.ApproveComponentAsync(db, reviewer, components[0]);
      Assert.False(missingPackageReview.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingPackageReview.ErrorCode);
      var partner = Actor(scope.Partner, "Partner");
      Assert.Equal(sourcePackagesBefore, await CurrencyPackageSourceManifestAsync(db, [packageA, packageB]));
      compositionA = await ReviewCurrencyCompositionAsync(db, scope, packageA);
      var compositionB = await ReviewCurrencyCompositionAsync(db, scope, packageB);
      Assert.NotEqual(compositionA, compositionB);
      // Rendering appends the approved artifact set; consolidation must preserve that exact set.
      sourcePackagesBefore = await CurrencyPackageSourceManifestAsync(db, [packageA, packageB]);
      foreach (var componentId in components)
        Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);
      // Approved component rows cannot substitute for reporting-period membership.
      // Roll back the future dates so the existing legacy workflow keeps its original basis.
      await using (var futureMemberships = await db.Database.BeginTransactionAsync())
      {
        Assert.Equal(2, await db.ClientGroupMemberships.Where(x => x.FirmId == scope.FirmId && x.GroupId == groupId)
          .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.EffectiveFrom, new DateOnly(2027, 1, 1))));
        Assert.Equal(2, await db.ConsolidationComponents.CountAsync(x => x.FirmId == scope.FirmId &&
          x.ScopeVersionId == consolidationScopeId && x.Status == AccountingWorkflowStates.Approved));
        var groupRevision = await db.ClientGroups.Where(x => x.Id == groupId).Select(x => x.Revision).SingleAsync();
        var matchesBefore = await db.IntercompanyMatches.CountAsync(x => x.ScopeVersionId == consolidationScopeId);
        var bothOutside = await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
          new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
            ConsolidationEliminationKinds.ReceivablePayable, "FY2026", "QAR", "IC-BOTH-FUTURE",
            100m, -100m, 100m, "both-future-membership-evidence", "CASH", "REVENUE",
            ExpectedGroupRevision: groupRevision));
        Assert.False(bothOutside.Succeeded);
        Assert.Equal(ErrorCodes.GateBlocked, bothOutside.ErrorCode);
        Assert.Equal(matchesBefore, await db.IntercompanyMatches.CountAsync(x => x.ScopeVersionId == consolidationScopeId));
        await futureMemberships.RollbackAsync();
      }
      var creatorApproval = await ConsolidationService.ApproveScopeAsync(db, preparer, consolidationScopeId);
      Assert.False(creatorApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, creatorApproval.ErrorCode);
      var missingCapability = await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId);
      Assert.False(missingCapability.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingCapability.ErrorCode);
      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.RestrictedMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "local-consolidation-profile")).Succeeded);
      var selfApproval = await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "self-approval-must-fail");
      Assert.False(selfApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfApproval.ErrorCode);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, Actor(scope.Preparer, "Partner"), capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "method-owner-approval-fixture")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId)).Succeeded);

      var missingDifferenceReason = await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-001", 100m, -90m, 90m, "ic-evidence"));
      Assert.False(missingDifferenceReason.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, missingDifferenceReason.ErrorCode);
      var unsupportedNature = await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          "UNCLASSIFIED", "2026", "QAR", "IC-UNSUPPORTED", 100m, -100m, 100m, "ic-evidence",
          "CASH", "REVENUE"));
      Assert.False(unsupportedNature.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unsupportedNature.ErrorCode);
      matchId = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-001", 100m, -100m, 100m, "ic-evidence",
            "CASH", "REVENUE"))).Value;
      var negativeRevisionApproval = await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, matchId, -1);
      Assert.False(negativeRevisionApproval.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, negativeRevisionApproval.ErrorCode);
      Assert.Equal(AccountingWorkflowStates.Submitted,
        (await db.IntercompanyMatches.AsNoTracking().SingleAsync(x => x.Id == matchId)).Status);
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, matchId)).Succeeded);

      var groupedFirst = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-G-001", 10m, -10m, 10m, "ic-grouped-a",
            "CASH", "REVENUE", "", IntercompanyMatchModes.Grouped, "IC-GROUP-001"))).Value;
      var incompleteGroupedApproval = await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, groupedFirst);
      Assert.False(incompleteGroupedApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.ReconciliationRejected, incompleteGroupedApproval.ErrorCode);
      var groupedSecond = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-G-002", 20m, -20m, 20m, "ic-grouped-b",
            "CASH", "REVENUE", "", IntercompanyMatchModes.Grouped, "IC-GROUP-001"))).Value;
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, groupedFirst)).Succeeded);
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, groupedSecond)).Succeeded);

      var outsideClientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient { Id = outsideClientId, FirmId = scope.FirmId, LegalName = "RELATED PARTY OUTSIDE GROUP", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      outsideMatchId = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, outsideClientId,
          "RELATED_PARTY", "2026", "QAR", "IC-OUTSIDE-001", 15m, -15m, 15m, "outside-related-party-review",
            "CASH", "REVENUE", "Outside the approved consolidation perimeter", IntercompanyMatchModes.OneToOne, "", true))).Value;
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, outsideMatchId)).Succeeded);
    }

    Guid journalId, runId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      journalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-001", "GROUP_RECLASSIFICATION", "QAR", "group-adjustment-001",
        [new(null, "CASH", 100m, 0m, "Group-only cash reclassification"),
         new(null, "REVENUE", 0m, 100m, "Group-only revenue reclassification")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, journalId)).Succeeded);
      runId = (await ConsolidationService.RunAsync(db, preparer, consolidationScopeId)).Value;
      var linkedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-LINKED", "GROUP_RECLASSIFICATION", "QAR", "linked-match-adjustment",
        [new(matchId, "CASH", 100m, 0m, "Reviewed linked elimination"),
         new(null, "REVENUE", 0m, 100m, "Reviewed linked elimination")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, linkedJournalId)).Succeeded);
      var duplicateLinkedJournal = await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-DUPLICATE-LINK", "GROUP_RECLASSIFICATION", "QAR", "duplicate-linked-match",
        [new(matchId, "CASH", 100m, 0m, "Must not duplicate a reviewed match"),
         new(null, "REVENUE", 0m, 100m, "Must not duplicate a reviewed match")]));
      Assert.False(duplicateLinkedJournal.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, duplicateLinkedJournal.ErrorCode);
      var changedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-002", "GROUP_RECLASSIFICATION", "QAR", "group-adjustment-002",
        [new(null, "CASH", 50m, 0m, "Post-run group-only cash reclassification"),
         new(null, "REVENUE", 0m, 50m, "Post-run group-only revenue reclassification")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, changedJournalId)).Succeeded);
      var stale = await ConsolidationService.ApproveRunAsync(db, reviewer, runId);
      Assert.False(stale.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, stale.ErrorCode);
      var rebuiltRunId = (await ConsolidationService.RunAsync(db, preparer, consolidationScopeId)).Value;
      Assert.NotEqual(runId, rebuiltRunId);
      var component = await db.ConsolidationComponents.OrderBy(x => x.Id).FirstAsync(x => x.ScopeVersionId == consolidationScopeId);
      var packageHash = component.PackageHash;
      component.PackageHash = Hashing.Sha256Hex("replacement-component-source");
      await db.SaveChangesAsync();
      var staleComponent = await ConsolidationService.ApproveRunAsync(db, reviewer, rebuiltRunId);
      Assert.False(staleComponent.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleComponent.ErrorCode);
      component.PackageHash = packageHash;
      await db.SaveChangesAsync();
      Assert.True((await ConsolidationService.ApproveRunAsync(db, reviewer, rebuiltRunId)).Succeeded);
      var report = await ConsolidationService.GetLatestReportAsync(db, groupViewerActor, consolidationScopeId);
      Assert.True(report.Succeeded, report.Message);
      Assert.Equal("CURRENT_APPROVED", report.Value!.State);
      Assert.Equal(rebuiltRunId, report.Value.RunId);
      Assert.Contains(report.Value.Lines, x => x.Component == "CLIENT A" && x.TaxonomyCode == "CASH");
      Assert.Equal(18, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId && x.ConsolidationJournalId == journalId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == runId && x.IntercompanyMatchId == matchId));
      Assert.Equal(20, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId));
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId && x.ConsolidationJournalId == changedJournalId));
      Assert.Equal(1, await db.ConsolidationRunLines.CountAsync(x => x.RunId == rebuiltRunId && x.IntercompanyMatchId == matchId));
      Assert.Empty(await db.ConsolidationRunLines.Where(x => x.RunId == rebuiltRunId && x.IntercompanyMatchId == outsideMatchId).ToListAsync());
      Assert.All(await db.ConsolidationComponents.Where(x => x.ScopeVersionId == consolidationScopeId).ToListAsync(), x => Assert.Equal(AccountingWorkflowStates.Approved, x.Status));
      Assert.Equal(sourceClientBooksBefore,
        await ClientBookSourceManifestAsync(db, scope.FirmId, [scope.ClientA, scope.ClientB]));

      var approvedRun = await db.ConsolidationRuns.AsNoTracking().SingleAsync(x => x.Id == rebuiltRunId);
      var approvedRunStatus = approvedRun.Status;
      var approvedRunHash = approvedRun.RunHash;
      var approvedRunManifest = approvedRun.InputManifest;
      var approvedRunBy = approvedRun.ApprovedByUserId;
      var approvedRunAt = approvedRun.ApprovedAt;
      var approvedRunLineIds = await db.ConsolidationRunLines.AsNoTracking().Where(x => x.RunId == rebuiltRunId)
        .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
      var runCountBeforeReviewReturn = await db.ConsolidationRuns.CountAsync(x =>
        x.FirmId == scope.FirmId && x.ScopeVersionId == consolidationScopeId);
      var composition = await db.FinancialStatementCompositions.AsNoTracking().SingleAsync(x => x.Id == compositionA);
      var returnedReview = await FinancialPackageReviewService.RecordAsync(db, Actor(scope.Partner, "Partner"),
        new FinancialPackageReviewRequest(packageA, FinancialPackageReviewStages.PartnerApproval,
          FinancialPackageReviewDecisions.ChangesRequired, FinancialPackageReviewEvidenceModes.SignedIn,
          "group-component-review-return", "Return the exact pinned package review for correction.",
          composition.PresentationRevisionId, null, composition.Id, composition.ManifestDigest));
      Assert.True(returnedReview.Succeeded, returnedReview.Message);

      var runAfterReviewReturn = await ConsolidationService.RunAsync(db, preparer, consolidationScopeId);
      Assert.False(runAfterReviewReturn.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, runAfterReviewReturn.ErrorCode);
      var approvalAfterReviewReturn = await ConsolidationService.ApproveRunAsync(db, reviewer, runId);
      Assert.False(approvalAfterReviewReturn.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, approvalAfterReviewReturn.ErrorCode);
      var staleReviewReport = await ConsolidationService.GetLatestReportAsync(db, groupViewerActor, consolidationScopeId);
      Assert.True(staleReviewReport.Succeeded, staleReviewReport.Message);
      Assert.Equal("STALE", staleReviewReport.Value!.State);
      Assert.Equal(rebuiltRunId, staleReviewReport.Value.RunId);
      Assert.Empty(staleReviewReport.Value.Lines);
      var rawPackage = await FinancialStatementService.GetStoredPackageArtifactAsync(db, groupViewerActor, packageA);
      Assert.False(rawPackage.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, rawPackage.ErrorCode);

      var retainedApprovedRun = await db.ConsolidationRuns.AsNoTracking().SingleAsync(x => x.Id == rebuiltRunId);
      Assert.Equal(approvedRunStatus, retainedApprovedRun.Status);
      Assert.Equal(approvedRunHash, retainedApprovedRun.RunHash);
      Assert.Equal(approvedRunManifest, retainedApprovedRun.InputManifest);
      Assert.Equal(approvedRunBy, retainedApprovedRun.ApprovedByUserId);
      Assert.Equal(approvedRunAt, retainedApprovedRun.ApprovedAt);
      Assert.Equal(approvedRunLineIds,
        await db.ConsolidationRunLines.AsNoTracking().Where(x => x.RunId == rebuiltRunId)
          .OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
      Assert.Equal(runCountBeforeReviewReturn,
        await db.ConsolidationRuns.CountAsync(x => x.FirmId == scope.FirmId && x.ScopeVersionId == consolidationScopeId));

      var newClientId = Guid.NewGuid();
      db.PracticeClients.Add(new PracticeClient { Id = newClientId, FirmId = scope.FirmId, LegalName = "CLIENT C", CreatedAt = DateTimeOffset.UtcNow });
      await db.SaveChangesAsync();
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, newClientId, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m,
          "ownership-c-added-after-scope"))).Succeeded);
      var staleJournal = await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-STALE", "GROUP_RECLASSIFICATION", "QAR", "stale-group-adjustment",
        [new(null, "CASH", 10m, 0m, "Must not write against a changed group perimeter"),
         new(null, "REVENUE", 0m, 10m, "Must not write against a changed group perimeter")]));
      Assert.False(staleJournal.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleJournal.ErrorCode);
      var changedPerimeter = await ConsolidationService.RunAsync(db, preparer, consolidationScopeId);
      Assert.False(changedPerimeter.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, changedPerimeter.ErrorCode);
      var staleReport = await ConsolidationService.GetLatestReportAsync(db, groupViewerActor, consolidationScopeId);
      Assert.True(staleReport.Succeeded, staleReport.Message);
      Assert.Equal("STALE", staleReport.Value!.State);
      Assert.Empty(staleReport.Value.Lines);
      Assert.Equal(sourcePackagesBefore, await CurrencyPackageSourceManifestAsync(db, [packageA, packageB]));
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GroupDeliverable_BuildsImmutableDeliverable_EnforcesIndependentIssuance_RetainsGroupScope_AndPreservesComponentBooks()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    var groupViewer = User(scope.FirmId, "deliverable-group-viewer");
    var groupViewerActor = Actor(groupViewer, "AccountingReviewer");
    var siblingUser = User(scope.FirmId, "sibling-group-user");
    var siblingActor = Actor(siblingUser, "Partner");
    var adminUser = User(scope.FirmId, "firm-admin-oversight");
    var adminOversightActor = Actor(adminUser, "Administrator");

    Guid groupId, siblingGroupId, consolidationScopeId, runId;
    string runHash;
    long groupRevision;
    var buildRequestKey = Guid.NewGuid().ToString("D");
    var issueRequestKey = Guid.NewGuid().ToString("D");
    const string issueReleaseKey = "REL-KEY-2026-GROUP";
    var archiveRequestKey = Guid.NewGuid().ToString("D");
    const string archiveReason = "Statutory archiving requirement";
    string sourcePackagesBefore, sourceClientBooksBefore;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer, new ClientGroupRequest("DELIV-GROUP", "Deliverable Group"))).Value;
      siblingGroupId = (await ConsolidationService.CreateGroupAsync(db, reviewer, new ClientGroupRequest("SIBLING-GROUP", "Sibling Group"))).Value;

      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-a"))).Succeeded);
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientB, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m, "ownership-b"))).Succeeded);

      db.Users.AddRange(groupViewer, siblingUser, adminUser);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = adminUser.Id, Role = "Administrator",
        ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = scope.Reviewer.Id, Reason = "Administrator oversight role without group grant"
      });
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id },
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = scope.Preparer.Id, Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id },
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = groupId, UserId = groupViewer.Id, Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id },
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = scope.FirmId, GroupId = siblingGroupId, UserId = siblingUser.Id, Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = scope.Reviewer.Id }
      );

      var clientBPartnerGrant = new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = scope.Partner.Id, Role = "Partner",
        ClientId = scope.ClientB, EngagementId = scope.EngagementB, GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = scope.Reviewer.Id, Reason = "Partner fixture"
      };
      db.RoleGrants.Add(clientBPartnerGrant);
      db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, ClientId = scope.ClientB, EngagementId = scope.EngagementB,
        UserId = scope.Partner.Id, StaffingLevel = StaffingLevels.EngagementPartner,
        RoleGrantId = clientBPartnerGrant.Id, AssignedByUserId = scope.Reviewer.Id, AssignedAt = DateTimeOffset.UtcNow
      });

      var periodId = Guid.NewGuid();
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = scope.FirmId, ClientId = scope.ClientA, PeriodCode = "FY2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Active,
        CreatedByUserId = scope.Preparer.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      consolidationScopeId = (await ConsolidationService.CreateScopeAsync(db, preparer,
        new ConsolidationScopeRequest(groupId, periodId, "", ConsolidationCalculator.RestrictedMethod, "OPENING-2026"))).Value;

      await BuildCurrencyPackageAsync(db, scope, scope.ClientA, scope.EngagementA, Gold07Lines, "group-a", "QAR");
      await BuildCurrencyPackageAsync(db, scope, scope.ClientB, scope.EngagementB,
        [("CASH", "ASSETS", 100m), ("CAPITAL", "EQUITY", 0m), ("REVENUE", "INCOME", -100m), ("EXPENSE", "EXPENSE", 0m)], "group-b", "QAR");
      await db.SaveChangesAsync();

      var packageA = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientA).Select(x => x.Id).SingleAsync();
      var packageB = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientB).Select(x => x.Id).SingleAsync();
      var packageMappings = await db.FinancialPackages.Where(x => x.Id == packageA || x.Id == packageB)
        .ToDictionaryAsync(x => x.Id, x => x.MappingVersionId);

      await ReviewCurrencyCompositionAsync(db, scope, packageA);
      await ReviewCurrencyCompositionAsync(db, scope, packageB);

      sourcePackagesBefore = await CurrencyPackageSourceManifestAsync(db, [packageA, packageB]);
      sourceClientBooksBefore = await ClientBookSourceManifestAsync(db, scope.FirmId, [scope.ClientA, scope.ClientB]);

      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientA, scope.EngagementA, packageA, 100m, "CONTROLLED", "STATUTORY", "tax-v1", packageMappings[packageA].ToString("D")))).Succeeded);
      Assert.True((await ConsolidationService.SubmitComponentAsync(db, preparer,
        new ConsolidationComponentRequest(consolidationScopeId, scope.ClientB, scope.EngagementB, packageB, 100m, "CONTROLLED", "STATUTORY", "tax-v1", packageMappings[packageB].ToString("D")))).Succeeded);

      foreach (var componentId in await db.ConsolidationComponents.Where(x => x.ScopeVersionId == consolidationScopeId).Select(x => x.Id).ToListAsync())
        Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);

      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.RestrictedMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "local-profile")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, Actor(scope.Preparer, "Partner"), capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "method-owner-fixture")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, consolidationScopeId)).Succeeded);

      var matchId = (await ConsolidationService.AddIntercompanyMatchAsync(db, preparer,
        new IntercompanyMatchRequest(consolidationScopeId, scope.ClientA, scope.ClientB,
          ConsolidationEliminationKinds.ReceivablePayable, "2026", "QAR", "IC-001", 100m, -100m, 100m, "ic-evidence", "CASH", "REVENUE"))).Value;
      Assert.True((await ConsolidationService.ApproveIntercompanyMatchAsync(db, reviewer, matchId)).Succeeded);

      var journalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(consolidationScopeId, "GC-001", "GROUP_RECLASSIFICATION", "QAR", "group-adjustment-001",
        [new(null, "CASH", 100m, 0m, "Cash reclass"), new(null, "REVENUE", 0m, 100m, "Rev reclass")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, journalId)).Succeeded);

      // Run is calculated by preparer and approved by independent reviewer
      runId = (await ConsolidationService.RunAsync(db, preparer, consolidationScopeId)).Value;
      Assert.True((await ConsolidationService.ApproveRunAsync(db, reviewer, runId)).Succeeded);
      runHash = await db.ConsolidationRuns.Where(x => x.FirmId == scope.FirmId && x.Id == runId)
        .Select(x => x.RunHash).SingleAsync();
      groupRevision = await db.ConsolidationScopeVersions.Where(x => x.FirmId == scope.FirmId && x.Id == consolidationScopeId)
        .Select(x => x.GroupRevision).SingleAsync();
    }

    // 1. Build immutable group deliverable (LC-06, T01)
    // Firm-wide administrator without group grant is denied build (T01-AC1)
    Guid deliverableId;
    string deliverableSha256;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var malformedKeyBuild = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash,
          "00000000-0000-4000-7000-000000000001", groupRevision));
      Assert.False(malformedKeyBuild.Succeeded);
      Assert.Equal("deliverable.invalid", malformedKeyBuild.ErrorCode);

      var adminBuildRefusal = await ConsolidationService.BuildGroupDeliverableAsync(db, adminOversightActor,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash, Guid.NewGuid().ToString("D"), groupRevision));
      Assert.False(adminBuildRefusal.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, adminBuildRefusal.ErrorCode);

      // Permitted oversight browsing works for administrator (T01-AC5)
      var oversightReport = await ConsolidationService.GetLatestReportAsync(db, adminOversightActor, consolidationScopeId);
      Assert.True(oversightReport.Succeeded);

      // T02: Build with RequestKey and ExpectedRunHash
      var build = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash, buildRequestKey, groupRevision));
      Assert.True(build.Succeeded, build.Message);
      Assert.Equal(GroupDeliverableStates.Built, build.Value!.Status);
      Assert.NotEmpty(build.Value.DeliverableSha256);
      Assert.NotEmpty(build.Value.DeliverableJson);
      Assert.Equal(groupId, build.Value.GroupId);
      Assert.Equal(consolidationScopeId, build.Value.ScopeVersionId);
      Assert.Equal(buildRequestKey, build.Value.RequestKey);
      deliverableId = build.Value.DeliverableId;
      deliverableSha256 = build.Value.DeliverableSha256;

      // Idempotent build replay with same key returns original deliverable
      var buildReplay = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash, buildRequestKey, groupRevision));
      Assert.True(buildReplay.Succeeded);
      Assert.Equal(deliverableId, buildReplay.Value!.DeliverableId);

      // The same request identity never discloses another actor's build receipt.
      var wrongActorBuildReplay = await ConsolidationService.BuildGroupDeliverableAsync(db, reviewer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash, buildRequestKey, groupRevision));
      Assert.False(wrongActorBuildReplay.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, wrongActorBuildReplay.ErrorCode);

      var wrongScopeBuildReplay = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(Guid.NewGuid(), runId, runHash, buildRequestKey, groupRevision));
      Assert.False(wrongScopeBuildReplay.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongScopeBuildReplay.ErrorCode);

      var changedBuildIntent = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId,
          runHash == new string('0', 64) ? new string('f', 64) : new string('0', 64), buildRequestKey, groupRevision));
      Assert.False(changedBuildIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedBuildIntent.ErrorCode);

      // Build with mismatching ExpectedRunHash fails with GenerationStale
      var badHashBuild = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId,
          runHash == new string('0', 64) ? new string('f', 64) : new string('0', 64), Guid.NewGuid().ToString("D"), groupRevision));
      Assert.False(badHashBuild.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, badHashBuild.ErrorCode);
    }

    // 2. Controlled group approval / issuance (LC-07, T01):
    //    Preparer of the run cannot self-issue (reviewer independence rule)
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var selfIssue = await ConsolidationService.IssueGroupDeliverableAsync(db, preparer,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          deliverableSha256, "REL-KEY-SELF-ISSUE", Guid.NewGuid().ToString("D")));
      Assert.False(selfIssue.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, selfIssue.ErrorCode);

      // Administrator without group grant cannot issue (T01-AC1)
      var adminIssueRefusal = await ConsolidationService.IssueGroupDeliverableAsync(db, adminOversightActor,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          deliverableSha256, "REL-KEY-ADMIN", Guid.NewGuid().ToString("D")));
      Assert.False(adminIssueRefusal.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, adminIssueRefusal.ErrorCode);

      // Scope mismatch / wrong parent scope is refused without mutation (T01-AC3)
      var wrongScopeIssue = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer,
        new IssueGroupDeliverableRequest(Guid.NewGuid(), deliverableId, 1, groupRevision,
          deliverableSha256, "REL-KEY-WRONG-SCOPE", Guid.NewGuid().ToString("D")));
      Assert.False(wrongScopeIssue.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongScopeIssue.ErrorCode);

      // Independent Partner/Reviewer with explicit group grant can issue (T01-AC2, T03)
      // Mismatched expected SHA fails
      var badShaIssue = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          new string('0', 64) == deliverableSha256 ? new string('f', 64) : new string('0', 64), issueReleaseKey, issueRequestKey));
      Assert.False(badShaIssue.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, badShaIssue.ErrorCode);

      var issue = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          deliverableSha256, issueReleaseKey, issueRequestKey));
      Assert.True(issue.Succeeded, issue.Message);
      Assert.Equal(GroupDeliverableStates.Issued, issue.Value!.Status);
      Assert.Equal(issueReleaseKey, issue.Value.AuthorizedReleaseKey);
      Assert.Equal(reviewer.UserId, issue.Value.IssuedByUserId);
      Assert.Equal(issueRequestKey, issue.Value.IssueRequestKey);
      Assert.NotNull(issue.Value.IssuedAt);
      Assert.Equal(2, issue.Value.Revision);

      // Replay issue with same request key returns Ok
      var issueReplay = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          deliverableSha256, issueReleaseKey, issueRequestKey));
      Assert.True(issueReplay.Succeeded);
      Assert.Equal(GroupDeliverableStates.Issued, issueReplay.Value!.Status);
      Assert.Equal(2, issueReplay.Value.Revision);

      var changedIssueIntent = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          deliverableSha256, "REL-KEY-CHANGED", issueRequestKey));
      Assert.False(changedIssueIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedIssueIntent.ErrorCode);
    }

    // 3. Retain issued group output with group-scope lineage (LC-08, T01):
    //    Sibling group user is denied; group viewer can retrieve and verify SHA-256 byte integrity
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var siblingRead = await ConsolidationService.GetIssuedGroupDeliverableAsync(db, siblingActor, consolidationScopeId);
      Assert.False(siblingRead.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, siblingRead.ErrorCode);

      // Administrator without group grant is denied retrieval and download (T01-AC1)
      var adminGetRefusal = await ConsolidationService.GetIssuedGroupDeliverableAsync(db, adminOversightActor, consolidationScopeId);
      Assert.False(adminGetRefusal.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, adminGetRefusal.ErrorCode);

      var adminDownloadRefusal = await ConsolidationService.DownloadIssuedGroupDeliverableAsync(db, adminOversightActor, consolidationScopeId);
      Assert.False(adminDownloadRefusal.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, adminDownloadRefusal.ErrorCode);

      var issued = await ConsolidationService.GetIssuedGroupDeliverableAsync(db, groupViewerActor, consolidationScopeId);
      Assert.True(issued.Succeeded, issued.Message);
      Assert.Equal(deliverableSha256, issued.Value!.DeliverableSha256);
      Assert.Equal(GroupDeliverableStates.Issued, issued.Value.Status);

      // Download bytes and verify integrity
      var export = await ConsolidationService.DownloadIssuedGroupDeliverableAsync(db, groupViewerActor, consolidationScopeId);
      Assert.True(export.Succeeded, export.Message);
      Assert.Equal(deliverableSha256, export.Value!.ContentSha256);
      Assert.Equal(deliverableSha256, export.Value.DeliverableSha256);
      Assert.NotEmpty(export.Value.Content);

      // Scope mismatch on archive is refused (T01-AC3)
      var wrongScopeArchive = await ConsolidationService.ArchiveGroupDeliverableAsync(db, reviewer,
        new ArchiveGroupDeliverableRequest(Guid.NewGuid(), deliverableId, 2, deliverableSha256,
          "Statutory archiving requirement", Guid.NewGuid().ToString("D")));
      Assert.False(wrongScopeArchive.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, wrongScopeArchive.ErrorCode);

      // Administrator without group grant is denied archive (T01-AC1)
      var adminArchiveRefusal = await ConsolidationService.ArchiveGroupDeliverableAsync(db, adminOversightActor,
        new ArchiveGroupDeliverableRequest(consolidationScopeId, deliverableId, 2, deliverableSha256,
          "Statutory archiving requirement", Guid.NewGuid().ToString("D")));
      Assert.False(adminArchiveRefusal.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, adminArchiveRefusal.ErrorCode);

      // Archive issued deliverable (T03)
      var badShaArchive = await ConsolidationService.ArchiveGroupDeliverableAsync(db, reviewer,
        new ArchiveGroupDeliverableRequest(consolidationScopeId, deliverableId, 2,
          new string('0', 64) == deliverableSha256 ? new string('f', 64) : new string('0', 64),
          archiveReason, archiveRequestKey));
      Assert.False(badShaArchive.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, badShaArchive.ErrorCode);

      var archive = await ConsolidationService.ArchiveGroupDeliverableAsync(db, reviewer,
        new ArchiveGroupDeliverableRequest(consolidationScopeId, deliverableId, 2, deliverableSha256,
          archiveReason, archiveRequestKey));
      Assert.True(archive.Succeeded, archive.Message);
      Assert.Equal(GroupDeliverableStates.Archived, archive.Value!.Status);
      Assert.Equal(reviewer.UserId, archive.Value.ArchivedByUserId);
      Assert.NotNull(archive.Value.ArchivedAt);
      Assert.Equal(archiveReason, archive.Value.ArchiveReason);
      Assert.Equal(archiveRequestKey, archive.Value.ArchiveRequestKey);
      Assert.Equal(3, archive.Value.Revision);

      // Archive replay with same request key returns Ok
      var archiveReplay = await ConsolidationService.ArchiveGroupDeliverableAsync(db, reviewer,
        new ArchiveGroupDeliverableRequest(consolidationScopeId, deliverableId, 2, deliverableSha256,
          archiveReason, archiveRequestKey));
      Assert.True(archiveReplay.Succeeded);
      Assert.Equal(GroupDeliverableStates.Archived, archiveReplay.Value!.Status);
      Assert.Equal(3, archiveReplay.Value.Revision);

      var changedArchiveIntent = await ConsolidationService.ArchiveGroupDeliverableAsync(db, reviewer,
        new ArchiveGroupDeliverableRequest(consolidationScopeId, deliverableId, 2, deliverableSha256,
          "Different archive reason", archiveRequestKey));
      Assert.False(changedArchiveIntent.Succeeded);
      Assert.Equal(ErrorCodes.IdempotencyConflict, changedArchiveIntent.ErrorCode);

      // A build retry after later transitions still returns the original build receipt.
      var buildReplayAfterArchive = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash, buildRequestKey, groupRevision));
      Assert.True(buildReplayAfterArchive.Succeeded);
      Assert.Equal(deliverableId, buildReplayAfterArchive.Value!.DeliverableId);
      Assert.Equal(GroupDeliverableStates.Built, buildReplayAfterArchive.Value.Status);
      Assert.Equal(1, buildReplayAfterArchive.Value.Revision);

      var issueReplayAfterArchive = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer,
        new IssueGroupDeliverableRequest(consolidationScopeId, deliverableId, 1, groupRevision,
          deliverableSha256, issueReleaseKey, issueRequestKey));
      Assert.True(issueReplayAfterArchive.Succeeded);
      Assert.Equal(GroupDeliverableStates.Issued, issueReplayAfterArchive.Value!.Status);
      Assert.Equal(2, issueReplayAfterArchive.Value.Revision);

      // T05: Exact deliverable lookup by ID and list deliverables
      var exactById = await ConsolidationService.GetGroupDeliverableByIdAsync(db, groupViewerActor, consolidationScopeId, deliverableId);
      Assert.True(exactById.Succeeded);
      Assert.Equal(deliverableId, exactById.Value!.DeliverableId);
      Assert.Equal(GroupDeliverableStates.Archived, exactById.Value.Status);

      var downloadById = await ConsolidationService.DownloadGroupDeliverableByIdAsync(db, groupViewerActor, consolidationScopeId, deliverableId);
      Assert.True(downloadById.Succeeded);
      Assert.Equal(deliverableSha256, downloadById.Value!.ContentSha256);

      var list = await ConsolidationService.ListGroupDeliverablesAsync(db, groupViewerActor, consolidationScopeId);
      Assert.True(list.Succeeded);
      Assert.Contains(list.Value!.Items, d => d.DeliverableId == deliverableId);
      Assert.Equal(25, list.Value.PageSize);
      Assert.DoesNotContain(typeof(GroupDeliverableSummary).GetProperties(),
        property => property.Name == nameof(GroupDeliverableResult.DeliverableJson));

      // Prepare a second, unissued artifact while the reviewed perimeter is still current.
      // Its first issue request will wait behind the membership revision transaction.
      var issueRecoveryBuild = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash,
          Guid.NewGuid().ToString("D"), groupRevision));
      Assert.True(issueRecoveryBuild.Succeeded, issueRecoveryBuild.Message);
      var uncommittedIssueDeliverableId = issueRecoveryBuild.Value!.DeliverableId;
      var uncommittedIssueDeliverableSha = issueRecoveryBuild.Value.DeliverableSha256;

      var lateClient = new PracticeClient
      {
        Id = Guid.CreateVersion7(), FirmId = scope.FirmId,
        LegalName = "Late perimeter member", CreatedAt = DateTimeOffset.UtcNow
      };
      db.PracticeClients.Add(lateClient);
      await db.SaveChangesAsync();

      var membershipPause = new PauseGroupMembershipSave();
      var membershipOptions = new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
        .AddInterceptors(membershipPause).Options;
      await using var membershipDb = new AuditSphereDbContext(membershipOptions);
      Task<CommandResult>? membershipTask = null;
      Task<CommandResult<GroupDeliverableResult>>? racingIssueTask = null;
      CommandResult? perimeterChange = null;
      CommandResult<GroupDeliverableResult>? racingIssue = null;
      var originalIssueRequest = new IssueGroupDeliverableRequest(consolidationScopeId,
        uncommittedIssueDeliverableId, 1, groupRevision, uncommittedIssueDeliverableSha,
        "REL-T18-UNCOMMITTED", Guid.NewGuid().ToString("D"));
      await using var issueDb = new AuditSphereDbContext(pg.Options);
      try
      {
        membershipTask = ConsolidationService.AddMembershipAsync(membershipDb, reviewer,
          new GroupMembershipRequest(groupId, lateClient.Id, new DateOnly(2027, 1, 1), null,
            "CONTROLLED", 100m, 100m, "T18 permanent perimeter revision"));
        await membershipPause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await issueDb.Database.OpenConnectionAsync();
        var issuePid = ((NpgsqlConnection)issueDb.Database.GetDbConnection()).ProcessID;
        racingIssueTask = ConsolidationService.IssueGroupDeliverableAsync(issueDb, reviewer, originalIssueRequest);
        await using var observer = new AuditSphereDbContext(pg.Options);
        await WaitForClientGroupLockBlockingPidAsync(observer, issuePid);
        Assert.False(racingIssueTask.IsCompleted);
      }
      finally
      {
        membershipPause.Release.TrySetResult(true);
        if (membershipTask is not null)
          perimeterChange = await membershipTask.WaitAsync(TimeSpan.FromSeconds(30));
        if (racingIssueTask is not null)
          racingIssue = await racingIssueTask.WaitAsync(TimeSpan.FromSeconds(30));
      }
      Assert.NotNull(perimeterChange);
      Assert.True(perimeterChange.Succeeded, perimeterChange.Message);
      Assert.Equal(groupRevision + 1, await db.ClientGroups.AsNoTracking()
        .Where(x => x.FirmId == scope.FirmId && x.Id == groupId).Select(x => x.Revision).SingleAsync());
      Assert.NotNull(racingIssue);
      Assert.False(racingIssue.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.GroupPerimeterStale, racingIssue.ErrorCode);

      // A retry resolves the absent original under the same lock; a late duplicate remains fenced.
      var originalIssueRecovery = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer, originalIssueRequest);
      Assert.False(originalIssueRecovery.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.GroupPerimeterStale, originalIssueRecovery.ErrorCode);
      var lateIssueArrival = await ConsolidationService.IssueGroupDeliverableAsync(db, reviewer, originalIssueRequest);
      Assert.False(lateIssueArrival.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.GroupPerimeterStale, lateIssueArrival.ErrorCode);
      var unissuedDeliverableAfterRace = await db.GroupDeliverables.AsNoTracking().SingleAsync(x =>
        x.FirmId == scope.FirmId && x.ScopeVersionId == consolidationScopeId && x.Id == uncommittedIssueDeliverableId);
      Assert.Equal(GroupDeliverableStates.Built, unissuedDeliverableAfterRace.Status);
      Assert.Equal(1, unissuedDeliverableAfterRace.Revision);
      Assert.Null(unissuedDeliverableAfterRace.IssueRequestKey);
      Assert.Null(unissuedDeliverableAfterRace.IssuedAt);

      var committedReplayAfterPerimeterChange = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer,
        new BuildGroupDeliverableRequest(consolidationScopeId, runId, runHash, buildRequestKey, groupRevision));
      Assert.True(committedReplayAfterPerimeterChange.Succeeded, committedReplayAfterPerimeterChange.Message);
      Assert.Equal(deliverableId, committedReplayAfterPerimeterChange.Value!.DeliverableId);

      var uncommittedRequestKey = Guid.NewGuid().ToString("D");
      var originalRequest = new BuildGroupDeliverableRequest(
        consolidationScopeId, runId, runHash, uncommittedRequestKey, groupRevision);
      var recoveryAfterPerimeterChange = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer, originalRequest);
      Assert.False(recoveryAfterPerimeterChange.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.GroupPerimeterStale, recoveryAfterPerimeterChange.ErrorCode);
      Assert.False(await db.GroupDeliverables.AsNoTracking().AnyAsync(x =>
        x.FirmId == scope.FirmId && x.ScopeVersionId == consolidationScopeId && x.RequestKey == uncommittedRequestKey));

      // Simulate the delayed original arrival after the client has reconciled the stale request.
      var lateOriginalArrival = await ConsolidationService.BuildGroupDeliverableAsync(db, preparer, originalRequest);
      Assert.False(lateOriginalArrival.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.GroupPerimeterStale, lateOriginalArrival.ErrorCode);
      Assert.False(await db.GroupDeliverables.AsNoTracking().AnyAsync(x =>
        x.FirmId == scope.FirmId && x.ScopeVersionId == consolidationScopeId && x.RequestKey == uncommittedRequestKey));

      // Component books are completely unchanged
      var packagesAfter = await db.FinancialPackages.Where(x => x.ClientId == scope.ClientA || x.ClientId == scope.ClientB).Select(x => x.Id).ToArrayAsync();
      Assert.Equal(sourcePackagesBefore, await CurrencyPackageSourceManifestAsync(db, packagesAfter));
      Assert.Equal(sourceClientBooksBefore, await ClientBookSourceManifestAsync(db, scope.FirmId, [scope.ClientA, scope.ClientB]));
    }

    await using (var update = new AuditSphereDbContext(pg.Options))
    {
      var immutableBytes = await Assert.ThrowsAsync<PostgresException>(() =>
        update.Database.ExecuteSqlInterpolatedAsync(
          $"UPDATE group_deliverables SET deliverable_sha256 = {new string('0', 64)} WHERE firm_id = {scope.FirmId} AND id = {deliverableId}"));
      Assert.Equal("55000", immutableBytes.SqlState);
    }

    await using (var delete = new AuditSphereDbContext(pg.Options))
    {
      var immutableDelete = await Assert.ThrowsAsync<PostgresException>(() =>
        delete.Database.ExecuteSqlInterpolatedAsync(
          $"DELETE FROM group_deliverables WHERE firm_id = {scope.FirmId} AND id = {deliverableId}"));
      Assert.Equal("55000", immutableDelete.SqlState);
    }

    await using (var truncate = new AuditSphereDbContext(pg.Options))
    {
      var immutableTruncate = await Assert.ThrowsAsync<PostgresException>(() =>
        truncate.Database.ExecuteSqlRawAsync("TRUNCATE TABLE group_deliverables"));
      Assert.Equal("55000", immutableTruncate.SqlState);
    }

    await using (var migrationDb = new AuditSphereDbContext(pg.Options))
    {
      var migrations = migrationDb.Database.GetMigrations().ToArray();
      var migrationIndex = Array.FindIndex(migrations,
        name => name.EndsWith("_LifecycleAuditRemediationIntegrity", StringComparison.Ordinal));
      Assert.True(migrationIndex > 0, "The lifecycle integrity migration must be discoverable.");

      var downgrade = await Assert.ThrowsAnyAsync<Exception>(() =>
        migrationDb.GetService<IMigrator>().MigrateAsync(migrations[migrationIndex - 1]));
      Assert.Equal("55000", Assert.IsType<PostgresException>(downgrade.GetBaseException()).SqlState);
      Assert.Contains(migrations[migrationIndex], await migrationDb.Database.GetAppliedMigrationsAsync());

      var retained = await migrationDb.GroupDeliverables.SingleAsync(x => x.FirmId == scope.FirmId && x.Id == deliverableId);
      Assert.Equal(deliverableSha256, retained.DeliverableSha256);
      Assert.Equal(GroupDeliverableStates.Archived, retained.Status);
    }
  }

  private static async Task WaitForClientGroupLockBlockingPidAsync(AuditSphereDbContext observer, int waitingPid)
  {
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    try
    {
      while (true)
      {
        await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()", timeout.Token);
        var blocked = await observer.Database.SqlQuery<bool>($"""
          SELECT EXISTS (
            SELECT 1 FROM pg_stat_activity AS waiting
            WHERE waiting.datname = current_database()
              AND waiting.pid = {waitingPid}
              AND waiting.wait_event_type = 'Lock'
              AND waiting.query ILIKE '%client_groups%'
              AND cardinality(pg_blocking_pids(waiting.pid)) > 0
          ) AS "Value"
          """).SingleAsync(timeout.Token);
        if (blocked) return;
        await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
      }
    }
    catch (OperationCanceledException) when (timeout.IsCancellationRequested)
    {
      Assert.Fail($"Issue session {waitingPid} did not wait behind the held client-group perimeter lock.");
    }
  }

  private sealed class PauseGroupMembershipSave : SaveChangesInterceptor
  {
    public TaskCompletionSource<bool> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
      InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
      var context = eventData.Context!;
      if (context.ChangeTracker.Entries<ClientGroup>().Any(x => x.State == EntityState.Modified) &&
          context.ChangeTracker.Entries<ClientGroupMembership>().Any(x => x.State == EntityState.Added))
      {
        Reached.TrySetResult(true);
        await Release.Task.WaitAsync(cancellationToken);
      }
      return result;
    }
  }
}
