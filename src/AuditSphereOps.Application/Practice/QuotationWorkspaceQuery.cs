using System.Globalization;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Application.Practice;

public sealed record QuotationRate(Guid Id, string Role, string Activity, string Rate);
public sealed record QuotationAmountLine(string Role, string Activity, string Hours, string Rate, string Amount, Guid RateCardId);
public sealed record QuotationAmounts(string BaseAmount, string ComplexityAmount, string RiskPremiumAmount,
  string DiscountAmount, string Fee, IReadOnlyList<QuotationAmountLine> Lines);
public sealed record QuotationRule(string Key, string Role, string Requirement, bool Approved, bool CanApprove,
  Guid? ApprovedBy, DateTimeOffset? ApprovedAt, string? Reason, Guid? ApprovalId, bool CanRevoke,
  DateTimeOffset? RevokedAt, string? RevocationReason);
public sealed record QuotationRevision(Guid Id, string Revision, string Status, string Complexity, string Risk,
  string Discount, bool NonStandardTerms, string? Note, DateTimeOffset? ValidUntil, bool ApprovalsStand, bool Expired,
  QuotationAmounts Amounts, IReadOnlyList<QuotationRule> Rules);
public sealed record QuotationWorkspace(Guid ProposalId, string ProposalRevision, string Currency, bool Editable,
  IReadOnlyList<QuotationRate> Rates, IReadOnlyList<QuotationRevision> Versions);

public static class QuotationWorkspaceQuery
{
  private static string Exact(decimal value) => value.ToString(CultureInfo.InvariantCulture);
  public static QuotationAmounts Amounts(QuotationPricingResult result) => new(Exact(result.BaseAmount),
    Exact(result.ComplexityAmount), Exact(result.RiskPremiumAmount), Exact(result.DiscountAmount), Exact(result.Fee),
    result.Lines.Select(l => new QuotationAmountLine(l.Role, l.Activity, Exact(l.Hours), Exact(l.RatePerHour), Exact(l.Amount), l.RateCardVersionId)).ToArray());

