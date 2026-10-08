namespace AuditSphereOps.Application.Practice;

/// <summary>One governed engagement-letter template. Its identity is persisted on every letter it generates.</summary>
public sealed record EngagementLetterTemplate(string Id, string ServiceRoute, string Heading);

/// <summary>
/// STE 4.1.4 service-specific engagement-letter templates. The template is derived server-side from the approved service
/// route, so a caller cannot choose one. A route with no configured template resolves to null and generation fails closed.
/// Heading wording is the draft pending firm approval; the template identity is what lineage and immutability rely on.
/// </summary>
public static class EngagementLetterTemplates
{
  public const string Statutory = "EL-STATUTORY-AUDIT-ISA210-v1";
  public const string InternalAudit = "EL-INTERNAL-AUDIT-v1";
  public const string AgreedUponProcedures = "EL-AUP-ISRS4400-v1";

  private static readonly IReadOnlyDictionary<string, EngagementLetterTemplate> ByRoute =
    new Dictionary<string, EngagementLetterTemplate>(StringComparer.Ordinal)
    {
      ["FinancialStatementAudit"] = new(Statutory, "FinancialStatementAudit", "Statutory audit engagement — ISA 210"),
      ["InternalAudit"] = new(InternalAudit, "InternalAudit", "Internal audit engagement — agreed service terms"),
      ["AgreedUponProcedures"] = new(AgreedUponProcedures, "AgreedUponProcedures", "Agreed-upon procedures engagement — ISRS 4400")
    };

  public static EngagementLetterTemplate? Resolve(string serviceRoute) =>
    ByRoute.TryGetValue(serviceRoute, out var template) ? template : null;
}
