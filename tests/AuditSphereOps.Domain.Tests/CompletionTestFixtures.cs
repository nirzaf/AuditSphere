using System.IO.Compression;
using System.Text;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Accounting;
using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Domain.Tests;

public static class CompletionTestFixtures
{
  public static byte[] Png(int width, int height)
  {
    static byte[] Chunk(string type, byte[] data)
    {
      var typeBytes = Encoding.ASCII.GetBytes(type);
      var crc = Crc32([.. typeBytes, .. data]);
      return [.. BigEndian(data.Length), .. typeBytes, .. data, .. BigEndian((int)crc)];
    }
    static byte[] BigEndian(int value) => [(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value];
    static uint Crc32(byte[] bytes)
    {
      var crc = 0xFFFFFFFFu;
      foreach (var b in bytes) { crc ^= b; for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1; }
      return ~crc;
    }
    byte[] header = [.. BigEndian(width), .. BigEndian(height), 8, 0, 0, 0, 0];
    var raw = new MemoryStream();
    for (var y = 0; y < height; y++) { raw.WriteByte(0); for (var x = 0; x < width; x++) raw.WriteByte((byte)(x == y ? 0 : 255)); }
    var compressed = new MemoryStream();
    using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.ToArray());
    return [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Chunk("IHDR", header), .. Chunk("IDAT", compressed.ToArray()), .. Chunk("IEND", [])];
  }

  public static byte[] SignedPdf() => AuditDeliverableRenderer.RenderPdf(new("Synthetic management", "Management-signed representation scan", "TEST-SCAN", new DateOnly(2026, 12, 31),
    "Auditors", [new("Synthetic acceptance fixture", ["Management signature and completeness are human-reviewed in this test."])]));

  public static async Task SeedTaxonomyAsync(AuditSphereDbContext db, Guid firmId, Guid actorId)
  {
    if (await db.ReportingTaxonomyNodes.AnyAsync(x => x.FirmId == firmId && x.Name == "Inventory")) return;
    var version = Guid.NewGuid();
    db.ReportingTaxonomyVersions.Add(new ReportingTaxonomyVersion
    {
      Id = version, FirmId = firmId, Code = "TEST-IFRS", Name = "Synthetic approved FSLI taxonomy", Framework = "IFRS", Status = AccountingWorkflowStates.Approved,
      EffectiveFrom = new DateOnly(2026, 1, 1), CreatedByUserId = actorId, ApprovedByUserId = actorId, ApprovedAt = DateTimeOffset.UtcNow, CreatedAt = DateTimeOffset.UtcNow
    });
    db.ReportingTaxonomyNodes.Add(new ReportingTaxonomyNode
    {
      Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = version, Code = "INV", Name = "Inventory", StatementSection = "ASSETS", DisplaySign = "SIGNED",
      NormalBalance = "DEBIT", DisclosureArea = "Inventory", IsPosting = true, Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
    });
    db.ReportingTaxonomyNodes.AddRange(new[] { ("CASH", "Cash", "ASSETS", "DEBIT"), ("REVENUE", "Revenue", "INCOME", "CREDIT") }.Select(x => new ReportingTaxonomyNode
    {
      Id = Guid.NewGuid(), FirmId = firmId, TaxonomyVersionId = version, Code = x.Item1, Name = x.Item2, StatementSection = x.Item3,
      DisplaySign = "SIGNED", NormalBalance = x.Item4, IsPosting = true, Applicability = "ALL", CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync();
  }
}
