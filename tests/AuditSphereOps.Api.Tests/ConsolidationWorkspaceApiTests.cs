using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ConsolidationWorkspaceApiTests
{
  [Fact]
  public async Task ScopeWorkspace_AccessDeniedWithoutGroupGrant()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-DENIED");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid scopeId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var group = new ClientGroup { Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Code = "GRP-01", Name = "Test Group" };
      db.ClientGroups.Add(group);
      var scope = new ConsolidationScopeVersion
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        PeriodId = Guid.NewGuid(), Method = ConsolidationCalculator.RestrictedMethod,
        ReportingCurrency = "QAR", Status = AccountingWorkflowStates.Draft,
        OpeningBasis = "OPENING_BALANCE",
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow,
        GroupRevision = 1
      };
      db.ConsolidationScopeVersions.Add(scope);
      await db.SaveChangesAsync();
      scopeId = scope.Id;
    }

    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    await SignIn(client);

    using var response = await client.GetAsync($"/api/ui/consolidation/scopes/{scopeId}");
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task ScopeWorkspace_CanQueryDetail_AndSubmitAndApproveComponentWithMakerChecker()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-COMPONENTS");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid groupId, scopeId, packageId;
    AppUser reviewer;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reviewer = new AppUser
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Email = "reviewer.consol@auditsphere.test",
        DisplayName = "Consol Reviewer", TenantId = "tenant-001", Subject = "sub-reviewer-consol"
      };
      db.Users.Add(reviewer);

      var group = new ClientGroup { Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Code = "GRP-ALPHA", Name = "Alpha Group", Revision = 1 };
      db.ClientGroups.Add(group);
      groupId = group.Id;

      db.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id, ClientId = seed.ClientId,
        EffectiveFrom = new DateOnly(2026, 1, 1), ControlMethod = "CONTROLLED", OwnershipPercent = 100m,
        Status = AccountingWorkflowStates.Approved, CreatedAt = DateTimeOffset.UtcNow
      });

      // Role grants: seed.Staff has Preparer on Group; reviewer has Reviewer on Group
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        UserId = seed.Staff.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        UserId = reviewer.Id, Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });

      // Reviewer and preparer need engagement scope for package review verification
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "AccountingPreparer", clientId: seed.ClientId, engagementId: seed.EngagementId));
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, reviewer, "Partner", clientId: seed.ClientId, engagementId: seed.EngagementId));

      var scope = new ConsolidationScopeVersion
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        PeriodId = Guid.NewGuid(), Method = ConsolidationCalculator.RestrictedMethod,
        ReportingCurrency = "QAR", Status = AccountingWorkflowStates.Draft,
        OpeningBasis = "OPENING_BALANCE",
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow,
        GroupRevision = 1
      };
      db.ConsolidationScopeVersions.Add(scope);
      scopeId = scope.Id;

      var datasetId = Guid.CreateVersion7();
      var planId = Guid.CreateVersion7();
      var mappingId = Guid.CreateVersion7();
      var adjustedId = Guid.CreateVersion7();
      var digest = new string('a', 64);

      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        SourceKind = "Raw", Revision = 1, LegalEntityKey = seed.ClientId.ToString("D"), Currency = "QAR",
        RawFileSha256Hex = digest, NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m,
        ImportedAt = DateTimeOffset.UtcNow, ImportedByUserId = seed.Staff.Id
      });

      db.AdjustmentPlans.Add(new AdjustmentPlan
      {
        Id = planId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        BaseDatasetId = datasetId, Status = "Finalized", ResultHash = digest,
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });

      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        DatasetId = datasetId, Version = 1, Generation = 1, TaxonomyVersion = "IFRS-2026",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
        CreatedByUserId = seed.Staff.Id, ApprovedByUserId = reviewer.Id, ApprovedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
      });

      db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
      {
        Id = adjustedId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        BaseDatasetId = datasetId, AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest,
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });

      // Seed validated financial package for seed.ClientId
      var package = new FinancialPackage
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        AdjustedDatasetId = adjustedId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
        Currency = "QAR", Framework = "IFRS", TaxonomyVersion = "IFRS-2026", TemplateVersion = "1.0",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
        Basis = "2026-01-01..2026-12-31", CalculationEngineVersion = "1.0", CalculationHash = digest,
        Status = AccountingPackageStates.PackageValidated, CreatedAt = DateTimeOffset.UtcNow
      };
      db.FinancialPackages.Add(package);
      packageId = package.Id;

      var artifactId = Guid.CreateVersion7();
      db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
      {
        Id = artifactId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        FinancialPackageId = package.Id, PackageRevision = 1, PackageGeneration = 1,
        PackageHash = digest, ArtifactVersion = "v1", FrameworkVersion = "IFRS", TemplateVersion = "1.0",
        ArtifactSha256Hex = digest, ArtifactBytes = [1, 2, 3], CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });

      // Add required review decisions so RequireCurrentAsync succeeds for package
      foreach (var stage in new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewStages.PartnerApproval })
      {
        db.FinancialPackageReviewDecisions.Add(new FinancialPackageReviewDecision
        {
          Id = Guid.CreateVersion7(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
          FinancialPackageId = package.Id, PackageRevision = 1, PackageGeneration = 1,
          PackageHash = package.CalculationHash, FinancialPackageArtifactId = artifactId,
          ArtifactVersion = "v1", ArtifactSha256Hex = digest,
          Stage = stage, Decision = FinancialPackageReviewDecisions.Approved,
          EvidenceMode = FinancialPackageReviewEvidenceModes.SignedIn, EvidenceReference = "EVID-01",
          DecidedByUserId = reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
        });
      }

      await db.SaveChangesAsync();
    }

    // Preparer client
    using var prepFactory = Factory(pg, seed.Staff);
    using var prepClient = prepFactory.CreateClient();
    var prepCsrf = await SignIn(prepClient);

    // 1. Get scope workspace
    var ws = await Read(prepClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.True(ws.GetProperty("canPrepare").GetBoolean());
    Assert.False(ws.GetProperty("canReview").GetBoolean());
    Assert.Equal("Alpha Group", ws.GetProperty("scope").GetProperty("groupName").GetString());
    Assert.Equal(0, ws.GetProperty("components").GetArrayLength());
    Assert.True(ws.GetProperty("eligiblePackages").GetArrayLength() >= 1);

    // 2. Submit component
    var submitRes = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/scopes/{scopeId}/components", new
    {
      clientId = seed.ClientId,
      engagementId = seed.EngagementId,
      packageId = packageId,
      ownershipPercent = 100,
      controlMethod = "CONTROLLED",
      periodBasis = "2026-01-01..2026-12-31",
      taxonomyVersion = "IFRS-2026",
      mappingVersion = (await Read(prepClient, $"/api/ui/consolidation/scopes/{scopeId}")).GetProperty("eligiblePackages")[0].GetProperty("mappingVersion").GetString()
    });
    Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);
    var submitJson = JsonDocument.Parse(await submitRes.Content.ReadAsStringAsync()).RootElement;
    var componentId = submitJson.GetProperty("value").GetGuid();

    // 3. Preparer tries to approve their own component -> rejected
    var selfApprove = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/components/{componentId}/approve", new { });
    Assert.True(selfApprove.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);

    // 4. Reviewer signs in and approves component -> succeeds
    using var revFactory = Factory(pg, reviewer);
    using var revClient = revFactory.CreateClient();
    var revCsrf = await SignIn(revClient);

    var revWs = await Read(revClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.True(revWs.GetProperty("canReview").GetBoolean());
    Assert.Equal(1, revWs.GetProperty("components").GetArrayLength());
    Assert.Equal("SUBMITTED", revWs.GetProperty("components")[0].GetProperty("status").GetString());

    var approveRes = await Post(revClient, revCsrf, $"/api/ui/consolidation/components/{componentId}/approve", new { });
    Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

    // 5. Verify component is now Approved
    var finalWs = await Read(revClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.Equal("APPROVED", finalWs.GetProperty("components")[0].GetProperty("status").GetString());
  }

  [Fact]
  public async Task ScopeWorkspace_EliminationJournals_CanCreateReturnResubmitAndApprove()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-JOURNALS");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid groupId, scopeId;
    AppUser reviewer;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      reviewer = new AppUser
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Email = "rev.journals@auditsphere.test",
        DisplayName = "Journal Reviewer", TenantId = "tenant-001", Subject = "sub-rev-journals"
      };
      db.Users.Add(reviewer);

      var group = new ClientGroup { Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Code = "GRP-BETA", Name = "Beta Group", Revision = 1 };
      db.ClientGroups.Add(group);
      groupId = group.Id;

      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        UserId = seed.Staff.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        UserId = reviewer.Id, Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });

      var scope = new ConsolidationScopeVersion
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        PeriodId = Guid.NewGuid(), Method = ConsolidationCalculator.RestrictedMethod,
        ReportingCurrency = "QAR", Status = AccountingWorkflowStates.Approved,
        OpeningBasis = "OPENING_BALANCE",
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow,
        GroupRevision = 1
      };
      db.ConsolidationScopeVersions.Add(scope);
      scopeId = scope.Id;

      await db.SaveChangesAsync();
    }

    using var prepFactory = Factory(pg, seed.Staff);
    using var prepClient = prepFactory.CreateClient();
    var prepCsrf = await SignIn(prepClient);

    // Unbalanced journal rejected
    var unbalanced = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/scopes/{scopeId}/journals", new
    {
      journalNumber = "EJ-001",
      journalType = "INTERCOMPANY_ELIMINATION",
      currency = "QAR",
      evidenceReference = "IC Agreement",
      lines = new[]
      {
        new { taxonomyCode = "1000", debit = 100, credit = 0, description = "Dr Cash" },
        new { taxonomyCode = "2000", debit = 0, credit = 90, description = "Cr Payables" }
      }
    });
    Assert.Equal(HttpStatusCode.BadRequest, unbalanced.StatusCode);

    // Balanced journal created
    var createRes = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/scopes/{scopeId}/journals", new
    {
      journalNumber = "EJ-001",
      journalType = "INTERCOMPANY_ELIMINATION",
      currency = "QAR",
      evidenceReference = "IC Agreement Ref 2026",
      lines = new[]
      {
        new { taxonomyCode = "1000", debit = 100, credit = 0, description = "Dr Cash" },
        new { taxonomyCode = "2000", debit = 0, credit = 100, description = "Cr Payables" }
      }
    });
    Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);
    var journalId = JsonDocument.Parse(await createRes.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();

    // Reviewer signs in
    using var revFactory = Factory(pg, reviewer);
    using var revClient = revFactory.CreateClient();
    var revCsrf = await SignIn(revClient);

    // Return journal with reason
    var returnRes = await Post(revClient, revCsrf, $"/api/ui/consolidation/journals/{journalId}/return", new
    {
      reason = "Please attach intercompany signed statement"
    });
    Assert.Equal(HttpStatusCode.OK, returnRes.StatusCode);

    var wsReturned = await Read(revClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.Equal("Returned", wsReturned.GetProperty("journals")[0].GetProperty("status").GetString());
    Assert.Equal("Please attach intercompany signed statement", wsReturned.GetProperty("journals")[0].GetProperty("returnReason").GetString());

    // Preparer resubmits journal
    var resubmitRes = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/journals/{journalId}/resubmit", new { });
    Assert.Equal(HttpStatusCode.OK, resubmitRes.StatusCode);

    // Reviewer approves journal
    var approveRes = await Post(revClient, revCsrf, $"/api/ui/consolidation/journals/{journalId}/approve", new { });
    Assert.Equal(HttpStatusCode.OK, approveRes.StatusCode);

    var wsApproved = await Read(revClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.Equal("APPROVED", wsApproved.GetProperty("journals")[0].GetProperty("status").GetString());
  }

  [Fact]
  public async Task AdvancedConsolidation_GetWorkspace_AndSubmitSchedule()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-ADVANCED");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid groupId, scopeId;

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var group = new ClientGroup { Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Code = "GRP-ADV", Name = "Advanced Group", Revision = 1 };
      db.ClientGroups.Add(group);
      groupId = group.Id;

      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        UserId = seed.Staff.Id, Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });

      var scope = new ConsolidationScopeVersion
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = group.Id,
        PeriodId = Guid.NewGuid(), Method = AdvancedConsolidationMethods.AcquisitionNci,
        ReportingCurrency = "QAR", Status = AccountingWorkflowStates.Draft,
        OpeningBasis = "OPENING_BALANCE",
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow,
        GroupRevision = 1
      };
      db.ConsolidationScopeVersions.Add(scope);
      await db.SaveChangesAsync();
      scopeId = scope.Id;
    }

    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);

    // Read workspace
    var ws = await Read(client, $"/api/ui/consolidation/advanced/{scopeId}");
    Assert.Equal(scopeId, ws.GetProperty("scope").GetProperty("id").GetGuid());
    Assert.Equal("Advanced Group", ws.GetProperty("scope").GetProperty("groupName").GetString());
    Assert.Equal(AdvancedConsolidationMethods.AcquisitionNci, ws.GetProperty("scope").GetProperty("method").GetString());
    Assert.True(ws.GetProperty("canPrepare").GetBoolean());
    Assert.False(ws.GetProperty("canReview").GetBoolean());

    // Submit schedule
    var sourceManifest = JsonSerializer.Serialize(new
    {
      sources = new[]
      {
        new { componentId = Guid.NewGuid(), kind = "Package", id = Guid.NewGuid(), hash = new string('a', 64) }
      }
    });
    var inputSnapshot = JsonSerializer.Serialize(new
    {
      targetOwnership = 0.8m,
      statementLines = new[]
      {
        new { taxonomyCode = "1000", comparativeAmount = 100m, currentAmount = 120m }
      }
    });

    var submitRes = await Post(client, csrf, $"/api/ui/consolidation/advanced/{scopeId}/schedules", new
    {
      sourceManifestJson = sourceManifest,
      inputSnapshotJson = inputSnapshot
    });
    Assert.Equal(HttpStatusCode.OK, submitRes.StatusCode);
    var scheduleId = JsonDocument.Parse(await submitRes.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();

    // Re-read workspace to verify schedule appears
    var wsAfter = await Read(client, $"/api/ui/consolidation/advanced/{scopeId}");
    var schedules = wsAfter.GetProperty("schedules");
    Assert.Equal(1, schedules.GetArrayLength());
    Assert.Equal(scheduleId, schedules[0].GetProperty("id").GetGuid());
    Assert.Equal("SUBMITTED", schedules[0].GetProperty("status").GetString());
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:TenantId"] = u.TenantId,
    ["DevelopmentIdentity:Subject"] = u.Subject,
    ["Application:AllowSimulationAdapters"] = "true",
    ["ExternalEffects:Enabled"] = "false"
  });

  private static async Task<string> SignIn(HttpClient c)
  {
    await c.GetAsync("/auth/sign-in");
    using var r = await c.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static async Task<JsonElement> Read(HttpClient c, string url)
  {
    using var r = await c.GetAsync(url);
    Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
  }

  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object body)
  {
    var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    r.Headers.Add("X-XSRF-TOKEN", csrf);
    return c.SendAsync(r);
  }
}
