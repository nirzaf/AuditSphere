using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Security;
using AuditSphereOps.Domain.Shared;

using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Domain.Tests;

/// <summary>
/// CP-18 composition. Selecting a candidate statement presentation seals PDF, Word and Excel together as one record. Each format's artifact
/// identity and digest are stored, and repeating the selection returns the same composition. Only a candidate can be composed.
/// </summary>
[Trait("Profile", "Database")]
public sealed class StatementCompositionTests
{
  [Fact]
  public async Task SelectingACandidate_SealsEveryRequiredFormatOnceAndIsIdempotent()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = Actor(s.Preparer, "AccountingPreparer");
    var reviewer = Actor(s.Reviewer, "AccountingReviewer");

    Guid candidateId;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
      Assert.True(pin.Succeeded, pin.Message);
      Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.Value!.RevisionId,
        FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
      var promoted = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, pin.Value!.RevisionId);
      Assert.True(promoted.Succeeded, promoted.Message);
      candidateId = promoted.Value!.RevisionId;
    }

    FinancialStatementComposition composition;
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var selected = await FinancialStatementService.SelectCompositionAsync(db, preparer, s.PackageId, candidateId);
      Assert.True(selected.Succeeded, selected.Message);
      composition = selected.Value!;
      Assert.Equal(candidateId, composition.PresentationRevisionId);
      Assert.Matches("^[a-f0-9]{64}$", composition.ManifestDigest);

      // Each sealed digest is the digest of the artifact stored for that format at this candidate.
      foreach (var (format, artifactId, digest) in new[]
      {
        (FinancialStatementPresentationRenderer.PdfFormat, composition.PdfArtifactId, composition.PdfSha256Hex),
        (FinancialStatementPresentationRenderer.WordFormat, composition.WordArtifactId, composition.WordSha256Hex),
        (FinancialStatementPresentationRenderer.ExcelFormat, composition.ExcelArtifactId, composition.ExcelSha256Hex),
      })
      {
        var artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleAsync(x => x.Id == artifactId);
        Assert.Equal(FinancialPackageArtifactVersions.StatementPresentation(candidateId, format), artifact.ArtifactVersion);
        Assert.Equal(artifact.ArtifactSha256Hex, digest);
      }
    }

    // Repeating the selection returns the same composition and never a second one for the candidate.
    await using (var db = new AuditSphereDbContext(pg.Options))
    {
      var again = await FinancialStatementService.SelectCompositionAsync(db, preparer, s.PackageId, candidateId);
      Assert.True(again.Succeeded, again.Message);
      Assert.Equal(composition.Id, again.Value!.Id);
      Assert.Equal(1, await db.FinancialStatementCompositions.CountAsync(x => x.PresentationRevisionId == candidateId));
    }
  }

  [Fact]
  public async Task AStoredComposition_CannotBeUpdatedOrDeleted()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = Actor(s.Preparer, "AccountingPreparer");
    var reviewer = Actor(s.Reviewer, "AccountingReviewer");
    await using var db = new AuditSphereDbContext(pg.Options);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
    Assert.True(pin.Succeeded, pin.Message);
    Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.Value!.RevisionId,
      FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
    var promoted = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, pin.Value.RevisionId);
    Assert.True(promoted.Succeeded, promoted.Message);
    var composition = (await FinancialStatementService.SelectCompositionAsync(db, preparer, s.PackageId, promoted.Value!.RevisionId)).Value!;

    // The sealed digests are history: the database refuses an update or a delete of the stored composition.
    var updated = Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"UPDATE financial_statement_compositions SET pdf_sha256_hex = pdf_sha256_hex WHERE id = {composition.Id}"));
    Assert.Contains("immutable history", (await updated).ToString(), StringComparison.Ordinal);
    var deleted = Assert.ThrowsAnyAsync<Exception>(() => db.Database.ExecuteSqlInterpolatedAsync(
      $"DELETE FROM financial_statement_compositions WHERE id = {composition.Id}"));
    Assert.Contains("immutable history", (await deleted).ToString(), StringComparison.Ordinal);
  }

  [Fact]
  public async Task OnlyACandidateIsComposed_AndAForeignPackageIsRefused()
  {
    await using var pg = await PgTestSchema.CreateAsync();
    var s = await StatementCandidatePromotionTests.SeedPresentableAsync(pg);
    var preparer = Actor(s.Preparer, "AccountingPreparer");
    var reviewer = Actor(s.Reviewer, "AccountingReviewer");

    await using var db = new AuditSphereDbContext(pg.Options);
    var pin = await FinancialStatementService.PinPresentationAsync(db, preparer, s.PackageId, s.LayoutId);
    Assert.True(pin.Succeeded, pin.Message);

    // An unapproved draft is not a candidate, so it cannot be composed.
    var draft = await FinancialStatementService.SelectCompositionAsync(db, preparer, s.PackageId, pin.Value!.RevisionId);
    Assert.Equal(ErrorCodes.GateBlocked, draft.ErrorCode);

    // A package the caller cannot see is refused without revealing that the revision exists.
    Assert.True((await FinancialStatementService.RenderPinnedPresentationArtifactAsync(db, reviewer, pin.Value.RevisionId,
      FinancialStatementPresentationRenderer.PdfFormat)).Succeeded);
    var promoted = await FinancialStatementService.PromotePresentationToCandidateAsync(db, reviewer, s.PackageId, pin.Value.RevisionId);
    Assert.True(promoted.Succeeded, promoted.Message);
    var foreign = await FinancialStatementService.SelectCompositionAsync(db, preparer, Guid.NewGuid(), promoted.Value!.RevisionId);
    Assert.Equal(ErrorCodes.ScopeDenied, foreign.ErrorCode);
    Assert.Equal(0, await db.FinancialStatementCompositions.CountAsync());
  }

  private static ActorContext Actor(AppUser user, string role) => new(user.Id, user.FirmId, user.SessionEpoch, [role]);
}
