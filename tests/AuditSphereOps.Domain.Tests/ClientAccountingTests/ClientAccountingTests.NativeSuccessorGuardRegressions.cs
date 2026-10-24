using System.Text.Json.Nodes;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class ClientAccountingTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorGuardRefusesHeldOrFrozenWritesAndRetainsOriginalHistory(bool frozen)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    var reviewer = Actor(f.Native.Scope.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var firstPreview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
    Assert.True(firstPreview.Succeeded, firstPreview.Message);
    var pending = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      f.Request with { Reviewed = true, ReviewBasis = firstPreview.Value!.ReviewBasis });
    Assert.True(pending.Succeeded, pending.Message);
    // A separate legitimate DRAFT target verifies new publication denial, not duplicate-opening refusal.
    var otherPeriod = await ClientAccountingService.CreatePeriodAsync(db, maker,
      new(f.Native.Scope.ClientA, "2027-HOLD-BOUNDARY", new(2027, 1, 1), new(2027, 12, 31), "IFRS", "QAR", f.Native.PeriodId));
    Assert.True(otherPeriod.Succeeded, otherPeriod.Message);
    var request = f.Request with { RequestId = Guid.CreateVersion7(), PeriodId = otherPeriod.Value };
    var beforeHold = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, request);
    Assert.True(beforeHold.Succeeded, beforeHold.Message);
    var openingCount = await db.ClientOperationalOpeningBalances.CountAsync();
    var proofCount = await db.ClientOperationalOpeningSuccessorProofs.CountAsync();
    var original = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == pending.Value!.Opening.Id);
    if (frozen)
    {
      var signed = DateTimeOffset.UtcNow.AddDays(-61); var report = Guid.CreateVersion7();
      // Explicit synthetic terminal evidence; no claim that a professional report was actually signed.
      db.AuditDeliverables.Add(new AuditDeliverable
      {
        Id = report, FirmId = f.Native.Scope.FirmId, ClientId = f.Native.Scope.ClientA, EngagementId = f.Native.Scope.EngagementA,
        Kind = DeliverableKinds.IndependentAuditorsReport, Version = 1, TemplateVersion = "synthetic-successor-freeze",
        InputDigest = Hashing.Sha256Hex("synthetic successor freeze"), InputSummaryJson = "{}", FileName = "synthetic.txt",
        ContentType = "text/plain", Content = [1], ContentSha256 = Hashing.Sha256Hex(new byte[] { 1 }),
        CreatedByUserId = maker.UserId, CreatedAt = signed
      });
      db.EngagementFileFreezes.Add(new EngagementFileFreeze
      {
        Id = Guid.CreateVersion7(), FirmId = f.Native.Scope.FirmId, ClientId = f.Native.Scope.ClientA,
        EngagementId = f.Native.Scope.EngagementA, ReportDeliverableId = report, ReportSignedAt = signed,
        DueAt = signed.AddDays(60), State = FileFreezeStates.Frozen, FrozenAt = signed.AddDays(60), UpdatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    else
      await db.Engagements.Where(x => x.Id == f.Native.Scope.EngagementA).ExecuteUpdateAsync(x => x.SetProperty(e => e.ProfessionalWorkBlocked, true));
    var catalog = await ClientOperationalOpeningBalanceWorkspace.SuccessorCatalogAsync(db, maker,
      f.Native.Scope.ClientA, request.PeriodId);
    Assert.True(catalog.Succeeded, catalog.Message); // A work hold does not revoke history read authority.
    Assert.False(catalog.Value!.CanPrepare);
    Assert.All(catalog.Value.Sources, x => Assert.False(x.Eligible));
    var reviewCatalog = await ClientOperationalOpeningBalanceWorkspace.SuccessorCatalogAsync(db, reviewer,
      f.Native.Scope.ClientA, original.PeriodId);
    Assert.True(reviewCatalog.Succeeded, reviewCatalog.Message);
    Assert.False(reviewCatalog.Value!.CanApprove);
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, request);
    var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      request with { Reviewed = true, ReviewBasis = beforeHold.Value!.ReviewBasis });
    var approved = await ClientOperationalOpeningBalanceWorkspace.ApproveSuccessorAsync(db, reviewer,
      new(Guid.CreateVersion7(), f.Native.Scope.ClientA, f.Native.Scope.EngagementA, original.Id,
        1, original.ManifestSha256, pending.Value!.ProofHash, true));
    var history = await ClientOperationalOpeningBalanceWorkspace.GetSuccessorAsync(db, maker, f.Native.Scope.ClientA, original.Id);
    Assert.True(history.Succeeded, history.Message);
    Assert.Equal(original.ManifestJson, (await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == original.Id)).ManifestJson);
    var expected = frozen ? ErrorCodes.ProtectedState : ErrorCodes.GateBlocked;
    Assert.Equal((expected, expected, expected), (preview.ErrorCode, created.ErrorCode, approved.ErrorCode));
    Assert.Equal(openingCount, await db.ClientOperationalOpeningBalances.CountAsync());
    Assert.Equal(proofCount, await db.ClientOperationalOpeningSuccessorProofs.CountAsync());
    Assert.Null((await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == original.Id)).ApprovedByUserId);
    Assert.Equal(pending.Value.ProofHash, history.Value!.ProofHash);
  }

  [Fact]
  [Trait("ClientOperationalLedger", "Database")]
  public async Task NativeSuccessorGuardRejectsSelfHashedWrongOpeningAmountsWithValidSourceFacts()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var f = await CreateNativeSuccessorFixtureAsync(pg);
    var maker = Actor(f.Native.Scope.Preparer, "AccountingPreparer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var preview = await ClientOperationalOpeningBalanceWorkspace.PreviewSuccessorAsync(db, maker, f.Request);
    Assert.True(preview.Succeeded, preview.Message);
    var created = await ClientOperationalOpeningBalanceWorkspace.CreateSuccessorAsync(db, maker,
      f.Request with { Reviewed = true, ReviewBasis = preview.Value!.ReviewBasis });
    Assert.True(created.Succeeded, created.Message);
    var original = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == created.Value!.Opening.Id);
    var retained = await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == original.Id);
    // Use genuine v1 bytes from the existing manual owner, not a second serializer or SQL bypass.
    var manualPeriod = await ClientAccountingService.CreatePeriodAsync(db, maker,
      new(f.Native.Scope.ClientA, "2027-WRONG-AMOUNTS", new(2027, 1, 1), new(2027, 12, 31), "IFRS", "QAR", f.Native.PeriodId));
    Assert.True(manualPeriod.Succeeded, manualPeriod.Message);
    var wrong = await ClientOperationalOpeningBalanceWorkspace.CreateAsync(db, maker,
      new(f.Native.Scope.ClientA, manualPeriod.Value, 1, new(2027, 1, 1), "QAR", original.EvidenceReference,
        f.SourceHash, [new("1000", 119m, 0m), new("2000", 0m, 19m), new("3000", 0m, 100m)]));
    Assert.True(wrong.Succeeded, wrong.Message);
    var forged = await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == wrong.Value);
    Assert.True(ClientOperationalOpeningBalanceWorkspace.ValidManifest(forged));
    var proof = JsonNode.Parse(retained.ProofJson)!.AsObject();
    proof["OpeningId"] = forged.Id;
    proof["OpeningManifestHash"] = forged.ManifestSha256;
    proof["Facts"]!["TargetPeriodId"] = manualPeriod.Value;
    var forgedProof = new ClientOperationalOpeningSuccessorProof
    {
      OpeningId = forged.Id, FirmId = retained.FirmId, ClientId = retained.ClientId, EngagementId = retained.EngagementId,
      PriorPeriodId = retained.PriorPeriodId, PriorPeriodRevision = retained.PriorPeriodRevision,
      SourceDatasetId = retained.SourceDatasetId, SourceIdentityHash = retained.SourceIdentityHash,
      ProofJson = proof.ToJsonString(), ProofHash = Hashing.Sha256Hex(proof.ToJsonString()), RequestId = retained.RequestId,
      ActorId = retained.ActorId, ActorEpoch = retained.ActorEpoch, RequestHash = retained.RequestHash, CreatedAt = retained.CreatedAt
    };
    var parsed = ClientOperationalOpeningBalanceWorkspace.ParseSuccessorProof(forged, forgedProof);
    Assert.Equal(original.ManifestJson, (await db.ClientOperationalOpeningBalances.AsNoTracking().SingleAsync(x => x.Id == original.Id)).ManifestJson);
    Assert.Equal(retained.ProofJson, (await db.ClientOperationalOpeningSuccessorProofs.AsNoTracking().SingleAsync(x => x.OpeningId == original.Id)).ProofJson);
    Assert.False(parsed.Succeeded, "Balanced and self-hashed 119/119 opening cannot claim actual 120/120 source facts.");
    Assert.Equal(ErrorCodes.ProtectedState, parsed.ErrorCode);
    var crossEngagement = await NativeTrialBalanceSourceWorkspace.CheckOpeningSuccessorChainAsync(db, original,
      default, expectedEngagementId: f.Native.Scope.EngagementB);
    Assert.Equal(ErrorCodes.GateBlocked, crossEngagement.ErrorCode);
    Assert.Contains("Cross-engagement", crossEngagement.Message!, StringComparison.Ordinal);
  }
}
