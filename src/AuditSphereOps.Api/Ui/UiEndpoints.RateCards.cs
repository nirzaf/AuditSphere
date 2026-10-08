using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

/// <summary>
/// Charge-out rate governance (STE 4.5.1): the current rate slots, draft revisions, approval by a separate firm-wide
/// approver, and the STE QAR baseline loaded as drafts. Every route is firm-wide; a narrower grant is refused.
/// </summary>
public static partial class UiEndpoints
{
  public sealed record RateCardInput(string Role, string Activity, string Currency, string RatePerHour, long? ExpectedVersion);

  private static void MapRateCardEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/practice/rate-cards", http => ReadAsync(http, (db, actor, ct) => RateCardWorkspaceQuery.GetAsync(db, actor, ct)));
    group.MapPost("/practice/rate-cards", (RateCardInput input, HttpContext http) =>
      TryDecimal(input.RatePerHour, out var rate)
        ? CommandAsync(http, (db, actor, ct) => PracticeTimeService.ReviseRateCardAsync(db, actor,
            new RateCardDraftRequest(input.Role, input.Activity, input.Currency, rate, input.ExpectedVersion), ct))
        : Task.FromResult(Invalid("Enter the hourly rate as a number.")));
    group.MapPost("/practice/rate-cards/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PracticeTimeService.ApproveRateCardAsync(db, actor, id, ct)));
    group.MapUiPost("/practice/rate-cards/ste-baseline", http =>
      CommandAsync(http, (db, actor, ct) => SteChargeOutRateBaseline.InitializeDraftsAsync(db, actor, ct)));
  }
}
