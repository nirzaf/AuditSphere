using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

// M25 seal: an atomic, immutable record that every required artifact format exists with
// exact persisted bytes for one package revision/generation. Two digests avoid the
// self-reference trap: the content manifest covers the immutable inputs captured before
// rendering, and the artifact manifest covers the completed ordered artifact identities.
public sealed record PackageArtifactManifestEntry(
  string ArtifactVersion, long ByteLength, string Sha256Hex,
  string FrameworkVersion, string TemplateVersion, string MimeType,
  Guid? PresentationRevisionId = null, string? PresentationRevisionHash = null,
  Guid? LayoutVersionId = null, string? LayoutHash = null, string? NoteSourceManifestHash = null,
  string? BuilderVersion = null, string? RendererVersion = null, string? DisplayPolicy = null,
  string? FileName = null);

public sealed record PackageSealValue(
  Guid SealId, Guid FinancialPackageId, long PackageRevision, long PackageGeneration,
  string ContentManifestDigest, string ArtifactManifestDigest,
  int ArtifactCount, long TotalByteLength);

public sealed record PackageSealStatusView(
  Guid FinancialPackageId, long PackageRevision, long PackageGeneration,
  bool IsSealed, Guid? SealId, string? ContentManifestDigest, string? ArtifactManifestDigest,
  IReadOnlyList<string> PresentFormats, IReadOnlyList<string> MissingRequiredFormats, bool CanSeal);

public static class PackageSealService
{
  private static readonly string[] PreparerRoles = ["AccountingPreparer", "Staff", "Manager", "Partner", "Administrator"];
  private static readonly string[] ReadRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Formats every sealed package must contain. The text artifact is the canonical
  /// rendered statement; the workbook, word and pdf artifacts are the client-facing outputs
  /// the completion and release gates depend on.</summary>
  public static readonly string[] RequiredFormats =
  [
    FinancialPackageArtifactVersions.Text,
    FinancialPackageArtifactVersions.Workbook,
    FinancialPackageArtifactVersions.Word,
    FinancialPackageArtifactVersions.Pdf
  ];

