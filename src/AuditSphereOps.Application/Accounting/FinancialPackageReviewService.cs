using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record FinancialPackageReviewRequest(
  Guid FinancialPackageId, string Stage, string Decision, string EvidenceMode,
  string EvidenceReference, string Comment);

public sealed record FinancialPackageReviewSummary(
  Guid Id, string Stage, string Decision, string EvidenceMode, string PackageHash,
  string EvidenceReference, string Comment, Guid? DecidedByUserId, DateTimeOffset DecidedAt);

public sealed record ClientFinancialPackageLineSummary(string StatementSection, decimal Amount);

public sealed record ClientFinancialPackageView(
  Guid PackageId, string Framework, string PeriodStart, string PeriodEnd, string Currency,
  string TemplateVersion, string PackageHash, bool HasCashFlow, bool HasDisclosures,
  string ManagementDecision, DateTimeOffset? ManagementDecidedAt,
  IReadOnlyList<ClientFinancialPackageLineSummary> StatementTotals);

public sealed record FinancialPackageReviewQueueItem(
  Guid PackageId, string Framework, string PeriodStart, string PeriodEnd, string Currency,
  string PackageHash, string ManagementDecision, string AccountingDecision,
  string PartnerDecision, string NextAction);

public static class FinancialPackageReviewService
{
  private static readonly string[] AccountingRoles = ["AccountingReviewer", "Manager", "Partner", "Administrator"];
  private static readonly string[] PartnerRoles = ["Partner", "Administrator"];
  private static readonly string[] OfflineManagementRoles = ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"];

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
    if (evidenceMode == FinancialPackageReviewEvidenceModes.Offline &&
        stage != FinancialPackageReviewStages.ManagementApproval)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.PackageInvalid, "Offline evidence is only supported for management approval.");

    var package = await db.FinancialPackages.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == request.FinancialPackageId && x.FirmId == actor.FirmId, ct);
    if (package is null)
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "The financial package must be validated before review.");

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
      new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId, roles, InternalOnly: internalOnly), ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);

    if (await db.FinancialPackageReviewDecisions.AnyAsync(x =>
        x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id && x.PackageHash == package.CalculationHash &&
        x.PackageRevision == package.Revision && x.PackageGeneration == package.Generation &&
        x.Stage == stage && x.Decision == decision, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "This package decision already exists for the current package version.");

    var review = new FinancialPackageReviewDecision
    {
      Id = Guid.CreateVersion7(), FirmId = package.FirmId, ClientId = package.ClientId,
      EngagementId = package.EngagementId, FinancialPackageId = package.Id,
      PackageRevision = package.Revision, PackageGeneration = package.Generation,
      PackageHash = package.CalculationHash, Stage = stage, Decision = decision,
      EvidenceMode = evidenceMode, EvidenceReference = evidenceReference, Comment = comment,
      DecidedByUserId = evidenceMode == FinancialPackageReviewEvidenceModes.SignedIn ? actor.UserId : null,
      DecidedAt = DateTimeOffset.UtcNow
    };
    db.FinancialPackageReviewDecisions.Add(review);
    await db.SaveChangesAsync(ct);
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
      .OrderBy(x => x.DecidedAt)
      .Select(x => new FinancialPackageReviewSummary(x.Id, x.Stage, x.Decision, x.EvidenceMode,
        x.PackageHash, x.EvidenceReference, x.Comment, x.DecidedByUserId, x.DecidedAt))
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

    var management = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId &&
        x.EngagementId == package.EngagementId && x.FinancialPackageId == package.Id &&
        x.Stage == FinancialPackageReviewStages.ManagementApproval &&
        x.PackageHash == package.CalculationHash && x.PackageRevision == package.Revision &&
        x.PackageGeneration == package.Generation)
      .OrderByDescending(x => x.DecidedAt)
      .Select(x => new { x.Decision, x.DecidedAt })
      .FirstOrDefaultAsync(ct);

    return CommandResult<ClientFinancialPackageView>.Ok(new ClientFinancialPackageView(
      package.Id, package.Framework, package.PeriodStart, package.PeriodEnd, package.Currency,
      package.TemplateVersion, package.CalculationHash, hasCashFlow, hasDisclosures,
      management?.Decision ?? "PENDING", management?.DecidedAt, statementTotals));
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

    var packages = await db.FinancialPackages.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Status == AccountingPackageStates.PackageValidated)
      .OrderByDescending(x => x.CreatedAt).Take(100).ToListAsync(ct);
    if (packages.Count == 0)
      return CommandResult<IReadOnlyList<FinancialPackageReviewQueueItem>>.Ok([]);

    var packageIds = packages.Select(x => x.Id).ToArray();
    var decisions = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && packageIds.Contains(x.FinancialPackageId))
      .ToListAsync(ct);
    var canPartnerReview = actor.Roles.Any(x => x is "Partner" or "Administrator");
    var queue = new List<FinancialPackageReviewQueueItem>();

    foreach (var package in packages)
    {
      var packageAuth = await AuthorizationDecision.AuthorizeAsync(db, actor,
        new AuthorizationRequest(package.FirmId, package.ClientId, package.EngagementId,
          AccountingRoles, InternalOnly: true), ct);
      if (!packageAuth.Succeeded)
        continue;

      var current = decisions.Where(x => x.FinancialPackageId == package.Id &&
          x.PackageHash == package.CalculationHash && x.PackageRevision == package.Revision &&
          x.PackageGeneration == package.Generation)
        .GroupBy(x => x.Stage, StringComparer.Ordinal)
        .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.DecidedAt).First().Decision,
          StringComparer.Ordinal);
      var management = current.GetValueOrDefault(FinancialPackageReviewStages.ManagementApproval, "PENDING");
      var accounting = current.GetValueOrDefault(FinancialPackageReviewStages.AccountingReview, "PENDING");
      var partner = current.GetValueOrDefault(FinancialPackageReviewStages.PartnerApproval, "PENDING");
      var next = management != FinancialPackageReviewDecisions.Approved
        ? FinancialPackageReviewStages.ManagementApproval
        : accounting != FinancialPackageReviewDecisions.Approved
          ? FinancialPackageReviewStages.AccountingReview
          : partner != FinancialPackageReviewDecisions.Approved
            ? FinancialPackageReviewStages.PartnerApproval
            : "COMPLETE";
      if (next == FinancialPackageReviewStages.PartnerApproval && !canPartnerReview)
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
    if (package.Status != AccountingPackageStates.PackageValidated)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "The financial package is not currently reviewable.");

    var requiredStages = requirePartner
      ? new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewStages.PartnerApproval }
      : new[] { FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview };
    var reviews = await db.FinancialPackageReviewDecisions.AsNoTracking()
      .Where(x => x.FirmId == package.FirmId && x.ClientId == package.ClientId && x.EngagementId == package.EngagementId &&
        x.FinancialPackageId == package.Id)
      .ToListAsync(ct);
    foreach (var stage in requiredStages)
    {
      var current = reviews.Where(x => x.Stage == stage).OrderByDescending(x => x.DecidedAt).FirstOrDefault();
      if (current is null || current.Decision != FinancialPackageReviewDecisions.Approved ||
          current.PackageHash != package.CalculationHash || current.PackageRevision != package.Revision ||
          current.PackageGeneration != package.Generation)
        return CommandResult.Fail(ErrorCodes.GateBlocked, $"The current {stage} decision is required before release.");
    }
    return CommandResult.Ok();
  }
}
