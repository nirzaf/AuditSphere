using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Records;
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
    await CompletionBundleFixture.ReleasedSupportedFinancialPackageAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U));
    await CompletionBundleFixture.PostSupportedBalanceFeeAsync(pg.Options, new(w.FirmId, w.ClientId, w.EngagementId, w.U), automateBalance: true);
    await RefreshSupportedPortalRiskAsync(pg, w);
    await ReviewSupportedBundleApplicabilityAsync(pg, w);
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
    if (component == "report")
    {
      var client = w.A("client", "ClientUser");
      var beforeFreeze = await AuditDeliverableService.GetBundleAsync(intact, client, accepted.Value);
      Assert.True(beforeFreeze.Succeeded, beforeFreeze.Message);
      var now = DateTimeOffset.UtcNow;
      var readiness = await FileFreezeService.GetArchiveReadinessAsync(intact, partner, w.EngagementId, now);
      Assert.True(readiness.Succeeded, readiness.Message);
      Assert.Empty(readiness.Value!.Blockers);
      var frozen = await FileFreezeService.RequestEarlyComplianceLockAsync(intact, partner,
        new(w.EngagementId, readiness.Value.Revision, true, "Reviewed complete retained file.",
          readiness.Value.ArchiveReadinessDigest!), now);
      Assert.True(frozen.Succeeded, frozen.Message);
      Assert.True(await intact.Engagements.AsNoTracking().Where(x => x.Id == w.EngagementId)
        .Select(x => x.ProfessionalWorkBlocked).SingleAsync());
      var download = await AuditDeliverableService.GetBundleAsync(intact, client, accepted.Value);
      Assert.True(download.Succeeded, download.Message);
      Assert.Equal(beforeFreeze.Value!.Content, download.Value!.Content);
      Assert.Equal(ErrorCodes.ProtectedState,
        (await ClientPortalService.RequireUploadWindowAsync(intact, client, w.EngagementId)).ErrorCode);
    }
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