  /// <summary>Reports whether the exact package revision can be sealed and which required
  /// formats are missing. A partially rendered package reports its gaps rather than a
  /// misleading ready state.</summary>
  public static async Task<CommandResult<PackageSealStatusView>> GetSealStatusAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId,
    CancellationToken ct = default)
  {
    if (financialPackageId == Guid.Empty)
      return CommandResult<PackageSealStatusView>.Fail(ErrorCodes.Accounting.PackageInvalid, "A package id is required.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<PackageSealStatusView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, ReadRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PackageSealStatusView>.Fail(auth.ErrorCode!, auth.Message!);

    var loaded = await LoadCurrentArtifactsAsync(db, actor, package, ct);
    if (!loaded.Succeeded || loaded.Value is null)
      return CommandResult<PackageSealStatusView>.Fail(loaded.ErrorCode!, loaded.Message!);
    var artifacts = loaded.Value;
    var seal = await db.FinancialPackageSeals.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
        x.ArtifactManifestDigest == ArtifactManifestDigest(artifacts))
      .OrderByDescending(x => x.SealedAt).ThenByDescending(x => x.Id)
      .FirstOrDefaultAsync(ct);
    var present = artifacts.Select(x => x.ArtifactVersion).ToList();
    var missing = RequiredFormats.Where(required => !present.Contains(required, StringComparer.Ordinal)).ToList();

    var readiness = await FinancialPackageValidationReadiness.GetAsync(db, package.FirmId, package.Id, ct);
    return CommandResult<PackageSealStatusView>.Ok(new PackageSealStatusView(
      package.Id, package.Revision, package.Generation,
      seal is not null, seal?.Id, seal?.ContentManifestDigest, seal?.ArtifactManifestDigest,
      present, missing, CanSeal: missing.Count == 0 && readiness.Ready));
  }

  /// <summary>Atomically seals the current package revision. Every required format must
  /// exist with non-empty bytes, and a structurally invalid artifact set is refused so a
  /// partially persisted package is never sealed. Re-sealing the identical artifact set
  /// returns the existing seal rather than writing a second one.</summary>
  public static async Task<CommandResult<PackageSealValue>> SealPackageAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PackageSealValue>.Fail(auth.ErrorCode!, auth.Message!);

    await using var tx = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(ct) : null;
    // Source selection and native posting use the same scope fences. Keep them
    // through readiness and publication; a read followed by a later INSERT is not atomic.
    var firm = await db.FirmSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM firm_safety_states WHERE id={actor.FirmId} FOR SHARE").AsNoTracking().SingleOrDefaultAsync(ct);
    var client = await db.ClientSafetyStates.FromSqlInterpolated(
      $"SELECT * FROM client_safety_states WHERE firm_id={actor.FirmId} AND id={package.ClientId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    var engagement = await db.Engagements.FromSqlInterpolated(
      $"SELECT * FROM engagements WHERE firm_id={actor.FirmId} AND practice_client_id={package.ClientId} AND id={package.EngagementId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (firm is null || client is null || engagement is null)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var current = await db.FinancialPackages.FromSqlInterpolated(
      $"SELECT * FROM financial_packages WHERE firm_id={actor.FirmId} AND client_id={package.ClientId} AND engagement_id={package.EngagementId} AND id={package.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
    if (current is null)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (current.Revision != package.Revision || current.Generation != package.Generation ||
        current.CalculationHash != package.CalculationHash)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.StaleRevision, "The package changed. Refresh its exact artifact set before sealing.");
    package = current;
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, PreparerRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<PackageSealValue>.Fail(auth.ErrorCode!, auth.Message!);

    var loaded = await LoadCurrentArtifactsAsync(db, actor, package, ct);
    if (!loaded.Succeeded || loaded.Value is null)
      return CommandResult<PackageSealValue>.Fail(loaded.ErrorCode!, loaded.Message!);
    var manifest = loaded.Value;
    var present = manifest.Select(x => x.ArtifactVersion).ToList();
    var missing = RequiredFormats.Where(required => !present.Contains(required, StringComparer.Ordinal)).ToList();
    if (missing.Count > 0)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.GateBlocked,
        $"Every required format must be rendered before sealing; missing: {string.Join(", ", missing)}.");
    if (manifest.Any(x => x.ByteLength == 0))
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.GateBlocked,
        "An artifact with no persisted bytes cannot be sealed.");
    var readiness = await FinancialPackageValidationReadiness.GetAsync(db, package.FirmId, package.Id, ct);
    if (!readiness.Ready)
      return CommandResult<PackageSealValue>.Fail(ErrorCodes.GateBlocked, readiness.Message);

    // The artifact digest covers ordered identities and their byte hashes only, so it is a
    // function of the persisted bytes rather than of this record.
    var artifactDigest = ArtifactManifestDigest(manifest);
    // The content digest covers the immutable inputs captured before rendering.
    var contentParts = new List<string>
    {
      package.CalculationHash, package.MappingVersionId.ToString("D"), package.AdjustmentPlanId.ToString("D"),
      package.AdjustedDatasetId.ToString("D"), package.TaxonomyVersion, package.TemplateVersion,
      package.CalculationEngineVersion, package.Currency, package.Revision.ToString(), package.Generation.ToString(),
      package.SupplementaryHash ?? string.Empty, package.EquityHash ?? string.Empty
    };
    var presentationInputs = manifest.Where(x => x.PresentationRevisionId is not null)
      .OrderBy(x => x.PresentationRevisionId).ThenBy(x => x.ArtifactVersion, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.PresentationRevisionId, x.PresentationRevisionHash, x.LayoutVersionId,
        x.LayoutHash, x.NoteSourceManifestHash, x.BuilderVersion, x.RendererVersion, x.DisplayPolicy));
    var presentationInputManifest = string.Join('\n', presentationInputs);
    if (presentationInputManifest.Length > 0) contentParts.Add(presentationInputManifest);
    var contentDigest = Hashing.Sha256Hex(string.Join('|', contentParts));

    var existing = await db.FinancialPackageSeals.SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
      x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
      x.ArtifactManifestDigest == artifactDigest, ct);
    if (existing is not null)
    {
      if (tx is not null) await tx.CommitAsync(ct);
      return CommandResult<PackageSealValue>.Ok(ToValue(existing));
    }

    var seal = new FinancialPackageSeal
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
      EngagementId = package.EngagementId, FinancialPackageId = package.Id,
      PackageRevision = package.Revision, PackageGeneration = package.Generation,
      PackageHash = package.CalculationHash, ContentManifestDigest = contentDigest,
      ArtifactManifestDigest = artifactDigest, ArtifactCount = manifest.Count,
      TotalByteLength = manifest.Sum(x => x.ByteLength),
      ArtifactManifestJson = System.Text.Json.JsonSerializer.Serialize(manifest),
      SealVersion = manifest.Any(x => x.PresentationRevisionId is not null) ? "package-seal.v2" : "package-seal.v1",
      SealedByUserId = actor.UserId, SealedAt = DateTimeOffset.UtcNow
    };
    db.FinancialPackageSeals.Add(seal);
    await db.SaveChangesAsync(ct);
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<PackageSealValue>.Ok(ToValue(seal));
  }

  private static async Task<CommandResult<List<PackageArtifactManifestEntry>>> LoadCurrentArtifactsAsync(
    IClientAccountingDbContext db, ActorContext actor, FinancialPackage package, CancellationToken ct)
  {
    var artifacts = await db.FinancialPackageArtifacts.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
        x.PackageHash == package.CalculationHash &&
        x.ArtifactVersion != FinancialPackageArtifactVersions.ControlledWorkbook)
      .OrderBy(x => x.ArtifactVersion).ThenBy(x => x.Id)
      .ToListAsync(ct);
    var artifactIds = artifacts.Select(x => x.Id).ToArray();
    List<FinancialStatementPresentationArtifact> metadata = artifactIds.Length == 0
      ? []
      : await db.FinancialStatementPresentationArtifacts.AsNoTracking()
        .Where(x => x.FirmId == package.FirmId && artifactIds.Contains(x.Id)).ToListAsync(ct);
    var metadataByArtifact = metadata.ToDictionary(x => x.Id);
    var result = new List<PackageArtifactManifestEntry>(artifacts.Count);

    foreach (var artifact in artifacts)
    {
      if (artifact.ArtifactBytes.Length == 0 || Hashing.Sha256Hex(artifact.ArtifactBytes) != artifact.ArtifactSha256Hex)
        return CommandResult<List<PackageArtifactManifestEntry>>.Fail(ErrorCodes.Accounting.PackageInvalid,
          $"The persisted artifact {artifact.ArtifactVersion} failed its exact-byte integrity check.");

      if (!metadataByArtifact.TryGetValue(artifact.Id, out var presentationArtifact))
      {
        if (artifact.ArtifactVersion.StartsWith("statement-presentation.", StringComparison.Ordinal))
          return CommandResult<List<PackageArtifactManifestEntry>>.Fail(ErrorCodes.Accounting.PackageInvalid,
            "A statement-presentation package artifact is missing its pinned revision metadata.");
        result.Add(new PackageArtifactManifestEntry(artifact.ArtifactVersion, artifact.ArtifactBytes.LongLength,
          artifact.ArtifactSha256Hex, artifact.FrameworkVersion, artifact.TemplateVersion,
          MimeTypeFor(artifact.ArtifactVersion)));
        continue;
      }

      var revision = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == presentationArtifact.PresentationRevisionId && x.FirmId == package.FirmId &&
        x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id, ct);
      if (revision is null || presentationArtifact.FirmId != package.FirmId ||
          presentationArtifact.ClientId != package.ClientId || presentationArtifact.EngagementId != package.EngagementId ||
          revision.PackageRevision != package.Revision || revision.PackageGeneration != package.Generation ||
          revision.PackageCalculationHash != package.CalculationHash ||
          artifact.ArtifactVersion != FinancialPackageArtifactVersions.StatementPresentation(revision.Id, presentationArtifact.Format) ||
          artifact.FrameworkVersion != revision.Framework || artifact.TemplateVersion != $"statement-layout:{revision.LayoutVersionId:D}" ||
          presentationArtifact.RendererVersion != revision.RendererVersion ||
          FinancialStatementPresentationRenderer.ContentTypeFor(presentationArtifact.Format) != presentationArtifact.ContentType ||
          artifact.ArtifactBytes.Length > 32 * 1024 * 1024)
        return CommandResult<List<PackageArtifactManifestEntry>>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "A statement-presentation artifact does not match its pinned package, layout, renderer or format.");

      var pinned = await FinancialStatementService.GetPinnedPresentationAsync(db, actor, package.Id, revision.Id, ct);
      if (!pinned.Succeeded || pinned.Value is null)
        return CommandResult<List<PackageArtifactManifestEntry>>.Fail(pinned.ErrorCode!,
          $"The statement presentation {revision.Id:D} is stale or failed integrity validation; refresh the source before sealing.");

      result.Add(new PackageArtifactManifestEntry(artifact.ArtifactVersion, artifact.ArtifactBytes.LongLength,
        artifact.ArtifactSha256Hex, artifact.FrameworkVersion, artifact.TemplateVersion, presentationArtifact.ContentType,
        revision.Id, revision.RevisionHash, revision.LayoutVersionId, revision.LayoutHash,
        revision.NoteSourceManifestHash, revision.BuilderVersion, presentationArtifact.RendererVersion,
        revision.DisplayPolicy, presentationArtifact.FileName));
    }

    return CommandResult<List<PackageArtifactManifestEntry>>.Ok(result);
  }

  private static string ArtifactManifestDigest(IReadOnlyCollection<PackageArtifactManifestEntry> manifest) =>
    Hashing.Sha256Hex(string.Join('\n', manifest.OrderBy(x => x.ArtifactVersion, StringComparer.Ordinal)
      .Select(x => string.Join('|', x.ArtifactVersion, x.ByteLength, x.Sha256Hex, x.RendererOrTemplateVersion()))));

  /// <summary>Genuine MIME type per artifact format; an unknown format is refused by the
  /// seal rather than labelled generically.</summary>
  private static string MimeTypeFor(string artifactVersion) => artifactVersion switch
  {
    FinancialPackageArtifactVersions.Text => "text/plain",
    FinancialPackageArtifactVersions.Workbook => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    FinancialPackageArtifactVersions.Word => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    FinancialPackageArtifactVersions.Pdf => "application/pdf",
    _ => "application/octet-stream"
  };

  private static PackageSealValue ToValue(FinancialPackageSeal seal) => new(
    seal.Id, seal.FinancialPackageId, seal.PackageRevision, seal.PackageGeneration,
    seal.ContentManifestDigest, seal.ArtifactManifestDigest, seal.ArtifactCount, seal.TotalByteLength);
}

internal static class PackageArtifactManifestEntryExtensions
{
  internal static string RendererOrTemplateVersion(this PackageArtifactManifestEntry entry) =>
    entry.PresentationRevisionId is null
      ? $"{entry.FrameworkVersion}|{entry.TemplateVersion}"
      : string.Join('|', entry.FrameworkVersion, entry.TemplateVersion, entry.PresentationRevisionId,
        entry.PresentationRevisionHash, entry.LayoutVersionId, entry.LayoutHash, entry.NoteSourceManifestHash,
        entry.BuilderVersion, entry.RendererVersion, entry.DisplayPolicy, entry.MimeType, entry.FileName);
}
