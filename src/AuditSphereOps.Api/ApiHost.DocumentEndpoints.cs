using System.Buffers;
using System.Security.Cryptography;
using AuditSphereOps.Api.Contracts;
using Microsoft.OpenApi;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Infrastructure.Persistence;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
  public sealed record PbcChunkSuccessResponse(Guid UploadId, int ChunkIndex, long Offset, long ByteCount, string State);
  public sealed record PbcChunkErrorResponse(string Code);

  private static void MapDocumentEndpoints(WebApplication app)
  {
    app.MapPost("/api/pbc/uploads/{uploadId:guid}/chunks/{chunkIndex:int}", async (
      Guid uploadId,
      int chunkIndex,
      HttpContext http,
      TrustedActorResolver actorResolver,
      IDbContextFactory<AuditSphereDbContext> dbFactory,
      CancellationToken ct) =>
    {
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null)
        return Results.Unauthorized();
      var origin = http.Request.Headers.Origin.ToString();
      if (string.IsNullOrWhiteSpace(origin) || !Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        return Results.Forbid();
      var expectedPort = http.Request.Host.Port ?? (http.Request.IsHttps ? 443 : 80);
      var originPort = originUri.IsDefaultPort ? (originUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80) : originUri.Port;
      if (!string.Equals(originUri.Scheme, http.Request.Scheme, StringComparison.OrdinalIgnoreCase) ||
          !string.Equals(originUri.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase) ||
          originPort != expectedPort)
        return Results.Forbid();
      if (chunkIndex < 0 || !long.TryParse(http.Request.Headers["X-Upload-Offset"], out var offset) || offset < 0 ||
          string.IsNullOrWhiteSpace(http.Request.Headers["X-Content-SHA256"]) ||
          string.IsNullOrWhiteSpace(http.Request.Headers["X-Pbc-Upload-Capability"]))
        return Results.BadRequest(new { code = "pbc.chunk.invalid" });
      if (http.Request.ContentLength is > PbcService.MaxChunkBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

      var configuredRoot = app.Configuration["Storage:PbcStagingRoot"];
      if (string.IsNullOrWhiteSpace(configuredRoot) && !app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Test"))
        return Results.Problem("PBC staging storage is not configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
      var stagingRoot = configuredRoot ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "pbc-staging");
      var directory = Path.Combine(stagingRoot, actor.FirmId.ToString("N"), uploadId.ToString("N"));
      var path = Path.Combine(directory, $"{chunkIndex}-{Guid.NewGuid():N}.part");
      var keep = false;
      try
      {
        Directory.CreateDirectory(directory);
        long count = 0;
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
          await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: buffer.Length, options: FileOptions.Asynchronous | FileOptions.SequentialScan);
          while (true)
          {
            var read = await http.Request.Body.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            count += read;
            if (count > PbcService.MaxChunkBytes)
              return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            digest.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
          }
          await output.FlushAsync(ct);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }

        var hash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
        var declaredHash = http.Request.Headers["X-Content-SHA256"].ToString();
        if (count == 0 || !string.Equals(hash, declaredHash, StringComparison.OrdinalIgnoreCase))
          return Results.BadRequest(new { code = "pbc.chunk.hash-mismatch" });
        if (http.Request.ContentLength.HasValue && http.Request.ContentLength.Value != count)
          return Results.BadRequest(new { code = "pbc.chunk.length-mismatch" });

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var result = await PbcService.RecordChunkAsync(db, actor,
          new RecordPbcUploadChunkRequest(uploadId, chunkIndex, offset, checked((int)count), hash,
            http.Request.Headers["X-Pbc-Upload-Capability"].ToString(), path), ct);
        if (!result.Succeeded)
        {
          return result.ErrorCode switch
          {
            "pbc.chunk.conflict" or "pbc.chunk-offset" => Results.Conflict(new { code = result.ErrorCode }),
            "pbc.quota" => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
            ErrorCodes.ScopeDenied => Results.Forbid(),
            _ => Results.BadRequest(new { code = result.ErrorCode })
          };
        }

        var stored = await db.PbcUploadChunks.AsNoTracking().SingleAsync(x =>
          x.FirmId == actor.FirmId && x.PbcUploadIntentId == uploadId && x.ChunkIndex == chunkIndex, ct);
        keep = string.Equals(stored.StagedPath, path, StringComparison.Ordinal);
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.Ok(new { uploadId, chunkIndex, offset, byteCount = count, state = result.Value!.State });
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested)
      {
        throw;
      }
      catch (IOException)
      {
        return Results.Problem("The bounded staging write could not be completed.", statusCode: StatusCodes.Status503ServiceUnavailable);
      }
      finally
      {
        if (!keep)
        {
          try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
        }
      }
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.FileUpload))
      .Accepts<Stream>("application/octet-stream")
      .WithMetadata(
        new ManualParameterMetadata("Origin", ParameterLocation.Header, true, JsonSchemaType.String,
          Format: "uri", Description: "Must match the request scheme, host and port."),
        new ManualParameterMetadata("X-Upload-Offset", ParameterLocation.Header, true, JsonSchemaType.Integer,
          Format: "int64", Description: "Nonnegative byte offset for this retained upload chunk.", Minimum: 0),
        new ManualParameterMetadata("X-Content-SHA256", ParameterLocation.Header, true, JsonSchemaType.String,
          Pattern: "^[a-fA-F0-9]{64}$", Description: "SHA-256 of the exact chunk bytes."),
        new ManualParameterMetadata("X-Pbc-Upload-Capability", ParameterLocation.Header, true, JsonSchemaType.String,
          Description: "The server-issued capability bound to this upload and actor; never persist it in client logs."))
      .Produces<PbcChunkSuccessResponse>(StatusCodes.Status200OK, "application/json")
      .Produces<PbcChunkErrorResponse>(StatusCodes.Status400BadRequest, "application/json")
      .Produces(StatusCodes.Status401Unauthorized)
      .Produces(StatusCodes.Status403Forbidden)
      .Produces<PbcChunkErrorResponse>(StatusCodes.Status409Conflict, "application/json")
      .Produces(StatusCodes.Status413PayloadTooLarge)
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")
      .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
      .WithMetadata(new CookieSessionContractMetadata());

    app.MapGet("/api/pbc/uploads/{uploadId:guid}/download", async (
      Guid uploadId,
      HttpContext http,
      TrustedActorResolver actorResolver,
      IDbContextFactory<AuditSphereDbContext> dbFactory,
      CancellationToken ct) =>
    {
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null) return Results.Unauthorized();
      await using var db = await dbFactory.CreateDbContextAsync(ct);
      var prepared = await PbcService.PrepareDownloadAsync(db, actor, uploadId, ct);
      if (!prepared.Succeeded)
        return prepared.ErrorCode == ErrorCodes.ScopeDenied ? Results.Forbid() :
          Results.Problem(prepared.Message, statusCode: StatusCodes.Status409Conflict);

      var download = prepared.Value!;
      if (download.Remote is { } remote)
      {
        // Exact registered SharePoint version: fully hashed into a private spool before any response byte, then the
        // actor and the identical registered source are rechecked after the remote I/O. No fallback to a local copy.
        var reader = http.RequestServices.GetService<IPbcRemoteContentReader>();
        if (reader is null)
          return Results.Problem("The registered SharePoint version is temporarily unavailable; retry later.",
            statusCode: StatusCodes.Status409Conflict);
        var read = await reader.ReadVerifiedAsync(remote, ct);
        if (!read.Succeeded) return Results.Problem(read.Message, statusCode: StatusCodes.Status409Conflict);
        await using var content = read.Value!;
        var current = await actorResolver.ResolveAsync(http.User, ct);
        if (current is null) return Results.Unauthorized();
        await using var recheckDb = await dbFactory.CreateDbContextAsync(ct);
        var recheck = await PbcService.RevalidateDownloadAsync(recheckDb, current, uploadId, download, ct);
        if (!recheck.Succeeded)
          return recheck.ErrorCode == ErrorCodes.ScopeDenied ? Results.Forbid() :
            Results.Problem(recheck.Message, statusCode: StatusCodes.Status409Conflict);
        if (content.Length != download.ByteCount)
          return Results.Problem("The registered source changed while it was being read; retry.",
            statusCode: StatusCodes.Status409Conflict);
        http.Response.StatusCode = StatusCodes.Status200OK;
        http.Response.ContentType = download.ContentType;
        http.Response.ContentLength = download.ByteCount;
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(download.FileName)}";
        await content.Content.CopyToAsync(http.Response.Body, ct);
        return Results.Empty;
      }
      var configuredRoot = app.Configuration["Storage:PbcStagingRoot"];
      var stagingRoot = Path.GetFullPath(configuredRoot ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "pbc-staging"));
      var rootPrefix = stagingRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      if (download.ChunkPaths.Any(path => !Path.GetFullPath(path).StartsWith(rootPrefix, StringComparison.Ordinal)))
        return Results.Forbid();

      var currentLocal = await actorResolver.ResolveAsync(http.User, ct);
      if (currentLocal is null) return Results.Unauthorized();
      await using var currentLocalDb = await dbFactory.CreateDbContextAsync(ct);
      var localRecheck = await PbcService.RevalidateDownloadAsync(currentLocalDb, currentLocal, uploadId, download, ct);
      if (!localRecheck.Succeeded)
        return localRecheck.ErrorCode == ErrorCodes.ScopeDenied ? Results.Forbid() :
          Results.Problem(localRecheck.Message, statusCode: StatusCodes.Status409Conflict);

      http.Response.StatusCode = StatusCodes.Status200OK;
      http.Response.ContentType = download.ContentType;
      http.Response.ContentLength = download.ByteCount;
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      http.Response.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(download.FileName)}";
      foreach (var path in download.ChunkPaths)
      {
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
          64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(http.Response.Body, ct);
      }
      return Results.Empty;
    }).Produces<Stream>(StatusCodes.Status200OK, "application/octet-stream")
      .WithMetadata(new RetainedBinaryContentTypeMetadata())
      .Produces(StatusCodes.Status401Unauthorized)
      .Produces(StatusCodes.Status403Forbidden)
      .Produces<Microsoft.AspNetCore.Mvc.ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")
      .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
      .WithMetadata(new CookieSessionContractMetadata());

    // Generated commercial documents (quotation, engagement letter, receipt): same authorization as the service, no
    // existence oracle (unknown and out-of-scope identifiers both refuse), never cached or sniffed.
    app.MapGet("/api/commercial/documents/{documentId:guid}/download", async (
      Guid documentId,
      HttpContext http,
      TrustedActorResolver actorResolver,
      IDbContextFactory<AuditSphereDbContext> dbFactory,
      CancellationToken ct) =>
    {
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null) return Results.Unauthorized();
      await using var db = await dbFactory.CreateDbContextAsync(ct);
      var result = await AuditSphereOps.Application.Practice.CommercialDocumentService.GetAsync(db, actor, documentId, ct);
      if (!result.Succeeded) return Results.Forbid();
      if (await actorResolver.ResolveAsync(http.User, ct) is null) return Results.Unauthorized();
      var document = result.Value!;
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      return Results.File(document.Bytes, document.ContentType, document.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export))
      .Produces<Stream>(StatusCodes.Status200OK, AuditSphereOps.Application.Practice.CommercialDocumentRenderer.DocxContentType)
      .Produces(StatusCodes.Status401Unauthorized)
      .Produces(StatusCodes.Status403Forbidden)
      .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
      .WithMetadata(new CookieSessionContractMetadata());

    app.MapGet("/api/deliverables/{deliverableId:guid}/download", async (
      Guid deliverableId,
      HttpContext http,
      TrustedActorResolver actorResolver,
      IDbContextFactory<AuditSphereDbContext> dbFactory,
      CancellationToken ct) =>
    {
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null) return Results.Unauthorized();
      await using var db = await dbFactory.CreateDbContextAsync(ct);
      var result = await AuditSphereOps.Application.Completion.AuditDeliverableService.GetForDownloadAsync(db, actor, deliverableId, ct);
      if (!result.Succeeded) return Results.Forbid();
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      return Results.File(result.Value!.Content, result.Value.ContentType, result.Value.FileName);
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export))
      .Produces<Stream>(StatusCodes.Status200OK, "application/pdf")
      .Produces<Stream>(StatusCodes.Status200OK, AuditSphereOps.Application.Completion.AuditDeliverableRenderer.DocxContentType)
      .Produces(StatusCodes.Status401Unauthorized)
      .Produces(StatusCodes.Status403Forbidden)
      .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
      .WithMetadata(new CookieSessionContractMetadata());

    app.MapGet("/api/representation-scans/{scanId:guid}/download", async (
      Guid scanId, HttpContext http, TrustedActorResolver actorResolver, IDbContextFactory<AuditSphereDbContext> dbFactory, CancellationToken ct) =>
    {
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null) return Results.Unauthorized();
      await using var db = await dbFactory.CreateDbContextAsync(ct);
      var result = await AuditSphereOps.Application.Completion.AuditDeliverableService.GetSignedRepresentationAsync(db, actor, scanId, ct);
      if (!result.Succeeded) return Results.Forbid();
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      return Results.File(result.Value!.Content, "application/pdf", $"Management-signed-LOR-{scanId}.pdf");
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export))
      .Produces<Stream>(StatusCodes.Status200OK, "application/pdf")
      .Produces(StatusCodes.Status401Unauthorized)
      .Produces(StatusCodes.Status403Forbidden)
      .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
      .WithMetadata(new CookieSessionContractMetadata());
    app.MapGet("/api/deliverable-bundles/{bundleId:guid}/download", async (
      Guid bundleId, HttpContext http, TrustedActorResolver actorResolver, IDbContextFactory<AuditSphereDbContext> dbFactory, CancellationToken ct) =>
    {
      var actor = await actorResolver.ResolveAsync(http.User, ct);
      if (actor is null) return Results.Unauthorized();
      await using var db = await dbFactory.CreateDbContextAsync(ct);
      var result = await AuditSphereOps.Application.Completion.AuditDeliverableService.GetBundleAsync(db, actor, bundleId, ct);
      if (!result.Succeeded) return Results.Forbid();
      http.Response.Headers.CacheControl = "no-store";
      http.Response.Headers["X-Content-Type-Options"] = "nosniff";
      return Results.File(result.Value!.Content, "application/zip", $"Final-audit-bundle-{bundleId}.zip");
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export))
      .Produces<Stream>(StatusCodes.Status200OK, "application/zip")
      .Produces(StatusCodes.Status401Unauthorized)
      .Produces(StatusCodes.Status403Forbidden)
      .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
      .WithMetadata(new CookieSessionContractMetadata());
  }
}
