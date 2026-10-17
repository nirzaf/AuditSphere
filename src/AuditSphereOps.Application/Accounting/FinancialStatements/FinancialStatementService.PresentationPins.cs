using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialStatementPresentationPin(
  Guid RevisionId, Guid PackageId, string PackageCalculationHash, Guid? PeriodId, Guid? BookId, string? Basis,
  string Framework, string TaxonomyVersion, string TemplateVersion, string CalculationEngineVersion,
  string Currency, string PeriodStart, string PeriodEnd, Guid LayoutVersionId, string LayoutHash,
  Guid? ComparativePackageId, string? ComparativeCalculationHash, string? ComparativeBasis,
  string? ComparativeEvidenceReference, string NoteSourceManifestHash,
  string BuilderVersion, string RendererVersion,
  string DisplayPolicy, string Status, string RevisionHash, string SnapshotSha256Hex,
  DateTimeOffset CreatedAt, bool SourceIsCurrent, IReadOnlyList<string> ArtifactFormats,
  Guid? PredecessorRevisionId = null, Guid? CompositionId = null, string? CompositionManifestDigest = null);

public sealed record FinancialStatementPresentationPinnedArtifact(
  Guid RevisionId, Guid ArtifactId, string Format, string FileName, string ContentType,
  string RendererVersion, string ArtifactSha256Hex, byte[] Bytes);

public static partial class FinancialStatementService
{
  private static readonly JsonSerializerOptions PresentationSnapshotJson = new(JsonSerializerDefaults.Web);

