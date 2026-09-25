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
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationMethods_RequireExplicitInputsAndConserveRollforwards()
  {
    var acquisition = AdvancedConsolidationCalculator.CalculateAcquisition(new AcquisitionAccountingInput(
      new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 120m, 20m, 100m, 8m, 10m));
    Assert.Equal(30m, acquisition.Goodwill);
    Assert.Equal(0m, acquisition.BargainPurchase);
    Assert.Equal(110m, acquisition.FairValueAdjustedNetAssets);
    Assert.Equal(AdvancedConsolidationCalculator.AcquisitionNciMethod, acquisition.Method);

    var nci = AdvancedConsolidationCalculator.RollForwardNci(20m, 5m, 2m, 3m);
    Assert.Equal(24m, nci.ClosingNci);

    var ownership = AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
      new DateOnly(2026, 6, 30), 80m, 60m, 10m, 0m, 100m, 20m, false));
    Assert.True(ownership.ControlRetained);
    Assert.Equal(20m, ownership.NciMovement);
    Assert.Equal(0m, ownership.DisposalGainOrLoss);

    var increasedOwnership = AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
      new DateOnly(2026, 9, 30), 60m, 80m, 12m, 0m, 100m, 40m, false));
    Assert.True(increasedOwnership.ControlRetained);
    Assert.Equal(-20m, increasedOwnership.NciMovement);
    Assert.Throws<InvalidOperationException>(() => AdvancedConsolidationCalculator.CalculateOwnershipChange(new OwnershipChangeInput(
      new DateOnly(2026, 9, 30), 60m, 60m, 12m, 0m, 100m, 40m, true)));

    Assert.Throws<InvalidOperationException>(() => AdvancedConsolidationCalculator.EnsureNoNestedDoubleCount([
      new("ENTITY-A", Guid.NewGuid(), false), new("entity-a", Guid.NewGuid(), true)
    ]));
    var elimination = AdvancedConsolidationCalculator.CalculateAssetTransferElimination(30m, 6m, 0.25m);
    Assert.Equal(30m, elimination.UnrealizedProfitElimination);
    Assert.Equal(6m, elimination.DepreciationAdjustment);
    Assert.Equal(6m, elimination.RelatedTaxEffect);
    Assert.Equal(-18m, elimination.NetElimination);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationCandidateFixture_BalancesCurrentAndComparativeStatements()
  {
    var acquisition = AdvancedConsolidationCalculator.CalculateAcquisition(new AcquisitionAccountingInput(
      new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 15), 120m, 20m, 100m, 8m, 10m));
    var nci = AdvancedConsolidationCalculator.RollForwardNci(20m, 5m, 2m, 3m);
    var translation = ForeignOperationTranslationCalculator.Translate(
      100m, 130m, 20m, 3.6m, 3.7m, 3.65m, 5m, "USD", "QAR");
    var elimination = AdvancedConsolidationCalculator.CalculateAssetTransferElimination(30m, 6m, 0.25m);
    AdvancedConsolidationCalculator.EnsureNoNestedDoubleCount([
      new("PARENT", Guid.Parse("00000000-0000-0000-0000-000000000101"), true),
      new("SUBSIDIARY", Guid.Parse("00000000-0000-0000-0000-000000000102"), false)
    ]);

    var comparative = new[]
    {
      ("Translated net assets", translation.OpeningNetAssetsTranslated),
      ("Goodwill", acquisition.Goodwill),
      ("NCI", -nci.OpeningNci),
      ("Translation reserve", -5m),
      ("Parent equity", -365m)
    };
    var current = new[]
    {
      ("Translated net assets", translation.ClosingNetAssetsTranslated),
      ("Goodwill", acquisition.Goodwill),
      ("Asset-transfer elimination", elimination.NetElimination),
      ("NCI", -nci.ClosingNci),
      ("Translation reserve", -translation.ClosingTranslationReserve),
      ("Parent equity", -416m)
    };

    Assert.Equal(new[]
    {
      ("Translated net assets", 360m), ("Goodwill", 30m), ("NCI", -20m),
      ("Translation reserve", -5m), ("Parent equity", -365m)
    }, comparative);
    Assert.Equal(new[]
    {
      ("Translated net assets", 481m), ("Goodwill", 30m), ("Asset-transfer elimination", -18m),
      ("NCI", -24m), ("Translation reserve", -53m), ("Parent equity", -416m)
    }, current);
    Assert.Equal(0m, comparative.Sum(x => x.Item2));
    Assert.Equal(0m, current.Sum(x => x.Item2));
    Assert.Equal(121m, current[0].Item2 - comparative[0].Item2);
    Assert.Equal(-4m, current[3].Item2 - comparative[2].Item2);
    Assert.Equal(-48m, current[4].Item2 - comparative[3].Item2);
    Assert.Equal(-51m, current[5].Item2 - comparative[4].Item2);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedMethodScheduleValidation_RequiresMethodSpecificInputs()
  {
    var valid = new[]
    {
      (AdvancedConsolidationMethods.ForeignCurrencyReserve,
        "{\"openingNetAssets\":100,\"closingNetAssets\":130,\"currentProfit\":20,\"openingRate\":3.6,\"closingRate\":3.7,\"averageRate\":3.65,\"openingTranslationReserve\":5,\"functionalCurrency\":\"USD\",\"presentationCurrency\":\"QAR\"}"),
      (AdvancedConsolidationMethods.AcquisitionNci,
        "{\"acquisitionDate\":\"2026-01-01\",\"controlDate\":\"2026-01-15\",\"consideration\":120,\"nciAtAcquisition\":20,\"fairValueNetAssets\":100,\"openingReserves\":8,\"fairValueAdjustments\":10,\"nciOpening\":20,\"nciProfit\":5,\"nciOci\":2,\"nciDistributions\":3}"),
      (AdvancedConsolidationMethods.OwnershipChange,
        "{\"effectiveDate\":\"2026-06-30\",\"previousOwnershipPercent\":80,\"newOwnershipPercent\":60,\"consideration\":10,\"fairValueRetainedInterest\":0,\"carryingNetAssets\":100,\"carryingNci\":20,\"controlLost\":false}"),
      (AdvancedConsolidationMethods.NestedGroup,
        "{\"components\":[{\"economicEntityKey\":\"PARENT\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000101\",\"includedDirectly\":true},{\"economicEntityKey\":\"SUBSIDIARY\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000102\",\"includedDirectly\":false}]}"),
      (AdvancedConsolidationMethods.AssetTransferElimination,
        "{\"unrealizedProfit\":30,\"postTransferDepreciation\":6,\"taxRate\":0.25}")
    };

    foreach (var (method, json) in valid)
      Assert.True(AdvancedConsolidationCalculator.TryValidateScheduleInput(method, json, out var error), $"{method}: {error}");

    Assert.False(AdvancedConsolidationCalculator.TryValidateScheduleInput(
      AdvancedConsolidationMethods.AcquisitionNci, "{\"consideration\":120}", out _));
    Assert.False(AdvancedConsolidationCalculator.TryValidateScheduleInput(
      AdvancedConsolidationMethods.NestedGroup,
      "{\"components\":[{\"economicEntityKey\":\"A\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000101\",\"includedDirectly\":true},{\"economicEntityKey\":\"a\",\"sourceScopeVersionId\":\"00000000-0000-0000-0000-000000000102\",\"includedDirectly\":false}]}",
      out _));
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationExecution_RequiresBalancedCurrentAndComparativeEvidence()
  {
    const string valid = """
      {
        "acquisitionDate":"2026-01-01",
        "controlDate":"2026-01-15",
        "consideration":120,
        "nciAtAcquisition":20,
        "fairValueNetAssets":100,
        "openingReserves":8,
        "fairValueAdjustments":10,
        "nciOpening":20,
        "nciProfit":5,
        "nciOci":2,
        "nciDistributions":3,
        "statementLines":[
          {"code":"NET_ASSETS","comparativeAmount":20,"currentAmount":14},
          {"code":"NCI","comparativeAmount":-20,"currentAmount":-24},
          {"code":"GOODWILL","comparativeAmount":0,"currentAmount":30},
          {"code":"PARENT_EQUITY","comparativeAmount":0,"currentAmount":-20}
        ]
      }
      """;

    Assert.True(AdvancedConsolidationExecutionCalculator.TryCalculate(
      AdvancedConsolidationMethods.AcquisitionNci, valid, out var calculation, out var error), error);
    Assert.NotNull(calculation);
    Assert.Equal(0m, calculation!.ComparativeSignedTotal);
    Assert.Equal(0m, calculation.CurrentSignedTotal);
    Assert.Equal(Hashing.Sha256Hex(calculation.OutputManifest), calculation.OutputDigest);
    Assert.Contains("GOODWILL", calculation.CurrentStatementJson, StringComparison.Ordinal);

    var unbalanced = valid.Replace("\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20", "\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-19", StringComparison.Ordinal);
    Assert.False(AdvancedConsolidationExecutionCalculator.TryCalculate(
      AdvancedConsolidationMethods.AcquisitionNci, unbalanced, out _, out var unbalancedError));
    Assert.Contains("balance", unbalancedError, StringComparison.OrdinalIgnoreCase);

    var missingMethodLine = valid
      .Replace("{\"code\":\"GOODWILL\",\"comparativeAmount\":0,\"currentAmount\":30},", string.Empty, StringComparison.Ordinal)
      .Replace("\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20", "\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":10", StringComparison.Ordinal);
    Assert.False(AdvancedConsolidationExecutionCalculator.TryCalculate(
      AdvancedConsolidationMethods.AcquisitionNci, missingMethodLine, out _, out var missingLineError));
    Assert.Contains("GOODWILL", missingLineError, StringComparison.Ordinal);
  }

  [Fact]
  [Trait("Profile", "Unit")]
  public void AdvancedConsolidationExecution_GoldenFixturesCoverEachMethod()
  {
    var fixtures = new[]
    {
      (AdvancedConsolidationMethods.ForeignCurrencyReserve, """
        {
          "openingNetAssets":100,"closingNetAssets":130,"currentProfit":20,
          "openingRate":3.6,"closingRate":3.7,"averageRate":3.65,"openingTranslationReserve":5,
          "functionalCurrency":"USD","presentationCurrency":"QAR",
          "statementLines":[
            {"code":"TRANSLATION_RESERVE","comparativeAmount":-5,"currentAmount":-53},
            {"code":"BALANCING_EQUITY","comparativeAmount":5,"currentAmount":53}
          ]
        }
        """),
      (AdvancedConsolidationMethods.AcquisitionNci, """
        {
          "acquisitionDate":"2026-01-01","controlDate":"2026-01-15","consideration":120,
          "nciAtAcquisition":20,"fairValueNetAssets":100,"openingReserves":8,"fairValueAdjustments":10,
          "nciOpening":20,"nciProfit":5,"nciOci":2,"nciDistributions":3,
          "statementLines":[
            {"code":"NET_ASSETS","comparativeAmount":20,"currentAmount":14},
            {"code":"NCI","comparativeAmount":-20,"currentAmount":-24},
            {"code":"GOODWILL","comparativeAmount":0,"currentAmount":30},
            {"code":"PARENT_EQUITY","comparativeAmount":0,"currentAmount":-20}
          ]
        }
        """),
      (AdvancedConsolidationMethods.OwnershipChange, """
        {
          "effectiveDate":"2026-06-30","previousOwnershipPercent":80,"newOwnershipPercent":60,
          "consideration":10,"fairValueRetainedInterest":0,"carryingNetAssets":100,"carryingNci":20,"controlLost":false,
          "statementLines":[
            {"code":"NCI_MOVEMENT","comparativeAmount":0,"currentAmount":20},
            {"code":"OWNERSHIP_CHANGE_GAIN_LOSS","comparativeAmount":0,"currentAmount":0},
            {"code":"EQUITY","comparativeAmount":0,"currentAmount":-20}
          ]
        }
        """),
      (AdvancedConsolidationMethods.NestedGroup, """
        {
          "components":[
            {"economicEntityKey":"PARENT","sourceScopeVersionId":"00000000-0000-0000-0000-000000000101","includedDirectly":true},
            {"economicEntityKey":"SUBSIDIARY","sourceScopeVersionId":"00000000-0000-0000-0000-000000000102","includedDirectly":false}
          ],
          "statementLines":[{"code":"GROUP_BALANCE","comparativeAmount":0,"currentAmount":0}]
        }
        """),
      (AdvancedConsolidationMethods.AssetTransferElimination, """
        {
          "unrealizedProfit":30,"postTransferDepreciation":6,"taxRate":0.25,
          "statementLines":[
            {"code":"ASSET_TRANSFER_ELIMINATION","comparativeAmount":0,"currentAmount":-18},
            {"code":"EQUITY","comparativeAmount":0,"currentAmount":18}
          ]
        }
        """)
    };

    foreach (var (method, json) in fixtures)
    {
      Assert.True(AdvancedConsolidationExecutionCalculator.TryCalculate(method, json, out var calculation, out var error),
        $"{method}: {error}");
      Assert.NotNull(calculation);
      Assert.Equal(0m, calculation!.ComparativeSignedTotal);
      Assert.Equal(0m, calculation.CurrentSignedTotal);
      Assert.Equal(Hashing.Sha256Hex(calculation.OutputManifest), calculation.OutputDigest);
      Assert.Contains(method, calculation.OutputManifest, StringComparison.Ordinal);
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdvancedMethodSchedules_AreCanonicalScopedAndIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");
    Guid scopeId, profileId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("ADV-SCHEDULE", "Advanced schedule group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
          "CONTROLLED", 100m, 100m, "advanced-schedule-membership"))).Succeeded);
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id
        },
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id
        });
      await db.SaveChangesAsync();
      scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.AcquisitionNci,
          "OPENING-2026"))).Value;
      profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
          "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.AcquisitionNci, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "advanced-schedule-local")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "advanced-schedule-method-owner")).Succeeded);

      var invalid = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci,
          "IFRS", "{\"notSources\":[]}", "{\"consideration\":120}"));
      Assert.False(invalid.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalid.ErrorCode);

      var request = new AdvancedConsolidationMethodScheduleRequest(scopeId,
        AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
        "{ \"sources\": [ { \"kind\": \"PACKAGE\", \"id\": \"pkg-1\" } ] }",
        "{ \"consideration\": 120, \"nci\": 20, \"fairValueNetAssets\": 100 }\n");
      var created = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer, request);
      Assert.True(created.Succeeded, created.Message);
      var duplicate = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer, request);
      Assert.True(duplicate.Succeeded);
      Assert.Equal(created.Value, duplicate.Value);
      var stored = await db.AdvancedConsolidationMethodSchedules.SingleAsync(x => x.Id == created.Value);
      Assert.Equal(AdvancedConsolidationMethodScheduleStates.Submitted, stored.Status);
      Assert.Equal(Hashing.Sha256Hex(stored.SourceManifestJson), stored.SourceManifestDigest);
      Assert.Equal(Hashing.Sha256Hex(stored.InputSnapshotJson), stored.InputSnapshotDigest);

      var scope = await db.ConsolidationScopeVersions.SingleAsync(x => x.Id == scopeId);
      scope.Status = AccountingWorkflowStates.Approved;
      await db.SaveChangesAsync();
      var invalidApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, created.Value);
      Assert.False(invalidApproval.Succeeded);
      Assert.Equal(ErrorCodes.Accounting.MappingInvalid, invalidApproval.ErrorCode);

      var validCreated = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci,
          "IFRS", "{ \"sources\": [ { \"kind\": \"PACKAGE\", \"id\": \"pkg-2\" } ] }",
          "{ \"acquisitionDate\": \"2026-01-01\", \"controlDate\": \"2026-01-15\", \"consideration\": 120, \"nciAtAcquisition\": 20, \"fairValueNetAssets\": 100, \"openingReserves\": 8, \"fairValueAdjustments\": 10, \"nciOpening\": 20, \"nciProfit\": 5, \"nciOci\": 2, \"nciDistributions\": 3 }"));
      Assert.True(validCreated.Succeeded, validCreated.Message);
      var approved = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, validCreated.Value);
      Assert.True(approved.Succeeded, approved.Message);
      Assert.Equal(AdvancedConsolidationMethodScheduleStates.Approved,
        await db.AdvancedConsolidationMethodSchedules.Where(x => x.Id == validCreated.Value).Select(x => x.Status).SingleAsync());
    }
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task NestedAdvancedSchedule_BindsApprovedSourceRun()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
      new ClientGroupRequest("NESTED-SCHEDULE", "Nested schedule group"))).Value;
    Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
      new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
        "CONTROLLED", 100m, 100m, "nested-schedule-membership"))).Succeeded);
    db.GroupAccessGrants.AddRange(
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id },
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id });
    await db.SaveChangesAsync();

    var targetScopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
      new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.NestedGroup,
        "OPENING-2026"))).Value;
    var profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
        "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.NestedGroup, "PARTNER", "GROUP"))).Value;
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
      AccountingCapabilityAcceptanceStages.LocalConstruction, "nested-schedule-local")).Succeeded);
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
      AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "nested-schedule-method-owner")).Succeeded);

    var packId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
      new ExternalComponentPackRequest(targetScopeId, fixture.ClientA, fixture.EngagementA, "2026-01-01", "2026-12-31",
        "IFRS", "QAR", "STATUTORY", "tax-v1", "mapping-v1", "nested-schedule-pack",
        new string('a', 64), new string('b', 64), 0m,
        [new("CASH", 100m, "QAR", "line-1"), new("EQUITY", -100m, "QAR", "line-2")]))).Value;
    Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
      new ExternalComponentReconciliationRequest(packId, "nested-pack-reconciliation"))).Succeeded);
    Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, packId)).Succeeded);
    var componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
      new ExternalComponentRequest(targetScopeId, packId))).Value;
    Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);

    var targetScope = await db.ConsolidationScopeVersions.SingleAsync(x => x.Id == targetScopeId);
    targetScope.Status = AccountingWorkflowStates.Approved;
    var sourceScopeId = Guid.NewGuid();
    var sourceRunId = Guid.NewGuid();
    var sourceRunHash = Hashing.Sha256Hex("nested-source-run");
    db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
    {
      Id = sourceScopeId, FirmId = fixture.FirmId, GroupId = groupId, GroupRevision = targetScope.GroupRevision,
      PeriodId = Guid.NewGuid(), Version = 1, ReportingCurrency = "QAR", Method = ConsolidationCalculator.RestrictedMethod,
      Status = AccountingWorkflowStates.Approved, OpeningBasis = "NESTED-SOURCE", CreatedByUserId = reviewer.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.ConsolidationRuns.Add(new ConsolidationRun
    {
      Id = sourceRunId, FirmId = fixture.FirmId, GroupId = groupId, ScopeVersionId = sourceScopeId,
      EngineVersion = "fixture", InputManifest = "fixture", RunHash = sourceRunHash, ReportingCurrency = "QAR",
      SignedTotal = 0m, Status = AccountingWorkflowStates.Approved, CreatedByUserId = reviewer.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();

    var input = $"{{\"components\":[{{\"economicEntityKey\":\"SUBGROUP\",\"sourceScopeVersionId\":\"{sourceScopeId:D}\",\"includedDirectly\":false}}]}}";
    var validInput = $"{{\"fixture\":\"approved-source-run\",\"components\":[{{\"economicEntityKey\":\"SUBGROUP\",\"sourceScopeVersionId\":\"{sourceScopeId:D}\",\"includedDirectly\":false}}],\"statementLines\":[{{\"code\":\"GROUP_BALANCE\",\"comparativeAmount\":0,\"currentAmount\":0}}]}}";
    var packHash = await db.ExternalComponentPacks.Where(x => x.Id == packId).Select(x => x.PackDigest).SingleAsync();
    var sources = $"\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}]";
    var missingEvidence = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(targetScopeId, AdvancedConsolidationMethods.NestedGroup,
        "IFRS", $"{{{sources}}}", input));
    Assert.True(missingEvidence.Succeeded, missingEvidence.Message);
    var missingApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, missingEvidence.Value);
    Assert.False(missingApproval.Succeeded);
    Assert.Equal(ErrorCodes.ManifestMismatch, missingApproval.ErrorCode);

    var validEvidence = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(targetScopeId, AdvancedConsolidationMethods.NestedGroup,
        "IFRS", $"{{{sources},\"nestedScopes\":[{{\"scopeVersionId\":\"{sourceScopeId:D}\",\"runId\":\"{sourceRunId:D}\",\"runHash\":\"{sourceRunHash}\"}}]}}", validInput));
    Assert.True(validEvidence.Succeeded, validEvidence.Message);
    Assert.True((await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, validEvidence.Value)).Succeeded);
    var execution = await ConsolidationService.RunAdvancedProfileAsync(db, preparer, targetScope);
    Assert.True(execution.Succeeded, execution.Message);
    Assert.True((await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, execution.Value)).Succeeded);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdvancedForeignSchedule_BindsApprovedRateEvidenceAndReserve()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");
    var rateDate = new DateOnly(2026, 12, 31);

    await using var db = new AuditSphereDbContext(pg.Options);
    var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
      new ClientGroupRequest("ADV-FX", "Advanced FX group"))).Value;
    Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
      new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
        "CONTROLLED", 100m, 100m, "advanced-fx-membership"))).Succeeded);
    db.GroupAccessGrants.AddRange(
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id },
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id });
    db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Preparer, "Partner"));
    await db.SaveChangesAsync();

    var rateSetId = (await CurrencyTranslationService.CreateRateSetAsync(db, reviewer,
      new ExchangeRateSetRequest("ADV-FX-2026", "advanced-fx-fixture",
        new DateOnly(2026, 1, 1), rateDate, 1))).Value;
    Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
      new ExchangeRateInput("USD", "QAR", new DateOnly(2026, 1, 1), "HISTORICAL", 3.60m, "DIRECT"))).Succeeded);
    Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
      new ExchangeRateInput("USD", "QAR", rateDate, "CLOSING", 3.70m, "DIRECT"))).Succeeded);
    Assert.True((await CurrencyTranslationService.AddRateAsync(db, reviewer, rateSetId,
      new ExchangeRateInput("USD", "QAR", rateDate, "AVERAGE", 3.65m, "DIRECT"))).Succeeded);
    var rateSetApproval = await CurrencyTranslationService.ApproveRateSetAsync(db, methodOwner, rateSetId);
    Assert.True(rateSetApproval.Succeeded, rateSetApproval.Message);
    var rateIds = await db.ExchangeRates.Where(x => x.RateSetVersionId == rateSetId)
      .ToDictionaryAsync(x => $"{x.RateType}:{x.RateDate:yyyy-MM-dd}", x => x.Id);
    var policyId = (await CurrencyTranslationService.CreatePolicyAsync(db, reviewer,
      new TranslationPolicyRequest("ADV-FX-POLICY", "USD", "QAR", "CLOSING", "AVERAGE", "HISTORICAL"))).Value;
    Assert.True((await CurrencyTranslationService.ApprovePolicyAsync(db, methodOwner, policyId)).Succeeded);

    var scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
      new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.ForeignCurrencyReserve,
        "OPENING-2026", rateSetId, policyId, rateDate, "CLOSING"))).Value;
    var profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
        "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.ForeignCurrencyReserve, "PARTNER", "GROUP"))).Value;
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
      AccountingCapabilityAcceptanceStages.LocalConstruction, "advanced-fx-local")).Succeeded);
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
      AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "advanced-fx-method-owner")).Succeeded);

    var packId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
      new ExternalComponentPackRequest(scopeId, fixture.ClientA, fixture.EngagementA, "2026-01-01", "2026-12-31",
        "IFRS", "USD", "STATUTORY", "tax-v1", "mapping-v1", "advanced-fx-pack",
        new string('c', 64), new string('d', 64), 0m,
        [new("CASH", 100m, "USD", "line-1"), new("EQUITY", -100m, "USD", "line-2")]))).Value;
    Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
      new ExternalComponentReconciliationRequest(packId, "advanced-fx-reconciliation"))).Succeeded);
    Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, packId)).Succeeded);
    var componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
      new ExternalComponentRequest(scopeId, packId))).Value;
    Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);
    Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, scopeId)).Succeeded);

    var packHash = await db.ExternalComponentPacks.Where(x => x.Id == packId).Select(x => x.PackDigest).SingleAsync();
    var sources = $"\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}]";
    var input = "{\"fixture\":\"advanced-fx-valid\",\"openingNetAssets\":100,\"closingNetAssets\":130,\"currentProfit\":20,\"openingRate\":3.6,\"closingRate\":3.7,\"averageRate\":3.65,\"openingTranslationReserve\":0,\"functionalCurrency\":\"USD\",\"presentationCurrency\":\"QAR\",\"statementLines\":[{\"code\":\"TRANSLATION_RESERVE\",\"comparativeAmount\":0,\"currentAmount\":-48},{\"code\":\"BALANCING_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":48}]}";
    var missingRates = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.ForeignCurrencyReserve,
        "IFRS", $"{{{sources}}}", input.Replace("advanced-fx-valid", "advanced-fx-missing-rates", StringComparison.Ordinal)));
    Assert.True(missingRates.Succeeded, missingRates.Message);
    var missingRatesApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, missingRates.Value);
    Assert.False(missingRatesApproval.Succeeded);
    Assert.Equal(ErrorCodes.ManifestMismatch, missingRatesApproval.ErrorCode);

    var openingRateId = rateIds["HISTORICAL:2026-01-01"];
    var closingRateId = rateIds["CLOSING:2026-12-31"];
    var averageRateId = rateIds["AVERAGE:2026-12-31"];
    var fxRates = $"\"fxRates\":[{{\"role\":\"OPENING\",\"id\":\"{openingRateId:D}\",\"date\":\"2026-01-01\",\"rate\":3.6}},{{\"role\":\"CLOSING\",\"id\":\"{closingRateId:D}\",\"date\":\"2026-12-31\",\"rate\":3.7}},{{\"role\":\"AVERAGE\",\"id\":\"{averageRateId:D}\",\"date\":\"2026-12-31\",\"rate\":3.65}}]";
    var validSchedule = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.ForeignCurrencyReserve,
        "IFRS", $"{{{sources},{fxRates}}}", input));
    Assert.True(validSchedule.Succeeded, validSchedule.Message);
    Assert.True((await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, validSchedule.Value)).Succeeded);
    var execution = await ConsolidationService.RunAdvancedProfileAsync(db, preparer,
      await db.ConsolidationScopeVersions.SingleAsync(x => x.Id == scopeId));
    Assert.True(execution.Succeeded, execution.Message);
    Assert.True((await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, execution.Value)).Succeeded);
  }

  [Fact]
  [Trait("Profile", "Database")]
  public async Task AdvancedConsolidationExecution_IsScopedVerifiedIdempotentlyAndSeparatelyApproved()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");
    Guid scopeId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("ADV-EXECUTION", "Advanced execution group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
          "CONTROLLED", 100m, 100m, "advanced-execution-membership"))).Succeeded);
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id },
        new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id });
      await db.SaveChangesAsync();

      scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", AdvancedConsolidationMethods.AcquisitionNci,
          "OPENING-2026"))).Value;
      var packId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, fixture.ClientA, fixture.EngagementA, "2026-01-01", "2026-12-31",
          "IFRS", "QAR", "STATUTORY", "tax-v1", "mapping-v1", "advanced-execution-fixture",
          new string('a', 64), new string('b', 64), 0m,
          [new("CASH", 100m, "QAR", "line-1"), new("EQUITY", -100m, "QAR", "line-2")]))).Value;
      Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
        new ExternalComponentReconciliationRequest(packId, "external-pack-reconciliation"))).Succeeded);
      Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, packId)).Succeeded);
      var componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
        new ExternalComponentRequest(scopeId, packId))).Value;
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);
      var packHash = await db.ExternalComponentPacks.Where(x => x.Id == packId).Select(x => x.PackDigest).SingleAsync();

      var profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
          "ANNUAL", "QAR", "STATUTORY", AdvancedConsolidationMethods.AcquisitionNci, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "advanced-execution-profile")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "advanced-execution-method-owner")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, scopeId)).Succeeded);
      var reviewedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(scopeId, "ADV-ACQ-001", "ACQUISITION_NCI", "QAR", "advanced-reviewed-journal",
          [new(null, "GOODWILL", 30m, 0m, "Reviewed acquisition goodwill"),
           new(null, "PARENT_EQUITY", 0m, 30m, "Reviewed acquisition equity")]))).Value;
      Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, reviewedJournalId)).Succeeded);

      var invalidSourceSchedule = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
          $"{{\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{Guid.NewGuid():D}\",\"hash\":\"{packHash}\"}}]}}",
          "{}"));
      Assert.True(invalidSourceSchedule.Succeeded, invalidSourceSchedule.Message);
      var invalidSourceApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, invalidSourceSchedule.Value);
      Assert.False(invalidSourceApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, invalidSourceApproval.ErrorCode);

      var sourceWithoutJournal = $"{{\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}]}}";
      var missingJournalSchedule = await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
          sourceWithoutJournal, "{\"fixture\":\"missing-reviewed-journal\"}"));
      Assert.True(missingJournalSchedule.Succeeded, missingJournalSchedule.Message);
      var missingJournalApproval = await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, missingJournalSchedule.Value);
      Assert.False(missingJournalApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, missingJournalApproval.ErrorCode);

      var scheduleId = (await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
        new AdvancedConsolidationMethodScheduleRequest(scopeId, AdvancedConsolidationMethods.AcquisitionNci, "IFRS",
          $"{{\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}],\"reviewedJournals\":[{{\"id\":\"{reviewedJournalId:D}\"}}]}}",
          "{\"acquisitionDate\":\"2026-01-01\",\"controlDate\":\"2026-01-15\",\"consideration\":120,\"nciAtAcquisition\":20,\"fairValueNetAssets\":100,\"openingReserves\":8,\"fairValueAdjustments\":10,\"nciOpening\":20,\"nciProfit\":5,\"nciOci\":2,\"nciDistributions\":3,\"statementLines\":[{\"code\":\"NET_ASSETS\",\"comparativeAmount\":20,\"currentAmount\":14},{\"code\":\"NCI\",\"comparativeAmount\":-20,\"currentAmount\":-24},{\"code\":\"GOODWILL\",\"comparativeAmount\":0,\"currentAmount\":30},{\"code\":\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20}]}"))).Value;
      Assert.True((await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, scheduleId)).Succeeded);

      var execution = await ConsolidationService.RunAsync(db, preparer, scopeId);
      Assert.True(execution.Succeeded, execution.Message);
      var repeated = await ConsolidationService.RunAsync(db, preparer, scopeId);
      Assert.True(repeated.Succeeded);
      Assert.Equal(execution.Value, repeated.Value);
      var stored = await db.AdvancedConsolidationExecutions.SingleAsync(x => x.Id == execution.Value);
      Assert.Equal(AdvancedConsolidationExecutionStates.Verified, stored.Status);
      Assert.Equal(0m, stored.ComparativeSignedTotal);
      Assert.Equal(0m, stored.CurrentSignedTotal);
      Assert.Equal(Hashing.Sha256Hex(stored.OutputManifest), stored.OutputDigest);
      var component = await db.ConsolidationComponents.SingleAsync(x => x.Id == componentId);
      var packageHash = component.PackageHash;
      component.PackageHash = Hashing.Sha256Hex("changed-after-execution");
      await db.SaveChangesAsync();
      var staleApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(staleApproval.Succeeded);
      Assert.Equal(ErrorCodes.GenerationStale, staleApproval.ErrorCode);
      component.PackageHash = packageHash;
      await db.SaveChangesAsync();
      var reviewedJournal = await db.ConsolidationJournals.SingleAsync(x => x.Id == reviewedJournalId);
      reviewedJournal.Status = AccountingWorkflowStates.Draft;
      await db.SaveChangesAsync();
      var staleJournalApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(staleJournalApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, staleJournalApproval.ErrorCode);
      reviewedJournal.Status = AccountingWorkflowStates.Approved;
      await db.SaveChangesAsync();
      reviewedJournal.JournalType = "OTHER";
      await db.SaveChangesAsync();
      var mismatchedJournalApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(mismatchedJournalApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, mismatchedJournalApproval.ErrorCode);
      reviewedJournal.JournalType = "ACQUISITION_NCI";
      await db.SaveChangesAsync();
      var journalLines = await db.ConsolidationJournalLines.Where(x => x.ConsolidationJournalId == reviewedJournalId).ToListAsync();
      var goodwillLine = journalLines.Single(x => x.TaxonomyCode == "GOODWILL");
      var balancingLine = journalLines.Single(x => x.TaxonomyCode == "PARENT_EQUITY");
      goodwillLine.Debit = 29m;
      balancingLine.Credit = 29m;
      reviewedJournal.TotalDebits = 29m;
      reviewedJournal.TotalCreditsAbs = 29m;
      await db.SaveChangesAsync();
      var changedJournalAmountApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(changedJournalAmountApproval.Succeeded);
      Assert.Equal(ErrorCodes.ManifestMismatch, changedJournalAmountApproval.ErrorCode);
      goodwillLine.Debit = 30m;
      balancingLine.Credit = 30m;
      reviewedJournal.TotalDebits = 30m;
      reviewedJournal.TotalCreditsAbs = 30m;
      await db.SaveChangesAsync();
      var methodAcceptance = await db.AccountingCapabilityAcceptances.SingleAsync(x => x.CapabilityProfileId == profileId &&
        x.Stage == AccountingCapabilityAcceptanceStages.MethodOwnerApproval);
      methodAcceptance.Status = AccountingWorkflowStates.Retired;
      await db.SaveChangesAsync();
      var unacceptedApproval = await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id);
      Assert.False(unacceptedApproval.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unacceptedApproval.ErrorCode);
      methodAcceptance.Status = AccountingWorkflowStates.Approved;
      await db.SaveChangesAsync();
      Assert.True((await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, stored.Id)).Succeeded);
      Assert.Equal(AdvancedConsolidationExecutionStates.Approved,
        await db.AdvancedConsolidationExecutions.Where(x => x.Id == stored.Id).Select(x => x.Status).SingleAsync());
    }
  }

  [Theory]
  [InlineData(AdvancedConsolidationMethods.OwnershipChange)]
  [InlineData(AdvancedConsolidationMethods.AssetTransferElimination)]
  [Trait("Profile", "Database")]
  public async Task AdvancedMethodExecution_BindsMethodJournalAndSource(string method)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "Partner");
    var methodOwner = Actor(fixture.Preparer, "Partner");

    await using var db = new AuditSphereDbContext(pg.Options);
    var groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
      new ClientGroupRequest($"ADV-{method}", "Advanced method group"))).Value;
    Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
      new GroupMembershipRequest(groupId, fixture.ClientA, new DateOnly(2026, 1, 1), null,
        "CONTROLLED", 100m, 100m, $"membership-{method}"))).Succeeded);
    db.GroupAccessGrants.AddRange(
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id },
      new GroupAccessGrant { Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
        Role = "Partner", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Reviewer.Id });
    db.RoleGrants.Add(Grant(fixture.FirmId, fixture.Preparer, "Partner"));
    await db.SaveChangesAsync();

    var scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
      new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", method, "OPENING-2026"))).Value;
    var profileId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
      new CapabilityProfileRequest(null, groupId, AccountingCapabilityServiceKinds.GroupReporting, "IFRS", "2026",
        "ANNUAL", "QAR", "STATUTORY", method, "PARTNER", "GROUP"))).Value;
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, profileId,
      AccountingCapabilityAcceptanceStages.LocalConstruction, $"local-{method}")).Succeeded);
    Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, methodOwner, profileId,
      AccountingCapabilityAcceptanceStages.MethodOwnerApproval, $"method-owner-{method}")).Succeeded);

    var packId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
      new ExternalComponentPackRequest(scopeId, fixture.ClientA, fixture.EngagementA, "2026-01-01", "2026-12-31",
        "IFRS", "QAR", "STATUTORY", "tax-v1", "mapping-v1", $"pack-{method}",
        new string('e', 64), new string('f', 64), 0m,
        [new("CASH", 100m, "QAR", "line-1"), new("EQUITY", -100m, "QAR", "line-2")]))).Value;
    Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
      new ExternalComponentReconciliationRequest(packId, $"reconcile-{method}"))).Succeeded);
    Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, packId)).Succeeded);
    var componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
      new ExternalComponentRequest(scopeId, packId))).Value;
    Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);
    Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, scopeId)).Succeeded);

    var journalType = method == AdvancedConsolidationMethods.OwnershipChange ? "OWNERSHIP_CHANGE" : "ASSET_TRANSFER_ELIMINATION";
    IReadOnlyList<ConsolidationJournalLineInput> journalLines = method == AdvancedConsolidationMethods.OwnershipChange
      ? [new ConsolidationJournalLineInput(null, "NCI_MOVEMENT", 20m, 0m, "Reviewed NCI movement"),
         new ConsolidationJournalLineInput(null, "OWNERSHIP_CHANGE_GAIN_LOSS", 0m, 0m, "Reviewed ownership gain or loss"),
         new ConsolidationJournalLineInput(null, "PARENT_EQUITY", 0m, 20m, "Reviewed ownership equity balancing")]
      : [new ConsolidationJournalLineInput(null, "ASSET_TRANSFER_ELIMINATION", 0m, 18m, "Reviewed asset-transfer elimination"),
         new ConsolidationJournalLineInput(null, "PARENT_EQUITY", 18m, 0m, "Reviewed asset-transfer equity balancing")];
    var reviewedJournalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
      new ConsolidationJournalRequest(scopeId, $"J-{method}", journalType, "QAR", $"journal-{method}", journalLines))).Value;
    Assert.True((await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, reviewedJournalId)).Succeeded);

    var packHash = await db.ExternalComponentPacks.Where(x => x.Id == packId).Select(x => x.PackDigest).SingleAsync();
    var sources = $"\"sources\":[{{\"componentId\":\"{componentId:D}\",\"kind\":\"EXTERNAL_PACK\",\"id\":\"{packId:D}\",\"hash\":\"{packHash}\"}}],\"reviewedJournals\":[{{\"id\":\"{reviewedJournalId:D}\"}}]";
    var input = method == AdvancedConsolidationMethods.OwnershipChange
      ? "{\"effectiveDate\":\"2026-06-30\",\"previousOwnershipPercent\":80,\"newOwnershipPercent\":60,\"consideration\":10,\"fairValueRetainedInterest\":0,\"carryingNetAssets\":100,\"carryingNci\":20,\"controlLost\":false,\"statementLines\":[{\"code\":\"NCI_MOVEMENT\",\"comparativeAmount\":0,\"currentAmount\":20},{\"code\":\"OWNERSHIP_CHANGE_GAIN_LOSS\",\"comparativeAmount\":0,\"currentAmount\":0},{\"code\":\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":-20}]}"
      : "{\"unrealizedProfit\":30,\"postTransferDepreciation\":6,\"taxRate\":0.25,\"statementLines\":[{\"code\":\"ASSET_TRANSFER_ELIMINATION\",\"comparativeAmount\":0,\"currentAmount\":-18},{\"code\":\"PARENT_EQUITY\",\"comparativeAmount\":0,\"currentAmount\":18}]}";
    var scheduleId = (await ConsolidationService.CreateAdvancedMethodScheduleAsync(db, preparer,
      new AdvancedConsolidationMethodScheduleRequest(scopeId, method, "IFRS", $"{{{sources}}}", input))).Value;
    Assert.True((await ConsolidationService.ApproveAdvancedMethodScheduleAsync(db, reviewer, scheduleId)).Succeeded);
    var execution = await ConsolidationService.RunAdvancedProfileAsync(db, preparer,
      await db.ConsolidationScopeVersions.SingleAsync(x => x.Id == scopeId));
    Assert.True(execution.Succeeded, execution.Message);
    Assert.True((await ConsolidationService.ApproveAdvancedExecutionAsync(db, reviewer, execution.Value)).Succeeded);
  }
}
