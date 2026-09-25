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
  public async Task ExternalComponentPack_WorkflowRequiresReconciliationAndPreservesHistory()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var scope = await SeedAsync(pg);
    var preparer = Actor(scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(scope.Reviewer, "Partner");
    Guid groupId, scopeId, returnedPackId, approvedPackId, componentId;
    var lines = new[]
    {
      new ExternalComponentPackLineRequest("CASH", 100m, "QAR", "external-line-1"),
      new ExternalComponentPackLineRequest("REVENUE", -100m, "QAR", "external-line-2")
    };

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      groupId = (await ConsolidationService.CreateGroupAsync(db, reviewer,
        new ClientGroupRequest("GROUP-EXT", "External component group"))).Value;
      Assert.True((await ConsolidationService.AddMembershipAsync(db, reviewer,
        new GroupMembershipRequest(groupId, scope.ClientA, new DateOnly(2026, 1, 1), null, "CONTROLLED", 100m, 100m,
          "external-membership"))).Succeeded);
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
      db.RoleGrants.Add(Grant(scope.FirmId, scope.Preparer, "Partner"));
      await db.SaveChangesAsync();
      scopeId = (await ConsolidationService.CreateScopeAsync(db, reviewer,
        new ConsolidationScopeRequest(groupId, Guid.NewGuid(), "QAR", ConsolidationCalculator.RestrictedMethod,
          "OPENING-2026"))).Value;

      returnedPackId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, scope.ClientA, scope.EngagementA, "2026-01-01", "2026-12-31", "IFRS",
          "QAR", "STATUTORY", "tax-external-v1", "mapping-external-v1", "client-upload-001",
          Hashing.Sha256Hex("raw-external-1"), Hashing.Sha256Hex("normalized-external-1"), 0m, lines))).Value;
      var unreconciled = await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, returnedPackId);
      Assert.False(unreconciled.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, unreconciled.ErrorCode);
      Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
        new ExternalComponentReconciliationRequest(returnedPackId, "external-tb-reconciliation-1"))).Succeeded);
      Assert.True((await ConsolidationService.ReturnExternalComponentPackAsync(db, reviewer, returnedPackId,
        "The client must resubmit the signed pack with the corrected source receipt.")).Succeeded);

      var missingBridge = await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, scope.ClientA, scope.EngagementA, "2026-01-01", "2026-12-31", "IFRS",
          "QAR", "MANAGEMENT", "tax-external-v1", "mapping-external-v2", "client-upload-002",
          Hashing.Sha256Hex("raw-external-2"), Hashing.Sha256Hex("normalized-external-2"), 0m, lines, returnedPackId));
      Assert.False(missingBridge.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, missingBridge.ErrorCode);

      approvedPackId = (await ConsolidationService.SubmitExternalComponentPackAsync(db, preparer,
        new ExternalComponentPackRequest(scopeId, scope.ClientA, scope.EngagementA, "2026-01-01", "2026-12-31", "IFRS",
          "QAR", "MANAGEMENT", "tax-external-v1", "mapping-external-v2", "client-upload-002",
          Hashing.Sha256Hex("raw-external-2"), Hashing.Sha256Hex("normalized-external-2"), 0m, lines, returnedPackId,
          "basis-bridge-reviewed-by-group-accounting"))).Value;
      Assert.True((await ConsolidationService.ReconcileExternalComponentPackAsync(db, reviewer,
        new ExternalComponentReconciliationRequest(approvedPackId, "external-tb-reconciliation-2"))).Succeeded);
      Assert.True((await ConsolidationService.ApproveExternalComponentPackAsync(db, reviewer, approvedPackId)).Succeeded);

      componentId = (await ConsolidationService.SubmitExternalComponentAsync(db, preparer,
        new ExternalComponentRequest(scopeId, approvedPackId))).Value;
      Assert.True((await ConsolidationService.ApproveComponentAsync(db, reviewer, componentId)).Succeeded);

      var capabilityId = (await ClientAccountingService.CreateCapabilityProfileAsync(db, reviewer,
        new CapabilityProfileRequest(null, groupId, "GROUP_REPORTING", "IFRS", "2026", "ANNUAL", "QAR",
          "STATUTORY", ConsolidationCalculator.RestrictedMethod, "PARTNER", "GROUP"))).Value;
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, reviewer, capabilityId,
        AccountingCapabilityAcceptanceStages.LocalConstruction, "external-pack-local-fixture")).Succeeded);
      Assert.True((await ClientAccountingService.RecordCapabilityAcceptanceAsync(db, Actor(scope.Preparer, "Partner"), capabilityId,
        AccountingCapabilityAcceptanceStages.MethodOwnerApproval, "external-pack-method-owner-fixture")).Succeeded);
      Assert.True((await ConsolidationService.ApproveScopeAsync(db, reviewer, scopeId)).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var history = await db.ExternalComponentPacks.AsNoTracking().Where(x => x.ScopeVersionId == scopeId)
        .OrderBy(x => x.Version).ToListAsync();
      Assert.Collection(history,
        first =>
        {
          Assert.Equal(returnedPackId, first.Id);
          Assert.Equal(ExternalComponentPackStates.Returned, first.Status);
          Assert.Null(first.PriorPackId);
        },
        second =>
        {
          Assert.Equal(approvedPackId, second.Id);
          Assert.Equal(ExternalComponentPackStates.Approved, second.Status);
          Assert.Equal(returnedPackId, second.PriorPackId);
          Assert.Equal(ExternalComponentBridgeStates.Approved, second.CompatibilityBridgeStatus);
          Assert.Equal("basis-bridge-reviewed-by-group-accounting", second.CompatibilityBridgeReference);
          Assert.Equal(ExternalComponentReconciliationStates.Reconciled, second.ReconciliationStatus);
        });
      Assert.Null(await db.ConsolidationComponents.Where(x => x.Id == componentId).Select(x => x.PackageId).SingleAsync());
      Assert.Equal(approvedPackId, await db.ConsolidationComponents.Where(x => x.Id == componentId)
        .Select(x => x.ExternalComponentPackId).SingleAsync());

      var run = await ConsolidationService.RunAsync(db, preparer, scopeId);
      Assert.True(run.Succeeded, run.Message);
      Assert.Equal(0m, await db.ConsolidationRuns.Where(x => x.Id == run.Value).Select(x => x.SignedTotal).SingleAsync());
      Assert.Equal(2, await db.ConsolidationRunLines.CountAsync(x => x.RunId == run.Value));
    }
  }
}
