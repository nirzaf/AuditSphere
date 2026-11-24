namespace AuditSphereOps.Application.Practice;

/// <summary>One governed engagement-letter template. Its identity is persisted on every letter it generates.</summary>
public sealed record EngagementLetterTemplate(string Id, string ServiceRoute, string Heading, string EntityScopeTemplate);

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
      ["FinancialStatementAudit"] = new(Statutory, "FinancialStatementAudit", "Statutory audit engagement — ISA 210",
        "Audit of the standalone financial statements of {clientName}."),
      ["InternalAudit"] = new(InternalAudit, "InternalAudit", "Internal audit engagement — agreed service terms",
        "Internal audit services for {clientName}."),
      ["AgreedUponProcedures"] = new(AgreedUponProcedures, "AgreedUponProcedures", "Agreed-upon procedures engagement — ISRS 4400",
        "Agreed-upon procedures for {clientName}.")
    };

  /// <summary>The routes currently supported by the governed commercial letter workflow.</summary>
  public static IReadOnlyList<EngagementLetterTemplate> All => ByRoute.Values
    .OrderBy(x => x.ServiceRoute, StringComparer.Ordinal).ToArray();

  public static EngagementLetterTemplate? Resolve(string serviceRoute) =>
    ByRoute.TryGetValue(serviceRoute, out var template) ? template : null;

  /// <summary>Safe discovery wording; additional entities are never inferred from a client's name.</summary>
  public static string DefaultEntityScope(string serviceRoute, string clientName) =>
    Resolve(serviceRoute)?.EntityScopeTemplate.Replace("{clientName}", clientName, StringComparison.Ordinal) ?? string.Empty;
}
