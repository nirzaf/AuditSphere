using System.Globalization;
using System.Text;
using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Accounting;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public static partial class ClientAccountingService
{
  public static async Task<CommandResult<Guid>> CreateTaxonomyVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, string code, string framework,
    string name, DateOnly effectiveFrom, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(framework) || string.IsNullOrWhiteSpace(name))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "Taxonomy code, framework and name are required.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (await db.ReportingTaxonomyVersions.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The taxonomy code already exists.");
    var taxonomy = new ReportingTaxonomyVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Code = code.Trim(), Framework = framework.Trim(),
      Name = name.Trim(), EffectiveFrom = effectiveFrom, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ReportingTaxonomyVersions.Add(taxonomy);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(taxonomy.Id);
  }

  public static async Task<CommandResult<Guid>> CreateTaxonomyOverlayAsync(
    IClientAccountingDbContext db, ActorContext actor, TaxonomyOverlayRequest request,
    CancellationToken ct = default)
  {
    var scope = request.OverlayScope.Trim().ToUpperInvariant();
    if (request.BaseTaxonomyVersionId == Guid.Empty || string.IsNullOrWhiteSpace(request.Code) ||
        string.IsNullOrWhiteSpace(request.Name) ||
        (!scope.StartsWith("INDUSTRY:", StringComparison.Ordinal) && !scope.StartsWith("GROUP:", StringComparison.Ordinal)))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid,
        "A taxonomy overlay needs a base version, generic industry/group scope, code and name.");
    var baseTaxonomy = await db.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == request.BaseTaxonomyVersionId && x.Status == AccountingWorkflowStates.Approved, ct);
    if (baseTaxonomy is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "An approved base taxonomy is required for an overlay.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (scope.StartsWith("GROUP:", StringComparison.Ordinal) &&
        (!Guid.TryParse(scope["GROUP:".Length..], out var groupId) ||
         !await db.ClientGroups.AsNoTracking().AnyAsync(x => x.FirmId == actor.FirmId && x.Id == groupId, ct)))
      return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "The taxonomy overlay group is outside the firm scope.");
    if (await db.ReportingTaxonomyVersions.AnyAsync(x => x.FirmId == actor.FirmId && x.Code == request.Code.Trim(), ct))
      return CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict, "The taxonomy code already exists.");
    var overlay = new ReportingTaxonomyVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, BaseTaxonomyVersionId = baseTaxonomy.Id,
      Code = request.Code.Trim(), Framework = baseTaxonomy.Framework, Name = request.Name.Trim(), OverlayScope = scope,
      EffectiveFrom = request.EffectiveFrom, CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ReportingTaxonomyVersions.Add(overlay);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(overlay.Id);
  }

  public static async Task<CommandResult> AddTaxonomyNodesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid taxonomyVersionId,
    IReadOnlyList<TaxonomyNodeInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name) ||
        string.IsNullOrWhiteSpace(x.StatementSection)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every taxonomy node needs a typed presentation location.");
    var taxonomy = await db.ReportingTaxonomyVersions.SingleOrDefaultAsync(x => x.Id == taxonomyVersionId && x.FirmId == actor.FirmId, ct);
    if (taxonomy is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (taxonomy.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Approved taxonomy versions are immutable.");
    var map = inputs.ToDictionary(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase);
    var existing = await db.ReportingTaxonomyNodes.Where(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id).ToListAsync(ct);
    var existingByCode = existing.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
    if (map.Count != inputs.Count || inputs.Any(x => x.ParentCode is not null && !map.ContainsKey(x.ParentCode.Trim()) &&
        !existingByCode.ContainsKey(x.ParentCode.Trim())))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Taxonomy parent codes must exist in the same taxonomy version.");
    if (HasTaxonomyCycle(inputs, map))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The taxonomy contains a parent cycle.");
    if (inputs.Any(x => existingByCode.ContainsKey(x.Code.Trim())))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "A taxonomy node code already exists.");
    var nodes = inputs.Select(x => new ReportingTaxonomyNode
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, TaxonomyVersionId = taxonomy.Id,
      Code = x.Code.Trim(), Name = x.Name.Trim(), StatementSection = x.StatementSection.Trim().ToUpperInvariant(),
      DisplaySign = x.DisplaySign.Trim(), NormalBalance = x.NormalBalance.Trim(), DisclosureArea = x.DisclosureArea.Trim(),
      IsPosting = x.IsPosting, Applicability = x.Applicability.Trim(), CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    var byCode = nodes.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
    foreach (var node in nodes)
      if (map[node.Code].ParentCode is { } parent)
        node.ParentNodeId = byCode.TryGetValue(parent.Trim(), out var newParent)
          ? newParent.Id : existingByCode[parent.Trim()].Id;
    db.ReportingTaxonomyNodes.AddRange(nodes);
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> PublishTaxonomyVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid taxonomyVersionId,
    CancellationToken ct = default)
  {
    var taxonomy = await db.ReportingTaxonomyVersions.SingleOrDefaultAsync(x => x.Id == taxonomyVersionId && x.FirmId == actor.FirmId, ct);
    if (taxonomy is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (taxonomy.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft taxonomy can be approved.");
    if (taxonomy.BaseTaxonomyVersionId is { } baseId && !await db.ReportingTaxonomyVersions.AsNoTracking().AnyAsync(x =>
        x.FirmId == actor.FirmId && x.Id == baseId && x.Status == AccountingWorkflowStates.Approved, ct))
      return CommandResult.Fail(ErrorCodes.GenerationStale, "The approved base taxonomy for this overlay is no longer current.");
    if (taxonomy.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The taxonomy preparer cannot approve the same version.");
    if (!await db.ReportingTaxonomyNodes.AnyAsync(x => x.FirmId == actor.FirmId && x.TaxonomyVersionId == taxonomy.Id, ct))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "A taxonomy version needs at least one node.");
    taxonomy.Status = AccountingWorkflowStates.Approved;
    taxonomy.ApprovedByUserId = actor.UserId;
    taxonomy.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<IReadOnlyList<TaxonomyImpactItem>>> GetTaxonomyPublishImpactAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid taxonomyVersionId,
    CancellationToken ct = default)
  {
    var taxonomy = await db.ReportingTaxonomyVersions.AsNoTracking().SingleOrDefaultAsync(x =>
      x.FirmId == actor.FirmId && x.Id == taxonomyVersionId, ct);
    if (taxonomy is null)
      return CommandResult<IReadOnlyList<TaxonomyImpactItem>>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeFirmAsync(db, actor, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<IReadOnlyList<TaxonomyImpactItem>>.Fail(auth.ErrorCode!, auth.Message!);
    var mappings = await db.MappingVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.TaxonomyVersion == taxonomy.Code).Select(x => new TaxonomyImpactItem(x.Id, "MAPPING", x.Status, x.TaxonomyVersion)).ToListAsync(ct);
    var packages = await db.FinancialPackages.AsNoTracking().Where(x => x.FirmId == actor.FirmId &&
      x.TaxonomyVersion == taxonomy.Code).Select(x => new TaxonomyImpactItem(x.Id, "PACKAGE", x.Status, x.TaxonomyVersion)).ToListAsync(ct);
    return CommandResult<IReadOnlyList<TaxonomyImpactItem>>.Ok(mappings.Concat(packages).OrderBy(x => x.Kind).ThenBy(x => x.Id).ToArray());
  }

  private static bool HasTaxonomyCycle(
    IReadOnlyList<TaxonomyNodeInput> inputs, IReadOnlyDictionary<string, TaxonomyNodeInput> map)
  {
    var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool Visit(string code)
    {
      if (state.GetValueOrDefault(code) == 1) return true;
      if (state.GetValueOrDefault(code) == 2) return false;
      state[code] = 1;
      if (map[code].ParentCode is { } parent && map.ContainsKey(parent.Trim()) && Visit(parent.Trim())) return true;
      state[code] = 2;
      return false;
    }
    return inputs.Any(x => Visit(x.Code.Trim()));
  }
}
