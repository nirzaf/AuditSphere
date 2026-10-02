using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record PackageValidationView(string Code, bool Passed, string Detail);
public sealed record PackageSectionTotal(string Section, decimal Amount);
public sealed record PackageCashFlowView(string Section, string Description, decimal Amount);
public sealed record PackageDisclosureView(string Code, string Response, bool NotApplicable, string? Rationale);
public sealed record PackageReviewView(string Stage, string Decision, string EvidenceMode, string EvidenceReference, string PackageHash, DateTimeOffset DecidedAt);
public sealed record PackageArtifactView(string ArtifactSha256Hex, int ByteCount, string RenderedText);
public sealed record FinancialPackageWorkspace(Guid Id, Guid EngagementId, string Status, string Framework, string PeriodStart, string PeriodEnd, string Currency,
  string TemplateVersion, string CalculationHash, string? SupplementaryHash, long Revision, decimal? CashBeginning, decimal? CashEnding,
  IReadOnlyList<PackageValidationView> Validations, IReadOnlyList<PackageSectionTotal> StatementTotals, IReadOnlyList<PackageCashFlowView> CashFlow,
  IReadOnlyList<PackageDisclosureView> Disclosures, IReadOnlyList<PackageReviewView> Reviews, PackageArtifactView? Artifact, bool CanReview,
  IReadOnlyList<string> ReviewStages, IReadOnlyList<string> ReviewDecisions);

/// <summary>
/// Staff view of one financial statement package: deterministic calculation outputs, validations, append-only review
/// decisions bound to the exact revision/generation/hash, and the persisted text artifact. Out-of-scope packages look missing.
/// </summary>
public static class FinancialPackageWorkspaceQuery
{
  private static readonly string[] Roles = ["Administrator", "Partner", "Manager", "Reviewer", "Staff", "AccountingPreparer", "AccountingReviewer"];

  public static async Task<CommandResult<FinancialPackageWorkspace>> GetAsync(IClientAccountingDbContext db, ActorContext actor, Guid packageId, CancellationToken ct = default)
  {
    var p = await db.FinancialPackages.AsNoTracking().SingleOrDefaultAsync(x => x.Id == packageId && x.FirmId == actor.FirmId, ct);
    if (p is null) return CommandResult<FinancialPackageWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(p.FirmId, p.ClientId, p.EngagementId, Roles, InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<FinancialPackageWorkspace>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var validations = await db.FinancialPackageValidations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.FinancialPackageId == p.Id).OrderBy(x => x.Code)
      .Select(x => new PackageValidationView(x.Code, x.Passed, x.Detail)).ToListAsync(ct);
    var lines = await db.FinancialPackageLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == p.ClientId && x.EngagementId == p.EngagementId &&
      x.FinancialPackageId == p.Id).Select(x => new { x.StatementSection, x.Amount }).ToListAsync(ct);
    var totals = lines.GroupBy(x => x.StatementSection, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal)
      .Select(x => new PackageSectionTotal(x.Key, MoneyPolicy.Normalize(x.Sum(y => y.Amount)))).ToList();
    var cash = await db.FinancialPackageCashFlowLines.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == p.ClientId && x.EngagementId == p.EngagementId &&
      x.FinancialPackageId == p.Id).OrderBy(x => x.Section).ThenBy(x => x.Description).Select(x => new PackageCashFlowView(x.Section, x.Description, x.Amount)).ToListAsync(ct);
    var disclosures = await db.FinancialPackageDisclosures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == p.ClientId && x.EngagementId == p.EngagementId &&
      x.FinancialPackageId == p.Id).OrderBy(x => x.Code).Select(x => new PackageDisclosureView(x.Code, x.Response, x.NotApplicable, x.Rationale)).ToListAsync(ct);
    var stored = await db.FinancialPackageArtifacts.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == p.FirmId && x.ClientId == p.ClientId &&
      x.EngagementId == p.EngagementId && x.FinancialPackageId == p.Id && x.PackageRevision == p.Revision && x.PackageGeneration == p.Generation &&
      x.PackageHash == p.CalculationHash && x.ArtifactVersion == FinancialPackageArtifactVersions.Text, ct);
    var reviews = await FinancialPackageReviewService.GetAsync(db, actor, p.Id, ct);
    var partner = actor.Roles.Any(x => x is "Partner" or "Administrator");
    return CommandResult<FinancialPackageWorkspace>.Ok(new(p.Id, p.EngagementId, p.Status, p.Framework, p.PeriodStart, p.PeriodEnd, p.Currency, p.TemplateVersion,
      p.CalculationHash, p.SupplementaryHash, p.Revision, p.CashBeginning, p.CashEnding, validations, totals, cash, disclosures,
      reviews.Succeeded ? reviews.Value!.Select(r => new PackageReviewView(r.Stage, r.Decision, r.EvidenceMode, r.EvidenceReference, r.PackageHash, r.DecidedAt)).ToList() : [],
      stored is { ArtifactBytes.Length: > 0 } ? new PackageArtifactView(stored.ArtifactSha256Hex, stored.ArtifactBytes.Length, System.Text.Encoding.UTF8.GetString(stored.ArtifactBytes)) : null,
      actor.Roles.Any(x => x is "Administrator" or "Partner" or "Manager" or "AccountingReviewer" or "AccountingPreparer"),
      partner ? [FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview, FinancialPackageReviewStages.PartnerApproval]
        : [FinancialPackageReviewStages.ManagementApproval, FinancialPackageReviewStages.AccountingReview],
      FinancialPackageReviewDecisions.All.Order(StringComparer.Ordinal).ToList()));
  }
}
