using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record EndOfServiceTreatmentInput(string MeasurementTreatment, string AccountantName, string AccountantCredential,
    Guid ProvisionAccountId, Guid ExpenseAccountId, long ExpectedVersion, bool Reviewed);
  public sealed record EndOfServiceConfirmationInput(string Note, bool Reviewed);
  public sealed record EndOfServiceAccrualForm(Guid PeriodId, Guid RequestId, string Amount,
    DateOnly CalculationDate, string Method, string Inputs, string Reason, bool Reviewed, IFormFile? Evidence);

  /// <summary>End-of-service provision: recorded and confirmed treatment, then manual accruals through the journal maker and checker.</summary>
  private static void MapEndOfServiceEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/finance/end-of-service", http => ReadAsync(http, (db, actor, ct) => EndOfServiceWorkspaceQuery.GetAsync(db, actor, ct)))
      .Produces<EndOfServiceWorkspace>().Produces<UiFailureResponse>(400).Produces<UiFailureResponse>(403);
    group.MapPost("/finance/end-of-service/treatment", (EndOfServiceTreatmentInput input, HttpContext http) =>
      !input.Reviewed
        ? Task.FromResult(Invalid("Confirm that you reviewed the treatment before recording it."))
        : CommandAsync(http, (db, actor, ct) => LedgerService.RecordEndOfServiceTreatmentAsync(db, actor,
            new RecordEndOfServiceTreatmentRequest(input.MeasurementTreatment ?? "", input.AccountantName ?? "", input.AccountantCredential ?? "",
              input.ProvisionAccountId, input.ExpenseAccountId, input.ExpectedVersion), ct)))
      .Produces<UiValueResponse<Guid>>().Produces<UiFailureResponse>(400).Produces<UiFailureResponse>(403).Produces<UiFailureResponse>(409);
    group.MapPost("/finance/end-of-service/treatment/{id:guid}/confirm", (Guid id, EndOfServiceConfirmationInput input, HttpContext http) =>
      !input.Reviewed
        ? Task.FromResult(Invalid("Confirm that you reviewed the treatment before confirming it."))
        : CommandAsync(http, (db, actor, ct) => LedgerService.ConfirmEndOfServiceTreatmentAsync(db, actor, id, input.Note ?? "", ct)))
      .Produces<UiValueResponse<bool>>().Produces<UiFailureResponse>(400).Produces<UiFailureResponse>(403).Produces<UiFailureResponse>(409);
    group.MapUiPost("/finance/end-of-service/accruals", http => CommandAsync(http, async (db, actor, ct) =>
    {
      if (!http.Request.HasFormContentType) return AuditSphereOps.Domain.Shared.CommandResult<Guid>.Fail("request.invalid", "Send the accrual and any supporting document as a multipart form.");
      var form = await http.Request.ReadFormAsync(ct);
      if (!Guid.TryParse(form["periodId"], out var periodId) || !Guid.TryParse(form["requestId"], out var requestId) ||
          !TryDecimal(form["amount"], out var amount) || !TryDate(form["calculationDate"], out var calculationDate) ||
          form["reviewed"] != "true")
        return AuditSphereOps.Domain.Shared.CommandResult<Guid>.Fail("request.invalid", "Period, amount, calculation date, request reference and the reviewed confirmation are required.");
      var evidenceFile = form.Files.GetFile("evidence");
      var evidence = await ReadUploadAsync(http, "evidence", LedgerService.MaxJournalEvidenceBytes);
      if (evidenceFile is not null && evidence is null)
        return AuditSphereOps.Domain.Shared.CommandResult<Guid>.Fail("request.invalid", "The supporting document must be between 1 byte and 5 MB.");
      return await LedgerService.CreateEndOfServiceAccrualDraftAsync(db, actor,
        new CreateEndOfServiceAccrualRequest(periodId, amount, form["method"].ToString(), form["inputs"].ToString(), calculationDate,
          form["reason"].ToString(), requestId, evidence?.Name, evidence?.ContentType, evidence?.Content), ct);
    })).WithMetadata(new AuditSphereOps.Api.HttpBoundary.ApiRateClassAttribute(AuditSphereOps.Api.HttpBoundary.ApiRateClass.FileUpload))
      .Accepts<EndOfServiceAccrualForm>("multipart/form-data")
      .Produces<UiValueResponse<Guid>>().Produces<UiFailureResponse>(400).Produces<UiFailureResponse>(403);
  }
}
