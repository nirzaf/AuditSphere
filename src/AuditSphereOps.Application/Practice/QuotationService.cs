using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record QuotationHoursLine(string Role, string Activity, decimal Hours, Guid? ExpectedRateCardId = null);

public sealed record SaveQuotationRequest(
  Guid ProposalId,
  IReadOnlyList<QuotationHoursLine> Lines,
  decimal ComplexityFactor,
  decimal RiskPremiumPercent,
  decimal DiscountPercent,
  bool NonStandardTerms,
  string? NonStandardTermsNote,
  long? ExpectedRevision = null, long? ExpectedProposalRevision = null,
  Guid? RequestId = null);

public sealed record QuotationRateOption(string Role, string Activity, decimal RatePerHour, Guid RateCardVersionId);

public sealed record QuotationView(
  QuotationVersion Version,
  IReadOnlyList<QuotationLineResult> Lines,
  IReadOnlyList<RequiredApproval> RequiredApprovals,
  IReadOnlyList<QuotationApproval> Approvals,
  IReadOnlyList<QuotationApprovalRevocation> Revocations);

/// <summary>
/// Versioned quotation workflow: the fee is calculated from approved rate cards, billable hours, a complexity factor
/// and a risk premium; every recalculation is a new immutable revision; the configurable approval matrix decides which
/// roles must approve a discount or non-standard terms. Preparers never approve their own version.
/// </summary>
public static class QuotationService
{
  internal static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
  private static readonly string[] RuleAdminRoles = ["Administrator", "Partner"];
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  public static async Task<CommandResult<Guid>> SaveAsync(
    IAuditSphereDbContext db, ActorContext actor, SaveQuotationRequest request, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    if (request.Lines is null || request.Lines.Count == 0)
      return CommandResult<Guid>.Fail("quotation.invalid", "Add at least one hour line.");
    var note = string.IsNullOrWhiteSpace(request.NonStandardTermsNote) ? null : request.NonStandardTermsNote.Trim();
    if (request.NonStandardTerms && (note is null || note.Length > 2000))
      return CommandResult<Guid>.Fail("quotation.invalid", "Describe the non-standard terms (up to 2000 characters).");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var proposal = await db.Proposals.SingleOrDefaultAsync(x => x.Id == request.ProposalId && x.FirmId == actor.FirmId, ct);
    if (proposal is null) return CommandResult<Guid>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (request.ExpectedProposalRevision.HasValue && request.ExpectedProposalRevision != proposal.Revision)
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Proposal revision changed.");
    if (proposal.Status is not CrmStates.ProposalDraft)
      return CommandResult<Guid>.Fail(ErrorCodes.ProtectedState, "Only a draft proposal can be priced; revise the proposal to reprice.");

    // Fail closed: every line must resolve to the latest APPROVED rate card for its role, activity and currency.
    var currency = proposal.Currency.ToUpperInvariant();
    var priced = new List<QuotationLineInput>();
    foreach (var line in request.Lines)
    {
      var role = (line.Role ?? string.Empty).Trim();
      var activity = (line.Activity ?? string.Empty).Trim();
      var card = await db.RateCardVersions.AsNoTracking()
        .Where(x => x.FirmId == actor.FirmId && x.Status == PracticeTimeStates.RateApproved && x.Currency == currency &&
          x.Role.ToUpper() == role.ToUpper() && x.Activity.ToUpper() == activity.ToUpper())
        .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
      if (line.ExpectedRateCardId.HasValue && line.ExpectedRateCardId != card?.Id)
        return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "An approved rate changed; refresh the quotation.");
      priced.Add(new(role, activity, line.Hours, card?.RatePerHour ?? 0m, card?.Id ?? Guid.Empty));
    }
    var input = new QuotationPricingInput(currency, priced, request.ComplexityFactor, request.RiskPremiumPercent, request.DiscountPercent);
    var invalid = QuotationCalculator.Validate(input);
    if (invalid is not null) return CommandResult<Guid>.Fail("quotation.invalid", invalid);
    var calculated = QuotationCalculator.Calculate(input);
    // The approved pricing policy bounds the quotation (STE 4.1.2): without an approved policy nothing changes;
    // with one, a discount over the cap or a fee outside the approved band fails closed.
    var policy = await db.FirmPricingPolicies.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Currency == currency && x.Status == FirmPricingPolicyStates.Approved)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    if (policy is not null)
    {
      if (policy.MaxDiscountPercent.HasValue && request.DiscountPercent > policy.MaxDiscountPercent.Value)
        return CommandResult<Guid>.Fail("quotation.limit",
          $"The discount {request.DiscountPercent:0.####}% exceeds the approved maximum of {policy.MaxDiscountPercent.Value:0.####}% for {currency}; revise the quotation within the approved pricing policy.");
      if (policy.MinimumFee.HasValue && calculated.Fee < policy.MinimumFee.Value)
        return CommandResult<Guid>.Fail("quotation.limit",
          $"The calculated fee falls below the approved minimum of {policy.MinimumFee.Value:N2} {currency}.");
      if (policy.MaximumFee.HasValue && calculated.Fee > policy.MaximumFee.Value)
        return CommandResult<Guid>.Fail("quotation.limit",
          $"The calculated fee exceeds the approved maximum of {policy.MaximumFee.Value:N2} {currency}; a new approved policy version is required.");
    }

    var hash = QuotationCalculator.InputHash(input, request.NonStandardTerms, note);

    if (request.RequestId is { } reqId && reqId != Guid.Empty)
    {
      var priorVersion = await db.QuotationVersions.AsNoTracking()
        .SingleOrDefaultAsync(x => x.Id == reqId, ct);
      if (priorVersion is not null)
      {
        var matches = priorVersion.FirmId == actor.FirmId
          && priorVersion.ProposalId == request.ProposalId
          && priorVersion.CreatedByUserId == actor.UserId
          && priorVersion.InputHash == hash;
        return matches
          ? CommandResult<Guid>.Ok(priorVersion.Id)
          : CommandResult<Guid>.Fail(ErrorCodes.IdempotencyConflict,
            "This quotation request identity is already bound to different terms.");
      }
    }

    var latest = await db.QuotationVersions.Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (latest is not null && latest.Status != QuotationStates.Superseded && latest.InputHash == hash)
      return CommandResult<Guid>.Ok(latest.Id); // identical recalculation is idempotent

    if (request.ExpectedRevision.HasValue && request.ExpectedRevision != (latest?.Revision ?? 0))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Quotation revision changed.");
    var result = calculated;
    var rules = await db.CommercialApprovalRules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Active).ToListAsync(ct);
    var required = CommercialApprovalMatrix.Required(rules, request.DiscountPercent, request.NonStandardTerms);

    if (latest is not null && latest.Status != QuotationStates.Superseded) latest.Status = QuotationStates.Superseded;
    var version = new QuotationVersion
    {
      Id = request.RequestId is { } id && id != Guid.Empty ? id : Guid.CreateVersion7(),
      FirmId = actor.FirmId, ProposalId = proposal.Id, Revision = (latest?.Revision ?? 0) + 1,
      Currency = currency, LinesJson = JsonSerializer.Serialize(result.Lines, Json), ComplexityFactor = request.ComplexityFactor,
      RiskPremiumPercent = request.RiskPremiumPercent, DiscountPercent = request.DiscountPercent,
      NonStandardTerms = request.NonStandardTerms, NonStandardTermsNote = note, BaseAmount = result.BaseAmount,
      ComplexityAmount = result.ComplexityAmount, RiskPremiumAmount = result.RiskPremiumAmount,
      DiscountAmount = result.DiscountAmount, Fee = result.Fee, InputHash = hash, Status = QuotationStates.Draft,
      RequiredApprovalsJson = JsonSerializer.Serialize(required, Json), CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.QuotationVersions.Add(version);
    // The proposal carries the calculated fee so it and the quotation can never disagree while the proposal is a draft.
    proposal.Fee = result.Fee;
    try { await db.SaveChangesAsync(ct); }
    catch (DbUpdateException) { return CommandResult<Guid>.Fail("quotation.conflict", "The quotation changed; reload and retry."); }
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(version.Id);
  }

  /// <summary>Submit for approval. With no required approval the version is approved immediately.</summary>
  public static async Task<CommandResult> SubmitAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid quotationVersionId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var version = await db.QuotationVersions.SingleOrDefaultAsync(x => x.Id == quotationVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (version.Status is QuotationStates.PendingApproval or QuotationStates.Approved) return CommandResult.Ok();
    if (version.Status != QuotationStates.Draft)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a current draft quotation can be submitted.");
    var required = ReadRequired(version);
    if (required.Count == 0)
    {
      version.Status = QuotationStates.Approved;
      version.ApprovedAt = DateTimeOffset.UtcNow;
      await ApplyValidityWindowAsync(db, version, ct);
    }
    else version.Status = QuotationStates.PendingApproval;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult> ApproveAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid quotationVersionId, string ruleKey, string reason,
    CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000)
      return CommandResult.Fail("quotation.invalid", "Record a reason of 5 to 1000 characters.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var version = await db.QuotationVersions.SingleOrDefaultAsync(x => x.Id == quotationVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var required = ReadRequired(version);
    var needed = required.SingleOrDefault(x => x.RuleKey == ruleKey);
    if (needed is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // The approver must hold the exact role the matrix names, firm-wide; the approval role is never inferred.
    var auth = await AuthorizeAsync(db, actor, [needed.Role], ct);
    if (!auth.Succeeded) return auth;
    if (version.CreatedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "The preparer of a quotation cannot approve it.");
    // An approved version is revisited only to replace a withdrawn approval; a draft or superseded version never is.
    if (version.Status is not (QuotationStates.PendingApproval or QuotationStates.Approved))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted, current quotation can be approved.");
    // A withdrawn approval no longer counts: the rule key can be approved again by a fresh decision.
    var already = await db.QuotationApprovals.AnyAsync(x => x.FirmId == actor.FirmId && x.QuotationVersionId == version.Id &&
      x.RuleKey == ruleKey && !db.QuotationApprovalRevocations.Any(r => r.FirmId == actor.FirmId && r.QuotationApprovalId == x.Id), ct);
    if (already) return CommandResult.Ok();

    db.QuotationApprovals.Add(new QuotationApproval
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, QuotationVersionId = version.Id, RuleKey = ruleKey,
      RequiredRole = needed.Role, ApprovedByUserId = actor.UserId, Reason = reason.Trim(), ApprovedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    var wasApproved = version.Status == QuotationStates.Approved;
    if (await RequiredApprovalsStandAsync(db, actor.FirmId, version, ct))
    {
      if (!wasApproved)
      {
        version.Status = QuotationStates.Approved;
        version.ApprovedAt = DateTimeOffset.UtcNow;
        await ApplyValidityWindowAsync(db, version, ct);
      }
      await db.SaveChangesAsync(ct);
    }
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>
  /// Withdraws a required approval with an append-only revocation record signed by a distinct role-holder (or the
  /// firm's Administrator safety authority). The approval row and the quotation version are never edited: the
  /// dispatched offer simply no longer stands, so dispatch and acceptance are refused.
  /// </summary>
  public static async Task<CommandResult> RevokeApprovalAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid approvalId, string reason, CancellationToken ct = default)
  {
    if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length is < 5 or > 1000)
      return CommandResult.Fail("quotation.invalid", "Record a revocation reason of 5 to 1000 characters.");
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    var approval = await db.QuotationApprovals.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == approvalId && x.FirmId == actor.FirmId, ct);
    if (approval is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    // The revoker must hold the role the matrix named — or act as the firm's Administrator safety authority —
    // and must never be the original approver.
    var auth = await AuthorizeAsync(db, actor, [approval.RequiredRole], ct);
    if (!auth.Succeeded)
    {
      auth = await AuthorizeAsync(db, actor, ["Administrator"], ct);
      if (!auth.Succeeded) return auth;
    }
    if (approval.ApprovedByUserId == actor.UserId)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "An approver cannot revoke their own approval; a distinct role-holder revokes it.");
    var version = await db.QuotationVersions.AsNoTracking()
      .SingleOrDefaultAsync(x => x.Id == approval.QuotationVersionId && x.FirmId == actor.FirmId, ct);
    if (version is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    if (await db.QuotationApprovalRevocations.AnyAsync(x => x.FirmId == actor.FirmId && x.QuotationApprovalId == approval.Id, ct))
      return CommandResult.Ok();
    // A superseded revision is history: nothing can be dispatched or accepted on it, so there is nothing to withdraw.
    if (version.Status is not (QuotationStates.PendingApproval or QuotationStates.Approved))
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only an approval on the current quotation revision can be revoked.");
    db.QuotationApprovalRevocations.Add(new QuotationApprovalRevocation
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, QuotationApprovalId = approval.Id,
      Reason = reason.Trim(), RevokedByUserId = actor.UserId, RevokedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  /// <summary>An approved pricing policy gives the quotation its validity window; without one the offer carries no expiry.</summary>
  private static async Task ApplyValidityWindowAsync(IAuditSphereDbContext db, QuotationVersion version, CancellationToken ct)
  {
    var policy = await db.FirmPricingPolicies.AsNoTracking()
      .Where(x => x.FirmId == version.FirmId && x.Currency == version.Currency && x.Status == FirmPricingPolicyStates.Approved)
      .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
    version.ValidUntil = policy is null ? null : DateTimeOffset.UtcNow.AddDays(policy.ValidityDays);
  }

  public static async Task<CommandResult<IReadOnlyList<QuotationView>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default, int? limit = null)
  {
    if (limit is < 1 or > 100) return CommandResult<IReadOnlyList<QuotationView>>.Fail("request.invalid", "Invalid limit.");
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<QuotationView>>.Fail(auth.ErrorCode!, auth.Message!);
    var query = db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).AsQueryable();
    if (limit.HasValue) query = query.Take(limit.Value);
    var versions = await query.ToListAsync(ct);
    var ids = versions.Select(x => x.Id).ToList();
    var approvals = await db.QuotationApprovals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.QuotationVersionId)).ToListAsync(ct);
    var approvalIds = approvals.Select(x => x.Id).ToList();
    var revocations = await db.QuotationApprovalRevocations.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && approvalIds.Contains(x.QuotationApprovalId)).ToListAsync(ct);
    IReadOnlyList<QuotationView> views = versions.Select(v =>
    {
      var own = approvals.Where(a => a.QuotationVersionId == v.Id).OrderBy(a => a.ApprovedAt).ThenBy(a => a.Id).ToList();
      var ownIds = own.Select(a => a.Id).ToHashSet();
      return new QuotationView(v, JsonSerializer.Deserialize<List<QuotationLineResult>>(v.LinesJson, Json) ?? [], ReadRequired(v),
        own, revocations.Where(r => ownIds.Contains(r.QuotationApprovalId)).ToList());
    }).ToList();
    return CommandResult<IReadOnlyList<QuotationView>>.Ok(views);
  }

  /// <summary>Latest approved rate per role and activity in the currency: exactly what a quotation line may use.</summary>
  public static async Task<CommandResult<IReadOnlyList<QuotationRateOption>>> ListRateOptionsAsync(
    IAuditSphereDbContext db, ActorContext actor, string currency, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<QuotationRateOption>>.Fail(auth.ErrorCode!, auth.Message!);
    var code = (currency ?? string.Empty).Trim().ToUpperInvariant();
    var cards = await db.RateCardVersions.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Status == PracticeTimeStates.RateApproved && x.Currency == code).ToListAsync(ct);
    IReadOnlyList<QuotationRateOption> options = cards
      .GroupBy(x => (x.Role.ToUpperInvariant(), x.Activity.ToUpperInvariant()))
      .Select(g => g.OrderByDescending(x => x.Version).First())
      .OrderBy(x => x.Role, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Activity, StringComparer.OrdinalIgnoreCase)
      .Select(x => new QuotationRateOption(x.Role, x.Activity, x.RatePerHour, x.Id)).ToList();
    return CommandResult<IReadOnlyList<QuotationRateOption>>.Ok(options);
  }

  // ---- Configurable approval matrix ----

  public static async Task<CommandResult<Guid>> SaveRuleAsync(
    IAuditSphereDbContext db, ActorContext actor, string kind, decimal? thresholdPercent, string requiredRole,
    CancellationToken ct = default, string? expectedRulesRevision = null)
  {
    var auth = await AuthorizeAsync(db, actor, RuleAdminRoles, ct);
    if (!auth.Succeeded) return CommandResult<Guid>.Fail(auth.ErrorCode!, auth.Message!);
    var role = RoleAdministrationService.CanonicalRole(requiredRole);
    if (role is null || role == "ClientUser")
      return CommandResult<Guid>.Fail("quotation.invalid", "Choose an internal AuditSphere role as the required approver.");
    if (kind == CommercialRuleKinds.DiscountOver)
    {
      if (thresholdPercent is null or < 0 or >= 100 || decimal.Round(thresholdPercent.Value, 4) != thresholdPercent)
        return CommandResult<Guid>.Fail("quotation.invalid", "A discount band needs a threshold from 0 up to (not including) 100.");
    }
    else if (kind == CommercialRuleKinds.NonStandardTerms)
    {
      if (thresholdPercent is not null) return CommandResult<Guid>.Fail("quotation.invalid", "A terms rule has no threshold.");
    }
    else return CommandResult<Guid>.Fail("quotation.invalid", "Unknown rule kind.");

    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult<Guid>.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (expectedRulesRevision is not null && expectedRulesRevision != await RulesRevisionAsync(db, actor.FirmId, ct))
      return CommandResult<Guid>.Fail(ErrorCodes.StaleRevision, "Approval rules changed; review the current matrix.");
    var previous = await db.CommercialApprovalRules.Where(x => x.FirmId == actor.FirmId && x.Kind == kind && x.ThresholdPercent == thresholdPercent)
      .OrderByDescending(x => x.Version).ToListAsync(ct);
    // A terms rule may exist for several roles; a band is one row per threshold, so a new version replaces it.
    if (kind == CommercialRuleKinds.DiscountOver) foreach (var old in previous) old.Active = false;
    else if (previous.Any(x => x.Active && x.RequiredRole == role)) return CommandResult<Guid>.Ok(previous.First(x => x.Active && x.RequiredRole == role).Id);
    var rule = new CommercialApprovalRule
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, Kind = kind, ThresholdPercent = thresholdPercent,
      RequiredRole = role, Version = (previous.FirstOrDefault()?.Version ?? 0) + 1, Active = true,
      CreatedByUserId = actor.UserId, CreatedAt = DateTimeOffset.UtcNow
    };
    db.CommercialApprovalRules.Add(rule);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult<Guid>.Ok(rule.Id);
  }

  public static async Task<CommandResult> DeactivateRuleAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, CancellationToken ct = default, string? expectedRulesRevision = null)
  {
    var auth = await AuthorizeAsync(db, actor, RuleAdminRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
    if (await LockFirmAsync(db, actor.FirmId, ct) is null)
      return CommandResult.Fail(ErrorCodes.GateBlocked, "Firm safety state is unavailable.");
    if (expectedRulesRevision is not null && expectedRulesRevision != await RulesRevisionAsync(db, actor.FirmId, ct))
      return CommandResult.Fail(ErrorCodes.StaleRevision, "Approval rules changed; review the current matrix.");
    var rule = await db.CommercialApprovalRules.SingleOrDefaultAsync(x => x.Id == ruleId && x.FirmId == actor.FirmId, ct);
    if (rule is null) return CommandResult.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    rule.Active = false;
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<IReadOnlyList<CommercialApprovalRule>>> ListRulesAsync(
    IAuditSphereDbContext db, ActorContext actor, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<CommercialApprovalRule>>.Fail(auth.ErrorCode!, auth.Message!);
    IReadOnlyList<CommercialApprovalRule> rules = await db.CommercialApprovalRules.AsNoTracking()
      .Where(x => x.FirmId == actor.FirmId && x.Active).OrderBy(x => x.Kind).ThenBy(x => x.ThresholdPercent).ToListAsync(ct);
    return CommandResult<IReadOnlyList<CommercialApprovalRule>>.Ok(rules);
  }

  internal static string RulesRevision(IEnumerable<CommercialApprovalRule> rules) => Hashing.Sha256Hex(
    System.Text.Encoding.UTF8.GetBytes(string.Join("|", rules.Where(r => r.Active).OrderBy(r => r.Id)
      .Select(r => $"{r.Id:D}:{r.Version}:{r.Kind}:{r.RequiredRole}:{r.ThresholdPercent?.ToString(System.Globalization.CultureInfo.InvariantCulture)}"))));
  private static async Task<string> RulesRevisionAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    RulesRevision(await db.CommercialApprovalRules.AsNoTracking().Where(r => r.FirmId == firmId && r.Active).ToListAsync(ct));

  private static IReadOnlyList<RequiredApproval> ReadRequired(QuotationVersion version) =>
    JsonSerializer.Deserialize<List<RequiredApproval>>(version.RequiredApprovalsJson, Json) ?? [];

  /// <summary>True when every approval the matrix requires for this version still stands (none has been revoked).</summary>
  internal static async Task<bool> ApprovalsStandAsync(IAuditSphereDbContext db, Guid firmId, Guid quotationVersionId, CancellationToken ct)
  {
    var version = await db.QuotationVersions.AsNoTracking().SingleOrDefaultAsync(x => x.FirmId == firmId && x.Id == quotationVersionId, ct);
    if (version is null) return false;
    return await RequiredApprovalsStandAsync(db, firmId, version, ct);
  }

  private static async Task<bool> RequiredApprovalsStandAsync(IAuditSphereDbContext db, Guid firmId, QuotationVersion version, CancellationToken ct)
  {
    var required = ReadRequired(version);
    if (required.Count == 0) return true;
    var activeKeys = await db.QuotationApprovals.AsNoTracking()
      .Where(x => x.FirmId == firmId && x.QuotationVersionId == version.Id &&
        !db.QuotationApprovalRevocations.Any(r => r.FirmId == firmId && r.QuotationApprovalId == x.Id))
      .Select(x => x.RuleKey).ToListAsync(ct);
    return required.All(r => activeKeys.Contains(r.RuleKey));
  }

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
