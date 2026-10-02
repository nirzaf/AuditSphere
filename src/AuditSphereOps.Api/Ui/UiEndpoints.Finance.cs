using AuditSphereOps.Application.Practice;
using AuditSphereOps.Domain.Shared;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record FiscalCloseInput(string Reason);

  private static void MapFinanceEndpoints(RouteGroupBuilder group)
  {
    group.MapUiGet("/finance", http => ReadAsync(http, async (db, actor, ct) =>
    {
      var r = await FirmFinanceQuery.GetAsync(db, actor, ct);
      return r.Succeeded
        ? CommandResult<object>.Ok(new
          {
            r.Value!.CanClosePeriod,
            Periods = r.Value.Periods.Select(p => new { p.Id, p.PeriodCode, p.Status, p.Revision, p.ClosedAt }),
            Accounts = r.Value.Accounts.Select(a => new { a.Id, a.Code, a.Name, a.AccountType, a.NormalSide, a.PostingAllowed }),
            Postings = r.Value.RecentPostings.Select(p => new { p.Id, p.PostedAt, p.Currency, p.PostedByUserId, p.ReversalOfPostingId }),
          })
        : CommandResult<object>.Fail(r.ErrorCode!, r.Message!);
    }));
    group.MapPost("/finance/periods/{id:guid}/close", (Guid id, FiscalCloseInput i, HttpContext http) =>
      CommandAsync(http, (db, actor, ct) => LedgerService.CloseFiscalPeriodAsync(db, actor, id, i.Reason ?? "", ct)));
    group.MapGet("/finance/invoices/{id:guid}", (Guid id, HttpContext http) => ReadAsync(http, async (db, actor, ct) =>
    {
      var r = await BillingService.GetInvoiceDetailAsync(db, actor, id, ct);
      if (!r.Succeeded || r.Value is null) return CommandResult<object>.Fail(r.ErrorCode ?? ErrorCodes.ScopeDenied, r.Message ?? "Access denied.");
      var v = r.Value;
      return CommandResult<object>.Ok(new
      {
        v.Invoice.Id, v.Invoice.InvoiceNumber, v.Invoice.Currency, v.Invoice.Subtotal, v.Invoice.Tax, v.Invoice.Total, v.Invoice.Revision, v.Invoice.Status,
        v.Invoice.CreatedAt, v.Invoice.PostedAt, Outstanding = v.Balance.Outstanding,
        Lines = v.Lines.Select(l => new { l.Description, l.Quantity, l.UnitPrice, l.LineTotal }),
        Allocations = v.Allocations.Select(a => new { a.ReceiptId, a.CreatedAt, a.Amount }),
        CanAct = actor.Roles.Any(x => x is "FinanceManager" or "FinanceReviewer"),
      });
    }));
    foreach (var (action, command) in new (string, Func<Application.Operations.IAuditSphereDbContext, Application.Abstractions.ActorContext, Guid, CancellationToken, Task<CommandResult>>)[]
    {
      ("approve", (db, a, id, ct) => BillingService.ApproveInvoiceAsync(db, a, id, ct)),
      ("post", (db, a, id, ct) => BillingService.PostInvoiceAsync(db, a, id, ct)),
      ("send", (db, a, id, ct) => BillingService.SendInvoiceAsync(db, a, id, ct)),
    })
      group.MapPost("/finance/invoices/{id:guid}/" + action, (Guid id, HttpContext http) => CommandAsync(http, (db, actor, ct) => command(db, actor, id, ct)));
  }
}
