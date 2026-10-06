using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record CounterpartyAmendmentHttpInput(string ExpectedRevision, string DisplayName, string Address,
    string TaxIdentifier, string ContactDetails, string PaymentTerms, string Reason, bool Reviewed);
  public sealed record CounterpartyReviewHttpInput(string ExpectedRevision, string Decision, string Reason, bool Reviewed);
  private static bool PartyRevision(string? value, out long revision) => long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out revision) && revision > 0 && value == revision.ToString(CultureInfo.InvariantCulture);
  private static void MapClientCounterpartyAmendmentEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{clientId:guid}/counterparties/{partyId:guid}", async (Guid clientId, Guid partyId,
      HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientBookkeepingCounterpartyWorkspace.HistoryAsync(db, actor, clientId, partyId, http.RequestAborted);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return result.Succeeded ? Results.Ok(result.Value) : Results.Json(new { code = result.ErrorCode }, statusCode: 403);
    }).Produces<CounterpartyHistory>();
    group.MapPost("/accounting/clients/{clientId:guid}/counterparties/{partyId:guid}/amendments", async (Guid clientId, Guid partyId,
      CounterpartyAmendmentHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !PartyRevision(input.ExpectedRevision, out var revision)) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientBookkeepingCounterpartyWorkspace.ProposeAmendmentAsync(db, actor, clientId, partyId,
        new(revision, input.DisplayName, input.Address, input.TaxIdentifier, input.ContactDetails, input.PaymentTerms, input.Reason), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    });
    group.MapPost("/accounting/clients/{clientId:guid}/counterparties/{partyId:guid}/amendments/{amendmentId:guid}/review", async (Guid clientId, Guid partyId,
      Guid amendmentId, CounterpartyReviewHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf,
      IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); } catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !PartyRevision(input.ExpectedRevision, out var revision)) return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientBookkeepingCounterpartyWorkspace.ReviewAmendmentAsync(db, actor, clientId, partyId, amendmentId,
        revision, input.Decision, input.Reason, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { decided = true }) : Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    });
  }
}
