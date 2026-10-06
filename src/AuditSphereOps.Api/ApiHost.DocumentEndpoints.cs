using System.Buffers;
using System.Security.Claims;
using System.Security.Cryptography;
using AuditSphereOps.Application.Abstractions;
using AuditSphereOps.Application.Accounting;
using AuditSphereOps.Application.Completion;
using AuditSphereOps.Application.Diagnostics;
using AuditSphereOps.Application.Documents;
using AuditSphereOps.Application.Microsoft365;
using AuditSphereOps.Application.Operations;
using AuditSphereOps.Domain.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using AuditSphereOps.Api.Authentication;
using AuditSphereOps.Api.Diagnostics;
using AuditSphereOps.Api.HttpBoundary;
using AuditSphereOps.Infrastructure.Persistence;
using AuditSphereOps.Infrastructure.Providers;
using Microsoft.AspNetCore.RateLimiting;

namespace AuditSphereOps.Api;

public static partial class ApiHost
{
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
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.FileUpload));

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
      var configuredRoot = app.Configuration["Storage:PbcStagingRoot"];
      var stagingRoot = Path.GetFullPath(configuredRoot ?? Path.Combine(Path.GetTempPath(), "AuditSphereOps", "pbc-staging"));
      var rootPrefix = stagingRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
      if (download.ChunkPaths.Any(path => !Path.GetFullPath(path).StartsWith(rootPrefix, StringComparison.Ordinal)))
        return Results.Forbid();

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
    });

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
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));

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
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));

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
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
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
    }).WithMetadata(new ApiRateClassAttribute(ApiRateClass.Export));
  }
}
