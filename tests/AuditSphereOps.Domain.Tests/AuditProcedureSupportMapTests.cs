using AuditSphereOps.Application.Audit;

namespace AuditSphereOps.Domain.Tests;

public sealed class AuditProcedureSupportMapTests
{
  [Fact]
  public void EverySourceProcedureRetainsCommonAndSectionSupport()
  {
    var engagement = Guid.NewGuid();
    Assert.Equal(165, AuditProgramCatalog.Items.Count);
    Assert.Equal(20, AuditProgramCatalog.Items.Select(x => x.SectionNumber).Distinct().Count());
    foreach (var item in AuditProgramCatalog.Items)
    {
      var links = AuditProcedureSupportMap.For(item.SourceProcedureId, item.SectionNumber, engagement, null);
      Assert.Contains(links, x => x.ToolKind == "WORKSPACE" && x.Availability == AuditProcedureSupportMap.Available);
      Assert.True(links.Count > 1);
      if (item.SectionNumber is 4 or 9 or 13)
        Assert.Contains(links, x => x.Route is null);
      else
        Assert.Contains(links, x => x.Route is not null);
      if (item.SectionNumber is 9 or 13)
        Assert.Contains(links, x => x.Availability == AuditProcedureSupportMap.ManualOnly);
    }
    for (var section = 1; section <= 20; section++)
      Assert.Contains(AuditProcedureSupportMap.For("AWP-TEST", section, engagement, null), x => x.ToolKind != "WORKSPACE");
    var withWorkpaper = AuditProcedureSupportMap.For("AWP-02-01", 2, engagement, Guid.NewGuid());
    Assert.Contains(withWorkpaper, x => x.ToolKind == "WORKPAPER" && x.Route!.Contains("/app/audit/workpapers/"));
  }
}
