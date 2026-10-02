using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record RestatementInput(Guid PeriodId, Guid OriginalPackageId, Guid RevisedPackageId, string RevisedBasis, string Reason, string EvidenceReference,
    string ChangeType, string? AffectedPeriods);

  private static void MapPeriodMaintenanceEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/accounting/evidence", http => ReadAsync(http, (db, actor, ct) => AccountingEvidenceQueueQuery.GetAsync(db, actor, ct)));
    group.MapUiGet("/accounting/period-maintenance", http => ReadAsync(http, (db, actor, ct) => PeriodMaintenanceQuery.OverviewAsync(db, actor, ct)));
    group.MapGet("/accounting/period-maintenance/clients/{id:guid}", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PeriodMaintenanceQuery.ClosedPeriodsAsync(db, actor, id, ct)));
    group.MapPost("/accounting/clients/{id:guid}/restatements", (Guid id, RestatementInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ClientAccountingService.CreatePeriodRestatementAsync(db, actor, new CreatePeriodRestatementRequest(id, i.PeriodId,
        i.OriginalPackageId, i.RevisedPackageId, i.RevisedBasis ?? "", i.Reason ?? "", i.EvidenceReference ?? "", i.ChangeType ?? "", i.AffectedPeriods ?? ""), ct)));
    group.MapPost("/accounting/restatements/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ClientAccountingService.ApprovePeriodRestatementAsync(db, actor, id, ct)));
  }
}
