using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record SaveFirmPricingPolicyRequest(string Currency, decimal? MinimumFee, decimal? MaximumFee,
  decimal? MaxDiscountPercent, int ValidityDays, long? ExpectedRevision = null);

public sealed record PricingPolicyItem(Guid Id, string Version, string Currency, string? MinimumFee, string? MaximumFee,
  string? MaxDiscountPercent, string ValidityDays, string Status, bool Current, bool CanSubmit, bool CanApprove,
  DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt);

/// <summary>
/// The firm's approved pricing basis and limits (STE 4.1.2 / AS-PAR-009): a rule administrator authors a versioned
/// policy and a distinct one approves it. The latest approved row for a currency bounds new quotations and defines
/// their validity window; historical documents are never rewritten when the policy changes.
/// </summary>
public static class FirmPricingPolicyService
{
  private static readonly string[] RuleAdminRoles = ["Administrator", "Partner"];

  /// <summary>The latest approved policy for a currency, or null when no policy governs it yet.</summary>
  public static async Task<CommandResult<FirmPricingPolicy?>> ActiveAsync(
    IAuditSphereDbContext db, ActorContext actor, string currency, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<FirmPricingPolicy?>.Fail(auth.ErrorCode!, auth.Message!);
    var code = NormalizeCurrency(currency);
    if (code is null) return CommandResult<FirmPricingPolicy?>.Fail("pricing-policy.invalid", "A three-letter currency is required.");
    return CommandResult<FirmPricingPolicy?>.Ok(await db.FirmPricingPolicies.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Currency == code && x.Status == FirmPricingPolicyStates.Approved)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct));
  }

  public static async Task<CommandResult<IReadOnlyList<FirmPricingPolicy>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<FirmPricingPolicy>>.Fail(auth.ErrorCode!, auth.Message!);
    IReadOnlyList<FirmPricingPolicy> rows = await db.FirmPricingPolicies.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId).OrderBy(x => x.Currency).ThenByDescending(x => x.Version).Take(100)
      .ToListAsync(ct);
    return CommandResult<IReadOnlyList<FirmPricingPolicy>>.Ok(rows);
  }

  /// <summary>Policy versions as the settings screen renders them: exact decimal text and server-computed actions.</summary>
  public static async Task<CommandResult<IReadOnlyList<PricingPolicyItem>>> WorkspaceAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var listed = await ListAsync(db, actor, ct);
    if (!listed.Succeeded) return CommandResult<IReadOnlyList<PricingPolicyItem>>.Fail(listed.ErrorCode!, listed.Message!);
    var admin = (await AuthorizeAsync(db, actor, RuleAdminRoles, ct)).Succeeded;
    static string? Exact(decimal? value) => value?.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
    var rows = listed.Value!;
    IReadOnlyList<PricingPolicyItem> items = rows.Select(p => new PricingPolicyItem(p.Id,
      p.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Currency, Exact(p.MinimumFee), Exact(p.MaximumFee),
      Exact(p.MaxDiscountPercent), p.ValidityDays.ToString(System.Globalization.CultureInfo.InvariantCulture), p.Status,
      p.Status == FirmPricingPolicyStates.Approved && !rows.Any(o => o.Currency == p.Currency &&
        o.Status == FirmPricingPolicyStates.Approved && o.Version > p.Version),
      admin && p.Status == FirmPricingPolicyStates.Draft,
      admin && p.Status == FirmPricingPolicyStates.PendingApproval && p.CreatedByUserId != actor.UserId,
      p.CreatedAt, p.ApprovedAt)).ToList();
    return CommandResult<IReadOnlyList<PricingPolicyItem>>.Ok(items);
  }

  public static async Task<CommandResult<Guid>> SaveAsync(
    IAuditSphereDbContext db, ActorContext actor, SaveFirmPricingPolicyRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, RuleAdminRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var code = NormalizeCurrency(request.Currency);
    if (code is null)
      return CommandResult<Guid>.Fail("pricing-policy.invalid", "A three-letter currency is required.");
    if (request.MinimumFee is < 0 || request.MaximumFee is < 0)
      return CommandResult<Guid>.Fail("pricing-policy.invalid", "Fee limits cannot be negative.");
    if (request.MinimumFee.HasValue && request.MaximumFee.HasValue && request.MinimumFee > request.MaximumFee)
      return CommandResult<Guid>.Fail("pricing-policy.invalid", "The minimum fee cannot exceed the maximum fee.");
    if (request.MaxDiscountPercent is { } cap && (cap is < 0 or >= 100 || decimal.Round(cap, 4) != cap))
      return CommandResult<Guid>.Fail("pricing-policy.invalid", "The maximum discount is a percentage from 0 up to (not including) 100.");
    if (request.ValidityDays is < 1 or > 365)
      return CommandResult<Guid>.Fail("pricing-policy.invalid", "The quotation validity window is 1 to 365 days.");
    var normalize = request.MinimumFee.HasValue ? MoneyPolicy.Normalize(request.MinimumFee.Value, 2) : (decimal?)null;
    var normalizeMax = request.MaximumFee.HasValue ? MoneyPolicy.Normalize(request.MaximumFee.Value, 2) : (decimal?)null;

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var latest = await db.FirmPricingPolicies.Where(x => x.FirmId == actor.FirmId && x.Currency == code)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (request.ExpectedRevision.HasValue && request.ExpectedRevision != (latest?.Version ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "The pricing policy changed; review the current version.");
    if (latest is not null && latest.Status == FirmPricingPolicyStates.Draft &&
      latest.MinimumFee == normalize && latest.MaximumFee == normalizeMax &&
      latest.MaxDiscountPercent == request.MaxDiscountPercent && latest.ValidityDays == request.ValidityDays)
      return CommandResult<Guid>.Ok(latest.Id); // an unchanged draft is idempotent
    var policy = new FirmPricingPolicy
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Currency = code, Version = (latest?.Version ?? 0) + 1,
      MinimumFee = normalize, MaximumFee = normalizeMax, MaxDiscountPercent = request.MaxDiscountPercent,
      ValidityDays = request.ValidityDays, Status = FirmPricingPolicyStates.Draft,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.FirmPricingPolicies.Add(policy);
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult<Guid>.Fail("pricing-policy.conflict", "The pricing policy changed; reload and retry."); }
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(policy.Id);
  }

  public static async Task<CommandResult> SubmitAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid policyId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, RuleAdminRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var policy = await db.FirmPricingPolicies.SingleOrDefaultAsync(x => x.Id == policyId && x.FirmId == actor.FirmId, ct);
    if (policy is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (policy.Status is FirmPricingPolicyStates.PendingApproval or FirmPricingPolicyStates.Approved) return CommandResult.Ok();
    policy.Status = FirmPricingPolicyStates.PendingApproval;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid policyId, string reason, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000)
      return CommandResult.Fail("pricing-policy.invalid", "Record an approval reason of 5 to 1000 characters.");
    var auth = await AuthorizeAsync(db, actor, RuleAdminRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var policy = await db.FirmPricingPolicies.SingleOrDefaultAsync(x => x.Id == policyId && x.FirmId == actor.FirmId, ct);
    if (policy is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (policy.Status == FirmPricingPolicyStates.Approved) return CommandResult.Ok();
    if (policy.Status != FirmPricingPolicyStates.PendingApproval)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted pricing policy can be approved.");
    if (policy.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The author of a pricing policy cannot approve it; a distinct second rule administrator decides.");
    policy.Status = FirmPricingPolicyStates.Approved;
    policy.ApprovedByUserId = actor.UserId;
    policy.ApprovedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  internal static string? NormalizeCurrency(string? currency)
  {
    var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
    return code.Length == 3 && code.All(char.IsAsciiLetter) ? code : null;
  }

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: QuotationService.CommercialRoles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
