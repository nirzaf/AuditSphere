using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Application.Security;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Application.Accounting;

public sealed record DrillDownProcedure(Guid ProcedureId, string SourceProcedureId, string Title, string Status, string? Section);
public sealed record StatementLineView(string DestinationCode, string StatementSection, string AuditArea, decimal Amount, int SourceAccountCount,
  IReadOnlyList<string> SourceAccounts, IReadOnlyList<DrillDownProcedure> Procedures);
public sealed record StatementView(string Title, IReadOnlyList<StatementLineView> Lines, decimal Total, string TotalLabel);
public sealed record StatementDrillDownView(Guid MappingVersionId, long MappingVersion, string DatasetDigest, string Currency,
  StatementView ProfitOrLoss, StatementView FinancialPosition, bool Balances);

/// <summary>
/// Profit and loss and financial position generated from the engagement's current approved mapping over its sealed
/// trial balance, with each line resolved to the audit procedures for its audit area (the allocation's audit area, or
/// the destination code): procedures whose programme section or linked risk area matches. Amounts are presented with
/// income, liabilities and equity as positive figures; the mapping and dataset identity is returned with the view.
/// </summary>
public static class FinancialStatementDrillDownQuery
{
  private static readonly string[] IncomeSections = ["INCOME", "REVENUE", "P&L", "PROFIT_LOSS", "P_AND_L"];
  private static readonly string[] ExpenseSections = ["EXPENSE", "EXPENSES"];
  private static readonly string[] AssetSections = ["ASSETS", "ASSET"];
  private static readonly string[] ClaimSections = ["LIABILITIES", "LIABILITY", "EQUITY"];

  public static async Task<CommandResult<StatementDrillDownView>> GetAsync(IAuditSphereDbContext db, ActorContext actor, Guid engagementId, CancellationToken ct = default)
  {
    var engagement = await db.Engagements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == engagementId && x.FirmId == actor.FirmId, ct);
    if (engagement is null) return CommandResult<StatementDrillDownView>.Fail(ErrorCodes.ScopeDenied, "Access denied.");
    var auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
      ["Partner", "Manager", "Senior", "Staff", "Auditor", "Reviewer", "Administrator", "AccountingPreparer", "AccountingReviewer"], InternalOnly: true), ct);
    if (!auth.Succeeded) return CommandResult<StatementDrillDownView>.Fail(auth.ErrorCode!, auth.Message!);
    var source = await MappedTrialBalanceSource.LoadAsync(db, actor.FirmId, engagementId, ct);
    if (source is null)
      return CommandResult<StatementDrillDownView>.Fail(ErrorCodes.GateBlocked, "Statements need an approved mapping over a sealed, balanced trial balance.");

    var procedures = await db.AuditProcedures.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .Select(x => new { x.Id, x.SourceProcedureId, x.Title, x.Status, x.SourceSectionTitle, x.RiskId }).ToListAsync(ct);
    var riskAreas = await db.AuditRisks.AsNoTracking().Where(x => x.FirmId == actor.FirmId && x.EngagementId == engagementId)
      .ToDictionaryAsync(x => x.Id, x => x.AccountArea, ct);
    IReadOnlyList<DrillDownProcedure> ProceduresFor(string area) => procedures
      .Where(p => Matches(p.SourceSectionTitle, area) || (p.RiskId is { } r && Matches(riskAreas.GetValueOrDefault(r), area)))
      .OrderBy(p => p.SourceProcedureId, StringComparer.Ordinal)
      .Select(p => new DrillDownProcedure(p.Id, p.SourceProcedureId, p.Title, p.Status, p.SourceSectionTitle)).ToList();

    var result = Project(source, ProceduresFor);
    auth = await AuthorizationDecision.AuthorizeAsync(db, actor, new AuthorizationRequest(actor.FirmId, engagement.PracticeClientId, engagementId,
      ["Partner", "Manager", "Senior", "Staff", "Auditor", "Reviewer", "Administrator", "AccountingPreparer", "AccountingReviewer"], InternalOnly: true), ct);
    return auth.Succeeded ? CommandResult<StatementDrillDownView>.Ok(result) : CommandResult<StatementDrillDownView>.Fail(auth.ErrorCode!, auth.Message!);
  }

  internal static bool SupportedSection(string section) => IncomeSections.Concat(ExpenseSections).Concat(AssetSections).Concat(ClaimSections).Contains(section.Trim().ToUpperInvariant());

  internal static decimal DisplaySign(string section) => AssetSections.Contains(section.Trim().ToUpperInvariant()) ? 1m : -1m;

  internal static StatementDrillDownView Project(MappedTrialBalanceSource.Source source, Func<string, IReadOnlyList<DrillDownProcedure>> proceduresFor)
  {
    StatementView Build(string title, string[] sections, Func<string, decimal> sign, string totalLabel)
    {
      var lines = source.Lines.Where(l => sections.Contains(l.StatementSection.Trim().ToUpperInvariant()))
        .GroupBy(l => (l.DestinationCode, Section: l.StatementSection.Trim().ToUpperInvariant()))
        .OrderBy(g => Array.IndexOf(sections, g.Key.Section)).ThenBy(g => g.Key.DestinationCode, StringComparer.Ordinal)
        .Select(g =>
        {
          var area = g.Select(x => x.AuditArea).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? g.Key.DestinationCode;
          return new StatementLineView(g.Key.DestinationCode, g.Key.Section, area, MoneyPolicy.Normalize(g.Sum(x => sign(g.Key.Section) * x.Amount)),
            g.Select(x => x.SourceAccountCode).Distinct().Count(), g.Select(x => x.SourceAccountCode).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList(),
            proceduresFor(area));
        }).ToList();
      return new StatementView(title, lines, MoneyPolicy.Normalize(lines.Sum(x => x.Amount)), totalLabel);
    }

    var profit = Build("Statement of profit or loss", [.. IncomeSections, .. ExpenseSections],
      _ => -1m, "Profit for the period");
    var position = Build("Statement of financial position", [.. AssetSections, .. ClaimSections],
      section => AssetSections.Contains(section) ? 1m : -1m, "Net of assets less liabilities and equity");
    // Signed TB lines total zero, so assets − (liabilities + equity) equals the unclosed profit for the period.
    var assets = position.Lines.Where(x => AssetSections.Contains(x.StatementSection)).Sum(x => x.Amount);
    var claims = position.Lines.Where(x => ClaimSections.Contains(x.StatementSection)).Sum(x => x.Amount);
    var balances = MoneyPolicy.Normalize(assets - claims) == profit.Total;
    position = position with { Total = MoneyPolicy.Normalize(assets - claims), TotalLabel = "Assets less liabilities and equity (equals profit before closing)" };
    return new(source.Mapping.Id, source.Mapping.Version, MappedTrialBalanceSource.Digest(source.Dataset),
      source.Dataset.Currency, profit, position, balances);
  }

  internal static bool Matches(string? candidate, string area) =>
    !string.IsNullOrWhiteSpace(candidate) && Normalize(candidate).Length > 0 && Normalize(area).Length > 0 &&
    (string.Equals(Normalize(candidate), Normalize(area), StringComparison.Ordinal) ||
     Normalize(candidate).Contains(Normalize(area), StringComparison.Ordinal) || Normalize(area).Contains(Normalize(candidate), StringComparison.Ordinal));

  private static string Normalize(string value) => new(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
}
