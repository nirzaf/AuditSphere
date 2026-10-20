using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.HttpBoundary;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record MappingApprovalInput(long Version, string? Revision = null, bool Reviewed = false);
  public sealed record MappingReviewInput(string Revision, bool Reviewed);

  private static void MapAccountingDetailEndpoints(RouteGroupBuilder group)
  {
    MapMappingDraftEndpoints(group);
    group.MapGet("/accounting/mappings/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => MappingWorkbenchQuery.GetAsync(db, actor, id, ct)))
      .Produces<MappingWorkbench>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/mappings/{id:guid}/approval", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => MappingApprovalWorkspace.GetAsync(db, actor, id, ct)))
      .Produces<MappingApprovalReview>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/mappings/{id:guid}/approval", (Guid id, MappingReviewInput? input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => MappingApprovalWorkspace.ApproveAsync(db, actor, id,
        input?.Revision ?? "", input?.Reviewed == true, ct)))
      .Produces<UiValueResponse<MappingApprovalReview>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapPost("/accounting/mappings/{id:guid}/approve", (Guid id, MappingApprovalInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FinancialStatementService.ApproveMappingAsync(db, actor, id, i.Version, ct, i.Revision ?? "", i.Reviewed)))
      .Produces<UiValueResponse<bool>>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/periods/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, (db, actor, ct) => AccountingPeriodRecordQuery.GetAsync(db, actor, id, ct)))
      .Produces<AccountingPeriodRecord>(StatusCodes.Status200OK, "application/json")
      .Produces<UiFailureResponse>(StatusCodes.Status403Forbidden, "application/json")
      .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");
    group.MapGet("/accounting/periods/{id:guid}/evidence", (Guid id, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.IndexAsync(db, actor, id, ct)))
      .WithUiResponse<AccountingPeriodEvidenceIndex>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/period-references", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.PeriodReferencesAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodReferenceEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/books", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.BooksAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodBookEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/trial-balances", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.TrialBalancesAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodTrialBalanceEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/general-ledgers", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.GeneralLedgersAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodGeneralLedgerEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/completeness", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.CompletenessAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodCompletenessEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/reconciliations", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.ReconciliationsAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodReconciliationEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/specialist-schedules", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.SpecialistSchedulesAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodSpecialistScheduleEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/pbc", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.PbcAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodPbcEvidence>>(includeConflict: true);
    group.MapGet("/accounting/periods/{id:guid}/evidence/tasks", (Guid id, int? page, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => AccountingPeriodEvidenceQuery.TasksAsync(db, actor, id, page ?? 1, ct)))
      .WithUiResponse<AccountingPeriodEvidencePage<PeriodTaskEvidence>>(includeConflict: true);
  }
}
