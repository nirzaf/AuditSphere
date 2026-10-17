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
  public async Task IntercompanyMatches_RequireIndependentCurrentGroupReviewAndHistory()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-INTERCOMPANY");
    var fixture = await SeedOwnershipAsync(pg);
    var wrongGroupReviewer = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, Email = "wrong-group-reviewer@auditsphere.test",
      DisplayName = "Wrong Group Reviewer", TenantId = "tenant-001", Subject = "sub-wrong-group-intercompany-reviewer"
    };
    var otherGroupId = Guid.CreateVersion7();
    var missingPeriodScopeId = Guid.CreateVersion7();
    int operationalJournalCountBefore;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(wrongGroupReviewer);
      foreach (var user in new[] { fixture.Seed.Staff, fixture.Seed.Reviewer })
        db.GroupAccessGrants.Add(new GroupAccessGrant
        {
          Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, GroupId = fixture.GroupId, UserId = user.Id,
          Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Seed.Staff.Id
        });
      db.ClientGroups.Add(new ClientGroup
      {
        Id = otherGroupId, FirmId = fixture.Seed.FirmId, Code = "OTHER-REVIEW-GROUP", Name = "Other Review Group",
        Revision = 1, CreatedByUserId = fixture.Seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, GroupId = otherGroupId, UserId = wrongGroupReviewer.Id,
        Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = fixture.Seed.Staff.Id
      });
      db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
      {
        Id = missingPeriodScopeId, FirmId = fixture.Seed.FirmId, GroupId = fixture.GroupId,
        PeriodId = Guid.CreateVersion7(), Version = 1, GroupRevision = 1, ReportingCurrency = "QAR",
        Method = ConsolidationCalculator.RestrictedMethod, OpeningBasis = "MISSING-PERIOD-TEST",
        Status = AccountingWorkflowStates.Draft, CreatedByUserId = fixture.Seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
      });
      await db.SaveChangesAsync();
      operationalJournalCountBefore = await db.ClientOperationalJournals.CountAsync();
    }

    using var preparerFactory = Factory(pg, fixture.Seed.Staff);
    using var preparer = preparerFactory.CreateClient();
    var preparerCsrf = await SignIn(preparer);
    var scopeUrl = $"/api/ui/consolidation/scopes/{fixture.ScopeId}";
    var matchesUrl = scopeUrl + "/intercompany-matches";
    var options = await Read(preparer, scopeUrl + "/intercompany-match-options");
    Assert.Equal(fixture.ScopeId, options.GetProperty("scopeVersionId").GetGuid());
    Assert.Equal(fixture.GroupId, options.GetProperty("groupId").GetGuid());
    Assert.Equal(1, options.GetProperty("pinnedGroupRevision").GetInt64());
    Assert.Equal(1, options.GetProperty("currentGroupRevision").GetInt64());
    Assert.DoesNotContain(options.GetProperty("members").EnumerateArray(),
      x => x.GetProperty("clientId").GetGuid() == fixture.OutsideClientId);
    Assert.DoesNotContain(options.GetProperty("members").EnumerateArray(),
      x => x.GetProperty("clientId").GetGuid() == fixture.FutureClientId);

    object Match(string transactionReference, long expectedGroupRevision = 1, Guid? buyerClientId = null,
      bool outsidePerimeterReview = true, string sellerAmount = "1200.123456",
      string buyerAmount = "-1200.000000", string matchedAmount = "1200.000000",
      string? differenceReason = "Timing difference per signed confirmation.") => new
    {
      sellerClientId = fixture.Seed.ClientId,
      buyerClientId = buyerClientId ?? fixture.OutsideClientId,
      accountNature = "RECEIVABLE_PAYABLE",
      periodCode = "FY2026",
      currency = "QAR",
      transactionReference,
      sellerAmount,
      buyerAmount,
      matchedAmount,
      evidenceReference = "SIGNED-IC-STATEMENT-01",
      sellerTaxonomyCode = "ASSETS.IC_AR",
      buyerTaxonomyCode = "LIABILITIES.IC_AP",
      differenceReason,
      matchMode = "ONE_TO_ONE",
      matchGroupReference = "",
      outsidePerimeterReview,
      expectedGroupRevision,
      reviewed = true
    };

    using var missingResidualReason = await Post(preparer, preparerCsrf, matchesUrl,
      Match("IC-RESIDUAL-MISSING-REASON", sellerAmount: "100.000000", buyerAmount: "-100.000000",
        matchedAmount: "90.000000", differenceReason: null));
    Assert.Equal(HttpStatusCode.BadRequest, missingResidualReason.StatusCode);
    Assert.Contains("difference reason", await missingResidualReason.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

    using var futureWithoutOutsideReview = await Post(preparer, preparerCsrf, matchesUrl,
      Match("IC-FUTURE-WITHOUT-OUTSIDE-REVIEW", buyerClientId: fixture.FutureClientId, outsidePerimeterReview: false));
    Assert.Equal(HttpStatusCode.BadRequest, futureWithoutOutsideReview.StatusCode);
    Assert.Contains("explicitly reviewed as outside-perimeter", await futureWithoutOutsideReview.Content.ReadAsStringAsync(),
      StringComparison.OrdinalIgnoreCase);

    using var missingPeriod = await Post(preparer, preparerCsrf,
      $"/api/ui/consolidation/scopes/{missingPeriodScopeId}/intercompany-matches", Match("IC-MISSING-PERIOD"));
    Assert.Equal(HttpStatusCode.BadRequest, missingPeriod.StatusCode);
    Assert.Contains("reporting period", await missingPeriod.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

    using var submitted = await Post(preparer, preparerCsrf, matchesUrl,
      Match("IC-PAIR-001", buyerClientId: fixture.FutureClientId));
    Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
    var matchId = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();

    using var selfApproval = await Post(preparer, preparerCsrf,
      $"/api/ui/consolidation/intercompany-matches/{matchId}/approve",
      new { expectedGroupRevision = 1, reviewed = true });
    Assert.Equal(HttpStatusCode.BadRequest, selfApproval.StatusCode);
    Assert.Contains("another user", await selfApproval.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

    using var reviewerFactory = Factory(pg, fixture.Seed.Reviewer);
    using var reviewer = reviewerFactory.CreateClient();
    var reviewerCsrf = await SignIn(reviewer);
    using var approved = await Post(reviewer, reviewerCsrf,
      $"/api/ui/consolidation/intercompany-matches/{matchId}/approve",
      new { expectedGroupRevision = 1, reviewed = true });
    Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

    var retained = await Read(preparer, matchesUrl + "?page=1&pageSize=50&unresolvedOnly=false");
    Assert.Equal(1, retained.GetProperty("totalCount").GetInt32());
    var row = retained.GetProperty("items")[0];
    Assert.Equal(matchId, row.GetProperty("matchId").GetGuid());
    Assert.Equal(fixture.FutureClientId, row.GetProperty("buyerClientId").GetGuid());
    Assert.Equal("1200.123456", row.GetProperty("sellerAmount").GetString());
    Assert.Equal("-1200.000000", row.GetProperty("buyerAmount").GetString());
    Assert.Equal("1200.000000", row.GetProperty("matchedAmount").GetString());
    Assert.Equal("0.123456", row.GetProperty("difference").GetString());
    Assert.Equal("ASSETS.IC_AR", row.GetProperty("sellerTaxonomyCode").GetString());
    Assert.Equal("LIABILITIES.IC_AP", row.GetProperty("buyerTaxonomyCode").GetString());
    Assert.Equal("APPROVED", row.GetProperty("status").GetString());
    Assert.Equal(fixture.Seed.Staff.Id, row.GetProperty("createdByUserId").GetGuid());
    Assert.Equal(fixture.Seed.Reviewer.Id, row.GetProperty("reviewedByUserId").GetGuid());
    Assert.NotEqual(JsonValueKind.Null, row.GetProperty("reviewedAt").ValueKind);
    var unresolved = await Read(preparer, matchesUrl + "?page=1&pageSize=50&unresolvedOnly=true");
    Assert.Equal(0, unresolved.GetProperty("totalCount").GetInt32());

    using var secondSubmitted = await Post(preparer, preparerCsrf, matchesUrl, Match("IC-PAIR-002"));
    Assert.Equal(HttpStatusCode.OK, secondSubmitted.StatusCode);
    var secondMatchId = JsonDocument.Parse(await secondSubmitted.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    using var wrongGroupFactory = Factory(pg, wrongGroupReviewer);
    using var wrongGroup = wrongGroupFactory.CreateClient();
    var wrongGroupCsrf = await SignIn(wrongGroup);
    using var deniedReview = await Post(wrongGroup, wrongGroupCsrf,
      $"/api/ui/consolidation/intercompany-matches/{secondMatchId}/approve",
      new { expectedGroupRevision = 1, reviewed = true });
    Assert.Equal(HttpStatusCode.Forbidden, deniedReview.StatusCode);

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var group = await db.ClientGroups.SingleAsync(x => x.FirmId == fixture.Seed.FirmId && x.Id == fixture.GroupId);
      group.Revision = 2;
      await db.SaveChangesAsync();
    }
    using var staleApproval = await Post(reviewer, reviewerCsrf,
      $"/api/ui/consolidation/intercompany-matches/{secondMatchId}/approve",
      new { expectedGroupRevision = 1, reviewed = true });
    Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
    using var staleSubmission = await Post(preparer, preparerCsrf, matchesUrl, Match("IC-PAIR-003", expectedGroupRevision: 2));
    Assert.Equal(HttpStatusCode.Conflict, staleSubmission.StatusCode);
    var changedRevision = await Read(preparer, scopeUrl + "/intercompany-match-options");
    Assert.Equal(1, changedRevision.GetProperty("pinnedGroupRevision").GetInt64());
    Assert.Equal(2, changedRevision.GetProperty("currentGroupRevision").GetInt64());

    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(2, await proof.IntercompanyMatches.CountAsync(x => x.ScopeVersionId == fixture.ScopeId));
    Assert.Empty(await proof.ConsolidationJournals.Where(x => x.ScopeVersionId == fixture.ScopeId).ToListAsync());
    Assert.Equal(operationalJournalCountBefore, await proof.ClientOperationalJournals.CountAsync());
  }

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
    var periodId = Guid.CreateVersion7();
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
    db.ClientReportingPeriods.Add(new ClientReportingPeriod
    {
      Id = periodId, FirmId = seed.FirmId, ClientId = seed.ClientId, PeriodCode = "FY2026",
      StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
      Basis = "ANNUAL", Currency = "QAR", Status = AccountingWorkflowStates.Active,
      CreatedByUserId = seed.Staff.Id, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ConsolidationScopeVersions.Add(new ConsolidationScopeVersion
    {
      Id = scopeId, FirmId = seed.FirmId, GroupId = groupId, PeriodId = periodId, Version = 1,
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
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.RoleGrants.Add(PbcSeed.Grant(seed.FirmId, seed.Staff, "Manager"));
      await db.SaveChangesAsync();
    }
    using var factory = Factory(pg, seed.Staff);
    using var client = factory.CreateClient();
    var csrf = await SignIn(client);
    using var createdGroup = await Post(client, csrf, "/api/ui/consolidation/groups",
      new { code = "SCOPE-OPT-01", name = "Scope Options Group" });
    Assert.Equal(HttpStatusCode.OK, createdGroup.StatusCode);
    var groupId = JsonDocument.Parse(await createdGroup.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    var periodId = Guid.CreateVersion7();
    var outsideClientId = Guid.CreateVersion7();
    var outsidePeriodId = Guid.CreateVersion7();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.PracticeClients.Add(new PracticeClient
      {
        Id = outsideClientId, FirmId = seed.FirmId, LegalName = "Outside scope options client", CreatedAt = DateTimeOffset.UtcNow
      });
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
      db.ClientReportingPeriods.Add(new ClientReportingPeriod
      {
        Id = outsidePeriodId, FirmId = seed.FirmId, ClientId = outsideClientId, PeriodCode = "FY2026-OUTSIDE",
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

    Assert.DoesNotContain(options.GetProperty("periods").EnumerateArray(),
      x => x.GetProperty("id").GetGuid() == outsidePeriodId);

    object ScopeRequest(Guid selectedPeriodId, long expectedGroupRevision) => new
    {
      groupId, periodId = selectedPeriodId, reportingCurrency = "QAR", method = ConsolidationCalculator.RestrictedMethod,
      openingBasis = "INITIAL_CONSOLIDATION", exchangeRateSetVersionId = (Guid?)null,
      translationPolicyVersionId = (Guid?)null, translationRateDate = (DateOnly?)null,
      translationRateType = (string?)null, priorScopeVersionId = (Guid?)null, expectedGroupRevision
    };

    using var unpinned = await Post(client, csrf, "/api/ui/consolidation/scopes", ScopeRequest(periodId, 0));
    Assert.Equal(HttpStatusCode.BadRequest, unpinned.StatusCode);
    using var outOfGroupPeriod = await Post(client, csrf, "/api/ui/consolidation/scopes", ScopeRequest(outsidePeriodId, groupRevision));
    Assert.Equal(HttpStatusCode.BadRequest, outOfGroupPeriod.StatusCode);

    using var scopeResponse = await Post(client, csrf, "/api/ui/consolidation/scopes", ScopeRequest(periodId, groupRevision));
    Assert.Equal(HttpStatusCode.OK, scopeResponse.StatusCode);
    var scopeId = JsonDocument.Parse(await scopeResponse.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    await using var proof = new AuditSphereDbContext(pg.Options);
    var saved = await proof.ConsolidationScopeVersions.SingleAsync(x => x.Id == scopeId);
    Assert.Equal(1, saved.Version);
    Assert.Equal(groupRevision, saved.GroupRevision);
    Assert.Null(saved.ExchangeRateSetVersionId);
    Assert.Null(saved.TranslationPolicyVersionId);
    Assert.Null(saved.PriorScopeVersionId);

    var pagedWorkspace = await Read(client,
      $"/api/ui/consolidation/scopes/{scopeId}?eligiblePackagesPage=2&journalsPage=3&runsPage=4&componentsPage=2&readinessPage=2&selectedComponentId={Guid.NewGuid()}");
    foreach (var (section, requestedPage) in new[] { ("eligiblePackages", 2), ("journals", 3), ("runs", 4) })
    {
      var history = pagedWorkspace.GetProperty(section);
      Assert.Equal(requestedPage, history.GetProperty("page").GetInt32());
      Assert.Equal(100, history.GetProperty("pageSize").GetInt32());
      Assert.Empty(history.GetProperty("items").EnumerateArray());
    }
    foreach (var section in new[] { "componentPaging", "readinessPaging" })
    {
      Assert.Equal(2, pagedWorkspace.GetProperty(section).GetProperty("page").GetInt32());
      Assert.Equal(100, pagedWorkspace.GetProperty(section).GetProperty("pageSize").GetInt32());
    }
    Assert.Equal(0, pagedWorkspace.GetProperty("componentPaging").GetProperty("totalCount").GetInt32());
    Assert.Equal(1, pagedWorkspace.GetProperty("readinessPaging").GetProperty("totalCount").GetInt32());
    Assert.Empty(pagedWorkspace.GetProperty("components").EnumerateArray());
    Assert.True(pagedWorkspace.GetProperty("componentReadiness").GetProperty("available").GetBoolean());
    Assert.Empty(pagedWorkspace.GetProperty("componentReadiness").GetProperty("value").EnumerateArray());
    Assert.Equal(JsonValueKind.Null, pagedWorkspace.GetProperty("resolvedComponent").ValueKind);
    foreach (var parameter in new[] { "eligiblePackagesPage", "journalsPage", "runsPage", "componentsPage", "readinessPage" })
    {
      using var invalidPage = await client.GetAsync($"/api/ui/consolidation/scopes/{scopeId}?{parameter}=0");
      Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    foreach (var parameter in new[] { "componentsPage", "readinessPage" })
    foreach (var invalid in new[] { -1, int.MaxValue })
    {
      using var invalidPage = await client.GetAsync($"/api/ui/consolidation/scopes/{scopeId}?{parameter}={invalid}");
      Assert.Equal(HttpStatusCode.BadRequest, invalidPage.StatusCode);
    }

    using var stale = await Post(client, csrf, "/api/ui/consolidation/scopes", new
    {
      groupId, periodId, reportingCurrency = "QAR", method = ConsolidationCalculator.RestrictedMethod,
      openingBasis = "INITIAL_CONSOLIDATION", exchangeRateSetVersionId = (Guid?)null,
      translationPolicyVersionId = (Guid?)null, translationRateDate = (DateOnly?)null,
      translationRateType = (string?)null, priorScopeVersionId = (Guid?)null, expectedGroupRevision = groupRevision + 1
    });
    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

    var concurrentCreates = await Task.WhenAll(
      Post(client, csrf, "/api/ui/consolidation/scopes", ScopeRequest(periodId, groupRevision)),
      Post(client, csrf, "/api/ui/consolidation/scopes", ScopeRequest(periodId, groupRevision)));
    try
    {
      Assert.All(concurrentCreates, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
      var createdScopeIds = new Guid[concurrentCreates.Length];
      for (var i = 0; i < concurrentCreates.Length; i++)
      {
        using var createdJson = JsonDocument.Parse(await concurrentCreates[i].Content.ReadAsStringAsync());
        createdScopeIds[i] = createdJson.RootElement.GetProperty("value").GetGuid();
      }
      var allocatedVersions = await proof.ConsolidationScopeVersions.AsNoTracking()
        .Where(x => createdScopeIds.Contains(x.Id)).OrderBy(x => x.Version).Select(x => x.Version).ToArrayAsync();
      Assert.Equal(new[] { 2, 3 }, allocatedVersions);
    }
    finally
    {
      foreach (var response in concurrentCreates) response.Dispose();
    }
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

  [Fact]
  public async Task ExternalComponentPacks_PageEveryLineAndSerializeConcurrentReviewWithRetainedLineage()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-CONSOL-EXTERNAL-PACKS");
    var fixture = await SeedOwnershipAsync(pg);
    var wrongGroupReviewer = new AppUser
    {
      Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, Email = "pack-wrong-group@auditsphere.test",
      DisplayName = "Wrong Group Pack Reviewer", TenantId = "tenant-001", Subject = "sub-wrong-group-pack-reviewer"
    };
    var otherGroupId = Guid.CreateVersion7();
    var reviewerGrantId = Guid.CreateVersion7();
    var futureEngagementId = Guid.CreateVersion7();
    var childEngagementId = Guid.CreateVersion7();
    int operationalJournalCountBefore;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Users.Add(wrongGroupReviewer);
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = reviewerGrantId, FirmId = fixture.Seed.FirmId, GroupId = fixture.GroupId,
        UserId = fixture.Seed.Reviewer.Id, Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = fixture.Seed.Staff.Id
      });
      foreach (var (engagementId, clientId) in new[]
        {
          (futureEngagementId, fixture.FutureClientId),
          (childEngagementId, fixture.ChildClientId)
        })
        db.Engagements.Add(new AuditSphereOps.Domain.Engagements.Engagement
        {
          Id = engagementId, FirmId = fixture.Seed.FirmId, PracticeClientId = clientId,
          PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31",
          Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
        });
      db.ClientGroups.Add(new ClientGroup
      {
        Id = otherGroupId, FirmId = fixture.Seed.FirmId, Code = "OTHER-PACK-REVIEW-GROUP",
        Name = "Other Pack Review Group", Revision = 1, CreatedByUserId = fixture.Seed.Staff.Id,
        CreatedAt = DateTimeOffset.UtcNow
      });
      db.GroupAccessGrants.Add(new GroupAccessGrant
      {
        Id = Guid.CreateVersion7(), FirmId = fixture.Seed.FirmId, GroupId = otherGroupId,
        UserId = wrongGroupReviewer.Id, Role = "AccountingReviewer", GrantedAt = DateTimeOffset.UtcNow,
        GrantedByUserId = fixture.Seed.Staff.Id
      });
      await db.SaveChangesAsync();
      operationalJournalCountBefore = await db.ClientOperationalJournals.CountAsync();
    }

    using var preparerFactory = Factory(pg, fixture.Seed.Staff);
    using var preparer = preparerFactory.CreateClient();
    var preparerCsrf = await SignIn(preparer);
    using var reviewerFactory = Factory(pg, fixture.Seed.Reviewer);
    using var reviewer = reviewerFactory.CreateClient();
    var reviewerCsrf = await SignIn(reviewer);
    using var wrongGroupFactory = Factory(pg, wrongGroupReviewer);
    using var wrongGroup = wrongGroupFactory.CreateClient();
    var wrongGroupCsrf = await SignIn(wrongGroup);

    var scopeUrl = $"/api/ui/consolidation/scopes/{fixture.ScopeId}";
    var packsUrl = scopeUrl + "/external-packs";
    var sourceLines = Enumerable.Range(1, 205).Select(index => (object)new
    {
      taxonomyCode = "TEST.PACK.LINE",
      amount = "0.000001",
      currency = "QAR",
      sourceLineReference = $"SOURCE-{index:D3}"
    }).ToArray();
    var normalizedManifest = string.Join('\n', Enumerable.Range(1, 205)
      .Select(index => $"TEST.PACK.LINE|0.000001|QAR|SOURCE-{index:D3}"));
    var normalizedDigest = Hashing.Sha256Hex(normalizedManifest);
    string RawSourceHash(string sourceVersion) => Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      sourceVersion,
      lines = sourceLines
    }));
    object PackInput(string sourceVersion, Guid? priorPackId, string? normalizedDigestOverride = null,
      long expectedGroupRevision = 1, Guid? clientId = null, Guid? engagementId = null) => new
    {
      clientId = clientId ?? fixture.Seed.ClientId,
      engagementId = engagementId ?? fixture.Seed.EngagementId,
      periodStart = "2026-01-01",
      periodEnd = "2026-12-31",
      framework = "IFRS",
      reportingCurrency = "QAR",
      periodBasis = "STATUTORY",
      taxonomyVersion = "taxonomy-v1",
      mappingVersion = "mapping-v1",
      sourceReference = $"source-evidence-{sourceVersion}",
      rawSourceHash = RawSourceHash(sourceVersion),
      normalizedSourceDigest = normalizedDigestOverride ?? normalizedDigest,
      declaredSignedTotal = "0.000205",
      lines = sourceLines,
      priorPackId,
      compatibilityBridgeReference = "",
      expectedGroupRevision,
      reviewed = true
    };

    var options = await Read(preparer,
      packsUrl + $"?clientPage=1&clientPageSize=1&selectedClientId={fixture.Seed.ClientId}" +
      $"&engagementPage=1&engagementPageSize=1&selectedEngagementId={fixture.Seed.EngagementId}");
    Assert.True(options.GetProperty("canSubmitPack").GetBoolean());
    Assert.Equal(1, options.GetProperty("eligibleClientPageSize").GetInt32());
    Assert.Equal(2, options.GetProperty("eligibleClientTotalCount").GetInt32());
    Assert.Equal(1, options.GetProperty("eligibleClients").GetArrayLength());
    Assert.Equal(fixture.Seed.ClientId, options.GetProperty("resolvedClient").GetProperty("clientId").GetGuid());
    Assert.Equal(fixture.Seed.EngagementId, options.GetProperty("resolvedEngagement").GetProperty("engagementId").GetGuid());
    var firstEligibleClientId = options.GetProperty("eligibleClients")[0].GetProperty("clientId").GetGuid();
    var secondOptionPage = await Read(preparer, packsUrl + "?clientPage=2&clientPageSize=1");
    Assert.Equal(2, secondOptionPage.GetProperty("eligibleClientPage").GetInt32());
    Assert.Equal(2, secondOptionPage.GetProperty("eligibleClientTotalCount").GetInt32());
    Assert.NotEqual(firstEligibleClientId,
      secondOptionPage.GetProperty("eligibleClients")[0].GetProperty("clientId").GetGuid());
    Assert.NotEqual(fixture.FutureClientId, firstEligibleClientId);
    Assert.NotEqual(fixture.FutureClientId,
      secondOptionPage.GetProperty("eligibleClients")[0].GetProperty("clientId").GetGuid());

    using var futureMembership = await Post(preparer, preparerCsrf, packsUrl,
      PackInput("future-membership", null, clientId: fixture.FutureClientId, engagementId: futureEngagementId));
    Assert.Equal(HttpStatusCode.BadRequest, futureMembership.StatusCode);
    await using (var futureProof = new AuditSphereDbContext(pg.Options))
      Assert.False(await futureProof.ExternalComponentPacks.AnyAsync(x =>
        x.ScopeVersionId == fixture.ScopeId && x.ClientId == fixture.FutureClientId));

    using var staleRevision = await Post(preparer, preparerCsrf, packsUrl, PackInput("stale-revision", null,
      expectedGroupRevision: 2));
    Assert.Equal(HttpStatusCode.Conflict, staleRevision.StatusCode);
    using var mismatchedDigest = await Post(preparer, preparerCsrf, packsUrl, PackInput("invalid-digest", null, new string('0', 64)));
    Assert.Equal(HttpStatusCode.BadRequest, mismatchedDigest.StatusCode);
    using var submitted = await Post(preparer, preparerCsrf, packsUrl, PackInput("v1", null));
    Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
    var firstPackId = JsonDocument.Parse(await submitted.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    var history = await Read(preparer, packsUrl + "?page=1&pageSize=25");
    Assert.Equal(1, history.GetProperty("totalCount").GetInt32());
    Assert.Equal("0.000205", history.GetProperty("items")[0].GetProperty("declaredSignedTotal").GetString());
    Assert.Equal(RawSourceHash("v1"), history.GetProperty("items")[0].GetProperty("rawSourceHash").GetString());

    async Task<JsonElement> ReadLines(int linePage) => await Read(preparer,
      $"{packsUrl}/{firstPackId}?linePage={linePage}&linePageSize=100");
    var firstLines = await ReadLines(1);
    Assert.Equal(205, firstLines.GetProperty("totalLineCount").GetInt32());
    Assert.Equal(1, firstLines.GetProperty("linePage").GetInt32());
    Assert.False(firstLines.GetProperty("hasPreviousLines").GetBoolean());
    Assert.True(firstLines.GetProperty("hasNextLines").GetBoolean());
    Assert.Equal(100, firstLines.GetProperty("lines").GetArrayLength());
    Assert.Equal("0.000001", firstLines.GetProperty("lines")[0].GetProperty("amount").GetString());
    var middleLines = await ReadLines(2);
    Assert.True(middleLines.GetProperty("hasPreviousLines").GetBoolean());
    Assert.True(middleLines.GetProperty("hasNextLines").GetBoolean());
    Assert.Equal(100, middleLines.GetProperty("lines").GetArrayLength());
    var finalLines = await ReadLines(3);
    Assert.True(finalLines.GetProperty("hasPreviousLines").GetBoolean());
    Assert.False(finalLines.GetProperty("hasNextLines").GetBoolean());
    Assert.Equal(5, finalLines.GetProperty("lines").GetArrayLength());
    Assert.Equal("SOURCE-205", finalLines.GetProperty("lines")[4].GetProperty("sourceLineReference").GetString());

    using var selfReview = await Post(preparer, preparerCsrf, $"{packsUrl}/{firstPackId}/reconcile", new
    {
      expectedGroupRevision = 1, reviewed = true, evidenceReference = "SIGNED-SOURCE-RECONCILIATION"
    });
    Assert.Equal(HttpStatusCode.BadRequest, selfReview.StatusCode);
    using var wrongGroupReview = await Post(wrongGroup, wrongGroupCsrf,
      $"{packsUrl}/{firstPackId}/reconcile", new
      {
        expectedGroupRevision = 1, reviewed = true, evidenceReference = "WRONG-GROUP-REVIEW"
      });
    Assert.Equal(HttpStatusCode.Forbidden, wrongGroupReview.StatusCode);

    using var returned = await Post(reviewer, reviewerCsrf, $"{packsUrl}/{firstPackId}/return", new
    {
      expectedGroupRevision = 1, reviewed = true, reason = "Retain this version and resubmit corrected source evidence."
    });
    Assert.Equal(HttpStatusCode.OK, returned.StatusCode);
    using var resubmitted = await Post(preparer, preparerCsrf, packsUrl, PackInput("v2", firstPackId));
    Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
    var secondPackId = JsonDocument.Parse(await resubmitted.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    var secondDetail = await Read(preparer, $"{packsUrl}/{secondPackId}?linePage=1&linePageSize=100");
    Assert.Equal(firstPackId, secondDetail.GetProperty("pack").GetProperty("priorPackId").GetGuid());
    Assert.Equal(2, secondDetail.GetProperty("pack").GetProperty("version").GetInt32());
    using var reconciled = await Post(reviewer, reviewerCsrf, $"{packsUrl}/{secondPackId}/reconcile", new
    {
      expectedGroupRevision = 1, reviewed = true, evidenceReference = "SIGNED-SOURCE-RECONCILIATION-V2"
    });
    Assert.Equal(HttpStatusCode.OK, reconciled.StatusCode);
    var reconciledDetail = await Read(preparer, $"{packsUrl}/{secondPackId}?linePage=1&linePageSize=100");
    Assert.False(reconciledDetail.GetProperty("pack").GetProperty("canReconcile").GetBoolean());
    Assert.False(reconciledDetail.GetProperty("pack").GetProperty("canApprove").GetBoolean());
    var reviewerReconciledDetail = await Read(reviewer, $"{packsUrl}/{secondPackId}?linePage=1&linePageSize=100");
    Assert.True(reviewerReconciledDetail.GetProperty("pack").GetProperty("canApprove").GetBoolean());

    // Hold the group fence until both independent decisions are waiting on it.
    await using var blocker = new AuditSphereDbContext(pg.Options);
    await using var observer = new AuditSphereDbContext(pg.Options);
    await using var fence = await blocker.Database.BeginTransactionAsync();
    await blocker.ClientGroups.FromSqlInterpolated($"""
      SELECT * FROM client_groups WHERE id = {fixture.GroupId} AND firm_id = {fixture.Seed.FirmId} FOR UPDATE
      """).SingleAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var concurrentReturn = Post(reviewer, reviewerCsrf, $"{packsUrl}/{secondPackId}/return", new
    {
      expectedGroupRevision = 1, reviewed = true, reason = "Independent return decision in race."
    });
    var concurrentApprove = Post(reviewer, reviewerCsrf, $"{packsUrl}/{secondPackId}/approve", new
    {
      expectedGroupRevision = 1, reviewed = true
    });
    try
    {
      await WaitForOwnershipGroupWaitersAsync(observer, 2, timeout.Token);
      await fence.CommitAsync();
    }
    finally
    {
      if (blocker.Database.CurrentTransaction is not null)
        await fence.RollbackAsync();
    }
    using var raceReturn = await concurrentReturn;
    using var raceApprove = await concurrentApprove;
    Assert.NotEqual(raceReturn.IsSuccessStatusCode, raceApprove.IsSuccessStatusCode);

    var currentPackId = secondPackId;
    if (raceReturn.IsSuccessStatusCode)
    {
      var returnedDetail = await Read(preparer, $"{packsUrl}/{secondPackId}?linePage=1&linePageSize=100");
      Assert.Equal("RETURNED", returnedDetail.GetProperty("pack").GetProperty("status").GetString());
      Assert.Equal("RECONCILED", returnedDetail.GetProperty("pack").GetProperty("reconciliationStatus").GetString());
      Assert.Equal("SIGNED-SOURCE-RECONCILIATION-V2",
        returnedDetail.GetProperty("pack").GetProperty("reconciliationReference").GetString());
      using var nextVersion = await Post(preparer, preparerCsrf, packsUrl, PackInput("v3", secondPackId));
      Assert.Equal(HttpStatusCode.OK, nextVersion.StatusCode);
      currentPackId = JsonDocument.Parse(await nextVersion.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
      using var reconciledAgain = await Post(reviewer, reviewerCsrf, $"{packsUrl}/{currentPackId}/reconcile", new
      {
        expectedGroupRevision = 1, reviewed = true, evidenceReference = "SIGNED-SOURCE-RECONCILIATION-V3"
      });
      Assert.Equal(HttpStatusCode.OK, reconciledAgain.StatusCode);
      using var approvedAgain = await Post(reviewer, reviewerCsrf, $"{packsUrl}/{currentPackId}/approve", new
      {
        expectedGroupRevision = 1, reviewed = true
      });
      Assert.Equal(HttpStatusCode.OK, approvedAgain.StatusCode);
    }
    else
    {
      Assert.Equal(HttpStatusCode.OK, raceApprove.StatusCode);
      var approvedDetail = await Read(preparer, $"{packsUrl}/{secondPackId}?linePage=1&linePageSize=100");
      Assert.Equal("APPROVED", approvedDetail.GetProperty("pack").GetProperty("status").GetString());
      Assert.Equal(fixture.Seed.Reviewer.Id, approvedDetail.GetProperty("pack").GetProperty("approvedByUserId").GetGuid());
    }

    using var pinned = await Post(preparer, preparerCsrf, scopeUrl + "/external-components", new
    {
      externalComponentPackId = currentPackId,
      ownershipPercent = "100.000000",
      controlMethod = "CONTROLLED",
      expectedGroupRevision = 1,
      reviewed = true
    });
    Assert.Equal(HttpStatusCode.OK, pinned.StatusCode);
    var componentId = JsonDocument.Parse(await pinned.Content.ReadAsStringAsync()).RootElement.GetProperty("value").GetGuid();
    using var componentApproved = await Post(reviewer, reviewerCsrf,
      $"/api/ui/consolidation/components/{componentId}/approve", new { });
    Assert.Equal(HttpStatusCode.OK, componentApproved.StatusCode);

    await using var proof = new AuditSphereDbContext(pg.Options);
    var component = await proof.ConsolidationComponents.AsNoTracking().SingleAsync(x => x.Id == componentId);
    Assert.Null(component.PackageId);
    Assert.Equal(currentPackId, component.ExternalComponentPackId);
    Assert.Equal(100m, component.OwnershipPercent);
    Assert.Equal("CONTROLLED", component.ControlMethod);
    Assert.Equal(operationalJournalCountBefore, await proof.ClientOperationalJournals.CountAsync());

    using var authFencePack = await Post(preparer, preparerCsrf, packsUrl,
      PackInput("auth-fence", null, clientId: fixture.ChildClientId, engagementId: childEngagementId));
    Assert.Equal(HttpStatusCode.OK, authFencePack.StatusCode);
    var authFencePackId = JsonDocument.Parse(await authFencePack.Content.ReadAsStringAsync())
      .RootElement.GetProperty("value").GetGuid();
    var reviewerEpochBefore = await proof.Users.AsNoTracking()
      .Where(x => x.Id == fixture.Seed.Reviewer.Id).Select(x => x.SessionEpoch).SingleAsync();

    async Task WaitForFirmWaiters(int expected, CancellationToken ct)
    {
      while (true)
      {
        var count = await observer.Database.SqlQueryRaw<int>(
          "SELECT count(*)::int AS \"Value\" FROM pg_stat_activity WHERE datname=current_database() AND pid<>pg_backend_pid() " +
          "AND wait_event_type='Lock' AND query LIKE '%firm_safety_states%'").SingleAsync(ct);
        if (count >= expected) return;
        await Task.Delay(25, ct);
      }
    }

    await using var firmBlocker = new AuditSphereDbContext(pg.Options);
    await using var firmFence = await firmBlocker.Database.BeginTransactionAsync();
    await firmBlocker.FirmSafetyStates.FromSqlInterpolated($"""
      SELECT * FROM firm_safety_states WHERE id = {fixture.Seed.FirmId} FOR UPDATE
      """).AsNoTracking().SingleAsync();
    await using var revoker = new AuditSphereDbContext(pg.Options);
    using var authFenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    // Queue the real exclusive revocation first, then the pack command's shared fence.
    // Revocation must persist the grant and epoch before releasing its firm lock.
    var revocation = AuditSphereOps.Application.Security.RoleAssignmentService.RevokeGroupGrantAsync(
      revoker, PbcSeed.Actor(fixture.Seed.Admin, "Administrator"), reviewerGrantId,
      "Revoke external-pack review while its command is waiting on the firm fence.",
      DateTimeOffset.UtcNow, authFenceTimeout.Token);
    Task<HttpResponseMessage>? waitingReconciliation = null;
    try
    {
      await WaitForFirmWaiters(1, authFenceTimeout.Token);
      waitingReconciliation = Post(reviewer, reviewerCsrf, $"{packsUrl}/{authFencePackId}/reconcile", new
      {
        expectedGroupRevision = 1, reviewed = true, evidenceReference = "MUST-NOT-PERSIST-AFTER-REVOCATION"
      });
      await WaitForFirmWaiters(2, authFenceTimeout.Token);
      await firmFence.CommitAsync();
    }
    finally
    {
      if (firmBlocker.Database.CurrentTransaction is not null)
        await firmFence.RollbackAsync();
    }
    var revoked = await revocation;
    Assert.True(revoked.Succeeded, revoked.Message);
    using var refusedReconciliation = await waitingReconciliation!;
    Assert.Equal(HttpStatusCode.Forbidden, refusedReconciliation.StatusCode);
    var retainedAuthFencePack = await proof.ExternalComponentPacks.AsNoTracking()
      .SingleAsync(x => x.Id == authFencePackId);
    Assert.Equal(ExternalComponentPackStates.Submitted, retainedAuthFencePack.Status);
    Assert.Equal(ExternalComponentReconciliationStates.Pending, retainedAuthFencePack.ReconciliationStatus);
    Assert.Equal(string.Empty, retainedAuthFencePack.ReconciliationReference);
    Assert.Equal(0m, retainedAuthFencePack.ReconciledSignedTotal);
    Assert.Null(retainedAuthFencePack.ReconciledByUserId);
    Assert.Null(retainedAuthFencePack.ReconciledAt);
    Assert.NotNull((await proof.GroupAccessGrants.AsNoTracking().SingleAsync(x => x.Id == reviewerGrantId)).RevokedAt);
    Assert.Equal(reviewerEpochBefore + 1, await proof.Users.AsNoTracking()
      .Where(x => x.Id == fixture.Seed.Reviewer.Id).Select(x => x.SessionEpoch).SingleAsync());
    Assert.Equal(operationalJournalCountBefore, await proof.ClientOperationalJournals.CountAsync());
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
