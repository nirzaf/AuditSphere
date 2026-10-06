using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record SalesDraftLineHttpInput(string AccountCode, string Description, string Quantity, string UnitPrice, string Discount);
  public sealed record SalesDraftHttpInput(Guid CommandId, Guid? InvoiceId, string ExpectedRevision, string DraftReference,
    string SourceReference, Guid PeriodId, Guid CustomerId, string DocumentDate, string AccountingDate, string SupplyDate,
    string DueDate, string Currency, ClientInvoiceMoneyPolicy Policy, IReadOnlyList<SalesDraftLineHttpInput> Lines, string EvidenceReference, bool Reviewed);
  private static bool SalesDate(string? value, out DateOnly date) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
  private static void MapClientSalesInvoiceDraftEndpoints(RouteGroupBuilder group)
  {
    group.MapPost("/accounting/clients/{clientId:guid}/sales-invoice-drafts", async (Guid clientId, SalesDraftHttpInput input,
      HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.Policy is null || input.Lines is null || input.Lines.Count is < 1 or > 100 ||
          !long.TryParse(input.ExpectedRevision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 0 || input.ExpectedRevision != revision.ToString(CultureInfo.InvariantCulture) ||
          !SalesDate(input.DocumentDate, out var document) || !SalesDate(input.AccountingDate, out var accounting) || !SalesDate(input.SupplyDate, out var supply) || !SalesDate(input.DueDate, out var due))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      var lines = new List<ClientInvoiceLineInput>();
      foreach (var line in input.Lines)
      {
        if (line is null || !AccountingAmount(line.Quantity, out var quantity) || !AccountingAmount(line.UnitPrice, out var price) || !AccountingAmount(line.Discount, out var discount)) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        lines.Add(new(line.Description, line.AccountCode, quantity, price, discount, "NONE", []));
      }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceDraftWorkspace.SaveAsync(db, actor, clientId,
        new(input.CommandId, input.InvoiceId, revision, input.DraftReference, input.SourceReference, input.PeriodId, input.CustomerId,
          document, accounting, supply, due, input.Currency, input.Policy, lines, input.EvidenceReference), http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).Produces<ClientSalesInvoiceDraftView>();
    group.MapGet("/accounting/clients/{clientId:guid}/sales-invoice-drafts/{invoiceId:guid}", async (Guid clientId, Guid invoiceId, string? revision,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      long? number = null;
      if (revision is not null) { if (!PartyRevision(revision, out var parsed)) return Results.Json(new { code = "request.invalid" }, statusCode: 400); number = parsed; }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceDraftWorkspace.GetAsync(db, actor, clientId, invoiceId, number, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    }).Produces<ClientSalesInvoiceDraftView>();
    group.MapGet("/accounting/clients/{clientId:guid}/sales-invoice-draft-requests/{commandId:guid}", async (Guid clientId, Guid commandId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientSalesInvoiceDraftWorkspace.GetByCommandAsync(db, actor, clientId, commandId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "command.receipt-not-found" ? 404 : result.ErrorCode == "scope.denied" ? 403 : 400);
    }).Produces<ClientSalesInvoiceDraftView>();
  }
}
