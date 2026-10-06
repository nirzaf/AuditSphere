namespace AuditSphereOps.Api.HttpBoundary;

/// <summary>Explicit server-side request-size policy for the HTTP boundary.</summary>
public sealed class RequestBodyLimitOptions
{
  public const string SectionName = "HttpBoundary:BodyLimits";

  /// <summary>Hard host-level ceiling for any request body (covers multipart and streaming uploads).</summary>
  public long MaxRequestBodyBytes { get; set; } = 30 * 1024 * 1024;

  /// <summary>Ceiling for JSON command bodies; oversized requests are refused before business processing.</summary>
  public long MaxJsonBodyBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Refuses oversized JSON command bodies with a safe structured 413 before any business handler
/// or database work runs. Bodies with a declared length are refused immediately; undeclared
/// (chunked or server-buffered) bodies are capped by buffering at most the limit and replacing
/// the stream, which also closes a transfer-encoding bypass. Multipart and the PBC chunk stream
/// are exempt: they carry explicit per-endpoint bounds and stream through bounded staging
/// writes, and the host-level ceiling still applies.
/// </summary>
internal sealed class RequestBodyLimitMiddleware(RequestDelegate next, RequestBodyLimitOptions options)
{
  public async Task InvokeAsync(HttpContext context)
  {
    var request = context.Request;
    if (IsJsonCommand(request) &&
        request.ContentLength is { } declared && declared > options.MaxJsonBodyBytes)
    {
      await RefuseAsync(context);
      return;
    }
    if (IsJsonCommand(request) && !request.ContentLength.HasValue)
    {
      var buffered = await ReadCappedAsync(request, (int)options.MaxJsonBodyBytes, context.RequestAborted);
      if (buffered is null)
      {
        await RefuseAsync(context);
        return;
      }
      request.Body = new MemoryStream(buffered, writable: false);
    }
    await next(context);
  }

  private static bool IsJsonCommand(HttpRequest request) =>
    (HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsPatch(request.Method)) &&
    request.Path.StartsWithSegments("/api") &&
    !request.Path.StartsWithSegments("/api/pbc/uploads") &&
    !request.HasFormContentType;

  private static async Task<byte[]?> ReadCappedAsync(HttpRequest request, int limit, CancellationToken ct)
  {
    var buffer = new MemoryStream(limit + 1);
    var chunk = new byte[16 * 1024];
    int read;
    while ((read = await request.Body.ReadAsync(new Memory<byte>(chunk, 0, Math.Min(chunk.Length, limit + 1 - (int)buffer.Length)), ct)) > 0)
    {
      await buffer.WriteAsync(new Memory<byte>(chunk, 0, read), ct);
      if (buffer.Length > limit) return null;
    }
    return buffer.ToArray();
  }

  private static async Task RefuseAsync(HttpContext context)
  {
    var response = context.Response;
    response.StatusCode = StatusCodes.Status413PayloadTooLarge;
    response.Headers.CacheControl = "no-store";
    await response.WriteAsJsonAsync(new ApiError("request.too-large",
      "The submitted request exceeds the accepted size.", context.TraceIdentifier), context.RequestAborted);
  }
}
