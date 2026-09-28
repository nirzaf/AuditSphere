using AuditSphereOps.Application.Documents;
using System.Text.Json;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Acceptance;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Microsoft365;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

[Trait("Profile", "Database")]
public sealed class PbcRepositoryBindingResolverTests
{
  [Fact]
  public async Task ExactReadyBindingResolves_ChangedScopeOrCapabilityFailsClosed()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var transfer = await PbcSeed.CreateTransferHarnessAsync(pg);
    var scope = new PbcTransferScope(transfer.Fixture.FirmId, transfer.Fixture.ClientId,
      transfer.Fixture.EngagementId, transfer.Staged.UploadIntentId);
    var factory = new OperationContextFactory(new PbcSeed.OptionsDbContextFactory(pg.Options));
    var resolver = new PbcRepositoryBindingResolver(factory);
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scope, CancellationToken.None));
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(
      scope with { ClientId = Guid.NewGuid() }, CancellationToken.None));

    var now = DateTimeOffset.UtcNow;
    var acceptanceId = Guid.NewGuid();
    var connectionId = Guid.NewGuid();
    var templateId = Guid.NewGuid();
    var bindingId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.AcceptanceDecisions.Add(new AcceptanceDecision
      {
        Id = acceptanceId, FirmId = scope.FirmId, PracticeClientId = scope.ClientId,
        Decision = "Accepted", ServiceRoute = "FinancialStatementAudit",
        Rationale = "Synthetic acceptance for repository-boundary test.",
        EvaluationTemplateVersion = "synthetic-v1", EvaluationSnapshotDigest = new string('a', 64),
        DecidedByUserId = transfer.Fixture.Admin.Id, DecidedAt = now
      });
      db.Microsoft365ConnectionRevisions.Add(new Microsoft365ConnectionRevision
      {
        Id = connectionId, FirmId = scope.FirmId, TenantId = "synthetic-tenant",
        LoginClientIdReference = "slot:login", RuntimeCredentialReference = "slot:selected-site",
        State = Microsoft365RevisionStates.Active, ConsentState = "OBSERVED", CreatedAt = now
      });
      db.FolderTemplateVersions.Add(new FolderTemplateVersion
      {
        Id = templateId, FirmId = scope.FirmId, Purpose = FolderTemplatePurposes.ClientWorkspace,
        ManifestJson = "{}", ManifestDigest = new string('b', 64), CreatedAt = now, ApprovedAt = now
      });
      db.FirmWorkspaceConfigurations.Add(new FirmWorkspaceConfiguration
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, ConnectionRevisionId = connectionId,
        FolderTemplateVersionId = templateId, TenantId = "synthetic-tenant",
        SiteId = "site-a", DriveId = "drive-a", RootFolderId = "firm-root",
        DisplayUrl = "https://synthetic.sharepoint.com/sites/audit",
        AccessProfile = Microsoft365AccessProfiles.AppMediated, CreatedAt = now
      });
      db.ClientWorkspaces.Add(new ClientWorkspace
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, PracticeClientId = scope.ClientId,
        AcceptanceDecisionId = acceptanceId, LogicalKey = "client-workspace/" + scope.ClientId,
        State = ClientWorkspaceStates.Ready, ConnectionRevisionId = connectionId,
        FolderTemplateVersionId = templateId, TenantId = "synthetic-tenant",
        SiteId = "site-a", DriveId = "drive-a", RootFolderId = "client-root",
        CreatedAt = now, LastVerifiedAt = now
      });
      var binding = new RepositoryBinding
      {
        Id = bindingId, FirmId = scope.FirmId, ClientId = scope.ClientId,
        EngagementId = scope.EngagementId, TenantId = "synthetic-tenant",
        SiteId = "site-a", DriveId = "drive-a", RootFolderId = "client-root",
        Classification = "working", DesiredAccess = "read-write", ObservedAccess = "read-write",
        CapabilityProfile = "selected-site", CreatedAt = now
      };
      db.RepositoryBindings.Add(binding);
      db.IntegrationCapabilities.Add(new IntegrationCapability
      {
        Id = Guid.NewGuid(), FirmId = scope.FirmId, RepositoryBindingId = bindingId,
        HealthStatus = "VERIFIED", TestedPermissions = JsonSerializer.Serialize(new
        {
          bindingSha256 = PbcRepositoryBindingResolver.BindingDigest(binding),
          operations = new[] { "PBC_UPLOAD", "PBC_READBACK" }
        }),
        TestedAt = now
      });
      await db.SaveChangesAsync();
    }

    var target = await resolver.ResolveAsync(scope, CancellationToken.None);
    Assert.Equal(bindingId, target.RepositoryBindingId);
    Assert.Equal("client-root", target.RootFolderId);
    Assert.Equal("slot:selected-site", target.RuntimeCredentialReference);
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(
      scope with { EngagementId = Guid.NewGuid() }, CancellationToken.None));

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var capability = await db.IntegrationCapabilities.SingleAsync();
      capability.HealthStatus = "BLOCKED";
      await db.SaveChangesAsync();
    }
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scope, CancellationToken.None));

    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var capability = await db.IntegrationCapabilities.SingleAsync();
      capability.HealthStatus = "VERIFIED";
      (await db.RepositoryBindings.SingleAsync()).RootFolderId = "changed-root";
      (await db.ClientWorkspaces.SingleAsync()).RootFolderId = "changed-root";
      await db.SaveChangesAsync();
    }
    await Assert.ThrowsAsync<OperationBlockedException>(() => resolver.ResolveAsync(scope, CancellationToken.None));
  }
}
