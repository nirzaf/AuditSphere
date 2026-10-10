using AuditSphereOps.Application.Practice;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record EndOfServiceTreatmentInput(string MeasurementTreatment, string AccountantName, string AccountantCredential,
    Guid ProvisionAccountId, Guid ExpenseAccountId, long ExpectedVersion, bool Reviewed);
  public sealed record EndOfServiceConfirmationInput(string Note, bool Reviewed);

  /// <summary>End-of-service provision: recorded and confirmed treatment, then manual accruals through the journal maker and checker.</summary>
  private static void MapEndOfServiceEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/finance/end-of-service", http => ReadAsync(http, (db, actor, ct) => EndOfServiceWorkspaceQuery.GetAsync(db, actor, ct)));
    group.MapPost("/finance/end-of-service/treatment", (EndOfServiceTreatmentInput input, HttpContext http) =>
      !input.Reviewed
        ? Task.FromResult(Invalid("Confirm that you reviewed the treatment before recording it."))
        : CommandAsync(http, (db, actor, ct) => LedgerService.RecordEndOfServiceTreatmentAsync(db, actor,
            new RecordEndOfServiceTreatmentRequest(input.MeasurementTreatment ?? "", input.AccountantName ?? "", input.AccountantCredential ?? "",
              input.ProvisionAccountId, input.ExpenseAccountId, input.ExpectedVersion), ct)));
    group.MapPost("/finance/end-of-service/treatment/{id:guid}/confirm", (Guid id, EndOfServiceConfirmationInput input, HttpContext http) =>
      !input.Reviewed
        ? Task.FromResult(Invalid("Confirm that you reviewed the treatment before confirming it."))
        : CommandAsync(http, (db, actor, ct) => LedgerService.ConfirmEndOfServiceTreatmentAsync(db, actor, id, input.Note ?? "", ct)));
    group.MapUiPost("/finance/end-of-service/accruals", async http =>
    {
      if (!http.Request.HasFormContentType) return Invalid("Send the accrual and any supporting document as a multipart form.");
      var form = await http.Request.ReadFormAsync(http.RequestAborted);
      if (!Guid.TryParse(form["periodId"], out var periodId) || !Guid.TryParse(form["requestId"], out var requestId) ||
          !TryDecimal(form["amount"], out var amount) || !TryDate(form["calculationDate"], out var calculationDate) ||
          form["reviewed"] != "true")
        return Invalid("Period, amount, calculation date, request reference and the reviewed confirmation are required.");
      var evidenceFile = form.Files.GetFile("evidence");
      var evidence = await ReadUploadAsync(http, "evidence", LedgerService.MaxJournalEvidenceBytes);
      if (evidenceFile is not null && evidence is null)
        return Invalid("The supporting document must be between 1 byte and 5 MB.");
      return await CommandAsync(http, (db, actor, ct) => LedgerService.CreateEndOfServiceAccrualDraftAsync(db, actor,
        new CreateEndOfServiceAccrualRequest(periodId, amount, form["method"].ToString(), form["inputs"].ToString(), calculationDate,
          form["reason"].ToString(), requestId, evidence?.Name, evidence?.ContentType, evidence?.Content), ct));
    });
  }
}
