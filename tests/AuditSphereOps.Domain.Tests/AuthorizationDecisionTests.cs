using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

// ND-02 / §8: authorization is decided inside commands from current stored state,
// never by the presentation layer. Synthetic firms/clients/users prove cross-scope
// denial through the same code path that real commands use.
[Trait("Profile", "Database")]
public sealed class AuthorizationDecisionTests
{
  private sealed record Scope(Guid FirmId, Guid ClientId, Guid EngagementId, Guid DatasetId);

  private static async Task<Scope> SeedScopeAsync(
    AuditSphereDbContext db, Guid firmId, string clientName, bool professionalWorkBlocked = false)
  {
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var datasetId = Guid.NewGuid();
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = clientName, CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = professionalWorkBlocked, CreatedAt = DateTimeOffset.UtcNow
    });
    if (!await db.FirmSafetyStates.AnyAsync(f => f.Id == firmId))
      db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState
      { Id = clientId, FirmId = firmId });
    db.TrialBalanceDatasets.Add(new TrialBalanceDataset
    {
      Id = datasetId, FirmId = firmId, ClientId = clientId, EngagementId = engagementId,
      Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow,
      ImportState = TrialBalanceImportStates.Sealed
    });
    await db.SaveChangesAsync();
    return new Scope(firmId, clientId, engagementId, datasetId);
  }

  private static async Task<AppUser> SeedUserAsync(
    AuditSphereDbContext db, Guid firmId, string kind = "Staff", bool disabled = false)
  {
    var user = new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId,
      Subject = "sub-" + Guid.NewGuid().ToString("N"),
      TenantId = "tenant-" + Guid.NewGuid().ToString("N"),
      Email = $"user-{Guid.NewGuid():N}@example.test",
      DisplayName = "Synthetic User",
      UserKind = kind, Disabled = disabled, SessionEpoch = 1,
      CreatedAt = DateTimeOffset.UtcNow
    };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return user;
  }

  private static async Task GrantAsync(
    AuditSphereDbContext db, Guid firmId, Guid userId, string role,
    Guid? clientId = null, Guid? engagementId = null, Guid? grantedBy = null)
  {
    db.RoleGrants.Add(new RoleGrant
    {
      Id = Guid.NewGuid(), FirmId = firmId, UserId = userId, Role = role,
      ClientId = clientId, EngagementId = engagementId,
      GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = grantedBy ?? userId
    });
    await db.SaveChangesAsync();
  }

  private static ActorContext Actor(AppUser user, params string[] roles) =>
    new(user.Id, user.FirmId, user.SessionEpoch, roles);

  [Fact]
  public async Task FirmWideGrant_CanReadOwnDataset_ThroughCommand()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope scope;
    AppUser user;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scope = await SeedScopeAsync(db, firmId, "CLIENT A");
      user = await SeedUserAsync(db, firmId);
      await GrantAsync(db, firmId, user.Id, "Staff");
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var result = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(user, "Staff"), scope.DatasetId);
    Assert.True(result.Succeeded);
    Assert.Equal(scope.DatasetId, result.Value!.Id);
  }

  [Fact]
  public async Task CrossFirm_DatasetRead_IsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    Scope a; Scope b; AppUser userA;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      a = await SeedScopeAsync(db, Guid.NewGuid(), "FIRM A CLIENT");
      b = await SeedScopeAsync(db, Guid.NewGuid(), "FIRM B CLIENT");
      userA = await SeedUserAsync(db, a.FirmId);
      await GrantAsync(db, a.FirmId, userA.Id, "Staff");
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var cross = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(userA, "Staff"), b.DatasetId);
    Assert.False(cross.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, cross.ErrorCode);
  }

  [Fact]
  public async Task CrossClient_GrantForOtherClient_IsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope a; Scope b; AppUser user;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      a = await SeedScopeAsync(db, firmId, "CLIENT A");
      b = await SeedScopeAsync(db, firmId, "CLIENT B");
      user = await SeedUserAsync(db, firmId);
      await GrantAsync(db, firmId, user.Id, "Staff", a.ClientId);
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var denied = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(user, "Staff"), b.DatasetId);
    Assert.False(denied.Succeeded);
    var allowed = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(user, "Staff"), a.DatasetId);
    Assert.True(allowed.Succeeded);
  }

  [Fact]
  public async Task EngagementScopedGrant_CoversOnlyItsEngagement()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope a; Scope b; AppUser user;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      a = await SeedScopeAsync(db, firmId, "CLIENT A");
      // Second engagement under the same client proves engagement scoping, not client scoping.
      b = new Scope(firmId, a.ClientId, Guid.NewGuid(), Guid.NewGuid());
      db.Engagements.Add(new Engagement
      {
        Id = b.EngagementId, FirmId = firmId, PracticeClientId = a.ClientId,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = b.DatasetId, FirmId = firmId, ClientId = a.ClientId, EngagementId = b.EngagementId,
        Currency = "QAR", ImportedAt = DateTimeOffset.UtcNow,
        ImportState = TrialBalanceImportStates.Sealed
      });
      await db.SaveChangesAsync();
      user = await SeedUserAsync(db, firmId);
      await GrantAsync(db, firmId, user.Id, "Staff", a.ClientId, a.EngagementId);
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var denied = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(user, "Staff"), b.DatasetId);
    Assert.False(denied.Succeeded);
    var allowed = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(user, "Staff"), a.DatasetId);
    Assert.True(allowed.Succeeded);
  }

  [Fact]
  public async Task DisabledUser_AndStaleSession_AreDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope scope; AppUser disabled; AppUser stale;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scope = await SeedScopeAsync(db, firmId, "CLIENT A");
      disabled = await SeedUserAsync(db, firmId, disabled: true);
      await GrantAsync(db, firmId, disabled.Id, "Staff");
      stale = await SeedUserAsync(db, firmId);
      await GrantAsync(db, firmId, stale.Id, "Staff");
      stale.SessionEpoch = 2;
      await db.SaveChangesAsync();
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var adapter = new OperationContextAdapter(query);
    var deniedDisabled = await TrialBalanceDatasetQuery.GetDatasetAsync(
      adapter, Actor(disabled, "Staff"), scope.DatasetId);
    Assert.False(deniedDisabled.Succeeded);
    // Actor presents the pre-disable epoch captured before the bump.
    var deniedStale = await TrialBalanceDatasetQuery.GetDatasetAsync(
      adapter, new ActorContext(stale.Id, stale.FirmId, 1, ["Staff"]), scope.DatasetId);
    Assert.False(deniedStale.Succeeded);
    Assert.Equal(ErrorCodes.GenerationStale, deniedStale.ErrorCode);
  }

  [Fact]
  public async Task MissingGrant_OrMissingRole_IsDenied()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope scope; AppUser nogt; AppUser wrong;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scope = await SeedScopeAsync(db, firmId, "CLIENT A");
      nogt = await SeedUserAsync(db, firmId);
      wrong = await SeedUserAsync(db, firmId);
      await GrantAsync(db, firmId, wrong.Id, "Staff", scope.ClientId);
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var adapter = new OperationContextAdapter(query);
    Assert.False((await TrialBalanceDatasetQuery.GetDatasetAsync(
      adapter, Actor(nogt, "Staff"), scope.DatasetId)).Succeeded);
    var roleDenied = await AuthorizationDecision.AuthorizeAsync(adapter,
      Actor(wrong, "Staff"),
      new AuthorizationRequest(firmId, scope.ClientId, scope.EngagementId, ["Partner"]));
    Assert.False(roleDenied.Succeeded);
    var roleAllowed = await AuthorizationDecision.AuthorizeAsync(adapter,
      Actor(wrong, "Staff"),
      new AuthorizationRequest(firmId, scope.ClientId, scope.EngagementId, ["Staff"]));
    Assert.True(roleAllowed.Succeeded);
  }

  [Fact]
  public async Task UnreleasedHold_BlocksProfessionalWork_ButNotPlainReads()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope scope; AppUser user;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scope = await SeedScopeAsync(db, firmId, "CLIENT A");
      user = await SeedUserAsync(db, firmId);
      await GrantAsync(db, firmId, user.Id, "Staff");
      db.EngagementHolds.Add(new EngagementHold
      {
        Id = Guid.NewGuid(), FirmId = firmId, EngagementId = scope.EngagementId,
        HoldKind = "Acceptance", Reason = "synthetic", CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var adapter = new OperationContextAdapter(query);
    var blocked = await AuthorizationDecision.AuthorizeAsync(adapter, Actor(user, "Staff"),
      new AuthorizationRequest(firmId, scope.ClientId, scope.EngagementId, null, false, true));
    Assert.False(blocked.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, blocked.ErrorCode);
    var read = await TrialBalanceDatasetQuery.GetDatasetAsync(adapter, Actor(user, "Staff"), scope.DatasetId);
    Assert.True(read.Succeeded);
  }

  [Fact]
  public async Task ClientUser_IsDenied_InternalOnlyTargets()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var firmId = Guid.NewGuid();
    Scope scope; AppUser client;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      scope = await SeedScopeAsync(db, firmId, "CLIENT A");
      client = await SeedUserAsync(db, firmId, kind: "Client");
      await GrantAsync(db, firmId, client.Id, "ClientUser", scope.ClientId);
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var adapter = new OperationContextAdapter(query);
    var internalDenied = await AuthorizationDecision.AuthorizeAsync(adapter,
      Actor(client, "ClientUser"),
      new AuthorizationRequest(firmId, scope.ClientId, scope.EngagementId, null, true));
    Assert.False(internalDenied.Succeeded);
  }

  [Fact]
  public async Task GuessedDatasetId_DoesNotDiscloseExistence()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    AppUser user;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var scope = await SeedScopeAsync(db, Guid.NewGuid(), "CLIENT A");
      user = await SeedUserAsync(db, scope.FirmId);
      await GrantAsync(db, scope.FirmId, user.Id, "Staff");
    }
    await using var query = new AuditSphereDbContext(pg.Options);
    var guessed = await TrialBalanceDatasetQuery.GetDatasetAsync(
      new OperationContextAdapter(query), Actor(user, "Staff"), Guid.NewGuid());
    Assert.False(guessed.Succeeded);
    Assert.Equal(ErrorCodes.ScopeDenied, guessed.ErrorCode);
  }

  [Fact]
  public async Task DuplicateIdentityBinding_IsRejected()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using var db = new AuditSphereDbContext(pg.Options);
    var firmId = Guid.NewGuid();
    db.Users.Add(new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "same-sub", TenantId = "same-tenant",
      Email = "a@example.test", DisplayName = "A", CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    db.Users.Add(new AppUser
    {
      Id = Guid.NewGuid(), FirmId = firmId, Subject = "same-sub", TenantId = "same-tenant",
      Email = "b@example.test", DisplayName = "B", CreatedAt = DateTimeOffset.UtcNow
    });
    await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
  }

  [Fact]
  public async Task OrphanGrant_OrphanEngagement_AndEngagementGrantWithoutClient_AreRejected()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var user = await SeedUserAsync(db, Guid.NewGuid());
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = user.FirmId, UserId = user.Id, Role = "Staff",
        ClientId = Guid.NewGuid(), GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Engagements.Add(new Engagement
      {
        Id = Guid.NewGuid(), FirmId = Guid.NewGuid(), PracticeClientId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var scope = await SeedScopeAsync(db, Guid.NewGuid(), "CLIENT A");
      var user = await SeedUserAsync(db, scope.FirmId);
      db.RoleGrants.Add(new RoleGrant
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, UserId = user.Id, Role = "Staff",
        EngagementId = scope.EngagementId, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
      });
      await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
  }

  // Minimal adapter: the decision service operates on the persistence boundary,
  // so commands never depend on the concrete DbContext type directly.
  private sealed class OperationContextAdapter(AuditSphereDbContext db)
    : AuditSphereOps.Application.Operations.IAuditSphereDbContext
  {
    public DbSet<AuditSphereOps.Domain.Completion.DurableOperation> DurableOperations => db.DurableOperations;
    public DbSet<AuditSphereOps.Domain.Completion.OperationAttempt> OperationAttempts => db.OperationAttempts;
    public DbSet<AuditSphereOps.Domain.Completion.OperationEvent> OperationEvents => db.OperationEvents;
    public DbSet<AuditSphereOps.Domain.Completion.FirmSafetyState> FirmSafetyStates => db.FirmSafetyStates;
    public DbSet<AuditSphereOps.Domain.Completion.RecoverySession> RecoverySessions => db.RecoverySessions;
    public DbSet<AuditSphereOps.Domain.Completion.ClientSafetyState> ClientSafetyStates => db.ClientSafetyStates;
    public DbSet<TrialBalanceDataset> TrialBalanceDatasets => db.TrialBalanceDatasets;
    public DbSet<AuditSphereOps.Domain.Accounting.TrialBalanceRow> TrialBalanceRows => db.TrialBalanceRows;
    public DbSet<AuditSphereOps.Domain.Accounting.MappingRule> MappingRules => db.MappingRules;
    public DbSet<AuditSphereOps.Domain.Accounting.MappingVersion> MappingVersions => db.MappingVersions;
    public DbSet<AuditSphereOps.Domain.Accounting.MappingAllocation> MappingAllocations => db.MappingAllocations;
    public DbSet<AuditSphereOps.Domain.Accounting.AdjustedTrialBalanceSnapshot> AdjustedTrialBalanceSnapshots => db.AdjustedTrialBalanceSnapshots;
    public DbSet<AuditSphereOps.Domain.Accounting.AdjustedTrialBalanceRow> AdjustedTrialBalanceRows => db.AdjustedTrialBalanceRows;
    public DbSet<AuditSphereOps.Domain.Accounting.AdjustmentJournal> AdjustmentJournals => db.AdjustmentJournals;
    public DbSet<AuditSphereOps.Domain.Accounting.AdjustmentLine> AdjustmentLines => db.AdjustmentLines;
    public DbSet<AuditSphereOps.Domain.Accounting.JournalSourceReconciliation> JournalSourceReconciliations => db.JournalSourceReconciliations;
    public DbSet<AuditSphereOps.Domain.Accounting.AdjustmentPlan> AdjustmentPlans => db.AdjustmentPlans;
    public DbSet<AuditSphereOps.Domain.Accounting.AdjustmentPlanLine> AdjustmentPlanLines => db.AdjustmentPlanLines;
    public DbSet<AuditSphereOps.Domain.Accounting.FinancialPackage> FinancialPackages => db.FinancialPackages;
    public DbSet<AuditSphereOps.Domain.Accounting.FinancialPackageLine> FinancialPackageLines => db.FinancialPackageLines;
    public DbSet<AuditSphereOps.Domain.Accounting.FinancialPackageValidation> FinancialPackageValidations => db.FinancialPackageValidations;
    public DbSet<AuditSphereOps.Domain.Accounting.FinancialPackageCashFlowLine> FinancialPackageCashFlowLines => db.FinancialPackageCashFlowLines;
    public DbSet<AuditSphereOps.Domain.Accounting.FinancialPackageDisclosure> FinancialPackageDisclosures => db.FinancialPackageDisclosures;
    public DbSet<Engagement> Engagements => db.Engagements;
    public DbSet<EngagementHold> EngagementHolds => db.EngagementHolds;
    public DbSet<AuditSphereOps.Domain.Documents.RepositoryBinding> RepositoryBindings => db.RepositoryBindings;
    public DbSet<AuditSphereOps.Domain.Documents.SyncCursor> SyncCursors => db.SyncCursors;
    public DbSet<AuditSphereOps.Domain.Documents.IntegrationCapability> IntegrationCapabilities => db.IntegrationCapabilities;
    public DbSet<AuditSphereOps.Domain.Documents.DocumentReference> DocumentReferences => db.DocumentReferences;
    public DbSet<AuditSphereOps.Domain.Documents.DocumentSnapshot> DocumentSnapshots => db.DocumentSnapshots;
    public DbSet<AuditSphereOps.Domain.Documents.PbcRequest> PbcRequests => db.PbcRequests;
    public DbSet<AuditSphereOps.Domain.Documents.PbcUploadIntent> PbcUploadIntents => db.PbcUploadIntents;
    public DbSet<AuditSphereOps.Domain.Documents.PbcUploadChunk> PbcUploadChunks => db.PbcUploadChunks;
    public DbSet<AuditSphereOps.Domain.Audit.Workpaper> Workpapers => db.Workpapers;
    public DbSet<AuditSphereOps.Domain.Audit.WorkpaperDraft> WorkpaperDrafts => db.WorkpaperDrafts;
    public DbSet<AuditSphereOps.Domain.Audit.WorkpaperSubmission> WorkpaperSubmissions => db.WorkpaperSubmissions;
    public DbSet<AuditSphereOps.Domain.Audit.MaterialityAssessment> MaterialityAssessments => db.MaterialityAssessments;
    public DbSet<AuditSphereOps.Domain.Audit.PopulationVersion> PopulationVersions => db.PopulationVersions;
    public DbSet<AuditSphereOps.Domain.Audit.AuditRisk> AuditRisks => db.AuditRisks;
    public DbSet<AuditSphereOps.Domain.Audit.AuditProcedure> AuditProcedures => db.AuditProcedures;
    public DbSet<AuditSphereOps.Domain.Audit.AuditProgramVersion> AuditProgramVersions => db.AuditProgramVersions;
    public DbSet<AuditSphereOps.Domain.Audit.AuditProgramProcedure> AuditProgramProcedures => db.AuditProgramProcedures;
    public DbSet<AuditSphereOps.Domain.Audit.EngagementAuditProgram> EngagementAuditPrograms => db.EngagementAuditPrograms;
    public DbSet<AuditSphereOps.Domain.Audit.AuditProcedureResult> AuditProcedureResults => db.AuditProcedureResults;
    public DbSet<AuditSphereOps.Domain.Audit.AuditProcedureReview> AuditProcedureReviews => db.AuditProcedureReviews;
    public DbSet<AuditSphereOps.Domain.Audit.AuditSchedule> AuditSchedules => db.AuditSchedules;
    public DbSet<AuditSphereOps.Domain.Audit.AuditScheduleRow> AuditScheduleRows => db.AuditScheduleRows;
    public DbSet<AuditSphereOps.Domain.Audit.AuditSelection> AuditSelections => db.AuditSelections;
    public DbSet<AuditSphereOps.Domain.Audit.AuditSelectionItem> AuditSelectionItems => db.AuditSelectionItems;
    public DbSet<AuditSphereOps.Domain.Audit.AuditItemTest> AuditItemTests => db.AuditItemTests;
    public DbSet<AuditSphereOps.Domain.Audit.AuditItemTestReview> AuditItemTestReviews => db.AuditItemTestReviews;
    public DbSet<AuditSphereOps.Domain.Audit.AuditConfirmationCase> AuditConfirmationCases => db.AuditConfirmationCases;
    public DbSet<AuditSphereOps.Domain.Audit.AuditConfirmationResponse> AuditConfirmationResponses => db.AuditConfirmationResponses;
    public DbSet<AuditSphereOps.Domain.Audit.AuditAlternativeProcedure> AuditAlternativeProcedures => db.AuditAlternativeProcedures;
    public DbSet<AuditSphereOps.Domain.Audit.AuditAreaAssessment> AuditAreaAssessments => db.AuditAreaAssessments;
    public DbSet<AuditSphereOps.Domain.Audit.AuditDifference> AuditDifferences => db.AuditDifferences;
    public DbSet<AuditSphereOps.Domain.Audit.Finding> Findings => db.Findings;
    public DbSet<AuditSphereOps.Domain.Reviews.ReviewPoint> ReviewPoints => db.ReviewPoints;
    public DbSet<AuditSphereOps.Domain.Reviews.Approval> Approvals => db.Approvals;
    public DbSet<AuditSphereOps.Domain.Reviews.ApprovalApplicability> ApprovalApplicabilities => db.ApprovalApplicabilities;
    public DbSet<AuditSphereOps.Domain.Completion.ReleaseCandidate> ReleaseCandidates => db.ReleaseCandidates;
    public DbSet<AuditSphereOps.Domain.Completion.Release> Releases => db.Releases;
    public DbSet<AuditSphereOps.Domain.Completion.ReleaseCheckpoint> ReleaseCheckpoints => db.ReleaseCheckpoints;
    public DbSet<AuditSphereOps.Domain.Completion.SignatureLineage> SignatureLineages => db.SignatureLineages;
    public DbSet<AuditSphereOps.Domain.Records.ProtectionAttestation> ProtectionAttestations => db.ProtectionAttestations;
    public DbSet<AuditSphereOps.Domain.Completion.Archive> Archives => db.Archives;
    public DbSet<AuditSphereOps.Domain.Records.RecordsProfile> RecordsProfiles => db.RecordsProfiles;
    public DbSet<AuditSphereOps.Domain.Records.ArchiveManifest> ArchiveManifests => db.ArchiveManifests;
    public DbSet<AuditSphereOps.Domain.Records.ArchiveManifestEntry> ArchiveManifestEntries => db.ArchiveManifestEntries;
    public DbSet<AuditSphereOps.Domain.Records.ArchiveStructuredExport> ArchiveStructuredExports => db.ArchiveStructuredExports;
    public DbSet<AuditSphereOps.Domain.Records.RecordsActionEvidence> RecordsActionEvidences => db.RecordsActionEvidences;
    public DbSet<AuditSphereOps.Domain.Records.RecordsAction> RecordsActions => db.RecordsActions;
    public DbSet<AuditSphereOps.Domain.Records.LegalHold> LegalHolds => db.LegalHolds;
    public DbSet<AuditSphereOps.Domain.Completion.EqrCase> EqrCases => db.EqrCases;

    public DbSet<AuditSphereOps.Domain.Completion.WrittenRepresentation> WrittenRepresentations => db.WrittenRepresentations;
    public DbSet<AuditSphereOps.Domain.Engagements.EngagementAssignment> EngagementAssignments => db.EngagementAssignments;
    public DbSet<AuditSphereOps.Domain.Acceptance.SpecialistClearance> SpecialistClearances => db.SpecialistClearances;
    public DbSet<AuditSphereOps.Domain.Acceptance.QuestionnaireTemplate> QuestionnaireTemplates => db.QuestionnaireTemplates;
    public DbSet<AuditSphereOps.Domain.Acceptance.QuestionDefinition> QuestionDefinitions => db.QuestionDefinitions;
    public DbSet<AuditSphereOps.Domain.Documents.SourceReceipt> SourceReceipts => db.SourceReceipts;
    public DbSet<AuditSphereOps.Domain.Documents.EvidenceLink> EvidenceLinks => db.EvidenceLinks;
    public DbSet<EvaluationResponse> EvaluationResponses => db.EvaluationResponses;
    public DbSet<AcceptanceDecision> AcceptanceDecisions => db.AcceptanceDecisions;
    public DbSet<Lead> Leads => db.Leads;
    public DbSet<Opportunity> Opportunities => db.Opportunities;
    public DbSet<Proposal> Proposals => db.Proposals;
    public DbSet<PracticeClient> PracticeClients => db.PracticeClients;
    public DbSet<ClientContact> ClientContacts => db.ClientContacts;
    public DbSet<WorkTask> WorkTasks => db.WorkTasks;
    public DbSet<TimeEntry> TimeEntries => db.TimeEntries;
    public DbSet<RateCardVersion> RateCardVersions => db.RateCardVersions;
    public DbSet<EngagementBudget> EngagementBudgets => db.EngagementBudgets;
    public DbSet<BudgetLine> BudgetLines => db.BudgetLines;
    public DbSet<BillingAccount> BillingAccounts => db.BillingAccounts;
    public DbSet<FirmFinanceProfile> FirmFinanceProfiles => db.FirmFinanceProfiles;
    public DbSet<Invoice> Invoices => db.Invoices;
    public DbSet<InvoiceLine> InvoiceLines => db.InvoiceLines;
    public DbSet<Receipt> Receipts => db.Receipts;
    public DbSet<ReceiptAllocation> ReceiptAllocations => db.ReceiptAllocations;
    public DbSet<CreditNote> CreditNotes => db.CreditNotes;
    public DbSet<BillingSourceAllocation> BillingSourceAllocations => db.BillingSourceAllocations;
    public DbSet<FirmAccount> FirmAccounts => db.FirmAccounts;
    public DbSet<FirmPeriod> FirmPeriods => db.FirmPeriods;
    public DbSet<FirmJournal> FirmJournals => db.FirmJournals;
    public DbSet<FirmJournalLine> FirmJournalLines => db.FirmJournalLines;
    public DbSet<FirmPosting> FirmPostings => db.FirmPostings;
    public DbSet<FirmPostingLine> FirmPostingLines => db.FirmPostingLines;
    public DbSet<LedgerSourceLink> LedgerSourceLinks => db.LedgerSourceLinks;
    public DbSet<LedgerPostingReceipt> LedgerPostingReceipts => db.LedgerPostingReceipts;
    public DbSet<PeriodCloseDecision> PeriodCloseDecisions => db.PeriodCloseDecisions;
    public DbSet<AppUser> Users => db.Users;
    public DbSet<RoleGrant> RoleGrants => db.RoleGrants;
    public Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database => db.Database;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }
}
