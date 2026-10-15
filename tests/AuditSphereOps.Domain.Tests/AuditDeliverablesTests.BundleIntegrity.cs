using AuditSphereOps.Application.Completion;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Shared;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AuditSphereOps.Domain.Tests;

public sealed partial class AuditDeliverablesTests
{
  [Theory]
  [InlineData("report")]
  [InlineData("management")]
  [InlineData("scan")]
  [InlineData("representation")]
  [InlineData("statements")]
  [InlineData("statement-word")]
  [InlineData("statement-excel")]
  public async Task FivePartBundle_RefusesCorruptedSourceRead_WithoutChangingStoredEvidence(string component)
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var w = await SeedAsync(pg);
    var partner = w.A("partner", "Partner");
    await CompletionBundleFixture.ReleasedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.SeedPostedFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    await SignedReportAsync(pg, w);
    await using (var db = new AuditSphereDbContext(pg.Options))
      Assert.True((await AuditDeliverableService.GenerateReportAsync(db, w.A("manager", "Manager"), w.EngagementId, DeliverableKinds.ManagementLetter)).Succeeded);
    // Model a corrupt storage read after EF materialization. No trigger is disabled and no stored artifact is edited.
    var corruption = new CorruptBundleSourceRead(component);
    await using (var db = new AuditSphereDbContext(new DbContextOptionsBuilder<AuditSphereDbContext>(pg.Options).AddInterceptors(corruption).Options))
    {
      var refused = await AuditDeliverableService.AssembleBundleAsync(db, partner, w.EngagementId, true);
      Assert.True(corruption.Fired); Assert.Equal(ErrorCodes.GateBlocked, refused.ErrorCode);
      Assert.Contains("integrity", refused.Message!, StringComparison.OrdinalIgnoreCase);
      Assert.Empty(await db.CommercialDeliverableBundles.ToListAsync());
    }
    await using var intact = new AuditSphereDbContext(pg.Options);
    var accepted = await AuditDeliverableService.AssembleBundleAsync(intact, partner, w.EngagementId, true);
    Assert.True(accepted.Succeeded, accepted.Message);
    Assert.Single(await intact.CommercialDeliverableBundles.ToListAsync());
  }

  private sealed class CorruptBundleSourceRead(string component) : IMaterializationInterceptor
  {
    public bool Fired { get; private set; }
    public object InitializedInstance(MaterializationInterceptionData data, object entity)
    {
      switch (entity)
      {
        case AuditDeliverable d when component == "report" && d.Kind == DeliverableKinds.IndependentAuditorsReport && d.SignedFromDeliverableId is not null:
          d.Content = [.. d.Content, 0x7f]; Fired = true; break;
        case AuditDeliverable d when component == "management" && d.Kind == DeliverableKinds.ManagementLetter:
          d.Content = [.. d.Content, 0x7f]; Fired = true; break;
        case AuditDeliverable d when component == "representation" && d.Kind == DeliverableKinds.RepresentationLetter:
          d.Content = [.. d.Content, 0x7f]; Fired = true; break;
        case SignedRepresentationLetter scan when component == "scan":
          scan.Content = [.. scan.Content, 0x7f]; Fired = true; break;
        case FinancialPackageArtifact pdfArtifact when component == "statements" && pdfArtifact.ArtifactVersion.StartsWith("statement-presentation.pdf.", StringComparison.Ordinal):
          pdfArtifact.ArtifactBytes = [.. pdfArtifact.ArtifactBytes, 0x7f]; Fired = true; break;
        case FinancialPackageArtifact wordArtifact when component == "statement-word" && wordArtifact.ArtifactVersion.StartsWith("statement-presentation.docx.", StringComparison.Ordinal):
          wordArtifact.ArtifactBytes = [.. wordArtifact.ArtifactBytes, 0x7f]; Fired = true; break;
        case FinancialPackageArtifact excelArtifact when component == "statement-excel" && excelArtifact.ArtifactVersion.StartsWith("statement-presentation.xlsx.", StringComparison.Ordinal):
          excelArtifact.ArtifactBytes = [.. excelArtifact.ArtifactBytes, 0x7f]; Fired = true; break;
      }
      return entity;
    }
  }
}
