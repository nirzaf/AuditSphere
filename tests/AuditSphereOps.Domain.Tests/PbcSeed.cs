using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Documents;
using AuditSphereOps.Domain.Engagements;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>Shared PBC test fixture: one firm/client/engagement scope with staff,
/// reviewer and client users plus scoped grants.</summary>
internal static class PbcSeed
{
  internal sealed record Fixture(
    Guid FirmId, Guid ClientId, Guid EngagementId,
    AppUser Staff, AppUser Reviewer, AppUser Client);

  internal static async Task<Fixture> SeedAsync(PgTestSchema pg)
  {
    var firmId = Guid.NewGuid();
    var clientId = Guid.NewGuid();
    var engagementId = Guid.NewGuid();
    var staff = User(firmId, "Staff");
    var reviewer = User(firmId, "Staff");
    var client = User(firmId, "Client");
    await using var db = new AuditSphereDbContext(pg.Options);
    db.PracticeClients.Add(new PracticeClient
    {
      Id = clientId, FirmId = firmId, LegalName = "PBC TEST CLIENT", CreatedAt = DateTimeOffset.UtcNow
    });
    db.Engagements.Add(new Engagement
    {
      Id = engagementId, FirmId = firmId, PracticeClientId = clientId,
      ProfessionalWorkBlocked = false, CreatedAt = DateTimeOffset.UtcNow
    });
    db.FirmSafetyStates.Add(new AuditSphereOps.Domain.Completion.FirmSafetyState { Id = firmId });
    db.ClientSafetyStates.Add(new AuditSphereOps.Domain.Completion.ClientSafetyState
    {
      Id = clientId, FirmId = firmId
    });
    db.Users.AddRange(staff, reviewer, client);
    db.RoleGrants.AddRange(
      Grant(firmId, staff, "Staff", clientId: clientId, engagementId: engagementId),
      Grant(firmId, reviewer, "Reviewer", clientId: clientId, engagementId: engagementId),
      Grant(firmId, client, "ClientUser", clientId: clientId, engagementId: engagementId));
    await db.SaveChangesAsync();
    return new Fixture(firmId, clientId, engagementId, staff, reviewer, client);
  }

  internal static ActorContext Actor(AppUser user, string role) =>
    new(user.Id, user.FirmId, user.SessionEpoch, [role]);

