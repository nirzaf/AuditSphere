using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record FirmExpenseReviewInput(bool Approve, string? Comment);

  private static void MapFirmBooksEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/finance/books", http => ReadAsync(http, (db, actor, ct) => FirmBooksWorkspaceQuery.GetAsync(db, actor, ct)));
    group.MapGet("/finance/books/trial-balance", (string from, string to, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FirmExpenseService.TrialBalanceAsync(db, actor, from.Trim(), to.Trim(), ct)));
    group.MapUiPost("/finance/books/expenses", async http =>
    {
      if (!http.Request.HasFormContentType) return Invalid("Send the expense as a form with its source document.");
      var form = await http.Request.ReadFormAsync(http.RequestAborted);
      if (!TryDate(form["expenseDate"], out var date) || !TryDecimal(form["amount"], out var amount) ||
          !Guid.TryParse(form["expenseAccountId"], out var expense) || !Guid.TryParse(form["paymentAccountId"], out var payment) ||
          !Guid.TryParse(form["requestId"], out var requestId) || string.IsNullOrWhiteSpace(form["requestHash"]))
        return Invalid("Date, amount, both accounts and the exact expense request reference are required.");
      var evidence = await ReadUploadAsync(http, "evidence", FirmExpenseService.MaxEvidenceBytes);
      if (evidence is null) return Invalid("Attach a source document up to 5 MB.");
      return await CommandAsync(http, (db, actor, ct) => FirmExpenseService.RecordAsync(db, actor, new RecordFirmExpenseRequest(date, form["category"].ToString(),
        form["payee"].ToString(), form["description"].ToString(), amount, form["currency"].ToString(), expense, payment,
        evidence.Value.Name, evidence.Value.ContentType, evidence.Value.Content, requestId, form["requestHash"].ToString()), ct));
    });
    group.MapGet("/finance/books/expenses/receipts/{requestId:guid}", (Guid requestId, string requestHash, HttpContext http) =>
      ReadAsync(http, (db, actor, ct) => FirmExpenseService.LookupCreationAsync(db, actor, requestId, requestHash, ct)));
    group.MapPost("/finance/books/expenses/{id:guid}/submit", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FirmExpenseService.SubmitAsync(db, actor, id, ct)));
    group.MapPost("/finance/books/expenses/{id:guid}/review", (Guid id, FirmExpenseReviewInput input, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FirmExpenseService.ReviewAsync(db, actor, id, input.Approve, input.Comment, ct)));
    group.MapPost("/finance/books/expenses/{id:guid}/post", (Guid id, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => FirmExpenseService.PostAsync(db, actor, id, ct)));
  }
}
