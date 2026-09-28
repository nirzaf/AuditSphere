using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// P2 end-to-end harness: two clients (A, B) in one firm, each with a sent PBC request, a verified
/// selected-site repository and the real durable LIVE transfer pipeline claimed by the isolated
/// Acceptance "pbc" worker options. The drive can be the fake Graph server or the live Development site.
/// </summary>
internal sealed class P2PbcHarness : IAsyncDisposable
{
  public PgTestSchema Pg { get; }
  public PbcSeed.Fixture A { get; }
  public PbcSeed.Fixture B { get; }
  public Guid RequestA { get; private set; }
  public Guid RequestB { get; private set; }
  public GraphSelectedSiteDrive Drive { get; }
  public IAuditSphereDbContextFactory Factory { get; }
  public PostgresOperationStore Store { get; }
  public PbcDocumentTransferHandler Handler { get; }
  public GraphPbcProviderSink Sink { get; }
  public AuditSphereOps.Worker.Worker Worker { get; }
  public SelectedSiteLocation Location { get; }
  public ActorContext Admin => PbcSeed.Actor(A.Admin, "Administrator");
  private readonly List<string> stagingRoots = [];

  private P2PbcHarness(PgTestSchema pg, PbcSeed.Fixture a, PbcSeed.Fixture b, GraphSelectedSiteDrive drive, SelectedSiteLocation location)
  {
    Pg = pg; A = a; B = b; Drive = drive; Location = location;
    Factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    Store = new PostgresOperationStore(Factory);
    Sink = new GraphPbcProviderSink(new PbcRepositoryBindingResolver(Factory), drive, Factory);
    Handler = new PbcDocumentTransferHandler(Factory, Sink, PbcDocumentTransferHandler.LiveDefinition);
    var options = new WorkerOptions(a.FirmId, "Acceptance", AllowSimulationAdapters: false, ExternalEffectsEnabled: true,
      Group: PbcDocumentTransferHandler.LiveGroup);
    Worker = new AuditSphereOps.Worker.Worker(new OperationDispatcher(Store, new DurableOperationRegistry([Handler], options), options),
      [new PbcTransferDiscovery(Factory, Store, Handler, options)], NullLogger<AuditSphereOps.Worker.Worker>.Instance);
  }

  public AuditSphereDbContext Db() => new(Pg.Options);