  internal static AppUser User(Guid firmId, string kind) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserKind = kind,
    Subject = "sub-" + Guid.NewGuid().ToString("N"), TenantId = "tenant-test",
    Email = Guid.NewGuid().ToString("N") + "@example.test", DisplayName = kind,
    CreatedAt = DateTimeOffset.UtcNow
  };

  internal static RoleGrant Grant(Guid firmId, AppUser user, string role,
    Guid? clientId = null, Guid? engagementId = null) => new()
  {
    Id = Guid.NewGuid(), FirmId = firmId, UserId = user.Id, Role = role,
    ClientId = clientId, EngagementId = engagementId,
    GrantedAt = DateTimeOffset.UtcNow, GrantedByUserId = user.Id
  };

  /// <summary>Stages a bounded upload through the service with real chunk files on a
  /// temporary staging root, exactly as the HTTP transport would record them.</summary>
  internal static async Task<StagedUpload> StageUploadAsync(
    PgTestSchema pg, Fixture fixture, ActorContext client, Guid requestId,
    byte[] content, string fileName = "bank-statements.csv", string contentType = "text/csv")
  {
    var stagingRoot = Path.Combine(Path.GetTempPath(), "AuditSphereOps-tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(stagingRoot);
    using var sha = System.Security.Cryptography.SHA256.Create();
    var declaredHash = Convert.ToHexString(sha.ComputeHash(content)).ToLowerInvariant();
    Guid uploadId;
    string capability;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var started = await PbcService.StartUploadAsync(db, client, new StartPbcUploadRequest(
        requestId, fileName, contentType, content.Length, declaredHash));
      if (!started.Succeeded)
        throw new InvalidOperationException("start failed: " + started.ErrorCode);
      uploadId = started.Value!.UploadIntentId;
      capability = started.Value.Capability!;
    }
    const int chunkSize = 8 * 1024 * 1024;
    for (var (index, offset) = (0, 0); offset < content.Length; index++, offset += chunkSize)
    {
      var length = Math.Min(chunkSize, content.Length - offset);
      var slice = content[offset..(offset + length)];
      var directory = Path.Combine(stagingRoot, uploadId.ToString("N"));
      Directory.CreateDirectory(directory);
      var path = Path.Combine(directory, index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".part");
      await File.WriteAllBytesAsync(path, slice);
      var hash = Convert.ToHexString(sha.ComputeHash(slice)).ToLowerInvariant();
      await using var db = new AuditSphereDbContext(pg.Options);
      var recorded = await PbcService.RecordChunkAsync(db, client, new RecordPbcUploadChunkRequest(
        uploadId, index, offset, length, hash, capability, path));
      if (!recorded.Succeeded)
        throw new InvalidOperationException("chunk failed: " + recorded.ErrorCode);
    }
    return new PbcSeed.StagedUpload(uploadId, capability, declaredHash, stagingRoot, content.Length);
  }

  internal sealed record StagedUpload(
    Guid UploadIntentId, string Capability, string DeclaredSha256Hex, string StagingRoot, long ByteCount);

  /// <summary>Creates a scoped PBC request as staff, sends it, and acknowledges it as the
  /// client owner, returning the request id ready to accept uploads.</summary>
  internal static async Task<Guid> CreateSentAcknowledgedRequestAsync(
    PgTestSchema pg, Fixture fixture, ActorContext staff, ActorContext client)
  {
    Guid requestId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var created = await PbcService.CreateRequestAsync(db, staff, new CreatePbcRequestRequest(
        fixture.EngagementId, "Bank statements", "TEST ENTITY", "2026-01-01", "2026-12-31",
        "Cash", "PDF or CSV", "12 months", fixture.Client.Id, fixture.Staff.Id, fixture.Reviewer.Id,
        "2027-01-31", "Confidential", "Complete period with readable account identity."));
      if (!created.Succeeded)
        throw new InvalidOperationException("create failed: " + created.ErrorCode);
      requestId = created.Value;
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var sent = await PbcService.ChangeStateAsync(db, staff,
        new PbcStateChangeRequest(requestId, PbcStates.Sent, 1));
      if (!sent.Succeeded)
        throw new InvalidOperationException("sent failed: " + sent.ErrorCode);
    }
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var acknowledged = await PbcService.ChangeStateAsync(db, client,
        new PbcStateChangeRequest(requestId, PbcStates.Acknowledged, 2));
      if (!acknowledged.Succeeded)
        throw new InvalidOperationException("acknowledged failed: " + acknowledged.ErrorCode);
    }
    return requestId;
  }

  /// <summary>Builds the trusted completion boundary for one schema: the durable operation
  /// store plus the transfer handler sharing its context factory.</summary>
  internal static (PostgresOperationStore Store, PbcDocumentTransferHandler Handler) Boundary(
    IPbcProviderSink sink, PgTestSchema pg)
  {
    var factory = new OperationContextFactory(new OptionsDbContextFactory(pg.Options));
    var store = new PostgresOperationStore(factory);
    var handler = new PbcDocumentTransferHandler(factory, sink);
    return (store, handler);
  }

  internal static void DeleteDirectory(string path)
  {
    try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
    catch (IOException) { }
  }

  internal sealed class OptionsDbContextFactory(DbContextOptions<AuditSphereDbContext> options)
    : IDbContextFactory<AuditSphereDbContext>
  {
    public AuditSphereDbContext CreateDbContext() => new(options);
  }
}
