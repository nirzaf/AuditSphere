using System.Reflection;
using System.Text.Json;
using AuditSphereOps.Application.Audit;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditProgramCatalogSourceTests
{
  [Fact(DisplayName = "Controlled audit programme matches every ordered source paragraph from the supplied document")]
  public void Catalog_MatchesIndependentWordSourceManifest()
  {
    using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
      "AuditSphereOps.Domain.Tests.Fixtures.audit-working-process-source.json");
    Assert.NotNull(stream);
    using var document = JsonDocument.Parse(stream!);
    var root = document.RootElement;
    Assert.Equal(AuditProgramCatalog.SourceHash, root.GetProperty("sourceSha256").GetString());
    Assert.Equal(20, root.GetProperty("sections").GetArrayLength());

    var catalog = AuditProgramCatalog.Items;
    var ordinal = 0;
    foreach (var section in root.GetProperty("sections").EnumerateArray())
    {
      var number = section.GetProperty("sectionNumber").GetInt32();
      var title = section.GetProperty("sectionTitle").GetString();
      var procedures = section.GetProperty("procedures").EnumerateArray().ToArray();
      Assert.Equal(procedures.Length, catalog.Count(x => x.SectionNumber == number));
      for (var index = 0; index < procedures.Length; index++)
      {
        var source = procedures[index];
        var actual = catalog[ordinal++];
        Assert.Equal(source.GetProperty("sourceProcedureId").GetString(), actual.SourceProcedureId);
        Assert.Equal(number, actual.SectionNumber);
        Assert.Equal(title, actual.SectionTitle);
        Assert.Equal(source.GetProperty("ordinal").GetInt32(), actual.Ordinal);
        Assert.Equal(source.GetProperty("sourceWording").GetString(), actual.SourceWording);
      }
    }
    Assert.Equal(165, ordinal);
    Assert.Equal(64, AuditProgramCatalog.ContentDigest.Length);
  }
}
