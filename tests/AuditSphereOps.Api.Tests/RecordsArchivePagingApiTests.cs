using System.Net;
using System.Text.Json;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Records;
using AuditSphereOps.Domain.Tests;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Testing;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Tests;

public sealed class RecordsArchivePagingApiTests
{
  [Fact]
  [Trait("CaseId", "AS-PAR-002-ARCHIVE-API-PAGING-01")]
  public async Task ManifestPagesAreBoundedOrderedAndForeignIdsAreNondisclosing()
  {
    await using var pg = await OwnedPostgresDatabase.CreateAsync("API-ARCHIVE-PAGING");
    var f = await PbcSeed.SeedAsync(pg);
    var foreign = await PbcSeed.SeedAsync(pg);
    var archiveId = Guid.NewGuid();
    var manifestId = Guid.NewGuid();
    var foreignArchiveId = Guid.NewGuid();
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      db.Archives.Add(new Archive
      {
        Id = archiveId, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ProfileId = "SYNTH-PAGED-ARCHIVE", ProfileVersion = 1,
        Status = ArchiveStates.AssemblyInProgress, CreatedAt = DateTimeOffset.UtcNow
      });
      db.ArchiveManifests.Add(new ArchiveManifest
      {
        Id = manifestId, FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ArchiveId = archiveId, Version = 1,
        Status = "BUILT", ManifestDigest = new string('a', 64), EntryCount = 205,
        CompletenessStatus = "COMPLETE", BuiltAt = DateTimeOffset.UtcNow
      });
      db.ArchiveManifestEntries.AddRange(Enumerable.Range(1, 205).Select(ordinal => new ArchiveManifestEntry
      {
        Id = Guid.NewGuid(), FirmId = f.FirmId, ClientId = f.ClientId,
        EngagementId = f.EngagementId, ArchiveManifestId = manifestId, Ordinal = ordinal,
        EntryKind = "DOCUMENT", SourceKind = "TEST", RelativeName = $"evidence/item-{ordinal:D3}.pdf",
        ContentHash = new string('b', 64), ByteCount = ordinal
      }));
      db.Archives.Add(new Archive
      {
        Id = foreignArchiveId, FirmId = foreign.FirmId, ClientId = foreign.ClientId,
        EngagementId = foreign.EngagementId, ProfileId = "FOREIGN-ARCHIVE-MARKER", ProfileVersion = 1,
        Status = ArchiveStates.AssemblyInProgress, CreatedAt = DateTimeOffset.UtcNow
      });
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
    await client.GetAsync("/auth/sign-in");

    var first = await GetArchive(client, archiveId);
    Assert.Equal(205, first.GetProperty("totalEntryCount").GetInt32());
    Assert.Equal(100, first.GetProperty("entries").GetArrayLength());
    Assert.Equal(100, first.GetProperty("nextOrdinal").GetInt32());
    AssertEntryRange(first.GetProperty("entries"), 1, 100);

    var second = await GetArchive(client, archiveId, 100);
    Assert.Equal(205, second.GetProperty("totalEntryCount").GetInt32());
    Assert.Equal(100, second.GetProperty("entries").GetArrayLength());
    Assert.Equal(200, second.GetProperty("nextOrdinal").GetInt32());
    AssertEntryRange(second.GetProperty("entries"), 101, 200);

    var last = await GetArchive(client, archiveId, 200);
    Assert.Equal(205, last.GetProperty("totalEntryCount").GetInt32());
    Assert.Equal(5, last.GetProperty("entries").GetArrayLength());
    Assert.Equal(JsonValueKind.Null, last.GetProperty("nextOrdinal").ValueKind);
    AssertEntryRange(last.GetProperty("entries"), 201, 205);

    using var invalidCursor = await client.GetAsync($"/api/ui/records/archives/{archiveId:D}?afterOrdinal=-1");
    Assert.Equal(HttpStatusCode.BadRequest, invalidCursor.StatusCode);
    using (var invalidBody = JsonDocument.Parse(await invalidCursor.Content.ReadAsStringAsync()))
      Assert.Equal("request.invalid", invalidBody.RootElement.GetProperty("code").GetString());

    var foreignResponse = await client.GetAsync($"/api/ui/records/archives/{foreignArchiveId:D}");
    var guessedResponse = await client.GetAsync($"/api/ui/records/archives/{Guid.NewGuid():D}");
    Assert.Equal(HttpStatusCode.Forbidden, foreignResponse.StatusCode);
    Assert.Equal(HttpStatusCode.Forbidden, guessedResponse.StatusCode);
    Assert.Equal(await foreignResponse.Content.ReadAsStringAsync(), await guessedResponse.Content.ReadAsStringAsync());
    Assert.DoesNotContain("FOREIGN-ARCHIVE-MARKER", await foreignResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
  }

  private static async Task<JsonElement> GetArchive(HttpClient client, Guid id, int? afterOrdinal = null)
  {
    var suffix = afterOrdinal is null ? "" : $"?afterOrdinal={afterOrdinal.Value}";
    using var response = await client.GetAsync($"/api/ui/records/archives/{id:D}{suffix}");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    return document.RootElement.Clone();
  }

  private static void AssertEntryRange(JsonElement entries, int first, int last)
  {
    var rows = entries.EnumerateArray().ToArray();
    Assert.Equal(last - first + 1, rows.Length);
    for (var index = 0; index < rows.Length; index++)
      Assert.Equal(first + index, rows[index].GetProperty("ordinal").GetInt32());
  }
}
