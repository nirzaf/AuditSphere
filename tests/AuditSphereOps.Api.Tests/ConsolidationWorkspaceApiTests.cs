using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class ConsolidationWorkspaceApiTests
{
  [Fact]
  public async Task OwnershipWorkspace_PinsExactPercentagePointsAndRefusesCycleStalenessAndUngrantableWrites()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-OWNERSHIP");
    var fixture = await SeedOwnershipAsync(pg);
    using var factory = Factory(pg, fixture.Seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);
    var url = $"/api/ui/consolidation/scopes/{fixture.ScopeId}/ownership-interests";
    var initial = await Read(client, url + "?page=0&pageSize=1");
    Assert.True(initial.GetProperty("canAddOwnership").GetBoolean());
    Assert.Equal(1, initial.GetProperty("groupRevision").GetInt64());
    Assert.Equal(3, initial.GetProperty("eligibleMembers").GetArrayLength());
    Assert.DoesNotContain(initial.GetProperty("eligibleMembers").EnumerateArray(),
      x => x.GetProperty("clientId").GetGuid() == fixture.OutsideClientId);
    Assert.Contains(initial.GetProperty("eligibleMembers").EnumerateArray(),
      x => x.GetProperty("clientId").GetGuid() == fixture.FutureClientId &&
        x.GetProperty("membershipEffectiveFrom").GetString() == "2099-01-01");
    object Intent(Guid parent, Guid child, long revision = 1) => new
    {
      parentClientId = parent, childClientId = child, effectiveFrom = "2026-01-01", effectiveTo = (string?)null,
      ownershipPercent = "73.123456", economicInterestPercent = "66.500000", controlAssessment = "CONTROLLED",
      method = "EVIDENCED-METHOD", evidenceReference = "OWNERSHIP-01", expectedGroupRevision = revision, reviewed = true
    };
    using var added = await Post(client, csrf, url, Intent(fixture.Seed.ClientId, fixture.ChildClientId));
    Assert.Equal(HttpStatusCode.OK, added.StatusCode);
    var id = JsonDocument.Parse(await added.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    var saved = await Read(client, url + "?page=0&pageSize=1");
    Assert.Equal(1, saved.GetProperty("total").GetInt32());
    var row = saved.GetProperty("interests")[0];
    Assert.Equal(id, row.GetProperty("id").GetGuid());
    Assert.Equal(fixture.Seed.ClientId, row.GetProperty("parentClientId").GetGuid());
    Assert.Equal(fixture.ChildClientId, row.GetProperty("childClientId").GetGuid());
    Assert.Equal("73.123456", row.GetProperty("ownershipPercent").GetString());
    Assert.Equal("66.500000", row.GetProperty("economicInterestPercent").GetString());
    Assert.Equal("2026-01-01", row.GetProperty("effectiveFrom").GetString());
    Assert.Equal("APPROVED", row.GetProperty("status").GetString());
    using var cycle = await Post(client, csrf, url, Intent(fixture.ChildClientId, fixture.Seed.ClientId));
    Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
    Assert.Contains("circular", await cycle.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    using var duplicate = await Post(client, csrf, url, Intent(fixture.Seed.ClientId, fixture.ChildClientId));
    Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    using var stale = await Post(client, csrf, url, Intent(fixture.Seed.ClientId, fixture.ChildClientId, 2));
    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    using var outside = await Post(client, csrf, url, Intent(fixture.Seed.ClientId, fixture.OutsideClientId));
    Assert.Equal(HttpStatusCode.BadRequest, outside.StatusCode);
    using var beforeMembership = await Post(client, csrf, url, Intent(fixture.Seed.ClientId, fixture.FutureClientId));
    Assert.Equal(HttpStatusCode.BadRequest, beforeMembership.StatusCode);

    using var readerFactory = Factory(pg, fixture.Seed.Reviewer);
    using var reader = readerFactory.CreateClient();
    var readerCsrf = await SignIn(reader);
    var readOnly = await Read(reader, url);
    Assert.False(readOnly.GetProperty("canAddOwnership").GetBoolean());
    using var deniedWrite = await Post(reader, readerCsrf, url, Intent(fixture.ChildClientId, fixture.Seed.ClientId));
    Assert.Equal(HttpStatusCode.Forbidden, deniedWrite.StatusCode);
    using var ungrantedFactory = Factory(pg, fixture.Seed.Admin);
    using var ungranted = ungrantedFactory.CreateClient();
    await SignIn(ungranted);
    using var deniedRead = await ungranted.GetAsync(url);
    Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await proof.OwnershipInterestVersions.CountAsync(x => x.ScopeVersionId == fixture.ScopeId));
  }

  [Fact]
  public async Task OwnershipCommands_SerializeConcurrentOppositeEdgesBeforeCycleValidation()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-OWNERSHIP-RACE");
    var fixture = await SeedOwnershipAsync(pg);
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var first = new AuditSphereDbContext(pg.Options);
    await using var second = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var fence = await blocker.Database.BeginTransactionAsync();
    await blocker.ClientGroups.FromSqlInterpolated(
      $"SELECT * FROM client_groups WHERE id = {fixture.GroupId} AND firm_id = {fixture.Seed.FirmId} FOR UPDATE").SingleAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    OwnershipInterestRequest Request(Guid parent, Guid child) => new(fixture.ScopeId, parent, child,
      new DateOnly(2026, 1, 1), null, 73.123456m, 66.500000m, "CONTROLLED", "METHOD", "CONCURRENT-EDGE", 1);
    var actor = PbcSeed.Actor(fixture.Seed.Staff, "Manager");
    var firstTask = ConsolidationService.AddOwnershipInterestAsync(first, actor,
      Request(fixture.Seed.ClientId, fixture.ChildClientId), timeout.Token);
    var secondTask = ConsolidationService.AddOwnershipInterestAsync(second, actor,
      Request(fixture.ChildClientId, fixture.Seed.ClientId), timeout.Token);
    try { await WaitForOwnershipGroupWaitersAsync(observer, 2, timeout.Token); }
    finally { await fence.RollbackAsync(); }
    var results = await Task.WhenAll(firstTask, secondTask);
    Assert.Single(results, x => x.Succeeded);
    Assert.Single(results, x => !x.Succeeded && x.ErrorCode == ErrorCodes.Accounting.MappingInvalid);
    Assert.Equal(1, await observer.OwnershipInterestVersions.CountAsync(x => x.ScopeVersionId == fixture.ScopeId));
  }

  [Fact]
  public async Task ScopeApproval_RechecksGraphAfterAConcurrentOwnershipWriterReleasesGroupLock()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-OWNERSHIP-APPROVAL-RACE");
    var fixture = await SeedOwnershipAsync(pg);
    await using var writer = new AuditSphereDbContext(pg.Options);
    await using var reviewer = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    writer.GroupAccessGrants.Add(new GroupAccessGrant
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, GroupId = fixture.GroupId,
      UserId = fixture.Seed.Reviewer.Id, Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow,
      GrantedByUserId = fixture.Seed.Staff.Id
    });
    await writer.SaveChangesAsync();
    await using var fence = await writer.Database.BeginTransactionAsync();
    await writer.ClientGroups.FromSqlInterpolated(
      $"SELECT * FROM client_groups WHERE id = {fixture.GroupId} AND firm_id = {fixture.Seed.FirmId} FOR UPDATE").SingleAsync();
    writer.OwnershipInterestVersions.Add(new OwnershipInterestVersion
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, GroupId = fixture.GroupId, ScopeVersionId = fixture.ScopeId,
      ParentClientId = fixture.Seed.ClientId, ChildClientId = fixture.ChildClientId,
      EffectiveFrom = new DateOnly(2026, 1, 1), OwnershipPercent = 73m, EconomicInterestPercent = 66m,
      ControlAssessment = "CONTROLLED", Method = "METHOD", EvidenceReference = "WRITER-EDGE",
      Status = AccountingWorkflowStates.Approved, CreatedAt = DateTimeOffset.UtcNow
    });
    await writer.SaveChangesAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var approval = ConsolidationService.ApproveScopeAsync(reviewer, PbcSeed.Actor(fixture.Seed.Reviewer, "AccountingReviewer"),
      fixture.ScopeId, timeout.Token);
    try
    {
      await WaitForOwnershipGroupWaitersAsync(observer, 1, timeout.Token);
      await fence.CommitAsync();
    }
    finally { if (writer.Database.CurrentTransaction is not null) await fence.RollbackAsync(); }
    var refused = await approval;
    Assert.False(refused.Succeeded);
    Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
    Assert.Contains("nested", refused.Message, StringComparison.OrdinalIgnoreCase);
    Assert.Equal(AccountingWorkflowStates.Draft,
      await observer.ConsolidationScopeVersions.Where(x => x.Id == fixture.ScopeId).Select(x => x.Status).SingleAsync());
  }

  private sealed record OwnershipFixture(PbcSeed.Fixture Seed, Guid GroupId, Guid ScopeId, Guid ChildClientId, Guid OutsideClientId, Guid FutureClientId);

  private static async Task<OwnershipFixture> SeedOwnershipAsync(OwnedPostgresDatabase pg)
  {
    var seed = await PbcSeed.SeedAsync(pg);
    var groupId = Guid.CreateVersion7();
    var scopeId = Guid.CreateVersion7();
    var childId = Guid.CreateVersion7();
    var outsideId = Guid.CreateVersion7();
    var futureId = Guid.CreateVersion7();
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.AddRange(new PracticeClient
    {
      Id = childId, FirmId = seed.FirmId, LegalName = "Synthetic ownership child", CreatedAt = DateTimeOffset.UtcNow
    }, new PracticeClient
    {
      Id = outsideId, FirmId = seed.FirmId, LegalName = "Synthetic outside entity", CreatedAt = DateTimeOffset.UtcNow
    }, new PracticeClient
    {
      Id = futureId, FirmId = seed.FirmId, LegalName = "Synthetic future member", CreatedAt = DateTimeOffset.UtcNow
    });
    db.ClientGroups.Add(new ClientGroup
    {
      Id = groupId, FirmId = seed.FirmId, Code = "OWNERSHIP-GROUP", Name = "Synthetic ownership group",
      Revision = 1, CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    foreach (var (user, role) in new[] { (seed.Staff, "Manager"), (seed.Reviewer, "AccountingPreparer") })
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = groupId, UserId = user.Id,
        Role = role, GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });
    db.ClientGroupMemberships.Add(new ClientGroupMembership
    {
      Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = groupId, ClientId = futureId,
      EffectiveFrom = new DateOnly(2099, 1, 1), Status = AccountingWorkflowStates.Approved,
      OwnershipPercent = 100m, EconomicInterestPercent = 100m, ControlMethod = "CONTROLLED",
      EvidenceReference = "FUTURE-MEMBER-EVIDENCE", CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    foreach (var clientId in new[] { seed.ClientId, childId })
      db.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = groupId, ClientId = clientId,
        EffectiveFrom = new DateOnly(2020, 1, 1), Status = AccountingWorkflowStates.Approved,
        OwnershipPercent = 100m, EconomicInterestPercent = 100m, ControlMethod = "CONTROLLED",
        EvidenceReference = "MEMBER-EVIDENCE", CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
    db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
    {
      Id = scopeId, FirmId = seed.FirmId, GroupId = groupId, PeriodId = Guid.CreateVersion7(), Version = 1,
      GroupRevision = 1, ReportingCurrency = "QAR", Method = ConsolidationCalculator.RestrictedMethod,
      OpeningBasis = "INITIAL_CONSOLIDATION", Status = AccountingWorkflowStates.Draft,
      CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
    return new(seed, groupId, scopeId, childId, outsideId, futureId);
  }

  private static async Task WaitForOwnershipGroupWaitersAsync(AuditSphereDbContext observer, int expected, CancellationToken ct)
  {
    while (true)
    {
      var count = await observer.Database.SqlQueryRaw<int>(
        "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() " +
        "AND wait_event_type='Lock' AND query LIKE '%client_groups%'").SingleAsync(ct);
      if (count >= expected) return;
      await Task.Delay(25, ct);
    }
  }

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
    using var optionsDenied = await client.GetAsync($"/api/ui/consolidation/groups/{scopeId}/scope-options");
    Assert.Equal(HttpStatusCode.Forbidden, optionsDenied.StatusCode);
  }

  [Fact]
  public async Task ScopeCreationOptions_AreGroupScopedAndExpectedRevisionIsPinnedOnCreate()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-SCOPE-OPTIONS");
    var seed = await PbcSeed.SeedAsync(pg);
    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);
    using var createdGroup = await Post(client, csrf, "/api/ui/consolidation/groups",
      new { code = "SCOPE-OPT-01", name = "Scope Options Group" });
    Assert.Equal(HttpStatusCode.OK, createdGroup.StatusCode);
    var groupId = JsonDocument.Parse(await createdGroup.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    var periodId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.ClientGroupMemberships.Add(new ClientGroupMembership
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = groupId, ClientId = seed.ClientId,
        EffectiveFrom = new DateOnly(2026, 1, 1), ControlMethod = "CONTROLLED", OwnershipPercent = 100m,
        EconomicInterestPercent = 100m, EvidenceReference = "scope-options-test",
        Status = AccountingWorkflowStates.Approved, CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = periodId, FirmId = seed.FirmId, ClientId = seed.ClientId, PeriodCode = "FY2026",
        StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Basis = "ANNUAL",
        Currency = "QAR", Status = AccountingWorkflowStates.Closed, CreatedByUserId = seed.Staff.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
    }

    var options = await Read(client, $"/api/ui/consolidation/groups/{groupId}/scope-options");
    var groupRevision = options.GetProperty("groupRevision").GetInt64();
    Assert.True(options.GetProperty("canCreateScope").GetBoolean());
    Assert.Equal(periodId, options.GetProperty("periods")[0].GetProperty("id").GetGuid());
    Assert.Contains(options.GetProperty("currencies").EnumerateArray(), x => x.GetString() == "QAR");
    Assert.Contains(options.GetProperty("methods").EnumerateArray(), x =>
      x.GetProperty("code").GetString() == ConsolidationCalculator.RestrictedMethod && x.GetProperty("enabled").GetBoolean());
    Assert.Empty(options.GetProperty("approvedPriorScopes").EnumerateArray());

    using var scopeResponse = await Post(client, csrf, "/api/ui/consolidation/scopes", new
    {
      groupId, periodId, reportingCurrency = "QAR", method = ConsolidationCalculator.RestrictedMethod,
      openingBasis = "INITIAL_CONSOLIDATION", exchangeRateSetVersionId = (Guid?)null,
      translationPolicyVersionId = (Guid?)null, translationRateDate = (DateOnly?)null,
      translationRateType = (string?)null, priorScopeVersionId = (Guid?)null, expectedGroupRevision = groupRevision
    });
    Assert.Equal(HttpStatusCode.OK, scopeResponse.StatusCode);
    var scopeId = JsonDocument.Parse(await scopeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    await using var proof = new AuditSphereDbContext(pg.Options);
    var saved = await proof.ConsolidationScopeVersions.SingleAsync(x => x.Id == scopeId);
    Assert.Equal(groupRevision, saved.GroupRevision);
    Assert.Null(saved.ExchangeRateSetVersionId);
    Assert.Null(saved.TranslationPolicyVersionId);
    Assert.Null(saved.PriorScopeVersionId);

    using var stale = await Post(client, csrf, "/api/ui/consolidation/scopes", new
    {
      groupId, periodId, reportingCurrency = "QAR", method = ConsolidationCalculator.RestrictedMethod,
      openingBasis = "INITIAL_CONSOLIDATION", exchangeRateSetVersionId = (Guid?)null,
      translationPolicyVersionId = (Guid?)null, translationRateDate = (DateOnly?)null,
      translationRateType = (string?)null, priorScopeVersionId = (Guid?)null, expectedGroupRevision = groupRevision + 1
    });
    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
  }

  [Fact]
  public async Task GroupCreation_RequiresFirmWideAuthorityAndGrantsOnlyTheCreatedGroup()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-GROUP-CREATE");
    var seed = await PbcSeed.SeedAsync(pg);
    var scopedManager = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Email = "scoped.manager@auditsphere.test",
      DisplayName = "Scoped Manager", TenantId = "tenant-001", Subject = "sub-scoped-manager-consol"
    };
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "Manager"));
      db.Users.Add(scopedManager);
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, scopedManager, "Manager", clientId: seed.ClientId));
      await db.SaveChangesAsync();
    }

    using var creatorFactory = Factory(pg, seed.Staff);
    using var creator = creatorFactory.CreateClient();
    var csrf = await SignIn(creator);
    var initial = await Read(creator, "/api/ui/consolidation");
    Assert.True(initial.GetProperty("canCreateGroup").GetBoolean());
    Assert.Empty(initial.GetProperty("groups").EnumerateArray());

    using var created = await Post(creator, csrf, "/api/ui/consolidation/groups", new { code = "HOLDCO-01", name = "Holding Company Group" });
    Assert.Equal(HttpStatusCode.OK, created.StatusCode);
    var groupId = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var group = await db.ClientGroups.SingleAsync(x => x.FirmId == seed.FirmId && x.Id == groupId);
      Assert.Equal("HOLDCO-01", group.Code);
      Assert.Equal("Holding Company Group", group.Name);
      var grant = await db.GroupAccessGrants.SingleAsync(x => x.FirmId == seed.FirmId && x.GroupId == groupId);
      Assert.Equal(seed.Staff.Id, grant.UserId);
      Assert.Equal("Manager", grant.Role);
    }
    var refreshed = await Read(creator, "/api/ui/consolidation");
    Assert.Single(refreshed.GetProperty("groups").EnumerateArray());
    Assert.Equal(groupId, refreshed.GetProperty("groups")[0].GetProperty("id").GetGuid());

    using var scopedFactory = Factory(pg, scopedManager);
    using var scoped = scopedFactory.CreateClient();
    var scopedCsrf = await SignIn(scoped);
    var scopedOverview = await Read(scoped, "/api/ui/consolidation");
    Assert.False(scopedOverview.GetProperty("canCreateGroup").GetBoolean());
    Assert.Empty(scopedOverview.GetProperty("groups").EnumerateArray());
    using var denied = await Post(scoped, scopedCsrf, "/api/ui/consolidation/groups", new { code = "HOLDCO-02", name = "Unauthorized Group" });
    Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await proof.ClientGroups.CountAsync(x => x.FirmId == seed.FirmId));
  }

  [Fact]
  public async Task GroupMembership_IsExplicitlyScopedAndPreservesPercentagesAndRevision()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-GROUP-MEMBERS");
    var seed = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var ungranted = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Email = "ungranted.consol@auditsphere.test",
      DisplayName = "Ungraded Viewer", TenantId = "tenant-001", Subject = "sub-ungranted-consol"
    };
    var reader = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = seed.FirmId, Email = "reader.consol@auditsphere.test",
      DisplayName = "Group Reader", TenantId = "tenant-001", Subject = "sub-reader-consol"
    };
    var groupId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(ungranted);
      db.Users.Add(reader);
      db.ClientGroups.Add(new ClientGroup
      {
        Id = groupId, FirmId = seed.FirmId, Code = "MEMBER-GROUP", Name = "Membership Group",
        CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow, Revision = 1
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = groupId, UserId = seed.Staff.Id,
        Role = "Manager", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, GroupId = groupId, UserId = reader.Id,
        Role = "AccountingPreparer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = seed.Staff.Id
      });
      await db.SaveChangesAsync();
    }

    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);
    var initial = await Read(client, $"/api/ui/consolidation/groups/{groupId}/members?page=0&pageSize=1");
    Assert.Equal(1, initial.GetProperty("groupRevision").GetInt64());
    Assert.True(initial.GetProperty("canAddMember").GetBoolean());
    Assert.Contains(initial.GetProperty("eligibleClients").EnumerateArray(), x => x.GetProperty("id").GetGuid() == seed.ClientId);
    Assert.DoesNotContain(initial.GetProperty("eligibleClients").EnumerateArray(), x => x.GetProperty("id").GetGuid() == foreign.ClientId);

    using var added = await Post(client, csrf, $"/api/ui/consolidation/groups/{groupId}/members", new
    {
      clientId = seed.ClientId, effectiveFrom = "2026-01-01", effectiveTo = "2026-12-31", controlMethod = "controlled",
      ownershipPercent = "91.123456", economicInterestPercent = "78.654321", evidenceReference = "PERIMETER-APPROVAL-01"
    });
    Assert.Equal(HttpStatusCode.OK, added.StatusCode);

    var members = await Read(client, $"/api/ui/consolidation/groups/{groupId}/members?page=0&pageSize=1");
    Assert.Equal(2, members.GetProperty("groupRevision").GetInt64());
    Assert.Equal(1, members.GetProperty("total").GetInt32());
    Assert.Equal("91.123456", members.GetProperty("members")[0].GetProperty("ownershipPercent").GetString());
    Assert.Equal("78.654321", members.GetProperty("members")[0].GetProperty("economicInterestPercent").GetString());
    Assert.Equal("CONTROLLED", members.GetProperty("members")[0].GetProperty("controlMethod").GetString());

    var exact = await Read(client, $"/api/ui/consolidation/groups/{groupId}/members?page=0&pageSize=25&clientId={seed.ClientId}&effectiveFrom=2026-01-01");
    Assert.Single(exact.GetProperty("members").EnumerateArray());

    using var overlap = await Post(client, csrf, $"/api/ui/consolidation/groups/{groupId}/members", new
    {
      clientId = seed.ClientId, effectiveFrom = "2026-06-01", effectiveTo = "2027-01-01", controlMethod = "CONTROLLED",
      ownershipPercent = "100", economicInterestPercent = "100", evidenceReference = "OVERLAP-01"
    });
    Assert.Equal(HttpStatusCode.BadRequest, overlap.StatusCode);
    Assert.Contains("Overlapping", (await overlap.Content.ReadAsStringAsync()), StringComparison.OrdinalIgnoreCase);

    using var crossFirm = await Post(client, csrf, $"/api/ui/consolidation/groups/{groupId}/members", new
    {
      clientId = foreign.ClientId, effectiveFrom = "2026-01-01", effectiveTo = (string?)null, controlMethod = "CONTROLLED",
      ownershipPercent = "100", economicInterestPercent = "100", evidenceReference = "CROSS-FIRM-01"
    });
    Assert.Equal(HttpStatusCode.Forbidden, crossFirm.StatusCode);

    using var readerFactory = Factory(pg, reader);
    using var readerClient = readerFactory.CreateClient();
    var readerCsrf = await SignIn(readerClient);
    var readerWorkspace = await Read(readerClient, $"/api/ui/consolidation/groups/{groupId}/members?page=0&pageSize=25");
    Assert.False(readerWorkspace.GetProperty("canAddMember").GetBoolean());
    Assert.Equal(1, readerWorkspace.GetProperty("total").GetInt32());
    using var deniedAdd = await Post(readerClient, readerCsrf, $"/api/ui/consolidation/groups/{groupId}/members", new
    {
      clientId = seed.ClientId, effectiveFrom = "2027-01-01", effectiveTo = (string?)null, controlMethod = "CONTROLLED",
      ownershipPercent = "100", economicInterestPercent = "100", evidenceReference = "READER-CANNOT-ADD"
    });
    Assert.Equal(HttpStatusCode.Forbidden, deniedAdd.StatusCode);

    using var ungrantedFactory = Factory(pg, ungranted);
    using var ungrantedClient = ungrantedFactory.CreateClient();
    await SignIn(ungrantedClient);
    using var deniedRead = await ungrantedClient.GetAsync($"/api/ui/consolidation/groups/{groupId}/members?page=0&pageSize=25");
    Assert.Equal(HttpStatusCode.Forbidden, deniedRead.StatusCode);
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(1, await proof.ClientGroupMemberships.CountAsync(x => x.FirmId == seed.FirmId && x.GroupId == groupId));
  }

  [Fact]
  public async Task ScopeWorkspace_CanQueryDetail_AndBlocksComponentApprovalWithoutSelectedComposition()
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
      var reviewerPartnerGrant = PbcSeed.Grant(seed.FirmId, reviewer, "Partner", clientId: seed.ClientId, engagementId: seed.EngagementId);
      db.RoleGrants.Add(reviewerPartnerGrant);
      db.EngagementStaffAssignments.Add(new EngagementStaffAssignment
      {
        Id = Guid.CreateVersion7(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        UserId = reviewer.Id, StaffingLevel = StaffingLevels.EngagementPartner, RoleGrantId = reviewerPartnerGrant.Id,
        AssignedByUserId = seed.Staff.Id, AssignedAt = DateTimeOffset.UtcNow
      });

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
      db.FinancialPackageValidations.AddRange(FinancialPackageValidationReadiness.RequiredCodes.Select(code =>
        new FinancialPackageValidation
        {
          Id = Guid.CreateVersion7(), FirmId = seed.FirmId, FinancialPackageId = package.Id,
          Code = code, Passed = true, Detail = "Seeded canonical validation for a validated synthetic package.",
          CreatedAt = DateTimeOffset.UtcNow
        }));

      var artifactId = Guid.CreateVersion7();
      var artifactBytes = new byte[] { 1, 2, 3 };
      var artifactDigest = Hashing.Sha256Hex(artifactBytes);
      db.FinancialPackageArtifacts.Add(new FinancialPackageArtifact
      {
        Id = artifactId, FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
        FinancialPackageId = package.Id, PackageRevision = 1, PackageGeneration = 1,
        PackageHash = digest, ArtifactVersion = "v1", FrameworkVersion = "IFRS", TemplateVersion = "1.0",
        ArtifactSha256Hex = artifactDigest, ArtifactBytes = artifactBytes, CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });

      await db.SaveChangesAsync();
      // Add required review decisions so RequireCurrentAsync succeeds for package
      foreach (var stage in new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewStages.PartnerApproval })
      {
        db.FinancialPackageReviewDecisions.Add(new FinancialPackageReviewDecision
        {
          Id = Guid.CreateVersion7(), FirmId = seed.FirmId, ClientId = seed.ClientId, EngagementId = seed.EngagementId,
          FinancialPackageId = package.Id, PackageRevision = 1, PackageGeneration = 1,
          PackageHash = package.CalculationHash, FinancialPackageArtifactId = artifactId,
          ArtifactVersion = "v1", ArtifactSha256Hex = artifactDigest,
          Stage = stage, Decision = FinancialPackageReviewDecisions.Approved,
          EvidenceMode = FinancialPackageReviewEvidenceModes.SignedIn, EvidenceReference = "EVID-01",
          DecidedByUserId = reviewer.Id, DecidedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
      }
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
    var eligiblePackage = (await Read(prepClient, $"/api/ui/consolidation/scopes/{scopeId}"))
      .GetProperty("eligiblePackages").EnumerateArray()
      .Single(x => x.GetProperty("packageId").GetGuid() == packageId);
    var submitRes = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/scopes/{scopeId}/components", new
    {
      clientId = seed.ClientId,
      engagementId = seed.EngagementId,
      packageId = packageId,
      ownershipPercent = 100,
      controlMethod = "CONTROLLED",
      periodBasis = "2026-01-01..2026-12-31",
      taxonomyVersion = "IFRS-2026",
      mappingVersion = eligiblePackage.GetProperty("mappingVersion").GetString()
    });
    Assert.True(submitRes.StatusCode == HttpStatusCode.OK,
      $"Expected component submission to succeed; got {(int)submitRes.StatusCode}: {await submitRes.Content.ReadAsStringAsync()}");
    var submitJson = JsonDocument.Parse(await submitRes.Content.ReadAsStringAsync()).RootElement;
    var componentId = submitJson.GetProperty("value").GetGuid();

    // 3. Preparer tries to approve their own component -> rejected
    var selfApprove = await Post(prepClient, prepCsrf, $"/api/ui/consolidation/components/{componentId}/approve", new { });
    Assert.True(selfApprove.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.BadRequest);

    // 4. A reviewer cannot approve this synthetic package until its exact three-format composition is selected and approved.
    using var revFactory = Factory(pg, reviewer);
    using var revClient = revFactory.CreateClient();
    var revCsrf = await SignIn(revClient);

    var revWs = await Read(revClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.True(revWs.GetProperty("canReview").GetBoolean());
    Assert.Equal(1, revWs.GetProperty("components").GetArrayLength());
    Assert.Equal("SUBMITTED", revWs.GetProperty("components")[0].GetProperty("status").GetString());

    var approveRes = await Post(revClient, revCsrf, $"/api/ui/consolidation/components/{componentId}/approve", new { });
    Assert.Equal(HttpStatusCode.BadRequest, approveRes.StatusCode);
    var approvalFailure = await approveRes.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Contains("composition", approvalFailure.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);

    // 5. The failed review leaves the submitted component unchanged.
    var afterBlockedApproval = await Read(revClient, $"/api/ui/consolidation/scopes/{scopeId}");
    Assert.Equal("SUBMITTED", afterBlockedApproval.GetProperty("components")[0].GetProperty("status").GetString());
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