  /// <summary>
  /// Stores an immutable unapproved draft snapshot tied to exact package, comparative and published-layout hashes.
  /// Repeating the same selection returns the same pin; selecting a different layout creates a different revision.
  /// </summary>
  public static async Task<CommandResult<FinancialStatementPresentationPin>> PinPresentationAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packageId, Guid layoutVersionId,
    CancellationToken ct = default)
  {
    if (packageId == Guid.Empty || layoutVersionId == Guid.Empty)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Select the exact package and published layout version to pin.");

    var packageClientId = await db.FinancialPackages.AsNoTracking().Where(x =>
      x.Id == packageId && x.FirmId == actor.FirmId).Select(x => (Guid?)x.ClientId).SingleOrDefaultAsync(ct);
    if (packageClientId is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "Firm safety state is unavailable.");
    var lockedSafety = await LockClientAsync(db, actor.FirmId, packageClientId.Value, ct);
    if (lockedSafety is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "Client safety state is unavailable.");
    var package = await db.FinancialPackages.FromSqlInterpolated(
      $"SELECT * FROM financial_packages WHERE id = {packageId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (package is null || package.ClientId != packageClientId.Value)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (package.ComparativePackageId is Guid comparativePackageId)
    {
      var lockedComparative = await db.FinancialPackages.FromSqlInterpolated(
        $"SELECT * FROM financial_packages WHERE id = {comparativePackageId} AND firm_id = {actor.FirmId} FOR UPDATE")
        .AsNoTracking().SingleOrDefaultAsync(ct);
      if (lockedComparative is null)
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
          "The comparative package is no longer available to pin.");
      if (!await CashFlowSourceApprovedAsync(db, lockedComparative, ct) ||
          !await AcceptedTrialBalanceIsCurrentAsync(db, lockedComparative, ct))
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
          "The comparative package no longer uses current accepted sources for its reporting period.");
    }
    // Client input generation is client-wide. Accepting a source for another reporting period
    // must not stale this package; the package snapshot and its period-scoped source review are
    // the authoritative freshness boundary here.
    if (!await CashFlowSourceApprovedAsync(db, package, ct))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
        "The package's period-specific cash-flow source review is no longer current.");
    if (!await AcceptedTrialBalanceIsCurrentAsync(db, package, ct))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
        "The package no longer uses the selected accepted trial-balance source for its reporting period.");
    var writeAuth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId,
      PreparerRoles, ct);
    if (!writeAuth.Succeeded)
      return CommandResult<FinancialStatementPresentationPin>.Fail(writeAuth.ErrorCode!, writeAuth.Message!);

    var layout = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == layoutVersionId && x.FirmId == actor.FirmId, ct);
    if (layout is null || layout.Status != StatementLayoutStatuses.Published || layout.ClientId != package.ClientId ||
        !string.Equals(layout.FrameworkPolicy, package.Framework, StringComparison.Ordinal))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.MappingIncomplete,
        "The selected layout is not a published version for this package's client and framework.");

    var layoutLines = await StatementLayoutService.LoadLinesAsync(db, actor.FirmId, layout.Id, ct);
    if (!string.Equals(StatementLayoutService.ComputeLayoutHash(layoutLines), layout.LayoutHash, StringComparison.Ordinal))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
        "The selected published layout no longer matches its recorded hash.");

    var built = await BuildPresentationAsync(db, actor, package.Id, layout.Id, ct);
    if (!built.Succeeded || built.Value is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(built.ErrorCode!, built.Message!);
    // Authorization capability is response metadata, not part of the immutable accounting snapshot.
    var presentation = built.Value with { CanPin = false };
    var snapshot = JsonSerializer.SerializeToUtf8Bytes(presentation, PresentationSnapshotJson);
    if (snapshot.Length is 0 or > 8 * 1024 * 1024)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The statement presentation exceeds the bounded saved-draft size.");
    var snapshotHash = Hashing.Sha256Hex(snapshot);
    var noteSourceManifestHash = presentation.Manifest.NoteSourceManifestHash;
    if (noteSourceManifestHash is null || noteSourceManifestHash.Length != 64 ||
        !noteSourceManifestHash.All(char.IsAsciiHexDigit))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The statement presentation does not contain a complete note-source manifest.");

    FinancialPackage? comparative = null;
    if (package.ComparativePackageId is Guid comparativeId)
    {
      comparative = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == comparativeId && x.FirmId == actor.FirmId, ct);
      if (comparative is null || presentation.Manifest.ComparativePackageId != comparative.Id ||
          presentation.Manifest.ComparativeCalculationHash != comparative.CalculationHash)
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
          "The comparative package changed while the draft was being pinned.");
    }

    var revisionHash = PresentationRevisionHash(package, layout, comparative, noteSourceManifestHash, snapshotHash,
      FinancialStatementPresentationPolicy.BuilderVersion, FinancialStatementPresentationPolicy.RendererVersion,
      FinancialStatementPresentationPolicy.DisplayPolicy);
    var existing = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id && x.RevisionHash == revisionHash, ct);
    if (existing is not null)
    {
      if (!existing.SnapshotJson.AsSpan().SequenceEqual(snapshot) || existing.SnapshotSha256Hex != snapshotHash)
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
          "The existing pinned draft does not match the deterministic presentation snapshot.");
      await transaction.CommitAsync(ct);
      return await PinViewAsync(db, existing, ct);
    }

    var revision = new FinancialStatementPresentationRevision
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
      EngagementId = package.EngagementId, FinancialPackageId = package.Id,
      PackageRevision = package.Revision, PackageGeneration = package.Generation,
      PeriodId = package.PeriodId, BookId = package.BookId, Basis = package.Basis,
      Framework = package.Framework, TaxonomyVersion = package.TaxonomyVersion,
      TemplateVersion = package.TemplateVersion, CalculationEngineVersion = package.CalculationEngineVersion,
      Currency = package.Currency, PeriodStart = package.PeriodStart, PeriodEnd = package.PeriodEnd,
      PackageCalculationHash = package.CalculationHash, LayoutVersionId = layout.Id, LayoutHash = layout.LayoutHash,
      ComparativePackageId = comparative?.Id, ComparativePackageRevision = comparative?.Revision,
      ComparativePackageGeneration = comparative?.Generation,
      ComparativeCalculationHash = comparative?.CalculationHash,
      ComparativeBasis = package.ComparativeBasis, ComparativeEvidenceReference = package.ComparativeEvidenceReference,
      NoteSourceManifestHash = noteSourceManifestHash,
      BuilderVersion = FinancialStatementPresentationPolicy.BuilderVersion,
      RendererVersion = FinancialStatementPresentationPolicy.RendererVersion,
      DisplayPolicy = FinancialStatementPresentationPolicy.DisplayPolicy,
      Status = FinancialStatementPresentationRevisionStatuses.UnapprovedDraft,
      RevisionHash = revisionHash, SnapshotSha256Hex = snapshotHash, SnapshotJson = snapshot,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.FinancialStatementPresentationRevisions.Add(revision);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      await transaction.RollbackAsync(ct);
      await transaction.DisposeAsync();
      var raced = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id && x.RevisionHash == revisionHash, ct);
      if (raced is null || !raced.SnapshotJson.AsSpan().SequenceEqual(snapshot) || raced.SnapshotSha256Hex != snapshotHash)
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.IdempotencyConflict,
          "The pinned draft was created concurrently; reload its saved state before retrying.");
      return await PinViewAsync(db, raced, ct);
    }
    await transaction.CommitAsync(ct);
    return await PinViewAsync(db, revision, ct);
  }

  /// <summary>
  /// Promotes an eligible, unapproved draft presentation to an immutable signing candidate successor.
  /// The draft itself remains unapproved and immutable; the candidate has distinct bytes, candidate banner
  /// and predecessor linkage for formal review decisions (CP-18 / CP-19).
  /// </summary>
  public static async Task<CommandResult<FinancialStatementPresentationPin>> PromotePresentationToCandidateAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packageId, Guid draftRevisionId,
    CancellationToken ct = default)
  {
    if (packageId == Guid.Empty || draftRevisionId == Guid.Empty)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Select the package and unapproved draft presentation revision to promote.");

    var packageClientId = await db.FinancialPackages.AsNoTracking().Where(x =>
      x.Id == packageId && x.FirmId == actor.FirmId).Select(x => (Guid?)x.ClientId).SingleOrDefaultAsync(ct);
    if (packageClientId is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "Firm safety state is unavailable.");
    var lockedSafety = await LockClientAsync(db, actor.FirmId, packageClientId.Value, ct);
    if (lockedSafety is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "Client safety state is unavailable.");

    var package = await db.FinancialPackages.FromSqlInterpolated(
      $"SELECT * FROM financial_packages WHERE id = {packageId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (package is null || package.ClientId != packageClientId.Value)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialStatementPresentationPin>.Fail(auth.ErrorCode!, auth.Message!);

    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "The financial package must be validated before promoting to candidate.");

    var readiness = await FinancialPackageValidationReadiness.GetAsync(db, package.FirmId, package.Id, ct);
    if (!readiness.Ready)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked, readiness.Message);

    if (!await CashFlowSourceApprovedAsync(db, package, ct))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "Current cash-flow source links need independent approval before promoting to candidate.");

    FinancialPackage? comparative = null;
    if (package.ComparativePackageId is Guid compId)
    {
      comparative = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == compId && x.FirmId == package.FirmId, ct);
      if (comparative is null)
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
          "The comparative package is no longer available.");
      if (!await CashFlowSourceApprovedAsync(db, comparative, ct))
        return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
          "Comparative cash-flow source links need independent approval before promoting to candidate.");
    }

    var draft = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == draftRevisionId && x.FirmId == actor.FirmId && x.FinancialPackageId == packageId, ct);
    if (draft is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The draft presentation revision does not exist.");
    if (draft.Status != FinancialStatementPresentationRevisionStatuses.UnapprovedDraft)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Only an unapproved draft can be promoted to a signing candidate.");

    if (!await PresentationSourceIsCurrentAsync(db, draft, ct))
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
        "The draft presentation source is no longer current.");

    var draftPdf = await db.FinancialStatementPresentationArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PresentationRevisionId == draft.Id && x.Format == FinancialStatementPresentationRenderer.PdfFormat, ct);
    if (draftPdf is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        "Render the draft statement presentation PDF before promoting to a candidate.");

    var existingCandidate = await db.FinancialStatementPresentationRevisions.AsNoTracking().FirstOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
      x.PredecessorRevisionId == draft.Id && x.Status == FinancialStatementPresentationRevisionStatuses.Candidate, ct);
    if (existingCandidate is not null)
    {
      await transaction.RollbackAsync(ct);
      return await PinViewAsync(db, existingCandidate, ct);
    }

    FinancialStatementPresentation? draftPresentation;
    try { draftPresentation = JsonSerializer.Deserialize<FinancialStatementPresentation>(draft.SnapshotJson, PresentationSnapshotJson); }
    catch (JsonException)
    {
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The draft snapshot could not be decoded safely.");
    }
    if (draftPresentation is null)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The draft snapshot could not be decoded safely.");

    var layout = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == draft.LayoutVersionId && x.FirmId == actor.FirmId && x.ClientId == package.ClientId, ct);
    if (layout is null || layout.Status != StatementLayoutStatuses.Published || layout.LayoutHash != draft.LayoutHash)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.StaleRevision,
        "The published layout no longer matches this draft.");

    // Candidate eligibility is complete final-content readiness: every presentation check must pass. The professional content checks
    // pass only through an independent approval of this exact draft snapshot; a missing schedule, comparative or reconciliation still
    // refuses, however many canonical package validations pass.
    var contentReview = await ApprovedStatementContentReviewAsync(db, draft, ct);
    var candidateChecks = CandidateChecks(draftPresentation.Checks, contentReview);
    var unresolved = UnresolvedChecks(candidateChecks);
    if (unresolved.Count > 0)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.GateBlocked,
        $"The draft is not ready to become a signing candidate under {StatementReadinessProfileVersion}. Unresolved checks: " +
        string.Join(", ", unresolved) + (contentReview is null && unresolved.Any(ContentReviewedChecks.Contains)
          ? ". Note, disclosure and equity content needs an independent content review of this exact draft." : "."));
    var candidateWarnings = contentReview is null ? draftPresentation.Warnings
      : draftPresentation.Warnings.Where(x => !ContentReviewPendingWarnings.Contains(x.Code, StringComparer.Ordinal)).ToList();
    var candidatePresentation = draftPresentation with
    {
      Status = FinancialStatementPresentationRevisionStatuses.Candidate, Checks = candidateChecks, Warnings = candidateWarnings
    };
    var candidateSnapshot = JsonSerializer.SerializeToUtf8Bytes(candidatePresentation, PresentationSnapshotJson);
    var candidateSnapshotHash = Hashing.Sha256Hex(candidateSnapshot);
    var candidateRevisionHash = PresentationRevisionHash(package, layout, comparative, draft.NoteSourceManifestHash,
      candidateSnapshotHash, draft.BuilderVersion, draft.RendererVersion, draft.DisplayPolicy);

    var candidateRevisionId = Guid.CreateVersion7();
    var candidateRevision = new FinancialStatementPresentationRevision
    {
      Id = candidateRevisionId,
      FirmId = draft.FirmId,
      ClientId = draft.ClientId,
      EngagementId = draft.EngagementId,
      FinancialPackageId = draft.FinancialPackageId,
      PackageRevision = draft.PackageRevision,
      PackageGeneration = draft.PackageGeneration,
      PeriodId = draft.PeriodId,
      BookId = draft.BookId,
      Basis = draft.Basis,
      Framework = draft.Framework,
      TaxonomyVersion = draft.TaxonomyVersion,
      TemplateVersion = draft.TemplateVersion,
      CalculationEngineVersion = draft.CalculationEngineVersion,
      Currency = draft.Currency,
      PeriodStart = draft.PeriodStart,
      PeriodEnd = draft.PeriodEnd,
      PackageCalculationHash = draft.PackageCalculationHash,
      LayoutVersionId = draft.LayoutVersionId,
      LayoutHash = draft.LayoutHash,
      ComparativePackageId = draft.ComparativePackageId,
      ComparativePackageRevision = draft.ComparativePackageRevision,
      ComparativePackageGeneration = draft.ComparativePackageGeneration,
      ComparativeCalculationHash = draft.ComparativeCalculationHash,
      ComparativeBasis = draft.ComparativeBasis,
      ComparativeEvidenceReference = draft.ComparativeEvidenceReference,
      NoteSourceManifestHash = draft.NoteSourceManifestHash,
      BuilderVersion = draft.BuilderVersion,
      RendererVersion = draft.RendererVersion,
      DisplayPolicy = draft.DisplayPolicy,
      Status = FinancialStatementPresentationRevisionStatuses.Candidate,
      PredecessorRevisionId = draft.Id,
      SnapshotJson = candidateSnapshot,
      SnapshotSha256Hex = candidateSnapshotHash,
      RevisionHash = candidateRevisionHash,
      CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };

    var candidatePdfBytes = FinancialStatementPresentationRenderer.Render(candidatePresentation, FinancialStatementPresentationRenderer.PdfFormat);
    if (candidatePdfBytes.Length is 0 or > 32 * 1024 * 1024)
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The rendered candidate presentation exceeds the bounded artifact size.");

    var candidatePdfArtifactId = Guid.CreateVersion7();
    var candidatePdfFileName = $"auditsphere-statement-presentation-{candidateRevision.FinancialPackageId:D}-{candidateRevision.Id:N}.pdf";
    var candidatePackageArtifact = new FinancialPackageArtifact
    {
      Id = candidatePdfArtifactId,
      FirmId = candidateRevision.FirmId,
      ClientId = candidateRevision.ClientId,
      EngagementId = candidateRevision.EngagementId,
      FinancialPackageId = candidateRevision.FinancialPackageId,
      PackageRevision = candidateRevision.PackageRevision,
      PackageGeneration = candidateRevision.PackageGeneration,
      PackageHash = candidateRevision.PackageCalculationHash,
      ArtifactVersion = FinancialPackageArtifactVersions.StatementPresentation(candidateRevision.Id, FinancialStatementPresentationRenderer.PdfFormat),
      FrameworkVersion = candidateRevision.Framework,
      TemplateVersion = $"statement-layout:{candidateRevision.LayoutVersionId:D}",
      ArtifactSha256Hex = Hashing.Sha256Hex(candidatePdfBytes),
      ArtifactBytes = candidatePdfBytes,
      CreatedByUserId = actor.UserId,
      CreatedAt = DateTimeOffset.UtcNow
    };
    var candidateArtifactMetadata = new FinancialStatementPresentationArtifact
    {
      Id = candidatePdfArtifactId,
      FirmId = candidateRevision.FirmId,
      ClientId = candidateRevision.ClientId,
      EngagementId = candidateRevision.EngagementId,
      PresentationRevisionId = candidateRevision.Id,
      Format = FinancialStatementPresentationRenderer.PdfFormat,
      FileName = candidatePdfFileName,
      ContentType = FinancialStatementPresentationRenderer.PdfContentType,
      RendererVersion = candidateRevision.RendererVersion
    };

    db.FinancialStatementPresentationRevisions.Add(candidateRevision);
    db.FinancialPackageArtifacts.Add(candidatePackageArtifact);
    db.FinancialStatementPresentationArtifacts.Add(candidateArtifactMetadata);

    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      await transaction.RollbackAsync(ct);
      var raced = await db.FinancialStatementPresentationRevisions.AsNoTracking().FirstOrDefaultAsync(x =>
        x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id &&
        x.PredecessorRevisionId == draft.Id && x.Status == FinancialStatementPresentationRevisionStatuses.Candidate, ct);
      if (raced is not null)
        return await PinViewAsync(db, raced, ct);
      return CommandResult<FinancialStatementPresentationPin>.Fail(ErrorCodes.IdempotencyConflict,
        "The candidate presentation was created concurrently; reload its state before retrying.");
    }

    await transaction.CommitAsync(ct);
    return await PinViewAsync(db, candidateRevision, ct);
  }


  public static async Task<CommandResult<IReadOnlyList<FinancialStatementPresentationPin>>> ListPresentationPinsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packageId, CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<IReadOnlyList<FinancialStatementPresentationPin>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PackageReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<FinancialStatementPresentationPin>>.Fail(auth.ErrorCode!, auth.Message!);
    var revisions = await db.FinancialStatementPresentationRevisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == package.Id)
      .OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).Take(50).ToListAsync(ct);
    var views = new List<FinancialStatementPresentationPin>(revisions.Count);
    foreach (var revision in revisions)
    {
      var view = await PinViewAsync(db, revision, ct);
      if (!view.Succeeded || view.Value is null)
        return CommandResult<IReadOnlyList<FinancialStatementPresentationPin>>.Fail(view.ErrorCode!, view.Message!);
      views.Add(view.Value);
    }
    return CommandResult<IReadOnlyList<FinancialStatementPresentationPin>>.Ok(views);
  }

  /// <summary>Returns the exact stored presentation snapshot after checking its hash and pinned identities.</summary>
  public static async Task<CommandResult<FinancialStatementPresentation>> GetPinnedPresentationAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid packageId, Guid revisionId, CancellationToken ct = default)
  {
    var revision = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == actor.FirmId, ct);
    if (revision is null || revision.FinancialPackageId != packageId)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revision.FinancialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null || package.ClientId != revision.ClientId || package.EngagementId != revision.EngagementId)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PackageReadRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<FinancialStatementPresentation>.Fail(auth.ErrorCode!, auth.Message!);

    if (package.Revision != revision.PackageRevision || package.Generation != revision.PackageGeneration ||
        package.PeriodId != revision.PeriodId || package.BookId != revision.BookId || package.Basis != revision.Basis ||
        package.Framework != revision.Framework || package.TaxonomyVersion != revision.TaxonomyVersion ||
        package.TemplateVersion != revision.TemplateVersion || package.CalculationEngineVersion != revision.CalculationEngineVersion ||
        package.Currency != revision.Currency || package.PeriodStart != revision.PeriodStart || package.PeriodEnd != revision.PeriodEnd ||
        package.ComparativeBasis != revision.ComparativeBasis || package.ComparativeEvidenceReference != revision.ComparativeEvidenceReference ||
        package.CalculationHash != revision.PackageCalculationHash || package.ComparativePackageId != revision.ComparativePackageId)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
        "The package no longer matches the exact source revision recorded for this draft.");
    if (!await CashFlowSourceApprovedAsync(db, package, ct))
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
        "The package's period-specific cash-flow source review changed after this draft was pinned.");
    if (!await AcceptedTrialBalanceIsCurrentAsync(db, package, ct))
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
        "The package no longer uses the selected accepted trial-balance source for its reporting period.");
    var layout = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revision.LayoutVersionId && x.FirmId == revision.FirmId && x.ClientId == revision.ClientId, ct);
    if (layout is null || layout.Status != StatementLayoutStatuses.Published || layout.LayoutHash != revision.LayoutHash)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
        "The published layout no longer matches this pinned draft.");
    var lines = await StatementLayoutService.LoadLinesAsync(db, revision.FirmId, layout.Id, ct);
    if (StatementLayoutService.ComputeLayoutHash(lines) != revision.LayoutHash)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
        "The layout line content failed its pinned hash check.");
    FinancialPackage? comparative = null;
    if (revision.ComparativePackageId is Guid comparativeId)
    {
      comparative = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == comparativeId && x.FirmId == revision.FirmId, ct);
      if (comparative is null || comparative.ClientId != package.ClientId || comparative.Framework != package.Framework ||
          comparative.Currency != package.Currency ||
          comparative.Revision != revision.ComparativePackageRevision || comparative.Generation != revision.ComparativePackageGeneration ||
          comparative.CalculationHash != revision.ComparativeCalculationHash)
        return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
          "The comparative package no longer matches the exact source revision recorded for this draft.");
      if (!await CashFlowSourceApprovedAsync(db, comparative, ct))
        return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
          "The comparative package's period-specific cash-flow source review changed after this draft was pinned.");
      if (!await AcceptedTrialBalanceIsCurrentAsync(db, comparative, ct))
        return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.StaleRevision,
          "The comparative package no longer uses the selected accepted trial-balance source for its reporting period.");
    }
    var packageIds = comparative is null ? new[] { package.Id } : new[] { package.Id, comparative.Id };
    var noteLines = await db.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var currentNoteSourceManifestHash = await ComputePresentationSourceManifestHashAsync(db, actor.FirmId, packageIds, noteLines, disclosures, ct);
    if (Hashing.Sha256Hex(revision.SnapshotJson) != revision.SnapshotSha256Hex ||
        currentNoteSourceManifestHash != revision.NoteSourceManifestHash ||
        PresentationRevisionHash(package, layout, comparative, revision.NoteSourceManifestHash,
          revision.SnapshotSha256Hex, revision.BuilderVersion, revision.RendererVersion, revision.DisplayPolicy) != revision.RevisionHash)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The pinned draft failed its stored integrity check.");

    FinancialStatementPresentation? presentation;
    try { presentation = JsonSerializer.Deserialize<FinancialStatementPresentation>(revision.SnapshotJson, PresentationSnapshotJson); }
    catch (JsonException)
    {
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The pinned draft snapshot could not be decoded safely.");
    }
    var validStatus = presentation is not null &&
      FinancialStatementPresentationRevisionStatuses.All.Contains(presentation.Status) &&
      (presentation.Status == revision.Status ||
       (revision.Status == FinancialStatementPresentationRevisionStatuses.Approved && presentation.Status == FinancialStatementPresentationRevisionStatuses.Candidate) ||
       (revision.Status == FinancialStatementPresentationRevisionStatuses.Superseded && presentation.Status == FinancialStatementPresentationRevisionStatuses.Candidate));
    if (!validStatus || presentation is null ||
        presentation.Manifest.PackageId != revision.FinancialPackageId ||
        presentation.Manifest.CalculationHash != revision.PackageCalculationHash ||
        presentation.Manifest.LayoutVersionId != revision.LayoutVersionId || presentation.Manifest.LayoutHash != revision.LayoutHash ||
        presentation.Manifest.ComparativePackageId != revision.ComparativePackageId ||
        presentation.Manifest.ComparativeCalculationHash != revision.ComparativeCalculationHash ||
        presentation.Manifest.PeriodId != revision.PeriodId || presentation.Manifest.BookId != revision.BookId ||
        presentation.Manifest.Basis != revision.Basis || presentation.Manifest.Framework != revision.Framework ||
        presentation.Manifest.TaxonomyVersion != revision.TaxonomyVersion ||
        presentation.Manifest.TemplateVersion != revision.TemplateVersion ||
        presentation.Manifest.CalculationEngineVersion != revision.CalculationEngineVersion ||
        presentation.Manifest.Currency != revision.Currency || presentation.Manifest.PeriodStart != revision.PeriodStart ||
        presentation.Manifest.PeriodEnd != revision.PeriodEnd || presentation.Manifest.ComparativeBasis != revision.ComparativeBasis ||
        presentation.Manifest.ComparativeEvidenceReference != revision.ComparativeEvidenceReference ||
        presentation.Manifest.NoteSourceManifestHash != revision.NoteSourceManifestHash ||
        presentation.Manifest.BuilderVersion != revision.BuilderVersion || presentation.Manifest.DisplayPolicy != revision.DisplayPolicy)
      return CommandResult<FinancialStatementPresentation>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The pinned draft snapshot does not match its recorded manifest.");
    return CommandResult<FinancialStatementPresentation>.Ok(presentation);
  }

  /// <summary>Renders and stores exact draft bytes once. Stored bytes are returned unchanged on every retry.</summary>
  public static async Task<CommandResult<FinancialStatementPresentationPinnedArtifact>> RenderPinnedPresentationArtifactAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid revisionId, string format,
    CancellationToken ct = default)
  {
    var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
    var contentType = FinancialStatementPresentationRenderer.ContentTypeFor(normalized);
    if (contentType is null)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Choose PDF, Word or Excel for the statement presentation.");
    var revision = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == actor.FirmId, ct);
    if (revision is null)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorizedPackage = await AuthorizePinnedPackageAsync(db, actor, revision, ct);
    if (!authorizedPackage.Succeeded)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(authorizedPackage.ErrorCode!, authorizedPackage.Message!);
    var existing = await db.FinancialStatementPresentationArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == revision.FirmId && x.PresentationRevisionId == revision.Id && x.Format == normalized, ct);
    if (existing is not null)
    {
      var existingPackageArtifact = await PackageArtifactForPinAsync(db, revision, existing.Id, existing.Format, ct);
      return existingPackageArtifact is null
        ? CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "The saved presentation metadata has no matching immutable package artifact.")
        : ExistingPinnedArtifact(existing, existingPackageArtifact);
    }

    var presentation = await GetPinnedPresentationAsync(db, actor, revision.FinancialPackageId, revision.Id, ct);
    if (!presentation.Succeeded || presentation.Value is null)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(presentation.ErrorCode!, presentation.Message!);
    if (revision.RendererVersion != FinancialStatementPresentationPolicy.RendererVersion)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.StaleRevision,
        "This pinned draft uses a renderer version that is not available for new format generation.");

    var bytes = FinancialStatementPresentationRenderer.Render(presentation.Value, normalized);
    if (bytes.Length is 0 or > 32 * 1024 * 1024)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The rendered statement draft exceeds the bounded artifact size.");
    var artifactId = Guid.CreateVersion7();
    var fileName = $"auditsphere-statement-presentation-{revision.FinancialPackageId:D}-{revision.Id:N}.{normalized}";
    var packageArtifact = new FinancialPackageArtifact
    {
      Id = artifactId, FirmId = revision.FirmId, ClientId = revision.ClientId,
      EngagementId = revision.EngagementId, FinancialPackageId = revision.FinancialPackageId,
      PackageRevision = revision.PackageRevision, PackageGeneration = revision.PackageGeneration,
      PackageHash = revision.PackageCalculationHash,
      ArtifactVersion = FinancialPackageArtifactVersions.StatementPresentation(revision.Id, normalized),
      FrameworkVersion = revision.Framework, TemplateVersion = $"statement-layout:{revision.LayoutVersionId:D}",
      ArtifactSha256Hex = Hashing.Sha256Hex(bytes), ArtifactBytes = bytes,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    var artifactMetadata = new FinancialStatementPresentationArtifact
    {
      Id = artifactId, FirmId = revision.FirmId, ClientId = revision.ClientId,
      EngagementId = revision.EngagementId, PresentationRevisionId = revision.Id, Format = normalized,
      FileName = fileName, ContentType = contentType, RendererVersion = revision.RendererVersion
    };
    db.FinancialPackageArtifacts.Add(packageArtifact);
    db.FinancialStatementPresentationArtifacts.Add(artifactMetadata);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
      var raced = await db.FinancialStatementPresentationArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == revision.FirmId && x.PresentationRevisionId == revision.Id && x.Format == normalized, ct);
      if (raced is null)
        return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.IdempotencyConflict,
          "The draft export was rendered concurrently; reload its saved state before retrying.");
      var racedArtifact = await PackageArtifactForPinAsync(db, revision, raced.Id, raced.Format, ct);
      return racedArtifact is null
        ? CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "The concurrently saved presentation has no matching immutable package artifact.")
        : ExistingPinnedArtifact(raced, racedArtifact);
    }
    return PinnedArtifactView(artifactMetadata, packageArtifact);
  }

  public static async Task<CommandResult<FinancialStatementPresentationPinnedArtifact>> GetPinnedPresentationArtifactAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid revisionId, string format,
    CancellationToken ct = default)
  {
    var normalized = (format ?? string.Empty).Trim().ToLowerInvariant();
    if (FinancialStatementPresentationRenderer.ContentTypeFor(normalized) is null)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "Choose PDF, Word or Excel for the statement presentation.");
    var revision = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revisionId && x.FirmId == actor.FirmId, ct);
    if (revision is null)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var authorizedPackage = await AuthorizePinnedPackageAsync(db, actor, revision, ct);
    if (!authorizedPackage.Succeeded)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(authorizedPackage.ErrorCode!, authorizedPackage.Message!);
    var artifact = await db.FinancialStatementPresentationArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.PresentationRevisionId == revisionId && x.Format == normalized, ct);
    if (artifact is null)
      return CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "No saved draft artifact exists for that format yet.");
    var packageArtifact = await PackageArtifactForPinAsync(db, revision, artifact.Id, artifact.Format, ct);
    return packageArtifact is null
      ? CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The saved presentation metadata has no matching immutable package artifact.")
      : ExistingPinnedArtifact(artifact, packageArtifact);
  }

  private static async Task<CommandResult<FinancialStatementPresentationPin>> PinViewAsync(
    IClientAccountingDbContext db, FinancialStatementPresentationRevision revision, CancellationToken ct)
  {
    var formats = await db.FinancialStatementPresentationArtifacts.AsNoTracking()
      .Where(x => x.FirmId == revision.FirmId && x.PresentationRevisionId == revision.Id)
      .OrderBy(x => x.Format).Select(x => x.Format).ToListAsync(ct);
    var sourceIsCurrent = await PresentationSourceIsCurrentAsync(db, revision, ct);
    var composition = await db.FinancialStatementCompositions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == revision.FirmId && x.PresentationRevisionId == revision.Id, ct);
    return CommandResult<FinancialStatementPresentationPin>.Ok(new FinancialStatementPresentationPin(
      revision.Id, revision.FinancialPackageId, revision.PackageCalculationHash, revision.PeriodId,
      revision.BookId, revision.Basis, revision.Framework, revision.TaxonomyVersion,
      revision.TemplateVersion, revision.CalculationEngineVersion, revision.Currency, revision.PeriodStart,
      revision.PeriodEnd, revision.LayoutVersionId, revision.LayoutHash, revision.ComparativePackageId,
      revision.ComparativeCalculationHash, revision.ComparativeBasis, revision.ComparativeEvidenceReference,
      revision.NoteSourceManifestHash,
      revision.BuilderVersion, revision.RendererVersion, revision.DisplayPolicy, revision.Status,
      revision.RevisionHash, revision.SnapshotSha256Hex, revision.CreatedAt, sourceIsCurrent, formats,
      revision.PredecessorRevisionId, composition?.Id, composition?.ManifestDigest));
  }

  private static async Task<CommandResult<FinancialPackage>> AuthorizePinnedPackageAsync(
    IClientAccountingDbContext db, ActorContext actor, FinancialStatementPresentationRevision revision,
    CancellationToken ct)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revision.FinancialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null || package.ClientId != revision.ClientId || package.EngagementId != revision.EngagementId)
      return CommandResult<FinancialPackage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeAsync(db, actor, package.FirmId, package.ClientId, package.EngagementId, PackageReadRoles, ct);
    return auth.Succeeded
      ? CommandResult<FinancialPackage>.Ok(package)
      : CommandResult<FinancialPackage>.Fail(auth.ErrorCode!, auth.Message!);
  }

  internal static async Task<bool> PresentationSourceIsCurrentAsync(
    IClientAccountingDbContext db, FinancialStatementPresentationRevision revision, CancellationToken ct)
  {
    var package = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revision.FinancialPackageId && x.FirmId == revision.FirmId, ct);
    if (package is null || package.ClientId != revision.ClientId || package.EngagementId != revision.EngagementId ||
        package.Revision != revision.PackageRevision || package.Generation != revision.PackageGeneration ||
        package.PeriodId != revision.PeriodId || package.BookId != revision.BookId || package.Basis != revision.Basis ||
        package.Framework != revision.Framework || package.TaxonomyVersion != revision.TaxonomyVersion ||
        package.TemplateVersion != revision.TemplateVersion || package.CalculationEngineVersion != revision.CalculationEngineVersion ||
        package.Currency != revision.Currency || package.PeriodStart != revision.PeriodStart || package.PeriodEnd != revision.PeriodEnd ||
        package.CalculationHash != revision.PackageCalculationHash || package.ComparativePackageId != revision.ComparativePackageId ||
        package.ComparativeBasis != revision.ComparativeBasis || package.ComparativeEvidenceReference != revision.ComparativeEvidenceReference)
      return false;
    if (!await CashFlowSourceApprovedAsync(db, package, ct))
      return false;
    if (!await AcceptedTrialBalanceIsCurrentAsync(db, package, ct))
      return false;

    var layout = await db.StatementLayoutVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == revision.LayoutVersionId && x.FirmId == revision.FirmId && x.ClientId == revision.ClientId, ct);
    if (layout is null || layout.Status != StatementLayoutStatuses.Published || layout.LayoutHash != revision.LayoutHash)
      return false;
    var layoutLines = await StatementLayoutService.LoadLinesAsync(db, revision.FirmId, revision.LayoutVersionId, ct);
    if (StatementLayoutService.ComputeLayoutHash(layoutLines) != revision.LayoutHash)
      return false;

    FinancialPackage? comparative = null;
    if (revision.ComparativePackageId is Guid comparativeId)
    {
      comparative = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == comparativeId && x.FirmId == revision.FirmId, ct);
      if (comparative is null || comparative.ClientId != package.ClientId || comparative.Framework != package.Framework ||
          comparative.Currency != package.Currency || comparative.Revision != revision.ComparativePackageRevision ||
          comparative.Generation != revision.ComparativePackageGeneration || comparative.CalculationHash != revision.ComparativeCalculationHash)
        return false;
      if (!await CashFlowSourceApprovedAsync(db, comparative, ct))
        return false;
      if (!await AcceptedTrialBalanceIsCurrentAsync(db, comparative, ct))
        return false;
    }
    var packageIds = comparative is null ? new[] { package.Id } : new[] { package.Id, comparative.Id };
    var noteLines = await db.FinancialPackageNoteLines.AsNoTracking()
      .Where(x => x.FirmId == revision.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking()
      .Where(x => x.FirmId == revision.FirmId && packageIds.Contains(x.FinancialPackageId)).ToListAsync(ct);
    return await ComputePresentationSourceManifestHashAsync(db, revision.FirmId, packageIds, noteLines, disclosures, ct) == revision.NoteSourceManifestHash &&
      Hashing.Sha256Hex(revision.SnapshotJson) == revision.SnapshotSha256Hex &&
      PresentationRevisionHash(package, layout, comparative, revision.NoteSourceManifestHash,
        revision.SnapshotSha256Hex, revision.BuilderVersion, revision.RendererVersion, revision.DisplayPolicy) == revision.RevisionHash;
  }

  /// <summary>
  /// A later accepted TB revision supersedes only the same period/book/basis/currency source.
  /// The client-wide input generation is deliberately not used here because other periods
  /// may be accepted independently and must remain usable as comparatives.
  /// </summary>
  private static async Task<bool> AcceptedTrialBalanceIsCurrentAsync(
    IClientAccountingDbContext db, FinancialPackage package, CancellationToken ct)
  {
    var baseDatasetId = await (from plan in db.AdjustmentPlans.AsNoTracking()
        join dataset in db.TrialBalanceDatasets.AsNoTracking() on plan.BaseDatasetId equals dataset.Id
        where plan.Id == package.AdjustmentPlanId && plan.FirmId == package.FirmId &&
          plan.ClientId == package.ClientId && plan.EngagementId == package.EngagementId &&
          dataset.FirmId == package.FirmId && dataset.ClientId == package.ClientId &&
          dataset.EngagementId == package.EngagementId
        select new { DatasetId = dataset.Id, dataset.PeriodId, dataset.BookId, dataset.Basis, dataset.Currency })
      .SingleOrDefaultAsync(ct);
    if (baseDatasetId is null || baseDatasetId.PeriodId != package.PeriodId || baseDatasetId.BookId != package.BookId ||
        !string.Equals(baseDatasetId.Basis, package.Basis, StringComparison.OrdinalIgnoreCase) ||
        !string.Equals(baseDatasetId.Currency, package.Currency, StringComparison.Ordinal))
      return false;

    var selectedDatasetId = await (from decision in db.SourceAcceptanceDecisions.AsNoTracking()
        join dataset in db.TrialBalanceDatasets.AsNoTracking() on decision.TrialBalanceDatasetId equals dataset.Id
        where decision.FirmId == package.FirmId && decision.ClientId == package.ClientId &&
          decision.EngagementId == package.EngagementId && decision.SourceKind == AccountingSourceKinds.TrialBalance &&
          decision.Decision == "ACCEPTED" && dataset.FirmId == package.FirmId &&
          dataset.ClientId == package.ClientId && dataset.EngagementId == package.EngagementId &&
          dataset.PeriodId == package.PeriodId && dataset.BookId == package.BookId &&
          dataset.Basis == baseDatasetId.Basis && dataset.Currency == package.Currency
        orderby decision.CreatedAt descending, decision.Id descending
        select (Guid?)dataset.Id).FirstOrDefaultAsync(ct);

    // Preserve legacy packages where no accepted TB pointer exists for this exact source scope.
    return selectedDatasetId is null || selectedDatasetId == baseDatasetId.DatasetId;
  }

  private static async Task<FinancialPackageArtifact?> PackageArtifactForPinAsync(
    IClientAccountingDbContext db, FinancialStatementPresentationRevision revision, Guid artifactId,
    string format, CancellationToken ct)
  {
    var artifactVersion = FinancialPackageArtifactVersions.StatementPresentation(revision.Id, format);
    return await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
      x.Id == artifactId && x.FirmId == revision.FirmId && x.ClientId == revision.ClientId &&
      x.EngagementId == revision.EngagementId && x.FinancialPackageId == revision.FinancialPackageId &&
      x.PackageRevision == revision.PackageRevision && x.PackageGeneration == revision.PackageGeneration &&
      x.PackageHash == revision.PackageCalculationHash && x.ArtifactVersion == artifactVersion, ct);
  }

  private static CommandResult<FinancialStatementPresentationPinnedArtifact> ExistingPinnedArtifact(
    FinancialStatementPresentationArtifact metadata, FinancialPackageArtifact artifact) =>
    artifact.ArtifactBytes.Length == 0 || Hashing.Sha256Hex(artifact.ArtifactBytes) != artifact.ArtifactSha256Hex ||
    artifact.ArtifactVersion != FinancialPackageArtifactVersions.StatementPresentation(metadata.PresentationRevisionId, metadata.Format) ||
    FinancialStatementPresentationRenderer.ContentTypeFor(metadata.Format) != metadata.ContentType ||
    string.IsNullOrWhiteSpace(metadata.FileName) || string.IsNullOrWhiteSpace(metadata.RendererVersion)
      ? CommandResult<FinancialStatementPresentationPinnedArtifact>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "The saved draft artifact failed its exact-byte integrity check.")
      : PinnedArtifactView(metadata, artifact);

  private static CommandResult<FinancialStatementPresentationPinnedArtifact> PinnedArtifactView(
    FinancialStatementPresentationArtifact metadata, FinancialPackageArtifact artifact) =>
    CommandResult<FinancialStatementPresentationPinnedArtifact>.Ok(new FinancialStatementPresentationPinnedArtifact(
      metadata.PresentationRevisionId, artifact.Id, metadata.Format, metadata.FileName, metadata.ContentType,
      metadata.RendererVersion, artifact.ArtifactSha256Hex, artifact.ArtifactBytes));

  private static string PresentationRevisionHash(
    FinancialPackage package, StatementLayoutVersion layout, FinancialPackage? comparative,
    string noteSourceManifestHash, string snapshotHash, string builderVersion, string rendererVersion, string displayPolicy) =>
    Hashing.Sha256Hex(JsonSerializer.Serialize(new
    {
      Version = "auditsphere.statement-presentation-revision.v1",
      package.FirmId,
      package.ClientId,
      package.EngagementId,
      PackageId = package.Id,
      package.Revision,
      package.Generation,
      package.PeriodId,
      package.BookId,
      package.Basis,
      package.Framework,
      package.TaxonomyVersion,
      package.TemplateVersion,
      package.CalculationEngineVersion,
      package.Currency,
      package.PeriodStart,
      package.PeriodEnd,
      PackageCalculationHash = package.CalculationHash,
      LayoutVersionId = layout.Id,
      layout.LayoutHash,
      ComparativePackageId = comparative?.Id,
      ComparativePackageRevision = comparative?.Revision,
      ComparativePackageGeneration = comparative?.Generation,
      ComparativeCalculationHash = comparative?.CalculationHash,
      package.ComparativeBasis,
      package.ComparativeEvidenceReference,
      NoteSourceManifestHash = noteSourceManifestHash,
      BuilderVersion = builderVersion,
      RendererVersion = rendererVersion,
      DisplayPolicy = displayPolicy,
      SnapshotSha256Hex = snapshotHash
    }));
}
