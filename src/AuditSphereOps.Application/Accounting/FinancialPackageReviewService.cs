using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialPackageReviewRequest(
  Guid FinancialPackageId, string Stage, string Decision, string EvidenceMode,
  string EvidenceReference, string Comment,
  Guid? PresentationRevisionId = null, string? TargetArtifactVersion = null,
  Guid? PresentationCompositionId = null, string? CompositionManifestDigest = null,
  Guid? RequestId = null, Guid? ExpectedLatestDecisionId = null);

public sealed record FinancialPackageReviewSummary(
  Guid Id, string Stage, string Decision, string EvidenceMode, string PackageHash,
  Guid ArtifactId, string ArtifactVersion, string ArtifactSha256Hex,
  string EvidenceReference, string Comment, Guid? DecidedByUserId, DateTimeOffset DecidedAt,
  Guid? PresentationRevisionId = null, Guid? PresentationCompositionId = null, string? CompositionManifestDigest = null);

public sealed record ClientFinancialPackageLineSummary(string StatementSection, decimal Amount);

/// <summary>
/// The exact three-format composition a client manager is asked to approve. Its identity and digest are what the management decision
/// names; the PDF fingerprint lets the manager confirm the downloaded document is the one the decision covers.
/// </summary>
public sealed record ClientFinancialPackageComposition(
  Guid PresentationRevisionId, Guid CompositionId, string CompositionManifestDigest,
  string PdfSha256Hex, string WordSha256Hex, string ExcelSha256Hex, DateTimeOffset SelectedAt);

public sealed record ClientFinancialPackageView(
  Guid PackageId, string Framework, string PeriodStart, string PeriodEnd, string Currency,
  string TemplateVersion, string PackageHash, bool HasCashFlow, bool HasDisclosures,
  string ManagementDecision, DateTimeOffset? ManagementDecidedAt,
  IReadOnlyList<ClientFinancialPackageLineSummary> StatementTotals,
  ClientFinancialPackageComposition? Composition = null, Guid? LatestDecisionId = null);

public sealed record FinancialPackageReviewQueueItem(
  Guid PackageId, string Framework, string PeriodStart, string PeriodEnd, string Currency,
  string PackageHash, string ManagementDecision, string AccountingDecision,
  string PartnerDecision, string NextAction);

public static partial class FinancialPackageReviewService
{
  private static readonly string[] AccountingRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  // Professional Partner authority is never derived from administration. Matches AcceptanceDecisionService.PartnerRoles.
  private static readonly string[] PartnerRoles = ["Partner"];
  private static readonly string[] OfflineManagementRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

  /// <summary>Compatibility alias for the canonical required validation population.</summary>
  public static readonly string[] BlockingValidationCodes = FinancialPackageValidationReadiness.RequiredCodes;

