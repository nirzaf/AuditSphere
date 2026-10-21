using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Documents;
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
    var csrf = await SignIn(client);
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
    AssertHomeActionFlags(responseBody, requestId, canUpload: false, canReply: true);
    Assert.Equal(reason, item.ClarificationReason);
    Assert.False(item.ClarificationReasonTruncated);
    Assert.DoesNotContain(workspace.Requests, x => x.Id == foreignRequestId);
    Assert.NotNull(workspace.AsOfDateUtc);
    Assert.False(workspace.HasMorePackages);

    using var reply = await Post(client, csrf, $"/api/ui/portal/requests/{requestId}/reply",
      new { Body = "The clarified item is ready for review." });
    Assert.Equal(HttpStatusCode.OK, reply.StatusCode);
    using (var resubmittedResponse = await client.GetAsync("/api/ui/portal?page=0&pageSize=50"))
    {
      Assert.Equal(HttpStatusCode.OK, resubmittedResponse.StatusCode);
      var resubmittedBody = await resubmittedResponse.Content.ReadAsStringAsync();
      var resubmittedWorkspace = JsonSerializer.Deserialize<PortalWorkspace>(resubmittedBody,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
      var resubmittedItem = Assert.Single(resubmittedWorkspace!.Requests, x => x.Id == requestId);
      Assert.Equal(PbcStates.Resubmitted, resubmittedItem.State);
      Assert.False(resubmittedItem.CanWrite);
      AssertHomeActionFlags(resubmittedBody, requestId, canUpload: true, canReply: true);
    }
    using (var resubmittedDetailResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}"))
    {
      Assert.Equal(HttpStatusCode.OK, resubmittedDetailResponse.StatusCode);
      var resubmittedDetailBody = await resubmittedDetailResponse.Content.ReadAsStringAsync();
      var resubmittedDetail = JsonSerializer.Deserialize<PortalRequestWorkspace>(resubmittedDetailBody,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
      Assert.Equal(PbcStates.Resubmitted, resubmittedDetail!.State);
      Assert.True(resubmittedDetail.CanWrite);
      AssertDetailActionFlags(resubmittedDetailBody, canUpload: true, canReply: true);
    }
    await using (var afterReply = new AuditSphereDbContext(pg.Options))
    {
      var persisted = await afterReply.PbcRequests.AsNoTracking().SingleAsync(x => x.Id == requestId);
      Assert.Equal(PbcStates.Resubmitted, persisted.State);
      revision = persisted.Revision;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s
        .SetProperty(x => x.State, PbcStates.Sent)
        .SetProperty(x => x.ClarificationReason, (string?)null));
    using var sentResponse = await client.GetAsync("/api/ui/portal?page=0&pageSize=50");
    Assert.Equal(HttpStatusCode.OK, sentResponse.StatusCode);
    var sentBody = await sentResponse.Content.ReadAsStringAsync();
    var sentWorkspace = JsonSerializer.Deserialize<PortalWorkspace>(sentBody,
      new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var sentItem = Assert.Single(sentWorkspace!.Requests, x => x.Id == requestId);
    Assert.True(sentItem.CanWrite);
    Assert.Null(sentItem.ClarificationReason);
    Assert.False(sentItem.ClarificationReasonTruncated);
    AssertHomeActionFlags(sentBody, requestId, canUpload: true, canReply: true);

    using var foreignRequest = await client.GetAsync($"/api/ui/portal/requests/{foreignRequestId}");
    Assert.NotEqual(HttpStatusCode.OK, foreignRequest.StatusCode);
    await using var afterRead = new AuditSphereDbContext(pg.Options);
    Assert.Equal(revision, await afterRead.PbcRequests.Where(x => x.Id == requestId).Select(x => x.Revision).SingleAsync());
    Assert.Empty(await afterRead.PbcUploadIntents.Where(x => x.PbcRequestId == requestId).ToListAsync());
  }

  [Fact]
  public async Task PortalWorkspaceContinuesValidatedPackageHistoryBeyondTheFormerHundredRowLimit()
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

    using var response = await client.GetAsync("/api/ui/portal?page=0&pageSize=50&packagePage=0&packagePageSize=50");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var workspace = await response.Content.ReadFromJsonAsync<PortalWorkspace>();
    Assert.NotNull(workspace);
    Assert.Equal(50, workspace.Packages.Count);
    Assert.True(workspace.HasMorePackages);
    Assert.Equal(0, workspace.PackagePage);
    Assert.Equal(50, workspace.PackagePageSize);

    using var olderResponse = await client.GetAsync("/api/ui/portal?page=0&pageSize=50&packagePage=2&packagePageSize=50");
    Assert.Equal(HttpStatusCode.OK, olderResponse.StatusCode);
    var older = await olderResponse.Content.ReadFromJsonAsync<PortalWorkspace>();
    Assert.NotNull(older);
    Assert.Equal(2, older.PackagePage);
    Assert.Single(older.Packages);
    Assert.False(older.HasMorePackages);
    Assert.DoesNotContain(older.Packages, item => workspace.Packages.Any(firstPage => firstPage.Id == item.Id));
  }

  [Fact]
  public async Task PortalRequestHistoryPagesAreBoundedOrderedAndRemainParticipantScoped()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-REQUEST-HISTORY");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var requestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, f,
      PbcSeed.Actor(f.Staff, "Staff"), PbcSeed.Actor(f.Client, "ClientUser"));
    var foreignRequestId = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, foreign,
      PbcSeed.Actor(foreign.Staff, "Staff"), PbcSeed.Actor(foreign.Client, "ClientUser"));
    AppUser clientUser;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      clientUser = await db.Users.SingleAsync(x => x.FirmId == f.FirmId && x.UserKind == "Client" && !x.Disabled);
      var request = await db.PbcRequests.SingleAsync(x => x.Id == requestId);
      request.ClientOwnerUserId = clientUser.Id;
      var occurred = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
      db.PbcCommunications.AddRange(Enumerable.Range(0, 2001).Select(index => new PbcCommunication
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = requestId, AuthorUserId = f.Staff.Id, Kind = PbcCommunicationKinds.ClientMessage,
        Body = $"Message {index:D4}", CreatedAt = occurred.AddMinutes(index)
      }));
      db.PbcUploadIntents.AddRange(Enumerable.Range(0, 501).Select(index => new PbcUploadIntent
      {
        Id = Guid.CreateVersion7(), FirmId = f.FirmId, ClientId = f.ClientId, EngagementId = f.EngagementId,
        PbcRequestId = requestId, UploaderUserId = clientUser.Id, FileName = $"upload-{index:D3}.pdf",
        ContentType = "application/pdf", DeclaredByteCount = 1, DeclaredSha256Hex = new string('a', 64),
        CapabilityHash = new string('b', 64), ReceivedByteCount = 0, State = PbcUploadStates.Started,
        ExpiresAt = occurred.AddMinutes(index).AddDays(1), CreatedAt = occurred.AddMinutes(index), Revision = 1
      }));
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, clientUser);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var summaryResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}");
    Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
    var summary = await summaryResponse.Content.ReadFromJsonAsync<PortalRequestWorkspace>();
    Assert.NotNull(summary);
    Assert.Equal(25, summary.Uploads.Count);
    Assert.Equal(25, summary.Conversation.Count);
    Assert.True(summary.HasMoreUploads);
    Assert.True(summary.HasMoreConversation);

    using var pageResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}/history?uploadPage=1&conversationPage=2&pageSize=10");
    Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
    var page = await pageResponse.Content.ReadFromJsonAsync<PortalRequestHistory>();
    Assert.NotNull(page);
    Assert.Equal(requestId, page.RequestId);
    Assert.Equal(1, page.UploadPage);
    Assert.Equal(2, page.ConversationPage);
    Assert.Equal(10, page.PageSize);
    Assert.Equal(10, page.Uploads.Count);
    Assert.Equal("upload-490.pdf", page.Uploads[0].FileName);
    Assert.Equal("upload-481.pdf", page.Uploads[^1].FileName);
    Assert.Equal(10, page.Conversation.Count);
    Assert.Equal("Message 0020", page.Conversation[0].Body);
    Assert.Equal("Message 0029", page.Conversation[^1].Body);
    Assert.True(page.HasMoreUploads);
    Assert.True(page.HasMoreConversation);

    using var lastResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}/history?uploadPage=50&conversationPage=200&pageSize=10");
    Assert.Equal(HttpStatusCode.OK, lastResponse.StatusCode);
    var last = await lastResponse.Content.ReadFromJsonAsync<PortalRequestHistory>();
    Assert.NotNull(last);
    Assert.Single(last.Uploads);
    Assert.Equal("upload-000.pdf", last.Uploads[0].FileName);
    // The fixture also retains the original request and client acknowledgement.
    Assert.Equal(new[] { "Message 2000", "Bank statements", "The client acknowledged this request." },
      last.Conversation.Select(message => message.Body));
    Assert.False(last.HasMoreUploads);
    Assert.False(last.HasMoreConversation);

    using var foreignResponse = await client.GetAsync($"/api/ui/portal/requests/{foreignRequestId}/history?uploadPage=0&conversationPage=0&pageSize=10");
    Assert.NotEqual(HttpStatusCode.OK, foreignResponse.StatusCode);
    using var invalidPage = await client.GetAsync($"/api/ui/portal/requests/{requestId}/history?uploadPage=-1&conversationPage=0&pageSize=10");
    Assert.NotEqual(HttpStatusCode.OK, invalidPage.StatusCode);
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
    var stateCases = new (string State, bool CanWrite, bool CanUpload, bool CanReply, bool DetailCanWrite)[]
    {
      (PbcStates.Sent, true, true, true, true),
      (PbcStates.Acknowledged, true, true, true, true),
      (PbcStates.PartiallyReceived, true, true, true, true),
      (PbcStates.Resubmitted, false, true, true, true),
      (PbcStates.Received, false, false, true, true),
      (PbcStates.UnderReview, false, false, true, true),
      (PbcStates.ClarificationRequired, true, false, true, true),
      (PbcStates.Accepted, false, false, false, false),
      (PbcStates.Closed, false, false, false, false),
    };
    // These direct state writes are read-side projections for the state matrix; the clarification reply above
    // separately exercises the real command transition to RESUBMITTED.
    foreach (var (state, canWrite, canUpload, canReply, detailCanWrite) in stateCases)
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
      using var workspaceResponse = await client.GetAsync("/api/ui/portal?page=0&pageSize=50");
      Assert.Equal(HttpStatusCode.OK, workspaceResponse.StatusCode);
      var workspaceBody = await workspaceResponse.Content.ReadAsStringAsync();
      var workspace = JsonSerializer.Deserialize<PortalWorkspace>(workspaceBody,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
      var item = Assert.Single(workspace!.Requests, x => x.Id == requestId);
      Assert.Equal(canWrite, item.CanWrite);
      Assert.Null(item.ClarificationReason);
      Assert.False(item.ClarificationReasonTruncated);
      AssertHomeActionFlags(workspaceBody, requestId, canUpload, canReply);
      // Legacy state/timestamps alone must not manufacture exact reviewer acceptance evidence.
      using var detailResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}");
      Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
      var detailBody = await detailResponse.Content.ReadAsStringAsync();
      var detail = JsonSerializer.Deserialize<PortalRequestWorkspace>(detailBody,
        new JsonSerializerOptions(JsonSerializerDefaults.Web));
      Assert.NotNull(detail);
      Assert.Equal(detailCanWrite, detail.CanWrite);
      Assert.Null(detail.Acceptance);
      AssertDetailActionFlags(detailBody, canUpload, canReply);

      if (state is PbcStates.UnderReview or PbcStates.Received or PbcStates.ClarificationRequired)
      {
        Assert.True(workspace.FirstSignIn.Completed);
        var stateUploadCount = await CountUploads(pg, requestId);
        using var stateUploadResponse = await Post(client, csrf, $"/api/ui/portal/requests/{requestId}/uploads", new
        {
          FileName = "eligibility-probe.pdf", ContentType = "application/pdf", ByteCount = "1", Sha256 = new string('a', 64)
        });
        Assert.NotEqual(HttpStatusCode.OK, stateUploadResponse.StatusCode);
        Assert.Equal(stateUploadCount, await CountUploads(pg, requestId));
      }
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

  private static void AssertHomeActionFlags(string body, Guid requestId, bool canUpload, bool canReply)
  {
    using var json = JsonDocument.Parse(body);
    var request = json.RootElement.GetProperty("requests").EnumerateArray()
      .Single(x => x.GetProperty("id").GetGuid() == requestId);
    Assert.Equal(canUpload, request.GetProperty("canUpload").GetBoolean());
    Assert.Equal(canReply, request.GetProperty("canReply").GetBoolean());
  }

  private static void AssertDetailActionFlags(string body, bool canUpload, bool canReply)
  {
    using var json = JsonDocument.Parse(body);
    Assert.Equal(canUpload, json.RootElement.GetProperty("canUpload").GetBoolean());
    Assert.Equal(canReply, json.RootElement.GetProperty("canReply").GetBoolean());
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
