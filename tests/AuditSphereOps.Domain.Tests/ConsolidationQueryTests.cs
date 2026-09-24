using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Module 26 read-side contracts: translation bridge, intercompany
/// exceptions and component readiness are group-scoped paged projections.</summary>
public sealed class ConsolidationQueryTests
{
  private sealed record Fixture(
    Guid FirmId, Guid ClientA, Guid ClientB, Guid EngagementA, Guid EngagementB,
    AppUser Manager, AppUser GroupReviewer, AppUser Outsider, Guid GroupId, Guid ScopeId);

  [Fact]
  [Trait("Profile", "Database")]
  public async Task TranslationBridgeAndExceptions_ArePagedScopedAndShowUnmatchedAmounts()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manager = Actor(fixture.Manager, "Partner");
    var groupReviewer = Actor(fixture.GroupReviewer, "AccountingReviewer");
    var outsider = Actor(fixture.Outsider, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);

    Guid componentId, rateSetId, policyId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var denied = await ConsolidationQuery.GetTranslationBridgeAsync(db, outsider, fixture.ScopeId);
      Assert.False(denied.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, denied.ErrorCode);

      rateSetId = (await CurrencyTranslationService.CreateRateSetAsync(db, manager,
        new ExchangeRateSetRequest("FX-BRIDGE", "Synthetic source", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)))).Value;
      Assert.True((await CurrencyTranslationService.AddRateAsync(db, manager, rateSetId,
        new ExchangeRateInput("USD", "QAR", rateDate, "CLOSING", 3.65m, ExchangeRateDirections.Direct))).Succeeded);
      Assert.True((await CurrencyTranslationService.ApproveRateSetAsync(db, groupReviewer, rateSetId)).Succeeded);
      policyId = (await CurrencyTranslationService.CreatePolicyAsync(db, manager,
        new TranslationPolicyRequest("FX-BRIDGE-POLICY", "USD", "QAR", "CLOSING", "AVERAGE", "HISTORICAL"))).Value;
      Assert.True((await CurrencyTranslationService.ApprovePolicyAsync(db, groupReviewer, policyId)).Succeeded);

      var packageId = AddValidatedPackage(db, fixture, fixture.ClientA, fixture.EngagementA, "USD", "bridge");
      componentId = Guid.CreateVersion7();
      db.ConsolidationComponents.Add(new ConsolidationComponent
      {
        Id = componentId, FirmId = fixture.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
        ClientId = fixture.ClientA, EngagementId = fixture.EngagementA, PackageId = packageId,
        PackageHash = new string('a', 64), PeriodBasis = "STATUTORY", TaxonomyVersion = "tax-v1",
        MappingVersion = Guid.NewGuid().ToString("D"), Currency = "USD", OwnershipPercent = 100m,
        ControlMethod = "CONTROLLED", Status = AccountingWorkflowStates.Approved,
        SubmittedByUserId = fixture.Manager.Id, ApprovedByUserId = fixture.GroupReviewer.Id,
        ApprovedAt = DateTimeOffset.UtcNow, SubmittedAt = DateTimeOffset.UtcNow
      });
      db.TranslationResults.Add(new TranslationResult
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
        ComponentId = componentId, RateSetVersionId = rateSetId, TranslationPolicyVersionId = policyId,
        RateDate = rateDate, RateType = "CLOSING", AppliedRate = 3.65m,
        FromCurrency = "USD", ToCurrency = "QAR", TranslatedAmount = 365m,
        RoundingAdjustment = 0.0004m, TranslationReserve = -12.5m,
        Status = AccountingWorkflowStates.Approved, CreatedByUserId = fixture.Manager.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.TranslationResults.Add(new TranslationResult
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
        ComponentId = componentId, RateSetVersionId = rateSetId, TranslationPolicyVersionId = policyId,
        CalculationVersion = "COMPONENT_TRANSLATION_V2-LEGACY",
        RateDate = rateDate, RateType = "CLOSING", AppliedRate = 3.6m,
        FromCurrency = "USD", ToCurrency = "QAR", TranslatedAmount = 36m,
        Status = AccountingWorkflowStates.Draft, CreatedByUserId = fixture.Manager.Id,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
      });
      db.IntercompanyMatches.Add(new IntercompanyMatch
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
        SellerClientId = fixture.ClientA, BuyerClientId = fixture.ClientB, AccountNature = "RECEIVABLE_PAYABLE",
        SellerTaxonomyCode = "REVENUE", BuyerTaxonomyCode = "EXPENSE",
        PeriodCode = "FY2026", Currency = "QAR", TransactionReference = "IC-001",
        SellerAmount = 100m, BuyerAmount = 90m, MatchedAmount = 90m, Difference = 10m,
        Status = AccountingWorkflowStates.Submitted, EvidenceReference = "ic-evidence-1",
        CreatedByUserId = fixture.Manager.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.IntercompanyMatches.Add(new IntercompanyMatch
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
        SellerClientId = fixture.ClientA, BuyerClientId = fixture.ClientB, AccountNature = "RECEIVABLE_PAYABLE",
        SellerTaxonomyCode = "REVENUE", BuyerTaxonomyCode = "EXPENSE",
        PeriodCode = "FY2026", Currency = "QAR", TransactionReference = "IC-002",
        SellerAmount = 50m, BuyerAmount = 50m, MatchedAmount = 50m, Difference = 0m,
        Status = AccountingWorkflowStates.Approved, ReviewedByUserId = fixture.Manager.Id,
        ReviewedAt = DateTimeOffset.UtcNow, CreatedByUserId = fixture.Manager.Id, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(2)
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var bridge = await ConsolidationQuery.GetTranslationBridgeAsync(db, manager, fixture.ScopeId);
      Assert.True(bridge.Succeeded, bridge.Message);
      Assert.Equal(2, bridge.Value!.TotalCount);
      Assert.Equal(401m, bridge.Value.TotalTranslatedAmount);
      Assert.Equal(-12.5m, bridge.Value.TotalTranslationReserve);
      var latest = bridge.Value.Items.Single(x => x.Status == AccountingWorkflowStates.Draft);
      Assert.Equal(3.6m, latest.AppliedRate);
      Assert.Equal("USD", latest.SourceCurrency);
      Assert.Equal("QAR", latest.PresentationCurrency);
      Assert.Equal(10m, latest.SourceAmount);

      var firstPage = await ConsolidationQuery.GetTranslationBridgeAsync(db, manager, fixture.ScopeId, page: 1, pageSize: 1);
      Assert.Equal(2, firstPage.Value!.TotalCount);
      Assert.Single(firstPage.Value.Items);

      var unresolved = await ConsolidationQuery.GetIntercompanyExceptionsAsync(db, manager, fixture.ScopeId);
      Assert.True(unresolved.Succeeded, unresolved.Message);
      var row = Assert.Single(unresolved.Value!.Items);
      Assert.Equal("IC-001", row.TransactionReference);
      Assert.Equal(10m, row.Difference);
      Assert.Equal(10m, unresolved.Value.TotalUnmatchedDifference);

      var all = await ConsolidationQuery.GetIntercompanyExceptionsAsync(db, manager, fixture.ScopeId, unresolvedOnly: false);
      Assert.Equal(2, all.Value!.TotalCount);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ComponentReadiness_ReportsMissingUnapprovedAndIncompatibleMembers()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var manager = Actor(fixture.Manager, "Partner");

    Guid submittedComponentId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var packageId = AddValidatedPackage(db, fixture, fixture.ClientA, fixture.EngagementA, "QAR", "readiness");
      submittedComponentId = Guid.CreateVersion7();
      db.ConsolidationComponents.Add(new ConsolidationComponent
      {
        Id = submittedComponentId, FirmId = fixture.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
        ClientId = fixture.ClientA, EngagementId = fixture.EngagementA, PackageId = packageId,
        PackageHash = new string('b', 64), PeriodBasis = "STATUTORY", TaxonomyVersion = "tax-v1",
        MappingVersion = Guid.NewGuid().ToString("D"), Currency = "QAR", OwnershipPercent = 100m,
        ControlMethod = "CONTROLLED", Status = AccountingWorkflowStates.Submitted,
        SubmittedByUserId = fixture.Manager.Id, SubmittedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var readiness = await ConsolidationQuery.GetComponentReadinessAsync(db, manager, fixture.ScopeId);
      Assert.True(readiness.Succeeded, readiness.Message);
      Assert.Equal(2, readiness.Value!.Members.Count);
      Assert.Equal("QAR", readiness.Value.ReportingCurrency);

      var submitted = readiness.Value.Members.Single(x => x.ClientId == fixture.ClientA);
      Assert.Equal(AccountingWorkflowStates.Submitted, submitted.ComponentState);
      Assert.False(submitted.CurrencyCompatible);
      Assert.Contains("not independently approved", submitted.MismatchReason, StringComparison.Ordinal);

      var missing = readiness.Value.Members.Single(x => x.ClientId == fixture.ClientB);
      Assert.Equal("NONE", missing.ComponentState);
      Assert.False(missing.CurrencyCompatible);
      Assert.Contains("No component has been pinned", missing.MismatchReason, StringComparison.Ordinal);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var component = await db.ConsolidationComponents.SingleAsync(x => x.Id == submittedComponentId);
      component.Status = AccountingWorkflowStates.Approved;
      component.Currency = "USD";
      await db.SaveChangesAsync();
      var readiness = await ConsolidationQuery.GetComponentReadinessAsync(db, manager, fixture.ScopeId);
      Assert.True(readiness.Succeeded);
      var incompatible = readiness.Value!.Members.Single(x => x.ClientId == fixture.ClientA);
      Assert.Equal(AccountingWorkflowStates.Approved, incompatible.ComponentState);
      Assert.False(incompatible.CurrencyCompatible);
      Assert.Contains("same-currency", incompatible.MismatchReason, StringComparison.Ordinal);
    }
  }

  private static Guid AddValidatedPackage(
    AuditSphereDbContext db, Fixture fixture, Guid clientId, Guid engagementId, string currency, string suffix)
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"bridge-package-{clientId:D}-{suffix}");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = fixture.FirmId, ClientId = clientId, EngagementId = engagementId, SourceKind = "Raw",
      Revision = 1, LegalEntityKey = clientId.ToString("D"), Currency = currency, RawFileSha256Hex = digest,
      NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true, ValidationStatus = "Accepted",
      ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m, ImportedAt = now, ImportedByUserId = fixture.Manager.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = fixture.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      Status = "Finalized", ResultHash = digest, CreatedByUserId = fixture.Manager.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = fixture.FirmId, ClientId = clientId, EngagementId = engagementId, DatasetId = datasetId,
      Version = 1, Generation = 1, TaxonomyVersion = "tax-v1", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      Status = AccountingPackageStates.MappingApproved, CreatedByUserId = fixture.Manager.Id,
      ApprovedByUserId = fixture.GroupReviewer.Id, ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = fixture.FirmId, ClientId = clientId, EngagementId = engagementId, BaseDatasetId = datasetId,
      AdjustmentPlanId = planId, Currency = currency, ResultHash = digest, CreatedByUserId = fixture.Manager.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = fixture.FirmId, ClientId = clientId, EngagementId = engagementId, AdjustedDatasetId = adjustedId,
      MappingVersionId = mappingId, AdjustmentPlanId = planId, Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
      TaxonomyVersion = "tax-v1", TemplateVersion = $"bridge-{suffix}", CalculationEngineVersion = "test-engine",
      CalculationHash = digest, Currency = currency, Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    return packageId;
  }

  private static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  private static AppUser User(Guid firmId, string name) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, Subject = "sub-" + name + Guid.NewGuid().ToString("N"),
    TenantId = "tenant-test", Email = name + "@example.test", DisplayName = name, CreatedAt = DateTimeOffset.UtcNow
  };

  private static RoleGrant Grant(Guid firmId, AppUser user, string role) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = null, EngagementId = null, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  private static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientA = Guid.NewGuid();
    var clientB = Guid.NewGuid();
    var engagementA = Guid.NewGuid();
    var engagementB = Guid.NewGuid();
    var manager = User(firmId, "manager");
    var groupReviewer = User(firmId, "group-reviewer");
    var outsider = User(firmId, "outsider");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.AddRange(
      new PracticeClient { Id = clientA, FirmId = firmId, LegalName = "GROUP MEMBER A", CreatedAt = DateTimeOffset.UtcNow },
      new PracticeClient { Id = clientB, FirmId = firmId, LegalName = "GROUP MEMBER B", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.AddRange(
      new Engagement { Id = engagementA, FirmId = firmId, PracticeClientId = clientA, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow },
      new Engagement { Id = engagementB, FirmId = firmId, PracticeClientId = clientB, ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.AddRange(new ClientSafetyState { Id = clientA, FirmId = firmId }, new ClientSafetyState { Id = clientB, FirmId = firmId });
    db.Users.AddRange(manager, groupReviewer, outsider);
    db.RoleGrants.AddRange(Grant(firmId, manager, "Partner"), Grant(firmId, groupReviewer, "AccountingReviewer"), Grant(firmId, outsider, "Partner"));
    await db.SaveChangesAsync();

    var managerActor = Actor(manager, "Partner");
    var groupId = (await ConsolidationService.CreateGroupAsync(db, managerActor,
      new ClientGroupRequest("GROUP-QUERY", "Consolidation query regression"))).Value;
    Assert.True((await ConsolidationService.AddMembershipAsync(db, managerActor,
      new GroupMembershipRequest(groupId, clientA, new DateOnly(2026, 1, 1), null,
        "CONTROLLED", 100m, 100m, "membership-evidence-a"))).Succeeded);
    Assert.True((await ConsolidationService.AddMembershipAsync(db, managerActor,
      new GroupMembershipRequest(groupId, clientB, new DateOnly(2026, 1, 1), null,
        "CONTROLLED", 100m, 100m, "membership-evidence-b"))).Succeeded);
    var scopeId = (await ConsolidationService.CreateScopeAsync(db, managerActor,
      new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR",
        ConsolidationCalculator.RestrictedMethod, "OPENING-2026"))).Value;
    return new Fixture(firmId, clientA, clientB, engagementA, engagementB, manager, groupReviewer, outsider, groupId, scopeId);
  }
}
