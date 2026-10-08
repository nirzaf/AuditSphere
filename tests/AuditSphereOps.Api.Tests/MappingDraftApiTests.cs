using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AuditSphereOps.Api.Tests;

public sealed class MappingDraftApiTests
{
  [Fact]
  public async Task ReviewedBatchCreatesOneNewVersionAndPreservesApprovedHistory()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-MAPPING-BATCH");
    var f = await PbcSeed.SeedAsync(pg); var parent = await Seed(pg, f);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c);
    var url = Url(parent); var editor = await Read(c, url + "/editor");
    Assert.Equal(2, editor.GetProperty("sourceAccountCount").GetInt32());
    Assert.Equal(25, editor.GetProperty("pageSize").GetInt32());
    Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync(url + "/editor?page=2")).StatusCode);
    var intent = Intent(editor);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(c, "wrong", url + "/draft-preview", intent)).StatusCode);
    var preview = await Preview(c, csrf, url, intent);
    Assert.True(preview.GetProperty("canCreate").GetBoolean());
    Assert.Equal(3, preview.GetProperty("allocationCount").GetInt32());
    var command = new { intent, revision = preview.GetProperty("revision").GetString(), reviewed = true };
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, url + "/draft", new { intent, command.revision, reviewed = false })).StatusCode);
    var replies = await Task.WhenAll(Post(c, csrf, url + "/draft", command), Post(c, csrf, url + "/draft", command));
    Assert.All(replies, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
    var receipt = JsonDocument.Parse(await replies[0].Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone();
    var id = receipt.GetProperty("mappingId").GetGuid();
    Assert.Equal(id, JsonDocument.Parse(await replies[1].Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetProperty("mappingId").GetGuid());
    Assert.Equal("DRAFT", receipt.GetProperty("status").GetString());
    Assert.Equal(id, (await Read(c, url + $"/draft-receipts/{intent.RequestId}")).GetProperty("mappingId").GetGuid());
    Assert.Equal(HttpStatusCode.OK, (await Post(c, csrf, url + "/draft", command)).StatusCode);
    var changed = intent with { Changes = [new("1000", [new("CASH", "1", "Different requested allocation")])] };
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, url + "/draft", new { intent = changed, command.revision, reviewed = true })).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      Assert.Equal(2, await db.MappingVersions.CountAsync());
      var old = await db.MappingVersions.SingleAsync(x => x.Id == parent);
      Assert.Equal("APPROVED", old.Status); Assert.Null(old.CreationRequestId);
      Assert.All(await db.MappingAllocations.Where(x => x.MappingVersionId == parent).ToListAsync(), x => Assert.Equal(1m, x.Fraction));
      var next = await db.MappingVersions.SingleAsync(x => x.Id == id);
      Assert.Equal(parent, next.BaseMappingVersionId); Assert.Equal(intent.RequestId, next.CreationRequestId);
      Assert.Equal(f.Staff.Id, next.CreatedByUserId); Assert.Null(next.ApprovedByUserId);
      Assert.Equal(2, await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).Select(x => x.InputGeneration).SingleAsync());
      Assert.Empty(await db.SourceAcceptanceDecisions.ToListAsync());
      await using (var direct = new NpgsqlConnection(pg.ConnectionString))
      {
        await direct.OpenAsync();
        await using var editAllocation = new NpgsqlCommand("UPDATE mapping_allocations SET rationale = 'Fabricated replacement' WHERE mapping_version_id = @id", direct);
        editAllocation.Parameters.AddWithValue("id", id);
        Assert.Equal("55000", (await Assert.ThrowsAsync<PostgresException>(() => editAllocation.ExecuteNonQueryAsync())).SqlState);
        await using var insertAllocation = new NpgsqlCommand("INSERT INTO mapping_allocations (id, firm_id, client_id, engagement_id, mapping_version_id, source_account_code, destination_code, statement_section, fraction, rationale, audit_area, residual_policy) SELECT gen_random_uuid(), firm_id, client_id, engagement_id, mapping_version_id, '9999', destination_code, statement_section, fraction, rationale, audit_area, residual_policy FROM mapping_allocations WHERE mapping_version_id = @id LIMIT 1", direct);
        insertAllocation.Parameters.AddWithValue("id", id);
        Assert.Equal("P0001", (await Assert.ThrowsAsync<PostgresException>(() => insertAllocation.ExecuteNonQueryAsync())).SqlState);
      }
      var plan = await MappingApprovalWorkspace.GetAsync(db, PbcSeed.Actor(f.Reviewer, "AccountingReviewer"), id);
      Assert.True(plan.Succeeded, plan.Message);
      Assert.True((await MappingApprovalWorkspace.ApproveAsync(db, PbcSeed.Actor(f.Reviewer, "AccountingReviewer"), id, plan.Value!.Revision, true)).Succeeded);
    }
    // Exercise the database guard directly, independently of tracked-write protection.
    await using var connection = new NpgsqlConnection(pg.ConnectionString); await connection.OpenAsync();
    await using var mutate = new NpgsqlCommand("UPDATE mapping_versions SET creation_request_hash = repeat('0',64) WHERE id = @id", connection);
    mutate.Parameters.AddWithValue("id", id);
    var error = await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());
    Assert.Equal("P0001", error.SqlState);
  }

  [Fact]
  public async Task IncompleteFractionsInvalidAccountsAndUnreviewedRequestsPublishNothing()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-MAPPING-BATCH-INVALID");
    var f = await PbcSeed.SeedAsync(pg); var parent = await Seed(pg, f);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c); var url = Url(parent);
    var editor = await Read(c, url + "/editor");
    foreach (var fraction in new[] { "1e0", "0.1234567", "-1", "0", "1.000001", "1,0" })
    {
      var invalid = Intent(editor) with { Changes = [new("1000", [new("CASH", fraction, "Synthetic invalid fraction")])] };
      Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, url + "/draft-preview", invalid)).StatusCode);
    }
    var guessed = Intent(editor) with { Changes = [new("unavailable-account", [new("CASH", "1", "Synthetic unknown source")])] };
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, url + "/draft-preview", guessed)).StatusCode);
    var incomplete = Intent(editor) with { Changes = [new("1000", [new("CASH", "0.5", "Synthetic incomplete split")])] };
    var plan = await Preview(c, csrf, url, incomplete); Assert.False(plan.GetProperty("canCreate").GetBoolean());
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, url + "/draft", new { intent = incomplete, revision = plan.GetProperty("revision").GetString(), reviewed = true })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await Post(c, csrf, url + "/draft", new { intent = (object?)null, revision = "", reviewed = true })).StatusCode);
    await using var db = new AuditSphereDbContext(pg.Options); Assert.Single(await db.MappingVersions.ToListAsync()); Assert.Equal(2, await db.MappingAllocations.CountAsync());
  }

  [Fact]
  public async Task ChangedGenerationClosedBookAndUnauthorizedIdentityFailClosed()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-MAPPING-BATCH-STALE");
    var f = await PbcSeed.SeedAsync(pg); var parent = await Seed(pg, f);
    using var factory = Factory(pg, f.Staff); using var c = factory.CreateClient(); var csrf = await SignIn(c); var url = Url(parent);
    var editor = await Read(c, url + "/editor"); var intent = Intent(editor); var plan = await Preview(c, csrf, url, intent);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.ClientSafetyStates.Where(x => x.Id == f.ClientId).ExecuteUpdateAsync(x => x.SetProperty(s => s.InputGeneration, s => s.InputGeneration + 1));
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, url + "/draft", new { intent, revision = plan.GetProperty("revision").GetString(), reviewed = true })).StatusCode);
    editor = await Read(c, url + "/editor"); intent = Intent(editor); plan = await Preview(c, csrf, url, intent);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.ClientReportingBooks.Where(x => x.ClientId == f.ClientId).ExecuteUpdateAsync(x => x.SetProperty(s => s.Status, "CLOSED"));
    Assert.Equal(HttpStatusCode.Conflict, (await Post(c, csrf, url + "/draft", new { intent, revision = plan.GetProperty("revision").GetString(), reviewed = true })).StatusCode);
    Assert.False((await Read(c, url + "/editor")).GetProperty("canCreate").GetBoolean());
    using var clientFactory = Factory(pg, f.Client); using var client = clientFactory.CreateClient(); await SignIn(client);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url + "/editor")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Url(Guid.NewGuid()) + "/editor")).StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Single(await proof.MappingVersions.ToListAsync()); Assert.Empty(await proof.SourceAcceptanceDecisions.ToListAsync());
  }

  [Fact]
  public async Task LateEpochRollsBackTheEntireNewVersionAndCreationReceipt()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-MAPPING-BATCH-EPOCH");
    var f = await PbcSeed.SeedAsync(pg); var parent = await Seed(pg, f); var actor = PbcSeed.Actor(f.Staff, "AccountingPreparer");
    var interceptor = new LateEpoch(async () => {
      await using var db = new AuditSphereDbContext(pg.Options);
      await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(x => x.SetProperty(s => s.SessionEpoch, s => s.SessionEpoch + 1));
    });
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options))
    {
      var editor = await MappingDraftWorkspace.GetAsync(db, actor, parent); Assert.True(editor.Succeeded, editor.Message);
      var intent = new MappingDraftIntent(Guid.NewGuid(), editor.Value!.Revision, [new("1000", [new("CASH", "1", "Synthetic independently reviewed replacement")])]);
      var preview = await MappingDraftWorkspace.PreviewAsync(db, actor, parent, intent); Assert.True(preview.Succeeded, preview.Message);
      var result = await MappingDraftWorkspace.CreateAsync(db, actor, parent, intent, preview.Value!.Revision, true);
      Assert.Equal(ErrorCodes.GenerationStale, result.ErrorCode);
    }
    await using var proof = new AuditSphereDbContext(pg.Options); Assert.Single(await proof.MappingVersions.ToListAsync()); Assert.Equal(2, await proof.MappingAllocations.CountAsync());
    Assert.False(await proof.MappingVersions.AnyAsync(x => x.CreationRequestId != null));
  }

  private sealed class LateEpoch(Func<Task> change) : DbCommandInterceptor
  {
    private bool done;
    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData e, DbDataReader reader, CancellationToken ct = default)
    { if (!done && command.CommandText.Contains("INSERT INTO mapping_versions", StringComparison.Ordinal)) { done = true; await change(); } return reader; }
  }
  private static MappingDraftIntent Intent(JsonElement editor) => new(Guid.NewGuid(), editor.GetProperty("revision").GetString()!, [new("1000", [new("CASH", "0.5", "Synthetic explicit split"), new("REVENUE", "0.5", "Synthetic explicit split")])]);
  private static async Task<Guid> Seed(OwnedPostgresDatabase pg, PbcSeed.Fixture f)
  {
    await using var db = new AuditSphereDbContext(pg.Options); var seed = await MappingApprovalSeed.SeedAsync(db, f); var actor = PbcSeed.Actor(f.Reviewer, "AccountingReviewer");
    var plan = await MappingApprovalWorkspace.GetAsync(db, actor, seed.MappingId); Assert.True(plan.Succeeded, plan.Message);
    Assert.True((await MappingApprovalWorkspace.ApproveAsync(db, actor, seed.MappingId, plan.Value!.Revision, true)).Succeeded); return seed.MappingId;
  }
  private static string Url(Guid id) => $"/api/ui/accounting/mappings/{id}";
  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser u) => new(new Dictionary<string, string?> {
    ["ConnectionStrings:AuditSphere"] = pg.ConnectionString, ["DevelopmentIdentity:Enabled"] = "true", ["DevelopmentIdentity:TenantId"] = u.TenantId,
    ["DevelopmentIdentity:Subject"] = u.Subject, ["Application:AllowSimulationAdapters"] = "true", ["ExternalEffects:Enabled"] = "false" });
  private static async Task<string> SignIn(HttpClient c)
  { await c.GetAsync("/auth/sign-in"); using var r = await c.GetAsync("/api/ui/session"); return Uri.UnescapeDataString(r.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]); }
  private static async Task<JsonElement> Read(HttpClient c, string url)
  { using var r = await c.GetAsync(url); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone(); }
  private static async Task<JsonElement> Preview(HttpClient c, string csrf, string url, MappingDraftIntent intent)
  { using var r = await Post(c, csrf, url + "/draft-preview", intent); Assert.Equal(HttpStatusCode.OK, r.StatusCode); return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.GetProperty("value").Clone(); }
  private static Task<HttpResponseMessage> Post(HttpClient c, string csrf, string url, object value)
  { var r = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(value) }; r.Headers.Add("X-XSRF-TOKEN", csrf); return c.SendAsync(r); }
}
