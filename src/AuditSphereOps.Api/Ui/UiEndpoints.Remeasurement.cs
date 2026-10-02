using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record RemeasurementItemInput(string Reference, Guid SnapshotId, Guid SourceLineId, bool IsMonetary, string Currency, string ForeignAmount,
    string PriorCarrying, string? HistoricalRateDate);
  public sealed record RemeasurementInput(Guid ClientId, Guid EngagementId, Guid PeriodId, Guid RateSetId, Guid PolicyId, string AsOfDate,
    IReadOnlyList<RemeasurementItemInput> Items, string ReviewToken = "", bool Reviewed = false);
  public sealed record RemeasurementApproval(string ReviewToken, bool Reviewed);

  private static void MapRemeasurementEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/accounting/remeasurement", http => ReadAsync(http, (db, actor, ct) => CurrencyRemeasurementWorkspaceQuery.GetAsync(db, actor, ct)));
    group.MapGet("/accounting/remeasurement/choices", (Guid periodId, Guid engagementId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => CurrencyRemeasurementWorkspaceQuery.ChoicesAsync(db, actor, periodId, engagementId, ct)));
    group.MapGet("/accounting/remeasurement/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => CurrencyRemeasurementWorkspace.DetailAsync(db, actor, id, ct)));
    group.MapPost("/accounting/remeasurement", (RemeasurementInput i, HttpContext http) =>
    {
      if (!TryDate(i.AsOfDate, out var asOf) || i.Items is not { Count: > 0 and <= 500 }) return Task.FromResult(Invalid("Select a scoped period and at least one evidence line."));
      var items = new List<CurrencyRemeasurementItemRequest>();
      foreach (var x in i.Items)
      {
        DateOnly? historical = TryDate(x.HistoricalRateDate, out var h) ? h : null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(x.ForeignAmount ?? "", @"^-?\d{1,14}(\.\d{1,6})?$") || !System.Text.RegularExpressions.Regex.IsMatch(x.PriorCarrying ?? "", @"^-?\d{1,14}(\.\d{1,6})?$") || !TryDecimal(x.ForeignAmount!, out var foreign) || !TryDecimal(x.PriorCarrying!, out var carrying) || (!x.IsMonetary && historical is null))
          return Task.FromResult(Invalid("Complete each line with decimal amounts; historical rate dates are required for non-monetary items."));
        items.Add(new(x.Reference?.Trim() ?? "", x.SnapshotId, x.IsMonetary, x.Currency ?? "", foreign, carrying, historical, x.SourceLineId));
      }
      return CommandAsync(http, (db, actor, ct) => CurrencyRemeasurementWorkspace.PrepareAsync(db, actor,
        new CurrencyRemeasurementScheduleRequest(i.ClientId, i.EngagementId, i.PeriodId, i.RateSetId, i.PolicyId, asOf, items), i.ReviewToken, i.Reviewed, ct));
    });
    group.MapPost("/accounting/remeasurement/{id:guid}/approve", (Guid id, RemeasurementApproval input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => CurrencyRemeasurementWorkspace.ApproveAsync(db, actor, id, input.ReviewToken, input.Reviewed, ct)));
  }
}
