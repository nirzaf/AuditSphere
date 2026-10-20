using System.Net;
using System.Data.Common;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class AccountingPeriodRecordApiTests
{
  [Fact]
  public async Task ExactPeriodRequiresClientAccountingScopeAndClearsAfterRevocation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PERIOD-EXACT-RECORD");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var periodId = Guid.NewGuid();
    var priorPeriodId = Guid.NewGuid();
    var subsequentPeriodId = Guid.NewGuid();
    var siblingClientId = Guid.NewGuid();
    var siblingPeriodId = Guid.NewGuid();
    var siblingTaskId = Guid.NewGuid();
    const string siblingTaskTitle = "Sibling-client evidence must stay hidden";
    var foreignPeriodId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = siblingClientId, FirmId = f.FirmId, LegalName = "SYNTHETIC SIBLING CLIENT", CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = f.FirmId });
      db.ClientReportingPeriods.AddRange(
        Period(periodId, f.FirmId, f.ClientId, f.Staff.Id, priorPeriodId, revision: 9_007_199_254_740_993L),
        Period(priorPeriodId, f.FirmId, f.ClientId, f.Staff.Id, periodCode: "SYNTHETIC-FY2025",
          startDate: new DateOnly(2025, 1, 1), endDate: new DateOnly(2025, 12, 31)),
        Period(subsequentPeriodId, f.FirmId, f.ClientId, f.Staff.Id, periodId, "SYNTHETIC-FY2027",
          new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31)),
        Period(siblingPeriodId, f.FirmId, siblingClientId, f.Staff.Id, periodCode: "SIBLING-FY2026"),
        Period(foreignPeriodId, foreign.FirmId, foreign.ClientId, foreign.Staff.Id));
      await db.SaveChangesAsync();
    }
    using var factory = new StandaloneApiApplicationFactory(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:Subject"] = f.Staff.Subject,
      ["DevelopmentIdentity:TenantId"] = f.Staff.TenantId,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    });
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    var path = "/api/ui/accounting/periods/" + periodId;
    var evidencePath = path + "/evidence";
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    await client.GetAsync("/auth/sign-in");
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(evidencePath)).StatusCode);

    // An engagement-only accounting grant cannot widen to client-level reporting periods.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId, f.EngagementId));
      await db.SaveChangesAsync();
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(evidencePath)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden,
      (await client.GetAsync(evidencePath + "/tasks?page=1")).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId));
      await db.SaveChangesAsync();
    }
    using var visible = await client.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
    Assert.True(visible.Headers.CacheControl!.NoStore);
    using var body = System.Text.Json.JsonDocument.Parse(await visible.Content.ReadAsStringAsync());
    Assert.Equal(periodId, body.RootElement.GetProperty("id").GetGuid());
    Assert.Equal(f.ClientId, body.RootElement.GetProperty("clientId").GetGuid());
    Assert.Equal("9007199254740993", body.RootElement.GetProperty("revision").GetString());
    Assert.Equal(System.Text.Json.JsonValueKind.Null, body.RootElement.GetProperty("pbc").ValueKind);
    Assert.Equal(System.Text.Json.JsonValueKind.Null, body.RootElement.GetProperty("task").ValueKind);

    // Keep the legacy single-period-record assertion above on its original empty-task fixture.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.WorkTasks.AddRange(Enumerable.Range(1, 26).Select(index => new WorkTask
      {
        Id = Guid.Parse($"00000000-0000-0000-0000-{index:D12}"), FirmId = f.FirmId, ClientId = f.ClientId,
        ReportingPeriodId = periodId, Title = $"Evidence task {index:D2}", CreatedAt = DateTimeOffset.UtcNow
      }));
      db.WorkTasks.Add(new WorkTask
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        ReportingPeriodId = priorPeriodId, Title = "Other period task", CreatedAt = DateTimeOffset.UtcNow
      });
      db.WorkTasks.Add(new WorkTask
      {
        Id = siblingTaskId, FirmId = f.FirmId, ClientId = siblingClientId,
        ReportingPeriodId = siblingPeriodId, Title = siblingTaskTitle, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingBooks.Add(new ClientReportingBook
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId, PeriodId = periodId,
        Code = "STATUTORY", Basis = "STATUTORY", InclusionRule = "ALL_ENTITIES", Currency = "QAR",
        Status = AccountingWorkflowStates.Draft, Revision = 9_007_199_254_740_993L,
        CreatedByUserId = f.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var siblingEvidencePath = $"/api/ui/accounting/periods/{siblingPeriodId}/evidence/tasks?page=1";
    using var siblingDenied = await client.GetAsync(siblingEvidencePath);
    var siblingDeniedBody = await siblingDenied.Content.ReadAsStringAsync();
    Assert.Equal(HttpStatusCode.Forbidden, siblingDenied.StatusCode);
    Assert.DoesNotContain(siblingTaskTitle, siblingDeniedBody, StringComparison.Ordinal);
    Assert.DoesNotContain(siblingTaskId.ToString("D"), siblingDeniedBody, StringComparison.OrdinalIgnoreCase);
    using var unknownSibling = await client.GetAsync($"/api/ui/accounting/periods/{Guid.NewGuid()}/evidence/tasks?page=1");
    Assert.Equal(siblingDenied.StatusCode, unknownSibling.StatusCode);
    Assert.Equal(siblingDeniedBody, await unknownSibling.Content.ReadAsStringAsync());

    using var indexResponse = await client.GetAsync(evidencePath);
    Assert.Equal(HttpStatusCode.OK, indexResponse.StatusCode);
    using var index = System.Text.Json.JsonDocument.Parse(await indexResponse.Content.ReadAsStringAsync());
    var indexRoot = index.RootElement;
    Assert.Equal(periodId, indexRoot.GetProperty("periodId").GetGuid());
    Assert.Equal(f.ClientId, indexRoot.GetProperty("clientId").GetGuid());
    var categories = indexRoot.GetProperty("categories").EnumerateArray().ToArray();
    var tasksCategory = Assert.Single(categories, x => x.GetProperty("kind").GetString() == "tasks");
    Assert.Equal(System.Text.Json.JsonValueKind.Number, tasksCategory.GetProperty("totalCount").ValueKind);
    Assert.Equal(26, tasksCategory.GetProperty("totalCount").GetInt32());
    Assert.Equal(25, tasksCategory.GetProperty("pageSize").GetInt32());

    using var booksResponse = await client.GetAsync(evidencePath + "/books?page=1");
    Assert.Equal(HttpStatusCode.OK, booksResponse.StatusCode);
    using var books = System.Text.Json.JsonDocument.Parse(await booksResponse.Content.ReadAsStringAsync());
    Assert.Equal(1, books.RootElement.GetProperty("totalCount").GetInt32());
    var book = Assert.Single(books.RootElement.GetProperty("items").EnumerateArray());
    Assert.Equal("9007199254740993", book.GetProperty("revision").GetString());

    foreach (var kind in new[] { "trial-balances", "general-ledgers", "completeness", "reconciliations", "specialist-schedules", "pbc" })
    {
      using var categoryResponse = await client.GetAsync(evidencePath + "/" + kind + "?page=1");
      Assert.Equal(HttpStatusCode.OK, categoryResponse.StatusCode);
      using var categoryPage = System.Text.Json.JsonDocument.Parse(await categoryResponse.Content.ReadAsStringAsync());
      var categoryRoot = categoryPage.RootElement;
      Assert.Equal(periodId, categoryRoot.GetProperty("periodId").GetGuid());
      Assert.Equal(f.ClientId, categoryRoot.GetProperty("clientId").GetGuid());
      Assert.Equal(kind, categoryRoot.GetProperty("kind").GetString());
      var expectedCount = Assert.Single(categories, x => x.GetProperty("kind").GetString() == kind)
        .GetProperty("totalCount").GetInt32();
      Assert.Equal(expectedCount, categoryRoot.GetProperty("totalCount").GetInt32());
      Assert.Equal(Math.Min(25, expectedCount), categoryRoot.GetProperty("items").GetArrayLength());
    }
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(evidencePath + "/books?page=0")).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(evidencePath + "/books?page=2147483647")).StatusCode);

    using var firstTasksResponse = await client.GetAsync(evidencePath + "/tasks?page=1");
    Assert.Equal(HttpStatusCode.OK, firstTasksResponse.StatusCode);
    using var firstTasks = System.Text.Json.JsonDocument.Parse(await firstTasksResponse.Content.ReadAsStringAsync());
    var firstTasksRoot = firstTasks.RootElement;
    Assert.Equal(periodId, firstTasksRoot.GetProperty("periodId").GetGuid());
    Assert.Equal(f.ClientId, firstTasksRoot.GetProperty("clientId").GetGuid());
    Assert.Equal(1, firstTasksRoot.GetProperty("page").GetInt32());
    Assert.Equal(26, firstTasksRoot.GetProperty("totalCount").GetInt32());
    var firstTaskRows = firstTasksRoot.GetProperty("items").EnumerateArray().ToArray();
    Assert.Equal(25, firstTaskRows.Length);
    Assert.All(firstTaskRows, task => Assert.Equal(System.Text.Json.JsonValueKind.Null,
      task.GetProperty("engagementId").ValueKind));

    using var secondTasksResponse = await client.GetAsync(evidencePath + "/tasks?page=2");
    Assert.Equal(HttpStatusCode.OK, secondTasksResponse.StatusCode);
    using var secondTasks = System.Text.Json.JsonDocument.Parse(await secondTasksResponse.Content.ReadAsStringAsync());
    var secondTasksRoot = secondTasks.RootElement;
    Assert.Equal(periodId, secondTasksRoot.GetProperty("periodId").GetGuid());
    Assert.Equal(f.ClientId, secondTasksRoot.GetProperty("clientId").GetGuid());
    Assert.Equal(2, secondTasksRoot.GetProperty("page").GetInt32());
    Assert.Equal(26, secondTasksRoot.GetProperty("totalCount").GetInt32());
    var secondTaskRows = secondTasksRoot.GetProperty("items").EnumerateArray().ToArray();
    Assert.Single(secondTaskRows);
    Assert.Equal(System.Text.Json.JsonValueKind.Null, secondTaskRows[0].GetProperty("engagementId").ValueKind);
    var pageOneIds = firstTaskRows.Select(x => x.GetProperty("id").GetGuid().ToString("D")).ToArray();
    var pageTwoIds = secondTaskRows.Select(x => x.GetProperty("id").GetGuid().ToString("D")).ToArray();
    Assert.Equal(pageOneIds.OrderBy(x => x, StringComparer.Ordinal), pageOneIds);
    Assert.True(string.CompareOrdinal(pageOneIds[^1], pageTwoIds[0]) < 0);
    Assert.Equal(26, pageOneIds.Concat(pageTwoIds).Distinct(StringComparer.Ordinal).Count());

    using var referencesResponse = await client.GetAsync(evidencePath + "/period-references?page=1");
    Assert.Equal(HttpStatusCode.OK, referencesResponse.StatusCode);
    using var references = System.Text.Json.JsonDocument.Parse(await referencesResponse.Content.ReadAsStringAsync());
    Assert.Equal(f.ClientId, references.RootElement.GetProperty("clientId").GetGuid());
    Assert.Equal(2, references.RootElement.GetProperty("totalCount").GetInt32());
    var referenceRows = references.RootElement.GetProperty("items").EnumerateArray().ToArray();
    Assert.Equal(2, referenceRows.Length);
    Assert.Contains(referenceRows, row => row.GetProperty("id").GetGuid() == priorPeriodId &&
      row.GetProperty("relation").GetString() == "PRIOR");
    Assert.Contains(referenceRows, row => row.GetProperty("id").GetGuid() == subsequentPeriodId &&
      row.GetProperty("relation").GetString() == "SUBSEQUENT");

    using var hidden = await client.GetAsync("/api/ui/accounting/periods/" + foreignPeriodId);
    using var guessed = await client.GetAsync("/api/ui/accounting/periods/" + Guid.NewGuid());
    Assert.Equal(HttpStatusCode.Forbidden, hidden.StatusCode);
    Assert.Equal(hidden.StatusCode, guessed.StatusCode);
    Assert.Equal(await hidden.Content.ReadAsStringAsync(), await guessed.Content.ReadAsStringAsync());
    Assert.True(hidden.Headers.CacheControl!.NoStore);
    using var hiddenEvidence = await client.GetAsync("/api/ui/accounting/periods/" + foreignPeriodId + "/evidence");
    using var guessedEvidence = await client.GetAsync("/api/ui/accounting/periods/" + Guid.NewGuid() + "/evidence");
    Assert.Equal(HttpStatusCode.Forbidden, hiddenEvidence.StatusCode);
    Assert.Equal(hiddenEvidence.StatusCode, guessedEvidence.StatusCode);
    Assert.Equal(await hiddenEvidence.Content.ReadAsStringAsync(), await guessedEvidence.Content.ReadAsStringAsync());
    Assert.True(hiddenEvidence.Headers.CacheControl!.NoStore);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      await db.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.Role == "AccountingPreparer")
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.RevokedAt, DateTimeOffset.UtcNow));
    }
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(evidencePath)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      await db.Users.Where(x => x.Id == f.Staff.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.SessionEpoch, x => x.SessionEpoch + 1));
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(evidencePath)).StatusCode);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.Equal(5, await db.ClientReportingPeriods.CountAsync());
  }

  [Fact]
  public async Task EvidencePageRechecksClientGrantAfterSnapshotRowsWereRead()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-PERIOD-EVIDENCE-LATE-REVOKE");
    var f = await PbcSeed.SeedAsync(pg);
    var periodId = Guid.NewGuid();
    var taskId = Guid.NewGuid();
    const string taskTitle = "Evidence row must not survive late client-grant revocation";
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientReportingPeriods.Add(Period(periodId, f.FirmId, f.ClientId, f.Staff.Id,
        periodCode: "SYNTHETIC-LATE-REVOKE-FY2026"));
      db.WorkTasks.Add(new WorkTask
      {
        Id = taskId, FirmId = f.FirmId, ClientId = f.ClientId, ReportingPeriodId = periodId,
        Title = taskTitle, CreatedAt = DateTimeOffset.UtcNow
      });
      db.RoleGrants.Add(PbcSeed.Grant(f.FirmId, f.Staff, "AccountingPreparer", f.ClientId));
      await db.SaveChangesAsync();
    }

    var interceptor = new RevokeClientGrantAfterEvidenceRead(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      return await db.RoleGrants.Where(x => x.UserId == f.Staff.Id && x.FirmId == f.FirmId &&
          x.ClientId == f.ClientId && x.EngagementId == null && x.Role == "AccountingPreparer" && x.RevokedAt == null)
        .ExecuteUpdateAsync(x => x.SetProperty(grant => grant.RevokedAt, DateTimeOffset.UtcNow));
    });
    using var factory = InterceptingFactory(pg, f, interceptor);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    var path = $"/api/ui/accounting/periods/{periodId}/evidence/tasks?page=1";

    using (var allowed = await client.GetAsync(path))
    {
      Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
      var allowedBody = await allowed.Content.ReadAsStringAsync();
      Assert.Contains(taskTitle, allowedBody, StringComparison.Ordinal);
      Assert.Contains(taskId.ToString("D"), allowedBody, StringComparison.OrdinalIgnoreCase);
    }

    interceptor.Arm();
    using var revoked = await client.GetAsync(path);
    var revokedBody = await revoked.Content.ReadAsStringAsync();
    Assert.True(interceptor.Fired, "The grant must be revoked after the task page query and before post-snapshot authorization.");
    Assert.True(interceptor.EvidenceRowsQuerySeen, "The task page rows must be queried before grant revocation.");
    Assert.Equal(1, interceptor.RevokedGrantCount);
    Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    Assert.DoesNotContain(taskTitle, revokedBody, StringComparison.Ordinal);
    Assert.DoesNotContain(taskId.ToString("D"), revokedBody, StringComparison.OrdinalIgnoreCase);
  }

  private static StandaloneApiApplicationFactory InterceptingFactory(OwnedPostgresDatabase pg,
    PbcSeed.Fixture fixture, DbCommandInterceptor interceptor) => new(new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:TenantId"] = fixture.Staff.TenantId,
      ["DevelopmentIdentity:Subject"] = fixture.Staff.Subject,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false"
    }, services =>
    {
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.AddSingleton<IDbContextFactory<AuditSphereDbContext>>(
        new InterceptingContextFactory(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
          .AddInterceptors(interceptor).Options));
    });

  private sealed class RevokeClientGrantAfterEvidenceRead(Func<Task<int>> revoke) : DbCommandInterceptor
  {
    private int armed;
    private int periodScopeReads;
    private int evidenceRowsQuerySeen;
    private int fired;
    private int revokedGrantCount;

    public bool Fired => Volatile.Read(ref fired) == 1;
    public bool EvidenceRowsQuerySeen => Volatile.Read(ref evidenceRowsQuerySeen) == 1;
    public int RevokedGrantCount => Volatile.Read(ref revokedGrantCount);

    public void Arm()
    {
      Interlocked.Exchange(ref periodScopeReads, 0);
      Interlocked.Exchange(ref evidenceRowsQuerySeen, 0);
      Interlocked.Exchange(ref fired, 0);
      Interlocked.Exchange(ref revokedGrantCount, 0);
      Volatile.Write(ref armed, 1);
    }

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref armed) != 1) return result;
      var sql = command.CommandText;
      if (sql.Contains("work_tasks", StringComparison.OrdinalIgnoreCase) &&
          sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) &&
          sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
        Interlocked.Exchange(ref evidenceRowsQuerySeen, 1);

      if (!sql.Contains("client_reporting_periods", StringComparison.OrdinalIgnoreCase)) return result;
      var scopeRead = Interlocked.Increment(ref periodScopeReads);
      if (scopeRead == 3 && EvidenceRowsQuerySeen && Interlocked.CompareExchange(ref fired, 1, 0) == 0)
        Volatile.Write(ref revokedGrantCount, await revoke());
      return result;
    }
  }

  private sealed class InterceptingContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }

  private static ClientReportingPeriod Period(Guid id, Guid firm, Guid client, Guid actor, Guid? priorPeriodId = null,
    string periodCode = "SYNTHETIC-FY2026", DateOnly? startDate = null, DateOnly? endDate = null, long revision = 1) => new()
  {
    Id = id, FirmId = firm, ClientId = client, PeriodCode = periodCode,
    StartDate = startDate ?? new DateOnly(2026, 1, 1), EndDate = endDate ?? new DateOnly(2026, 12, 31),
    Basis = "STATUTORY", Currency = "QAR", Status = AccountingWorkflowStates.Draft, Revision = revision,
    PriorPeriodId = priorPeriodId, CreatedByUserId = actor, CreatedAt = DateTimeOffset.UtcNow
  };
}
