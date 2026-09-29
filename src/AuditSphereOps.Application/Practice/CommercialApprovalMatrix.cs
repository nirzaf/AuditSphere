using AuditSphereOps.Domain.Practice;

namespace AuditSphereOps.Application.Practice;

/// <summary>An approval a quotation version needs before it can be used: which rule, which role, and why.</summary>
public sealed record RequiredApproval(string RuleKey, string Role, string Reason);

/// <summary>
/// Pure evaluation of the configurable approval matrix. Discount bands select the single highest band the discount
/// exceeds; non-standard-terms rules all apply. When the firm has configured no active rule of a kind, a documented
/// fail-safe default applies (Partner for a discount above 10% and for non-standard terms) so an unconfigured firm
/// is never silently unguarded.
/// </summary>
public static class CommercialApprovalMatrix
{
  public const decimal DefaultDiscountThresholdPercent = 10m;
  public const string DefaultRole = "Partner";

  public static IReadOnlyList<RequiredApproval> Required(
    IReadOnlyCollection<CommercialApprovalRule> rules, decimal discountPercent, bool nonStandardTerms)
  {
    var active = rules.Where(x => x.Active).ToList();
    var required = new List<RequiredApproval>();

    var bands = active.Where(x => x.Kind == CommercialRuleKinds.DiscountOver && x.ThresholdPercent.HasValue).ToList();
    if (bands.Count == 0)
    {
      if (discountPercent > DefaultDiscountThresholdPercent)
        required.Add(new("DEFAULT:DISCOUNT", DefaultRole,
          $"Discount {discountPercent:0.##}% exceeds the default {DefaultDiscountThresholdPercent:0.##}% (no rule configured)."));
    }
    else
    {
      var band = bands.Where(x => discountPercent > x.ThresholdPercent!.Value).OrderByDescending(x => x.ThresholdPercent).FirstOrDefault();
      if (band is not null)
        required.Add(new(band.Id.ToString("D"), band.RequiredRole,
          $"Discount {discountPercent:0.##}% exceeds the {band.ThresholdPercent:0.##}% band."));
    }

    if (nonStandardTerms)
    {
      var terms = active.Where(x => x.Kind == CommercialRuleKinds.NonStandardTerms).ToList();
      if (terms.Count == 0)
        required.Add(new("DEFAULT:TERMS", DefaultRole, "Non-standard contractual terms (no rule configured)."));
      else
        required.AddRange(terms.Select(x => new RequiredApproval(x.Id.ToString("D"), x.RequiredRole, "Non-standard contractual terms.")));
    }
    return required;
  }
}
