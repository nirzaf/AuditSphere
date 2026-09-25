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
  public static async Task<CommandResult<Guid>> CreateChartVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    string sourceScope, DateOnly effectiveFrom, CancellationToken ct = default)
  {
    var auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (string.IsNullOrWhiteSpace(sourceScope))
      return CommandResult<Guid>.Fail(ErrorCodes.Accounting.MappingInvalid, "A chart source scope is required.");
    var version = (await db.ClientChartVersions.Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .Select(x => (int?)x.Version).MaxAsync(ct) ?? 0) + 1;
    var chart = new ClientChartVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      Version = version, SourceScope = sourceScope.Trim(), EffectiveFrom = effectiveFrom,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.ClientChartVersions.Add(chart);
    await db.SaveChangesAsync(ct);
    return CommandResult<Guid>.Ok(chart.Id);
  }

  public static async Task<CommandResult> AddAccountsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    IReadOnlyList<ClientAccountInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x => string.IsNullOrWhiteSpace(x.StableIdentity) ||
        string.IsNullOrWhiteSpace(x.AccountCode) || string.IsNullOrWhiteSpace(x.AccountName) ||
        string.IsNullOrWhiteSpace(x.AccountType) || string.IsNullOrWhiteSpace(x.NormalBalance)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every client account needs typed identity and classification.");
    var chart = await db.ClientChartVersions.SingleOrDefaultAsync(x => x.Id == chartVersionId && x.FirmId == actor.FirmId, ct);
    if (chart is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (chart.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Published chart versions are immutable.");
    var duplicateInput = inputs.GroupBy(x => x.StableIdentity.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1) ||
      inputs.GroupBy(x => x.AccountCode.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1);
    if (duplicateInput)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Account identities and codes must be unique within a chart version.");
    var stable = inputs.ToDictionary(x => x.StableIdentity.Trim(), StringComparer.OrdinalIgnoreCase);
    if (inputs.Any(x => x.ParentStableIdentity is not null && !stable.ContainsKey(x.ParentStableIdentity.Trim()) &&
        !db.ClientAccounts.Any(a => a.FirmId == chart.FirmId && a.ClientId == chart.ClientId && a.ChartVersionId == chart.Id &&
          a.StableIdentity == x.ParentStableIdentity.Trim())))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every parent account must exist in the same client scope.");
    if (HasParentCycle(inputs, stable))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The client chart contains a parent cycle.");
    var existingCodes = await db.ClientAccounts.Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id)
      .Select(x => x.AccountCode).ToListAsync(ct);
    if (inputs.Any(x => existingCodes.Contains(x.AccountCode.Trim(), StringComparer.OrdinalIgnoreCase)))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "An account code already exists in this chart version.");
    var byStable = await db.ClientAccounts.Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id)
      .ToDictionaryAsync(x => x.StableIdentity, StringComparer.OrdinalIgnoreCase, ct);
    if (inputs.Any(x => x.ParentStableIdentity is { } parent &&
        (stable.TryGetValue(parent.Trim(), out var declaredParent) ? declaredParent.IsPosting : byStable[parent.Trim()].IsPosting)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "A posting account cannot be a chart parent.");
    foreach (var input in inputs)
    {
      if (input.ParentStableIdentity is null || stable.ContainsKey(input.ParentStableIdentity.Trim()))
        continue;
      Guid? parentId = input.ParentStableIdentity is null ? null :
        (stable.TryGetValue(input.ParentStableIdentity.Trim(), out _) ? Guid.Empty : byStable[input.ParentStableIdentity.Trim()].Id);
      db.ClientAccounts.Add(new ClientAccount
      {
        Id = Guid.CreateVersion7(), FirmId = chart.FirmId, ClientId = chart.ClientId, ChartVersionId = chart.Id,
        StableIdentity = input.StableIdentity.Trim(), AccountCode = input.AccountCode.Trim(), AccountName = input.AccountName.Trim(),
        AccountType = input.AccountType.Trim(), NormalBalance = input.NormalBalance.Trim(), ParentAccountId = parentId,
        IsPosting = input.IsPosting, CreatedAt = DateTimeOffset.UtcNow
      });
    }
    // Parents declared in the same request are inserted after IDs are known so the FK remains exact.
    var pending = inputs.Where(x => x.ParentStableIdentity is null || stable.ContainsKey(x.ParentStableIdentity.Trim())).ToArray();
    var added = pending.Select(input => new ClientAccount
    {
      Id = Guid.CreateVersion7(), FirmId = chart.FirmId, ClientId = chart.ClientId, ChartVersionId = chart.Id,
      StableIdentity = input.StableIdentity.Trim(), AccountCode = input.AccountCode.Trim(), AccountName = input.AccountName.Trim(),
      AccountType = input.AccountType.Trim(), NormalBalance = input.NormalBalance.Trim(), IsPosting = input.IsPosting,
      CreatedAt = DateTimeOffset.UtcNow
    }).ToArray();
    var addedByStable = added.ToDictionary(x => x.StableIdentity, StringComparer.OrdinalIgnoreCase);
    foreach (var account in added)
      if (stable[account.StableIdentity].ParentStableIdentity is { } parent)
        account.ParentAccountId = addedByStable.TryGetValue(parent.Trim(), out var newParent)
          ? newParent.Id : byStable[parent.Trim()].Id;
    db.ClientAccounts.AddRange(added);
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> AddSourceAccountAliasesAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    IReadOnlyList<SourceAccountAliasInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x => x.ClientAccountId == Guid.Empty ||
        string.IsNullOrWhiteSpace(x.SourceSystem) || string.IsNullOrWhiteSpace(x.AliasCode)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every source alias needs a client account, source system and code.");
    var chart = await db.ClientChartVersions.SingleOrDefaultAsync(x => x.Id == chartVersionId && x.FirmId == actor.FirmId, ct);
    if (chart is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (chart.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Published chart versions are immutable.");
    var normalized = inputs.Select(x => new
    {
      x.ClientAccountId, SourceSystem = x.SourceSystem.Trim(), AliasCode = x.AliasCode.Trim(), AliasName = x.AliasName.Trim()
    }).ToArray();
    if (normalized.GroupBy(x => (x.SourceSystem, x.AliasCode), StringTupleComparer.Instance).Any(x => x.Count() > 1))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Source aliases must be unique within a chart and source system.");
    var accountIds = normalized.Select(x => x.ClientAccountId).Distinct().ToArray();
    var accounts = await db.ClientAccounts.AsNoTracking().Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId &&
      x.ChartVersionId == chart.Id && accountIds.Contains(x.Id)).Select(x => x.Id).ToListAsync(ct);
    if (accounts.Count != accountIds.Length)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Every source alias account must belong to the same chart and client.");
    var existingAliases = await db.SourceAccountAliases.AsNoTracking().Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId &&
      x.ChartVersionId == chart.Id).Select(x => new { x.SourceSystem, x.AliasCode }).ToListAsync(ct);
    if (existingAliases.Any(existing => normalized.Any(input =>
        string.Equals(existing.SourceSystem, input.SourceSystem, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.AliasCode, input.AliasCode, StringComparison.OrdinalIgnoreCase))))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "A source alias already exists in this chart.");
    db.SourceAccountAliases.AddRange(normalized.Select(x => new SourceAccountAlias
    {
      Id = Guid.CreateVersion7(), FirmId = chart.FirmId, ClientId = chart.ClientId,
      ChartVersionId = chart.Id, ClientAccountId = x.ClientAccountId, SourceSystem = x.SourceSystem,
      AliasCode = x.AliasCode, AliasName = x.AliasName, CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> PublishChartVersionAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    CancellationToken ct = default)
  {
    // Serialize concurrent publishers on the chart revision row before validation.
    var charts = await db.ClientChartVersions
      .FromSqlInterpolated($"SELECT * FROM client_chart_versions WHERE id = {chartVersionId} AND firm_id = {actor.FirmId} FOR UPDATE")
      .ToListAsync(ct);
    var chart = charts.SingleOrDefault(x => x.Id == chartVersionId && x.FirmId == actor.FirmId);
    if (chart is null)
      return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, ReviewerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    if (chart.Status != AccountingWorkflowStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a draft chart can be published.");
    if (chart.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "The chart preparer cannot publish the same version.");
    var accounts = await db.ClientAccounts.Where(x => x.FirmId == chart.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id).ToListAsync(ct);
    if (accounts.Count == 0 || accounts.Any(x => x.ParentAccountId == x.Id))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "A chart needs at least one valid account.");
    // Full publication-time re-validation under the row lock: the draft-time checks are
    // insufficient when two concurrent edits land before publish.
    var duplicateCodes = accounts.GroupBy(x => x.AccountCode.Trim(), StringComparer.OrdinalIgnoreCase)
      .Where(g => g.Count() > 1).Select(g => g.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    if (duplicateCodes.Length > 0)
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete,
        $"Duplicate account codes at publication: {string.Join(", ", duplicateCodes)}.");
    if (accounts.Any(x => x.IsPosting && x.ParentAccountId.HasValue &&
        accounts.SingleOrDefault(p => p.Id == x.ParentAccountId.Value)?.IsPosting == true))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "A posting account cannot have a posting parent.");
    var byStable = accounts.ToDictionary(x => x.StableIdentity.Trim(), StringComparer.OrdinalIgnoreCase);
    var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool Visits(string identity)
    {
      if (state.GetValueOrDefault(identity) == 1) return true;
      if (state.GetValueOrDefault(identity) == 2) return false;
      state[identity] = 1;
      if (byStable[identity].ParentAccountId is { } parentId &&
          accounts.SingleOrDefault(p => p.Id == parentId) is { } parent &&
          byStable.ContainsKey(parent.StableIdentity.Trim()) && Visits(parent.StableIdentity.Trim()))
        return true;
      state[identity] = 2;
      return false;
    }
    if (accounts.Any(x => Visits(x.StableIdentity.Trim())))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "The chart contains an account-parent cycle at publication.");
    if (accounts.Any(x => x.ParentAccountId.HasValue && !accounts.Any(p => p.Id == x.ParentAccountId.Value)))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingIncomplete, "The chart contains an account with a missing parent.");
    chart.Status = AccountingWorkflowStates.Approved;
    chart.PublishedByUserId = actor.UserId;
    chart.PublishedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<ChartRevisionAccountsPage>> GetChartRevisionAccountsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid chartVersionId,
    int page = 1, int pageSize = 100, CancellationToken ct = default)
  {
    if (page < 1 || pageSize is < 1 or > 500)
      return CommandResult<ChartRevisionAccountsPage>.Fail(ErrorCodes.Accounting.MappingInvalid, "A valid page number and size (1-500) are required.");
    var chart = await db.ClientChartVersions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == chartVersionId && x.FirmId == actor.FirmId, ct);
    if (chart is null)
      return CommandResult<ChartRevisionAccountsPage>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizeClientAsync(db, actor, chart.ClientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return CommandResult<ChartRevisionAccountsPage>.Fail(auth.ErrorCode!, auth.Message!);

    var accountsQuery = db.ClientAccounts.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == chart.ClientId && x.ChartVersionId == chart.Id);
    var totalCount = await accountsQuery.CountAsync(ct);
    var allAccounts = await accountsQuery.ToListAsync(ct);
    var byId = allAccounts.ToDictionary(x => x.Id);
    var childCounts = allAccounts.Where(x => x.ParentAccountId.HasValue)
      .GroupBy(x => x.ParentAccountId!.Value)
      .ToDictionary(g => g.Key, g => g.Count());

    var skip = (page - 1) * pageSize;
    var paged = allAccounts.OrderBy(x => x.AccountCode, StringComparer.Ordinal)
      .Skip(skip).Take(pageSize)
      .Select(x => new ChartAccountViewDto(
        x.Id,
        x.StableIdentity,
        x.AccountCode,
        x.AccountName,
        x.AccountType,
        x.NormalBalance,
        x.IsPosting,
        x.ParentAccountId,
        x.ParentAccountId.HasValue && byId.TryGetValue(x.ParentAccountId.Value, out var parent) ? parent.AccountCode : null,
        childCounts.GetValueOrDefault(x.Id, 0)))
      .ToList();

    return CommandResult<ChartRevisionAccountsPage>.Ok(new ChartRevisionAccountsPage(paged, totalCount, page, pageSize));
  }

  public static async Task<CommandResult> AddDimensionDefinitionsAsync(
    IClientAccountingDbContext db, ActorContext actor, Guid clientId,
    IReadOnlyList<AccountingDimensionInput> inputs, CancellationToken ct = default)
  {
    if (inputs.Count == 0 || inputs.Any(x =>
        !AccountingDimensionTypes.All.Contains(x.DimensionType.Trim().ToUpperInvariant()) ||
        string.IsNullOrWhiteSpace(x.Code) || x.Code.Trim().Length > 100 ||
        string.IsNullOrWhiteSpace(x.Name) || x.Name.Trim().Length > 300))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Every accounting dimension needs a supported type, code and name.");
    var auth = await AuthorizeClientAsync(db, actor, clientId, PreparerRoles, ct);
    if (!auth.Succeeded)
      return auth;
    var normalized = inputs.Select(x => new
    {
      DimensionType = x.DimensionType.Trim().ToUpperInvariant(), Code = x.Code.Trim(), Name = x.Name.Trim()
    }).ToArray();
    if (normalized.GroupBy(x => (x.DimensionType, x.Code), StringTupleComparer.Instance).Any(x => x.Count() > 1))
      return CommandResult.Fail(ErrorCodes.Accounting.MappingInvalid, "Accounting dimension codes must be unique within each type.");
    var existing = await db.ClientAccountingDimensionDefinitions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.ClientId == clientId)
      .Select(x => new { x.DimensionType, x.Code }).ToListAsync(ct);
    if (normalized.Any(input => existing.Any(x => x.DimensionType == input.DimensionType && x.Code == input.Code)))
      return CommandResult.Fail(ErrorCodes.IdempotencyConflict, "An accounting dimension code already exists for this client.");
    db.ClientAccountingDimensionDefinitions.AddRange(normalized.Select(x => new ClientAccountingDimensionDefinition
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ClientId = clientId,
      DimensionType = x.DimensionType, Code = x.Code, Name = x.Name,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    }));
    await db.SaveChangesAsync(ct);
    return CommandResult.Ok();
  }

  private static bool HasParentCycle(
    IReadOnlyList<ClientAccountInput> inputs, IReadOnlyDictionary<string, ClientAccountInput> map)
  {
    var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    bool Visit(string identity)
    {
      if (state.GetValueOrDefault(identity) == 1) return true;
      if (state.GetValueOrDefault(identity) == 2) return false;
      state[identity] = 1;
      if (map[identity].ParentStableIdentity is { } parent && map.ContainsKey(parent.Trim()) && Visit(parent.Trim())) return true;
      state[identity] = 2;
      return false;
    }
    return inputs.Any(x => Visit(x.StableIdentity.Trim()));
  }
}
