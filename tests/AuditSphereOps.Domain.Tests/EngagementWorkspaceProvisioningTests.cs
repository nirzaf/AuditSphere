using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// Automatic workspace provisioning when a Partner activates an engagement, against the fake Graph drive: the exact
/// /Client Name/Engagement Year/01-05 tree from an approved template, exactly one tree per engagement even on repeat
/// runs, deterministic disambiguation instead of merging, evidence identical to the administrator action, and no
/// provisioning before activation.
/// </summary>
[Trait("Profile", "Database")]
public sealed class EngagementWorkspaceProvisioningTests
{
  private const string SteClientManifest = """{"nodes":[]}""";
  private const string SteEngagementManifest = """
    {"nodes":[{"key":"admin","name":"01_Administration & Planning"},{"key":"pbc","name":"02_Trial Balance & Schedules"},{"key":"fieldwork","name":"03_Fieldwork & Testing"},{"key":"drafts","name":"04_Drafts & Deliverables"},{"key":"archive","name":"05_Final Signed Archive"}]}
    """;
  private static readonly string[] SteFolders =
    ["01_Administration & Planning", "02_Trial Balance & Schedules", "03_Fieldwork & Testing", "04_Drafts & Deliverables", "05_Final Signed Archive"];

  private sealed record Rig(P2PbcHarness H, FakeGraphDrive Graph, AuditSphereOps.Worker.Worker Worker, EngagementWorkspaceProvisioningHandler Handler);

