using System.Globalization;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ClientManualSettlementHttpInput(Guid CommandId, Guid PeriodId, Guid CounterpartyId,
    string SourceKind, string JournalNumber, string Description, string PostingDate, string CashAccountCode,
    string Amount, string Reference, string EvidenceReference, string PreviewDigest, bool Reviewed);

  private static void MapClientManualSettlementEndpoints(RouteGroupBuilder group)
  {
    static IResult Failure(string? code) => Results.Json(new { code }, statusCode: code == "scope.denied" ? 403 : 400);
    static bool TryRequest(Guid clientId, ClientManualSettlementHttpInput input, out ClientManualSettlementDraftRequest request)
    {
      request = new(input.CommandId, clientId, input.PeriodId, input.CounterpartyId, input.SourceKind,
        input.JournalNumber, input.Description, default, input.CashAccountCode, 0, input.Reference, input.EvidenceReference);
      if (!DateOnly.TryParseExact(input.PostingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
          !decimal.TryParse(input.Amount, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)) return false;
      request = request with { PostingDate = date, Amount = amount };
      return true;
    }

    group.MapGet("/accounting/clients/{clientId:guid}/manual-settlement-options", async (Guid clientId, DateOnly postingDate,
      string sourceKind, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.SettlementOptionsAsync(db, actor, clientId, postingDate, sourceKind, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientManualSettlementOptions>();

    group.MapPost("/accounting/clients/{clientId:guid}/manual-settlements/preview", async (Guid clientId,
      ClientManualSettlementHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!TryRequest(clientId, input, out var request)) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.PreviewSettlementAsync(db, actor, request, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientManualSettlementPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/manual-settlements", async (Guid clientId,
      ClientManualSettlementHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !TryRequest(clientId, input, out var request)) return Failure("request.invalid");
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.CreateSettlementDraftAsync(db, actor, request, input.PreviewDigest, http.RequestAborted);
      return result.Succeeded ? Results.Ok(result.Value) : Failure(result.ErrorCode);
    }).Produces<ClientManualSettlementReceipt>();
  }
}