  public static async Task<CommandResult<Guid>> RecordAsync(
    IClientAccountingDbContext db, ActorContext actor, FinancialPackageReviewRequest request,
    CancellationToken ct = default)
  {
    var stage = request.Stage?.Trim().ToUpperInvariant() ?? string.Empty;
    var decision = request.Decision?.Trim().ToUpperInvariant() ?? string.Empty;
    var evidenceMode = request.EvidenceMode?.Trim().ToUpperInvariant() ?? string.Empty;
    var evidenceReference = request.EvidenceReference?.Trim() ?? string.Empty;
    var comment = request.Comment?.Trim() ?? string.Empty;
    if (request.FinancialPackageId == Guid.Empty || !FinancialPackageReviewStages.All.Contains(stage) ||
        !FinancialPackageReviewDecisions.All.Contains(decision) ||
        evidenceMode is not (FinancialPackageReviewEvidenceModes.SignedIn or FinancialPackageReviewEvidenceModes.Offline) ||
        evidenceReference.Length is < 1 or > 2000 || comment.Length > 4000)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "A valid package review decision and evidence reference are required.");
    if ((request.PresentationCompositionId is null) != (request.CompositionManifestDigest is null) ||
        request.CompositionManifestDigest is { } suppliedDigest && !IsSha256(suppliedDigest) ||
        request.PresentationRevisionId is null && request.PresentationCompositionId is not null)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
        "A presentation composition must include its exact revision and lowercase SHA-256 manifest digest.");
    if (evidenceMode == FinancialPackageReviewEvidenceModes.Offline &&
        stage != FinancialPackageReviewStages.ManagementApproval)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "Offline evidence is only supported for management approval.");
    if (request.RequestId == Guid.Empty)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "A decision request identity cannot be empty.");

    var unlocked = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.FinancialPackageId && x.FirmId == actor.FirmId, ct);
    if (unlocked is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var (roles, internalOnly) = stage switch
    {
      FinancialPackageReviewStages.ManagementApproval when evidenceMode == FinancialPackageReviewEvidenceModes.SignedIn
        => (new[] { "ClientUser" }, false),
      FinancialPackageReviewStages.ManagementApproval => (OfflineManagementRoles, true),
      FinancialPackageReviewStages.AccountingReview => (AccountingRoles, true),
      FinancialPackageReviewStages.PartnerApproval => (PartnerRoles, true),
      _ => (Array.Empty<string>(), true)
    };
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(unlocked.FirmId, unlocked.ClientId, unlocked.EngagementId, roles, InternalOnly: internalOnly), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // Shared lock order (firm safety, client safety, package, then dependent rows): the client safety row, the package row, then the
    // decider's grants. Decisions on one package serialize on the package row, so concurrent approvers cannot build an inconsistent chain.
    // Authority is checked again after the locks are held, because a grant can be revoked while this command waits.
    await using var tx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
    await db.ClientSafetyStates.FromSqlInterpolated(
        $"SELECT * FROM client_safety_states WHERE firm_id = {unlocked.FirmId} AND id = {unlocked.ClientId} FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    var package = await db.FinancialPackages.FromSqlInterpolated(
        $"SELECT * FROM financial_packages WHERE firm_id = {unlocked.FirmId} AND id = {unlocked.Id} FOR UPDATE")
      .AsNoTracking().SingleOrDefaultAsync(ct);
    if (package is null || package.ClientId != unlocked.ClientId)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    await db.RoleGrants.FromSqlInterpolated(
        $"SELECT * FROM role_grants WHERE firm_id = {package.FirmId} AND user_id = {actor.UserId} AND revoked_at IS NULL FOR SHARE")
      .AsNoTracking().ToListAsync(ct);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, roles, InternalOnly: internalOnly), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    // The same original intent recovers the stored decision. The same identity with a different payload is a conflict, never an overwrite.
    var intentHash = request.RequestId is Guid
      ? ReviewRequestHash(stage, decision, evidenceMode, evidenceReference, comment, request.PresentationRevisionId,
          request.PresentationCompositionId, request.CompositionManifestDigest, request.ExpectedLatestDecisionId)
      : null;
    if (request.RequestId is Guid requestId)
    {
      var recorded = await db.FinancialPackageReviewDecisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == package.FirmId && x.FinancialPackageId == package.Id && x.RequestId == requestId, ct);
      if (recorded is not null)
      {
        // A signed-in request identity belongs to the person who recorded it. Another person presenting it is refused.
        if (recorded.EvidenceMode == FinancialPackageReviewEvidenceModes.SignedIn && recorded.DecidedByUserId != actor.UserId)
          return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request was already used by another person.");
        return recorded.RequestHash == intentHash
          ? CommandResult<Guid>.Ok(recorded.Id)
          : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This request was already used for a different package decision.");
      }
    }

    var readiness = await FinancialPackageValidationReadiness.GetAsync(db, package.FirmId, package.Id, ct);
    if (!readiness.Ready)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, readiness.Message);
    if (!await CashFlowSourcesIndependentlyApprovedAsync(db, actor, package, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
        "Current and comparative cash-flow source links need independent approval against the accepted general ledger.");

    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The financial package must be validated before review.");

    FinancialStatementPresentationRevision? candidate = null;
    FinancialStatementComposition? composition = null;
    FinancialPackageArtifact? artifact = null;
    if (request.PresentationRevisionId is Guid candidateRevisionId)
    {
      candidate = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == candidateRevisionId && x.FirmId == package.FirmId && x.FinancialPackageId == package.Id, ct);
      if (candidate is null)
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "The specified candidate presentation does not exist.");
      if (candidate.Status is not (FinancialStatementPresentationRevisionStatuses.Candidate or FinancialStatementPresentationRevisionStatuses.Approved))
        return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid,
          "Formal review decisions can only be recorded against an active signing candidate.");
      if (!await FinancialStatementService.PresentationSourceIsCurrentAsync(db, candidate, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
          "The candidate presentation source is no longer current; generate a new signing candidate before deciding.");
      var comparativeAccess = await FinancialStatementService.AuthorizeComparativeAccessAsync(db, actor, package, ct);
      if (!comparativeAccess.Succeeded)
        return CommandResult<Guid>.Fail(comparativeAccess.ErrorCode!, comparativeAccess.Message!);
      if (!FinancialStatementService.PresentationChecksAllPass(candidate))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "This signing candidate carries unresolved presentation checks. Complete the content review and create a new candidate before deciding.");

      composition = await db.FinancialStatementCompositions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id && x.PresentationRevisionId == candidate.Id, ct);
      if (composition is null || !await CompositionArtifactsAreIntactAsync(db, package, candidate, composition, ct))
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "Seal the candidate's exact PDF, Word and Excel composition before recording a formal review decision.");
      if (request.PresentationCompositionId != composition.Id || request.CompositionManifestDigest != composition.ManifestDigest)
        return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch,
          "The selected composition identity or manifest changed. Refresh the presentation and select the current composition before reviewing.");

      var targetArtifactVersion = FinancialPackageArtifactVersions.StatementPresentation(candidate.Id, FinancialStatementPresentationRenderer.PdfFormat);
      if (!string.IsNullOrWhiteSpace(request.TargetArtifactVersion) &&
          !string.Equals(request.TargetArtifactVersion.Trim(), targetArtifactVersion, StringComparison.Ordinal))
        return CommandResult<Guid>.Fail(ErrorCodes.ManifestMismatch,
          "Formal review is bound to the PDF in the selected three-format composition.");
      artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == composition.PdfArtifactId && x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation && x.PackageHash == package.CalculationHash &&
        x.ArtifactVersion == targetArtifactVersion && x.ArtifactSha256Hex == composition.PdfSha256Hex, ct);
      if (artifact is null || artifact.ArtifactBytes.Length == 0)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "The reviewed candidate presentation artifact (PDF) has not been rendered yet.");

    }
    else
    {
      artifact = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation && x.PackageHash == package.CalculationHash &&
        x.ArtifactVersion == FinancialPackageArtifactVersions.Text, ct);
      if (artifact is null || artifact.ArtifactBytes.Length == 0 ||
          artifact.FrameworkVersion != package.Framework || artifact.TemplateVersion != package.TemplateVersion)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "A deterministic rendered artifact for the exact package framework/template version is required before review.");
    }

    if (evidenceMode == FinancialPackageReviewEvidenceModes.SignedIn)
    {
      var samePersonElsewhere = await db.FinancialPackageReviewDecisions.AsNoTracking().AnyAsync(x =>
        x.FirmId == package.FirmId && x.FinancialPackageId == package.Id &&
        x.PackageHash == package.CalculationHash && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation && x.Stage != stage &&
        x.Decision == FinancialPackageReviewDecisions.Approved && x.DecidedByUserId == actor.UserId, ct);
      if (samePersonElsewhere)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked,
          "The same person cannot record approved decisions at two different stages of one package version.");
    }

    if (stage == FinancialPackageReviewStages.PartnerApproval &&
        !await PartnerDecisionHeldByPartnerAsync(db, package, actor.UserId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied,
        "Only the currently assigned Engagement Partner can record this Partner decision.");

    // The decision chain is every decision on this exact package version and composition identity (none, for the legacy package path).
    Guid? selectedPresentationId = candidate?.Id;
    Guid? selectedCompositionId = composition?.Id;
    var selectedCompositionDigest = composition?.ManifestDigest;
    var chain = await db.FinancialPackageReviewDecisions.AsNoTracking().Where(x =>
        x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id && x.PackageHash == package.CalculationHash &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
        x.PresentationRevisionId == selectedPresentationId &&
        x.PresentationCompositionId == selectedCompositionId &&
        x.CompositionManifestDigest == selectedCompositionDigest)
      .ToListAsync(ct);
    var latestDecision = InRecordedOrder(chain).LastOrDefault();
    // A caller with an original intent decides on the chain it saw. A decision recorded since then makes the request stale.
    if (request.RequestId is Guid && latestDecision?.Id != request.ExpectedLatestDecisionId)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision,
        "A package decision was recorded after this review was loaded. Reload the package before deciding.");

    if (stage is FinancialPackageReviewStages.AccountingReview or FinancialPackageReviewStages.PartnerApproval)
    {
      var predecessorStage = stage == FinancialPackageReviewStages.AccountingReview
        ? FinancialPackageReviewStages.ManagementApproval
        : FinancialPackageReviewStages.AccountingReview;
      if (EffectiveApprovals(chain)[predecessorStage] is null)
        return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, candidate is null
          ? $"A current approved {predecessorStage} decision is required before {stage}."
          : $"A current approved {predecessorStage} decision for this signing candidate is required before {stage}.");
    }

    if (IsCurrentDuplicate(chain, stage, decision))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This package decision already exists for the current package version.");

    var review = new FinancialPackageReviewDecision
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
      EngagementId = package.EngagementId, FinancialPackageId = package.Id,
      PackageRevision = package.Revision, PackageGeneration = package.Generation,
      PackageHash = package.CalculationHash, Stage = stage, Decision = decision,
      PresentationRevisionId = candidate?.Id,
      PresentationCompositionId = composition?.Id,
      CompositionManifestDigest = composition?.ManifestDigest,
      FinancialPackageArtifactId = artifact.Id, ArtifactVersion = artifact.ArtifactVersion,
      ArtifactSha256Hex = artifact.ArtifactSha256Hex,
      EvidenceMode = evidenceMode, EvidenceReference = evidenceReference, Comment = comment,
      DecidedByUserId = evidenceMode == FinancialPackageReviewEvidenceModes.SignedIn ? actor.UserId : null,
      DecidedAt = DateTimeOffset.UtcNow,
      RequestId = request.RequestId, RequestHash = intentHash, PreviousDecisionId = latestDecision?.Id
    };
    db.FinancialPackageReviewDecisions.Add(review);
    try
    {
      await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("ux_financial_package_review_request", StringComparison.Ordinal) == true)
    {
      // Only the request-identity uniqueness maps to a conflict. Any other database failure is a fault and propagates.
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This package decision was recorded concurrently. Reload the package.");
    }
    if (tx is not null) await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(review.Id);
  }

  public static async Task<CommandResult<IReadOnlyList<FinancialPackageReviewSummary>>> GetAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<IReadOnlyList<FinancialPackageReviewSummary>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, AccountingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<FinancialPackageReviewSummary>>.Fail(auth.ErrorCode!, auth.Message!);

    var reviews = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id)
      .OrderBy(x => x.Sequence)
      .Select(x => new FinancialPackageReviewSummary(x.Id, x.Stage, x.Decision, x.EvidenceMode,
        x.PackageHash, x.FinancialPackageArtifactId, x.ArtifactVersion, x.ArtifactSha256Hex,
        x.EvidenceReference, x.Comment, x.DecidedByUserId, x.DecidedAt, x.PresentationRevisionId,
        x.PresentationCompositionId, x.CompositionManifestDigest))
      .ToListAsync(ct);
    return CommandResult<IReadOnlyList<FinancialPackageReviewSummary>>.Ok(reviews);
  }

  /// <summary>
  /// Returns only the client-safe presentation of a validated package. Internal
  /// review decisions, source rows, mappings and workpaper evidence never cross
  /// this boundary. The caller must hold an explicit ClientUser grant for the
  /// package's client and engagement.
  /// </summary>
  public static async Task<CommandResult<ClientFinancialPackageView>> GetClientViewAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId,
    CancellationToken ct = default)
  {
    if (financialPackageId == Guid.Empty)
      return CommandResult<ClientFinancialPackageView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");

    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<ClientFinancialPackageView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult<ClientFinancialPackageView>.Fail(ErrorCodes.GateBlocked,
        "The financial package is not available for management approval.");

    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId,
        ["ClientUser"], InternalOnly: false), ct);
    if (!auth.Succeeded)
      return CommandResult<ClientFinancialPackageView>.Fail(auth.ErrorCode!, auth.Message!);

    var readiness = await FinancialPackageValidationReadiness.GetAsync(db, package.FirmId, package.Id, ct);
    if (!readiness.Ready)
      return CommandResult<ClientFinancialPackageView>.Fail(ErrorCodes.GateBlocked,
        "The financial package is not available for management approval.");
    if (!await CashFlowSourcesIndependentlyApprovedAsync(db, actor, package, ct))
      return CommandResult<ClientFinancialPackageView>.Fail(ErrorCodes.GateBlocked,
        "The financial package is not available for management approval.");

    var statementLines = await db.FinancialPackageLines.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id)
      .ToListAsync(ct);
    var statementTotals = statementLines
      .GroupBy(x => x.StatementSection, StringComparer.Ordinal)
      .Select(x => new ClientFinancialPackageLineSummary(x.Key, MoneyPolicy.Normalize(x.Sum(y => y.Amount))))
      .OrderBy(x => x.StatementSection, StringComparer.Ordinal)
      .ToList();
    var hasCashFlow = await db.FinancialPackageCashFlowLines.AsNoTracking()
      .AnyAsync(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id, ct);
    var hasDisclosures = await db.FinancialPackageDisclosures.AsNoTracking()
      .AnyAsync(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id, ct);

    // When a sealed candidate composition exists, the management decision is about that exact composition. A package-only decision
    // recorded before it remains historical package evidence and is not shown as approval of the composition.
    var composition = await CurrentClientCompositionAsync(db, package, ct);
    Guid? compositionRevisionId = composition?.PresentationRevisionId;
    Guid? compositionId = composition?.Id;
    var compositionDigest = composition?.ManifestDigest;
    var chain = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id &&
        x.PackageHash == package.CalculationHash && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation && x.PresentationRevisionId == compositionRevisionId &&
        x.PresentationCompositionId == compositionId && x.CompositionManifestDigest == compositionDigest)
      .ToListAsync(ct);
    var ordered = InRecordedOrder(chain);
    var management = ordered.LastOrDefault(x => x.Stage == FinancialPackageReviewStages.ManagementApproval);

    return CommandResult<ClientFinancialPackageView>.Ok(new ClientFinancialPackageView(
      package.Id, package.Framework, package.PeriodStart, package.PeriodEnd, package.Currency,
      package.TemplateVersion, package.CalculationHash, hasCashFlow, hasDisclosures,
      management?.Decision ?? "PENDING", management?.DecidedAt, statementTotals,
      composition is null ? null : new ClientFinancialPackageComposition(composition.PresentationRevisionId, composition.Id,
        composition.ManifestDigest, composition.PdfSha256Hex, composition.WordSha256Hex, composition.ExcelSha256Hex, composition.SelectedAt),
      ordered.LastOrDefault()?.Id));
  }

  /// <summary>
  /// Lists only current, validated packages that still need an internal review
  /// stage. Each package is re-authorized at its client/engagement scope before
  /// entering the queue; a firm-wide count is never used as an access shortcut.
  /// </summary>
  public static async Task<CommandResult<IReadOnlyList<FinancialPackageReviewQueueItem>>> GetStaffQueueAsync(
    IClientAccountingDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var firmAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: AccountingRoles, InternalOnly: true), ct);
    if (!firmAuth.Succeeded)
      return CommandResult<IReadOnlyList<FinancialPackageReviewQueueItem>>.Fail(firmAuth.ErrorCode!, firmAuth.Message!);

    var userGrants = db.RoleGrants.AsNoTracking().Where(g =>
      g.FirmId == actor.FirmId && g.UserId == actor.UserId && g.RevokedAt == null && AccountingRoles.Contains(g.Role));
    var packages = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Status == AccountingPackageStates.PackageValidated &&
        !db.FinancialPackageValidations.Any(v => v.FinancialPackageId == x.Id && !v.Passed &&
          BlockingValidationCodes.Contains(v.Code)) &&
        userGrants.Any(g =>
          (g.ClientId == null && g.EngagementId == null) ||
          (g.ClientId == x.ClientId && g.EngagementId == null) ||
          (g.ClientId == x.ClientId && g.EngagementId == x.EngagementId)))
      .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
    if (packages.Count == 0)
      return CommandResult<IReadOnlyList<FinancialPackageReviewQueueItem>>.Ok([]);

    var packageIds = packages.Select(x => x.Id).ToArray();
    var decisions = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId))
      .ToListAsync(ct);
    var validations = await db.FinancialPackageValidations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId) &&
        FinancialPackageValidationReadiness.RequiredCodes.Contains(x.Code)).ToListAsync(ct);
    var queue = new List<FinancialPackageReviewQueueItem>();

    foreach (var package in packages)
    {
      var packageAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId,
          AccountingRoles, InternalOnly: true), ct);
      if (!packageAuth.Succeeded)
        continue;

      if (!FinancialPackageValidationReadiness.Evaluate(package.FirmId, package.Id, validations).Ready) continue;
      if (!await CashFlowSourcesIndependentlyApprovedAsync(db, actor, package, ct)) continue;

      // Status follows the chain the latest management decision names. An approval that a later return at an earlier stage defeated
      // shows as pending, because it must be recorded again.
      var version = decisions.Where(x => x.FinancialPackageId == package.Id &&
          x.PackageHash == package.CalculationHash && x.PackageRevision == package.Revision &&
          x.PackageGeneration == package.Generation).ToList();
      var latestManagement = InRecordedOrder(version.Where(x => x.Stage == FinancialPackageReviewStages.ManagementApproval)).LastOrDefault();
      var chain = latestManagement is null ? version : version.Where(x =>
        x.PresentationRevisionId == latestManagement.PresentationRevisionId &&
        x.PresentationCompositionId == latestManagement.PresentationCompositionId &&
        x.CompositionManifestDigest == latestManagement.CompositionManifestDigest).ToList();
      var effective = EffectiveApprovals(chain);
      string StageStatus(string stage) => effective[stage] is not null
        ? FinancialPackageReviewDecisions.Approved
        : InRecordedOrder(chain.Where(x => x.Stage == stage)).LastOrDefault()?.Decision is { } latest &&
          latest != FinancialPackageReviewDecisions.Approved ? latest : "PENDING";
      var management = StageStatus(FinancialPackageReviewStages.ManagementApproval);
      var accounting = StageStatus(FinancialPackageReviewStages.AccountingReview);
      var partner = StageStatus(FinancialPackageReviewStages.PartnerApproval);
      var next = management != FinancialPackageReviewDecisions.Approved
        ? FinancialPackageReviewStages.ManagementApproval
        : accounting != FinancialPackageReviewDecisions.Approved
          ? FinancialPackageReviewStages.AccountingReview
          : partner != FinancialPackageReviewDecisions.Approved
            ? FinancialPackageReviewStages.PartnerApproval
            : "COMPLETE";
      if (next == FinancialPackageReviewStages.PartnerApproval &&
          !await IsCurrentAssignedEngagementPartnerAsync(db, package, actor.UserId, ct))
        next = "PARTNER_APPROVAL_REQUIRED";
      if (next != "COMPLETE")
        queue.Add(new FinancialPackageReviewQueueItem(package.Id, package.Framework, package.PeriodStart,
          package.PeriodEnd, package.Currency, package.CalculationHash, management, accounting, partner, next));
    }

    return CommandResult<IReadOnlyList<FinancialPackageReviewQueueItem>>.Ok(queue);
  }

  public static async Task<CommandResult> RequireCurrentAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid financialPackageId, bool requirePartner,
    CancellationToken ct = default)
  {
    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == financialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, AccountingRoles, InternalOnly: true), ct);
    if (!auth.Succeeded)
      return auth;
    var comparativeAccess = await FinancialStatementService.AuthorizeComparativeAccessAsync(db, actor, package, ct);
    if (!comparativeAccess.Succeeded)
      return comparativeAccess;
    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The financial package is not currently reviewable.");
    var readiness = await FinancialPackageValidationReadiness.GetAsync(db, package.FirmId, package.Id, ct);
    if (!readiness.Ready)
      return CommandResult.Fail(ErrorCodes.GateBlocked, readiness.Message);
    if (!await CashFlowSourcesIndependentlyApprovedAsync(db, actor, package, ct))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Current and comparative cash-flow source links need independent approval against the accepted general ledger.");

    var requiredStages = requirePartner
      ? new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewStages.PartnerApproval }
      : new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview };
    var reviews = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id)
      .ToListAsync(ct);
    // The composition the chain is about is the one the latest management decision names. Every required stage must then hold the
    // latest-effective approval on that exact chain: a later return defeats an earlier approval, and an approval recorded before the
    // predecessor's latest approval does not count.
    Guid? expectedPresentationRevisionId = null;
    Guid? expectedCompositionId = null;
    string? expectedCompositionDigest = null;
    IReadOnlyDictionary<string, FinancialPackageReviewDecision?>? effective = null;
    foreach (var stage in requiredStages)
    {
      var current = InRecordedOrder(reviews.Where(x => x.Stage == stage)).LastOrDefault();
      if (current is null || current.Decision != FinancialPackageReviewDecisions.Approved ||
          current.PackageHash != package.CalculationHash || current.PackageRevision != package.Revision ||
          current.PackageGeneration != package.Generation)
        return CommandResult.Fail(ErrorCodes.GateBlocked, $"The current {stage} decision is required before release.");

      if (effective is null)
      {
        expectedPresentationRevisionId = current.PresentationRevisionId;
        expectedCompositionId = current.PresentationCompositionId;
        expectedCompositionDigest = current.CompositionManifestDigest;
        effective = EffectiveApprovals(reviews.Where(x =>
          x.PackageHash == package.CalculationHash && x.PackageRevision == package.Revision &&
          x.PackageGeneration == package.Generation && x.PresentationRevisionId == expectedPresentationRevisionId &&
          x.PresentationCompositionId == expectedCompositionId && x.CompositionManifestDigest == expectedCompositionDigest));
      }
      else if (current.PresentationRevisionId != expectedPresentationRevisionId ||
          current.PresentationCompositionId != expectedCompositionId ||
          current.CompositionManifestDigest != expectedCompositionDigest)
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          "The management, accounting and Partner decisions must approve the same exact candidate composition.");

      if (effective[stage]?.Id != current.Id)
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          $"The current {stage} decision predates a later decision at an earlier stage and must be recorded again before release.");
    }
    // A Partner decision remains current only while its recorder remains the assigned Engagement Partner with the exact staffing grant.
    if (requirePartner)
    {
      var partner = effective![FinancialPackageReviewStages.PartnerApproval]!;
      if (!await PartnerDecisionHeldByPartnerAsync(db, package, partner.DecidedByUserId, ct))
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          "The current PartnerApproval decision was not recorded by the current assigned Engagement Partner for this engagement.");
    }
    if (requirePartner && (expectedPresentationRevisionId is null || expectedCompositionId is null ||
        string.IsNullOrWhiteSpace(expectedCompositionDigest)))
      return CommandResult.Fail(ErrorCodes.GateBlocked,
        "Release requires management, accounting and Partner decisions bound to one selected three-format composition.");
    if (expectedPresentationRevisionId is Guid presentationId)
    {
      var candidate = await db.FinancialStatementPresentationRevisions.AsNoTracking().SingleOrDefaultAsync(x =>
        x.Id == presentationId && x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id, ct);
      var composition = expectedCompositionId is Guid compositionId
        ? await db.FinancialStatementCompositions.AsNoTracking().SingleOrDefaultAsync(x =>
          x.Id == compositionId && x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
          x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id &&
          x.PresentationRevisionId == presentationId && x.ManifestDigest == expectedCompositionDigest, ct)
        : null;
      if (candidate is null || composition is null || candidate.PackageCalculationHash != package.CalculationHash ||
          candidate.PackageGeneration != package.Generation ||
          candidate.Status is not (FinancialStatementPresentationRevisionStatuses.Candidate or FinancialStatementPresentationRevisionStatuses.Approved) ||
          !await FinancialStatementService.PresentationSourceIsCurrentAsync(db, candidate, ct) ||
          !FinancialStatementService.PresentationChecksAllPass(candidate) ||
          !await CompositionArtifactsAreIntactAsync(db, package, candidate, composition, ct))
        return CommandResult.Fail(ErrorCodes.GateBlocked,
          "The approved statement composition is missing, stale or fails its exact-artifact integrity check.");
    }
    return CommandResult.Ok();
  }

  internal static async Task<bool> CompositionArtifactsAreIntactAsync(
    IClientAccountingDbContext db, FinancialPackage package, FinancialStatementPresentationRevision candidate,
    FinancialStatementComposition composition, CancellationToken ct)
  {
    if (composition.FirmId != package.FirmId || composition.ClientId != package.ClientId ||
        composition.EngagementId != package.EngagementId || composition.FinancialPackageId != package.Id ||
        composition.PresentationRevisionId != candidate.Id || composition.PackageCalculationHash != package.CalculationHash)
      return false;
    var expected = new[]
    {
      (FinancialStatementPresentationRenderer.PdfFormat, composition.PdfArtifactId, composition.PdfSha256Hex),
      (FinancialStatementPresentationRenderer.WordFormat, composition.WordArtifactId, composition.WordSha256Hex),
      (FinancialStatementPresentationRenderer.ExcelFormat, composition.ExcelArtifactId, composition.ExcelSha256Hex),
    };
    var manifest = JsonSerializer.SerializeToUtf8Bytes(new
    {
      Version = "statement-composition.v1",
      PresentationRevisionId = candidate.Id,
      PackageCalculationHash = candidate.PackageCalculationHash,
      Formats = expected.Select(item => new { Format = item.Item1, ArtifactId = item.Item2, ArtifactSha256Hex = item.Item3 }).ToArray(),
    });
    if (Hashing.Sha256Hex(manifest) != composition.ManifestDigest) return false;
    var ids = expected.Select(x => x.Item2).ToArray();
    var artifacts = await db.FinancialPackageArtifacts.AsNoTracking().Where(x =>
      x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId && ids.Contains(x.Id)).ToListAsync(ct);
    if (artifacts.Count != expected.Length) return false;
    return expected.All(item =>
    {
      var artifact = artifacts.SingleOrDefault(x => x.Id == item.Item2);
      return artifact is not null && artifact.FinancialPackageId == package.Id &&
        artifact.PackageRevision == package.Revision && artifact.PackageGeneration == package.Generation &&
        artifact.PackageHash == package.CalculationHash &&
        artifact.ArtifactVersion == FinancialPackageArtifactVersions.StatementPresentation(candidate.Id, item.Item1) &&
        artifact.ArtifactSha256Hex == item.Item3 && artifact.ArtifactBytes.Length > 0 &&
        Hashing.Sha256Hex(artifact.ArtifactBytes) == item.Item3;
    });
  }

  private static async Task<bool> PartnerDecisionHeldByPartnerAsync(IClientAccountingDbContext db, FinancialPackage package,
    Guid? decidedByUserId, CancellationToken ct)
  {
    if (decidedByUserId is not Guid decider) return false;
    return await IsCurrentAssignedEngagementPartnerAsync(db, package, decider, ct);
  }

  internal static async Task<bool> IsCurrentAssignedEngagementPartnerAsync(IClientAccountingDbContext db,
    FinancialPackage package, Guid userId, CancellationToken ct)
  {
    var now = DateTimeOffset.UtcNow;
    return await (from assignment in db.EngagementStaffAssignments.AsNoTracking()
      join grant in db.RoleGrants.AsNoTracking() on new { assignment.FirmId, Id = assignment.RoleGrantId }
        equals new { grant.FirmId, grant.Id }
      where assignment.FirmId == package.FirmId && assignment.ClientId == package.ClientId &&
        assignment.EngagementId == package.EngagementId && assignment.UserId == userId &&
        assignment.StaffingLevel == StaffingLevels.EngagementPartner && assignment.RevokedAt == null &&
        grant.UserId == userId && grant.Role == "Partner" && grant.ClientId == package.ClientId &&
        grant.EngagementId == package.EngagementId && grant.RevokedAt == null && (grant.ExpiresAt == null || grant.ExpiresAt > now)
      select assignment.Id).AnyAsync(ct);
  }

  private static bool IsSha256(string value) => value.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

  private static async Task<bool> CashFlowSourcesIndependentlyApprovedAsync(
    IClientAccountingDbContext db, ActorContext actor, FinancialPackage package, CancellationToken ct)
  {
    var packages = new List<FinancialPackage> { package };
    if (package.ComparativePackageId is Guid comparativeId)
    {
      var comparative = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x =>
        x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.Id == comparativeId, ct);
      if (comparative is null) return false;
      packages.Add(comparative);
    }
    foreach (var candidate in packages)
    {
      var hasMovements = await db.FinancialPackageCashFlowLines.AsNoTracking().AnyAsync(x =>
        x.FirmId == candidate.FirmId && x.ClientId == candidate.ClientId &&
        x.EngagementId == candidate.EngagementId && x.FinancialPackageId == candidate.Id, ct);
      if (hasMovements && !await FinancialStatementService.CashFlowSourceApprovedAsync(db, candidate, ct))
        return false;
    }
    return true;
  }
}
