using AuditSphereOps.Application.Accounting;
namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  public sealed record CurrencySetInput(string Code, string Source, string? EffectiveFrom, string? EffectiveTo, int Version, string Revision, bool Reviewed);
  public sealed record CurrencyRateInput(string FromCurrency, string ToCurrency, string Date, string Purpose, string Rate, string Direction, string Revision, bool Reviewed);
  public sealed record CurrencyPolicyInput(string Code, string FunctionalCurrency, string PresentationCurrency, string ClosingRule, string AverageRule, string HistoricalRule, string Revision, bool Reviewed);
  public sealed record CurrencyApprovalInput(string Revision, bool Reviewed);
  private static bool CurrencyCode(string? value) => value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^[A-Z]{3}$");
  private static void MapCurrencyConfigurationEndpoints(RouteGroupBuilder group)
  {
    const string root = "/accounting/currency-configuration";
    group.MapUiGet(root, h => ReadAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.GetAsync(db, a, ct)));
    group.MapGet(root + "/sets/{id:guid}", (Guid id, HttpContext h) => ReadAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.RateSetAsync(db, a, id, ct)));
    group.MapGet(root + "/policies/{id:guid}", (Guid id, HttpContext h) => ReadAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.PolicyAsync(db, a, id, ct)));
    group.MapPost(root + "/sets", (CurrencySetInput i, HttpContext h) =>
    {
      DateOnly? from = null, to = null;
      if (!string.IsNullOrEmpty(i.EffectiveFrom)) { if (!TryDate(i.EffectiveFrom, out var d)) return Task.FromResult(Invalid("Use an ISO effective-from date.")); from = d; }
      if (!string.IsNullOrEmpty(i.EffectiveTo)) { if (!TryDate(i.EffectiveTo, out var d)) return Task.FromResult(Invalid("Use an ISO effective-to date.")); to = d; }
      if (i.Code is not { Length: > 0 and <= 100 } || i.Source is not { Length: > 0 and <= 2000 }) return Task.FromResult(Invalid("Supply a bounded code and rate source."));
      return CommandAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.CreateSetAsync(db, a, new(i.Code, i.Source, from, to, i.Version), i.Revision, i.Reviewed, ct));
    });
    group.MapPost(root + "/sets/{id:guid}/rates", (Guid id, CurrencyRateInput i, HttpContext h) =>
    {
      if (!CurrencyCode(i.FromCurrency) || !CurrencyCode(i.ToCurrency) || !TryDate(i.Date, out var date) || i.Purpose is not ("CLOSING" or "AVERAGE" or "HISTORICAL") ||
        i.Direction != "DIRECT" || !System.Text.RegularExpressions.Regex.IsMatch(i.Rate ?? "", @"^\d{1,12}(\.\d{1,6})?$") || !TryDecimal(i.Rate!, out var rate) || rate <= 0)
        return Task.FromResult(Invalid("Supply distinct three-letter currencies, an ISO date, declared purpose and positive DIRECT rate with at most six decimal places."));
      return CommandAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.AddRateAsync(db, a, id, new(i.FromCurrency, i.ToCurrency, date, i.Purpose, rate, i.Direction), i.Revision, i.Reviewed, ct));
    });
    group.MapPost(root + "/policies", (CurrencyPolicyInput i, HttpContext h) =>
    {
      if (i.Code is not { Length: > 0 and <= 100 } || !CurrencyCode(i.FunctionalCurrency) || !CurrencyCode(i.PresentationCurrency) || i.ClosingRule is not "CLOSING" || i.AverageRule is not "AVERAGE" || i.HistoricalRule is not "HISTORICAL")
        return Task.FromResult(Invalid("Supply explicit currencies and the supported closing, average and historical purpose rules."));
      return CommandAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.CreatePolicyAsync(db, a, new(i.Code, i.FunctionalCurrency, i.PresentationCurrency, i.ClosingRule, i.AverageRule, i.HistoricalRule), i.Revision, i.Reviewed, ct));
    });
    group.MapPost(root + "/sets/{id:guid}/approve", (Guid id, CurrencyApprovalInput i, HttpContext h) => CommandAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.ApproveSetAsync(db, a, id, i.Revision, i.Reviewed, ct)));
    group.MapPost(root + "/policies/{id:guid}/approve", (Guid id, CurrencyApprovalInput i, HttpContext h) => CommandAsync(h, (db, a, ct) => CurrencyConfigurationWorkspace.ApprovePolicyAsync(db, a, id, i.Revision, i.Reviewed, ct)));
  }
}