  public static async Task<P2PbcHarness> CreateAsync(GraphSelectedSiteDrive drive, string tenantId, string credentialReference,
    string siteId, string driveId, string rootId, string clientNamePrefix = "P2 TEST")
  {
    var pg = await PgTestSchema.CreateAsync();
    var a = await PbcSeed.SeedAsync(pg);
    var b = await AddSecondClientAsync(pg, a);
    var now = DateTimeOffset.UtcNow;
    var connectionId = Guid.NewGuid();
    var clientTemplateId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      foreach (var (fixture, label) in new[] { (a, "Client A"), (b, "Client B") })
      {
        (await db.PracticeClients.SingleAsync(x => x.Id == fixture.ClientId)).LegalName = $"{clientNamePrefix} {label}";
        var acceptanceId = Guid.NewGuid();
        db.AcceptanceDecisions.Add(new AcceptanceDecision { Id = acceptanceId, FirmId = a.FirmId, PracticeClientId = fixture.ClientId,
          Decision = "Accepted", ServiceRoute = "FinancialStatementAudit", Rationale = "Synthetic P2 acceptance fixture.",
          EvaluationTemplateVersion = "synthetic-v1", EvaluationSnapshotDigest = new string('a', 64), DecidedByUserId = a.Admin.Id, DecidedAt = now });
        db.ClientWorkspaces.Add(new ClientWorkspace { Id = Guid.NewGuid(), FirmId = a.FirmId, PracticeClientId = fixture.ClientId,
          AcceptanceDecisionId = acceptanceId, LogicalKey = "client-workspace/" + fixture.ClientId,
          State = ClientWorkspaceStates.WaitingForIntegration, CreatedAt = now });
      }
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision { Id = connectionId, FirmId = a.FirmId, TenantId = tenantId,
        LoginClientIdReference = "slot:login", RuntimeCredentialReference = credentialReference,
        State = Microsoft365RevisionStates.Active, ConsentState = "VERIFIED", CreatedAt = now });
      foreach (var (purpose, id) in new[] { (FolderTemplatePurposes.ClientWorkspace, clientTemplateId), (FolderTemplatePurposes.EngagementWorkspace, Guid.NewGuid()) })
        db.FolderTemplateVersions.Add(new FolderTemplateVersion { Id = id, FirmId = a.FirmId, Purpose = purpose,
          ManifestJson = Microsoft365ConfigurationService.DefaultManifest(purpose), ManifestDigest = new string('b', 64), CreatedAt = now, ApprovedAt = now });
      db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration { Id = Guid.NewGuid(), FirmId = a.FirmId, ConnectionRevisionId = connectionId,
        FolderTemplateVersionId = clientTemplateId, TenantId = tenantId, SiteId = siteId, DriveId = driveId, RootFolderId = rootId,
        DisplayUrl = "https://example.sharepoint.com/", AccessProfile = Microsoft365AccessProfiles.AppMediated, CreatedAt = now });
      await db.SaveChangesAsync();
    }
    var harness = new P2PbcHarness(pg, a, b, drive, new SelectedSiteLocation(tenantId, siteId, driveId, credentialReference));
    harness.RequestA = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, a, PbcSeed.Actor(a.Staff, "Staff"), PbcSeed.Actor(a.Client, "ClientUser"));
    harness.RequestB = await PbcSeed.CreateSentAcknowledgedRequestAsync(pg, b, PbcSeed.Actor(b.Staff, "Staff"), PbcSeed.Actor(b.Client, "ClientUser"));
    return harness;
  }

  /// <summary>Second client, engagement and client user in the same firm; staff/reviewer also assigned to it.</summary>
  private static async Task<PbcSeed.Fixture> AddSecondClientAsync(PgTestSchema pg, PbcSeed.Fixture a)
  {
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var clientUser = PbcSeed.User(a.FirmId, "Client");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient { Id = clientId, FirmId = a.FirmId, LegalName = "PBC TEST CLIENT B", CreatedAt = DateTimeOffset.UtcNow });
    db.Engagements.Add(new Engagement { Id = engagementId, FirmId = a.FirmId, PracticeClientId = clientId, Status = "Active",
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState { Id = clientId, FirmId = a.FirmId });
    db.Users.Add(clientUser);
    db.RoleGrants.AddRange(
      PbcSeed.Grant(a.FirmId, a.Staff, "Staff", clientId, engagementId),
      PbcSeed.Grant(a.FirmId, a.Reviewer, "Reviewer", clientId, engagementId),
      PbcSeed.Grant(a.FirmId, clientUser, "ClientUser", clientId, engagementId));
    await db.SaveChangesAsync();
    return a with { ClientId = clientId, EngagementId = engagementId, Client = clientUser };
  }

  public async Task ProvisionAsync(PbcSeed.Fixture fixture)
  {
    await using var db = Db();
    var client = await PbcRepositoryProvisioningService.ProvisionClientWorkspaceAsync(db, Admin, Drive, fixture.ClientId, DateTimeOffset.UtcNow);
    if (!client.Succeeded) throw new InvalidOperationException("client provisioning: " + client.Message);
    var repository = await PbcRepositoryProvisioningService.ProvisionEngagementRepositoryAsync(db, Admin, Drive, fixture.EngagementId, DateTimeOffset.UtcNow);
    if (repository.Value?.State != "VERIFIED") throw new InvalidOperationException("repository provisioning: " + (repository.Value?.DiagnosticCode ?? repository.Message));
  }

  /// <summary>Client stages the document through PbcService; staff completes it, which enqueues the LIVE transfer.</summary>
  public async Task<PbcSeed.StagedUpload> UploadAsync(PbcSeed.Fixture fixture, Guid requestId, byte[] content, string fileName = "bank-statements.csv")
  {
    var staged = await PbcSeed.StageUploadAsync(Pg, fixture, PbcSeed.Actor(fixture.Client, "ClientUser"), requestId, content, fileName);
    stagingRoots.Add(staged.StagingRoot);
    await using var db = Db();
    var completed = await PbcService.CompleteUploadAsync(db, PbcSeed.Actor(fixture.Staff, "Staff"),
      new CompletePbcUploadRequest(staged.UploadIntentId, staged.DeclaredSha256Hex), Store, Handler);
    if (!completed.Succeeded) throw new InvalidOperationException("completion: " + completed.ErrorCode);
    return staged;
  }

  public async Task<DurableOperation> OperationAsync(Guid uploadIntentId)
  {
    await using var db = Db();
    return await db.DurableOperations.AsNoTracking().SingleAsync(x => x.TargetId == uploadIntentId && x.OperationKind == PbcDocumentTransferHandler.Kind);
  }

  public async Task MakeDueAsync()
  {
    await using var db = Db();
    await db.Database.ExecuteSqlRawAsync("UPDATE durable_operations SET next_attempt_at = statement_timestamp()");
  }

  public async Task<string?> RemoteItemIdAsync(Guid clientId)
  {
    await using var db = Db();
    return await db.ClientWorkspaces.AsNoTracking().Where(x => x.PracticeClientId == clientId).Select(x => x.RemoteItemId).SingleAsync();
  }

  public PbcTransferScope Scope(PbcSeed.Fixture fixture, Guid uploadIntentId) =>
    new(fixture.FirmId, fixture.ClientId, fixture.EngagementId, uploadIntentId);

  public async ValueTask DisposeAsync()
  {
    foreach (var root in stagingRoots) PbcSeed.DeleteDirectory(root);
    await Pg.DisposeAsync();
  }
}