  private static async Task<Rig> CreateAsync(bool ste)
  {
    var graph = new FakeGraphDrive();
    var drive = new GraphSelectedSiteDrive(new HttpClient(graph), new FakeSelectedSiteTokens(), new GraphPreauthenticatedTransport(graph), configured: true);
    var h = await P2PbcHarness.CreateAsync(drive, "11111111-1111-1111-1111-111111111111", "slot:selected-site", "site-a", FakeGraphDrive.DriveId, FakeGraphDrive.RootId);
    await using (var db = h.Db())
    {
      foreach (var e in await db.Engagements.Where(x => x.FirmId == h.A.FirmId).ToListAsync())
      {
        e.PeriodStart = "2026-01-01"; e.PeriodEnd = "2026-12-31"; e.ServiceRoute = "FinancialStatementAudit";
      }
      if (ste)
        foreach (var template in await db.FolderTemplateVersions.Where(x => x.FirmId == h.A.FirmId).ToListAsync())
          template.ManifestJson = template.Purpose == FolderTemplatePurposes.ClientWorkspace ? SteClientManifest : SteEngagementManifest;
      await db.SaveChangesAsync();
    }
    var handler = new EngagementWorkspaceProvisioningHandler(h.Factory, drive);
    var options = new WorkerOptions(h.A.FirmId, "Acceptance", AllowSimulationAdapters: false, ExternalEffectsEnabled: true, Group: PbcDocumentTransferHandler.LiveGroup);
    var worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(h.Store, new DurableOperationRegistry([handler], options), options),
      [new EngagementWorkspaceDiscovery(h.Factory, h.Store, handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
    return new(h, graph, worker, handler);
  }

  private static async Task ActivateAsync(P2PbcHarness h, Guid clientId, Guid engagementId)
  {
    await using var db = h.Db();
    var decision = await db.AcceptanceDecisions.AsNoTracking().FirstAsync(x => x.PracticeClientId == clientId && x.Decision == "Accepted");
    db.EngagementActivations.Add(new EngagementActivation
    {
      Id = Guid.NewGuid(), FirmId = h.A.FirmId, PracticeClientId = clientId, EngagementId = engagementId, AcceptanceDecisionId = decision.Id,
      ClientGeneration = 1, AcceptancePath = "NEW_CLIENT", ActivatedByUserId = h.A.Admin.Id, ActivatedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync();
  }

  private static async Task DrainAsync(Rig rig) { while (await rig.Worker.ProcessNextAsync()) { } }

  private static string[] Children(FakeGraphDrive graph, string parentId) =>
    graph.Items.Where(x => x.ParentId == parentId && x.Folder).Select(x => x.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();

  private static (string Id, string Name, string ParentId, bool Folder) Folder(FakeGraphDrive graph, string parentId, string name) =>
    graph.Items.Single(x => x.ParentId == parentId && x.Folder && x.Name == name);

  [Fact]
  public async Task NothingIsProvisioned_UntilThePartnerActivatesTheEngagement()
  {
    var rig = await CreateAsync(ste: true);
    await using var _ = rig.H;
    await DrainAsync(rig);
    Assert.DoesNotContain(rig.Graph.Items, x => x.Folder && x.Id != FakeGraphDrive.RootId);
    await using var db = rig.H.Db();
    Assert.False(await db.DurableOperations.AnyAsync(x => x.OperationKind == EngagementWorkspaceProvisioningHandler.Kind));
  }

  [Fact]
  public async Task ExactSteTree_IsCreatedOnce_ScopedPerClient_AndRepeatRunsNeverDuplicate()
  {
    var rig = await CreateAsync(ste: true);
    await using var _ = rig.H;
    var (h, graph) = (rig.H, rig.Graph);
    await ActivateAsync(h, h.A.ClientId, h.A.EngagementId);
    await ActivateAsync(h, h.B.ClientId, h.B.EngagementId);
    await DrainAsync(rig);

    // /P2 TEST Client A/2026/{01..05}: no Engagements level, no identifier suffixes, exactly five folders.
    var clientA = Folder(graph, FakeGraphDrive.RootId, "P2 TEST Client A");
    Assert.Equal(["2026"], Children(graph, clientA.Id));
    var yearA = Folder(graph, clientA.Id, "2026");
    Assert.Equal(SteFolders.OrderBy(x => x, StringComparer.Ordinal), Children(graph, yearA.Id));
    Assert.Equal(["P2 TEST Client A", "P2 TEST Client B"], Children(graph, FakeGraphDrive.RootId));
    var clientB = Folder(graph, FakeGraphDrive.RootId, "P2 TEST Client B");
    Assert.NotEqual(yearA.Id, Folder(graph, clientB.Id, "2026").Id); // each client has its own tree

    await using (var db = h.Db())
    {
      var operations = await db.DurableOperations.AsNoTracking().Where(x => x.OperationKind == EngagementWorkspaceProvisioningHandler.Kind).ToListAsync();
      Assert.Equal(2, operations.Count);
      Assert.All(operations, x => Assert.Equal((OperationState.COMPLETED, OperationMode.LIVE, "pbc"), (x.Status, x.ExecutionMode, x.ExecutionGroup)));

      // Same evidence as the administrator action: Ready workspace, a binding on the intake folder, a VERIFIED capability.
      var workspace = await db.ClientWorkspaces.AsNoTracking().SingleAsync(x => x.PracticeClientId == h.A.ClientId);
      Assert.Equal((ClientWorkspaceStates.Ready, clientA.Id), (workspace.State, workspace.RemoteItemId));
      var binding = await db.RepositoryBindings.AsNoTracking().SingleAsync(x => x.EngagementId == h.A.EngagementId);
      Assert.Equal(Folder(graph, yearA.Id, "02_Trial Balance & Schedules").Id, binding.RootFolderId);
      Assert.Equal("read-write", binding.ObservedAccess);
      var capability = await db.IntegrationCapabilities.AsNoTracking().SingleAsync(x => x.RepositoryBindingId == binding.Id);
      Assert.Equal("VERIFIED", capability.HealthStatus);
      Assert.Contains(RepositoryBindingDigest.Compute(binding), capability.TestedPermissions);
      // The client cannot see another client's binding.
      Assert.NotEqual(binding.RootFolderId, (await db.RepositoryBindings.AsNoTracking().SingleAsync(x => x.EngagementId == h.B.EngagementId)).RootFolderId);
    }

    // Repeat runs create nothing new: same operations, same folders.
    var before = graph.Items.Count(x => x.Folder);
    await DrainAsync(rig);
    Assert.Equal(before, graph.Items.Count(x => x.Folder));
    await using var verify = h.Db();
    Assert.Equal(2, await verify.DurableOperations.CountAsync(x => x.OperationKind == EngagementWorkspaceProvisioningHandler.Kind));
  }

  [Fact]
  public async Task SecondEngagementOfTheSameYear_AndDuplicateClientNames_AreDisambiguatedNotMerged()
  {
    var rig = await CreateAsync(ste: true);
    await using var _ = rig.H;
    var (h, graph) = (rig.H, rig.Graph);
    var secondEngagement = Guid.NewGuid();
    await using (var db = h.Db())
    {
      db.Engagements.Add(new Engagement
      {
        Id = secondEngagement, FirmId = h.A.FirmId, PracticeClientId = h.A.ClientId, Status = "Active", ProfessionalWorkBlocked = false,
        ServiceRoute = "AccountingOnly", PeriodStart = "2026-01-01", PeriodEnd = "2026-12-31", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(5)
      });
      // Client B trades under the same display name as client A.
      (await db.PracticeClients.SingleAsync(x => x.Id == h.B.ClientId)).CommercialName = "P2 TEST Client A";
      await db.SaveChangesAsync();
    }
    await ActivateAsync(h, h.A.ClientId, h.A.EngagementId);
    await DrainAsync(rig);
    await ActivateAsync(h, h.A.ClientId, secondEngagement);
    await ActivateAsync(h, h.B.ClientId, h.B.EngagementId);
    await DrainAsync(rig);

    var clientA = Folder(graph, FakeGraphDrive.RootId, "P2 TEST Client A");
    Assert.Equal(["2026", "2026 - AccountingOnly"], Children(graph, clientA.Id));
    Assert.Equal(SteFolders.OrderBy(x => x, StringComparer.Ordinal), Children(graph, Folder(graph, clientA.Id, "2026 - AccountingOnly").Id));
    // Client B did not reuse client A's folder: it received its own disambiguated folder.
    var rootChildren = Children(graph, FakeGraphDrive.RootId);
    Assert.Equal(2, rootChildren.Length);
    Assert.Single(rootChildren, x => x == "P2 TEST Client A");
    await using var db2 = h.Db();
    var workspaces = await db2.ClientWorkspaces.AsNoTracking().Where(x => x.FirmId == h.A.FirmId).ToListAsync();
    Assert.Equal(2, workspaces.Select(x => x.RemoteItemId).Distinct().Count());
  }

  [Fact]
  public async Task LegacyTemplates_KeepTheirEngagementsLayout_AndTheSameEvidence()
  {
    var rig = await CreateAsync(ste: false);
    await using var _ = rig.H;
    var (h, graph) = (rig.H, rig.Graph);
    await ActivateAsync(h, h.A.ClientId, h.A.EngagementId);
    await DrainAsync(rig);
    var client = graph.Items.Single(x => x.ParentId == FakeGraphDrive.RootId && x.Folder && x.Name.StartsWith("P2 TEST Client A (", StringComparison.Ordinal));
    var engagements = Folder(graph, client.Id, "Engagements");
    var engagement = graph.Items.Single(x => x.ParentId == engagements.Id && x.Folder);
    Assert.StartsWith("2026-12-31 FinancialStatementAudit (", engagement.Name);
    Assert.Contains("03_PBC_Data_Intake", Children(graph, engagement.Id));
    await using var db = h.Db();
    Assert.Equal("VERIFIED", (await db.IntegrationCapabilities.AsNoTracking().SingleAsync()).HealthStatus);
  }
}
