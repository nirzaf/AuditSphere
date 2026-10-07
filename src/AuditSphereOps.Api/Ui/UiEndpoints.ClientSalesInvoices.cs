using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Infrastructure.Persistence;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace AuditSphereOps.Api.Ui;

public static partial class UiEndpoints
{
  public sealed record SalesSubmitHttpInput(Guid CommandId,string ExpectedDraftRevision,Guid ReceivableRoleId,string NoTaxReason,
    Guid? SourceReceiptId,string SourceBasis,string PreviewDigest,bool Reviewed);
  public sealed record SalesReviewHttpInput(Guid CommandId,Guid SubmissionId,string Decision,string Reason,string PreviewDigest,bool Reviewed);
  private static IResult SalesFailure(string? code)=>Results.Json(new{code},statusCode:code=="scope.denied"?403:code=="command.receipt-not-found"?404:400);
  private static void MapClientSalesInvoiceEndpoints(RouteGroupBuilder group)
  {
    group.MapGet("/accounting/clients/{clientId:guid}/sales-invoices/{invoiceId:guid}/lifecycle",async(Guid clientId,Guid invoiceId,
      HttpContext http,TrustedActorResolver resolver,IDbContextFactory<AuditSphereDbContext> factory)=>{
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);
      if(actor is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientSalesInvoiceWorkflow.GetLifecycleAsync(db,actor,clientId,invoiceId,http.RequestAborted);
      if(await resolver.ResolveAsync(http.User,http.RequestAborted) is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      return result.Succeeded?Results.Ok(result.Value):SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesInvoiceLifecycleView>();

    group.MapPost("/accounting/clients/{clientId:guid}/sales-invoices/{invoiceId:guid}/preview",async(Guid clientId,Guid invoiceId,SalesSubmitHttpInput input,
      HttpContext http,TrustedActorResolver resolver,IAntiforgery csrf,IDbContextFactory<AuditSphereDbContext> factory)=>{
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);
      if(actor is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      try{await csrf.ValidateRequestAsync(http);}catch(AntiforgeryValidationException){return Results.Json(new{code="csrf.invalid"},statusCode:403);}
      if(!PartyRevision(input.ExpectedDraftRevision,out var revision))return SalesFailure("request.invalid");
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientSalesInvoiceWorkflow.PreviewAsync(db,actor,clientId,new(input.CommandId,invoiceId,revision,input.ReceivableRoleId,input.NoTaxReason,input.SourceReceiptId,input.SourceBasis,input.PreviewDigest),http.RequestAborted);
      return result.Succeeded?Results.Ok(result.Value):SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesInvoicePreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/sales-invoices/{invoiceId:guid}/submit",async(Guid clientId,Guid invoiceId,SalesSubmitHttpInput input,
      HttpContext http,TrustedActorResolver resolver,IAntiforgery csrf,IDbContextFactory<AuditSphereDbContext> factory)=>{
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);
      if(actor is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      try{await csrf.ValidateRequestAsync(http);}catch(AntiforgeryValidationException){return Results.Json(new{code="csrf.invalid"},statusCode:403);}
      if(!input.Reviewed||!PartyRevision(input.ExpectedDraftRevision,out var revision))return SalesFailure("request.invalid");
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientSalesInvoiceWorkflow.SubmitAsync(db,actor,clientId,new(input.CommandId,invoiceId,revision,input.ReceivableRoleId,input.NoTaxReason,input.SourceReceiptId,input.SourceBasis,input.PreviewDigest),http.RequestAborted);
      return result.Succeeded?Results.Ok(result.Value):SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesInvoiceCommandReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/sales-invoice-submissions/{submissionId:guid}/preview",async(Guid clientId,Guid submissionId,
      HttpContext http,TrustedActorResolver resolver,IDbContextFactory<AuditSphereDbContext> factory)=>{
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);
      if(actor is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientSalesInvoiceWorkflow.ReviewPreviewAsync(db,actor,clientId,submissionId,http.RequestAborted);
      if(await resolver.ResolveAsync(http.User,http.RequestAborted) is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      return result.Succeeded?Results.Ok(result.Value):SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesInvoiceReviewPreview>();

    group.MapPost("/accounting/clients/{clientId:guid}/sales-invoice-reviews",async(Guid clientId,SalesReviewHttpInput input,
      HttpContext http,TrustedActorResolver resolver,IAntiforgery csrf,IDbContextFactory<AuditSphereDbContext> factory)=>{
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);
      if(actor is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      try{await csrf.ValidateRequestAsync(http);}catch(AntiforgeryValidationException){return Results.Json(new{code="csrf.invalid"},statusCode:403);}
      if(!input.Reviewed)return SalesFailure("request.invalid");
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientSalesInvoiceWorkflow.ReviewAsync(db,actor,clientId,new(input.CommandId,input.SubmissionId,input.Decision,input.Reason,input.PreviewDigest),http.RequestAborted);
      return result.Succeeded?Results.Ok(result.Value):SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesInvoiceCommandReceipt>();

    group.MapGet("/accounting/clients/{clientId:guid}/sales-invoice-requests/{commandId:guid}",async(Guid clientId,Guid commandId,string kind,
      HttpContext http,TrustedActorResolver resolver,IDbContextFactory<AuditSphereDbContext> factory)=>{
      var actor=await resolver.ResolveAsync(http.User,http.RequestAborted);
      if(actor is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      await using var db=await factory.CreateDbContextAsync(http.RequestAborted);
      var result=await ClientSalesInvoiceWorkflow.GetCommandAsync(db,actor,clientId,commandId,kind,http.RequestAborted);
      if(await resolver.ResolveAsync(http.User,http.RequestAborted) is null)return Results.Json(new{code="session.unavailable"},statusCode:401);
      return result.Succeeded?Results.Ok(result.Value):SalesFailure(result.ErrorCode);
    }).Produces<ClientSalesInvoiceCommandReceipt>();
  }
}
