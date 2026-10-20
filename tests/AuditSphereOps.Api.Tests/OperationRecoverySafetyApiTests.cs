using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class OperationRecoverySafetyApiTests
{
  private sealed class ContextFactory(DbContextOptions<AuditSphereDbContext> options) : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, PbcSeed.Fixture f,
    bool staff = false, DbCommandInterceptor? interceptor = null) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = staff ? f.Staff.Subject : f.Admin.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Admin.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, services =>
    {
      if (interceptor is null) return;
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.AddSingleton<IDbContextFactory<AuditSphereDbContext>>(new ContextFactory(
        new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(interceptor).Options));
    });

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static Task<HttpResponseMessage> Post(HttpClient client, string csrf, string route, object body)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(body) };
    request.Headers.Add("X-XSRF-TOKEN", csrf);
    return client.SendAsync(request);
  }

  private static async Task<CommandResult<OperationCommandReceiptView>> ExecuteReviewedAsync(
    AuditSphereDbContext db, AuditSphereOps.Application.Abstractions.ActorContext actor, OperationCommandInput input)
  {
    var draft = new OperationCommandRequest(Guid.NewGuid(), input);
    var preview = await OperationCommandWorkspace.PreviewAsync(db, actor, draft);
    Assert.True(preview.Succeeded, preview.Message);
    Assert.True(preview.Value!.CanProceed, string.Join(';', preview.Value.Blockers));
    return await OperationCommandWorkspace.ExecuteAsync(db, actor, draft with
    {
      Input = preview.Value.CanonicalInput,
      ReviewBasis = preview.Value.ReviewBasis,
      ExpectedRequestHash = preview.Value.RequestHash,
      Reviewed = true
    });
  }

  private static DurableOperation Operation(Guid firm, OperationState state) => new()
  {
    Id = Guid.NewGuid(), FirmId = firm, OperationKind = "SYNTHETIC-RECOVERY", TargetId = Guid.NewGuid(),
    ExpectedRevision = 1, CorrelationId = Guid.NewGuid(), IdempotencyKey = Guid.NewGuid().ToString("D"),
    RequestDigest = new string('a', 64), RequestBytes = [1], PayloadJson = "{\"private\":\"SYNTHETIC-HIDDEN\"}",
    Status = state, NextAttemptAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
  };

  [Fact]
  public async Task LegacyHttpRecoveryAliasesCannotBypassPreviewAssentAndReceipt()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-RECOVERY-UNCERTAIN");
    var f = await PbcSeed.SeedAsync(pg);
    var uncertain = Operation(f.FirmId, OperationState.RESULT_UNCERTAIN);
    var queued = Operation(f.FirmId, OperationState.PENDING);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.DurableOperations.AddRange(uncertain, queued);
    await db.SaveChangesAsync();
    using var factory = Factory(pg, f);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, $"/api/ui/operations/{uncertain.Id}/retry", new { })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, $"/api/ui/operations/{uncertain.Id}/cancel", new { disposition = "Cannot cancel unknown effects" })).StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, $"/api/ui/operations/{queued.Id}/cancel", new { disposition = "Reviewed queued cancellation" })).StatusCode);
    var retained = await db.DurableOperations.AsNoTracking().SingleAsync(x => x.Id == uncertain.Id);
    Assert.Equal(OperationState.RESULT_UNCERTAIN, retained.Status);
    Assert.Equal(OperationState.PENDING, await db.DurableOperations.Where(x => x.Id == queued.Id).Select(x => x.Status).SingleAsync());
    using var response = await client.GetAsync("/api/ui/operations");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadAsStringAsync();
    Assert.DoesNotContain("SYNTHETIC-HIDDEN", body);
    Assert.DoesNotContain("requestBytes", body);
    Assert.DoesNotContain("leaseOwner", body);
    Assert.Empty(await db.OperationEvents.ToListAsync());
    Assert.Empty(await db.OperatorCommandReceipts.ToListAsync());
  }

  [Fact]
  public async Task HttpLiftCannotBypassApprovalAndApprovedRestartAtomicallyLeavesQuarantine()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-RECOVERY-APPROVAL");
    var f = await PbcSeed.SeedAsync(pg);
    await using var db = new AuditSphereDbContext(pg.Options);
    var actor = PbcSeed.Actor(f.Admin, "Administrator");
    var session = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor,
      new("Synthetic restore", 9, "Exact recovery scope", "Pending findings"));
    Assert.True(session.Succeeded, session.Message);
    using var factory = Factory(pg, f);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, "/api/ui/operations/lift-quarantine", new { })).StatusCode);
    Assert.Equal("RECOVERY_QUARANTINE", await db.FirmSafetyStates.Select(x => x.OperatingMode).SingleAsync());
    var approved = await ExecuteReviewedAsync(db, actor, new(OperationCommandActions.ApproveRecoveryRestart,
      RecoverySessionId: session.Value, Findings: "Exact persisted reconciliation findings"));
    Assert.True(approved.Succeeded, approved.Message);
    Assert.Equal("LOCAL_ONLY", await db.FirmSafetyStates.Select(x => x.OperatingMode).SingleAsync());
    Assert.Equal(2L, await db.FirmSafetyStates.Select(x => x.DeploymentEpoch).SingleAsync());
    var newer = await OperationRecoveryService.BeginRecoverySessionAsync(db, actor, new("New synthetic restore", 10, "New scope", "Pending new findings"));
    Assert.True(newer.Succeeded, newer.Message);
    Assert.Equal(HttpStatusCode.Conflict, (await Post(client, csrf, "/api/ui/operations/lift-quarantine", new { })).StatusCode);
  }

  [Fact]
  public async Task OperatorCommandEvidenceTablesRejectTruncate()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-OPERATOR-COMMAND-TRUNCATE");
    await using var db = new AuditSphereDbContext(pg.Options);

    var receiptError = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
      db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE operator_command_receipts"));
    Assert.Equal("55000", receiptError.SqlState);

    var eventError = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
      db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE operator_command_events, operator_command_receipts"));
    Assert.Equal("55000", eventError.SqlState);
  }

  [Fact]
  public async Task ClientScopedAdministratorAndForeignOrGuessedOperationRemainDenied()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-RECOVERY-SCOPE");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var hidden = Operation(foreign.FirmId, OperationState.DEAD_LETTER);
    await using var db = new AuditSphereDbContext(pg.Options);
    db.DurableOperations.Add(hidden);
    db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "Administrator", f.ClientId));
    await db.SaveChangesAsync();
    using var scopedFactory = Factory(pg, f, staff: true);
    using var scoped = scopedFactory.CreateClient(new() { AllowAutoRedirect = false });
    var scopedCsrf = await SignIn(scoped);
    Assert.Equal(HttpStatusCode.Forbidden, (await scoped.GetAsync("/api/ui/operations")).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await Post(scoped, scopedCsrf, $"/api/ui/operations/{hidden.Id}/retry", new { })).StatusCode);
    using var factory = Factory(pg, f);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    using var foreignResponse = await Post(client, csrf, $"/api/ui/operations/{hidden.Id}/retry", new { });
    using var unknownResponse = await Post(client, csrf, $"/api/ui/operations/{Guid.NewGuid()}/retry", new { });
    Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    Assert.Equal(foreignResponse.StatusCode, unknownResponse.StatusCode);
    Assert.Equal(await foreignResponse.Content.ReadAsStringAsync(), await unknownResponse.Content.ReadAsStringAsync());
  }

  private sealed class ObserveLock : DbCommandInterceptor
  {
    public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
      if (command.CommandText.Contains("firm_safety_states", StringComparison.Ordinal) && command.CommandText.Contains("FOR ", StringComparison.Ordinal)) Reached.TrySetResult();
      return ValueTask.FromResult(result);
    }
  }

  [Theory, InlineData("retry"), InlineData("cancel"), InlineData("lift-quarantine")]
  public async Task RevocationWinningFirmLockRefusesHttpMutation(string action)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-RECOVERY-REVOKED");
    var f = await PbcSeed.SeedAsync(pg);
    var operation = Operation(f.FirmId, action == "retry" ? OperationState.DEAD_LETTER : OperationState.PENDING);
    await using var blocker = new AuditSphereDbContext(pg.Options);
    blocker.DurableOperations.Add(operation);
    await blocker.SaveChangesAsync();
    var signal = new ObserveLock();
    using var factory = Factory(pg, f, interceptor: signal);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var csrf = await SignIn(client);
    await using var tx = await blocker.Database.BeginTransactionAsync();
    await blocker.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {f.FirmId} FOR UPDATE").SingleAsync();
    var route = action == "lift-quarantine" ? "/api/ui/operations/lift-quarantine" : $"/api/ui/operations/{operation.Id}/{action}";
    var pending = Post(client, csrf, route, new { disposition = "Synthetic cancellation" });
    await signal.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
    await blocker.RoleGrants.Where(x => x.UserId == f.Admin.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    await tx.CommitAsync();
    using var result = await pending.WaitAsync(TimeSpan.FromSeconds(15));
    Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
    Assert.Empty(await blocker.OperationEvents.ToListAsync());
    Assert.Equal(operation.Status, await blocker.DurableOperations.Where(x => x.Id == operation.Id).Select(x => x.Status).SingleAsync());
    Assert.Equal(1L, await blocker.FirmSafetyStates.Select(x => x.DeploymentEpoch).SingleAsync());
  }
}
