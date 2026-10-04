using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record RestatementInput(Guid PeriodId, Guid OriginalPackageId, Guid RevisedPackageId, string RevisedBasis, string Reason, string EvidenceReference,
    string ChangeType, string? AffectedPeriods);

  private static void MapPeriodMaintenanceEndpoints(RouteGroupBuilder group)
  {
    MapValuationPreparationEndpoints(group);
    group.MapGet("/engagements/{id:guid}/analytical-preparation", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.ContextAsync(db, actor, id, ct)));
    group.MapGet("/engagements/{id:guid}/analytical-preparation/state", (Guid id, Guid periodId,
      Guid? comparisonPeriodId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.StateAsync(db, actor, id, periodId, comparisonPeriodId, ct)));
    group.MapPost("/engagements/{id:guid}/analytical-preparation/preview", (Guid id,
      AnalyticalPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.PreviewAsync(db, actor, id, input, ct)));
    group.MapPost("/engagements/{id:guid}/analytical-preparation", (Guid id,
      AnalyticalPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.ExecuteAsync(db, actor, id, input, ct)));
    group.MapGet("/engagements/{id:guid}/analytical-preparation/receipts/{requestId:guid}",
      (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.LookupAsync(db, actor, id, requestId, requestHash, ct)));
    group.MapUiGet("/accounting/evidence", http => ReadAsync(http, (db, actor, ct) => AccountingEvidenceQueueQuery.GetAsync(db, actor, ct)));
    group.MapGet("/accounting/reconciliations/{id:guid}", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReconciliationWorkspaceQuery.GetAsync(db, actor, id, page ?? 0, ct)));
    group.MapGet("/accounting/evidence/{kind}/{id:guid}", (string kind, Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingAnalysisReviewQuery.GetAsync(db, actor, kind, id, page ?? 0, ct)));
    group.MapGet("/accounting/evidence/{kind}/{id:guid}/actions", (string kind, Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.StateAsync(db, actor, kind, id, page ?? 0, ct)));
    group.MapGet("/accounting/evidence/{kind}/{id:guid}/procedure-results", (string kind, Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.ProceduresAsync(db, actor, kind, id, page ?? 0, ct)));
    group.MapPost("/accounting/evidence/{kind}/{id:guid}/preview", (string kind, Guid id, EvidenceActionRequest i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.PreviewAsync(db, actor, kind, id, i, ct)));
    group.MapPost("/accounting/evidence/{kind}/{id:guid}/actions", (string kind, Guid id, EvidenceActionRequest i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.ExecuteAsync(db, actor, kind, id, i, ct)));
    group.MapGet("/accounting/evidence/{kind}/{id:guid}/receipts/{requestId:guid}", (string kind, Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.LookupAsync(db, actor, kind, id, requestId, requestHash, ct)));
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
