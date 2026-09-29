using System.Text.Json;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Completion;
using AuditSphereOps.Domain.Practice;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Practice;

public sealed record QuotationHoursLine(string Role, string Activity, decimal Hours);

public sealed record SaveQuotationRequest(
  Guid ProposalId,
  IReadOnlyList<QuotationHoursLine> Lines,
  decimal ComplexityFactor,
  decimal RiskPremiumPercent,
  decimal DiscountPercent,
  bool NonStandardTerms,
  string? NonStandardTermsNote);

public sealed record QuotationRateOption(string Role, string Activity, decimal RatePerHour, Guid RateCardVersionId);

public sealed record QuotationView(
  QuotationVersion Version,
  IReadOnlyList<QuotationLineResult> Lines,
  IReadOnlyList<RequiredApproval> RequiredApprovals,
  IReadOnlyList<QuotationApproval> Approvals);

/// <summary>
/// Versioned quotation workflow: the fee is calculated from approved rate cards, billable hours, a complexity factor
/// and a risk premium; every recalculation is a new immutable revision; the configurable approval matrix decides which
/// roles must approve a discount or non-standard terms. Preparers never approve their own version.
/// </summary>
public static class QuotationService
{
  private static readonly string[] CommercialRoles = ["Administrator", "Partner", "Manager", "RelationshipManager"];
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
      priced.Add(new(role, activity, line.Hours, card?.RatePerHour ?? 0m, card?.Id ?? Guid.Empty));
    }
    var input = new QuotationPricingInput(currency, priced, request.ComplexityFactor, request.RiskPremiumPercent, request.DiscountPercent);
    var invalid = QuotationCalculator.Validate(input);
    if (invalid is not null) return CommandResult<Guid>.Fail("quotation.invalid", invalid);

    var hash = QuotationCalculator.InputHash(input, request.NonStandardTerms, note);
    var latest = await db.QuotationVersions.Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposal.Id)
      .OrderByDescending(x => x.Revision).FirstOrDefaultAsync(ct);
    if (latest is not null && latest.Status != QuotationStates.Superseded && latest.InputHash == hash)
      return CommandResult<Guid>.Ok(latest.Id); // identical recalculation is idempotent

    var result = QuotationCalculator.Calculate(input);
    var rules = await db.CommercialApprovalRules.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.Active).ToListAsync(ct);
    var required = CommercialApprovalMatrix.Required(rules, request.DiscountPercent, request.NonStandardTerms);

    if (latest is not null && latest.Status != QuotationStates.Superseded) latest.Status = QuotationStates.Superseded;
    var version = new QuotationVersion
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, ProposalId = proposal.Id, Revision = (latest?.Revision ?? 0) + 1,
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
    if (version.Status == QuotationStates.Approved) return CommandResult.Ok();
    if (version.Status != QuotationStates.PendingApproval)
      return CommandResult.Fail(ErrorCodes.ProtectedState, "Only a submitted, current quotation can be approved.");
    var already = await db.QuotationApprovals.AnyAsync(x => x.FirmId == actor.FirmId && x.QuotationVersionId == version.Id && x.RuleKey == ruleKey, ct);
    if (already) return CommandResult.Ok();

    db.QuotationApprovals.Add(new QuotationApproval
    {
      Id = Guid.CreateVersion7(), FirmId = actor.FirmId, QuotationVersionId = version.Id, RuleKey = ruleKey,
      RequiredRole = needed.Role, ApprovedByUserId = actor.UserId, Reason = reason.Trim(), ApprovedAt = DateTimeOffset.UtcNow
    });
    await db.SaveChangesAsync(ct);
    var approvedKeys = await db.QuotationApprovals.Where(x => x.FirmId == actor.FirmId && x.QuotationVersionId == version.Id)
      .Select(x => x.RuleKey).ToListAsync(ct);
    if (required.All(x => approvedKeys.Contains(x.RuleKey)))
    {
      version.Status = QuotationStates.Approved;
      version.ApprovedAt = DateTimeOffset.UtcNow;
      await db.SaveChangesAsync(ct);
    }
    await tx.CommitAsync(ct);
    return CommandResult.Ok();
  }

  public static async Task<CommandResult<IReadOnlyList<QuotationView>>> ListAsync(
    IAuditSphereDbContext db, ActorContext actor, Guid proposalId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, CommercialRoles, ct);
    if (!auth.Succeeded) return CommandResult<IReadOnlyList<QuotationView>>.Fail(auth.ErrorCode!, auth.Message!);
    var versions = await db.QuotationVersions.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.ProposalId == proposalId)
      .OrderByDescending(x => x.Revision).ToListAsync(ct);
    var ids = versions.Select(x => x.Id).ToList();
    var approvals = await db.QuotationApprovals.AsNoTracking().Where(x => x.FirmId == actor.FirmId && ids.Contains(x.QuotationVersionId)).ToListAsync(ct);
    IReadOnlyList<QuotationView> views = versions.Select(v => new QuotationView(v,
      JsonSerializer.Deserialize<List<QuotationLineResult>>(v.LinesJson, Json) ?? [], ReadRequired(v),
      approvals.Where(a => a.QuotationVersionId == v.Id).OrderBy(a => a.ApprovedAt).ToList())).ToList();
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
    CancellationToken ct = default)
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
    IAuditSphereDbContext db, ActorContext actor, Guid ruleId, CancellationToken ct = default)
  {
    var auth = await AuthorizeAsync(db, actor, RuleAdminRoles, ct);
    if (!auth.Succeeded) return auth;
    await using var tx = await db.Database.BeginTransactionAsync(ct);
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

  private static IReadOnlyList<RequiredApproval> ReadRequired(QuotationVersion version) =>
    JsonSerializer.Deserialize<List<RequiredApproval>>(version.RequiredApprovalsJson, Json) ?? [];

  private static Task<CommandResult> AuthorizeAsync(IAuditSphereDbContext db, ActorContext actor, string[] roles, CancellationToken ct) =>
    AuthorizationDecision.AuthorizeAsync(db, actor,
      new AuthorizationRequest(actor.FirmId, RequiredRoles: roles, InternalOnly: true, RequireFirmWide: true), ct);

  private static Task<FirmSafetyState?> LockFirmAsync(IAuditSphereDbContext db, Guid firmId, CancellationToken ct) =>
    db.FirmSafetyStates.FromSqlInterpolated($"SELECT * FROM firm_safety_states WHERE id = {firmId} FOR UPDATE").SingleOrDefaultAsync(ct);
}
