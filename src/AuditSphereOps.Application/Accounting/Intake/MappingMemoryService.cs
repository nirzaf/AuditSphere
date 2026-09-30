using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static class MappingMemoryStatuses
{
  /// <summary>Same account code and name as the prior approved mapping; its allocation is proposed unchanged.</summary>
  public const string Reused = "REUSED";
  /// <summary>Known account code whose name changed; the prior allocation is proposed and flagged for review.</summary>
  public const string NameChanged = "NAME_CHANGED";
  /// <summary>No prior approved mapping for this account; the preparer must map it.</summary>
  public const string NewAccount = "NEW_ACCOUNT";
}

public sealed record MappingProposal(string AccountCode, string AccountName, string Status, string? PriorAccountName,
  IReadOnlyList<MappingAllocationInput> Allocations);

public sealed record MappingMemoryView(Guid DatasetId, Guid? SourceMappingVersionId, long? SourceMappingVersion, IReadOnlyList<MappingProposal> Proposals);

/// <summary>
/// Historical auto-mapping memory. For a newly loaded trial balance it proposes the allocations of the client's most
/// recently approved mapping for each known account, flags renamed and new accounts, and creates only a draft mapping
/// version; the existing maker/checker approval stays the control that makes it current.
/// </summary>
public static class MappingMemoryService
{
  public static async Task<CommandResult<MappingMemoryView>> ProposeAsync(IAuditSphereDbContext db, ActorContext actor, Guid datasetId, CancellationToken ct = default)
  {
    var dataset = await db.TrialBalanceDatasets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == datasetId && x.FirmId == actor.FirmId, ct);
    if (dataset is null) return CommandResult<MappingMemoryView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, dataset.ClientId, dataset.EngagementId,
      ["AccountingPreparer", "AccountingReviewer", "Manager", "Partner", "Administrator"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<MappingMemoryView>.Fail(auth.ErrorCode!, auth.Message!);
    var accounts = await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == datasetId)
      .GroupBy(x => x.AccountCode).Select(g => new { Code = g.Key, Name = g.Min(x => x.AccountName) ?? "" }).ToListAsync(ct);
    var prior = await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ClientId == dataset.ClientId &&
        x.DatasetId != datasetId && x.Status == AccountingPackageStates.MappingApproved)
      .OrderByDescending(x => x.ApprovedAt).ThenByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    var allocations = prior is null ? [] : await db.MappingAllocations.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.MappingVersionId == prior.Id).ToListAsync(ct);
    var priorNames = prior is null ? new Dictionary<string, string>() : await db.TrialBalanceRows.AsNoTracking().Where(x => x.DatasetId == prior.DatasetId)
      .GroupBy(x => x.AccountCode).Select(g => new { g.Key, Name = g.Min(x => x.AccountName) ?? "" }).ToDictionaryAsync(x => x.Key, x => x.Name, ct);
    var proposals = accounts.OrderBy(x => x.Code, StringComparer.Ordinal).Select(a =>
    {
      var mine = allocations.Where(x => x.SourceAccountCode == a.Code).OrderBy(x => x.DestinationCode, StringComparer.Ordinal)
        .Select(x => new MappingAllocationInput(x.SourceAccountCode, x.DestinationCode, x.StatementSection, x.Fraction,
          $"Reused from approved mapping v{prior!.Version}", x.AuditArea, x.ResidualPolicy)).ToList();
      var priorName = priorNames.GetValueOrDefault(a.Code);
      var status = mine.Count == 0 ? MappingMemoryStatuses.NewAccount
        : string.Equals(priorName?.Trim(), a.Name.Trim(), StringComparison.OrdinalIgnoreCase) ? MappingMemoryStatuses.Reused : MappingMemoryStatuses.NameChanged;
      return new MappingProposal(a.Code, a.Name, status, priorName, mine);
    }).ToList();
    return CommandResult<MappingMemoryView>.Ok(new(datasetId, prior?.Id, prior?.Version, proposals));
  }

  /// <summary>
  /// Creates a draft mapping version from the proposals plus the preparer's allocations for new accounts (which may
  /// also override a proposal). Every account must be covered; approval remains a separate reviewer action.
  /// </summary>
  public static async Task<CommandResult<Guid>> CreateDraftAsync(IAuditSphereDbContext db, ActorContext actor, Guid datasetId,
    string taxonomyVersion, string periodStart, string periodEnd, IReadOnlyList<MappingAllocationInput> overrides, CancellationToken ct = default)
  {
    var view = await ProposeAsync(db, actor, datasetId, ct);
    if (!view.Succeeded) return CommandResult<Guid>.Fail(view.ErrorCode!, view.Message!);
    var overridden = overrides.Select(x => x.SourceAccountCode).ToHashSet(StringComparer.Ordinal);
    var missing = view.Value!.Proposals.Where(p => p.Allocations.Count == 0 && !overridden.Contains(p.AccountCode)).Select(p => p.AccountCode).ToList();
    if (missing.Count > 0)
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingIncomplete, $"Map the new accounts first: {string.Join(", ", missing)}.");
    var allocations = view.Value.Proposals.Where(p => !overridden.Contains(p.AccountCode)).SelectMany(p => p.Allocations).Concat(overrides).ToList();
    return await FinancialStatementService.CreateMappingVersionAsync(db, actor,
      new CreateMappingVersionRequest(datasetId, taxonomyVersion, periodStart, periodEnd, allocations), ct);
  }
}