  public static async Task<CommandResult<QuotationWorkspace>> GetAsync(IAuditSphereDbContext db, ActorContext actor,
    Guid proposalId, CancellationToken ct = default)
  {
    var proposal = await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposalId, ct);
    if (!proposal.Succeeded) return CommandResult<QuotationWorkspace>.Fail(ErrorCodes.ScopeDenied, "Quotation unavailable.");
    var p = proposal.Value!;
    var rates = await QuotationService.ListRateOptionsAsync(db, actor, p.Currency, ct);
    var versions = await QuotationService.ListAsync(db, actor, proposalId, ct, limit: 100);
    if (!rates.Succeeded || !versions.Succeeded || rates.Value!.Count > 200)
      return CommandResult<QuotationWorkspace>.Fail(ErrorCodes.GateBlocked, "Quotation unavailable.");
    var rows = new List<QuotationRevision>();
    foreach (var view in versions.Value!)
    {
      if (view.Lines.Count > 100 || view.RequiredApprovals.Count > 100)
        return CommandResult<QuotationWorkspace>.Fail(ErrorCodes.GateBlocked, "Quotation requires bounded review.");
      var rules = new List<QuotationRule>();
      foreach (var needed in view.RequiredApprovals)
      {
        // The standing approval is the one no revocation has withdrawn; a withdrawn approval stays visible as evidence.
        var revoked = view.Revocations.Select(r => r.QuotationApprovalId).ToHashSet();
        var approved = view.Approvals.LastOrDefault(a => a.RuleKey == needed.RuleKey && !revoked.Contains(a.Id));
        var withdrawn = approved is null
          ? view.Revocations.Where(r => view.Approvals.Any(a => a.Id == r.QuotationApprovalId && a.RuleKey == needed.RuleKey))
              .OrderByDescending(r => r.RevokedAt).FirstOrDefault()
          : null;
        var open = view.Version.Status is "PENDING_APPROVAL" or "APPROVED";
        var holdsRole = (await AuthorizationDecision.AuthorizeAsync(db, actor,
          new AuthorizationRequest(actor.FirmId, RequiredRoles: [needed.Role], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded;
        var allowed = approved is null && view.Version.CreatedByUserId != actor.UserId && open && holdsRole;
        var revocable = approved is not null && open && approved.ApprovedByUserId != actor.UserId && (holdsRole ||
          (await AuthorizationDecision.AuthorizeAsync(db, actor,
            new AuthorizationRequest(actor.FirmId, RequiredRoles: ["Administrator"], InternalOnly: true, RequireFirmWide: true), ct)).Succeeded);
        rules.Add(new(needed.RuleKey, needed.Role, needed.Reason, approved is not null, allowed,
          approved?.ApprovedByUserId, approved?.ApprovedAt, approved?.Reason, approved?.Id, revocable,
          withdrawn?.RevokedAt, withdrawn?.Reason));
      }
      var v = view.Version;
      var stands = v.Status == "APPROVED" && rules.All(r => r.Approved);
      rows.Add(new(v.Id, v.Revision.ToString(CultureInfo.InvariantCulture), v.Status, Exact(v.ComplexityFactor),
        Exact(v.RiskPremiumPercent), Exact(v.DiscountPercent), v.NonStandardTerms, v.NonStandardTermsNote, v.ValidUntil,
        stands, v.ValidUntil is { } until && until < DateTimeOffset.UtcNow,
        Amounts(new(view.Lines, v.BaseAmount, v.ComplexityAmount, v.RiskPremiumAmount, v.DiscountAmount, v.Fee)), rules));
    }
    if (!(await CommercialWorkspaceQuery.ProposalAsync(db, actor, proposalId, ct)).Succeeded)
      return CommandResult<QuotationWorkspace>.Fail(ErrorCodes.ScopeDenied, "Quotation unavailable.");
    return CommandResult<QuotationWorkspace>.Ok(new(proposalId, p.Revision, p.Currency, p.Status == "DRAFT",
      rates.Value.Select(r => new QuotationRate(r.RateCardVersionId, r.Role, r.Activity, Exact(r.RatePerHour))).ToArray(), rows));
  }

  public static async Task<CommandResult<QuotationAmounts>> PreviewAsync(IAuditSphereDbContext db, ActorContext actor,
    SaveQuotationRequest request, CancellationToken ct = default)
  {
    var workspace = await GetAsync(db, actor, request.ProposalId, ct);
    if (!workspace.Succeeded) return CommandResult<QuotationAmounts>.Fail(workspace.ErrorCode!, workspace.Message!);
    var w = workspace.Value!;
    if (!w.Editable || request.ExpectedProposalRevision?.ToString(CultureInfo.InvariantCulture) != w.ProposalRevision
      || request.ExpectedRevision?.ToString(CultureInfo.InvariantCulture) != (w.Versions.FirstOrDefault()?.Revision ?? "0"))
      return CommandResult<QuotationAmounts>.Fail(ErrorCodes.StaleRevision, "Refresh the reviewed quotation.");
    var lines = new List<QuotationLineInput>();
    foreach (var line in request.Lines)
    {
      var rate = w.Rates.SingleOrDefault(r => r.Id == line.ExpectedRateCardId && r.Role == line.Role && r.Activity == line.Activity);
      if (rate is null) return CommandResult<QuotationAmounts>.Fail(ErrorCodes.StaleRevision, "Approved rate unavailable.");
      lines.Add(new(line.Role, line.Activity, line.Hours, decimal.Parse(rate.Rate, CultureInfo.InvariantCulture), rate.Id));
    }
    var input = new QuotationPricingInput(w.Currency, lines, request.ComplexityFactor, request.RiskPremiumPercent, request.DiscountPercent);
    var invalid = QuotationCalculator.Validate(input);
    if (invalid is not null) return CommandResult<QuotationAmounts>.Fail("quotation.invalid", invalid);
    return CommandResult<QuotationAmounts>.Ok(Amounts(QuotationCalculator.Calculate(input)));
  }
}
