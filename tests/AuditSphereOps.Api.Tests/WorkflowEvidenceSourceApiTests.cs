using System.Data.Common;
using System.Net;
using System.Text.Json;
using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Audit;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AuditSphereOps.Api.Tests;

public sealed class WorkflowEvidenceSourceApiTests
{
  [Fact]
  public async Task SourcePickerPagesSearchesAndResolvesSelectionWithinOnlyTheExactEngagement()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-EVIDENCE-SOURCES");
    var fixture = await SeedSourcesAsync(pg);
    using var staffFactory = Factory(pg, fixture.Scope.Staff, enabled: true);
    using var staff = staffFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(staff);

    var pbcFirst = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId, WorkflowEvidenceKinds.AcceptedPbc));
    Assert.Equal(fixture.Scope.EngagementId, pbcFirst.Page.EngagementId);
    Assert.Equal(WorkflowEvidenceKinds.AcceptedPbc, pbcFirst.Page.Kind);
    Assert.Equal(0, pbcFirst.Page.Page);
    Assert.Equal(25, pbcFirst.Page.PageSize);
    Assert.True(pbcFirst.Page.HasMore);
    Assert.Equal(fixture.PbcIds.Take(25), pbcFirst.Page.Items.Select(x => x.Id));
    var firstPbc = pbcFirst.Page.Items[0];
    Assert.Equal("Synthetic area 001 — Pending reference 001", firstPbc.Label);
    Assert.Equal(PbcStates.Draft, firstPbc.State);
    Assert.Equal("4", firstPbc.Revision);

    var pbcSecond = await ReadPageAsync(staff,
      PathFor(fixture.Scope.EngagementId, WorkflowEvidenceKinds.AcceptedPbc, page: 1));
    Assert.False(pbcSecond.Page.HasMore);
    Assert.Equal(fixture.PbcIds.Skip(25), pbcSecond.Page.Items.Select(x => x.Id));
    Assert.Equal(fixture.PbcIds, pbcFirst.Page.Items.Concat(pbcSecond.Page.Items).Select(x => x.Id));

    // '%' and '_' are literal search characters, and matching ignores case.
    var pbcLiteral = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.AcceptedPbc, query: "100%_domain"));
    Assert.Equal(fixture.PbcIds[5], Assert.Single(pbcLiteral.Page.Items).Id);

    // The selected row is on another page and does not have to match the current search.
    var pbcOffPage = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.AcceptedPbc, query: "no-such-source", selectedId: fixture.PbcIds[^1]));
    Assert.Empty(pbcOffPage.Page.Items);
    Assert.False(pbcOffPage.Page.HasMore);
    Assert.Equal(fixture.PbcIds[^1], pbcOffPage.Page.Selected!.Id);
    Assert.Equal("4", pbcOffPage.Page.Selected.Revision);

    var foreignPbcSelection = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.AcceptedPbc, selectedId: fixture.SiblingPbcId));
    Assert.Null(foreignPbcSelection.Page.Selected);
    Assert.DoesNotContain(fixture.SiblingPbcId.ToString("D"), foreignPbcSelection.Body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("SIBLING-ONLY PBC MARKER", foreignPbcSelection.Body, StringComparison.Ordinal);

    var procedureFirst = await ReadPageAsync(staff,
      PathFor(fixture.Scope.EngagementId, WorkflowEvidenceKinds.ReviewedProcedure));
    Assert.Equal(WorkflowEvidenceKinds.ReviewedProcedure, procedureFirst.Page.Kind);
    Assert.True(procedureFirst.Page.HasMore);
    Assert.Equal(fixture.ProcedureIds.Take(25), procedureFirst.Page.Items.Select(x => x.Id));
    Assert.Equal("SYN-PROC-001 — Pending procedure 001", procedureFirst.Page.Items[0].Label);
    Assert.Equal(AuditProcedureStatuses.Submitted, procedureFirst.Page.Items[0].State);
    Assert.Equal("17", procedureFirst.Page.Items[0].Revision);
    Assert.Equal(AuditProcedureStatuses.Planned, procedureFirst.Page.Items[1].State);
    Assert.Null(procedureFirst.Page.Items[1].Revision);

    var procedureSecond = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.ReviewedProcedure, page: 1));
    Assert.False(procedureSecond.Page.HasMore);
    Assert.Equal(fixture.ProcedureIds.Skip(25), procedureSecond.Page.Items.Select(x => x.Id));
    Assert.Equal(fixture.ProcedureIds,
      procedureFirst.Page.Items.Concat(procedureSecond.Page.Items).Select(x => x.Id));

    var procedureLiteral = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.ReviewedProcedure, query: "syn-proc-lit%_id"));
    Assert.Equal(fixture.ProcedureIds[5], Assert.Single(procedureLiteral.Page.Items).Id);

    var procedureOffPage = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.ReviewedProcedure, query: "no-such-source", selectedId: fixture.ProcedureIds[^1]));
    Assert.Empty(procedureOffPage.Page.Items);
    Assert.Equal(fixture.ProcedureIds[^1], procedureOffPage.Page.Selected!.Id);

    var foreignProcedureSelection = await ReadPageAsync(staff, PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.ReviewedProcedure, selectedId: fixture.SiblingProcedureId));
    Assert.Null(foreignProcedureSelection.Page.Selected);
    Assert.DoesNotContain(fixture.SiblingProcedureId.ToString("D"), foreignProcedureSelection.Body,
      StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("SIBLING-ONLY PROCEDURE MARKER", foreignProcedureSelection.Body, StringComparison.Ordinal);

    using var siblingScope = await staff.GetAsync(PathFor(fixture.SiblingEngagementId, WorkflowEvidenceKinds.AcceptedPbc));
    var siblingDenied = await siblingScope.Content.ReadAsStringAsync();
    Assert.Equal(HttpStatusCode.Forbidden, siblingScope.StatusCode);
    Assert.DoesNotContain(fixture.SiblingPbcId.ToString("D"), siblingDenied, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("SIBLING-ONLY PBC MARKER", siblingDenied, StringComparison.Ordinal);

    using var clientFactory = Factory(pg, fixture.Scope.Client);
    using var client = clientFactory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var clientDenied = await client.GetAsync(PathFor(fixture.Scope.EngagementId, WorkflowEvidenceKinds.AcceptedPbc));
    var clientDeniedBody = await clientDenied.Content.ReadAsStringAsync();
    Assert.Equal(HttpStatusCode.Forbidden, clientDenied.StatusCode);
    Assert.DoesNotContain(fixture.PbcIds[0].ToString("D"), clientDeniedBody, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Pending reference 001", clientDeniedBody, StringComparison.Ordinal);

    using var invalidPageSize = await staff.GetAsync(PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.AcceptedPbc, pageSize: 51));
    Assert.Equal(HttpStatusCode.BadRequest, invalidPageSize.StatusCode);
    using (var error = JsonDocument.Parse(await invalidPageSize.Content.ReadAsStringAsync()))
      Assert.Equal("request.invalid", error.RootElement.GetProperty("code").GetString());
    using var longSearch = await staff.GetAsync(PathFor(fixture.Scope.EngagementId,
      WorkflowEvidenceKinds.AcceptedPbc, query: new string('x', 121)));
    Assert.Equal(HttpStatusCode.BadRequest, longSearch.StatusCode);
    using (var error = JsonDocument.Parse(await longSearch.Content.ReadAsStringAsync()))
      Assert.Equal("request.invalid", error.RootElement.GetProperty("code").GetString());

    // These are projection-only pending rows: source status is exposed as stored and no approval is created.
    await using var proof = new AuditSphereDbContext(pg.Options);
    Assert.Equal(fixture.PbcIds.Length, await proof.PbcRequests.CountAsync(x => x.EngagementId == fixture.Scope.EngagementId));
    Assert.All(await proof.PbcRequests.Where(x => x.EngagementId == fixture.Scope.EngagementId).ToListAsync(),
      x => Assert.Equal(PbcStates.Draft, x.State));
    Assert.Equal(fixture.ProcedureIds.Length,
      await proof.AuditProcedures.CountAsync(x => x.EngagementId == fixture.Scope.EngagementId));
    var procedures = await proof.AuditProcedures.Where(x => x.EngagementId == fixture.Scope.EngagementId)
      .OrderBy(x => x.Id).ToListAsync();
    Assert.Equal(AuditProcedureStatuses.Submitted, procedures[0].Status);
    Assert.All(procedures.Skip(1), x => Assert.Equal(AuditProcedureStatuses.Planned, x.Status));
    var submitted = Assert.Single(await proof.AuditProcedureResults.ToListAsync());
    Assert.Equal(fixture.ProcedureIds[0], submitted.AuditProcedureId);
    Assert.Equal(17L, submitted.Revision);
    Assert.Equal(AuditProcedureResultStatuses.Submitted, submitted.Status);
    Assert.Empty(await proof.AuditProcedureReviews.ToListAsync());
  }

  [Theory]
  [InlineData("grant", HttpStatusCode.Forbidden)]
  [InlineData("session", HttpStatusCode.Conflict)]
  public async Task AuthorizationIsRecheckedAfterSourceRowsAreRead(string revoke, HttpStatusCode expectedStatus)
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-EVIDENCE-SOURCE-REVOKE");
    var fixture = await SeedSourcesAsync(pg, sourceCount: 1);
    var interceptor = new RevokeAfterPbcRead(async () =>
    {
      await using var db = new AuditSphereDbContext(pg.Options);
      if (revoke == "session")
        await db.Users.Where(x => x.Id == fixture.Scope.Staff.Id)
          .ExecuteUpdateAsync(x => x.SetProperty(u => u.SessionEpoch, u => u.SessionEpoch + 1));
      else
        await db.RoleGrants.Where(x => x.UserId == fixture.Scope.Staff.Id && x.Role == "Staff" &&
            x.ClientId == fixture.Scope.ClientId && x.EngagementId == fixture.Scope.EngagementId && x.RevokedAt == null)
          .ExecuteUpdateAsync(x => x.SetProperty(g => g.RevokedAt, DateTimeOffset.UtcNow));
    });
    using var factory = Factory(pg, fixture.Scope.Staff, enabled: true, interceptor: interceptor);
    using var staff = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(staff);
    interceptor.Arm();

    using var response = await staff.GetAsync(PathFor(fixture.Scope.EngagementId, WorkflowEvidenceKinds.AcceptedPbc));
    var body = await response.Content.ReadAsStringAsync();
    Assert.True(interceptor.Fired);
    Assert.Equal(expectedStatus, response.StatusCode);
    if (revoke == "session")
    {
      using var error = JsonDocument.Parse(body);
      Assert.Equal("generation.stale", error.RootElement.GetProperty("code").GetString());
    }
    Assert.DoesNotContain(fixture.PbcIds[0].ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("Pending reference 001", body, StringComparison.Ordinal);
  }

  [Fact]
  public async Task DisabledWorkflowExperienceDoesNotExposeTheSourcePickerRoute()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-WORKFLOW-EVIDENCE-SOURCES-OFF");
    var fixture = await SeedSourcesAsync(pg, sourceCount: 1);
    using var factory = Factory(pg, fixture.Scope.Admin, enabled: false);
    using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
    await SignIn(client);
    using var response = await client.GetAsync(PathFor(fixture.Scope.EngagementId, WorkflowEvidenceKinds.AcceptedPbc));
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  private static async Task<PickerFixture> SeedSourcesAsync(OwnedPostgresDatabase pg, int sourceCount = 27)
  {
    var scope = await PbcSeed.SeedAsync(pg);
    var now = DateTimeOffset.UtcNow;
    var siblingClientId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    var siblingEngagementId = Guid.Parse("10000000-0000-0000-0000-000000000002");
    var siblingStaff = PbcSeed.User(scope.FirmId, "Staff");
    var siblingReviewer = PbcSeed.User(scope.FirmId, "Staff");
    var siblingClient = PbcSeed.User(scope.FirmId, "Client");
    var pbcIds = Enumerable.Range(1, sourceCount).Select(SourceId).ToArray();
    var procedureIds = Enumerable.Range(1, sourceCount).Select(x => SourceId(100 + x)).ToArray();

    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = siblingClientId, FirmId = scope.FirmId, LegalName = "Synthetic sibling client", CreatedAt = now
    });
    db.Engagements.Add(new Engagement
    {
      Id = siblingEngagementId, FirmId = scope.FirmId, PracticeClientId = siblingClientId,
      Status = "Active", ProfessionalWorkBlocked = false, CreatedAt = now
    });
    db.ClientSafetyStates.Add(new ClientSafetyState { Id = siblingClientId, FirmId = scope.FirmId });
    db.Users.AddRange(siblingStaff, siblingReviewer, siblingClient);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(scope.FirmId, siblingStaff, "Staff", siblingClientId, siblingEngagementId),
      PbcSeed.Grant(scope.FirmId, siblingReviewer, "Reviewer", siblingClientId, siblingEngagementId),
      PbcSeed.Grant(scope.FirmId, siblingClient, "ClientUser", siblingClientId, siblingEngagementId));

    db.PbcRequests.AddRange(pbcIds.Select((id, index) => NewPbc(id, scope.FirmId, scope.ClientId,
      scope.EngagementId, scope.Client, scope.Staff, scope.Reviewer, index + 1, now)));
    db.AuditProcedures.AddRange(procedureIds.Select((id, index) => NewProcedure(id, scope.FirmId,
      scope.ClientId, scope.EngagementId, index + 1, now)));
    db.AuditProcedureResults.Add(new AuditProcedureResult
    {
      Id = SourceId(801), FirmId = scope.FirmId, ClientId = scope.ClientId, EngagementId = scope.EngagementId,
      AuditProcedureId = procedureIds[0], Revision = 17, InputGeneration = 1,
      WorkPerformed = "Synthetic pending projection fixture.", StructuredResultJson = "{}",
      EvidenceReferencesJson = "[]", Conclusion = "Pending review fixture.",
      Status = AuditProcedureResultStatuses.Submitted, PreparedByUserId = scope.Staff.Id, SubmittedAt = now
    });
    var siblingPbcId = SourceId(501);
    var siblingProcedureId = SourceId(601);
    db.PbcRequests.Add(NewPbc(siblingPbcId, scope.FirmId, siblingClientId, siblingEngagementId,
      siblingClient, siblingStaff, siblingReviewer, 0, now, "SIBLING-ONLY PBC MARKER"));
    db.AuditProcedures.Add(NewProcedure(siblingProcedureId, scope.FirmId, siblingClientId,
      siblingEngagementId, 0, now, "SIBLING-ONLY PROCEDURE MARKER"));
    await db.SaveChangesAsync();

    return new(scope, siblingClientId, siblingEngagementId, pbcIds, procedureIds,
      siblingPbcId, siblingProcedureId);
  }

  private static PbcRequest NewPbc(Guid id, Guid firmId, Guid clientId, Guid engagementId,
    AppUser clientOwner, AppUser firmOwner, AppUser reviewer, int index, DateTimeOffset now,
    string? objectiveOverride = null) => new()
  {
    Id = id,
    FirmId = firmId,
    ClientId = clientId,
    EngagementId = engagementId,
    Objective = objectiveOverride ?? (index switch
    {
      6 => "Pending reference literal 100%_DoMaIn marker",
      7 => "Pending reference literal 100ABDoMaIn marker",
      _ => $"Pending reference {index:D3}"
    }),
    EntityScope = "Synthetic test entity",
    PeriodStart = "2026-01-01",
    PeriodEnd = "2026-12-31",
    Area = index == 0 ? "Synthetic sibling area" : $"Synthetic area {index:D3}",
    RequestedFormat = "Synthetic projection fixture",
    ControlTotals = string.Empty,
    ClientOwnerUserId = clientOwner.Id,
    FirmOwnerUserId = firmOwner.Id,
    ReviewerUserId = reviewer.Id,
    DueDate = "2027-01-31",
    Confidentiality = "Synthetic test data",
    AcceptanceCriteria = "Projection fixture only; no approval is established.",
    State = PbcStates.Draft,
    Revision = 4,
    CreatedAt = now.AddMinutes(index),
    CreatedByUserId = firmOwner.Id,
    UpdatedAt = now.AddMinutes(index)
  };

  private static AuditProcedure NewProcedure(Guid id, Guid firmId, Guid clientId, Guid engagementId,
    int index, DateTimeOffset now, string? titleOverride = null) => new()
  {
    Id = id,
    FirmId = firmId,
    ClientId = clientId,
    EngagementId = engagementId,
    SourceProcedureId = index switch
    {
      6 => "Syn-ProC-Lit%_ID",
      7 => "Syn-ProC-LitAB_ID",
      _ => $"SYN-PROC-{index:D3}"
    },
    Title = titleOverride ?? $"Pending procedure {index:D3}",
    ApplicabilityStatus = AuditApplicabilityStatuses.Pending,
    Status = index == 1 ? AuditProcedureStatuses.Submitted : AuditProcedureStatuses.Planned,
    CurrentResultRevision = index == 1 ? 17 : 0,
    CreatedAt = now.AddMinutes(index)
  };

  private static Guid SourceId(int value) =>
    Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

  private static string PathFor(Guid engagementId, string kind, int page = 0, int pageSize = 25,
    string? query = null, Guid? selectedId = null)
  {
    var values = new List<string>
    {
      "kind=" + Uri.EscapeDataString(kind),
      "page=" + page.ToString(System.Globalization.CultureInfo.InvariantCulture),
      "pageSize=" + pageSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
    if (query is not null) values.Add("query=" + Uri.EscapeDataString(query));
    if (selectedId is { } id) values.Add("selectedId=" + Uri.EscapeDataString(id.ToString("D")));
    return $"/api/ui/engagements/{engagementId:D}/workflow-evidence-sources?{string.Join("&", values)}";
  }

  private static async Task<(WorkflowEvidenceSourcePage Page, string Body)> ReadPageAsync(HttpClient client, string path)
  {
    using var response = await client.GetAsync(path);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(response.Headers.CacheControl?.NoStore == true);
    var body = await response.Content.ReadAsStringAsync();
    var page = JsonSerializer.Deserialize<WorkflowEvidenceSourcePage>(body,
      new JsonSerializerOptions(JsonSerializerDefaults.Web));
    return (Assert.IsType<WorkflowEvidenceSourcePage>(page), body);
  }

  private static async Task<string> SignIn(HttpClient client)
  {
    await client.GetAsync("/auth/sign-in");
    using var session = await client.GetAsync("/api/ui/session");
    Assert.Equal(HttpStatusCode.OK, session.StatusCode);
    return Uri.UnescapeDataString(session.Headers.GetValues("Set-Cookie")
      .Single(x => x.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal)).Split(';')[0]["XSRF-TOKEN=".Length..]);
  }

  private static StandaloneApiApplicationFactory Factory(OwnedPostgresDatabase pg, AppUser user,
    bool enabled = true, DbCommandInterceptor? interceptor = null)
  {
    var settings = new Dictionary<string, string?>
    {
      ["ConnectionStrings:AuditSphere"] = pg.ConnectionString,
      ["DevelopmentIdentity:Enabled"] = "true",
      ["DevelopmentIdentity:TenantId"] = user.TenantId,
      ["DevelopmentIdentity:Subject"] = user.Subject,
      ["Application:AllowSimulationAdapters"] = "true",
      ["ExternalEffects:Enabled"] = "false",
      ["WorkflowExperience:Enabled"] = enabled.ToString()
    };
    if (interceptor is null) return new StandaloneApiApplicationFactory(settings);
    return new StandaloneApiApplicationFactory(settings, services =>
    {
      services.RemoveAll<IDbContextFactory<AuditSphereDbContext>>();
      services.AddSingleton<IDbContextFactory<AuditSphereDbContext>>(
        new InterceptingContextFactory(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options)
          .AddInterceptors(interceptor).Options));
    });
  }

  private sealed record PickerFixture(PbcSeed.Fixture Scope, Guid SiblingClientId, Guid SiblingEngagementId,
    Guid[] PbcIds, Guid[] ProcedureIds, Guid SiblingPbcId, Guid SiblingProcedureId);

  private sealed class RevokeAfterPbcRead(Func<Task> revoke) : DbCommandInterceptor
  {
    private int armed;
    private int fired;
    public bool Fired => Volatile.Read(ref fired) == 1;
    public void Arm() => Interlocked.Exchange(ref armed, 1);

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command,
      CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
      if (Volatile.Read(ref armed) == 1 &&
          command.CommandText.Contains("pbc_requests", StringComparison.OrdinalIgnoreCase) &&
          Interlocked.CompareExchange(ref fired, 1, 0) == 0)
        await revoke();
      return result;
    }
  }

  private sealed class InterceptingContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }
}
