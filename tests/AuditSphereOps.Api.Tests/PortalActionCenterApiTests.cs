using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class PortalActionCenterApiTests
{
  [Fact]
  public async Task PortalWorkspaceReturnsParticipantScopedClarificationAndCurrentEligibility()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-ACTION-CENTER");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, f,
      PbcSeed.Actor(f.Staff, "Staff"), PbcSeed.Actor(f.Client, "ClientUser"));
    var foreignRequestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, foreign,
      PbcSeed.Actor(foreign.Staff, "Staff"), PbcSeed.Actor(foreign.Client, "ClientUser"));
    AppUser clientUser;
    long revision;
    var reason = new string('R', 2000);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      clientUser = await db.Users.SingleAsync(x => x.FirmId == f.Admin.FirmId && x.UserKind == "Client" && !x.Disabled);
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.ClientOwnerUserId = clientUser.Id;
      request.State = PbcStates.ClarificationRequired;
      request.ClarificationReason = reason;
      request.AcceptanceCriteria = "Staff-only review note must not appear in the portal projection.";
      requestId = request.Id;
      revision = request.Revision;

      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, clientUser);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var response = await client.GetAsync("/api/ui/portal?page=0&pageSize=50");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var responseBody = await response.Content.ReadAsStringAsync();
    Assert.DoesNotContain("Staff-only review note", responseBody, StringComparison.Ordinal);
    var workspace = System.Text.Json.JsonSerializer.Deserialize<PortalWorkspace>(responseBody,
      new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
    Assert.NotNull(workspace);
    var item = Assert.Single(workspace.Requests, x => x.Id == requestId);
    Assert.True(workspace.FirstSignIn.Completed);
    Assert.True(item.CanWrite);
    Assert.Equal(reason, item.ClarificationReason);
    Assert.False(item.ClarificationReasonTruncated);
    Assert.DoesNotContain(workspace.Requests, x => x.Id == foreignRequestId);
    Assert.NotNull(workspace.AsOfDateUtc);
    Assert.False(workspace.HasMorePackages);

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s
        .SetProperty(x => x.State, PbcStates.Sent)
        .SetProperty(x => x.ClarificationReason, (string?)null));
    var sentWorkspace = await client.GetFromJsonAsync<PortalWorkspace>("/api/ui/portal?page=0&pageSize=50");
    var sentItem = Assert.Single(sentWorkspace!.Requests, x => x.Id == requestId);
    Assert.True(sentItem.CanWrite);
    Assert.Null(sentItem.ClarificationReason);
    Assert.False(sentItem.ClarificationReasonTruncated);

    using var foreignRequest = await client.GetAsync($"/api/ui/portal/requests/{foreignRequestId}");
    Assert.NotEqual(HttpStatusCode.OK, foreignRequest.StatusCode);
    await using var afterRead = new AuditSphereDbContext(pg.Options);
    Assert.Equal(revision, await afterRead.PbcRequests.Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync());
    Assert.Empty(await afterRead.PbcUploadIntents.Where(x => x.PbcRequestId == requestId).ToListAsync());
  }

  [Fact]
  public async Task PortalWorkspaceCapsValidatedPackageSummariesAtOneHundredAndReportsMore()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-PACKAGE-PAGING");
    var f = await PbcSeed.SeedAsync(pg);
    AppUser clientUser;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      clientUser = await db.Users.SingleAsync(x => x.FirmId == f.FirmId && x.UserKind == "Client" && !x.Disabled);

      var digest = new string('a', 64);
      var datasetId = Guid.CreateVersion7();
      var planId = Guid.CreateVersion7();
      var mappingId = Guid.CreateVersion7();
      var adjustedId = Guid.CreateVersion7();
      var createdAt = DateTimeOffset.UtcNow;

      db.TrialBalanceDatasets.Add(new TrialBalanceDataset
      {
        Id = datasetId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        SourceKind = "Raw", Revision = 1, LegalEntityKey = f.ClientId.ToString("D"), Currency = "QAR",
        RawFileSha256Hex = digest, NormalizedDatasetDigest = digest, Sha256Hex = digest, Balanced = true,
        ValidationStatus = "Accepted", ImportState = TrialBalanceImportStates.Sealed, ControlTotal = 0m,
        ImportedAt = createdAt, ImportedByUserId = f.Staff.Id
      });
      db.AdjustmentPlans.Add(new AdjustmentPlan
      {
        Id = planId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        BaseDatasetId = datasetId, Status = "Finalized", ResultHash = digest,
        CreatedByUserId = f.Staff.Id, CreatedAt = createdAt
      });
      db.MappingVersions.Add(new MappingVersion
      {
        Id = mappingId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        DatasetId = datasetId, Version = 1, Generation = 1, TaxonomyVersion = "IFRS-2026",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Status = AccountingPackageStates.MappingApproved,
        CreatedByUserId = f.Staff.Id, ApprovedByUserId = f.Admin.Id, ApprovedAt = createdAt,
        CreatedAt = createdAt
      });
      db.AdjustedTrialBalanceSnapshots.Add(new AdjustedTrialBalanceSnapshot
      {
        Id = adjustedId, FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        BaseDatasetId = datasetId, AdjustmentPlanId = planId, Currency = "QAR", ResultHash = digest,
        CreatedByUserId = f.Staff.Id, CreatedAt = createdAt
      });
      db.FinancialPackages.AddRange(Enumerable.Range(0, 101).Select(index => new FinancialPackage
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        AdjustedDatasetId = adjustedId, MappingVersionId = mappingId, AdjustmentPlanId = planId,
        Currency = "QAR", Framework = "IFRS", TaxonomyVersion = "IFRS-2026", TemplateVersion = $"portal-{index:D3}",
        PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", Basis = "2026-01-01..2026-12-31",
        CalculationEngineVersion = "1.0", CalculationHash = digest,
        Status = AccountingPackageStates.PackageValidated, CreatedAt = createdAt.AddTicks(index)
      }));
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, clientUser);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);

    using var response = await client.GetAsync("/api/ui/portal?page=0&pageSize=50");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var workspace = await response.Content.ReadFromJsonAsync<PortalWorkspace>();
    Assert.NotNull(workspace);
    Assert.Equal(100, workspace.Packages.Count);
    Assert.True(workspace.HasMorePackages);
  }

  [Fact]
  public async Task PortalActionEligibilityFailsClosedForReviewStatesAndUploadRouteStillRequiresFirstSignIn()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-ACTION-GATES");
    var f = await PbcSeed.SeedAsync(pg);
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, f,
      PbcSeed.Actor(f.Staff, "Staff"), PbcSeed.Actor(f.Client, "ClientUser"));
    AppUser clientUser;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      clientUser = await db.Users.SingleAsync(x => x.FirmId == f.Admin.FirmId && x.UserKind == "Client" && !x.Disabled);
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.ClientOwnerUserId = clientUser.Id;
      request.State = PbcStates.Sent;
      request.ClarificationReason = null;
      requestId = request.Id;
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, clientUser);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    foreach (var state in new[] { PbcStates.Accepted, PbcStates.Closed, PbcStates.UnderReview, PbcStates.Resubmitted })
    {
      await using (var db = new AuditSphereDbContext(pg.Options))
      {
        var acceptedAt = state == PbcStates.Accepted ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
        var acceptedBy = state == PbcStates.Accepted ? clientUser.Id : (Guid?)null;
        await db.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s
          .SetProperty(x => x.State, state)
          .SetProperty(x => x.AcceptedAt, acceptedAt)
          .SetProperty(x => x.AcceptedByUserId, acceptedBy));
      }
      var workspace = await client.GetFromJsonAsync<PortalWorkspace>("/api/ui/portal?page=0&pageSize=50");
      var item = Assert.Single(workspace!.Requests, x => x.Id == requestId);
      Assert.False(item.CanWrite);
      Assert.Null(item.ClarificationReason);
      Assert.False(item.ClarificationReasonTruncated);
    }

    var notOnboarded = PbcSeed.User(f.FirmId, "Client");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(notOnboarded);
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, notOnboarded, "ClientUser", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
    }
    var notOnboardedRequestId = await CreateSentRequestForClientAsync(pg, f, notOnboarded.Id);
    using var notOnboardedFactory = Factory(pg, notOnboarded);
    using var notOnboardedClient = notOnboardedFactory.CreateClient(new() { AllowAutoRedirect = false });
    var notOnboardedCsrf = await SignIn(notOnboardedClient);
    var withoutSignIn = await notOnboardedClient.GetFromJsonAsync<PortalWorkspace>("/api/ui/portal?page=0&pageSize=50");
    Assert.False(Assert.Single(withoutSignIn!.Requests, x => x.Id == notOnboardedRequestId).CanWrite);

    var before = await CountUploads(pg, notOnboardedRequestId);
    using var upload = await Post(notOnboardedClient, notOnboardedCsrf, $"/api/ui/portal/requests/{notOnboardedRequestId}/uploads", new
    {
      FileName = "requested.pdf", ContentType = "application/pdf", ByteCount = "1", Sha256 = new string('a', 64)
    });
    Assert.NotEqual(HttpStatusCode.OK, upload.StatusCode);
    Assert.Equal(before, await CountUploads(pg, notOnboardedRequestId));
  }

  [Fact]
  public async Task PortalDelegatedParticipantLosesRequestImmediatelyWhenDelegationIsRevoked()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-ACTION-DELEGATION");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, f,
      PbcSeed.Actor(f.Staff, "Staff"), PbcSeed.Actor(f.Client, "ClientUser"));
    var foreignRequestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, foreign,
      PbcSeed.Actor(foreign.Staff, "Staff"), PbcSeed.Actor(foreign.Client, "ClientUser"));
    AppUser delegateUser;
    Guid delegationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      delegateUser = await db.Users.SingleAsync(x => x.FirmId == f.Admin.FirmId && x.UserKind == "Client" && !x.Disabled);
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.ClientOwnerUserId = f.Admin.Id;
      request.State = PbcStates.Sent;
      delegationId = Guid.NewGuid();
      db.PbcRequestDelegations.Add(new()
      {
        Id = delegationId, FirmId = f.Admin.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = requestId, DelegatorUserId = f.Admin.Id, DelegateUserId = delegateUser.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, delegateUser);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    var assigned = await client.GetFromJsonAsync<PortalWorkspace>("/api/ui/portal?page=0&pageSize=50");
    var delegatedItem = Assert.Single(assigned!.Requests, x => x.Id == requestId);
    Assert.True(delegatedItem.Delegated);
    Assert.DoesNotContain(assigned.Requests, x => x.Id == foreignRequestId);

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.PbcRequestDelegations.Where(x => x.Id == delegationId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)
          .SetProperty(x => x.RevokedByUserId, f.Admin.Id));
    var afterRevocation = await client.GetFromJsonAsync<PortalWorkspace>("/api/ui/portal?page=0&pageSize=50");
    Assert.DoesNotContain(afterRevocation!.Requests, x => x.Id == requestId);
  }

  private static async Task<Guid> CreateSentRequestForClientAsync(
    OwnedPostgresDatabase pg, PbcSeed.Fixture fixture, Guid clientOwnerUserId)
  {
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"), new CreatePbcRequestRequest(
        fixture.EngagementId, "No-sign-in upload gate", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF or CSV", "12 months", clientOwnerUserId, fixture.Staff.Id, fixture.Reviewer.Id,
        "2027-01-31", "Confidential", "Synthetic request for the portal sign-in gate."));
      Assert.True(created.Succeeded, created.Message);
      requestId = created.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var sent = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"),
        new PbcStateChangeRequest(requestId, PbcStates.Sent, 1));
      Assert.True(sent.Succeeded, sent.Message);
    }
    return requestId;
  }

  [Fact]
  public async Task PortalPagingAndSessionRevocationRemainFailClosed()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-ACTION-SESSION");
    var f = await PbcSeed.SeedAsync(pg);
    AppUser clientUser;
    await using (var db = new AuditSphereDbContext(pg.Options))
      clientUser = await db.Users.SingleAsync(x => x.FirmId == f.Admin.FirmId && x.UserKind == "Client" && !x.Disabled);

    using var factory = Factory(pg, clientUser);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var invalidPage = await client.GetAsync("/api/ui/portal?page=-1&pageSize=50");
    Assert.NotEqual(HttpStatusCode.OK, invalidPage.StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == clientUser.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    using var revoked = await client.GetAsync("/api/ui/portal?page=0&pageSize=50");
    Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);

    using var nonClientFactory = Factory(pg, f.Admin);
    using var nonClient = nonClientFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(nonClient);
    using var missingClientIdentity = await nonClient.GetAsync("/api/ui/portal?page=0&pageSize=50");
    Assert.NotEqual(HttpStatusCode.OK, missingClientIdentity.StatusCode);
  }

  private static async Task<int> CountUploads(OwnedPostgresDatabase pg, Guid requestId)
  {
    await using var db = new AuditSphereDbContext(pg.Options);
    return await db.PbcUploadIntents.CountAsync(x => x.PbcRequestId == requestId);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
    ["DevelopmentIdentity:Enabled"] = "true",
    ["DevelopmentIdentity:Subject"] = user.Subject,
    ["DevelopmentIdentity:TenantId"] = user.TenantId,
    ["ExternalEffects:Enabled"] = "false",
    ["Application:AllowSimulationAdapters"] = "true"
  });

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal))
      .Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string path, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }
}
