using AuditSphereOps.Application.Accounting;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record RestatementInput(Guid PeriodId, Guid OriginalPackageId, Guid RevisedPackageId, string RevisedBasis, string Reason, string EvidenceReference,
    string ChangeType, string? AffectedPeriods);

  private static void MapPeriodMaintenanceEndpoints(RouteGroupBuilder group)
  {
    const string maintenance = "/accounting/period-maintenance/clients/{clientId:guid}";
    UiEndpointContractMetadata.WithUiResponse<PeriodMaintenanceContext>(group.MapGet(maintenance + "/context", (Guid clientId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PeriodMaintenanceWorkspace.ContextAsync(db, actor, clientId, ct))), includeBadRequest: false);
    UiEndpointContractMetadata.WithUiResponse<PeriodMaintenanceState>(group.MapGet(maintenance + "/state", (Guid clientId, string action, Guid? periodId, Guid? restatementId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PeriodMaintenanceWorkspace.StateAsync(db, actor, clientId, action, periodId, restatementId, ct))));
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<PeriodMaintenancePreview>>(group.MapPost(maintenance + "/preview", (Guid clientId, PeriodMaintenanceRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PeriodMaintenanceWorkspace.PreviewAsync(db, actor, clientId, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<PeriodMaintenanceReceiptView>>(group.MapPost(maintenance + "/actions", (Guid clientId, PeriodMaintenanceRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => PeriodMaintenanceWorkspace.ExecuteAsync(db, actor, clientId, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<PeriodMaintenanceLookup>(group.MapGet(maintenance + "/receipts/{requestId:guid}", (Guid clientId, Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PeriodMaintenanceWorkspace.LookupAsync(db, actor, clientId, requestId, requestHash, ct))), includeConflict: true);
    MapValuationPreparationEndpoints(group);
    const string reconciliation = "/engagements/{id:guid}/reconciliation-preparation";
    UiEndpointContractMetadata.WithUiResponse<ReconciliationPreparationContext>(group.MapGet(reconciliation, (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReconciliationPreparationWorkspace.ContextAsync(db, actor, id, ct))), includeBadRequest: false);
    UiEndpointContractMetadata.WithUiResponse<ReconciliationPreparationState>(group.MapGet(reconciliation + "/state", (Guid id, string? sourceKind, Guid sourceId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReconciliationPreparationWorkspace.StateAsync(db, actor, id, sourceKind, sourceId, ct))));
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<ReconciliationPreparationPreview>>(group.MapPost(reconciliation + "/preview", (Guid id, ReconciliationPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReconciliationPreparationWorkspace.PreviewAsync(db, actor, id, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<ReconciliationPreparationReceipt>>(group.MapPost(reconciliation, (Guid id, ReconciliationPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ReconciliationPreparationWorkspace.ExecuteAsync(db, actor, id, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<ReconciliationPreparationLookup>(group.MapGet(reconciliation + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReconciliationPreparationWorkspace.LookupAsync(db, actor, id, requestId, requestHash, ct))),
      includeBadRequest: false, includeConflict: true);

    const string specialist = "/engagements/{id:guid}/specialist-preparation";
    UiEndpointContractMetadata.WithUiResponse<SpecialistPreparationContext>(group.MapGet(specialist, (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => SpecialistSchedulePreparationWorkspace.ContextAsync(db, actor, id, ct))), includeBadRequest: false);
    UiEndpointContractMetadata.WithUiResponse<FinancialScheduleSources>(group.MapGet(specialist + "/source-lines", (Guid id, Guid periodId, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FinancialScheduleSourceQuery.GetAsync(db, actor, id, periodId, page ?? 1, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<SpecialistPreparationState>(group.MapGet(specialist + "/state", (Guid id, Guid periodId, string? area, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => SpecialistSchedulePreparationWorkspace.StateAsync(db, actor, id, periodId, area, ct))));
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<SpecialistPreparationPreview>>(group.MapPost(specialist + "/preview", (Guid id, SpecialistPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => SpecialistSchedulePreparationWorkspace.PreviewAsync(db, actor, id, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<SpecialistPreparationReceipt>>(group.MapPost(specialist, (Guid id, SpecialistPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => SpecialistSchedulePreparationWorkspace.ExecuteAsync(db, actor, id, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<SpecialistPreparationLookup>(group.MapGet(specialist + "/receipts/{requestId:guid}", (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => SpecialistSchedulePreparationWorkspace.LookupAsync(db, actor, id, requestId, requestHash, ct))),
      includeBadRequest: false, includeConflict: true);

    UiEndpointContractMetadata.WithUiResponse<AnalyticalPreparationContext>(group.MapGet("/engagements/{id:guid}/analytical-preparation", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.ContextAsync(db, actor, id, ct))));
    UiEndpointContractMetadata.WithUiResponse<AnalyticalPreparationState>(group.MapGet("/engagements/{id:guid}/analytical-preparation/state", (Guid id, Guid periodId,
      Guid? comparisonPeriodId, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.StateAsync(db, actor, id, periodId, comparisonPeriodId, ct))));
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<AnalyticalPreparationPreview>>(group.MapPost("/engagements/{id:guid}/analytical-preparation/preview", (Guid id,
      AnalyticalPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.PreviewAsync(db, actor, id, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<AnalyticalPreparationReceipt>>(group.MapPost("/engagements/{id:guid}/analytical-preparation", (Guid id,
      AnalyticalPreparationRequest input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.ExecuteAsync(db, actor, id, input, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<AnalyticalPreparationLookup>(group.MapGet("/engagements/{id:guid}/analytical-preparation/receipts/{requestId:guid}",
      (Guid id, Guid requestId, string? requestHash, HttpContext http) =>
        ReadAsync(http, (db, actor, ct) => AnalyticalReviewPreparationWorkspace.LookupAsync(db, actor, id, requestId, requestHash, ct))),
      includeBadRequest: false, includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<IReadOnlyList<AccountingEvidenceRow>>(group.MapUiGet("/accounting/evidence",
      http => ReadAsync(http, (db, actor, ct) => AccountingEvidenceQueueQuery.GetAsync(db, actor, ct))), includeBadRequest: false);
    UiEndpointContractMetadata.WithUiResponse<ReconciliationReview>(group.MapGet("/accounting/reconciliations/{id:guid}", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => ReconciliationWorkspaceQuery.GetAsync(db, actor, id, page ?? 0, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<AccountingAnalysisReview>(group.MapGet("/accounting/evidence/{kind}/{id:guid}", (string kind, Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingAnalysisReviewQuery.GetAsync(db, actor, kind, id, page ?? 0, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<EvidenceActionState>(group.MapGet("/accounting/evidence/{kind}/{id:guid}/actions", (string kind, Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.StateAsync(db, actor, kind, id, page ?? 0, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<EvidenceProcedurePage>(group.MapGet("/accounting/evidence/{kind}/{id:guid}/procedure-results", (string kind, Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.ProceduresAsync(db, actor, kind, id, page ?? 0, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<EvidenceActionPreview>>(group.MapPost("/accounting/evidence/{kind}/{id:guid}/preview", (string kind, Guid id, EvidenceActionRequest i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.PreviewAsync(db, actor, kind, id, i, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<EvidenceActionReceipt>>(group.MapPost("/accounting/evidence/{kind}/{id:guid}/actions", (string kind, Guid id, EvidenceActionRequest i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.ExecuteAsync(db, actor, kind, id, i, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<EvidenceActionLookup>(group.MapGet("/accounting/evidence/{kind}/{id:guid}/receipts/{requestId:guid}", (string kind, Guid id, Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingEvidenceWorkspace.LookupAsync(db, actor, kind, id, requestId, requestHash, ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<PeriodMaintenanceOverview>(group.MapUiGet("/accounting/period-maintenance",
      http => ReadAsync(http, (db, actor, ct) => PeriodMaintenanceQuery.OverviewAsync(db, actor, ct))), includeBadRequest: false);
    UiEndpointContractMetadata.WithUiResponse<IReadOnlyList<MaintenanceClosedPeriod>>(group.MapGet("/accounting/period-maintenance/clients/{id:guid}", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => PeriodMaintenanceQuery.ClosedPeriodsAsync(db, actor, id, ct))), includeBadRequest: false);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<Guid>>(group.MapPost("/accounting/clients/{id:guid}/restatements", (Guid id, RestatementInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ClientAccountingService.CreatePeriodRestatementAsync(db, actor, new CreatePeriodRestatementRequest(id, i.PeriodId,
        i.OriginalPackageId, i.RevisedPackageId, i.RevisedBasis ?? "", i.Reason ?? "", i.EvidenceReference ?? "", i.ChangeType ?? "", i.AffectedPeriods ?? ""), ct))), includeConflict: true);
    UiEndpointContractMetadata.WithUiResponse<UiValueResponse<bool>>(group.MapPost("/accounting/restatements/{id:guid}/approve", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => ClientAccountingService.ApprovePeriodRestatementAsync(db, actor, id, ct))), includeConflict: true);
  }
}
