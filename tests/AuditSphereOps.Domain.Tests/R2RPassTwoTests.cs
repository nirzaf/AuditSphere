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
using Npgsql;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Pass-two R2R contract slices: immutable reconciliation proofs gate approval,
/// reversals cannot be duplicated, adjustment journals carry technical submit/return
/// states, restatements carry supported IAS 8 change types, and group journals can be
/// returned with a mandatory reason and resubmitted.</summary>
public sealed class R2RPassTwoTests
{
  private sealed record Fixture(Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId,
    AppUser Preparer, AppUser Reviewer, AppUser Partner);  // ---- M23: persisted reconciliation proof -----------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task ReconciliationApproval_RequiresCurrentImmutableProof()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var (periodId, bookId) = await CreateGlFixtureAsync(pg, fixture, preparer);
    var sourceHash = Hashing.Sha256Hex("proof-source");
    var datasetId = Guid.CreateVersion7();

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
        PeriodId = periodId, BookId = bookId, Basis = "STATUTORY", SourceKind = "Raw", LegalEntityKey = "PROOF-CLIENT",
        Currency = "QAR", RawFileSha256Hex = sourceHash, NormalizedDatasetDigest = sourceHash, Sha256Hex = sourceHash,
        Balanced = true, ValidationStatus = "Pending", ImportState = TrialBalanceImportStates.Loading,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = fixture.Preparer.Id
      });
      db.TrialBalanceRows.Add(new TrialBalanceRow
      {
        Id = Guid.CreateVersion7(), DatasetId = datasetId, AccountCode = "1000", AccountName = "Cash",
        Amount = 100m, Currency = "QAR", Entity = "PROOF-CLIENT"
      });
      await db.SaveChangesAsync();
      var sealedDataset = await db.TrialBalanceDatasets.SingleAsync(x => x.Id == datasetId);
      sealedDataset.ValidationStatus = "Accepted";
      sealedDataset.ImportState = TrialBalanceImportStates.Sealed;
      await db.SaveChangesAsync();
      var reconciliationId = (await AccountingAnalysisService.CreateReconciliationAsync(db, preparer,
        new AccountingReconciliationRequest(fixture.ClientId, fixture.EngagementId, periodId, bookId, "CASH",
          datasetId, null, ["1000"], new DateOnly(2026, 12, 31)))).Value;
      Assert.True((await AccountingAnalysisService.AddReconciliationItemsAsync(db, preparer, reconciliationId,
        [
          new ReconciliationItemInput("BANK-1", 100m, "QAR", new DateOnly(2026, 12, 31), "timing", "bank-evidence-1", "OPEN"),
          new ReconciliationItemInput("BANK-2", -100m, "QAR", new DateOnly(2026, 12, 31), "timing", "bank-evidence-2", "OPEN")
        ])).Succeeded);

      // Approval without any calculated proof is refused.
      var early = await AccountingAnalysisService.ApproveReconciliationAsync(db, reviewer, reconciliationId);
      Assert.False(early.Succeeded);
      Assert.Equal(ErrorCodes.GateBlocked, early.ErrorCode);
      Assert.Contains("proof", early.Message!, StringComparison.OrdinalIgnoreCase);

      var proof = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, reconciliationId);
      Assert.True(proof.Succeeded, proof.Message);
      Assert.True(proof.Value!.IsReconciled);
      Assert.Equal(1, await db.AccountingReconciliationProofs.CountAsync(
        x => x.ReconciliationId == reconciliationId));

      var approved = await AccountingAnalysisService.ApproveReconciliationAsync(db, reviewer, reconciliationId);
      Assert.True(approved.Succeeded, approved.Message);

      // Recalculating appends a new immutable proof instead of editing the old one.
      var again = await AccountingAnalysisService.CalculateReconciliationProofAsync(db, preparer, reconciliationId);
      Assert.True(again.Succeeded);
      Assert.NotEqual(proof.Value.ProofId, again.Value!.ProofId);
      Assert.Equal(2, await db.AccountingReconciliationProofs.CountAsync(x => x.ReconciliationId == reconciliationId));
      var firstProof = await db.AccountingReconciliationProofs.AsNoTracking()
        .SingleAsync(x => x.Id == proof.Value.ProofId);
      var tamper = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE accounting_reconciliation_proofs SET residual = 5 WHERE id = {firstProof.Id}"));
      Assert.Equal("55000", tamper.SqlState);
    }
  }

  // ---- M22: duplicate reversal guard ------------------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task DuplicateReversal_IsRejected()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid postedId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var journal = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-REV-1", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journal)).Succeeded);
      postedId = journal;
      var first = await AdjustmentJournalService.CreateReversalDraftAsync(db, preparer, postedId, "AJ-REV-1-R");
      Assert.True(first.Succeeded, first.Message);
      var duplicate = await AdjustmentJournalService.CreateReversalDraftAsync(db, preparer, postedId, "AJ-REV-1-R2");
      Assert.False(duplicate.Succeeded);
      Assert.Equal(ErrorCodes.ProtectedState, duplicate.ErrorCode);
    }
  }

  // ---- M22: technical submit/return lifecycle ----------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task JournalTechnicalStates_SupportSubmitReturnAndResubmission()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");

    Guid journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      journalId = (await AdjustmentJournalService.CreateDraftAsync(db, preparer, fixture.DatasetId,
        "AJ-TECH-1", [("1000", 10m, 0m), ("4000", 0m, 10m)])).Value;

      // Self-return by the preparer is denied before any state change.
      var selfReturn = await AdjustmentJournalService.ReturnSubmissionAsync(db, preparer, journalId, "self review");
      Assert.False(selfReturn.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfReturn.ErrorCode);

      Assert.True((await AdjustmentJournalService.SubmitDraftAsync(db, preparer, journalId)).Succeeded);
      Assert.Equal("Submitted", await db.AdjustmentJournals.Where(x => x.Id == journalId).Select(x => x.Status).SingleAsync());

      var missingReason = await AdjustmentJournalService.ReturnSubmissionAsync(db, reviewer, journalId, " ");
      Assert.False(missingReason.Succeeded);

      Assert.True((await AdjustmentJournalService.ReturnSubmissionAsync(db, reviewer, journalId,
        "Line 2 lacks supporting evidence.")).Succeeded);
      var returned = await db.AdjustmentJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal("Returned", returned.Status);
      Assert.Equal("Line 2 lacks supporting evidence.", returned.ReturnReason);

      Assert.True((await AdjustmentJournalService.SubmitDraftAsync(db, preparer, journalId)).Succeeded);
      Assert.True((await AdjustmentJournalService.PostAsync(db, reviewer, journalId)).Succeeded);
    }
  }

  // ---- M24: restatement change types --------------------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task Restatement_RequiresSupportedChangeTypeAndAffectedPeriods()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "AccountingPreparer");
    var reviewer = Actor(fixture.Reviewer, "AccountingReviewer");
    var partner = Actor(fixture.Partner, "Partner");

    Guid periodId, originalPackageId, revisedPackageId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(fixture.ClientId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR"))).Value;
      originalPackageId = AddValidatedPackage(db, fixture, 100m, "CASH", "restatement-original");
      revisedPackageId = AddValidatedPackage(db, fixture, 90m, "CASH", "restatement-revised");
      await db.SaveChangesAsync();
      foreach (var (actor, stage, evidence) in new[]
      {
        (preparer, FinancialPackageReviewStages.ManagementApproval, "mgmt-restatement"),
        (reviewer, FinancialPackageReviewStages.AccountingReview, "accounting-restatement"),
        (partner, FinancialPackageReviewStages.PartnerApproval, "partner-restatement")
      })
      {
        foreach (var packageId in new[] { originalPackageId, revisedPackageId })
          Assert.True((await FinancialPackageReviewService.RecordAsync(db, actor,
            new FinancialPackageReviewRequest(packageId, stage, FinancialPackageReviewDecisions.Approved,
              stage == FinancialPackageReviewStages.ManagementApproval
                ? FinancialPackageReviewEvidenceModes.Offline
                : FinancialPackageReviewEvidenceModes.SignedIn,
              evidence, "Restatement fixture approval."))).Succeeded);
      }
      Assert.True((await ClientAccountingService.ClosePeriodAsync(db, reviewer, periodId, "Restatement fixture close")).Succeeded);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var missingType = await ClientAccountingService.CreatePeriodRestatementAsync(db, preparer,
        new CreatePeriodRestatementRequest(fixture.ClientId, periodId, originalPackageId, revisedPackageId,
          "IFRS", "Reclassification of cash equivalents", "restatement-evidence-1"));
      Assert.False(missingType.Succeeded);
      Assert.Contains("change type", missingType.Message!, StringComparison.OrdinalIgnoreCase);

      var missingPeriods = await ClientAccountingService.CreatePeriodRestatementAsync(db, preparer,
        new CreatePeriodRestatementRequest(fixture.ClientId, periodId, originalPackageId, revisedPackageId,
          "IFRS", "Estimate revised with prospective treatment", "restatement-evidence-1",
          PeriodRestatementChangeTypes.ProspectiveEstimateChange));
      Assert.False(missingPeriods.Succeeded);
      Assert.Contains("prospective", missingPeriods.Message!, StringComparison.OrdinalIgnoreCase);

      var created = await ClientAccountingService.CreatePeriodRestatementAsync(db, preparer,
        new CreatePeriodRestatementRequest(fixture.ClientId, periodId, originalPackageId, revisedPackageId,
          "IFRS", "Prior-period error corrected", "restatement-evidence-1",
          PeriodRestatementChangeTypes.RestatedError, "FY2026"));
      Assert.True(created.Succeeded, created.Message);
      var saved = await db.ClientPeriodRestatements.SingleAsync(x => x.Id == created.Value);
      Assert.Equal(PeriodRestatementChangeTypes.RestatedError, saved.ChangeType);
      Assert.Equal("FY2026", saved.AffectedPeriods);
      Assert.True((await ClientAccountingService.ApprovePeriodRestatementAsync(db, reviewer, created.Value)).Succeeded);
    }
  }

  // ---- M26: group journal return and resubmission --------------------------------------

  [Fact]
  [Trait("Profile", "Database")]
  public async Task GroupJournal_ReturnRequiresReasonAndResubmission()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var fixture = await SeedAsync(pg);
    var preparer = Actor(fixture.Preparer, "Partner");
    var reviewer = Actor(fixture.Reviewer, "Partner");

    Guid groupId, scopeId, journalId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var partnerActor = Actor(fixture.Partner, "Partner");
      groupId = (await ConsolidationService.CreateGroupAsync(db, partnerActor,
        new ClientGroupRequest("GROUP-RETURN", "Group journal return regression"))).Value;
      // Explicit group access for the preparer and reviewer actors.
      db.GroupAccessGrants.AddRange(
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Preparer.Id,
          Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Partner.Id
        },
        new GroupAccessGrant
        {
          Id = Guid.NewGuid(), FirmId = fixture.FirmId, GroupId = groupId, UserId = fixture.Reviewer.Id,
          Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Partner.Id
        });
      await db.SaveChangesAsync();
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var scopeIdLocal = Guid.CreateVersion7();
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = scopeIdLocal, FirmId = fixture.FirmId, GroupId = groupId, GroupRevision = 1,
        PeriodId = Guid.NewGuid(), Version = 1, ReportingCurrency = "QAR",
        Method = ConsolidationCalculator.RestrictedMethod, Status = AccountingWorkflowStates.Approved,
        OpeningBasis = "OPENING-2026", CreatedByUserId = fixture.Preparer.Id,
        ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      scopeId = scopeIdLocal;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      journalId = (await ConsolidationService.CreateConsolidationJournalAsync(db, preparer,
        new ConsolidationJournalRequest(scopeId, "GJ-RETURN-1", "ELIMINATION", "QAR", "elimination-evidence-1",
          [
            new ConsolidationJournalLineInput(null, "REVENUE", 100m, 0m, "Eliminate intragroup revenue"),
            new ConsolidationJournalLineInput(null, "EXPENSE", 0m, 100m, "Eliminate intragroup expense")
          ]))).Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var missingReason = await ConsolidationService.ReturnConsolidationJournalAsync(db, reviewer, journalId, " ");
      Assert.False(missingReason.Succeeded);

      var selfReturn = await ConsolidationService.ReturnConsolidationJournalAsync(db, preparer, journalId, "self");
      Assert.False(selfReturn.Succeeded);
      Assert.Equal(ErrorCodes.ScopeDenied, selfReturn.ErrorCode);

      Assert.True((await ConsolidationService.ReturnConsolidationJournalAsync(db, reviewer, journalId,
        "The elimination needs the agreed intercompany balance attached.")).Succeeded);
      var returned = await db.ConsolidationJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal("Returned", returned.Status);
      Assert.Equal("The elimination needs the agreed intercompany balance attached.", returned.ReturnReason);

      // Approval of a returned journal is refused; resubmission re-enters review.
      var approveReturned = await ConsolidationService.ApproveConsolidationJournalAsync(db, reviewer, journalId);
      Assert.False(approveReturned.Succeeded);
      Assert.True((await ConsolidationService.ResubmitConsolidationJournalAsync(db, preparer, journalId)).Succeeded);
      var resubmitted = await db.ConsolidationJournals.SingleAsync(x => x.Id == journalId);
      Assert.Equal(AccountingWorkflowStates.Submitted, resubmitted.Status);
      Assert.Null(resubmitted.ReturnReason);
    }
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
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    var preparer = User(firmId, "preparer");
    var reviewer = User(firmId, "reviewer");
    var partner = User(firmId, "partner");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "R2R PASS TWO CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId, ProfessionalWorkBlocked = false,
      CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientId, FirmId = firmId });
    db.Users.AddRange(preparer, reviewer, partner);
    db.RoleGrants.AddRange(
      Grant(firmId, preparer, "AccountingPreparer"), Grant(firmId, reviewer, "AccountingReviewer"),
      Grant(firmId, partner, "Partner"));
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      SourceKind = "Raw", Revision = 1, Currency = "QAR", Balanced = true,
      ValidationStatus = "Accepted", ControlTotal = 0m, ImportedAt = DateTimeOffset.UtcNow,
      ImportedByUserId = preparer.Id
    });
    db.TrialBalanceRows.AddRange(
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "1000",
        AccountName = "Cash", Amount = 100m, Currency = "QAR", Entity = "TEST"
      },
      new TrialBalanceRow
      {
        Id = Guid.NewGuid(), DatasetId = datasetId, AccountCode = "4000",
        AccountName = "Revenue", Amount = -100m, Currency = "QAR", Entity = "TEST"
      });
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, datasetId, preparer, reviewer, partner);
  }

  private static async Task<(Guid PeriodId, Guid BookId)> CreateGlFixtureAsync(
    PgTestSchema pg, Fixture fixture, ActorContext preparer)
  {
    Guid periodId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.True((await ClientAccountingService.CreateProfileAsync(db, preparer,
        new ClientAccountingProfileRequest(fixture.ClientId, "QA", "QAR", 1, 1, "LEDGER-P2", "P2-1"))).Succeeded);
      periodId = (await ClientAccountingService.CreatePeriodAsync(db, preparer,
        new ReportingPeriodRequest(fixture.ClientId, "2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
          "STATUTORY", "QAR"))).Value;
      var book = await ClientAccountingService.CreateBookAsync(db, preparer,
        new ReportingBookRequest(fixture.ClientId, periodId, "STAT", "STATUTORY", "STATUTORY_ONLY", "QAR"));
      Assert.True(book.Succeeded, book.Message);
    }
    await using var verify = new AuditSphereDbContext(pg.Options);
    var bookId = await verify.ClientReportingBooks.Where(x => x.ClientId == fixture.ClientId && x.PeriodId == periodId)
      .Select(x => x.Id).SingleAsync();
    return (periodId, bookId);
  }

  private static Guid AddValidatedPackage(
    AuditSphereDbContext db, Fixture fixture, decimal amount, string destination, string suffix)
  {
    var now = DateTimeOffset.UtcNow;
    var datasetId = Guid.CreateVersion7();
    var planId = Guid.CreateVersion7();
    var mappingId = Guid.CreateVersion7();
    var adjustedId = Guid.CreateVersion7();
    var packageId = Guid.CreateVersion7();
    var digest = Hashing.Sha256Hex($"p2-package-{suffix}");
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      SourceKind = "Raw", Revision = 1, LegalEntityKey = suffix.ToUpperInvariant(), Currency = "QAR",
      RawFileSha256Hex = digest, NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true,
      ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m,
      ImportedAt = now, ImportedByUserId = fixture.Preparer.Id
    });
    db.AdjustmentPlans.Add(new AdjustmentPlan
    {
      Id = planId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      BaseDatasetId = datasetId, Status = "Finalized", ResultHash = digest,
      CreatedByUserId = fixture.Preparer.Id, CreatedAt = now
    });
    db.MappingVersions.Add(new MappingVersion
    {
      Id = mappingId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      DatasetId = datasetId, Version = 1, Generation = 1, TaxonomyVersion = "tax-v1",
      PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
      CreatedByUserId = fixture.Preparer.Id, ApprovedByUserId = fixture.Reviewer.Id, ApprovedAt = now, CreatedAt = now
    });
    db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
    {
      Id = adjustedId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      BaseDatasetId = datasetId, AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest,
      CreatedByUserId = fixture.Preparer.Id, CreatedAt = now
    });
    db.FinancialPackages.Add(new FinancialPackage
    {
      Id = packageId, FirmId = fixture.FirmId, ClientId = fixture.ClientId, EngagementId = fixture.EngagementId,
      AdjustedDatasetId = adjustedId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
      Framework = "IFRS", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", TaxonomyVersion = "tax-v1",
      TemplateVersion = $"p2-{suffix}", CalculationEngineVersion = "test-engine", CalculationHash = digest,
      Currency = "QAR", Status = AccountingPackageStates.PackageValidated, CreatedAt = now
    });
    db.FinancialPackageLines.Add(new FinancialPackageLine
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, FinancialPackageId = packageId,
      SourceAccountCode = destination == "CASH" ? "1000" : "4000", DestinationCode = destination,
      StatementSection = "STATEMENT", Amount = amount, Fraction = 1m, Currency = "QAR",
      AdjustedSnapshotId = adjustedId, CreatedAt = now
    });
    var artifactBytes = System.Text.Encoding.UTF8.GetBytes($"package-artifact|{packageId:D}|{digest}");
    db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.FirmId, ClientId = fixture.ClientId,
      EngagementId = fixture.EngagementId, FinancialPackageId = packageId, PackageRevision = 1,
      PackageGeneration = 1, PackageHash = digest, ArtifactVersion = FinancialPackageArtifactVersions.Text,
      FrameworkVersion = "IFRS", TemplateVersion = $"p2-{suffix}",
      ArtifactSha256Hex = Hashing.Sha256Hex(artifactBytes), ArtifactBytes = artifactBytes,
      CreatedByUserId = fixture.Preparer.Id, CreatedAt = now
    });
    return packageId;
  }
}
