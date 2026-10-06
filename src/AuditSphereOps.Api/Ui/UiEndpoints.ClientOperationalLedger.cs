using System.Globalization;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record ClientOperationalJournalLineHttpInput(string AccountCode, string Description, string Debit, string Credit);
  public sealed record ClientOperationalJournalCreateHttpInput(Guid PeriodId, string JournalNumber, string Description,
    string PostingDate, IReadOnlyList<ClientOperationalJournalLineHttpInput> Lines, bool Reviewed);
  public sealed record ClientOperationalJournalSubmitHttpInput(string Revision, bool Reviewed);
  public sealed record ClientOperationalJournalPostHttpInput(string Revision, string Reason, bool Reviewed);

  private static void MapClientOperationalLedgerEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{clientId:guid}/operational-ledger", async (Guid clientId, Guid periodId,
      int page, int pageSize, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalGeneralLedgerWorkspace.GetAsync(db, actor, clientId, periodId, page, pageSize, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });

    group.MapPost("/accounting/clients/{clientId:guid}/operational-journals", async (Guid clientId,
      ClientOperationalJournalCreateHttpInput input, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || input.Lines is null || input.Lines.Count is < 2 or > 100 ||
          !DateOnly.TryParseExact(input.PostingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var postingDate))
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      var lines = new List<ClientOperationalJournalLineInput>(input.Lines.Count);
      foreach (var line in input.Lines)
      {
        if (!AccountingAmount(line.Debit, out var debit) || !AccountingAmount(line.Credit, out var credit))
          return Results.Json(new { code = "request.invalid" }, statusCode: 400);
        lines.Add(new(line.AccountCode, line.Description, debit, credit));
      }
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.CreateDraftAsync(db, actor,
        new(clientId, input.PeriodId, input.JournalNumber, input.Description, postingDate, lines), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { id = result.Value }) :
        Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    });

    group.MapGet("/accounting/clients/{clientId:guid}/operational-journals/{journalId:guid}", async (Guid clientId,
      Guid journalId, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.GetAsync(db, actor, clientId, journalId, http.RequestAborted);
      if (!result.Succeeded) return Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
      if (await resolver.ResolveAsync(http.User, http.RequestAborted) is null)
        return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      return Results.Ok(result.Value);
    });

    group.MapPost("/accounting/clients/{clientId:guid}/operational-journals/{journalId:guid}/submit", async (Guid clientId,
      Guid journalId, ClientOperationalJournalSubmitHttpInput input, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !long.TryParse(input.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.SubmitAsync(db, actor, clientId, journalId, revision, http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { accepted = true }) :
        Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    });

    group.MapPost("/accounting/clients/{clientId:guid}/operational-journals/{journalId:guid}/post", async (Guid clientId,
      Guid journalId, ClientOperationalJournalPostHttpInput input, HttpContext http, TrustedActorResolver resolver,
      IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) =>
    {
      var actor = await resolver.ResolveAsync(http.User, http.RequestAborted);
      if (actor is null) return Results.Json(new { code = "session.unavailable" }, statusCode: 401);
      try { await csrf.ValidateRequestAsync(http); }
      catch (AntiforgeryValidationException) { return Results.Json(new { code = "csrf.invalid" }, statusCode: 403); }
      if (!input.Reviewed || !long.TryParse(input.Revision, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) || revision < 1)
        return Results.Json(new { code = "request.invalid" }, statusCode: 400);
      await using var db = await factory.CreateDbContextAsync(http.RequestAborted);
      var result = await ClientOperationalLedgerWorkspace.ReviewAndPostAsync(db, actor, clientId, journalId,
        new(revision, "APPROVE", input.Reason), http.RequestAborted);
      return result.Succeeded ? Results.Ok(new { posted = true }) :
        Results.Json(new { code = result.ErrorCode }, statusCode: result.ErrorCode == "scope.denied" ? 403 : 400);
    });
  }
}
