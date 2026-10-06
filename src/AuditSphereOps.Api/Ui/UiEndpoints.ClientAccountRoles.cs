using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;
public static partial class UiEndpoints
{
  public sealed record ClientAccountRoleHttpInput(ClientAccountRoleRequest Proposal, bool Reviewed);
  public sealed record ClientAccountRoleReviewHttpInput(string Decision, string Reason, bool Reviewed);
  private static void MapClientAccountRoleEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{clientId:guid}/account-roles", async (Guid clientId, int? page, string? accountSearch, HttpContext http, TrustedActorResolver resolver, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor = await resolver.ResolveAsync(http.User,http.RequestAborted); if(actor is null) return Results.Json(new {code="session.unavailable"},statusCode:401);
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientAccountRoleWorkspace.ListAsync(db,actor,clientId,page??0,accountSearch??"",http.RequestAborted);
      if(await resolver.ResolveAsync(http.User,http.RequestAborted) is null) return Results.Json(new {code="session.unavailable"},statusCode:401);
      return result.Succeeded?Results.Ok(result.Value):Results.Json(new {code=result.ErrorCode},statusCode:result.ErrorCode=="scope.denied"?403:400);
    }).Produces<ClientAccountRoleList>();
    group.MapPost("/accounting/clients/{clientId:guid}/account-roles", async (Guid clientId, ClientAccountRoleHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);if(actor is null)return Results.Json(new {code="session.unavailable"},statusCode:401);
      try {await csrf.ValidateRequestAsync(http);}catch(AntiforgeryValidationException){return Results.Json(new {code="csrf.invalid"},statusCode:403);}
      if(!input.Reviewed||input.Proposal is null)return Results.Json(new {code="request.invalid"},statusCode:400);
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);var result=await ClientAccountRoleWorkspace.ProposeAsync(db,actor,clientId,input.Proposal,http.RequestAborted);
      return result.Succeeded?Results.Ok(new {id=result.Value}):Results.Json(new {code=result.ErrorCode},statusCode:result.ErrorCode=="scope.denied"?403:400);
    });
    group.MapPost("/accounting/clients/{clientId:guid}/account-roles/{configurationId:guid}/review", async (Guid clientId, Guid configurationId, ClientAccountRoleReviewHttpInput input, HttpContext http, TrustedActorResolver resolver, IAntiforgery csrf, IDbContextFactory<AuditSphereDbContext> factory) => {
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);if(actor is null)return Results.Json(new {code="session.unavailable"},statusCode:401);
      try {await csrf.ValidateRequestAsync(http);}catch(AntiforgeryValidationException){return Results.Json(new {code="csrf.invalid"},statusCode:403);}
      if(!input.Reviewed)return Results.Json(new {code="request.invalid"},statusCode:400);
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);var result=await ClientAccountRoleWorkspace.ReviewAsync(db,actor,clientId,configurationId,input.Decision,input.Reason,http.RequestAborted);
      return result.Succeeded?Results.Ok(new {saved=true}):Results.Json(new {code=result.ErrorCode},statusCode:result.ErrorCode=="scope.denied"?403:400);
    });
  }
}
