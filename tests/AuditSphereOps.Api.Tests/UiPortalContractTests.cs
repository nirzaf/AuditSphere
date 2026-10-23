using System.Net;
using System.Net.Http.Json;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class UiPortalContractTests
{
  private static StandaloneApiApplicationFactory Factory(string connection, string subject, string tenant) => new(new Dictionary<string, string?>
  {
    ["ConnectionStrings:AuditSphere"] = connection, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:Subject"] = subject,
    ["DevelopmentIdentity:TenantId"] = tenant, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
  });
  private static async Task<string> SignInAsync(HttpClient client)
  {
    Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/auth/sign-in?returnUrl=%2Fui%2Fportal")).StatusCode);
    var response = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return Uri.UnescapeDataString(response.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith("XSRF-TOKEN=")).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }
  private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string? proof = null)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
    if (proof is not null) request.Headers.Add("X-XSRF-TOKEN", proof);
    return await client.SendAsync(request);
  }

  [Fact]
  public async Task PortalContracts_RequireCurrentParticipantAndCsrf_AndNeverReturnCapabilityInReads()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-PORTAL-API");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staff = PbcSeed.Actor(seed.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(seed.EngagementId, "Bank statements", "TEST", "2026-01-01", "2026-12-31", "Bank evidence", "PDF", "Totals",
        seed.Client.Id, seed.Staff.Id, seed.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message); requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }
    using var clientFactory = Factory(pg.ConnectionString, seed.Client.Subject, seed.Client.TenantId);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/ui/portal")).StatusCode);
    var csrf = await SignInAsync(client);
    var workspace = await client.GetFromJsonAsync<JsonElement>("/api/ui/portal?page=0&pageSize=10");
    Assert.Equal(requestId, workspace.GetProperty("requests")[0].GetProperty("id").GetGuid());
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/ui/portal?page=0&pageSize=20")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/portal/requests/{Guid.NewGuid()}")).StatusCode);
    var replyOperationId = Guid.CreateVersion7();
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, $"/api/ui/portal/requests/{requestId}/reply",
      new { requestId = replyOperationId, body = "Ready for review" })).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await PostAsync(client, $"/api/ui/portal/requests/{requestId}/reply",
      new { requestId = replyOperationId, body = "Ready for review" }, csrf)).StatusCode);
    var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ui/portal/requests/{requestId}");
    Assert.Contains(detail.GetProperty("conversation").EnumerateArray(), x => x.GetProperty("body").GetString() == "Ready for review");
    using var started = await PostAsync(client, $"/api/ui/portal/requests/{requestId}/uploads", new { fileName = "statement.txt", contentType = "text/plain", byteCount = "1", sha256 = new string('a', 64) }, csrf);
    Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    var receipt = await started.Content.ReadFromJsonAsync<JsonElement>();
    Assert.False(string.IsNullOrWhiteSpace(receipt.GetProperty("value").GetProperty("capability").GetString()));
    detail = await client.GetFromJsonAsync<JsonElement>($"/api/ui/portal/requests/{requestId}");
    Assert.Single(detail.GetProperty("uploads").EnumerateArray());
    Assert.DoesNotContain("capability", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("stagedPath", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, $"/api/ui/portal/requests/{requestId}/delegations", new { userId = seed.Admin.Id }, csrf)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/ui/portal/accounting/packages/{Guid.NewGuid()}")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/ui/portal/documents")).StatusCode);
    using var staffFactory = Factory(pg.ConnectionString, seed.Staff.Subject, seed.Staff.TenantId);
    using var staffClient = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(staffClient);
    Assert.Equal(HttpStatusCode.Forbidden, (await staffClient.GetAsync("/api/ui/portal")).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == seed.Client.Id).ExecuteUpdateAsync(x => x.SetProperty(u => u.Disabled, true).SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
      Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/ui/portal/requests/{requestId}")).StatusCode);
  }

  [Fact]
  public async Task PortalRequestDetailAndHistory_ReturnCurrentParticipantEvidence()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-REQUEST-READ-POSITIVE");
    var seed = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateSentRequestAsync(pg, seed);
    var candidateId = await AddPrimaryContactAndCandidateAsync(pg, seed);

    using var factory = Factory(pg.ConnectionString, seed.Client.Subject, seed.Client.TenantId);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignInAsync(client);

    using var detailResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}");
    Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    var detail = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(requestId, detail.GetProperty("id").GetGuid());
    Assert.Equal("SENT", detail.GetProperty("state").GetString());
    Assert.Equal("2", detail.GetProperty("revision").GetString());
    Assert.True(detail.GetProperty("canWrite").GetBoolean());
    Assert.True(detail.GetProperty("canDelegate").GetBoolean());
    Assert.Contains(detail.GetProperty("candidates").EnumerateArray(), candidate =>
      candidate.GetProperty("userId").GetGuid() == candidateId);
    Assert.Contains(detail.GetProperty("conversation").EnumerateArray(),
      message => message.GetProperty("body").GetString() is { } body && body.Contains("Bank statements", StringComparison.Ordinal));

    using var historyResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}/history");
    Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
    var history = await historyResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(requestId, history.GetProperty("requestId").GetGuid());
    Assert.Contains(history.GetProperty("conversation").EnumerateArray(),
      message => message.GetProperty("body").GetString() is { } body && body.Contains("Bank statements", StringComparison.Ordinal));

    Guid delegationId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var delegated = await ClientPortalService.DelegateRequestAsync(db,
        PbcSeed.Actor(seed.Client, "ClientUser"), requestId, candidateId);
      Assert.True(delegated.Succeeded, delegated.ErrorCode);
      delegationId = delegated.Value;
    }

    // Projection-only terminal-state fixture; this does not record a professional approval.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var closed = await db.PbcRequests.Where(x => x.FirmId == seed.FirmId && x.ClientId == seed.ClientId &&
          x.EngagementId == seed.EngagementId && x.Id == requestId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, PbcStates.Closed)
          .SetProperty(x => x.Revision, x => x.Revision + 1));
      Assert.Equal(1, closed);
    }

    using var closedResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}");
    Assert.Equal(HttpStatusCode.OK, closedResponse.StatusCode);
    var closedDetail = await closedResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("CLOSED", closedDetail.GetProperty("state").GetString());
    Assert.False(closedDetail.GetProperty("canWrite").GetBoolean());
    Assert.False(closedDetail.GetProperty("canDelegate").GetBoolean());
    Assert.True(closedDetail.GetProperty("canManageDelegations").GetBoolean());
    Assert.Empty(closedDetail.GetProperty("candidates").EnumerateArray());
    Assert.Contains(closedDetail.GetProperty("delegations").EnumerateArray(), delegation =>
      delegation.GetProperty("id").GetGuid() == delegationId);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var blocked = await ClientPortalService.DelegateRequestAsync(db,
        PbcSeed.Actor(seed.Client, "ClientUser"), requestId, candidateId);
      Assert.False(blocked.Succeeded);
      Assert.Equal("pbc.delegation.state", blocked.ErrorCode);
    }

    using var revokeRequest = new HttpRequestMessage(HttpMethod.Post,
      $"/api/ui/portal/requests/{requestId}/delegations/{delegationId}/revoke");
    revokeRequest.Headers.Add("X-XSRF-TOKEN", csrf);
    using var revokeResponse = await client.SendAsync(revokeRequest);
    Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);
    var revokeBody = await revokeResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.True(revokeBody.GetProperty("value").GetBoolean());

    using var afterRevokeResponse = await client.GetAsync($"/api/ui/portal/requests/{requestId}");
    Assert.Equal(HttpStatusCode.OK, afterRevokeResponse.StatusCode);
    var afterRevoke = await afterRevokeResponse.Content.ReadFromJsonAsync<JsonElement>();
    Assert.True(afterRevoke.GetProperty("canManageDelegations").GetBoolean());
    Assert.Empty(afterRevoke.GetProperty("delegations").EnumerateArray());
    Assert.Empty(afterRevoke.GetProperty("candidates").EnumerateArray());
  }

  [Theory]
  [InlineData("state", PbcStates.Sent, "financial_packages", HttpStatusCode.Conflict)]
  [InlineData("revision", PbcStates.Sent, "financial_packages", HttpStatusCode.Conflict)]
  [InlineData("window", PbcStates.UnderReview, "commercial_deliverable_bundles", HttpStatusCode.OK)]
  [InlineData("grant", PbcStates.UnderReview, "financial_packages", HttpStatusCode.Forbidden)]
  public async Task PortalHomeActionsRecheckRequestStateRevisionAndTheReplyOnlyWindow(
    string mutation, string initialState, string triggerTable, HttpStatusCode expectedStatus)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-ACTION-READ-FENCE");
    var seed = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateSentRequestAsync(pg, seed);
    // State projection fixture only; no professional review or provider completion is asserted.
    await using (var setup = new AuditSphereDbContext(pg.Options))
      await setup.PbcRequests.Where(x => x.Id == requestId).ExecuteUpdateAsync(s => s.SetProperty(x => x.State, initialState));
    var affected = 0;
    var interceptor = new AfterPortalRead(triggerTable, async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      affected = mutation switch
      {
        "state" => await db.PbcRequests.Where(x => x.Id == requestId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.State, PbcStates.UnderReview)),
        "revision" => await db.PbcRequests.Where(x => x.Id == requestId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Revision, x => x.Revision + 1)),
        "window" => await db.Engagements.Where(x => x.Id == seed.EngagementId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Closed")),
        "grant" => await db.RoleGrants.Where(x => x.FirmId == seed.FirmId && x.UserId == seed.Client.Id && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
      };
    });
    using var factory = InterceptingFactory(pg.ConnectionString, seed.Client.Subject, seed.Client.TenantId, pg.Options, interceptor);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(client);
    interceptor.Arm();
    using var response = await client.GetAsync("/api/ui/portal?page=0&pageSize=10");
    Assert.True(interceptor.Fired);
    Assert.True(affected > 0);
    Assert.Equal(expectedStatus, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    if (expectedStatus == HttpStatusCode.OK)
    {
      var workspace = JsonSerializer.Deserialize<PortalWorkspace>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
      var item = Assert.Single(workspace!.Requests);
      Assert.False(item.CanWrite);
      Assert.False(item.CanUpload);
      Assert.False(item.CanReply);
    }
    else
    {
      Assert.DoesNotContain(requestId.ToString(), body, StringComparison.OrdinalIgnoreCase);
      Assert.DoesNotContain("canUpload", body, StringComparison.Ordinal);
      Assert.DoesNotContain("canReply", body, StringComparison.Ordinal);
      Assert.Contains(expectedStatus == HttpStatusCode.Conflict ? "generation.stale" : "scope.denied", body, StringComparison.Ordinal);
    }
  }

  [Theory]
  [InlineData("detail", "revision", HttpStatusCode.Conflict, "generation.stale")]
  [InlineData("history", "revision", HttpStatusCode.Conflict, "generation.stale")]
  [InlineData("detail", "owner", HttpStatusCode.Forbidden, "scope.denied")]
  [InlineData("history", "owner", HttpStatusCode.Forbidden, "scope.denied")]
  [InlineData("detail", "grant", HttpStatusCode.Forbidden, "scope.denied")]
  [InlineData("history", "grant", HttpStatusCode.Forbidden, "scope.denied")]
  [InlineData("detail", "primary", HttpStatusCode.Conflict, "generation.stale")]
  [InlineData("detail", "window", HttpStatusCode.Conflict, "generation.stale")]
  public async Task PortalRequestReads_RecheckRequestAndGrantAfterCommunicationsAreRead(
    string route, string mutation, HttpStatusCode expectedStatus, string expectedCode)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-REQUEST-READ-FENCE");
    var seed = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateSentRequestAsync(pg, seed);
    var affectedRows = 0;
    var replacementOwnerId = Guid.Empty;
    if (mutation == "primary")
      await AddPrimaryContactAndCandidateAsync(pg, seed);
    if (mutation == "owner")
    {
      var replacementOwner = PbcSeed.User(seed.FirmId, "Client");
      replacementOwnerId = replacementOwner.Id;
      await using var db = new AuditSphereDbContext(pg.Options);
      db.Users.Add(replacementOwner);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, replacementOwner, "ClientUser",
        clientId: seed.ClientId, engagementId: seed.EngagementId));
      await db.SaveChangesAsync();
    }

    var interceptor = new AfterPbcCommunicationsRead(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      affectedRows = mutation switch
      {
        "revision" => await AcknowledgeRequestAsync(db, seed, requestId),
        "owner" => await db.PbcRequests.Where(x => x.FirmId == seed.FirmId && x.ClientId == seed.ClientId &&
            x.EngagementId == seed.EngagementId && x.Id == requestId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientOwnerUserId, replacementOwnerId)
            .SetProperty(x => x.Revision, x => x.Revision + 1)),
        "grant" => await db.RoleGrants.Where(x => x.FirmId == seed.FirmId && x.UserId == seed.Client.Id &&
            x.Role == "ClientUser" && x.ClientId == seed.ClientId && x.EngagementId == seed.EngagementId && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)),
        "primary" => await db.ClientContacts.Where(x => x.FirmId == seed.FirmId && x.PracticeClientId == seed.ClientId &&
            x.Email == seed.Client.Email && x.Primary)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Primary, false)),
        "window" => await db.Engagements.Where(x => x.FirmId == seed.FirmId && x.Id == seed.EngagementId && x.Status == "Active")
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Closed")),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
      };
    });

    using var factory = InterceptingFactory(pg.ConnectionString, seed.Client.Subject,
      seed.Client.TenantId, pg.Options, interceptor);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(client);
    interceptor.Arm();

    var path = route == "history"
      ? $"/api/ui/portal/requests/{requestId}/history"
      : $"/api/ui/portal/requests/{requestId}";
    using var response = await client.GetAsync(path);
    Assert.True(interceptor.Fired, "The test must mutate request identity/state or the grant after pbc_communications rows are read.");
    Assert.Equal(1, affectedRows);
    Assert.Equal(expectedStatus, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    using var error = JsonDocument.Parse(body);
    Assert.Equal(expectedCode, error.RootElement.GetProperty("code").GetString());
    Assert.DoesNotContain("Bank statements", body, StringComparison.Ordinal);
    Assert.DoesNotContain("Complete period with readable account identity.", body, StringComparison.Ordinal);
  }

  [Fact]
  public async Task PortalReplyReplayAndReceiptRead_RecheckOwnerAfterOperationLookup()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PORTAL-REPLY-OWNER-RACE");
    var seed = await PbcSeed.SeedAsync(pg);
    var requestId = await CreateSentRequestAsync(pg, seed);
    var operationId = Guid.CreateVersion7();
    const string replyBody = "Recoverable client reply";
    var originalActor = PbcSeed.Actor(seed.Client, "ClientUser");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.ReplyAsync(db, originalActor, requestId, operationId, replyBody);
      Assert.True(created.Succeeded, created.Message);
    }

    var replacementOwner = PbcSeed.User(seed.FirmId, "Client");
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(replacementOwner);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, replacementOwner, "ClientUser",
        clientId: seed.ClientId, engagementId: seed.EngagementId));
      await db.SaveChangesAsync();
    }

    async Task<int> ReassignOwnerAsync()
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await db.PbcRequests.Where(x => x.FirmId == seed.FirmId && x.ClientId == seed.ClientId &&
          x.EngagementId == seed.EngagementId && x.Id == requestId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientOwnerUserId, replacementOwner.Id)
          .SetProperty(x => x.Revision, x => x.Revision + 1));
    }

    var readAffectedRows = 0;
    var readInterceptor = new AfterPbcCommunicationsRead(async () =>
      readAffectedRows = await ReassignOwnerAsync());
    using (var factory = InterceptingFactory(pg.ConnectionString, seed.Client.Subject,
      seed.Client.TenantId, pg.Options, readInterceptor))
    using (var client = factory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      await SignInAsync(client);
      readInterceptor.Arm();
      using var response = await client.GetAsync($"/api/ui/portal/requests/{requestId}/reply-receipts/{operationId}");
      Assert.True(readInterceptor.Fired, "The request owner must change after the receipt lookup.");
      Assert.Equal(1, readAffectedRows);
      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    using (var replacementFactory = Factory(pg.ConnectionString, replacementOwner.Subject, replacementOwner.TenantId))
    using (var replacementClient = replacementFactory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      await SignInAsync(replacementClient);
      using var currentOwnerRead = await replacementClient.GetAsync(
        $"/api/ui/portal/requests/{requestId}/reply-receipts/{operationId}");
      Assert.Equal(HttpStatusCode.OK, currentOwnerRead.StatusCode);
      using var receiptJson = JsonDocument.Parse(await currentOwnerRead.Content.ReadAsStringAsync());
      Assert.Equal(JsonValueKind.Null, receiptJson.RootElement.ValueKind);
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(1, await db.PbcRequests.Where(x => x.FirmId == seed.FirmId && x.Id == requestId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.ClientOwnerUserId, seed.Client.Id)
          .SetProperty(x => x.Revision, x => x.Revision + 1)));

    var replayAffectedRows = 0;
    var replayInterceptor = new AfterPbcCommunicationsRead(async () =>
      replayAffectedRows = await ReassignOwnerAsync());
    using (var factory = InterceptingFactory(pg.ConnectionString, seed.Client.Subject,
      seed.Client.TenantId, pg.Options, replayInterceptor))
    using (var client = factory.CreateClient(new() { AllowAutoRedirect = false }))
    {
      var csrf = await SignInAsync(client);
      replayInterceptor.Arm();
      using var response = await PostAsync(client, $"/api/ui/portal/requests/{requestId}/reply",
        new { requestId = operationId, body = replyBody }, csrf);
      Assert.True(replayInterceptor.Fired, "The request owner must change after the replay lookup.");
      Assert.Equal(1, replayAffectedRows);
      Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    await using var verify = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await verify.PbcCommunications.CountAsync(x => x.FirmId == seed.FirmId &&
      x.AuthorUserId == seed.Client.Id && x.ClientRequestId == operationId));
  }

  private static async Task<Guid> CreateSentRequestAsync(OwnedPostgresDatabase pg, PbcSeed.Fixture seed)
  {
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, PbcSeed.Actor(seed.Staff, "Staff"),
        new(seed.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31", "Cash", "PDF or CSV",
          "12 months", seed.Client.Id, seed.Staff.Id, seed.Reviewer.Id, "2027-01-31", "Confidential",
          "Complete period with readable account identity."));
      Assert.True(created.Succeeded, created.Message);
      requestId = created.Value;
    }

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var sent = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(seed.Staff, "Staff"),
        new(requestId, PbcStates.Sent, 1));
      Assert.True(sent.Succeeded, sent.Message);
    }
    return requestId;
  }

  private static async Task<int> AcknowledgeRequestAsync(AuditSphereDbContext db, PbcSeed.Fixture seed, Guid requestId)
  {
    var acknowledged = await PbcService.ChangeStateAsync(db, PbcSeed.Actor(seed.Client, "ClientUser"),
      new(requestId, PbcStates.Acknowledged, 2));
    return acknowledged.Succeeded ? 1 : 0;
  }

  private static async Task<Guid> AddPrimaryContactAndCandidateAsync(OwnedPostgresDatabase pg, PbcSeed.Fixture seed)
  {
    var candidate = PbcSeed.User(seed.FirmId, "Client");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.Users.Add(candidate);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, candidate, "ClientUser",
      clientId: seed.ClientId, engagementId: seed.EngagementId));
    db.ClientContacts.Add(new()
    {
      Id = Guid.NewGuid(), FirmId = seed.FirmId, PracticeClientId = seed.ClientId,
      FullName = seed.Client.DisplayName, Email = seed.Client.Email, Role = "Primary contact",
      Primary = true, IsActive = true
    });
    await db.SaveChangesAsync();
    return candidate.Id;
  }

  [Fact]
  public async Task PortalUploadResume_RotatesCapabilityForExactFileAndKeepsItOutOfReads()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-PORTAL-UPLOAD-RESUME");
    var seed = await PbcSeed.SeedAsync(pg);
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var staff = PbcSeed.Actor(seed.Staff, "Staff");
      var created = await PbcService.CreateRequestAsync(db, staff, new(seed.EngagementId, "Bank statements", "TEST", "2026-01-01", "2026-12-31", "Bank evidence", "PDF", "Totals",
        seed.Client.Id, seed.Staff.Id, seed.Reviewer.Id, "2027-01-31", "Confidential", "Complete readable statements"));
      Assert.True(created.Succeeded, created.Message); requestId = created.Value;
      Assert.True((await PbcService.ChangeStateAsync(db, staff, new(requestId, PbcStates.Sent, 1))).Succeeded);
    }

    var bytes = "exact synthetic portal resume bytes"u8.ToArray();
    var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    using var clientFactory = Factory(pg.ConnectionString, seed.Client.Subject, seed.Client.TenantId);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignInAsync(client);
    using var started = await PostAsync(client, $"/api/ui/portal/requests/{requestId}/uploads", new
    {
      fileName = "resume-evidence.txt", contentType = "text/plain", byteCount = bytes.Length.ToString(), sha256 = hash
    }, csrf);
    Assert.Equal(HttpStatusCode.OK, started.StatusCode);
    var startBody = await started.Content.ReadFromJsonAsync<JsonElement>();
    var startReceipt = startBody.GetProperty("value");
    var uploadId = startReceipt.GetProperty("uploadIntentId").GetGuid();
    var oldCapability = startReceipt.GetProperty("capability").GetString();
    Assert.False(string.IsNullOrWhiteSpace(oldCapability));

    await using var cached = new AuditSphereDbContext(pg.Options);
    var trackedIntent = await cached.PbcUploadIntents.SingleAsync(x =>
      x.Id == uploadId && x.FirmId == seed.FirmId);
    var trackedRevision = trackedIntent.Revision;

    var resumeUrl = $"/api/ui/portal/requests/{requestId}/uploads/{uploadId}/resume";
    var resumeInput = new { fileName = "resume-evidence.txt", byteCount = bytes.Length.ToString(), sha256 = hash };
    using (var noCsrf = await PostAsync(client, resumeUrl, resumeInput))
      Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
    using (var mismatch = await PostAsync(client, resumeUrl,
      new { fileName = "resume-evidence.txt", byteCount = bytes.Length.ToString(), sha256 = new string('0', 64) }, csrf))
      Assert.NotEqual(HttpStatusCode.OK, mismatch.StatusCode);

    using var resumed = await PostAsync(client, resumeUrl, resumeInput, csrf);
    Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
    Assert.Equal("no-store", resumed.Headers.CacheControl?.ToString());
    var resumeBody = await resumed.Content.ReadFromJsonAsync<JsonElement>();
    var resume = resumeBody.GetProperty("value");
    Assert.Equal(uploadId, resume.GetProperty("uploadIntentId").GetGuid());
    Assert.Equal(requestId, resume.GetProperty("pbcRequestId").GetGuid());
    Assert.Equal(0, resume.GetProperty("receivedByteCount").GetInt64());
    Assert.Equal(0, resume.GetProperty("nextChunkIndex").GetInt32());
    var newCapability = resume.GetProperty("capability").GetString();
    Assert.False(string.IsNullOrWhiteSpace(newCapability));
    Assert.Equal(trackedRevision + 1, resume.GetProperty("revision").GetInt64());
    Assert.False(string.Equals(oldCapability, newCapability, StringComparison.Ordinal));

    var actor = PbcSeed.Actor(seed.Client, "ClientUser");
    var resumedOnCachedContext = await PbcService.ResumeUploadAsync(cached, actor,
      new ResumePbcUploadRequest(requestId, uploadId, "resume-evidence.txt", bytes.Length, hash));
    Assert.True(resumedOnCachedContext.Succeeded, resumedOnCachedContext.ErrorCode);
    var cachedContextCapability = resumedOnCachedContext.Value!.Capability;
    Assert.Equal(resume.GetProperty("revision").GetInt64() + 1, resumedOnCachedContext.Value.Revision);
    Assert.False(string.Equals(newCapability, cachedContextCapability, StringComparison.Ordinal));

    using var resumedAgain = await PostAsync(client, resumeUrl, resumeInput, csrf);
    Assert.Equal(HttpStatusCode.OK, resumedAgain.StatusCode);
    Assert.Equal("no-store", resumedAgain.Headers.CacheControl?.ToString());
    var resumedAgainBody = await resumedAgain.Content.ReadFromJsonAsync<JsonElement>();
    var resumedAgainValue = resumedAgainBody.GetProperty("value");
    Assert.Equal(uploadId, resumedAgainValue.GetProperty("uploadIntentId").GetGuid());
    Assert.Equal(requestId, resumedAgainValue.GetProperty("pbcRequestId").GetGuid());
    Assert.Equal(0, resumedAgainValue.GetProperty("receivedByteCount").GetInt64());
    Assert.Equal(0, resumedAgainValue.GetProperty("nextChunkIndex").GetInt32());
    Assert.Equal(resumedOnCachedContext.Value.Revision + 1, resumedAgainValue.GetProperty("revision").GetInt64());
    var finalCapability = resumedAgainValue.GetProperty("capability").GetString();
    Assert.False(string.IsNullOrWhiteSpace(finalCapability));
    Assert.False(string.Equals(cachedContextCapability, finalCapability, StringComparison.Ordinal));

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var stale = await PbcService.RecordChunkAsync(db, actor,
        new RecordPbcUploadChunkRequest(uploadId, 0, 0, bytes.Length, hash, oldCapability!, "synthetic-old-capability.part"));
      Assert.False(stale.Succeeded);
      Assert.Equal(AuditSphereOps.Domain.Shared.ErrorCodes.ScopeDenied, stale.ErrorCode);
    }
    var current = await PbcService.RecordChunkAsync(cached, actor,
      new RecordPbcUploadChunkRequest(uploadId, 0, 0, bytes.Length, hash, finalCapability!, "synthetic-resumed-chunk.part"));
    Assert.True(current.Succeeded, current.ErrorCode);
    Assert.Equal(bytes.Length, current.Value!.ReceivedByteCount);
    Assert.Equal(resumedAgainValue.GetProperty("revision").GetInt64() + 1, current.Value.Revision);

    var detail = await client.GetFromJsonAsync<JsonElement>($"/api/ui/portal/requests/{requestId}");
    Assert.DoesNotContain("capability", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("stagedPath", detail.ToString(), StringComparison.OrdinalIgnoreCase);
    await using var verify = new AuditSphereDbContext(pg.Options);
    var persisted = await verify.PbcUploadIntents.AsNoTracking().SingleAsync(x => x.Id == uploadId);
    Assert.Equal(current.Value.Revision, persisted.Revision);
    Assert.Equal(PbcUploadStates.Chunking, persisted.State);
    Assert.Equal(bytes.Length, persisted.ReceivedByteCount);
    Assert.Equal(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(finalCapability!), persisted.CapabilityHash);
    Assert.NotEqual(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(oldCapability!), persisted.CapabilityHash);
    Assert.NotEqual(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(newCapability!), persisted.CapabilityHash);
    Assert.NotEqual(AuditSphereOps.Domain.Shared.Hashing.Sha256Hex(cachedContextCapability), persisted.CapabilityHash);
    Assert.Single(await verify.PbcUploadChunks.AsNoTracking().Where(x => x.PbcUploadIntentId == uploadId).ToListAsync());
  }

  [Fact]
  public async Task PortalDocuments_ProjectOnlyTheCurrentClientContextAndRecords()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-PORTAL-DOC-CONTEXT");
    var fixture = await SeedSharedLettersAsync(pg);

    using var clientAFactory = Factory(pg.ConnectionString, fixture.Seed.Client.Subject, fixture.Seed.Client.TenantId);
    using var clientA = clientAFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(clientA);
    using var responseA = await clientA.GetAsync("/api/ui/portal/documents");
    Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
    Assert.Equal("no-store", responseA.Headers.CacheControl?.ToString());
    var bodyA = await responseA.Content.ReadFromJsonAsync<JsonElement>();
    AssertClientDocument(bodyA, fixture.ClientA, fixture.ClientB, fixture.Seed.Staff.Id, fixture.Seed.Client.Id);

    using var clientBFactory = Factory(pg.ConnectionString, fixture.ClientBSubject, fixture.ClientBTenant);
    using var clientB = clientBFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(clientB);
    using var responseB = await clientB.GetAsync("/api/ui/portal/documents");
    Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
    Assert.Equal("no-store", responseB.Headers.CacheControl?.ToString());
    var bodyB = await responseB.Content.ReadFromJsonAsync<JsonElement>();
    AssertClientDocument(bodyB, fixture.ClientB, fixture.ClientA, fixture.Seed.Staff.Id, fixture.Seed.Client.Id);
  }

  [Theory]
  [InlineData("grant", HttpStatusCode.Forbidden, "scope.denied")]
  [InlineData("context", HttpStatusCode.Conflict, "generation.stale")]
  public async Task PortalDocuments_RecheckScopeAndContextAfterDeliverableRowsAreRead(
    string mutation, HttpStatusCode expectedStatus, string expectedCode)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("ANGULAR-PORTAL-DOC-FENCE");
    var fixture = await SeedSharedLettersAsync(pg);
    var affectedRows = 0;
    var interceptor = new AfterAuditDeliverablesRead(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      affectedRows = mutation switch
      {
        "grant" => await db.RoleGrants.Where(x => x.FirmId == fixture.Seed.FirmId &&
            x.UserId == fixture.Seed.Client.Id && x.Role == "ClientUser" && x.ClientId == fixture.ClientA.ClientId &&
            x.EngagementId == fixture.ClientA.EngagementId && x.RevokedAt == null)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow)),
        "context" => await db.PracticeClients.Where(x => x.FirmId == fixture.Seed.FirmId && x.Id == fixture.ClientA.ClientId)
          .ExecuteUpdateAsync(s => s.SetProperty(x => x.LegalName, "Changed Portal Context Client A")),
        _ => throw new ArgumentOutOfRangeException(nameof(mutation))
      };
    });

    using var factory = InterceptingFactory(pg.ConnectionString, fixture.Seed.Client.Subject,
      fixture.Seed.Client.TenantId, pg.Options, interceptor);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignInAsync(client);
    interceptor.Arm();

    using var response = await client.GetAsync("/api/ui/portal/documents");
    Assert.True(interceptor.Fired, "The test must mutate scope or context after audit_deliverables has returned rows.");
    Assert.Equal(1, affectedRows);
    Assert.Equal(expectedStatus, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    using var error = JsonDocument.Parse(body);
    Assert.Equal(expectedCode, error.RootElement.GetProperty("code").GetString());
    AssertNoPortalEvidence(body, fixture);
  }

  private static async Task<PortalFixture> SeedSharedLettersAsync(OwnedPostgresDatabase pg)
  {
    // Projection-only synthetic management letters; this is not a report approval or final-bundle fixture.
    var seed = await PbcSeed.SeedAsync(pg);
    var secondClient = PbcSeed.User(seed.FirmId, "Client");
    var secondClientId = Guid.NewGuid();
    var secondEngagementId = Guid.NewGuid();
    var clientA = new PortalTestContext(seed.ClientId, seed.EngagementId, "Synthetic Portal Context Client A",
      "FinancialStatementAudit", "2026-01-01", "2026-12-31", Guid.NewGuid(), string.Empty,
      DateTimeOffset.Parse("2026-10-15T13:00:00+03:00").ToUniversalTime(), DateTimeOffset.Parse("2026-10-16T13:05:00+03:00").ToUniversalTime());
    var clientB = new PortalTestContext(secondClientId, secondEngagementId, "Synthetic Portal Context Client B",
      "AccountingOnly", "2025-04-01", "2026-03-31", Guid.NewGuid(), string.Empty,
      DateTimeOffset.Parse("2026-09-01T09:00:00+03:00").ToUniversalTime(), null);
    var contentA = Encoding.UTF8.GetBytes("synthetic shared management letter for client A");
    var contentB = Encoding.UTF8.GetBytes("synthetic shared management letter for client B");
    clientA = clientA with { Sha256 = Sha256(contentA) };
    clientB = clientB with { Sha256 = Sha256(contentB) };

    await using var db = new AuditSphereDbContext(pg.Options);
    var engagementA = await db.Engagements.SingleAsync(x => x.Id == seed.EngagementId && x.FirmId == seed.FirmId);
    engagementA.ServiceRoute = clientA.ServiceRoute;
    engagementA.PeriodStart = clientA.PeriodStart;
    engagementA.PeriodEnd = clientA.PeriodEnd;
    var practiceClientA = await db.PracticeClients.SingleAsync(x => x.Id == seed.ClientId && x.FirmId == seed.FirmId);
    practiceClientA.LegalName = clientA.ClientLegalName;

    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientB.ClientId, FirmId = seed.FirmId, LegalName = clientB.ClientLegalName, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = clientB.ClientId, FirmId = seed.FirmId });
    db.Engagements.Add(new Engagement
    {
      Id = clientB.EngagementId, FirmId = seed.FirmId, PracticeClientId = clientB.ClientId,
      ServiceRoute = clientB.ServiceRoute, PeriodStart = clientB.PeriodStart, PeriodEnd = clientB.PeriodEnd,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.Users.Add(secondClient);
    db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, secondClient, "ClientUser", clientB.ClientId, clientB.EngagementId));
    db.ClientPortalFirstSignIns.Add(PbcSeed.FirstSignIn(secondClient));
    db.AuditDeliverables.Add(SharedLetter(seed, clientA, contentA));
    db.AuditDeliverables.Add(SharedLetter(seed, clientB, contentB));
    db.ClientDeliverableReviews.Add(SharedReview(seed, clientA));
    db.ClientDeliverableReviews.Add(SharedReview(seed, clientB));
    await db.SaveChangesAsync();

    return new PortalFixture(seed, secondClient.Subject, secondClient.TenantId, clientA, clientB);
  }

  private static AuditDeliverable SharedLetter(PbcSeed.Fixture seed, PortalTestContext context, byte[] content) => new()
  {
    Id = context.DeliverableId, FirmId = seed.FirmId, ClientId = context.ClientId, EngagementId = context.EngagementId,
    Kind = DeliverableKinds.ManagementLetter, Version = 1, TemplateVersion = "synthetic-read-fixture-v1",
    InputDigest = Sha256(Encoding.UTF8.GetBytes($"synthetic input for {context.ClientLegalName}")), InputSummaryJson = "{}",
    FileName = "synthetic-management-letter.txt", ContentType = "text/plain", Content = content,
    ContentSha256 = context.Sha256, CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
  };

  private static ClientDeliverableReview SharedReview(PbcSeed.Fixture seed, PortalTestContext context) => new()
  {
    Id = Guid.NewGuid(), FirmId = seed.FirmId, ClientId = context.ClientId, EngagementId = context.EngagementId,
    DeliverableId = context.DeliverableId, SharedByUserId = seed.Staff.Id, SharedAt = context.SharedAt,
    AcknowledgedAt = context.AcknowledgedAt,
    AcknowledgedSha256 = context.AcknowledgedAt is null ? null : context.Sha256,
    AcknowledgedByUserId = context.AcknowledgedAt is null ? null : seed.Client.Id
  };

  private static void AssertClientDocument(JsonElement body, PortalTestContext visible, PortalTestContext hidden, Guid issuerUserId, Guid acknowledgerUserId)
  {
    var documents = body.GetProperty("documents").EnumerateArray().ToArray();
    var document = Assert.Single(documents);
    Assert.Equal(visible.DeliverableId, document.GetProperty("deliverableId").GetGuid());
    Assert.Equal(visible.Sha256, document.GetProperty("sha256").GetString());
    AssertPortalContext(document.GetProperty("context"), visible);
    Assert.Equal(visible.SharedAt, document.GetProperty("sharedAt").GetDateTimeOffset());
    var acknowledged = document.GetProperty("acknowledgedAt");
    if (visible.AcknowledgedAt is { } expectedAcknowledged)
      Assert.Equal(expectedAcknowledged, acknowledged.GetDateTimeOffset());
    else Assert.Equal(JsonValueKind.Null, acknowledged.ValueKind);
    Assert.NotEqual(hidden.SharedAt, document.GetProperty("sharedAt").GetDateTimeOffset());
    Assert.Empty(body.GetProperty("signedLetters").EnumerateArray());
    Assert.Empty(body.GetProperty("bundles").EnumerateArray());

    var json = body.GetRawText();
    Assert.DoesNotContain(hidden.ClientLegalName, json, StringComparison.Ordinal);
    Assert.DoesNotContain(hidden.ServiceRoute, json, StringComparison.Ordinal);
    Assert.DoesNotContain(hidden.PeriodStart, json, StringComparison.Ordinal);
    Assert.DoesNotContain(hidden.PeriodEnd, json, StringComparison.Ordinal);
    Assert.DoesNotContain(hidden.DeliverableId.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(hidden.Sha256, json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(issuerUserId.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain(acknowledgerUserId.ToString("D"), json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("acknowledgedByUserId", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("sharedByUserId", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("createdByUserId", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("clientId", json, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("engagementId", json, StringComparison.OrdinalIgnoreCase);
  }

  private static void AssertPortalContext(JsonElement context, PortalTestContext expected)
  {
    Assert.Equal(expected.ClientLegalName, context.GetProperty("clientLegalName").GetString());
    Assert.Equal(expected.ServiceRoute, context.GetProperty("serviceRoute").GetString());
    Assert.Equal(expected.PeriodStart, context.GetProperty("periodStart").GetString());
    Assert.Equal(expected.PeriodEnd, context.GetProperty("periodEnd").GetString());
    Assert.Equal(new[] { "clientLegalName", "periodEnd", "periodStart", "serviceRoute" },
      context.EnumerateObject().Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray());
  }

  private static void AssertNoPortalEvidence(string body, PortalFixture fixture)
  {
    foreach (var value in new[]
    {
      fixture.ClientA.ClientLegalName, fixture.ClientA.ServiceRoute, fixture.ClientA.PeriodStart, fixture.ClientA.PeriodEnd,
      fixture.ClientA.DeliverableId.ToString("D"), fixture.ClientA.Sha256, fixture.ClientB.ClientLegalName,
      fixture.ClientB.ServiceRoute, fixture.ClientB.PeriodStart, fixture.ClientB.PeriodEnd,
      fixture.ClientB.DeliverableId.ToString("D"), fixture.ClientB.Sha256, "Management Letter",
      fixture.Seed.Staff.Id.ToString("D")
    })
      Assert.DoesNotContain(value, body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("clientLegalName", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("serviceRoute", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("periodStart", body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("periodEnd", body, StringComparison.OrdinalIgnoreCase);
  }

  private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

  private static StandaloneApiApplicationFactory InterceptingFactory(string connection, string subject, string tenant,
    DbContextOptions<AuditSphereDbContext> options, DbCommandInterceptor interceptor) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = connection, ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = subject, ["DevelopmentIdentity:TenantId"] = tenant,
      ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false"
    }, services =>
    {
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.AddSingleton<IDbContextFactory<AuditSphereDbContext>>(
        new InterceptingContextFactory(new DbContextOptionsBuilder<AuditSphereDbContext>(options)
          .AddInterceptors(interceptor).Options));
    });

  private sealed record PortalTestContext(Guid ClientId, Guid EngagementId, string ClientLegalName, string ServiceRoute,
    string PeriodStart, string PeriodEnd, Guid DeliverableId, string Sha256, DateTimeOffset SharedAt, DateTimeOffset? AcknowledgedAt);

  private sealed record PortalFixture(PbcSeed.Fixture Seed, string ClientBSubject, string ClientBTenant,
    PortalTestContext ClientA, PortalTestContext ClientB);

  private sealed class AfterPortalRead(string table, Func<Task> mutate) : DbCommandInterceptor
  {
    private int armed;
    private int fired;
    public bool Fired => Volatile.Read(ref fired) == 1;
    public void Arm() => Volatile.Write(ref armed, 1);
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref armed) == 1 && command.CommandText.Contains(table, StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref fired, 1, 0) == 0) await mutate();
      return result;
    }
  }

  private sealed class AfterAuditDeliverablesRead(Func<Task> mutate) : DbCommandInterceptor
  {
    private int armed;
    private int fired;
    public bool Fired => Volatile.Read(ref fired) == 1;
    public void Arm() => Volatile.Write(ref armed, 1);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref armed) == 1 && result.HasRows &&
          command.CommandText.Contains("audit_deliverables", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref fired, 1, 0) == 0)
        await mutate();
      return result;
    }
  }

  private sealed class AfterPbcCommunicationsRead(Func<Task> mutate) : DbCommandInterceptor
  {
    private int armed;
    private int fired;
    public bool Fired => Volatile.Read(ref fired) == 1;
    public void Arm() => Volatile.Write(ref armed, 1);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
      DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref armed) == 1 && result.HasRows &&
          command.CommandText.Contains("pbc_communications", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref fired, 1, 0) == 0)
        await mutate();
      return result;
    }
  }

  private sealed class InterceptingContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }
}
