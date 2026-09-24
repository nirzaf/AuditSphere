using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed class R2RRemainingModulesTests
{
  private sealed record TestScope(
    Guid FirmId,
    Guid ClientId,
    Guid EngagementId,
    AppUser Preparer,
    AppUser Reviewer);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReviseProfile_SucceedsAndGuardsRevisionConcurrency()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");

    Guid profileId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var createResult = await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(scope.ClientId, "QA", "QAR", 1, 1, "SAP", "SYSTEM-01"));
      Assert.True(createResult.Succeeded);
      profileId = createResult.Value;
    }

    // Revise successfully with expectedRevision = 1
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var reviseResult = await ClientAccountingService.ReviseProfileAsync(db, preparer, profileId,
        new ClientAccountingProfileRequest(scope.ClientId, "QA", "QAR", 4, 1, "SAP-REVISED", "SYSTEM-02"),
        expectedRevision: 1);
      Assert.True(reviseResult.Succeeded);
      Assert.Equal(2, reviseResult.Value);
    }

    // Stale revision attempt should fail closed
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staleResult = await ClientAccountingService.ReviseProfileAsync(db, preparer, profileId,
        new ClientAccountingProfileRequest(scope.ClientId, "QA", "QAR", 4, 1, "SAP-REVISED", "SYSTEM-02"),
        expectedRevision: 1);
      Assert.False(staleResult.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleResult.ErrorCode);

      var profile = await db.ClientAccountingProfiles.SingleAsync(x => x.Id == profileId);
      Assert.Equal(2, profile.Revision);
      Assert.Equal("SAP-REVISED", profile.SourceSystem);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GetChartRevisionAccounts_ReturnsHierarchyAndChildCounts()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");

    Guid chartId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var chartResult = await ClientAccountingService.CreateChartVersionAsync(db, preparer, scope.ClientId, "LEDGER-1", new DateOnly(2026, 1, 1));
      Assert.True(chartResult.Succeeded);
      chartId = chartResult.Value;

      var addResult = await ClientAccountingService.AddAccountsAsync(db, preparer, chartId, [
        new("asset-header", "1000", "Assets", "ASSET", "DEBIT", false),
        new("cash-account", "1010", "Cash at Bank", "ASSET", "DEBIT", true, "asset-header"),
        new("petty-cash", "1020", "Petty Cash", "ASSET", "DEBIT", true, "asset-header"),
        new("rev-header", "4000", "Revenue", "INCOME", "CREDIT", true)
      ]);
      Assert.True(addResult.Succeeded, addResult.Message);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pageResult = await ClientAccountingService.GetChartRevisionAccountsAsync(db, preparer, chartId, page: 1, pageSize: 10);
      Assert.True(pageResult.Succeeded);
      var page = pageResult.Value!;
      Assert.Equal(4, page.TotalCount);
      Assert.Equal(4, page.Items.Count);

      var assetHeader = page.Items.Single(x => x.AccountCode == "1000");
      Assert.Equal(2, assetHeader.ChildCount);
      Assert.Null(assetHeader.ParentAccountCode);

      var cashAccount = page.Items.Single(x => x.AccountCode == "1010");
      Assert.Equal("1000", cashAccount.ParentAccountCode);
      Assert.Equal(0, cashAccount.ChildCount);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task TrialBalanceRows_PagedQueryAndCsvExport()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");

    var datasetId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var dataset = new TrialBalanceDataset
      {
        Id = datasetId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        Sha256Hex = new string('1', 64),
        RawFileSha256Hex = new string('2', 64),
        NormalizedDatasetDigest = new string('3', 64),
        ValidationStatus = "Accepted",
        ImportState = TrialBalanceImportStates.Loading,
        Balanced = true,
        Currency = "QAR",
        Basis = "STATUTORY",
        Revision = 1,
        ImportedByUserId = scope.Preparer.Id,
        ImportedAt = DateTimeOffset.UtcNow
      };
      db.TrialBalanceDatasets.Add(dataset);

      db.TrialBalanceRows.AddRange(
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash", Amount = 100m, SourceDebit = 100m, Currency = "QAR", Entity = "HQ" },
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1010", AccountName = "Bank", Amount = 200m, SourceDebit = 200m, Currency = "QAR", Entity = "HQ" },
        new TrialBalanceRow { Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "4000", AccountName = "Sales", Amount = -300m, SourceCredit = 300m, Currency = "QAR", Entity = "HQ" });
      await db.SaveChangesAsync();

      dataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      // Query paged rows
      var pagedResult = await TrialBalanceDatasetQuery.GetTrialBalanceRowsAsync(db, preparer, datasetId, page: 1, pageSize: 2);
      Assert.True(pagedResult.Succeeded);
      Assert.Equal(3, pagedResult.Value!.TotalCount);
      Assert.Equal(2, pagedResult.Value.Items.Count);
      Assert.Equal(0m, pagedResult.Value.TotalAmount); // 100 + 200 - 300 = 0

      // Filter by account prefix
      var filteredResult = await TrialBalanceDatasetQuery.GetTrialBalanceRowsAsync(db, preparer, datasetId, accountCodeFilter: "10");
      Assert.True(filteredResult.Succeeded);
      Assert.Equal(2, filteredResult.Value!.TotalCount);
      Assert.Equal(300m, filteredResult.Value.TotalAmount);

      // Export CSV
      var exportResult = await TrialBalanceDatasetQuery.ExportTrialBalanceCsvAsync(db, preparer, datasetId);
      Assert.True(exportResult.Succeeded);
      Assert.Contains("\"account_code\",\"account_name\",\"amount\"", exportResult.Value!.Csv);
      Assert.Contains("\"1000\",\"Cash\",100", exportResult.Value.Csv);
      Assert.Contains("\"4000\",\"Sales\",-300", exportResult.Value.Csv);
      Assert.Equal(1, exportResult.Value.Revision);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task UpdateAdjustmentDraft_SucceedsAndIncrementsRevision()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");

    var datasetId = Guid.CreateVersion7();
    Guid journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        Sha256Hex = new string('1', 64),
        RawFileSha256Hex = new string('2', 64),
        NormalizedDatasetDigest = new string('3', 64),
        ValidationStatus = "Accepted",
        ImportState = TrialBalanceImportStates.Sealed,
        Balanced = true,
        Currency = "QAR",
        Basis = "STATUTORY",
        Revision = 1,
        ImportedByUserId = scope.Preparer.Id,
        ImportedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();

      var draftResult = await AdjustmentJournalService.CreateDraftAsync(db, preparer, datasetId, "JRN-001",
        [("1000", 50m, 0m), ("4000", 0m, 50m)]);
      Assert.True(draftResult.Succeeded);
      journalId = draftResult.Value;
    }

    // Update lines and reason
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var updateResult = await AdjustmentJournalService.UpdateDraftAsync(db, preparer, journalId,
        [("1000", 150m, 0m), ("4000", 0m, 150m)],
        "UPDATED_REASON",
        "EVIDENCE_V2",
        expectedRevision: 1);
      Assert.True(updateResult.Succeeded);
      Assert.Equal(2, updateResult.Value);
    }

    // Check updated lines in DB
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = await db.AdjustmentJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal(2, journal.Revision);
      Assert.Equal("UPDATED_REASON", journal.Reason);
      Assert.Equal("EVIDENCE_V2", journal.EvidenceReference);

      var lines = await db.AdjustmentLines.Where(x => x.JournalId == journalId).ToListAsync();
      Assert.Equal(2, lines.Count);
      Assert.Equal(150m, lines.Single(x => x.AccountCode == "1000").Debit);

      // Attempt update with outdated revision should fail
      var staleUpdate = await AdjustmentJournalService.UpdateDraftAsync(db, preparer, journalId,
        [("1000", 200m, 0m), ("4000", 0m, 200m)],
        "STALE", "STALE_EV", expectedRevision: 1);
      Assert.False(staleUpdate.Succeeded);
      Assert.Equal(ErrorCodes.StaleRevision, staleUpdate.ErrorCode);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReconciliationProof_CalculatesResidualAndLinksCorrectionJournal()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");

    var periodId = Guid.CreateVersion7();
    var datasetId = Guid.CreateVersion7();
    var reconciliationId = Guid.CreateVersion7();
    var itemId = Guid.CreateVersion7();
    var journalId = Guid.CreateVersion7();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        PeriodCode = "2026",
        StartDate = new DateOnly(2026, 1, 1),
        EndDate = new DateOnly(2026, 12, 31),
        Basis = "STATUTORY",
        Currency = "QAR",
        CreatedByUserId = scope.Preparer.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        PeriodId = periodId,
        Sha256Hex = new string('1', 64),
        RawFileSha256Hex = new string('2', 64),
        NormalizedDatasetDigest = new string('3', 64),
        ValidationStatus = "Accepted",
        ImportState = TrialBalanceImportStates.Sealed,
        Balanced = true,
        Currency = "QAR",
        Basis = "STATUTORY",
        Revision = 1,
        ImportedByUserId = scope.Preparer.Id,
        ImportedAt = DateTimeOffset.UtcNow
      });

      db.AccountingReconciliations.Add(new AccountingReconciliation
      {
        Id = reconciliationId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        PeriodId = periodId,
        Area = "BANK",
        TrialBalanceDatasetId = datasetId,
        AccountSelection = "1000",
        AsOfDate = new DateOnly(2026, 6, 30),
        SourceTotal = 1000m,
        GlTotal = 1200m, // Difference of 200
        Residual = 200m,
        Status = "UNRECONCILED",
        SourceHash = new string('3', 64),
        CreatedByUserId = scope.Preparer.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.AccountingReconciliationItems.Add(new AccountingReconciliationItem
      {
        Id = itemId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        ReconciliationId = reconciliationId,
        StableItemId = "ITEM-001",
        SignedAmount = 200m, // Reconciling item of 200 explains difference
        Currency = "QAR",
        Reason = "Deposit in transit",
        EvidenceReference = "BANK-STMT-01",
        Disposition = "PENDING_JOURNAL",
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.AdjustmentJournals.Add(new AdjustmentJournal
      {
        Id = journalId,
        FirmId = scope.FirmId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        BaseDatasetId = datasetId,
        JournalNumber = "CORR-01",
        Purpose = AdjustmentJournalPurposes.ReportingAdjustment,
        Origin = AdjustmentJournalOrigins.AuditProposed,
        Reason = "Bank correction",
        EvidenceReference = "BANK-REC",
        Status = "Draft",
        Revision = 1,
        CreatedByUserId = scope.Preparer.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });

      await db.SaveChangesAsync();
    }

    // Calculate reconciliation proof
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var proofResult = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, reconciliationId);
      Assert.True(proofResult.Succeeded);
      var proof = proofResult.Value!;
      Assert.Equal(1000m, proof.SourceTotal);
      Assert.Equal(1200m, proof.GlTotal);
      Assert.Equal(200m, proof.ItemsSignedTotal);
      Assert.Equal(0m, proof.Residual);
      Assert.True(proof.IsReconciled);
      Assert.Equal("RECONCILED", proof.Status);

      // Link correction journal
      var linkResult = await AccountingAnalysisService.LinkReconciliationCorrectionAsync(db, preparer, reconciliationId, itemId, journalId);
      Assert.True(linkResult.Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var item = await db.AccountingReconciliationItems.SingleAsync(x => x.Id == itemId);
      Assert.Equal("JOURNAL:CORR-01", item.SettlementReference);
      Assert.Equal("LINKED_JOURNAL", item.Disposition);
      Assert.NotNull(item.SettlementDate);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReplaceConsolidationComponent_SucceedsAndValidatesPerimeter()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");

    var groupId = Guid.CreateVersion7();
    var scopeId = Guid.CreateVersion7();
    var oldComponentId = Guid.CreateVersion7();

    Guid package1Id, package2Id, mappingVersionId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId,
        FirmId = scope.FirmId,
        Code = "GRP-01",
        Name = "Alpha Group",
        Revision = 1,
        Status = AccountingWorkflowStates.Active,
        CreatedByUserId = scope.Preparer.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.CreateVersion7(),
        FirmId = scope.FirmId,
        GroupId = groupId,
        ClientId = scope.ClientId,
        EffectiveFrom = new DateOnly(2026, 1, 1),
        ControlMethod = "CONTROLLED",
        OwnershipPercent = 100m,
        Status = AccountingWorkflowStates.Approved,
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeId,
        FirmId = scope.FirmId,
        GroupId = groupId,
        GroupRevision = 1,
        Version = 1,
        ReportingCurrency = "QAR",
        Method = ConsolidationCalculator.RestrictedMethod,
        OpeningBasis = "STATUTORY",
        Status = AccountingWorkflowStates.Draft,
        CreatedByUserId = scope.Preparer.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.NewGuid(),
        FirmId = scope.FirmId,
        GroupId = groupId,
        UserId = scope.Preparer.Id,
        Role = "AccountingPreparer",
        GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = scope.Preparer.Id
      });

      await db.SaveChangesAsync();

      var pkg1 = await AddPackageFixtureAsync(db, scope, scope.ClientId, scope.EngagementId, "hash-1");
      package1Id = pkg1.PackageId;
      mappingVersionId = pkg1.MappingVersionId;

      var pkg2 = await AddPackageFixtureAsync(db, scope, scope.ClientId, scope.EngagementId, "hash-2", mappingVersionId);
      package2Id = pkg2.PackageId;

      db.ConsolidationComponents.Add(new ConsolidationComponent
      {
        Id = oldComponentId,
        FirmId = scope.FirmId,
        GroupId = groupId,
        ScopeVersionId = scopeId,
        ClientId = scope.ClientId,
        EngagementId = scope.EngagementId,
        PackageId = package1Id,
        PackageHash = pkg1.CalculationHash,
        PeriodBasis = "STATUTORY",
        TaxonomyVersion = "tax-v1",
        MappingVersion = mappingVersionId.ToString("D"),
        Currency = "QAR",
        OwnershipPercent = 100m,
        ControlMethod = "CONTROLLED",
        SubmittedByUserId = scope.Preparer.Id,
        SubmittedAt = DateTimeOffset.UtcNow
      });

      await db.SaveChangesAsync();
    }

    // Replace component with Package 2
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var replaceResult = await ConsolidationService.ReplaceComponentAsync(db, preparer, oldComponentId,
        new ConsolidationComponentRequest(
          scopeId,
          scope.ClientId,
          scope.EngagementId,
          package2Id,
          100m,
          "CONTROLLED",
          "STATUTORY",
          "tax-v1",
          mappingVersionId.ToString("D")));

      Assert.True(replaceResult.Succeeded);
      Assert.NotEqual(oldComponentId, replaceResult.Value);
    }

    // Verify DB state
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var components = await db.ConsolidationComponents.Where(x => x.ScopeVersionId == scopeId).ToListAsync();
      Assert.Single(components);
      var current = components[0];
      Assert.Equal(package2Id, current.PackageId);
    }
  }

  private static async Task<(Guid PackageId, Guid MappingVersionId, string CalculationHash)> AddPackageFixtureAsync(
    AuditSphereDbContext db, TestScope scope, Guid clientId, Guid engagementId, string key, Guid? existingMappingId = null)
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = existingMappingId ?? Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"package-{clientId:D}-{key}");

    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw",
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = "QAR", RawFileSha256Hex = digest,
      NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m, ImportedAt = now, ImportedByUserId = scope.Preparer.Id
    });

    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      Status = "Finalized", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });

    if (!existingMappingId.HasValue)
    {
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId,
        Version = 1, Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Status = AccountingPackageStates.MappingApproved, CreatedByUserId = scope.Preparer.Id, ApprovedByUserId = scope.Reviewer.Id,
        ApprovedAt = now, CreatedAt = now
      });
    }

    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest, CreatedByUserId = scope.Preparer.Id, CreatedAt = now
    });

    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = scope.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = "template-v1", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = "QAR", Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });

    await db.SaveChangesAsync();
    return (packageId, mappingId, digest);
  }

  private static async Task<TestScope> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient { Id = clientId, FirmId = firmId, LegalName = "ALPHA CORP", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.Add(new Engagement { Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"),
      Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, reviewer, "Partner"));
    await db.SaveChangesAsync();

    return new TestScope(firmId, clientId, engagementId, preparer, reviewer);
  }

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(),
    FirmId = firmId,
    Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test",
    Email = name + "@example.test",
    DisplayName = name,
    CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(),
    FirmId = firmId,
    UserId = user.Id,
    Role = role,
    GrantedAt = DateTimeOffset.UtcNow,
    GrantedByUserId = user.Id
  };

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
